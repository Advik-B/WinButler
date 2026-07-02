# Crop a rectangle out of a capture and upscale it (nearest-neighbor, so pixels stay crisp)
# for close inspection of small UI detail.
#   crop.ps1 -In dashboard.png -X 760 -Y 238 -W 520 -H 100 -Scale 2.5 -Out readout.png
# -In/-Out resolve against -OutDir (the shoot.ps1 capture dir) unless given as absolute paths.
param(
  [Parameter(Mandatory=$true)][string]$In,
  [Parameter(Mandatory=$true)][int]$X,
  [Parameter(Mandatory=$true)][int]$Y,
  [Parameter(Mandatory=$true)][int]$W,
  [Parameter(Mandatory=$true)][int]$H,
  [double]$Scale = 2.0,
  [string]$Out,
  [string]$OutDir = "$PSScriptRoot\out"
)
Add-Type -AssemblyName System.Drawing
if (-not $Out) { $Out = "crop_$In" }
if (-not [System.IO.Path]::IsPathRooted($In))  { $In  = Join-Path $OutDir $In }
if (-not [System.IO.Path]::IsPathRooted($Out)) { $Out = Join-Path $OutDir $Out }
if (-not (Test-Path $In)) { Write-Output "NO INPUT at $In"; exit 1 }

$src = [System.Drawing.Bitmap]::FromFile($In)
$rect = New-Object System.Drawing.Rectangle $X, $Y, $W, $H
$crop = $src.Clone($rect, $src.PixelFormat)
$ow = [int]($W * $Scale); $oh = [int]($H * $Scale)
$big = New-Object System.Drawing.Bitmap $ow, $oh
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.DrawImage($crop, 0, 0, $ow, $oh)
$g.Dispose()
$big.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$crop.Dispose(); $big.Dispose(); $src.Dispose()
Write-Output "wrote $Out ($ow x $oh)"
