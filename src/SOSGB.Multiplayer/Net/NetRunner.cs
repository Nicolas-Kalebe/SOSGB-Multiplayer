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

    /// <summary>
    /// Tarefa 2.6: hospeda ou entra automaticamente quando um save é carregado. Vale a escolha FEITA NO PAINEL do título
    /// (antes o modo era lido uma vez ao abrir o jogo e "Sozinho" decidia no primeiro quadro: trocar no título só valia
    /// depois de reiniciar). Voltar ao título libera de novo para o próximo save.
    /// </summary>
    private void TryAutoStart()
    {
        if (!BokuMono.SceneManager.IsInstance) return;
        var scene = BokuMono.SceneManager.Instance.CurrentSceneMode;
        if (scene == BokuMono.SceneMode.Title) { _autoStartDone = false; return; }
        if (_autoStartDone || scene != BokuMono.SceneMode.Game) return;
        if (!BokuMono.GameController.IsInstance || BokuMono.GameController.Instance.GetPlayerTransform() == null) return;

        _autoStartDone = true;
        if (NetSession.Current != NetSession.Mode.Offline) return; // já conectado (ex.: F9 antes de carregar)
        var mode = Plugin.EffectiveNetAutoStart;
        if (mode == "off") return;
        if (mode == "host") NetSession.Host(Plugin.NetPort.Value);
        else if (mode == "join") NetSession.Join(Environment.GetEnvironmentVariable("SOSGB_JOIN_ADDRESS") ?? Plugin.NetJoinAddress.Value, Plugin.NetPort.Value);
        Plugin.Logger.LogInfo($"[Net] AutoStart = {mode}");
    }

    private void OnDestroy() => NetSession.Stop();
}
