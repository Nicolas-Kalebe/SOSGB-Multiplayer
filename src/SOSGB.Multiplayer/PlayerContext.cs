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
        Make("bag",
            () => InventoryManager.Instance.BagItemStorage.ToSaveData(),
            d => InventoryManager.Instance.BagItemStorage.FromSaveData(d)),
        Make("tools",
            () => InventoryManager.Instance.BagToolStorage.ToSaveData(),
            d => InventoryManager.Instance.BagToolStorage.FromSaveData(d)),
        Make("toolstate",
            () => ToolManager.Instance.ToSaveData(),
            d => ToolManager.Instance.FromSaveData(d)),
    };

    private static void ApplyEventFlags(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var efm = EventFlagManager.Instance;
        efm.ResetEventFlag();
        foreach (var e in doc.RootElement.GetProperty("EventFlagData").EnumerateArray())
            efm.SetEventFlag(e.GetProperty("FlagId").GetUInt32(), e.GetProperty("Flag").GetInt32(),
                e.GetProperty("FlagHistoryTime").GetInt64());
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
