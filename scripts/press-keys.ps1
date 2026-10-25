# 🤖 AUTODOCUMENTAÇÃO: ver scripts/CONTEXTO.md.
# Aperta teclas DE VERDADE na janela de uma instância do jogo (traz a janela para frente e usa
# SendInput com scancode, que o Unity InputSystem lê como teclado real). Serve para a IA testar
# coisas que exigem andar/agir (ex.: animação do avatar remoto). ATENÇÃO: rouba o foco da tela.
# Uso: powershell -File press-keys.ps1 -ProcessId <pid> -Keys "W" -Milliseconds 1500
#      -Keys aceita várias separadas por vírgula, apertadas juntas (ex.: "W,D").
param([int]$ProcessId, [string]$Keys = "W", [int]$Milliseconds = 1000, [string]$Text = "", [int]$Backspaces = 0)
# -Text "192.168.0.5" digita caractere por caractere (dígitos, ponto, letras); -Backspaces N apaga antes.
Add-Type @"
using System; using System.Text; using System.Runtime.InteropServices;
public class SosgbKeys {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
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
$scan = @{ "BACK"=0x0E; "."=0x34; "1"=0x02; "2"=0x03; "3"=0x04; "4"=0x05; "5"=0x06; "6"=0x07; "7"=0x08; "8"=0x09; "9"=0x0A; "0"=0x0B; "B"=0x30; "F"=0x21; "G"=0x22; "H"=0x23; "I"=0x17; "J"=0x24; "K"=0x25; "L"=0x26; "N"=0x31; "O"=0x18; "P"=0x19; "R"=0x13; "T"=0x14; "U"=0x16; "V"=0x2F; "X"=0x2D; "Y"=0x15; "Z"=0x2C; "F8"=0x42; "F9"=0x43; "F10"=0x44; "W"=0x11; "A"=0x1E; "S"=0x1F; "D"=0x20; "SPACE"=0x39; "SHIFT"=0x2A; "E"=0x12; "Q"=0x10; "C"=0x2E; "ESC"=0x01; "ENTER"=0x1C; "M"=0x32 }
$h = [SosgbKeys]::Find([uint32]$ProcessId)
if ($h -eq [IntPtr]::Zero) { Write-Output "janela não encontrada"; exit 1 }
[SosgbKeys]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [SosgbKeys]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
[SosgbKeys]::ShowWindow($h, 9) | Out-Null
[SosgbKeys]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 300
function Tap($k) { [SosgbKeys]::keybd_event(0, [byte]$scan[$k], 0x0008, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40; [SosgbKeys]::keybd_event(0, [byte]$scan[$k], 0x000A, [UIntPtr]::Zero); Start-Sleep -Milliseconds 40 }
if ($Backspaces -gt 0 -or $Text -ne "") {
  for ($i = 0; $i -lt $Backspaces; $i++) { Tap "BACK" }
  foreach ($c in $Text.ToUpper().ToCharArray()) { Tap ([string]$c) }
  Write-Output "digitado: $Text"; exit 0
}
$list = $Keys.Split(",") | ForEach-Object { $_.Trim().ToUpper() }
foreach ($k in $list) { [SosgbKeys]::keybd_event(0, [byte]$scan[$k], 0x0008, [UIntPtr]::Zero) }          # KEYEVENTF_SCANCODE
Start-Sleep -Milliseconds $Milliseconds
foreach ($k in $list) { [SosgbKeys]::keybd_event(0, [byte]$scan[$k], 0x0008 -bor 0x0002, [UIntPtr]::Zero) } # + KEYUP
Write-Output "teclas $Keys por $Milliseconds ms"
