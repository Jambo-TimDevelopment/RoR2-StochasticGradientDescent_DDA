# Run posthog_export_all.ps1 (-TelemetrySchemaVersion 7) repeatedly with -ResumeFromStateFile until every
# ALL_events_schema_ge7_*.jsonl.export_state.json disappears (full export per your pipeline criteria).
$ErrorActionPreference = "Stop"
$dir = $PSScriptRoot
$ps1 = Join-Path $dir "posthog_export_all.ps1"
$outDir = Join-Path $dir "posthog_exports"
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$log = Join-Path $outDir ("orchestrator_schema_ge7_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")
function Write-O([string]$m) {
  Write-Host $m
  Add-Content -LiteralPath $log -Encoding utf8 -Value $m
}

$psExe = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"

for ($attempt = 1; $attempt -le 250; $attempt++) {
  $states = @(
    Get-ChildItem -LiteralPath $outDir -Filter "ALL_events_schema_ge7_*.jsonl.export_state.json" -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTime -Descending
  )

  $argList = New-Object System.Collections.Generic.List[string]
  foreach ($a in @(
      "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ps1,
      "-ApiHost", "https://us.posthog.com",
      "-Limit", "1000",
      "-SleepMsBetweenPages", "0",
      "-TelemetrySchemaVersion", "7",
      "-ExcludePersons",
      "-ProgressIntervalPages", "25"
    )) { [void]$argList.Add($a) }

  if ($states.Count -gt 0) {
    Write-O ("[attempt $attempt] resume -> " + $states[0].FullName)
    [void]$argList.Add("-ResumeFromStateFile")
    [void]$argList.Add($states[0].FullName)
  } else {
    Write-O "[attempt $attempt] fresh export (no matching ALL_events_schema_ge7_*.export_state.json)"
  }

  $pinfo = New-Object System.Diagnostics.ProcessStartInfo
  $pinfo.FileName = $psExe
  $pinfo.Arguments = ($argList | ForEach-Object {
      if ($_ -match "\s") { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join " "
  $pinfo.UseShellExecute = $false
  $pinfo.RedirectStandardOutput = $true
  $pinfo.RedirectStandardError = $true
  $pinfo.CreateNoWindow = $true

  $p = New-Object System.Diagnostics.Process
  $p.StartInfo = $pinfo
  [void]$p.Start()
  $outTxt = $p.StandardOutput.ReadToEnd()
  $errTxt = $p.StandardError.ReadToEnd()
  [void]$p.WaitForExit()

  if (-not [string]::IsNullOrEmpty($outTxt)) {
    $outTxt -split "`n" | ForEach-Object { Write-O $_.TrimEnd("`r") }
  }
  if (-not [string]::IsNullOrEmpty($errTxt)) {
    $errTxt -split "`n" | ForEach-Object { Write-O ("ERR: " + $_.TrimEnd("`r")) }
  }
  if ($p.ExitCode -ne 0) {
    Write-O ("child powershell exited " + $p.ExitCode)
    exit $p.ExitCode
  }

  $still = @(Get-ChildItem -LiteralPath $outDir -Filter "ALL_events_schema_ge7_*.jsonl.export_state.json" -ErrorAction SilentlyContinue)
  if ($still.Count -eq 0) {
    Write-O "[orchestrator] no schema_ge7 export_state files remain."
    break
  }
  Write-O ("[orchestrator] state persists (" + $still.Count + " file(s)); next iteration...")
}

$latestJsonl = Get-ChildItem -LiteralPath $outDir -Filter "ALL_events_schema_ge7_*.jsonl" -ErrorAction SilentlyContinue |
  Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($latestJsonl) {
  Write-O ("Latest JSONL: " + $latestJsonl.FullName + ", size=" + $latestJsonl.Length + " bytes")
}
Write-O ("Orchestrator log: " + $log)
exit 0
