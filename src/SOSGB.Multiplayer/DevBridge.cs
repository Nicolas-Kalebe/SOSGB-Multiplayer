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
    private static string _eventsBackup; // comando "events" (testes da D67)

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

    /// <summary>Saves de teste (world savefile/loadfile): na pasta do BepInEx, longe dos saves do usuário.</summary>
    private static string DevSavePath(string name) =>
        Path.Combine(BepInEx.Paths.BepInExRootPath, "devsaves", new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray()) + ".sav");

    private void TryAutoLoad()
    {
        var slot = Plugin.EffectiveAutoLoadSlot;
        var file = Environment.GetEnvironmentVariable("SOSGB_AUTOLOAD_FILE"); // save de teste (world savefile)
        if (!string.IsNullOrEmpty(file) && slot < 0) slot = 2;
        if (_autoLoadDone || slot < 0 || !SceneManager.IsInstance) return;
        if (SceneManager.Instance.CurrentSceneMode != SceneMode.Title) { _titleSince = -1f; return; }

        // Espera a tela de título assentar e os dados de save terem sido lidos.
        if (_titleSince < 0) _titleSince = Time.unscaledTime;
        if (Time.unscaledTime - _titleSince < 3f) return;
        if (!SaveLoadManager.IsInstance || !SaveLoadManager.Instance.IsLoadedSaveDataAll) return;

        _autoLoadDone = true;
        if (!string.IsNullOrEmpty(file) && File.Exists(DevSavePath(file)))
        {
            Plugin.Logger.LogInfo($"[DevBridge] auto-load do save de teste '{file}'");
            WorldSnapshot.SetCaptured(File.ReadAllBytes(DevSavePath(file)));
            WorldSnapshot.Load(2);
            return;
        }
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
                        "screenshot [nome]", "menu <UILoadKey>", "closemenu", "decodesave <caminho .sav>", "like get|add <npc> [pts]", "ctx save|load <nome>", "ctx json <nome> [parte]", "bag [take <slot> <qtd>]", "flag get|set <id> [valor]", "world capture|load|info", "net host [porta] | join <ip> [porta] | status | leave", "warp <dx> <dz>", "field <FieldMasterId>", "press <HumanActionInputFlags> [quadros]", "log <texto>", "quit",
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

            case "ui":
            {
                // ui | ui closedialog | ui closemessage — tela atual do jogo / fechar diálogo ou mensagem (testes)
                var uiM = UnityEngine.Object.FindObjectOfType<UIManager>();
                string U(Func<object> f) { try { return f()?.ToString(); } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                if (arg.Trim() == "closedialog") U(() => { uiM.CloseDialog(null, null, true, false, false); return 0; });
                if (arg.Trim() == "closemessage") U(() => { uiM.CloseMessage(null, null); return 0; });
                return Json(new { ok = true, current = U(() => uiM.CurrentUIKey), type = U(() => uiM.CurrentUIType), menu = U(() => uiM.UIMenuKey), isMenu = U(() => uiM.IsMenu) });
            }

            case "stamina":
            {
                // stamina | stamina max <n> | stamina buff <id> — vigor (máximo compartilhado) e bônus (individuais)
                var sa3 = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var gcS = GameController.Instance;
                if (sa3.Length >= 2 && sa3[0] == "max") gcS.PlayerMaxHP = float.Parse(sa3[1], System.Globalization.CultureInfo.InvariantCulture);
                if (sa3.Length >= 2 && sa3[0] == "buff") PlayerBuffManager.Instance.AddBuff(uint.Parse(sa3[1]), sa3.Length >= 3 ? float.Parse(sa3[2], System.Globalization.CultureInfo.InvariantCulture) : 0, null, (BuffActionAttr)6);
                if (sa3.Length >= 1 && sa3[0] == "ids")
                {
                    var ids = new List<uint>();
                    foreach (var bmd in MasterDataManager.Instance.BuffMaster.list) { ids.Add(bmd.Id); if (ids.Count >= 15) break; }
                    return Json(new { ok = true, ids });
                }
                var buffs = new List<string>();
                foreach (var bf in PlayerBuffManager.Instance.GetPlayerBuffs()) buffs.Add(StringExtensions.ToJson(bf));
                return Json(new { ok = true, hp = gcS.PlayerHP, max = gcS.PlayerMaxHP, buffs });
            }

            case "gates":
            {
                // gates — portões e áreas de transição da cena atual: destino e posição (para andar até o Bazaar)
                var list = new List<string>();
                foreach (var g in UnityEngine.Object.FindObjectsOfType<FieldGate>())
                {
                    string gDest; try { gDest = g.FieldMasterId.ToString(); } catch { gDest = "?"; }
                    var gp = g.transform.position;
                    list.Add($"portão → {gDest} em ({gp.x:0.0}, {gp.z:0.0})");
                }
                foreach (var t in UnityEngine.Object.FindObjectsOfType<FieldTransitionArea>())
                {
                    string tDest; try { tDest = t.FieldId.ToString(); } catch { tDest = "?"; }
                    var tp = t.transform.position;
                    list.Add($"área → {tDest} em ({tp.x:0.0}, {tp.z:0.0})");
                }
                var pp = GameController.Instance.GetPlayerTransform().position;
                return Json(new { ok = true, field = FieldManager.Instance.CurrentFieldId, player = $"({pp.x:0.0}, {pp.z:0.0})", gates = list });
            }

            case "itemhist":
            {
                // itemhist <id> | itemhist add <id> <qualidade> — histórico de itens/enciclopédia (D43)
                var ih = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (ih.Length >= 3 && ih[0] == "add") ItemHistoryManager.Instance.AddHistory(uint.Parse(ih[1]), int.Parse(ih[2]), 0);
                var hid = uint.Parse(ih[ih.Length >= 3 ? 1 : 0]);
                return Json(new { ok = true, id = hid, has = ItemHistoryManager.Instance.IsHistory(hid) });
            }

            case "bazaar":
            {
                // bazaar — estado do Bazaar e do relógio (investigação da D47)
                var bm = BazaarManager.IsInstance ? BazaarManager.Instance : null;
                if (bm != null && arg.Trim() == "open") bm.RequestOpenShopDialog();
                string T(Func<object> f) { try { return f()?.ToString(); } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                return Json(new
                {
                    ok = true,
                    now = DateManager.Instance.Now.ToString(),
                    clockUpdate = DateManager.Instance.IsUpdateEnabled,
                    exists = bm != null,
                    openDay = T(() => bm.IsOpenDay()), openTime = T(() => bm.IsOpenTime()), openField = T(() => bm.IsOpenField()),
                    canOpenShop = T(() => bm.IsEnableOpenMyShopTime()), inShop = T(() => bm.IsInShop), timeStop = T(() => bm.IsBazaarTimeStop),
                    period = T(() => bm.CurrentPeriod), openPeriod = T(() => bm.OpenPeriod), procedure = T(() => bm.CurrentProcedureMask),
                    sales = T(() => bm.ThisOnceSalePrice), visitors = T(() => bm.SellVisitorNum),
                });
            }

            case "marrydiag":
                // marrydiag on|off — registra quem consulta o casamento (diagnóstico da 6.5)
                Patches.MarriageDiagnosticsPatch.Enabled = arg.Trim() != "off";
                return Json(new { ok = true, enabled = Patches.MarriageDiagnosticsPatch.Enabled });

            case "romance":
            {
                // romance | romance fiance <npc> | romance check <npc> <item> — romance exclusivo (D44)
                var ra = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var lmR = LikeabilityManager.Instance;
                if (ra.Length >= 2 && ra[0] == "fiance") lmR.SetFiance(uint.Parse(ra[1]));
                if (ra.Length >= 2 && ra[0] == "reset") lmR.ResetPartner();
                if (ra.Length >= 2 && ra[0] == "partner") lmR.SetPartner(uint.Parse(ra[1]));
                object check = null;
                if (ra.Length >= 3 && ra[0] == "check") check = lmR.CheckMarryable(uint.Parse(ra[1]), uint.Parse(ra[2]), false);
                return Json(new { ok = true, fiance = lmR.FianceCharaId, partner = lmR.PartnerCharaId, check, status = Net.RomanceSync.Status() });
            }

            case "shared":
            {
                // shared | shared test | shared json <parte> | shared poke <parte> — estado compartilhado genérico (D42)
                var sa2 = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (sa2.Length >= 1 && sa2[0] == "test") return Json(new { ok = true, test = Net.SharedSync.SelfTest() });
                if (sa2.Length >= 5 && sa2[0] == "poke")
                {
                    // shared poke <parte> <guid> <campo> <valor> — muda um campo de um objeto como AÇÃO LOCAL (vai para os outros)
                    var root = System.Text.Json.Nodes.JsonNode.Parse(Net.SharedSync.CaptureJson(sa2[1]))!;
                    var found = false;
                    void Walk(System.Text.Json.Nodes.JsonNode n)
                    {
                        if (n is System.Text.Json.Nodes.JsonObject o)
                        {
                            if (o["Guid"]?.ToString() == sa2[2]) { o[sa2[3]] = int.TryParse(sa2[4], out var iv2) ? iv2 : sa2[4]; found = true; }
                            foreach (var kv in o.ToList()) Walk(kv.Value);
                        }
                        else if (n is System.Text.Json.Nodes.JsonArray arr) foreach (var e in arr) Walk(e);
                    }
                    Walk(root);
                    if (!found) return Json(new { ok = false, error = "guid não encontrado" });
                    return Json(new { ok = true, result = Net.SharedSync.ApplyPart(sa2[1], root.ToJsonString(), markSynced: false) });
                }
                // shared reapply <parte> — reaplica o próprio estado (teste de duplicação: comparar com "objcount" antes e depois)
                // shared set <parte> <caminho.com.pontos> <valor> — muda um valor do JSON da parte (índices de lista como
                // números) e aplica como se viesse de outro jogador; responde o valor lido de volta do jogo.
                if (sa2.Length >= 4 && sa2[0] == "set")
                {
                    var rootS = System.Text.Json.Nodes.JsonNode.Parse(Net.SharedSync.CaptureJson(sa2[1]))!;
                    System.Text.Json.Nodes.JsonNode Step(System.Text.Json.Nodes.JsonNode n, string k) =>
                        n is System.Text.Json.Nodes.JsonArray ar ? ar[int.Parse(k)]! : n[k]!;
                    var keysS = sa2[2].Split('.');
                    var parentS = rootS;
                    for (var i = 0; i < keysS.Length - 1; i++) parentS = Step(parentS, keysS[i]);
                    var lastS = keysS[^1];
                    System.Text.Json.Nodes.JsonNode valS = long.TryParse(sa2[3], out var lv) ? lv : sa2[3];
                    if (parentS is System.Text.Json.Nodes.JsonArray pa2) pa2[int.Parse(lastS)] = valS; else parentS[lastS] = valS;
                    var resS = Net.SharedSync.ApplyPart(sa2[1], rootS.ToJsonString(), markSynced: true);
                    var backS = System.Text.Json.Nodes.JsonNode.Parse(Net.SharedSync.CaptureJson(sa2[1]))!;
                    for (var i = 0; i < keysS.Length; i++) backS = Step(backS, keysS[i]);
                    return Json(new { ok = true, result = resS, agora = backS.ToJsonString() });
                }
                // shared roundtrip bazaar [raw] — JSON → SaveData (montado à mão; "raw" = JsonToObject puro do jogo) → JSON: o que se perde
                if (sa2.Length >= 2 && sa2[0] == "roundtrip" && sa2[1] == "bazaar")
                {
                    var js0 = Net.SharedSync.CaptureJson("bazaar");
                    var sd0 = sa2.Length >= 3 && sa2[2] == "raw" ? StringExtensions.JsonToObject<BazaarManager.BazaarSaveData>(js0) : Net.SharedSync.BuildBazaar(js0);
                    return Json(new { ok = true, nulo = sd0 == null, original = js0, volta = sd0 == null ? null : StringExtensions.ToJson(sd0) });
                }
                if (sa2.Length >= 2 && sa2[0] == "reapply") return Json(new { ok = true, result = Net.SharedSync.ApplyPart(sa2[1], Net.SharedSync.CaptureJson(sa2[1]), markSynced: true) });
                if (sa2.Length >= 2 && sa2[0] == "json") { var js = Net.SharedSync.CaptureJson(sa2[1]) ?? ""; return Json(new { ok = true, length = js.Length, head = js.Substring(0, Math.Min(900, js.Length)) }); }
                return Json(new { ok = true, status = Net.SharedSync.Status() });
            }

            case "jsoncmp":
            {
                // Tamanho do JSON de cada SaveData compartilhado: ToJson do jogo (Newtonsoft: campos/propriedades públicos)
                // × JsonUtility da Unity (campos [SerializeField] privados, formato do save). Unity maior = a parte perde dados.
                var objs = new Dictionary<string, Func<Il2CppSystem.Object>>
                {
                    ["windmill"] = () => WindmillCraftingManager.Instance.ToSaveData(),
                    ["beekeeping"] = () => BeekeepingManager.Instance.ToSaveData(),
                    ["mushroom"] = () => MushroomFarmingManager.Instance.ToSaveData(),
                    ["flowerpot"] = () => FlowerPotManager.Instance.ToSaveData(),
                    ["expansion"] = () => ExpansionManager.Instance.ToSaveData(),
                    ["awards"] = () => AwardManager.Instance.ToSaveData(),
                    ["basement"] = () => BasementManager.Instance.ToSaveData(),
                    ["bath"] = () => BathManager.Instance.ToSaveData(),
                    ["cooking"] = () => CookingManager.Instance.ToSaveData(),
                    ["coromission"] = () => CoroMissionManager.Instance.ToSaveData(),
                    ["fish"] = () => FishManager.Instance.ToSaveData(),
                    ["reinforce"] = () => ReinForceManager.Instance.ToSaveData(),
                    ["shop"] = () => ShopManager.Instance.ToSaveData(),
                    ["shopanimal"] = () => ShopAnimalInfoManager.Instance.ToSaveData(),
                    ["bazaar"] = () => BazaarManager.Instance.ToBazaarSaveData(),
                    ["bazaartrend"] = () => BazaarManager.Instance.ToTrendSaveData(),
                    ["bazaarcustom"] = () => BazaarManager.Instance.ToSaveData(),
                    ["festival"] = () => FestivalExecutor.Instance.ToSaveData(),
                    ["generalinfo"] = () => GeneralInfoManager.Instance.ToSaveData(),
                    ["pets"] = () => BokuMono.Animal.PetAnimalController.Instance.ToSaveData(),
                    ["horse"] = () => BokuMono.Animal.RideableAnimalController.Instance.ToSaveData(),
                };
                var only = arg.Trim();
                var res = new Dictionary<string, object>();
                foreach (var kv in objs)
                {
                    if (only.Length > 0 && kv.Key != only) continue;
                    try
                    {
                        var o = kv.Value();
                        var a = StringExtensions.ToJson(o);
                        var u = JsonUtility.ToJson(o);
                        res[kv.Key] = only.Length > 0 ? new { jogo = a, unity = u } : $"jogo={a.Length} unity={u.Length}";
                    }
                    catch (Exception e) { res[kv.Key] = "erro: " + e.Message.Split('\n')[0]; }
                }
                return Json(new { ok = true, res });
            }

            case "visitors":
            {
                // visitors [params] — clientes do Bazaar (NpcAgentList/NpcAgentPools): quem, onde, estado, animação (D55)
                var bmV = BazaarManager.Instance;
                string V(Func<object> f) { try { return f()?.ToString(); } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                object Agent(NPCBazaarVisitorAgent a)
                {
                    var anim = a.GetComponentInChildren<Animator>();
                    var p = a.transform.position;
                    object pars = null;
                    if (arg.Trim() == "params" && anim != null)
                    {
                        var pl = new List<string>();
                        foreach (var ap in anim.parameters) pl.Add($"{ap.name}:{ap.type}={(ap.type == AnimatorControllerParameterType.Float ? anim.GetFloat(ap.nameHash).ToString("0.00") : ap.type == AnimatorControllerParameterType.Bool ? anim.GetBool(ap.nameHash).ToString() : ap.type == AnimatorControllerParameterType.Int ? anim.GetInteger(ap.nameHash).ToString() : "trig")}");
                        pars = pl;
                    }
                    return new
                    {
                        name = a.name, active = a.gameObject.activeInHierarchy, spawned = a.IsSpawned,
                        visitor = V(() => a.VisitorMaster?.Id), chara = V(() => a.VisitorMaster?.CharacterId), preset = V(() => a.PresetId),
                        state = V(() => a.CurrentState), category = V(() => a.Category),
                        pos = $"{p.x:0.0},{p.y:0.0},{p.z:0.0}", rotY = a.transform.eulerAngles.y.ToString("0"),
                        animState = anim == null ? null : V(() => anim.GetCurrentAnimatorStateInfo(0).shortNameHash), pars,
                        emote = V(() => { var em = a.EmoteSign; return em == null ? "null" : $"{(em.IsShowing ? "visível" : "-")} item={em.itemId} n={em.itemCount?.buyNum} balcão={em.counterIndex}"; }),
                    };
                }
                var list = new List<object>();
                if (bmV?.NpcAgentList != null) foreach (var a in bmV.NpcAgentList) list.Add(Agent(a));
                var pools = 0;
                if (bmV?.NpcAgentPools != null) pools = bmV.NpcAgentPools.Count;
                var balloons = new List<string>();
                var bnm = UnityEngine.Object.FindObjectOfType<BazaarNpcBalloonManager>();
                if (bnm?.NpcBalloon != null)
                    foreach (var bl in bnm.NpcBalloon) if (bl != null) balloons.Add($"{(bl.IsShowing ? "visível" : "-")} item={bl.itemId} n={bl.itemCount?.buyNum} balcão={bl.counterIndex}");
                return Json(new { ok = true, count = list.Count, pools, agents = list, balloons });
            }

            case "vtest":
            {
                // vtest pool <id> | pop <id> | puppet <id> <x> <z> [velocidade] [full|soft] | free <id> — clientes do Bazaar (D55)
                var vt = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var bmT = BazaarManager.Instance;
                NPCBazaarVisitorAgent FindV(uint vid)
                {
                    foreach (var a0 in bmT.NpcAgentList) if (a0.VisitorMaster != null && a0.VisitorMaster.Id == vid) return a0;
                    return null;
                }
                var idT = uint.Parse(vt[1]);
                switch (vt[0])
                {
                    case "pool":
                        bmT.AddVisitorPool(idT, null);
                        return Json(new { ok = true, pools = bmT.NpcAgentPools?.Count, list = bmT.NpcAgentList?.Count });
                    case "pop":
                        bmT.PopVisitor(idT, (Il2CppSystem.Action<NPCBazaarVisitorAgent>)null);
                        return Json(new { ok = true, pools = bmT.NpcAgentPools?.Count, list = bmT.NpcAgentList?.Count });
                }
                var ag = FindV(idT);
                if (ag == null) return Json(new { ok = false, error = "cliente não está na lista" });
                var nav = ag.Agent;
                var animT = ag.GetComponentInChildren<Animator>();
                var rb = ag.GetComponent<Rigidbody>();
                if (vt[0] == "free")
                {
                    if (nav != null) nav.enabled = true;
                    if (rb != null) rb.isKinematic = false;
                    if (animT != null) animT.applyRootMotion = true;
                    ag.enabled = true;
                    ag.SetEnableUpdate(true);
                    return Json(new { ok = true });
                }
                var mode = vt.Length > 5 ? vt[5] : "full";
                ag.SetEnableUpdate(false);
                if (nav != null) nav.enabled = false;
                if (mode == "full")
                {
                    ag.enabled = false;
                    if (rb != null) { rb.velocity = Vector3.zero; rb.isKinematic = true; }
                    if (animT != null) animT.applyRootMotion = false;
                }
                var px = float.Parse(vt[2], System.Globalization.CultureInfo.InvariantCulture);
                var pz = float.Parse(vt[3], System.Globalization.CultureInfo.InvariantCulture);
                var spd = vt.Length > 4 ? float.Parse(vt[4], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                ag.transform.position = new Vector3(px, ag.transform.position.y, pz);
                if (animT != null) { animT.SetFloat("Speed", spd); animT.SetBool("Walk", spd > 0.01f); }
                var pp = ag.transform.position;
                return Json(new { ok = true, nav = nav != null, rb = rb != null, root = animT != null && animT.applyRootMotion, pos = $"{pp.x:0.0},{pp.y:0.0},{pp.z:0.0}", state = ag.CurrentState.ToString() });
            }

            case "shelves":
            {
                // shelves — barracas do Bazaar (BazaarShopShelf): ativas?, onde, pais inativos (D55: visual da barraca aberta)
                var bmS = BazaarManager.Instance;
                var res = new List<object>();
                var arr = bmS.ShopShelfs;
                if (arr != null)
                    for (var i = 0; i < arr.Length; i++)
                    {
                        var sh = arr[i];
                        if (sh == null) { res.Add(new { i, nulo = true }); continue; }
                        var chain = new List<string>();
                        for (var t = sh.transform; t != null; t = t.parent) chain.Add($"{t.name}{(t.gameObject.activeSelf ? "" : "(off)")}");
                        var p = sh.transform.position;
                        var items = new List<string>();
                        for (var k = 0; k < sh.GetShopItemLength(); k++) { var it = sh.GetShopItem(k)?.ItemInvData(); items.Add(it == null ? "-" : $"{it.ItemId}x{it.Stack}"); }
                        res.Add(new { i, active = sh.gameObject.activeInHierarchy, pos = $"{p.x:0.0},{p.y:0.0},{p.z:0.0}", chain = string.Join(" < ", chain), items });
                    }
                var named = new List<string>();
                foreach (var nm in new[] { "BazaarMyShopClose(Clone)", "BazaarOpenShopAreaCollider", "TentMotion_Root", "bazr_bld_001_01_colider" })
                {
                    var go = GameObject.Find(nm);
                    if (go == null) { named.Add(nm + ": não achado (ou inativo)"); continue; }
                    var chainN = new List<string>();
                    for (var t = go.transform; t != null; t = t.parent) chainN.Add(t.name);
                    var pn = go.transform.position;
                    named.Add($"{nm}: ativo={go.activeInHierarchy} pos={pn.x:0.0},{pn.z:0.0} filhos={go.transform.childCount} em {string.Join(" < ", chainN)}");
                }
                return Json(new { ok = true, current = bmS.GetCurrentShelfIndex(), shelves = res, named });
            }

            case "findobj":
            {
                // findobj <nome> [on|off] — acha objetos pelo nome, inclusive inativos (D55); on/off liga/desliga o 1º achado
                var fo = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var found = new List<string>();
                GameObject first = null;
                foreach (var tr in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (tr == null || tr.name != fo[0] || !tr.gameObject.scene.IsValid()) continue;
                    first ??= tr.gameObject;
                    var chainF = new List<string>();
                    for (var t = tr; t != null; t = t.parent) chainF.Add($"{t.name}{(t.gameObject.activeSelf ? "" : "(off)")}");
                    found.Add($"ativo={tr.gameObject.activeInHierarchy} filhos={tr.childCount} em {string.Join(" < ", chainF)}");
                    if (found.Count >= 10) break;
                }
                if (first != null && fo.Length > 1) first.SetActive(fo[1] == "on");
                return Json(new { ok = true, found });
            }

            case "bmcall":
            {
                // bmcall initshelf | setupfield — chama rotinas internas do BazaarManager (experimento D55)
                var bmC = BazaarManager.Instance;
                string R(Func<object> f) { try { return f()?.ToString() ?? "ok"; } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                var r0 = arg.Trim() switch
                {
                    "initshelf" => R(() => { bmC.InitShopShelfRoot(); return null; }),
                    "setupfield" => R(() => { bmC.SetupFieldRoot(out var isOpen); return "isOpenField=" + isOpen; }),
                    "custom" => R(() => { bmC.SetupPartsBuff(); return null; }),
                    "parts" => R(() => { UnityEngine.Object.FindObjectOfType<BazaarMyShop>().SetBazaarPartsModel((Il2CppSystem.Action)null); return null; }),
                    "initcreate" => R(() => { UnityEngine.Object.FindObjectOfType<BazaarMyShop>().InitCreateMyShop(); return null; }),
                    "tent on" => R(() => { UnityEngine.Object.FindObjectOfType<BazaarMyShop>().ShopShelfRoot.ChangeTent(true, false); return null; }),
                    "tent off" => R(() => { UnityEngine.Object.FindObjectOfType<BazaarMyShop>().ShopShelfRoot.ChangeTent(false, false); return null; }),
                    "rootshelves" => R(() => UnityEngine.Object.FindObjectOfType<BazaarMyShop>().ShopShelfRoot.ShopShelfs?.Length),
                    "close" => R(() => { bmC.CloseShopCallBack(); return null; }),
                    // deckinfo / deckrefresh: lista do mini deck (D56: o menu de itens da barraca aparecia vazio)
                    "deckinfo" => R(() =>
                    {
                        var d = InventoryManager.Instance.DeckStorage;
                        var bar = d.selectedBarItemDatas;
                        var barS = new List<string>();
                        if (bar != null) foreach (var it in bar) barS.Add(it == null ? "null" : $"{it.ItemId}x{it.Stack}#{it.DeckNumber}");
                        var nums = new List<string>();
                        foreach (var it in d.ItemDatas) if (it != null && it.ItemId != 0) nums.Add($"{it.ItemId}#{it.DeckNumber}");
                        return $"lista={d.ItemDataInDeckList?.Count} numeroSel={d.selectedItemDeckNumber} barra=[{string.Join(",", barS)}] itens=[{string.Join(",", nums)}] podeAbrir={bmC.CanOpenBazaarMiniDeck}";
                    }),
                    "deckrefresh" => R(() =>
                    {
                        var d = InventoryManager.Instance.DeckStorage;
                        var l = d.GetMiniDeckItemsData();
                        return $"GetMiniDeckItemsData={l?.Count} lista={d.ItemDataInDeckList?.Count}";
                    }),
                    "minireset" => R(() => { bmC.ResetMiniDeck(); return null; }),
                    // bell: o anel de atração do sino a partir do jogador local (o que o pedido de sino do ajudante faz no dono)
                    "bell" => R(() => { var mg = GameController.Instance.GetPlayer().RipperAtractionColliderManager; mg.RippleAtractionLevel = 2; mg.ShowRippleAtraction(); return $"atraídos={mg.GetAttractedCount()} max={mg.GetMaxRippleLevel()}"; }),
                    // setslot <balcão> <espaço> [select <posDeck> | access]: aponta o "espaço atual" e age nele (pedidos do ajudante, D56)
                    var x when x.StartsWith("setslot ") => R(() =>
                    {
                        var sp = x.Split(' ');
                        var shelf = bmC.ShopShelfs[int.Parse(sp[1])];
                        var slot = int.Parse(sp[2]);
                        var id = shelf.GetShopItems()[slot].GetSalePointObjId();
                        bmC.UpdateCurrentShopShelf(id);
                        var extra = "";
                        if (sp.Length >= 5 && sp[3] == "select") { bmC.SelectExhibit(int.Parse(sp[4])); extra = " select"; }
                        if (sp.Length >= 4 && sp[3] == "access") { extra = $" standby={bmC.IsBuyStandby(slot)}"; bmC.ActionAccessShopShelf(slot); }
                        if (sp.Length >= 4 && sp[3] == "sell") { extra = $" standby={bmC.IsBuyStandby(slot)}"; bmC.Sell(slot, false); }
                        return $"id={id} atual={bmC.GetCurrentShelfIndex()}{extra}";
                    }),
                    // shelfset <espaço> <item> <qtd>: põe um item no VISUAL da prateleira atual (teste do espelhamento)
                    var x when x.StartsWith("shelfset ") => R(() =>
                    {
                        var sp = x.Split(' ');
                        var sh = bmC.ShopShelfs[bmC.GetCurrentShelfIndex()];
                        sh.GetShopItem(int.Parse(sp[1])).UpdateExhibitUniqueData(new BokuMono.Data.ItemData { ItemId = uint.Parse(sp[2]), Stack = int.Parse(sp[3]), QualityValue = 10, FreshnessValue = 100 });
                        return "visual atualizado";
                    }),
                    _ => "?",
                };
                return Json(new { ok = true, result = r0, shelves = bmC.ShopShelfs?.Length ?? -1 });
            }

            case "exhibit":
            {
                // exhibit | exhibit put <espaçoDeck> <espaçoBalcão> | exhibit select <espaçoBalcão> <espaçoDeck> | exhibit refresh
                // Prateleiras do Bazaar (ExhibitionShelfManager) × deck: experimentos para "ajudar a repor" (D56)
                var ex = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var invE = InventoryManager.Instance;
                var bmE = BazaarManager.Instance;
                string RE(Func<object> f) { try { return f()?.ToString() ?? "ok"; } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                string result = null;
                if (ex.Length >= 3 && ex[0] == "put")
                    result = RE(() =>
                    {
                        var item = invE.DeckStorage.PutOut(int.Parse(ex[1]));
                        if (item == null) return "deck vazio nesse espaço";
                        var okPut = invE.ExhibitionShelf.PutIn(item, int.Parse(ex[2]), out var rem);
                        if (rem != null && rem.ItemId != 0 && rem.Stack > 0) invE.DeckStorage.PutIn(rem, out _, false);
                        bmE.ReExhibit();
                        return $"putIn={okPut}";
                    });
                if (ex.Length >= 3 && ex[0] == "select")
                    result = RE(() => { bmE.ActionAccessShopShelf(int.Parse(ex[1])); bmE.SelectExhibit(int.Parse(ex[2])); return "ok"; });
                if (ex.Length >= 1 && ex[0] == "refresh") result = RE(() => { bmE.ReExhibit(); return "ok"; });
                if (ex.Length >= 2 && ex[0] == "sell") result = RE(() => { bmE.Sell(int.Parse(ex[1]), false); return "ok"; });
                if (ex.Length >= 2 && ex[0] == "access") result = RE(() => { bmE.ActionAccessShopShelf(int.Parse(ex[1])); return "ok"; });
                if (ex.Length >= 2 && ex[0] == "state") result = RE(() => bmE.GetShopShelfAccessState(int.Parse(ex[1])));
                if (ex.Length >= 1 && ex[0] == "where")
                    result = RE(() =>
                    {
                        var shW = bmE.ShopShelfs[bmE.GetCurrentShelfIndex()];
                        var pl = GameController.Instance.GetPlayer().transform.position;
                        var sb = new System.Text.StringBuilder($"vendedor {pl.x:0.0},{pl.z:0.0} | ");
                        var its = shW.GetShopItems();
                        for (var k = 0; k < its.Length; k++)
                        {
                            var h = its[k].GetHelpTarget().position; var n = its[k].GetNpcTarget().position; var s0 = its[k].transform.position;
                            sb.Append($"[{k}] item {s0.x:0.0},{s0.z:0.0} ajudante {h.x:0.0},{h.z:0.0} cliente {n.x:0.0},{n.z:0.0}; ");
                        }
                        return sb.ToString();
                    });
                if (ex.Length >= 1 && ex[0] == "near")
                    result = RE(() =>
                    {
                        // espaço do balcão atual mais perto do vendedor (pelo ponto onde um ajudante atenderia)
                        var shW = bmE.ShopShelfs[bmE.GetCurrentShelfIndex()];
                        var pl = GameController.Instance.GetPlayer().transform.position;
                        var its = shW.GetShopItems();
                        int best = -1; float bd = float.MaxValue;
                        for (var k = 0; k < its.Length; k++)
                        {
                            var h = its[k].GetHelpTarget().position;
                            var d = (new Vector2(h.x, h.z) - new Vector2(pl.x, pl.z)).sqrMagnitude;
                            if (d < bd) { bd = d; best = k; }
                        }
                        return best;
                    });
                if (ex.Length >= 2 && ex[0] == "goto")
                    result = RE(() =>
                    {
                        // leva o vendedor para trás do espaço k (mesmo x do item, z do vendedor) e aperta a ação
                        var shW = bmE.ShopShelfs[bmE.GetCurrentShelfIndex()];
                        var it = shW.GetShopItems()[int.Parse(ex[1])].transform.position;
                        var pc = GameController.Instance.GetPlayer();
                        var pp = pc.transform.position;
                        pc.transform.position = new Vector3(it.x, pp.y, pp.z);
                        if (ex.Length >= 3 && ex[2] == "act") pc.Action();
                        return $"vendedor em {it.x:0.0},{pp.z:0.0}";
                    });
                var standby = new List<int>();
                var locked = new List<int>();
                for (var si = 0; si < 12; si++)
                {
                    try { if (bmE.IsBuyStandby(si)) standby.Add(si); } catch { }
                    try { if (bmE.IsShelfLocked(si)) locked.Add(si); } catch { }
                }
                List<string> List(StorageManager sm)
                {
                    var l = new List<string>();
                    var datas = sm.ItemDatas;
                    for (var i = 0; i < datas.Length; i++) { var d = datas[i]; l.Add(d == null || d.ItemId == 0 ? "-" : $"{d.ItemId}x{d.Stack}"); }
                    return l;
                }
                return Json(new { ok = true, result, standby, locked, shelf = List(invE.ExhibitionShelf), deck = List(invE.DeckStorage), money = ExternalData.Instance.UserInfo.PlayerMoney });
            }

            case "bzui":
            {
                // bzui dir <Up|Down|Left|Right> | south | north | east | west | minideck — aperta "botões" da tela do Bazaar
                // direto na UIBazaarMainPage (as teclas sintéticas não chegam a ela). D56
                var page = UnityEngine.Object.FindObjectOfType<UIBazaarMainPage>();
                if (page == null) return Json(new { ok = false, error = "tela do Bazaar não está aberta" });
                var bu = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string RB(Action f) { try { f(); return "ok"; } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                var rb = bu[0] switch
                {
                    "dir" => RB(() => page.InputDirection(Enum.Parse<UIDirection>(bu[1]))),
                    "south" => RB(() => page.OnSouth()),
                    "north" => RB(() => page.OnNorth()),
                    "east" => RB(() => page.OnEast()),
                    "west" => RB(() => page.OnWest()),
                    "minideck" => RB(() => page.OpenMiniDeck()),
                    _ => "?",
                };
                return Json(new { ok = true, result = rb, inputEnable = page.IsInputEnable(), miniDeck = page.IsMiniDeck() });
            }

            case "d57":
            {
                // d57 happy [add <n>] | mission [add <id> <n> | state <id> <0-4>] | tutorial [add <id>] | brand [add <item>]
                // Testes do que passou a ser compartilhado na D57.
                var da = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var uiD = ExternalData.Instance.UserInfo;
                string RD(Func<object> f) { try { return f()?.ToString() ?? "ok"; } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                object res = null;
                switch (da.Length > 0 ? da[0] : "")
                {
                    case "happy":
                        if (da.Length >= 3 && da[1] == "add") RD(() => { GameController.Instance.AddHappyPointNotUpdateHeld(uint.Parse(da[2])); return null; });
                        res = new { happy = uiD.PlayerHappyPoint };
                        break;
                    case "mission":
                    {
                        var mm = MissionManager.Instance;
                        string r0 = null;
                        if (da.Length >= 4 && da[1] == "add") r0 = RD(() => { var od = mm.GetMissionData(uint.Parse(da[2])); od.SetProgress(od.ProgressValue + int.Parse(da[3])); return od.ProgressValue; });
                        if (da.Length >= 4 && da[1] == "state") r0 = RD(() => { var od = mm.GetMissionData(uint.Parse(da[2])); od.State = (MissionManager.OrderState)int.Parse(da[3]); return od.State; });
                        var all = new List<string>();
                        foreach (var od in mm.OrderDatas) if (od != null) all.Add($"{od.Id}:{(int)od.State}:{od.ProgressValue}");
                        res = new { result = r0, count = all.Count, orders = all.Take(60) };
                        break;
                    }
                    case "tutorial":
                    {
                        var tm = TutorialManager.Instance;
                        if (da.Length >= 3 && da[1] == "add") tm.AddTutorialRead(uint.Parse(da[2]));
                        var sd = tm.ToSaveData();
                        // tutorial has <id>: só consulta
                        res = new { read = sd.ReadTutorialIdList?.Count ?? -1, tips = sd.ReadTipsIdList?.Count ?? -1, has = da.Length >= 3 && sd.ReadTutorialIdList != null && sd.ReadTutorialIdList.Contains(uint.Parse(da[2])) };
                        break;
                    }
                    case "brand":
                        if (da.Length >= 3 && da[1] == "add") { uiD.BrandItemIds ??= new Il2CppSystem.Collections.Generic.List<uint>(); uiD.BrandItemIds.Add(uint.Parse(da[2])); }
                        var bl = new List<uint>();
                        if (uiD.BrandItemIds != null) foreach (var b in uiD.BrandItemIds) bl.Add(b);
                        res = new { items = bl };
                        break;
                }
                return Json(new { ok = true, res });
            }

            case "objcount":
            {
                // Quantos GameObjects ativos há na cena, por nome (teste de duplicação ao reaplicar estado)
                var counts = new Dictionary<string, int>();
                foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
                {
                    var n = go.name;
                    counts[n] = counts.TryGetValue(n, out var c0) ? c0 + 1 : 1;
                }
                return Json(new { ok = true, total = counts.Values.Sum(), counts });
            }

            case "pets":
            {
                // pets | pets old | pets apply — pets e cavalo: parâmetros × personagens na cena (bug dos pets em dobro).
                // "old" reaplica pelo caminho antigo (FromSaveData do controlador); "apply" pelo caminho atual da SharedSync.
                var pa = arg.Trim();
                var pc = BokuMono.Animal.PetAnimalController.Instance;
                var rc = BokuMono.Animal.RideableAnimalController.Instance;
                if (pa == "old")
                {
                    pc.FromSaveData(StringExtensions.JsonToObject<BokuMono.Animal.PetAnimalController.SaveData>(StringExtensions.ToJson(pc.ToSaveData())));
                    rc.FromSaveData(StringExtensions.JsonToObject<BokuMono.Animal.RideableAnimalController.SaveData>(StringExtensions.ToJson(rc.ToSaveData())));
                }
                string applied = null;
                if (pa == "addcopy")
                {
                    // Simula "pet comprado no jogo de outro jogador": aplica o estado com uma cópia do 1º pet (Guid novo).
                    var root = System.Text.Json.Nodes.JsonNode.Parse(Net.SharedSync.CaptureJson("pets"))!;
                    var list = root["PetParams"]!.AsArray();
                    var copy = System.Text.Json.Nodes.JsonNode.Parse(list[0]!.ToJsonString())!;
                    copy["Guid"] = Guid.NewGuid().ToString();
                    copy["Name"] = "Copia";
                    list.Add(copy);
                    applied = Net.SharedSync.ApplyPart("pets", root.ToJsonString(), markSynced: false); // como mudança local: vai para a rede
                }
                if (pa == "apply")
                    applied = Net.SharedSync.ApplyPart("pets", Net.SharedSync.CaptureJson("pets"), markSynced: true) + " / "
                            + Net.SharedSync.ApplyPart("horse", Net.SharedSync.CaptureJson("horse"), markSynced: true);
                var names = new List<string>();
                foreach (var c in UnityEngine.Object.FindObjectsOfType<PetAnimalCharaecter>()) names.Add("pet:" + c.name);
                foreach (var c in UnityEngine.Object.FindObjectsOfType<RideableAnimalCharaecter>()) names.Add("cavalo:" + c.name);
                return Json(new
                {
                    ok = true, applied,
                    petParams = pc.PetCount, petContainers = pc.PetContainers?.Length ?? -1,
                    horseParams = rc.ToSaveData().Parameters?.Count ?? -1,
                    characters = names,
                });
            }

            case "animal":
            {
                // animal | animal get <guid> | animal set <guid> <campo> <valor> — animais da fazenda (5.3, D41)
                var aa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (aa.Length >= 1 && aa[0] == "barns")
                {
                    var bl = new List<string>();
                    foreach (var bn in Net.AnimalSync.Barns())
                    {
                        var jn = System.Text.Json.Nodes.JsonNode.Parse(Net.AnimalSync.BarnJson(bn))!;
                        bl.Add($"{jn["Guid"]} campo={jn["FieldId"]} ovos={jn["ProductEggCount"]} ração={jn["FeedItemId"]} manhã={jn["ExecutedMorningFeed"]}");
                    }
                    return Json(new { ok = true, barns = bl });
                }
                if (aa.Length == 0)
                {
                    var list = new List<string>();
                    foreach (var an in Net.AnimalSync.FarmAnimals())
                    {
                        var jn = System.Text.Json.Nodes.JsonNode.Parse(Net.AnimalSync.Json(an))!;
                        list.Add($"{an.Guid} {jn["Name"]} campo={jn["LocationFieldId"]} afeição={jn["FriendPoint"]} carinho={jn["DailyStokeCount"]} produto={jn["ProductRegetDays"]} comeu={jn["AteFeedType"]}");
                    }
                    return Json(new { ok = true, count = list.Count, animals = list });
                }
                var target = Net.AnimalSync.FarmAnimals().FirstOrDefault(x => x.Guid == aa[1]);
                if (target == null) return Json(new { ok = false, error = "animal não encontrado" });
                if (aa[0] == "get") return Json(new { ok = true, json = Net.AnimalSync.Json(target) });
                if ((aa[0] == "set" || aa[0] == "poke") && aa.Length >= 4)
                {
                    // set = como se viesse da rede (não reenvia); poke = como uma ação local (vai para os outros)
                    var node = System.Text.Json.Nodes.JsonNode.Parse(Net.AnimalSync.Json(target))!.AsObject();
                    node[aa[2]] = int.TryParse(aa[3], out var iv) ? iv : bool.TryParse(aa[3], out var bv) ? bv : aa[3];
                    var res = Net.AnimalSync.Apply(aa[1], node.ToJsonString(), markSynced: aa[0] == "set");
                    return Json(new { ok = true, result = res, json = Net.AnimalSync.Json(target).Substring(0, 400) });
                }
                return Json(new { ok = false, error = "use: animal | animal get <guid> | animal set <guid> <campo> <valor>" });
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

            case "avatarmake":
            {
                // avatarmake <Prologue|ClothesShop|Closet|WeddingCloth|Direct> — abre a tela de aparência do jogo nesse modo (D72)
                var cat = Enum.Parse<AvatarMakeCategory>(string.IsNullOrWhiteSpace(arg) ? "Prologue" : arg.Trim(), true);
                if (cat == AvatarMakeCategory.Prologue) { Net.CharacterCreator.Open(); return Json(new { ok = true, category = "Prologue (CharacterCreator)" }); }
                UIAccessor.Instance.RequestOpenAvatarMakeup(cat, null);
                return Json(new { ok = true, category = cat.ToString() });
            }

            case "meshes":
            {
                // meshes local | meshes <id> — nomes das malhas carregadas do personagem local ou do avatar de um jogador (D73/D75)
                GameObject go = arg.Trim() == "local" || arg.Trim() == "" ? GameController.Instance.GetPlayer()?.Model : Net.PlayerSync.RemoteAvatar(byte.Parse(arg.Trim()));
                if (go == null) return Json(new { ok = false, error = "sem modelo" });
                var names = new List<string>();
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMesh != null) names.Add(smr.sharedMesh.name);
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) names.Add(mf.sharedMesh.name);
                names.Sort();
                return Json(new { ok = true, count = names.Count, names });
            }

            case "textures":
            {
                // textures local | textures <id> — texturas dos materiais (rosto, pele, roupa) do personagem local ou de um avatar (D76)
                GameObject tgo = arg.Trim() == "local" || arg.Trim() == "" ? GameController.Instance.GetPlayer()?.Model : Net.PlayerSync.RemoteAvatar(byte.Parse(arg.Trim()));
                if (tgo == null) return Json(new { ok = false, error = "sem modelo" });
                return Json(new { ok = true, signature = Net.AppearanceSync.TextureSignature(tgo).Split('|') });
            }

            case "apppart":
            {
                // apppart <cat>:<id>[,...] — AvatarMakeManager.SetAvatarParts em cada peça (investigação D78: rosto/pele)
                var pamm = AvatarMakeManager.Instance;
                var pdone = new List<string>();
                foreach (var kv in arg.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var pid = uint.Parse(kv.Split(':')[1]);
                    var pm = MasterDataManager.Instance.AvatarMakeMaster.GetData(pid);
                    if (pm == null) { pdone.Add(kv + "=sem master"); continue; }
                    try { pamm.SetAvatarParts(AvatarInfo.CreateParts(pm)); pdone.Add(kv + "=ok"); }
                    catch (Exception e) { pdone.Add(kv + "=" + e.Message.Split((char)10)[0]); }
                }
                return Json(new { ok = true, done = pdone });
            }

            case "appreset":
            {
                // appreset <cat>:<id>[,...] — troca essas peças nas infos do jogador e roda ResetAvatarAppearance (investigação D78)
                var ramm = AvatarMakeManager.Instance;
                var rinfos = new List<AvatarInfo>(ramm.PlayerAvatarInfos);
                foreach (var kv in arg.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var cat = int.Parse(kv.Split(':')[0]); var id = uint.Parse(kv.Split(':')[1]);
                    var m = MasterDataManager.Instance.AvatarMakeMaster.GetData(id);
                    var idx = rinfos.FindIndex(i => (int)i.Category == cat);
                    if (m != null && idx >= 0) rinfos[idx] = AvatarInfo.CreateParts(m);
                }
                var arr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AvatarInfo>(rinfos.ToArray());
                ramm.PlayerAvatarInfos = arr; GameController.Instance.PlayerAvatarInfos = arr; ExternalData.Instance.UserInfo.AvatarInfos = arr;
                Instance.StartCoroutine(ramm.ResetAvatarAppearance(arr));
                return Json(new { ok = true });
            }

            case "appcreate":
            {
                // appcreate <cat>:<id>[,...] — troca essas peças nas infos do jogador e chama CreatePlayerAvatar (investigação D78)
                var camm = AvatarMakeManager.Instance;
                var cinfos = new List<AvatarInfo>(camm.PlayerAvatarInfos);
                foreach (var kv in arg.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var ccat = int.Parse(kv.Split(':')[0]); var cid = uint.Parse(kv.Split(':')[1]);
                    var cm = MasterDataManager.Instance.AvatarMakeMaster.GetData(cid);
                    var cidx = cinfos.FindIndex(i => (int)i.Category == ccat);
                    if (cm != null && cidx >= 0) cinfos[cidx] = AvatarInfo.CreateParts(cm);
                }
                var carr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AvatarInfo>(cinfos.ToArray());
                camm.PlayerAvatarInfos = carr; GameController.Instance.PlayerAvatarInfos = carr; ExternalData.Instance.UserInfo.AvatarInfos = carr;
                camm.CreatePlayerAvatar();
                return Json(new { ok = true });
            }

            case "appchange":
            case "appapply":
            {
                // appchange|appapply <cat>:<id>[,...] — troca as peças nas infos e roda ChangeAvatar(Default) / ApplyPlayerAvatarInfo (D78)
                var xamm = AvatarMakeManager.Instance;
                var xinfos = new List<AvatarInfo>(xamm.PlayerAvatarInfos);
                foreach (var kv in arg.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var xcat = int.Parse(kv.Split(':')[0]); var xid = uint.Parse(kv.Split(':')[1]);
                    var xm = MasterDataManager.Instance.AvatarMakeMaster.GetData(xid);
                    var xidx = xinfos.FindIndex(i => (int)i.Category == xcat);
                    if (xm != null && xidx >= 0) xinfos[xidx] = AvatarInfo.CreateParts(xm);
                }
                var xarr = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<AvatarInfo>(xinfos.ToArray());
                xamm.PlayerAvatarInfos = xarr; GameController.Instance.PlayerAvatarInfos = xarr; ExternalData.Instance.UserInfo.AvatarInfos = xarr;
                if (cmd == "appchange") Instance.StartCoroutine(xamm.ChangeAvatar(AvatarMakeManager.ChangeType.Default));
                else xamm.ApplyPlayerAvatarInfo();
                return Json(new { ok = true });
            }

            case "renderers":
            {
                // renderers local | renderers <id> — cada renderer: objeto, malha, ossos e texturas dos materiais (investigação D78)
                GameObject rgo = arg.Trim() == "local" || arg.Trim() == "" ? GameController.Instance.GetPlayer()?.Model : Net.PlayerSync.RemoteAvatar(byte.Parse(arg.Trim()));
                if (rgo == null) return Json(new { ok = false, error = "sem modelo" });
                var rlist = new List<string>();
                foreach (var r in rgo.GetComponentsInChildren<Renderer>(true))
                {
                    var smr = r.TryCast<SkinnedMeshRenderer>();
                    var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    var texs = new List<string>();
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        foreach (var p in m.GetTexturePropertyNames()) { var t = m.GetTexture(p); if (t != null && (p == "_BaseMap" || p == "_ShadowColorTextureMap" || p == "_MainTex")) texs.Add(m.name + ":" + p + "=" + t.name); }
                    }
                    rlist.Add($"{r.gameObject.name} | malha {mesh?.name} | ossos {(smr != null ? smr.bones.Length : 0)} raiz {(smr != null && smr.rootBone != null ? smr.rootBone.name : "-")} | {string.Join(", ", texs)}");
                }
                return Json(new { ok = true, renderers = rlist });
            }

            case "loadpart":
            {
                // loadpart prefab|tex <categoria AvatarResourceManager.Category> <nome> | loadpart result — carrega uma peça pelo jogo (D78)
                var lp = arg.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (lp[0] == "result") return Json(new { ok = true, result = _loadPartResult });
                var lcat = (AvatarResourceManager.Category)int.Parse(lp[1]);
                _loadPartResult = "carregando...";
                if (lp[0] == "tex")
                    AvatarMakeManager.Instance.LoadAvatarPartsTexture(lcat, lp[2], (Action<Texture, bool>)((t, ok) => _loadPartResult = $"ok={ok} textura={t?.name} {t?.width}x{t?.height}"));
                else
                    AvatarMakeManager.Instance.LoadAvatarPartsPrefab(lcat, lp[2], (Action<AvatarPartsModel, bool>)((pm, ok) =>
                    {
                        var info = new List<string>();
                        if (pm != null && pm.SkinnedMeshRenderers != null)
                            foreach (var sd in pm.SkinnedMeshRenderers)
                            {
                                var bn = new List<string>();
                                if (sd.Bones != null) foreach (var b in sd.Bones) bn.Add(b?.Name);
                                var mats = new List<string>();
                                if (sd.Renderer != null) foreach (var m in sd.Renderer.sharedMaterials) if (m != null) { var t = m.GetTexture("_BaseMap"); mats.Add(m.name + "=" + t?.name); }
                                info.Add($"{sd.Name} | malha {sd.Renderer?.sharedMesh?.name} | raiz {sd.RootBone?.Name} | ossos {bn.Count}: {string.Join(",", bn.Take(6))}... | {string.Join(", ", mats)}");
                            }
                        _loadPartResult = $"ok={ok} modelo={pm?.name} " + string.Join(" || ", info);
                    }));
                return Json(new { ok = true, started = true });
            }

            case "master":
            {
                // master <id>[,...] — campos da peça no AvatarMakeMaster (investigação D78)
                var mlist = new List<string>();
                foreach (var idt in arg.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var mm = MasterDataManager.Instance.AvatarMakeMaster.GetData(uint.Parse(idt));
                    mlist.Add(mm == null ? idt + ": -" : $"{idt}: cat={mm.Category} parts={mm.PartsId} parts2={mm.PartsId2} color={mm.ColorId} obj={mm.ObjectId} obj2={mm.ObjectId2} hat={mm.HatRoot} motion={mm.MotionCategory}");
                }
                return Json(new { ok = true, master = mlist });
            }

            case "matsig":
            {
                // matsig local | matsig <id> — por renderer: vértices da malha, texturas e cores dos materiais (investigação D78)
                GameObject sgo = arg.Trim() == "local" || arg.Trim() == "" ? GameController.Instance.GetPlayer()?.Model : Net.PlayerSync.RemoteAvatar(byte.Parse(arg.Trim()));
                if (sgo == null) return Json(new { ok = false, error = "sem modelo" });
                var slist = new List<string>();
                foreach (var r in sgo.GetComponentsInChildren<Renderer>(true))
                {
                    var n = r.gameObject.name;
                    if (n.StartsWith("itm_") || n.StartsWith("Hand") || n == "Glider") continue;
                    var smr = r.TryCast<SkinnedMeshRenderer>();
                    var mesh = smr != null ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                    var sparts = new List<string> { $"{n}:v{(mesh != null ? mesh.vertexCount : 0)}:{(r.enabled && r.gameObject.activeInHierarchy ? "on" : "off")}" };
                    var mpb = new MaterialPropertyBlock();
                    r.GetPropertyBlock(mpb);
                    if (!mpb.isEmpty && r.sharedMaterial != null && r.sharedMaterial.shader != null)
                    {
                        var bsh = r.sharedMaterial.shader;
                        for (var i = 0; i < bsh.GetPropertyCount(); i++)
                            if (bsh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                            {
                                var bc = mpb.GetColor(bsh.GetPropertyName(i));
                                sparts.Add($"bloco{bsh.GetPropertyName(i)}={bc.r:0.00},{bc.g:0.00},{bc.b:0.00}");
                            }
                    }
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null) continue;
                        var sh = m.shader;
                        for (var i = 0; i < sh.GetPropertyCount(); i++)
                        {
                            var pn = sh.GetPropertyName(i);
                            var pt = sh.GetPropertyType(i);
                            if (pt == UnityEngine.Rendering.ShaderPropertyType.Texture) { var t = m.GetTexture(pn); if (t != null) sparts.Add($"{pn}={t.name}"); }
                            else if (pt == UnityEngine.Rendering.ShaderPropertyType.Color) { var col = m.GetColor(pn); sparts.Add($"{pn}={col.r:0.00},{col.g:0.00},{col.b:0.00}"); }
                            else if (pt == UnityEngine.Rendering.ShaderPropertyType.Vector) { var vv = m.GetVector(pn); sparts.Add($"{pn}=v{vv.x:0.00},{vv.y:0.00},{vv.z:0.00},{vv.w:0.00}"); }
                            else if (pt == UnityEngine.Rendering.ShaderPropertyType.Float || pt == UnityEngine.Rendering.ShaderPropertyType.Range) sparts.Add($"{pn}={m.GetFloat(pn):0.00}");
                        }
                    }
                    slist.Add(string.Join(" ", sparts));
                }
                return Json(new { ok = true, sig = slist });
            }

            case "appdirect":
            {
                // appdirect <cat>:<id>[,...] — chama direto a função interna do jogo de cada peça (SetFaceParts, SetSkinParts,
                // SetEyeColor, SetHairColor, SetBeardParts, SetBeardColor, SetMoleParts, SetFrecklesParts) no personagem local (D78)
                var damm = AvatarMakeManager.Instance;
                var dres = new List<string>();
                foreach (var kv in arg.Trim().Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var dcat = int.Parse(kv.Split(':')[0]); var did = uint.Parse(kv.Split(':')[1]);
                    var dm = did == 0 ? null : MasterDataManager.Instance.AvatarMakeMaster.GetData(did);
                    var dinfo = dm == null ? AvatarInfo.CreateNoParts((AvatarPartsCategory)dcat) : AvatarInfo.CreateParts(dm);
                    var tag = kv;
                    Action dok = () => Plugin.Logger.LogInfo($"[appdirect] {tag} terminou");
                    try
                    {
                        switch (dcat)
                        {
                            case 0: damm.SetFaceParts(dinfo, dok, false); break;
                            case 1: damm.SetSkinParts(dinfo, dok); break;
                            case 2: damm.SetEyeColor(dinfo, dok); break;
                            case 4: damm.SetHairColor(dinfo, dok); damm.SetEyebrowColor(dinfo); break;
                            case 5: damm.SetBeardParts(dinfo, dok); break;
                            case 6: damm.SetBeardColor(dinfo, dok); break;
                            case 7: damm.SetMoleParts(dinfo, dok); break;
                            case 8: damm.SetFrecklesParts(dinfo, dok); break;
                            default: damm.SetAvatarParts(dinfo, dok); break;
                        }
                        dres.Add(kv + "=chamado");
                    }
                    catch (Exception e) { dres.Add(kv + "=" + e.Message.Split((char)10)[0]); }
                }
                return Json(new { ok = true, done = dres });
            }

            case "locks":
            {
                // locks — nomes na lista de proteção do jogo e nas peças do visual atual (investigação D78)
                var lamm = AvatarMakeManager.Instance;
                var main = new List<string>(); var en = new List<string>(); var arm = new List<string>();
                try { foreach (var n in lamm.avatarResourceManager.unloadBlocker.MainAvatarResources) main.Add(n); } catch (Exception e) { main.Add("erro " + e.Message); }
                try { foreach (var n in lamm.GetEnableResourceNames()) en.Add(n); } catch (Exception e) { en.Add("erro " + e.Message); }
                try { foreach (var n in lamm.avatarResourceManager.GetResourceNames()) arm.Add(n); } catch (Exception e) { arm.Add("erro " + e.Message); }
                main.Sort(); en.Sort(); arm.Sort();
                return Json(new { ok = true, main, enable = en, loaded = arm, remote = Net.AppearanceSync.LockedList() });
            }

            case "rebuild":
                // rebuild <id> — remonta o avatar de um jogador (testes da D79)
                Net.PlayerSync.InvalidateAvatar(byte.Parse(arg.Trim()));
                return Json(new { ok = true });

            case "fadein":
                // fadein — clareia a tela (destravar uma tela preta de "carregando" em testes)
                FadeManager.FadeIn(null, null, 0f, 0f);
                return Json(new { ok = true });

            case "report":
                // report — gera o .zip de logs (o mesmo do botão "Gerar log", D71)
                return Json(new { ok = true, path = BugReport.Create() });

            case "press":
            {
                // press <Flag[,Flag]> [quadros] — aperta botões de ação do jogador por dentro (HumanActionInputFlags).
                // Ex.: press Item = "Colocar" o item da mão no chão (o Shift sintético não chega ao jogo).
                var pa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var flags = Enum.Parse<HumanActionInputFlags>(pa[0], true);
                Patches.InputInjector.PressFlags(flags, pa.Length > 1 ? int.Parse(pa[1]) : 3);
                return Json(new { ok = true, flags = flags.ToString() });
            }

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

            case "windmill":
            {
                // windmill | windmill addcomplete <tipo 1-3> <item> | windmill takeout <tipo> | windmill droptool <item> — moinhos (P12/D66)
                var wmA = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var wm = WindmillCraftingManager.Instance;
                var sd = wm.saveData;
                string RW(Func<object> f) { try { return f()?.ToString() ?? "ok"; } catch (Exception e) { return "erro: " + e.Message.Split((char)10)[0]; } }
                string res = null;
                if (wmA.Length >= 3 && wmA[0] == "addcomplete")
                    res = RW(() => { sd.AddCompleteItem((WindmillCraftingType)int.Parse(wmA[1]), new BokuMono.Data.ItemData { ItemId = uint.Parse(wmA[2]), Stack = 1, QualityValue = 10, FreshnessValue = 100 }); return "ok"; });
                if (wmA.Length >= 2 && wmA[0] == "droptool")
                    res = RW(() =>
                    {
                        // tira uma ferramenta da mochila (simula quem a deixou no moinho para melhorar)
                        foreach (var d in InventoryManager.Instance.BagToolStorage.ItemDatas)
                            if (d != null && d.ItemId == uint.Parse(wmA[1])) { InventoryManager.Instance.BagToolStorage.PutOut(d); return "tirada"; }
                        return "não tem";
                    });
                if (wmA.Length >= 2 && wmA[0] == "takeout")
                    res = RW(() => { wm.TakeOutAction((WindmillCraftingType)int.Parse(wmA[1]), (Il2CppSystem.Action)(() => PushEvent("moinho:retirado"))); return "chamado"; });
                var mills = new List<string>();
                for (var t = 1; t <= 3; t++)
                {
                    var ty = (WindmillCraftingType)t;
                    string L(Il2CppSystem.Collections.Generic.List<BokuMono.Data.ItemData> l)
                    {
                        var parts = new List<string>();
                        if (l != null) foreach (var it in l) parts.Add(it == null ? "null" : $"{it.ItemId}x{it.Stack}");
                        return string.Join(",", parts);
                    }
                    var crafts = new List<string>();
                    try
                    {
                        var dl = sd.GetDataList(ty);
                        if (dl != null) foreach (var c in dl)
                        {
                            var mats = new List<string>();
                            if (c.materialList != null) foreach (var m in c.materialList) if (m != null) mats.Add($"{m.ItemId}x{m.Stack}");
                            crafts.Add($"receita {c.recipeDataId} faz {RW(() => c.GetCreateItemId())} materiais [{string.Join(",", mats)}] falta {RW(() => c.LeftTimeMinute)} min");
                        }
                    }
                    catch (Exception e) { crafts.Add("erro: " + e.Message); }
                    mills.Add($"{ty}: pedidos {string.Join(" | ", crafts)}");
                    mills.Add($"{ty}: liberado={RW(() => wm.IsReleaseWindmill(ty))} fazendo={RW(() => sd.GetDataList(ty)?.Count)} pronto=[{RW(() => L(sd.GetCompleteList(ty)))}] cancelados=[{RW(() => L(sd.GetCancelToolList(ty)))}] mesmaLista={RW(() => sd.GetCompleteList(ty)?.Pointer == sd.GetCompleteList(ty)?.Pointer)}");
                }
                var recipes = new List<string>();
                foreach (var r in MasterDataManager.Instance.WindmillCraftingMaster.list)
                    if (r != null && (int)r.ToolType != 0 && recipes.Count < 12) {
                        var req = new List<string>();
                        if (r.RequiredItemId != null) for (var k = 0; k < r.RequiredItemId.Count; k++) req.Add($"{r.RequiredItemType[k]}:{r.RequiredItemId[k]}x{r.RequiredItemStack[k]}");
                        recipes.Add($"{r.Id}: faz {r.CraftingItemId} ferramenta={r.ToolType} nv{r.ToolLv} moinho={r.WindmillType} tempo={r.Time} pede [{string.Join(",", req)}]");
                    }
                var tools = new List<string>();
                foreach (var d in InventoryManager.Instance.BagToolStorage.ItemDatas) if (d != null && d.ItemId != 0) tools.Add(d.ItemId.ToString());
                return Json(new { ok = true, result = res, mills, recipes, tools });
            }

            case "trophy":
            {
                // trophy — estante de troféus: o que o jogo guarda (troféus "usados" e a estante) e troféus na mochila
                var tr = TrophyRackManager.Instance;
                var inBag = new List<string>();
                foreach (var d in InventoryManager.Instance.BagItemStorage.ItemDatas)
                    if (d != null && d.ItemId != 0 && MasterDataManager.Instance.TrophyRackMaster != null)
                        foreach (var tm in MasterDataManager.Instance.TrophyRackMaster.list)
                            if (tm != null && tm.trophyItemId == (int)d.ItemId) { inBag.Add($"{d.ItemId}x{d.Stack}"); break; }
                return Json(new { ok = true, save = StringExtensions.ToJson(tr.ToSaveData()), unity = JsonUtility.ToJson(tr.ToSaveData()), inBag, masterCount = MasterDataManager.Instance.TrophyRackMaster?.list?.Count ?? -1 });
            }

            case "events":
            {
                // events | events drop <n> | events restore — cenas vistas (P15/D67): carregar substitui ou soma?
                var ev = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int Count() => BokuMono.FieldEvent.EventManager.Instance.EventInfo.ToSaveData().ReadEventIdList?.Count ?? -1;
                var evBefore = Count();
                string res = null;
                if (ev.Length >= 2 && ev[0] == "drop")
                {
                    var full = PlayerContext.CaptureParts(new[] { "events" })["events"];
                    _eventsBackup ??= full;
                    var node = System.Text.Json.Nodes.JsonNode.Parse(full)!;
                    var arr = node["ReadEventIdList"]!.AsArray();
                    for (var i = 0; i < int.Parse(ev[1]) && arr.Count > 0; i++) arr.RemoveAt(arr.Count - 1);
                    res = System.Text.Json.JsonSerializer.Serialize(PlayerContext.ApplyParts(new Dictionary<string, string> { ["events"] = node.ToJsonString() }));
                }
                if (ev.Length >= 1 && ev[0] == "restore" && _eventsBackup != null)
                    res = System.Text.Json.JsonSerializer.Serialize(PlayerContext.ApplyParts(new Dictionary<string, string> { ["events"] = _eventsBackup }));
                var head = PlayerContext.CaptureParts(new[] { "events" })["events"];
                return Json(new { ok = true, result = res, evBefore, after = Count(), length = head.Length, head = head.Substring(0, Math.Min(300, head.Length)) });
            }

            case "npcs":
            {
                // npcs — moradores no mapa atual: id, posição, se aceitam conversa; estado da NpcSync (D68)
                var list = new List<string>();
                foreach (var npc in UnityEngine.Object.FindObjectsOfType<NonPlayerCharacter>())
                {
                    if (npc == null || !npc.gameObject.activeInHierarchy) continue;
                    var p = npc.transform.position;
                    string talk; try { talk = npc.IsTalkable().ToString(); } catch (Exception e) { talk = "erro " + e.Message.Split((char)10)[0]; }
                    list.Add($"{npc.CharacterId} {npc.name} pos={p.x:0.0},{p.z:0.0} conversa={talk}");
                }
                return Json(new { ok = true, npcs = list, sync = Net.NpcSync.Status() });
            }

            case "spectate":
                // spectate — estado do espectador (D69); a tecla F7 é apertada de verdade nos testes
                return Json(new { ok = true, spectator = Net.Spectator.Status(), field = FieldManager.Instance.CurrentFieldId });

            case "bedsleep":
            {
                // bedsleep [índice] — opções da cama e a escolha direta (D69: voltar a deitar depois de espectar)
                var bed = UnityEngine.Object.FindObjectOfType<FieldPlayerBed>();
                if (bed == null) return Json(new { ok = false, error = "sem cama neste mapa" });
                var pl = GameController.Instance.GetPlayer();
                var cd = bed.GetAccessChoicesData(pl);
                var ids = new List<uint>();
                if (cd.idList != null) foreach (var i in cd.idList) ids.Add(i);
                string res = null;
                if (arg.Trim() == "access") { try { bed.Access(); res = "janela aberta"; } catch (Exception e) { res = "erro: " + e.Message.Split((char)10)[0]; } }
                else if (!string.IsNullOrWhiteSpace(arg)) { try { cd.DecideAction(int.Parse(arg)); res = "escolhido"; } catch (Exception e) { res = "erro: " + e.Message.Split((char)10)[0]; } }
                return Json(new { ok = true, count = cd.ChoiceCount, ids, result = res });
            }

            case "pose":
                // pose save | pose load — guarda/restaura a pose do personagem (D69: voltar deitado depois de espectar)
                if (arg.Trim() == "dump") return Json(new { ok = true, pose = Net.Spectator.PoseDebug() });
                if (arg.Trim() == "info")
                {
                    var tI = GameController.Instance.GetPlayerTransform();
                    var mI = GameController.Instance.GetPlayer().Model;
                    var anI = mI != null ? mI.GetComponentInChildren<Animator>() : null;
                    var bedI = UnityEngine.Object.FindObjectOfType<FieldPlayerBed>();
                    return Json(new
                    {
                        ok = true,
                        transform = tI.position.ToString("F2"), rot = tI.eulerAngles.ToString("F0"),
                        model = mI != null ? mI.transform.position.ToString("F2") : null,
                        modelParent = mI != null && mI.transform.parent != null ? mI.transform.parent.name : null,
                        transformParent = tI.parent != null ? tI.parent.name : null,
                        rootMotion = anI != null ? anI.applyRootMotion : (bool?)null,
                        waist = bedI != null && bedI.GetWaistRoot != null ? bedI.GetWaistRoot.position.ToString("F2") : null,
                        startL = bedI != null && bedI.GetStartPositionL != null ? bedI.GetStartPositionL.position.ToString("F2") : null,
                        startR = bedI != null && bedI.GetStartPositionR != null ? bedI.GetStartPositionR.position.ToString("F2") : null,
                    });
                }
                return Json(new { ok = true, result = arg.Trim() == "save" ? Net.Spectator.SavePose() : Net.Spectator.RestorePose(), pos = GameController.Instance.GetPlayerTransform().position.ToString() });

            case "phys":
            {
                // phys — física do personagem (corpo, colisores, camadas): conferir se tudo volta ao normal ao levantar
                var pc = GameController.Instance.GetPlayer();
                var list = new List<string>();
                foreach (var root in new Component[] { pc, pc.Model != null ? pc.Model.transform : null, GameController.Instance.GetPlayerTransform() })
                {
                    if (root == null) continue;
                    list.Add($"[{root.name}] camada={root.gameObject.layer} ativo={root.gameObject.activeSelf}");
                    foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true))
                        list.Add($"  corpo {rb.name}: cinemático={rb.isKinematic} colisões={rb.detectCollisions} gravidade={rb.useGravity} travas={rb.constraints}");
                    foreach (var co in root.GetComponentsInChildren<Collider>(true))
                        list.Add($"  colisor {co.name} {co.GetIl2CppType().Name}: ligado={co.enabled} gatilho={co.isTrigger} camada={co.gameObject.layer}");
                }
                var anP = pc.Model != null ? pc.Model.GetComponentInChildren<Animator>() : null;
                list.Add($"animador ligado={anP?.enabled} velocidade={anP?.speed} | pausado={pc.PauseObject?.IsPaused} | entrada={pc.IsMoveInputEnabled} | dormindo={pc.IsSleep}");
                return Json(new { ok = true, phys = list.Distinct().ToList() });
            }

            case "holdworld":
                // holdworld [on|off|title] — D59: simula "convidado com o mundo do host na memória" e a volta ao título
                if (arg.Trim() == "on") WorldSnapshot.HoldingHostWorld = true;
                if (arg.Trim() == "off") WorldSnapshot.HoldingHostWorld = false;
                if (arg.Trim() == "title") SceneManager.Instance.LoadScene(SceneMode.Title, null, null);
                return Json(new { ok = true, holding = WorldSnapshot.HoldingHostWorld, savesBlocked = WorldSnapshot.SavesBlocked, scene = SceneManager.Instance.CurrentSceneMode });

            case "addmoney":
                var before = ExternalData.Instance.UserInfo.PlayerMoney;
                GameController.Instance.AddMoney(uint.Parse(arg), true);
                return Json(new { ok = true, before, after = ExternalData.Instance.UserInfo.PlayerMoney });

            case "storage":
            {
                // storage | storage put <itemId> <qtd> | storage take <slot> <qtd> — armazém da casa (4.2)
                // storage [deck|parts] ... — armazém 0 (casa), deck (1) ou peças do Bazaar (2)
                var sa = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                var sidx = 0;
                if (sa.Count > 0 && sa[0] == "deck") { sidx = 1; sa.RemoveAt(0); }
                else if (sa.Count > 0 && sa[0] == "parts") { sidx = 2; sa.RemoveAt(0); }
                var hs = Net.StorageSync.Get(sidx);
                if (sa.Count >= 3 && sa[0] == "put")
                {
                    hs.PutIn(BokuMono.Data.ItemData.Create(uint.Parse(sa[1]), int.Parse(sa[2])), out _, false);
                    // Deck: o "Ver estoque" do jogo dá um número de deck a cada item; sem ele o mini deck da barraca quebra (D56).
                    if (sidx == 1) Net.BazaarHelpSync.FixDeckNumbers();
                }
                if (sa.Count >= 3 && sa[0] == "take") hs.PutOut(int.Parse(sa[1]), int.Parse(sa[2]));
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
                if (arg.StartsWith("live ")) return Json(new { ok = true, live = FarmLab.LiveKind(arg.Substring(5).Trim()) });
                if (arg == "free") return Json(new { ok = true, free = FarmLab.Free() });
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
                return StatusJson();

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
                if (arg.StartsWith("savefile "))
                {
                    // world savefile <nome> — grava o mundo atual em BepInEx/devsaves/<nome>.sav (fora dos saves do usuário)
                    var devPath = DevSavePath(arg.Substring(9).Trim());
                    WorldSnapshot.Capture(2, okCap =>
                    {
                        if (okCap) { Directory.CreateDirectory(Path.GetDirectoryName(devPath)); File.WriteAllBytes(devPath, WorldSnapshot.Captured); }
                        PushEvent($"worldsavefile:{okCap}");
                        Plugin.Logger.LogInfo($"[DevBridge] save de teste {(okCap ? "gravado" : "FALHOU")}: {devPath}");
                    });
                    return Json(new { ok = true, started = "savefile", path = devPath });
                }
                if (arg.StartsWith("loadfile "))
                {
                    // world loadfile <nome> — volta para um save de teste gravado com savefile
                    var devPath = DevSavePath(arg.Substring(9).Trim());
                    if (!File.Exists(devPath)) return Json(new { ok = false, error = "não existe: " + devPath });
                    WorldSnapshot.SetCaptured(File.ReadAllBytes(devPath));
                    WorldSnapshot.Load(2);
                    return Json(new { ok = true, started = "loadfile", path = devPath });
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

    private static string _loadPartResult = "";

    private static string Json(object o) => JsonSerializer.Serialize(o, JsonOpts);

    /// <summary>Estado de cada parte do mod (o "net status"); também vai no relatório de bugs (D71), mesmo com a ponte desligada.</summary>
    public static string StatusJson() => Json(new { ok = true, net = Net.NetSession.Status(), remotes = Net.PlayerSync.Status(), clock = Net.ClockSync.Status(), day = Net.DaySync.Status(), profiles = Net.PlayerProfiles.Status(), appearance = Net.AppearanceSync.Status(), money = Net.MoneySync.Status(), storage = Net.StorageSync.Status(), mail = Net.LetterSync.Status(), farm = Net.FarmSync.Status(), animals = Net.AnimalSync.Status(), shared = Net.SharedSync.Status(), romance = Net.RomanceSync.Status(), bazaarView = Net.BazaarViewSync.Status(), bazaarHelp = Net.BazaarHelpSync.Status(), missions = Net.MissionSync.Status(), character = Net.CharacterCreator.Status() });
}
