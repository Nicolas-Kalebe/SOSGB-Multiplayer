// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md.
using System;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>Processa os eventos de rede na thread principal da Unity, a cada frame.</summary>
public class NetRunner : MonoBehaviour
{
    public NetRunner(IntPtr ptr) : base(ptr) { }

    private bool _autoStartDone;

    private void Update()
    {
        try
        {
            TryAutoStart();
            NetSession.Poll();
        }
        catch (Exception e) { Plugin.Logger.LogError($"[Net] {e}"); }
    }

    /// <summary>Tarefa 2.6: hospeda ou entra automaticamente na primeira vez que um save é carregado.</summary>
    private void TryAutoStart()
    {
        if (_autoStartDone) return;
        var mode = Plugin.EffectiveNetAutoStart;
        if (mode == "off") { _autoStartDone = true; return; }
        if (!BokuMono.SceneManager.IsInstance || BokuMono.SceneManager.Instance.CurrentSceneMode != BokuMono.SceneMode.Game) return;
        if (!BokuMono.GameController.IsInstance || BokuMono.GameController.Instance.GetPlayerTransform() == null) return;

        _autoStartDone = true;
        if (mode == "host") NetSession.Host(Plugin.NetPort.Value);
        else if (mode == "join") NetSession.Join(Plugin.NetJoinAddress.Value, Plugin.NetPort.Value);
        Plugin.Logger.LogInfo($"[Net] AutoStart = {mode}");
    }

    private void OnDestroy() => NetSession.Stop();
}
