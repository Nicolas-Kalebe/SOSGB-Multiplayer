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
///  - este jogo carrega o mapa onde ele está (cada jogo só tem o mapa do próprio jogador), esconde o próprio personagem,
///    trava o controle e põe a câmera do jogo no avatar dele (FollowingCamera.SetTarget);
///  - para os outros, este jogador continua deitado na casa (a posição dele não é enviada enquanto espectar);
///  - F7 de novo passa para o próximo; depois do último, volta para casa (continua "dormindo": Backspace levanta).
///  - Quando o dia vira, o mundo novo/a manhã põem tudo no lugar; levantar (Backspace) encerra e volta para casa.
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
    private static Vector3 _homePos;
    private static bool _returning;
    private static float _nextTick;
    private static float _fieldRequestedAt = -100;
    private static GameObject _followed;
    private static bool _inputLocked;

    private void Awake() => WorldSync.OnWorldReceived += () => { if (Active) Finish("mundo novo"); };

    private void Update()
    {
        try
        {
            var f7 = Input.GetKeyDown(KeyCode.F7) && !MultiplayerUI.TypingInMenu;
            if (!Active)
            {
                if (_returning) { ReturnTick(); return; }
                // O controle ficou travado enquanto "dormia" depois de espectar: o dia virou (ou levantou) → solta.
                if (_inputLocked && !DaySync.LocalSleeping) { _inputLocked = false; try { GameController.Instance.GetPlayer().IsMoveInputEnabled = true; } catch { } }
                if (f7) { if (CanStart()) Next(); else GameNotify.Show("Espectar: deite na cama esperando os outros (F7 segue quem está acordado)."); }
                return;
            }
            if (NetSession.Current == NetSession.Mode.Offline || !DaySync.LocalSleeping) { Stop("acordou ou a sessão acabou"); return; }
            if (f7) { Next(); if (!Active) return; }
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.3f;
            KeepHidden(true);
            var field = PlayerSync.RemoteField(_target);
            if (field == 0) { Next(); return; } // saiu do mundo
            if (!FieldManager.IsInstance || FieldManager.Instance.IsChangeFieldUnloading) return;
            if (FieldManager.Instance.CurrentFieldId != field)
            {
                if (Time.unscaledTime - _fieldRequestedAt < 8f) return; // carregando
                _fieldRequestedAt = Time.unscaledTime;
                _followed = null;
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
        var next = ids.FirstOrDefault(id => !Active || id > _target);
        if (ids.Count == 0 || (Active && !ids.Any(id => id > _target))) { if (Active) Stop("fim da lista"); else GameNotify.Show("Espectar: ninguém acordado para seguir."); return; }
        if (!Active)
        {
            _homeField = FieldManager.Instance.CurrentFieldId;
            _homePos = GameController.Instance.GetPlayerTransform().position;
            DaySync.LeaveBedKeepSleeping(); // deitado o jogo não troca de mapa
            Active = true;
            Plugin.Logger.LogInfo("[Spectator] começou");
        }
        _target = next;
        _followed = null;
        _fieldRequestedAt = -100;
        var name = NetSession.Players.TryGetValue(next, out var p) ? p.Name : "jogador";
        GameNotify.Show($"Espectando {name}. F7: próximo / voltar para casa. Backspace: levantar.");
    }

    private static void KeepHidden(bool hidden)
    {
        var player = GameController.Instance.GetPlayer();
        if (player == null) return;
        // O jogo move o MODELO (GetPlayerTransform), que não fica embaixo da raiz do PlayerCharacter (CODIGO.md).
        var model = GameController.Instance.GetPlayerTransform();
        foreach (var root in new Component[] { player, model })
            if (root != null) foreach (var r in root.GetComponentsInChildren<Renderer>(true)) if (r != null) r.enabled = !hidden;
        if (hidden) player.IsMoveInputEnabled = false;
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

    /// <summary>Volta para casa (se ainda dormindo, fica deitado esperando na mesma posição).</summary>
    private static void Stop(string why)
    {
        Active = false;
        _followed = null;
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
        try { GameController.Instance.GetPlayer().SetPlayerClosestGroundPosition(_homePos, new Il2CppSystem.Nullable<Vector3>(), 10f); } catch { }
        FinishReturn();
    }

    private static void FinishReturn()
    {
        KeepHidden(false);
        RestoreCamera();
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
        Plugin.Logger.LogInfo($"[Spectator] encerrado ({why})");
    }

    public static object Status() => new { active = Active, target = _target, homeField = _homeField, returning = _returning };
}
