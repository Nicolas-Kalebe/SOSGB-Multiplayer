// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d4 e #d36.
using System;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Carteira compartilhada (4.1, D4/D36): o host guarda o valor oficial.
///
/// Em vez de interceptar cada função de dinheiro do jogo (AddMoney, SpendMoney, venda no Bazaar, prêmio de
/// festival...), cada jogo OBSERVA o próprio UserInfo.PlayerMoney:
///  - convidado: quando o valor muda por conta própria (comprou, vendeu, ganhou), manda a diferença ao host
///    (MoneyDelta). A mudança local continua valendo na hora, então a loja funciona sem esperar a rede;
///  - host: aplica a diferença na carteira dele (nunca abaixo de 0) e manda o total a todos (MoneyState);
///    mudanças feitas no próprio host também viram MoneyState;
///  - convidado ao receber MoneyState: ajusta a carteira local para o total, sem gerar diferença de volta.
/// Depois de carregar um mundo (entrada, virada do dia), a base é zerada: o valor do snapshot não é diferença.
///
/// Limite conhecido: se dois jogadores gastam ao mesmo tempo mais do que existe, o host corta em 0 (uma das
/// compras sai "de graça"). Entre amigos e com a carteira visível, é aceitável.
/// </summary>
public class MoneySync : MonoBehaviour
{
    public MoneySync(IntPtr ptr) : base(ptr) { }

    private static long _baseline = -1;   // último valor conhecido e já sincronizado
    private static bool _subscribed;
    private float _nextHeartbeat;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => _baseline = -1;     // o mundo novo traz o próprio valor
        NetSession.OnHosting += () => _baseline = -1;
        NetSession.OnJoined += () => _baseline = -1;
        NetSession.OnPlayerJoinedNamed += (id, name) => { if (NetSession.Current == NetSession.Mode.Host) _forceSend = true; };
    }

    private static bool _forceSend;

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!ExternalData.IsInstance || ExternalData.Instance.UserInfo == null) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    private static long Money => ExternalData.Instance.UserInfo.PlayerMoney;

    private void Update()
    {
        try
        {
            if (!Ready()) return;
            var now = Money;
            if (_baseline < 0) { _baseline = now; if (NetSession.Current == NetSession.Mode.Host) _forceSend = true; return; }

            if (NetSession.Current == NetSession.Mode.Host)
            {
                // Host: o valor dele é o oficial. Mudou (ou alguém entrou)? Manda o total. E um "lembrete" a cada 5 s.
                if (now != _baseline || _forceSend || Time.unscaledTime >= _nextHeartbeat)
                {
                    _baseline = now;
                    _forceSend = false;
                    _nextHeartbeat = Time.unscaledTime + 5f;
                    Broadcast(now);
                }
                return;
            }

            // Convidado: diferença feita aqui → host.
            if (now != _baseline)
            {
                var delta = now - _baseline;
                _baseline = now;
                NetSession.SendGame(MsgType.MoneyDelta, w => w.Put(delta), DeliveryMethod.ReliableOrdered);
                Plugin.Logger.LogInfo($"[MoneySync] mudança local de {delta:+#;-#;0} G enviada ao host");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[MoneySync] {e.Message}"); }
    }

    private static void Broadcast(long total) =>
        NetSession.SendGame(MsgType.MoneyState, w => w.Put(total), DeliveryMethod.ReliableOrdered);

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.MoneyDelta when NetSession.Current == NetSession.Mode.Host:
            {
                var delta = r.GetLong();
                if (!Ready()) return; // virada do dia: o mundo novo vai junto com o valor certo
                var ui = ExternalData.Instance.UserInfo;
                var total = Math.Clamp((long)ui.PlayerMoney + delta, 0, uint.MaxValue);
                ui.PlayerMoney = (uint)total;
                _baseline = total;
                Broadcast(total);
                Plugin.Logger.LogInfo($"[MoneySync] jogador #{from}: {delta:+#;-#;0} G → carteira {total} G");
                DevBridge.PushEvent($"dinheiro:{total}");
                break;
            }
            case MsgType.MoneyState when NetSession.Current == NetSession.Mode.Client && from == Protocol.HostPlayerId:
            {
                var total = r.GetLong();
                if (!Ready()) return;
                var ui = ExternalData.Instance.UserInfo;
                // Mudança local que ainda não saiu (a mensagem chegou antes do Update deste quadro): mantém ela
                // por cima do total do host; a base fica no total, então o Update ainda manda essa diferença.
                var pending = _baseline >= 0 ? (long)ui.PlayerMoney - _baseline : 0;
                ui.PlayerMoney = (uint)Math.Clamp(total + pending, 0, uint.MaxValue);
                _baseline = total;
                DevBridge.PushEvent($"dinheiro:{total}");
                break;
            }
        }
    }

    public static object Status() => new { baseline = _baseline, money = ExternalData.IsInstance && ExternalData.Instance.UserInfo != null ? (long)ExternalData.Instance.UserInfo.PlayerMoney : -1 };
}
