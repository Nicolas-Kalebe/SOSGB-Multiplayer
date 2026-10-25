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
        public bool Seen;
    }

    private static readonly int SpeedParam = Animator.StringToHash("Speed");
    private static readonly Dictionary<uint, Puppet> Puppets = new();
    private static readonly List<NPCBazaarVisitorAgent> Hidden = new();
    private static readonly Dictionary<uint, float> PopTried = new();
    private static readonly Dictionary<IntPtr, Animator> AnimCache = new();

    // recebido do dono
    private static List<Visitor> _visitors;
    private static bool _visitorsFresh;
    private static byte _owner = NetSession.UnknownId;
    private static uint _ownerField;
    private static float _lastVisitorsAt = -100;
    private static Slot[] _shelf;
    private static int _shelfIndex = -1;
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
            var owner = bm.IsInShop;
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
                    if (an != null)
                    {
                        var st = an.GetCurrentAnimatorStateInfo(0);
                        v.Hash = st.fullPathHash;
                        v.Norm = st.normalizedTime;
                        v.Speed = an.GetFloat(SpeedParam);
                    }
                    list.Add(v);
                    if (list.Count >= 64) break;
                }
            var me = NetSession.LocalId;
            var field = FieldManager.Instance.CurrentFieldId;
            NetSession.SendGame(MsgType.BazaarVisitors, w =>
            {
                w.Put(me); w.Put(field); w.Put(true); w.Put((byte)list.Count);
                foreach (var v in list)
                {
                    w.Put(v.Id); w.Put(v.Pos.x); w.Put(v.Pos.y); w.Put(v.Pos.z); w.Put(v.RotY);
                    w.Put(v.Hash); w.Put(v.Norm); w.Put(v.Speed); w.Put(v.Sign);
                }
            }, DeliveryMethod.Sequenced);
            Sent++;
        }

        if (Time.unscaledTime < _nextShelfCheck) return;
        _nextShelfCheck = Time.unscaledTime + ShelfInterval;
        var idx = bm.GetCurrentShelfIndex();
        var shelves = bm.ShopShelfs;
        if (shelves == null || idx < 0 || idx >= shelves.Length || shelves[idx] == null) return;
        var shelf = shelves[idx];
        var n = shelf.GetShopItemLength();
        var slots = new Slot[n];
        var sig = idx + ":";
        for (var i = 0; i < n; i++)
        {
            var it = shelf.GetShopItem(i)?.ItemInvData();
            if (it != null && it.ItemId != 0 && it.Stack > 0)
                slots[i] = new Slot { ItemId = it.ItemId, Stack = it.Stack, Quality = it.QualityValue, Freshness = it.FreshnessValue };
            sig += $"{slots[i].ItemId}x{slots[i].Stack}q{slots[i].Quality},";
        }
        if (sig == _shelfSent && Time.unscaledTime < _nextShelfResend) return;
        _shelfSent = sig;
        _nextShelfResend = Time.unscaledTime + ShelfResend;
        var me2 = NetSession.LocalId;
        NetSession.SendGame(MsgType.BazaarShelves, w =>
        {
            w.Put(me2); w.Put(idx); w.Put((byte)slots.Length);
            foreach (var s in slots) { w.Put(s.ItemId); w.Put(s.Stack); w.Put(s.Quality); w.Put(s.Freshness); }
        }, DeliveryMethod.ReliableOrdered);
    }

    private static void SendClosed()
    {
        var me = NetSession.LocalId;
        var field = FieldManager.Instance.CurrentFieldId;
        NetSession.SendGame(MsgType.BazaarVisitors, w => { w.Put(me); w.Put(field); w.Put(false); w.Put((byte)0); }, DeliveryMethod.ReliableOrdered);
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
                list.Add(new Visitor
                {
                    Id = r.GetUInt(),
                    Pos = new Vector3(r.GetFloat(), r.GetFloat(), r.GetFloat()),
                    RotY = r.GetFloat(),
                    Hash = r.GetInt(),
                    Norm = r.GetFloat(),
                    Speed = r.GetFloat(),
                    Sign = r.GetByte(),
                });
            _owner = owner;
            _ownerField = field;
            _visitors = open ? list : null;
            _visitorsFresh = true;
            _lastVisitorsAt = open ? Time.unscaledTime : -100;
            if (!open) { _shelf = null; _shelfFresh = true; }
            Received++;
        }
        else if (type == MsgType.BazaarShelves)
        {
            var owner = r.GetByte();
            if (owner == NetSession.LocalId) return;
            if (NetSession.Current == NetSession.Mode.Host && owner != from) return;
            _shelfIndex = r.GetInt();
            var n = r.GetByte();
            var slots = new Slot[n];
            for (var i = 0; i < n; i++)
                slots[i] = new Slot { ItemId = r.GetUInt(), Stack = r.GetInt(), Quality = r.GetInt(), Freshness = r.GetInt() };
            _shelf = slots;
            _shelfFresh = true;
        }
    }

    // ---------- quem não está vendendo: cópias ----------

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
            Plugin.Logger.LogInfo($"[BazaarView] mostrando os clientes e a barraca de #{_owner}");
            OpenTent();
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
            if ((tr.position - p.Target).sqrMagnitude > 25f) tr.position = p.Target;
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
                p = Capture(a);
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
        }

        var gone = new List<uint>();
        foreach (var kv in Puppets) if (!kv.Value.Seen) gone.Add(kv.Key);
        foreach (var id in gone) { Release(Puppets[id]); Puppets.Remove(id); }

        // Clientes deste jogo que o dono não tem: escondidos enquanto as cópias estão na tela.
        if (bm.NpcAgentList != null)
            foreach (var a in bm.NpcAgentList)
            {
                if (a == null || a.VisitorMaster == null) continue;
                if (Puppets.TryGetValue(a.VisitorMaster.Id, out var p) && p.Agent != null && p.Agent.Pointer == a.Pointer) continue;
                if (Hidden.Exists(h => h != null && h.Pointer == a.Pointer)) continue;
                a.SetActiveGraphic(false);
                Hidden.Add(a);
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
        if (_shelf == null || shelves == null || _shelfIndex < 0 || _shelfIndex >= shelves.Length || shelves[_shelfIndex] == null)
        {
            ClearShownShelf(bm);
            return;
        }
        var shelf = shelves[_shelfIndex];
        var n = Math.Min(_shelf.Length, shelf.GetShopItemLength());
        var shown = _shelfIndex + ":";
        for (var i = 0; i < n; i++)
        {
            var s = _shelf[i];
            var si = shelf.GetShopItem(i);
            if (si == null) continue;
            try
            {
                if (s.ItemId == 0) si.ClearContent();
                else si.UpdateExhibitUniqueData(new BokuMono.Data.ItemData { ItemId = s.ItemId, Stack = s.Stack, QualityValue = s.Quality, FreshnessValue = s.Freshness });
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[BazaarView] prateleira {i}: {e.Message}"); }
            shown += $"{s.ItemId}x{s.Stack},";
        }
        _shelfShown = shown;
    }

    private static void ClearShownShelf(BazaarManager bm)
    {
        if (_shelfShown.Length == 0) return;
        var idx = int.Parse(_shelfShown.Substring(0, _shelfShown.IndexOf(':')));
        _shelfShown = "";
        var shelves = bm?.ShopShelfs;
        if (shelves == null || idx < 0 || idx >= shelves.Length || shelves[idx] == null) return;
        for (var i = 0; i < shelves[idx].GetShopItemLength(); i++)
            try { shelves[idx].GetShopItem(i)?.ClearContent(); } catch { }
    }

    private static void ReleaseAll()
    {
        foreach (var p in Puppets.Values) Release(p);
        Puppets.Clear();
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
        sent = Sent,
        received = Received,
    };
}
