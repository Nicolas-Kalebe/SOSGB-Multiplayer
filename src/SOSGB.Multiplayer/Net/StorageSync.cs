// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d37.
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using BokuMono;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>
/// Armazém da casa compartilhado (4.2, D37).
///
///  - UMA pessoa por vez com o armazém aberto (como os baús do Stardew). O host guarda quem está com ele
///    (StorageHolder). Se outro jogador abrir a tela, o mod fecha (CloseMenu) e avisa.
///  - Sincronização pelo CONTEÚDO: cada jogo confere o armazém a cada 0,5 s (assinatura dos espaços). Mudou
///    por conta própria (guardou, pegou, cozinhou com ingredientes de lá)? Manda o armazém inteiro, em JSON
///    comprimido, ao host (StorageData). O host aplica e repassa aos outros.
///  - Conflito (alguém mudou o armazém enquanto outro estava com ele aberto): vence quem está com ele aberto.
///  - Depois de carregar um mundo (entrada, virada do dia), a base é zerada: o snapshot já traz o armazém.
/// </summary>
public class StorageSync : MonoBehaviour
{
    public StorageSync(IntPtr ptr) : base(ptr) { }

    public const byte NoHolder = 255;
    private const int HouseStorageKey = 102; // UILoadKey.HouceStorage (sic, nome do jogo)

    /// <summary>Quem está com o armazém aberto (id do jogador) ou NoHolder.</summary>
    public static byte Holder { get; private set; } = NoHolder;

    private static long _baseline;            // assinatura do armazém já sincronizado
    private static bool _hasBaseline;
    private static bool _subscribed;
    private static bool _localOpen;
    private float _nextCheck;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => { _hasBaseline = false; _localOpen = false; };
        NetSession.OnHosting += () => { _hasBaseline = false; Holder = NoHolder; };
        NetSession.OnJoined += () => { _hasBaseline = false; Holder = NoHolder; };
        NetSession.OnPlayerLeft += id => { if (NetSession.Current == NetSession.Mode.Host && Holder == id) SetHolder(NoHolder); };
        NetSession.OnPlayerJoinedNamed += (id, name) => { if (NetSession.Current == NetSession.Mode.Host) BroadcastHolder(); };
    }

    private static bool Ready()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return false;
        if (!SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode != SceneMode.Game) return false;
        if (!InventoryManager.IsInstance || InventoryManager.Instance.HouseStorage == null) return false;
        if (NetSession.Current == NetSession.Mode.Client && !PlayerProfiles.WorldApplied) return false;
        return !DaySync.InDayChange;
    }

    private void Update()
    {
        try
        {
            if (!Ready()) return;
            WatchLocalUi();
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            var sig = Signature();
            if (!_hasBaseline) { _baseline = sig; _hasBaseline = true; return; }
            if (sig == _baseline) return;
            _baseline = sig;
            var someoneElse = Holder != NoHolder && Holder != NetSession.LocalId;
            if (someoneElse)
            {
                // Mexeram aqui enquanto outro está com o armazém aberto: vence quem está com ele (o próximo
                // StorageData dele corrige este). Só registra.
                Plugin.Logger.LogInfo("[StorageSync] armazém mudou aqui enquanto outro jogador estava com ele; vale o dele");
                return;
            }
            SendSnapshot();
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[StorageSync] {e.Message}"); }
    }

    // ---------- tela do armazém (trava) ----------

    // UIManager não é singleton com Instance: achamos o objeto uma vez e guardamos (procura de novo se sumir).
    private static UIManager _ui;
    private static float _nextUiLookup;

    private static UIManager Ui
    {
        get
        {
            if (_ui != null) return _ui;
            if (Time.unscaledTime < _nextUiLookup) return null;
            _nextUiLookup = Time.unscaledTime + 2f;
            _ui = UnityEngine.Object.FindObjectOfType<UIManager>();
            return _ui;
        }
    }

    private static void WatchLocalUi()
    {
        var ui = Ui;
        var open = ui != null && (int)ui.UIMenuKey == HouseStorageKey && ui.IsMenu;
        if (open == _localOpen) return;
        _localOpen = open;
        var me = NetSession.LocalId;
        if (open)
        {
            if (Holder != NoHolder && Holder != me)
            {
                var name = NetSession.Players.TryGetValue(Holder, out var p) ? p.Name : "Outro jogador";
                Plugin.Logger.LogInfo($"[StorageSync] armazém em uso por {name}; fechando a tela");
                GameNotify.Show($"{name} está usando o armazém. Espere fechar.");
                CloseStorageUi(ui);
                _localOpen = false;
                return;
            }
            Claim(true);
        }
        else
        {
            // Fechou: manda já a versão final (sem esperar a conferência de 0,5 s) e solta a trava.
            var sig = Signature();
            if (!_hasBaseline || sig != _baseline) { _baseline = sig; _hasBaseline = true; SendSnapshot(); }
            Claim(false);
        }
    }

    /// <summary>
    /// Fecha a tela do armazém. CloseMenu funcionou nos testes; ForceBackUIMain deu KeyNotFoundException
    /// ('HouceStorage') quando a tela não veio do histórico de menus, então fica só de reserva.
    /// </summary>
    private static void CloseStorageUi(UIManager ui)
    {
        try { ui.CloseMenu(null, null, false, null, false); }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[StorageSync] CloseMenu falhou ({e.Message}); tentando ForceBackUIMain");
            try { ui.ForceBackUIMain(null); } catch (Exception e2) { Plugin.Logger.LogWarning($"[StorageSync] ForceBackUIMain: {e2.Message}"); }
        }
    }

    private static void Claim(bool open)
    {
        if (NetSession.Current == NetSession.Mode.Host)
        {
            if (open && Holder == NoHolder) SetHolder(NetSession.LocalId);
            else if (!open && Holder == NetSession.LocalId) SetHolder(NoHolder);
        }
        else
            NetSession.SendGame(MsgType.StorageUse, w => w.Put(open), DeliveryMethod.ReliableOrdered);
    }

    private static void SetHolder(byte id)
    {
        Holder = id;
        BroadcastHolder();
        DevBridge.PushEvent($"armazem:{(id == NoHolder ? "livre" : id.ToString())}");
    }

    private static void BroadcastHolder()
    {
        var h = Holder;
        NetSession.SendGame(MsgType.StorageHolder, w => w.Put(h), DeliveryMethod.ReliableOrdered);
    }

    // ---------- conteúdo ----------

    /// <summary>Assinatura (FNV) dos espaços do armazém: item, quantidade, qualidade, frescor e embrulho.</summary>
    private static long Signature()
    {
        unchecked
        {
            var h = (long)1469598103934665603;
            var datas = InventoryManager.Instance.HouseStorage.ItemDatas;
            for (var i = 0; i < datas.Length; i++)
            {
                var d = datas[i];
                if (d == null || d.ItemId == 0) continue;
                h = (h ^ i) * 1099511628211;
                h = (h ^ d.ItemId) * 1099511628211;
                h = (h ^ d.Stack) * 1099511628211;
                h = (h ^ d.QualityValue) * 1099511628211;
                h = (h ^ d.FreshnessValue) * 1099511628211;
                h = (h ^ d.WrapId) * 1099511628211;
            }
            return h;
        }
    }

    private static string Capture() => StringExtensions.ToJson(InventoryManager.Instance.HouseStorage.ToSaveData());

    private static void Apply(string json)
    {
        var hs = InventoryManager.Instance.HouseStorage;
        hs.FromSaveData(StringExtensions.JsonToObject<StorageManager.SaveData>(json));
        _baseline = Signature();
        _hasBaseline = true;
    }

    private static byte[] Pack(string json)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal)) { var b = Encoding.UTF8.GetBytes(json); gz.Write(b, 0, b.Length); }
        return ms.ToArray();
    }

    private static string Unpack(byte[] data)
    {
        using var gz = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var sr = new StreamReader(gz, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    private static void SendSnapshot()
    {
        var packed = Pack(Capture());
        if (NetSession.Current == NetSession.Mode.Host)
        {
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId) NetSession.SendTo(id, MsgType.StorageData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
        }
        else
            NetSession.SendGame(MsgType.StorageData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
        Plugin.Logger.LogInfo($"[StorageSync] armazém enviado ({packed.Length} bytes comprimidos)");
        DevBridge.PushEvent("armazem:enviado");
    }

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.StorageUse when NetSession.Current == NetSession.Mode.Host:
                var open = r.GetBool();
                if (open && Holder == NoHolder) SetHolder(from);
                else if (!open && Holder == from) SetHolder(NoHolder);
                else if (open) BroadcastHolder(); // já está com outro: reenviar faz o convidado fechar a tela
                break;

            case MsgType.StorageHolder when NetSession.Current == NetSession.Mode.Client && from == Protocol.HostPlayerId:
                Holder = r.GetByte();
                // Corrida: abri junto com outro e o host deu para ele → fecho a minha tela.
                if (_localOpen && Holder != NoHolder && Holder != NetSession.LocalId && Ui != null)
                {
                    GameNotify.Show("Outro jogador abriu o armazém primeiro. Espere ele fechar.");
                    CloseStorageUi(Ui);
                    _localOpen = false;
                }
                break;

            case MsgType.StorageData:
            {
                var data = r.GetBytesWithLength();
                if (!Ready()) return;
                if (NetSession.Current == NetSession.Mode.Host)
                {
                    // Só aceita de quem está com a trava (ou de qualquer um, se ninguém está).
                    if (Holder != NoHolder && Holder != from) { Plugin.Logger.LogInfo($"[StorageSync] armazém de #{from} ignorado (está com #{Holder})"); SendTo(from); return; }
                    Apply(Unpack(data));
                    foreach (var id in NetSession.Players.Keys)
                        if (id != NetSession.LocalId && id != from) NetSession.SendTo(id, MsgType.StorageData, w => w.PutBytesWithLength(data), DeliveryMethod.ReliableOrdered);
                    Plugin.Logger.LogInfo($"[StorageSync] armazém atualizado por #{from}");
                }
                else if (from == Protocol.HostPlayerId)
                {
                    Apply(Unpack(data));
                    Plugin.Logger.LogInfo("[StorageSync] armazém atualizado pelo host");
                }
                DevBridge.PushEvent("armazem:recebido");
                break;
            }
        }
    }

    /// <summary>Host: manda o armazém oficial para um jogador (corrige quem mexeu sem a trava).</summary>
    private static void SendTo(byte id)
    {
        var packed = Pack(Capture());
        NetSession.SendTo(id, MsgType.StorageData, w => w.PutBytesWithLength(packed), DeliveryMethod.ReliableOrdered);
    }

    public static object Status() => new { holder = Holder, localOpen = _localOpen, baseline = _baseline };
}
