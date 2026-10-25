// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d26.
using System;
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;

namespace SOSGB.Multiplayer;

/// <summary>
/// Avisos do mod no SISTEMA DE AVISOS DO PRÓPRIO JOGO (as "informações laterais", como
/// "chegou correspondência" / "item pronto"), em vez de uma janela própria (D26).
///
/// O sistema só aceita textos traduzidos por ID. Truque: pedimos um aviso de correio com uma
/// contagem-marcador (UIAccessor.RequestShowSideInfoPost(MarkerCount)) e, quando o jogo monta o
/// aviso (UISideInfomationModerate.SetData), trocamos o texto pelo nosso. Fica com animação, som e
/// posição nativos. Fora do jogo (título/carregamento) cai no aviso simples do overlay (Notice).
/// </summary>
public static class GameNotify
{
    private const int MarkerCount = 9_731; // nenhuma pessoa recebe 9731 cartas; identifica os nossos avisos
    private static readonly Queue<string> Pending = new();

    public static void Show(string text)
    {
        Plugin.Logger.LogInfo($"[Aviso] {text}");
        DevBridge.PushEvent($"aviso:{text}");
        try
        {
            if (UIAccessor.IsInstance && SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game)
            {
                Pending.Enqueue(text);
                UIAccessor.Instance.RequestShowSideInfoPost(MarkerCount);
                return;
            }
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[GameNotify] aviso nativo falhou ({e.Message}); usando o overlay");
        }
        Notice.Show(text);
    }

    [HarmonyPatch(typeof(UISideInfomationModerate), nameof(UISideInfomationModerate.SetData))]
    private static class ModerateSetDataPatch
    {
        private static void Postfix(UISideInfomationModerate __instance, ISideInfoData sideInfoData)
        {
            try
            {
                var post = sideInfoData?.TryCast<SideInfoDataPost>();
                if (post == null || post.m_Data == null || post.m_Data.Count != MarkerCount) return;
                var text = Pending.Count > 0 ? Pending.Dequeue() : "";
                __instance.infomationText.text = text;
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[GameNotify] {e.Message}");
            }
        }
    }
}
