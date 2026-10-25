// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d42.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Estado compartilhado genérico (5.4, D42): gerenciadores do jogo com ToSaveData/FromSaveData cujo estado é do
/// MUNDO (moinhos, apicultura, cogumelos, vasos de flor, pets, cavalo...).
///
///  - A cada 1 s, cada parte é comparada (JSON sem os campos de movimento, em qualquer nível) com a última versão
///    sincronizada. Mudou → o JSON completo vai ao host, que aplica e repassa.
///  - Quem recebe mescla: objetos com "Guid" mantêm os campos de movimento LOCAIS (pets não pulam de lugar).
///  - Uma parte que falhar (tipo que o JsonToObject do jogo não monta, etc.) é desligada sozinha e registrada.
/// </summary>
public class SharedSync : MonoBehaviour
{
    public SharedSync(IntPtr ptr) : base(ptr) { }

    public sealed class Part
    {
        public string Name;
        public Func<bool> Available;
        public Func<string> Capture;
        public Action<string> Apply;
        public bool Disabled;
    }

    /// <summary>Campos de movimento/IA (em qualquer nível do JSON): fora da comparação, mantidos locais ao mesclar.</summary>
    private static readonly HashSet<string> Volatile = new()
    {
        "Position", "Rotation", "ScheduleLayerId", "Sleep", "prevDateTime", "MoveInfos", "Order", "LocationData",
    };

    private static readonly List<Part> Parts = new()
    {
        Make("windmill", () => WindmillCraftingManager.IsInstance,
            () => StringExtensions.ToJson(WindmillCraftingManager.Instance.ToSaveData()),
            j => WindmillCraftingManager.Instance.FromSaveData(StringExtensions.JsonToObject<WindmillCraftingManager.SaveData>(j))),
        Make("beekeeping", () => BeekeepingManager.IsInstance,
            () => StringExtensions.ToJson(BeekeepingManager.Instance.ToSaveData()),
            j => BeekeepingManager.Instance.FromSaveData(StringExtensions.JsonToObject<BeekeepingManager.SaveData>(j))),
        Make("mushroom", () => MushroomFarmingManager.IsInstance,
            () => StringExtensions.ToJson(MushroomFarmingManager.Instance.ToSaveData()),
            j => MushroomFarmingManager.Instance.FromSaveData(StringExtensions.JsonToObject<MushroomFarmingData>(j))),
        Make("flowerpot", () => FlowerPotManager.IsInstance,
            () => StringExtensions.ToJson(FlowerPotManager.Instance.ToSaveData()),
            j => FlowerPotManager.Instance.FromSaveData(StringExtensions.JsonToObject<FlowerPotManager.SaveData>(j))),
        // Ampliações (casa, armazém, cama, mochila, canteiros...): reservadas no carpinteiro, concluídas na virada (5.5).
        Make("expansion", () => ExpansionManager.IsInstance,
            () => StringExtensions.ToJson(ExpansionManager.Instance.ToSaveData()),
            j => ExpansionManager.Instance.FromSaveData(StringExtensions.JsonToObject<ExpansionData>(j))),
        Make("pets", () => AnimalManager.IsInstance && AnimalManager.Instance.PetAnimalController != null,
            () => StringExtensions.ToJson(AnimalManager.Instance.PetAnimalController.ToSaveData()),
            j => AnimalManager.Instance.PetAnimalController.FromSaveData(StringExtensions.JsonToObject<BokuMono.Animal.PetAnimalController.SaveData>(j))),
        Make("horse", () => AnimalManager.IsInstance && AnimalManager.Instance.RideableAnimalController != null,
            () => StringExtensions.ToJson(AnimalManager.Instance.RideableAnimalController.ToSaveData()),
            j => AnimalManager.Instance.RideableAnimalController.FromSaveData(StringExtensions.JsonToObject<BokuMono.Animal.RideableAnimalController.SaveData>(j))),
    };

    private static Part Make(string name, Func<bool> available, Func<string> capture, Action<string> apply) =>
        new() { Name = name, Available = available, Capture = capture, Apply = apply };

    private static readonly Dictionary<string, string> Baseline = new();
    private static bool _subscribed;
    private float _nextTick;
    public static int Sent, Received;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => Baseline.Clear();
        NetSession.OnHosting += () => Baseline.Clear();
        NetSession.OnJoined += () => Baseline.Clear();
    }

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    public static string Signature(string json)
    {
        var node = JsonNode.Parse(json);
        Strip(node);
        return node?.ToJsonString() ?? "";
    }

    private static void Strip(JsonNode node)
    {
        if (node is JsonObject o)
        {
            foreach (var k in o.Select(kv => kv.Key).ToList())
                if (Volatile.Contains(k)) o.Remove(k); else Strip(o[k]);
        }
        else if (node is JsonArray a) foreach (var e in a) Strip(e);
    }

    /// <summary>Copia os campos voláteis do JSON local para os objetos de mesmo Guid do JSON recebido.</summary>
    public static string Merge(string incoming, string local)
    {
        var byGuid = new Dictionary<string, JsonObject>();
        void Index(JsonNode n)
        {
            if (n is JsonObject o)
            {
                if (o["Guid"] is JsonValue gv && gv.TryGetValue<string>(out var g) && !string.IsNullOrEmpty(g)) byGuid[g] = o;
                foreach (var kv in o) Index(kv.Value);
            }
            else if (n is JsonArray a) foreach (var e in a) Index(e);
        }
        var loc = JsonNode.Parse(local);
        Index(loc);
        var inc = JsonNode.Parse(incoming);
        void Fix(JsonNode n)
        {
            if (n is JsonObject o)
            {
                if (o["Guid"] is JsonValue gv && gv.TryGetValue<string>(out var g) && byGuid.TryGetValue(g, out var lo))
                    foreach (var k in Volatile)
                        if (lo[k] != null) o[k] = JsonNode.Parse(lo[k]!.ToJsonString());
                foreach (var kv in o.ToList()) Fix(kv.Value);
            }
            else if (n is JsonArray a) foreach (var e in a) Fix(e);
        }
        Fix(inc);
        return inc!.ToJsonString();
    }

    private void Update()
    {
        try
        {
            if (!Ready() || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            foreach (var p in Parts)
            {
                if (p.Disabled || !p.Available()) continue;
                string json;
                try { json = p.Capture(); }
                catch (Exception e) { Disable(p, "capturar", e); continue; }
                var sig = Signature(json);
                if (!Baseline.TryGetValue(p.Name, out var old)) { Baseline[p.Name] = sig; continue; }
                if (old == sig) continue;
                Baseline[p.Name] = sig;
                var packed = LetterSync.Pack(json);
                var name = p.Name;
                if (NetSession.Current == NetSession.Mode.Host) SendToClients(name, packed, except: NetSession.LocalId);
                else NetSession.SendGame(MsgType.SharedState, w => { w.Put(name); w.PutBytesWithLength(packed); }, DeliveryMethod.ReliableOrdered);
                Sent++;
                Plugin.Logger.LogInfo($"[SharedSync] {name} mudou: enviado ({packed.Length} bytes)");
                DevBridge.PushEvent($"compartilhado:enviado:{name}");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[SharedSync] {e.Message}"); }
    }

    private static void Disable(Part p, string what, Exception e)
    {
        p.Disabled = true;
        Plugin.Logger.LogWarning($"[SharedSync] parte '{p.Name}' desligada (falha ao {what}): {e.Message}");
    }

    private static void SendToClients(string name, byte[] packed, byte except)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id != except)
                NetSession.SendTo(id, MsgType.SharedState, w => { w.Put(name); w.PutBytesWithLength(packed); }, DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.SharedState) return;
        var name = r.GetString();
        var packed = r.GetBytesWithLength();
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        if (!Ready()) return;
        var res = ApplyPart(name, LetterSync.Unpack(packed));
        if (res == "ok") Received++;
        if (NetSession.Current == NetSession.Mode.Host) SendToClients(name, packed, except: from);
        Plugin.Logger.LogInfo($"[SharedSync] {name} de #{from}: {res}");
        DevBridge.PushEvent($"compartilhado:recebido:{name}:{res}");
    }

    /// <summary>Aplica uma parte (mesclando com o estado local). Também usado nos testes (ponte).</summary>
    public static string ApplyPart(string name, string json, bool markSynced = true)
    {
        var p = Parts.FirstOrDefault(x => x.Name == name);
        if (p == null) return "parte desconhecida";
        if (p.Disabled) return "parte desligada";
        if (!p.Available()) return "indisponível agora";
        try
        {
            var merged = Merge(json, p.Capture());
            p.Apply(merged);
            if (markSynced) Baseline[name] = Signature(p.Capture());
            return "ok";
        }
        catch (Exception e) { Disable(p, "aplicar", e); return "erro: " + e.Message; }
    }

    /// <summary>Teste: captura e reaplica cada parte; diz se o estado ficou igual.</summary>
    public static object SelfTest()
    {
        var report = new Dictionary<string, string>();
        foreach (var p in Parts)
        {
            if (!p.Available()) { report[p.Name] = "indisponível"; continue; }
            try
            {
                var before = p.Capture();
                var res = ApplyPart(p.Name, before);
                var same = Signature(p.Capture()) == Signature(before);
                report[p.Name] = $"{res}; igual depois={same}; {before.Length} chars";
            }
            catch (Exception e) { report[p.Name] = "erro: " + e.Message; }
        }
        return report;
    }

    public static string CaptureJson(string name) => Parts.FirstOrDefault(x => x.Name == name)?.Capture();

    public static object Status()
    {
        var parts = new Dictionary<string, string>();
        foreach (var p in Parts) parts[p.Name] = p.Disabled ? "desligada" : Baseline.ContainsKey(p.Name) ? "ok" : "-";
        return new { parts, sent = Sent, received = Received };
    }
}
