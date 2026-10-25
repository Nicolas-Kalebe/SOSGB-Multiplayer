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
        _net?.Stop();
        _net = null;
        _server = null;
        Players.Clear();
        Current = Mode.Offline;
    }

    public static void Poll() => _net?.PollEvents();

    private static void Setup()
    {
        LastError = null;
        _listener = new EventBasedNetListener();
        _net = new NetManager(_listener) { AutoRecycle = true, DisconnectTimeout = 10000 };
        _listener.ConnectionRequestEvent += OnConnectionRequest;
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnReceive;
        _listener.NetworkErrorEvent += (ep, err) => { LastError = $"{ep}: {err}"; Plugin.Logger.LogWarning($"[Net] erro {LastError}"); };
    }

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

    private static void HandleHello(NetPeer peer, NetPacketReader reader)
    {
        var proto = reader.GetInt();
        var modVersion = reader.GetString();
        var name = reader.GetString();

        if (proto != Protocol.Version)
        {
            Reject(peer, $"versão do protocolo diferente (host {Protocol.Version}, você {proto}; mod {Plugin.Version} × {modVersion})");
            return;
        }
        var id = (byte)Enumerable.Range(1, Protocol.MaxPlayers - 1).First(i => !Players.ContainsKey((byte)i));
        Players[id] = new Player { Id = id, Name = name, Peer = peer };
        Plugin.Logger.LogInfo($"[Net] {name} entrou como #{id} (mod {modVersion})");

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
    private static void RouteGameMessage(NetPeer from, MsgType type, NetPacketReader reader, DeliveryMethod method)
    {
        if (Current == Mode.Host)
        {
            var sender = Players.Values.FirstOrDefault(p => p.Peer == from);
            if (sender == null) return;
            var body = reader.GetRemainingBytes();
            Writer.Reset();
            Writer.Put((byte)type);
            Writer.Put(body);
            SendToAllExcept(from, method);
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
        lastError = LastError,
    };
}
