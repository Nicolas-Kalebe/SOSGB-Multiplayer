// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e DECISOES.md#d56.
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// SÓ DIAGNÓSTICO (modo de teste): registra as rotinas da barraca do Bazaar que o jogo chama quando o jogador vende,
/// repõe ou toca o sino (D56: descobrir o caminho real para o ajudante repetir no jogo do dono).
/// </summary>
[HarmonyPatch]
public static class BazaarDiagnosticsPatch
{
    private static void Log(string what)
    {
        if (!Plugin.DevMode) return;
        Plugin.Logger.LogInfo($"[BazaarDiag] {what}");
        DevBridge.PushEvent("bazaar:" + what);
    }

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.Sell))]
    [HarmonyPrefix]
    private static void Sell(int idx, bool isBonus) => Log($"Sell({idx}, bonus={isBonus})");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.ActionAccessShopShelf))]
    [HarmonyPrefix]
    private static void Access(int idx) => Log($"ActionAccessShopShelf({idx})");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.SelectExhibit))]
    [HarmonyPrefix]
    private static void Select(int idx) => Log($"SelectExhibit({idx})");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.VisitorBuyEnd))]
    [HarmonyPrefix]
    private static void BuyEnd() => Log("VisitorBuyEnd()");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.AddBazaarResultData))]
    [HarmonyPrefix]
    private static void Result(uint itemId, int saleNum) => Log($"AddBazaarResultData(item={itemId}, qtd={saleNum})");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.CloseShopCallBack))]
    [HarmonyPrefix]
    private static void Close() => Log("CloseShopCallBack()");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.ShowResultPage))]
    [HarmonyPrefix]
    private static void ResultPage() => Log("ShowResultPage()");

    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.ExitAllVisitor))]
    [HarmonyPrefix]
    private static void ExitAll() => Log("ExitAllVisitor()");

    [HarmonyPatch(typeof(UIBazaarMainPage), nameof(UIBazaarMainPage.OpenMiniDeck))]
    [HarmonyPrefix]
    private static void MiniDeck() => Log("OpenMiniDeck()");
}
