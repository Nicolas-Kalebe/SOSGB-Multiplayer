// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d12 / #d23 e ROTEIRO.md (3.4/3.5).
using System;
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Dormir individual (3.4, D12) e virada do dia controlada pelo host (3.5, D23).
///
/// No jogo, dormir termina em GameController.StartNextDay() (confirmado em runtime, ver CODIGO.md).
/// No multiplayer, um patch nesse método SEGURA a virada:
///  - quem chama (deitou na cama) fica "dormindo" e esperando; a virada original não roda;
///  - o convidado avisa o host (SleepReady); o host conta e anuncia "dormindo X/Y" (SleepStatus);
///  - quando TODOS estão dormindo, o host roda a virada original (o jogo processa o dia e faz o
///    save automático, que é o save oficial do D23) e depois manda o mundo novo para cada convidado
///    (WorldSync), que recarrega já no dia seguinte.
/// Jogando sozinho (sem rede), nada muda.
/// </summary>
public class DaySync : MonoBehaviour
{
    public DaySync(IntPtr ptr) : base(ptr) { }

    public static bool LocalSleeping { get; private set; }
    public static int SleepingCount { get; private set; }
    public static int Total { get; private set; }

    private static readonly HashSet<byte> GuestsSleeping = new();
    private static bool _allowNextDay;   // host: libera a chamada original
    private static bool _waitingNewDay;  // host: virada em andamento; mandar o mundo quando terminar
    private static int _dayBefore;
    private static float _newDaySince = -1;
    private static bool _subscribed;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id =>
        {
            GuestsSleeping.Remove(id);
            if (NetSession.Current == NetSession.Mode.Host) Evaluate();
        };
        WorldSync.OnWorldReceived += () => { LocalSleeping = false; SleepingCount = 0; };
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline) { LocalSleeping = false; GuestsSleeping.Clear(); return; }
            if (NetSession.Current == NetSession.Mode.Host) WatchNewDay();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[DaySync] {e.Message}"); }
    }

    /// <summary>Chamado pelo patch quando o jogador local tenta virar o dia (deitou).</summary>
    private static void LocalWantsToSleep()
    {
        if (LocalSleeping) return;
        LocalSleeping = true;
        Plugin.Logger.LogInfo("[DaySync] jogador local dormindo; esperando os outros");
        DevBridge.PushEvent("dormindo:local");
        if (NetSession.Current == NetSession.Mode.Client)
        {
            PlayerProfiles.Upload(); // o host grava o perfil junto com o save do dia (D23)
            NetSession.SendGame(MsgType.SleepReady, w => w.Put(true), DeliveryMethod.ReliableOrdered);
        }
        else
            Evaluate();
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.SleepReady when NetSession.Current == NetSession.Mode.Host:
                if (r.GetBool()) GuestsSleeping.Add(from); else GuestsSleeping.Remove(from);
                Evaluate();
                break;
            case MsgType.SleepStatus when NetSession.Current == NetSession.Mode.Client:
                SleepingCount = r.GetByte();
                Total = r.GetByte();
                break;
        }
    }

    /// <summary>Host: conta quem dorme, anuncia e, se todos dormem, vira o dia.</summary>
    private static void Evaluate()
    {
        Total = NetSession.Players.Count;
        SleepingCount = (LocalSleeping ? 1 : 0) + GuestsSleeping.Count;
        byte s = (byte)SleepingCount, t = (byte)Total;
        NetSession.SendGame(MsgType.SleepStatus, w => { w.Put(s); w.Put(t); }, DeliveryMethod.ReliableOrdered);
        DevBridge.PushEvent($"dormindo:{s}/{t}");
        if (SleepingCount < Total || _waitingNewDay) return;

        Plugin.Logger.LogInfo("[DaySync] todos dormindo: virando o dia");
        _waitingNewDay = true;
        _dayBefore = DateManager.Instance.Now.Day;
        _newDaySince = -1;
        _allowNextDay = true;
        GameController.Instance.StartNextDay();
    }

    /// <summary>Host: quando o dia virou e o jogo assentou, manda o mundo novo e zera os "dormindo".</summary>
    private static void WatchNewDay()
    {
        if (!_waitingNewDay || !DateManager.IsInstance || !GameController.IsInstance) return;
        if (DateManager.Instance.Now.Day == _dayBefore || GameController.Instance.IsWaitChangeDate()) return;
        if (_newDaySince < 0) { _newDaySince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _newDaySince < 3f) return; // deixa o jogo terminar de processar o dia

        _waitingNewDay = false;
        LocalSleeping = false;
        GuestsSleeping.Clear();
        SleepingCount = 0;
        Plugin.Logger.LogInfo("[DaySync] dia novo; enviando o mundo aos convidados");
        DevBridge.PushEvent("dia-novo");
        PlayerProfiles.SaveStore();
        WorldSync.BroadcastWorld();
    }

    public static object Status() => new
    {
        localSleeping = LocalSleeping,
        sleeping = SleepingCount,
        total = Total,
        hostWaitingNewDay = _waitingNewDay,
    };

    [HarmonyPatch(typeof(GameController), nameof(GameController.StartNextDay))]
    private static class StartNextDayPatch
    {
        private static bool Prefix(ref Cysharp.Threading.Tasks.UniTask __result)
        {
            if (NetSession.Current == NetSession.Mode.Offline) return true; // sozinho: normal
            if (NetSession.Current == NetSession.Mode.Host && _allowNextDay)
            {
                _allowNextDay = false;
                return true; // virada liberada pelo host (todos dormindo)
            }
            LocalWantsToSleep();
            __result = default; // UniTask concluída: segura a virada
            return false;
        }
    }
}
