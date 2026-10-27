// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d66 e #d85.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// D66 (pedido do usuário, P12): a ferramenta que alguém deixou no moinho para melhorar só sai para quem deixou. O resto do
/// moinho continua compartilhado (SharedSync, parte "windmill").
///
/// D85 (revisão A01): cada encomenda de ferramenta tem DONO. Quem faz a encomenda (WindmillCraftingManager.Crafting com
/// receita de ferramenta) registra {moinho, item que vai sair, o PlayerGuid dele, início da encomenda}; o registro vai para o
/// host, que guarda num arquivo ao lado dos perfis e manda a lista a todos (inclusive a quem entra depois). Na retirada
/// (TakeOutAction), uma ferramenta pronta com registro só sai para um jogador que tenha um registro daquele item; quem
/// retira consome o registro. Antes valia só "quem já tem uma ferramenta daquele tipo na mochila não é o dono" (a receita
/// de upgrade consome a ferramenta antiga), que falhava com dois jogadores melhorando o mesmo tipo: os dois passavam.
/// Ferramentas sem registro (encomendas de antes desta versão ou feitas sozinho) seguem a regra antiga.
///
/// As ferramentas que não são de quem retira saem da lista antes da retirada e voltam quando ela termina (o jogo avisa o
/// fim pelo endAction, depois da mensagem "Conseguiu ..."). Sem isto, quem retirasse a ferramenta de outro ganhava o
/// upgrade (o jogo troca a ferramenta da mochila dele pela nova; testado) e o dono ficava sem a dele.
/// </summary>
internal static class WindmillToolGuard
{
    private const byte OpAdd = 1, OpRemove = 2, OpList = 3;

    private sealed class Order
    {
        public int Type { get; set; }       // WindmillCraftingType
        public uint Item { get; set; }      // ferramenta que vai sair
        public string Owner { get; set; }   // PlayerGuid de quem encomendou
        public long Start { get; set; }     // CraftingData.StartTimeTicks (identifica a encomenda)
    }

    private static readonly List<Order> Orders = new();
    private static Dictionary<uint, BokuMono.Data.ToolType> _toolOfItem;

    public static void Init()
    {
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnHosting += Load;
        NetSession.OnJoined += () => Orders.Clear(); // a lista vem do host
    }

    /// <summary>Item → tipo de ferramenta, pelas receitas de upgrade do moinho (o que elas fazem e a ferramenta que pedem).</summary>
    private static BokuMono.Data.ToolType ToolTypeOf(uint itemId)
    {
        if (_toolOfItem == null)
        {
            var map = new Dictionary<uint, BokuMono.Data.ToolType>();
            foreach (var r in MasterDataManager.Instance.WindmillCraftingMaster.list)
            {
                if (r == null || (int)r.ToolType == 0) continue;
                map[r.CraftingItemId] = r.ToolType;
                if (r.RequiredItemId != null && r.RequiredItemId.Count > 0 && r.RequiredItemId[0] != 0) map[r.RequiredItemId[0]] = r.ToolType;
            }
            _toolOfItem = map;
        }
        return _toolOfItem.TryGetValue(itemId, out var t) ? t : 0;
    }

    // ---------- registro (host guarda e distribui) ----------

    private static void Change(byte op, Order o)
    {
        if (NetSession.Current == NetSession.Mode.Host) { ApplyOp(op, o); Save(); Broadcast(); }
        else
        {
            ApplyOp(op, o); // já vale aqui; a lista do host substitui quando chegar
            NetSession.SendGame(MsgType.WindmillOwner, w => { w.Put(op); Write(w, o); }, DeliveryMethod.ReliableOrdered);
        }
    }

    private static void ApplyOp(byte op, Order o)
    {
        if (op == OpAdd)
        {
            if (!Orders.Any(x => Same(x, o))) Orders.Add(o);
        }
        else if (op == OpRemove)
        {
            var i = Orders.FindIndex(x => Same(x, o));
            if (i >= 0) Orders.RemoveAt(i);
        }
    }

    private static bool Same(Order a, Order b) => a.Type == b.Type && a.Item == b.Item && a.Owner == b.Owner && a.Start == b.Start;

    private static void Write(NetDataWriter w, Order o) { w.Put(o.Type); w.Put(o.Item); w.Put(o.Owner ?? ""); w.Put(o.Start); }

    private static Order Read(NetDataReader r) => new() { Type = r.GetInt(), Item = r.GetUInt(), Owner = r.GetString(), Start = r.GetLong() };

    private static void WriteList(NetDataWriter w)
    {
        w.Put(OpList);
        w.Put(Orders.Count);
        foreach (var o in Orders) Write(w, o);
    }

    private static void Broadcast()
    {
        if (NetSession.Current == NetSession.Mode.Host) NetSession.SendGame(MsgType.WindmillOwner, WriteList, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Host: chamado pelo WorldSync depois de mandar o mundo para quem acabou de entrar.</summary>
    public static void SendList(byte playerId) => NetSession.SendTo(playerId, MsgType.WindmillOwner, WriteList, DeliveryMethod.ReliableOrdered);

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.WindmillOwner) return;
        try
        {
            var op = r.GetByte();
            if (op == OpList)
            {
                if (NetSession.Current != NetSession.Mode.Client || from != Protocol.HostPlayerId) return;
                Orders.Clear();
                var n = r.GetInt();
                for (var i = 0; i < n; i++) Orders.Add(Read(r));
                return;
            }
            if (NetSession.Current != NetSession.Mode.Host) return;
            var o = Read(r);
            ApplyOp(op, o);
            Save();
            Broadcast();
            Plugin.Logger.LogInfo($"[Moinho] encomenda de ferramenta {(op == OpAdd ? "registrada" : "retirada")} por #{from}: item {o.Item} no moinho {o.Type}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] dono das encomendas: {e.Message}"); }
    }

    private static string StorePath()
    {
        var ui = ExternalData.Instance.UserInfo;
        string Clean(string s) => new string((s ?? "x").Where(char.IsLetterOrDigit).ToArray());
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STORY OF SEASONS Grand Bazaar", "SOSGB-MP");
        if (Plugin.DevMode) dir = Path.Combine(dir, "dev"); // testes: longe dos dados reais (como os perfis)
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"windmill_owners_{Clean(ui.FarmName)}_{Clean(ui.PlayerName)}.json");
    }

    private static void Load()
    {
        Orders.Clear();
        try
        {
            var path = StorePath();
            if (File.Exists(path)) Orders.AddRange(JsonSerializer.Deserialize<List<Order>>(File.ReadAllText(path)) ?? new List<Order>());
            if (Orders.Count > 0) Plugin.Logger.LogInfo($"[Moinho] {Orders.Count} encomenda(s) de ferramenta com dono carregada(s)");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] ler donos das encomendas: {e.Message}"); }
    }

    private static void Save()
    {
        try
        {
            var path = StorePath();
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Orders));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] gravar donos das encomendas: {e.Message}"); }
    }

    /// <summary>Teste (ponte): a lista de encomendas com dono.</summary>
    public static object Status() => Orders.Select(o => $"moinho {o.Type} item {o.Item} dono {(o.Owner == Plugin.EffectivePlayerGuid ? "eu" : o.Owner)} início {o.Start}").ToList();

    /// <summary>Teste (ponte): registra uma encomenda como se outro jogador (ou eu) a tivesse feito.</summary>
    public static string TestAdd(int type, uint item, string owner)
    {
        Change(OpAdd, new Order { Type = type, Item = item, Owner = owner == "eu" ? Plugin.EffectivePlayerGuid : owner, Start = DateTime.UtcNow.Ticks });
        return "ok";
    }

    // ---------- ganchos ----------

    /// <summary>Encomenda feita aqui: se é de ferramenta, registra o dono (D85).</summary>
    [HarmonyPatch(typeof(WindmillCraftingManager), nameof(WindmillCraftingManager.Crafting))]
    private static class CraftingPatch
    {
        private static void Postfix(WindmillCraftingManager __instance, BokuMono.Data.WindmillCraftingMasterData recipe)
        {
            if (NetSession.Current == NetSession.Mode.Offline || recipe == null || (int)recipe.ToolType == 0) return;
            try
            {
                var type = __instance.CraftingType;
                var list = __instance.GetCraftingList(type);
                var start = list != null && list.Count > 0 ? list[list.Count - 1].StartTimeTicks : DateTime.UtcNow.Ticks;
                Change(OpAdd, new Order { Type = (int)type, Item = recipe.CraftingItemId, Owner = Plugin.EffectivePlayerGuid, Start = start });
                Plugin.Logger.LogInfo($"[Moinho] encomenda de ferramenta registrada: item {recipe.CraftingItemId} no moinho {type}");
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] registrar encomenda: {e.Message}"); }
        }
    }

    /// <summary>Encomendas canceladas aqui: os registros delas saem (a ferramenta antiga volta pela lista de cancelados).</summary>
    [HarmonyPatch(typeof(WindmillCraftingManager), nameof(WindmillCraftingManager.CancelCraftingList))]
    private static class CancelPatch
    {
        private static void Prefix(WindmillCraftingManager __instance, WindmillCraftingType craftingType, out List<long> __state)
        {
            __state = new List<long>();
            try
            {
                var list = __instance.GetCraftingList(craftingType);
                if (list != null) foreach (var c in list) if (c != null) __state.Add(c.StartTimeTicks);
            }
            catch { /* sem a lista: nada a tirar */ }
        }

        private static void Postfix(WindmillCraftingType craftingType, List<long> __state)
        {
            if (NetSession.Current == NetSession.Mode.Offline || __state == null || __state.Count == 0) return;
            foreach (var o in Orders.Where(x => x.Type == (int)craftingType && __state.Contains(x.Start)).ToList()) Change(OpRemove, o);
        }
    }

    [HarmonyPatch(typeof(WindmillCraftingManager), nameof(WindmillCraftingManager.TakeOutAction))]
    private static class TakeOutPatch
    {
        private static void Prefix(WindmillCraftingType type, ref Il2CppSystem.Action endAction)
        {
            if (NetSession.Current == NetSession.Mode.Offline) return;
            try
            {
                var list = WindmillCraftingManager.Instance.saveData.GetCompleteList(type); // a lista viva do jogo (testado)
                var bagTools = InventoryManager.Instance.BagToolStorage;
                if (list == null || bagTools == null) return;
                var me = Plugin.EffectivePlayerGuid;
                var mine = Orders.Where(o => o.Type == (int)type && o.Owner == me).OrderBy(o => o.Start).ToList();
                var claimed = new List<Order>();
                var kept = new List<BokuMono.Data.ItemData>();
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var it = list[i];
                    if (it == null) continue;
                    var tt = ToolTypeOf(it.ItemId);
                    if ((int)tt == 0) continue;
                    bool allowed;
                    if (Orders.Any(o => o.Type == (int)type && o.Item == it.ItemId))
                    {
                        var m = mine.FirstOrDefault(o => o.Item == it.ItemId);
                        allowed = m != null;
                        if (m != null) { mine.Remove(m); claimed.Add(m); }
                    }
                    else allowed = !bagTools.IsHaved(tt); // sem registro (encomenda antiga): a regra de antes
                    if (allowed) continue;
                    kept.Add(it);
                    list.RemoveAt(i);
                }
                if (kept.Count == 0 && claimed.Count == 0) return;
                if (kept.Count > 0)
                {
                    GameNotify.Show("A ferramenta no moinho é de outro jogador: só quem deixou pode retirar.");
                    Plugin.Logger.LogInfo($"[Moinho] {kept.Count} ferramenta(s) de outro jogador ficaram no moinho {type}");
                }
                var original = endAction;
                endAction = (Il2CppSystem.Action)(() =>
                {
                    Consume(type, claimed);
                    Restore(type, kept);
                    original?.Invoke();
                });
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] separar ferramentas: {e.Message}"); }
        }

        /// <summary>As minhas ferramentas que de fato saíram (não estão mais na lista): os registros delas acabam.</summary>
        private static void Consume(WindmillCraftingType type, List<Order> claimed)
        {
            if (claimed.Count == 0) return;
            try
            {
                var list = WindmillCraftingManager.Instance.saveData.GetCompleteList(type);
                var left = new Dictionary<uint, int>();
                if (list != null) foreach (var it in list) if (it != null) left[it.ItemId] = left.TryGetValue(it.ItemId, out var c) ? c + 1 : 1;
                foreach (var o in claimed)
                {
                    if (left.TryGetValue(o.Item, out var c) && c > 0) { left[o.Item] = c - 1; continue; } // ficou (mochila cheia?)
                    Change(OpRemove, o);
                }
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] fechar encomendas retiradas: {e.Message}"); }
        }

        private static void Restore(WindmillCraftingType type, List<BokuMono.Data.ItemData> kept)
        {
            try
            {
                var sd = WindmillCraftingManager.Instance.saveData;
                for (var i = kept.Count - 1; i >= 0; i--) sd.AddCompleteItem(type, kept[i]);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[Moinho] devolver ferramentas: {e.Message}"); }
        }
    }
}
