# Drive WinButler's controls via UI Automation.
#   uia.ps1 dump                 - list every Button/RadioButton/CheckBox (AutomationId + Name)
#   uia.ps1 invoke "Clean All"   - invoke the first Button/RadioButton whose Name contains the text
#   uia.ps1 invoketext "Redirect"- invoke the first Button with a descendant Text element matching
#   uia.ps1 checkall             - toggle every CheckBox to checked
param([string]$Action = "dump", [string]$Match = "")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$root = $AE::RootElement

# Find the WinButler window by owning process name.
$wbProc = Get-Process WinButler -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $wbProc) { Write-Output "NO WINBUTLER PROCESS"; exit 1 }
$cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $wbProc.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "NO WINDOW for pid $($wbProc.Id)"; exit 1 }

function All($ctrlType) {
  $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $ctrlType)
  $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $c)
}

if ($Action -eq "dump") {
  foreach ($ct in @(
      [System.Windows.Automation.ControlType]::Button,
      [System.Windows.Automation.ControlType]::RadioButton,
      [System.Windows.Automation.ControlType]::CheckBox)) {
    foreach ($e in All($ct)) {
      $id = $e.Current.AutomationId; $nm = $e.Current.Name
      Write-Output ("{0,-12} id='{1}' name='{2}'" -f $ct.ProgrammaticName.Split('.')[-1], $id, $nm)
    }
  }
}
elseif ($Action -eq "invoke") {
  # Invoke the first Button/RadioButton whose Name contains $Match (case-insensitive).
  $hit = $null
  foreach ($ct in @([System.Windows.Automation.ControlType]::Button, [System.Windows.Automation.ControlType]::RadioButton)) {
    foreach ($e in All($ct)) {
      if ($e.Current.Name -and $e.Current.Name.ToLower().Contains($Match.ToLower())) { $hit = $e; break }
    }
    if ($hit) { break }
  }
  if (-not $hit) { Write-Output "NO MATCH for '$Match'"; exit 2 }
  try {
    $ip = $hit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $ip.Invoke(); Write-Output "invoked '$($hit.Current.Name)'"
  } catch {
    try { $sip = $hit.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern); $sip.Select(); Write-Output "selected '$($hit.Current.Name)'" }
    catch { Write-Output "could not invoke '$($hit.Current.Name)': $($_.Exception.Message)" }
  }
}
elseif ($Action -eq "invoketext") {
  # Invoke the first Button that has a descendant Text element containing $Match.
  $hit = $null
  $tc = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
  foreach ($b in All([System.Windows.Automation.ControlType]::Button)) {
    foreach ($t in $b.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) {
      if ($t.Current.Name -and $t.Current.Name.ToLower().Contains($Match.ToLower())) { $hit = $b; break }
    }
    if ($hit) { break }
  }
  if (-not $hit) { Write-Output "NO BUTTON with text '$Match'"; exit 2 }
  $ip = $hit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $ip.Invoke(); Write-Output "invoked button containing '$Match'"
}
elseif ($Action -eq "checkall") {
  # Toggle every CheckBox to checked.
  $n = 0
  foreach ($e in All([System.Windows.Automation.ControlType]::CheckBox)) {
    try {
      $tp = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
      if ($tp.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) { $tp.Toggle(); $n++ }
    } catch {}
  }
  Write-Output "toggled $n checkbox(es)"
}
