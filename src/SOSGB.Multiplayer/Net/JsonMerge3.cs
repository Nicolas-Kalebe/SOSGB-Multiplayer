// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d91.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Mescla em três vias de estados do jogo em JSON (D91, revisão A04). Antes, quem recebia o estado inteiro de uma parte
/// (moinho, cogumelos...) aplicava por cima do seu: se dois jogadores mexessem ao mesmo tempo, a mudança de um apagava a do
/// outro. Aqui, com a BASE (a última versão que os dois tinham), juntam-se as mudanças de cada lado:
///  - campo que só um lado mudou → fica a mudança;
///  - os dois mudaram para o mesmo valor → fica;
///  - os dois mudaram diferente (conflito real) → vence o REMOTO (no host, o que chegou; no convidado, o canônico do host);
///  - campos de movimento (Volatile) → sempre os locais (pets não pulam de lugar);
///  - listas: com objetos de "Guid", casam pelo Guid; com o mesmo tamanho nos três (espaços fixos: um por moinho, por
///    tronco de cogumelo), item a item; senão, como multiconjunto: cada item fica com local + remoto − base cópias
///    (os dois põem receitas no moinho → ficam as duas; um retira um item pronto → sai uma vez).
/// </summary>
public static class JsonMerge3
{
    public static int Conflicts; // conflitos reais resolvidos a favor do remoto (diagnóstico)

    /// <summary>multisetLists = false: listas de tamanhos diferentes não somam acréscimos (fica a do lado que mudou; nos
    /// dois, a remota). Para estados em que somar duplicaria (animais), D91.</summary>
    public static string Merge(string baseJson, string localJson, string remoteJson, ISet<string> volatileKeys, bool multisetLists = true, ISet<string> additiveKeys = null)
    {
        var b = JsonNode.Parse(baseJson);
        var l = JsonNode.Parse(localJson);
        var r = JsonNode.Parse(remoteJson);
        _multiset = multisetLists;
        _additive = additiveKeys;
        try { return Node(b, l, r, volatileKeys)?.ToJsonString() ?? "null"; }
        finally { _multiset = true; _additive = null; }
    }

    [ThreadStatic] private static bool _multiset;
    [ThreadStatic] private static ISet<string> _additive;

    /// <summary>
    /// Contadores de progresso (D93, revisão A07/A06): os dois avançaram o mesmo contador → soma as duas contribuições
    /// (base + o que cada um fez). Ex.: título em 5, um faz +1 e o outro +2 → 8 (sem isto, vencia o remoto: 7).
    /// </summary>
    public static readonly HashSet<string> ProgressKeys = new() { "ProgressNumber", "progress", "easy", "hard" };

    /// <summary>Chaves que identificam um objeto numa lista (casam por elas, como o Guid): títulos, cartelas.</summary>
    private static readonly string[] IdKeys = { "Guid", "CardId", "AwardId" };

    private static string S(JsonNode n) => n?.ToJsonString() ?? "null";
    private static bool Eq(JsonNode a, JsonNode b) => S(a) == S(b);
    private static JsonNode Clone(JsonNode n) => n == null ? null : JsonNode.Parse(n.ToJsonString());

    private static JsonNode Node(JsonNode b, JsonNode l, JsonNode r, ISet<string> vol)
    {
        if (Eq(l, r)) return Clone(l);
        if (Eq(l, b)) return Clone(r);
        if (Eq(r, b)) return Clone(l);
        if (l is JsonObject lo && r is JsonObject ro) return Obj(b as JsonObject, lo, ro, vol);
        if (l is JsonArray la && r is JsonArray ra) return Arr(b as JsonArray, la, ra, vol);
        Conflicts++;
        return Clone(r);
    }

    private static JsonObject Obj(JsonObject b, JsonObject l, JsonObject r, ISet<string> vol)
    {
        var res = new JsonObject();
        var keys = new List<string>();
        foreach (var kv in l) keys.Add(kv.Key);
        foreach (var kv in r) if (!l.ContainsKey(kv.Key)) keys.Add(kv.Key);
        foreach (var k in keys)
        {
            var lv = l.ContainsKey(k) ? l[k] : null;
            var rv = r.ContainsKey(k) ? r[k] : null;
            if (vol.Contains(k)) { res[k] = Clone(l.ContainsKey(k) ? lv : rv); continue; }
            var bv = b != null && b.ContainsKey(k) ? b[k] : null;
            if (_additive != null && _additive.Contains(k) && Num(bv, out var bn) && Num(lv, out var ln) && Num(rv, out var rn)
                && ln != bn && rn != bn && ln != rn)
            {
                res[k] = JsonValue.Create(bn + (ln - bn) + (rn - bn));
                continue;
            }
            res[k] = Node(bv, lv, rv, vol);
        }
        return res;
    }

    private static bool Num(JsonNode n, out long v)
    {
        v = 0;
        return n is JsonValue jv && jv.TryGetValue<long>(out v);
    }

    /// <summary>Identidade de um objeto de lista: Guid, CardId ou AwardId (o primeiro que tiver).</summary>
    private static string GuidOf(JsonNode n)
    {
        if (n is not JsonObject o) return null;
        foreach (var k in IdKeys)
            if (o[k] is JsonValue v)
            {
                var s = v.ToJsonString().Trim('"');
                if (!string.IsNullOrEmpty(s)) return k + ":" + s;
            }
        return null;
    }

    private static JsonArray Arr(JsonArray b, JsonArray l, JsonArray r, ISet<string> vol)
    {
        b ??= new JsonArray();
        // 1) objetos com Guid: casam pelo Guid
        if (l.Count + r.Count > 0 && l.Concat(r).All(x => GuidOf(x) != null) && Unique(b) && Unique(l) && Unique(r))
        {
            var bm = ByGuid(b); var lm = ByGuid(l); var rm = ByGuid(r);
            var res = new JsonArray();
            foreach (var lx in l)
            {
                var g = GuidOf(lx);
                var inB = bm.TryGetValue(g, out var bx);
                if (rm.TryGetValue(g, out var rx)) res.Add(Node(inB ? bx : null, lx, rx, vol));
                else if (!inB) res.Add(Clone(lx));  // novo daqui
                // senão: o remoto tirou → sai
            }
            foreach (var rx in r)
            {
                var g = GuidOf(rx);
                if (!lm.ContainsKey(g) && !bm.ContainsKey(g)) res.Add(Clone(rx)); // novo de lá
            }
            return res;
        }
        // 2) espaços fixos: mesmo tamanho nos três → item a item
        if (b.Count == l.Count && l.Count == r.Count)
        {
            var res = new JsonArray();
            for (var i = 0; i < l.Count; i++) res.Add(Node(b[i], l[i], r[i], vol));
            return res;
        }
        if (!_multiset) { Conflicts++; return (JsonArray)Clone(r); }
        // 3) lista: multiconjunto (cada item fica com local + remoto − base cópias)
        var cb = Count(b); var cl = Count(l); var cr = Count(r);
        var want = new Dictionary<string, int>();
        foreach (var k in cl.Keys.Concat(cr.Keys).Distinct())
            want[k] = Math.Max(0, cl.GetValueOrDefault(k) + cr.GetValueOrDefault(k) - cb.GetValueOrDefault(k));
        var outArr = new JsonArray();
        foreach (var x in l.Concat(r))
        {
            var k = S(x);
            if (want.TryGetValue(k, out var n) && n > 0) { outArr.Add(Clone(x)); want[k] = n - 1; }
        }
        return outArr;
    }

    private static Dictionary<string, JsonNode> ByGuid(JsonArray a)
    {
        var d = new Dictionary<string, JsonNode>();
        foreach (var x in a) { var g = GuidOf(x); if (g != null) d[g] = x; }
        return d;
    }

    private static bool Unique(JsonArray a)
    {
        var seen = new HashSet<string>();
        foreach (var x in a) { var g = GuidOf(x); if (g != null && !seen.Add(g)) return false; }
        return true;
    }

    /// <summary>Casos de teste (ponte: shared merge3): devolve "ok" ou o primeiro que falhou.</summary>
    public static string SelfTest()
    {
        var vol = new HashSet<string> { "Position" };
        (string b, string l, string r, string want)[] cases =
        {
            // moinho: os dois põem receitas diferentes → ficam as duas
            ("{\"q\":[]}", "{\"q\":[{\"id\":1}]}", "{\"q\":[{\"id\":2}]}", "{\"q\":[{\"id\":1},{\"id\":2}]}"),
            // um retira o item pronto, o outro põe outro → sai um, entra o outro
            ("{\"q\":[{\"id\":1}]}", "{\"q\":[]}", "{\"q\":[{\"id\":1},{\"id\":2}]}", "{\"q\":[{\"id\":2}]}"),
            // os dois retiram o mesmo → sai uma vez só
            ("{\"q\":[{\"id\":1},{\"id\":1}]}", "{\"q\":[{\"id\":1}]}", "{\"q\":[{\"id\":1}]}", "{\"q\":[{\"id\":1}]}"),
            // espaços fixos: cada um mexe num tronco → os dois
            ("{\"t\":[{\"c\":0},{\"c\":0}]}", "{\"t\":[{\"c\":5},{\"c\":0}]}", "{\"t\":[{\"c\":0},{\"c\":7}]}", "{\"t\":[{\"c\":5},{\"c\":7}]}"),
            // campos diferentes do mesmo objeto → os dois
            ("{\"a\":1,\"b\":1}", "{\"a\":2,\"b\":1}", "{\"a\":1,\"b\":3}", "{\"a\":2,\"b\":3}"),
            // conflito real → remoto
            ("{\"a\":1}", "{\"a\":2}", "{\"a\":3}", "{\"a\":3}"),
            // Guid: posição local fica, carinho remoto entra, pet novo de lá entra
            ("{\"p\":[{\"Guid\":\"x\",\"Position\":0,\"f\":1}]}", "{\"p\":[{\"Guid\":\"x\",\"Position\":9,\"f\":1}]}",
             "{\"p\":[{\"Guid\":\"x\",\"Position\":4,\"f\":2},{\"Guid\":\"y\",\"Position\":1,\"f\":0}]}",
             "{\"p\":[{\"Guid\":\"x\",\"Position\":9,\"f\":2},{\"Guid\":\"y\",\"Position\":1,\"f\":0}]}"),
            // Guid tirado de lá (vendido) → sai daqui também
            ("{\"p\":[{\"Guid\":\"x\"},{\"Guid\":\"y\"}]}", "{\"p\":[{\"Guid\":\"x\"},{\"Guid\":\"y\"}]}", "{\"p\":[{\"Guid\":\"x\"}]}", "{\"p\":[{\"Guid\":\"x\"}]}"),
            // D93: título em 5, um faz +1 e o outro +2 → 8; cada um conclui um título diferente → os dois
            ("{\"A\":[{\"AwardId\":1,\"ProgressNumber\":5},{\"AwardId\":2,\"IsComplete\":false},{\"AwardId\":3,\"IsComplete\":false}]}",
             "{\"A\":[{\"AwardId\":1,\"ProgressNumber\":6},{\"AwardId\":2,\"IsComplete\":true},{\"AwardId\":3,\"IsComplete\":false}]}",
             "{\"A\":[{\"AwardId\":1,\"ProgressNumber\":7},{\"AwardId\":2,\"IsComplete\":false},{\"AwardId\":3,\"IsComplete\":true}]}",
             "{\"A\":[{\"AwardId\":1,\"ProgressNumber\":8},{\"AwardId\":2,\"IsComplete\":true},{\"AwardId\":3,\"IsComplete\":true}]}"),
            // D93: cartela nova só de um lado + progresso do outro numa cartela existente
            ("{\"cards\":[{\"CardId\":1,\"sq\":[{\"slot\":0,\"progress\":1}]}]}",
             "{\"cards\":[{\"CardId\":1,\"sq\":[{\"slot\":0,\"progress\":2}]}]}",
             "{\"cards\":[{\"CardId\":1,\"sq\":[{\"slot\":0,\"progress\":1}]},{\"CardId\":9,\"sq\":[]}]}",
             "{\"cards\":[{\"CardId\":1,\"sq\":[{\"slot\":0,\"progress\":2}]},{\"CardId\":9,\"sq\":[]}]}"),
        };
        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            var got = Merge(c.b, c.l, c.r, vol, additiveKeys: ProgressKeys);
            if (got != JsonNode.Parse(c.want)!.ToJsonString()) return $"caso {i + 1}: esperado {c.want}, veio {got}";
        }
        return "ok";
    }

    private static Dictionary<string, int> Count(JsonArray a)
    {
        var d = new Dictionary<string, int>();
        foreach (var x in a) { var k = S(x); d[k] = d.GetValueOrDefault(k) + 1; }
        return d;
    }
}
