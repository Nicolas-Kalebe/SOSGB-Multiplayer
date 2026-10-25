// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md.
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Aviso curto na tela (mostrado pelo DebugOverlay por alguns segundos). Usado, por exemplo,
/// quando o save manual é recusado no multiplayer (D23). Quando houver UI própria (Fase 8),
/// isto vira uma mensagem do jogo.
/// </summary>
public static class Notice
{
    private static string _text;
    private static float _until;

    public static void Show(string text, float seconds = 6f)
    {
        _text = text;
        _until = Time.unscaledTime + seconds;
        Plugin.Logger.LogInfo($"[Aviso] {text}");
    }

    public static string Current => _text != null && Time.unscaledTime < _until ? _text : null;
}
