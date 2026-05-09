param(
  [string]$PersonalApiKey = "",
  [string]$ApiHost = "https://us.posthog.com",
  [int]$ProjectId = 0,
  [string]$Event = "",
  [int]$Limit = 200,
  [int]$MaxEvents = 0,
  [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[posthog_export_last_hour] $msg" }

if ([string]::IsNullOrWhiteSpace($OutDir)) {
  $OutDir = Join-Path $PSScriptRoot "posthog_exports_last_hour"
}
if (-not (Test-Path $OutDir)) {
  New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
}

$nowUtc = (Get-Date).ToUniversalTime()
$afterUtc = $nowUtc.AddHours(-1)
$before = $nowUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
$after = $afterUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$safeEvent = if ([string]::IsNullOrWhiteSpace($Event)) { "all" } else { ($Event -replace '[^a-zA-Z0-9._-]+', '_') }
$outPath = Join-Path $OutDir "events_last_hour_${safeEvent}_${stamp}.jsonl"

$eventsScript = Join-Path $PSScriptRoot "posthog_export_events.ps1"
if (-not (Test-Path $eventsScript)) {
  Write-Host "[ERROR] Missing dependency script: $eventsScript"
  exit 1
}

$args = @{
  ApiHost = $ApiHost
  Event = $Event
  After = $after
  Before = $before
  Limit = $Limit
  MaxEvents = $MaxEvents
  OutPath = $outPath
}

if (-not [string]::IsNullOrWhiteSpace($PersonalApiKey)) {
  $args.PersonalApiKey = $PersonalApiKey
}
if ($ProjectId -gt 0) {
  $args.ProjectId = $ProjectId
}

Write-Info "Exporting PostHog events for the last hour"
Write-Info "After (UTC): $after"
Write-Info "Before (UTC): $before"
Write-Info "OutPath: $outPath"

& $eventsScript @args
exit $LASTEXITCODE
