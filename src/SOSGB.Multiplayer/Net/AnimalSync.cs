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
///  - COMPRA E VENDA ao vivo (D81): animal que aparece depois da linha de base (comprado: AddAnimal) vai com o estado
///    completo e quem recebe o cria com a mesma rotina da compra; animal que some da lista por 3 s seguidos (vendido:
///    Leave) vira um aviso "del:" e quem recebe o remove. Sem isso, o animal comprado pelo convidado sumia na virada
///    do dia (o mundo do host não o tinha), com o dinheiro já gasto da carteira do grupo.
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

    private static readonly HashSet<string> VolatileSet = new(Volatile);
    private static readonly Dictionary<string, string> Baseline = new(); // guid → assinatura de cuidados (último enviado/recebido)
    // D91: guid → assinatura de cuidados do último estado CONFIRMADO (o canônico do host). Cada mudança vai com ele como base.
    private static readonly Dictionary<string, string> Known = new();
    private static readonly Dictionary<string, string> BarnBaseline = new(); // guid do celeiro → JSON
    private static readonly Dictionary<string, float> Missing = new(); // guid → desde quando sumiu da lista (D81)
    private const float RemovedAfter = 3f;

    /// <summary>Campos do celeiro que não sincronizamos (hora da última entrada pelo portão é de cada jogo).</summary>
    private static readonly string[] BarnVolatile = { "LastEnterGateTicks" };
    private static bool _hasBaseline;
    private static bool _subscribed;
    private float _nextTick;
    public static int Sent, Received;
    private static readonly HeldMessages Held = new("AnimalSync");

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += Reset;
        NetSession.OnHosting += Reset;
        NetSession.OnJoined += Reset;
    }

    private static void Reset() { Baseline.Clear(); Known.Clear(); BarnBaseline.Clear(); Missing.Clear(); _hasBaseline = false; }

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
            if (!Ready()) return;
            if (Held.Count > 0) Held.Drain(OnMessage); // o que chegou enquanto carregava (D63)
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            var changed = new List<(string guid, string json)>();
            var present = new HashSet<string>();
            foreach (var a in FarmAnimals())
            {
                present.Add(a.Guid);
                Missing.Remove(a.Guid);
                var json = Json(a);
                var sig = CareSignature(json);
                if (Baseline.TryGetValue(a.Guid, out var old) && old == sig) continue;
                var isNew = !Baseline.ContainsKey(a.Guid);
                if (!isNew && Plugin.DevMode) LogDiff(a.Guid, old, sig);
                Baseline[a.Guid] = sig;
                if (!Known.ContainsKey(a.Guid)) Known[a.Guid] = isNew ? sig : old; // 1º estado visto = 1ª base
                if (!_hasBaseline) continue;
                changed.Add((a.Guid, json)); // novo depois da linha de base = comprado aqui (D81): vai completo
                if (isNew) Plugin.Logger.LogInfo($"[AnimalSync] animal novo neste jogo (compra): {a.Guid}");
            }
            // D81: animal que saiu da lista (vendido). A lista não muda na troca de mapa (medido), mas só vale
            // depois de 3 s seguidos e com outros animais presentes, para nunca apagar animais por uma lista vazia.
            if (_hasBaseline && present.Count > 0)
                foreach (var guid in Baseline.Keys.Where(g => !present.Contains(g)).ToList())
                {
                    if (!Missing.TryGetValue(guid, out var since)) { Missing[guid] = Time.unscaledTime; continue; }
                    if (Time.unscaledTime - since < RemovedAfter) continue;
                    Missing.Remove(guid);
                    Baseline.Remove(guid);
                    changed.Add(("del:" + guid, ""));
                    Plugin.Logger.LogInfo($"[AnimalSync] animal saiu deste jogo (venda): {guid}");
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
            // D91: cada animal leva a base (último confirmado que este jogo tinha); o host mescla com ela.
            var packed = Pack(changed.Select(c => (c.guid, c.json, Known.TryGetValue(c.guid, out var kb) ? kb : "")).ToList());
            if (NetSession.Current == NetSession.Mode.Host)
                foreach (var c in changed) if (!c.guid.Contains(':')) Known[c.guid] = CareSignature(c.json); // o do host é o canônico
            if (NetSession.Current == NetSession.Mode.Host) SendToClients(packed, except: NetSession.LocalId);
            else NetSession.SendGame(MsgType.AnimalData, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
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
                NetSession.SendTo(id, MsgType.AnimalData, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.AnimalData) return;
        if (!Ready()) { Held.HoldIfAllowed(type, from, r); return; } // D63: guarda em vez de descartar
        var packed = r.GetBlob();
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        try
        {
            var list = Unpack(packed);
            var applied = 0;
            var host = NetSession.Current == NetSession.Mode.Host;
            foreach (var (guid, json, baseSig) in list)
            {
                // Host: base = a que o convidado mandou. Convidado: o que chega do host é o canônico; base = o último
                // confirmado daqui (mudanças daqui ainda não enviadas ficam e saem no próximo tick).
                var b = host ? baseSig : Known.TryGetValue(guid, out var kb) ? kb : null;
                var res = Apply(guid, json, markSynced: true, baseSig: string.IsNullOrEmpty(b) ? null : b);
                if (res == "ok" || res == "ok-mesclado") applied++;
            }
            Received += applied;
            if (host)
            {
                // D91: o resultado de cada animal (canônico) vai a TODOS, inclusive a quem mandou. Celeiros e vendas, como
                // antes, só repassados.
                var animals = list.Where(x => !x.guid.Contains(':')).Select(x => x.guid).ToHashSet();
                var canon = FarmAnimals().Where(x => animals.Contains(x.Guid)).Select(x => (x.Guid, Json(x), "")).ToList();
                var others = list.Where(x => x.guid.Contains(':')).Select(x => (x.guid, x.json, "")).ToList();
                if (others.Count > 0) SendToClients(Pack(others), except: from);
                if (canon.Count > 0)
                {
                    foreach (var c in canon) Known[c.Item1] = CareSignature(c.Item2);
                    SendToClients(Pack(canon), except: NetSession.UnknownId);
                }
            }
            Plugin.Logger.LogInfo($"[AnimalSync] {applied}/{list.Count} animal(is) atualizado(s) por #{from}");
            DevBridge.PushEvent($"animais:recebido:{applied}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[AnimalSync] aplicar: {e.Message}"); }
    }

    /// <summary>Aplica o estado recebido no animal local, mantendo os campos de movimento locais.</summary>
    public static string Apply(string guid, string incoming) => Apply(guid, incoming, markSynced: true);

    /// <summary>markSynced=false simula uma ação LOCAL (testes): a mudança será enviada no próximo tick.</summary>
    public static string Apply(string guid, string incoming, bool markSynced) => Apply(guid, incoming, markSynced, null);

    public static string Apply(string guid, string incoming, bool markSynced, string baseSig)
    {
        if (guid.StartsWith("barn:")) return ApplyBarn(guid.Substring(5), incoming, markSynced);
        if (guid.StartsWith("del:")) return Remove(guid.Substring(4));
        var a = FarmAnimals().FirstOrDefault(x => x.Guid == guid);
        var created = false;
        if (a == null)
        {
            a = Create(guid, incoming, out var why);
            if (a == null) return why;
            created = true;
        }
        var localJson = Json(a);
        var local = JsonNode.Parse(localJson)!.AsObject();
        var merged = JsonNode.Parse(incoming)!.AsObject();
        // D91 (revisão A04): cuidados de dois jogadores no mesmo animal ao mesmo tempo (um escova, outro alimenta): mescla
        // em três vias dos campos de cuidado, com a base = o último estado CONFIRMADO de onde a mudança partiu (vem junto
        // na mensagem). Antes o recebido sobrescrevia tudo e um dos cuidados sumia (medido: os dois trocavam os cuidados).
        // Listas de tamanho diferente não somam (duplicaria).
        var mergedByUs = false;
        if (!created && baseSig != null)
        {
            var localSig = CareSignature(localJson);
            var incSig = CareSignature(incoming);
            if (localSig != baseSig && localSig != incSig)
            {
                merged = JsonNode.Parse(JsonMerge3.Merge(baseSig, localSig, incSig, VolatileSet, multisetLists: false))!.AsObject();
                mergedByUs = true;
            }
        }
        foreach (var k in created ? Volatile.Concat(BelongFields) : Volatile)
        {
            merged.Remove(k);
            if (local[k] != null) merged[k] = JsonNode.Parse(local[k]!.ToJsonString()); // cópia (net6 não tem JsonNode.DeepClone)
        }
        var sd = StringExtensions.JsonToObject<FarmAnimalParameter.SaveData>(merged.ToJsonString());
        if (sd == null) return "JsonToObject devolveu nulo";
        a.FromSaveData(sd);
        if (markSynced) Baseline[guid] = CareSignature(Json(a));
        // Convidado: o que chegou do host é o confirmado. Se ele juntou uma mudança daqui ainda não enviada, a comparação
        // fica com o que CHEGOU, para o próximo tick mandar a parte daqui (senão ela nunca sairia).
        if (markSynced && NetSession.Current == NetSession.Mode.Client)
        {
            Known[guid] = CareSignature(incoming);
            if (mergedByUs) Baseline[guid] = Known[guid];
        }
        Missing.Remove(guid);
        if (mergedByUs) { Plugin.Logger.LogInfo($"[AnimalSync] animal {guid}: cuidados dos dois jogadores juntados"); return "ok-mesclado"; }
        if (created)
        {
            if (a.Guid != guid) Plugin.Logger.LogWarning($"[AnimalSync] animal criado ficou com outro Guid: {a.Guid} (esperado {guid})");
            Plugin.Logger.LogInfo($"[AnimalSync] animal comprado por outro jogador criado aqui: {guid} ({merged["Name"]})");
            DevBridge.PushEvent("animais:criado");
        }
        return "ok";
    }

    /// <summary>Num animal recém-criado, o celeiro é o que o próprio jogo escolheu (o personagem já está no curral dele).</summary>
    private static readonly string[] BelongFields = { "BelongedBarn", "BelongStatus" };

    /// <summary>D81: cria o animal comprado no jogo de outro jogador, com a mesma rotina da compra.</summary>
    private static FarmAnimalParameter Create(string guid, string incoming, out string why)
    {
        why = null;
        try
        {
            var n = JsonNode.Parse(incoming)!;
            var id = n["FieldAnimalMasterId"]!.GetValue<uint>();
            var name = n["Name"]?.GetValue<string>() ?? "";
            var pattern = n["PatternId"]?.GetValue<uint>() ?? 0;
            var a = AnimalManager.Instance.AddAnimal(id, name, pattern);
            if (a == null) { why = "AddAnimal devolveu nulo (celeiro cheio?)"; Plugin.Logger.LogWarning($"[AnimalSync] não deu para criar o animal {guid}: {why}"); return null; }
            var inBarn = n["BelongedBarn"]?.GetValue<string>();
            var localBarn = a.ToSaveData().BelongedBarn;
            if (!string.IsNullOrEmpty(inBarn) && localBarn != inBarn)
                Plugin.Logger.LogInfo($"[AnimalSync] animal {guid}: o jogo pôs em outro celeiro ({localBarn} × {inBarn})");
            return a;
        }
        catch (Exception e) { why = "criar: " + e.Message; Plugin.Logger.LogWarning($"[AnimalSync] {why}"); return null; }
    }

    /// <summary>D81: remove o animal vendido no jogo de outro jogador (mesma rotina da venda).</summary>
    private static string Remove(string guid)
    {
        Baseline.Remove(guid);
        Missing.Remove(guid);
        var a = FarmAnimals().FirstOrDefault(x => x.Guid == guid);
        if (a == null) return "ok"; // já não existe aqui
        try { a.Leave(); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[AnimalSync] remover {guid}: {e.Message}"); return "remover: " + e.Message; }
        var still = FarmAnimals().Any(x => x.Guid == guid);
        Plugin.Logger.LogInfo($"[AnimalSync] animal vendido por outro jogador removido aqui: {guid}{(still ? " (AINDA NA LISTA)" : "")}");
        DevBridge.PushEvent("animais:removido");
        return still ? "ainda na lista" : "ok";
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

    // ---------- formato: gzip de [int n]{[string guid][string json][string base]} (base: D91, "" = sem base) ----------

    private static byte[] Pack(List<(string guid, string json, string baseSig)> items)
    {
        var w = new NetDataWriter();
        w.Put(items.Count);
        foreach (var (g, j, b) in items) { w.Put(g); w.Put(j); w.Put(b ?? ""); }
        return LetterSync.Pack(Convert.ToBase64String(w.Data, 0, w.Length));
    }

    private static List<(string guid, string json, string baseSig)> Unpack(byte[] packed)
    {
        var r = new NetDataReader(Convert.FromBase64String(LetterSync.Unpack(packed)));
        var n = r.GetInt();
        var list = new List<(string, string, string)>(n);
        for (var i = 0; i < n; i++) list.Add((r.GetString(), r.GetString(), r.GetString()));
        return list;
    }

    public static object Status() => new { tracked = Baseline.Count, barns = BarnBaseline.Count, sent = Sent, received = Received };
}
