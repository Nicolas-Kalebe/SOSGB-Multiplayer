// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d56.
using System;
using BokuMono;
using HarmonyLib;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Ajudante na barraca do Bazaar (D56, escolha do usuário: "dentro da barraca, com os botões do jogo").
///
///  - Com outro jogador vendendo (trava da D51), quem escolhe "Abrir loja" entra no MODO AJUDANTE: o jogo dele abre
///    a loja normalmente (câmera, botões), mas os clientes e os balcões são os do dono (BazaarViewSync espelha).
///  - No modo ajudante, as ações viram PEDIDOS ao dono, que executa no jogo dele:
///      • Enter num item (ActionAccessShopShelf) → "atender": o dono aponta o balcão (UpdateCurrentShopShelf) e, se o
///        cliente está esperando ali (IsBuyStandby), chama Sell — o mesmo que o Enter dele faria;
///      • escolha no mini deck (SelectExhibit) → "pôr item": o dono tira o item do deck e põe no espaço
///        (ExhibitionShelf.PutIn + ReExhibit). O que estava no espaço volta para o deck.
///  - Durante a venda só o jogo do dono mexe no deck e nos balcões: o deck é sincronizado pelo conteúdo, e dois
///    jogos tirando itens ao mesmo tempo poderiam duplicar itens (preocupação do usuário). Por isso o ajudante não
///    envia o deck nem o progresso do Bazaar enquanto ajuda (e por alguns segundos depois), e no fim volta ao último
///    deck recebido.
/// </summary>
public class BazaarHelpSync : MonoBehaviour
{
    public BazaarHelpSync(IntPtr ptr) : base(ptr) { }

    private const byte KindServe = 1, KindPut = 2;
    private const float GraceSeconds = 8f;

    /// <summary>Este jogo está com a loja aberta como ajudante de outro jogador.</summary>
    public static bool Active { get; private set; }

    private static float _endedAt = -100;
    private static float _startedAt;
    private static bool _sawShop;

    /// <summary>O ajudante não envia deck nem progresso do Bazaar (só o dono escreve), durante e logo depois de ajudar.</summary>
    public static bool SuppressWrites => Active || Time.unscaledTime - _endedAt < GraceSeconds;

    private static bool _pendingRevert;
    private static int _targetShelf = -1, _targetSlot = -1;
    private static bool _subscribed;
    public static int Requests, Executed;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
    }

    private static BazaarManager Bm => BazaarManager.IsInstance ? BazaarManager.Instance : null;

    private void Update()
    {
        try
        {
            var bm = Bm;
            if (NetSession.Current == NetSession.Mode.Offline || bm == null)
            {
                if (Active) Stop("sessão encerrada");
                return;
            }
            // Reserva: se a loja abriu com outro jogador segurando a barraca e o patch não pegou, entra no modo ajudante.
            if (!Active && bm.IsInShop && OtherOwner()) Start();
            if (Active)
            {
                // O modo liga ANTES de a loja abrir (patch em ChoiceBarActionOpenShop): só desliga depois de ela ter
                // aberto e fechado, ou se não abriu em 15 s (cancelou).
                if (bm.IsInShop) _sawShop = true;
                else if (_sawShop) Stop("loja fechada");
                else if (Time.unscaledTime - _startedAt > 15f) Stop("a loja não abriu");
            }
            if (_pendingRevert && !SuppressWrites)
            {
                _pendingRevert = false;
                StorageSync.RevertDeckToLastReceived();
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] {e.Message}"); }
    }

    private static bool OtherOwner()
    {
        var h = StorageSync.BazaarHolder;
        return h != StorageSync.NoHolder && h != NetSession.LocalId;
    }

    private static void Start()
    {
        if (Active) return;
        Active = true;
        _sawShop = false;
        _startedAt = Time.unscaledTime;
        var h = StorageSync.BazaarHolder;
        var name = NetSession.Players.TryGetValue(h, out var p) ? p.Name : "o dono";
        GameNotify.Show($"Ajudando na barraca de {name}: atenda com Enter e reponha com Tab.");
        Plugin.Logger.LogInfo($"[BazaarHelp] modo ajudante ligado (dono #{h})");
    }

    private static void Stop(string why)
    {
        if (!Active) return;
        Active = false;
        _endedAt = Time.unscaledTime;
        _pendingRevert = true;
        ClearLocalShelf();
        Plugin.Logger.LogInfo($"[BazaarHelp] modo ajudante desligado ({why})");
    }

    /// <summary>Esvazia os balcões deste jogo (eram cópia dos do dono) para o jogo não "devolver" ao deck itens que não são dele.</summary>
    public static void ClearLocalShelf()
    {
        try
        {
            var esm = InventoryManager.Instance?.ExhibitionShelf;
            if (esm == null) return;
            var datas = esm.ItemDatas;
            for (var i = 0; i < datas.Length; i++)
                if (datas[i] != null && datas[i].ItemId != 0) esm.PutOut(i);
            if (Bm != null && Bm.IsInShop) Bm.ReExhibit();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] limpar balcões: {e.Message}"); }
    }

    /// <summary>Chamado quando o dono avisa que fechou a loja: limpa os balcões antes de o jogo do ajudante fechar.</summary>
    public static void OwnerClosed()
    {
        if (!Active) return;
        ClearLocalShelf();
        GameNotify.Show("O dono fechou a loja. Aperte Esc para sair da barraca.");
    }

    // ---------- espaço atual do ajudante ----------

    private static (int shelf, int slot) NearestSlot(BazaarManager bm)
    {
        var shelfIdx = bm.GetCurrentShelfIndex();
        var shelves = bm.ShopShelfs;
        if (shelves == null || shelfIdx < 0 || shelfIdx >= shelves.Length || shelves[shelfIdx] == null) return (-1, -1);
        var pl = GameController.Instance.GetPlayer().transform.position;
        var its = shelves[shelfIdx].GetShopItems();
        int best = -1; var bd = float.MaxValue;
        for (var k = 0; k < its.Length; k++)
        {
            var h = its[k].GetHelpTarget().position;
            var d = (new Vector2(h.x, h.z) - new Vector2(pl.x, pl.z)).sqrMagnitude;
            if (d < bd) { bd = d; best = k; }
        }
        return (shelfIdx, best);
    }

    private static bool LocalSlotHasItem(int shelf, int slot)
    {
        var esm = InventoryManager.Instance?.ExhibitionShelf;
        if (esm == null || shelf < 0 || slot < 0) return false;
        var datas = esm.ItemDatas;
        var g = shelf * 4 + slot;
        return g < datas.Length && datas[g] != null && datas[g].ItemId != 0 && datas[g].Stack > 0;
    }

    // ---------- pedidos ----------

    private static void SendRequest(byte kind, int shelf, int slot, BokuMono.Data.ItemData item = null)
    {
        var me = NetSession.LocalId;
        uint id = item?.ItemId ?? 0;
        int st = item?.Stack ?? 0, q = item?.QualityValue ?? 0, f = item?.FreshnessValue ?? 0;
        NetSession.SendGame(MsgType.BazaarHelp, w =>
        {
            w.Put(me); w.Put(kind); w.Put((byte)shelf); w.Put((byte)slot);
            w.Put(id); w.Put(st); w.Put(q); w.Put(f);
        }, DeliveryMethod.ReliableOrdered);
        Requests++;
        Plugin.Logger.LogInfo($"[BazaarHelp] pedido {(kind == KindServe ? "atender" : "pôr item")} balcão {shelf} espaço {slot}{(item != null ? $" item {id}x{st}" : "")}");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.BazaarHelp) return;
        var helper = r.GetByte();
        var kind = r.GetByte();
        var shelf = r.GetByte();
        var slot = r.GetByte();
        var item = new BokuMono.Data.ItemData { ItemId = r.GetUInt(), Stack = r.GetInt(), QualityValue = r.GetInt(), FreshnessValue = r.GetInt() };
        if (NetSession.Current == NetSession.Mode.Host && helper != from) return;
        var owner = StorageSync.BazaarHolder;
        if (owner == NetSession.LocalId) { Execute(kind, shelf, slot, item, helper); return; }
        // O host repassa ao dono (as mensagens 20+ só vão ao host).
        if (NetSession.Current == NetSession.Mode.Host && owner != StorageSync.NoHolder)
            NetSession.SendTo(owner, MsgType.BazaarHelp, w =>
            {
                w.Put(helper); w.Put(kind); w.Put(shelf); w.Put(slot);
                w.Put(item.ItemId); w.Put(item.Stack); w.Put(item.QualityValue); w.Put(item.FreshnessValue);
            }, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Dono: executa o pedido do ajudante no próprio jogo.</summary>
    private static void Execute(byte kind, int shelf, int slot, BokuMono.Data.ItemData item, byte helper)
    {
        var bm = Bm;
        if (bm == null || !bm.IsInShop || Active) return;
        var shelves = bm.ShopShelfs;
        if (shelves == null || shelf >= shelves.Length || shelves[shelf] == null) return;
        var its = shelves[shelf].GetShopItems();
        if (slot >= its.Length) return;
        try
        {
            if (kind == KindServe)
            {
                // O índice do espaço é do balcão atual: aponta o balcão do ajudante (o jogo volta ao do dono no quadro seguinte).
                bm.UpdateCurrentShopShelf(its[slot].GetSalePointObjId());
                if (!bm.IsBuyStandby(slot)) return;
                bm.Sell(slot, false);
                Executed++;
                Plugin.Logger.LogInfo($"[BazaarHelp] #{helper} atendeu no balcão {shelf} espaço {slot}");
            }
            else if (kind == KindPut)
            {
                var inv = InventoryManager.Instance;
                var deck = inv.DeckStorage;
                var esm = inv.ExhibitionShelf;
                var datas = deck.ItemDatas;
                var from = -1;
                for (var i = 0; i < datas.Length && from < 0; i++)
                {
                    var d = datas[i];
                    if (d != null && d.ItemId == item.ItemId && d.QualityValue == item.QualityValue && d.FreshnessValue == item.FreshnessValue && d.Stack > 0) from = i;
                }
                if (from < 0) { Plugin.Logger.LogInfo($"[BazaarHelp] item {item.ItemId} não está mais no deck"); return; }
                var take = deck.PutOut(from, Math.Min(item.Stack > 0 ? item.Stack : datas[from].Stack, datas[from].Stack));
                if (take == null) return;
                var g = shelf * 4 + slot;
                var cur = esm.ItemDatas;
                if (g < cur.Length && cur[g] != null && cur[g].ItemId != 0)
                {
                    var old = esm.PutOut(g);
                    if (old != null) deck.PutIn(old, out _, false);
                }
                esm.PutIn(take, g, out var rem);
                if (rem != null && rem.ItemId != 0 && rem.Stack > 0) deck.PutIn(rem, out _, false);
                bm.ReExhibit();
                Executed++;
                Plugin.Logger.LogInfo($"[BazaarHelp] #{helper} pôs {take.ItemId}x{take.Stack} no balcão {shelf} espaço {slot}");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] executar pedido: {e.Message}"); }
    }

    // ---------- estado dos espaços (avisos de vender/pôr/trocar) ----------

    /// <summary>Recebido do dono: estado de cada espaço (balcão×4 + espaço), valores de BazaarManager.ShopShelfAccessState; 255 = sem espaço.</summary>
    public static byte[] OwnerStates = Array.Empty<byte>();

    private static int _gameShelfId;
    private static bool _computing;

    /// <summary>
    /// Dono: estado de cada espaço de todos os balcões. GetShopShelfAccessState usa o balcão ATUAL; apontamos cada
    /// balcão (UpdateCurrentShopShelf) e no fim voltamos ao que o jogo tinha apontado (pela posição do dono).
    /// </summary>
    public static byte[] ComputeAccessStates(BazaarManager bm)
    {
        var shelves = bm.ShopShelfs;
        if (shelves == null) return Array.Empty<byte>();
        var res = new byte[shelves.Length * 4];
        for (var i = 0; i < res.Length; i++) res[i] = 255;
        _computing = true;
        try
        {
            for (var sh = 0; sh < shelves.Length; sh++)
            {
                if (shelves[sh] == null) continue;
                var its = shelves[sh].GetShopItems();
                for (var k = 0; k < its.Length && k < 4; k++)
                {
                    bm.UpdateCurrentShopShelf(its[k].GetSalePointObjId());
                    res[sh * 4 + k] = (byte)bm.GetShopShelfAccessState(k);
                }
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] estados dos espaços: {e.Message}"); }
        finally
        {
            if (_gameShelfId != 0) bm.UpdateCurrentShopShelf(_gameShelfId);
            _computing = false;
        }
        return res;
    }

    private static int OwnerState(int idx)
    {
        var bm = Bm;
        var g = bm.GetCurrentShelfIndex() * 4 + idx;
        return g >= 0 && g < OwnerStates.Length ? OwnerStates[g] : 255;
    }

    public static object Status() => new { active = Active, suppress = SuppressWrites, target = $"{_targetShelf}:{_targetSlot}", requests = Requests, executed = Executed };

    // ---------- patches (só agem no modo ajudante) ----------

    /// <summary>"Abrir loja" com outro jogador vendendo → modo ajudante (antes de o jogo abrir a loja).</summary>
    [HarmonyPatch(typeof(BazaarManager), "ChoiceBarActionOpenShop")]
    private static class OpenShopPatch
    {
        private static void Prefix()
        {
            if (NetSession.Current != NetSession.Mode.Offline && OtherOwner()) Start();
        }
    }

    /// <summary>Enter num item: no modo ajudante, se há item no espaço, vira pedido "atender" ao dono.</summary>
    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.ActionAccessShopShelf))]
    private static class AccessPatch
    {
        private static bool Prefix(int idx)
        {
            if (!Active) return true;
            var bm = Bm;
            var shelf = bm.GetCurrentShelfIndex();
            _targetShelf = shelf;
            _targetSlot = idx;
            if (!LocalSlotHasItem(shelf, idx)) return true; // espaço vazio: deixa abrir o mini deck (repor)
            SendRequest(KindServe, shelf, idx);
            return false;
        }
    }

    /// <summary>Tab: guarda o espaço onde o ajudante está (alvo da escolha no mini deck).</summary>
    [HarmonyPatch(typeof(UIBazaarMainPage), nameof(UIBazaarMainPage.OpenMiniDeck))]
    private static class MiniDeckPatch
    {
        private static void Prefix()
        {
            if (!Active || Bm == null) return;
            var (shelf, slot) = NearestSlot(Bm);
            _targetShelf = shelf;
            _targetSlot = slot;
        }
    }

    /// <summary>Escolha no mini deck: no modo ajudante vira pedido "pôr item" (o idx conta só as entradas não vazias do deck).</summary>
    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.SelectExhibit))]
    private static class SelectPatch
    {
        private static bool Prefix(int idx)
        {
            if (!Active) return true;
            var deck = InventoryManager.Instance.DeckStorage.ItemDatas;
            BokuMono.Data.ItemData chosen = null;
            var n = 0;
            for (var i = 0; i < deck.Length; i++)
            {
                var d = deck[i];
                if (d == null || d.ItemId == 0 || d.Stack <= 0) continue;
                if (n++ == idx) { chosen = d; break; }
            }
            if (chosen != null && _targetShelf >= 0 && _targetSlot >= 0)
                SendRequest(KindPut, _targetShelf, _targetSlot, chosen);
            return false;
        }
    }

    /// <summary>Guarda o balcão que o jogo do dono aponta pela posição dele (para voltar depois de ComputeAccessStates).</summary>
    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.UpdateCurrentShopShelf))]
    private static class CurrentShelfPatch
    {
        private static void Prefix(int id)
        {
            if (!_computing) _gameShelfId = id;
        }
    }

    /// <summary>Ajudante: o estado do espaço (o que faz o aviso "vender" aparecer) é o do dono.</summary>
    // EXPERIMENTO: desligado
    //[HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.GetShopShelfAccessState))]
    private static class AccessStatePatch
    {
        private static bool Prefix(int idx, ref BazaarManager.ShopShelfAccessState __result)
        {
            if (!Active) return true;
            var st = OwnerState(idx);
            if (st == 255) return true;
            __result = (BazaarManager.ShopShelfAccessState)st;
            return false;
        }
    }

    /// <summary>Ajudante: "cliente esperando neste espaço" vem do dono (estado Vender).</summary>
    // EXPERIMENTO: desligado
    //[HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.IsBuyStandby))]
    private static class StandbyPatch
    {
        private static bool Prefix(int idx, ref bool __result)
        {
            if (!Active) return true;
            var st = OwnerState(idx);
            if (st == 255) return true;
            __result = st == (int)BazaarManager.ShopShelfAccessState.Sell;
            return false;
        }
    }

    /// <summary>Segurança: o jogo do ajudante nunca vende (as vendas são do dono).</summary>
    [HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.Sell))]
    private static class SellPatch
    {
        private static bool Prefix() => !Active;
    }
}
