// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d41.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using BokuMono;
using BokuMono.Animal;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Animais da fazenda compartilhados (5.3, D41): afeição, carinho/escova/sino do dia, alimentação, produto
/// (leite, ovo), gravidez, sinais... — o "estado de cuidados" de cada FarmAnimalParameter.
///
///  - A cada 1 s, cada jogo compara o estado de cuidados de cada animal (o ToSaveData SEM os campos de movimento:
///    posição, rotação, ordens, rotas) com a última versão sincronizada. Os animais que mudaram vão (JSON completo)
///    ao host, que aplica e repassa.
///  - Quem recebe MESCLA: usa o estado recebido, mas mantém os campos de movimento do próprio jogo (o animal não
///    pula de lugar), e aplica com FromSaveData do próprio animal.
///  - CELEIROS (FarmAnimalBarn) também: ovos produzidos (ProductEggContainers — é daqui que o jogo recria os ovos
///    ao entrar no celeiro), cochos de ração e alimentação do dia. Mesmo esquema, com o GUID do celeiro.
/// Animais novos/vendidos não são criados/removidos ao vivo: chegam na virada do dia (mundo novo do host).
/// </summary>
public class AnimalSync : MonoBehaviour
{
    public AnimalSync(IntPtr ptr) : base(ptr) { }

    /// <summary>Campos que mudam com o movimento/IA do animal: ficam fora da comparação e não são sobrescritos.</summary>
    private static readonly string[] Volatile =
    {
        "Position", "Rotation", "prevDateTime", "PastureElapsedTicks", "StartOutTime", "IsExecuteTimeLagMove",
        "Order", "MoveInfos", "LocationData", "BarnLosePos", "ProbabilityOfFinestUpNow",
    };

    private static readonly Dictionary<string, string> Baseline = new(); // guid → assinatura de cuidados
    private static readonly Dictionary<string, string> BarnBaseline = new(); // guid do celeiro → JSON

    /// <summary>Campos do celeiro que não sincronizamos (hora da última entrada pelo portão é de cada jogo).</summary>
    private static readonly string[] BarnVolatile = { "LastEnterGateTicks" };
    private static bool _hasBaseline;
    private static bool _subscribed;
    private float _nextTick;
    public static int Sent, Received;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += Reset;
        NetSession.OnHosting += Reset;
        NetSession.OnJoined += Reset;
    }

    private static void Reset() { Baseline.Clear(); BarnBaseline.Clear(); _hasBaseline = false; }

    public static IEnumerable<FarmAnimalBarn> Barns()
    {
        var list = new List<FarmAnimalBarn>();
        var info = AnimalManager.Instance.FarmAnimalBarns;
        if (info?.Barns == null) return list;
        foreach (var b in info.Barns)
        {
            var fb = b?.TryCast<FarmAnimalBarn>();
            if (fb != null) list.Add(fb);
        }
        return list;
    }

    public static string BarnJson(FarmAnimalBarn b) => StringExtensions.ToJson(b.ToSaveData());

    private static string BarnSignature(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        foreach (var k in BarnVolatile) o.Remove(k);
        return o.ToJsonString();
    }

    private static string BarnGuid(string json) => JsonNode.Parse(json)!["Guid"]?.GetValue<string>() ?? "";

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!AnimalManager.IsInstance) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    public static IEnumerable<FarmAnimalParameter> FarmAnimals()
    {
        // IEnumerable IL2CPP: o foreach do C# não enxerga MoveNext; usamos o enumerador não genérico.
        var en = AnimalManager.Instance.AllAnimalParams.Cast<Il2CppSystem.Collections.IEnumerable>().GetEnumerator();
        var list = new List<FarmAnimalParameter>();
        while (en.MoveNext())
        {
            var f = en.Current?.TryCast<FarmAnimalParameter>();
            if (f != null && !string.IsNullOrEmpty(f.Guid)) list.Add(f);
        }
        return list;
    }

    public static string Json(FarmAnimalParameter a) => StringExtensions.ToJson(a.ToSaveData());

    private static string CareSignature(string json)
    {
        var o = JsonNode.Parse(json)!.AsObject();
        foreach (var k in Volatile) o.Remove(k);
        return o.ToJsonString();
    }

    private void Update()
    {
        try
        {
            if (!Ready() || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            var changed = new List<(string guid, string json)>();
            foreach (var a in FarmAnimals())
            {
                var json = Json(a);
                var sig = CareSignature(json);
                if (Baseline.TryGetValue(a.Guid, out var old) && old == sig) continue;
                var isNew = !Baseline.ContainsKey(a.Guid);
                if (!isNew && Plugin.DevMode) LogDiff(a.Guid, old, sig);
                Baseline[a.Guid] = sig;
                if (_hasBaseline && !isNew) changed.Add((a.Guid, json));
            }
            foreach (var b in Barns())
            {
                var json = BarnJson(b);
                var guid = BarnGuid(json);
                if (string.IsNullOrEmpty(guid)) continue;
                var sig = BarnSignature(json);
                if (BarnBaseline.TryGetValue(guid, out var old) && old == sig) continue;
                var isNew = !BarnBaseline.ContainsKey(guid);
                BarnBaseline[guid] = sig;
                if (_hasBaseline && !isNew) changed.Add(("barn:" + guid, json));
            }
            _hasBaseline = true;
            if (changed.Count == 0) return;
            var packed = Pack(changed);
            if (NetSession.Current == NetSession.Mode.Host) SendToClients(packed, except: NetSession.LocalId);
            else NetSession.SendGame(MsgType.AnimalData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
            Sent += changed.Count;
            Plugin.Logger.LogInfo($"[AnimalSync] {changed.Count} animal(is) mudou(aram): enviado ({packed.Length} bytes)");
            DevBridge.PushEvent($"animais:enviado:{changed.Count}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[AnimalSync] {e.Message}"); }
    }

    /// <summary>Modo de teste: quais campos mudaram (para achar campos que mudam sozinhos).</summary>
    private static void LogDiff(string guid, string oldSig, string newSig)
    {
        try
        {
            var o = JsonNode.Parse(oldSig)!.AsObject();
            var n = JsonNode.Parse(newSig)!.AsObject();
            var keys = n.Select(kv => kv.Key).Where(k => (o[k]?.ToJsonString() ?? "null") != (n[k]?.ToJsonString() ?? "null")).ToList();
            Plugin.Logger.LogInfo($"[AnimalSync] {guid.Substring(0, 8)} mudou: {string.Join(", ", keys.Select(k => $"{k}={n[k]?.ToJsonString()}"))}");
        }
        catch { }
    }

    private static void SendToClients(byte[] packed, byte except)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id != except)
                NetSession.SendTo(id, MsgType.AnimalData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.AnimalData) return;
        var packed = r.GetBytesWithLength();
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        if (!Ready()) return;
        try
        {
            var list = Unpack(packed);
            var applied = 0;
            foreach (var (guid, json) in list) if (Apply(guid, json) == "ok") applied++;
            Received += applied;
            if (NetSession.Current == NetSession.Mode.Host) SendToClients(packed, except: from);
            Plugin.Logger.LogInfo($"[AnimalSync] {applied}/{list.Count} animal(is) atualizado(s) por #{from}");
            DevBridge.PushEvent($"animais:recebido:{applied}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[AnimalSync] aplicar: {e.Message}"); }
    }

    /// <summary>Aplica o estado recebido no animal local, mantendo os campos de movimento locais.</summary>
    public static string Apply(string guid, string incoming) => Apply(guid, incoming, markSynced: true);

    /// <summary>markSynced=false simula uma ação LOCAL (testes): a mudança será enviada no próximo tick.</summary>
    public static string Apply(string guid, string incoming, bool markSynced)
    {
        if (guid.StartsWith("barn:")) return ApplyBarn(guid.Substring(5), incoming, markSynced);
        var a = FarmAnimals().FirstOrDefault(x => x.Guid == guid);
        if (a == null) return "animal não existe aqui";
        var local = JsonNode.Parse(Json(a))!.AsObject();
        var merged = JsonNode.Parse(incoming)!.AsObject();
        foreach (var k in Volatile)
        {
            merged.Remove(k);
            if (local[k] != null) merged[k] = JsonNode.Parse(local[k]!.ToJsonString()); // cópia (net6 não tem JsonNode.DeepClone)
        }
        var sd = StringExtensions.JsonToObject<FarmAnimalParameter.SaveData>(merged.ToJsonString());
        if (sd == null) return "JsonToObject devolveu nulo";
        a.FromSaveData(sd);
        if (markSynced) Baseline[guid] = CareSignature(Json(a));
        return "ok";
    }

    private static string ApplyBarn(string guid, string incoming, bool markSynced)
    {
        var b = Barns().FirstOrDefault(x => BarnGuid(BarnJson(x)) == guid);
        if (b == null) return "celeiro não existe aqui";
        var local = JsonNode.Parse(BarnJson(b))!.AsObject();
        var merged = JsonNode.Parse(incoming)!.AsObject();
        foreach (var k in BarnVolatile)
        {
            merged.Remove(k);
            if (local[k] != null) merged[k] = JsonNode.Parse(local[k]!.ToJsonString());
        }
        var sd = StringExtensions.JsonToObject<FarmAnimalBarn.SaveData>(merged.ToJsonString());
        if (sd == null) return "JsonToObject devolveu nulo (celeiro)";
        b.FromSaveData(sd);
        if (markSynced) BarnBaseline[guid] = BarnSignature(BarnJson(b));
        return "ok";
    }

    // ---------- formato: gzip de [int n]{[string guid][string json]} ----------

    private static byte[] Pack(List<(string guid, string json)> items)
    {
        var w = new NetDataWriter();
        w.Put(items.Count);
        foreach (var (g, j) in items) { w.Put(g); w.Put(j); }
        return LetterSync.Pack(Convert.ToBase64String(w.Data, 0, w.Length));
    }

    private static List<(string, string)> Unpack(byte[] packed)
    {
        var r = new NetDataReader(Convert.FromBase64String(LetterSync.Unpack(packed)));
        var n = r.GetInt();
        var list = new List<(string, string)>(n);
        for (var i = 0; i < n; i++) list.Add((r.GetString(), r.GetString()));
        return list;
    }

    public static object Status() => new { tracked = Baseline.Count, barns = BarnBaseline.Count, sent = Sent, received = Received };
}
