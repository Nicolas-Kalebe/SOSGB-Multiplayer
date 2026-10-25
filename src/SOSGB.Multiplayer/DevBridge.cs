// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d20.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Ponte de teste (D20), SÓ PARA DESENVOLVIMENTO. Permite que uma IA teste o jogo sozinha:
///  - servidor TCP em 127.0.0.1 (nunca na rede): um comando por linha, uma resposta JSON por linha;
///  - log de estado periódico em BepInEx/sosgb-state.jsonl;
///  - screenshots em BepInEx/screenshots/;
///  - auto-load de um slot ao chegar na tela de título.
/// Comandos chegam numa thread de rede e são executados na thread principal da Unity (Update),
/// porque a API do jogo não é thread-safe.
/// </summary>
public class DevBridge : MonoBehaviour
{
    public DevBridge(IntPtr ptr) : base(ptr) { }

    private static DevBridge Instance;

    private void Awake()
    {
        Instance = this;
        // Sem isso a Unity congela o jogo quando a janela perde o foco, e os testes param
        // (e duas instâncias no mesmo PC, para testar o multiplayer, não rodariam juntas).
        Application.runInBackground = true;
    }

    private sealed class Pending
    {
        public string Line;
        public string Response;
        public readonly ManualResetEventSlim Done = new(false);
    }

    private static readonly ConcurrentQueue<Pending> Queue = new();
    private static readonly ConcurrentQueue<string> Events = new();
    private static TcpListener _listener;
    private static Thread _thread;

    private static string _stateLogPath;
    private static string _screenshotDir;

    private float _nextStateLog;
    private bool _autoLoadDone;
    private float _titleSince = -1f;

    /// <summary>Registra um evento no próximo registro do sosgb-state.jsonl.</summary>
    public static void PushEvent(string ev) => Events.Enqueue(ev);

    // ---------- inicialização (chamada pelo Plugin.Load) ----------

    public static void Start(int port)
    {
        _stateLogPath = Path.Combine(Paths.BepInExRootPath, "sosgb-state.jsonl");
        _screenshotDir = Path.Combine(Paths.BepInExRootPath, "screenshots");
        Directory.CreateDirectory(_screenshotDir);
        File.WriteAllText(_stateLogPath, ""); // um log por sessão de jogo

        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "SOSGB.DevBridge" };
        _thread.Start();
        Plugin.Logger.LogWarning($"[DevBridge] ATIVA em 127.0.0.1:{port} (só desenvolvimento, D20).");
    }

    private static void AcceptLoop()
    {
        while (true)
        {
            try
            {
                var client = _listener.AcceptTcpClient();
                new Thread(() => HandleClient(client)) { IsBackground = true }.Start();
            }
            catch (Exception e)
            {
                Plugin.Logger.LogError($"[DevBridge] accept: {e.Message}");
                Thread.Sleep(1000);
            }
        }
    }

    private static void HandleClient(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = new Pending { Line = line.Trim() };
                Queue.Enqueue(p);
                // A thread principal pode estar ocupada carregando; 60 s cobre trocas de cena.
                writer.WriteLine(p.Done.Wait(TimeSpan.FromSeconds(60))
                    ? p.Response
                    : Json(new { ok = false, error = "timeout esperando a thread principal" }));
            }
        }
    }

    // ---------- thread principal ----------

    private void Update()
    {
        // Reforça a cada frame caso algo do jogo/engine volte a desligar (D20).
        if (!Application.runInBackground) Application.runInBackground = true;

        while (Queue.TryDequeue(out var p))
        {
            try { p.Response = Execute(p.Line); }
            catch (Exception e) { p.Response = Json(new { ok = false, error = e.ToString() }); }
            p.Done.Set();
        }

        if (Time.unscaledTime >= _nextStateLog)
        {
            _nextStateLog = Time.unscaledTime + Plugin.StateLogInterval.Value;
            var snap = GameState.Snapshot();
            var evs = new List<string>();
            while (Events.TryDequeue(out var ev)) evs.Add(ev);
            if (evs.Count > 0) snap["events"] = evs;
            AppendStateLog(snap);
        }

        TryAutoLoad();
    }

    private void TryAutoLoad()
    {
        var slot = Plugin.AutoLoadSlot.Value;
        if (_autoLoadDone || slot < 0 || !SceneManager.IsInstance) return;
        if (SceneManager.Instance.CurrentSceneMode != SceneMode.Title) { _titleSince = -1f; return; }

        // Espera a tela de título assentar e os dados de save terem sido lidos.
        if (_titleSince < 0) _titleSince = Time.unscaledTime;
        if (Time.unscaledTime - _titleSince < 3f) return;
        if (!SaveLoadManager.IsInstance || !SaveLoadManager.Instance.IsLoadedSaveDataAll) return;

        _autoLoadDone = true;
        Plugin.Logger.LogInfo($"[DevBridge] auto-load do slot {slot}");
        LoadSlot(slot);
    }

    // ---------- comandos ----------

    private static string Execute(string line)
    {
        var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var cmd = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : "";

        switch (cmd)
        {
            case "ping":
                return Json(new { ok = true, pong = true, version = Plugin.Version });

            case "help":
                return Json(new
                {
                    ok = true,
                    commands = new[]
                    {
                        "ping", "state", "pause", "resume", "addmoney <n>", "load <slot 0-2>",
                        "screenshot [nome]", "menu <UILoadKey>", "closemenu", "decodesave <caminho .sav>", "like get|add <npc> [pts]", "ctx save|load|json <nome>", "log <texto>", "quit",
                    },
                });

            case "state":
                return Json(new { ok = true, state = GameState.Snapshot() });

            case "pause":
                DateManager.Instance.Pause();
                return Json(new { ok = true, clockRunning = DateManager.Instance.IsPlay() });

            case "resume":
                DateManager.Instance.Resume();
                return Json(new { ok = true, clockRunning = DateManager.Instance.IsPlay() });

            case "addmoney":
                var before = ExternalData.Instance.UserInfo.PlayerMoney;
                GameController.Instance.AddMoney(uint.Parse(arg), true);
                return Json(new { ok = true, before, after = ExternalData.Instance.UserInfo.PlayerMoney });

            case "load":
                LoadSlot(int.Parse(arg));
                return Json(new { ok = true, started = true, note = "acompanhe com 'state' ou no sosgb-state.jsonl" });

            case "screenshot":
                var name = string.IsNullOrWhiteSpace(arg) ? DateTime.Now.ToString("yyyyMMdd-HHmmss") : arg;
                var path = Path.Combine(_screenshotDir, name + ".png");
                // ScreenCapture.CaptureScreenshot(string) quebra no Il2CppInterop (MissingMethodException
                // no marshalling da string); por isso capturamos como textura no fim do frame.
                Instance.StartCoroutine(CaptureAtEndOfFrame(path).WrapToIl2Cpp());
                // O arquivo é gravado no fim do frame; o cliente deve esperar ~1 s antes de abrir.
                return Json(new { ok = true, path });

            case "menu":
                // Abre um menu do jogo pela chave UILoadKey (ex.: 100 = Menu, 101 = BagMenu, 122 = Calendar, 123 = Map).
                var ui = UnityEngine.Object.FindObjectOfType<UIManager>();
                ui.OpenMenu((UILoadKey)int.Parse(arg), null, false);
                return Json(new { ok = true, opened = ((UILoadKey)int.Parse(arg)).ToString() });

            case "closemenu":
                UnityEngine.Object.FindObjectOfType<UIManager>().CloseMenu(null, null, false, null, false);
                return Json(new { ok = true });

            case "like":
                return LikeCommand(arg);

            case "ctx":
                return CtxCommand(arg);

            case "decodesave":
                return DecodeSave(arg);

            case "log":
                Plugin.Logger.LogInfo($"[DevBridge] {arg}");
                Events.Enqueue(arg);
                return Json(new { ok = true });

            case "quit":
                Application.Quit();
                return Json(new { ok = true });

            default:
                return Json(new { ok = false, error = $"comando desconhecido: {cmd} (use 'help')" });
        }
    }

    /// <summary>
    /// Carrega um slot e entra na cena Game. Replica o que a tela de save/load faz
    /// (SaveLoadManager.LoadSlot → SceneManager.LoadScene(Game)); validar em runtime.
    /// </summary>
    private static void LoadSlot(int slot)
    {
        Events.Enqueue($"load:{slot}:iniciado");
        SaveLoadManager.Instance.LoadSlot(slot, (Action<SaveLoadResult>)(result =>
        {
            Plugin.Logger.LogInfo($"[DevBridge] LoadSlot({slot}) = {result}");
            Events.Enqueue($"load:{slot}:{result}");
            if (result == SaveLoadResult.Succeess)
                SceneManager.Instance.LoadScene(SceneMode.Game, null, null);
        }));
    }

    // Tarefa 1.3: inspecionar e alterar a amizade.
    private static string LikeCommand(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lm = LikeabilityManager.Instance;
        var id = uint.Parse(a[1]);
        if (a[0] == "add")
        {
            lm.AddPoint(id, float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture), out var result);
        }
        return Json(new
        {
            ok = true,
            npc = id,
            point = lm.GetPoint(id),
            level = lm.GetLevel(id, false),
            talkCount = lm.GetTalkCount(id),
            partner = lm.PartnerCharaId,
            fiance = lm.FianceCharaId,
        });
    }

    // Tarefa 1.3: troca de contexto (ver PlayerContext).
    private static string CtxCommand(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (a[0])
        {
            case "save":
                PlayerContext.SaveLikeability(a[1]);
                return Json(new { ok = true, saved = a[1], contexts = PlayerContext.Names });
            case "load":
                PlayerContext.LoadLikeability(a[1]);
                return Json(new { ok = true, loaded = a[1] });
            case "json":
                var json = PlayerContext.GetJson(a[1]);
                return Json(new { ok = true, length = json.Length, head = json.Substring(0, Math.Min(400, json.Length)) });
            default:
                return Json(new { ok = false, error = "use: ctx save|load|json <nome>" });
        }
    }

    /// <summary>
    /// Tarefa 1.2: tenta decodificar um .sav com a própria rotina do jogo (BokuMono.Utility.Obfuscation)
    /// e grava o resultado em BepInEx/savedump/ (fora do repositório: são dados do usuário).
    /// </summary>
    private static string DecodeSave(string path)
    {
        var bytes = File.ReadAllBytes(path.Trim('"'));
        var outDir = Path.Combine(Paths.BepInExRootPath, "savedump");
        Directory.CreateDirectory(outDir);
        var results = new Dictionary<string, object>();
        foreach (var mode in new[] { BokuMono.Utility.Obfuscation.Deflate.Original, BokuMono.Utility.Obfuscation.Deflate.General })
        {
            try
            {
                var text = BokuMono.Utility.Obfuscation.Decode(bytes, mode);
                var outPath = Path.Combine(outDir, Path.GetFileNameWithoutExtension(path.Trim('"')) + $".{mode}.txt");
                File.WriteAllText(outPath, text ?? "");
                results[mode.ToString()] = new { length = text?.Length ?? 0, head = text == null ? null : text.Substring(0, Math.Min(300, text.Length)), outPath };
            }
            catch (Exception e)
            {
                results[mode.ToString()] = new { error = e.GetType().Name + ": " + e.Message.Split('\n')[0] };
            }
        }
        return Json(new { ok = true, size = bytes.Length, results });
    }

    // ---------- util ----------

    private static IEnumerator CaptureAtEndOfFrame(string path)
    {
        yield return new WaitForEndOfFrame();
        var tex = ScreenCapture.CaptureScreenshotAsTexture();
        try
        {
            // ImageConversion.EncodeToPNG também quebra no interop (BlittableArrayWrapper.Unmarshal),
            // então lemos os pixels crus e codificamos o PNG em C# (PngWriter).
            Color32[] px = tex.GetPixels32();
            PngWriter.Write(path, tex.width, tex.height, px);
            Events.Enqueue($"screenshot:{Path.GetFileName(path)}");
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[DevBridge] screenshot: {e}");
        }
        finally
        {
            Destroy(tex);
        }
    }

    private static void AppendStateLog(Dictionary<string, object> snap)
    {
        try { File.AppendAllText(_stateLogPath, Json(snap) + "\n"); }
        catch (Exception e) { Plugin.Logger.LogWarning($"[DevBridge] state log: {e.Message}"); }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string Json(object o) => JsonSerializer.Serialize(o, JsonOpts);
}
