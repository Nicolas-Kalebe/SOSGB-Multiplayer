// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e ROTEIRO.md (3.4/3.5).
using BokuMono;
using HarmonyLib;
using UnityEngine;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// DIAGNÓSTICO (só com a DevBridge ligada). Tarefas 3.4/3.5: descobrir a ordem das chamadas
/// ao dormir e virar o dia (o dump não tem corpo de método). Só registra; não muda nada.
/// </summary>
[HarmonyPatch]
internal static class SleepDiagnosticsPatch
{
    private static void Report(string what)
    {
        if (!Plugin.DevMode) return;
        Plugin.Logger.LogInfo($"[SleepDiag] {what}\n{StackTraceUtility.ExtractStackTrace()}");
        DevBridge.PushEvent($"sleep:{what}");
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.ActionSleep))]
    private static void ActionSleep() => Report("PlayerCharacter.ActionSleep");

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.StartNextDay))]
    private static void PcStartNextDay() => Report("PlayerCharacter.StartNextDay");

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), nameof(GameController.StartNextDay))]
    private static void GcStartNextDay() => Report("GameController.StartNextDay");

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), "EndToday")]
    private static void EndToday() => Report("GameController.EndToday");

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), "PreChangeDate")]
    private static void PreChangeDate() => Report("GameController.PreChangeDate");

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), "PostChangeDate")]
    private static void PostChangeDate() => Report("GameController.PostChangeDate");

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), "FadeStartNextDay")]
    private static void FadeStartNextDay() => Report("GameController.FadeStartNextDay");

    [HarmonyPrefix, HarmonyPatch(typeof(GameController), "set_IsSleeping")]
    private static void SetIsSleeping(bool value) => Report($"GameController.IsSleeping={value}");
}
