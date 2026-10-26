// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d26.
using System;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Avisos de multiplayer no sistema de avisos do jogo (GameNotify, D26):
///  - entrou / saiu do mundo; conexão perdida;
///  - foi dormir; desmaiou (cansaço ou sono); entrou em um evento; abriu a barraca do Bazaar (D47).
/// Cada jogador detecta os PRÓPRIOS acontecimentos (por leitura de estado, sem patch) e avisa os
/// outros com PlayerEvent; entrar/sair vem da própria sessão de rede.
/// </summary>
public class NotifySync : MonoBehaviour
{
    public NotifySync(IntPtr ptr) : base(ptr) { }

    public enum EventCode : byte { Slept = 1, Fainted = 2, SleepyFainted = 3, EventStarted = 4, BazaarOpened = 5 }

    private static bool _subscribed;
    private bool _wasFaint, _wasSleepyFaint, _wasInEvent, _wasSleeping, _wasInShop;
    private float _nextPoll;
    private static bool _pendingJoinNotice;
    private static float _returnToTitleAt = -1; // P16/D65: queda de conexão no convidado → menu inicial

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnPlayerJoinedNamed += (id, name) => GameNotify.Show($"{name} entrou no mundo!");
        NetSession.OnPlayerLeftNamed += (id, name) => GameNotify.Show($"{name} saiu do mundo.");
        // O convidado recarrega o mundo logo depois de entrar; o aviso sai depois da recarga.
        NetSession.OnJoined += () => _pendingJoinNotice = true;
        NetSession.OnDisconnectedFromHost += reason =>
        {
            GameNotify.Show($"Conexão com o host perdida ({reason}).");
            if (!WorldSnapshot.HoldingHostWorld) return;
            // Pedido do usuário (P16): o mundo é do host e não é salvo aqui (D59), então volta ao menu inicial.
            GameNotify.Show("Voltando ao menu inicial em alguns segundos (o mundo do host não é salvo aqui).");
            _returnToTitleAt = Time.unscaledTime + 4f;
        };
        NetSession.OnGameMessage += OnMessage;
    }

    /// <summary>P16/D65: o convidado que perdeu a conexão volta ao menu inicial (a volta ao título libera os saves, D59).</summary>
    private static void ReturnToTitle()
    {
        _returnToTitleAt = -1;
        try
        {
            if (NetSession.Current != NetSession.Mode.Offline) return; // reconectou (F9) nesse meio-tempo
            if (!WorldSnapshot.HoldingHostWorld || !SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return;
            Plugin.Logger.LogInfo("[NotifySync] conexão perdida: voltando ao menu inicial");
            SceneManager.Instance.LoadScene(SceneMode.Title, null, null);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NotifySync] voltar ao menu: {e.Message}"); }
    }

    private void Update()
    {
        if (_returnToTitleAt > 0 && Time.unscaledTime >= _returnToTitleAt) ReturnToTitle();
        if (NetSession.Current == NetSession.Mode.Offline || Time.unscaledTime < _nextPoll) return;
        _nextPoll = Time.unscaledTime + 0.25f;
        try
        {
            if (_pendingJoinNotice && PlayerProfiles.WorldApplied)
            {
                _pendingJoinNotice = false;
                var host = NetSession.Players.TryGetValue(Protocol.HostPlayerId, out var h) ? h.Name : "host";
                GameNotify.Show($"Conectado ao mundo de {host}!");
            }
            if (!GameController.IsInstance) return;
            var gc = GameController.Instance;
            var p = gc.GetPlayer();
            if (p == null) return;

            Edge(ref _wasFaint, p.IsFaint, EventCode.Fainted);
            Edge(ref _wasSleepyFaint, p.IsSleepyFaint, EventCode.SleepyFainted);
            Edge(ref _wasInEvent, gc.IsPlayingEvent, EventCode.EventStarted);
            Edge(ref _wasSleeping, DaySync.LocalSleeping, EventCode.Slept);
            // Quem entra na barraca de outro jogador só para ajudar (D56) não "abriu a barraca".
            var seller = StorageSync.BazaarHolder;
            Edge(ref _wasInShop, BazaarManager.IsInstance && BazaarManager.Instance.IsInShop && !BazaarHelpSync.Active
                && (seller == StorageSync.NoHolder || seller == NetSession.LocalId), EventCode.BazaarOpened);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NotifySync] {e.Message}"); }
    }

    /// <summary>Quando um estado passa de falso para verdadeiro, avisa os outros jogadores.</summary>
    private static void Edge(ref bool last, bool now, EventCode code)
    {
        if (now && !last)
        {
            var id = NetSession.LocalId;
            NetSession.SendGame(MsgType.PlayerEvent, w => { w.Put(id); w.Put((byte)code); }, DeliveryMethod.ReliableOrdered);
        }
        last = now;
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.PlayerEvent) return;
        var id = r.GetByte();
        var code = (EventCode)r.GetByte();
        if (id == NetSession.LocalId) return;
        var name = NetSession.Players.TryGetValue(id, out var pl) ? pl.Name : $"Jogador {id}";
        switch (code)
        {
            case EventCode.Slept:
                var extra = DaySync.Total > 0 ? $" ({DaySync.SleepingCount}/{DaySync.Total})" : "";
                GameNotify.Show($"{name} foi dormir{extra}.");
                break;
            case EventCode.Fainted: GameNotify.Show($"{name} desmaiou de cansaço!"); break;
            case EventCode.SleepyFainted: GameNotify.Show($"{name} desmaiou de sono!"); break;
            case EventCode.EventStarted: GameNotify.Show($"{name} entrou em um evento."); break;
            case EventCode.BazaarOpened: GameNotify.Show($"{name} abriu a barraca do Bazaar!"); break;
        }
    }
}
