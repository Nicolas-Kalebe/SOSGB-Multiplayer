// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d68.
using System;
using System.Collections.Generic;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// NPC ocupado durante a conversa (D68; P9, a ideia original da D14). Cada jogo tem a sua cópia de cada morador, e a
/// conversa (corações, conversa do dia) é de cada jogador: não há conflito de dados. O que o usuário pediu é o
/// comportamento de um mundo só:
///  - quem começa a conversar (NonPlayerCharacter.TalkStart) avisa os outros; ao terminar (TalkEnd), avisa de novo;
///  - nos outros jogos, a cópia do morador PARA de andar (PauseObject.Pause(Idle), a pausa que o jogo usa; só o
///    SetNpcTalkStop da conversa não segurava: andou ~3 m em 3 s no teste) e olha para o avatar de quem está conversando,
///    se ele estiver no mesmo mapa. Se o próprio jogo tirar a pausa (ex.: ao fechar um menu), ela volta no tick seguinte;
///  - enquanto isso o morador não aceita conversa dos outros (IsTalkable = falso): um de cada vez.
/// </summary>
public class NpcSync : MonoBehaviour
{
    public NpcSync(IntPtr ptr) : base(ptr) { }

    private static readonly Dictionary<uint, byte> Busy = new();            // morador → quem está conversando (outro jogador)
    private static readonly Dictionary<uint, NonPlayerCharacter> Frozen = new(); // cópias paradas neste jogo
    private static uint _localTalk;
    private static float _talkIdleSince = -1;                                 // quem conversa: tela normal desde (fim garantido)
    private static readonly Dictionary<uint, float> BusySince = new();       // quem recebe: libera sozinho depois de 90 s
    private static UIManager _ui;
    private static bool _subscribed;
    private float _nextTick;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerLeft += id =>
        {
            var free = new List<uint>();
            foreach (var kv in Busy) if (kv.Value == id) free.Add(kv.Key);
            foreach (var n in free) SetBusy(n, id, false);
        };
        NetSession.OnHosting += ResetAll;
        NetSession.OnJoined += ResetAll;
        WorldSync.OnWorldReceived += ResetAll;
    }

    private static void ResetAll()
    {
        foreach (var n in new List<uint>(Frozen.Keys)) Unfreeze(n);
        if (_holdKey != null) { NpcHold.ReleaseAll(_holdKey); _holdKey = null; }
        Busy.Clear();
        BusySince.Clear();
        _localTalk = 0;
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline) { if (Busy.Count > 0 || Frozen.Count > 0) ResetAll(); return; }
            NpcHold.Tick(); // a cada quadro: o jogo religa o agente entre um tick e outro (D90)
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            // Garantia do fim da conversa: se o jogo não chamar TalkEnd por algum caminho, a tela de volta ao normal por
            // 3 s encerra; e quem recebe libera sozinho depois de 90 s (o morador nunca fica preso nos outros jogos).
            if (_localTalk != 0)
            {
                if (_ui == null) _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
                var normal = _ui != null && _ui.CurrentUIType.ToString() == "Main" && !_ui.IsMenu;
                if (!normal) _talkIdleSince = -1;
                else if (_talkIdleSince < 0) _talkIdleSince = Time.unscaledTime;
                else if (Time.unscaledTime - _talkIdleSince > 3f) { Send(_localTalk, false); _localTalk = 0; _talkIdleSince = -1; }
            }
            foreach (var n in new List<uint>(BusySince.Keys))
                if (Time.unscaledTime - BusySince[n] > 90f && Busy.TryGetValue(n, out var who)) SetBusy(n, who, false);
            // Cópias que somem (troca de mapa) saem da lista; moradores ocupados que aparecem (entrei no mapa) param.
            foreach (var n in new List<uint>(Frozen.Keys))
                if (Frozen[n] == null || !Busy.ContainsKey(n)) Unfreeze(n);
            foreach (var kv in Busy) Freeze(kv.Key, kv.Value);
            HoldNpcsWhileGroupHeld();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] {e.Message}"); }
    }

    private static bool FestivalDay()
    {
        try { return FestivalExecutor.IsInstance && FestivalExecutor.Instance.IsInSession; } catch { return false; }
    }

    private static string _holdKey;              // NpcHold.KeyPause ou KeyFestival enquanto seguramos o mapa
    private static float _heldOffSince = -1;

    /// <summary>
    /// Menu de pausa (Esc) de QUALQUER jogador, inclusive o daqui (D89). O relógio do grupo para (D50) e os personagens
    /// também (D74), mas os moradores seguiam andando nos dois jogos (visto pelo usuário): no de quem pausou, o
    /// KeepNpcsWalking os soltava; no dos outros, nada os parava. O próprio menu daqui vale na hora (o PausedBy do host
    /// chega com atraso para o convidado).
    /// </summary>
    private static bool GroupPauseMenu() =>
        ClockSync.InPauseMenu || ClockSync.PauseMenuOpenNow() || (ClockSync.GroupPaused && ClockSync.PausedBy != NetSession.UnknownId);

    /// <summary>
    /// D83/D89: o tempo do grupo está parado (menu de pausa de alguém, ou cena do festival de outro jogador). No jogo
    /// sozinho o mundo para junto; aqui seguramos cada morador do mapa pelo NpcHold (D90: a pausa por objeto sozinha não
    /// segurava, medido). Menu de pausa congela também a animação, como o Esc do jogo. Volta quando o tempo fica solto:
    /// na hora para o menu, depois de 2 s para o festival (entre uma fala e outra a trava some por um instante, e liberar
    /// na hora fazia os moradores darem uns passos e pararem de novo).
    /// </summary>
    private static void HoldNpcsWhileGroupHeld()
    {
        var festival = ClockSync.HeldBy != NetSession.UnknownId && ClockSync.HeldBy != NetSession.LocalId;
        var pause = GroupPauseMenu();
        if (pause || festival)
        {
            _heldOffSince = -1;
            var key = pause ? NpcHold.KeyPause : NpcHold.KeyFestival;
            if (_holdKey != null && _holdKey != key) NpcHold.ReleaseAll(_holdKey);
            var first = _holdKey != key;
            _holdKey = key;
            var n = 0;
            try
            {
                foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
                {
                    if (npc == null || !npc.gameObject.activeInHierarchy || npc.CharacterId == _localTalk) continue;
                    if (!NpcHold.IsHeldBy(npc, key)) { NpcHold.Hold(npc, key, freezeAnim: pause); n++; }
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] parar moradores: {e.Message}"); }
            if (first) Plugin.Logger.LogInfo($"[NpcSync] tempo do grupo parado ({(pause ? "menu de pausa" : "festival")}): {n} morador(es) seguro(s)");
            return;
        }
        if (_holdKey == null) return;
        if (_holdKey == NpcHold.KeyFestival)
        {
            if (_heldOffSince < 0) { _heldOffSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _heldOffSince < 2f) return;
        }
        _heldOffSince = -1;
        NpcHold.ReleaseAll(_holdKey);
        Plugin.Logger.LogInfo($"[NpcSync] tempo do grupo voltou: moradores soltos ({_holdKey})");
        _holdKey = null;
    }

    private static NonPlayerCharacter Find(uint charaId)
    {
        foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
            if (npc != null && npc.CharacterId == charaId && npc.gameObject.activeInHierarchy) return npc;
        return null;
    }

    private static void Freeze(uint charaId, byte talker)
    {
        if (!Frozen.TryGetValue(charaId, out var npc) || npc == null)
        {
            npc = Find(charaId);
            if (npc == null) return; // o morador não está neste mapa
            Frozen[charaId] = npc;
            Plugin.Logger.LogInfo($"[NpcSync] morador {charaId} parado (conversando com #{talker})");
        }
        try
        {
            // Só a pausa Idle não segurava: o morador ficava "tentando sair do lugar" e escorregava devagar (visto pelo
            // usuário; medido na D90). O NpcHold desliga também o script da agenda e o agente.
            if (!NpcHold.IsHeldBy(npc, NpcHold.KeyTalk)) NpcHold.Hold(npc, NpcHold.KeyTalk);
            npc.NPCSchedule?.SetNpcTalkStop(true);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] parar {charaId}: {e.Message}"); }
        if (PlayerSync.TryGetRemotePosition(talker, out var pos))
            try { npc.LookAtTarget(pos + Vector3.up * 1.2f); } catch { }
    }

    private static void Unfreeze(uint charaId)
    {
        if (Frozen.TryGetValue(charaId, out var npc) && npc != null)
        {
            try { npc.NPCSchedule?.SetNpcTalkStop(false); npc.ResetLookAt(); } catch { }
            NpcHold.Release(npc, NpcHold.KeyTalk);
            Plugin.Logger.LogInfo($"[NpcSync] morador {charaId} liberado");
        }
        Frozen.Remove(charaId);
    }

    private static void SetBusy(uint charaId, byte talker, bool talking)
    {
        if (talker == NetSession.LocalId) return;
        if (talking) { Busy[charaId] = talker; BusySince[charaId] = Time.unscaledTime; }
        else if (Busy.TryGetValue(charaId, out var who) && who == talker) { Busy.Remove(charaId); BusySince.Remove(charaId); }
        if (talking) Freeze(charaId, talker); else if (!Busy.ContainsKey(charaId)) Unfreeze(charaId);
    }

    private static void Send(uint charaId, bool talking)
    {
        var me = NetSession.LocalId;
        NetSession.SendGame(MsgType.NpcTalk, w => { w.Put(me); w.Put(charaId); w.Put(talking); }, DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.NpcTalk) return;
        var talker = r.GetByte();
        var charaId = r.GetUInt();
        var talking = r.GetBool();
        if (NetSession.Current == NetSession.Mode.Host)
        {
            if (talker != from) return;
            // As mensagens 20+ só chegam ao host: ele repassa aos outros convidados.
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId && id != from)
                    NetSession.SendTo(id, MsgType.NpcTalk, w => { w.Put(talker); w.Put(charaId); w.Put(talking); }, DeliveryMethod.ReliableOrdered);
        }
        else if (from != Protocol.HostPlayerId) return;
        SetBusy(charaId, talker, talking);
    }

    public static object Status() => new { busy = Busy, frozen = new List<uint>(Frozen.Keys), localTalk = _localTalk, held = NpcHold.Count, holdKey = _holdKey, positions = NpcPosSync.Status() };

    /// <summary>Morador com quem ESTE jogador está conversando (0 = nenhum). A NpcPosSync o devolve ao jogo daqui (D90).</summary>
    public static uint LocalTalk => _localTalk;



    [HarmonyPatch(typeof(NonPlayerCharacter), nameof(NonPlayerCharacter.TalkStart))]
    private static class TalkStartPatch
    {
        private static void Postfix(NonPlayerCharacter __instance)
        {
            if (NetSession.Current == NetSession.Mode.Offline || __instance == null) return;
            try
            {
                var id = __instance.CharacterId;
                if (id == 0 || id == _localTalk) return;
                if (_localTalk != 0) Send(_localTalk, false);
                _localTalk = id;
                _talkIdleSince = -1;
                Send(id, true);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] início da conversa: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(NonPlayerCharacter), nameof(NonPlayerCharacter.TalkEnd))]
    private static class TalkEndPatch
    {
        private static void Postfix(NonPlayerCharacter __instance)
        {
            if (NetSession.Current == NetSession.Mode.Offline || _localTalk == 0) return;
            try
            {
                var id = __instance != null ? __instance.CharacterId : _localTalk;
                if (id != _localTalk) return;
                _localTalk = 0;
                Send(id, false);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] fim da conversa: {e.Message}"); }
        }
    }

    /// <summary>
    /// No jogo sozinho, conversa e menus pausam TODOS os moradores (GameController.Pause/PauseTalk → Pauser): o mundo
    /// para. No multiplayer o tempo do grupo continua (D50), então os moradores de quem conversava ficavam parados e
    /// atrasados em relação aos outros jogos (visto pelo usuário). Depois da pausa do jogo, tiramos a pausa dos moradores,
    /// menos os ocupados por outro jogador; o da conversa o jogo não pausa (ele está conversando). O menu de pausa (Esc)
    /// continua parando tudo: ele pausa o grupo inteiro (D89: e os moradores ficam parados, ver GroupPauseMenu).
    /// </summary>
    private static void KeepNpcsWalking()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return;
        if (GroupPauseMenu())
        {
            if (Plugin.DevMode) Plugin.Logger.LogInfo("[NpcSync] pausa do jogo com menu de pausa: moradores continuam parados");
            return;
        }
        try
        {
            var pausing = GameController.Instance.pauser?.pausingObjects;
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
            {
                if (npc == null || Frozen.ContainsKey(npc.CharacterId) || NpcHold.IsHeld(npc)) continue;
                var po = npc.PauseObject;
                if (po == null || !po.IsPaused) continue;
                po.Resume();
                pausing?.Remove(po); // o "despausar" do jogo no fim não mexe mais nele
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] manter moradores andando: {e.Message}"); }
    }

    [HarmonyPatch(typeof(GameController), nameof(GameController.Pause))]
    private static class GamePausePatch
    {
        private static void Postfix() => KeepNpcsWalking();
    }

    [HarmonyPatch(typeof(GameController), nameof(GameController.PauseTalk))]
    private static class PauseTalkPatch
    {
        private static void Postfix() => KeepNpcsWalking();
    }

    /// <summary>Morador conversando com outro jogador não aceita conversa daqui (um de cada vez, D14).</summary>
    [HarmonyPatch(typeof(NonPlayerCharacter), nameof(NonPlayerCharacter.IsTalkable))]
    private static class IsTalkablePatch
    {
        private static void Postfix(NonPlayerCharacter __instance, ref bool __result)
        {
            if (!__result || __instance == null) return;
            if (Busy.Count > 0 && Busy.ContainsKey(__instance.CharacterId)) __result = false;
            else if (ClockSync.FestivalHostBusy(__instance.CharacterId)) __result = false; // D83/P13: outro na cerimônia
        }
    }
}
