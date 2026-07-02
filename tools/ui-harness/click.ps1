# Bring the WinButler window to the foreground and click at window-relative (X,Y).
# Coordinates are relative to the window origin, which shoot.ps1 pins to screen (40,40).
param([int]$X, [int]$Y)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class U {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x,int y,int cx,int cy,uint f);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint dx,uint dy,uint d,IntPtr e);
}
"@
$p = Get-Process WinButler -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "NO APP"; exit 1 }
$h = $p.MainWindowHandle
[U]::ShowWindow($h, 9) | Out-Null
[U]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, 1320, 900, 0x0040) | Out-Null
$fg = [U]::GetForegroundWindow(); $tp = [uint32]0
$t1 = [U]::GetWindowThreadProcessId($fg, [ref]$tp); $t2 = [U]::GetCurrentThreadId()
[U]::AttachThreadInput($t2, $t1, $true) | Out-Null
[U]::BringWindowToTop($h) | Out-Null
[U]::SetForegroundWindow($h) | Out-Null
[U]::AttachThreadInput($t2, $t1, $false) | Out-Null
Start-Sleep -Milliseconds 350
$sx = 40 + $X; $sy = 40 + $Y
[U]::SetCursorPos($sx, $sy)
Start-Sleep -Milliseconds 120
[U]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)
[U]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)
Write-Output "clicked window($X,$Y) -> screen($sx,$sy)"
