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
    public const string Version = "0.1.0-dev";

    internal static ManualLogSource Logger;

    // Configuração em BepInEx/config/com.nicolaskalebe.sosgb.multiplayer.cfg
    internal static ConfigEntry<bool> KeepRunningWhenUnfocused;
    internal static ConfigEntry<string> PlayerName;
    internal static ConfigEntry<string> PlayerGuid;
    internal static ConfigEntry<int> RecoverySnapshotMinutes;
    internal static ConfigEntry<int> PauseWhenPlayersInMenu;
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
    internal static string EffectiveNetAutoStart;
    internal static string InstanceTag = "";
    internal static string EffectivePlayerGuid;

    private static int? EnvInt(string name) =>
        int.TryParse(System.Environment.GetEnvironmentVariable(name), out var v) ? v : null;

    public override void Load()
    {
        Logger = Log;

        // D21: o jogo se pausa ao perder o foco; no multiplayer isso congelaria todos.
        KeepRunningWhenUnfocused = Config.Bind("Game", "KeepRunningWhenUnfocused", true,
            "Mantém o jogo rodando quando a janela perde o foco (necessário no multiplayer).");

        // D9: o relógio do grupo pausa quando N jogadores estão em menu/evento (0 = todos).
        PauseWhenPlayersInMenu = Config.Bind("Game", "PauseWhenPlayersInMenu", 1,
            "Multiplayer: o tempo para quando este número de jogadores estiver em menu ou evento. 1 = qualquer um pausa todos; 0 = só quando todos estiverem.");

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
        EffectiveNetAutoStart = System.Environment.GetEnvironmentVariable("SOSGB_NET_AUTOSTART") ?? NetAutoStart.Value;
        InstanceTag = System.Environment.GetEnvironmentVariable("SOSGB_BRIDGE_PORT") != null ? $"-{BridgePort}" : "";

        Logger.LogInfo($"{Name} {Version} carregado (instância{(InstanceTag == "" ? " padrão" : InstanceTag)}).");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        AddComponent<FocusGuard>();
        AddComponent<Net.NetRunner>();
        AddComponent<Net.PlayerSync>();
        Net.WorldSync.Init();
        AddComponent<Net.ClockSync>();
        AddComponent<Net.DaySync>();
        AddComponent<Net.PlayerProfiles>();
        AddComponent<DebugOverlay>();

        if (DevBridgeEnabled.Value)
        {
            DevBridge.Start(BridgePort);
            AddComponent<DevBridge>();
        }
    }
}
