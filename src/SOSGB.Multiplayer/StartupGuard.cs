// 🤖 AUTODOCUMENTAÇÃO: ver src/SOSGB.Multiplayer/CONTEXTO.md e DECISOES.md#d25.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BokuMono;
using UnityEngine;

namespace SOSGB.Multiplayer;

/// <summary>
/// Corrige o "jogo só inicializa depois de clicar / dar alt-tab" (D25).
///
/// Com o BepInEx, o console abre ANTES da janela do jogo e fica com o foco; a janela da Unity nasce
/// inativa e a tela inicial (Launcher) não avança até receber uma ativação real do Windows.
/// Aqui, dentro do próprio processo (que é dono do console em primeiro plano e, por isso, pode
/// trocar o foco), ativamos a janela do jogo enquanto ela estiver presa no Launcher.
/// </summary>
public class StartupGuard : MonoBehaviour
{
    public StartupGuard(IntPtr ptr) : base(ptr) { }

    private float _launcherSince = -1;
    private float _nextAttempt;
    private int _attempts;
    private bool _done;

    private void Update()
    {
        if (_done || !Plugin.EffectiveFixStartup) return;
        try
        {
            var inLauncher = !SceneManager.IsInstance || SceneManager.Instance.CurrentSceneMode == SceneMode.Launcher
                             || SceneManager.Instance.CurrentSceneMode == SceneMode.None;
            if (!inLauncher)
            {
                if (_attempts > 0) Plugin.Logger.LogInfo($"[StartupGuard] tela inicial liberada após {_attempts} ativação(ões)");
                _done = true;
                return;
            }
            if (_launcherSince < 0) { _launcherSince = Time.realtimeSinceStartup; _nextAttempt = _launcherSince + 4f; return; }
            if (Time.realtimeSinceStartup < _nextAttempt || _attempts >= 6) return;
            _nextAttempt = Time.realtimeSinceStartup + 4f;
            // Já em primeiro plano: o Launcher só está carregando (leva ~10 s); não insistir.
            if (GetForegroundWindow() == FindUnityWindow()) return;
            _attempts++;
            // 1ª tentativa: trazer para frente (funciona — testado). Minimizar/restaurar só como reserva.
            Activate(_attempts == 1 ? 1 : 2);
        }
        catch (Exception e)
        {
            Plugin.Logger.LogWarning($"[StartupGuard] {e.Message}");
            _done = true;
        }
    }

    /// <summary>1 = trazer para frente (SetForegroundWindow); 2 = minimizar e restaurar; 3 = só restaurar/mostrar.</summary>
    public static string Activate(int mode)
    {
        var h = FindUnityWindow();
        if (h == IntPtr.Zero) return "janela não encontrada";
        var before = GetForegroundWindow() == h;
        switch (mode)
        {
            case 1:
                keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); // ALT libera a troca de foco
                ShowWindow(h, 9);
                BringWindowToTop(h);
                SetForegroundWindow(h);
                break;
            case 2:
                ShowWindow(h, 6); // minimizar
                ShowWindow(h, 9); // restaurar
                SetForegroundWindow(h);
                break;
            case 3:
                ShowWindow(h, 5);
                break;
        }
        var msg = $"modo {mode}: estava em primeiro plano={before}, agora={GetForegroundWindow() == h}";
        Plugin.Logger.LogInfo($"[StartupGuard] {msg}");
        DevBridge.PushEvent($"startup-kick:{mode}");
        return msg;
    }

    public static IntPtr FindUnityWindow()
    {
        var pid = (uint)Process.GetCurrentProcess().Id;
        var found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var p);
            if (p != pid) return true;
            var sb = new StringBuilder(64);
            GetClassName(h, sb, 64);
            if (sb.ToString() != "UnityWndClass") return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
