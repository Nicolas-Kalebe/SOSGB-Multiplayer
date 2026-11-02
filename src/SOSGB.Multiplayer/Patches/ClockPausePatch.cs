// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e DECISOES.md#d99.
using System;
using BokuMono;
using HarmonyLib;
using UnityEngine;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// Relógio preso (D99). O DateManager tem `updateEnabled` e `storedUpdateEnabled`: Pause() guarda o estado atual e
/// desliga; Resume() restaura o guardado. Dois Pause() seguidos (menu de pausa aberto por cima de um diálogo, cena que
/// começa com o relógio já parado...) guardam "desligado" por cima de "ligado" e NENHUM Resume() liga mais: o relógio
/// fica parado para sempre com a tela normal (medido com a ponte: pause, pause, resume, resume, resume → parado; visto
/// no host depois da fala do Arata, com o grupo seguindo em frente).
///
///  1. Pause/Resume aninháveis (só no multiplayer): um Pause() com o relógio já parado só conta a profundidade e não roda
///     o original (não sobrescreve o guardado); o Resume() correspondente só desconta. O par de fora continua restaurando
///     o estado de verdade. Nos pares simples nada muda. Play() zera a contagem.
///  2. Vigia: no multiplayer, se o relógio ficar parado (flag bruta) por mais de 8 s com a tela normal, sem menu, diálogo,
///     cena, sono, desmaio, carregamento nem pausa geral, religa com Play() e registra um aviso (para a causa aparecer no
///     log). Sozinho (offline) nada muda.
/// </summary>
[HarmonyPatch]
internal static class ClockPausePatch
{
    private static int _depth;
    public static int Depth => _depth;
    public static int Rescues;

    private static bool Multiplayer => Net.NetSession.Current != Net.NetSession.Mode.Offline;

    public static void Reset() => _depth = 0;

    [HarmonyPrefix, HarmonyPatch(typeof(DateManager), nameof(DateManager.Pause))]
    private static bool PausePrefix(DateManager __instance)
    {
        if (!Multiplayer) return true;
        if (__instance.IsPlay()) return true; // rodando: pausa de verdade (guarda "ligado")
        _depth++;                              // já parado: só conta; o original guardaria "desligado" por cima
        if (Plugin.DevMode) Plugin.Logger.LogInfo($"[ClockPause] Pause aninhado (profundidade {_depth})");
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(DateManager), nameof(DateManager.Resume))]
    private static bool ResumePrefix()
    {
        if (!Multiplayer || _depth == 0) return true;
        _depth--;
        if (Plugin.DevMode) Plugin.Logger.LogInfo($"[ClockPause] Resume aninhado (profundidade {_depth})");
        return false;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(DateManager), nameof(DateManager.Play))]
    private static void PlayPostfix() => _depth = 0;

    // ---------- vigia ----------

    private const float StuckSeconds = 8f;
    private static float _stuckSince = -1;
    private static UIManager _ui;
    private static float _nextUiLookup;

    /// <summary>Chamado a cada quadro pela ClockSync (multiplayer). Devolve verdadeiro se religou o relógio.</summary>
    public static bool Watchdog()
    {
        try
        {
            if (!Multiplayer || !DateManager.IsInstance || !GameController.IsInstance) { _stuckSince = -1; return false; }
            var dm = DateManager.Instance;
            if (dm.IsPlay()) { _stuckSince = -1; return false; }
            if (!LooksIdle()) { _stuckSince = -1; return false; }
            if (_stuckSince < 0) { _stuckSince = Time.unscaledTime; return false; }
            if (Time.unscaledTime - _stuckSince < StuckSeconds) return false;
            _stuckSince = -1;
            _depth = 0;
            dm.Play();
            Rescues++;
            Plugin.Logger.LogWarning($"[ClockPause] relógio preso por {StuckSeconds:0} s com a tela normal: religado (Play). Se repetir, gere o log (F9).");
            DevBridge.PushEvent("relogio:religado");
            return true;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[ClockPause] vigia: {e.Message}"); return false; }
    }

    /// <summary>Tela normal: nada que justifique o relógio parado.</summary>
    private static bool LooksIdle()
    {
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (Net.DaySync.InDayChange || Net.DaySync.LocalSleeping) return false;
        if (Net.NetSession.Current == Net.NetSession.Mode.Client && !Net.PlayerProfiles.WorldApplied) return false;
        var gc = GameController.Instance;
        if (gc.IsPlayingEvent || gc.IsSleeping) return false;
        if (gc.pauser != null && gc.pauser.IsPausingAny) return false;
        var p = gc.GetPlayer();
        if (p == null || p.IsFaint || p.IsSleepyFaint || p.IsSleep) return false;
        if (FieldManager.IsInstance && FieldManager.Instance.IsChangeFieldUnloading) return false;
        if (Net.ClockSync.GroupPaused) return false; // o mod está segurando de propósito (menu de pausa de alguém, festival)
        if (_ui == null && Time.unscaledTime >= _nextUiLookup)
        {
            _nextUiLookup = Time.unscaledTime + 2f;
            _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
        }
        if (_ui == null) return false;
        return !_ui.IsMenu && _ui.CurrentUIType == UIType.Main;
    }
}
