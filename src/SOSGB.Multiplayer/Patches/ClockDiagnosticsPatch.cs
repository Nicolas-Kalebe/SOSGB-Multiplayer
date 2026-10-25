// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e ROTEIRO.md (tarefa 1.1).
using BokuMono;
using HarmonyLib;
using UnityEngine;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// DIAGNÓSTICO (só com a DevBridge ligada). Tarefa 1.1 do ROTEIRO: descobrir quem pausa o relógio
/// em menus e diálogos, base da pausa configurável (D9).
/// Jogo: DateManager.Pause()/Resume() e GameController.Pause/Resume/PauseTalk/ResumeTalk.
/// Só registra (postfix) e não muda o comportamento. Cada chamada vai para o log do BepInEx
/// com o stack trace da Unity e para o sosgb-state.jsonl como evento.
/// </summary>
[HarmonyPatch]
internal static class ClockDiagnosticsPatch
{
    private static void Report(string what)
    {
        if (!Plugin.DevMode) return;
        var stack = StackTraceUtility.ExtractStackTrace();
        Plugin.Logger.LogInfo($"[ClockDiag] {what}\n{stack}");
        DevBridge.PushEvent($"clock:{what}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(DateManager), nameof(DateManager.Pause))]
    private static void DatePause() => Report("DateManager.Pause");

    [HarmonyPostfix, HarmonyPatch(typeof(DateManager), nameof(DateManager.Resume))]
    private static void DateResume() => Report("DateManager.Resume");

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.Pause))]
    private static void GcPause(PauseType type) => Report($"GameController.Pause({type})");

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.Resume))]
    private static void GcResume() => Report("GameController.Resume");

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.PauseTalk))]
    private static void GcPauseTalk() => Report("GameController.PauseTalk");

    [HarmonyPostfix, HarmonyPatch(typeof(GameController), nameof(GameController.ResumeTalk))]
    private static void GcResumeTalk() => Report("GameController.ResumeTalk");
}
