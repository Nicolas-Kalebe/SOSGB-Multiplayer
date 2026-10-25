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
/// Estado compartilhado genérico (5.4, D42; progresso do mundo na D43): gerenciadores do jogo com
/// ToSaveData/FromSaveData cujo estado é do MUNDO (moinhos, apicultura, cogumelos, vasos de flor, ampliações, pets,
/// cavalo, prêmios, adega, banho, receitas, missões, peixes, reforços, lojas, troféus, flags de evento).
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
        public bool CaptureFailLogged;
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
        // Progresso do mundo que o convidado pode mudar durante o dia (sem isto, perderia na virada).
        Make("awards", () => AwardManager.IsInstance,
            () => StringExtensions.ToJson(AwardManager.Instance.ToSaveData()),
            ApplyAwards),
        Make("basement", () => BasementManager.IsInstance,
            () => StringExtensions.ToJson(BasementManager.Instance.ToSaveData()),
            ApplyBasement),
        Make("bath", () => BathManager.IsInstance,
            () => StringExtensions.ToJson(BathManager.Instance.ToSaveData()),
            j => BathManager.Instance.FromSaveData(StringExtensions.JsonToObject<BathData>(j))),
        Make("cooking", () => CookingManager.IsInstance,
            () => StringExtensions.ToJson(CookingManager.Instance.ToSaveData()),
            j => CookingManager.Instance.FromSaveData(StringExtensions.JsonToObject<CookingManager.SaveData>(j))),
        Make("coromission", () => CoroMissionManager.IsInstance,
            () => StringExtensions.ToJson(CoroMissionManager.Instance.ToSaveData()),
            j => CoroMissionManager.Instance.FromSaveData(StringExtensions.JsonToObject<CoroMissionManager.SaveData>(j))),
        Make("fish", () => FishManager.IsInstance,
            () => StringExtensions.ToJson(FishManager.Instance.ToSaveData()),
            j => FishManager.Instance.FromSaveData(StringExtensions.JsonToObject<FishManager.SaveData>(j))),
        Make("reinforce", () => ReinForceManager.IsInstance,
            () => StringExtensions.ToJson(ReinForceManager.Instance.ToSaveData()),
            j => ReinForceManager.Instance.FromSaveData(StringExtensions.JsonToObject<ReinForceData>(j))),
        Make("shop", () => ShopManager.IsInstance,
            () => StringExtensions.ToJson(ShopManager.Instance.ToSaveData()),
            ApplyShop),
        Make("shopanimal", () => ShopAnimalInfoManager.IsInstance,
            () => StringExtensions.ToJson(ShopAnimalInfoManager.Instance.ToSaveData()),
            j => ShopAnimalInfoManager.Instance.FromSaveData(StringExtensions.JsonToObject<ShopAnimalInfoManager.ShopAnimalInfoData>(j))),
        // Bazaar (7.1, D46): progresso (nível, reputação, vendas), tendências e decoração da barraca.
        Make("bazaar", () => BazaarManager.IsInstance,
            () => StringExtensions.ToJson(BazaarManager.Instance.ToBazaarSaveData()),
            j => BazaarManager.Instance.FromBazaarSaveData(StringExtensions.JsonToObject<BazaarManager.BazaarSaveData>(j))),
        Make("bazaartrend", () => BazaarManager.IsInstance,
            () => StringExtensions.ToJson(BazaarManager.Instance.ToTrendSaveData()),
            j => BazaarManager.Instance.FromSaveData(StringExtensions.JsonToObject<BazaarManager.TrendData>(j))),
        Make("bazaarcustom", () => BazaarManager.IsInstance,
            () => StringExtensions.ToJson(BazaarManager.Instance.ToSaveData()),
            j => BazaarManager.Instance.FromSaveData(StringExtensions.JsonToObject<BazaarManager.CustomData>(j), BazaarManager.Instance.DevelopLevel)),
        // NÃO incluir TrophyRackManager: o FromSaveData dele ACUMULA (cada aplicação soma os troféus de novo; testado).
        // Flags de evento (eventos de história e de coração já vistos): UNIÃO — só acrescenta/atualiza, nunca apaga.
        Make("eventflags", () => EventFlagManager.IsInstance,
            () => StringExtensions.ToJson(new EventFlagSaveData(EventFlagManager.Instance.ToSaveData())),
            UnionEventFlags),
        Make("pets", () => AnimalManager.IsInstance && AnimalManager.Instance.PetAnimalController != null,
            () => StringExtensions.ToJson(AnimalManager.Instance.PetAnimalController.ToSaveData()),
            j => AnimalManager.Instance.PetAnimalController.FromSaveData(StringExtensions.JsonToObject<BokuMono.Animal.PetAnimalController.SaveData>(j))),
        Make("horse", () => AnimalManager.IsInstance && AnimalManager.Instance.RideableAnimalController != null,
            () => StringExtensions.ToJson(AnimalManager.Instance.RideableAnimalController.ToSaveData()),
            j => AnimalManager.Instance.RideableAnimalController.FromSaveData(StringExtensions.JsonToObject<BokuMono.Animal.RideableAnimalController.SaveData>(j))),
    };

    // ----- Partes que o JsonToObject do jogo não monta (classes sem construtor vazio): montadas à mão -----

    private static System.Text.Json.JsonElement Prop(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v : default;

    private static Il2CppSystem.Collections.Generic.List<uint> UList(System.Text.Json.JsonElement e)
    {
        var l = new Il2CppSystem.Collections.Generic.List<uint>();
        if (e.ValueKind == System.Text.Json.JsonValueKind.Array) foreach (var x in e.EnumerateArray()) l.Add(x.GetUInt32());
        return l;
    }

    private static Il2CppSystem.Collections.Generic.List<int> IList(System.Text.Json.JsonElement e)
    {
        var l = new Il2CppSystem.Collections.Generic.List<int>();
        if (e.ValueKind == System.Text.Json.JsonValueKind.Array) foreach (var x in e.EnumerateArray()) l.Add(x.GetInt32());
        return l;
    }

    /// <summary>Adega: SaveData só tem construtor com os 4 valores.</summary>
    private static void ApplyBasement(string json)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        var r = d.RootElement;
        bool B(string n) => r.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.True;
        BasementManager.Instance.FromSaveData(new BasementManager.SaveData(B("IsSetSpringSun"), B("IsSetSummerSun"), B("IsSetAutumnSun"), B("IsSetWinterSun")));
    }

    /// <summary>Prêmios: cada AwardData só se constrói com o dado mestre do prêmio.</summary>
    private static void ApplyAwards(string json)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        var r = d.RootElement;
        var sd = AwardManager.Instance.ToSaveData(); // base local (mantém o que não vier)
        var list = new Il2CppSystem.Collections.Generic.List<AwardData>();
        var master = MasterDataManager.Instance.AwardMaster;
        foreach (var a in Prop(r, "AwardDatas").EnumerateArray())
        {
            var id = a.GetProperty("AwardId").GetUInt32();
            if (!master.TryGetData(id, out var m)) continue;
            var ad = new AwardData(m)
            {
                AwardId = id,
                ProgressNumber = a.GetProperty("ProgressNumber").GetInt32(),
                IsComplete = a.GetProperty("IsComplete").GetBoolean(),
                IsReceived = a.GetProperty("IsReceived").GetBoolean(),
                IsSelected = a.GetProperty("IsSelected").GetBoolean(),
                saveValue = IList(Prop(a, "saveValue")),
            };
            list.Add(ad);
        }
        sd.AwardDatas = list;
        sd.SettingTitleId = Prop(r, "SettingTitleId").ValueKind == System.Text.Json.JsonValueKind.Number ? r.GetProperty("SettingTitleId").GetUInt32() : sd.SettingTitleId;
        sd.ReservedShowAwardIds = UList(Prop(r, "ReservedShowAwardIds"));
        sd.AlReadAwardIds = UList(Prop(r, "AlReadAwardIds"));
        AwardManager.Instance.FromSaveData(sd);
    }

    /// <summary>
    /// Loja: o histórico de compras (HistoryData) não se monta do JSON e fica o local; itens limitados (estoque),
    /// roupas compradas, sorteio e catálogo vêm do recebido.
    /// </summary>
    private static void ApplyShop(string json)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        var r = d.RootElement;
        var sd = ShopManager.Instance.ToSaveData();
        var inc = StringExtensions.JsonToObject<ShopManager.SaveData>(json);
        if (inc != null)
        {
            if (inc.LimitedItems != null) sd.LimitedItems = inc.LimitedItems;
            if (inc.BuyClothesData != null) sd.BuyClothesData = inc.BuyClothesData;
            if (inc.LotteryStatusList != null) sd.LotteryStatusList = inc.LotteryStatusList;
            sd.lotterySpecialRemain = inc.lotterySpecialRemain;
            sd.lotterySpecialAlreadyCount = inc.lotterySpecialAlreadyCount;
            sd.lotteryCountOneDay = inc.lotteryCountOneDay;
        }
        var cat = new Il2CppSystem.Collections.Generic.List<ShopManager.CatalogData>();
        foreach (var c in Prop(r, "CatelogList").EnumerateArray())
            cat.Add(new ShopManager.CatalogData(c.GetProperty("ItemId").GetUInt32(), c.GetProperty("BuyCount").GetInt32(),
                c.GetProperty("Quality").GetInt32(), new BokuMonoDateTime(c.GetProperty("BuyDateTimeTick").GetInt64())));
        sd.CatelogList = cat;
        ShopManager.Instance.FromSaveData(sd);
    }

    /// <summary>Acrescenta/atualiza as flags recebidas que diferem das locais (não apaga nenhuma).</summary>
    private static void UnionEventFlags(string json)
    {
        var efm = EventFlagManager.Instance;
        var local = new Dictionary<uint, (int flag, long time)>();
        using (var ld = System.Text.Json.JsonDocument.Parse(StringExtensions.ToJson(new EventFlagSaveData(efm.ToSaveData()))))
            foreach (var e in ld.RootElement.GetProperty("EventFlagData").EnumerateArray())
                local[e.GetProperty("FlagId").GetUInt32()] = (e.GetProperty("Flag").GetInt32(), e.GetProperty("FlagHistoryTime").GetInt64());
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var e in doc.RootElement.GetProperty("EventFlagData").EnumerateArray())
        {
            var id = e.GetProperty("FlagId").GetUInt32();
            var flag = e.GetProperty("Flag").GetInt32();
            var time = e.GetProperty("FlagHistoryTime").GetInt64();
            if (local.TryGetValue(id, out var cur) && cur.flag == flag) continue;
            efm.SetEventFlag(id, flag, time);
        }
    }

    private static Part Make(string name, Func<bool> available, Func<string> capture, Action<string> apply) =>
        new() { Name = name, Available = available, Capture = capture, Apply = apply };

    private static readonly Dictionary<string, string> Baseline = new();
    private static int _cursor = -1;
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
            // Rodízio: uma parte por tick, para espalhar o custo (com ~19 partes, cada uma é conferida a cada ~5 s;
            // pets e animais mudam pouco, e o progresso do mundo não tem pressa).
            _nextTick = Time.unscaledTime + 0.25f;
            _cursor = (_cursor + 1) % Parts.Count;
            foreach (var p in new[] { Parts[_cursor] })
            {
                if (p.Disabled || !p.Available()) continue;
                string json;
                try { json = p.Capture(); }
                catch (Exception e)
                {
                    // Falha ao capturar é TEMPORÁRIA (ex.: o Bazaar só fica completo no dia e no mapa dele): só pula.
                    if (!p.CaptureFailLogged) { p.CaptureFailLogged = true; Plugin.Logger.LogInfo($"[SharedSync] '{p.Name}' indisponível agora ({FirstLine(e.Message)}); tentando de novo depois"); }
                    continue;
                }
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

    private static string FirstLine(string s) { var i = s.IndexOf((char)10); return i < 0 ? s : s.Substring(0, i); }

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
        string localJson;
        try { localJson = p.Capture(); }
        catch { return "indisponível agora (não deu para ler o estado local)"; }
        try
        {
            var merged = Merge(json, localJson);
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
