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
        HoldPaused.Clear();
        Busy.Clear();
        BusySince.Clear();
        _localTalk = 0;
    }

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Offline) { if (Busy.Count > 0 || Frozen.Count > 0) ResetAll(); return; }
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

    private static readonly List<PauseObject> HoldPaused = new(); // pausados por nós (tempo do grupo segurado por outro)
    private static float _heldOffSince = -1;

    /// <summary>
    /// Os mesmos grupos que a pausa do jogo (Pauser.PauseAllPausableTargets) pausa: moradores e os pausáveis do Bazaar (os
    /// visitantes do festival). A pausa geral do jogo não serve: ela trava o controle do jogador (testado pelo usuário).
    /// </summary>
    private static IEnumerable<IPausable> GamePausables()
    {
        var npcm = ScheduleManager.IsInstance ? ScheduleManager.Instance.NPCManager : null;
        if (npcm != null)
        {
            var l = npcm.GetPausables(null);
            if (l != null) foreach (var x in l) yield return x;
        }
        if (BazaarManager.IsInstance)
        {
            var l = BazaarManager.Instance.GetPausables(null);
            if (l != null) foreach (var x in l) yield return x;
        }
    }

    /// <summary>
    /// D83: outro jogador segura o tempo do grupo (diálogo ou cena em dia de festival). Aqui o relógio também para, mas os
    /// moradores seguiam andando: pausar só os moradores não segurava os visitantes do festival (medido: 7 a 10 andando,
    /// ~1,3 m/s). Pausamos também os pausáveis do Bazaar, objeto por objeto (sem a pausa geral, que trava o jogador), e
    /// reaplicamos se o jogo desfizer. Volta quando o tempo fica solto por 2 s (entre uma fala e outra a trava some por um
    /// instante, e liberar na hora fazia os moradores darem uns passos e pararem de novo).
    /// </summary>
    private static void HoldNpcsWhileGroupHeld()
    {
        var held = ClockSync.HeldBy != NetSession.UnknownId && ClockSync.HeldBy != NetSession.LocalId;
        if (held)
        {
            _heldOffSince = -1;
            var n = 0;
            try
            {
                foreach (var pa in GamePausables())
                {
                    var po = pa?.PauseObject;
                    if (po == null || po.IsPaused) continue;
                    po.Pause(PauseType.Idle);
                    if (!HoldPaused.Contains(po)) HoldPaused.Add(po);
                    n++;
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[NpcSync] parar moradores: {e.Message}"); }
            if (n > 0 && HoldPaused.Count == n) Plugin.Logger.LogInfo($"[NpcSync] tempo do grupo segurado por outro jogador: {n} parado(s)");
            return;
        }
        if (HoldPaused.Count == 0) return;
        if (_heldOffSince < 0) { _heldOffSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - _heldOffSince < 2f) return;
        _heldOffSince = -1;
        // se o próprio jogo está numa tela que pausa (menu, conversa daqui), deixa: ele mesmo retoma ao fechar
        if (!ClockSync.LocalUiPause)
            foreach (var po in HoldPaused)
                try { if (po != null && po.IsPaused) po.Resume(); } catch { }
        Plugin.Logger.LogInfo($"[NpcSync] tempo do grupo voltou: {HoldPaused.Count} solto(s)");
        HoldPaused.Clear();
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
            var po = npc.PauseObject;
            if (po != null && !po.IsPaused) po.Pause(PauseType.Idle);
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
            try { if (npc.PauseObject != null && npc.PauseObject.IsPaused) npc.PauseObject.Resume(); } catch { }
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

    public static object Status() => new { busy = Busy, frozen = new List<uint>(Frozen.Keys), localTalk = _localTalk };

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
    /// continua parando tudo: ele pausa o grupo inteiro.
    /// </summary>
    private static void KeepNpcsWalking()
    {
        if (NetSession.Current == NetSession.Mode.Offline || ClockSync.InPauseMenu) return;
        try
        {
            var pausing = GameController.Instance.pauser?.pausingObjects;
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
            {
                if (npc == null || Frozen.ContainsKey(npc.CharacterId)) continue;
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
