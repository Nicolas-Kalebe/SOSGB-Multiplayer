// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/Net/CONTEXTO.md.
using System;
using UnityEngine;

namespace SOSGB.Multiplayer.Net;

/// <summary>Processa os eventos de rede na thread principal da Unity, a cada frame.</summary>
public class NetRunner : MonoBehaviour
{
    public NetRunner(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        try { NetSession.Poll(); }
        catch (Exception e) { Plugin.Logger.LogError($"[Net] poll: {e}"); }
    }

    private void OnDestroy() => NetSession.Stop();
}
