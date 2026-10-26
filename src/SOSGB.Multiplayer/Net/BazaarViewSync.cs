// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d55.
using System;
using System.Collections.Generic;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Quem está no Bazaar vê os MESMOS clientes e a mesma barraca de quem está vendendo (D55, pedido do usuário:
/// "não dá pros dois verem os mesmos clientes?").
///
///  - DONO = quem está com a loja aberta (BazaarManager.IsInShop; a trava da D51 garante um por vez). 10×/s ele manda
///    cada cliente (NPCBazaarVisitorAgent): número (VisitorMaster.Id), posição, rotação, estado do Animator e balão.
///    As compras só acontecem no jogo dele (o dinheiro já é compartilhado, D36).
///  - Os OUTROS, no mesmo mapa: o elenco de clientes do Bazaar é o mesmo nos dois jogos (testado: 300–326 sempre;
///    os clientes já passeiam pelo mapa com o Bazaar aberto, estado WanderField). Cada um "assume" o cliente de mesmo
///    número no próprio jogo: desliga a IA (SetEnableUpdate, NavMeshAgent, o script, Rigidbody cinemático, sem root
///    motion) e o move como cópia. Clientes do próprio jogo que o dono não tem ficam escondidos. Quando a loja fecha
///    (ou as mensagens param), tudo é devolvido e a IA volta.
///  - BARRACA: no jogo de quem assiste ela é aberta (ChangeTent + peças da decoração; sem isso aparece fechada) e
///    mostra os itens que o dono manda de cada espaço da prateleira (ShopItem.UpdateExhibitUniqueData / ClearContent).
///    Só visual: ajudar a repor é a próxima etapa.
/// </summary>
public class BazaarViewSync : MonoBehaviour
{
    public BazaarViewSync(IntPtr ptr) : base(ptr) { }

    private const float SendInterval = 0.1f;
    private const float ShelfInterval = 1f;
    private const float ShelfResend = 5f;
    private const float StaleSeconds = 1.5f;
    private const float PopRetry = 3f;

    private struct Visitor
    {
        public uint Id;
        public Vector3 Pos;
        public float RotY;
        public int Hash;
        public float Norm;
        public float Speed;
        public byte Sign;
        // Balão de desejo (item que o cliente quer e quantos), o UIEmoteSign do cliente. 0 = sem balão.
        public uint WishItem;
        public int WishBuy, WishBuff, WishCounter;
    }

    private struct Slot
    {
        public uint ItemId;
        public int Stack, Quality, Freshness;
    }

    private sealed class Puppet
    {
        public NPCBazaarVisitorAgent Agent;
        public Animator Anim;
        public Rigidbody Body;
        public bool BodyWasKinematic, RootMotion;
        public Vector3 Target;
        public float RotY;
        public int Hash;
        public byte Sign;
        public uint Wish;
        public int WishBuy;
        public bool Seen;
    }

    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly Dictionary<uint, Puppet> Puppets = new();
    private static readonly List<NPCBazaarVisitorAgent> Hidden = new();
    private static readonly Dictionary<IntPtr, Puppet> Frozen = new();
    private static readonly Dictionary<uint, float> PopTried = new();
    private static readonly Dictionary<IntPtr, Animator> AnimCache = new();

    // recebido do dono
    private static List<Visitor> _visitors;
    private static bool _visitorsFresh;
    private static byte _owner = NetSession.UnknownId;
    private static uint _ownerField;
    private static float _lastVisitorsAt = -100;
    private static Slot[] _shelf;          // os 12 espaços do dono (balcão×4 + espaço)
    private static string _esmJson;        // os mesmos, no formato do jogo (StorageManager.SaveData), para o ajudante
    private static string _esmApplied = "";
    private static bool _shelfFresh;
    private static string _shelfShown = "";
    private static bool _active;
    private static bool _tentOpen;
    private static uint _partsField;

    // dono
    private static bool _wasOwner;
    private float _nextSend, _nextShelfCheck, _nextShelfResend;
    private static string _shelfSent = "";

    private static bool _subscribed;
    public static int Sent, Received;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => { ReleaseAll(); _partsField = 0; };
    }

    private static BazaarManager Bm => BazaarManager.IsInstance ? BazaarManager.Instance : null;

    private void Update()
    {
        try
        {
            var bm = Bm;
            if (NetSession.Current == NetSession.Mode.Offline || bm == null || !FieldManager.IsInstance)
            {
                if (_active) ReleaseAll();
                _wasOwner = false;
                return;
            }
            // Quem abriu a loja como ajudante (D56) não é dono: assiste e manda pedidos. A trava da barraca também conta:
            // no 1º quadro da loja do ajudante o modo ajudante ainda não ligou (ele mandaria os próprios clientes).
            var holder = StorageSync.BazaarHolder;
            var owner = bm.IsInShop && !BazaarHelpSync.Active && (holder == StorageSync.NoHolder || holder == NetSession.LocalId);
            if (owner) OwnerTick(bm);
            else if (_wasOwner) SendClosed();
            _wasOwner = owner;
            ViewerTick(bm, owner);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] {e.Message}"); }
    }

    // ---------- dono: manda clientes e prateleira ----------

    private static Animator AnimOf(NPCBazaarVisitorAgent a)
    {
        if (AnimCache.TryGetValue(a.Pointer, out var an) && an != null) return an;
        an = a.GetComponentInChildren<Animator>();
        AnimCache[a.Pointer] = an;
        return an;
    }

    private void OwnerTick(BazaarManager bm)
    {
        try { _lastSales = bm.ThisOnceSalePrice; } catch { }
        if (Time.unscaledTime >= _nextSend)
        {
            _nextSend = Time.unscaledTime + SendInterval;
            var list = new List<Visitor>();
            if (bm.NpcAgentList != null)
                foreach (var a in bm.NpcAgentList)
                {
                    if (a == null || a.VisitorMaster == null || !a.gameObject.activeInHierarchy) continue;
                    var an = AnimOf(a);
                    var v = new Visitor
                    {
                        Id = a.VisitorMaster.Id,
                        Pos = a.transform.position,
                        RotY = a.transform.eulerAngles.y,
                        Sign = (byte)a.currentSignType,
                    };
                    try
                    {
                        // IsShowing do balão diz "não" mesmo com ele na tela (testado); vale o estado do cliente:
                        // olhando (Watch) ou comprando (Buy) com item definido.
                        var em = a.EmoteSign;
                        var stName = a.CurrentState.ToString();
                        if (em != null && em.itemId != 0 && (stName == "Watch" || stName == "Buy"))
                        {
                            v.WishItem = em.itemId;
                            var c = em.itemCount;
                            v.WishBuy = c?.buyNum ?? 0;
                            v.WishBuff = c?.buffNum ?? 0;
                            v.WishCounter = em.counterIndex;
                        }
                    }
                    catch { /* balão é só enfeite */ }
                    if (an != null)
                    {
                        var st = an.GetCurrentAnimatorStateInfo(0);
                        v.Hash = st.fullPathHash;
                        v.Norm = st.normalizedTime;
                        v.Speed = an.GetFloat(SpeedParam);
                    }
                    list.Add(v);
                    if (list.Count >= 36) break; // cabe num pacote sem garantia (~1000 bytes)
                }
            var me = NetSession.LocalId;
            var field = FieldManager.Instance.CurrentFieldId;
            // Estado de cada espaço dos balcões (vender, trocar, pôr...): o jogo do ajudante mostra os mesmos avisos (D56).
            var states = BazaarHelpSync.ComputeAccessStates(bm);
            NetSession.SendGame(MsgType.BazaarVisitors, w =>
            {
                w.Put(me); w.Put(field); w.Put(true); w.Put((byte)list.Count);
                // Compacto (o envio sem garantia tem limite de ~1000 bytes por pacote e são ~27 clientes):
                // posição em centímetros (short), giro/tempo/velocidade em 1 byte, balão só quando existe.
                foreach (var v in list)
                {
                    w.Put((ushort)v.Id);
                    w.Put(Cm(v.Pos.x)); w.Put(Cm(v.Pos.y)); w.Put(Cm(v.Pos.z));
                    w.Put((byte)(Mathf.Repeat(v.RotY, 360f) / 360f * 255f));
                    w.Put(v.Hash);
                    w.Put((byte)(Mathf.Repeat(v.Norm, 1f) * 255f));
                    w.Put((byte)Mathf.Clamp(v.Speed * 50f, 0, 255));
                    w.Put(v.Sign);
                    w.Put(v.WishItem != 0);
                    if (v.WishItem != 0) { w.Put(v.WishItem); w.Put((byte)Mathf.Clamp(v.WishBuy, 0, 255)); w.Put((byte)Mathf.Clamp(v.WishBuff, 0, 255)); w.Put((byte)Mathf.Clamp(v.WishCounter, 0, 255)); }
                }
                w.Put((byte)states.Length);
                foreach (var st in states) w.Put(st);
            }, DeliveryMethod.Sequenced);
            Sent++;
        }

        if (Time.unscaledTime < _nextShelfCheck) return;
        _nextShelfCheck = Time.unscaledTime + ShelfInterval;
        // Os balcões inteiros (12 espaços = balcão×4 + espaço) com os dados completos dos itens: o ajudante (D56) aplica
        // nos dados do jogo dele; quem só assiste usa para o visual.
        var esm = InventoryManager.Instance?.ExhibitionShelf;
        if (esm == null) return;
        var json = StringExtensions.ToJson(esm.ToSaveData());
        if (json == _shelfSent && Time.unscaledTime < _nextShelfResend) return;
        _shelfSent = json;
        _nextShelfResend = Time.unscaledTime + ShelfResend;
        var me2 = NetSession.LocalId;
        NetSession.SendGame(MsgType.BazaarShelves, w => { w.Put(me2); w.Put(json); }, DeliveryMethod.ReliableOrdered);
    }

    private static short Cm(float metros) => (short)Mathf.Clamp(Mathf.RoundToInt(metros * 100f), short.MinValue, short.MaxValue);

    private static int _lastSales;

    private static void SendClosed()
    {
        var me = NetSession.LocalId;
        var field = FieldManager.Instance.CurrentFieldId;
        var sales = _lastSales;
        // Leva o total vendido no turno: o jogo do ajudante mostra os resultados DELE (0 G); o aviso conta o de verdade.
        NetSession.SendGame(MsgType.BazaarVisitors, w => { w.Put(me); w.Put(field); w.Put(false); w.Put((byte)0); w.Put(sales); }, DeliveryMethod.ReliableOrdered);
        _lastSales = 0;
        _shelfSent = "";
        Plugin.Logger.LogInfo("[BazaarView] loja fechada: os outros devolvem os clientes");
    }

    // ---------- recepção ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type == MsgType.BazaarVisitors)
        {
            var owner = r.GetByte();
            if (owner == NetSession.LocalId) return;
            if (NetSession.Current == NetSession.Mode.Host && owner != from) return;
            var field = r.GetUInt();
            var open = r.GetBool();
            var n = r.GetByte();
            var list = new List<Visitor>(n);
            for (var i = 0; i < n; i++)
            {
                var v = new Visitor
                {
                    Id = r.GetUShort(),
                    Pos = new Vector3(r.GetShort() / 100f, r.GetShort() / 100f, r.GetShort() / 100f),
                    RotY = r.GetByte() / 255f * 360f,
                    Hash = r.GetInt(),
                    Norm = r.GetByte() / 255f,
                    Speed = r.GetByte() / 50f,
                    Sign = r.GetByte(),
                };
                if (r.GetBool()) { v.WishItem = r.GetUInt(); v.WishBuy = r.GetByte(); v.WishBuff = r.GetByte(); v.WishCounter = r.GetByte(); }
                list.Add(v);
            }
            if (open && r.AvailableBytes >= 1)
            {
                var ns = r.GetByte();
                var states = new byte[ns];
                for (var i = 0; i < ns; i++) states[i] = r.GetByte();
                BazaarHelpSync.OwnerStates = states;
            }
            _owner = owner;
            _ownerField = field;
            _visitors = open ? list : null;
            _visitorsFresh = true;
            _lastVisitorsAt = open ? Time.unscaledTime : -100;
            if (!open)
            {
                _shelf = null; _esmJson = null; _shelfFresh = true;
                var sales = r.AvailableBytes >= 4 ? r.GetInt() : -1;
                BazaarHelpSync.OwnerClosed(owner, sales);
            }
            Received++;
        }
        else if (type == MsgType.BazaarShelves)
        {
            var owner = r.GetByte();
            if (owner == NetSession.LocalId) return;
            if (NetSession.Current == NetSession.Mode.Host && owner != from) return;
            _esmJson = r.GetString();
            _shelf = ParseShelf(_esmJson);
            _shelfFresh = true;
        }
    }

    // ---------- quem não está vendendo: cópias ----------

    private static Slot[] ParseShelf(string json)
    {
        var list = new List<Slot>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("itemDatas", out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != System.Text.Json.JsonValueKind.Object) { list.Add(new Slot()); continue; }
                    list.Add(new Slot { ItemId = U(e, "ItemId"), Stack = N(e, "Stack"), Quality = N(e, "QualityValue"), Freshness = N(e, "FreshnessValue") });
                }
        }
        catch (Exception ex) { Plugin.Logger.LogWarning($"[BazaarView] balcões: {ex.Message}"); }
        return list.ToArray();
    }

    private static uint U(System.Text.Json.JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetUInt32() : 0;

    private static int N(System.Text.Json.JsonElement e, string n) =>
        e.TryGetProperty(n, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetInt32() : 0;

    private void ViewerTick(BazaarManager bm, bool owner)
    {
        var want = !owner && _visitors != null && Time.unscaledTime - _lastVisitorsAt < StaleSeconds
                   && FieldManager.Instance.CurrentFieldId == _ownerField;
        if (!want)
        {
            if (_active) ReleaseAll();
            return;
        }
        if (!_active)
        {
            Plugin.Logger.LogInfo($"[BazaarView] mostrando os clientes e a barraca de #{_owner}{(BazaarHelpSync.Active ? " (ajudante)" : "")}");
            if (!bm.IsInShop) OpenTent();
        }
        _active = true;

        if (_visitorsFresh)
        {
            _visitorsFresh = false;
            ApplyVisitors(bm);
        }
        // As prateleiras só existem depois que as peças da barraca carregam (assíncrono): reaplica quando surgirem.
        if (_shelfFresh || (_shelf != null && _shelfShown.Length == 0 && Time.frameCount % 30 == 0))
        {
            _shelfFresh = false;
            ApplyShelf(bm);
        }

        // Interpolação entre os pacotes (10/s), como nos avatares dos jogadores.
        var k = Time.deltaTime * 12f;
        foreach (var p in Puppets.Values)
        {
            if (p.Agent == null) continue;
            var tr = p.Agent.transform;
            // Longe da posição do dono (o jogo daqui reposicionou o cliente): pula direto, sem atravessar a barraca.
            if ((tr.position - p.Target).sqrMagnitude > 2.25f) tr.position = p.Target;
            else tr.position = Vector3.Lerp(tr.position, p.Target, k);
            tr.rotation = Quaternion.Slerp(tr.rotation, Quaternion.Euler(0, p.RotY, 0), k);
        }
    }

    /// <summary>
    /// Barraca ABERTA no jogo de quem assiste: sem isto ela aparece fechada (BazaarMyShopClose: barris e caixotes) e os
    /// clientes ficam em volta de um balcão invisível. ChangeTent troca o visual; SetBazaarPartsModel carrega as peças
    /// (balcões = as prateleiras BazaarShopShelf, decoração) com a decoração compartilhada (D46). Testado na D55.
    /// </summary>
    private static void OpenTent()
    {
        try
        {
            var shop = UnityEngine.Object.FindObjectOfType<BazaarMyShop>();
            if (shop == null || shop.ShopShelfRoot == null) return;
            shop.ShopShelfRoot.ChangeTent(true, false);
            var field = FieldManager.Instance.CurrentFieldId;
            if (_partsField != field)
            {
                shop.SetBazaarPartsModel((Il2CppSystem.Action)null);
                _partsField = field;
            }
            _tentOpen = true;
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] abrir a barraca: {e.Message}"); }
    }

    private static void CloseTent(BazaarManager bm)
    {
        if (!_tentOpen) return;
        _tentOpen = false;
        // Se este jogador abriu a própria loja, a barraca é do jogo dele agora: não mexe.
        if (bm != null && bm.IsInShop) return;
        try
        {
            var shop = UnityEngine.Object.FindObjectOfType<BazaarMyShop>();
            shop?.ShopShelfRoot?.ChangeTent(false, false);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] fechar a barraca: {e.Message}"); }
    }

    private static NPCBazaarVisitorAgent FindAgent(BazaarManager bm, uint id)
    {
        if (bm.NpcAgentList == null) return null;
        foreach (var a in bm.NpcAgentList)
            if (a != null && a.VisitorMaster != null && a.VisitorMaster.Id == id) return a;
        return null;
    }

    private static void ApplyVisitors(BazaarManager bm)
    {
        foreach (var p in Puppets.Values) p.Seen = false;
        foreach (var v in _visitors)
        {
            if (!Puppets.TryGetValue(v.Id, out var p) || p.Agent == null)
            {
                var a = FindAgent(bm, v.Id);
                if (a == null)
                {
                    // Cliente que este jogo ainda não criou: pede ao próprio Bazaar (aparece na lista no frame seguinte).
                    if (!PopTried.TryGetValue(v.Id, out var t) || Time.unscaledTime - t > PopRetry)
                    {
                        PopTried[v.Id] = Time.unscaledTime;
                        try { bm.PopVisitor(v.Id, (Il2CppSystem.Action<NPCBazaarVisitorAgent>)null); }
                        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] criar cliente {v.Id}: {e.Message}"); }
                    }
                    continue;
                }
                if (Frozen.TryGetValue(a.Pointer, out var fp))
                {
                    // Estava congelado como "extra" e agora o dono tem esse cliente: vira cópia sem capturar de novo
                    // (capturar de novo gravaria o estado congelado como se fosse o original).
                    Frozen.Remove(a.Pointer);
                    for (var i = Hidden.Count - 1; i >= 0; i--)
                        if (Hidden[i] == null || Hidden[i].Pointer == a.Pointer) Hidden.RemoveAt(i);
                    a.SetActiveGraphic(true);
                    fp.Hash = 0;
                    fp.Sign = 0;
                    p = fp;
                }
                else p = Capture(a);
                Puppets[v.Id] = p;
                a.transform.position = v.Pos;
            }
            p.Seen = true;
            p.Target = v.Pos;
            p.RotY = v.RotY;
            if (p.Anim != null)
            {
                if (v.Hash != 0 && v.Hash != p.Hash) { p.Anim.Play(v.Hash, 0, v.Norm % 1f); p.Hash = v.Hash; }
                p.Anim.SetFloat(SpeedParam, v.Speed);
            }
            if (v.Sign != p.Sign)
            {
                try
                {
                    if (v.Sign == 0) p.Agent.HideSign();
                    else p.Agent.ShowSign((SignType)v.Sign);
                }
                catch { /* balão é só enfeite */ }
                p.Sign = v.Sign;
            }
            // Balão de desejo (o item que o cliente quer e quantos), como no jogo do dono.
            if (v.WishItem != p.Wish || v.WishBuy != p.WishBuy)
            {
                try
                {
                    var em = p.Agent.EmoteSign;
                    if (em != null)
                    {
                        if (v.WishItem == 0) em.Hide(null);
                        else em.Show(new BuyNumBuffParam(v.WishBuy, v.WishBuff), v.WishItem, v.WishCounter, 0);
                        p.Wish = v.WishItem;
                        p.WishBuy = v.WishBuy;
                    }
                }
                catch { /* balão é só enfeite */ }
            }
        }

        var gone = new List<uint>();
        foreach (var kv in Puppets) if (!kv.Value.Seen) gone.Add(kv.Key);
        foreach (var id in gone)
        {
            var gp = Puppets[id];
            Puppets.Remove(id);
            // No modo ajudante o cliente que saiu da loja do dono não pode voltar a ter IA aqui: fica congelado e escondido.
            if (BazaarHelpSync.Active && gp.Agent != null) { Frozen[gp.Agent.Pointer] = gp; gp.Agent.SetActiveGraphic(false); }
            else Release(gp);
        }

        // Clientes deste jogo que o dono não tem: escondidos enquanto as cópias estão na tela.
        if (bm.NpcAgentList != null)
            foreach (var a in bm.NpcAgentList)
            {
                if (a == null || a.VisitorMaster == null) continue;
                if (Puppets.TryGetValue(a.VisitorMaster.Id, out var p) && p.Agent != null && p.Agent.Pointer == a.Pointer) continue;
                // Ajudante em modo loja: os clientes do próprio jogo não podem andar até o balcão nem reservar espaços.
                if (BazaarHelpSync.Active && !Frozen.ContainsKey(a.Pointer)) Frozen[a.Pointer] = Capture(a);
                // O jogo daqui às vezes volta a mostrar o cliente (aparecer, reposicionar): esconde de novo a cada pacote.
                a.SetActiveGraphic(false);
                if (!Hidden.Exists(h => h != null && h.Pointer == a.Pointer)) Hidden.Add(a);
            }
    }

    private static Puppet Capture(NPCBazaarVisitorAgent a)
    {
        var p = new Puppet { Agent = a, Anim = AnimOf(a), Body = a.GetComponent<Rigidbody>() };
        // Se ele estava escondido (era "extra"), volta a aparecer como cópia.
        for (var i = Hidden.Count - 1; i >= 0; i--)
            if (Hidden[i] == null || Hidden[i].Pointer == a.Pointer) Hidden.RemoveAt(i);
        a.SetActiveGraphic(true);
        a.SetEnableUpdate(false);
        if (a.Agent != null) a.Agent.enabled = false;
        a.enabled = false;
        if (p.Body != null)
        {
            p.BodyWasKinematic = p.Body.isKinematic;
            if (!p.Body.isKinematic) p.Body.velocity = Vector3.zero;
            p.Body.isKinematic = true;
        }
        if (p.Anim != null) { p.RootMotion = p.Anim.applyRootMotion; p.Anim.applyRootMotion = false; }
        return p;
    }

    private static void Release(Puppet p)
    {
        var a = p.Agent;
        if (a == null) return;
        try
        {
            if (p.Sign != 0) a.HideSign();
            if (p.Wish != 0) a.EmoteSign?.Hide(null);
            if (p.Anim != null) p.Anim.applyRootMotion = p.RootMotion;
            if (p.Body != null) p.Body.isKinematic = p.BodyWasKinematic;
            a.enabled = true;
            if (a.Agent != null) a.Agent.enabled = true;
            a.SetEnableUpdate(true);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] devolver cliente: {e.Message}"); }
    }

    private static void ApplyShelf(BazaarManager bm)
    {
        var shelves = bm.ShopShelfs;
        if (BazaarHelpSync.Active)
        {
            // Ajudante: os balcões do jogo dele passam a ter os mesmos itens do dono (dados + visual, ReExhibit), para os
            // botões do jogo (Enter, Tab) enxergarem o que está em cada espaço.
            if (_esmJson == null || _esmJson == _esmApplied) return;
            try
            {
                var esm = InventoryManager.Instance.ExhibitionShelf;
                esm.FromSaveData(StringExtensions.JsonToObject<StorageManager.SaveData>(_esmJson));
                bm.ReExhibit();
                _esmApplied = _esmJson;
                _shelfShown = "ajudante";
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] balcões do ajudante: {e.Message}"); }
            return;
        }
        if (_shelf == null || shelves == null)
        {
            ClearShownShelf(bm);
            return;
        }
        var shown = "";
        for (var sh = 0; sh < shelves.Length; sh++)
        {
            if (shelves[sh] == null) continue;
            var n = shelves[sh].GetShopItemLength();
            for (var i = 0; i < n; i++)
            {
                var g = sh * 4 + i;
                var s = g < _shelf.Length ? _shelf[g] : new Slot();
                var si = shelves[sh].GetShopItem(i);
                if (si == null) continue;
                try
                {
                    if (s.ItemId == 0 || s.Stack <= 0) si.ClearContent();
                    else si.UpdateExhibitUniqueData(new BokuMono.Data.ItemData { ItemId = s.ItemId, Stack = s.Stack, QualityValue = s.Quality, FreshnessValue = s.Freshness });
                }
                catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] balcão {sh} espaço {i}: {e.Message}"); }
                shown += $"{s.ItemId}x{s.Stack},";
            }
        }
        _shelfShown = shown;
    }

    private static void ClearShownShelf(BazaarManager bm)
    {
        if (_shelfShown.Length == 0) return;
        var wasHelper = _shelfShown == "ajudante";
        _shelfShown = "";
        _esmApplied = "";
        if (wasHelper) return; // os balcões do ajudante são limpos pelo BazaarHelpSync
        var shelves = bm?.ShopShelfs;
        if (shelves == null) return;
        foreach (var sh in shelves)
        {
            if (sh == null) continue;
            for (var i = 0; i < sh.GetShopItemLength(); i++)
                try { sh.GetShopItem(i)?.ClearContent(); } catch { }
        }
    }

    private static void ReleaseAll()
    {
        foreach (var p in Puppets.Values) Release(p);
        Puppets.Clear();
        foreach (var p in Frozen.Values) Release(p);
        Frozen.Clear();
        foreach (var a in Hidden)
            if (a != null) try { a.SetActiveGraphic(true); } catch { }
        Hidden.Clear();
        PopTried.Clear();
        try { ClearShownShelf(Bm); } catch { }
        CloseTent(Bm);
        if (_active) Plugin.Logger.LogInfo("[BazaarView] cópias devolvidas (loja fechou ou saiu do mapa)");
        _active = false;
    }

    public static object Status() => new
    {
        active = _active,
        owner = _owner,
        ownerField = _ownerField,
        visitors = _visitors?.Count ?? 0,
        lastSeconds = Math.Round(Time.unscaledTime - _lastVisitorsAt, 1),
        puppets = Puppets.Count,
        hidden = Hidden.Count,
        shelf = _shelfShown,
        frozen = Frozen.Count,
        sent = Sent,
        received = Received,
    };
}
