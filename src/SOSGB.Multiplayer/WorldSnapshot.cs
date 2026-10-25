// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e ROTEIRO.md (tarefa 1.5).
using System;
using System.IO;
using BepInEx;
using BokuMono;
using BokuMono.SaveData;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SOSGB.Multiplayer;

/// <summary>
/// Snapshot do mundo = os bytes exatos que o jogo gravaria no .sav, sem tocar no disco.
/// É o que o convidado vai receber ao entrar no mundo do host (Fase 3).
///
/// Como funciona:
///  - Capturar: armamos a captura e pedimos ao jogo para salvar (SaveLoadManager.Save).
///    O patch em SaveDataAccessorWindows.SlotSave/SlotSystemSave desvia os bytes para a memória
///    e NÃO escreve no disco.
///  - Carregar: armamos a injeção e pedimos ao jogo para carregar um slot (LoadSlot).
///    O patch em SlotLoad devolve os bytes do snapshot em vez de ler o arquivo.
/// Assim não precisamos conhecer o formato do save: usamos o pipeline do próprio jogo.
/// </summary>
public static class WorldSnapshot
{
    public static byte[] Captured { get; private set; }
    private static bool _captureArmed;
    private static bool _injectArmed;

    /// <summary>Com a ponte de teste ligada, nenhum save vai para o disco (proteção dos saves do usuário).</summary>
    public static bool SavesBlocked => Plugin.DevBridgeEnabled.Value && Plugin.BlockDiskSaves.Value;

    public static string DumpPath => Path.Combine(Paths.BepInExRootPath, "worldsnap.sav");

    public static void Capture(int slot, Action<bool> done)
    {
        _captureArmed = true;
        Captured = null;
        var field = (FieldMasterId)FieldManager.Instance.CurrentFieldId;
        SaveLoadManager.Instance.Save(slot, field, (Action<int, SaveLoadResult>)((s, r) =>
        {
            _captureArmed = false;
            if (Captured != null) File.WriteAllBytes(DumpPath, Captured); // só para inspeção
            Plugin.Logger.LogInfo($"[WorldSnapshot] captura: {r}, {Captured?.Length ?? 0} bytes");
            done(Captured != null);
        }));
    }

    public static void Load(int slot)
    {
        if (Captured == null) throw new InvalidOperationException("nenhum snapshot capturado");
        _injectArmed = true;
        SaveLoadManager.Instance.LoadSlot(slot, (Action<SaveLoadResult>)(r =>
        {
            _injectArmed = false;
            Plugin.Logger.LogInfo($"[WorldSnapshot] carga: {r}");
            DevBridge.PushEvent($"worldload:{r}");
            if (r == SaveLoadResult.Succeess)
                SceneManager.Instance.LoadScene(SceneMode.Game, null, null);
        }));
    }

    [HarmonyPatch(typeof(SaveDataAccessorWindows), nameof(SaveDataAccessorWindows.SlotSave))]
    private static class SlotSavePatch
    {
        private static bool Prefix(Il2CppStructArray<byte> datas, int size, int slot, ref Result __result)
        {
            if (!_captureArmed)
            {
                if (!SavesBlocked) return true;
                Plugin.Logger.LogWarning($"[WorldSnapshot] save do slot {slot} BLOQUEADO (DevBridge.BlockDiskSaves)");
                DevBridge.PushEvent($"save-bloqueado:{slot}");
                __result = Result.Succeess;
                return false;
            }
            var bytes = new byte[size];
            for (var i = 0; i < size; i++) bytes[i] = datas[i];
            Captured = bytes;
            __result = Result.Succeess;
            return false; // não grava no disco
        }
    }

    [HarmonyPatch(typeof(SaveDataAccessorWindows), nameof(SaveDataAccessorWindows.SlotSystemSave))]
    private static class SlotSystemSavePatch
    {
        private static bool Prefix(ref Result __result)
        {
            if (!_captureArmed && !SavesBlocked) return true;
            __result = Result.Succeess;
            return false; // o cabeçalho do slot também não vai para o disco durante a captura
        }
    }

    [HarmonyPatch(typeof(SaveDataAccessorWindows), nameof(SaveDataAccessorWindows.SlotLoad))]
    private static class SlotLoadPatch
    {
        private static bool Prefix(ref Il2CppStructArray<byte> datas, ref Result __result)
        {
            if (!_injectArmed || Captured == null) return true;
            datas = Captured;
            __result = Result.Succeess;
            return false; // devolve o snapshot em vez de ler o arquivo
        }
    }
}
