// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d40.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Fazenda sincronizada ao vivo (Fase 5, D40) — mapas RESIDENTES (o jogo os mantém sempre na memória de todos;
/// hoje: o mapa 1, a área externa com a fazenda).
///
///  - Quem está num mapa residente compara, a cada 0,5 s, os objetos dele (terra arada, plantas, árvores,
///    pedras/mato, grama, flores, sprinklers, ovos) com o último estado sincronizado e manda as diferenças
///    (FieldOps: criar/alterar = dados completos; remover = GUID). Convidado → host; host aplica e repassa.
///  - Todos aplicam as operações no cache daquele mapa, ESTEJAM ONDE ESTIVEREM (no mapa atual aparece na hora; no
///    cache residente, aparece quando a pessoa voltar). Por isso não é preciso mandar o mapa inteiro.
///  - Antes de aplicar operações recebidas no mapa atual, as mudanças locais pendentes saem primeiro (para não
///    serem engolidas pela nova base).
///  - Na virada do dia todos recarregam o mundo do host (que processou o dia no cache dele, já com tudo).
/// Conflito (dois mexendo no mesmo objeto no mesmo meio segundo): vale a última operação que o host processou.
/// Itens largados no chão (D70) são a exceção: pegar é "tirar do mapa e pôr na mochila", então dois pegando o mesmo
/// item ao mesmo tempo duplicaria. O host anota cada item pego; quem chega depois recebe ItemTakeBack e devolve o que
/// entrou na mochila dele (o item fica só com quem pegou primeiro).
///
/// Mapas NÃO residentes (celeiro com ovos etc.): as operações valem entre quem está no mesmo mapa; quem não está
/// com ele carregado ignora. Ao SAIR de um mapa não residente em que algo mudou, o jogador manda o mapa inteiro
/// (FieldSnapshot, o FieldSaveData que o jogo acabou de gravar) e os outros instalam no save de mapas deles —
/// inclusive o host, que processa o dia com ele (sem isso, um ovo pego pelo convidado voltaria no dia seguinte).
/// </summary>
public class FarmSync : MonoBehaviour
{
    public FarmSync(IntPtr ptr) : base(ptr) { }

    private const byte OpUpsert = 1, OpRemove = 2;

    private static uint _baselineField;
    private static uint _lastRoot;          // mapa (raiz) em que o jogador estava no último tick
    private static bool _dirtyNonResident;  // mudou algo no mapa não residente atual
    private static Dictionary<string, FieldState.Entry> _baseline;
    private static bool _subscribed;
    private float _nextTick;
    public static int SentOps, ReceivedOps;
    private static readonly HeldMessages Held = new("FarmSync");
    private static readonly HashSet<string> TakenItems = new();                          // host: itens do chão já pegos (D70)
    private static readonly Dictionary<string, (string json, float at, FieldState.Kind kind)> MyPickups = new(); // convidado: itens/ovos que peguei há pouco

    /// <summary>D70/D92: o que vira item na mochila ao sair do mapa: item largado no chão e ovo (A04: dois pegando o mesmo ovo).</summary>
    private static bool Pickable(FieldState.Kind k) => k == FieldState.Kind.Item || k == FieldState.Kind.Egg;
    public static int TakeBacks;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => { _baseline = null; _dirtyNonResident = false; _lastRoot = 0; MyPickups.Clear(); };
        NetSession.OnHosting += () => { _baseline = null; TakenItems.Clear(); };
        NetSession.OnJoined += () => { _baseline = null; MyPickups.Clear(); };
    }

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!FieldManager.IsInstance || FieldManager.Instance.CurrentFieldCache == null) return false;
        if (FieldManager.Instance.IsChangeFieldUnloading) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    /// <summary>Mapas residentes (FieldManager.ResidentFieldCacheIds, ex.: 1). Só eles são sincronizados por aqui.</summary>
    public static bool IsResident(uint root)
    {
        try
        {
            foreach (var id in FieldManager.ResidentFieldCacheIds) if ((uint)id == root) return true;
        }
        catch { /* sem a lista: só o mapa 1 */ return root == 1; }
        return false;
    }

    private void Update()
    {
        try
        {
            if (!Ready()) return;
            if (Held.Count > 0) Held.Drain(OnMessage); // o que chegou enquanto carregava/trocava de mapa (D63)
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            Flush();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] {e.Message}"); }
    }

    /// <summary>A03: manda agora o que mudou no mapa (antes do aviso "dormi").</summary>
    public static void FlushNow()
    {
        try { if (Ready()) Flush(); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] enviar antes de dormir: {e.Message}"); }
    }

    /// <summary>Compara o mapa atual com a base e manda as diferenças.</summary>
    private static void Flush()
    {
        var root = FieldState.Root(FieldState.CurrentField);
        if (root != _lastRoot)
        {
            // Trocou de mapa: se saiu de um não residente em que mexeu, manda o mapa inteiro (já gravado pelo jogo).
            if (_lastRoot != 0 && _dirtyNonResident && !IsResident(_lastRoot)) SendSnapshot(_lastRoot);
            _dirtyNonResident = false;
            _lastRoot = root;
        }
        var cur = FieldState.CaptureLive();
        if (_baseline == null || _baselineField != root) { _baseline = cur; _baselineField = root; return; }
        var ops = new List<(byte op, FieldState.Entry e, string guid)>();
        foreach (var kv in cur)
            if (!_baseline.TryGetValue(kv.Key, out var old) || old.Json != kv.Value.Json) ops.Add((OpUpsert, kv.Value, kv.Key));
        foreach (var kv in _baseline)
            if (!cur.ContainsKey(kv.Key)) ops.Add((OpRemove, kv.Value, kv.Key));
        _baseline = cur;
        if (ops.Count == 0) return;
        if (!IsResident(root)) _dirtyNonResident = true;
        NotePickups(ops);
        var packed = Pack(root, ops);
        if (NetSession.Current == NetSession.Mode.Host) SendToClients(packed, except: NetSession.LocalId);
        else NetSession.SendGame(MsgType.FieldOps, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
        SentOps += ops.Count;
        Plugin.Logger.LogInfo($"[FarmSync] {ops.Count} mudança(s) no mapa {root} enviada(s) ({packed.Length} bytes)");
        DevBridge.PushEvent($"fazenda:enviado:{ops.Count}");
    }

    /// <summary>Itens do chão que sumiram do meu mapa = eu peguei (D70). O host anota; o convidado guarda para devolver se perder.</summary>
    private static void NotePickups(List<(byte op, FieldState.Entry e, string guid)> ops)
    {
        foreach (var o in ops)
        {
            if (o.op != OpRemove || !Pickable(o.e.Type)) continue;
            if (NetSession.Current == NetSession.Mode.Host) TakenItems.Add(o.guid);
            else MyPickups[o.guid] = (o.e.Json, Time.unscaledTime, o.e.Type);
        }
        if (MyPickups.Count > 50)
            foreach (var g in MyPickups.Where(kv => Time.unscaledTime - kv.Value.at > 60f).Select(kv => kv.Key).ToList()) MyPickups.Remove(g);
        if (TakenItems.Count > 2000) TakenItems.Clear(); // GUIDs são únicos; só não deixa crescer sem fim
    }

    /// <summary>
    /// Host: tira das operações de um convidado os itens do chão que alguém já pegou (ele pegou junto, no mesmo
    /// instante) e manda ele devolver. As minhas mudanças pendentes saem antes, para eu entrar na fila primeiro.
    /// </summary>
    private static List<(byte op, FieldState.Entry e, string guid)> RefuseLatePickups(byte from, uint root, List<(byte op, FieldState.Entry e, string guid)> ops)
    {
        if (!ops.Any(o => o.op == OpRemove && Pickable(o.e.Type))) return ops;
        if (FieldState.Root(FieldState.CurrentField) == root) Flush();
        var late = ops.Where(o => o.op == OpRemove && Pickable(o.e.Type) && !TakenItems.Add(o.guid)).Select(o => o.guid).ToHashSet();
        if (late.Count == 0) return ops;
        foreach (var g in late)
        {
            NetSession.SendTo(from, MsgType.ItemTakeBack, w => { w.Put(root); w.Put(g); }, DeliveryMethod.ReliableOrdered);
            Plugin.Logger.LogInfo($"[FarmSync] item {g} no mapa {root}: #{from} pegou depois de outro jogador; ele devolve");
        }
        TakeBacks += late.Count;
        return ops.Where(o => !(o.op == OpRemove && late.Contains(o.guid))).ToList();
    }

    /// <summary>Convidado: o host disse que outro jogador pegou este item primeiro; tira da mochila o que entrou.</summary>
    private static void OnTakeBack(byte from, NetDataReader r)
    {
        if (from != Protocol.HostPlayerId) return;
        var root = r.GetUInt();
        var guid = r.GetString();
        if (!MyPickups.TryGetValue(guid, out var p)) { Plugin.Logger.LogWarning($"[FarmSync] devolver item {guid}: não sei o que era"); return; }
        MyPickups.Remove(guid);
        BokuMono.Data.ItemData item;
        if (p.kind == FieldState.Kind.Egg)
        {
            // Ovo: vira o item ItemId (Count unidades, qualidade Quality) na mochila de quem pegou.
            var egg = StringExtensions.JsonToObject<FieldEggEntityData>(p.json);
            if (egg == null) return;
            item = new BokuMono.Data.ItemData { ItemId = egg.ItemId, Stack = Math.Max(1, egg.Count), QualityValue = egg.Quality };
        }
        else item = StringExtensions.JsonToObject<FieldItemEntityData>(p.json)?.ItemDataInField;
        if (item == null) return;
        GiveBack(item, Math.Max(1, item.Stack), $"mapa {root}");
    }

    /// <summary>Devolve n unidades de um item pego em disputa (D92). Público para o teste da ponte (takeback).</summary>
    public static string GiveBack(BokuMono.Data.ItemData item, int n, string where)
    {
        // D92 (revisão A12): antes só tirava de UM espaço da mochila com o mesmo item; se o item já tinha sido guardado,
        // dividido em pilhas, comido ou vendido nesse meio segundo, nada saía e ele ficava duplicado. Agora, em cadeia:
        // mochila (mesma qualidade, depois qualquer qualidade, em quantos espaços precisar) → armazém (compartilhado; a
        // D88 manda a diferença) → o que ainda faltar sai do dinheiro do grupo pelo preço de venda.
        var left = TakeFrom(InventoryManager.Instance.BagItemStorage, item, n);
        var fromBag = n - left;
        var fromHouse = 0;
        if (left > 0)
        {
            var before = left;
            try { left = TakeFrom(InventoryManager.Instance.HouseStorage, item, left); } catch { }
            fromHouse = before - left;
        }
        uint charged = 0;
        if (left > 0)
        {
            try
            {
                var price = (long)Math.Max(0, item.SellPrice) * left;
                var money = (long)GameController.Instance.PlayerMoney;
                charged = (uint)Math.Min(price, money);
                if (charged > 0) GameController.Instance.SpendMoney(charged, true, null);
            }
            catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] devolver item {item.ItemId}: cobrar {left}: {e.Message}"); }
        }
        TakeBacks++;
        var msg = $"item {item.ItemId} x{n} devolvido (outro jogador pegou primeiro, {where}): mochila {fromBag}, armazém {fromHouse}, cobrado {charged} G por {left}";
        Plugin.Logger.LogInfo($"[FarmSync] {msg}");
        GameNotify.Show(charged > 0
            ? $"Outro jogador pegou esse item primeiro. Ele já tinha sido usado: {charged} G descontados."
            : "Outro jogador pegou esse item primeiro.");
        DevBridge.PushEvent($"item:devolvido:{item.ItemId}:{fromBag}:{fromHouse}:{charged}");
        return msg;
    }

    /// <summary>Tira até n unidades do item: primeiro os espaços de mesma qualidade, depois qualquer qualidade. Devolve o que faltou.</summary>
    private static int TakeFrom(StorageManager st, BokuMono.Data.ItemData item, int n)
    {
        if (st == null || n <= 0) return n;
        var datas = st.ItemDatas;
        if (datas == null) return n;
        for (var pass = 0; pass < 2 && n > 0; pass++)
            for (var i = 0; i < datas.Length && n > 0; i++)
            {
                var d = datas[i];
                if (d == null || d.ItemId != item.ItemId || d.Stack <= 0) continue;
                if (pass == 0 && d.QualityValue != item.QualityValue) continue;
                var k = Math.Min(n, d.Stack);
                st.Use(i, k);
                n -= k;
            }
        return n;
    }

    private static void SendToClients(byte[] packed, byte except)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id != except)
                NetSession.SendTo(id, MsgType.FieldOps, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
    }

    private static void SendSnapshot(uint root)
    {
        var sd = FieldManager.Instance.SaveManager.GetData(root);
        if (sd == null) return;
        var packed = LetterSync.Pack(FieldState.ToJson(sd));
        if (NetSession.Current == NetSession.Mode.Host) SendSnapshotToClients(packed, except: NetSession.LocalId);
        else NetSession.SendGame(MsgType.FieldSnapshot, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
        Plugin.Logger.LogInfo($"[FarmSync] saí do mapa {root}: mapa inteiro enviado ({packed.Length} bytes)");
        DevBridge.PushEvent($"fazenda:mapa-enviado:{root}");
    }

    private static void SendSnapshotToClients(byte[] packed, byte except)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id != except)
                NetSession.SendTo(id, MsgType.FieldSnapshot, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
    }

    private static void OnSnapshot(byte from, byte[] packed)
    {
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        if (!Ready()) return;
        var sd = FieldState.FromJson(LetterSync.Unpack(packed));
        var root = FieldState.Root((uint)sd.FieldId);
        if (NetSession.Current == NetSession.Mode.Host) SendSnapshotToClients(packed, except: from);
        if (FieldState.Root(FieldState.CurrentField) == root)
        {
            // Estou nesse mapa: as operações ao vivo já me deixaram igual.
            Plugin.Logger.LogInfo($"[FarmSync] mapa {root} recebido, mas estou nele (ao vivo); ignorado");
            return;
        }
        var ok = FieldState.Install(sd);
        Plugin.Logger.LogInfo($"[FarmSync] mapa {root} de #{from} {(ok ? "instalado no save de mapas" : "NÃO instalado (mapa desconhecido)")}");
        DevBridge.PushEvent($"fazenda:mapa-instalado:{root}:{ok}");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type == MsgType.ItemTakeBack)
        {
            try { OnTakeBack(from, r); }
            catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] devolver item: {e.Message}"); }
            return;
        }
        if (type != MsgType.FieldSnapshot && type != MsgType.FieldOps) return;
        // Carregando o mundo ou trocando de mapa: guarda e aplica quando ficar pronto (antes era descartado, D63).
        if (!Ready()) { Held.HoldIfAllowed(type, from, r); return; }
        if (type == MsgType.FieldSnapshot)
        {
            try { OnSnapshot(from, r.GetBlob()); }
            catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] mapa recebido: {e.Message}"); }
            return;
        }
        if (type != MsgType.FieldOps) return;
        var packed = r.GetBlob();
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        try
        {
            var (root, ops) = Unpack(packed);
            if (NetSession.Current == NetSession.Mode.Host)
            {
                var kept = RefuseLatePickups(from, root, ops);
                if (kept.Count != ops.Count) { ops = kept; packed = Pack(root, ops); }
                if (ops.Count == 0) return;
            }
            Apply(root, ops);
            ReceivedOps += ops.Count;
            if (NetSession.Current == NetSession.Mode.Host) SendToClients(packed, except: from);
            DevBridge.PushEvent($"fazenda:recebido:{ops.Count}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] aplicar: {e.Message}"); }
    }

    private static void Apply(uint root, List<(byte op, FieldState.Entry e, string guid)> ops)
    {
        var isCurrent = FieldState.Root(FieldState.CurrentField) == root;
        if (isCurrent) Flush(); // mudanças locais pendentes saem antes
        var cache = FieldState.CacheFor(root);
        if (cache == null) { Plugin.Logger.LogInfo($"[FarmSync] mapa {root} não carregado aqui; {ops.Count} mudança(s) ficam para o mapa inteiro (ao sair)"); return; }

        // Remover (inclusive os que serão recriados): filhos antes dos pais.
        foreach (var o in ops.OrderByDescending(o => (int)o.e.Type))
        {
            var ent = FieldState.Find(cache, o.guid);
            if (ent != null) ent.Unregister();
        }
        // Criar: pais antes dos filhos.
        var failed = 0;
        foreach (var o in ops.Where(o => o.op == OpUpsert).OrderBy(o => (int)o.e.Type))
        {
            try { FieldState.Create(o.e, cache); }
            catch (Exception ex) { failed++; Plugin.Logger.LogWarning($"[FarmSync] criar {o.e.Type} {o.guid}: {ex.Message}"); }
        }
        if (isCurrent) { _baseline = FieldState.CaptureLive(); _baselineField = root; }
        Plugin.Logger.LogInfo($"[FarmSync] {ops.Count} mudança(s) recebida(s) no mapa {root}{(isCurrent ? "" : " (residente)")}{(failed > 0 ? $", {failed} falha(s)" : "")}");
    }

    // ---------- formato: [uint mapa][int n]{[byte op][byte tipo][string guid][string json]} em gzip ----------

    private static byte[] Pack(uint root, List<(byte op, FieldState.Entry e, string guid)> ops)
    {
        var w = new NetDataWriter();
        w.Put(root);
        w.Put(ops.Count);
        foreach (var o in ops)
        {
            w.Put(o.op);
            w.Put((byte)o.e.Type);
            w.Put(o.guid);
            w.Put(o.op == OpUpsert ? o.e.Json : "");
        }
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) gz.Write(w.Data, 0, w.Length);
        return ms.ToArray();
    }

    private static (uint, List<(byte, FieldState.Entry, string)>) Unpack(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        var r = new NetDataReader(ms.ToArray());
        var root = r.GetUInt();
        var n = r.GetInt();
        var ops = new List<(byte, FieldState.Entry, string)>(n);
        for (var i = 0; i < n; i++)
        {
            var op = r.GetByte();
            var kind = (FieldState.Kind)r.GetByte();
            var guid = r.GetString();
            var json = r.GetString();
            ops.Add((op, new FieldState.Entry { Type = kind, Json = json }, guid));
        }
        return (root, ops);
    }

    public static object Status() => new { baselineField = _baselineField, tracked = _baseline?.Count ?? 0, sent = SentOps, received = ReceivedOps, takenItems = TakenItems.Count, myPickups = MyPickups.Count, takeBacks = TakeBacks };
}
