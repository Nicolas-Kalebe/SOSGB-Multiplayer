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
    public const string Version = "0.0.3";

    internal static ManualLogSource Logger;

    // Configuração em BepInEx/config/com.nicolaskalebe.sosgb.multiplayer.cfg
    internal static ConfigEntry<bool> KeepRunningWhenUnfocused;
    internal static ConfigEntry<bool> DevBridgeEnabled;
    internal static ConfigEntry<int> DevBridgePort;
    internal static ConfigEntry<int> AutoLoadSlot;
    internal static ConfigEntry<float> StateLogInterval;

    public override void Load()
    {
        Logger = Log;

        // D21: o jogo se pausa ao perder o foco; no multiplayer isso congelaria todos.
        KeepRunningWhenUnfocused = Config.Bind("Game", "KeepRunningWhenUnfocused", true,
            "Mantém o jogo rodando quando a janela perde o foco (necessário no multiplayer).");

        // D20: ponte de teste desligada por padrão; só o PC de desenvolvimento liga.
        DevBridgeEnabled = Config.Bind("DevBridge", "Enabled", false,
            "Ponte de teste para IA (só desenvolvimento). Abre um servidor em 127.0.0.1.");
        DevBridgePort = Config.Bind("DevBridge", "Port", 47800, "Porta TCP local da ponte de teste.");
        AutoLoadSlot = Config.Bind("DevBridge", "AutoLoadSlot", -1,
            "Slot (0-2) carregado automaticamente na tela de título. -1 desliga.");
        StateLogInterval = Config.Bind("DevBridge", "StateLogIntervalSeconds", 2f,
            "Intervalo de gravação do BepInEx/sosgb-state.jsonl.");

        Logger.LogInfo($"{Name} {Version} carregado.");

        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        AddComponent<FocusGuard>();
        AddComponent<DebugOverlay>();

        if (DevBridgeEnabled.Value)
        {
            DevBridge.Start(DevBridgePort.Value);
            AddComponent<DevBridge>();
        }
    }
}
