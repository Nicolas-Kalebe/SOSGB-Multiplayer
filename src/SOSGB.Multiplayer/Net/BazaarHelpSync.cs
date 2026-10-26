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
            // A loja abriu com a barraca de outro jogador → modo ajudante; fechou → sai.
            if (!Active && bm.IsInShop && OtherOwner()) Start();
            else if (Active && !bm.IsInShop) Stop("loja fechada");
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
        InstallPatches();
        RefreshMiniDeck();
        var h = StorageSync.BazaarHolder;
        var name = NetSession.Players.TryGetValue(h, out var p) ? p.Name : "o dono";
        GameNotify.Show($"Ajudando na barraca de {name}: atenda com Enter e reponha com Tab.");
        Plugin.Logger.LogInfo($"[BazaarHelp] modo ajudante ligado (dono #{h})");
    }

    private static void Stop(string why)
    {
        if (!Active) return;
        Active = false;
        RemovePatches();
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

    /// <summary>
    /// A lista do mini deck (escolher item no Tab) é uma cópia que o jogo só monta em certos momentos (ex.: "Ver
    /// estoque"): com o deck vindo da rede ela fica vazia ou velha e o menu abre vazio (visto pelo usuário). Recria a
    /// lista e a barra do mini deck, sem mexer se o menu estiver aberto agora.
    /// </summary>
    public static void RefreshMiniDeck()
    {
        try
        {
            var deck = InventoryManager.Instance?.DeckStorage;
            if (deck == null) return;
            FixDeckNumbers();
            deck.GetMiniDeckItemsData();
            var bm = Bm;
            if (bm == null || !bm.IsInShop) return;
            var page = UnityEngine.Object.FindObjectOfType<UIBazaarMainPage>();
            if (page != null && page.IsMiniDeck()) return;
            bm.ResetMiniDeck();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] lista do mini deck: {e.Message}"); }
    }

    /// <summary>
    /// Itens do deck precisam de um número de deck (ItemData.DeckNumber, 1, 2, 3...), que o jogo dá ao guardar pelo
    /// "Ver estoque". Item com número 0 (vindo por outro caminho) quebra o mini deck da barraca: o painel de detalhes dá
    /// erro e a lista não aparece (achado da D56). Dá o próximo número livre a esses itens. Não muda a assinatura do
    /// deck (StorageSync não compara o número), então não gera envio.
    /// </summary>
    public static void FixDeckNumbers()
    {
        try
        {
            var deck = InventoryManager.Instance?.DeckStorage;
            if (deck == null) return;
            var missing = false;
            foreach (var d in deck.ItemDatas) if (d != null && d.ItemId != 0 && d.Stack > 0 && d.DeckNumber <= 0) missing = true;
            // A rotina do próprio jogo (a mesma do "Ver estoque"): numera em ordem.
            if (missing) deck.RefreshDeckNumber();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] números do deck: {e.Message}"); }
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
                RefreshMiniDeck();
                Executed++;
                Plugin.Logger.LogInfo($"[BazaarHelp] #{helper} pôs {take.ItemId}x{take.Stack} no balcão {shelf} espaço {slot}");
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] executar pedido: {e.Message}"); }
    }

    // ---------- estado dos espaços (avisos de vender/pôr/trocar) ----------

    /// <summary>Recebido do dono: estado de cada espaço (balcão×4 + espaço), valores de BazaarManager.ShopShelfAccessState; 255 = sem espaço.</summary>
    public static byte[] OwnerStates = Array.Empty<byte>();

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
        try
        {
            for (var sh = 0; sh < shelves.Length; sh++)
            {
                if (shelves[sh] == null) continue;
                var its = shelves[sh].GetShopItems();
                for (var k = 0; k < its.Length && k < 4; k++)
                {
                    bm.UpdateCurrentShopShelf(its[k].GetSalePointObjId());
                    var st = bm.GetShopShelfAccessState(k);
                    // "Escolhendo item para este espaço" é do vendedor que abriu o mini deck, não do espaço.
                    if (st == BazaarManager.ShopShelfAccessState.SelectPut) st = BazaarManager.ShopShelfAccessState.Put;
                    res[sh * 4 + k] = (byte)st;
                }
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] estados dos espaços: {e.Message}"); }
        finally
        {
            // Volta ao balcão do espaço mais perto do dono (o jogo recalcula isso todo quadro pela posição dele).
            try
            {
                var pl = GameController.Instance.GetPlayer().transform.position;
                int best = 0; var bd = float.MaxValue;
                for (var sh = 0; sh < shelves.Length; sh++)
                {
                    if (shelves[sh] == null) continue;
                    foreach (var it in shelves[sh].GetShopItems())
                    {
                        var h = it.GetHelpTarget().position;
                        var d = (new Vector2(h.x, h.z) - new Vector2(pl.x, pl.z)).sqrMagnitude;
                        if (d < bd) { bd = d; best = it.GetSalePointObjId(); }
                    }
                }
                if (best != 0) bm.UpdateCurrentShopShelf(best);
            }
            catch { }
        }
        return res;
    }

    private static int OwnerState(int idx)
    {
        var bm = Bm;
        var shelf = bm.GetCurrentShelfIndex();
        if (shelf < 0 || idx < 0) return 255;
        var g = shelf * 4 + idx;
        return g < OwnerStates.Length ? OwnerStates[g] : 255;
    }

    public static object Status() => new { active = Active, suppress = SuppressWrites, target = $"{_targetShelf}:{_targetSlot}", requests = Requests, executed = Executed };

    // ---------- patches: instalados SÓ enquanto este jogo é ajudante ----------
    // Interceptar esses métodos muda o jogo mesmo quando a interceptação "deixa passar": se o método dá erro por
    // dentro (IsBuyStandby dá, em CustomPartsCompositeLevel), o erro é engolido e o método devolve o padrão — o aviso
    // de vender sumia até no jogo do dono. Por isso eles só existem no jogo do ajudante, e só enquanto ele ajuda.

    private static Harmony _harmony;
    private static bool _patched;

    private static void InstallPatches()
    {
        if (_patched) return;
        try
        {
            _harmony ??= new Harmony(Plugin.Guid + ".bazaarhelp");
            var bmT = typeof(BazaarManager);
            void P(System.Reflection.MethodBase target, string prefix) =>
                _harmony.Patch(target, prefix: new HarmonyMethod(typeof(BazaarHelpSync).GetMethod(prefix, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)));
            P(AccessTools.Method(bmT, nameof(BazaarManager.ActionAccessShopShelf)), nameof(AccessPrefix));
            P(AccessTools.Method(bmT, nameof(BazaarManager.SelectExhibit)), nameof(SelectPrefix));
            P(AccessTools.Method(bmT, nameof(BazaarManager.Sell)), nameof(SellPrefix));
            P(AccessTools.Method(bmT, nameof(BazaarManager.GetShopShelfAccessState)), nameof(AccessStatePrefix));
            P(AccessTools.Method(bmT, nameof(BazaarManager.IsBuyStandby)), nameof(StandbyPrefix));
            P(AccessTools.Method(typeof(UIBazaarMainPage), nameof(UIBazaarMainPage.OpenMiniDeck)), nameof(MiniDeckPrefix));
            _patched = true;
            Plugin.Logger.LogInfo("[BazaarHelp] interceptações do ajudante instaladas");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] instalar interceptações: {e.Message}"); }
    }

    private static void RemovePatches()
    {
        if (!_patched) return;
        try { _harmony.UnpatchSelf(); Plugin.Logger.LogInfo("[BazaarHelp] interceptações do ajudante retiradas"); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarHelp] tirar interceptações: {e.Message}"); }
        _patched = false;
    }

    /// <summary>Enter num item: se há item no espaço, vira pedido "atender" ao dono; espaço vazio abre o mini deck (repor).</summary>
    private static bool AccessPrefix(int idx)
    {
        if (!Active) return true;
        var shelf = Bm.GetCurrentShelfIndex();
        _targetShelf = shelf;
        _targetSlot = idx;
        if (!LocalSlotHasItem(shelf, idx)) return true;
        SendRequest(KindServe, shelf, idx);
        return false;
    }

    /// <summary>Tab: guarda o espaço onde o ajudante está (alvo da escolha no mini deck).</summary>
    private static void MiniDeckPrefix()
    {
        if (!Active || Bm == null) return;
        var (shelf, slot) = NearestSlot(Bm);
        _targetShelf = shelf;
        _targetSlot = slot;
    }

    /// <summary>Escolha no mini deck: vira pedido "pôr item" (o idx conta só as entradas não vazias do deck).</summary>
    private static bool SelectPrefix(int idx)
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

    private static UIBazaarMainPage _page;

    private static bool MiniDeckOpen()
    {
        if (_page == null) _page = UnityEngine.Object.FindObjectOfType<UIBazaarMainPage>();
        try { return _page != null && _page.IsMiniDeck(); } catch { _page = null; return false; }
    }

    /// <summary>
    /// O estado do espaço (o que faz o aviso "vender" aparecer) é o do dono. Com o mini deck DESTE jogo aberto, espaço
    /// livre vira "escolhendo item" (SelectPut), como no jogo do dono — é isso que libera o "Enter Colocar".
    /// </summary>
    private static bool AccessStatePrefix(int idx, ref BazaarManager.ShopShelfAccessState __result)
    {
        if (!Active) return true;
        var st = OwnerState(idx);
        if (st == 255) { __result = BazaarManager.ShopShelfAccessState.None; return false; }
        var state = (BazaarManager.ShopShelfAccessState)st;
        if (state == BazaarManager.ShopShelfAccessState.Put && MiniDeckOpen()) state = BazaarManager.ShopShelfAccessState.SelectPut;
        __result = state;
        return false;
    }

    /// <summary>"Cliente esperando neste espaço" vem do dono (estado Vender). Nunca chama o original (dá erro por dentro).</summary>
    private static bool StandbyPrefix(int idx, ref bool __result)
    {
        if (!Active) return true;
        __result = OwnerState(idx) == (int)BazaarManager.ShopShelfAccessState.Sell;
        return false;
    }

    /// <summary>Segurança: o jogo do ajudante nunca vende (as vendas são do dono).</summary>
    private static bool SellPrefix() => !Active;
}
