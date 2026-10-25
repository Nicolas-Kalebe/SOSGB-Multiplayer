// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e DECISOES.md#d53.
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// Pula as logos de abertura (Marvelous, CRIWARE...) e vai direto para a tela inicial (D53, pedido do usuário para
/// testar mais rápido; vale também para jogar). Mesma técnica do mod "Skip Splash Screen" (Nexus, Lalaz4 / GitHub
/// gmm39/SOSGrandBazaarMods): toda fase da animação de logo (CompanyLogoAnimation.EnterPhase) vira a de saída, com
/// as transições em 0 s, e o fundo da logo (CompanyLogo) fica transparente. Opção [Game] SkipIntroLogos.
/// </summary>
[HarmonyPatch(typeof(CompanyLogoAnimation), "EnterPhase")]
public static class SkipLogosPatch
{
    private static void Prefix(CompanyLogoAnimation __instance, ref CompanyLogoAnimation.LogoAnimationPhase phase)
    {
        if (!Plugin.SkipIntroLogos.Value) return;
        phase = CompanyLogoAnimation.LogoAnimationPhase.FadeOut;
        __instance.animManager.ChangeBackwardFade(0, 0, 0, DG.Tweening.Ease.Linear);
        __instance.animManager.ChangeForwardFade(0, 0, 0, DG.Tweening.Ease.Linear);
    }

    [HarmonyPatch(typeof(CompanyLogo), "Update")]
    private static class Background
    {
        private static void Postfix(CompanyLogo __instance)
        {
            if (!Plugin.SkipIntroLogos.Value) return;
            __instance.fadeTimer = 0f;
            MaskableGraphicExtension.SetAlpha(__instance.bg, 0f);
        }
    }
}
