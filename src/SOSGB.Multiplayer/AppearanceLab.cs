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
