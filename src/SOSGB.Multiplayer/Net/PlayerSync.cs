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
        public int Meshes;                           // malhas do modelo instalado (0 = medir de novo), D73
        public bool Broken;                          // perdeu peças: fica escondido até remontar (D77)
        public float NextHealthCheck;
        public float LastRepair = -999f;
        public float RepairBackoff = 5f;
        public Vector2Int Grid;                      // posição em grade (PlayerCharacter.GridPosition), para o mapa (D82)
        public bool HasGrid;
        // D84: espelho do personagem local durante a cena assistida (ossos copiados quadro a quadro)
        public bool Mirroring;
        public List<(Transform src, Transform dst)> Bones;
        public Animator MirrorSrcAnim;
        public AnimatorCullingMode MirrorCulling;
    }

    private static readonly Dictionary<byte, Remote> Remotes = new();

    /// <summary>Animador do avatar de um jogador remoto, se ele estiver no mapa local. Pose deitada (D69).</summary>
    public static Animator RemoteAnimator(byte id) =>
        Remotes.TryGetValue(id, out var rm) && rm.Avatar != null && rm.Animator != null && FieldManager.IsInstance
        && rm.Field == FieldManager.Instance.CurrentFieldId ? rm.Animator : null;

    /// <summary>Avatar de um jogador remoto, se ele estiver no mapa local. Espectador (D69).</summary>
    public static GameObject RemoteAvatar(byte id) =>
        Remotes.TryGetValue(id, out var rm) && rm.Avatar != null && FieldManager.IsInstance && rm.Field == FieldManager.Instance.CurrentFieldId ? rm.Avatar : null;

    /// <summary>Última posição conhecida do avatar de um jogador remoto (no mapa local).</summary>
    public static bool TryGetRemotePosition(byte id, out Vector3 pos)
    {
        pos = default;
        if (!Remotes.TryGetValue(id, out var rm) || !rm.HasState) return false;
        if (FieldManager.IsInstance && rm.Field != FieldManager.Instance.CurrentFieldId) return false;
        pos = rm.Target;
        return true;
    }
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
            if (rm.Avatar != null) AppearanceSync.Release(rm.Avatar);
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
        // Espectando (D69): para os outros, este jogador continua deitado na casa (o último estado enviado).
        if (Spectator.Active || Spectator.Returning || Spectator.Pinned) return;
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
        var grid = GameController.Instance.GetPlayer()?.GridPosition ?? default;
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
            w.Put(grid.x); w.Put(grid.y); // D82: no fim (quem não lê ignora)
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
        if (r.AvailableBytes >= 8) { rm.Grid = new Vector2Int(r.GetInt(), r.GetInt()); rm.HasGrid = true; }
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
            CheckHealth(kv.Key, rm);
            if (rm.Avatar == null) continue;
            EnsureAppearance(kv.Key, rm);
            SyncController(rm);

            // 2.5: só no mesmo mapa. D75: com a tela de aparência aberta, a prévia é montada no mapa e os outros apareciam nela.
            // D84: numa cena, os outros não aparecem (a cena é de quem a vê: visto pelo usuário, a convidada ficava parada no fundo
            // da cerimônia do host); assistindo à cena de alguém, só o avatar dele (o protagonista) aparece.
            var visible = rm.Field == localField && !CharacterCreator.OnScreen && !rm.Broken && EventWatch.ShowRemoteInEvent(kv.Key);
            if (rm.Avatar.activeSelf != visible) rm.Avatar.SetActive(visible);
            if (!visible) continue;

            if (rm.Mirroring) continue; // na cena assistida quem posiciona é o espelho (LateUpdate)
            var tr = rm.Avatar.transform;
            // Interpolação simples: suaviza os 10 pacotes/s em movimento contínuo.
            if ((tr.position - rm.Target).sqrMagnitude > 25f) tr.position = rm.Target; // teleporte (>5 m)
            else tr.position = Vector3.Lerp(tr.position, rm.Target, Time.deltaTime * 12f);
            var rot = Quaternion.Euler(0, rm.TargetRotY, 0);
            tr.rotation = Quaternion.Slerp(tr.rotation, rot, Time.deltaTime * 12f);
        }
    }

    // ---------- D84: espelho na cena assistida ----------

    /// <summary>
    /// Assistindo à cena de alguém, a MESMA cena roda aqui no personagem local escondido, com todas as animações dela (beber
    /// suco, sentar, segurar o troféu, rosto). O avatar do protagonista não tem essas animações (só as normais, vindas pela
    /// rede) e ficava parado em pé (visto pelo usuário). Durante a cena, o avatar copia o personagem local: posição e cada osso,
    /// depois das animações do quadro (LateUpdate).
    /// </summary>
    private void LateUpdate()
    {
        try
        {
            foreach (var kv in Remotes)
            {
                var rm = kv.Value;
                var want = rm.Avatar != null && rm.Avatar.activeSelf && EventWatch.MirrorRemote(kv.Key);
                if (!want) { if (rm.Mirroring) EndMirror(rm); continue; }
                Mirror(rm);
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[PlayerSync] espelho na cena: {e.Message}"); }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        if (root.name == name) return root;
        for (var i = 0; i < root.childCount; i++)
        {
            var f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    private static void MapBones(Transform src, Transform dst, string path, Dictionary<string, Transform> into)
    {
        into[path] = src;
        for (var i = 0; i < src.childCount; i++) { var c = src.GetChild(i); MapBones(c, null, path + "/" + c.name, into); }
    }

    private static void Mirror(Remote rm)
    {
        var player = GameController.Instance.GetPlayer();
        var lt = GameController.Instance.GetPlayerTransform();
        if (player == null || lt == null || player.Model == null) return;
        if (!rm.Mirroring || rm.Bones == null || rm.Bones.Count == 0 || rm.Bones[0].src == null)
        {
            var srcRoot = FindDeep(player.Model.transform, "Chr_root");
            var dstRoot = FindDeep(rm.Avatar.transform, "Chr_root");
            if (srcRoot == null || dstRoot == null) return;
            var srcMap = new Dictionary<string, Transform>();
            MapBones(srcRoot, null, "", srcMap);
            var dstMap = new Dictionary<string, Transform>();
            MapBones(dstRoot, null, "", dstMap);
            rm.Bones = new List<(Transform, Transform)>();
            foreach (var kv in srcMap) if (dstMap.TryGetValue(kv.Key, out var d)) rm.Bones.Add((kv.Value, d));
            // o personagem local está invisível: sem isto o Animator dele pode parar de animar os ossos (culling)
            rm.MirrorSrcAnim = player.Model.GetComponentInChildren<Animator>();
            if (rm.MirrorSrcAnim != null) { rm.MirrorCulling = rm.MirrorSrcAnim.cullingMode; rm.MirrorSrcAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            if (rm.Animator != null) rm.Animator.enabled = false;
            rm.Mirroring = true;
            Plugin.Logger.LogInfo($"[PlayerSync] avatar espelhando a cena local ({rm.Bones.Count} ossos)");
        }
        var tr = rm.Avatar.transform;
        tr.position = lt.position;
        tr.rotation = lt.rotation;
        foreach (var (s, d) in rm.Bones)
        {
            if (s == null || d == null) continue;
            d.localPosition = s.localPosition;
            d.localRotation = s.localRotation;
            d.localScale = s.localScale;
        }
    }

    private static void EndMirror(Remote rm)
    {
        rm.Mirroring = false;
        rm.Bones = null;
        if (rm.MirrorSrcAnim != null) { try { rm.MirrorSrcAnim.cullingMode = rm.MirrorCulling; } catch { } }
        rm.MirrorSrcAnim = null;
        if (rm.Animator != null) { rm.Animator.enabled = true; rm.Hashes = Array.Empty<int>(); } // volta aos estados da rede
        Plugin.Logger.LogInfo("[PlayerSync] avatar voltou a seguir a rede");
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
        AppearanceSync.OwnMaterials(clone); // D78: não muda junto quando o visual local muda
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
        rm.Meshes = 0;
        rm.Broken = false;
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
            if (gen != rm.Gen) { if (model != null) AppearanceSync.Release(model); return; } // de antes de recarregar
            rm.Building = false;
            if (model == null) { rm.BuiltAppearance = app; return; } // não deu para montar: fica o provisório (sem tentar de novo)
            if (!Remotes.ContainsKey(id)) { AppearanceSync.Release(model); return; }
            PrepareClone(model, id);
            if (rm.Avatar != null)
            {
                model.transform.position = rm.Avatar.transform.position;
                model.transform.rotation = rm.Avatar.transform.rotation;
                model.SetActive(rm.Avatar.activeSelf);
                AppearanceSync.Release(rm.Avatar);
            }
            rm.Avatar = model;
            rm.Animator = model.GetComponentInChildren<Animator>();
            // Sempre uma CÓPIA própria do controller capturado na montagem (já com as animações do gênero do
            // jogador). Compartilhar o do jogador local não serve: SwitchGenderMotion altera aquele objeto no
            // lugar, e o avatar passaria a usar as animações do gênero de quem está olhando (D32, teste em rede).
            var real = controller != null && controller.TryCast<AnimatorOverrideController>() != null;
            rm.Controller = real ? UnityEngine.Object.Instantiate(controller) : null; // sem o real: segue o do local
            if (rm.Animator != null && rm.Controller != null)
            {
                rm.Animator.runtimeAnimatorController = rm.Controller;
                rm.Animator.Rebind(); // sem isto o clone pode continuar nas animações anteriores
            }
            rm.Hashes = Array.Empty<int>();
            rm.BuiltAppearance = app;
            rm.Meshes = 0;
            rm.Broken = false;
            Plugin.Logger.LogInfo($"[PlayerSync] avatar do jogador #{id} montado com a aparência dele");
            DevBridge.PushEvent($"avatar:{id}:aparencia");
        });
    }

    /// <summary>Malhas presentes no modelo (renderers com malha carregada).</summary>
    private static int CountMeshes(GameObject go)
    {
        var n = 0;
        foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMesh != null) n++;
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) n++;
        return n;
    }

    /// <summary>
    /// O avatar é uma CÓPIA do modelo montado com peças carregadas no jogo local. O jogo descarrega as peças que o personagem
    /// local não usa (ao trocar de mapa, ao fechar o guarda-roupa) e a cópia perde o corpo: ficava invisível ou só com o rosto
    /// (D73, visto pelo usuário). A cada segundo conta as malhas; se sumiram, monta de novo (com espera crescente se repetir).
    /// </summary>
    private static void CheckHealth(byte id, Remote rm)
    {
        if (rm.Building || AppearanceSync.Swapping || CharacterCreator.AvatarMakeBusy || Time.unscaledTime < rm.NextHealthCheck) return;
        rm.NextHealthCheck = Time.unscaledTime + 0.25f;
        var n = CountMeshes(rm.Avatar);
        if (rm.Meshes == 0) { rm.Meshes = n; return; } // primeira medida depois de instalar o modelo
        if (n >= rm.Meshes) return;
        rm.Broken = true; // D77: escondido até remontar (nada de "rosto flutuante")
        if (Time.unscaledTime - rm.LastRepair < rm.RepairBackoff) return;
        rm.RepairBackoff = Time.unscaledTime - rm.LastRepair < 60f ? Math.Min(rm.RepairBackoff * 2f, 120f) : 5f;
        rm.LastRepair = Time.unscaledTime;
        Plugin.Logger.LogInfo($"[PlayerSync] avatar de #{id} perdeu peças ({rm.Meshes} → {n} malhas: o jogo descarregou); montando de novo");
        DevBridge.PushEvent($"avatar:{id}:remontar");
        rm.Meshes = 0;
        if (AppearanceSync.KnownAppearance(id) != null) { rm.BuiltAppearance = null; return; }
        // Avatar provisório (aparência ainda desconhecida): nova cópia do modelo local no lugar.
        AppearanceSync.Release(rm.Avatar);
        rm.Avatar = null;
        CreateAvatar(id, rm);
    }

    private static void DestroyRemote(byte id)
    {
        if (!Remotes.TryGetValue(id, out var rm)) return;
        if (rm.Avatar != null) AppearanceSync.Release(rm.Avatar);
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

    /// <summary>Mapa e posição em grade de cada jogador remoto, para o mapa do jogo (D82).</summary>
    public static List<(byte id, uint field, Vector2Int grid)> RemoteMapInfos()
    {
        var list = new List<(byte, uint, Vector2Int)>();
        foreach (var kv in Remotes)
            if (kv.Value.HasState && kv.Value.HasGrid && NetSession.Players.ContainsKey(kv.Key)) list.Add((kv.Key, kv.Value.Field, kv.Value.Grid));
        return list;
    }

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
