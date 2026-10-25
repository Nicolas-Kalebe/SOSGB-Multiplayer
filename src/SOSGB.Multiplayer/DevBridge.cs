// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d20.
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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

    internal static DevBridge Instance;

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
        // Arquivos separados por instância quando há mais de uma no PC (tarefa 1.6).
        _stateLogPath = Path.Combine(Paths.BepInExRootPath, $"sosgb-state{Plugin.InstanceTag}.jsonl");
        _screenshotDir = Path.Combine(Paths.BepInExRootPath, "screenshots" + Plugin.InstanceTag);
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
        var slot = Plugin.EffectiveAutoLoadSlot;
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
                return Json(new { ok = true, pong = true, version = Plugin.Version, pid = System.Diagnostics.Process.GetCurrentProcess().Id });

            case "help":
                return Json(new
                {
                    ok = true,
                    commands = new[]
                    {
                        "ping", "state", "pause", "resume", "addhours <h>", "sleep", "nextday", "save", "addmoney <n>", "load <slot 0-2>",
                        "screenshot [nome]", "menu <UILoadKey>", "closemenu", "decodesave <caminho .sav>", "like get|add <npc> [pts]", "ctx save|load <nome>", "ctx json <nome> [parte]", "bag [take <slot> <qtd>]", "flag get|set <id> [valor]", "world capture|load|info", "net host [porta] | join <ip> [porta] | status | leave", "warp <dx> <dz>", "field <FieldMasterId>", "log <texto>", "quit",
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

            case "addhours":
                // addhours <h> — adianta o relógio local (teste da correção de hora, 3.2)
                DateManager.Instance.AddHours(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                return Json(new { ok = true, date = GameState.Snapshot()["date"] });

            case "sleep":
                // sleep — chama PlayerCharacter.ActionSleep. NÃO CONFIÁVEL: fora do fluxo da cama o personagem pode
                // ficar preso andando até a cama (ForceCleanMove). Para testar dormir, use scripts/bed.sh (D33).
                GameController.Instance.GetPlayer().ActionSleep();
                return Json(new { ok = true });

            case "act":
                // act — usa a ferramenta/item da mão (PlayerCharacter.Action, o mesmo do clique). Testes da Fase 5:
                // os cliques sintéticos não chegam ao sistema de entrada do jogo.
                GameController.Instance.GetPlayer().Action();
                return Json(new { ok = true });

            case "toolmaster":
            {
                // toolmaster <de> <até> — entradas da tabela de ferramentas e se o ID existe como ITEM (investigação D30)
                var tm = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                uint from = uint.Parse(tm[0]), to = uint.Parse(tm[1]);
                var rows = new List<string>();
                var mdmT = MasterDataManager.Instance;
                foreach (var t in mdmT.ToolMaster.list)
                {
                    if (t.Id < from || t.Id > to) continue;
                    var isItem = mdmT.ItemMaster.TryGetData(t.Id, out var im);
                    rows.Add($"{t.Id}: nível {t.ToolLevel} cat {t.Category} sobe→{t.UpgradeToolId} preço {t.Price} item={(isItem ? "sim" : "NÃO")}");
                }
                return Json(new { ok = true, rows });
            }

            case "equip":
            {
                // equip <TipoDeFerramenta|idDoItem> — põe a ferramenta na mão (testes da Fase 5). Ex.: equip Hoe
                uint toolId;
                if (!uint.TryParse(arg.Trim(), out toolId))
                {
                    var tt = Enum.Parse<BokuMono.Data.ToolType>(arg.Trim(), true);
                    var item = InventoryManager.Instance.BagToolStorage.GetToolItem(tt);
                    if (item == null) return Json(new { ok = false, error = $"sem ferramenta do tipo {tt}" });
                    toolId = item.ItemId;
                }
                ToolManager.Instance.ChangeTool(toolId, true);
                return Json(new { ok = true, equipped = toolId, current = ToolManager.Instance.CurrentToolId, water = ToolManager.Instance.WateringPotInnerCapacity });
            }

            case "tool":
                // tool [quadros] — usa a ferramenta da mão pelo caminho real do jogo (injeta a intenção, ver InputInjector)
                Patches.InputInjector.PressTool(string.IsNullOrWhiteSpace(arg) ? 3 : int.Parse(arg));
                return Json(new { ok = true });

            case "wakeup":
                // wakeup [wake|flag|both] — levanta da cama enquanto espera os outros (D34)
                return Json(new { ok = true, result = Net.DaySync.WakeUp(string.IsNullOrWhiteSpace(arg) ? "both" : arg.Trim()) });

            case "pfix":
                // pfix <sleep|move|wake|wait|resume|init> — mexe numa marca de estado (diagnóstico D34)
                return Json(new { ok = true, result = Net.DaySync.Fix(arg.Trim()), pstate = Net.DaySync.PlayerState() });

            case "pstate":
                // pstate — estado de sono/controle do jogador local (diagnóstico da D34)
                return Json(new { ok = true, pstate = Net.DaySync.PlayerState() });

            case "nextday":
                // nextday — dispara a virada do dia do jogo (GameController.StartNextDay), tarefa 3.5
                GameController.Instance.StartNextDay();
                return Json(new { ok = true });

            case "recovery":
                // recovery — força um snapshot de recuperação agora (teste da 3.7)
                Net.PlayerProfiles.ForceRecovery();
                return Json(new { ok = true });

            case "save":
                // save — pede um save "manual" ao jogo (teste da regra do D23, tarefa 3.7)
                SaveLoadManager.Instance.Save(ExternalData.Instance.UserInfo.Slot, (FieldMasterId)FieldManager.Instance.CurrentFieldId,
                    (Action<int, SaveLoadResult>)((s, r) => PushEvent($"save-manual:{s}:{r}")));
                return Json(new { ok = true, started = true });

            case "addmoney":
                var before = ExternalData.Instance.UserInfo.PlayerMoney;
                GameController.Instance.AddMoney(uint.Parse(arg), true);
                return Json(new { ok = true, before, after = ExternalData.Instance.UserInfo.PlayerMoney });

            case "storage":
            {
                // storage | storage put <itemId> <qtd> | storage take <slot> <qtd> — armazém da casa (4.2)
                var hs = InventoryManager.Instance.HouseStorage;
                var sa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (sa.Length >= 3 && sa[0] == "put") hs.PutIn(BokuMono.Data.ItemData.Create(uint.Parse(sa[1]), int.Parse(sa[2])), out _, false);
                if (sa.Length >= 3 && sa[0] == "take") hs.PutOut(int.Parse(sa[1]), int.Parse(sa[2]));
                var list = new List<string>();
                var hd = hs.ItemDatas;
                for (var i = 0; i < hd.Length; i++) if (hd[i] != null && hd[i].ItemId != 0) list.Add($"{i}:{hd[i].ItemId}x{hd[i].Stack}");
                return Json(new { ok = true, count = list.Count, items = list.Take(12), status = Net.StorageSync.Status() });
            }

            case "mail":
            {
                // mail | mail add <letterId> | mail take — correio (4.6): carta nova na caixa / pegar as cartas
                var lm = LetterManager.Instance;
                var ma = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (ma.Length >= 2 && ma[0] == "add") lm.AddPostSimple(uint.Parse(ma[1]));
                if (ma.Length >= 1 && ma[0] == "take") lm.PutInBag();
                if (ma.Length >= 1 && ma[0] == "json") return Json(new { ok = true, debug = Net.LetterSync.Debug() });
                return Json(new { ok = true, mail = Net.LetterSync.Status() });
            }

            case "farm":
            {
                // farm | farm dump <tipo> [n] — objetos do mapa atual por tipo / dados de alguns (investigação da Fase 5)
                if (arg == "cap") return Json(new { ok = true, cap = FarmLab.Cap() });
                if (arg == "apply") return Json(new { ok = true, apply = FarmLab.ApplyCaptured() });
                if (arg.StartsWith("saved ")) return Json(new { ok = true, saved = FarmLab.Saved(uint.Parse(arg.Substring(6))) });
                if (arg == "residents") return Json(new { ok = true, residents = FarmLab.Residents() });
                if (arg == "resident") return Json(new { ok = true, resident = FarmLab.ApplyResident() });
                if (arg == "install") return Json(new { ok = true, install = FarmLab.InstallCaptured() });
                if (arg.StartsWith("bench")) return Json(new { ok = true, bench = FarmLab.Bench() });
                if (arg.StartsWith("snap")) return Json(new { ok = true, snap = FarmLab.Snapshot(arg.Length > 5 ? arg.Substring(5).Trim() : null) });
                if (arg.StartsWith("near")) return Json(new { ok = true, near = FarmLab.Near(arg.Length > 5 ? int.Parse(arg.Substring(5)) : 8) });
                if (arg.StartsWith("rm ")) return Json(new { ok = true, result = FarmLab.Remove(arg.Substring(3).Trim()) });
                if (arg.StartsWith("mk "))
                {
                    // farm mk <Tipo> <json> — cria uma entidade a partir dos dados (teste da Fase 5)
                    var sp = arg.IndexOf(' ', 3);
                    var mkType = Enum.Parse<FieldDynamicObjectType>(arg.Substring(3, sp - 3));
                    return Json(new { ok = true, result = FarmLab.Create(mkType, arg.Substring(sp + 1)) });
                }
                if (arg.StartsWith("re ")) return Json(new { ok = true, result = FarmLab.Recreate(arg.Substring(3).Trim()) });
                if (arg.StartsWith("data ")) { var en = FarmLab.Cache.GetLayerObject(arg.Substring(5).Trim()); return Json(new { ok = true, json = en != null ? FarmLab.DataJson(en) : "não achei" }); }
                var fc = FieldManager.Instance.CurrentFieldCache;
                var counts = new Dictionary<string, int>();
                var samples = new List<string>();
                var farmArgs = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var want = farmArgs.Length >= 2 && farmArgs[0] == "dump" ? farmArgs[1] : null;
                var n = farmArgs.Length >= 3 ? int.Parse(farmArgs[2]) : 2;
                foreach (var kv in fc.LayerObjects)
                    foreach (var e in kv.Value)
                    {
                        if (e == null) continue;
                        var t = e.FieldObjectType.ToString();
                        counts[t] = counts.TryGetValue(t, out var c0) ? c0 + 1 : 1;
                        if (want != null && t == want && samples.Count < n)
                            samples.Add(StringExtensions.ToJson(e.ToData<FieldLayerObjectEntityData>()));
                    }
                return Json(new { ok = true, field = FieldManager.Instance.CurrentFieldId, layers = fc.LayerObjects.Count, counts, samples });
            }

            case "spendmoney":
                // spendmoney <n> — gasta pelo caminho do jogo (SpendMoney), teste da carteira compartilhada (4.1)
                var beforeSpend = ExternalData.Instance.UserInfo.PlayerMoney;
                GameController.Instance.SpendMoney(uint.Parse(arg), true, null);
                return Json(new { ok = true, before = beforeSpend, after = ExternalData.Instance.UserInfo.PlayerMoney });

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

            case "net":
                // net host [porta] | net join <ip> [porta] | net status | net leave — tarefa 2.1
                var na = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                switch (na.Length > 0 ? na[0] : "status")
                {
                    case "host": Net.NetSession.Host(na.Length > 1 ? int.Parse(na[1]) : Plugin.NetPort.Value); break;
                    case "join": Net.NetSession.Join(na[1], na.Length > 2 ? int.Parse(na[2]) : Plugin.NetPort.Value); break;
                    case "leave": Net.NetSession.Stop(); break;
                }
                return Json(new { ok = true, net = Net.NetSession.Status(), remotes = Net.PlayerSync.Status(), clock = Net.ClockSync.Status(), day = Net.DaySync.Status(), profiles = Net.PlayerProfiles.Status(), appearance = Net.AppearanceSync.Status(), money = Net.MoneySync.Status(), storage = Net.StorageSync.Status(), mail = Net.LetterSync.Status(), farm = Net.FarmSync.Status() });

            case "avatarinfo":
                // avatarinfo — peças de aparência do jogador local + hierarquia do modelo (investigação da aparência)
                return Json(new { ok = true, info = AvatarInfoDump() });

            case "applab":
                // applab run | applab report — experimento de aparência (AppearanceLab)
                if (arg.StartsWith("run"))
                {
                    var local = AvatarMakeManager.Instance.PlayerAvatarInfos;
                    var variant = AppearanceLab.Variant(local);
                    AppearanceLab.Run(Instance, variant);
                    return Json(new { ok = true, started = true, local = AppearanceLab.Serialize(local), variant });
                }
                if (arg.StartsWith("probe "))
                {
                    // applab probe <cat>:<id>,... [none|detail|costume] — sonda do travamento (D29)
                    var pa = arg.Substring(6).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    AppearanceLab.Probe(Instance, pa[0], pa.Length > 1 ? pa[1] : "none");
                    return Json(new { ok = true, started = true, local = AppearanceLab.Serialize(AvatarMakeManager.Instance.PlayerAvatarInfos) });
                }
                if (arg.StartsWith("motion "))
                {
                    // applab motion <0|1> — troca só as animações de gênero (D29)
                    AppearanceLab.Motion(Instance, int.Parse(arg.Substring(7)));
                    return Json(new { ok = true, started = true });
                }
                if (arg.StartsWith("make"))
                {
                    // applab make [<cat>:<id>,...] — monta num modelo separado (tela de customização), D29
                    AppearanceLab.Make(Instance, arg.Length > 5 ? arg.Substring(5) : "");
                    return Json(new { ok = true, started = true });
                }
                if (arg.StartsWith("build "))
                {
                    // applab build <cat>:<id>,... — "jogador falso": testa o caminho do avatar remoto (D32)
                    AppearanceLab.FakeRemote(arg.Substring(6));
                    return Json(new { ok = true, started = true });
                }
                if (arg.StartsWith("cat "))
                {
                    // applab cat <categoria> — peças disponíveis numa categoria (ids, partes, motion)
                    var catN = int.Parse(arg.Substring(4));
                    var found = new List<string>();
                    foreach (var m in MasterDataManager.Instance.AvatarMakeMasterData)
                        if (m.Category == catN) found.Add($"{m.Id}:{m.PartsId}:{m.MotionCategory}:dlc{m.DlcId}");
                    return Json(new { ok = true, count = found.Count, items = found.Take(40) });
                }
                if (arg.StartsWith("set "))
                {
                    // applab set <cat>:<id>,... — aplica no jogador local a aparência atual com essas peças trocadas
                    var curParts = AppearanceLab.Serialize(AvatarMakeManager.Instance.PlayerAvatarInfos).Split(',').ToList();
                    foreach (var kv in arg.Substring(4).Split(','))
                    {
                        var c = kv.Split(':')[0];
                        var idx = curParts.FindIndex(x => x.StartsWith(c + ":"));
                        if (idx >= 0) curParts[idx] = kv; else curParts.Add(kv);
                    }
                    Net.AppearanceSync.ApplyToLocal(string.Join(",", curParts));
                    return Json(new { ok = true, applying = string.Join(",", curParts) });
                }
                if (arg.StartsWith("apply"))
                {
                    // applab apply — aplica uma aparência variante no jogador local (testes da D28)
                    var v = AppearanceLab.Variant(AvatarMakeManager.Instance.PlayerAvatarInfos);
                    Net.AppearanceSync.ApplyToLocal(v);
                    return Json(new { ok = true, applying = v });
                }
                return Json(new { ok = true, report = AppearanceLab.LastReport });

            case "fieldname":
                // fieldname <id> — nome traduzido de um mapa (lista de jogadores)
                return Json(new { ok = true, name = Net.Locations.Name(uint.Parse(arg)) });

            case "notify":
                // notify <texto> — mostra um aviso no sistema de avisos do jogo (D26)
                GameNotify.Show(arg);
                return Json(new { ok = true });

            case "kick":
                // kick <1|2|3> — testa as formas de ativar a janela do jogo (D25)
                return Json(new { ok = true, result = StartupGuard.Activate(int.Parse(arg)) });

            case "anim":
                return Json(new { ok = true, anim = Net.PlayerSync.AnimDiag() });

            case "warp":
                // warp <dx> <dz> — teleporta o jogador local (teste de sincronização, 2.4).
                // Mexer no transform direto não adianta: o jogo devolve o modelo para o lugar.
                // Usamos o teleporte do próprio jogo (SetPlayerClosestGroundPosition).
                var wa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var cur = GameController.Instance.GetPlayerTransform().position;
                var dest = cur + new Vector3(float.Parse(wa[0], inv), 0, float.Parse(wa[1], inv));
                GameController.Instance.GetPlayer().SetPlayerClosestGroundPosition(dest, new Il2CppSystem.Nullable<Vector3>(), 10f);
                var now = GameController.Instance.GetPlayerTransform().position;
                return Json(new { ok = true, pos = new[] { now.x, now.y, now.z } });

            case "field":
                // field <FieldMasterId> — troca de mapa (teste da 2.5). Ex.: 100010 = casa.
                GameController.Instance.ChangeField((FieldMasterId)uint.Parse(arg), "", "", false, true, false, null, null, null, false);
                return Json(new { ok = true, changing = uint.Parse(arg) });

            case "world":
                // world capture | world load — tarefa 1.5 (ver WorldSnapshot). Slot de teste = 2.
                if (arg.StartsWith("capture"))
                {
                    WorldSnapshot.Capture(2, okCap => PushEvent($"worldcapture:{okCap}:{WorldSnapshot.Captured?.Length ?? 0}"));
                    return Json(new { ok = true, started = "capture", note = "acompanhe com 'world info'" });
                }
                if (arg.StartsWith("load"))
                {
                    WorldSnapshot.Load(2);
                    return Json(new { ok = true, started = "load" });
                }
                return Json(new { ok = true, capturedBytes = WorldSnapshot.Captured?.Length ?? 0, dump = WorldSnapshot.DumpPath });

            case "flag":
                // flag get|set <id> [valor] — flags de evento (parte "eventflags" do contexto)
                var fa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var efm = EventFlagManager.Instance;
                var fid = uint.Parse(fa[1]);
                if (fa[0] == "set") efm.SetEventFlag(fid, int.Parse(fa[2]), 0);
                return Json(new { ok = true, id = fid, value = efm.GetEventFlag(fid) });

            case "bag":
                return BagCommand(arg);

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
            {
                // A tela de save/load define o slot atual; como pulamos a tela, definimos aqui.
                // Sem isso o save automático da virada do dia vai para o slot 0 (o save principal!).
                ExternalData.Instance.UserInfo.Slot = slot;
                SceneManager.Instance.LoadScene(SceneMode.Game, null, null);
            }
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

    // Tarefas 1.3/1.4: troca de contexto (ver PlayerContext).
    private static string CtxCommand(string arg)
    {
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (a[0])
        {
            case "save":
                return Json(new { ok = true, saved = a[1], parts = PlayerContext.Save(a[1]) });
            case "load":
                return Json(new { ok = true, loaded = a[1], parts = PlayerContext.Load(a[1]) });
            case "json":
                var json = PlayerContext.GetJson(a[1], a.Length > 2 ? a[2] : "likeability");
                return Json(new { ok = true, length = json.Length, head = json.Substring(0, Math.Min(400, json.Length)) });
            case "fresh":
                // ctx fresh — aplica no jogador local o perfil "do zero" de um convidado novo (D30), sem rede.
                // Teste com uma instância só; desfazer com ctx save/load antes e depois.
                var names = new List<string>(PlayerContext.ProfileParts);
                names.Remove("appearance");
                var parts = PlayerContext.CaptureParts(names);
                var fresh = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(Net.FreshProfile.Make(parts, null));
                var tools = InventoryManager.Instance.BagToolStorage.ItemDatas;
                var before = new List<uint>();
                for (var i = 0; i < tools.Length; i++) if (tools[i] != null && tools[i].ItemId != 0) before.Add(tools[i].ItemId);
                var applied = PlayerContext.ApplyParts(fresh);
                tools = InventoryManager.Instance.BagToolStorage.ItemDatas;
                var after = new List<uint>();
                for (var i = 0; i < tools.Length; i++) if (tools[i] != null && tools[i].ItemId != 0) after.Add(tools[i].ItemId);
                var heads = new Dictionary<string, string>();
                foreach (var kv in fresh) heads[kv.Key] = kv.Value.Substring(0, Math.Min(300, kv.Value.Length));
                return Json(new { ok = true, applied, toolsBefore = before, toolsAfter = after, heads });
            default:
                return Json(new { ok = false, error = "use: ctx save|load <nome> | ctx json <nome> [parte] | ctx fresh", parts = PlayerContext.PartNames });
        }
    }

    // Tarefa 1.4: ver e mexer na mochila.
    private static string BagCommand(string arg)
    {
        var bag = InventoryManager.Instance.BagItemStorage;
        var a = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length > 0 && a[0] == "take") // tira <qtd> do slot <n>
            bag.PutOut(int.Parse(a[1]), int.Parse(a[2]));
        var items = new List<string>();
        var datas = bag.ItemDatas;
        for (var i = 0; i < datas.Length; i++)
        {
            var d = datas[i];
            if (d != null && d.ItemId != 0) items.Add($"{i}:{d.ItemId}x{d.Stack}");
        }
        return Json(new { ok = true, count = items.Count, items });
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

    private static object AvatarInfoDump()
    {
        var ui = ExternalData.Instance.UserInfo;
        var infos = new List<object>();
        foreach (var ai in ui.AvatarInfos)
            infos.Add(new { ai.MasterId, cat = ai.Category.ToString(), ai.MeshName, color = SafeInt(() => ai.ColorIndex), hair = SafeStr(() => ai.HairId) });
        var model = GameController.Instance.GetPlayer().Model;
        var nodes = new List<string>();
        void Walk(Transform t, int depth)
        {
            if (depth > 2) return;
            var r = t.GetComponent<Renderer>();
            nodes.Add(new string(' ', depth * 2) + t.name + (r != null ? $" [{r.GetIl2CppType().Name}{(r.enabled && t.gameObject.activeInHierarchy ? "" : " OFF")}]" : "") + (t.gameObject.activeSelf ? "" : " (inativo)"));
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1);
        }
        Walk(model.transform, 0);
        return new { gender = ui.PlayerGender.ToString(), infos, nodes };
    }

    private static int SafeInt(Func<int> f) { try { return f(); } catch { return -1; } }
    private static string SafeStr(Func<string> f) { try { return f(); } catch { return null; } }

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
