// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e ROTEIRO.md (6.5).
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;
using UnityEngine;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// SÓ DIAGNÓSTICO (modo de teste, comando "marrydiag on"): registra QUEM consulta o casamento do jogador
/// (LikeabilityManager.IsPartner / PartnerCharaId / IsMarried), uma vez por pilha de chamadas. Serve para achar a
/// verificação que as rotinas dos NPCs usam (6.5, D12a), já que o dump não tem os corpos dos métodos.
/// </summary>
[HarmonyPatch]
public static class MarriageDiagnosticsPatch
{
    public static bool Enabled;
    private static readonly HashSet<string> Seen = new();

    private static void Report(string what)
    {
        if (!Enabled || !Plugin.DevMode) return;
        var st = StackTraceUtility.ExtractStackTrace();
        var key = what + "|" + st;
        if (Seen.Count > 200 || !Seen.Add(key)) return;
        Plugin.Logger.LogInfo($"[MarryDiag] {what}\n{st}");
    }

    [HarmonyPostfix, HarmonyPatch(typeof(LikeabilityManager), nameof(LikeabilityManager.IsPartner))]
    private static void IsPartner() => Report("IsPartner");

    [HarmonyPostfix, HarmonyPatch(typeof(LikeabilityManager), "get_PartnerCharaId")]
    private static void PartnerCharaId() => Report("PartnerCharaId");

    [HarmonyPostfix, HarmonyPatch(typeof(LikeabilityManager), "get_IsMarried")]
    private static void IsMarried() => Report("IsMarried");

    [HarmonyPostfix, HarmonyPatch(typeof(MarriageInfo), "get_IsMarried")]
    private static void MiIsMarried() => Report("MarriageInfo.IsMarried");

    [HarmonyPostfix, HarmonyPatch(typeof(BokuMono.Data.CharacterMarriableMasterData), "get_IsMarried")]
    private static void CmIsMarried() => Report("CharacterMarriable.IsMarried");

    [HarmonyPostfix, HarmonyPatch(typeof(BokuMono.Data.CharacterMarriableMasterData), "get_IsLover")]
    private static void CmIsLover() => Report("CharacterMarriable.IsLover");

    [HarmonyPostfix, HarmonyPatch(typeof(FieldStayableChecker), nameof(FieldStayableChecker.IsPartnersHouse))]
    private static void PartnersHouse() => Report("IsPartnersHouse");
}
