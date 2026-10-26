// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d69.
using System;
using System.Collections.Generic;
using System.Linq;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Espectar quem está acordado enquanto dorme (D69, pedido do usuário na P8: "gostaria de pelo menos tentar").
/// EXPERIMENTAL. Deitado esperando os outros (DaySync), F7 segue o próximo jogador acordado:
///  - este jogo carrega o mapa onde ele está (cada jogo só tem o mapa do próprio jogador) e põe a câmera do jogo no avatar
///    dele (FollowingCamera.SetTarget). O personagem local CONTINUA dormindo por dentro (o jogo troca de mapa com ele
///    deitado, desde que o pedido seja pelo número base do mapa): fica escondido (forceRenderingOff, que o jogo não
///    religa) e parado onde chegou;
///  - para os outros, este jogador continua deitado na casa (a posição dele não é enviada enquanto espectar);
///  - a interface do jogo e os painéis do mod somem; fica só uma linha "Espectando Fulano";
///  - F7 de novo passa para o próximo; depois do último, volta para casa DEITADO na cama: a pose guardada antes de sair
///    (posição, rotação, animador) é restaurada e o personagem fica PAUSADO nela (a pausa do próprio jogo,
///    PauseObject.Pause(All)) até o dia virar ou levantar (pedido do usuário: voltava em pé; repor a pose a cada quadro
///    fazia o personagem tremer na cama). Backspace levanta, como sempre.
///  - Quando o dia vira, a virada do jogo segue o caminho normal (o personagem estava dormindo de verdade).
/// </summary>
public class Spectator : MonoBehaviour
{
    public Spectator(IntPtr ptr) : base(ptr) { }

    /// <summary>Espectando agora (o PlayerSync não manda a posição deste jogador).</summary>
    public static bool Active { get; private set; }

    /// <summary>Voltando para casa depois de espectar (a posição ainda não é a da casa).</summary>
    public static bool Returning => _returning;

    private static byte _target = 255;
    private static uint _homeField;
    private static bool _returning;
    private static float _nextTick;
    private static float _fieldRequestedAt = -100;
    private static GameObject _followed;
    private static bool _inputLocked;
    private static bool _pinned;            // de volta em casa: mantido na pose deitada até o dia virar ou levantar
    private static Vector3? _parkPos;       // espectando: o personagem escondido fica parado onde chegou
    private static uint _parkField;
    private static CanvasGroup _hudGroup, _guideGroup;
    private static readonly List<Renderer> Hidden = new();

    /// <summary>Nome de quem está sendo seguido (linha da interface).</summary>
    public static string TargetName => NetSession.Players.TryGetValue(_target, out var p) ? p.Name : "jogador";

    private void Awake() => WorldSync.OnWorldReceived += () => { _pinned = false; if (Active) Finish("mundo novo"); };

    private void LateUpdate()
    {
        try
        {
            if (Active)
            {
                // Todo quadro, depois do jogo: escondido (o jogo religa "enabled" no esmaecimento por câmera) e parado.
                foreach (var r in Hidden) if (r != null) r.forceRenderingOff = true;
                var t = GameController.Instance.GetPlayerTransform();
                if (t != null && FieldManager.IsInstance && !FieldManager.Instance.IsChangeFieldUnloading)
                {
                    var f = FieldManager.Instance.CurrentFieldId;
                    if (_parkPos == null || _parkField != f) { _parkPos = t.position; _parkField = f; CollectRenderers(); }
                    else t.position = _parkPos.Value;
                }
                return;
            }
            if (!_pinned) return;
            if (!DaySync.LocalSleeping || _returning) { Unpin(); return; }
            // O jogo tirou a pausa (ex.: fechou um menu): volta à pose e pausa de novo.
            var po = GameController.Instance.GetPlayer()?.PauseObject;
            if (po != null && !po.IsPaused) { RestorePose(); po.Pause(PauseType.All); var a = LocalAnimator(); if (a != null) a.enabled = false; RestorePose(); }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Spectator] quadro: {e.Message}"); _pinned = false; }
    }

    private void Update()
    {
        try
        {
            var f7 = Input.GetKeyDown(KeyCode.F7) && !MultiplayerUI.TypingInMenu;
            if (!Active)
            {
                if (_returning) { ReturnTick(); return; }
                // Controle travado enquanto "dormia" depois de espectar: o dia virou (ou levantou) → solta.
                if (_inputLocked && !DaySync.LocalSleeping) { _inputLocked = false; try { GameController.Instance.GetPlayer().IsMoveInputEnabled = true; } catch { } }
                if (f7) { if (CanStart()) Next(); else GameNotify.Show("Espectar: deite na cama esperando os outros (F7 segue quem está acordado)."); }
                return;
            }
            if (NetSession.Current == NetSession.Mode.Offline || !DaySync.LocalSleeping) { Stop("acordou ou a sessão acabou"); return; }
            if (f7) { Next(); if (!Active) return; }
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.3f;
            SetGameHud(false);
            try { GameController.Instance.GetPlayer().IsMoveInputEnabled = false; } catch { }
            var field = PlayerSync.RemoteField(_target);
            if (field == 0) { Next(); return; } // saiu do mundo
            if (!FieldManager.IsInstance || FieldManager.Instance.IsChangeFieldUnloading) return;
            if (FieldManager.Instance.CurrentFieldId != field)
            {
                if (Time.unscaledTime - _fieldRequestedAt < 8f) return; // carregando
                _fieldRequestedAt = Time.unscaledTime;
                _followed = null;
                _parkPos = null;
                GoTo(field);
                return;
            }
            var avatar = PlayerSync.RemoteAvatar(_target);
            if (avatar != null && avatar != _followed)
            {
                CameraManager.Instance.FollowingCamera.SetTarget(avatar);
                _followed = avatar;
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Spectator] {e}"); }
    }

    /// <summary>
    /// Troca de mapa pelo número BASE (FieldMaster.ConvertToFieldId): o jogo não aceita o número da variação da estação
    /// (pedir 20002 não faz nada / dá NullReference; 20001 vai para 20002, testado).
    /// </summary>
    private static void GoTo(uint field)
    {
        uint req = 0;
        try { req = FieldState.Root(field); } catch { }
        if (req == 0) req = field;
        Plugin.Logger.LogInfo($"[Spectator] indo para o mapa {field} (pedido {req})");
        GameController.Instance.ChangeField((FieldMasterId)req, "", "", false, true, false, null, null, null, false);
    }

    private static bool CanStart() =>
        NetSession.Current != NetSession.Mode.Offline && DaySync.LocalSleeping && !DaySync.InDayChange
        && SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game && GameController.IsInstance;

    /// <summary>Próximo jogador acordado (com estado conhecido); depois do último, volta para casa.</summary>
    private static void Next()
    {
        var ids = NetSession.Players.Keys.Where(id => id != NetSession.LocalId && PlayerSync.RemoteField(id) != 0).OrderBy(id => id).ToList();
        if (ids.Count == 0 || (Active && !ids.Any(id => id > _target)))
        {
            if (Active) Stop("fim da lista"); else GameNotify.Show("Espectar: ninguém acordado para seguir.");
            return;
        }
        var next = ids.First(id => !Active || id > _target);
        if (!Active)
        {
            _homeField = FieldManager.Instance.CurrentFieldId;
            SavePose(); // para voltar deitado
            _parkPos = null;
            _pinned = false;
            Active = true;
            Plugin.Logger.LogInfo("[Spectator] começou");
        }
        _target = next;
        _followed = null;
        _fieldRequestedAt = -100;
        GameNotify.Show($"Espectando {TargetName}. F7: próximo / voltar para casa. Backspace: levantar.");
    }

    private static void CollectRenderers()
    {
        foreach (var r in Hidden) if (r != null) r.forceRenderingOff = false;
        Hidden.Clear();
        var player = GameController.Instance.GetPlayer();
        var model = GameController.Instance.GetPlayerTransform();
        foreach (var root in new Component[] { player, model })
            if (root != null) foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r != null && !Hidden.Contains(r)) Hidden.Add(r);
    }

    private static void ShowLocal()
    {
        foreach (var r in Hidden) if (r != null) r.forceRenderingOff = false;
        Hidden.Clear();
        SetGameHud(true);
    }

    /// <summary>
    /// Some com a interface do jogo enquanto especta (pedido do usuário): transparência na página principal (vigor,
    /// relógio, ferramentas) e nas dicas de ação ("C Cumprimentar").
    /// </summary>
    private static void SetGameHud(bool visible)
    {
        _hudGroup = SetAlpha<UIGameMainPage>(_hudGroup, visible);
        _guideGroup = SetAlpha<ActionGuideManager>(_guideGroup, visible);
    }

    private static CanvasGroup SetAlpha<T>(CanvasGroup group, bool visible) where T : Component
    {
        try
        {
            if (group == null)
            {
                var c = UnityEngine.Object.FindObjectOfType<T>();
                if (c == null) return null;
                group = c.GetComponent<CanvasGroup>() ?? c.gameObject.AddComponent<CanvasGroup>();
            }
            group.alpha = visible ? 1f : 0f;
            return group;
        }
        catch { return null; }
    }

    private static void RestoreCamera()
    {
        try
        {
            var t = GameController.Instance.GetPlayerTransform();
            if (t != null) CameraManager.Instance.FollowingCamera.SetTarget(t.gameObject);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Spectator] câmera: {e.Message}"); }
    }

    /// <summary>Volta para casa (se ainda dormindo, deitado na cama).</summary>
    private static void Stop(string why)
    {
        Active = false;
        _followed = null;
        _parkPos = null;
        Plugin.Logger.LogInfo($"[Spectator] parou ({why})");
        if (FieldManager.IsInstance && FieldManager.Instance.CurrentFieldId != _homeField)
        {
            _returning = true;
            _fieldRequestedAt = Time.unscaledTime;
            GoTo(_homeField);
            return;
        }
        FinishReturn();
    }

    private static void ReturnTick()
    {
        if (!FieldManager.IsInstance || FieldManager.Instance.IsChangeFieldUnloading) return;
        if (FieldManager.Instance.CurrentFieldId != _homeField) { if (Time.unscaledTime - _fieldRequestedAt > 20f) { _returning = false; FinishReturn(); } return; }
        if (GameController.Instance.GetPlayerTransform() == null) return;
        _returning = false;
        FinishReturn();
    }

    private static void FinishReturn()
    {
        ShowLocal();
        RestoreCamera();
        if (DaySync.LocalSleeping && RestorePose())
        {
            // Volta deitado na cama: pausado na pose (sem brigar com o jogo a cada quadro). O animador fica desligado
            // para não sobrescrever o esqueleto restaurado.
            try { GameController.Instance.GetPlayer().PauseObject?.Pause(PauseType.All); } catch { }
            try { var a = LocalAnimator(); if (a != null) a.enabled = false; RestorePose(); } catch { }
            _pinned = true;
        }
        // Ainda esperando os outros: continua "dormindo" (sem andar) até o dia virar ou levantar com Backspace.
        _inputLocked = DaySync.LocalSleeping;
        try { GameController.Instance.GetPlayer().IsMoveInputEnabled = !DaySync.LocalSleeping; } catch { }
    }

    /// <summary>O mundo recarregou (virada no convidado): só desliga; a cena nova já vem certa.</summary>
    private static void Finish(string why)
    {
        Active = false;
        _returning = false;
        _followed = null;
        _parkPos = null;
        Hidden.Clear();
        _hudGroup = null;
        _guideGroup = null;
        Plugin.Logger.LogInfo($"[Spectator] encerrado ({why})");
    }

    /// <summary>
    /// Solta o personagem da pose pausada. Chamado ANTES de levantar (Backspace) e da virada do dia, para a animação de
    /// levantar e a manhã do jogo rodarem normalmente.
    /// </summary>
    public static void Unpin()
    {
        if (!_pinned) return;
        _pinned = false;
        try
        {
            var a = LocalAnimator();
            if (a != null) a.enabled = true;
            var po = GameController.Instance.GetPlayer()?.PauseObject;
            if (po != null && po.IsPaused) po.Resume();
        }
        catch { }
    }

    // ---------- pose deitada na cama (para voltar deitado, e não em pé; pedido do usuário) ----------

    private struct Pose
    {
        public Vector3 Pos;
        public Quaternion Rot;
        public int[] Hashes;
        public float[] Norms;
        public float[] Weights;
        public List<(int hash, AnimatorControllerParameterType type, float value)> Params;
        public Vector3[] BonePos;     // esqueleto inteiro (a cama encaixa o corpo no colchão fora do animador)
        public Quaternion[] BoneRot;
        public int BoneCount;
    }

    private static Pose? _pose;

    private static Animator LocalAnimator()
    {
        var model = GameController.Instance.GetPlayer()?.Model; // o Animator fica no Model (como no PlayerSync)
        return model != null ? model.GetComponentInChildren<Animator>() : null;
    }

    /// <summary>Guarda posição, rotação e o estado do animador (todas as camadas e parâmetros) do personagem agora.</summary>
    public static bool SavePose()
    {
        var t = GameController.Instance.GetPlayerTransform();
        var anim = LocalAnimator();
        if (t == null || anim == null) return false;
        var n = anim.layerCount;
        var p = new Pose { Pos = t.position, Rot = t.rotation, Hashes = new int[n], Norms = new float[n], Weights = new float[n], Params = new() };
        for (var i = 0; i < n; i++)
        {
            var st = anim.GetCurrentAnimatorStateInfo(i);
            p.Hashes[i] = st.fullPathHash; p.Norms[i] = st.normalizedTime; p.Weights[i] = anim.GetLayerWeight(i);
        }
        foreach (var pr in anim.parameters)
        {
            switch (pr.type)
            {
                case AnimatorControllerParameterType.Float: p.Params.Add((pr.nameHash, pr.type, anim.GetFloat(pr.nameHash))); break;
                case AnimatorControllerParameterType.Int: p.Params.Add((pr.nameHash, pr.type, anim.GetInteger(pr.nameHash))); break;
                case AnimatorControllerParameterType.Bool: p.Params.Add((pr.nameHash, pr.type, anim.GetBool(pr.nameHash) ? 1 : 0)); break;
            }
        }
        var bones = GameController.Instance.GetPlayer().Model.GetComponentsInChildren<Transform>(true);
        p.BoneCount = bones.Length;
        p.BonePos = new Vector3[bones.Length];
        p.BoneRot = new Quaternion[bones.Length];
        for (var i = 0; i < bones.Length; i++) { p.BonePos[i] = bones[i].localPosition; p.BoneRot[i] = bones[i].localRotation; }
        _pose = p;
        return true;
    }

    /// <summary>
    /// Põe o personagem de volta na pose guardada (deitado na cama). keepAnimation (todo quadro): reaplica posição e
    /// parâmetros e só toca de novo as camadas que saíram do estado guardado (sem reiniciar a animação de dormir).
    /// </summary>
    public static bool RestorePose(bool keepAnimation = false)
    {
        if (_pose == null) return false;
        var p = _pose.Value;
        var t = GameController.Instance.GetPlayerTransform();
        var anim = LocalAnimator();
        if (t == null || anim == null) return false;
        t.SetPositionAndRotation(p.Pos, p.Rot);
        foreach (var (hash, type, value) in p.Params)
        {
            if (type == AnimatorControllerParameterType.Float) anim.SetFloat(hash, value);
            else if (type == AnimatorControllerParameterType.Int) anim.SetInteger(hash, (int)value);
            else anim.SetBool(hash, value > 0.5f);
        }
        for (var i = 0; i < p.Hashes.Length && i < anim.layerCount; i++)
        {
            if (keepAnimation && anim.GetCurrentAnimatorStateInfo(i).fullPathHash == p.Hashes[i]) continue;
            anim.SetLayerWeight(i, p.Weights[i]);
            anim.Play(p.Hashes[i], i, p.Norms[i] % 1f);
        }
        // Aplica a pose já (o Play só valeria no próximo quadro e a pausa congelaria o personagem em pé).
        anim.Update(0f);
        t.SetPositionAndRotation(p.Pos, p.Rot);
        // Esqueleto exatamente como estava deitado (o encaixe no colchão é feito pela cama, não pelo animador).
        var bones = GameController.Instance.GetPlayer().Model.GetComponentsInChildren<Transform>(true);
        if (p.BonePos != null && bones.Length == p.BoneCount)
            for (var i = 0; i < bones.Length; i++) { if (bones[i] == t) continue; bones[i].localPosition = p.BonePos[i]; bones[i].localRotation = p.BoneRot[i]; }
        return true;
    }

    /// <summary>Testes: estado do animador agora × pose guardada, camada por camada.</summary>
    public static object PoseDebug()
    {
        var anim = LocalAnimator();
        var t = GameController.Instance.GetPlayerTransform();
        var layers = new List<string>();
        if (anim != null)
            for (var i = 0; i < anim.layerCount; i++)
            {
                var st = anim.GetCurrentAnimatorStateInfo(i);
                var saved = _pose != null && i < _pose.Value.Hashes.Length ? $"{_pose.Value.Hashes[i]}/{_pose.Value.Weights[i]:0.##}" : "-";
                layers.Add($"{i} {anim.GetLayerName(i)}: agora {st.fullPathHash}/{anim.GetLayerWeight(i):0.##} t={st.normalizedTime:0.##} | guardada {saved}");
            }
        return new { pos = t?.position.ToString("F2"), savedPos = _pose?.Pos.ToString("F2"), animName = anim?.name, animEnabled = anim?.enabled, speed = anim?.speed, layers };
    }

    public static object Status() => new { active = Active, target = _target, homeField = _homeField, returning = _returning, pinned = _pinned };
}
