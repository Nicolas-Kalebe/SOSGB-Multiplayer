// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d5 / #d44.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Romance exclusivo (6.4, D5/D44): um NPC noivo ou casado com um jogador fica indisponível para os outros.
///
///  - Host: a cada 2 s monta o registro NPC → jogador a partir do próprio LikeabilityManager (FianceCharaId,
///    PartnerCharaId) e dos perfis guardados dos convidados (parte "likeability"). Mudou → manda a todos (RomanceClaims).
///  - Todos: patches em LikeabilityManager.CheckMarryable (pode oferecer o item de compromisso?) e SetFiance
///    (noivado) recusam um NPC comprometido com OUTRO jogador, com um aviso no sistema do jogo.
///  - Convidado: quando o próprio noivado/casamento muda, envia o perfil na hora (o host atualiza o registro).
/// Jogando sozinho, nada muda.
/// </summary>
public class RomanceSync : MonoBehaviour
{
    public RomanceSync(IntPtr ptr) : base(ptr) { }

    /// <summary>NPC (charaId) → nome do jogador comprometido. Inclui o próprio jogador (com o nome dele).</summary>
    public static readonly Dictionary<uint, string> Claims = new();
    private static string _lastSent = "";
    private static (uint fiance, uint partner) _lastLocal;
    private static bool _subscribed;
    private float _next;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnPlayerJoinedNamed += (id, name) => _lastSent = ""; // reenviar para quem entrou
    }

    private static bool Ready() =>
        NetSession.Current != NetSession.Mode.Offline && LikeabilityManager.IsInstance
        && SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game;

    private void Update()
    {
        try
        {
            if (!Ready() || Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2f;
            var lm = LikeabilityManager.Instance;
            var local = (lm.FianceCharaId, lm.PartnerCharaId);
            if (local != _lastLocal)
            {
                _lastLocal = local;
                if (NetSession.Current == NetSession.Mode.Client) PlayerProfiles.Upload(); // host atualiza o registro
            }
            if (NetSession.Current == NetSession.Mode.Host) HostRebuild();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Romance] {e.Message}"); }
    }

    private static void HostRebuild()
    {
        var claims = new Dictionary<uint, string>();
        var lm = LikeabilityManager.Instance;
        var hostName = NetSession.Players.TryGetValue(Protocol.HostPlayerId, out var hp) ? hp.Name : "host";
        if (lm.PartnerCharaId != 0) claims[lm.PartnerCharaId] = hostName;
        if (lm.FianceCharaId != 0) claims[lm.FianceCharaId] = hostName;
        foreach (var kv in PlayerProfiles.StoredProfiles())
        {
            try
            {
                var parts = JsonSerializer.Deserialize<Dictionary<string, string>>(kv.Value);
                if (!parts.TryGetValue("likeability", out var lj)) continue;
                var o = JsonNode.Parse(lj)!;
                var name = PlayerProfiles.NameOfGuid(kv.Key) ?? "outro jogador";
                foreach (var key in new[] { "PartnerCharaId", "FianceCharaId" })
                {
                    var id = o[key]?.GetValue<uint>() ?? 0;
                    if (id != 0 && !claims.ContainsKey(id)) claims[id] = name;
                }
            }
            catch { /* perfil antigo/incompleto */ }
        }
        var text = string.Join(";", claims.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"));
        Claims.Clear();
        foreach (var c in claims) Claims[c.Key] = c.Value;
        if (text == _lastSent) return;
        _lastSent = text;
        NetSession.SendGame(MsgType.RomanceClaims, w => w.Put(text), DeliveryMethod.ReliableOrdered);
        Plugin.Logger.LogInfo($"[Romance] registro: {(text.Length == 0 ? "(ninguém comprometido)" : text)}");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.RomanceClaims || NetSession.Current != NetSession.Mode.Client || from != Protocol.HostPlayerId) return;
        var text = r.GetString();
        Claims.Clear();
        foreach (var part in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            if (i > 0 && uint.TryParse(part.Substring(0, i), out var id)) Claims[id] = part.Substring(i + 1);
        }
        DevBridge.PushEvent($"romance:{Claims.Count}");
    }

    /// <summary>Nome de OUTRO jogador comprometido com o NPC (null = livre ou comprometido comigo).</summary>
    public static string ClaimedByOther(uint charaId)
    {
        if (NetSession.Current == NetSession.Mode.Offline || !Claims.TryGetValue(charaId, out var who)) return null;
        var lm = LikeabilityManager.IsInstance ? LikeabilityManager.Instance : null;
        if (lm != null && (lm.FianceCharaId == charaId || lm.PartnerCharaId == charaId)) return null; // é comigo
        return who;
    }

    private static float _lastNotice;

    private static void Notice(uint charaId, string who)
    {
        if (Time.unscaledTime - _lastNotice < 5f) return;
        _lastNotice = Time.unscaledTime;
        GameNotify.Show($"Esse coração já tem dono: está comprometido(a) com {who}.");
        Plugin.Logger.LogInfo($"[Romance] romance com o NPC {charaId} bloqueado (comprometido com {who})");
        DevBridge.PushEvent($"romance:bloqueado:{charaId}");
    }

    [HarmonyPatch(typeof(LikeabilityManager), nameof(LikeabilityManager.CheckMarryable))]
    private static class CheckMarryablePatch
    {
        private static void Postfix(uint charaId, ref bool __result)
        {
            if (!__result) return;
            var who = ClaimedByOther(charaId);
            if (who == null) return;
            __result = false;
            Notice(charaId, who);
        }
    }

    [HarmonyPatch(typeof(LikeabilityManager), nameof(LikeabilityManager.SetFiance))]
    private static class SetFiancePatch
    {
        private static bool Prefix(uint charaId)
        {
            var who = ClaimedByOther(charaId);
            if (who == null) return true;
            Notice(charaId, who);
            return false;
        }
    }

    public static object Status()
    {
        var d = new Dictionary<string, string>();
        foreach (var c in Claims) d[c.Key.ToString()] = c.Value;
        return new { claims = d };
    }
}
