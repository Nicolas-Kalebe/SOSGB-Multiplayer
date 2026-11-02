// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d4, #d36 e #d57.
using System;
using System.Collections.Generic;
using System.Linq;
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
/// D98 (revisão A09, escolha do usuário): se dois jogadores gastam ao mesmo tempo mais do que existe, o gasto que chegou
/// depois é RECUSADO pelo host (a carteira não muda) e quem gastou desfaz a compra: os itens que entraram na mochila ou no
/// armazém naquele instante saem, e o dinheiro volta sozinho com o total do host. Compra que não vira item (ampliação,
/// animal) não tem o que desfazer: aí vale a regra antiga (o host corta em 0). Antes (D96) o que faltava virava dívida.
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
        public byte Index;           // D98: número da carteira na mensagem de recusa
        public bool ForceSend;
        public float NextHeartbeat;
    }

    private static readonly Wallet[] Wallets =
    {
        new() { Name = "dinheiro", Unit = "G", Index = 0, DeltaMsg = MsgType.MoneyDelta, StateMsg = MsgType.MoneyState, Get = u => u.PlayerMoney, Set = (u, v) => u.PlayerMoney = v },
        new() { Name = "happy", Unit = "HE", Index = 1, DeltaMsg = MsgType.HappyDelta, StateMsg = MsgType.HappyState, Get = u => u.PlayerHappyPoint, Set = (u, v) => u.PlayerHappyPoint = v },
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
            if (NetSession.Current == NetSession.Mode.Client && Time.unscaledTime >= _nextTrack) { _nextTrack = Time.unscaledTime + 0.1f; TrackStock(); }
            foreach (var w in Wallets) Tick(w, ui);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[MoneySync] {e.Message}"); }
    }

    private static void Tick(Wallet w, UserInfo ui)
    {
        var now = w.Get(ui);
        if (w.Baseline < 0) { w.Baseline = now; if (NetSession.Current == NetSession.Mode.Host) w.ForceSend = true; return; }

        // D84: assistindo à cena de outro jogador (e logo depois dela), um GANHO aqui é o prêmio da cena, que já foi para quem
        // estava nela (a carteira é compartilhada). Desfaz e não conta. Ex.: a contagem regressiva dá +500 de Happy Energy
        // depois do fim da cena, e a convidada ganhava também (testado). Gastos seguem normais.
        if (now > w.Baseline && EventWatch.BlockRewards)
        {
            w.Set(ui, (uint)w.Baseline);
            Plugin.Logger.LogInfo($"[MoneySync] {w.Name}: ganho de {now - w.Baseline} {w.Unit} na cena assistida desfeito (o prêmio é de quem estava nela)");
            return;
        }

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

        // Convidado: diferença feita aqui → host. Gasto leva um número (D98: o host pode recusar e mandar desfazer).
        if (now != w.Baseline)
        {
            var delta = now - w.Baseline;
            w.Baseline = now;
            var seq = 0;
            if (delta < 0)
            {
                seq = ++_spendSeq;
                Spends[seq] = (w, delta, Time.unscaledTime);
                if (Spends.Count > 50) Spends.Remove(Spends.Keys.Min());
            }
            NetSession.SendGame(w.DeltaMsg, x => { x.Put(delta); x.Put(seq); }, DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo($"[MoneySync] {w.Name}: mudança local de {delta:+#;-#;0} {w.Unit} enviada ao host");
        }
    }

    private static void Broadcast(Wallet w, long total) =>
        NetSession.SendGame(w.StateMsg, x => x.Put(total), DeliveryMethod.ReliableOrdered);

    private static readonly HeldMessages Held = new("MoneySync");

    // ---------- D98: desfazer a compra recusada ----------

    private static int _spendSeq;
    private static float _nextTrack;
    private static readonly Dictionary<int, (Wallet w, long delta, float at)> Spends = new();
    // Itens que entraram na mochila/armazém, com o instante (a compra põe o item no mesmo quadro ou logo depois do gasto).
    private static readonly List<(float at, bool house, uint item, int quality, int n)> Arrivals = new();
    private static Dictionary<(uint, int), int> _bagPrev, _housePrev;
    private const float ArrivalWindow = 1.5f;

    private static Dictionary<(uint, int), int> Count(StorageManager st)
    {
        var d = new Dictionary<(uint, int), int>();
        var datas = st?.ItemDatas;
        if (datas == null) return d;
        foreach (var x in datas)
        {
            if (x == null || x.ItemId == 0 || x.Stack <= 0) continue;
            var k = (x.ItemId, x.QualityValue);
            d[k] = d.GetValueOrDefault(k) + x.Stack;
        }
        return d;
    }

    private static void TrackStock()
    {
        try
        {
            var inv = InventoryManager.Instance;
            var bag = Count(inv.BagItemStorage);
            var house = Count(inv.HouseStorage);
            var t = Time.unscaledTime;
            void Diff(Dictionary<(uint, int), int> prev, Dictionary<(uint, int), int> cur, bool isHouse)
            {
                if (prev == null) return;
                foreach (var kv in cur)
                {
                    var more = kv.Value - prev.GetValueOrDefault(kv.Key);
                    if (more > 0) Arrivals.Add((t, isHouse, kv.Key.Item1, kv.Key.Item2, more));
                }
            }
            Diff(_bagPrev, bag, false);
            Diff(_housePrev, house, true);
            _bagPrev = bag;
            _housePrev = house;
            Arrivals.RemoveAll(a => t - a.at > 10f);
        }
        catch { /* inventário indisponível agora */ }
    }

    /// <summary>Convidado: o host recusou o gasto. Tira o que entrou com ele; sem item para tirar, manda o gasto de novo
    /// como "sem recusa" (a regra antiga: o host corta em 0).</summary>
    private static void OnRefused(byte walletIndex, int seq)
    {
        if (!Spends.TryGetValue(seq, out var sp)) return;
        Spends.Remove(seq);
        var got = Arrivals.Where(a => a.at >= sp.at - 0.2f && a.at <= sp.at + ArrivalWindow).ToList();
        if (got.Count == 0)
        {
            var d = sp.delta;
            NetSession.SendGame(sp.w.DeltaMsg, x => { x.Put(d); x.Put(-1); }, DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo($"[MoneySync] {sp.w.Name}: gasto {d} recusado, mas não virou item (nada a desfazer): mandado sem recusa");
            return;
        }
        var inv = InventoryManager.Instance;
        var removed = 0;
        foreach (var a in got)
        {
            var it = new BokuMono.Data.ItemData { ItemId = a.item, QualityValue = a.quality, Stack = a.n };
            var left = FarmSync.TakeFrom(a.house ? inv.HouseStorage : inv.BagItemStorage, it, a.n);
            if (left > 0) left = FarmSync.TakeFrom(a.house ? inv.BagItemStorage : inv.HouseStorage, it, left); // já mudou de lugar
            removed += a.n - left;
            Arrivals.Remove(a);
        }
        Plugin.Logger.LogInfo($"[MoneySync] {sp.w.Name}: gasto {sp.delta} recusado pelo host (saldo não cobria): {removed} item(ns) devolvido(s)");
        GameNotify.Show("Não havia saldo para essa compra (outro jogador gastou ao mesmo tempo). A compra foi desfeita.");
        DevBridge.PushEvent($"compra:desfeita:{removed}");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type == MsgType.MoneyRefused)
        {
            if (NetSession.Current != NetSession.Mode.Client || from != Protocol.HostPlayerId) return;
            var wi = r.GetByte();
            var sq = r.GetInt();
            try { OnRefused(wi, sq); } catch (Exception e) { Plugin.Logger.LogWarning($"[MoneySync] desfazer compra: {e.Message}"); }
            return;
        }
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
                var seq = r.AvailableBytes >= 4 ? r.GetInt() : -1;
                var ui = ExternalData.Instance.UserInfo;
                if (delta < 0 && seq > 0 && w.Get(ui) + delta < 0)
                {
                    // D98: o saldo não cobre (outro jogador gastou junto): não aplica; quem gastou desfaz a compra.
                    var wi = w.Index;
                    NetSession.SendTo(from, MsgType.MoneyRefused, x => { x.Put(wi); x.Put(seq); }, DeliveryMethod.ReliableOrdered);
                    Broadcast(w, w.Get(ui)); // o total sem esse gasto: o dinheiro de quem gastou volta
                    Plugin.Logger.LogInfo($"[MoneySync] {w.Name}: gasto de {delta} de #{from} recusado (saldo {w.Get(ui)})");
                    return;
                }
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
