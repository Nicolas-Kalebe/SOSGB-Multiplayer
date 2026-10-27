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

    private const byte KStart = 1, KStep = 2, KEnd = 3, KChoice = 4, KReach = 5, KWatchBegin = 6, KWatchEnd = 7, KSkip = 8;
    private const float LockstepTimeout = 8f; // quem está na cena espera no máximo isso por quem assiste, em cada fala
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
    private static int _lastWait = -1;
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

    /// <summary>
    /// Assistindo, com a cena já aberta: a parte "festival" do outro fica guardada (o fim da cerimônia daqui depende da mudança de
    /// estado que o FestivalEnd faz aqui) e a daqui não é enviada. Aplicada no fim (StopWatch).
    /// </summary>
    public static bool HoldFestivalSync => Watching && _deferred == null;

    /// <summary>Avatar de outro jogador durante uma cena: assistindo, só o protagonista; na própria cena, nenhum.</summary>
    public static bool ShowRemoteInEvent(byte id)
    {
        if (Watching) return id == _watchedId;
        return !InEvent;
    }

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

    /// <summary>Festivais com competição (classificação na cena): exposições, corrida, concurso de pets, culinária, sucos.</summary>
    private static bool NeedsRanking()
    {
        try
        {
            if (!FestivalExecutor.IsInstance) return false;
            var cat = FestivalExecutor.Instance.GetCurrentFestivalCategory().ToString();
            return cat.StartsWith("Competition") || cat.StartsWith("HorseRacing") || cat.StartsWith("PetContest")
                   || cat.StartsWith("Food") || cat == "Juice";
        }
        catch { return false; }
    }

    private static readonly Dictionary<long, int> HostChoice = new();   // (cena << 20 | comando) → opção escolhida por quem está na cena
    private static int _bcLastDialog = -1;
    private static int _bcLastWait = -1;
    private static readonly Dictionary<byte, int> Watchers = new(); // quem assiste à minha cena → última espera a que chegou
    private static bool _pendingNext, _allowNext;
    private static float _pendingSince;
    private static bool _skipTried, _closeTried, _sawCommand; // _sawCommand: algum comando já rodou (antes disso, "acabaram" = ainda carregando)
    private static float _allEndSince = -1;
    private static bool _closingWatch, _finishTried;
    private static bool _closeSeen;      // o CloseEvent do jogo já rodou nesta cena (fechamento natural ou pelo Pular)
    private static float _closeSeenAt;
    private static EventExecCommandBase _fadeCmd;
    private static float _fadeAt;
    private static bool _fadeIsIn;

    /// <summary>Fade de quem assiste parado mais de 3 s: completa (o comando é dado como terminado).</summary>
    private static void RescueStuckFade()
    {
        if (_fadeCmd == null) return;
        try
        {
            if (_fadeCmd.IsCommandEnd) { _fadeCmd = null; return; }
            if (Time.unscaledTime - _fadeAt < 3f) return;
            if (_fadeIsIn) FadeManager.FadeIn(null, null, 0.3f, 0f);
            else FadeManager.FadeOut(FadeType.Black, null, null, 0.3f);
            _fadeCmd.IsForceCommandNext = true;
            _fadeCmd.IsCommandEnd = true;
            Plugin.Logger.LogInfo($"[EventWatch] fade {(_fadeIsIn ? "de entrada" : "de saída")} parado aqui: completado");
        }
        catch { /* comando já liberado */ }
        _fadeCmd = null;
    }
    private static long ChoiceKey(uint ev, int idx) => ((long)ev << 20) | (uint)idx;

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
        _bcLastWait = -1;
        _bcLastDialog = -1;
        _bcSkipped = false;
        Watchers.Clear();
        _pendingNext = false;
        var field =FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0;
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
        if (Watching)
        {
            _myWait = commandIndex; _myWaitKind = kind; _waitSince = Time.unscaledTime; _advanceTries = 0;
            var evw = _curEvent;
            Send(KReach, w => { w.Put(evw); w.Put(commandIndex); }); // espera conjunta: quem está na cena só passa quando eu chego
            return;
        }
        if (!_broadcasting) return;
        var ev = _bcEvent;
        _bcLastWait = commandIndex;
        if (kind == WaitDialog) _bcLastDialog = commandIndex;
        Send(KStep, w => { w.Put(ev); w.Put(commandIndex); w.Put(kind); });
    }

    /// <summary>
    /// Quem está na cena passou a fala/tela (Enter: EventConductor.Next): quem assiste passa a dela NA HORA. Antes o aviso só ia
    /// quando a fala seguinte começava, e quem assiste ficava sempre um trecho atrás: tocava as animações depois e as falas
    /// fechavam de uma vez, cortando animações (visto pelo usuário).
    /// </summary>
    /// <summary>
    /// Espera conjunta: quem está na cena só passa a fala quando todos os que assistem já chegaram nela (até 8 s). Assim os
    /// dois tocam juntos as animações do trecho seguinte, e quem assiste não precisa fechar falas de uma vez para alcançar
    /// (isso cortava as animações, visto pelo usuário). O Enter fica guardado e é aplicado sozinho quando o outro chega.
    /// </summary>
    private static bool HoldNext()
    {
        if (_allowNext || _bcSkipped || !_broadcasting || Watchers.Count == 0 || _bcLastWait < 0) return false;
        var behind = Watchers.Any(kv => kv.Value < _bcLastWait);
        if (!behind) return false;
        if (!_pendingNext) { _pendingNext = true; _pendingSince = Time.unscaledTime; Plugin.Logger.LogInfo($"[EventWatch] esperando quem assiste chegar na fala {_bcLastWait}"); }
        return true;
    }

    private static void OnLocalNext()
    {
        if (!_broadcasting || _bcLastWait < 0) return;
        var ev = _bcEvent;
        var passed = _bcLastWait + 1;
        Send(KStep, w => { w.Put(ev); w.Put(passed); w.Put((byte)0); });
    }

    private static bool _bcSkipped;

    /// <summary>Quem está na cena confirmou "Pular": quem assiste pula junto (a espera conjunta não segura mais nada).</summary>
    private static void OnLocalSkip()
    {
        if (!_broadcasting || _bcSkipped) return;
        _bcSkipped = true;
        _pendingNext = false;
        var ev = _bcEvent;
        Send(KSkip, w => w.Put(ev));
        Plugin.Logger.LogInfo($"[EventWatch] cena {ev} pulada aqui: quem assiste pula junto");
    }

    /// <summary>Quem está na cena escolheu uma opção num diálogo: quem assiste escolhe a mesma (roteiros com bifurcação).</summary>
    private static void OnLocalChoice(int selectIndex)
    {
        if (!_broadcasting || _bcLastDialog < 0) return;
        var ev = _bcEvent;
        var idx = _bcLastDialog;
        Send(KChoice, w => { w.Put(ev); w.Put(idx); w.Put(selectIndex); });
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
                case KWatchBegin:
                    if (_broadcasting) { Watchers[who] = -1; Plugin.Logger.LogInfo($"[EventWatch] #{who} está assistindo à minha cena"); }
                    break;
                case KWatchEnd:
                    if (Watchers.Remove(who)) Plugin.Logger.LogInfo($"[EventWatch] #{who} parou de assistir à minha cena");
                    break;
                case KReach:
                {
                    var ev = rr.GetUInt();
                    var idx = rr.GetInt();
                    if (_broadcasting && ev == _bcEvent) Watchers[who] = Watchers.TryGetValue(who, out var cur) ? Math.Max(cur, idx) : idx;
                    break;
                }
                case KChoice:
                {
                    var ev = rr.GetUInt();
                    var idx = rr.GetInt();
                    var choice = rr.GetInt();
                    if (!Watching || who != _watchedId) return;
                    HostChoice[ChoiceKey(ev, idx)] = choice;
                    break;
                }
                case KSkip:
                    if (!Watching || who != _watchedId) return;
                    _hostSkipped = true;
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
        HostChoice.Clear();
        Pending.Clear();
        _hostEnded = false;
        _hostSkipped = false;
        var name = NetSession.Players.TryGetValue(who, out var p) ? p.Name : "outro jogador";
        Last = $"assistindo a cena {info.Event} de {name}";
        Plugin.Logger.LogInfo($"[EventWatch] {Last}");
        Send(KWatchBegin, null);
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
        _skipTried = false;
        _closeTried = false;
        _finishTried = false;
        _closeSeen = false;
        _skipCloseAt = -1;
        _fadeCmd = null;
        _sawCommand = false;
        _allEndSince = -1;
        var list = new Il2CppSystem.Collections.Generic.List<EventMasterData>();
        foreach (var md in MasterDataManager.Instance.EventMasterData)
            if (md != null && info.Masters.Contains(md.Id)) list.Add(md);
        if (list.Count == 0) { StopWatch($"cadastro da cena {info.Event} não encontrado"); return false; }
        // mapa de destino 0 = "nenhum": usa o mapa atual (testes com uma instância mandavam 0)
        _openList = list;
        _openNext = info.NextField != 0 ? info.NextField : (FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0);
        _openSpawn = info.Spawn;
        // Como o TalkEventStart do jogo: escurece a tela ANTES de abrir a cena. O primeiro comando da cena é um FadeIn de cena
        // (FadeManager.EventFadeIn), que espera a tela escura; sem esse escurecer prévio ele nunca terminava, e daí em diante
        // nenhum fade completava (o FadeOut só age com o fade anterior completo), inclusive o do fim de cena, que é o que devolve
        // o personagem, os moradores e o HUD (visto pelo usuário: P2 presa e praça vazia no fim).
        try
        {
            if (FadeManager.IsFadeOut) OpenPrepared();
            else
            {
                _awaitingFade = true;
                _awaitFadeSince = Time.unscaledTime;
                FadeManager.FadeOut(FadeType.Black, null, (Il2CppSystem.Action)new Action(OpenPrepared), 0f);
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] escurecer antes da cena: {e.Message}"); OpenPrepared(); }
        return true;
    }

    private static Il2CppSystem.Collections.Generic.List<EventMasterData> _openList;
    private static uint _openNext;
    private static string _openSpawn = "";
    private static bool _awaitingFade;
    private static float _awaitFadeSince;

    /// <summary>Abre a cena preparada (depois do escurecer, ou no socorro de 3 s se o escurecer não avisar).</summary>
    private static void OpenPrepared()
    {
        _awaitingFade = false;
        var list = _openList;
        _openList = null;
        if (list == null || !Watching) return;
        _startedAt = Time.unscaledTime;
        _starting = true;
        try
        {
            var gc = GameController.Instance;
            switch (_openMode)
            {
                case "talk": gc.TalkEventStart(list, _openNext, _openSpawn, "", false, false, null, null, null, null); break;
                case "talkfade": gc.TalkEventStart(list, _openNext, _openSpawn, "", true, false, null, null, null, null); break;
                default: gc.StartEvent(list, _openNext, _openSpawn, ""); break;
            }
            Plugin.Logger.LogInfo($"[EventWatch] abrindo a cena {_curEvent} ({_openMode}, mapa {_openNext}, ponto '{_openSpawn}')");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] abrir a cena {_curEvent}: {e.Message}"); Recover("erro ao abrir"); }
        finally { _starting = false; }
    }

    private static void StopWatch(string why)
    {
        if (Watching) Send(KWatchEnd, null);
        Watching = false;
        _testAuto = false;
        _deferred = null;
        _awaitingFade = false;
        _openList = null;
        _watchedId = NetSession.UnknownId;
        _curEvent = 0;
        _myWait = -1;
        Pending.Clear();
        foreach (var rd in Hidden) if (rd != null) rd.enabled = true;
        Hidden.Clear();
        try { SharedSync.ReleaseHeldFestival(); } catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] festival guardado: {e.Message}"); }
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

            if (_pendingNext)
            {
                var ready = !_broadcasting || Watchers.Count == 0 || Watchers.All(kv => kv.Value >= _bcLastWait);
                if (ready || Time.unscaledTime - _pendingSince > LockstepTimeout)
                {
                    _pendingNext = false;
                    if (!ready) Plugin.Logger.LogInfo($"[EventWatch] quem assiste não chegou na fala {_bcLastWait} em {LockstepTimeout} s: seguindo");
                    _allowNext = true;
                    try { EventManager.Instance.conductor?.Next(); } catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] passar a fala: {e.Message}"); }
                    finally { _allowNext = false; }
                }
            }

            if (!Watching) return;
            if (_deferred != null)
            {
                if (!NeedsRanking() || RankingReady() || _testAuto) { var d = _deferred; _deferred = null; StartLocal(d); }
                else if (Time.unscaledTime - _deferredSince > 8f) { _deferred = null; StopWatch("a classificação do festival não chegou: cena não aberta"); }
                return;
            }
            if (_awaitingFade)
            {
                if (Time.unscaledTime - _awaitFadeSince > 3f) { Plugin.Logger.LogInfo("[EventWatch] o escurecer antes da cena não avisou: abrindo assim mesmo"); OpenPrepared(); }
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

            // a cena já está fechando pelo caminho do jogo (EventEnd: mapa recarregado, HUD, moradores): só espera; se travar, socorro
            if (_closeSeen)
            {
                var since = Time.unscaledTime - _closeSeenAt;
                if (since > 20f && !_finishTried)
                {
                    _finishTried = true;
                    try { EventManager.Instance.OnFinishEventEnd(true); Plugin.Logger.LogInfo($"[EventWatch] cena {_curEvent}: o fechamento do jogo parou; fim de cena chamado"); }
                    catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] fim de cena do jogo: {e.Message}"); }
                }
                if (since > 35f) Recover("o fechamento da cena não terminou");
                return;
            }

            RetrySkipClose();

            // quem está na cena pulou: pula junto, pelo mesmo caminho do "Pular" do jogo
            if (_hostSkipped && !_skipTried)
            {
                SkipLikeGame($"cena {_curEvent}: o outro pulou; pulando junto");
                return;
            }

            // fala/tela/escolha esperando: passa quando o outro já passou dela (ou já terminou a cena)
            if (_myWait >= 0)
            {
                var done = _hostEnded || HostDone.Contains(_curEvent) || Pending.Count > 0;
                var passed = HostStep.TryGetValue(_curEvent, out var hs) && hs > _myWait;
                if (_myWaitKind == WaitDialog && HostChoice.ContainsKey(ChoiceKey(_curEvent, _myWait))) passed = true;
                if (_testAuto) passed = Time.unscaledTime - _waitSince > 1.5f;
                if (done || passed)
                {
                    if (_myWaitKind == WaitMessage) AdvanceMessage();
                    else Advance();
                }
            }

            RescueStuckFade();

            // Todos os comandos acabaram e o jogo não fechou a cena em 20 s: o fechamento normal do jogo (é o que a corrotina de
            // entrada, CheckInputCoroutine, chama no fim). Daí em diante quem cuida é o bloco do _closeSeen.
            var cdc = EventManager.Instance.conductor;
            if (cdc != null && _sawCommand && cdc.IsAllCommandEnd)
            {
                if (_allEndSince < 0) _allEndSince = Time.unscaledTime;
                else if (!_closeTried && Time.unscaledTime - _allEndSince > 20f)
                {
                    _closeTried = true;
                    _closingWatch = true;
                    try { EventManager.Instance.CloseEvent(); Plugin.Logger.LogInfo($"[EventWatch] cena {_curEvent}: comandos acabaram; fechando como o jogo fecha"); }
                    catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] fechar: {e}"); }
                    finally { _closingWatch = false; }
                }
            }
            else _allEndSince = -1;

            // O outro terminou e a cena daqui não fechou: "Pular" do próprio jogo (leva ao fim e fecha direito, devolvendo o
            // personagem); só se nem isso fechar, a recuperação.
            if (_hostEnded && !_skipTried && !_closeTried && Time.unscaledTime - _hostEndedAt > 6f)
                SkipLikeGame($"cena {_curEvent}: pulando até o fim (o outro já terminou)");
            if (_hostEnded && Time.unscaledTime - _hostEndedAt > 30f) Recover("cena encerrada (o outro já tinha terminado)");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] {e.Message}"); }
    }

    private static bool _hostSkipped;

    /// <summary>
    /// Pula até o fim e fecha a cena como o "Pular" do jogo faz quando o roteiro não tem rótulo de pulo (o fechamento b__0 do
    /// EventManager.OpenSkipDialog: pulo forçado, ExecuteSkip, para as corrotinas da cena e ForceEventClose, que escurece e
    /// chama o CloseEvent normal). O próprio b__0 não serve aqui: ele só age com o fade completo, e em quem assiste quase
    /// sempre há um fade da cena em andamento (testado: o host pulou e a convidada seguiu a cena até o socorro de 30 s).
    /// </summary>
    private static void SkipLikeGame(string why)
    {
        _skipTried = true;
        _myWait = -1;
        if (!EventManager.IsInstance) return;
        var em = EventManager.Instance;
        var cd = em.conductor;
        try { if (cd != null) { cd.SetIsForceSkip(true); cd.ExecuteSkip(); } }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] pular os comandos: {e.Message}"); }
        try { em.StopAllCoroutines(); } catch { /* segue para o fechamento */ }
        try { EventCoroutineManager.instance?.AllStopRunningCoroutine(); } catch { /* segue para o fechamento */ }
        // O ForceEventClose não faz nada (devolve false) com um fade em andamento: o Update tenta de novo e, em 2 s, fecha direto.
        var closing = false;
        try { closing = em.ForceEventClose(null); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] fechar depois de pular: {e.Message}"); }
        _skipCloseAt = closing ? -1 : Time.unscaledTime;
        Plugin.Logger.LogInfo($"[EventWatch] {why}{(closing ? "" : " (fechamento espera o fade)")}");
    }

    private static float _skipCloseAt = -1;

    /// <summary>Depois de pular: o fechamento que o ForceEventClose recusou (fade em andamento).</summary>
    private static void RetrySkipClose()
    {
        if (_skipCloseAt < 0 || _closeSeen) { _skipCloseAt = -1; return; }
        var em = EventManager.Instance;
        bool ok;
        try { ok = em.ForceEventClose(null); } catch { ok = false; }
        if (ok) { _skipCloseAt = -1; return; }
        if (Time.unscaledTime - _skipCloseAt < 2f) return;
        _skipCloseAt = -1;
        try { FadeManager.FadeOut(FadeType.Black, null, null, 0f); } catch { /* fecha mesmo assim */ }
        _closingWatch = true;
        try { em.CloseEvent(); Plugin.Logger.LogInfo($"[EventWatch] cena {_curEvent}: fechada direto depois de pular"); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] fechar depois de pular: {e.Message}"); }
        finally { _closingWatch = false; }
    }

    private static int _advanceTries;
    private static float _nextAdvanceTry;

    /// <summary>
    /// Passa a fala de quem assiste pelo mesmo caminho do Enter (EventConductor.Next). Fechar a janela por fora
    /// (UIManager.CloseMessage) fazia o jogo tratar a fala como pulada e cortava as animações (visto pelo usuário). Se a fala
    /// não passar em 4 tentativas, fecha por fora.
    /// </summary>
    private static void AdvanceMessage()
    {
        if (Time.unscaledTime < _nextAdvanceTry) return;
        var cd = EventManager.IsInstance ? EventManager.Instance.conductor : null;
        if (cd == null) { Advance(); return; }
        if (!cd.IsMessageCommand) { _myWait = -1; _advanceTries = 0; return; } // a fala já passou
        if (_advanceTries >= 4) { _advanceTries = 0; Advance(); return; }
        _advanceTries++;
        _nextAdvanceTry = Time.unscaledTime + 0.4f;
        try { cd.Next(); } catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] passar a fala aqui: {e.Message}"); Advance(); }
    }

    private static void Advance()
    {
        if (_ui == null) _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        var kind = _myWaitKind;
        _lastWait = _myWait;
        _myWait = -1;
        if (_ui == null) return;
        try
        {
            if (kind == WaitMessage) _ui.CloseMessage(null, null);
            else if (kind == WaitDialog)
            {
                if (HostChoice.TryGetValue(ChoiceKey(_curEvent, _lastWait), out var choice)) EventManager.Instance.conductor?.SendDialogParameter(choice);
                _ui.CloseDialog(null, null, true, false, false);
            }
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
        if (a.Length >= 1 && a[0] == "skip") // como o jogador confirmando "Pular" no diálogo do jogo
        {
            var em = EventManager.Instance;
            var canSkip = em.IsEnableSkip;
            if (em.conductor != null) em.conductor.IsOpenDialog = true; // o fechamento só age com o diálogo aberto
            new EventManager.__c__DisplayClass38_0 { __4__this = em }._OpenSkipDialog_b__0(true);
            return $"pulando (a cena permite pular: {canSkip})";
        }
        string cond;
        try
        {
            var cd = EventManager.Instance.conductor;
            var pd = cd?.PrevData;
            var cur = pd?.GetCurrentMasterData();
            cond = $"condutor={(cd != null)} dados={(pd != null)} cenaAtual={cur?.Id}";
            if (cd != null)
            {
                var om = cd.ObjectModel;
                cond += $" comandosAcabaram={cd.IsAllCommandEnd} próximo={cd.IsCommandNext} janelaFala={cd.IsPlayingMessageWindowAnim} diálogo={cd.IsOpenDialog} pausada={cd.IsPauseEvent}";
                if (om != null) cond += $" mexendo={om.CheckMoveAnyObject()} animando={om.CheckPlayAnyMotion()} pescoço={om.CheckMoveAnyNeck()} efeito={om.CheckMoveAnyEffect()} olhos={om.CheckMoveAnyEye()}";
            }
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

    /// <summary>Marca que a cena de quem assiste já está rodando; com a ponte de teste, registra cada comando (diagnóstico).</summary>
    [HarmonyPatch(typeof(EventExecCommandBase), nameof(EventExecCommandBase.Execute))]
    private static class TraceCommandsPatch
    {
        private static void Postfix(EventExecCommandBase __instance)
        {
            if (Watching) _sawCommand = true;
            if (!Plugin.DevMode || (!Watching && !_broadcasting) || __instance == null) return;
            try { Plugin.Logger.LogInfo($"[EventWatch] {(Watching ? "assiste" : "cena")} cmd {__instance.commandIndex} {__instance.Type} {__instance.GetIl2CppType().Name}"); }
            catch { /* diagnóstico */ }
        }
    }

    /// <summary>
    /// No fim da cena de quem assiste, o fechamento normal (CloseEvent → EventConductor.Reset → ReleaseAllEventObjects) quebrava
    /// num objeto com id −1 (dicionário do jogo; o objeto exato não foi identificado) e o personagem ficava preso. Em quem assiste,
    /// a falha é registrada e o fechamento continua.
    /// </summary>
    [HarmonyPatch(typeof(EventObjectModel), nameof(EventObjectModel.ReleaseAllEventObjects))]
    private static class ReleasePatch
    {
        // Diagnóstico (ponte de teste): os objetos da cena no momento da liberação, nos dois jogos, para achar o id −1.
        private static void Prefix(EventObjectModel __instance)
        {
            if (!Plugin.DevMode || !(Watching || _closingWatch || _broadcasting) || __instance == null) return;
            try
            {
                var parts = new List<string>();
                var dic = __instance.transformInfoDic;
                if (dic != null)
                    foreach (var kv in dic)
                    {
                        var ti = kv.Value;
                        var oi = ti?.TryCast<ObjectInfo>();
                        parts.Add($"{kv.Key}:{ti?.ObjectName}/{ti?.Type}{(oi != null ? $"/cat{oi.ModelCategory}/t{oi.TableId}/{(oi.Instance != null ? oi.Instance.name : "sem-instância")}" : "")}");
                    }
                var comp = new List<string>();
                if (__instance.festivalCompetitionDic != null) foreach (var kv in __instance.festivalCompetitionDic) comp.Add($"{kv.Key}:{kv.Value?.EventObjectName}");
                Plugin.Logger.LogInfo($"[EventWatch] liberando objetos ({(Watching || _closingWatch ? "assiste" : "cena")}): {string.Join(" | ", parts)} || competidores: {string.Join(", ", comp)}");
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] diagnóstico da liberação: {e.Message}"); }
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null || !(Watching || _closingWatch)) return __exception;
            Plugin.Logger.LogWarning($"[EventWatch] liberar objetos da cena falhou aqui (seguindo com o fechamento): {__exception.Message.Split((char)10)[0]}");
            return null;
        }
    }

    private static float _traceUntil;

    /// <summary>Diagnóstico (ponte de teste): cada passo do fim de cena, nos dois jogos, até 90 s depois da cena.</summary>
    [HarmonyPatch]
    private static class EndTracePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var (t, n) in new (Type, string)[]
                     {
                         (typeof(EventManager), "EventEnd"), (typeof(EventManager), "OnFinishEventEnd"), (typeof(EventManager), "OnCompleteEventEnd"),
                         (typeof(EventManager), "CloseEvent"), (typeof(GameController), "TalkEventEnd"), (typeof(GameController), "EventFadeIn"),
                         (typeof(GameController), "RewardDialogEndAction"), (typeof(EventEndChangeField), "EndChangeField"),
                         (typeof(PlayerCharacter), "OnEndEvent"), (typeof(FestivalExecutor), "OnFadeInImmidiateExecute"),
                     })
            {
                var m = AccessTools.GetDeclaredMethods(t).FirstOrDefault(x => x.Name == n);
                if (m != null) yield return m;
            }
        }

        private static void Prefix(MethodBase __originalMethod)
        {
            if (!Plugin.DevMode) return;
            if (Watching || _broadcasting) _traceUntil = Time.unscaledTime + 90f;
            if (Time.unscaledTime > _traceUntil) return;
            Plugin.Logger.LogInfo($"[EventWatch] fim: {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name} ({(Watching ? "assiste" : _broadcasting ? "cena" : "depois")})");
        }
    }

    [HarmonyPatch(typeof(EventConductor), nameof(EventConductor.Next))]
    private static class NextPatch
    {
        private static bool Prefix(EventConductor __instance)
        {
            try { if (__instance.IsMessageCommand && HoldNext()) return false; } catch { /* segue */ }
            return true;
        }

        private static void Postfix()
        {
            try { OnLocalNext(); } catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] passar: {e.Message}"); }
        }
    }

    /// <summary>"Pular" confirmado no diálogo do jogo (fechamento b__0 do OpenSkipDialog, isSkip = true).</summary>
    [HarmonyPatch(typeof(EventManager.__c__DisplayClass38_0), nameof(EventManager.__c__DisplayClass38_0._OpenSkipDialog_b__0))]
    private static class SkipDialogPatch
    {
        private static void Prefix(bool __0)
        {
            if (!__0 || Watching) return;
            try
            {
                var cd = EventManager.IsInstance ? EventManager.Instance.conductor : null;
                if (cd != null && cd.IsOpenDialog) OnLocalSkip(); // sem o diálogo aberto o jogo não pula (o fechamento sai sem fazer nada)
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] pular: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(EventConductor), nameof(EventConductor.SendDialogParameter))]
    private static class ChoicePatch
    {
        private static void Postfix(int selectIndex)
        {
            try { OnLocalChoice(selectIndex); } catch (Exception e) { Plugin.Logger.LogWarning($"[EventWatch] escolha: {e.Message}"); }
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
        // Em quem assiste, os fades da cena rodam normalmente (feitos na hora, eles sumiam da tela: visto pelo usuário); só o que
        // ficar parado mais de 3 s é completado pelo Update (na convidada o FadeIn do comando 285 nunca terminava a espera dele).
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(BokuMono.FieldEvent.FadeIn), "Execute");
            yield return AccessTools.DeclaredMethod(typeof(BokuMono.FieldEvent.FadeOut), "Execute");
        }

        private static void Postfix(EventExecCommandBase __instance)
        {
            if (!Watching || __instance == null) return;
            _fadeCmd = __instance;
            _fadeAt = Time.unscaledTime;
            _fadeIsIn = __instance.TryCast<BokuMono.FieldEvent.FadeIn>() != null;
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

    /// <summary>
    /// Em quem assiste: o fim do roteiro (recompensas e finais especiais) não roda, mas responde "seguir". No CloseEvent do jogo,
    /// ScenarioEnd = false faz o fechamento PARAR ali, antes do EventEnd (que religa o HUD, a agenda dos moradores e recarrega o
    /// mapa): era o que deixava a P2 presa no fim da cerimônia, sem HUD (rastro do fim nos dois jogos, 2026-10-27).
    /// </summary>
    [HarmonyPatch(typeof(EventExternal), nameof(EventExternal.ScenarioEnd))]
    private static class SkipScenarioEndPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Watching) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Quem assiste: a cena começou a fechar pelo caminho do jogo; os socorros não mexem mais nela.</summary>
    [HarmonyPatch(typeof(EventManager), "CloseEvent")]
    private static class CloseSeenPatch
    {
        private static void Prefix()
        {
            if (!Watching || _closeSeen) return;
            _closeSeen = true;
            _closeSeenAt = Time.unscaledTime;
        }
    }
}
