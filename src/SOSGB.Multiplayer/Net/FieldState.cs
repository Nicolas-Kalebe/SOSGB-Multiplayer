// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d40.
using System;
using System.Collections.Generic;
using System.Linq;
using BokuMono;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Estado dos objetos de um mapa (terra arada, plantas, árvores, pedras/mato, grama, flores, sprinklers, ovos) —
/// a base da sincronização da fazenda (Fase 5, D40). Animais e itens soltos no chão ficam de fora (por enquanto).
///
///  - Captura: serializador OFICIAL do jogo (FieldSaveManager.CreateData + DecodeObjectSaveData): listas tipadas e
///    completas. (ToData chamado pela classe base vem com os campos do tipo zerados.)
///  - Aplicar ao vivo: compara com o mapa carregado e só mexe no que difere: remove (filhos antes dos pais) e cria
///    (pais antes dos filhos) com as mesmas funções que o jogo usa ao carregar o mapa (CreateX(fieldCache, dados)).
///    Mudança = remover + criar com o mesmo GUID. Validado na tela: some e volta igual.
///  - Instalar: põe o estado no save de mapas em memória (FieldSaveManager), para um mapa onde o jogador não está.
/// </summary>
public static class FieldState
{
    /// <summary>Tipo de objeto do mod (o enum do jogo não tem nome para grama e flor). A ordem é a de criação.</summary>
    public enum Kind { Ridge, Tree, General, Crop, Sprinkler, Egg, Grass, Flower }

    public sealed class Entry
    {
        public Kind Type;
        public string Json;
        public string Parent;
    }

    // Pais primeiro ao criar (ordem do enum Kind); ao remover, a ordem inversa.

    public static FieldCache Cache => FieldManager.Instance.CurrentFieldCache;
    public static uint CurrentField => FieldManager.Instance.CurrentFieldId;

    /// <summary>FieldSaveData do mapa atual (ou de outro cache carregado), gerado pelo próprio jogo.</summary>
    public static FieldSaveData CaptureSaveData(FieldCache cache = null, uint fieldId = 0)
    {
        cache ??= Cache;
        if (fieldId == 0) fieldId = cache == Cache ? CurrentField : 0;
        return FieldManager.Instance.SaveManager.CreateData((FieldMasterId)fieldId, cache);
    }

    /// <summary>
    /// Cache carregado de um mapa (pelo ID raiz): o atual, ou um RESIDENTE (o jogo mantém alguns mapas sempre na
    /// memória, ex.: a fazenda, e processa o dia neles direto). Null = mapa não carregado (use Install).
    /// </summary>
    public static FieldCache CacheFor(uint root)
    {
        var fm = FieldManager.Instance;
        if (Root(fm.CurrentFieldId) == root) return fm.CurrentFieldCache;
        var dict = fm.fieldCacheDict;
        return dict != null && dict.TryGetValue((FieldMasterId)root, out var c) ? c : null;
    }

    public static Dictionary<string, Entry> Entries(FieldSaveData sd)
    {
        var od = FieldSaveManager.DecodeObjectSaveData(sd);
        var map = new Dictionary<string, Entry>();
        void Add<T>(Il2CppSystem.Collections.Generic.List<T> list, Kind type) where T : FieldDynamicObjectEntityData
        {
            if (list == null) return;
            foreach (var d in list)
                if (d != null && !string.IsNullOrEmpty(d.Guid))
                    map[d.Guid] = new Entry { Type = type, Json = StringExtensions.ToJson(d), Parent = d.ParentGuid };
        }
        Add(od.RidgeObjectSaveDatas, Kind.Ridge);
        Add(od.TreeObjectSaveDatas, Kind.Tree);
        Add(od.GeneralObjectSaveDatas, Kind.General);
        Add(od.CropObjectSaveDatas, Kind.Crop);
        Add(od.InRidgeObjectSaveDatas, Kind.Sprinkler);
        Add(od.EggObjectSaveDatas, Kind.Egg);
        Add(od.GrassObjectSaveDatas, Kind.Grass);
        Add(od.FlowerObjectSaveDatas, Kind.Flower);
        return map;
    }

    public static Dictionary<string, Entry> CaptureLive(FieldCache cache = null, uint fieldId = 0) => Entries(CaptureSaveData(cache, fieldId));

    /// <summary>Resultado de uma aplicação: quantos objetos foram removidos, criados e quantos falharam.</summary>
    public sealed class ApplyReport { public int Removed, Created, Failed; public string LastError; public override string ToString() => $"−{Removed} +{Created} falhas {Failed}{(LastError != null ? " (" + LastError + ")" : "")}"; }

    /// <summary>Deixa o mapa carregado igual a <paramref name="target"/>, mexendo só no que difere.</summary>
    public static ApplyReport ApplyLive(Dictionary<string, Entry> target, Dictionary<string, Entry> current = null, FieldCache cache = null, uint fieldId = 0)
    {
        cache ??= Cache;
        current ??= CaptureLive(cache, fieldId);
        var rep = new ApplyReport();
        var toRemove = current.Where(kv => !target.TryGetValue(kv.Key, out var t) || t.Json != kv.Value.Json).Select(kv => kv.Key).ToList();
        var toCreate = target.Where(kv => !current.TryGetValue(kv.Key, out var c) || c.Json != kv.Value.Json).Select(kv => kv.Key).ToList();

        // Remover: filhos antes dos pais.
        foreach (var guid in toRemove.OrderByDescending(g => (int)current[g].Type))
        {
            try { var e = cache.GetLayerObject(guid); if (e != null) { e.Unregister(); rep.Removed++; } }
            catch (Exception ex) { rep.Failed++; rep.LastError = ex.Message; }
        }
        // Criar: pais antes dos filhos.
        foreach (var guid in toCreate.OrderBy(g => (int)target[g].Type))
        {
            try { Create(target[guid], cache); rep.Created++; }
            catch (Exception ex) { rep.Failed++; rep.LastError = ex.Message; }
        }
        return rep;
    }

    public static void Create(Entry e, FieldCache cache = null)
    {
        var acc = FieldManager.Instance.ManagerAccessor;
        var c = cache ?? Cache;
        switch (e.Type)
        {
            case Kind.Ridge: acc.AttributeManager.CreateRidge(c, StringExtensions.JsonToObject<FieldRidgeEntityData>(e.Json), null); break;
            case Kind.Tree:
            {
                var d = StringExtensions.JsonToObject<FieldTreeEntityData>(e.Json);
                acc.TreeManager.CreateTree(c, d, c.GetLayerObject(d.ParentGuid)?.TryCast<FieldRidgeEntity>(), null);
                break;
            }
            case Kind.General: acc.GeneralManager.CreateLayerObject(c, StringExtensions.JsonToObject<FieldGeneralEntityData>(e.Json)); break;
            case Kind.Crop:
            {
                var d = StringExtensions.JsonToObject<FieldCropEntityData>(e.Json);
                var parent = c.GetLayerObject(d.ParentGuid);
                acc.CropManager.CreateCrop(c, d, parent?.TryCast<FieldRidgeEntity>(), parent?.TryCast<FieldTreeEntity>());
                break;
            }
            case Kind.Sprinkler: acc.SprinklerManager.CreateObject(c, StringExtensions.JsonToObject<FieldSprinklerEntityData>(e.Json)); break;
            case Kind.Egg: acc.EggManager.CreateEgg(c, StringExtensions.JsonToObject<FieldEggEntityData>(e.Json)); break;
            case Kind.Grass: acc.GrassManager.CreateGrass(c, StringExtensions.JsonToObject<FieldGrassEntityData>(e.Json)); break;
            case Kind.Flower: acc.FlowerManager.CreateFlower(c, StringExtensions.JsonToObject<FieldFlowerEntityData>(e.Json)); break;
        }
    }

    /// <summary>Põe um FieldSaveData no save de mapas em memória (substitui o do mesmo mapa). Retorna se achou.</summary>
    public static bool Install(FieldSaveData sd)
    {
        // O save de mapas guarda pelo ID RAIZ (ex.: 1); o mapa carregado tem o ID sazonal (ex.: 2 = mapa 1 no verão).
        var root = Root((uint)sd.FieldId);
        var arr = FieldManager.Instance.SaveManager.SaveDataArray;
        for (var i = 0; i < arr.Length; i++)
        {
            if (arr[i] == null || (uint)arr[i].FieldId != root) continue;
            sd.FieldId = (FieldMasterId)root;
            arr[i] = sd;
            return true;
        }
        return false;
    }

    /// <summary>ID raiz do mapa (variantes sazonais viram o mesmo mapa). Use para saber se dois jogadores estão no mesmo mapa.</summary>
    public static uint Root(uint fieldId) => (uint)MasterDataManager.Instance.FieldMaster.ConvertToFieldId(fieldId);

    public static string ToJson(FieldSaveData sd) => StringExtensions.ToJson(sd);
    public static FieldSaveData FromJson(string json) => StringExtensions.JsonToObject<FieldSaveData>(json);
}
