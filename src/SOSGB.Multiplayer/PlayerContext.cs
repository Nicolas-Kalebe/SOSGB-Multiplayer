// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d3 (troca de contexto).
using System.Collections.Generic;
using BokuMono;

namespace SOSGB.Multiplayer;

/// <summary>
/// Troca de contexto por jogador (D3/D13). Os managers do jogo são singletons com UM estado
/// (ex.: LikeabilityManager tem a amizade de um só protagonista). Para ter dados por jogador,
/// o host guarda o SaveData de cada jogador e o carrega no manager antes de rodar a lógica
/// original para aquele jogador.
///
/// IMPORTANTE (achado da tarefa 1.3): ToSaveData() e o construtor de cópia do SaveData são
/// RASOS, porque os objetos LikeabilityData continuam sendo os mesmos do manager vivo. Um snapshot
/// "guardado" mudava junto com o jogo. Por isso os snapshots são guardados como JSON
/// (StringExtensions.ToJson/JsonToObject, o Newtonsoft do próprio jogo): cópia profunda de verdade,
/// e é o mesmo formato que vai pela rede.
/// </summary>
public static class PlayerContext
{
    private static readonly Dictionary<string, string> Likeability = new();

    public static string CaptureLikeability() =>
        StringExtensions.ToJson(LikeabilityManager.Instance.ToSaveData());

    public static void ApplyLikeability(string json) =>
        LikeabilityManager.Instance.FromSaveData(StringExtensions.JsonToObject<LikeabilityManager.SaveData>(json));

    public static void SaveLikeability(string name) => Likeability[name] = CaptureLikeability();

    public static void LoadLikeability(string name)
    {
        if (!Likeability.TryGetValue(name, out var json))
            throw new KeyNotFoundException($"contexto '{name}' não existe");
        ApplyLikeability(json);
    }

    public static string GetJson(string name) =>
        Likeability.TryGetValue(name, out var json) ? json : throw new KeyNotFoundException($"contexto '{name}' não existe");

    public static IEnumerable<string> Names => Likeability.Keys;
}
