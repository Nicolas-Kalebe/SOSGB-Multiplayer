# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md e DECISOES.md#d21.
# Quando o jogo é aberto por script (sem foco), a tela inicial (Launcher) não avança até a janela
# receber uma ativação real do Windows, como um alt-tab. Mensagens WM_ACTIVATE não bastam.
# ATENÇÃO: o processo tem DUAS janelas (jogo + console do BepInEx). Process.MainWindowHandle
# pode apontar para o console, então procuramos a janela da classe "UnityWndClass" do processo.
param([int]$Mode = 0)  # 0 = ativar de verdade (traz para frente e devolve o foco); 1 = só minimizar/restaurar
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class SosgbWin {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static IntPtr FindUnityWindow(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p != pid) return true;
      var sb = new StringBuilder(64); GetClassName(h, sb, 64);
      if (sb.ToString() == "UnityWndClass") { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
"@
$p = Get-Process SOSGrandBazaar -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "jogo não está aberto"; exit 1 }
$h = [SosgbWin]::FindUnityWindow([uint32]$p.Id)
if ($h -eq [IntPtr]::Zero) { Write-Output "janela UnityWndClass não encontrada"; exit 1 }

$prev = [SosgbWin]::GetForegroundWindow()
if ($Mode -eq 1) {
  [SosgbWin]::ShowWindow($h, 6) | Out-Null; Start-Sleep -Milliseconds 400
  [SosgbWin]::ShowWindow($h, 9) | Out-Null; Start-Sleep -Milliseconds 400
} else {
  # O Windows só deixa trocar o foco se "acabou de haver input"; um toque de ALT libera.
  [SosgbWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [SosgbWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [SosgbWin]::SetForegroundWindow($h) | Out-Null
  Start-Sleep -Milliseconds 800
}
if ($prev -ne $h -and $prev -ne [IntPtr]::Zero) {
  [SosgbWin]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [SosgbWin]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
  [SosgbWin]::SetForegroundWindow($prev) | Out-Null
}
Write-Output "janela do jogo ativada (handle $h)"
