// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d24, #d30 e ROTEIRO.md (3.6).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Dados individuais dos convidados guardados pelo host, com reconexão (3.6, D24).
///
/// O mundo que o convidado recebe (WorldData) é o save do host, que traz a mochila, os corações etc.
/// DO HOST. Por isso, depois de cada carga de mundo, o convidado aplica o próprio "perfil"
/// (partes individuais do PlayerContext: amizade, conversas, mochila, ferramentas, regador).
///
///  - Identidade: [Net] PlayerGuid (gerado automaticamente), enviado no Hello.
///  - Primeira entrada (o host não conhece o GUID): o convidado começa DO ZERO (D30, FreshProfile), como os
///    farmhands do Stardew. Do save próprio só vem o visual do personagem.
///  - O convidado envia o perfil ao host (ProfileUpload, comprimido) ao dormir, ao sair, ~1,5 s depois de a mochila
///    ou as ferramentas mudarem e a cada 60 s (antes só a cada 60 s: quem caía logo depois de guardar itens no
///    armazém voltava com eles também na mochila).
///  - O host guarda em memória e grava em arquivo na virada do dia (junto do save oficial, D23):
///    %APPDATA%\STORY OF SEASONS Grand Bazaar\SOSGB-MP\profiles_&lt;fazenda&gt;_&lt;host&gt;.json
///  - Ao (re)entrar, o host manda o perfil guardado (ProfileData).
///  - Virada do dia: o convidado reaplica o perfil que tinha ao dormir e roda o OnChangeDate dos
///    sistemas pessoais (zera presente/conversa do dia, frescor dos itens).
/// </summary>
public class PlayerProfiles : MonoBehaviour
{
    public PlayerProfiles(IntPtr ptr) : base(ptr) { }

    private const float UploadInterval = 60f;
    private const float ChangeUploadDelay = 1.5f; // mochila/ferramentas mudaram: manda o perfil logo (revisão do núcleo)

    // ---- host ----
    private static readonly Dictionary<string, string> Stored = new(); // guid → JSON do perfil
    private static readonly Dictionary<byte, string> GuidOf = new();   // id do jogador → guid
    private static string _storePath;

    // ---- convidado ----
    private static string _pending;        // perfil a aplicar quando o mundo carregar
    private static bool _profileAnswered;  // o host já respondeu se conhece este jogador (ProfileData)
    private static bool _makeFresh;        // host não conhece: começar do zero (D30)
    private static string _ownAppearance;  // visual do personagem do save próprio (único dado aproveitado)
    private static float _answerWaitSince = -1;
    private static bool _pendingIsNewDay;
    private static bool _worldLoading;
    private static IntPtr _oldPlayer;       // personagem do mundo antigo (esperar ser trocado)
    private static float _readySince = -1;
    private static bool _sawSceneChange;   // a cena saiu de Game durante a recarga
    private static bool _firstWorldDone;
    private static string _lastUploaded;
    private float _nextUpload;
    private long _invSig;
    private float _invChangedAt = -1;
    private float _nextInvCheck;
    private static bool _subscribed;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        NetSession.OnHelloAccepted += (id, guid) => { GuidOf[id] = guid; };
        NetSession.OnPlayerLeft += id => GuidOf.Remove(id);
        NetSession.OnJoined += () =>
        {
            // D30: o progresso do convidado só existe no mundo do host. Se o host não conhece este
            // jogador, ele começa DO ZERO (FreshProfile); do save próprio só aproveitamos o visual.
            _firstWorldDone = false;
            _pending = null;
            _profileAnswered = false;
            _makeFresh = false;
            _ownAppearance = Environment.GetEnvironmentVariable("SOSGB_TEST_APPEARANCE") is { Length: > 0 } forced
                ? forced // só testes
                : (AppearanceSync.Stable ?? SafeLocalAppearance());
            _pendingIsNewDay = false;
        };
        WorldSync.OnWorldReceived += () =>
        {
            _worldLoading = true;
            _readySince = -1;
            _sawSceneChange = false;
            // A recarga é assíncrona: a cena antiga continua "Game" por um tempo. Guardamos o
            // personagem atual para só aplicar o perfil quando o personagem NOVO existir.
            _oldPlayer = GameController.IsInstance && GameController.Instance.GetPlayer() != null
                ? GameController.Instance.GetPlayer().Pointer : IntPtr.Zero;
            if (_firstWorldDone) { _pending = _lastUploaded ?? Capture(); _pendingIsNewDay = true; }
        };
        NetSession.OnHosting += LoadStore;
        // NÃO aplicar a aparência no save recebido antes da cena: testado e trava o carregamento (personagem
        // nulo, relógio parado). A aparência própria é aplicada DEPOIS da carga (parte "appearance" do perfil,
        // AppearanceSync.ApplyToLocal). Ver D28/D32.
    }

    private float _nextRecovery = -1;

    private void Update()
    {
        try
        {
            if (NetSession.Current == NetSession.Mode.Host) RecoveryTick();
            if (NetSession.Current == NetSession.Mode.Client) ClientTick();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Profiles] {e.Message}"); }
    }

    /// <summary>Host: snapshot de recuperação a cada [Net] RecoverySnapshotMinutes (0 = desligado), D23.</summary>
    private void RecoveryTick()
    {
        var minutes = Plugin.RecoverySnapshotMinutes.Value;
        if (minutes <= 0 || DaySync.InDayChange || !WorldReadyHost()) return;
        if (_forceRecovery) { _forceRecovery = false; _nextRecovery = 0; }
        if (_nextRecovery < 0) { _nextRecovery = Time.unscaledTime + minutes * 60f; return; }
        if (Time.unscaledTime < _nextRecovery) return;
        _nextRecovery = Time.unscaledTime + minutes * 60f;
        // Perfis num arquivo À PARTE, junto da cópia de recuperação: gravar o arquivo oficial no meio do dia deixava os
        // perfis à frente do save oficial se o host travasse (itens colhidos à tarde na mochila e no pé; revisão do núcleo).
        SaveStore(StorePath().Replace("profiles_", "recovery_profiles_"));
        WorldSnapshot.CaptureRecovery(Path.Combine(Path.GetDirectoryName(StorePath()),
            Path.GetFileName(StorePath()).Replace("profiles_", "recovery_").Replace(".json", ".sav")));
    }

    public static void ForceRecovery() => _forceRecovery = true;
    private static bool _forceRecovery;

    private static bool WorldReadyHost() =>
        SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game
        && GameController.IsInstance && GameController.Instance.GetPlayerTransform() != null;

    // ---------- convidado ----------

    private void ClientTick()
    {
        if (_worldLoading)
        {
            if (SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode != SceneMode.Game) _sawSceneChange = true;
            if (!WorldReady()) { _readySince = -1; return; }
            if (_readySince < 0) { _readySince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _readySince < 1.5f) return; // deixa os sistemas do mundo novo assentarem
            // Na entrada, espera a resposta do host (ProfileData) por até 8 s; sem resposta, começa do zero.
            if (!_firstWorldDone && !_profileAnswered)
            {
                if (_answerWaitSince < 0) _answerWaitSince = Time.unscaledTime;
                if (Time.unscaledTime - _answerWaitSince < 8f) return;
                _makeFresh = true;
            }
            _answerWaitSince = -1;
            if (!_firstWorldDone && _pending == null && _makeFresh)
            {
                // Dados carregados agora são os do host: viram a base do perfil zerado.
                _pending = FreshProfile.Make(PlayerContext.CaptureParts(PlayerContext.ProfileParts), _ownAppearance);
                Plugin.Logger.LogInfo("[Profiles] primeira vez neste mundo: começando do zero (D30)");
                DevBridge.PushEvent("perfil:do-zero");
            }
            _worldLoading = false;
            ApplyPending();
            _firstWorldDone = true;
            _nextUpload = Time.unscaledTime + 5f;
            return;
        }
        if (!_firstWorldDone) return;
        if (Time.unscaledTime >= _nextInvCheck)
        {
            _nextInvCheck = Time.unscaledTime + 0.5f;
            var sig = InventorySignature();
            if (sig != _invSig) { _invSig = sig; _invChangedAt = Time.unscaledTime; }
        }
        var changedReady = _invChangedAt >= 0 && Time.unscaledTime - _invChangedAt >= ChangeUploadDelay;
        if (changedReady || Time.unscaledTime >= _nextUpload)
        {
            _invChangedAt = -1;
            _nextUpload = Time.unscaledTime + UploadInterval;
            Upload();
        }
    }

    /// <summary>Assinatura barata da mochila e das ferramentas (item, quantidade, qualidade): mudou → manda o perfil.</summary>
    private static long InventorySignature()
    {
        unchecked
        {
            var h = (long)1469598103934665603;
            var inv = InventoryManager.Instance;
            if (inv == null) return 0;
            foreach (var sm in new StorageManager[] { inv.BagItemStorage, inv.BagToolStorage })
            {
                var datas = sm?.ItemDatas;
                if (datas == null) continue;
                for (var i = 0; i < datas.Length; i++)
                {
                    var d = datas[i];
                    if (d == null || d.ItemId == 0) continue;
                    h = (h ^ i) * 1099511628211;
                    h = (h ^ d.ItemId) * 1099511628211;
                    h = (h ^ d.Stack) * 1099511628211;
                    h = (h ^ d.QualityValue) * 1099511628211;
                }
                h = (h ^ 0x5bd1e995) * 1099511628211;
            }
            return h;
        }
    }

    private static bool WorldReady() =>
        SceneManager.IsInstance && SceneManager.Instance.CurrentSceneMode == SceneMode.Game
        && GameController.IsInstance && GameController.Instance.GetPlayerTransform() != null
        && GameController.Instance.GetPlayer() != null
        && (_sawSceneChange || GameController.Instance.GetPlayer().Pointer != _oldPlayer)
        && LikeabilityManager.IsInstance && InventoryManager.IsInstance;

    private static void ApplyPending()
    {
        if (string.IsNullOrEmpty(_pending)) return;
        var parts = JsonSerializer.Deserialize<Dictionary<string, string>>(_pending);
        var report = PlayerContext.ApplyParts(parts);
        // Entrada depois de dias fora (D63): o perfil é de um dia anterior ao do mundo do host. Roda a virada pessoal
        // uma vez (zera presente/conversa do dia, frescor), senão o jogo diria "já deu presente hoje" a um morador.
        var staleEntry = !_pendingIsNewDay && parts.TryGetValue(DayKey, out var dayText)
                         && long.TryParse(dayText, out var savedDay) && savedDay < GameDay();
        if (staleEntry) Plugin.Logger.LogInfo("[Profiles] perfil de um dia anterior: rodando a virada pessoal");
        if (_pendingIsNewDay || staleEntry)
        {
            // O host processou o dia com os dados DELE; aqui processamos a virada nos nossos.
            LikeabilityManager.Instance.OnChangeDate();
            TalkManager.Instance.OnChangeDate(DateManager.Instance.Now.Year);
            InventoryManager.Instance.BagItemStorage.OnChangeDate();
        }
        Plugin.Logger.LogInfo($"[Profiles] perfil aplicado ({(_pendingIsNewDay ? "dia novo" : "entrada")}): {string.Join(", ", report.Select(kv => $"{kv.Key}={kv.Value}"))}");
        DevBridge.PushEvent(_pendingIsNewDay ? "perfil-aplicado:dia-novo" : "perfil-aplicado:entrada");
        _pending = null;
        Upload(); // o host passa a ter a versão atual
    }

    private static string SafeLocalAppearance()
    {
        try { return AppearanceSync.Local(); } catch { return null; }
    }

    private const string DayKey = "_day"; // dia do jogo em que o perfil foi capturado (não é uma parte; ApplyParts ignora)

    /// <summary>Dia do jogo, com a troca às 06:00 (de madrugada ainda conta como o dia anterior).</summary>
    private static long GameDay() =>
        (DateManager.Instance.Now.Ticks - 6L * 3600 * 10_000_000) / (24L * 3600 * 10_000_000);

    private static string Capture()
    {
        var parts = PlayerContext.CaptureParts(PlayerContext.ProfileParts);
        try { parts[DayKey] = GameDay().ToString(); } catch { /* sem relógio: sem a data */ }
        return JsonSerializer.Serialize(parts);
    }

    /// <summary>Convidado → host: perfil atual. Chamado ao dormir, periodicamente e ao sair.</summary>
    public static void Upload()
    {
        if (NetSession.Current != NetSession.Mode.Client || !_firstWorldDone || _worldLoading) return;
        var json = Capture();
        _lastUploaded = json;
        // Comprimido e com tamanho int: o texto puro passava de ~40 KB num convidado novo e o limite de um texto no
        // pacote é 64 KB (estourava depois de semanas de jogo e travava a noite; revisão do núcleo).
        var packed = LetterSync.Pack(json);
        NetSession.SendGame(MsgType.ProfileUpload, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
    }

    // ---------- host ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.ProfileUpload when NetSession.Current == NetSession.Mode.Host:
                if (GuidOf.TryGetValue(from, out var guid)) Stored[guid] = LetterSync.Unpack(r.GetBlob());
                break;

            case MsgType.ProfileData when NetSession.Current == NetSession.Mode.Client:
                var json = LetterSync.Unpack(r.GetBlob());
                _profileAnswered = true;
                if (!string.IsNullOrEmpty(json)) _pending = json; // progresso deste jogador neste mundo
                else _makeFresh = true;                           // primeira vez neste mundo (D30)
                Plugin.Logger.LogInfo($"[Profiles] host {(string.IsNullOrEmpty(json) ? "não me conhece: vou começar do zero" : "mandou meu perfil neste mundo")}");
                break;
        }
    }

    /// <summary>Host: chamado pelo WorldSync logo depois de mandar o mundo para quem acabou de entrar.</summary>
    public static void SendStoredProfile(byte playerId)
    {
        var json = GuidOf.TryGetValue(playerId, out var g) && Stored.TryGetValue(g, out var p) ? p : "";
        var packed = LetterSync.Pack(json);
        NetSession.SendTo(playerId, MsgType.ProfileData, w => w.PutBlob(packed), DeliveryMethod.ReliableOrdered);
    }

    private static string StorePath()
    {
        var ui = ExternalData.Instance.UserInfo;
        string Clean(string s) => new string((s ?? "x").Where(char.IsLetterOrDigit).ToArray());
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "STORY OF SEASONS Grand Bazaar", "SOSGB-MP");
        // Modo de teste: perfis e snapshots de recuperação numa subpasta, longe dos perfis reais do jogador.
        if (Plugin.DevMode) dir = Path.Combine(dir, "dev");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"profiles_{Clean(ui.FarmName)}_{Clean(ui.PlayerName)}.json");
    }

    private static bool _storeUnreadable; // o arquivo existe mas não deu para ler: nunca sobrescrever (perderia todos)

    private static void LoadStore()
    {
        Stored.Clear();
        _storeUnreadable = false;
        try
        {
            _storePath = StorePath();
            foreach (var path in new[] { _storePath, _storePath + ".bak" })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)))
                        Stored[kv.Key] = kv.Value;
                    Plugin.Logger.LogInfo($"[Profiles] {Stored.Count} perfil(is) carregado(s) de {path}");
                    return;
                }
                catch (Exception e) { Plugin.Logger.LogWarning($"[Profiles] não deu para ler {path}: {e.Message}"); }
            }
            _storeUnreadable = File.Exists(_storePath);
            if (_storeUnreadable) GameNotify.Show("Multiplayer: o arquivo de perfis dos amigos está corrompido; ele não será sobrescrito (mande o log).");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Profiles] carregar perfis: {e.Message}"); }
    }

    /// <summary>
    /// Host: grava os perfis em disco (na virada do dia, junto do save oficial — D23). Grava num arquivo temporário e
    /// troca, guardando o anterior como .bak: uma queda no meio da gravação não corrompe os perfis de todos.
    /// </summary>
    public static void SaveStore(string path = null)
    {
        if (NetSession.Current != NetSession.Mode.Host) return;
        _storePath ??= StorePath();
        path ??= _storePath;
        if (path == _storePath && _storeUnreadable) { Plugin.Logger.LogWarning("[Profiles] arquivo de perfis ilegível: não sobrescrito"); return; }
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Stored));
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
            Plugin.Logger.LogInfo($"[Profiles] {Stored.Count} perfil(is) gravado(s) em {path}");
            DevBridge.PushEvent($"perfis-gravados:{Stored.Count}");
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[Profiles] gravar perfis: {e.Message}"); }
    }

    /// <summary>Host: o GUID já está em uso por outro jogador online (ou é o do próprio host)?</summary>
    public static bool GuidInUse(string guid) =>
        !string.IsNullOrEmpty(guid) && (guid == Plugin.EffectivePlayerGuid || GuidOf.ContainsValue(guid));

    /// <summary>Host: perfis guardados (guid → JSON das partes). Usado pelo registro de romance (D44).</summary>
    public static IEnumerable<KeyValuePair<string, string>> StoredProfiles() => Stored.ToList();

    /// <summary>Host: nome do jogador dono de um GUID (se estiver online).</summary>
    public static string NameOfGuid(string guid)
    {
        foreach (var kv in GuidOf)
            if (kv.Value == guid && NetSession.Players.TryGetValue(kv.Key, out var p)) return p.Name;
        return null;
    }

    /// <summary>Host: GUID de um jogador online.</summary>
    public static string GuidOfPlayer(byte id) => GuidOf.TryGetValue(id, out var g) ? g : null;

    /// <summary>No convidado: o mundo do host já foi carregado e o perfil aplicado.</summary>
    public static bool WorldApplied => _firstWorldDone && !_worldLoading;

    public static object Status() => new
    {
        stored = Stored.Keys.ToArray(),
        players = GuidOf,
        pending = _pending != null,
        firstWorldDone = _firstWorldDone,
        path = _storePath,
    };
}
