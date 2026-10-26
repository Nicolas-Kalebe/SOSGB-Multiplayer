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
    private static long _hostBehind;     // host: quanto a hora do grupo está à frente da hora local do host (ticks)
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
        NetSession.OnPlayerLeft += id => GuestInPause.Remove(id);
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
                UpdateFreeze();
                return;
            }
            if (!DateManager.IsInstance) return;
            InPauseMenu = PauseMenuOpen();
            _ = DateManager.Instance.IsUpdateEnabled; // atualiza LocalPaused pelo patch

            if (NetSession.Current == NetSession.Mode.Host) HostTick();
            else ClientTick();
            UpdateFreeze();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[ClockSync] {e.Message}"); }
    }

    /// <summary>Hora do grupo vista pelo host (hora local + o que passou enquanto ele estava parado).</summary>
    public static long GroupTicks => DateManager.Instance.Now.Ticks + _hostBehind;

    private void HostTick()
    {
        var by = InPauseMenu ? NetSession.LocalId : NetSession.UnknownId;
        foreach (var kv in GuestInPause) if (kv.Value && by == NetSession.UnknownId) by = kv.Key;
        PausedBy = by;
        GroupPaused = by != NetSession.UnknownId;

        var dm = DateManager.Instance;
        var hostStopped = LocalPaused && !DaySync.LocalSleeping;
        if (!GroupPaused && hostStopped && !DaySync.InDayChange)
        {
            // O host está numa tela que para o relógio dele, mas o grupo segue: a hora do grupo anda por conta.
            _hostBehind = Math.Min(_hostBehind + (long)(Time.unscaledDeltaTime * TicksPerRealSecond), MaxJumpTicks);
        }
        else if (!hostStopped && _hostBehind > 0)
        {
            // Saiu da tela: o host é adiantado até a hora do grupo.
            var target = dm.Now.Ticks + _hostBehind;
            _hostBehind = 0;
            if (!DaySync.InDayChange) SetLocal(target, "host saiu de uma tela; adiantado até a hora do grupo");
        }

        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        var ticks = GroupTicks;
        var running = !GroupPaused;
        var pausedBy = PausedBy;
        NetSession.SendGame(MsgType.ClockState, w => { w.Put(ticks); w.Put(running); w.Put(pausedBy); }, DeliveryMethod.Sequenced);
    }

    private void ClientTick()
    {
        // Avisa o host quando entra/sai do menu de pausa (e a cada 1 s, para o caso de um pacote se perder).
        if (InPauseMenu == _lastReported && Time.unscaledTime < _nextReport) return;
        _lastReported = InPauseMenu;
        _nextReport = Time.unscaledTime + 1f;
        var p = InPauseMenu;
        NetSession.SendGame(MsgType.PauseReport, w => w.Put(p), DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Pausa de outro jogador (D74, visto pelo usuário: "o esc não impede o outro jogador de se movimentar nem dá um aviso"):
    /// o personagem local fica parado (PauseObject, a mesma pausa que o espectador usa) até a pausa acabar. O aviso na tela
    /// é da MultiplayerUI. Só mexe no personagem se foi a gente que pausou.
    /// </summary>
    private static void UpdateFreeze()
    {
        var want = NetSession.Current != NetSession.Mode.Offline && GroupPaused && !InPauseMenu
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
                break;

            case MsgType.ClockJump when NetSession.Current == NetSession.Mode.Host:
            {
                var ticks = r.GetLong();
                if (!DateManager.IsInstance || DaySync.InDayChange) return;
                var dm = DateManager.Instance;
                var ahead = ticks - GroupTicks;
                if (ahead <= MinJumpTicks || ahead > MaxJumpTicks) return;
                if (new BokuMonoDateTime(ticks).Day != dm.Now.Day) return; // não atravessa a virada do dia (é do DaySync)
                if (LocalPaused && !DaySync.LocalSleeping) _hostBehind = ticks - dm.Now.Ticks; // host numa tela: adianta ao sair
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
