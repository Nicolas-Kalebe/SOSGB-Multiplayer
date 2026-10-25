// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e DECISOES.md#d21.
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// Jogo: BokuMono.AppManager.OnFocusChanged(bool focus).
/// O jogo se pausa sozinho (Time.timeScale = 0, música para) quando a janela perde o foco.
/// Num multiplayer isso é inaceitável: o host dando alt-tab congelaria todo mundo; e a IA
/// não conseguiria testar com o jogo em segundo plano (D20). Por isso ignoramos a perda de foco.
/// Decisão: D21. Configurável em [Game] KeepRunningWhenUnfocused.
/// </summary>
[HarmonyPatch(typeof(AppManager), "OnFocusChanged")]
internal static class AppManagerPatch
{
    private static bool Prefix(bool focus)
    {
        if (focus || !Plugin.KeepRunningWhenUnfocused.Value)
            return true; // executa o original

        Plugin.Logger.LogDebug("[AppManagerPatch] perda de foco ignorada (jogo continua rodando)");
        return false; // pula o original: não pausa
    }
}
