// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d84.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BokuMono;
using BokuMono.Data;
using BokuMono.FieldEvent;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Assistir junto à cena do festival (D84; pedido do usuário: "tem como colocar pra visualizar a cena junto o convidado?
/// mesmo sem poder interagir?" e "tenta sincronizar com os inputs de quem tá na cena pro diálogo passar junto").
///  - Quem começa uma cena na janela do festival (dia de festival, antes de o festival terminar) avisa os outros: id e
///    roteiro da cena e os dados com que o jogo a abriu (EventManager.OpenMessage público: cadastros da cena, mapa e ponto
///    de chegada). A cada fala, tela ou escolha que espera o jogador, avisa o índice do comando a que chegou.
///  - Quem está no MESMO mapa, livre, abre a mesma cena pelo mesmo caminho público do jogo (o privado sozinho deixava o
///    jogo "começando cena" sem a cena: personagem preso, visto no teste) e assiste: cada fala/tela/escolha fica parada até
///    quem está na cena passar dela (aí o mod fecha a janela aqui). Comandos com efeito no mundo (prêmio, flag, fim do
///    festival, troca de mapa, Happy Energy, minigame) são pulados em quem assiste: o resultado vem do jogo de quem se
///    inscreveu (SharedSync). O personagem de quem assiste fica escondido; o protagonista visto é o avatar do outro.
///  - Segurança: se a cena não abrir em 6 s, o mod a encerra (ForceEventClose), clareia a tela e devolve o controle.
///    Se a cena de quem está nela acaba e a daqui não, a daqui é encerrada.
/// </summary>
public class EventWatch : MonoBehaviour
{
    public EventWatch(IntPtr ptr) : base(ptr) { }

    private const byte KStart = 1, KStep = 2, KEnd = 3;
    private const byte WaitMessage = 1, WaitUi = 2, WaitDialog = 3;
    private const float OpenTimeout = 25f; // a cena carrega de forma assíncrona (EventConductor.Load)

    private sealed class StartInfo
    {
        public uint Event;
        public string Script = "";
        public List<uint> Masters = new();
        public uint NextField;
        public string Spawn = "";
    }

    // ----- quem está na cena (transmite) -----
    private static bool _broadcasting;
    private static uint _bcEvent;
    private static float _bcIdleSince = -1;
    private static List<uint> _pubMasters;   // dados da abertura pública (o público chama o privado no fim)
    private static uint _pubNextField;
    private static string _pubSpawn;

    // ----- quem assiste -----
    public static bool Watching { get; private set; }
    private static byte _watchedId = NetSession.UnknownId;
    private static uint _curEvent;
    private static readonly Dictionary<uint, int> HostStep = new();
    private static readonly HashSet<uint> HostDone = new();
    private static readonly Queue<StartInfo> Pending = new();
    private static bool _hostEnded;
    private static float _hostEndedAt;
    private static int _myWait = -1;
    private static byte _myWaitKind;
    private static float _waitSince;
    private static float _startedAt;
    private static bool _everInEvent;
    private static bool _starting;
    private static bool _testAuto;               // teste com uma instância: as falas passam sozinhas
    private static string _openMode = "start";   // teste: start (StartEvent) | talk | talkfade (TalkEventStart)
    private static readonly List<Renderer> Hidden = new();
    private static UIManager _ui;
    private static bool _subscribed;
    public static string Last = "";

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id => { if (Watching && id == _watchedId) { _hostEnded = true; _hostEndedAt = Time.unscaledTime; } };
    }

    private static bool FestivalWindow()
    {
        if (!FestivalExecutor.IsInstance) return false;
        var fe = FestivalExecutor.Instance;
        return fe.IsInSession && fe.State != FestivalExecutor.FestivalState.JoinAfter;
    }

    private static bool InEvent => GameController.IsInstance && GameController.Instance.IsPlayingEvent;

    /// <summary>Ligado na configuração, ou só nesta execução por SOSGB_WATCH_FESTIVAL=1 (testes; o .cfg fica como está).</summary>
    private static bool Enabled => Plugin.WatchFestivalScenes.Value || System.Environment.GetEnvironmentVariable("SOSGB_WATCH_FESTIVAL") == "1";

    /// <summary>
    /// A cena da competição monta os competidores pela classificação do festival (LoadModel/GetModelParam); sem ela a
    /// corrotina de carregamento quebra (índice fora da lista, Player.log) e a cena fica parada. A classificação sai na
    /// inscrição e chega pela SharedSync: quem assiste espera por ela antes de abrir.
    /// </summary>
    private static bool RankingReady()
    {
        try { return FestivalExecutor.IsInstance && FestivalExecutor.Instance.GetCurrentFestivalPlayerRanking() > 0; }
        catch { return false; }
    }

    private static StartInfo _deferred;
    private static float _deferredSince;
    private static bool Executing => EventManager.IsInstance && EventManager.Instance.IsExecute;

    private static void Send(byte kind, Action<NetDataWriter> body)
    {
        var me = NetSession.LocalId;
        NetSession.SendGame(MsgType.EventWatch, w => { w.Put(kind); w.Put(me); body?.Invoke(w); }, DeliveryMethod.ReliableOrdered);
    }

    // ---------- quem está na cena ----------

    private static void OnLocalEventOpened(uint eventId, string scriptId)
    {
        var masters = _pubMasters;
        var nextField = _pubNextField;
        var spawn = _pubSpawn ?? "";
        _pubMasters = null;
        if (NetSession.Current == NetSession.Mode.Offline || _starting || Watching || !FestivalWindow()) return;
        _broadcasting = true;
        _bcEvent = eventId;
        _bcIdleSince = -1;
        var field = FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0;
        var script = scriptId ?? "";
        var ids = masters ?? new List<uint>();
        Send(KStart, w =>
        {
            w.Put(eventId); w.Put(script); w.Put(field);
            w.Put(ids.Count); foreach (var m in ids) w.Put(m);
            w.Put(nextField); w.Put(spawn);
        });
        Plugin.Logger.LogInfo($"[EventWatch] cena {eventId} ({script}) começou aqui (cadastros {string.Join(",", ids)}, mapa {nextField}, ponto '{spawn}'): os outros no mapa {field} podem assistir");
    }

    private static void OnLocalWait(int commandIndex, byte kind)
    {
        if (Watching) { _myWait = commandIndex; _myWaitKind = kind; _waitSince = Time.unscaledTime; return; }
        if (!_broadcasting) return;
        var ev = _bcEvent;
        Send(KStep, w => { w.Put(ev); w.Put(commandIndex); w.Put(kind); });
    }

    // ---------- quem assiste ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.EventWatch) return;
        var raw = r.GetRemainingBytes();
        var rr = new NetDataReader(raw);
        var kind = rr.GetByte();
        var who = rr.GetByte();
        if (NetSession.Current == NetSession.Mode.Host)
        {
            if (who != from) return;
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId && id != from)
                    NetSession.SendTo(id, MsgType.EventWatch, w => w.Put(raw), DeliveryMethod.ReliableOrdered);
        }
        else if (from != Protocol.HostPlayerId) return;
        if (who == NetSession.LocalId) return;
        try
        {
            switch (kind)
            {
                case KStart:
                {
                    var info = new StartInfo { Event = rr.GetUInt(), Script = rr.GetString() };
                    var field = rr.GetUInt();
                    var n = rr.GetInt();
                    for (var i = 0; i < n; i++) info.Masters.Add(rr.GetUInt());
                    info.NextField = rr.GetUInt();
                    info.Spawn = rr.GetString();
                    if (Watching && who == _watchedId)
                    {
                        // a cena anterior do outro acabou e começou outra (cenas em sequência)
                        foreach (var k in HostStep.Keys.ToList()) if (k != info.Event) HostDone.Add(k);
                        if (_curEvent != 0 && _curEvent != info.Event) HostDone.Add(_curEvent);
                        Pending.Enqueue(info);
                        _hostEnded = false;
                        return;
                    }
                    if (!CanWatch(field, info, out var why))
                    {
                        Last = $"não assistiu a cena {info.Event} de #{who}: {why}";
                        Plugin.Logger.LogInfo($"[EventWatch] {Last}");
                        return;
                    }
                    BeginWatch(who, info);
                    break;
                }
                case KStep:
                {
                    var ev = rr.GetUInt();
                    var idx = rr.GetInt();
                    if (!Watching || who != _watchedId) return;
                    HostStep[ev] = HostStep.TryGetValue(ev, out var cur) ? Math.Max(cur, idx) : idx;
                    break;
                }
                case KEnd:
                    if (!Watching || who != _watchedId) return;
                    _hostEnded = true;
                    _hostEndedAt = Time.unscaledTime;
                    break;
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] mensagem: {e.Message}"); }
    }

    private static bool CanWatch(uint field, StartInfo info, out string why)
    {
        why = null;
        if (!Enabled) { why = "desligado na configuração"; return false; }
        if (info.Masters.Count == 0) { why = "sem os dados de abertura da cena"; return false; }
        if (!FieldManager.IsInstance || FieldManager.Instance.CurrentFieldId != field) { why = "em outro mapa"; return false; }
        if (InEvent) { why = "já está numa cena"; return false; }
        if (DaySync.LocalSleeping || DaySync.InDayChange) { why = "dormindo"; return false; }
        if (ClockSync.InPauseMenu) { why = "no menu de pausa"; return false; }
        if (_ui == null) _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        if (_ui != null && _ui.CurrentUIType != UIType.Main) { why = "numa tela ou conversa"; return false; }
        return true;
    }

    private static void BeginWatch(byte who, StartInfo info)
    {
        Watching = true;
        _watchedId = who;
        HostStep.Clear();
        HostDone.Clear();
        Pending.Clear();
        _hostEnded = false;
        var name = NetSession.Players.TryGetValue(who, out var p) ? p.Name : "outro jogador";
        Last = $"assistindo a cena {info.Event} de {name}";
        Plugin.Logger.LogInfo($"[EventWatch] {Last}");
        _deferred = info;                       // abre quando a classificação do festival estiver aqui (Update)
        _deferredSince = Time.unscaledTime;
        _startedAt = Time.unscaledTime;
    }

    /// <summary>
    /// Abre a cena pela preparação do jogo (GameController.StartEvent: câmera, objetos da cena, personagem, som; depois o
    /// OpenMessage), com os mesmos cadastros de quem está nela. Testado: o OpenMessage direto deixava a corrotina de
    /// carregamento parada e o personagem preso; o TalkEventStart (o de depois de conversa) ficava na tela preta carregando.
    /// </summary>
    private static bool StartLocal(StartInfo info)
    {
        _curEvent = info.Event;
        _myWait = -1;
        _startedAt = Time.unscaledTime;
        _everInEvent = false;
        var list = new Il2CppSystem.Collections.Generic.List<EventMasterData>();
        foreach (var md in MasterDataManager.Instance.EventMasterData)
            if (md != null && info.Masters.Contains(md.Id)) list.Add(md);
        if (list.Count == 0) { StopWatch($"cadastro da cena {info.Event} não encontrado"); return false; }
        _starting = true;
        try
        {
            // mapa de destino 0 = "nenhum": usa o mapa atual (testes com uma instância mandavam 0)
            var next = info.NextField != 0 ? info.NextField : (FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0);
            var gc = GameController.Instance;
            switch (_openMode)
            {
                case "talk": gc.TalkEventStart(list, next, info.Spawn, "", false, false, null, null, null, null); break;
                case "talkfade": gc.TalkEventStart(list, next, info.Spawn, "", true, false, null, null, null, null); break;
                default: gc.StartEvent(list, next, info.Spawn, ""); break;
            }
            Plugin.Logger.LogInfo($"[EventWatch] abrindo a cena {info.Event} ({_openMode}, mapa {next}, ponto '{info.Spawn}')");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] abrir a cena {info.Event}: {e.Message}"); Recover("erro ao abrir"); return false; }
        finally { _starting = false; }
        return true;
    }

    private static void StopWatch(string why)
    {
        Watching = false;
        _testAuto = false;
        _deferred = null;
        _watchedId = NetSession.UnknownId;
        _curEvent = 0;
        _myWait = -1;
        Pending.Clear();
        foreach (var rd in Hidden) if (rd != null) rd.enabled = true;
        Hidden.Clear();
        Last = $"parou de assistir: {why}";
        Plugin.Logger.LogInfo($"[EventWatch] {Last}");
    }

    /// <summary>A cena daqui não abriu ou ficou para trás: encerra, clareia a tela e devolve o controle.</summary>
    private static void Recover(string why)
    {
        try { if (EventManager.IsInstance) EventManager.Instance.ForceEventClose(null); } catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] encerrar: {e.Message}"); }
        // ainda em cena: o fim normal de cena do jogo (o que o OnFinishEventEnd chama), com os prêmios ainda bloqueados
        if (InEvent && _curEvent != 0)
        {
            try { GameController.Instance.TalkEventEnd(_curEvent, "", null, false, false, false); Plugin.Logger.LogInfo($"[EventWatch] fim de cena forçado ({_curEvent})"); }
            catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] fim de cena: {e.Message}"); }
        }
        try { FadeManager.FadeIn(null, null, 0f, 0f); } catch { /* segue */ }
        StopWatch(why);
    }

    /// <summary>O personagem de quem assiste some: o protagonista visto é o avatar de quem está na cena.</summary>
    private static void HideLocalPlayer()
    {
        var t = GameController.IsInstance ? GameController.Instance.GetPlayerTransform() : null;
        if (t == null) return;
        foreach (var rd in t.GetComponentsInChildren<Renderer>(true))
        {
            if (rd == null || !rd.enabled) continue;
            rd.enabled = false;
            if (!Hidden.Contains(rd)) Hidden.Add(rd);
        }
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline && !_testAuto)
            {
                if (Watching) Recover("sem conexão");
                _broadcasting = false;
                return;
            }

            if (_broadcasting)
            {
                if (InEvent) _bcIdleSince = -1;
                else if (_bcIdleSince < 0) _bcIdleSince = Time.unscaledTime;
                else if (Time.unscaledTime - _bcIdleSince > 1.5f)
                {
                    _broadcasting = false;
                    Send(KEnd, null);
                    Plugin.Logger.LogInfo("[EventWatch] cena terminou aqui: avisando quem assiste");
                }
            }

            if (!Watching) return;
            if (_deferred != null)
            {
                if (RankingReady() || _testAuto) { var d = _deferred; _deferred = null; StartLocal(d); }
                else if (Time.unscaledTime - _deferredSince > 8f) { _deferred = null; StopWatch("a classificação do festival não chegou: cena não aberta"); }
                return;
            }
            if (!_testAuto) HideLocalPlayer();

            if (InEvent || Executing) _everInEvent = true;
            else if (!_everInEvent)
            {
                // ainda abrindo; se não abrir, desfaz tudo (nunca deixa o personagem preso)
                if (Time.unscaledTime - _startedAt > OpenTimeout) Recover($"a cena {_curEvent} não abriu aqui");
                return;
            }
            else
            {
                // a cena daqui acabou: a próxima da sequência, ou fim
                if (Pending.Count > 0) StartLocal(Pending.Dequeue());
                else StopWatch(_hostEnded ? "a cena terminou" : "a cena daqui terminou antes");
                return;
            }

            // fala/tela/escolha esperando: passa quando o outro já passou dela (ou já terminou a cena)
            if (_myWait >= 0)
            {
                var done = _hostEnded || HostDone.Contains(_curEvent) || Pending.Count > 0;
                var passed = HostStep.TryGetValue(_curEvent, out var hs) && hs > _myWait;
                if (_testAuto) passed = Time.unscaledTime - _waitSince > 1.5f;
                if (done || passed) Advance();
            }

            // o outro terminou faz tempo e a cena daqui não: encerra
            if (_hostEnded && Time.unscaledTime - _hostEndedAt > 20f) Recover("cena encerrada (o outro já tinha terminado)");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] {e.Message}"); }
    }

    private static void Advance()
    {
        if (_ui == null) _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        var kind = _myWaitKind;
        _myWait = -1;
        if (_ui == null) return;
        try
        {
            if (kind == WaitMessage) _ui.CloseMessage(null, null);
            else if (kind == WaitDialog) _ui.CloseDialog(null, null, true, false, false);
            else _ui.CloseMenu(null, null, false, null, false);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] passar: {e.Message}"); }
    }

    // ---------- testes (DevBridge) ----------

    /// <summary>event watch &lt;id...&gt; | event close | event state — assistir uma cena com uma instância só (as falas passam sozinhas).</summary>
    public static string Test(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length >= 2 && a[0] == "mode") { _openMode = a[1]; return "modo " + _openMode; }
        if (a.Length >= 2 && a[0] == "watch")
        {
            var info = new StartInfo { Event = uint.Parse(a[1]) };
            for (var i = 1; i < a.Length; i++) info.Masters.Add(uint.Parse(a[i]));
            Watching = true;
            _testAuto = true;
            _watchedId = NetSession.UnknownId;
            HostStep.Clear(); HostDone.Clear(); Pending.Clear();
            _hostEnded = false;
            return StartLocal(info) ? "abrindo" : Last;
        }
        if (a.Length >= 3 && a[0] == "raw")
        {
            // reproduz a abertura que travou a convidada (só o OpenMessage privado), para testar a recuperação
            Watching = true; _testAuto = true; _everInEvent = false; _startedAt = Time.unscaledTime; _curEvent = uint.Parse(a[1]);
            _starting = true;
            try { EventManager.Instance.OpenMessage(uint.Parse(a[1]), a[2], false, null); } finally { _starting = false; }
            return "aberta pelo caminho privado; a recuperação deve agir em 6 s se não abrir";
        }
        if (a.Length >= 1 && a[0] == "close") { Recover("teste"); return "encerrada"; }
        string cond;
        try
        {
            var cd = EventManager.Instance.conductor;
            var pd = cd?.PrevData;
            var cur = pd?.GetCurrentMasterData();
            cond = $"condutor={(cd != null)} dados={(pd != null)} cenaAtual={cur?.Id}";
        }
        catch (Exception e) { cond = "condutor: " + e.Message; }
        return $"emCena={InEvent} executando={Executing} assistindo={Watching} cena={_curEvent} espera={_myWait} {cond} last={Last}";
    }

    public static object Status() => new { watching = Watching, watched = _watchedId, cur = _curEvent, myWait = _myWait, hostStep = HostStep, broadcasting = _broadcasting, last = Last };

    // ---------- ganchos ----------

    private static MethodBase OpenMessageOverload(bool byId) =>
        AccessTools.GetDeclaredMethods(typeof(EventManager)).First(m => m.Name == "OpenMessage" && m.GetParameters().Length == 4
                                                                      && (m.GetParameters()[0].ParameterType == typeof(uint)) == byId);

    /// <summary>Abertura pública (a que o jogo usa): guarda os dados para mandar junto.</summary>
    [HarmonyPatch]
    private static class OpenMessagePublicPatch
    {
        private static MethodBase TargetMethod() => OpenMessageOverload(byId: false);

        private static void Prefix(Il2CppSystem.Collections.Generic.List<EventMasterData> masterList, uint nextFieldId, string nextSpawnName)
        {
            try
            {
                var ids = new List<uint>();
                if (masterList != null) foreach (var m in masterList) if (m != null) ids.Add(m.Id);
                _pubMasters = ids;
                _pubNextField = nextFieldId;
                _pubSpawn = nextSpawnName;
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] abertura pública: {e.Message}"); }
        }
    }

    /// <summary>Toda cena começa aqui (id + roteiro); o público chama este no fim.</summary>
    [HarmonyPatch]
    private static class OpenMessagePatch
    {
        private static MethodBase TargetMethod() => OpenMessageOverload(byId: true);

        private static void Postfix(uint eventId, string scriptId)
        {
            if (_starting) Plugin.Logger.LogInfo($"[EventWatch] abertura privada chamada para assistir: {eventId} ({scriptId}); executando={Executing}");
            try { OnLocalEventOpened(eventId, scriptId); }
            catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] início da cena: {e.Message}"); }
        }
    }

    /// <summary>Diagnóstico (só com a ponte de teste): cada comando executado em quem assiste e em quem está na cena.</summary>
    [HarmonyPatch(typeof(EventExecCommandBase), nameof(EventExecCommandBase.Execute))]
    private static class TraceCommandsPatch
    {
        private static void Postfix(EventExecCommandBase __instance)
        {
            if (!Plugin.DevMode || (!Watching && !_broadcasting) || __instance == null) return;
            try { Plugin.Logger.LogInfo($"[EventWatch] {(Watching ? "assiste" : "cena")} cmd {__instance.commandIndex} {__instance.Type} {__instance.GetIl2CppType().Name}"); }
            catch { /* diagnóstico */ }
        }
    }

    [HarmonyPatch(typeof(Message), nameof(Message.Execute))]
    private static class MessagePatch
    {
        private static void Postfix(Message __instance) => OnLocalWait(__instance.commandIndex, WaitMessage);
    }

    [HarmonyPatch(typeof(BokuMono.FieldEvent.UI), nameof(BokuMono.FieldEvent.UI.Execute))]
    private static class UiPatch
    {
        private static void Postfix(BokuMono.FieldEvent.UI __instance) => OnLocalWait(__instance.commandIndex, WaitUi);
    }

    [HarmonyPatch(typeof(Dialog), nameof(Dialog.Execute))]
    private static class DialogPatch
    {
        private static void Postfix(Dialog __instance) => OnLocalWait(__instance.commandIndex, WaitDialog);
    }

    /// <summary>Em quem assiste, comandos com efeito no mundo são pulados (marcados como terminados, como o Label faz).</summary>
    [HarmonyPatch]
    private static class SkipEffectsPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // FestivalEnd NÃO entra: a mudança de estado dele é o que encerra a cerimônia (sem ela a cena de quem assiste não acabava)
            foreach (var t in new[] { typeof(RewardItem), typeof(EventFlag), typeof(ChangeField), typeof(ExecludeHappyEnergy),
                                      typeof(KagetsuBuff), typeof(PlayableTutorialStart), typeof(BazaarResultSkip) })
            {
                // só o Execute da própria classe: o da base valeria para todos os comandos
                var m = AccessTools.DeclaredMethod(t, "Execute");
                if (m != null) yield return m;
            }
        }

        private static bool Prefix(EventExecCommandBase __instance)
        {
            if (!Watching) return true;
            __instance.IsForceCommandNext = true;
            __instance.IsCommandEnd = true;
            return false;
        }
    }

    /// <summary>Em quem assiste: fades da cena feitos na hora (ver o comentário dentro).</summary>
    [HarmonyPatch]
    private static class FadePatch
    {
        // Em quem assiste, os fades da cena são feitos na hora e o comando é dado como terminado: na convidada o FadeIn do
        // meio da cerimônia (comando 285) nunca terminava a espera dele e a cena parava ali (teste com duas instâncias).
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(BokuMono.FieldEvent.FadeIn), "Execute");
            yield return AccessTools.DeclaredMethod(typeof(BokuMono.FieldEvent.FadeOut), "Execute");
        }

        private static bool Prefix(EventExecCommandBase __instance)
        {
            if (!Watching) return true;
            try
            {
                if (__instance.TryCast<BokuMono.FieldEvent.FadeIn>() != null) FadeManager.FadeIn(null, null, 0.3f, 0f);
                else FadeManager.FadeOut(FadeType.Black, null, null, 0.3f);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] fade: {e.Message}"); }
            __instance.IsForceCommandNext = true;
            __instance.IsCommandEnd = true;
            return false;
        }
    }

    /// <summary>
    /// Em quem assiste: sem Happy Energy, minigame nem prêmio (a mudança de estado do festival roda: é o mesmo estado que vem do
    /// host e é ela que encerra a cerimônia). O cálculo do prêmio do festival
    /// (ApplyFestivalExtraReward, já na abertura da cena) quebrava sem inscrição e impedia a cena de abrir (visto no teste).
    /// </summary>
    [HarmonyPatch]
    private static class SkipExternalPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(EventExternal), "SetHappyPoint");
            yield return AccessTools.Method(typeof(EventExternal), "ExecuteMiniGame");
            yield return AccessTools.Method(typeof(EventReward), "ApplyFestivalExtraReward");
            yield return AccessTools.Method(typeof(EventReward), "ReciveReward");
        }

        private static bool Prefix() => !Watching;
    }

    /// <summary>Em quem assiste: o fim do roteiro (recompensas e finais especiais) não roda.</summary>
    [HarmonyPatch(typeof(EventExternal), nameof(EventExternal.ScenarioEnd))]
    private static class SkipScenarioEndPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Watching) return true;
            __result = false;
            return false;
        }
    }
}
