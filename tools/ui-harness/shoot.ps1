# Launch (or reuse) WinButler, pin its window to a fixed size/position, and capture it
# to a PNG via PrintWindow (works without stealing foreground focus).
param(
  [string]$OutName = "dashboard.png",
  [int]$WaitSeconds = 2,
  [string]$OutDir = "$PSScriptRoot\out",
  [string]$Exe = ""
)
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32 {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
# Resolve the Debug exe relative to the repo root (this script lives in tools/ui-harness/).
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot '..\..\bin\Debug\net10.0\WinButler.exe' }
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }
$existing = Get-Process WinButler -ErrorAction SilentlyContinue
if ($existing) { $p = $existing | Select-Object -First 1 }
else {
  if (-not (Test-Path $Exe)) { Write-Output "NO EXE at $Exe (run 'dotnet build' first)"; exit 1 }
  $p = Start-Process $Exe -PassThru
}
$h = [IntPtr]::Zero
for ($i=0; $i -lt 60; $i++) {
  Start-Sleep -Milliseconds 500
  $p.Refresh()
  if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { Write-Output 'NO WINDOW'; exit 1 }
# Restore + size the window (no foreground steal needed for PrintWindow)
[Win32]::ShowWindow($h, 9) | Out-Null   # SW_RESTORE
[Win32]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, 1320, 900, 0x0040) | Out-Null  # SWP_SHOWWINDOW
Start-Sleep -Seconds $WaitSeconds
$r = New-Object Win32+RECT
[Win32]::GetWindowRect($h, [ref]$r) | Out-Null
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
$ok = [Win32]::PrintWindow($h, $hdc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($hdc)
$g.Dispose()
$out = Join-Path $OutDir $OutName
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
# quick emptiness heuristic: sample center pixel
$center = $bmp.GetPixel([int]($w/2), [int]($ht/2))
$bmp.Dispose()
Write-Output "Saved $out ($w x $ht) PrintWindow=$ok center=$($center.R),$($center.G),$($center.B) pid=$($p.Id)"
