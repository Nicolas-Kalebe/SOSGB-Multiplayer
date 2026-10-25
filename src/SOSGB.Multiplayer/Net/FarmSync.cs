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
/// </summary>
public class FarmSync : MonoBehaviour
{
    public FarmSync(IntPtr ptr) : base(ptr) { }

    private const byte OpUpsert = 1, OpRemove = 2;

    private static uint _baselineField;
    private static Dictionary<string, FieldState.Entry> _baseline;
    private static bool _subscribed;
    private float _nextTick;
    public static int SentOps, ReceivedOps;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => _baseline = null;
        NetSession.OnHosting += () => _baseline = null;
        NetSession.OnJoined += () => _baseline = null;
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
            if (!Ready() || Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            Flush();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[FarmSync] {e.Message}"); }
    }

    /// <summary>Compara o mapa atual (se residente) com a base e manda as diferenças.</summary>
    private static void Flush()
    {
        var root = FieldState.Root(FieldState.CurrentField);
        if (!IsResident(root)) { _baseline = null; return; }
        var cur = FieldState.CaptureLive();
        if (_baseline == null || _baselineField != root) { _baseline = cur; _baselineField = root; return; }
        var ops = new List<(byte op, FieldState.Entry e, string guid)>();
        foreach (var kv in cur)
            if (!_baseline.TryGetValue(kv.Key, out var old) || old.Json != kv.Value.Json) ops.Add((OpUpsert, kv.Value, kv.Key));
        foreach (var kv in _baseline)
            if (!cur.ContainsKey(kv.Key)) ops.Add((OpRemove, kv.Value, kv.Key));
        _baseline = cur;
        if (ops.Count == 0) return;
        var packed = Pack(root, ops);
        if (NetSession.Current == NetSession.Mode.Host) SendToClients(packed, except: NetSession.LocalId);
        else NetSession.SendGame(MsgType.FieldOps, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
        SentOps += ops.Count;
        Plugin.Logger.LogInfo($"[FarmSync] {ops.Count} mudança(s) no mapa {root} enviada(s) ({packed.Length} bytes)");
        DevBridge.PushEvent($"fazenda:enviado:{ops.Count}");
    }

    private static void SendToClients(byte[] packed, byte except)
    {
        foreach (var id in NetSession.Players.Keys)
            if (id != NetSession.LocalId && id != except)
                NetSession.SendTo(id, MsgType.FieldOps, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.FieldOps) return;
        var packed = r.GetBytesWithLength();
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        if (!Ready()) return; // carregando mundo/virada do dia: o mundo novo já vem certo
        try
        {
            var (root, ops) = Unpack(packed);
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
        if (cache == null) { Plugin.Logger.LogWarning($"[FarmSync] mapa {root} não carregado; {ops.Count} mudança(s) ignorada(s)"); return; }

        // Remover (inclusive os que serão recriados): filhos antes dos pais.
        foreach (var o in ops.OrderByDescending(o => (int)o.e.Type))
        {
            var ent = cache.GetLayerObject(o.guid);
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

    public static object Status() => new { baselineField = _baselineField, tracked = _baseline?.Count ?? 0, sent = SentOps, received = ReceivedOps };
}
