// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d27.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BokuMono;
using SOSGB.Multiplayer.Net;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Interface do multiplayer para os jogadores (D27), em IMGUI com layout manual (GUILayout foi removido do jogo).
///  - Tela de título: escolher "Jogar sozinho / Hospedar / Entrar", nome e IP do host. Mostra os IPs
///    deste PC para o host passar aos amigos. Grava direto no .cfg ([Net] AutoStart/JoinAddress/PlayerName).
///    "Entrar agora" conecta dali mesmo, sem carregar save (D72): o mundo do host chega pela rede.
///  - "Gerar log" (título e F9): .zip com os logs na Área de Trabalho para mandar ao desenvolvedor (D71).
///  - Dentro do jogo: lista compacta (quem está conectado, onde está, ping, "dormindo X/Y").
///  - F9: menu dentro do jogo (hospedar agora, entrar agora, desconectar).
/// </summary>
public class MultiplayerUI : MonoBehaviour
{
    public MultiplayerUI(IntPtr ptr) : base(ptr) { }

    private bool _menuOpen;
    private bool _hudVisible = true;
    private string _nameField;
    private string _ipField;
    private string[] _localIps;
    private float _nextIpRefresh;
    private GUIStyle _box, _title, _label, _small, _button, _buttonOn, _field, _fieldBg, _fieldFocus;
    private bool _stylesReady;
    private string _lastError;

    private void Update()
    {
        try
        {
            if (Input.GetKeyDown(KeyCode.F9)) _menuOpen = !_menuOpen;
            if (Input.GetKeyDown(KeyCode.F10)) _hudVisible = !_hudVisible;
            // Deitado esperando os outros: Backspace levanta da cama (D34).
            if (DaySync.LocalSleeping && !TypingInMenu && Input.GetKeyDown(KeyCode.Backspace)) DaySync.WakeUp();
        }
        catch { /* input legado indisponível: só o painel do título funciona */ }
    }

    private static int Scene => SceneManager.IsInstance ? SceneManager.Instance.CurrentSceneMode : SceneMode.None;

    private void OnGUI()
    {
        try
        {
            EnsureStyles();
            var scene = Scene;
            TypingInMenu = _focus != 0;
            if (scene == SceneMode.Title) DrawSetupPanel(inGame: false);
            else if (scene == SceneMode.Game)
            {
                if (Spectator.Active)
                {
                    // Espectando (D69): só uma linha discreta, o resto da interface some.
                    var line = $"Espectando {Spectator.TargetName}   [F7] próximo   [Backspace] levantar";
                    var r = new Rect(Screen.width / 2f - 240, Screen.height - 48, 480, 28);
                    Panel(r, _panelBg);
                    GUI.Label(new Rect(r.x + 12, r.y + 4, r.width - 20, 22), line, _label);
                    DrawNameTags();
                    return;
                }
                if (_menuOpen) DrawSetupPanel(inGame: true);
                if (_hudVisible) DrawHud();
                DrawNameTags();
            }
        }
        catch (Exception e)
        {
            if (_lastError != e.Message) { _lastError = e.Message; Plugin.Logger.LogWarning($"[UI] {e}"); }
        }
    }

    // ---------- painel de configuração (título e F9) ----------

    // GUILayout foi removido do jogo no build ("Method unstripping failed" em GUILayout.BeginArea).
    // Por isso o layout é manual: um cursor vertical (_y) dentro da caixa.
    private float _x, _y, _w;

    private void Row(float h) => _y += h;

    private void Label(string text, GUIStyle style, float h = 22) { GUI.Label(new Rect(_x, _y, _w, h), text, style); Row(h); }

    /// <summary>
    /// Botão próprio: GUI.Button existe mas não reage neste jogo (o estado de controle do IMGUI foi
    /// removido no build). Lemos o clique direto do Event.current, como no campo de texto.
    /// </summary>
    private bool Button(Rect r, string text, GUIStyle style)
    {
        var e = Event.current;
        var hover = r.Contains(e.mousePosition);
        Panel(r, style == _buttonOn ? _fieldFocusTex : hover ? _hoverTex : _fieldTex);
        GUI.Label(new Rect(r.x, r.y + (r.height - 22) / 2, r.width, 22), text, style == _buttonOn ? _btnTextOn : _btnText);
        if (e.type == EventType.MouseDown && e.button == 0 && hover) { e.Use(); return true; }
        return false;
    }

    // GUI.TextField também foi removido do jogo (GUIStateObjects). Campo de texto próprio:
    // clicar dá foco; letras/números/ponto/espaço digitam; Backspace apaga; Enter/Esc tira o foco.
    private int _focus;

    private string TextBox(int id, Rect r, string value, int max)
    {
        var focused = _focus == id;
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (r.Contains(e.mousePosition)) { _focus = id; e.Use(); }
            else if (focused) _focus = 0;
        }
        if (focused && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace) { if (value.Length > 0) value = value.Substring(0, value.Length - 1); e.Use(); }
            else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Tab) { _focus = 0; e.Use(); }
            else if (e.character != '\0' && !char.IsControl(e.character) && value.Length < max) { value += e.character; e.Use(); }
        }
        Panel(r, focused ? _fieldFocusTex : _fieldTex);
        var caret = focused && (int)(Time.unscaledTime * 2) % 2 == 0 ? "|" : "";
        GUI.Label(new Rect(r.x + 6, r.y + 2, r.width - 10, r.height), value + caret, _label);
        return value;
    }

    /// <summary>Com um campo de texto em foco, o jogo não deve reagir às teclas (ex.: andar com WASD).</summary>
    public static bool TypingInMenu { get; private set; }

    private void DrawSetupPanel(bool inGame)
    {
        _nameField ??= Plugin.PlayerName.Value;
        _ipField ??= Plugin.NetJoinAddress.Value;
        var ips = LocalIps();
        var mode = inGame ? (NetSession.Current == NetSession.Mode.Offline ? "choose" : NetSession.Current.ToString()) : Plugin.NetAutoStart.Value;
        var showIp = mode == "join" || mode == "choose";
        var showHostIps = mode == "host" || mode == "choose" || mode == "Host";
        var w = 460f;
        var joinNow = !inGame && mode == "join";
        var h = 150f + (showIp ? 60 : 0) + (joinNow ? 70 : 0) + (showHostIps ? 24 + 22 * ips.Length : 0) + 50;
        // Título: canto inferior direito (o logo ocupa o topo/centro). Jogo: abaixo do relógio/ícones.
        var rect = inGame ? new Rect(Screen.width - w - 24, 260, w, h) : new Rect(Screen.width - w - 40, Screen.height - h - 110, w, h);
        Panel(rect, _panelBg);
        _x = rect.x + 16; _y = rect.y + 10; _w = w - 32;

        if (Button(new Rect(rect.xMax - 16 - 120, rect.y + 12, 120, 26), "Gerar log", _button)) TryRun(MakeReport);
        Label(inGame ? "Multiplayer   [F9 fecha]" : "Multiplayer", _title, 30);

        GUI.Label(new Rect(_x, _y + 2, 95, 24), "Seu nome:", _label);
        _nameField = TextBox(1, new Rect(_x + 100, _y, _w - 100, 26), _nameField ?? "", 24);
        if (_nameField != Plugin.PlayerName.Value) Plugin.PlayerName.Value = _nameField;
        Row(34);

        if (!inGame)
        {
            Label("Modo:", _label);
            var bw = (_w - 16) / 3f;
            ModeButton(new Rect(_x, _y, bw, 32), "Sozinho", "off");
            ModeButton(new Rect(_x + bw + 8, _y, bw, 32), "Hospedar", "host");
            ModeButton(new Rect(_x + 2 * (bw + 8), _y, bw, 32), "Entrar", "join");
            Row(40);
        }
        else
        {
            Label(StatusLine(), _label, 24);
            Row(30);
        }

        if (showIp)
        {
            GUI.Label(new Rect(_x, _y + 2, 95, 24), "IP do host:", _label);
            _ipField = TextBox(2, new Rect(_x + 100, _y, _w - 100, 26), _ipField ?? "", 64);
            if (_ipField.Trim() != Plugin.NetJoinAddress.Value) Plugin.NetJoinAddress.Value = _ipField.Trim();
            Row(30);
            Label("Mesma rede: IP local do host. Internet: IP do host no Radmin VPN (26.x).", _small, 30);
        }
        if (joinNow)
        {
            // D72: entrar daqui, sem save. Ao ser aceito, o mundo do host chega pela rede e é carregado.
            if (NetSession.Current == NetSession.Mode.Offline)
            {
                if (Button(new Rect(_x, _y, _w, 36), "Entrar agora", _button))
                    TryRun(() => NetSession.Join(Plugin.NetJoinAddress.Value, Plugin.NetPort.Value));
                Row(40);
                Label(OfflineReason() is { } why ? "Não conectou: " + why : "Não precisa carregar save. Na primeira vez você cria o seu personagem.", _small, 26);
            }
            else
            {
                Label(NetSession.LocalId == NetSession.UnknownId ? $"Conectando a {Plugin.NetJoinAddress.Value}..." : $"Entrando no mundo de {HostName()}...", _label, 26);
                if (Button(new Rect(_x, _y, _w, 32), "Cancelar", _button)) NetSession.Stop();
                Row(40);
            }
        }
        if (showHostIps)
        {
            Label("Passe um destes IPs para os amigos:", _small, 24);
            foreach (var ip in ips) Label("   " + ip, _label);
        }

        _y = rect.yMax - 46;
        if (inGame)
        {
            var bw = (_w - 8) / 2f;
            if (NetSession.Current == NetSession.Mode.Offline)
            {
                if (Button(new Rect(_x, _y, bw, 34), "Hospedar agora", _button)) TryRun(() => NetSession.Host(Plugin.NetPort.Value));
                if (Button(new Rect(_x + bw + 8, _y, bw, 34), "Entrar agora", _button)) TryRun(() => NetSession.Join(Plugin.NetJoinAddress.Value, Plugin.NetPort.Value));
            }
            else
            {
                if (DaySync.LocalSleeping && Button(new Rect(_x, _y - 42, _w, 34), "Levantar da cama", _button)) DaySync.WakeUp();
                // D72: a criação de personagem completa do jogo, sem jogo novo.
                if (Button(new Rect(_x, _y, bw, 34), "Mudar visual", _button))
                {
                    if (CharacterCreator.CanOpen()) { _menuOpen = false; CharacterCreator.Open(); }
                    else GameNotify.Show("Agora não dá para mudar o visual (feche menus e saia da cama).");
                }
                if (Button(new Rect(_x + bw + 8, _y, bw, 34), "Desconectar", _button)) NetSession.Stop();
            }
        }
        else
        {
            Label(Plugin.NetAutoStart.Value switch
            {
                "host" => "Carregue o seu save: os amigos poderão entrar no seu mundo.",
                "join" => "Ou carregue um save seu: você entra no mundo do host.",
                _ => "Modo sozinho: o mod não conecta a ninguém.",
            }, _small, 40);
        }
    }

    private void ModeButton(Rect r, string text, string value)
    {
        var on = Plugin.NetAutoStart.Value == value;
        if (Button(r, text, on ? _buttonOn : _button)) Plugin.NetAutoStart.Value = value;
    }

    private static void MakeReport()
    {
        var path = BugReport.Create();
        GameNotify.Show($"Log salvo na Área de Trabalho: {System.IO.Path.GetFileName(path)}");
        Notice.Show($"Log salvo: {path}", 12f);
    }

    private static void TryRun(Action a)
    {
        try { a(); }
        catch (Exception e) { GameNotify.Show("Erro: " + e.Message); }
    }

    private static string StatusLine() => NetSession.Current switch
    {
        NetSession.Mode.Host => $"Hospedando ({NetSession.Players.Count}/{Protocol.MaxPlayers} jogadores)",
        NetSession.Mode.Client => $"No mundo de {HostName()}",
        _ => OfflineReason() is { } why ? "Não conectou: " + why : "Desconectado (jogando sozinho)",
    };

    /// <summary>Por que a última tentativa de entrar não deu certo, em palavras de jogador (nulo = nada a dizer).</summary>
    private static string OfflineReason()
    {
        var e = NetSession.LastError;
        if (string.IsNullOrEmpty(e) || e == "DisconnectPeerCalled") return null; // saiu porque quis
        if (e.StartsWith("recusado: ")) return e.Substring(10); // ex.: versão do mod diferente
        return e switch
        {
            "ConnectionFailed" => "o host não respondeu (confira o IP e o Radmin)",
            "Timeout" => "a conexão caiu (tempo esgotado)",
            "RemoteConnectionClose" => "o host fechou a conexão",
            _ => e,
        };
    }

    private static string HostName() =>
        NetSession.Players.TryGetValue(Protocol.HostPlayerId, out var h) ? h.Name : "host";

    // ---------- HUD dentro do jogo ----------

    private void DrawHud()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return;
        var lines = new List<string> { StatusLine() };
        var localField = FieldManager.IsInstance ? FieldManager.Instance.CurrentFieldId : 0u;
        foreach (var p in NetSession.Players.Values.OrderBy(x => x.Id))
        {
            var isMe = p.Id == NetSession.LocalId;
            var field = isMe ? localField : PlayerSync.RemoteField(p.Id);
            var where = field == 0 ? "..." : Locations.Name(field);
            var ping = isMe ? "" : NetSession.PingOf(p.Id) is int ms ? $"  {ms} ms" : "";
            var same = !isMe && field == localField && field != 0 ? " (aqui)" : "";
            lines.Add($"{(isMe ? "▶" : "•")} {p.Name}{(isMe ? " (você)" : "")} — {where}{same}{ping}");
        }
        if (DaySync.LocalSleeping || DaySync.SleepingCount > 0)
            lines.Add($"Dormindo {DaySync.SleepingCount}/{DaySync.Total}{(DaySync.LocalSleeping ? " — esperando os outros  [Backspace] levantar  [F7] espectar" : "")}");
        if (Notice.Current != null) lines.Add(Notice.Current);
        lines.Add("[F9] menu   [F10] esconder");

        var text = string.Join("\n", lines);
        var h = 18f * lines.Count + 20;
        var rect = new Rect(16, Screen.height - h - 150, 420, h);
        Panel(rect, _panelBg);
        GUI.Label(new Rect(rect.x + 12, rect.y + 8, rect.width - 20, h - 12), text, _label);
    }

    private GUIStyle _tag, _tagShadow;

    /// <summary>Nome de cada jogador acima do avatar (com sombra, para ler sobre qualquer fundo).</summary>
    private void DrawNameTags()
    {
        if (NetSession.Current == NetSession.Mode.Offline) return;
        // Em menu, diálogo ou evento os nomes ficariam por cima das janelas do jogo (visto no teste da cama).
        if (Net.ClockSync.LocalPaused) return;
        foreach (var (id, p) in PlayerSync.VisibleHeads())
        {
            var name = NetSession.Players.TryGetValue(id, out var pl) ? pl.Name : $"Jogador {id}";
            var r = new Rect(p.x - 120, p.y - 14, 240, 28);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), name, _tagShadow);
            GUI.Label(r, name, _tag);
        }
    }

    // ---------- util ----------

    private static string[] _ipsCache;
    private string[] LocalIps()
    {
        if (_localIps != null && Time.unscaledTime < _nextIpRefresh) return _localIps;
        _nextIpRefresh = Time.unscaledTime + 10f;
        try
        {
            // Laços simples: lambdas aninhadas aqui esbarram num atributo de nulabilidade ausente no build.
            var list = new List<string>();
            foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var a in n.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = a.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;
                    var kind = Describe(n);
                    if (kind == null) continue; // adaptadores virtuais do Windows (Hyper-V/WSL): só confundem
                    list.Add($"{ip}  ({kind})");
                }
            }
            _localIps = list.ToArray();
        }
        catch { _localIps = new[] { "(não foi possível listar os IPs)" }; }
        return _localIps;
    }

    private static string Describe(NetworkInterface n)
    {
        var d = (n.Description + " " + n.Name).ToLowerInvariant();
        if (d.Contains("radmin")) return "Radmin VPN";
        if (d.Contains("zerotier")) return "ZeroTier";
        if (d.Contains("tailscale")) return "Tailscale";
        if (d.Contains("hyper-v") || d.Contains("vethernet") || d.Contains("wsl") || d.Contains("virtualbox") || d.Contains("vmware")) return null;
        return n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "rede local";
    }

    private static Texture2D _panelBg, _fieldTex, _fieldFocusTex, _hoverTex;
    private GUIStyle _btnText, _btnTextOn;

    /// <summary>
    /// Caixa de fundo. Neste jogo GUI.DrawTexture foi removido no build e GUIStyle.background não é
    /// desenhado; o que funciona é GUI.Box com o estilo PADRÃO. Tingimos com GUI.color e desenhamos
    /// em camadas para ficar opaco.
    /// </summary>
    private static void Panel(Rect r, Texture2D tex)
    {
        var old = GUI.color;
        GUI.color = tex == _hoverTex ? new Color(0.5f, 0.45f, 0.4f, 1f)
                  : tex == _fieldFocusTex ? new Color(1f, 0.75f, 0.35f, 1f)
                  : tex == _fieldTex ? new Color(0.2f, 0.2f, 0.2f, 1f)
                  : new Color(0.75f, 0.55f, 0.35f, 1f);
        for (var i = 0; i < (tex == _panelBg ? 3 : 2); i++) GUI.Box(r, "");
        GUI.color = old;
    }

    private static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    private void EnsureStyles()
    {
        if (_stylesReady) return;
        var bg = Solid(new Color(0.18f, 0.13f, 0.09f, 0.88f)); // marrom escuro, combina com a UI do jogo
        _panelBg = bg;
        _fieldTex = Solid(new Color(0f, 0f, 0f, 0.5f));
        _fieldFocusTex = Solid(new Color(0.45f, 0.32f, 0.12f, 0.95f));
        _hoverTex = Solid(new Color(0.3f, 0.3f, 0.3f, 0.9f));
        _box = new GUIStyle(GUI.skin.box);
        _box.normal.background = bg;
        _title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
        _title.normal.textColor = new Color(1f, 0.93f, 0.75f);
        _label = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        _label.normal.textColor = Color.white;
        _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
        _small.normal.textColor = new Color(0.9f, 0.85f, 0.75f);
        _button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 32 };
        _buttonOn = new GUIStyle(_button) { fontStyle = FontStyle.Bold };
        _buttonOn.normal.textColor = new Color(1f, 0.85f, 0.3f);
        _field = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        _btnText = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
        _btnText.normal.textColor = Color.white;
        _btnTextOn = new GUIStyle(_btnText) { fontStyle = FontStyle.Bold };
        _btnTextOn.normal.textColor = new Color(1f, 0.92f, 0.6f);
        _fieldBg = new GUIStyle(GUI.skin.box);
        _fieldBg.normal.background = Solid(new Color(0f, 0f, 0f, 0.45f));
        _fieldFocus = new GUIStyle(GUI.skin.box);
        _fieldFocus.normal.background = Solid(new Color(0.35f, 0.25f, 0.1f, 0.9f));
        _tag = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _tag.normal.textColor = new Color(1f, 0.95f, 0.7f);
        _tagShadow = new GUIStyle(_tag);
        _tagShadow.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        _stylesReady = true;
    }
}
