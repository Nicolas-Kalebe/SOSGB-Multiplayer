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
    private static bool _subscribed;

    public static void Init()
    {
        if (_subscribed) return;
        _subscribed = true;
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
                Pending.Enqueue(from);
                if (!WorldSnapshot.Capturing) CaptureNext();
                break;

            case MsgType.WorldData when NetSession.Current == NetSession.Mode.Client:
                var len = r.GetInt();
                var bytes = new byte[len];
                r.GetBytes(bytes, len);
                Plugin.Logger.LogInfo($"[WorldSync] mundo recebido ({len} bytes); recarregando");
                DevBridge.PushEvent($"world-recebido:{len}");
                WorldSnapshot.SetCaptured(bytes);
                WorldSnapshot.Load(GuestLoadSlot);
                break;
        }
    }

    private static void CaptureNext()
    {
        if (Pending.Count == 0) return;
        WorldSnapshot.Capture(GuestLoadSlot, ok =>
        {
            var data = WorldSnapshot.Captured;
            while (Pending.Count > 0)
            {
                var id = Pending.Dequeue();
                if (!ok || data == null) { Plugin.Logger.LogError($"[WorldSync] falha ao capturar o mundo para #{id}"); continue; }
                NetSession.SendTo(id, MsgType.WorldData, w => { w.Put(data.Length); w.Put(data); }, DeliveryMethod.ReliableOrdered);
                Plugin.Logger.LogInfo($"[WorldSync] mundo enviado para #{id} ({data.Length} bytes)");
                DevBridge.PushEvent($"world-enviado:{id}:{data.Length}");
            }
        });
    }
}
