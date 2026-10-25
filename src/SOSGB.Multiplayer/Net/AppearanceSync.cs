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
        // Avançamos a corrotina do jogo por aqui para saber exatamente quando terminou.
        var ie = amm.ChangePlayerAvatar(AvatarMakeManager.ChangeType.Default, null);
        var guard = 0;
        while (ie.MoveNext() && guard++ < 2000) yield return ie.Current;
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
            // Aplicar no jogador local (definitivo).
            yield return SetLocal(Parse(job.Target)).WrapToIl2Cpp();
            Plugin.Logger.LogInfo("[Appearance] aparência própria aplicada no personagem local");
            _busy = false;
            Announce();
            yield break;
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
