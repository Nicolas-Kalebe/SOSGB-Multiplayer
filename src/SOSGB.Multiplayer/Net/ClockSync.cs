// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d9 e ROTEIRO.md (3.2/3.3).
using System;
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Relógio controlado pelo host (3.2) e pausa configurável (3.3, D9).
///
/// O jogo para o relógio pela propriedade DateManager.IsUpdateEnabled (menus, eventos; ver CODIGO.md).
/// Um patch nesse getter guarda o valor "natural" (este jogador está em menu/evento?) e, no
/// multiplayer, força false quando o grupo deve estar pausado.
///
///  - Convidado: informa ao host quando fica pausado/despausado (PauseReport) e segue o relógio
///    do host (ClockState): pausa quando o host manda e corrige a hora se divergir mais de 30 s de jogo.
///  - Host: conta quantos jogadores estão pausados; se ≥ N ([Game] PauseWhenPlayersInMenu;
///    0 = todos), o grupo pausa. Manda ClockState 2×/s.
/// </summary>
public class ClockSync : MonoBehaviour
{
    public ClockSync(IntPtr ptr) : base(ptr) { }

    private const float SendInterval = 0.5f;
    private const long MaxDriftTicks = 30L * 10_000_000; // 30 s de jogo (ticks de 100 ns)

    /// <summary>Este jogador está em menu/evento (valor original do jogo).</summary>
    public static bool LocalPaused { get; private set; }

    /// <summary>O grupo deve estar pausado (decidido pelo host).</summary>
    public static bool GroupPaused { get; private set; }

    private static readonly Dictionary<byte, bool> GuestPaused = new();
    private static bool _subscribed;
    private float _nextSend;
    private bool _lastReported;
    private float _nextReport;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id => GuestPaused.Remove(id);
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline) { GroupPaused = false; GuestPaused.Clear(); return; }
            if (!DateManager.IsInstance) return;
            _ = DateManager.Instance.IsUpdateEnabled; // atualiza LocalPaused pelo patch

            if (NetSession.Current == NetSession.Mode.Host) HostTick();
            else ClientTick();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[ClockSync] {e.Message}"); }
    }

    private void HostTick()
    {
        var paused = LocalPaused ? 1 : 0;
        foreach (var v in GuestPaused.Values) if (v) paused++;
        var n = Plugin.PauseWhenPlayersInMenu.Value;
        var total = NetSession.Players.Count;
        GroupPaused = n <= 0 ? paused >= total : paused >= n;

        if (Time.unscaledTime < _nextSend) return;
        _nextSend = Time.unscaledTime + SendInterval;
        var ticks = DateManager.Instance.Now.Ticks;
        var running = !GroupPaused;
        NetSession.SendGame(MsgType.ClockState, w => { w.Put(ticks); w.Put(running); }, DeliveryMethod.Sequenced);
    }

    private void ClientTick()
    {
        // Avisa o host quando muda (e a cada 1 s, para o caso de um pacote se perder).
        if (LocalPaused == _lastReported && Time.unscaledTime < _nextReport) return;
        _lastReported = LocalPaused;
        _nextReport = Time.unscaledTime + 1f;
        var p = LocalPaused;
        NetSession.SendGame(MsgType.PauseReport, w => w.Put(p), DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.PauseReport when NetSession.Current == NetSession.Mode.Host:
                GuestPaused[from] = r.GetBool();
                break;

            case MsgType.ClockState when NetSession.Current == NetSession.Mode.Client:
                var hostTicks = r.GetLong();
                GroupPaused = !r.GetBool();
                if (!DateManager.IsInstance) return;
                var dm = DateManager.Instance;
                var drift = dm.Now.Ticks - hostTicks;
                if (Math.Abs(drift) > MaxDriftTicks)
                {
                    dm.SetDate(new BokuMonoDateTime(hostTicks), true);
                    Plugin.Logger.LogInfo($"[ClockSync] hora corrigida para a do host (divergência de {drift / 10_000_000} s de jogo)");
                    DevBridge.PushEvent("clock-corrigido");
                }
                break;
        }
    }

    public static object Status() => new
    {
        localPaused = LocalPaused,
        groupPaused = GroupPaused,
        guestsPaused = GuestPaused,
        rule = Plugin.PauseWhenPlayersInMenu.Value,
    };

    [HarmonyPatch(typeof(DateManager), nameof(DateManager.IsUpdateEnabled), MethodType.Getter)]
    private static class IsUpdateEnabledPatch
    {
        private static void Postfix(ref bool __result)
        {
            LocalPaused = !__result;
            if (NetSession.Current != NetSession.Mode.Offline && GroupPaused)
                __result = false;
        }
    }
}
