// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d69.
using System;
using System.Collections.Generic;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Pose de quem está dormindo (ou desmaiado) vista pelos outros (D69, visto pelo usuário: "o cara continua em pé").
/// O avatar remoto copia só o animador (PlayerSync); na cama, o jogo encaixa o corpo no colchão FORA do animador, então
/// para os outros o jogador dormindo aparecia em pé dentro da cama. Quem deita manda uma vez o esqueleto deitado
/// (caminho de cada osso a partir do animador + posição/rotação locais); os outros desligam o animador do avatar dele e
/// aplicam esse esqueleto até ele levantar.
/// </summary>
public class SleepPoseSync : MonoBehaviour
{
    public SleepPoseSync(IntPtr ptr) : base(ptr) { }

    private struct Bone { public int Path; public Vector3 Pos; public Quaternion Rot; }

    private static readonly Dictionary<byte, Bone[]> Poses = new();          // jogador → esqueleto deitado
    private static readonly Dictionary<byte, IntPtr> Applied = new();       // jogador → animador em que já aplicamos
    private static readonly Dictionary<byte, Vector3> AppliedAt = new();    // onde o avatar estava ao aplicar (D75)
    private static bool _sent;
    private static float _sleepingSince = -1;
    private static bool _subscribed;
    private float _nextApply;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id => Clear(id);
        WorldSync.OnWorldReceived += () =>
        {
            foreach (var id in new List<byte>(Poses.Keys)) Clear(id);
            // D75: se mandei a pose deitada, aviso que levantei (antes o "levantou" se perdia na recarga e os outros me viam
            // deitado andando pelo mundo).
            if (_sent) SendClear();
            _sent = false; _sleepingSince = -1;
        };
        // D75: no host ninguém "recebe o mundo"; na virada as poses dos outros saem aqui.
        DaySync.OnHostNewDay += () => { foreach (var id in new List<byte>(Poses.Keys)) Clear(id); };
        NetSession.OnHosting += () => { Poses.Clear(); Applied.Clear(); _sent = false; };
        NetSession.OnJoined += () => { Poses.Clear(); Applied.Clear(); _sent = false; };
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline) { _sent = false; _sleepingSince = -1; return; }
            // Quem dorme: depois de deitar (a animação de deitar leva ~3 s), manda o esqueleto uma vez; ao levantar, limpa.
            if (DaySync.LocalSleeping && !Spectator.Active && !Spectator.Returning && !DaySync.InDayChange)
            {
                if (!_sent)
                {
                    if (_sleepingSince < 0) _sleepingSince = Time.unscaledTime;
                    else if (Time.unscaledTime - _sleepingSince > 4f) { _sent = true; SendPose(); }
                }
            }
            else if (!DaySync.LocalSleeping)
            {
                if (_sent) SendClear();
                _sent = false;
                _sleepingSince = -1;
            }
            if (Time.unscaledTime >= _nextApply) { _nextApply = Time.unscaledTime + 0.25f; ApplyAll(); }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[SleepPose] {e.Message}"); }
    }

    private static int PathHash(Transform t, Transform root)
    {
        var path = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
        unchecked
        {
            var h = (int)2166136261;
            foreach (var c in path) h = (h ^ c) * 16777619;
            return h;
        }
    }

    private static void SendPose()
    {
        var model = GameController.Instance.GetPlayer()?.Model;
        var anim = model != null ? model.GetComponentInChildren<Animator>() : null;
        if (anim == null) return;
        var root = anim.transform;
        var bones = new List<Bone>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t != root) bones.Add(new Bone { Path = PathHash(t, root), Pos = t.localPosition, Rot = t.localRotation });
        var me = NetSession.LocalId;
        NetSession.SendGame(MsgType.PlayerPose, w =>
        {
            w.Put(me); w.Put(bones.Count);
            foreach (var b in bones)
            {
                w.Put(b.Path);
                w.Put(b.Pos.x); w.Put(b.Pos.y); w.Put(b.Pos.z);
                w.Put(b.Rot.x); w.Put(b.Rot.y); w.Put(b.Rot.z); w.Put(b.Rot.w);
            }
        }, DeliveryMethod.ReliableOrdered);
        Plugin.Logger.LogInfo($"[SleepPose] pose deitada enviada ({bones.Count} ossos)");
    }

    private static void SendClear()
    {
        var me = NetSession.LocalId;
        NetSession.SendGame(MsgType.PlayerPose, w => { w.Put(me); w.Put(0); }, DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.PlayerPose) return;
        var raw = r.GetRemainingBytes();
        var rd = new NetDataReader(raw);
        var who = rd.GetByte();
        var n = rd.GetInt();
        if (NetSession.Current == NetSession.Mode.Host)
        {
            if (who != from) return;
            // As mensagens 20+ só chegam ao host: ele repassa aos outros convidados.
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId && id != from)
                    NetSession.SendTo(id, MsgType.PlayerPose, w => w.Put(raw), DeliveryMethod.ReliableOrdered);
        }
        else if (from != Protocol.HostPlayerId) return;
        if (who == NetSession.LocalId) return;
        if (n <= 0) { Clear(who); return; }
        var bones = new Bone[n];
        for (var i = 0; i < n; i++)
            bones[i] = new Bone
            {
                Path = rd.GetInt(),
                Pos = new Vector3(rd.GetFloat(), rd.GetFloat(), rd.GetFloat()),
                Rot = new Quaternion(rd.GetFloat(), rd.GetFloat(), rd.GetFloat(), rd.GetFloat()),
            };
        Poses[who] = bones;
        Applied.Remove(who);
    }

    /// <summary>Aplica as poses nos avatares que estão neste mapa (o animador do avatar fica desligado enquanto deitado).</summary>
    private static void ApplyAll()
    {
        var stale = new List<byte>();
        foreach (var kv in Poses)
        {
            var anim = PlayerSync.RemoteAnimator(kv.Key);
            if (anim == null) continue;
            // D75: quem está andando não está deitado (pose que ficou para trás): solta.
            if (AppliedAt.TryGetValue(kv.Key, out var at) && PlayerSync.TryGetRemotePosition(kv.Key, out var now)
                && (new Vector2(now.x - at.x, now.z - at.z)).magnitude > 1.5f)
            {
                Plugin.Logger.LogInfo($"[SleepPose] #{kv.Key} saiu andando: pose deitada solta");
                stale.Add(kv.Key);
                continue;
            }
            if (Applied.TryGetValue(kv.Key, out var ptr) && ptr == anim.Pointer && !anim.enabled) continue;
            anim.enabled = false;
            var root = anim.transform;
            var map = new Dictionary<int, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t != root) map[PathHash(t, root)] = t;
            var hit = 0;
            foreach (var b in kv.Value)
                if (map.TryGetValue(b.Path, out var t)) { t.localPosition = b.Pos; t.localRotation = b.Rot; hit++; }
            Applied[kv.Key] = anim.Pointer;
            if (PlayerSync.TryGetRemotePosition(kv.Key, out var where)) AppliedAt[kv.Key] = where;
            Plugin.Logger.LogInfo($"[SleepPose] pose deitada aplicada no avatar de #{kv.Key} ({hit}/{kv.Value.Length} ossos)");
        }
        foreach (var id in stale) Clear(id);
    }

    private static void Clear(byte id)
    {
        if (!Poses.Remove(id)) return;
        Applied.Remove(id);
        AppliedAt.Remove(id);
        var anim = PlayerSync.RemoteAnimator(id);
        if (anim != null) anim.enabled = true;
    }

    public static object Status() => new { poses = new List<byte>(Poses.Keys), sent = _sent };
}
