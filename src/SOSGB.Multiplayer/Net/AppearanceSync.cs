// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d28.
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
/// Aparência própria de cada jogador (D28).
///
/// Aparência = AvatarInfos do jogador (rosto, pele, olhos, cabelo, cor, barba, roupa, MotionType=gênero…)
/// serializadas como "categoria:masterId,…". Montar um modelo com outra aparência só é possível pelo
/// AvatarMakeManager, que trabalha sobre o modelo do JOGADOR LOCAL. Truque (validado em runtime):
///   troca temporariamente a aparência do jogador local → espera o jogo remontar → copia o modelo →
///   devolve a aparência original. A cópia continua válida depois da devolução.
/// As montagens são feitas uma de cada vez (fila), acompanhando a corrotina do jogo até o fim.
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

    private sealed class Job
    {
        public string Appearance;      // null = aplicar no jogador local (Target)
        public string Target;          // aparência para o jogador local (quando Appearance == null)
        public Action<GameObject, RuntimeAnimatorController> Done;
    }

    /// <summary>A última montagem passou do tempo limite (peças que não carregaram).</summary>
    public static bool TimedOut;

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
        if (!Plugin.SyncAppearance.Value) { Jobs.Clear(); return; } // experimental, desligado por padrão
        if (_busy || Jobs.Count == 0) return;
        if (!Ready()) return;
        StartCoroutine(Run(Jobs.Dequeue()).WrapToIl2Cpp());
    }

    private static bool Ready() =>
        SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game
        && AvatarMakeManager.IsInstance && GameController.IsInstance && GameController.Instance.GetPlayer() != null
        && !DaySync.InDayChange;

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

    /// <summary>
    /// Coloca a aparência direto no UserInfo do save recebido, para o jogo montar o personagem já com ela quando
    /// a cena carregar (sem troca visível depois). O GÊNERO do corpo é mantido o do save (o do host): trocar o
    /// gênero ali trava o carregamento (testado: personagem nulo, relógio parado). Limitação conhecida (D28).
    /// </summary>
    public static void ApplyToUserInfo(string appearance)
    {
        if (string.IsNullOrEmpty(appearance)) return;
        var ui = ExternalData.Instance.UserInfo;
        var saveMotion = MotionTypeId(string.Join(",", ui.AvatarInfos.Select(i => $"{(int)i.Category}:{i.MasterId}")));
        var adjusted = WithMotionType(appearance, saveMotion);
        ui.AvatarInfos = Parse(adjusted);
        Plugin.Logger.LogInfo($"[Appearance] aparência própria aplicada no save recebido{(adjusted != appearance ? " (gênero do mundo do host mantido)" : "")}");
    }

    /// <summary>Pré-carrega as peças da aparência atual do UserInfo (necessário antes de montar a cena com ela).</summary>
    public static IEnumerator PreloadUserInfoAppearance()
    {
        var pre = AvatarMakeManager.Instance.PreloadAvatarSource(ExternalData.Instance.UserInfo.AvatarInfos);
        var aw = pre.GetAwaiter();
        var waited = 0f;
        while (!aw.IsCompleted && waited < 15f) { waited += Time.unscaledDeltaTime; yield return null; }
        Plugin.Logger.LogInfo($"[Appearance] peças da aparência pré-carregadas ({waited:0.0}s)");
    }

    /// <summary>Troca a peça MotionType de uma aparência pela de outra (fallback de gênero).</summary>
    public static string WithMotionType(string appearance, string motionPart)
    {
        if (motionPart == null) return appearance;
        var key = ((int)AvatarPartsCategory.MotionType) + ":";
        return string.Join(",", appearance.Split(',').Select(p => p.StartsWith(key) ? motionPart : p));
    }

    /// <summary>ID da peça MotionType (gênero das animações) dentro de uma aparência serializada.</summary>
    public static string MotionTypeId(string appearance)
    {
        if (string.IsNullOrEmpty(appearance)) return null;
        var key = ((int)AvatarPartsCategory.MotionType) + ":";
        foreach (var part in appearance.Split(',')) if (part.StartsWith(key)) return part;
        return null;
    }

    public static string KnownAppearance(byte id) => Known.TryGetValue(id, out var a) ? a : null;

    /// <summary>Pede a montagem de um modelo com a aparência dada (resultado no callback).</summary>
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

    private static int MotionTypeOf(AvatarInfo[] infos)
    {
        foreach (var i in infos)
            if (i.Category == AvatarPartsCategory.MotionType)
                try { return i.MotionCategory; } catch { return 0; }
        return 0;
    }

    /// <summary>Troca a aparência do jogador local e espera o jogo terminar de remontar.</summary>
    private static IEnumerator SetLocal(AvatarInfo[] infos)
    {
        var amm = AvatarMakeManager.Instance;
        var gc = GameController.Instance;
        amm.PlayerAvatarInfos = infos;
        gc.PlayerAvatarInfos = infos;
        ExternalData.Instance.UserInfo.AvatarInfos = infos;
        // Peças de outra roupa/cabelo podem não estar carregadas (só as do save e as padrão estão).
        // O carregador da tela de customização (DetailPageResourceLoad) carrega peças por masterId.
        var ids = new Il2CppSystem.Collections.Generic.List<uint>();
        foreach (var i in infos) if (i.MasterId != 0) ids.Add(i.MasterId);
        amm.DetailPageResourceLoad(ids);
        var waited = 0f;
        yield return null;
        while (amm.IsLoadingDetailPage && waited < 15f) { waited += Time.unscaledDeltaTime; yield return null; }
        Plugin.Logger.LogInfo($"[Appearance] peças carregadas ({waited:0.0}s)");
        // Avançamos a corrotina do jogo por aqui para saber exatamente quando terminou.
        var ie = amm.ChangePlayerAvatar(AvatarMakeManager.ChangeType.Default, null);
        var t0 = Time.realtimeSinceStartup;
        while (ie.MoveNext())
        {
            if (Time.realtimeSinceStartup - t0 > 20f) { Plugin.Logger.LogWarning("[Appearance] montagem demorou demais; abortando"); TimedOut = true; break; }
            yield return ie.Current;
        }
        // Animações masculinas/femininas seguem o MotionType da aparência.
        var done = false;
        gc.GetPlayer().SwitchGenderMotion(MotionTypeOf(infos), (Action)(() => done = true));
        var t = 0f;
        while (!done && t < 3f) { t += Time.unscaledDeltaTime; yield return null; }
        yield return null;
    }

    private static IEnumerator Run(Job job)
    {
        _busy = true;
        var player = GameController.Instance.GetPlayer();
        var original = Local();

        if (job.Appearance == null)
        {
            // Aplicar no jogador local (definitivo). Mesmo gênero obrigatório (ver fallback abaixo).
            var target = MotionTypeId(job.Target) == MotionTypeId(original) ? job.Target : WithMotionType(job.Target, MotionTypeId(original));
            if (target == original) { _busy = false; Announce(); yield break; }
            yield return SetLocal(Parse(target)).WrapToIl2Cpp();
            Plugin.Logger.LogInfo("[Appearance] aparência própria aplicada no personagem local");
            _busy = false;
            Announce();
            yield break;
        }

        // Corpo de outro gênero não pode ser montado sem recarregar a cena (vira um modelo invisível).
        // Fallback: a aparência da pessoa (cabelo, rosto, cores, roupa) sobre o corpo do gênero local.
        if (MotionTypeId(job.Appearance) != MotionTypeId(original))
        {
            Plugin.Logger.LogInfo("[Appearance] gênero diferente do local: usando o corpo do gênero local (limitação conhecida)");
            job.Appearance = WithMotionType(job.Appearance, MotionTypeId(original));
        }

        if (job.Appearance == original)
        {
            // Mesma aparência do jogador local: basta copiar.
            Finish(job, player);
            _busy = false;
            yield break;
        }

        yield return SetLocal(Parse(job.Appearance)).WrapToIl2Cpp();
        Finish(job, player);
        yield return SetLocal(Parse(original)).WrapToIl2Cpp();
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
