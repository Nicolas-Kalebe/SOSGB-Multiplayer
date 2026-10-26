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

    /// <summary>
    /// A virada do dia está em andamento. Host: do "todos dormindo" até mandar o mundo novo (único momento em que o save
    /// oficial é permitido, D23). Convidado: desde o aviso do host até o mundo novo chegar (D63): não levanta da cama,
    /// não aplica o relógio do dia novo e os módulos guardam as mensagens para depois da recarga.
    /// </summary>
    public static bool InDayChange => NetSession.Current == NetSession.Mode.Client ? _guestDayChanging : _waitingNewDay;
    public static int SleepingCount { get; private set; }
    public static int Total { get; private set; }

    private static readonly HashSet<byte> GuestsSleeping = new();
    private static bool _allowNextDay;   // host: libera a chamada original
    private static bool _waitingNewDay;  // host: virada em andamento; mandar o mundo quando terminar
    private static int _dayBefore;
    private static bool _playerNewDay;   // host: o jogo preparou o personagem para o dia novo (PlayerCharacter.StartNextDay)
    private static float _dayChangeStartedAt;
    private static float _newDaySince = -1;
    private const float DayChangeTimeout = 60f; // segundos reais; depois disso manda o mundo mesmo sem o sinal
    private static bool _guestDayChanging;      // convidado: o host avisou que a virada começou
    private static float _guestDayChangeSince;
    private static bool _evaluateLater;
    private static NetSession.Mode _lastMode = NetSession.Mode.Offline;
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
        WorldSync.OnWorldReceived += () => { LocalSleeping = false; SleepingCount = 0; _guestDayChanging = false; };
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline)
            {
                if (LocalSleeping) LeftSessionInBed();
                GuestsSleeping.Clear();
                _guestDayChanging = false;
                _lastMode = NetSession.Mode.Offline;
                return;
            }
            _lastMode = NetSession.Current;
            if (NetSession.Current == NetSession.Mode.Host)
            {
                if (_evaluateLater && !WorldSnapshot.Capturing) { _evaluateLater = false; Evaluate(); }
                WatchNewDay();
                WorldSync.Tick();
            }
            if (_guestDayChanging && Time.unscaledTime - _guestDayChangeSince > 120f)
            {
                _guestDayChanging = false; // o mundo novo não veio: não fica preso (o host manda o mundo em até 60 s, D58)
                Plugin.Logger.LogWarning("[DaySync] a virada do host não terminou em 120 s; voltando ao normal");
            }
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

    /// <summary>
    /// Levantar da cama enquanto alguém ainda está acordado (pedido do usuário, D34). Só antes de o host
    /// começar a virada. Tira o personagem do estado de sono e avisa o host (SleepReady = false).
    /// mode (testes): "wake" = só ActionWakeUp (NÃO destrava); "flag" = só as marcas; "both" = o certo.
    /// </summary>
    /// <summary>Diagnóstico (testes): estado de sono/controle do jogador local.</summary>
    public static object PlayerState()
    {
        var gc = GameController.Instance;
        var p = gc.GetPlayer();
        string S(Func<object> f) { try { return f()?.ToString(); } catch (Exception e) { return "erro: " + e.Message; } }
        return new
        {
            waitChangeDate = S(() => gc.IsWaitChangeDate()),
            gcSleeping = S(() => gc.IsSleeping),
            isSleep = S(() => p.IsSleep),
            isSleeping = S(() => p.IsSleeping()),
            wakeupState = S(() => p.IsWakeupState()),
            playerState = S(() => p.CurrentUpdatePlayerState),
            defaultState = S(() => p.CurrentUpdateDefaultState),
            fixedState = S(() => p.CurrentFixedUpdateState),
            localSleeping = LocalSleeping,
            moveInput = S(() => p.IsMoveInputEnabled),
        };
    }

    /// <summary>Testes (D34): mexe numa marca de estado do jogador local para achar o que destrava o controle.</summary>
    public static string Fix(string what)
    {
        var gc = GameController.Instance;
        var p = gc.GetPlayer();
        switch (what)
        {
            case "sleep": p.IsSleep = false; break;
            case "move": p.IsMoveInputEnabled = true; break;
            case "wake": p.ActionWakeUp(); break;
            case "wait": gc.SetWaitChangeDate(false); break;
            case "resume": gc.Resume(); break;
            case "init": p.InitializeUpdateState(); break;
            default: return "use: sleep|move|wake|wait|resume|init";
        }
        return "ok";
    }

    public static string WakeUp(string mode = "both")
    {
        if (!LocalSleeping) return "não está dormindo";
        if (InDayChange) return "o dia já está virando";
        Unstick(mode);
        LocalSleeping = false;
        Plugin.Logger.LogInfo($"[DaySync] jogador local levantou da cama ({mode})");
        DevBridge.PushEvent("levantou");
        if (NetSession.Current == NetSession.Mode.Client)
            NetSession.SendGame(MsgType.SleepReady, w => w.Put(false), DeliveryMethod.ReliableOrdered);
        else
            Evaluate();
        return "ok";
    }

    /// <summary>
    /// Desfaz o que o jogo fez ao deitar (testado, D34): animação de sono (ActionWakeUp), marca IsSleep (sem ela o estado
    /// de movimento fica em ForceCleanMove e o personagem não anda), "esperando a virada do dia" (SetWaitChangeDate) e a
    /// pausa do GameController. Ele levanta em cima da cama e sai andando.
    /// </summary>
    private static void Unstick(string mode = "both")
    {
        var gc = GameController.Instance;
        var p = gc.GetPlayer();
        if (mode is "wake" or "both") p.ActionWakeUp();
        if (mode is "flag" or "both") { p.IsSleep = false; p.IsMoveInputEnabled = true; }
        if (gc.IsWaitChangeDate()) gc.SetWaitChangeDate(false);
        gc.Resume();
    }

    /// <summary>
    /// A sessão acabou (desconectou ou caiu) com o jogador deitado esperando os outros: a virada que o jogo pediu tinha
    /// sido segurada e o personagem ficaria preso na cama (revisão do núcleo). Host: agora sozinho, o dia vira como no
    /// jogo sem o mod. Convidado: o mundo é do host (não salva, D59), então só levanta.
    /// </summary>
    private static void LeftSessionInBed()
    {
        LocalSleeping = false;
        try
        {
            if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game || !GameController.IsInstance) return;
            if (_lastMode == NetSession.Mode.Host && !_waitingNewDay)
            {
                Plugin.Logger.LogInfo("[DaySync] sessão encerrada com o host deitado: virando o dia (sozinho)");
                GameController.Instance.StartNextDay();
            }
            else
            {
                Unstick();
                Plugin.Logger.LogInfo("[DaySync] sessão encerrada deitado: levantou da cama");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[DaySync] sair da cama ao desconectar: {e.Message}"); }
    }

    /// <summary>
    /// Espectador (D69): tira o personagem do estado de cama (o jogo não troca de mapa com ele deitado: NullReference em
    /// ChangeField), mas ele continua contando como "dormindo" para o grupo e a virada continua segurada.
    /// </summary>
    public static void LeaveBedKeepSleeping()
    {
        if (LocalSleeping) Unstick();
    }

    /// <summary>Voltando ao título: esquece o "dormindo" sem mexer no personagem (a cena vai ser descarregada).</summary>
    public static void ForgetForTitle()
    {
        LocalSleeping = false;
        _guestDayChanging = false;
        _lastMode = NetSession.Mode.Offline;
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
                if (r.AvailableBytes > 0 && r.GetBool() && !_guestDayChanging)
                {
                    _guestDayChanging = true;
                    _guestDayChangeSince = Time.unscaledTime;
                    Plugin.Logger.LogInfo("[DaySync] o host começou a virada do dia");
                }
                break;
        }
    }

    /// <summary>Host: conta quem dorme, anuncia e, se todos dormem, vira o dia.</summary>
    private static void Evaluate()
    {
        // Teste com UMA instância: SOSGB_TEST_FAKE_AWAKE=1 conta um jogador acordado a mais (segura a virada).
        Total = NetSession.Players.Count + (Plugin.DevMode && Environment.GetEnvironmentVariable("SOSGB_TEST_FAKE_AWAKE") == "1" ? 1 : 0);
        SleepingCount = (LocalSleeping ? 1 : 0) + GuestsSleeping.Count;
        byte s = (byte)SleepingCount, t = (byte)Total;
        var changing = SleepingCount >= Total || _waitingNewDay; // D63: o convidado fica sabendo que a virada começou
        NetSession.SendGame(MsgType.SleepStatus, w => { w.Put(s); w.Put(t); w.Put(changing); }, DeliveryMethod.ReliableOrdered);
        DevBridge.PushEvent($"dormindo:{s}/{t}");
        if (SleepingCount < Total || _waitingNewDay) return;
        if (WorldSnapshot.Capturing) { _evaluateLater = true; return; } // não vira o dia no meio de uma captura do mundo

        Plugin.Logger.LogInfo("[DaySync] todos dormindo: virando o dia");
        _waitingNewDay = true;
        _dayBefore = DateManager.Instance.Now.Day;
        _playerNewDay = false;
        _dayChangeStartedAt = Time.unscaledTime;
        _newDaySince = -1;
        _allowNextDay = true;
        GameController.Instance.StartNextDay();
    }

    /// <summary>
    /// Host: quando o dia virou e o jogo assentou, manda o mundo novo e zera os "dormindo".
    /// O NÚMERO do dia não serve sozinho de sinal: dormindo (ou desmaiando às 5h) depois da meia-noite, o calendário já
    /// está no dia seguinte e o jogo acorda no MESMO número (06:00, ou 10:00 depois do desmaio). Por isso o sinal
    /// principal é o jogo preparar o personagem para o dia novo; o número do dia e um tempo-limite ficam de reserva.
    /// </summary>
    private static void WatchNewDay()
    {
        if (!_waitingNewDay || !DateManager.IsInstance || !GameController.IsInstance) return;
        var changed = _playerNewDay || DateManager.Instance.Now.Day != _dayBefore;
        var timedOut = Time.unscaledTime - _dayChangeStartedAt > DayChangeTimeout;
        if ((!changed && !timedOut) || GameController.Instance.IsWaitChangeDate()) { _newDaySince = -1; return; }
        if (_newDaySince < 0) { _newDaySince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _newDaySince < 3f) return; // deixa o jogo terminar de processar o dia
        if (!changed) Plugin.Logger.LogWarning("[DaySync] a virada não deu sinal em 60 s; mandando o mundo assim mesmo");

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
        playerNewDay = _playerNewDay,
    };

    /// <summary>Sinal de que a virada chegou ao fim: o jogo prepara o personagem para o dia novo (vem depois do save).</summary>
    [HarmonyPatch(typeof(PlayerCharacter), nameof(PlayerCharacter.StartNextDay))]
    private static class PlayerStartNextDayPatch
    {
        private static void Postfix()
        {
            if (_waitingNewDay) _playerNewDay = true;
        }
    }

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
            // UniTask concluída: segura a virada. NÃO usar "default": no interop IL2CPP o UniTask é uma CLASSE
            // (struct não-blittable), então default = null e o jogo cai ao receber o retorno (achado no P2,
            // dormindo pela cama de verdade: NullReferenceException em StartNextDay).
            __result = Cysharp.Threading.Tasks.UniTask.CompletedTask;
            return false;
        }
    }
}
