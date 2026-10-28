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
///  - D91 (revisão A04): versões e mescla em três vias. O host numera cada estado canônico e guarda os últimos; o
///    convidado manda junto a versão de onde partiu. O host junta só o que CADA UM mudou desde essa base (JsonMerge3),
///    aplica e devolve o resultado canônico a TODOS, inclusive a quem mandou. Antes, quem recebia aplicava o estado
///    inteiro por cima e duas mudanças ao mesmo tempo (ex.: os dois pondo receitas no moinho) apagavam uma à outra.
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
        public int Failures; // falhas SEGUIDAS ao aplicar (A08): desliga só na 3ª; um sucesso zera
        // D91 (A04): versão canônica. Host: a atual (e as últimas em History). Convidado: a última recebida do host.
        // KnownJson (convidado) = BASE da próxima mescla: o último estado que ele mandou ou aplicou. -1 = ainda não sabe.
        public int Version = -1;
        public string KnownJson;
        // Convidado: um envio esperando a confirmação do host. Enquanto isso não manda outro (a base do segundo seria um
        // estado que o host ainda não tem, e a mudança contaria duas vezes: medido, item duplicado no moinho).
        public bool InFlight;
        public float InFlightAt;
        public readonly Dictionary<int, string> History = new();
    }

    private const int HistoryKeep = 64;

    /// <summary>Campos de movimento/IA (em qualquer nível do JSON): fora da comparação, mantidos locais ao mesclar.</summary>
    private static readonly HashSet<string> Volatile = new()
    {
        "Position", "Rotation", "ScheduleLayerId", "Sleep", "prevDateTime", "MoveInfos", "Order", "LocationData",
    };

    private static readonly List<Part> Parts = new()
    {
        // Moinho, cogumelos e festivais guardam o estado em campos PRIVADOS [SerializeField]: o ToJson do jogo
        // (Newtonsoft) devolvia "{}" ou só parte (D54). Estes vão pelo JsonUtility da Unity, o mesmo formato do save.
        Make("windmill", () => WindmillCraftingManager.IsInstance,
            () => JsonUtility.ToJson(WindmillCraftingManager.Instance.ToSaveData()),
            j => WindmillCraftingManager.Instance.FromSaveData(FromUnityJson<WindmillCraftingManager.SaveData>(j))),
        Make("beekeeping", () => BeekeepingManager.IsInstance,
            () => StringExtensions.ToJson(BeekeepingManager.Instance.ToSaveData()),
            j => BeekeepingManager.Instance.FromSaveData(StringExtensions.JsonToObject<BeekeepingManager.SaveData>(j))),
        Make("mushroom", () => MushroomFarmingManager.IsInstance,
            () => JsonUtility.ToJson(MushroomFarmingManager.Instance.ToSaveData()),
            j => MushroomFarmingManager.Instance.FromSaveData(FromUnityJson<MushroomFarmingData>(j))),
        Make("flowerpot", () => FlowerPotManager.IsInstance,
            () => StringExtensions.ToJson(FlowerPotManager.Instance.ToSaveData()),
            j => FlowerPotManager.Instance.FromSaveData(StringExtensions.JsonToObject<FlowerPotManager.SaveData>(j))),
        // Vigor máximo: do MUNDO, compartilhado (pedido do usuário: "tem poucos jeitos de aumentar"). Ao aplicar, o
        // vigor atual fica na mesma proporção.
        Make("maxstamina", () => GameController.IsInstance,
            () => System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, float> { ["max"] = GameController.Instance.PlayerMaxHP }),
            ApplyMaxStamina),
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
            ApplyBazaar),
        Make("bazaartrend", () => BazaarManager.IsInstance,
            () => StringExtensions.ToJson(BazaarManager.Instance.ToTrendSaveData()),
            j => BazaarManager.Instance.FromSaveData(StringExtensions.JsonToObject<BazaarManager.TrendData>(j))),
        Make("bazaarcustom", () => BazaarManager.IsInstance,
            () => StringExtensions.ToJson(BazaarManager.Instance.ToSaveData()),
            j => BazaarManager.Instance.FromSaveData(StringExtensions.JsonToObject<BazaarManager.CustomData>(j), BazaarManager.Instance.DevelopLevel)),
        // NÃO incluir TrophyRackManager: o FromSaveData dele ACUMULA (cada aplicação soma os troféus de novo; testado).
        // Festivais (resultados, participação) e informações gerais do mundo (7.2).
        Make("festival", () => FestivalExecutor.IsInstance,
            () => JsonUtility.ToJson(FestivalExecutor.Instance.ToSaveData()),
            j => FestivalExecutor.Instance.FromSaveData(FromUnityJson<FestivalExecutor.SaveData>(j))),
        Make("generalinfo", () => GeneralInfoManager.IsInstance,
            () => StringExtensions.ToJson(GeneralInfoManager.Instance.ToSaveData()),
            j => GeneralInfoManager.Instance.FromSaveData(StringExtensions.JsonToObject<GeneralInfoManager.SaveData>(j))),
        // Histórico de itens / enciclopédia (itens já obtidos, maior qualidade, maior peixe): UNIÃO via AddHistory.
        Make("itemhistory", () => ItemHistoryManager.IsInstance,
            () => StringExtensions.ToJson(ItemHistoryManager.Instance.ToSaveData()),
            UnionItemHistory),
        // Flags de evento (eventos de história e de coração já vistos): UNIÃO — só acrescenta/atualiza, nunca apaga.
        Make("eventflags", () => EventFlagManager.IsInstance,
            () => StringExtensions.ToJson(new EventFlagSaveData(EventFlagManager.Instance.ToSaveData())),
            UnionEventFlags),
        Make("pets", () => AnimalManager.IsInstance && AnimalManager.Instance.PetAnimalController != null,
            () => StringExtensions.ToJson(AnimalManager.Instance.PetAnimalController.ToSaveData()),
            ApplyPets),
        // Tutoriais já lidos (D57): UNIÃO — quem leu, ninguém vê de novo. Sem isto o convidado via o mesmo tutorial
        // todo dia (o que ele lia se perdia na virada, quando chega o mundo do host).
        Make("tutorial", () => TutorialManager.IsInstance,
            CaptureTutorial,
            UnionTutorial),
        // Selo BRAND (preço 3x) ganho nos festivais, em itens e animais (D57): UNIÃO.
        Make("brand", () => ExternalData.IsInstance && ExternalData.Instance.UserInfo != null,
            CaptureBrand,
            UnionBrand),
        Make("horse", () => AnimalManager.IsInstance && AnimalManager.Instance.RideableAnimalController != null,
            () => StringExtensions.ToJson(AnimalManager.Instance.RideableAnimalController.ToSaveData()),
            ApplyHorse),
        // Happy Bingo (D61): MESCLA — progresso de cada quadrado fica com o maior, prêmio resgatado por qualquer um fica
        // resgatado para todos. Sem isto o progresso do convidado sumia na virada e ele podia resgatar de novo, todo dia,
        // um prêmio de linha completa (o mundo do host não sabia do resgate).
        Make("bingo", () => BokuMono.Bingo.BingoManager.IsInstance,
            CaptureBingo,
            MergeBingo),
    };

    // ----- Happy Bingo: só o que muda jogando (estado/progresso dos quadrados, prêmios resgatados), por cartela -----

    private static BokuMono.Bingo.BingoLine LineOf(BokuMono.Bingo.BingoCard c, int i) => i switch
    {
        0 => c.HorizontalLine,
        1 => c.VerticalLine,
        2 => c.DiagonalLine,
        _ => c.CompleteLine,
    };

    private static string CaptureBingo()
    {
        var cards = new JsonArray();
        foreach (var c in BokuMono.Bingo.BingoManager.Instance.GetAllBingoCard())
        {
            if (c == null) continue;
            var sq = new JsonArray();
            if (c.Squares != null)
                foreach (var s in c.Squares)
                    if (s != null) sq.Add(new JsonArray(s.Slot, s.Id, s.State, s.Progress));
            var ln = new JsonArray();
            for (var i = 0; i < 4; i++)
            {
                var got = new JsonArray();
                var l = LineOf(c, i);
                if (l?.IsReceived != null) foreach (var b in l.IsReceived) got.Add(b);
                ln.Add(got);
            }
            cards.Add(new JsonObject { ["id"] = c.CardId, ["sq"] = sq, ["ln"] = ln });
        }
        return new JsonObject { ["cards"] = cards }.ToJsonString();
    }

    private static void MergeBingo(string json)
    {
        if (JsonNode.Parse(json)?["cards"] is not JsonArray cards) return;
        var bmg = BokuMono.Bingo.BingoManager.Instance;
        foreach (var cn in cards)
        {
            if (cn is not JsonObject co || co["id"] == null) continue;
            var card = bmg.GetBingoCard(co["id"]!.GetValue<uint>());
            if (card == null) continue; // cartela que este jogo não tem: chega com o mundo, na virada
            if (co["sq"] is JsonArray sq && card.Squares != null)
                foreach (var sn in sq)
                {
                    if (sn is not JsonArray a || a.Count < 4) continue;
                    int slot = a[0]!.GetValue<int>(), state = a[2]!.GetValue<int>(), progress = a[3]!.GetValue<int>();
                    var id = a[1]!.GetValue<uint>();
                    foreach (var s in card.Squares)
                    {
                        // Missão trocada (redesenho da cartela) num dos jogos: não mistura progresso de missões diferentes.
                        if (s == null || s.Slot != slot || s.Id != id) continue;
                        if (progress > s.Progress) s.Progress = progress;
                        if (state > s.State) s.State = state; // Nenhum 0 → pronto para carimbar 1 → feito 2
                    }
                }
            if (co["ln"] is JsonArray ln)
                for (var i = 0; i < 4 && i < ln.Count; i++)
                {
                    var l = LineOf(card, i);
                    if (l?.IsReceived == null || ln[i] is not JsonArray got) continue;
                    for (var k = 0; k < got.Count && k < l.IsReceived.Length; k++)
                        if (got[k]?.ToString() == "true" && !l.IsReceived[k]) l.IsReceived[k] = true;
                }
        }
    }

    // ----- Pets e cavalo: animal por animal -----
    // O FromSaveData do PetAnimalController / RideableAnimalController RECRIA os animais (parâmetros e personagens)
    // sem descarregar os que já estão na cena: aplicar a parte inteira a cada mudança deixava os pets EM DOBRO (visto
    // pelo usuário). Aqui cada animal recebe o estado no próprio parâmetro (FromSaveData do animal, mesmo Guid), como
    // a AnimalSync faz com os animais da fazenda. Animal que ainda não existe neste jogo é criado e registrado.

    // ----- Tutoriais e BRAND: união de listas de números -----

    private static string Ids(Il2CppSystem.Collections.Generic.List<uint> l)
    {
        var res = new List<uint>();
        if (l != null) foreach (var x in l) res.Add(x);
        res.Sort();
        return System.Text.Json.JsonSerializer.Serialize(res);
    }

    private static List<uint> IdsIn(string json, string key)
    {
        using var d = System.Text.Json.JsonDocument.Parse(json);
        var res = new List<uint>();
        if (d.RootElement.TryGetProperty(key, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray()) res.Add(x.GetUInt32());
        return res;
    }

    private static string CaptureTutorial()
    {
        var sd = TutorialManager.Instance.ToSaveData();
        return "{\"t\":" + Ids(sd.ReadTutorialIdList) + ",\"p\":" + Ids(sd.ReadTipsIdList) + "}";
    }

    private static void UnionTutorial(string json)
    {
        var tm = TutorialManager.Instance;
        var sd = tm.ToSaveData();
        foreach (var id in IdsIn(json, "t"))
            if (sd.ReadTutorialIdList == null || !sd.ReadTutorialIdList.Contains(id)) tm.AddTutorialRead(id);
        var tips = tm.ReadTipsIdList;
        if (tips != null)
            foreach (var id in IdsIn(json, "p"))
                if (!tips.Contains(id)) tips.Add(id);
    }

    private static string CaptureBrand()
    {
        var ui = ExternalData.Instance.UserInfo;
        return "{\"i\":" + Ids(ui.BrandItemIds) + ",\"a\":" + Ids(ui.BrandAnimalIds) + "}";
    }

    private static void UnionBrand(string json)
    {
        var ui = ExternalData.Instance.UserInfo;
        ui.BrandItemIds ??= new Il2CppSystem.Collections.Generic.List<uint>();
        ui.BrandAnimalIds ??= new Il2CppSystem.Collections.Generic.List<uint>();
        foreach (var id in IdsIn(json, "i")) if (!ui.BrandItemIds.Contains(id)) ui.BrandItemIds.Add(id);
        foreach (var id in IdsIn(json, "a")) if (!ui.BrandAnimalIds.Contains(id)) ui.BrandAnimalIds.Add(id);
    }

    /// <summary>
    /// Host aplicando estado vindo de um convidado (D57). Nos dias dos presentes da manhã (pet grátis em 1 de verão,
    /// cavalo em 14 de verão; e o dia seguinte, caso o evento rode atrasado) o evento roda em cada jogo e criaria um
    /// animal a mais por jogador: nesses dias pets e cavalos NOVOS só nascem no mundo do host. Nos outros dias um pet
    /// novo vindo do convidado é aceito (ele pode ter COMPRADO o pet: recusar faria perder o animal e o dinheiro;
    /// achado na revisão da D57). Quando o host recusa algo, ele manda o estado DELE a todos.
    /// </summary>
    private static bool _applyingFromGuest, _filtered;

    private const int SummerMonth = 2; // BokuMonoDateTime.Month: 1 primavera, 2 verão, 3 outono, 4 inverno

    private static bool IsAnimalGiftDay(int giftDay)
    {
        if (!DateManager.IsInstance) return false;
        var now = DateManager.Instance.Now;
        return now.Month == SummerMonth && (now.Day == giftDay || now.Day == giftDay + 1);
    }

    private static IEnumerable<(string guid, string json)> AnimalsIn(string json, string listName)
    {
        var list = new List<(string, string)>();
        if (JsonNode.Parse(json)?[listName] is not JsonArray arr) return list;
        foreach (var e in arr)
            if (e is JsonObject o && o["Guid"] is JsonValue gv && gv.TryGetValue<string>(out var g) && !string.IsNullOrEmpty(g))
                list.Add((g, o.ToJsonString()));
        return list;
    }

    private static void ApplyPets(string json)
    {
        var pc = AnimalManager.Instance.PetAnimalController;
        foreach (var (guid, one) in AnimalsIn(json, "PetParams"))
        {
            var sd = StringExtensions.JsonToObject<BokuMono.Animal.PetAnimalParameter.SaveData>(one);
            if (sd == null) throw new Exception("JsonToObject devolveu nulo (pet)");
            var p = pc.FindParameter(guid);
            if (p != null) { p.FromSaveData(sd); continue; }
            if (_applyingFromGuest && IsAnimalGiftDay(1)) { _filtered = true; Plugin.Logger.LogInfo($"[SharedSync] pet novo vindo de convidado ignorado ({guid}): dia do pet de presente, vale o do host"); continue; }
            p = new BokuMono.Animal.PetAnimalParameter();
            p.FromSaveData(sd);
            pc.AddParameter(p);
            Plugin.Logger.LogInfo($"[SharedSync] pet novo vindo de outro jogador: {guid}");
        }
    }

    private static void ApplyHorse(string json)
    {
        var rc = AnimalManager.Instance.RideableAnimalController;
        foreach (var (guid, one) in AnimalsIn(json, "Parameters"))
        {
            var sd = StringExtensions.JsonToObject<BokuMono.Animal.RideableAnimalParameter.SaveData>(one);
            if (sd == null) throw new Exception("JsonToObject devolveu nulo (cavalo)");
            var p = rc.FindParameter(guid);
            if (p != null) { p.FromSaveData(sd); continue; }
            if (_applyingFromGuest && IsAnimalGiftDay(14)) { _filtered = true; Plugin.Logger.LogInfo($"[SharedSync] cavalo novo vindo de convidado ignorado ({guid}): dia do cavalo de presente, vale o do host"); continue; }
            p = new BokuMono.Animal.RideableAnimalParameter();
            p.FromSaveData(sd);
            rc.AddParameter(p);
            Plugin.Logger.LogInfo($"[SharedSync] cavalo novo vindo de outro jogador: {guid}");
        }
    }

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

    private static void ApplyMaxStamina(string json)
    {
        var max = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, float>>(json)["max"];
        if (max <= 0) return;
        var gc = GameController.Instance;
        var ratio = gc.PlayerMaxHP > 0 ? Math.Clamp(gc.PlayerHP / gc.PlayerMaxHP, 0f, 1f) : 1f;
        gc.PlayerMaxHP = max;
        gc.PlayerHP = max * ratio;
    }

    /// <summary>
    /// Bazaar (progresso: nível, vendas, reputação, recorde): o JsonToObject do jogo devolve NULO para o
    /// BazaarSaveData inteiro, porque SaleItemCount, BazaarMaxSaleData e BazaarSaleData não têm construtor vazio (até a
    /// D52 a parte se desligava na 1ª mudança). Essas listas são montadas à mão. Os avisos de interface
    /// (showSideInfoType, IsShowNotificationDialog) ficam os deste jogo.
    /// </summary>
    private static void ApplyBazaar(string json)
    {
        var bm = BazaarManager.Instance;
        var local = JsonNode.Parse(StringExtensions.ToJson(bm.ToBazaarSaveData()))!.AsObject();
        var node = JsonNode.Parse(json)!.AsObject();
        foreach (var k in new[] { "showSideInfoType", "IsShowNotificationDialog" })
            node[k] = local[k] == null ? null : JsonNode.Parse(local[k]!.ToJsonString());
        bm.FromBazaarSaveData(BuildBazaar(node.ToJsonString()));
    }

    public static BazaarManager.BazaarSaveData BuildBazaar(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        var sales = node["SaleItemCountList"] as JsonArray;
        var maxInfo = node["maxPriceInfo"] as JsonObject;
        node.Remove("SaleItemCountList");
        node.Remove("maxPriceInfo");
        var sd = StringExtensions.JsonToObject<BazaarManager.BazaarSaveData>(node.ToJsonString())
                 ?? throw new Exception("JsonToObject devolveu nulo (bazaar sem as listas)");
        sd.SaleItemCountList = new Il2CppSystem.Collections.Generic.List<BazaarManager.SaleItemCount>();
        if (sales != null)
            foreach (var e in sales)
                sd.SaleItemCountList.Add(new BazaarManager.SaleItemCount(e!["Category"]!.GetValue<short>(), e["SaleCount"]!.GetValue<int>()));
        if (maxInfo != null) sd.maxPriceInfo = BuildMaxPriceInfo(maxInfo);
        return sd;
    }

    private static BazaarManager.MaxPriceInfo BuildMaxPriceInfo(JsonObject m)
    {
        var items = m["itemDic"] as JsonArray;
        var custom = m["customData"];
        m.Remove("itemDic");
        m.Remove("customData");
        var mi = StringExtensions.JsonToObject<BazaarManager.MaxPriceInfo>(m.ToJsonString())
                 ?? throw new Exception("JsonToObject devolveu nulo (recorde do bazaar)");
        if (custom != null) mi.customData = StringExtensions.JsonToObject<BazaarManager.CustomData>(custom.ToJsonString());
        mi.itemDic = new Il2CppSystem.Collections.Generic.List<BazaarManager.BazaarMaxSaleData>();
        if (items == null) return mi;
        foreach (var e in items)
        {
            var s = e!["bazaarSaleData"]!;
            // BazaarSaleData só tem construtores que pedem a tabela de preço: criamos o objeto sem construtor
            // (todos os campos são números) e preenchemos.
            var sale = new BazaarManager.BazaarSaleData(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(
                Il2CppInterop.Runtime.Il2CppClassPointerStore<BazaarManager.BazaarSaleData>.NativeClassPtr))
            {
                Id = s["Id"]!.GetValue<uint>(),
                Quality = s["Quality"]!.GetValue<int>(),
                Freshness = s["Freshness"]!.GetValue<int>(),
                TotalNum = s["TotalNum"]!.GetValue<int>(),
                BaseProfit = s["BaseProfit"]!.GetValue<int>(),
                HtProfit = s["HtProfit"]!.GetValue<int>(),
                CustomProfit = s["CustomProfit"]!.GetValue<int>(),
            };
            mi.itemDic.Add(new BazaarManager.BazaarMaxSaleData(sale, e["isBrand"]!.GetValue<bool>(), e["isTrend"]!.GetValue<bool>()));
        }
        return mi;
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

    /// <summary>Registra no histórico local cada item recebido (AddHistory guarda o maior valor; não apaga nada).</summary>
    private static void UnionItemHistory(string json)
    {
        var ihm = ItemHistoryManager.Instance;
        var local = new Dictionary<uint, (int q, int f)>();
        using (var ld = System.Text.Json.JsonDocument.Parse(StringExtensions.ToJson(ihm.ToSaveData())))
            foreach (var e in Items(ld.RootElement)) local[e.id] = (e.q, e.f);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        foreach (var e in Items(doc.RootElement))
        {
            if (local.TryGetValue(e.id, out var cur) && cur.q >= e.q && cur.f >= e.f) continue;
            ihm.AddHistory(e.id, e.q, e.f);
        }
    }

    private static IEnumerable<(uint id, int q, int f)> Items(System.Text.Json.JsonElement root)
    {
        if (!root.TryGetProperty("PictureBookSaveData", out var pb) || !pb.TryGetProperty("PictureBookDatas", out var list)
            || list.ValueKind != System.Text.Json.JsonValueKind.Array) yield break;
        foreach (var e in list.EnumerateArray())
            yield return (e.GetProperty("ItemId").GetUInt32(), e.GetProperty("MaxQuality").GetInt32(), e.GetProperty("MaxFishSize").GetInt32());
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

    private static T FromUnityJson<T>(string json) where T : Il2CppSystem.Object =>
        JsonUtility.FromJson(json, Il2CppInterop.Runtime.Il2CppType.Of<T>())?.Cast<T>()
        ?? throw new Exception($"JsonUtility devolveu nulo ({typeof(T).Name})");

    private static Part Make(string name, Func<bool> available, Func<string> capture, Action<string> apply) =>
        new() { Name = name, Available = available, Capture = capture, Apply = apply };

    private static readonly Dictionary<string, string> Baseline = new();
    private static readonly HeldMessages Held = new("SharedSync");
    private static int _cursor = -1;
    private static bool _subscribed;
    private float _nextTick;
    public static int Sent, Received;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += ResetSession;
        NetSession.OnHosting += ResetSession;
        NetSession.OnJoined += ResetSession;
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
            if (!Ready()) return;
            if (Held.Count > 0) Held.Drain(OnMessage); // o que chegou enquanto carregava (D63)
            if (Time.unscaledTime < _nextTick) return;
            // Rodízio: uma parte por tick, para espalhar o custo (com ~27 partes, cada uma é conferida a cada ~7 s;
            // pets e animais mudam pouco, e o progresso do mundo não tem pressa). Antes de dormir, FlushAll manda tudo (A03).
            _nextTick = Time.unscaledTime + 0.25f;
            _cursor = (_cursor + 1) % Parts.Count;
            TrySend(Parts[_cursor]);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[SharedSync] {e.Message}"); }
    }

    /// <summary>
    /// A03: confere e manda AGORA todas as partes que mudaram. Chamado quando o convidado deita, antes do aviso "dormi": as
    /// mensagens vão em ordem pelo mesmo canal, então o host aplica estas mudanças antes de contar o convidado como dormindo
    /// (sem isso, uma mudança feita até ~7 s antes de deitar podia ficar fora do save da manhã).
    /// </summary>
    public static int FlushAll()
    {
        if (!Ready()) return 0;
        var n = 0;
        foreach (var p in Parts)
        {
            try { if (TrySend(p)) n++; }
            catch (Exception e) { Plugin.Logger.LogWarning($"[SharedSync] enviar '{p.Name}' antes de dormir: {e.Message}"); }
        }
        return n;
    }

    /// <summary>Captura a parte e, se mudou desde o último envio/recebimento, manda. Devolve se mandou.</summary>
    private static bool TrySend(Part p)
    {
        if (p.Disabled || !p.Available() || TestDisabled(p.Name)) return false;
        if (p.Name == "festival" && EventWatch.HoldFestivalSync) return false; // D84: quem assiste não manda o festival dele
        if (p.InFlight)
        {
            if (Time.unscaledTime - p.InFlightAt < 5f) return false;
            p.InFlight = false; // a confirmação não veio (parte desligada no host?): não trava para sempre
            Plugin.Logger.LogWarning($"[SharedSync] '{p.Name}': sem confirmação do host em 5 s; enviando de novo");
        }
        string json;
        try { json = p.Capture(); }
        catch (Exception e)
        {
            // Falha ao capturar é TEMPORÁRIA (ex.: o Bazaar só fica completo no dia e no mapa dele): só pula.
            if (!p.CaptureFailLogged) { p.CaptureFailLogged = true; Plugin.Logger.LogInfo($"[SharedSync] '{p.Name}' indisponível agora ({FirstLine(e.Message)}); tentando de novo depois"); }
            return false;
        }
        var sig = Signature(json);
        var host = NetSession.Current == NetSession.Mode.Host;
        if (!Baseline.TryGetValue(p.Name, out var old))
        {
            Baseline[p.Name] = sig;
            // D91: o primeiro estado é a base. No host, a versão 0; no convidado, o mundo que veio do host (versão ainda
            // desconhecida: a primeira mescla no host cai na de duas vias, como antes).
            if (host) Remember(p, json);
            else { p.KnownJson = json; p.Version = -1; }
            return false;
        }
        if (old == sig) return false;
        Baseline[p.Name] = sig;
        // Ajudante do Bazaar (D56): o jogo dele abre e fecha uma loja "de mentira"; o progresso do Bazaar é só do dono.
        if (p.Name.StartsWith("bazaar") && BazaarHelpSync.SuppressWrites) return false;
        var packed = LetterSync.Pack(json);
        var name = p.Name;
        if (host)
        {
            var v = Remember(p, json);
            SendToClients(name, v, packed, except: NetSession.LocalId, ackFor: NetSession.UnknownId);
        }
        else
        {
            var baseV = p.Version;
            NetSession.SendGame(MsgType.SharedState, w => { w.Put(name); w.Put(baseV); w.Put(false); w.PutBlob(packed); }, DeliveryMethod.ReliableOrdered);
            p.KnownJson = json; // a confirmação do host já traz esta mudança: é a base dela
            p.InFlight = true;
            p.InFlightAt = Time.unscaledTime;
        }
        Sent++;
        Plugin.Logger.LogInfo($"[SharedSync] {name} mudou: enviado ({packed.Length} bytes)");
        DevBridge.PushEvent($"compartilhado:enviado:{name}");
        return true;
    }

    // Testes: SOSGB_TEST_DISABLE_SHARED="parte1,parte2" desliga partes nesta execução (para isolar problemas).
    private static string[] _testDisabled;
    private static bool TestDisabled(string name)
    {
        _testDisabled ??= (Environment.GetEnvironmentVariable("SOSGB_TEST_DISABLE_SHARED") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        return Array.IndexOf(_testDisabled, name) >= 0;
    }

    private static string FirstLine(string s) { var i = s.IndexOf((char)10); return i < 0 ? s : s.Substring(0, i); }

    /// <summary>
    /// Sessão nova ou mundo recebido de novo (A08): tudo volta a sincronizar do zero, inclusive partes desligadas por falha na
    /// sessão anterior (antes ficavam desligadas até fechar o jogo, sem aviso).
    /// </summary>
    private static void ResetSession()
    {
        Baseline.Clear();
        JoinVersions.Clear();
        foreach (var p in Parts)
        {
            if (p.Disabled) Plugin.Logger.LogInfo($"[SharedSync] parte '{p.Name}' religada (sessão nova)");
            p.Disabled = false;
            p.Failures = 0;
            p.CaptureFailLogged = false;
            p.Version = -1;
            p.KnownJson = null;
            p.History.Clear();
            p.InFlight = false;
        }
    }

    private const int MaxFailures = 3;

    /// <summary>
    /// Falha ao aplicar o que veio do outro (A08): tenta de novo na próxima atualização; na 3ª falha seguida, desliga a parte
    /// (não fica repetindo uma aplicação inválida) e AVISA na tela, porque a partir daí aquela parte do mundo pode divergir.
    /// </summary>
    private static void Disable(Part p, string what, Exception e)
    {
        p.Failures++;
        if (p.Failures < MaxFailures)
        {
            Plugin.Logger.LogWarning($"[SharedSync] parte '{p.Name}': falha ao {what} ({p.Failures}/{MaxFailures}; tenta de novo na próxima): {e.Message}");
            return;
        }
        p.Disabled = true;
        Plugin.Logger.LogWarning($"[SharedSync] parte '{p.Name}' desligada (falha ao {what}, {MaxFailures}x seguidas): {e.Message}");
        try { GameNotify.Show($"Multiplayer: \"{p.Name}\" parou de sincronizar (erro). Reconecte e gere o log (F9)."); }
        catch { /* aviso é só ajuda */ }
    }

    /// <summary>Host → convidados. ackFor: quem mandou a mudança que este canônico incorpora (para ele, é a confirmação).</summary>
    private static void SendToClients(string name, int version, byte[] packed, byte except, byte ackFor)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id != except)
            {
                var ack = id == ackFor;
                NetSession.SendTo(id, MsgType.SharedState, w => { w.Put(name); w.Put(version); w.Put(ack); w.PutBlob(packed); }, DeliveryMethod.ReliableOrdered);
            }
    }

    // D91: versão de cada parte na foto do mundo que cada convidado recebeu (base da 1ª mudança dele, que chega com v-1).
    private static readonly Dictionary<byte, Dictionary<string, int>> JoinVersions = new();

    /// <summary>Host, antes de fotografar o mundo: manda o que estiver pendente e devolve a versão de cada parte.</summary>
    public static Dictionary<string, int> VersionsForSnapshot()
    {
        var d = new Dictionary<string, int>();
        if (NetSession.Current != NetSession.Mode.Host) return d;
        foreach (var p in Parts)
        {
            try { if (Ready()) TrySend(p); } catch { }
            if (p.Version >= 0) d[p.Name] = p.Version;
        }
        return d;
    }

    public static void SetJoinVersions(byte id, Dictionary<string, int> versions) => JoinVersions[id] = versions;

    /// <summary>Host: registra um novo estado canônico (versão nova) e devolve o número.</summary>
    private static int Remember(Part p, string json)
    {
        p.Version++;
        p.History[p.Version] = json;
        p.History.Remove(p.Version - HistoryKeep);
        return p.Version;
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.SharedState) return;
        if (!Ready()) { Held.HoldIfAllowed(type, from, r); return; } // D63: guarda em vez de descartar
        var name = r.GetString();
        var version = r.GetInt();
        var ack = r.GetBool();
        var packed = r.GetBlob();
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        if (name == "festival" && EventWatch.HoldFestivalSync)
        {
            // D84: assistindo à cerimônia de outro jogador. O fim da cerimônia é disparado pela MUDANÇA de estado do festival
            // (FestivalEnd: inscrito → terminado); se o estado "terminado" do outro chegasse antes, a cena daqui nunca acabava
            // (P2 presa no fim). Guarda a versão mais recente e aplica quando a cena daqui terminar.
            _heldFestival = packed;
            if (NetSession.Current == NetSession.Mode.Host) SendToClients(name, version, packed, except: from, ackFor: NetSession.UnknownId);
            Plugin.Logger.LogInfo($"[SharedSync] festival de #{from} guardado até a cena daqui terminar");
            return;
        }
        _applyingFromGuest = NetSession.Current == NetSession.Mode.Host;
        _filtered = false;
        string res;
        try { res = ApplyVersioned(name, version, LetterSync.Unpack(packed), from, ack); }
        finally { _applyingFromGuest = false; }
        if (res == "ok" || res == "igual") Received++;
        if (NetSession.Current == NetSession.Mode.Host && res == "ok")
        {
            // D91: o resultado da mescla é o novo estado canônico; vai para TODOS, inclusive quem mandou (antes o host
            // repassava o pacote recebido, e cada jogo ficava com uma versão diferente quando havia mescla ou filtro).
            var own = Parts.FirstOrDefault(x => x.Name == name);
            if (own != null)
            {
                var canon = own.Capture();
                Baseline[name] = Signature(canon);
                var v = Remember(own, canon);
                SendToClients(name, v, LetterSync.Pack(canon), except: NetSession.UnknownId, ackFor: from);
            }
        }
        Plugin.Logger.LogInfo($"[SharedSync] {name} de #{from} (v{version}): {res}{(JsonMerge3.Conflicts > 0 ? $" (conflitos resolvidos até agora: {JsonMerge3.Conflicts})" : "")}");
        DevBridge.PushEvent($"compartilhado:recebido:{name}:{res}");
    }

    private static byte[] _heldFestival;

    /// <summary>D84: aplica a parte "festival" guardada enquanto a cena era assistida.</summary>
    public static void ReleaseHeldFestival()
    {
        var packed = _heldFestival;
        _heldFestival = null;
        if (packed == null) return;
        string res;
        try { res = ApplyPart("festival", LetterSync.Unpack(packed)); }
        catch (Exception e) { res = "erro: " + e.Message; }
        Plugin.Logger.LogInfo($"[SharedSync] festival guardado aplicado: {res}");
    }

    /// <summary>
    /// D91: aplica o que chegou com a mescla em três vias.
    ///  - Host (veio de um convidado): base = a versão de onde o convidado partiu (History). Sem ela (convidado recém-chegado,
    ///    versão antiga demais), cai na mescla de duas vias de antes.
    ///  - Convidado (veio do host, canônico): base = o último canônico que ele tinha; as mudanças daqui ainda não enviadas
    ///    ficam, e o rodízio as manda em seguida a partir da versão nova.
    /// </summary>
    private static string ApplyVersioned(string name, int version, string json, byte from, bool ack)
    {
        var p = Parts.FirstOrDefault(x => x.Name == name);
        if (p == null) return "parte desconhecida";
        if (p.Disabled) return "parte desligada";
        if (!p.Available()) return "indisponível agora";
        var host = NetSession.Current == NetSession.Mode.Host;
        if (!host && ack) p.InFlight = false;
        string localJson;
        try { localJson = p.Capture(); }
        catch { return "indisponível agora (não deu para ler o estado local)"; }
        try
        {
            string baseJson = null;
            if (host)
            {
                // v-1 = o convidado ainda não recebeu nenhum canônico: a base é a foto do mundo que ele recebeu.
                if (version < 0 && JoinVersions.TryGetValue(from, out var jv) && jv.TryGetValue(name, out var j)) version = j;
                p.History.TryGetValue(version, out baseJson);
            }
            else baseJson = p.KnownJson;
            if (!host && Signature(json) == Signature(localJson))
            {
                // O canônico é o que já temos (o eco da nossa própria mudança): não reaplica.
                p.Version = version; p.KnownJson = json; Baseline[name] = Signature(json);
                return "igual";
            }
            var merged = baseJson != null ? JsonMerge3.Merge(baseJson, localJson, json, Volatile) : Merge(json, localJson);
            p.Apply(merged);
            p.Failures = 0;
            if (host) Baseline[name] = Signature(p.Capture());
            else { p.Version = version; p.KnownJson = json; Baseline[name] = Signature(json); }
            return "ok";
        }
        catch (Exception e) { Disable(p, "aplicar", e); return "erro: " + e.Message; }
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
            p.Failures = 0;
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

    /// <summary>Ponte (D91): aplica um estado como AÇÃO LOCAL (sem mexer na base): o rodízio vê a mudança e manda.</summary>
    public static string ApplyLocal(string name, string json)
    {
        var p = Parts.FirstOrDefault(x => x.Name == name);
        if (p == null) return "parte desconhecida";
        try { p.Apply(json); return "ok"; }
        catch (Exception e) { return "erro: " + e.Message; }
    }

    public static string CaptureJson(string name) => Parts.FirstOrDefault(x => x.Name == name)?.Capture();

    public static object Status()
    {
        var parts = new Dictionary<string, string>();
        foreach (var p in Parts) parts[p.Name] = p.Disabled ? "desligada" : Baseline.ContainsKey(p.Name) ? $"ok v{p.Version}" : "-";
        return new { parts, sent = Sent, received = Received, conflicts = JsonMerge3.Conflicts };
    }
}
