// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e CODIGO.md ("Aparência do personagem").
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// EXPERIMENTO (só modo de teste): montar um avatar com a aparência de OUTRO jogador usando o caminho
/// do próprio jogo que remonta o modelo do jogador (AvatarMakeManager.ChangePlayerAvatar):
///   1) troca temporariamente as AvatarInfos do jogador local pelas do outro;
///   2) espera o jogo remontar e copia (Instantiate) o modelo resultante;
///   3) devolve as AvatarInfos originais e remonta de novo.
/// Objetivo: descobrir se a cópia continua válida depois que o jogador volta ao normal.
/// </summary>
public static class AppearanceLab
{
    public static string Serialize(AvatarInfo[] infos) =>
        string.Join(",", infos.Select(i => $"{(int)i.Category}:{i.MasterId}"));

    public static AvatarInfo[] Build(string data)
    {
        var mdm = MasterDataManager.Instance;
        var list = new List<AvatarInfo>();
        foreach (var part in data.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            var cat = (AvatarPartsCategory)int.Parse(kv[0]);
            var id = uint.Parse(kv[1]);
            var master = id == 0 ? null : mdm.AvatarMakeMaster.GetData(id);
            list.Add(master == null ? AvatarInfo.CreateNoParts(cat) : AvatarInfo.CreateParts(master));
        }
        return list.ToArray();
    }

    /// <summary>Variação da aparência local: troca cabelo, cor do cabelo e roupa por outras peças válidas.</summary>
    public static string Variant(AvatarInfo[] local)
    {
        var all = MasterDataManager.Instance.AvatarMakeMasterData;
        var cats = new[] { AvatarPartsCategory.HairStyle, AvatarPartsCategory.HairColor, AvatarPartsCategory.Clothes };
        var parts = new List<string>();
        foreach (var i in local)
        {
            var id = i.MasterId;
            if (cats.Contains(i.Category))
            {
                foreach (var m in all)
                {
                    if (m.Category != (int)i.Category || m.Id == id || m.DlcId != 0) continue;
                    id = m.Id;
                    break;
                }
            }
            parts.Add($"{(int)i.Category}:{id}");
        }
        return string.Join(",", parts);
    }

    /// <summary>
    /// Sonda (D29): troca peças do jogador local e avança o ChangePlayerAvatar com o CoroutineProbe, para saber
    /// em que sub-etapa ele trava. mode: "none" (direto), "detail" (DetailPageResourceLoad antes) ou
    /// "costume" (PreloadCostume antes).
    /// </summary>
    public static void Probe(MonoBehaviour host, string changes, string mode) =>
        host.StartCoroutine(ProbeRoutine(changes, mode).WrapToIl2Cpp());

    public static string WithChanges(string current, string changes)
    {
        var parts = current.Split(',').ToList();
        foreach (var kv in changes.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = kv.Split(':')[0];
            var idx = parts.FindIndex(x => x.StartsWith(c + ":"));
            if (idx >= 0) parts[idx] = kv; else parts.Add(kv);
        }
        return string.Join(",", parts);
    }

    private static string Flags(AvatarMakeManager amm)
    {
        string F(Func<bool> f) { try { return f() ? "1" : "0"; } catch { return "?"; } }
        return $"trying={F(() => amm.IsTryingSetAvatarParts)} backyard={F(() => amm.IsBackYardLoad)} detail={F(() => amm.IsLoadingDetailPage)} making={F(() => amm.IsAvatarMaking)}";
    }

    private static string Visible(PlayerCharacter p)
    {
        var rs = p.Model.GetComponentsInChildren<Renderer>(true);
        var on = 0;
        foreach (var r in rs) if (r.enabled && r.gameObject.activeInHierarchy) on++;
        return $"{on}/{rs.Length} renderers visíveis";
    }

    private static IEnumerator ProbeRoutine(string changes, string mode)
    {
        var amm = AvatarMakeManager.Instance;
        var gc = GameController.Instance;
        var before = Serialize(amm.PlayerAvatarInfos);
        var target = WithChanges(before, changes);
        var infos = Build(target);
        LastReport = $"sonda rodando ({mode}) {changes}";
        var log = new System.Text.StringBuilder();
        log.AppendLine($"antes: {Flags(amm)}; {Visible(gc.GetPlayer())}");

        amm.PlayerAvatarInfos = infos;
        gc.PlayerAvatarInfos = infos;
        ExternalData.Instance.UserInfo.AvatarInfos = infos;

        var t = 0f;
        if (mode == "detail")
        {
            var ids = new Il2CppSystem.Collections.Generic.List<uint>();
            foreach (var i in infos) if (i.MasterId != 0) ids.Add(i.MasterId);
            amm.DetailPageResourceLoad(ids);
            yield return null;
            while (amm.IsLoadingDetailPage && t < 15f) { t += Time.unscaledDeltaTime; yield return null; }
            log.AppendLine($"detail: {t:0.0}s; {Flags(amm)}");
        }
        else if (mode == "costume")
        {
            var done = false;
            var count = -1;
            amm.PreloadCostume((Action<Il2CppSystem.Collections.Generic.HashSet<string>>)(names => { done = true; count = names?.Count ?? -1; }));
            while (!done && t < 15f) { t += Time.unscaledDeltaTime; yield return null; }
            log.AppendLine($"costume: {t:0.0}s done={done} recursos={count}; {Flags(amm)}");
        }

        var result = new CoroutineProbe.Result();
        yield return CoroutineProbe.Run(amm.ChangePlayerAvatar(AvatarMakeManager.ChangeType.Default, null), 15f, result, "AppearanceProbe").WrapToIl2Cpp();
        log.AppendLine($"ChangePlayerAvatar: fim={result.Finished} tempo-limite={result.TimedOut} {result.Seconds:0.0}s passos={result.Steps}");
        log.AppendLine($"depois: {Flags(amm)}; {Visible(gc.GetPlayer())}; agora={Serialize(amm.PlayerAvatarInfos)}");
        if (result.TimedOut) { log.AppendLine("PILHA:"); log.Append(result.Stack); }
        log.AppendLine("CAMINHO:");
        foreach (var step in result.Trail) log.AppendLine(step);
        LastReport = log.ToString();
        Plugin.Logger.LogInfo($"[AppearanceLab] sonda {mode} {changes}: {LastReport}");
        DevBridge.PushEvent("appearance-probe:fim");
    }

/// <summary>Sonda (D29): troca só as animações de gênero (SwitchGenderMotion) e mede o resultado.</summary>
    public static void Motion(MonoBehaviour host, int type) => host.StartCoroutine(MotionRoutine(type).WrapToIl2Cpp());

    private static IEnumerator MotionRoutine(int type)
    {
        var p = GameController.Instance.GetPlayer();
        LastReport = $"sonda rodando (motion {type})";
        var log = new System.Text.StringBuilder();
        log.AppendLine($"antes: {Visible(p)}; clip={Clip(p)}");
        var done = false;
        p.SwitchGenderMotion(type, (Action)(() => done = true));
        var t = 0f;
        while (!done && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
        log.AppendLine($"SwitchGenderMotion: fim={done} {t:0.0}s; {Visible(p)}; clip={Clip(p)}");
        yield return new WaitForSeconds(2f);
        log.AppendLine($"+2s: {Visible(p)}; clip={Clip(p)}");
        LastReport = log.ToString();
        Plugin.Logger.LogInfo($"[AppearanceLab] motion {type}: {LastReport}");
    }

    private static string Clip(PlayerCharacter p)
    {
        try
        {
            var anim = p.Model.GetComponentInChildren<Animator>();
            var infos = anim.GetCurrentAnimatorClipInfo(0);
            var ctrl = anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "null";
            return $"{(infos.Length > 0 ? infos[0].clip.name : "-")} ({ctrl})";
        }
        catch (Exception e) { return "? " + e.Message; }
    }

/// <summary>
    /// Sonda (D29): monta a aparência num modelo SEPARADO (o da tela de customização: CreateMakeAvatarModel +
    /// CreateAvatar), sem mexer no jogador local. Se funcionar, acaba o "pisca" da troca temporária.
    /// </summary>
    public static void Make(MonoBehaviour host, string changes) => host.StartCoroutine(MakeRoutine(changes).WrapToIl2Cpp());

    private static IEnumerator MakeRoutine(string changes)
    {
        var amm = AvatarMakeManager.Instance;
        var p = GameController.Instance.GetPlayer();
        var target = WithChanges(Serialize(amm.PlayerAvatarInfos), changes);
        LastReport = "sonda rodando (make)";
        var log = new System.Text.StringBuilder();
        log.AppendLine($"antes: {Flags(amm)}; local {Visible(p)}; ModelTransform={(amm.ModelTransform != null ? amm.ModelTransform.name : "null")}");

        var created = false;
        try { amm.CreateMakeAvatarModel((Action)(() => created = true)); }
        catch (Exception e) { log.AppendLine($"CreateMakeAvatarModel: exceção {e.Message}"); }
        var t = 0f;
        while (!created && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
        var mt = amm.ModelTransform;
        log.AppendLine($"CreateMakeAvatarModel: fim={created} {t:0.0}s; ModelTransform={(mt != null ? mt.name : "null")}; {Flags(amm)}");
        if (mt != null) log.AppendLine($"  modelo: pos={mt.position} ativo={mt.gameObject.activeInHierarchy} {VisibleOf(mt.gameObject)}");

        var result = new CoroutineProbe.Result();
        yield return CoroutineProbe.Run(amm.CreateAvatar(Build(target)), 15f, result, "MakeProbe").WrapToIl2Cpp();
        mt = amm.ModelTransform;
        log.AppendLine($"CreateAvatar: fim={result.Finished} tempo-limite={result.TimedOut} {result.Seconds:0.0}s passos={result.Steps}; {Flags(amm)}");
        if (result.TimedOut) { log.AppendLine("PILHA:"); log.Append(result.Stack); }
        yield return new WaitForSeconds(2f);
        if (mt != null)
        {
            log.AppendLine($"  modelo +2s: pos={mt.position} ativo={mt.gameObject.activeInHierarchy} {VisibleOf(mt.gameObject)}");
            var clone = UnityEngine.Object.Instantiate(mt.gameObject);
            clone.name = "SOSGB_MakeProbe";
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(mb);
            clone.transform.position = p.Model.transform.position + p.Model.transform.forward * 1.6f;
            clone.SetActive(true);
            LastClone = clone;
            log.AppendLine($"  clone: {VisibleOf(clone)}");
        }
        log.AppendLine($"local depois: {Visible(p)}; aparência local={Serialize(amm.PlayerAvatarInfos)}");
        log.AppendLine("CAMINHO:");
        foreach (var step in result.Trail) log.AppendLine(step);
        LastReport = log.ToString();
        Plugin.Logger.LogInfo($"[AppearanceLab] make {changes}: {LastReport}");
    }

    private static string VisibleOf(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        var on = 0;
        foreach (var r in rs) if (r.enabled && r.gameObject.activeInHierarchy) on++;
        return $"{on}/{rs.Length} renderers visíveis";
    }

/// <summary>
    /// Teste (D32) do caminho do avatar REMOTO numa instância só: pede ao AppearanceSync a montagem de uma
    /// aparência (a local com as peças trocadas) e põe a cópia na frente do jogador, como um "jogador falso".
    /// </summary>
    public static void FakeRemote(string changes)
    {
        var amm = AvatarMakeManager.Instance;
        var app = WithChanges(Serialize(amm.PlayerAvatarInfos), changes);
        var localMotion = Net.AppearanceSync.MotionTypeId(Serialize(amm.PlayerAvatarInfos));
        LastReport = $"sonda rodando (build) {app}";
        var t0 = Time.realtimeSinceStartup;
        Net.AppearanceSync.Build(app, (model, controller) =>
        {
            var p = GameController.Instance.GetPlayer();
            if (model == null) { LastReport = $"build: FALHOU ({Time.realtimeSinceStartup - t0:0.0}s)"; return; }
            if (LastClone != null) UnityEngine.Object.Destroy(LastClone);
            model.name = "SOSGB_FakeRemote";
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(mb);
            foreach (var col in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(col);
            var anim = model.GetComponentInChildren<Animator>();
            var otherGender = Net.AppearanceSync.MotionTypeId(app) != localMotion;
            var localAnim = p.Model.GetComponentInChildren<Animator>();
            var diag = $"local no callback: clip={ClipOf(localAnim)} ctrl={localAnim.runtimeAnimatorController?.name}#{localAnim.runtimeAnimatorController?.Pointer}; capturado={controller?.name}#{controller?.Pointer} tipo={controller?.GetIl2CppType().Name}";
            if (anim != null && controller != null && otherGender)
            {
                anim.runtimeAnimatorController = UnityEngine.Object.Instantiate(controller);
                anim.Rebind();
                anim.Update(0f);
                diag += $"; cópia={anim.runtimeAnimatorController.name}#{anim.runtimeAnimatorController.Pointer} clip após Rebind={ClipOf(anim)}";
            }
            Plugin.Logger.LogInfo($"[AppearanceLab] {diag}");
            model.transform.position = p.Model.transform.position + p.Model.transform.forward * 1.6f;
            LastClone = model;
            LastReport = $"build: ok em {Time.realtimeSinceStartup - t0:0.0}s; clone {VisibleOf(model)}; outro gênero={otherGender}; clip clone={ClipOf(anim)}\n{diag}";
            DevBridge.Instance.StartCoroutine(AfterRestore(p, model).WrapToIl2Cpp());
        });
    }

    private static IEnumerator AfterRestore(PlayerCharacter p, GameObject clone)
    {
        while (Net.AppearanceSync.Swapping) yield return null;
        yield return new WaitForSeconds(0.5f);
        LastReport += $"\nlocal depois de devolver: {Visible(p)}; aparência local={Serialize(AvatarMakeManager.Instance.PlayerAvatarInfos)}; clip local={ClipOf(p.Model.GetComponentInChildren<Animator>())}; clone ainda {VisibleOf(clone)}, clip={ClipOf(clone.GetComponentInChildren<Animator>())}";
    }

    private static string ClipOf(Animator anim)
    {
        try
        {
            var infos = anim.GetCurrentAnimatorClipInfo(0);
            return infos.Length > 0 ? infos[0].clip.name : "-";
        }
        catch { return "?"; }
    }

public static string LastReport = "ainda não rodou";
    public static GameObject LastClone;

    public static void Run(MonoBehaviour host, string variant) => host.StartCoroutine(RunRoutine(variant).WrapToIl2Cpp());

    private static IEnumerator RunRoutine(string variant)
    {
        var amm = AvatarMakeManager.Instance;
        var gc = GameController.Instance;
        var player = gc.GetPlayer();
        var original = amm.PlayerAvatarInfos;
        var originalStr = Serialize(original);
        LastReport = "trocando aparência...";

        amm.PlayerAvatarInfos = Build(variant);
        gc.PlayerAvatarInfos = amm.PlayerAvatarInfos;
        DevBridge.Instance.StartCoroutine(amm.ChangePlayerAvatar(AvatarMakeManager.ChangeType.Default, null));
        yield return new WaitForSeconds(4f);

        var model = player.Model;
        var clone = UnityEngine.Object.Instantiate(model);
        clone.name = "SOSGB_AppearanceLab";
        foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(mb);
        clone.transform.position = model.transform.position + model.transform.forward * 1.6f;
        LastClone = clone;
        var cloneName = clone.transform.childCount > 0 ? clone.transform.GetChild(0).name : "?";

        amm.PlayerAvatarInfos = Build(originalStr);
        gc.PlayerAvatarInfos = amm.PlayerAvatarInfos;
        DevBridge.Instance.StartCoroutine(amm.ChangePlayerAvatar(AvatarMakeManager.ChangeType.Default, null));
        yield return new WaitForSeconds(4f);

        var localName = player.Model.transform.childCount > 0 ? player.Model.transform.GetChild(0).name : "?";
        var cloneAlive = LastClone != null;
        LastReport = $"clone={cloneName} vivo={cloneAlive}; local voltou para {localName}; original={originalStr}; variante={variant}";
        Plugin.Logger.LogInfo($"[AppearanceLab] {LastReport}");
        DevBridge.PushEvent("appearance-lab:fim");
    }
}
