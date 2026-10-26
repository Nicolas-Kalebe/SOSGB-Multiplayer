// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e ROTEIRO.md (tarefa 1.5).
using System;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP.Utils.Collections;
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

    /// <summary>
    /// Este jogo está com o mundo de OUTRO jogador na memória (recebido como convidado). Continua valendo depois de uma
    /// queda de conexão (o modo vira "offline"), até voltar ao título: gravar agora poria o mundo do host no save deste
    /// jogador (o save automático da cama vai para o slot 0, o dele). D59.
    /// </summary>
    public static bool HoldingHostWorld { get; set; }

    /// <summary>Com a ponte de teste ligada, nenhum save vai para o disco (proteção dos saves do usuário).</summary>
    public static bool SavesBlocked =>
        (Plugin.DevMode && Plugin.BlockDiskSaves.Value)
        || Net.NetSession.Current == Net.NetSession.Mode.Client // D2/D23: convidado nunca grava no disco
        || HoldingHostWorld;

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

    /// <summary>Recebe um snapshot vindo da rede (convidado, Fase 3.1).</summary>
    public static void SetCaptured(byte[] bytes) => Captured = bytes;

    public static bool Capturing => _captureArmed;

    /// <summary>Depois de o save (snapshot) ser lido e ANTES da cena Game carregar: dá para ajustar o UserInfo
    /// (ex.: o convidado coloca a própria aparência/gênero, e o jogo monta o personagem já certo — D28).</summary>
    public static event Action BeforeSceneLoad;

    /// <summary>Tarefa assíncrona opcional antes de carregar a cena (ex.: pré-carregar peças de aparência).</summary>
    public static Func<System.Collections.IEnumerator> BeforeSceneLoadRoutine;

    /// <summary>
    /// D23 — recuperação contra queda: o host captura o mundo periodicamente e grava num arquivo À PARTE
    /// (nunca no slot oficial): %APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\recovery_*.sav.
    /// O arquivo é um .sav válido (bytes do próprio jogo). Restaurar é manual (ver Net/CONTEXTO.md).
    /// </summary>
    public static void CaptureRecovery(string path)
    {
        if (_captureArmed) return;
        Capture(ExternalData.Instance.UserInfo.Slot, ok =>
        {
            if (!ok || Captured == null) return;
            File.WriteAllBytes(path, Captured);
            Plugin.Logger.LogInfo($"[WorldSnapshot] snapshot de recuperação gravado ({Captured.Length} bytes): {path}");
            DevBridge.PushEvent($"recuperacao:{Captured.Length}");
        });
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
            {
                ExternalData.Instance.UserInfo.Slot = slot; // ver DevBridge.LoadSlot (save automático no slot certo)
                try { BeforeSceneLoad?.Invoke(); }
                catch (Exception e) { Plugin.Logger.LogWarning($"[WorldSnapshot] BeforeSceneLoad: {e.Message}"); }
                var routine = BeforeSceneLoadRoutine?.Invoke();
                if (routine != null && Net.AppearanceSync.Instance != null)
                    Net.AppearanceSync.Instance.StartCoroutine(ThenLoadScene(routine).WrapToIl2Cpp());
                else
                    SceneManager.Instance.LoadScene(SceneMode.Game, null, null);
            }
        }));
    }

    private static System.Collections.IEnumerator ThenLoadScene(System.Collections.IEnumerator before)
    {
        yield return before.WrapToIl2Cpp();
        SceneManager.Instance.LoadScene(SceneMode.Game, null, null);
    }

    /// <summary>Voltar ao título encerra o "mundo do host na memória" (D59): o próximo jogo carregado é um save deste jogador.</summary>
    [HarmonyPatch]
    private static class TitlePatch
    {
        // LoadScene tem duas versões (a pública recebe o int da cena; a privada, um SceneParam): só a pública.
        private static System.Reflection.MethodBase TargetMethod() =>
            System.Linq.Enumerable.First(typeof(SceneManager).GetMethods(), m => m.Name == nameof(SceneManager.LoadScene)
                && m.GetParameters().Length == 3 && m.GetParameters()[0].ParameterType == typeof(int));

        private static void Prefix(int sceneMode)
        {
            if (sceneMode != SceneMode.Title || !HoldingHostWorld) return;
            HoldingHostWorld = false;
            Plugin.Logger.LogInfo("[WorldSnapshot] voltou ao título: o mundo do host saiu da memória (saves liberados)");
        }
    }

    [HarmonyPatch(typeof(SaveDataAccessorWindows), nameof(SaveDataAccessorWindows.SlotSave))]
    private static class SlotSavePatch
    {
        private static bool Prefix(Il2CppStructArray<byte> datas, int size, int slot, ref Result __result)
        {
            if (!_captureArmed)
            {
                // D23: no multiplayer, o save oficial só acontece na virada do dia, feito pelo host.
                if (Net.NetSession.Current == Net.NetSession.Mode.Host && !Net.DaySync.InDayChange)
                {
                    Plugin.Logger.LogWarning($"[WorldSnapshot] save manual do slot {slot} RECUSADO (multiplayer: só ao dormir, D23)");
                    DevBridge.PushEvent($"save-recusado-d23:{slot}");
                    Notice.Show("Multiplayer: o jogo só salva quando todos dormem.");
                    __result = Result.TargetLocked;
                    return false;
                }
                if (Net.NetSession.Current == Net.NetSession.Mode.Host)
                    DevBridge.PushEvent($"save-permitido-d23:{slot}");
                if (!SavesBlocked) return true;
                if (Net.NetSession.Current == Net.NetSession.Mode.Client)
                    Notice.Show("Multiplayer: quem salva é o host, quando todos dormem.");
                else if (HoldingHostWorld)
                    Notice.Show("Este mundo é do host (a conexão caiu): não é salvo aqui. Volte ao título.");
                Plugin.Logger.LogWarning($"[WorldSnapshot] save do slot {slot} BLOQUEADO (DevBridge.BlockDiskSaves, convidado ou mundo do host)");
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
