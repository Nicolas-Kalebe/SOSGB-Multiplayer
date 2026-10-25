// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e ROTEIRO.md (Fase 5).
using System;
using System.Collections.Generic;
using System.Linq;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// LABORATÓRIO da Fase 5 (só testes, comando "farm" da ponte): objetos do mapa atual (terra arada, plantas,
/// árvores, pedras/mato...) e experimentos de remover/recriar um objeto a partir dos dados dele — a base de
/// uma sincronização genérica da fazenda (cada tipo tem CreateX(fieldCache, dados) e entidade.Unregister()).
/// </summary>
public static class FarmLab
{
    public static FieldCache Cache => FieldManager.Instance.CurrentFieldCache;

    public static IEnumerable<FieldLayerObjectEntity> All()
    {
        foreach (var kv in Cache.LayerObjects)
            foreach (var e in kv.Value)
                if (e != null) yield return e;
    }

    /// <summary>Dados completos (do tipo certo) de uma entidade, em JSON.</summary>
    // ToData chamado pela classe BASE não copia os campos do tipo (vêm zerados): converter antes (Cast).
    // A classe real decide (flores silvestres se declaram "Crop", mas são FieldFlowerEntity).
    public static string DataJson(FieldLayerObjectEntity e)
    {
        if (e.TryCast<FieldFlowerEntity>() is { } fl) return StringExtensions.ToJson(fl.ToData<FieldFlowerEntityData>());
        if (e.TryCast<FieldCropEntity>() is { } cr) return StringExtensions.ToJson(cr.ToData<FieldCropEntityData>());
        if (e.TryCast<FieldRidgeEntity>() is { } ri) return StringExtensions.ToJson(ri.ToData<FieldRidgeEntityData>());
        if (e.TryCast<FieldTreeEntity>() is { } tr) return StringExtensions.ToJson(tr.ToData<FieldTreeEntityData>());
        if (e.TryCast<FieldGeneralEntity>() is { } ge) return StringExtensions.ToJson(ge.ToData<FieldGeneralEntityData>());
        return StringExtensions.ToJson(e.ToData<FieldLayerObjectEntityData>());
    }

    /// <summary>Serializador oficial do mapa: CreateData + DecodeObjectSaveData (listas tipadas completas).</summary>
    public static object Snapshot(string guid)
    {
        var fm = FieldManager.Instance;
        var sd = fm.SaveManager.CreateData((FieldMasterId)fm.CurrentFieldId, Cache);
        var od = FieldSaveManager.DecodeObjectSaveData(sd);
        string found = null;
        if (guid != null)
            foreach (var c in od.CropObjectSaveDatas) if (c.Guid == guid) found = StringExtensions.ToJson(c);
        return new
        {
            crops = od.CropObjectSaveDatas?.Count, ridges = od.RidgeObjectSaveDatas?.Count, trees = od.TreeObjectSaveDatas?.Count,
            general = od.GeneralObjectSaveDatas?.Count, items = od.ItemObjectSaveDatas?.Count, grass = od.GrassObjectSaveDatas?.Count,
            flowers = od.FlowerObjectSaveDatas?.Count, inRidge = od.InRidgeObjectSaveDatas?.Count, animals = od.AnimalObjectSaveDatas?.Count,
            found,
        };
    }

    /// <summary>Cria uma entidade a partir do JSON dos dados, pelo mesmo caminho que o jogo usa ao carregar o mapa.</summary>
    public static string Create(FieldDynamicObjectType type, string json)
    {
        var acc = FieldManager.Instance.ManagerAccessor;
        switch (type)
        {
            case FieldDynamicObjectType.Ridge:
                acc.AttributeManager.CreateRidge(Cache, StringExtensions.JsonToObject<FieldRidgeEntityData>(json), null);
                return "ok";
            case FieldDynamicObjectType.Crop:
            {
                var d = StringExtensions.JsonToObject<FieldCropEntityData>(json);
                var ridge = Cache.GetLayerObject(d.ParentGuid)?.TryCast<FieldRidgeEntity>();
                var tree = Cache.GetLayerObject(d.ParentGuid)?.TryCast<FieldTreeEntity>();
                acc.CropManager.CreateCrop(Cache, d, ridge, tree);
                return $"ok (pai: {(ridge != null ? "canteiro" : tree != null ? "árvore" : "nenhum")})";
            }
            case FieldDynamicObjectType.Tree:
                acc.TreeManager.CreateTree(Cache, StringExtensions.JsonToObject<FieldTreeEntityData>(json));
                return "ok";
            case FieldDynamicObjectType.General:
                acc.GeneralManager.CreateLayerObject(Cache, StringExtensions.JsonToObject<FieldGeneralEntityData>(json));
                return "ok";
            default:
                return $"tipo {type} ainda não suportado";
        }
    }

    /// <summary>Custo de ler o estado do mapa: JSON por entidade x serializador oficial (ms).</summary>
    public static object Bench()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var n = 0; var bytes = 0;
        foreach (var e in All())
        {
            if (e.FieldObjectType is FieldDynamicObjectType.Animal) continue;
            bytes += DataJson(e).Length; n++;
        }
        var perEntity = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var fm = FieldManager.Instance;
        var sd = fm.SaveManager.CreateData((FieldMasterId)fm.CurrentFieldId, Cache);
        var create = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var od = FieldSaveManager.DecodeObjectSaveData(sd);
        var decode = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        var cheap = 0L;
        foreach (var e in All()) cheap ^= e.Guid.GetHashCode() ^ e.HP ^ e.Position.GetHashCode();
        var walk = sw.Elapsed.TotalMilliseconds;
        return new { entities = n, jsonBytes = bytes, perEntityJsonMs = perEntity, createDataMs = create, decodeMs = decode, walkOnlyMs = walk };
    }

    private static string _captured; // FieldSaveData capturado (JSON), testes de aplicar/instalar

    public static object Cap()
    {
        var sd = Net.FieldState.CaptureSaveData();
        _captured = Net.FieldState.ToJson(sd);
        var back = Net.FieldState.FromJson(_captured);
        return new { field = (uint)sd.FieldId, bytes = _captured.Length, entries = Net.FieldState.Entries(sd).Count, roundTripEntries = back != null ? Net.FieldState.Entries(back).Count : -1 };
    }

    public static object ApplyCaptured()
    {
        if (_captured == null) return "nada capturado";
        var target = Net.FieldState.Entries(Net.FieldState.FromJson(_captured));
        var rep = Net.FieldState.ApplyLive(target);
        var after = Net.FieldState.CaptureLive();
        var diff = target.Count(kv => !after.TryGetValue(kv.Key, out var a) || a.Json != kv.Value.Json) + after.Count(kv => !target.ContainsKey(kv.Key));
        return new { report = rep.ToString(), differencesAfter = diff };
    }

    /// <summary>Aplica a captura no cache RESIDENTE do mapa capturado, estando em outro mapa.</summary>
    public static object ApplyResident()
    {
        if (_captured == null) return "nada capturado";
        var sd = Net.FieldState.FromJson(_captured);
        var root = Net.FieldState.Root((uint)sd.FieldId);
        var cache = Net.FieldState.CacheFor(root);
        if (cache == null) return $"mapa {root} não está carregado";
        var rep = Net.FieldState.ApplyLive(Net.FieldState.Entries(sd), null, cache, root);
        var crops = 0;
        foreach (var kv in cache.LayerObjects) foreach (var e in kv.Value) if (e != null && e.FieldObjectType == FieldDynamicObjectType.Crop) crops++;
        return new { root, active = cache.IsActiveField, report = rep.ToString(), cropsInCache = crops };
    }

    public static object InstallCaptured()
    {
        var sm = FieldManager.Instance.SaveManager;
        var ids = new List<string>();
        foreach (var d in sm.SaveDataArray) ids.Add(d == null ? "null" : ((uint)d.FieldId).ToString());
        var g2 = sm.GetData(2u);
        return new
        {
            installed = _captured != null && Net.FieldState.Install(Net.FieldState.FromJson(_captured)),
            arrayIds = string.Join(",", ids), getData2 = g2 == null ? "null" : ((uint)g2.FieldId).ToString(),
            convert2 = MasterDataManager.Instance.FieldMaster.ConvertToFieldId(2u).ToString(),
        };
    }

    /// <summary>Quantos objetos o save de mapas tem para um mapa (raiz), por tipo; e se o array é o mesmo objeto.</summary>
    public static object Saved(uint root)
    {
        var sm = FieldManager.Instance.SaveManager;
        var sd = sm.GetData(root);
        if (sd == null) return "sem dados";
        var counts = new Dictionary<string, int>();
        foreach (var e in Net.FieldState.Entries(sd).Values) counts[e.Type.ToString()] = counts.TryGetValue(e.Type.ToString(), out var c) ? c + 1 : 1;
        var a1 = sm.SaveDataArray; var a2 = sm.SaveDataArray;
        return new { field = (uint)sd.FieldId, lastTicks = sd.LastTicks, counts, sameArrayObject = a1.Pointer == a2.Pointer };
    }

    /// <summary>Objetos "soltos" (não de grade) do mapa atual: itens no chão, insetos etc., por tipo.</summary>
    public static object Free()
    {
        var counts = new Dictionary<string, int>();
        var samples = new List<string>();
        foreach (var f in Cache.FreeObjects)
        {
            if (f == null) continue;
            var t = f.FieldObjectType.ToString() + "/" + f.GetIl2CppType().Name;
            counts[t] = counts.TryGetValue(t, out var c) ? c + 1 : 1;
            if (samples.Count < 3) samples.Add($"{t} {f.Guid} {f.Position}");
        }
        return new { counts, samples };
    }

    public static object LiveKind(string kind)
    {
        var list = new List<string>();
        foreach (var e in Net.FieldState.CaptureLive().Values) if (e.Type.ToString() == kind) list.Add(e.Json);
        return list;
    }

    public static object Residents()
    {
        var list = new List<string>();
        foreach (var kv in FieldManager.Instance.fieldCacheDict)
        {
            var n = 0;
            foreach (var l in kv.Value.LayerObjects) n += l.Value.Count;
            list.Add($"{(uint)kv.Key} ({Net.Locations.Name((uint)kv.Key)}): {n} objetos, ativo={kv.Value.IsActiveField}");
        }
        return list;
    }

    public static object Near(int n)
    {
        var p = GameController.Instance.GetPlayerTransform().position;
        return All().OrderBy(e => (e.Position - p).sqrMagnitude).Take(n)
            .Select(e => $"{e.FieldObjectType} {e.Guid} ({e.Position.x:0.0},{e.Position.z:0.0}) d={(e.Position - p).magnitude:0.0}").ToList();
    }

    public static object Remove(string guid)
    {
        var e = Cache.GetLayerObject(guid);
        if (e == null) return "não achei";
        var json = DataJson(e);
        e.Unregister();
        return new { removed = e.FieldObjectType.ToString(), json, after = Cache.GetLayerObject(guid) != null ? "ainda existe" : "sumiu" };
    }

    /// <summary>Remove e recria a partir dos próprios dados (teste de ida e volta).</summary>
    public static object Recreate(string guid)
    {
        var e = Cache.GetLayerObject(guid);
        if (e == null) return "não achei";
        var type = e.FieldObjectType;
        var json = DataJson(e);
        e.Unregister();
        var gone = Cache.GetLayerObject(guid) == null;
        var result = Create(type, json);
        var back = Cache.GetLayerObject(guid);
        return new { type = type.ToString(), goneAfterUnregister = gone, create = result, exists = back != null, sameData = back != null && DataJson(back) == json, json };
    }
}
