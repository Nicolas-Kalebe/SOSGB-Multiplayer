// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e docs/PROTOCOLO.md (PlayerState).
using System;
using System.Collections.Generic;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

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
        public bool HasState;
    }

    private static readonly Dictionary<byte, Remote> Remotes = new();
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
            SendLocalState();
            UpdateRemotes();
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[PlayerSync] {e.Message}");
        }
    }

    // ---------- envio ----------

    private void SendLocalState()
    {
        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        if (!GameController.IsInstance || !FieldManager.IsInstance) return;
        var player = GameController.Instance.GetPlayer();
        if (player == null) return;

        var t = player.transform;
        var field = FieldManager.Instance.CurrentFieldId;
        int hash = 0;
        float norm = 0;
        var anim = player.Model != null ? player.Model.GetComponentInChildren<Animator>() : null;
        if (anim != null)
        {
            var st = anim.GetCurrentAnimatorStateInfo(0);
            hash = st.fullPathHash;
            norm = st.normalizedTime;
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
            w.Put(hash);
            w.Put(norm);
        }, DeliveryMethod.Sequenced);
    }

    // ---------- recepção ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.PlayerState) return;
        var id = r.GetByte();
        if (id == NetSession.LocalId) return;
        if (!Remotes.TryGetValue(id, out var rm)) Remotes[id] = rm = new Remote();
        rm.Field = r.GetUInt();
        rm.Target = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat());
        rm.TargetRotY = r.GetFloat();
        var hash = r.GetInt();
        var norm = r.GetFloat();
        if (rm.Animator != null && hash != 0 && hash != rm.AnimHash)
            rm.Animator.Play(hash, 0, norm % 1f);
        rm.AnimHash = hash;
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

    private static void CreateAvatar(byte id, Remote rm)
    {
        if (!GameController.IsInstance) return;
        var local = GameController.Instance.GetPlayer();
        if (local == null || local.Model == null) return;

        var clone = UnityEngine.Object.Instantiate(local.Model);
        clone.name = $"SOSGB_RemotePlayer_{id}";
        // Remove scripts do jogo do clone: ele é só visual (posição vem da rede).
        foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true))
            UnityEngine.Object.Destroy(mb);
        foreach (var col in clone.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.Destroy(col);
        rm.Avatar = clone;
        rm.Animator = clone.GetComponentInChildren<Animator>();
        clone.transform.position = rm.Target;
        Plugin.Logger.LogInfo($"[PlayerSync] avatar do jogador #{id} criado");
        DevBridge.PushEvent($"avatar:{id}:criado");
    }

    private static void DestroyRemote(byte id)
    {
        if (!Remotes.TryGetValue(id, out var rm)) return;
        if (rm.Avatar != null) UnityEngine.Object.Destroy(rm.Avatar);
        Remotes.Remove(id);
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
                anim = kv.Value.AnimHash,
            });
        }
        return list;
    }
}
