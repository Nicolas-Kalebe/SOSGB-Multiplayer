// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, docs/PROTOCOLO.md e ROTEIRO.md (3.1).
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Tarefa 3.1: o convidado entra no MUNDO DO HOST.
///  1. Convidado é aceito (Welcome) → manda WorldRequest.
///  2. Host captura o mundo com o pipeline de save do jogo, sem gravar no disco (WorldSnapshot)
///     e responde WorldData com os bytes do .sav (~73 KB, ReliableOrdered fragmentado pelo LiteNetLib).
///  3. Convidado injeta esses bytes no LoadSlot e recarrega a cena Game → passa a ver o mundo do host.
/// O convidado nunca grava o save (WorldSnapshot.SavesBlocked).
/// </summary>
public static class WorldSync
{
    private const int GuestLoadSlot = 2; // qualquer slot: os bytes vêm da rede, o arquivo não é lido
    private static readonly Queue<byte> Pending = new();
    private static readonly HashSet<byte> Joining = new(); // receberam o mundo pela 1ª vez (mandar o perfil junto)
    private static bool _subscribed;

    /// <summary>No convidado: o mundo do host chegou e vai ser carregado.</summary>
    public static event System.Action OnWorldReceived;

    private static int _retries;
    private static bool _kick;

    /// <summary>Host, todo quadro (pelo DaySync): captura para quem está esperando o mundo, quando der.</summary>
    public static void Tick()
    {
        if (!_kick || Pending.Count == 0 || DaySync.InDayChange || WorldSnapshot.Capturing) return;
        _kick = false;
        CaptureNext();
    }

    public static void Init()
    {
        if (_subscribed) return;
        _subscribed = true;
        // Pedido de entrada que chegou durante outra captura (ex.: a de recuperação) ficava esquecido (revisão do núcleo).
        // A próxima captura sai no quadro seguinte (Tick), nunca de dentro do retorno do save do jogo.
        WorldSnapshot.CaptureFinished += () => _kick = true;
        NetSession.OnJoined += () =>
        {
            Plugin.Logger.LogInfo("[WorldSync] pedindo o mundo ao host");
            NetSession.SendGame(MsgType.WorldRequest, _ => { }, DeliveryMethod.ReliableOrdered);
        };
        NetSession.OnGameMessage += OnMessage;
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.WorldRequest when NetSession.Current == NetSession.Mode.Host:
                Enqueue(from);
                Joining.Add(from);
                // Durante a virada do dia, espera: o mundo novo vai para todos no fim dela (BroadcastWorld), e capturar no
                // meio do processamento do dia mandaria um mundo pela metade (revisão do núcleo).
                if (!WorldSnapshot.Capturing && !DaySync.InDayChange) CaptureNext();
                break;

            case MsgType.WorldData when NetSession.Current == NetSession.Mode.Client:
                var len = r.GetInt();
                var bytes = new byte[len];
                r.GetBytes(bytes, len);
                Plugin.Logger.LogInfo($"[WorldSync] mundo recebido ({len} bytes); recarregando");
                DevBridge.PushEvent($"world-recebido:{len}");
                OnWorldReceived?.Invoke();
                WorldSnapshot.HoldingHostWorld = true;
                WorldSnapshot.SetCaptured(bytes);
                WorldSnapshot.Load(GuestLoadSlot);
                break;
        }
    }

    /// <summary>Host → todos os convidados: manda o mundo atual (usado após a virada do dia, 3.5).</summary>
    public static void BroadcastWorld()
    {
        if (NetSession.Current != NetSession.Mode.Host) return;
        foreach (var p in NetSession.Players.Values)
            if (p.Id != Protocol.HostPlayerId) Enqueue(p.Id);
        if (!WorldSnapshot.Capturing) CaptureNext();
    }

    private static void Enqueue(byte id)
    {
        if (!Pending.Contains(id)) Pending.Enqueue(id);
    }

    private static void CaptureNext()
    {
        if (Pending.Count == 0) return;
        WorldSnapshot.Capture(GuestLoadSlot, ok =>
        {
            var data = WorldSnapshot.Captured;
            if (!ok || data == null)
            {
                // Tenta de novo (até 2 vezes) em vez de deixar o amigo sem mundo; o CaptureFinished chama a próxima.
                if (_retries++ < 2) { Plugin.Logger.LogWarning("[WorldSync] falha ao capturar o mundo; tentando de novo"); return; }
                Plugin.Logger.LogError("[WorldSync] falha ao capturar o mundo; pedidos descartados");
                Pending.Clear();
                _retries = 0;
                return;
            }
            _retries = 0;
            while (Pending.Count > 0)
            {
                var id = Pending.Dequeue();
                if (!NetSession.Players.ContainsKey(id)) continue; // saiu enquanto capturava
                NetSession.SendTo(id, MsgType.WorldData, w => { w.Put(data.Length); w.Put(data); }, DeliveryMethod.ReliableOrdered);
                Plugin.Logger.LogInfo($"[WorldSync] mundo enviado para #{id} ({data.Length} bytes)");
                if (Joining.Remove(id)) PlayerProfiles.SendStoredProfile(id);
                DevBridge.PushEvent($"world-enviado:{id}:{data.Length}");
            }
        });
    }
}
