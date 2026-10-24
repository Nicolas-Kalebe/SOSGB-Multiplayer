// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md.
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace SOSGB.Multiplayer;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.nicolaskalebe.sosgb.multiplayer";
    public const string Name = "SOSGB Multiplayer";
    public const string Version = "0.0.1";

    internal static ManualLogSource Logger;

    public override void Load()
    {
        Logger = Log;
        Logger.LogInfo($"{Name} {Version} carregado.");

        // Passo 4 do PLANO §7: provar que conseguimos ler o estado do jogo por dentro.
        AddComponent<DebugOverlay>();
    }
}
