// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d21.
using System;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Complementa o AppManagerPatch (D21): garante Application.runInBackground = true para a Unity
/// não congelar o player loop quando a janela perde o foco (host dando alt-tab, duas instâncias
/// no mesmo PC, testes da IA). Verificado em runtime: com isso + o patch, o relógio continua
/// andando sem foco (timeScale 1).
/// Observação: a tela inicial (Launcher) ainda exige UMA ativação real da janela ao abrir sem foco;
/// isso só acontece em aberturas por script e é resolvido por scripts/kick-window.ps1.
/// </summary>
public class FocusGuard : MonoBehaviour
{
    public FocusGuard(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        if (Plugin.KeepRunningWhenUnfocused.Value && !Application.runInBackground)
            Application.runInBackground = true;
    }
}
