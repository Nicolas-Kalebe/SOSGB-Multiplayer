// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e ROTEIRO.md (Fase 5).
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// SÓ TESTES (modo de teste): "aperta" botões de ação do jogador por dentro do jogo. O clique do mouse (usar
/// ferramenta) não chega ao sistema de entrada do jogo quando é sintético (mouse_event/SendInput), então
/// injetamos a intenção direto em PlayerCharacter.GetHumanActionInput — o jogo segue o caminho real, com
/// animação e regras, como se o jogador tivesse clicado.
/// </summary>
[HarmonyPatch(typeof(PlayerCharacter), "GetHumanActionInput")]
public static class InputInjector
{
    private static int _toolFrames;
    private static bool _toolRelease;

    /// <summary>Segura "usar ferramenta" por alguns quadros e depois solta (um uso normal).</summary>
    public static void PressTool(int frames = 3)
    {
        if (!Plugin.DevMode) return;
        _toolFrames = frames;
    }

    private static void Postfix(ref HumanActionInputFlags __result)
    {
        if (_toolFrames > 0)
        {
            __result |= HumanActionInputFlags.Tool;
            if (--_toolFrames == 0) _toolRelease = true;
        }
        else if (_toolRelease)
        {
            __result |= HumanActionInputFlags.ToolUp;
            _toolRelease = false;
        }
    }
}
