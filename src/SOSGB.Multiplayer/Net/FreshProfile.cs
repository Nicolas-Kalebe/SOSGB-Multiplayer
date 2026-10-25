// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d30.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BokuMono;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Perfil de um convidado NOVO no mundo do host (D30: "começa do zero, como no Stardew/Minecraft").
/// O progresso do convidado só existe no mundo do host; o que ele fez jogando sozinho não importa.
///
/// Partimos das partes do perfil capturadas logo após carregar o mundo do host (são os dados DO HOST)
/// e "zeramos" o que é progresso pessoal:
///   likeability → corações/contadores zerados, sem namoro/noivado (tetos de nível do mundo mantidos);
///   talk        → ninguém conhecido (falas de "primeiro encontro" voltam; TalkCond sem histórico);
///   bag         → mochila vazia (mesmo número de espaços);
///   tools       → mesmas ferramentas do host, mas no NÍVEL BÁSICO (cadeia de UpgradeToolId, só itens válidos);
///   toolstate   → regador vazio;
///   buffs       → sem bônus de comida (os do host não passam);
///   appearance  → o visual do personagem do save do próprio convidado (só aparência, não progresso).
/// </summary>
public static class FreshProfile
{
    public static string Make(Dictionary<string, string> hostParts, string ownAppearance)
    {
        var fresh = new Dictionary<string, string>();
        foreach (var kv in hostParts)
        {
            try
            {
                fresh[kv.Key] = kv.Key switch
                {
                    "likeability" => Likeability(kv.Value),
                    "talk" => EmptyArrays(kv.Value),
                    "bag" => EmptyBag(kv.Value),
                    "tools" => BasicTools(kv.Value),
                    "toolstate" => ZeroNumbers(kv.Value),
                    "buffs" => EmptyArrays(kv.Value), // sem bônus de comida herdados do host
                    "appearance" => string.IsNullOrEmpty(ownAppearance) ? kv.Value : ownAppearance,
                    _ => kv.Value,
                };
            }
            catch (Exception e)
            {
                Plugin.Logger.LogWarning($"[FreshProfile] parte {kv.Key}: {e.Message} (mantida como estava)");
                fresh[kv.Key] = kv.Value;
            }
        }
        return JsonSerializer.Serialize(fresh);
    }

    private static string Likeability(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        o["FianceCharaId"] = 0;
        o["PartnerCharaId"] = 0;
        foreach (var listName in new[] { "LikeabilityDatas", "YesterdayLikeabilityDataList" })
            if (o[listName] is JsonArray arr)
                foreach (var e in arr)
                    if (e?["Value"] is JsonObject v)
                        foreach (var key in v.Select(p => p.Key).ToList())
                            if (v[key] is JsonValue) v[key] = 0;
        if (o["NonLikeabilityCharaTalkedList"] is JsonArray) o["NonLikeabilityCharaTalkedList"] = new JsonArray();
        // LikeabilityLevelCapDatas: tetos de coração liberados pelo progresso do MUNDO — mantidos.
        return o.ToJsonString();
    }

    /// <summary>Esvazia todas as listas, inclusive as aninhadas (TalkCond.characterCond/FestCond).</summary>
    private static string EmptyArrays(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        EmptyArraysIn(o);
        return o.ToJsonString();
    }

    private static void EmptyArraysIn(JsonObject o)
    {
        foreach (var key in o.Select(p => p.Key).ToList())
        {
            if (o[key] is JsonArray) o[key] = new JsonArray();
            else if (o[key] is JsonObject child) EmptyArraysIn(child);
        }
    }

    /// <summary>
    /// A mochila tem espaços FIXOS (ex.: 32 entradas, as vazias com ItemId 0): esvaziar a lista quebraria a
    /// mochila. Cada espaço vira um espaço vazio, no formato que o jogo grava (o tamanho da mochila é mantido).
    /// </summary>
    private static string EmptyBag(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        if (o["itemDatas"] is not JsonArray arr) return json;
        foreach (var e in arr)
        {
            if (e is not JsonObject slot) continue;
            foreach (var key in new[] { "ItemId", "Stack", "QualityValue", "FreshnessValue", "WrapId" }) slot[key] = 0;
            slot["IsAttachment"] = false;
            slot["isInExhibitStorage"] = false;
            slot["IsPictureBookIcon"] = false;
        }
        return o.ToJsonString();
    }

    private static string ZeroNumbers(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        foreach (var key in o.Select(p => p.Key).ToList())
            if (o[key] is JsonValue jv && jv.TryGetValue<double>(out _)) o[key] = 0;
        return o.ToJsonString();
    }

    private static string BasicTools(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        if (o["itemDatas"] is not JsonArray arr) return json;
        var previous = PreviousGrades();
        var items = MasterDataManager.Instance.ItemMaster;
        foreach (var e in arr)
        {
            if (e?["ItemId"] is not JsonValue idv || !idv.TryGetValue<uint>(out var id)) continue;
            // Desce a cadeia só enquanto o nível de baixo EXISTE COMO ITEM. Na tabela de ferramentas há entradas
            // internas (200, 500, 600, 700, nível 1) que não são itens: viravam espaços vazios na barra (achado
            // no teste em rede da Fase 5). O básico de verdade é 201, 501, 601, 701 (e 100 para o machado).
            var basic = id;
            var guard = 0;
            while (previous.TryGetValue(basic, out var lower) && items.TryGetData(lower, out _) && guard++ < 10) basic = lower;
            e["ItemId"] = basic;
        }
        return o.ToJsonString();
    }

    /// <summary>ferramenta → a versão anterior dela (quem tem UpgradeToolId apontando para ela).</summary>
    private static Dictionary<uint, uint> PreviousGrades()
    {
        var map = new Dictionary<uint, uint>();
        foreach (var t in MasterDataManager.Instance.ToolMaster.list)
            if (t.UpgradeToolId != 0 && t.UpgradeToolId != t.Id)
                map[t.UpgradeToolId] = t.Id;
        return map;
    }
}
