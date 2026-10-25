// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d28 e #d32.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BokuMono;
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
        && !GameController.Instance.IsPlayingEvent && !DaySync.InDayChange && HasRealController(GameController.Instance.GetPlayer());

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
            Finish(job, player);
            _busy = false;
            yield break;
        }

        yield return SetWithRetry(app, original, res).WrapToIl2Cpp();
        if (GameController.Instance.GetPlayer() == null || GameController.Instance.GetPlayer().Pointer != player.Pointer)
        {
            // O mundo recarregou no meio da montagem: o personagem antigo não existe mais.
            Plugin.Logger.LogWarning("[Appearance] mundo recarregou durante a montagem; descartada");
            job.Done?.Invoke(null, null);
            _busy = false;
            yield break;
        }
        if (res.Ok) Finish(job, player);
        else
        {
            Plugin.Logger.LogWarning($"[Appearance] não deu para montar a aparência de outro jogador ({res.Why}); fica o modelo provisório");
            job.Done?.Invoke(null, null);
        }
        yield return SetLocal(original, new SetResult()).WrapToIl2Cpp();
        _busy = false;
    }

    private static void Finish(Job job, PlayerCharacter player)
    {
        var model = player.Model;
        var clone = Instantiate(model);
        var anim = model.GetComponentInChildren<Animator>();
        job.Done?.Invoke(clone, anim != null ? anim.runtimeAnimatorController : null);
    }

    public static object Status() => new { known = Known, queued = Jobs.Count, busy = _busy, local = Ready() ? Local() : null };
}
