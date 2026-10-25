// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d10 / #d39.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Correio compartilhado (4.6, D10/D10a/D39): uma caixa de correio para o grupo.
///
/// O estado do correio é o LetterManager (cartas na caixa, cartas já pegas/lidas/com anexo recebido, histórico).
/// Cada jogo confere esse estado a cada 1 s (JSON do ToSaveData). Mudou por conta própria (pegou as cartas,
/// leu, recebeu o anexo)? Manda o estado inteiro, comprimido, ao host (LetterData), que aplica e repassa.
/// Assim quem pega as cartas tira da caixa de todos, e um anexo recebido não pode ser recebido de novo por outro.
/// Sem trava: duas pessoas mexendo no correio no mesmo segundo é raro; vale a última mudança.
/// </summary>
public class LetterSync : MonoBehaviour
{
    public LetterSync(IntPtr ptr) : base(ptr) { }

    private static ulong _baseline;
    private static bool _hasBaseline;
    private static bool _subscribed;
    private float _nextCheck;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => _hasBaseline = false;
        NetSession.OnHosting += () => _hasBaseline = false;
        NetSession.OnJoined += () => _hasBaseline = false;
    }

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!LetterManager.IsInstance) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    private void Update()
    {
        try
        {
            if (!Ready() || Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 1f;
            var json = Capture();
            var sig = Fnv(json);
            if (!_hasBaseline) { _baseline = sig; _hasBaseline = true; return; }
            if (sig == _baseline) return;
            _baseline = sig;
            Send(Pack(json), except: NetSession.LocalId);
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[LetterSync] {e.Message}"); }
    }

    /// <summary>Diagnóstico: começo do JSON do correio e se ele volta igual depois de Build (ida e volta).</summary>
    public static object Debug()
    {
        var json = Capture();
        string rebuilt;
        try { rebuilt = StringExtensions.ToJson(Build(json)); } catch (Exception e) { rebuilt = "erro: " + e.Message; }
        return new { head = json.Substring(0, Math.Min(700, json.Length)), length = json.Length, roundTripEqual = rebuilt == json, rebuiltHead = rebuilt.Substring(0, Math.Min(300, rebuilt.Length)) };
    }

    private static string Capture() => StringExtensions.ToJson(LetterManager.Instance.ToSaveData());

    private static void Apply(string json)
    {
        LetterManager.Instance.FromSaveData(Build(json));
        _baseline = Fnv(Capture()); // o jogo pode normalizar o JSON; a base é o que ele gera depois de aplicar
        _hasBaseline = true;
    }

    /// <summary>
    /// Monta o SaveData do correio à mão a partir do JSON. O JsonToObject do jogo devolve NULO para este tipo
    /// (InBagLetterData não tem construtor vazio), então usamos os construtores do próprio jogo.
    /// </summary>
    private static LetterManager.SaveData Build(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        var sd = new LetterManager.SaveData();
        sd.NowIndex = U(root, "NowIndex");
        sd.InPostLetterIndex = UList(root, "InPostLetterIndex");
        sd.InPostLetterId = UList(root, "InPostLetterId");
        sd.LetterHistoryId = UList(root, "LetterHistoryId");
        sd.InPostLetterInfoList = InfoList(root, "InPostLetterInfoList");
        sd.LetterTrendData = new Il2CppSystem.Collections.Generic.List<LetterManager.LetterTrendData>();
        if (root.TryGetProperty("LetterTrendData", out var trends) && trends.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var t in trends.EnumerateArray())
            {
                var td = new LetterManager.LetterTrendData();
                td.index = t.TryGetProperty("index", out var ix) ? ix.GetInt32() : 0;
                td.data = UList(t, "data");
                sd.LetterTrendData.Add(td);
            }
        sd.InBagLetterList = new Il2CppSystem.Collections.Generic.List<LetterManager.InBagLetterData>();
        if (root.TryGetProperty("InBagLetterList", out var bag) && bag.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var l in bag.EnumerateArray())
            {
                var letter = new LetterManager.InBagLetterData(U(l, "Id"), U(l, "Index"));
                letter.IsOpen = l.TryGetProperty("IsOpen", out var o) && o.GetBoolean();
                letter.IsReceive = l.TryGetProperty("IsReceive", out var rc) && rc.GetBoolean();
                letter.Ticks = l.TryGetProperty("Ticks", out var tk) ? tk.GetInt64() : 0;
                letter.Info = InfoList(l, "Info");
                sd.InBagLetterList.Add(letter);
            }
        return sd;
    }

    private static uint U(System.Text.Json.JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetUInt32() : 0u;

    private static Il2CppSystem.Collections.Generic.List<uint> UList(System.Text.Json.JsonElement e, string name)
    {
        var list = new Il2CppSystem.Collections.Generic.List<uint>();
        if (e.TryGetProperty(name, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray()) list.Add(x.GetUInt32());
        return list;
    }

    private static Il2CppSystem.Collections.Generic.List<LetterManager.InBagLetterDataInfo> InfoList(System.Text.Json.JsonElement e, string name)
    {
        var list = new Il2CppSystem.Collections.Generic.List<LetterManager.InBagLetterDataInfo>();
        if (e.TryGetProperty(name, out var arr) && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray())
            {
                var info = new LetterManager.InBagLetterDataInfo();
                info.Id = U(x, "Id");
                info.Key = x.TryGetProperty("Key", out var k) && k.ValueKind == System.Text.Json.JsonValueKind.String ? k.GetString() : "";
                info.Value = U(x, "Value");
                list.Add(info);
            }
        return list;
    }

    /// <summary>Convidado → host; host → todos menos <paramref name="except"/>.</summary>
    private static void Send(byte[] packed, byte except)
    {
        if (NetSession.Current == NetSession.Mode.Host)
        {
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId && id != except)
                    NetSession.SendTo(id, MsgType.LetterData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
        }
        else
            NetSession.SendGame(MsgType.LetterData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
        Plugin.Logger.LogInfo($"[LetterSync] correio enviado ({packed.Length} bytes comprimidos)");
        DevBridge.PushEvent("correio:enviado");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        if (type != MsgType.LetterData) return;
        var data = r.GetBytesWithLength();
        if (!Ready()) return;
        if (NetSession.Current == NetSession.Mode.Client && from != Protocol.HostPlayerId) return;
        Apply(Unpack(data));
        if (NetSession.Current == NetSession.Mode.Host) Send(data, except: from);
        Plugin.Logger.LogInfo($"[LetterSync] correio atualizado por #{from}");
        DevBridge.PushEvent("correio:recebido");
    }

    private static ulong Fnv(string s)
    {
        unchecked
        {
            var h = 14695981039346656037UL;
            foreach (var c in s) h = (h ^ c) * 1099511628211UL;
            return h;
        }
    }

    internal static byte[] Pack(string json)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) { var b = Encoding.UTF8.GetBytes(json); gz.Write(b, 0, b.Length); }
        return ms.ToArray();
    }

    internal static string Unpack(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var sr = new StreamReader(gz, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    public static object Status() => new
    {
        inPost = LetterManager.IsInstance ? LetterManager.Instance.GetInPostLetterCount() : -1,
        inBag = LetterManager.IsInstance ? LetterManager.Instance.GetInBagLetterList().Count : -1,
        baseline = _baseline,
    };
}
