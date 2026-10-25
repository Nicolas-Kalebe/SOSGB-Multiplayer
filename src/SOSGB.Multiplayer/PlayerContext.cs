// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d3 / #d13 (troca de contexto).
using System;
using System.Collections.Generic;
using System.Linq;
using BokuMono;

namespace SOSGB.Multiplayer;

/// <summary>
/// Troca de contexto por jogador (D3/D13). Os managers do jogo são singletons com UM estado
/// (o de um só protagonista). Para ter dados por jogador, o host guarda o SaveData de cada
/// jogador e o carrega nos managers antes de rodar a lógica original para aquele jogador.
///
/// Partes individuais por jogador (ver DECISOES D3 e D13):
///   likeability → corações, presente/conversa do dia, namoro/casamento (LikeabilityManager)
///   talk        → conversas já feitas (TalkManager)
///   eventflags  → eventos vistos (EventFlagManager)
///   bag         → mochila de itens (InventoryManager.BagItemStorage)
///   tools       → mochila de ferramentas, incluindo upgrades (InventoryManager.BagToolStorage)
///   toolstate   → estado das ferramentas, ex.: água no regador (ToolManager)
///
/// IMPORTANTE (achado da tarefa 1.3): ToSaveData() e os construtores de cópia do jogo são RASOS.
/// Por isso tudo é guardado como JSON (StringExtensions.ToJson/JsonToObject, o Newtonsoft do
/// próprio jogo): cópia profunda de verdade, e é o mesmo formato que vai pela rede.
/// </summary>
public static class PlayerContext
{
    private sealed class Part
    {
        public string Name;
        public Func<string> Capture;
        public Action<string> Apply;
    }

    private static Part Make<T>(string name, Func<T> capture, Action<T> apply) where T : Il2CppSystem.Object => new()
    {
        Name = name,
        Capture = () => StringExtensions.ToJson(capture()),
        Apply = json => apply(StringExtensions.JsonToObject<T>(json)),
    };

    private static readonly Part[] Parts =
    {
        Make("likeability",
            () => LikeabilityManager.Instance.ToSaveData(),
            d => LikeabilityManager.Instance.FromSaveData(d)),
        Make("talk",
            () => TalkManager.Instance.ToSaveData(),
            d => TalkManager.Instance.FromSaveData(d)),
        // Flags de evento: o formato "Runtime" (bitset Flags) quebra o ToJson, e desserializar com
        // JsonToObject<EventFlagSaveData> duplica entradas ("same key has already been added").
        // Então capturamos na forma que o jogo grava no save e aplicamos flag a flag.
        new Part
        {
            Name = "eventflags",
            Capture = () => StringExtensions.ToJson(new EventFlagSaveData(EventFlagManager.Instance.ToSaveData())),
            Apply = ApplyEventFlags,
        },
        // Aparência (D28): o mundo do host traz a aparência DO HOST; o convidado reaplica a dele.
        new Part
        {
            Name = "appearance",
            Capture = () => Net.AppearanceSync.Swapping && Net.AppearanceSync.Stable != null ? Net.AppearanceSync.Stable : Net.AppearanceSync.Local(),
            Apply = json => Net.AppearanceSync.ApplyToLocal(json),
        },
        Make("bag",
            () => InventoryManager.Instance.BagItemStorage.ToSaveData(),
            d => InventoryManager.Instance.BagItemStorage.FromSaveData(d)),
        Make("tools",
            () => InventoryManager.Instance.BagToolStorage.ToSaveData(),
            d => InventoryManager.Instance.BagToolStorage.FromSaveData(d)),
        Make("toolstate",
            () => ToolManager.Instance.ToSaveData(),
            d => ToolManager.Instance.FromSaveData(d)),
        // Bloco por tipo de ferramenta (UserInfo.ToolSaveDatas, cifrado pelo jogo). Ferramentas são individuais
        // (D13), então o estado delas também; sem isto o convidado herdava o do host a cada virada (4.4).
        new Part
        {
            Name = "tooldata",
            Capture = CaptureToolData,
            Apply = ApplyToolData,
        },
        // Bônus de comida e outros efeitos temporários (PlayerBuffManager) são de cada jogador (pedido do usuário).
        // O vigor máximo é do MUNDO (compartilhado, SharedSync "maxstamina"), não entra aqui.
        Make("buffs",
            () => PlayerBuffManager.Instance.ToSaveData(),
            d => PlayerBuffManager.Instance.FromSaveData(d)),
    };

    // O ToJson do jogo não aceita o array IL2CPP direto: cada ToolSaveData vira um JSON, numa lista.
    private static string CaptureToolData()
    {
        var arr = ToolManager.Instance.SetToolSaveData();
        var list = new List<string>();
        foreach (var t in arr) if (t != null) list.Add(StringExtensions.ToJson(t));
        return System.Text.Json.JsonSerializer.Serialize(list);
    }

    private static void ApplyToolData(string json)
    {
        var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
        var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ToolSaveData>(list.Count);
        for (var i = 0; i < list.Count; i++) arr[i] = StringExtensions.JsonToObject<ToolSaveData>(list[i]);
        ToolManager.Instance.FromSaveData(arr);
    }

    private static void ApplyEventFlags(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var efm = EventFlagManager.Instance;
        efm.ResetEventFlag();
        foreach (var e in doc.RootElement.GetProperty("EventFlagData").EnumerateArray())
            efm.SetEventFlag(e.GetProperty("FlagId").GetUInt32(), e.GetProperty("Flag").GetInt32(),
                e.GetProperty("FlagHistoryTime").GetInt64());
    }

    /// <summary>Partes do perfil individual de um convidado (D24: flags de evento ficam no mundo por enquanto).</summary>
    public static readonly string[] ProfileParts = { "likeability", "talk", "bag", "tools", "toolstate", "tooldata", "buffs", "appearance" };

    public static Dictionary<string, string> CaptureParts(IEnumerable<string> names)
    {
        var set = new HashSet<string>(names);
        var data = new Dictionary<string, string>();
        foreach (var p in Parts) if (set.Contains(p.Name)) data[p.Name] = p.Capture();
        return data;
    }

    public static Dictionary<string, object> ApplyParts(Dictionary<string, string> data)
    {
        var report = new Dictionary<string, object>();
        foreach (var p in Parts)
        {
            if (!data.TryGetValue(p.Name, out var json)) continue;
            try { p.Apply(json); report[p.Name] = "ok"; }
            catch (Exception e) { report[p.Name] = "ERRO: " + e.Message.Split('\n')[0]; }
        }
        return report;
    }

    /// <summary>nome do contexto → (parte → JSON)</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> Contexts = new();

    public static IEnumerable<string> PartNames => Parts.Select(p => p.Name);
    public static IEnumerable<string> Names => Contexts.Keys;

    /// <summary>Captura todas as partes. Retorna parte → tamanho do JSON (ou erro).</summary>
    public static Dictionary<string, object> Save(string name)
    {
        var data = new Dictionary<string, string>();
        var report = new Dictionary<string, object>();
        foreach (var p in Parts)
        {
            try
            {
                var json = p.Capture();
                data[p.Name] = json;
                report[p.Name] = json.Length;
            }
            catch (Exception e) { report[p.Name] = "ERRO: " + e.Message.Split('\n')[0]; }
        }
        Contexts[name] = data;
        return report;
    }

    /// <summary>Aplica todas as partes guardadas. Retorna parte → "ok" (ou erro).</summary>
    public static Dictionary<string, object> Load(string name)
    {
        if (!Contexts.TryGetValue(name, out var data))
            throw new KeyNotFoundException($"contexto '{name}' não existe");
        var report = new Dictionary<string, object>();
        foreach (var p in Parts)
        {
            if (!data.TryGetValue(p.Name, out var json)) { report[p.Name] = "ausente"; continue; }
            try { p.Apply(json); report[p.Name] = "ok"; }
            catch (Exception e) { report[p.Name] = "ERRO: " + e.Message.Split('\n')[0]; }
        }
        return report;
    }

    public static string GetJson(string name, string part) =>
        Contexts.TryGetValue(name, out var d) && d.TryGetValue(part, out var json)
            ? json
            : throw new KeyNotFoundException($"contexto '{name}' / parte '{part}' não existe");
}
