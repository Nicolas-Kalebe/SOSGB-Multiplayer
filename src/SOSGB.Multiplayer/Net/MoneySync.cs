// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d4, #d36 e #d57.
using System;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Carteiras compartilhadas (4.1, D4/D36; Happy Energy na D57): o host guarda o valor oficial.
///
/// Em vez de interceptar cada função de dinheiro do jogo (AddMoney, SpendMoney, venda no Bazaar, prêmio de
/// festival...), cada jogo OBSERVA o próprio valor no UserInfo:
///  - convidado: quando o valor muda por conta própria (comprou, vendeu, ganhou), manda a diferença ao host.
///    A mudança local continua valendo na hora, então a loja funciona sem esperar a rede;
///  - host: aplica a diferença na carteira dele (nunca abaixo de 0) e manda o total a todos;
///    mudanças feitas no próprio host também viram total;
///  - convidado ao receber o total: ajusta a carteira local, sem gerar diferença de volta.
/// Depois de carregar um mundo (entrada, virada do dia), a base é zerada: o valor do snapshot não é diferença.
///
/// Carteiras: dinheiro (UserInfo.PlayerMoney) e Happy Energy (UserInfo.PlayerHappyPoint, a moeda dos Nature
/// Sprites / Happy Shoppe). A Happy Energy não era sincronizada: o que um convidado ganhava sumia na virada do dia.
///
/// Limite conhecido: se dois jogadores gastam ao mesmo tempo mais do que existe, o host corta em 0 (uma das
/// compras sai "de graça"). Entre amigos e com a carteira visível, é aceitável.
/// </summary>
public class MoneySync : MonoBehaviour
{
    public MoneySync(IntPtr ptr) : base(ptr) { }

    private sealed class Wallet
    {
        public string Name, Unit;
        public MsgType DeltaMsg, StateMsg;
        public Func<UserInfo, long> Get;
        public Action<UserInfo, uint> Set;
        public long Baseline = -1;   // último valor conhecido e já sincronizado
        public bool ForceSend;
        public float NextHeartbeat;
    }

    private static readonly Wallet[] Wallets =
    {
        new() { Name = "dinheiro", Unit = "G", DeltaMsg = MsgType.MoneyDelta, StateMsg = MsgType.MoneyState, Get = u => u.PlayerMoney, Set = (u, v) => u.PlayerMoney = v },
        new() { Name = "happy", Unit = "HE", DeltaMsg = MsgType.HappyDelta, StateMsg = MsgType.HappyState, Get = u => u.PlayerHappyPoint, Set = (u, v) => u.PlayerHappyPoint = v },
    };

    private static bool _subscribed;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        // O mundo novo traz o próprio valor.
        WorldSync.OnWorldReceived += () => { foreach (var w in Wallets) w.Baseline = -1; };
        NetSession.OnHosting += () => { foreach (var w in Wallets) w.Baseline = -1; };
        NetSession.OnJoined += () => { foreach (var w in Wallets) w.Baseline = -1; };
        NetSession.OnPlayerJoinedNamed += (id, name) =>
        {
            if (NetSession.Current == NetSession.Mode.Host) foreach (var w in Wallets) w.ForceSend = true;
        };
    }

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!ExternalData.IsInstance || ExternalData.Instance.UserInfo == null) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    private void Update()
    {
        try
        {
            if (!Ready()) return;
            if (Held.Count > 0) Held.Drain(OnMessage); // o que chegou enquanto carregava (D63)
            var ui = ExternalData.Instance.UserInfo;
            foreach (var w in Wallets) Tick(w, ui);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[MoneySync] {e.Message}"); }
    }

    private static void Tick(Wallet w, UserInfo ui)
    {
        var now = w.Get(ui);
        if (w.Baseline < 0) { w.Baseline = now; if (NetSession.Current == NetSession.Mode.Host) w.ForceSend = true; return; }

        if (NetSession.Current == NetSession.Mode.Host)
        {
            // Host: o valor dele é o oficial. Mudou (ou alguém entrou)? Manda o total. E um "lembrete" a cada 5 s.
            if (now != w.Baseline || w.ForceSend || Time.unscaledTime >= w.NextHeartbeat)
            {
                w.Baseline = now;
                w.ForceSend = false;
                w.NextHeartbeat = Time.unscaledTime + 5f;
                Broadcast(w, now);
            }
            return;
        }

        // Convidado: diferença feita aqui → host.
        if (now != w.Baseline)
        {
            var delta = now - w.Baseline;
            w.Baseline = now;
            NetSession.SendGame(w.DeltaMsg, x => x.Put(delta), DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo($"[MoneySync] {w.Name}: mudança local de {delta:+#;-#;0} {w.Unit} enviada ao host");
        }
    }

    private static void Broadcast(Wallet w, long total) =>
        NetSession.SendGame(w.StateMsg, x => x.Put(total), DeliveryMethod.ReliableOrdered);

    private static readonly HeldMessages Held = new("MoneySync");

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        foreach (var w in Wallets)
        {
            if (type != w.DeltaMsg && type != w.StateMsg) continue;
            // Não pronto (carregando, trocando de cena): guarda e aplica depois (D63). No host, durante a virada do dia,
            // HeldMessages não guarda: o mundo novo vai junto com o valor certo.
            if (!Ready()) { Held.HoldIfAllowed(type, from, r); return; }
            break;
        }
        foreach (var w in Wallets)
        {
            if (type == w.DeltaMsg && NetSession.Current == NetSession.Mode.Host)
            {
                var delta = r.GetLong();
                var ui = ExternalData.Instance.UserInfo;
                var total = Math.Clamp(w.Get(ui) + delta, 0, uint.MaxValue);
                w.Set(ui, (uint)total);
                w.Baseline = total;
                Broadcast(w, total);
                Plugin.Logger.LogInfo($"[MoneySync] jogador #{from}: {delta:+#;-#;0} {w.Unit} → {w.Name} {total} {w.Unit}");
                DevBridge.PushEvent($"{w.Name}:{total}");
                return;
            }
            if (type == w.StateMsg && NetSession.Current == NetSession.Mode.Client && from == Protocol.HostPlayerId)
            {
                var total = r.GetLong();
                var ui = ExternalData.Instance.UserInfo;
                // Mudança local que ainda não saiu (a mensagem chegou antes do Update deste quadro): mantém ela
                // por cima do total do host; a base fica no total, então o Update ainda manda essa diferença.
                var pending = w.Baseline >= 0 ? w.Get(ui) - w.Baseline : 0;
                w.Set(ui, (uint)Math.Clamp(total + pending, 0, uint.MaxValue));
                w.Baseline = total;
                DevBridge.PushEvent($"{w.Name}:{total}");
                return;
            }
        }
    }

    public static object Status()
    {
        var ui = ExternalData.IsInstance ? ExternalData.Instance.UserInfo : null;
        return new
        {
            baseline = Wallets[0].Baseline,
            money = ui != null ? (long)ui.PlayerMoney : -1,
            happyBaseline = Wallets[1].Baseline,
            happy = ui != null ? (long)ui.PlayerHappyPoint : -1,
        };
    }
}
