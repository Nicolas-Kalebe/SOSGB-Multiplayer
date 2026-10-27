// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d50 (substitui a regra N da D9).
using System;
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Relógio do grupo (3.2/3.3; regras atuais da D50, pedidas pelo usuário).
///
///  - O tempo do grupo SÓ PARA quando alguém está no MENU DE PAUSA do jogo (Esc) — e aí para para todos.
///  - Em qualquer outra situação (outros menus, diálogos, eventos, telas do Bazaar...) o tempo do grupo continua.
///    O jogo para o relógio de quem está nessas telas; esse jogador fica "para trás" e, ao sair, é adiantado até a
///    hora do grupo. No host (a fonte da hora), enquanto ele está parado, a hora do grupo é calculada (hora do host +
///    tempo que passou) e ele mesmo é adiantado ao sair.
///  - O relógio do grupo segue o MAIS ADIANTADO: se o jogo de alguém pula o tempo para frente (barraca do Bazaar
///    esgotou, evento que avança a hora...), o convidado avisa o host (ClockJump) e todos pulam junto.
///  - Dormindo à espera dos outros, o relógio de quem dormiu continua (D33).
///  - D83: em DIA DE FESTIVAL, diálogo, mensagem, cena e as telas da inscrição (Fair, Animal, FairEvaluate) param o
///    tempo do grupo, como no jogo sozinho (pedido do usuário: "nos festivais pode deixar o tempo pausar nos diálogos
///    e eventos"). Os outros não ficam parados (só o relógio), com um aviso de quem está segurando o tempo. Enquanto
///    alguém está numa cena do festival (a cerimônia), o anfitrião do festival não aceita conversa dos outros (P13:
///    um só se inscreve). A cena da cerimônia leva o relógio dela a 16:00; o grupo vai junto pelo ClockJump.
///  - D83: host sozinho (ninguém conectado) não adianta o próprio relógio ao sair de uma tela; e ao sair o relógio vai
///    para a MAIOR hora entre a local e a do grupo (antes somava: uma cena que já tinha avançado a hora contava duas vezes).
/// </summary>
public class ClockSync : MonoBehaviour
{
    public ClockSync(IntPtr ptr) : base(ptr) { }

    private const float SendInterval = 0.5f;
    private const long MaxDriftTicks = 30L * 10_000_000;          // 30 s de jogo (ticks de 100 ns)
    private const long MaxJumpTicks = 12L * 3600 * 10_000_000;    // pulos maiores que 12 h de jogo são ignorados
    // Abaixo disso não é "pulo de tempo" (evento, barraca esgotada), é travada: o relógio anda ~1 min de jogo por segundo
    // real, então meio segundo travado no host já deixava um convidado 30 s à frente e o grupo inteiro pulava com aviso (D70).
    private const long MinJumpTicks = 5L * 60 * 10_000_000;       // 5 min de jogo
    private const int PauseMenuKey = 119;                        // UILoadKey.Pause

    /// <summary>O relógio LOCAL está parado pelo jogo (menu, diálogo, evento). Valor original do jogo.</summary>
    public static bool LocalPaused { get; private set; }

    /// <summary>Este jogador está no menu de pausa do jogo (única coisa que pausa o grupo).</summary>
    public static bool InPauseMenu { get; private set; }

    /// <summary>O grupo está pausado (alguém no menu de pausa), decidido pelo host.</summary>
    public static bool GroupPaused { get; private set; }

    /// <summary>Quem abriu o menu de pausa (o aviso diz o nome), D74. UnknownId = ninguém.</summary>
    public static byte PausedBy { get; private set; } = NetSession.UnknownId;

    private static bool _frozenByPause; // o personagem local foi parado por causa da pausa de OUTRO jogador (D74)

    private static readonly Dictionary<byte, bool> GuestInPause = new();
    private static long _hostBehind;     // host: quanto tempo do grupo passou desde que o host parou (ticks)
    private static long _stoppedAt;      // host: hora local quando ele parou numa tela (D83)

    /// <summary>D83: quem segura o tempo do grupo num diálogo/cena em dia de festival. UnknownId = ninguém.</summary>
    public static byte HeldBy { get; private set; } = NetSession.UnknownId;
    /// <summary>D83: o que segura o tempo: 0 nada, 1 diálogo/tela, 2 cena (evento).</summary>
    public static byte HeldKind { get; private set; }
    /// <summary>D83: o próprio jogador segura o tempo (0, 1 ou 2, como HeldKind).</summary>
    public static byte LocalHold { get; private set; }
    private static readonly Dictionary<byte, byte> GuestHold = new();
    private byte _lastReportedHold;
    private const int FairEvaluateKey = 127, FairKey = 128, AnimalSelectKey = 120; // telas da inscrição no festival
    private static float _jumpRequestedAt = -10;
    private static bool _subscribed;
    private float _nextSend;
    private bool _lastReported;
    private float _nextReport;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id => { GuestInPause.Remove(id); GuestHold.Remove(id); };
        WorldSync.OnWorldReceived += () => _hostBehind = 0;
    }

    // UIManager não é singleton com Instance: achamos o objeto e guardamos.
    private static UIManager _ui;
    private static float _nextUiLookup;

    private static bool PauseMenuOpen()
    {
        if (_ui == null && Time.unscaledTime >= _nextUiLookup)
        {
            _nextUiLookup = Time.unscaledTime + 2f;
            _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        }
        return _ui != null && (int)_ui.UIMenuKey == PauseMenuKey && _ui.IsMenu;
    }

    private static long TicksPerRealSecond => (long)(DateManager.Instance.TimeScale * 10_000_000f);

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline)
            {
                GroupPaused = false; PausedBy = NetSession.UnknownId; GuestInPause.Clear(); _hostBehind = 0;
                HeldBy = NetSession.UnknownId; HeldKind = 0; LocalHold = 0; GuestHold.Clear();
                UpdateFreeze();
                return;
            }
            if (!DateManager.IsInstance) return;
            InPauseMenu = PauseMenuOpen();
            _ = DateManager.Instance.IsUpdateEnabled; // atualiza LocalPaused pelo patch
            LocalHold = FestivalHold();

            if (NetSession.Current == NetSession.Mode.Host) HostTick();
            else ClientTick();
            UpdateFreeze();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[ClockSync] {e.Message}"); }
    }

    /// <summary>Hora do grupo vista pelo host (a maior entre a local e a de quando parou + o que passou desde então).</summary>
    public static long GroupTicks => _hostBehind > 0 ? Math.Max(DateManager.Instance.Now.Ticks, _stoppedAt + _hostBehind) : DateManager.Instance.Now.Ticks;

    /// <summary>
    /// D83: em dia de festival, o relógio deste jogo está parado por diálogo, mensagem, cena ou tela da inscrição?
    /// 0 = não, 1 = diálogo/tela, 2 = cena. O menu de pausa tem a regra própria (D74).
    /// </summary>
    private static byte FestivalHold()
    {
        if (!LocalPaused || DaySync.LocalSleeping || InPauseMenu) return 0;
        if (!FestivalExecutor.IsInstance || !FestivalExecutor.Instance.IsInSession) return 0;
        if (GameController.IsInstance && GameController.Instance.IsPlayingEvent) return 2;
        if (_ui == null) return 0;
        var type = _ui.CurrentUIType;
        if (type == UIType.Dialog || type == UIType.Message) return 1;
        var key = (int)_ui.CurrentUIKey;
        return (byte)(key == FairKey || key == FairEvaluateKey || key == AnimalSelectKey ? 1 : 0);
    }

    private static uint _festHostDay = uint.MaxValue, _festHost;

    /// <summary>
    /// D83/P13: enquanto OUTRO jogador está numa cena do festival (a cerimônia), o anfitrião do festival não aceita
    /// conversa aqui: não dá para se inscrever de novo no mesmo festival. Depois da cerimônia o festival fica "JoinAfter"
    /// (compartilhado pela SharedSync) e o próprio jogo não inscreve mais.
    /// </summary>
    public static bool FestivalHostBusy(uint charaId)
    {
        if (HeldKind != 2 || HeldBy == NetSession.UnknownId || HeldBy == NetSession.LocalId) return false;
        try
        {
            if (!FestivalExecutor.IsInstance || !DateManager.IsInstance) return false;
            var now = DateManager.Instance.Now;
            var day = (uint)(now.Month * 100 + now.Day);
            if (day != _festHostDay)
            {
                _festHostDay = day;
                _festHost = FestivalExecutor.Instance.IsAnyFestivalOpen(now, out var fm) && fm != null ? fm.HostCharaId : 0;
            }
            return _festHost != 0 && charaId == _festHost;
        }
        catch { return false; }
    }

    private void HostTick()
    {
        var by = InPauseMenu ? NetSession.LocalId : NetSession.UnknownId;
        foreach (var kv in GuestInPause) if (kv.Value && by == NetSession.UnknownId) by = kv.Key;
        PausedBy = by;
        // D83: em dia de festival, diálogo/cena de qualquer um segura o tempo do grupo (a cena vale mais que o diálogo).
        var held = LocalHold != 0 ? NetSession.LocalId : NetSession.UnknownId;
        var kind = LocalHold;
        foreach (var kv in GuestHold)
            if (kv.Value > kind) { held = kv.Key; kind = kv.Value; }
        HeldBy = held;
        HeldKind = kind;
        GroupPaused = by != NetSession.UnknownId || held != NetSession.UnknownId;

        var dm = DateManager.Instance;
        var hostStopped = LocalPaused && !DaySync.LocalSleeping;
        var alone = NetSession.Players.Count <= 1; // D83: sem ninguém conectado, o tempo para nas telas como no jogo sozinho
        if (!GroupPaused && hostStopped && !DaySync.InDayChange && !alone)
        {
            // O host está numa tela que para o relógio dele, mas o grupo segue: a hora do grupo anda por conta.
            if (_hostBehind == 0) _stoppedAt = dm.Now.Ticks;
            _hostBehind = Math.Min(_hostBehind + (long)(Time.unscaledDeltaTime * TicksPerRealSecond), MaxJumpTicks);
        }
        else if (!hostStopped && _hostBehind > 0)
        {
            // Saiu da tela: o host vai para a hora do grupo, se ela estiver à frente (a tela pode ter avançado a hora
            // sozinha, ex.: a cerimônia do festival termina às 16:00; antes isso era somado e contava duas vezes).
            var target = Math.Max(dm.Now.Ticks, _stoppedAt + _hostBehind);
            _hostBehind = 0;
            if (!DaySync.InDayChange && target > dm.Now.Ticks) SetLocal(target, "host saiu de uma tela; adiantado até a hora do grupo");
        }

        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        var ticks = GroupTicks;
        var running = !GroupPaused;
        var pausedBy = PausedBy;
        var heldBy = HeldBy;
        var heldKind = HeldKind;
        NetSession.SendGame(MsgType.ClockState, w => { w.Put(ticks); w.Put(running); w.Put(pausedBy); w.Put(heldBy); w.Put(heldKind); }, DeliveryMethod.Sequenced);
    }

    private void ClientTick()
    {
        // Avisa o host quando entra/sai do menu de pausa (e a cada 1 s, para o caso de um pacote se perder).
        if (InPauseMenu == _lastReported && LocalHold == _lastReportedHold && Time.unscaledTime < _nextReport) return;
        _lastReported = InPauseMenu;
        _lastReportedHold = LocalHold;
        _nextReport = Time.unscaledTime + 1f;
        var p = InPauseMenu;
        var h = LocalHold;
        NetSession.SendGame(MsgType.PauseReport, w => { w.Put(p); w.Put(h); }, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Pausa de outro jogador (D74, visto pelo usuário: "o esc não impede o outro jogador de se movimentar nem dá um aviso"):
    /// o personagem local fica parado (PauseObject, a mesma pausa que o espectador usa) até a pausa acabar. O aviso na tela
    /// é da MultiplayerUI. Só mexe no personagem se foi a gente que pausou.
    /// </summary>
    private static void UpdateFreeze()
    {
        var want = NetSession.Current != NetSession.Mode.Offline && GroupPaused && PausedBy != NetSession.UnknownId && !InPauseMenu
                   && !DaySync.LocalSleeping && !Spectator.Active && !Spectator.Pinned;
        if (want == _frozenByPause) return;
        var po = GameController.IsInstance ? GameController.Instance.GetPlayer()?.PauseObject : null;
        if (want)
        {
            if (po == null || po.IsPaused) return;
            po.Pause(PauseType.All);
            _frozenByPause = true;
            Plugin.Logger.LogInfo($"[ClockSync] pausa de #{PausedBy}: personagem parado");
        }
        else
        {
            if (po != null && po.IsPaused) po.Resume();
            _frozenByPause = false;
            Plugin.Logger.LogInfo("[ClockSync] fim da pausa: personagem livre");
        }
    }

    private static void SetLocal(long ticks, string why)
    {
        DateManager.Instance.SetDate(new BokuMonoDateTime(ticks), true);
        Plugin.Logger.LogInfo($"[ClockSync] {why} ({new BokuMonoDateTime(ticks)})");
        DevBridge.PushEvent("clock-ajustado");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.PauseReport when NetSession.Current == NetSession.Mode.Host:
                GuestInPause[from] = r.GetBool();
                GuestHold[from] = r.AvailableBytes > 0 ? r.GetByte() : (byte)0;
                break;

            case MsgType.ClockJump when NetSession.Current == NetSession.Mode.Host:
            {
                var ticks = r.GetLong();
                if (!DateManager.IsInstance || DaySync.InDayChange) return;
                var dm = DateManager.Instance;
                var ahead = ticks - GroupTicks;
                if (ahead <= MinJumpTicks || ahead > MaxJumpTicks) return;
                if (new BokuMonoDateTime(ticks).Day != dm.Now.Day) return; // não atravessa a virada do dia (é do DaySync)
                if (LocalPaused && !DaySync.LocalSleeping) { _stoppedAt = dm.Now.Ticks; _hostBehind = ticks - dm.Now.Ticks; } // host numa tela: adianta ao sair
                else SetLocal(ticks, $"tempo pulou no jogo de #{from}; o grupo pula junto");
                var name = NetSession.Players.TryGetValue(from, out var pl) ? pl.Name : "outro jogador";
                var t = new BokuMonoDateTime(ticks);
                GameNotify.Show($"O tempo avançou para {t.Hour:00}:{t.Minute:00} ({name}).");
                break;
            }

            case MsgType.ClockState when NetSession.Current == NetSession.Mode.Client:
            {
                var hostTicks = r.GetLong();
                GroupPaused = !r.GetBool();
                PausedBy = r.AvailableBytes > 0 ? r.GetByte() : NetSession.UnknownId;
                HeldBy = r.AvailableBytes > 0 ? r.GetByte() : NetSession.UnknownId;
                HeldKind = r.AvailableBytes > 0 ? r.GetByte() : (byte)0;
                if (!DateManager.IsInstance || DaySync.InDayChange) return;
                // Numa tela que para o relógio (menu, diálogo, evento): não mexe agora; ao sair, é adiantado.
                if (LocalPaused && !DaySync.LocalSleeping) return;
                var dm = DateManager.Instance;
                var drift = dm.Now.Ticks - hostTicks;
                if (drift > MinJumpTicks && drift < MaxJumpTicks && new BokuMonoDateTime(dm.Now.Ticks).Day == new BokuMonoDateTime(hostTicks).Day)
                {
                    // Este jogo está À FRENTE: o tempo pulou aqui (ex.: barraca esgotou). Pede ao host para o grupo pular.
                    if (Time.unscaledTime - _jumpRequestedAt > 5f)
                    {
                        _jumpRequestedAt = Time.unscaledTime;
                        var mine = dm.Now.Ticks;
                        NetSession.SendGame(MsgType.ClockJump, w => w.Put(mine), DeliveryMethod.ReliableOrdered);
                        Plugin.Logger.LogInfo($"[ClockSync] o tempo pulou aqui ({dm.Now}); pedindo ao host para o grupo pular");
                        return;
                    }
                    if (Time.unscaledTime - _jumpRequestedAt < 3f) return; // esperando o host
                }
                if (Math.Abs(drift) > MaxDriftTicks) SetLocal(hostTicks, $"hora corrigida para a do grupo (diferença de {drift / 10_000_000} s de jogo)");
                break;
            }
        }
    }

    public static object Status() => new
    {
        localPaused = LocalPaused,
        inPauseMenu = InPauseMenu,
        pausedBy = PausedBy,
        frozenByPause = _frozenByPause,
        groupPaused = GroupPaused,
        guestsInPause = GuestInPause,
        heldBy = HeldBy,
        heldKind = HeldKind,
        localHold = LocalHold,
        guestsHold = GuestHold,
        hostBehindSeconds = _hostBehind / 10_000_000,
    };

    [HarmonyPatch(typeof(DateManager), nameof(DateManager.IsUpdateEnabled), MethodType.Getter)]
    private static class IsUpdateEnabledPatch
    {
        private static void Postfix(ref bool __result)
        {
            LocalPaused = !__result;
            if (NetSession.Current == NetSession.Mode.Offline) return;
            if (GroupPaused) __result = false;
            // Dormindo à espera dos outros: o relógio do mundo continua para quem está acordado (D33).
            else if (DaySync.LocalSleeping) __result = true;
        }
    }
}
