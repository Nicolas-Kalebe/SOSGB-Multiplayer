// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md.
using System;
using System.Collections.Generic;
using BokuMono;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Nome traduzido de um mapa ("fieldId" → "Fazenda", "Cidade"...), para a lista de jogadores e os avisos.
/// Usa a tabela de textos do próprio jogo (LocalizeTextTableType.FieldNameText). Os IDs "sazonais"
/// (ex.: 10002) não têm nome próprio; usamos o ID raiz da tabela de mapas (FieldMaster.ConvertToFieldId).
/// </summary>
public static class Locations
{
    private static readonly Dictionary<uint, string> Cache = new();

    public static string Name(uint fieldId)
    {
        if (fieldId == 0) return "-";
        if (Cache.TryGetValue(fieldId, out var cached)) return cached;
        var name = Lookup(fieldId);
        // ID sazonal (ex.: fazenda no verão) → ID "raiz" pela tabela de mapas do jogo.
        if (string.IsNullOrEmpty(name))
        {
            try
            {
                var root = (uint)MasterDataManager.Instance.FieldMaster.ConvertToFieldId(fieldId);
                if (root != 0 && root != fieldId) name = Lookup(root);
            }
            catch { /* tabela ainda não carregada */ }
        }
        if (string.IsNullOrEmpty(name)) name = $"mapa {fieldId}";
        Cache[fieldId] = name;
        return name;
    }

    private static string Lookup(uint id)
    {
        try
        {
            var text = new TextData().GetNumberLight(id, LocalizeTextTableType.FieldNameText, Array.Empty<int>());
            // Textos inexistentes costumam voltar vazios ou como marcador do próprio sistema.
            // Texto inexistente volta vazio ou como o próprio número.
            if (string.IsNullOrWhiteSpace(text) || uint.TryParse(text.Trim(), out _) || text.Contains("FieldName")) return null;
            return text;
        }
        catch { return null; }
    }
}
