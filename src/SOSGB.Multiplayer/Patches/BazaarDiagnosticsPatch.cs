// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e DECISOES.md#d56.
using System.Reflection;
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// SÓ DIAGNÓSTICO, sob pedido (SOSGB_BAZAAR_DIAG=1 no modo de teste): registra as rotinas da barraca do Bazaar que o
/// jogo chama quando o jogador vende, repõe ou fecha (D56: descobrir o caminho real para o ajudante repetir no jogo
/// do dono). Não é instalado sempre porque interceptar esses métodos muda o comportamento do jogo quando eles dão
/// erro por dentro (o erro, em vez de seguir no jogo, é engolido e o método devolve o valor padrão).
/// </summary>
public static class BazaarDiagnosticsPatch
{
    public static void Apply(Harmony h)
    {
        var bm = typeof(BazaarManager);
        void P(MethodBase target, string prefix)
        {
            if (target == null) return;
            h.Patch(target, prefix: new HarmonyMethod(typeof(BazaarDiagnosticsPatch).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)));
        }
        P(AccessTools.Method(bm, nameof(BazaarManager.Sell)), nameof(Sell));
        P(AccessTools.Method(bm, nameof(BazaarManager.ActionAccessShopShelf)), nameof(Access));
        P(AccessTools.Method(bm, nameof(BazaarManager.SelectExhibit)), nameof(Select));
        P(AccessTools.Method(bm, nameof(BazaarManager.VisitorBuyEnd)), nameof(BuyEnd));
        P(AccessTools.Method(bm, nameof(BazaarManager.AddBazaarResultData)), nameof(Result));
        P(AccessTools.Method(bm, nameof(BazaarManager.CloseShopCallBack)), nameof(Close));
        P(AccessTools.Method(bm, nameof(BazaarManager.ShowResultPage)), nameof(ResultPage));
        P(AccessTools.Method(bm, nameof(BazaarManager.ExitAllVisitor)), nameof(ExitAll));
        P(AccessTools.Method(typeof(UIBazaarMainPage), nameof(UIBazaarMainPage.OpenMiniDeck)), nameof(MiniDeck));
        Plugin.Logger.LogInfo("[BazaarDiag] registro das rotinas da barraca ligado");
    }

    private static void Log(string what)
    {
        Plugin.Logger.LogInfo($"[BazaarDiag] {what}");
        DevBridge.PushEvent("bazaar:" + what);
    }

    private static void Sell(int idx, bool isBonus) => Log($"Sell({idx}, bonus={isBonus})");
    private static void Access(int idx) => Log($"ActionAccessShopShelf({idx})");
    private static void Select(int idx) => Log($"SelectExhibit({idx})");
    private static void BuyEnd() => Log("VisitorBuyEnd()");
    private static void Result(uint itemId, int saleNum) => Log($"AddBazaarResultData(item={itemId}, qtd={saleNum})");
    private static void Close() => Log("CloseShopCallBack()");
    private static void ResultPage() => Log("ShowResultPage()");
    private static void ExitAll() => Log("ExitAllVisitor()");
    private static void MiniDeck() => Log("OpenMiniDeck()");
}
