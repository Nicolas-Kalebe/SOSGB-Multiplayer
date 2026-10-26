// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d57.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Pedidos dos moradores compartilhados (D57; achado no inventário do save: MissionData não era sincronizado).
/// Os pedidos desbloqueiam o mundo (moinhos do Isaac, lojas novas, colmeias...); sem isto, o que um convidado
/// fazia se perdia na virada do dia (ele recebe o mundo do host).
///
///  - Cada jogo compara, a cada 1 s, os pedidos (MissionManager.OrderDatas: estado, progresso, datas) com a última
///    versão sincronizada.
///  - Convidado → host: só as MUDANÇAS — o estado novo e a DIFERENÇA de progresso. O host SOMA o progresso (pedidos
///    como "colha 200" ou "dê 20 presentes" contam a ajuda de todos) e fica com o estado mais avançado
///    (Antes → Disponível → Aceito → Completo → Loja aberta). Depois manda a lista oficial a todos.
///  - Exceção: pedidos que o jogo define por VALOR (afeição de animais e pets, venda do dia no Bazaar: métodos
///    Overwrite* do MissionManager) não são contadores. Somar faria dois jogos calculando a mesma afeição de um animal
///    compartilhado contarem em dobro. Esses vão com o valor e o host fica com o MAIOR (achado na revisão da D57).
///  - Host → todos: a lista inteira quando muda.
///  - O efeito no mundo (loja que abre, moinho que aparece) é aplicado pelo jogo do host na virada do dia, como sem
///    o mod; o convidado recebe o mundo pronto. A recompensa vai para a mochila de quem entregou (individual).
/// </summary>
public class MissionSync : MonoBehaviour
{
    public MissionSync(IntPtr ptr) : base(ptr) { }

    private const byte KindDelta = 0, KindSnapshot = 1;
    private const byte FlagAbsolute = 1;

    /// <summary>Pedidos cujo progresso o jogo define por valor (vistos mudando dentro de um Overwrite* do MissionManager).</summary>
    private static readonly HashSet<uint> AbsoluteIds = new();

    private struct Order
    {
        public uint Id;
        public int State;
        public int Progress;
        public long Received, Completed;
    }

    private static readonly Dictionary<uint, Order> Baseline = new();
    private static bool _baselineNewShop;
    private static bool _hasBaseline;
    private static bool _subscribed;
    private static bool _forceSnapshot;
    private static bool _hostNewShop; // convidado: último "loja nova" visto do host (só a passagem para true vale)
    private float _nextCheck;
    public static int Sent, Received;
    private static readonly HeldMessages Held = new("MissionSync");

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => _hasBaseline = false;
        NetSession.OnHosting += () => _hasBaseline = false;
        NetSession.OnJoined += () => _hasBaseline = false;
        NetSession.OnPlayerJoinedNamed += (id, name) => { if (NetSession.Current == NetSession.Mode.Host) _forceSnapshot = true; };
    }

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!MissionManager.IsInstance || MissionManager.Instance.OrderDatas == null) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    private static Dictionary<uint, Order> Capture()
    {
        var res = new Dictionary<uint, Order>();
        foreach (var od in MissionManager.Instance.OrderDatas)
        {
            if (od == null) continue;
            res[od.Id] = new Order { Id = od.Id, State = (int)od.State, Progress = od.ProgressValue, Received = od.ReceivedTicks, Completed = od.CompleteTicks };
        }
        return res;
    }

    private static void SetBaseline(Dictionary<uint, Order> now)
    {
        Baseline.Clear();
        foreach (var kv in now) Baseline[kv.Key] = kv.Value;
        _baselineNewShop = MissionManager.Instance.IsNewOpenShop;
        _hasBaseline = true;
    }

    private void Update()
    {
        try
        {
            if (!Ready()) return;
            if (Held.Count > 0) Held.Drain(OnMessage); // o que chegou enquanto carregava (D63)
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            var now = Capture();
            if (!_hasBaseline) { SetBaseline(now); if (NetSession.Current == NetSession.Mode.Host) _forceSnapshot = true; return; }

            var changed = new List<Order>();
            foreach (var kv in now)
            {
                if (Baseline.TryGetValue(kv.Key, out var old) && old.State == kv.Value.State && old.Progress == kv.Value.Progress
                    && old.Received == kv.Value.Received && old.Completed == kv.Value.Completed) continue;
                var o = kv.Value;
                if (NetSession.Current == NetSession.Mode.Client && !AbsoluteIds.Contains(kv.Key))
                    o.Progress -= Baseline.TryGetValue(kv.Key, out var b) ? b.Progress : 0; // diferença
                changed.Add(o);
            }
            var newShop = MissionManager.Instance.IsNewOpenShop;
            var shopChanged = newShop != _baselineNewShop;
            if (changed.Count == 0 && !shopChanged && !_forceSnapshot) return;

            if (NetSession.Current == NetSession.Mode.Host)
            {
                _forceSnapshot = false;
                SetBaseline(now);
                SendSnapshot(now);
            }
            else
            {
                SetBaseline(now);
                Send(KindDelta, newShop, changed);
                Plugin.Logger.LogInfo($"[MissionSync] {changed.Count} pedido(s) mudaram aqui: enviado ao host");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[MissionSync] {e.Message}"); }
    }

    private static void SendSnapshot(Dictionary<uint, Order> now) => Send(KindSnapshot, MissionManager.Instance.IsNewOpenShop, new List<Order>(now.Values));

    private static void Send(byte kind, bool newShop, List<Order> orders)
    {
        NetSession.SendGame(MsgType.MissionData, w =>
        {
            w.Put(kind); w.Put(newShop); w.Put(orders.Count);
            foreach (var o in orders)
            {
                w.Put(o.Id); w.Put((byte)o.State); w.Put(o.Progress); w.Put(o.Received); w.Put(o.Completed);
                w.Put(AbsoluteIds.Contains(o.Id) ? FlagAbsolute : (byte)0);
            }
        }, DeliveryMethod.ReliableOrdered);
        Sent++;
    }

    private static List<Order> Read(NetDataReader r, out byte kind, out bool newShop)
    {
        kind = r.GetByte();
        newShop = r.GetBool();
        var n = r.GetInt();
        var list = new List<Order>(n);
        for (var i = 0; i < n; i++)
        {
            var o = new Order { Id = r.GetUInt(), State = r.GetByte(), Progress = r.GetInt(), Received = r.GetLong(), Completed = r.GetLong() };
            if ((r.GetByte() & FlagAbsolute) != 0) AbsoluteIds.Add(o.Id);
            list.Add(o);
        }
        return list;
    }

    private static MissionManager.OrderData Find(uint id)
    {
        var od = MissionManager.Instance.GetMissionData(id);
        if (od != null) return od;
        // Pedido que este jogo ainda não tinha na lista: cria (o jogo também cria os pedidos com new OrderData(id)).
        od = new MissionManager.OrderData(id);
        MissionManager.Instance.OrderDatas.Add(od);
        return od;
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.MissionData) return;
        if (!Ready()) { Held.HoldIfAllowed(type, from, r); return; } // D63 (na virada do dia do host: o mundo novo traz os pedidos)
        var orders = Read(r, out var kind, out var newShop);
        Received++;
        try
        {
            if (NetSession.Current == NetSession.Mode.Host && kind == KindDelta)
            {
                // Convidado mudou pedidos: soma o progresso e fica com o estado mais avançado.
                foreach (var o in orders)
                {
                    var od = Find(o.Id);
                    if (o.State > (int)od.State) od.State = (MissionManager.OrderState)o.State;
                    if (AbsoluteIds.Contains(o.Id)) { if (o.Progress > od.ProgressValue) od.SetProgress(o.Progress); }
                    else if (o.Progress != 0) od.SetProgress(Math.Max(0, od.ProgressValue + o.Progress));
                    if (o.Received != 0 && (od.ReceivedTicks == 0 || o.Received < od.ReceivedTicks)) od.ReceivedTicks = o.Received;
                    if (o.Completed > od.CompleteTicks) od.CompleteTicks = o.Completed;
                }
                if (newShop) MissionManager.Instance.IsNewOpenShop = true;
                var now = Capture();
                SetBaseline(now);
                SendSnapshot(now);
                Plugin.Logger.LogInfo($"[MissionSync] {orders.Count} pedido(s) atualizado(s) por #{from}");
                DevBridge.PushEvent("pedidos:atualizados");
            }
            else if (NetSession.Current == NetSession.Mode.Client && kind == KindSnapshot && from == Protocol.HostPlayerId)
            {
                // Lista oficial do host. Progresso feito aqui e ainda não enviado continua por cima (o Update manda depois).
                var local = Capture();
                foreach (var o in orders)
                {
                    var od = Find(o.Id);
                    var hasLocal = local.TryGetValue(o.Id, out var lo);
                    var pending = hasLocal && Baseline.TryGetValue(o.Id, out var bo) ? lo.Progress - bo.Progress : 0;
                    var pendingState = hasLocal && Baseline.TryGetValue(o.Id, out var bs) && lo.State != bs.State ? lo.State : -1;
                    od.State = (MissionManager.OrderState)Math.Max(o.State, pendingState);
                    // Valor (afeição etc.): o maior entre o do host e o daqui; contador: o do host + o que ainda não foi mandado.
                    var value = AbsoluteIds.Contains(o.Id) ? Math.Max(o.Progress, hasLocal ? lo.Progress : 0) : Math.Max(0, o.Progress + pending);
                    if (od.ProgressValue != value) od.SetProgress(value);
                    // Mudança de estado feita aqui e ainda não enviada: mantém as datas locais (vão junto no próximo envio).
                    if (pendingState <= o.State)
                    {
                        od.ReceivedTicks = o.Received;
                        od.CompleteTicks = o.Completed;
                    }
                }
                // "Loja nova" é um aviso: só a passagem para true no host acende aqui. Repetir o true do host a cada
                // lista faria o aviso voltar depois que este jogador já viu (o host não recebe o false de ninguém).
                if (newShop && !_hostNewShop) MissionManager.Instance.IsNewOpenShop = true;
                _hostNewShop = newShop;
                // A base fica na lista do host: a diferença local que sobrou ainda vai ser mandada.
                Baseline.Clear();
                foreach (var o in orders) Baseline[o.Id] = o;
                _baselineNewShop = MissionManager.Instance.IsNewOpenShop;
                _hasBaseline = true;
                DevBridge.PushEvent("pedidos:recebidos");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[MissionSync] aplicar: {e.Message}"); }
    }

    public static object Status()
    {
        var active = new List<string>();
        if (MissionManager.IsInstance && MissionManager.Instance.OrderDatas != null)
            foreach (var od in MissionManager.Instance.OrderDatas)
                if (od != null && (int)od.State is >= 1 and <= 2) active.Add($"{od.Id}:{od.State}:{od.ProgressValue}");
        return new { tracked = Baseline.Count, sent = Sent, received = Received, active, absolute = AbsoluteIds.ToList() };
    }

    /// <summary>
    /// Descobre os pedidos definidos por valor: guarda o progresso antes de um Overwrite* do MissionManager e vê quais
    /// mudaram depois. Só lê; não muda nada do jogo.
    /// </summary>
    [HarmonyPatch]
    private static class OverwritePatch
    {
        private static IEnumerable<MethodBase> TargetMethods() =>
            new[] { nameof(MissionManager.OverwriteAnimalParamValue), nameof(MissionManager.OverwritePetParamValue), nameof(MissionManager.OverwriteOneDayBazaarSale) }
                .Select(n => (MethodBase)AccessTools.Method(typeof(MissionManager), n)).Where(m => m != null);

        private static void Prefix(out Dictionary<uint, int> __state)
        {
            __state = null;
            if (NetSession.Current == NetSession.Mode.Offline || !MissionManager.IsInstance || MissionManager.Instance.OrderDatas == null) return;
            __state = new Dictionary<uint, int>();
            foreach (var od in MissionManager.Instance.OrderDatas) if (od != null) __state[od.Id] = od.ProgressValue;
        }

        private static void Postfix(Dictionary<uint, int> __state)
        {
            if (__state == null) return;
            foreach (var od in MissionManager.Instance.OrderDatas)
                if (od != null && __state.TryGetValue(od.Id, out var before) && before != od.ProgressValue && AbsoluteIds.Add(od.Id))
                    Plugin.Logger.LogInfo($"[MissionSync] pedido {od.Id} é por valor (não soma)");
        }
    }
}
