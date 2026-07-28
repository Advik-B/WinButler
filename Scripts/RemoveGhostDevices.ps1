<#
    Removes "ghost" (non-present) PnP devices -- device nodes Windows keeps around after the
    underlying hardware is gone (uninstalled, unplugged, swapped) but never cleans up itself.

    Concept credited to the "remove ghost devices natively with PowerShell" technique originally
    published at theorypc.ca (2017) by TrententTye / Alexander Boersch. The live page now returns
    HTTP 403; archived copy via the Wayback Machine. An unofficial third-party fork with
    additional flags exists at github.com/istvans/scripts -- not the source of this script.

    This is WinButler's own reimplementation: the original technique P/Invoked SetupAPI/CfgMgr32
    directly because no built-in tool exposed ghost-device removal in 2017; pnputil.exe's
    /enum-devices and /remove-device flags now do, so this uses those instead.

    THIS IS DESTRUCTIVE AND HAS NO UNDO. pnputil's "/enum-devices /disconnected" reports every
    PnP node Windows currently considers non-present -- verified against a real machine, that set
    includes not just dead/removed peripherals but also live, currently-installed components that
    are non-present for unrelated reasons: disk-drive PnP nodes for real mounted disks, a GPU's
    integrated USB-C/HD-audio controller nodes, an integrated GPU sidelined by hybrid-graphics
    switching, Volume Shadow Copy snapshot entries, and internal software/virtual device stubs
    (MIDI service test loopbacks, Virtual HID Framework nodes). Removing any of those can
    destabilize a running system. So this only ever removes an ALLOW-listed shape of instance ID
    -- genuinely pluggable peripherals identified by vendor/product ID (USB\VID_*, HID\VID_*),
    Bluetooth devices (BTH\*), and audio-endpoint stubs (SWD\MMDEVAPI\*) -- and explicitly still
    skips USB root hubs even though they're USB\-rooted. Everything else found is left alone and
    reported as skipped, never removed.
#>

# $Mode may already be set by a prelude the caller prepends (EmbeddedScript.RunCommand's
# `prelude` param) — "List" previews the exact same classification below without removing
# anything, so the read-only action is always an accurate preview of what "Remove" will do.
if (-not $Mode) { $Mode = 'Remove' }

function Test-SafeToRemove([string]$InstanceId) {
    if ($InstanceId -like 'USB\ROOT_HUB*')  { return $false }
    if ($InstanceId -like 'USB\VID_*')      { return $true }
    if ($InstanceId -like 'HID\VID_*')      { return $true }
    if ($InstanceId -like 'BTH\*')          { return $true }
    if ($InstanceId -like 'SWD\MMDEVAPI\*') { return $true }
    return $false
}

$raw = & pnputil.exe /enum-devices /disconnected
$ids = $raw | Select-String '^Instance ID:\s*(.+)$' |
    ForEach-Object { $_.Matches[0].Groups[1].Value.Trim() }

if (-not $ids) {
    Write-Output "No ghost devices found."
    exit 0
}

foreach ($id in $ids) {
    if (Test-SafeToRemove $id) {
        if ($Mode -eq 'List') {
            Write-Output "Ghost (would remove): $id"
        } else {
            Write-Output "Removing: $id"
            & pnputil.exe /remove-device "$id"
        }
    } else {
        Write-Output "Ghost (kept — not a recognised removable peripheral): $id"
    }
}
