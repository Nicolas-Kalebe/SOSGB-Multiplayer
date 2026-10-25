// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e docs/PROTOCOLO.md (PlayerState).
using System;
using System.Collections.Generic;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnhollowedRenderers = Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<UnityEngine.Renderer>;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Tarefas 2.3–2.5: ver os outros jogadores andando.
///  - Envia o estado do jogador local 10×/s: mapa, posição, rotação e o estado do Animator
///    (fullPathHash + normalizedTime). Sincronizar o hash do estado replica qualquer animação
///    sem precisar conhecer os parâmetros do Animator do jogo.
///  - Para cada jogador remoto, mantém um "avatar": um clone visual do modelo do player local
///    (PlayerCharacter.Model) sem os scripts do jogo. Só aparece se estiver no mesmo mapa.
/// </summary>
public class PlayerSync : MonoBehaviour
{
    public PlayerSync(IntPtr ptr) : base(ptr) { }

    private const float SendInterval = 0.1f;
    private float _nextSend;

    private sealed class Remote
    {
        public GameObject Avatar;
        public Animator Animator;
        public uint Field;
        public Vector3 Target;
        public float TargetRotY;
        public int AnimHash;
        public RuntimeAnimatorController Controller; // controller capturado na montagem (gênero certo)
        public string BuiltAppearance;               // aparência com que o avatar atual foi montado
        public bool Building;
        public int Gen;                              // muda quando o mundo recarrega (descarta montagens antigas)
        public int[] Hashes = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
        public bool HasState;
    }

    private static readonly Dictionary<byte, Remote> Remotes = new();
    private static IntPtr _localPlayerPtr;
    private static bool _subscribed;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id => DestroyRemote(id);
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline)
            {
                if (Remotes.Count > 0) foreach (var id in new List<byte>(Remotes.Keys)) DestroyRemote(id);
                return;
            }
            RebuildAvatarsIfWorldReloaded();
            SendLocalState();
            UpdateRemotes();
            if (Time.unscaledTime >= _nextControllerCheck) _nextControllerCheck = Time.unscaledTime + 1f;
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[PlayerSync] {e.Message}");
        }
    }

    /// <summary>
    /// Bug "o convidado não via o host": o avatar copiado antes da recarga do mundo (WorldData) continua
    /// existindo, mas os modelos/materiais em que ele se apoiava são liberados pelo jogo na recarga,
    /// e ele deixa de ser desenhado. Quando o personagem local muda (recarga), recriamos os avatares
    /// a partir do modelo novo.
    /// </summary>
    private static void RebuildAvatarsIfWorldReloaded()
    {
        if (!GameController.IsInstance) return;
        var p = GameController.Instance.GetPlayer();
        if (p == null) return;
        if (p.Pointer == _localPlayerPtr) return;
        var first = _localPlayerPtr == IntPtr.Zero;
        _localPlayerPtr = p.Pointer;
        if (first) return;
        foreach (var rm in Remotes.Values)
        {
            if (rm.Avatar != null) UnityEngine.Object.Destroy(rm.Avatar);
            rm.Avatar = null;
            rm.Animator = null;
            rm.Controller = null;
            rm.BuiltAppearance = null;
            rm.Building = false;
            rm.Gen++;
            rm.Hashes = Array.Empty<int>();
        }
        Plugin.Logger.LogInfo("[PlayerSync] mundo recarregado: avatares serão recriados");
    }

    // ---------- envio ----------

    private void SendLocalState()
    {
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        if (!GameController.IsInstance || !FieldManager.IsInstance) return;
        var player = GameController.Instance.GetPlayer();
        if (player == null) return;

        // O jogo move o MODELO (PlayerCharacter.Model / GetPlayerTransform), não a raiz do
        // PlayerCharacter, que fica parada na origem. Usamos o transform que de fato anda.
        var t = GameController.Instance.GetPlayerTransform();
        if (t == null) return;
        var field = FieldManager.Instance.CurrentFieldId;
        // O jogo anima em VÁRIAS camadas: andar/correr/pular ficam na camada 1+ (BaseAnimationObject usa
        // layerIndex = 1 por padrão). Antes só a camada 0 ("parado") era enviada e o avatar deslizava sem
        // animação. Agora vão todas: peso + estado + tempo.
        var anim = player.Model != null ? player.Model.GetComponentInChildren<Animator>() : null;
        var layers = anim != null ? Math.Min(anim.layerCount, 16) : 0; // o player tem 12 camadas
        var hashes = new int[layers];
        var norms = new float[layers];
        var weights = new float[layers];
        for (var i = 0; i < layers; i++)
        {
            var st = anim.GetCurrentAnimatorStateInfo(i);
            hashes[i] = st.fullPathHash;
            norms[i] = st.normalizedTime;
            weights[i] = anim.GetLayerWeight(i);
        }
        // Parâmetros do Animator: a "Move Layer" é um blend tree (parado/andar/correr) controlado por
        // parâmetros; o estado (hash) não muda ao andar, só os parâmetros. Triggers ficam de fora.
        var paramHashes = new List<int>();
        var paramTypes = new List<byte>();
        var paramValues = new List<float>();
        if (anim != null)
        {
            foreach (var p in anim.parameters)
            {
                switch (p.type)
                {
                    case AnimatorControllerParameterType.Float:
                        paramHashes.Add(p.nameHash); paramTypes.Add(0); paramValues.Add(anim.GetFloat(p.nameHash)); break;
                    case AnimatorControllerParameterType.Int:
                        paramHashes.Add(p.nameHash); paramTypes.Add(1); paramValues.Add(anim.GetInteger(p.nameHash)); break;
                    case AnimatorControllerParameterType.Bool:
                        paramHashes.Add(p.nameHash); paramTypes.Add(2); paramValues.Add(anim.GetBool(p.nameHash) ? 1 : 0); break;
                }
            }
        }

        var id = NetSession.LocalId;
        var pos = t.position;
        var rotY = t.eulerAngles.y;
        NetSession.SendGame(MsgType.PlayerState, w =>
        {
            w.Put(id);
            w.Put(field);
            w.Put(pos.x); w.Put(pos.y); w.Put(pos.z);
            w.Put(rotY);
            w.Put((byte)layers);
            for (var i = 0; i < layers; i++) { w.Put(hashes[i]); w.Put(norms[i]); w.Put(weights[i]); }
            w.Put((byte)Math.Min(paramHashes.Count, 255));
            for (var i = 0; i < Math.Min(paramHashes.Count, 255); i++) { w.Put(paramHashes[i]); w.Put(paramTypes[i]); w.Put(paramValues[i]); }
        }, DeliveryMethod.Sequenced);
    }

    // ---------- recepção ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.PlayerState) return;
        var id = r.GetByte();
        if (id == NetSession.LocalId || id == NetSession.UnknownId) return;
        if (NetSession.Current == NetSession.Mode.Host && id != from) return; // id tem que ser do remetente
        if (!NetSession.Players.ContainsKey(id)) return;                        // jogador que não está na sessão
        if (!Remotes.TryGetValue(id, out var rm)) Remotes[id] = rm = new Remote();
        rm.Field = r.GetUInt();
        rm.Target = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
        rm.TargetRotY = r.GetFloat();
        var layers = r.GetByte();
        if (rm.Hashes.Length != layers) { rm.Hashes = new int[layers]; rm.Weights = new float[layers]; }
        for (var i = 0; i < layers; i++)
        {
            var hash = r.GetInt();
            var norm = r.GetFloat();
            var weight = r.GetFloat();
            rm.Weights[i] = weight;
            if (rm.Animator != null && i < rm.Animator.layerCount)
            {
                rm.Animator.SetLayerWeight(i, weight);
                if (hash != 0 && hash != rm.Hashes[i]) rm.Animator.Play(hash, i, norm % 1f);
            }
            rm.Hashes[i] = hash;
        }
        var pc = r.GetByte();
        for (var i = 0; i < pc; i++)
        {
            var ph = r.GetInt();
            var pt = r.GetByte();
            var pv = r.GetFloat();
            if (rm.Animator == null) continue;
            if (pt == 0) rm.Animator.SetFloat(ph, pv);
            else if (pt == 1) rm.Animator.SetInteger(ph, (int)pv);
            else rm.Animator.SetBool(ph, pv > 0.5f);
        }
        rm.AnimHash = layers > 0 ? rm.Hashes[0] : 0;
        rm.HasState = true;
    }

    private void UpdateRemotes()
    {
        var localField = FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0u;
        foreach (var kv in Remotes)
        {
            var rm = kv.Value;
            if (!rm.HasState) continue;
            if (rm.Avatar == null) CreateAvatar(kv.Key, rm);
            if (rm.Avatar == null) continue;
            EnsureAppearance(kv.Key, rm);
            SyncController(rm);

            var visible = rm.Field == localField; // 2.5: só no mesmo mapa
            if (rm.Avatar.activeSelf != visible) rm.Avatar.SetActive(visible);
            if (!visible) continue;

            var tr = rm.Avatar.transform;
            // Interpolação simples: suaviza os 10 pacotes/s em movimento contínuo.
            if ((tr.position - rm.Target).sqrMagnitude > 25f) tr.position = rm.Target; // teleporte (>5 m)
            else tr.position = Vector3.Lerp(tr.position, rm.Target, Time.deltaTime * 12f);
            var rot = Quaternion.Euler(0, rm.TargetRotY, 0);
            tr.rotation = Quaternion.Slerp(tr.rotation, rot, Time.deltaTime * 12f);
        }
    }

    private float _nextControllerCheck;

    /// <summary>
    /// O player usa um AnimatorOverrideController carregado DEPOIS de o modelo existir (troca os clips
    /// "dummy_*" do controller base pelas animações reais). Se o clone pegar o controller base, ele anima
    /// com os dummies (pose torta, "afundado"). Mantemos o controller do clone igual ao do player local.
    /// </summary>
    private void SyncController(Remote rm)
    {
        if (rm.Animator == null || Time.unscaledTime < _nextControllerCheck) return;
        if (AppearanceSync.Swapping) return; // o jogador local está com aparência temporária
        RuntimeAnimatorController want = rm.Controller;
        if (want == null)
        {
            var local = GameController.Instance.GetPlayer();
            var la = local != null && local.Model != null ? local.Model.GetComponentInChildren<Animator>() : null;
            if (la == null || la.runtimeAnimatorController == null) return;
            want = la.runtimeAnimatorController;
        }
        var cur = rm.Animator.runtimeAnimatorController;
        if (cur != null && cur.Pointer == want.Pointer) return;
        rm.Animator.runtimeAnimatorController = want;
        rm.Hashes = Array.Empty<int>(); // força replay dos estados no controller novo
        Plugin.Logger.LogInfo("[PlayerSync] controller de animação do avatar atualizado (override real)");
    }

    private static void CreateAvatar(byte id, Remote rm)
    {
        if (!GameController.IsInstance) return;
        var local = GameController.Instance.GetPlayer();
        if (local == null || local.Model == null) return;

        // Avatar provisório: cópia do modelo local, até a aparência do jogador chegar e ser montada (D28).
        var clone = UnityEngine.Object.Instantiate(local.Model);
        PrepareClone(clone, id);
        rm.Avatar = clone;
        rm.Animator = clone.GetComponentInChildren<Animator>();
        var localAnim = local.Model.GetComponentInChildren<Animator>();
        if (rm.Animator != null && localAnim != null)
        {
            // O controller do player é carregado em tempo de execução (override); garante o mesmo no clone.
            if (rm.Animator.runtimeAnimatorController == null) rm.Animator.runtimeAnimatorController = localAnim.runtimeAnimatorController;
            rm.Animator.enabled = true;
        }
        clone.transform.position = rm.Target;
        Plugin.Logger.LogInfo($"[PlayerSync] avatar do jogador #{id} criado");
        DevBridge.PushEvent($"avatar:{id}:criado");
    }

    /// <summary>Clone só visual: sem scripts nem colisores do jogo (posição/animação vêm da rede).</summary>
    private static void PrepareClone(GameObject clone, byte id)
    {
        clone.name = $"SOSGB_RemotePlayer_{id}";
        foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.Destroy(mb);
        foreach (var col in clone.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(col);
    }

    /// <summary>A aparência do jogador mudou/chegou: o avatar será remontado.</summary>
    public static void InvalidateAvatar(byte id)
    {
        if (Remotes.TryGetValue(id, out var rm)) rm.BuiltAppearance = null;
    }

    /// <summary>Se a aparência do jogador é conhecida e o avatar não foi montado com ela, pede a montagem.</summary>
    private static void EnsureAppearance(byte id, Remote rm)
    {
        if (!Plugin.OwnAppearance.Value || rm.Building || rm.Avatar == null) return;
        var app = AppearanceSync.KnownAppearance(id);
        if (app == null || app == rm.BuiltAppearance) return;
        rm.Building = true;
        var gen = rm.Gen;
        AppearanceSync.Build(app, (model, controller) =>
        {
            if (gen != rm.Gen) { if (model != null) UnityEngine.Object.Destroy(model); return; } // de antes de recarregar
            rm.Building = false;
            if (model == null) { rm.BuiltAppearance = app; return; } // não deu para montar: fica o provisório (sem tentar de novo)
            if (!Remotes.ContainsKey(id)) { UnityEngine.Object.Destroy(model); return; }
            PrepareClone(model, id);
            if (rm.Avatar != null)
            {
                model.transform.position = rm.Avatar.transform.position;
                model.transform.rotation = rm.Avatar.transform.rotation;
                model.SetActive(rm.Avatar.activeSelf);
                UnityEngine.Object.Destroy(rm.Avatar);
            }
            rm.Avatar = model;
            rm.Animator = model.GetComponentInChildren<Animator>();
            // Mesmo gênero (MotionType) do jogador local: usa o controller dele (SyncController mantém em dia).
            // Gênero diferente: cópia própria do controller capturado na montagem; a referência original não é
            // estável (o jogo cria outro controller ao devolver a aparência local).
            var sameMotion = AppearanceSync.MotionTypeId(app) == AppearanceSync.MotionTypeId(AppearanceSync.Stable ?? AppearanceSync.Local());
            rm.Controller = !sameMotion && controller != null ? UnityEngine.Object.Instantiate(controller) : null;
            if (rm.Animator != null && rm.Controller != null)
            {
                rm.Animator.runtimeAnimatorController = rm.Controller;
                rm.Animator.Rebind(); // sem isto o clone pode continuar nas animações do outro gênero
            }
            rm.Hashes = Array.Empty<int>();
            rm.BuiltAppearance = app;
            Plugin.Logger.LogInfo($"[PlayerSync] avatar do jogador #{id} montado com a aparência dele");
            DevBridge.PushEvent($"avatar:{id}:aparencia");
        });
    }

    private static void DestroyRemote(byte id)
    {
        if (!Remotes.TryGetValue(id, out var rm)) return;
        if (rm.Avatar != null) UnityEngine.Object.Destroy(rm.Avatar);
        Remotes.Remove(id);
    }

    // Diagnóstico do clone (por que não aparece?): renderers, camadas, escala.
    private static object Diag(GameObject a)
    {
        var rs = a.GetComponentsInChildren<Renderer>(true);
        int enabled = 0, visible = 0;
        foreach (var r in rs) { if (r.enabled && r.gameObject.activeInHierarchy) enabled++; if (r.isVisible) visible++; }
        var local = GameController.Instance.GetPlayer().Model;
        var lrs = local.GetComponentsInChildren<Renderer>(true);
        return new
        {
            active = a.activeInHierarchy, layer = a.layer, localLayer = local.layer,
            scale = new[] { a.transform.lossyScale.x, a.transform.lossyScale.y, a.transform.lossyScale.z },
            localScale = new[] { local.transform.lossyScale.x, local.transform.lossyScale.y, local.transform.lossyScale.z },
            renderers = rs.Length, enabled, visible, localRenderers = lrs.Length,
            children = a.transform.childCount,
            cloneBounds = Center(rs),
            localModelPos = V(local.transform.position),
            localBounds = Center(lrs),
            enabledNames = EnabledNames(rs),
            localEnabledNames = EnabledNames(lrs),
        };
    }

    private static float[] V(Vector3 v) => new[] { (float)Math.Round(v.x, 2), (float)Math.Round(v.y, 2), (float)Math.Round(v.z, 2) };

    private static float[] Center(UnhollowedRenderers rs)
    {
        foreach (var r in rs)
            if (r.enabled && r.gameObject.activeInHierarchy) return V(r.bounds.center);
        return null;
    }

    private static string[] EnabledNames(UnhollowedRenderers rs)
    {
        var l = new List<string>();
        foreach (var r in rs) if (r.enabled && r.gameObject.activeInHierarchy) l.Add($"{r.name}:{r.GetIl2CppType().Name}:{r.isVisible}");
        return l.ToArray();
    }

    /// <summary>Ponte de teste: camadas do Animator do jogador local e dos avatares (diagnóstico de animação).</summary>
    public static object AnimDiag()
    {
        object Describe(Animator a)
        {
            if (a == null) return null;
            var layers = new List<object>();
            for (var i = 0; i < a.layerCount; i++)
            {
                var st = a.GetCurrentAnimatorStateInfo(i);
                var clips = a.GetCurrentAnimatorClipInfo(i);
                layers.Add(new { i, name = a.GetLayerName(i), weight = a.GetLayerWeight(i), hash = st.fullPathHash, norm = (float)Math.Round(st.normalizedTime, 2), clip = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : null });
            }
            var ps = new Dictionary<string, object>();
            foreach (var p in a.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Float) ps[p.name] = (float)Math.Round(a.GetFloat(p.nameHash), 3);
                else if (p.type == AnimatorControllerParameterType.Int) ps[p.name] = a.GetInteger(p.nameHash);
                else if (p.type == AnimatorControllerParameterType.Bool) ps[p.name] = a.GetBool(p.nameHash);
                else ps[p.name] = "trigger";
            }
            return new { controller = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : null, enabled = a.enabled, layers, parameters = ps };
        }
        var local = GameController.IsInstance ? GameController.Instance.GetPlayer() : null;
        var remotes = new Dictionary<string, object>();
        foreach (var kv in Remotes) remotes[kv.Key.ToString()] = Describe(kv.Value.Animator);
        return new
        {
            localLayerIndex = local != null ? local.CurrentLayerIndex : -1,
            local = local != null && local.Model != null ? Describe(local.Model.GetComponentInChildren<Animator>()) : null,
            remotes,
        };
    }

    /// <summary>
    /// Posição na tela (pixels, origem no canto superior esquerdo, como no IMGUI) da cabeça de cada avatar
    /// visível, para desenhar o nome acima (MultiplayerUI). Enquanto a aparência própria de cada jogador
    /// não existe (todos usam o modelo do jogador local), o nome é o que diferencia quem é quem.
    /// </summary>
    public static List<(byte id, Vector2 screen)> VisibleHeads()
    {
        var list = new List<(byte, Vector2)>();
        var cam = Camera.main;
        if (cam == null) return list;
        foreach (var kv in Remotes)
        {
            var a = kv.Value.Avatar;
            if (a == null || !a.activeSelf) continue;
            var head = a.transform.position + Vector3.up * 1.75f;
            var sp = cam.WorldToScreenPoint(head);
            if (sp.z <= 0) continue; // atrás da câmera
            list.Add((kv.Key, new Vector2(sp.x, Screen.height - sp.y)));
        }
        return list;
    }

    /// <summary>Mapa atual de um jogador remoto (0 = desconhecido). Usado pela lista de jogadores.</summary>
    public static uint RemoteField(byte id) => Remotes.TryGetValue(id, out var rm) && rm.HasState ? rm.Field : 0;

    /// <summary>Para a ponte de teste: posição dos avatares remotos.</summary>
    public static object Status()
    {
        var list = new List<object>();
        foreach (var kv in Remotes)
        {
            var a = kv.Value.Avatar;
            list.Add(new
            {
                id = kv.Key,
                field = kv.Value.Field,
                target = new[] { kv.Value.Target.x, kv.Value.Target.y, kv.Value.Target.z },
                avatar = a == null ? null : new[] { a.transform.position.x, a.transform.position.y, a.transform.position.z },
                visible = a != null && a.activeSelf,
                diag = a == null ? null : Diag(a),
                anim = kv.Value.AnimHash,
            });
        }
        return list;
    }
}
