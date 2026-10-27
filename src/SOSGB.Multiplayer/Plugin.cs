// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md.
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace SOSGB.Multiplayer;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.nicolaskalebe.sosgb.multiplayer";
    public const string Name = "SOSGB Multiplayer";
    public const string Version = "0.6.3";

    internal static ManualLogSource Logger;

    // Configuração em BepInEx/config/com.nicolaskalebe.sosgb.multiplayer.cfg
    internal static ConfigEntry<bool> KeepRunningWhenUnfocused;
    internal static ConfigEntry<bool> SkipIntroLogos;
    internal static ConfigEntry<bool> WatchFestivalScenes;
    internal static ConfigEntry<bool> FixStartupFocus;
    internal static ConfigEntry<bool> OwnAppearance;
    internal static ConfigEntry<string> PlayerName;
    internal static ConfigEntry<string> PlayerGuid;
    internal static ConfigEntry<int> RecoverySnapshotMinutes;
    internal static ConfigEntry<int> NetPort;
    internal static ConfigEntry<string> NetAutoStart;
    internal static ConfigEntry<string> NetJoinAddress;
    internal static ConfigEntry<bool> DevBridgeEnabled;
    internal static ConfigEntry<int> DevBridgePort;
    internal static ConfigEntry<int> AutoLoadSlot;
    internal static ConfigEntry<float> StateLogInterval;
    internal static ConfigEntry<bool> BlockDiskSaves;

    // Valores efetivos (config ou variável de ambiente; ver tarefa 1.6).
    internal static int BridgePort;
    internal static int EffectiveAutoLoadSlot;
    /// <summary>Modo ao carregar um save: variável de teste ou o valor ATUAL do .cfg (o painel do título muda na hora).</summary>
    internal static string EffectiveNetAutoStart => System.Environment.GetEnvironmentVariable("SOSGB_NET_AUTOSTART") ?? NetAutoStart.Value;
    internal static string InstanceTag = "";
    internal static string EffectivePlayerGuid;

    /// <summary>
    /// Ponte de teste ativa (D20): pelo .cfg ([DevBridge] Enabled) OU pela variável de ambiente
    /// SOSGB_DEVBRIDGE=1 (usada pelos scripts/). Assim o .cfg do jogador fica em modo de jogo normal
    /// e só as execuções abertas pelos scripts entram em modo de teste.
    /// </summary>
    internal static bool DevMode;
    internal static bool EffectiveFixStartup;

    private static int? EnvInt(string name) =>
        int.TryParse(System.Environment.GetEnvironmentVariable(name), out var v) ? v : null;

    public override void Load()
    {
        Logger = Log;

        // D21: o jogo se pausa ao perder o foco; no multiplayer isso congelaria todos.
        KeepRunningWhenUnfocused = Config.Bind("Game", "KeepRunningWhenUnfocused", true,
            "Mantém o jogo rodando quando a janela perde o foco (necessário no multiplayer).");

        // D53: pula as logos de abertura (pedido do usuário, para testar e entrar mais rápido).
        SkipIntroLogos = Config.Bind("Game", "SkipIntroLogos", true,
            "Pula as logos de abertura (Marvelous, CRIWARE...) e vai direto para a tela inicial.");

        // D84: quem está na praça assiste junto à cena do festival de quem se inscreveu.
        // Desligado por padrão: a cena aberta por fora do fluxo de conversa não carrega e o personagem fica preso (D84, em aberto).
        WatchFestivalScenes = Config.Bind("Multiplayer", "WatchFestivalScenes", false,
            "EXPERIMENTAL (não ligar): assistir junto à cena do festival quando outro jogador começa a cerimônia no mesmo mapa.");

        // D25: o jogo trava na tela inicial se a janela nasce inativa (o console do BepInEx rouba o foco).
        FixStartupFocus = Config.Bind("Game", "FixStartupFocus", true,
            "Ativa a janela do jogo automaticamente se ele ficar preso na tela inicial (sem precisar clicar/alt-tab).");
        // Só para testes (scripts): SOSGB_FIX_STARTUP=0 desliga nesta execução, sem gravar no .cfg.
        EffectiveFixStartup = FixStartupFocus.Value && System.Environment.GetEnvironmentVariable("SOSGB_FIX_STARTUP") != "0";

        // D28/D32: aparência própria de cada jogador (rosto, cabelo, roupa, gênero). A D29 tinha desligado por
        // travar; a causa era um id de peça inválido e a cópia feita com o modelo escondido (corrigido na D32).
        // Chave nova (a antiga "SyncAppearance" ficou false nos .cfg já criados). Desligada = todos com o modelo
        // do jogador local + nome em cima.
        OwnAppearance = Config.Bind("Visual", "OwnAppearance", true,
            "Mostra cada jogador com a própria aparência (rosto, cabelo, roupa, gênero). Ao montar a aparência de quem entrou, o seu personagem pisca por 1 a 2 segundos.");

        // D9/D50: o tempo do grupo só para no menu de pausa do jogo (pedido do usuário); a regra "N jogadores em menu" saiu.

        // Rede (Fase 2). Nome e porta; o menu no jogo vem na Fase 8.
        PlayerName = Config.Bind("Net", "PlayerName", "", "Seu nome no multiplayer (vazio = nome de usuário do Windows).");
        NetPort = Config.Bind("Net", "Port", Net.Protocol.DefaultPort, "Porta UDP do host.");
        // D23: o save oficial é só na virada do dia; isto é só uma cópia de segurança contra queda do host.
        RecoverySnapshotMinutes = Config.Bind("Net", "RecoverySnapshotMinutes", 15,
            "Host: a cada X minutos grava uma cópia de recuperação do mundo (arquivo à parte, não é o save oficial). 0 = desligado.");
        // 3.6: identidade estável do jogador (o host guarda o perfil por ela). Gerada na primeira vez.
        PlayerGuid = Config.Bind("Net", "PlayerGuid", "", "Identificador deste jogador (gerado automaticamente; não edite).");
        if (string.IsNullOrEmpty(PlayerGuid.Value)) PlayerGuid.Value = System.Guid.NewGuid().ToString("N");
        // Várias instâncias no mesmo PC: a variável de ambiente vale só para esta execução (não é gravada no .cfg).
        var envGuid = System.Environment.GetEnvironmentVariable("SOSGB_PLAYER_GUID");
        EffectivePlayerGuid = string.IsNullOrEmpty(envGuid) ? PlayerGuid.Value : envGuid;
        // Tarefa 2.6: até existir menu no jogo (Fase 8), hospedar/entrar é configurado aqui.
        NetAutoStart = Config.Bind("Net", "AutoStart", "off",
            new ConfigDescription("Ao carregar um save: 'off' (nada), 'host' (hospedar) ou 'join' (entrar em JoinAddress).",
                new AcceptableValueList<string>("off", "host", "join")));
        NetJoinAddress = Config.Bind("Net", "JoinAddress", "127.0.0.1",
            "IP do host para AutoStart = join (na LAN ou na LAN virtual: Radmin/ZeroTier/Tailscale).");

        // D20: ponte de teste desligada por padrão; só o PC de desenvolvimento liga.
        DevBridgeEnabled = Config.Bind("DevBridge", "Enabled", false,
            "Ponte de teste para IA (só desenvolvimento). Abre um servidor em 127.0.0.1.");
        DevBridgePort = Config.Bind("DevBridge", "Port", 47800, "Porta TCP local da ponte de teste.");
        AutoLoadSlot = Config.Bind("DevBridge", "AutoLoadSlot", -1,
            "Slot (0-2) carregado automaticamente na tela de título. -1 desliga.");
        BlockDiskSaves = Config.Bind("DevBridge", "BlockDiskSaves", true,
            "Com a ponte ligada, impede o jogo de gravar saves no disco (testes nunca alteram os saves).");
        StateLogInterval = Config.Bind("DevBridge", "StateLogIntervalSeconds", 2f,
            "Intervalo de gravação do BepInEx/sosgb-state.jsonl.");

        // Tarefa 1.6: várias instâncias no mesmo PC compartilham a mesma pasta e o mesmo .cfg.
        // Variáveis de ambiente permitem que cada instância tenha a sua porta e o seu slot.
        BridgePort = EnvInt("SOSGB_BRIDGE_PORT") ?? DevBridgePort.Value;
        EffectiveAutoLoadSlot = EnvInt("SOSGB_AUTOLOAD_SLOT") ?? AutoLoadSlot.Value;
        InstanceTag = System.Environment.GetEnvironmentVariable("SOSGB_BRIDGE_PORT") != null ? $"-{BridgePort}" : "";

        DevMode = DevBridgeEnabled.Value || System.Environment.GetEnvironmentVariable("SOSGB_DEVBRIDGE") == "1";

        BugReport.StartSessionLog(); // D71: cópia do log desta sessão (o LogOutput.log é apagado ao abrir o jogo de novo)
        Logger.LogInfo($"{Name} {Version} carregado (instância{(InstanceTag == "" ? " padrão" : InstanceTag)}).");

        var harmony = new Harmony(Guid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        // Só testes: registro das rotinas da barraca do Bazaar, sob pedido (D56).
        if (DevMode && System.Environment.GetEnvironmentVariable("SOSGB_BAZAAR_DIAG") == "1") Patches.BazaarDiagnosticsPatch.Apply(harmony);
        // Só testes: SOSGB_TEST_UNPATCH="BazaarManager.IsBuyStandby,UIBazaarMainPage.OpenMiniDeck" tira os patches do mod
        // desses métodos (achar qual patch atrapalha o jogo sem recompilar).
        var unpatch = System.Environment.GetEnvironmentVariable("SOSGB_TEST_UNPATCH");
        if (DevMode && !string.IsNullOrWhiteSpace(unpatch))
            foreach (var spec in unpatch.Split(','))
            {
                var dot = spec.Trim().LastIndexOf('.');
                if (dot <= 0) continue;
                var type = AccessTools.TypeByName("BokuMono." + spec.Trim().Substring(0, dot)) ?? AccessTools.TypeByName(spec.Trim().Substring(0, dot));
                var name = spec.Trim().Substring(dot + 1);
                if (type == null) { Logger.LogWarning($"[Teste] tipo não achado: {spec}"); continue; }
                foreach (var m in AccessTools.GetDeclaredMethods(type))
                    if (m.Name == name) { harmony.Unpatch(m, HarmonyPatchType.All, Guid); Logger.LogInfo($"[Teste] patches do mod tirados de {type.Name}.{name}"); }
            }

        AddComponent<FocusGuard>();
        AddComponent<StartupGuard>();
        AddComponent<Net.NetRunner>();
        AddComponent<Net.PlayerSync>();
        Net.WorldSync.Init();
        AddComponent<Net.ClockSync>();
        AddComponent<Net.DaySync>();
        AddComponent<Net.PlayerProfiles>();
        AddComponent<Net.NotifySync>();
        AddComponent<Net.AppearanceSync>();
        AddComponent<Net.MoneySync>();
        AddComponent<Net.MissionSync>();
        AddComponent<Net.StorageSync>();
        AddComponent<Net.LetterSync>();
        AddComponent<Net.FarmSync>();
        AddComponent<Net.AnimalSync>();
        AddComponent<Net.SharedSync>();
        AddComponent<Net.RomanceSync>();
        AddComponent<Net.BazaarViewSync>();
        AddComponent<Net.BazaarHelpSync>();
        AddComponent<Net.NpcSync>();
        AddComponent<Net.Spectator>();
        AddComponent<Net.SleepPoseSync>();
        AddComponent<Net.CharacterCreator>();
        AddComponent<Net.EventWatch>();
        AddComponent<MultiplayerUI>();
        // Janela de diagnóstico (dinheiro/relógio/posição) só no modo de teste (D27).
        if (DevMode) AddComponent<DebugOverlay>();

        if (DevMode)
        {
            DevBridge.Start(BridgePort);
            AddComponent<DevBridge>();
        }
    }
}
