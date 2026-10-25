// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Patches/CONTEXTO.md e DECISOES.md#d23 / #d48.
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer.Patches;

/// <summary>
/// Menu de pausa no multiplayer (8.4c, D48): "Salvar" e "Carregar" não fazem sentido com outros jogadores (o save
/// oficial é feito pelo host quando todos dormem, D23; carregar outro save quebraria a sessão). Em vez da mensagem
/// confusa "Não foi possível acessar os dados", mostramos um aviso claro e não abrimos a tela.
/// As opções são reconhecidas pelo ID do texto (1130 = "Salvar", 1140 = "Carregar"; conferido no jogo).
/// </summary>
[HarmonyPatch(typeof(UIPausePage), "OnDecide")]
public static class PauseMenuPatch
{
    private const uint SaveText = 1130, LoadText = 1140;

    private static bool Prefix(int index, uint textId)
    {
        if (Plugin.DevMode) Plugin.Logger.LogInfo($"[PauseMenu] opção {index} (texto {textId})");
        if (Net.NetSession.Current == Net.NetSession.Mode.Offline) return true;
        switch (textId)
        {
            case SaveText:
                GameNotify.Show("No multiplayer o jogo salva sozinho quando todos dormem.");
                return false;
            case LoadText:
                GameNotify.Show("Para carregar outro save, saia do multiplayer primeiro (F9 → Desconectar).");
                return false;
            default:
                return true;
        }
    }
}
