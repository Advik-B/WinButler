# Bring the WinButler window to the foreground and drag the mouse from window-relative
# (X,Y) to (X2,Y2) — press, move in steps, release. For scrollbar-thumb / splitter testing.
param([int]$X, [int]$Y, [int]$X2, [int]$Y2, [int]$Steps = 20)
Add-Type @"
using System; using System.Runtime.InteropServices;
public class UD {
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
[UD]::ShowWindow($h, 9) | Out-Null
[UD]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, 1320, 900, 0x0040) | Out-Null
$fg = [UD]::GetForegroundWindow(); $tp = [uint32]0
$t1 = [UD]::GetWindowThreadProcessId($fg, [ref]$tp); $t2 = [UD]::GetCurrentThreadId()
[UD]::AttachThreadInput($t2, $t1, $true) | Out-Null
[UD]::BringWindowToTop($h) | Out-Null
[UD]::SetForegroundWindow($h) | Out-Null
[UD]::AttachThreadInput($t2, $t1, $false) | Out-Null
Start-Sleep -Milliseconds 350
$sx = 40 + $X; $sy = 40 + $Y; $ex = 40 + $X2; $ey = 40 + $Y2
[UD]::SetCursorPos($sx, $sy)
Start-Sleep -Milliseconds 150
[UD]::mouse_event(0x02, 0, 0, 0, [IntPtr]::Zero)   # left down
Start-Sleep -Milliseconds 150
for ($i = 1; $i -le $Steps; $i++) {
  $mx = [int]($sx + ($ex - $sx) * $i / $Steps)
  $my = [int]($sy + ($ey - $sy) * $i / $Steps)
  [UD]::SetCursorPos($mx, $my)
  Start-Sleep -Milliseconds 25
}
Start-Sleep -Milliseconds 150
[UD]::mouse_event(0x04, 0, 0, 0, [IntPtr]::Zero)   # left up
Write-Output "dragged window($X,$Y) -> ($X2,$Y2)"
