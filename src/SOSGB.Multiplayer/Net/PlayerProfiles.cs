// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md, DECISOES.md#d24 e ROTEIRO.md (3.6).
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
///  - Primeira entrada (o host não conhece o GUID): o perfil inicial é o do PRÓPRIO save que o convidado
///    tinha carregado antes de entrar (D24, provisório).
///  - O convidado envia o perfil ao host (ProfileUpload) ao dormir, a cada 60 s e ao sair.
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

    // ---- host ----
    private static readonly Dictionary<string, string> Stored = new(); // guid → JSON do perfil
    private static readonly Dictionary<byte, string> GuidOf = new();   // id do jogador → guid
    private static string _storePath;

    // ---- convidado ----
    private static string _pending;        // perfil a aplicar quando o mundo carregar
    private static bool _pendingIsNewDay;
    private static bool _worldLoading;
    private static IntPtr _oldPlayer;       // personagem do mundo antigo (esperar ser trocado)
    private static float _readySince = -1;
    private static bool _sawSceneChange;   // a cena saiu de Game durante a recarga
    private static bool _firstWorldDone;
    private static string _lastUploaded;
    private float _nextUpload;
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
            // Antes do mundo do host chegar: o save que este convidado carregou é o perfil
            // inicial, caso o host ainda não o conheça (D24).
            _firstWorldDone = false;
            _pending = Capture();
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
        SaveStore(); // perfis dos convidados junto
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
            _worldLoading = false;
            ApplyPending();
            _firstWorldDone = true;
            _nextUpload = Time.unscaledTime + 5f;
            return;
        }
        if (_firstWorldDone && Time.unscaledTime >= _nextUpload)
        {
            _nextUpload = Time.unscaledTime + UploadInterval;
            Upload();
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
        if (_pendingIsNewDay)
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

    private static string Capture() => JsonSerializer.Serialize(PlayerContext.CaptureParts(PlayerContext.ProfileParts));

    /// <summary>Convidado → host: perfil atual. Chamado ao dormir, periodicamente e ao sair.</summary>
    public static void Upload()
    {
        if (NetSession.Current != NetSession.Mode.Client || !_firstWorldDone || _worldLoading) return;
        var json = Capture();
        _lastUploaded = json;
        NetSession.SendGame(MsgType.ProfileUpload, w => w.Put(json), DeliveryMethod.ReliableOrdered);
    }

    // ---------- host ----------

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.ProfileUpload when NetSession.Current == NetSession.Mode.Host:
                if (GuidOf.TryGetValue(from, out var guid)) Stored[guid] = r.GetString();
                break;

            case MsgType.ProfileData when NetSession.Current == NetSession.Mode.Client:
                var json = r.GetString();
                if (!string.IsNullOrEmpty(json)) _pending = json; // perfil salvo no host vence o do save local
                Plugin.Logger.LogInfo($"[Profiles] host {(string.IsNullOrEmpty(json) ? "não tinha perfil (usando o do meu save)" : "mandou o perfil guardado")}");
                break;
        }
    }

    /// <summary>Host: chamado pelo WorldSync logo depois de mandar o mundo para quem acabou de entrar.</summary>
    public static void SendStoredProfile(byte playerId)
    {
        var json = GuidOf.TryGetValue(playerId, out var g) && Stored.TryGetValue(g, out var p) ? p : "";
        NetSession.SendTo(playerId, MsgType.ProfileData, w => w.Put(json), DeliveryMethod.ReliableOrdered);
    }

    private static string StorePath()
    {
        var ui = ExternalData.Instance.UserInfo;
        string Clean(string s) => new string((s ?? "x").Where(char.IsLetterOrDigit).ToArray());
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "STORY OF SEASONS Grand Bazaar", "SOSGB-MP");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"profiles_{Clean(ui.FarmName)}_{Clean(ui.PlayerName)}.json");
    }

    private static void LoadStore()
    {
        Stored.Clear();
        _storePath = StorePath();
        if (!File.Exists(_storePath)) return;
        foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_storePath)))
            Stored[kv.Key] = kv.Value;
        Plugin.Logger.LogInfo($"[Profiles] {Stored.Count} perfil(is) carregado(s) de {_storePath}");
    }

    /// <summary>Host: grava os perfis em disco (na virada do dia, junto do save oficial — D23).</summary>
    public static void SaveStore()
    {
        if (NetSession.Current != NetSession.Mode.Host) return;
        _storePath ??= StorePath();
        File.WriteAllText(_storePath, JsonSerializer.Serialize(Stored));
        Plugin.Logger.LogInfo($"[Profiles] {Stored.Count} perfil(is) gravado(s) em {_storePath}");
        DevBridge.PushEvent($"perfis-gravados:{Stored.Count}");
    }

    public static object Status() => new
    {
        stored = Stored.Keys.ToArray(),
        players = GuidOf,
        pending = _pending != null,
        firstWorldDone = _firstWorldDone,
        path = _storePath,
    };
}
