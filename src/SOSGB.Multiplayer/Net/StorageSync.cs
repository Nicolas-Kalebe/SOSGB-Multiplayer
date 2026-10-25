// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md e DECISOES.md#d37 / #d46.
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
/// Armazéns compartilhados (4.2, D37; deck e peças do Bazaar na D46).
///
///  - UMA pessoa por vez com a tela do armazém aberta (como os baús do Stardew). O host guarda quem está com cada
///    armazém (StorageHolder). Se outro jogador abrir a tela, o mod fecha (CloseMenu) e avisa.
///  - Sincronização pelo CONTEÚDO: cada jogo confere cada armazém a cada 0,5 s (assinatura dos espaços). Mudou
///    por conta própria (guardou, pegou, cozinhou com ingredientes de lá)? Manda o armazém inteiro, em JSON
///    comprimido, ao host (StorageData). O host aplica e repassa aos outros.
///  - Conflito (alguém mudou o armazém enquanto outro estava com ele aberto): vence quem está com ele aberto.
///  - Depois de carregar um mundo (entrada, virada do dia), a base é zerada: o snapshot já traz os armazéns.
/// </summary>
public class StorageSync : MonoBehaviour
{
    public StorageSync(IntPtr ptr) : base(ptr) { }

    public const byte NoHolder = 255;

    private sealed class Store
    {
        public byte Index;
        public string Name;            // nome no aviso ("o armazém", "o deck do Bazaar")
        public int UiKey;              // UILoadKey da tela (trava); -1 = sem trava
        public Func<bool> OpenNow;     // em vez da tela: outra condição de "em uso" (ex.: barraca do Bazaar aberta)
        public Func<StorageManager> Get;
        public byte Holder = NoHolder;
        public long Baseline;
        public bool HasBaseline;
        public bool LocalOpen;
        public string LastReceived;    // último conteúdo vindo da rede (ou enviado): o ajudante do Bazaar volta a ele (D56)
    }

    private static readonly Store[] Stores =
    {
        new() { Index = 0, Name = "o armazém", UiKey = 102, Get = () => InventoryManager.Instance.HouseStorage },          // UILoadKey.HouceStorage
        new() { Index = 1, Name = "o deck do Bazaar", UiKey = 103, Get = () => InventoryManager.Instance.DeckStorage },     // UILoadKey.DeckStorage
        new() { Index = 2, Name = "as peças do Bazaar", UiKey = 113, Get = () => InventoryManager.Instance.BazaarCustomPartsStorage }, // UILoadKey.BazaarCustom
        // Barraca do Bazaar: só uma pessoa vende por vez (D51; evita duas barracas vendendo os mesmos itens). Não tem
        // conteúdo próprio; a trava vale enquanto a loja está aberta. Quem abre a loja com a barraca de outro é
        // ajudante (D56) e não pega a trava.
        new() { Index = 3, Name = "a barraca do Bazaar", UiKey = -1, OpenNow = () => BazaarManager.IsInstance && BazaarManager.Instance.IsInShop && !BazaarHelpSync.Active, Get = () => null },
    };

    /// <summary>Quem está com a barraca do Bazaar aberta (NoHolder = ninguém).</summary>
    public static byte BazaarHolder => Stores[3].Holder;

    /// <summary>Quem está com o armazém da casa aberto (compatível com testes antigos).</summary>
    public static byte Holder => Stores[0].Holder;

    private static bool _subscribed;
    private float _nextCheck;

    private void Awake()
    {
        if (_subscribed) return;
        _subscribed = true;
        NetSession.OnGameMessage += OnMessage;
        WorldSync.OnWorldReceived += () => { foreach (var s in Stores) { s.HasBaseline = false; s.LocalOpen = false; } };
        NetSession.OnHosting += () => { foreach (var s in Stores) { s.HasBaseline = false; s.Holder = NoHolder; } };
        NetSession.OnJoined += () => { foreach (var s in Stores) { s.HasBaseline = false; s.Holder = NoHolder; } };
        NetSession.OnPlayerLeft += id =>
        {
            if (NetSession.Current != NetSession.Mode.Host) return;
            foreach (var s in Stores) if (s.Holder == id) SetHolder(s, NoHolder);
        };
        NetSession.OnPlayerJoinedNamed += (id, name) =>
        {
            if (NetSession.Current == NetSession.Mode.Host) foreach (var s in Stores) BroadcastHolder(s);
        };
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
            foreach (var s in Stores) WatchLocalUi(s);
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            foreach (var s in Stores)
            {
                if (s.Get() == null) continue;
                var sig = Signature(s);
                if (!s.HasBaseline) { s.Baseline = sig; s.HasBaseline = true; continue; }
                if (sig == s.Baseline) continue;
                s.Baseline = sig;
                if (s.Index == 1 && BazaarHelpSync.SuppressWrites)
                {
                    // Ajudante do Bazaar: o deck só é escrito pelo dono da barraca (D56). Mudança local não vai para a rede.
                    Plugin.Logger.LogInfo("[StorageSync] deck mudou no jogo do ajudante; não enviado (vale o do dono)");
                    continue;
                }
                if (s.Holder != NoHolder && s.Holder != NetSession.LocalId)
                {
                    // Mexeram aqui enquanto outro está com a tela aberta: vale o dele (o próximo StorageData corrige).
                    Plugin.Logger.LogInfo($"[StorageSync] {s.Name} mudou aqui enquanto outro jogador estava com ele; vale o dele");
                    continue;
                }
                SendSnapshot(s);
            }
        }
        catch (Exception e) { Plugin.Logger.LogWarning($"[StorageSync] {e.Message}"); }
    }

    // ---------- tela (trava) ----------

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

    private static void WatchLocalUi(Store s)
    {
        if (s.UiKey < 0 && s.OpenNow == null) return;
        var ui = Ui;
        bool open;
        if (s.OpenNow != null) { try { open = s.OpenNow(); } catch { open = false; } }
        else open = ui != null && (int)ui.UIMenuKey == s.UiKey && ui.IsMenu;
        if (open == s.LocalOpen) return;
        s.LocalOpen = open;
        var me = NetSession.LocalId;
        if (open)
        {
            // D56: durante a venda o deck (e as peças) ficam com quem vende — dois jogos mexendo no deck ao mesmo tempo
            // poderiam duplicar itens.
            var seller = Stores[3].Holder;
            if ((s.Index == 1 || s.Index == 2) && seller != NoHolder && seller != me)
            {
                var sname = NetSession.Players.TryGetValue(seller, out var sp) ? sp.Name : "Outro jogador";
                GameNotify.Show($"{sname} está vendendo: {s.Name} fica com quem vende até a loja fechar.");
                CloseStorageUi(ui);
                s.LocalOpen = false;
                return;
            }
            if (s.Holder != NoHolder && s.Holder != me)
            {
                var name = NetSession.Players.TryGetValue(s.Holder, out var p) ? p.Name : "Outro jogador";
                if (s.UiKey < 0)
                {
                    // Barraca: o menu já devia ter sido bloqueado; se escapou (os dois abriram juntos), só avisa.
                    Plugin.Logger.LogWarning($"[StorageSync] {s.Name} aberta aqui e com {name} ao mesmo tempo");
                    GameNotify.Show($"{name} também abriu a barraca. Combinem quem vende neste turno.");
                    return;
                }
                Plugin.Logger.LogInfo($"[StorageSync] {s.Name} em uso por {name}; fechando a tela");
                GameNotify.Show($"{name} está usando {s.Name}. Espere fechar.");
                CloseStorageUi(ui);
                s.LocalOpen = false;
                return;
            }
            Claim(s, true);
        }
        else
        {
            // Fechou: manda já a versão final (sem esperar a conferência de 0,5 s) e solta a trava.
            if (s.Get() != null)
            {
                var sig = Signature(s);
                if (!s.HasBaseline || sig != s.Baseline) { s.Baseline = sig; s.HasBaseline = true; SendSnapshot(s); }
            }
            Claim(s, false);
        }
    }

    /// <summary>
    /// Fecha a tela. CloseMenu funcionou nos testes; ForceBackUIMain deu KeyNotFoundException ('HouceStorage')
    /// quando a tela não veio do histórico de menus, então fica só de reserva.
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

    private static void Claim(Store s, bool open)
    {
        if (NetSession.Current == NetSession.Mode.Host)
        {
            if (open && s.Holder == NoHolder) SetHolder(s, NetSession.LocalId);
            else if (!open && s.Holder == NetSession.LocalId) SetHolder(s, NoHolder);
        }
        else
        {
            var idx = s.Index;
            NetSession.SendGame(MsgType.StorageUse, w => { w.Put(idx); w.Put(open); }, DeliveryMethod.ReliableOrdered);
        }
    }

    private static void SetHolder(Store s, byte id)
    {
        s.Holder = id;
        BroadcastHolder(s);
        DevBridge.PushEvent($"armazem{s.Index}:{(id == NoHolder ? "livre" : id.ToString())}");
    }

    private static void BroadcastHolder(Store s)
    {
        var idx = s.Index;
        var h = s.Holder;
        NetSession.SendGame(MsgType.StorageHolder, w => { w.Put(idx); w.Put(h); }, DeliveryMethod.ReliableOrdered);
    }

    // ---------- conteúdo ----------

    /// <summary>Assinatura (FNV) dos espaços: item, quantidade, qualidade, frescor e embrulho.</summary>
    private static long Signature(Store s)
    {
        unchecked
        {
            var h = (long)1469598103934665603;
            var datas = s.Get().ItemDatas;
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

    private static string Capture(Store s) => StringExtensions.ToJson(s.Get().ToSaveData());

    private static void Apply(Store s, string json)
    {
        s.Get().FromSaveData(StringExtensions.JsonToObject<StorageManager.SaveData>(json));
        s.Baseline = Signature(s);
        s.HasBaseline = true;
        s.LastReceived = json;
    }

    /// <summary>
    /// Ajudante do Bazaar, ao terminar (D56): o jogo dele pode ter "devolvido" ao deck itens dos balcões, que eram cópia
    /// dos do dono. Volta ao último deck recebido da rede, sem enviar nada.
    /// </summary>
    public static void RevertDeckToLastReceived()
    {
        var s = Stores[1];
        if (s.LastReceived == null || s.Get() == null) return;
        var before = Signature(s);
        Apply(s, s.LastReceived);
        if (Signature(s) != before) Plugin.Logger.LogInfo("[StorageSync] deck do ajudante voltou ao último recebido do dono");
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

    private static void SendSnapshot(Store s)
    {
        var json = Capture(s);
        s.LastReceived = json;
        var packed = Pack(json);
        var idx = s.Index;
        if (NetSession.Current == NetSession.Mode.Host)
        {
            foreach (var id in NetSession.Players.Keys)
                if (id != NetSession.LocalId) NetSession.SendTo(id, MsgType.StorageData, w => { w.Put(idx); w.PutBytesWithLength(packed); }, DeliveryMethod.ReliableOrdered);
        }
        else
            NetSession.SendGame(MsgType.StorageData, w => { w.Put(idx); w.PutBytesWithLength(packed); }, DeliveryMethod.ReliableOrdered);
        Plugin.Logger.LogInfo($"[StorageSync] {s.Name} enviado ({packed.Length} bytes comprimidos)");
        DevBridge.PushEvent($"armazem{s.Index}:enviado");
    }

    private static Store ByIndex(byte i) => i < Stores.Length ? Stores[i] : null;

    private static void OnMessage(MsgType type, byte from, NetDataReader r)
    {
        switch (type)
        {
            case MsgType.StorageUse when NetSession.Current == NetSession.Mode.Host:
            {
                var s = ByIndex(r.GetByte());
                var open = r.GetBool();
                if (s == null) return;
                if (open && s.Holder == NoHolder) SetHolder(s, from);
                else if (!open && s.Holder == from) SetHolder(s, NoHolder);
                else if (open) BroadcastHolder(s); // já está com outro: reenviar faz o convidado fechar a tela
                break;
            }
            case MsgType.StorageHolder when NetSession.Current == NetSession.Mode.Client && from == Protocol.HostPlayerId:
            {
                var s = ByIndex(r.GetByte());
                var h = r.GetByte();
                if (s == null) return;
                s.Holder = h;
                // Corrida: abri junto com outro e o host deu para ele → fecho a minha tela.
                if (s.LocalOpen && h != NoHolder && h != NetSession.LocalId && Ui != null)
                {
                    GameNotify.Show($"Outro jogador abriu {s.Name} primeiro. Espere ele fechar.");
                    CloseStorageUi(Ui);
                    s.LocalOpen = false;
                }
                break;
            }
            case MsgType.StorageData:
            {
                var s = ByIndex(r.GetByte());
                var data = r.GetBytesWithLength();
                if (s == null || !Ready() || s.Get() == null) return;
                if (NetSession.Current == NetSession.Mode.Host)
                {
                    // Só aceita de quem está com a trava (ou de qualquer um, se ninguém está).
                    if (s.Holder != NoHolder && s.Holder != from) { Plugin.Logger.LogInfo($"[StorageSync] {s.Name} de #{from} ignorado (está com #{s.Holder})"); SendTo(s, from); return; }
                    Apply(s, Unpack(data));
                    var idx = s.Index;
                    foreach (var id in NetSession.Players.Keys)
                        if (id != NetSession.LocalId && id != from) NetSession.SendTo(id, MsgType.StorageData, w => { w.Put(idx); w.PutBytesWithLength(data); }, DeliveryMethod.ReliableOrdered);
                    Plugin.Logger.LogInfo($"[StorageSync] {s.Name} atualizado por #{from}");
                }
                else if (from == Protocol.HostPlayerId)
                {
                    Apply(s, Unpack(data));
                    Plugin.Logger.LogInfo($"[StorageSync] {s.Name} atualizado pelo host");
                }
                DevBridge.PushEvent($"armazem{s.Index}:recebido");
                break;
            }
        }
    }

    /// <summary>
    /// D51/D56: com OUTRO jogador vendendo, o menu da barraca abre, mas "Abrir loja" vira ajudar (BazaarHelpSync);
    /// "Ver estoque" e "Redecorar" ficam bloqueados pelo deck/peças estarem com quem vende.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(BazaarManager), nameof(BazaarManager.RequestOpenShopDialog))]
    private static class BazaarOpenPatch
    {
        private static bool Prefix()
        {
            if (Plugin.DevMode) Plugin.Logger.LogInfo("[StorageSync] menu da barraca do Bazaar pedido");
            if (NetSession.Current == NetSession.Mode.Offline) return true;
            var h = Stores[3].Holder;
            if (h == NoHolder || h == NetSession.LocalId) return true;
            var name = NetSession.Players.TryGetValue(h, out var p) ? p.Name : "Outro jogador";
            GameNotify.Show($"{name} está vendendo. Escolha \"Abrir loja\" para ajudar na barraca.");
            Plugin.Logger.LogInfo($"[StorageSync] menu da barraca aberto como ajudante ({name} está vendendo)");
            return true;
        }
    }

    /// <summary>Host: manda o armazém oficial para um jogador (corrige quem mexeu sem a trava).</summary>
    private static void SendTo(Store s, byte id)
    {
        var packed = Pack(Capture(s));
        var idx = s.Index;
        NetSession.SendTo(id, MsgType.StorageData, w => { w.Put(idx); w.PutBytesWithLength(packed); }, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Testes: StorageManager de um armazém pelo índice (0 casa, 1 deck, 2 peças do Bazaar).</summary>
    public static StorageManager Get(int index) => index >= 0 && index < Stores.Length ? Stores[index].Get() : null;

    public static object Status()
    {
        var list = new System.Collections.Generic.List<object>();
        foreach (var s in Stores) list.Add(new { s.Name, holder = s.Holder, localOpen = s.LocalOpen, baseline = s.Baseline });
        return new { holder = Stores[0].Holder, localOpen = Stores[0].LocalOpen, baseline = Stores[0].Baseline, stores = list };
    }
}
