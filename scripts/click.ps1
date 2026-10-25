# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md.
# Clica com o mouse DE VERDADE num ponto da janela do jogo (coordenadas da área do jogo, em pixels,
# origem no canto superior esquerdo). Traz a janela para frente antes. Rouba o foco/mouse da tela.
# Uso: powershell -File click.ps1 -ProcessId <pid> -X 1795 -Y 813
param([int]$ProcessId, [int]$X, [int]$Y, [int]$HoldMs = 60)
# -HoldMs: quanto tempo o botão fica apertado (o jogo pode precisar de mais de um quadro, ex.: usar ferramenta).
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class SosgbMouse {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref POINT p);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  public static IntPtr Find(uint pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p != pid) return true;
      var sb = new StringBuilder(64); GetClassName(h, sb, 64);
      if (sb.ToString() == "UnityWndClass") { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
}
"@
$h = [SosgbMouse]::Find([uint32]$ProcessId)
if ($h -eq [IntPtr]::Zero) { Write-Output "janela não encontrada"; exit 1 }
[SosgbMouse]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [SosgbMouse]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[SosgbMouse]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 250
$p = New-Object SosgbMouse+POINT; $p.X = $X; $p.Y = $Y
[SosgbMouse]::ClientToScreen($h, [ref]$p) | Out-Null
[SosgbMouse]::SetCursorPos($p.X, $p.Y) | Out-Null
Start-Sleep -Milliseconds 120
[SosgbMouse]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds $HoldMs
[SosgbMouse]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
Write-Output "clique em ($X,$Y) -> tela ($($p.X),$($p.Y))"
