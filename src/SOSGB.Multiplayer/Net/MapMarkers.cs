// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d82.
using System;
using System.Collections.Generic;
using BokuMono;
using BokuMono.Data;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Mapa do jogo (M) com os outros jogadores (ROTEIRO 8.8, D82; pedido do usuário: "quero o mapa com localização do
/// outro player"). O jogo põe o marcador do jogador em UIMapArea.SetPlayerPosition:
///  - mapa da página = mapa em que o jogador está: lugar calculado da grade dele (PlayerCharacter.GridPosition,
///    com Grid_Left/Grid_Top e SppedX/SppedY do UIMapData) no marcador playerPosition;
///  - senão: no ponto do prédio (TryGetInDoorAnchorData) ou do mapa (página de todos os mapas), como os moradores.
/// Depois que o jogo põe o marcador local, para cada jogador remoto: se ele está no mapa da página, o próprio jogo
/// calcula o lugar com a grade dele (troca a grade do jogador local por um instante, chama SetPlayerPosition de novo
/// e restaura tudo no mesmo quadro); se está em outro mapa, vai para o ponto do prédio. O marcador é uma cópia do
/// marcador do jogador, com outra cor. A grade de cada jogador vem no fim da mensagem de posição (PlayerSync).
/// </summary>
[HarmonyPatch(typeof(UIMapArea), "SetPlayerPosition")]
public static class MapMarkers
{
    // Longe do azul do marcador do jogador local (por id: host rosa, convidados laranja, verde, amarelo).
    private static readonly Color[] Colors =
    {
        new(1f, 0.55f, 0.8f), new(1f, 0.6f, 0.25f), new(0.55f, 1f, 0.45f), new(1f, 0.9f, 0.3f),
    };

    private static readonly List<GameObject> Clones = new();
    private static bool _inside;
    public static int Shown;
    public static string Last = "";

    private static void Postfix(UIMapArea __instance, FieldMasterData curFieldData, UIMapData data)
    {
        if (_inside) return;
        try { Refresh(__instance, data); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[MapMarkers] {e.Message}"); }
    }

    private static void Refresh(UIMapArea area, UIMapData data)
    {
        foreach (var c in Clones) if (c != null) UnityEngine.Object.Destroy(c);
        Clones.Clear();
        Shown = 0;
        Last = "";
        if (NetSession.Current == NetSession.Mode.Offline || data == null) return;
        var marker = area.playerPosition;
        var player = GameController.Instance.GetPlayer();
        if (marker == null || player == null) return;
        var notes = new List<string>();
        // o marcador local já está no lugar: guarda e restaura no fim
        var savedGrid = player.GridPosition;
        var savedParent = marker.parent;
        var savedSibling = marker.GetSiblingIndex();
        var savedPos = marker.anchoredPosition;
        var savedActive = marker.gameObject.activeSelf;
        try
        {
            foreach (var (id, field, grid) in PlayerSync.RemoteMapInfos())
            {
                var fm = MasterDataManager.Instance.FieldMaster.GetDataBySeasonalFieldId(field);
                if (fm == null) { notes.Add($"#{id}: mapa {field} desconhecido"); continue; }
                GameObject clone = null;
                var here = fm.Id == data.FieldId;
                if (here)
                {
                    player.GridPosition = grid;
                    _inside = true;
                    try { area.SetPlayerPosition(fm, data); }
                    finally { _inside = false; }
                    if (marker.gameObject.activeSelf) clone = UnityEngine.Object.Instantiate(marker.gameObject, marker.parent);
                }
                else
                {
                    var anchor = FindAnchor(area, data, fm.Id);
                    if (anchor != null)
                    {
                        clone = UnityEngine.Object.Instantiate(marker.gameObject, savedParent);
                        clone.SetActive(true);
                        clone.transform.position = anchor.transform.position;
                    }
                }
                if (clone == null) { notes.Add($"#{id}: fora deste mapa ({fm.Id})"); continue; }
                clone.name = $"SOSGB-Jogador{id}";
                foreach (var img in clone.GetComponentsInChildren<Image>(true)) img.color = Colors[id % Colors.Length];
                Clones.Add(clone);
                Shown++;
                notes.Add($"#{id}: {(here ? "no mapa" : "no prédio")} grade={grid} pos={clone.GetComponent<RectTransform>()?.anchoredPosition}");
            }
        }
        finally
        {
            player.GridPosition = savedGrid;
            marker.SetParent(savedParent, false);
            marker.SetSiblingIndex(savedSibling);
            marker.anchoredPosition = savedPos;
            marker.gameObject.SetActive(savedActive);
        }
        Last = string.Join("; ", notes);
        if (notes.Count > 0) Plugin.Logger.LogInfo($"[MapMarkers] mapa {data.FieldId}: {Last}");
    }

    /// <summary>Ponto do prédio (ou do mapa, na página de todos os mapas) em que o jogador remoto está.</summary>
    private static UIMapAnchor FindAnchor(UIMapArea area, UIMapData data, uint fieldId)
    {
        UIMapAnchorData ad = null;
        var ok = data.FieldId == UIMapData.ALL_MAP_ID ? data.TryGetFieldAnchorData(fieldId, out ad) : data.TryGetInDoorAnchorData(fieldId, out ad);
        if (!ok || ad == null || area.anchorList == null) return null;
        foreach (var a in area.anchorList)
            if (a != null && a.AnchorData != null && a.AnchorData.Pointer == ad.Pointer) return a;
        return null;
    }

    public static object Status() => new { shown = Shown, last = Last };
}
