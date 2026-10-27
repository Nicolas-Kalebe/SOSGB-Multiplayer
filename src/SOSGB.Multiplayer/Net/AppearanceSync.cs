// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d28 e #d32.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Aparência própria de cada jogador (D28, corrigida na D32).
///
/// Aparência = AvatarInfos do jogador (rosto, pele, olhos, cabelo, cor, barba, roupa, MotionType=gênero…)
/// serializadas como "categoria:masterId,…". Montar um modelo com outra aparência só é possível pelo
/// AvatarMakeManager, que trabalha sobre o modelo do JOGADOR LOCAL. Truque (validado em runtime):
///   troca temporariamente a aparência do jogador local → espera o jogo remontar → copia o modelo →
///   devolve a aparência original. A cópia continua válida depois da devolução.
/// As montagens são feitas uma de cada vez (fila).
///
/// Regras aprendidas na D32 (sonda CoroutineProbe):
///  - peça com id que NÃO existe na categoria trava a montagem para sempre → <see cref="Sanitize"/>;
///  - ao trocar por peças ainda não carregadas, o jogo ESCONDE o modelo por ~1–2 s enquanto carrega; copiar
///    nesse intervalo gera um avatar invisível → esperar o modelo reaparecer (<see cref="WaitVisible"/>);
///  - gênero: ChangePlayerAvatar troca o corpo e SwitchGenderMotion troca as animações (Mn_/Fn_). Funciona;
///  - o ChangePlayerAvatar é avançado pelo CoroutineProbe, com tempo limite de verdade.
///
/// O convidado, ao carregar o mundo do host, herda a aparência DO HOST (ela está no save); por isso a
/// aparência também é parte do perfil individual (PlayerContext "appearance"): reaplicada no personagem
/// local depois de cada carga de mundo.
/// </summary>
public class AppearanceSync : MonoBehaviour
{
    public AppearanceSync(IntPtr ptr) : base(ptr) { }

    // id do jogador → aparência anunciada
    private static readonly Dictionary<byte, string> Known = new();

    // D77: aparência de outro jogador → peças do jogo (prefabs e texturas) que ela usa. Ficam na lista de proteção do jogo
    // (o jogo descarrega o que o personagem local não usa ao trocar de mapa e ao fechar o guarda-roupa; a cópia do avatar
    // ficava sem corpo e era preciso remontar, com segundos de "rosto flutuante").
    private static readonly Dictionary<string, HashSet<string>> RemoteResources = new();
    private float _nextLock;
    private static readonly Queue<Job> Jobs = new();
    private static bool _busy;
    private static bool _subscribed;
    internal static AppearanceSync Instance;

    // Categorias de "roupa" (as que podem ser de DLC/loja): numa falha, tenta de novo com as do jogador local.
    private static readonly int[] DressCategories =
    {
        (int)AvatarPartsCategory.Hat, (int)AvatarPartsCategory.Accessories, (int)AvatarPartsCategory.Clothes,
        (int)AvatarPartsCategory.HorseRidingHat, (int)AvatarPartsCategory.HorseRidingClothes,
        (int)AvatarPartsCategory.BagDesign, (int)AvatarPartsCategory.Glider,
    };

    private sealed class Job
    {
        public string Appearance;      // null = aplicar no jogador local (Target)
        public string Target;          // aparência para o jogador local (quando Appearance == null)
        public Action<GameObject, RuntimeAnimatorController> Done; // modelo null = não deu para montar
    }

    /// <summary>Verdadeiro durante uma troca temporária: o modelo local não está com a aparência do jogador.</summary>
    public static bool Swapping => _busy;

    private void Awake()
    {
        Instance = this;
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id => Known.Remove(id);
        // Alguém entrou: todos reanunciam a própria aparência (quem entrou precisa conhecer as dos outros).
        NetSession.OnPlayerJoinedNamed += (id, name) => Announce();
        NetSession.OnHosting += Announce;
    }

    private void Update()
    {
        if (Time.unscaledTime >= _nextLock) { _nextLock = Time.unscaledTime + 0.5f; ApplyLocks(); }
        if (!Plugin.OwnAppearance.Value) { Jobs.Clear(); return; }
        if (_busy || Jobs.Count == 0) return;
        if (!Ready()) return;
        StartCoroutine(Run(Jobs.Dequeue()).WrapToIl2Cpp());
    }

    // Sem montar durante eventos/cenas: o personagem local aparece neles e a troca temporária ficaria visível.
    // E só depois de o jogo instalar o controlador de animação real (AnimatorOverrideController): logo após
    // carregar o mundo o player ainda tem o controlador base, com clips "dummy_*" (avatar com pose quebrada).
    private static bool Ready() =>
        SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game
        && AvatarMakeManager.IsInstance && GameController.IsInstance && GameController.Instance.GetPlayer() != null
        && !GameController.Instance.IsPlayingEvent && !DaySync.InDayChange && !CharacterCreator.AvatarMakeBusy // D72/D75: criação/guarda-roupa mexem no modelo local
        && HasRealController(GameController.Instance.GetPlayer());

    private static bool HasRealController(PlayerCharacter p)
    {
        var anim = p.Model != null ? p.Model.GetComponentInChildren<Animator>() : null;
        return anim != null && anim.runtimeAnimatorController != null
            && anim.runtimeAnimatorController.TryCast<AnimatorOverrideController>() != null;
    }

    // ---------- API ----------

    /// <summary>Aparência atual do jogador local.</summary>
    public static string Local()
    {
        var infos = AvatarMakeManager.Instance.PlayerAvatarInfos;
        return string.Join(",", infos.Select(i => $"{(int)i.Category}:{i.MasterId}"));
    }

    /// <summary>Anuncia a aparência local para os outros (quando não está no meio de uma troca).</summary>
    public static void Announce()
    {
        if (NetSession.Current == NetSession.Mode.Offline || _busy || !Ready()) { _announcePending = true; return; }
        var id = NetSession.LocalId;
        var app = Local();
        _lastAnnounced = app;
        NetSession.SendGame(MsgType.PlayerAppearance, w => { w.Put(id); w.Put(app); }, DeliveryMethod.ReliableOrdered);
    }

    private static bool _announcePending;
    private static string _lastAnnounced;
    private float _nextCheck;

    /// <summary>Última aparência local fora de uma troca (para o perfil não capturar a aparência temporária).</summary>
    public static string Stable { get; private set; }

    private void LateUpdate()
    {
        if (!_busy && Ready() && Time.unscaledTime >= _nextCheck)
        {
            _nextCheck = Time.unscaledTime + 3f;
            Stable = Local();
            if (NetSession.Current != NetSession.Mode.Offline && _lastAnnounced != null && Stable != _lastAnnounced) Announce();
        }
        if (_announcePending && !_busy && Ready() && NetSession.Current != NetSession.Mode.Offline)
        {
            _announcePending = false;
            Announce();
        }
    }

    /// <summary>ID da peça MotionType (gênero das animações) dentro de uma aparência serializada.</summary>
    public static string MotionTypeId(string appearance)
    {
        if (string.IsNullOrEmpty(appearance)) return null;
        var key = ((int)AvatarPartsCategory.MotionType) + ":";
        foreach (var part in appearance.Split(',')) if (part.StartsWith(key)) return part;
        return null;
    }

    /// <summary>
    /// Troca por peças do <paramref name="fallback"/> toda peça cujo id não existe na categoria (D32: um id
    /// inválido, ex. cabelo 45020, deixa a montagem do jogo esperando para sempre).
    /// </summary>
    public static string Sanitize(string appearance, string fallback)
    {
        var fb = ToMap(fallback);
        var mdm = MasterDataManager.Instance;
        var parts = new List<string>();
        foreach (var kv in ToMap(appearance))
        {
            var id = kv.Value;
            if (id != 0)
            {
                BokuMono.Data.AvatarMakeMasterData m = null;
                try { m = mdm.AvatarMakeMaster.GetData(id); } catch { /* id desconhecido */ }
                if (m == null || m.Category != kv.Key)
                {
                    var replacement = fb.TryGetValue(kv.Key, out var f) ? f : 0;
                    Plugin.Logger.LogWarning($"[Appearance] peça inválida {kv.Key}:{id}; usando {kv.Key}:{replacement}");
                    id = replacement;
                }
            }
            parts.Add($"{kv.Key}:{id}");
        }
        return string.Join(",", parts);
    }

    /// <summary>A aparência com as peças de roupa (DressCategories) trocadas pelas de <paramref name="dressFrom"/>.</summary>
    private static string WithDressFrom(string appearance, string dressFrom)
    {
        var dress = ToMap(dressFrom);
        var parts = new List<string>();
        foreach (var kv in ToMap(appearance))
            parts.Add(DressCategories.Contains(kv.Key) && dress.TryGetValue(kv.Key, out var d) ? $"{kv.Key}:{d}" : $"{kv.Key}:{kv.Value}");
        return string.Join(",", parts);
    }

    private static List<KeyValuePair<int, uint>> ToMapList(string appearance)
    {
        var list = new List<KeyValuePair<int, uint>>();
        if (string.IsNullOrEmpty(appearance)) return list;
        foreach (var part in appearance.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[0], out var c) && uint.TryParse(kv[1], out var id)) list.Add(new(c, id));
        }
        return list;
    }

    private static Dictionary<int, uint> ToMap(string appearance)
    {
        var map = new Dictionary<int, uint>();
        foreach (var kv in ToMapList(appearance)) map[kv.Key] = kv.Value;
        return map;
    }

    public static string KnownAppearance(byte id) => Known.TryGetValue(id, out var a) ? a : null;

    /// <summary>Pede a montagem de um modelo com a aparência dada (resultado no callback; modelo null = falhou).</summary>
    public static void Build(string appearance, Action<GameObject, RuntimeAnimatorController> done) =>
        Jobs.Enqueue(new Job { Appearance = appearance, Done = done });

    /// <summary>Aplica uma aparência no jogador local (convidado: a própria, depois de carregar o mundo do host).</summary>
    public static void ApplyToLocal(string appearance)
    {
        if (string.IsNullOrEmpty(appearance)) return;
        Jobs.Enqueue(new Job { Appearance = null, Target = appearance });
    }

    // ---------- rede ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.PlayerAppearance) return;
        var id = r.GetByte();
        var app = r.GetString();
        if (id == NetSession.LocalId || id == NetSession.UnknownId) return;
        if (NetSession.Current == NetSession.Mode.Host && id != from) return;
        if (Known.TryGetValue(id, out var old) && old == app) return;
        Known[id] = app;
        Plugin.Logger.LogInfo($"[Appearance] aparência do jogador #{id} recebida");
        PlayerSync.InvalidateAvatar(id); // recria o avatar com a aparência certa
    }

    // ---------- montagem ----------

    private static AvatarInfo[] Parse(string data)
    {
        var mdm = MasterDataManager.Instance;
        var list = new List<AvatarInfo>();
        foreach (var kv in ToMapList(data))
        {
            var cat = (AvatarPartsCategory)kv.Key;
            var master = kv.Value == 0 ? null : mdm.AvatarMakeMaster.GetData(kv.Value);
            list.Add(master == null ? AvatarInfo.CreateNoParts(cat) : AvatarInfo.CreateParts(master));
        }
        return list.ToArray();
    }

    private static int MotionTypeOf(AvatarInfo[] infos)
    {
        foreach (var i in infos)
            if (i.Category == AvatarPartsCategory.MotionType)
                try { return i.MotionCategory; } catch { return 0; }
        return 0;
    }

    private sealed class SetResult { public bool Ok; public string Why = ""; }

    /// <summary>Troca a aparência do jogador local e espera o jogo terminar de remontar E mostrar o modelo.</summary>
    private static IEnumerator SetLocal(string appearance, SetResult res)
    {
        var amm = AvatarMakeManager.Instance;
        var gc = GameController.Instance;
        var infos = Parse(appearance);
        amm.PlayerAvatarInfos = infos;
        gc.PlayerAvatarInfos = infos;
        ExternalData.Instance.UserInfo.AvatarInfos = infos;

        var probe = new CoroutineProbe.Result();
        yield return CoroutineProbe.Run(amm.ChangePlayerAvatar(AvatarMakeManager.ChangeType.Default, null), 15f, probe, "Appearance").WrapToIl2Cpp();
        if (probe.TimedOut) { res.Why = "montagem passou do tempo limite"; yield break; }
        // D76: o jogo ainda aplica peças depois de o ChangePlayerAvatar terminar. Copiar ou devolver antes disso deixava o
        // visual de um jogador no personagem do outro.
        yield return WaitPartsSettled(amm, 6f).WrapToIl2Cpp();
        // D78: o ChangePlayerAvatar é a troca de roupa do jogo: aplica penteado, roupa, acessórios e corpo, mas NÃO rosto, pele,
        // cores (olhos, cabelo, sobrancelha, barba), barba, sinal e sardas. Cada um vai pela função do próprio jogo.
        yield return ApplyAppearanceParts(amm, infos).WrapToIl2Cpp();
        yield return WaitPartsSettled(amm, 4f).WrapToIl2Cpp();

        // Animações masculinas/femininas seguem o MotionType da aparência.
        var done = false;
        gc.GetPlayer().SwitchGenderMotion(MotionTypeOf(infos), (Action)(() => done = true));
        var t = 0f;
        while (!done && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }

        var vis = new SetResult();
        yield return WaitVisible(gc.GetPlayer().Model, 8f, vis).WrapToIl2Cpp();
        res.Ok = vis.Ok;
        if (!vis.Ok) res.Why = "modelo não reapareceu";
    }

    /// <summary>Anota as peças que o visual vestido agora (o de outro jogador, no meio da montagem) usa.</summary>
    private static void RememberResources(string appearance)
    {
        try
        {
            var set = new HashSet<string>();
            foreach (var n in AvatarMakeManager.Instance.GetEnableResourceNames()) if (!string.IsNullOrEmpty(n)) set.Add(n);
            RemoteResources[appearance] = set;
            ApplyLocks();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Appearance] peças do avatar: {e.Message}"); }
    }

    /// <summary>Põe as peças dos avatares dos outros (só de quem está no jogo) na lista de proteção do jogo.</summary>
    public static int ApplyLocks()
    {
        try
        {
            if (RemoteResources.Count == 0 || !AvatarMakeManager.IsInstance) return 0;
            var main = AvatarMakeManager.Instance.avatarResourceManager?.unloadBlocker?.MainAvatarResources;
            if (main == null) return 0;
            var live = new HashSet<string>(Known.Values);
            var added = 0;
            foreach (var kv in RemoteResources)
            {
                if (!live.Contains(kv.Key)) continue;
                foreach (var n in kv.Value) if (main.Add(n)) added++;
            }
            return added;
        }
        catch { return 0; }
    }

    /// <summary>Quando o jogo refaz a lista de proteção (do personagem local), as peças dos outros entram de novo.</summary>
    [HarmonyPatch(typeof(AvatarResourceManager), nameof(AvatarResourceManager.SetBlockMainAvatarResource))]
    private static class BlockPatch
    {
        private static void Postfix() => ApplyLocks();
    }

    // Categorias que a troca de roupa do jogo não aplica, na ordem certa: o rosto traz as decorações (sinal, sardas) e o
    // material dos olhos; a pele e as cores vão por cima dele (medido com o comando matsig, D78).
    private static readonly int[] FaceCategories = { 0 /*rosto*/, 1 /*pele*/, 2 /*cor dos olhos*/, 4 /*cor do cabelo*/,
        5 /*barba*/, 6 /*cor da barba*/, 7 /*sinal*/, 8 /*sardas*/ };

    /// <summary>Aplica rosto, pele, cores, barba, sinal e sardas no personagem local pelas funções internas do jogo.</summary>
    private static IEnumerator ApplyAppearanceParts(AvatarMakeManager amm, AvatarInfo[] infos)
    {
        foreach (var cat in FaceCategories)
        {
            AvatarInfo info = null;
            foreach (var i in infos) if ((int)i.Category == cat) { info = i; break; }
            if (info == null) continue;
            var done = false;
            Action ok = () => done = true;
            try
            {
                switch (cat)
                {
                    case 0: amm.SetFaceParts(info, ok, false); break;
                    case 1: amm.SetSkinParts(info, ok); break;
                    case 2: amm.SetEyeColor(info, ok); break;
                    case 4: amm.SetHairColor(info, ok); amm.SetEyebrowColor(info); break;
                    case 5: amm.SetBeardParts(info, ok); break;
                    case 6: amm.SetBeardColor(info, ok); break;
                    case 7: amm.SetMoleParts(info, ok); break;
                    case 8: amm.SetFrecklesParts(info, ok); break;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Appearance] categoria {cat}: {e.Message.Split((char)10)[0]}"); continue; }
            var t = 0f;
            while (!done && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
        }
    }

    /// <summary>Espera o contador de peças em aplicação do jogo zerar (e a fila de peças pendentes esvaziar).</summary>
    private static IEnumerator WaitPartsSettled(AvatarMakeManager amm, float timeout)
    {
        var t = 0f;
        var calm = 0f;
        while (t < timeout)
        {
            var busy = false;
            try { busy = amm.tryingSetAvatarPartsCount > 0 || (amm.reserveAvatarInfo != null && amm.reserveAvatarInfo.Count > 0); }
            catch { yield break; } // campo inacessível: segue como antes
            calm = busy ? 0f : calm + Time.unscaledDeltaTime;
            if (calm >= 0.25f) yield break;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        Plugin.Logger.LogWarning("[Appearance] o jogo não terminou de aplicar as peças no tempo limite");
    }

    /// <summary>Nomes de todas as texturas nos materiais do modelo (rosto, pele, roupa...): a "impressão digital" do visual.</summary>
    public static string TextureSignature(GameObject model)
    {
        var names = new List<string>();
        if (model == null) return "";
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                foreach (var prop in m.GetTexturePropertyNames())
                {
                    var tex = m.GetTexture(prop);
                    if (tex != null) names.Add(prop + "=" + tex.name);
                }
            }
        }
        names.Sort();
        return string.Join("|", names);
    }

    /// <summary>
    /// O jogo esconde o modelo enquanto carrega peças novas (~1–2 s). Espera ele voltar a aparecer (algum
    /// renderer ligado por 0,3 s seguidos); copiar antes disso gera um avatar invisível (D32).
    /// </summary>
    private static IEnumerator WaitVisible(GameObject model, float timeout, SetResult res)
    {
        var t = 0f;
        var stable = 0f;
        while (t < timeout)
        {
            if (VisibleRenderers(model) > 0) { stable += Time.unscaledDeltaTime; if (stable >= 0.3f) { res.Ok = true; yield break; } }
            else stable = 0f;
            t += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private static int VisibleRenderers(GameObject go)
    {
        if (go == null) return 0;
        var n = 0;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy) n++;
        return n;
    }

    /// <summary>Tenta a aparência; se falhar, tenta de novo com as roupas de <paramref name="dressFallback"/>.</summary>
    private static IEnumerator SetWithRetry(string appearance, string dressFallback, SetResult res)
    {
        yield return SetLocal(appearance, res).WrapToIl2Cpp();
        if (res.Ok) yield break;
        var retry = WithDressFrom(appearance, dressFallback);
        if (retry == appearance) yield break;
        Plugin.Logger.LogWarning($"[Appearance] {res.Why}; tentando de novo com as roupas do jogador local");
        res.Why = "";
        yield return SetLocal(retry, res).WrapToIl2Cpp();
    }

    private static IEnumerator Run(Job job)
    {
        _busy = true;
        var player = GameController.Instance.GetPlayer();
        var original = Local();
        var res = new SetResult();

        if (job.Appearance == null)
        {
            // Aplicar no jogador local (definitivo).
            var target = Sanitize(job.Target, original);
            if (target != original)
            {
                yield return SetWithRetry(target, original, res).WrapToIl2Cpp();
                if (res.Ok) Plugin.Logger.LogInfo("[Appearance] aparência própria aplicada no personagem local");
                else
                {
                    Plugin.Logger.LogWarning($"[Appearance] não deu para aplicar a aparência própria ({res.Why}); voltando à anterior");
                    yield return SetLocal(original, new SetResult()).WrapToIl2Cpp();
                }
            }
            _busy = false;
            Announce();
            yield break;
        }

        var app = Sanitize(job.Appearance, original);
        if (app == original)
        {
            // Mesma aparência do jogador local: basta copiar.
            Finish(job, player, null);
            _busy = false;
            yield break;
        }

        var signature = TextureSignature(player.Model);
        var localAssets = AssetIds(player.Model);
        yield return SetWithRetry(app, original, res).WrapToIl2Cpp();
        if (GameController.Instance.GetPlayer() == null || GameController.Instance.GetPlayer().Pointer != player.Pointer)
        {
            // O mundo recarregou no meio da montagem: o personagem antigo não existe mais.
            Plugin.Logger.LogWarning("[Appearance] mundo recarregou durante a montagem; descartada");
            job.Done?.Invoke(null, null);
            _busy = false;
            yield break;
        }
        if (res.Ok) { RememberResources(job.Appearance); Finish(job, player, localAssets); }
        else
        {
            Plugin.Logger.LogWarning($"[Appearance] não deu para montar a aparência de outro jogador ({res.Why}); fica o modelo provisório");
            job.Done?.Invoke(null, null);
        }
        yield return SetLocal(original, new SetResult()).WrapToIl2Cpp();
        // D76: confere se o visual local voltou de verdade (texturas iguais às de antes); se não, devolve de novo.
        for (var attempt = 0; attempt < 2 && GameController.Instance.GetPlayer() != null; attempt++)
        {
            var now = TextureSignature(GameController.Instance.GetPlayer().Model);
            if (now == signature) break;
            Plugin.Logger.LogWarning($"[Appearance] o visual local não voltou igual depois de montar outro jogador (tentativa {attempt + 1}); aplicando de novo");
            for (var w = 0f; w < 0.5f; w += Time.unscaledDeltaTime) yield return null;
            yield return SetLocal(original, new SetResult()).WrapToIl2Cpp();
        }
        _busy = false;
    }

    private static void Finish(Job job, PlayerCharacter player, HashSet<int> localAssets)
    {
        var model = player.Model;
        var clone = Instantiate(model);
        OwnMaterials(clone);
        if (localAssets != null) DetachRemoteAssets(clone, localAssets);
        var anim = model.GetComponentInChildren<Animator>();
        job.Done?.Invoke(clone, anim != null ? anim.runtimeAnimatorController : null);
    }

    // Cópias (materiais, malhas, texturas) criadas para cada avatar: não somem com o GameObject; liberadas em Release.
    private static readonly Dictionary<IntPtr, List<UnityEngine.Object>> Owned = new();

    private static void Own(GameObject clone, UnityEngine.Object obj)
    {
        if (!Owned.TryGetValue(clone.Pointer, out var list)) Owned[clone.Pointer] = list = new List<UnityEngine.Object>();
        list.Add(obj);
    }

    /// <summary>Destrói o avatar e as cópias próprias dele (sem isso, cada remontagem deixava texturas na memória).</summary>
    public static void Release(GameObject clone)
    {
        if (clone == null) return;
        if (Owned.TryGetValue(clone.Pointer, out var list))
        {
            foreach (var o in list) if (o != null) Destroy(o);
            Owned.Remove(clone.Pointer);
        }
        Destroy(clone);
    }

    /// <summary>Ids das malhas e texturas que o modelo usa.</summary>
    private static HashSet<int> AssetIds(GameObject model)
    {
        var ids = new HashSet<int>();
        if (model == null) return ids;
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var smr = r.TryCast<SkinnedMeshRenderer>();
            var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh != null) ids.Add(mesh.GetInstanceID());
            foreach (var m in r.sharedMaterials)
            {
                if (m == null) continue;
                foreach (var p in m.GetTexturePropertyNames()) { var t = m.GetTexture(p); if (t != null) ids.Add(t.GetInstanceID()); }
            }
        }
        return ids;
    }

    /// <summary>
    /// Peças que são SÓ do outro jogador (o personagem local não usa) somem quando o jogo as descarrega: o jogo solta do
    /// cache o rosto/pele/roupa do outro assim que o visual local volta e, na troca de mapa, descarrega de fato — a lista de
    /// proteção (D77) não segura o que não está no cache (D78). A cópia ganha malhas e texturas próprias dessas peças;
    /// cópias feitas em tempo de jogo não pertencem a nenhum pacote do jogo e não são descarregadas.
    /// </summary>
    private static void DetachRemoteAssets(GameObject clone, HashSet<int> localAssets)
    {
        var meshCopies = new Dictionary<int, Mesh>();
        var texCopies = new Dictionary<int, Texture>();
        int meshes = 0, textures = 0;
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
        {
            var smr = r.TryCast<SkinnedMeshRenderer>();
            var mf = smr == null ? r.GetComponent<MeshFilter>() : null;
            var mesh = smr != null ? smr.sharedMesh : mf?.sharedMesh;
            if (mesh != null && !localAssets.Contains(mesh.GetInstanceID()))
            {
                if (!meshCopies.TryGetValue(mesh.GetInstanceID(), out var copy))
                {
                    copy = Instantiate(mesh);
                    copy.name = mesh.name;
                    Own(clone, copy);
                    meshCopies[mesh.GetInstanceID()] = copy;
                    meshes++;
                }
                if (smr != null) smr.sharedMesh = copy; else mf.sharedMesh = copy;
            }
            foreach (var m in r.sharedMaterials) // já são materiais próprios (OwnMaterials)
            {
                if (m == null) continue;
                foreach (var p in m.GetTexturePropertyNames())
                {
                    var t = m.GetTexture(p);
                    if (t == null || localAssets.Contains(t.GetInstanceID())) continue;
                    if (!texCopies.TryGetValue(t.GetInstanceID(), out var tcopy))
                    {
                        tcopy = Instantiate(t);
                        tcopy.name = t.name;
                        Own(clone, tcopy);
                        texCopies[t.GetInstanceID()] = tcopy;
                        textures++;
                    }
                    m.SetTexture(p, tcopy);
                }
            }
        }
        if (meshes + textures > 0) Plugin.Logger.LogInfo($"[Appearance] avatar com peças próprias: {meshes} malha(s), {textures} textura(s) que o personagem local não usa");
    }

    /// <summary>
    /// A cópia nasce usando OS MESMOS materiais do personagem local; quando o visual local volta, as texturas desses materiais
    /// (rosto, pele) trocam e a cópia trocava junto: o avatar do outro ficava com o rosto/pele de quem olha (D78).
    /// Cada renderer da cópia ganha materiais próprios, com as texturas do momento da cópia.
    /// </summary>
    public static void OwnMaterials(GameObject clone)
    {
        foreach (var r in clone.GetComponentsInChildren<Renderer>(true))
        {
            var shared = r.sharedMaterials;
            if (shared == null || shared.Length == 0) continue;
            var own = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Material>(shared.Length);
            for (var i = 0; i < shared.Length; i++)
            {
                own[i] = shared[i] != null ? new Material(shared[i]) : null;
                if (own[i] != null) Own(clone, own[i]);
            }
            r.sharedMaterials = own;
        }
    }

    public static object Status() => new { known = Known, queued = Jobs.Count, busy = _busy, local = Ready() ? Local() : null,
        lockedSets = RemoteResources.Count, lockedNames = RemoteResources.Values.Sum(v => v.Count) };

    public static List<string> LockedList()
    {
        var all = new HashSet<string>();
        foreach (var set in RemoteResources.Values) foreach (var n in set) all.Add(n);
        var list = new List<string>(all);
        list.Sort();
        return list;
    }
}
