// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, docs/PROTOCOLO.md e DECISOES.md#d7.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Sessão de rede (LiteNetLib, D7). Um processo é host OU convidado.
/// Host-autoritativo: o host aceita conexões, distribui IDs (0 = host, 1..3 = convidados)
/// e retransmite mensagens. Tudo roda na thread principal da Unity: <see cref="Poll"/> é chamado
/// no Update de <see cref="NetRunner"/>.
/// </summary>
public static class NetSession
{
    public enum Mode { Offline, Host, Client }

    public sealed class Player
    {
        public byte Id;
        public string Name;
        public NetPeer Peer; // só no host (null para o próprio host)
    }

    public static Mode Current { get; private set; } = Mode.Offline;
    public static byte LocalId { get; private set; }
    public static readonly Dictionary<byte, Player> Players = new();
    public static string LastError { get; private set; }

    /// <summary>Recebe mensagens de jogo (≥ PlayerState) já com o ID de quem enviou.</summary>
    public static event Action<MsgType, byte, NetDataReader> OnGameMessage;
    public static event Action<byte> OnPlayerLeft;

    /// <summary>No convidado: disparado quando o host aceita a entrada (Welcome).</summary>
    public static event Action OnJoined;

    /// <summary>No host: convidado aceito (id, guid).</summary>
    public static event Action<byte, string> OnHelloAccepted;

    /// <summary>Disparado quando este processo passa a hospedar.</summary>
    public static event Action OnHosting;

    private static NetManager _net;
    private static EventBasedNetListener _listener;
    private static NetPeer _server; // no convidado: conexão com o host
    private static readonly NetDataWriter Writer = new();

    public static string LocalName =>
        string.IsNullOrWhiteSpace(Plugin.PlayerName.Value) ? Environment.UserName : Plugin.PlayerName.Value;

    // ---------- ciclo de vida ----------

    public static void Host(int port)
    {
        Stop();
        Setup();
        if (!_net.Start(port)) throw new InvalidOperationException($"não foi possível abrir a porta {port}");
        Current = Mode.Host;
        LocalId = Protocol.HostPlayerId;
        Players[LocalId] = new Player { Id = LocalId, Name = LocalName };
        Plugin.Logger.LogInfo($"[Net] hospedando na porta {port}");
        OnHosting?.Invoke();
    }

    public static void Join(string address, int port)
    {
        Stop();
        Setup();
        _net.Start();
        _server = _net.Connect(address, port, Protocol.ConnectionKey);
        Current = Mode.Client;
        Plugin.Logger.LogInfo($"[Net] conectando em {address}:{port}");
    }

    public static void Stop()
    {
        if (Current == Mode.Client && _server != null)
        {
            // Sair de forma limpa: manda o perfil e dá tempo do pacote confiável sair antes de
            // desconectar (o LiteNetLib envia numa thread própria; parar na hora descarta a fila).
            PlayerProfiles.Upload();
            var until = DateTime.UtcNow.AddMilliseconds(500);
            while (DateTime.UtcNow < until && _server.GetPacketsCountInReliableQueue(0, true) > 0)
                System.Threading.Thread.Sleep(20);
            System.Threading.Thread.Sleep(100);
        }
        _net?.Stop();
        _net = null;
        _server = null;
        Players.Clear();
        Delayed.Clear();
        Current = Mode.Offline;
    }

    public static void Poll()
    {
        _net?.PollEvents();
        ProcessDelayed();
    }

    private static void Setup()
    {
        LastError = null;
        _listener = new EventBasedNetListener();
        _net = new NetManager(_listener) { AutoRecycle = true, DisconnectTimeout = 10000 };
        ApplyNetworkSimulation();
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
        _listener.NetworkErrorEvent += (ep, err) => { LastError = $"{ep}: {err}"; Plugin.Logger.LogWarning($"[Net] erro {LastError}"); };
    }

    /// <summary>
    /// Simulação de rede ruim (atraso e perda) para testar no mesmo PC como se fossem dois PCs
    /// pela internet/LAN virtual. Variáveis de ambiente (por instância):
    /// SOSGB_NET_LAG_MIN / SOSGB_NET_LAG_MAX (ms) e SOSGB_NET_LOSS (% de perda).
    /// O simulador embutido do LiteNetLib NÃO funciona no pacote do NuGet (só em build de debug),
    /// por isso fazemos aqui: os pacotes recebidos esperam numa fila; pacotes não confiáveis
    /// (Unreliable/Sequenced) podem ser descartados; os confiáveis só atrasam, mantendo a ordem.
    /// </summary>
    private static void ApplyNetworkSimulation()
    {
        int Env(string n) => int.TryParse(Environment.GetEnvironmentVariable(n), out var v) ? v : 0;
        _lagMin = Env("SOSGB_NET_LAG_MIN");
        _lagMax = Math.Max(_lagMin, Env("SOSGB_NET_LAG_MAX"));
        _loss = Env("SOSGB_NET_LOSS");
        var on = _lagMax > 0 || _loss > 0;
        if (on) Plugin.Logger.LogWarning($"[Net] SIMULAÇÃO de rede: atraso {_lagMin}-{_lagMax} ms, perda {_loss}%");
        Simulation = on ? $"{_lagMin}-{_lagMax}ms/{_loss}%" : null;
    }

    private static int _lagMin, _lagMax, _loss;
    private static readonly System.Random Rng = new();
    private static readonly List<(double at, NetPeer peer, byte[] data, DeliveryMethod method)> Delayed = new();
    private static double _lastReliableAt;
    private static double Now => System.Diagnostics.Stopwatch.GetTimestamp() / (double)System.Diagnostics.Stopwatch.Frequency * 1000.0;

    private static void ProcessDelayed()
    {
        if (Delayed.Count == 0) return;
        var now = Now;
        for (var i = 0; i < Delayed.Count;)
        {
            var d = Delayed[i];
            if (d.at > now) { i++; continue; }
            Delayed.RemoveAt(i);
            Process(d.peer, new NetDataReader(d.data), d.method);
        }
    }

    public static string Simulation { get; private set; }

    // ---------- eventos ----------

    private static void OnConnectionRequest(ConnectionRequest req)
    {
        if (Current == Mode.Host && Players.Count < Protocol.MaxPlayers)
            req.AcceptIfKey(Protocol.ConnectionKey);
        else
            req.Reject();
    }

    private static void OnPeerConnected(NetPeer peer)
    {
        if (Current == Mode.Client)
        {
            // Primeira coisa: apresentar-se ao host.
            Writer.Reset();
            Writer.Put((byte)MsgType.Hello);
            Writer.Put(Protocol.Version);
            Writer.Put(Plugin.Version);
            Writer.Put(LocalName);
            Writer.Put(Plugin.EffectivePlayerGuid);
            peer.Send(Writer, DeliveryMethod.ReliableOrdered);
        }
        // No host, o jogador só é registrado depois do Hello válido.
    }

    private static void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (Current == Mode.Host)
        {
            var p = Players.Values.FirstOrDefault(x => x.Peer == peer);
            if (p == null) return;
            Players.Remove(p.Id);
            Plugin.Logger.LogInfo($"[Net] {p.Name} (#{p.Id}) saiu: {info.Reason}");
            Writer.Reset();
            Writer.Put((byte)MsgType.PlayerLeft);
            Writer.Put(p.Id);
            SendToAllExcept(null, DeliveryMethod.ReliableOrdered);
            OnPlayerLeft?.Invoke(p.Id);
        }
        else
        {
            Plugin.Logger.LogWarning($"[Net] desconectado do host: {info.Reason}");
            LastError ??= info.Reason.ToString();
            Players.Clear();
            Current = Mode.Offline;
        }
    }

    private static void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (Simulation == null) { Process(peer, reader, method); return; }

        var reliable = method == DeliveryMethod.ReliableOrdered || method == DeliveryMethod.ReliableUnordered || method == DeliveryMethod.ReliableSequenced;
        if (!reliable && Rng.Next(100) < _loss) return; // perdido
        var at = Now + Rng.Next(_lagMin, _lagMax + 1);
        if (reliable) { at = Math.Max(at, _lastReliableAt); _lastReliableAt = at; } // mantém a ordem
        Delayed.Add((at, peer, reader.GetRemainingBytes(), method));
    }

    private static void Process(NetPeer peer, NetDataReader reader, DeliveryMethod method)
    {
        var type = (MsgType)reader.GetByte();
        switch (type)
        {
            case MsgType.Hello when Current == Mode.Host:
                HandleHello(peer, reader);
                break;
            case MsgType.Welcome when Current == Mode.Client:
                LocalId = reader.GetByte();
                Players.Clear();
                var n = reader.GetByte();
                for (var i = 0; i < n; i++)
                {
                    var id = reader.GetByte();
                    Players[id] = new Player { Id = id, Name = reader.GetString() };
                }
                Plugin.Logger.LogInfo($"[Net] entrei como #{LocalId}; jogadores: {string.Join(", ", Players.Values.Select(p => $"{p.Name}#{p.Id}"))}");
                OnJoined?.Invoke();
                break;
            case MsgType.Reject when Current == Mode.Client:
                LastError = "recusado: " + reader.GetString();
                Plugin.Logger.LogWarning($"[Net] {LastError}");
                break;
            case MsgType.PlayerJoined when Current == Mode.Client:
                var jid = reader.GetByte();
                Players[jid] = new Player { Id = jid, Name = reader.GetString() };
                break;
            case MsgType.PlayerLeft when Current == Mode.Client:
                var lid = reader.GetByte();
                Players.Remove(lid);
                OnPlayerLeft?.Invoke(lid);
                break;
            default:
                if ((byte)type >= (byte)MsgType.PlayerState)
                    RouteGameMessage(peer, type, reader, method);
                break;
        }
    }

    private static void HandleHello(NetPeer peer, NetDataReader reader)
    {
        var proto = reader.GetInt();
        var modVersion = reader.GetString();
        var name = reader.GetString();
        var guid = reader.AvailableBytes > 0 ? reader.GetString() : name;

        if (proto != Protocol.Version)
        {
            Reject(peer, $"versão do protocolo diferente (host {Protocol.Version}, você {proto}; mod {Plugin.Version} × {modVersion})");
            return;
        }
        var id = (byte)Enumerable.Range(1, Protocol.MaxPlayers - 1).First(i => !Players.ContainsKey((byte)i));
        Players[id] = new Player { Id = id, Name = name, Peer = peer };
        Plugin.Logger.LogInfo($"[Net] {name} entrou como #{id} (mod {modVersion})");
        OnHelloAccepted?.Invoke(id, guid);

        Writer.Reset();
        Writer.Put((byte)MsgType.Welcome);
        Writer.Put(id);
        Writer.Put((byte)Players.Count);
        foreach (var p in Players.Values) { Writer.Put(p.Id); Writer.Put(p.Name); }
        peer.Send(Writer, DeliveryMethod.ReliableOrdered);

        Writer.Reset();
        Writer.Put((byte)MsgType.PlayerJoined);
        Writer.Put(id);
        Writer.Put(name);
        SendToAllExcept(peer, DeliveryMethod.ReliableOrdered);
    }

    private static void Reject(NetPeer peer, string reason)
    {
        Writer.Reset();
        Writer.Put((byte)MsgType.Reject);
        Writer.Put(reason);
        peer.Send(Writer, DeliveryMethod.ReliableOrdered);
        peer.Disconnect();
        Plugin.Logger.LogWarning($"[Net] conexão recusada: {reason}");
    }

    /// <summary>Mensagens de jogo: o host entrega localmente e retransmite aos outros convidados.</summary>
    private static void RouteGameMessage(NetPeer from, MsgType type, NetDataReader reader, DeliveryMethod method)
    {
        if (Current == Mode.Host)
        {
            var sender = Players.Values.FirstOrDefault(p => p.Peer == from);
            if (sender == null) return;
            var body = reader.GetRemainingBytes();
            if ((byte)type < Protocol.FirstDirectMsg) // 10–19: difusão; 20+: só para o host
            {
                Writer.Reset();
                Writer.Put((byte)type);
                Writer.Put(body);
                SendToAllExcept(from, method);
            }
            OnGameMessage?.Invoke(type, sender.Id, new NetDataReader(body));
        }
        else
        {
            OnGameMessage?.Invoke(type, Protocol.HostPlayerId, reader);
        }
    }

    // ---------- envio ----------

    /// <summary>Envia uma mensagem de jogo do jogador local (o corpo é escrito por <paramref name="write"/>).</summary>
    public static void SendGame(MsgType type, Action<NetDataWriter> write, DeliveryMethod method)
    {
        if (Current == Mode.Offline) return;
        Writer.Reset();
        Writer.Put((byte)type);
        write(Writer);
        if (Current == Mode.Host) SendToAllExcept(null, method);
        else _server?.Send(Writer, method);
    }

    /// <summary>Host → um convidado específico.</summary>
    public static void SendTo(byte playerId, MsgType type, Action<NetDataWriter> write, DeliveryMethod method)
    {
        if (Current != Mode.Host || !Players.TryGetValue(playerId, out var p) || p.Peer == null) return;
        Writer.Reset();
        Writer.Put((byte)type);
        write(Writer);
        p.Peer.Send(Writer, method);
    }

    private static void SendToAllExcept(NetPeer except, DeliveryMethod method)
    {
        foreach (var p in Players.Values)
            if (p.Peer != null && p.Peer != except)
                p.Peer.Send(Writer, method);
    }

    public static object Status() => new
    {
        mode = Current.ToString(),
        localId = LocalId,
        players = Players.Values.Select(p => new { p.Id, p.Name, ping = p.Peer?.Ping }).ToArray(),
        serverPing = _server?.Ping,
        simulatedExtraMs = Simulation == null ? 0 : (_lagMin + _lagMax) / 2,
        lastError = LastError,
        simulation = Simulation,
    };
}
