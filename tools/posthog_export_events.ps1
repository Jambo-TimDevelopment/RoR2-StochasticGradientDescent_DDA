param(
  [string]$PersonalApiKey = "",
  [string]$ApiHost = "https://us.posthog.com",
  [int]$ProjectId = 0,
  [string]$Event = "",
  [string]$After = "",
  [string]$Before = "",
  [int]$Limit = 200,
  [int]$MaxEvents = 0,
  [string]$OutPath = "",
  [string]$SecretsPropsPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[posthog_export_events] $msg" }
function Write-Warn([string]$msg) { Write-Warning "[posthog_export_events] $msg" }

function Try-LoadSecrets([string]$path) {
  try {
    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path $path)) { return $null }
    [xml]$xml = Get-Content -Raw -Path $path
    $props = @{}
    $groups = @($xml.Project.PropertyGroup)
    foreach ($g in $groups) {
      if ($null -eq $g) { continue }
      foreach ($child in $g.ChildNodes) {
        if ($child.NodeType -ne "Element") { continue }
        $props[$child.Name] = [string]$child.InnerText
      }
    }
    return $props
  } catch {
    return $null
  }
}

if ([string]::IsNullOrWhiteSpace($SecretsPropsPath)) {
  $repoRoot = Split-Path -Parent $PSScriptRoot
  $SecretsPropsPath = Join-Path $repoRoot "TelemetrySecrets.props"
}

if ([string]::IsNullOrWhiteSpace($PersonalApiKey)) { $PersonalApiKey = $env:POSTHOG_PERSONAL_API_KEY }

if (([string]::IsNullOrWhiteSpace($PersonalApiKey)) -or ($ProjectId -le 0)) {
  $secrets = Try-LoadSecrets $SecretsPropsPath
  if ($secrets) {
    if ([string]::IsNullOrWhiteSpace($PersonalApiKey) -and $secrets["TelemetryPostHogPersonalApiKey"]) {
      $PersonalApiKey = $secrets["TelemetryPostHogPersonalApiKey"]
    }
    if ($ProjectId -le 0 -and $secrets["TelemetryPostHogProjectId"]) {
      $tmp = 0
      if ([int]::TryParse($secrets["TelemetryPostHogProjectId"], [ref]$tmp)) { $ProjectId = $tmp }
    }
  }
}

if ([string]::IsNullOrWhiteSpace($PersonalApiKey)) {
  Write-Host "[ERROR] Missing Personal API key. Pass -PersonalApiKey or set env POSTHOG_PERSONAL_API_KEY."
  Write-Host "[ERROR] You can also put it into TelemetrySecrets.props as <TelemetryPostHogPersonalApiKey>...</TelemetryPostHogPersonalApiKey>."
  Write-Host "[ERROR] Note: this is NOT the ingest token (phc_...). Create a Personal API key in PostHog UI."
  exit 1
}

if ($ProjectId -le 0) {
  Write-Host "[ERROR] Missing ProjectId. Pass -ProjectId or put it into TelemetrySecrets.props as <TelemetryPostHogProjectId>...</TelemetryPostHogProjectId>."
  exit 1
}

if ($Limit -lt 1 -or $Limit -gt 1000) {
  Write-Host "[ERROR] -Limit must be in range 1..1000"
  exit 1
}

$ApiHost = $ApiHost.Trim().Trim('"').Trim("'").TrimEnd("/")
$base = "$ApiHost/api/projects/$ProjectId/events/"

$qs = New-Object System.Collections.Generic.List[string]
$qs.Add("limit=$Limit") | Out-Null
if (-not [string]::IsNullOrWhiteSpace($Event)) { $qs.Add("event=$([System.Uri]::EscapeDataString($Event))") | Out-Null }
if (-not [string]::IsNullOrWhiteSpace($After)) { $qs.Add("after=$([System.Uri]::EscapeDataString($After))") | Out-Null }
if (-not [string]::IsNullOrWhiteSpace($Before)) { $qs.Add("before=$([System.Uri]::EscapeDataString($Before))") | Out-Null }

$nextUrl = "$base?$(($qs -join '&'))"

if ([string]::IsNullOrWhiteSpace($OutPath)) {
  $exportDir = Join-Path $PSScriptRoot "posthog_exports"
  if (-not (Test-Path $exportDir)) { New-Item -ItemType Directory -Force -Path $exportDir | Out-Null }

  $safeEvent = if ([string]::IsNullOrWhiteSpace($Event)) { "all" } else { ($Event -replace '[^a-zA-Z0-9._-]+', '_') }
  $stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
  $OutPath = Join-Path $exportDir "events_${safeEvent}_${stamp}.jsonl"
}

$outDir = Split-Path -Parent $OutPath
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

$headers = @{
  Authorization = "Bearer $PersonalApiKey"
}

Write-Info "API host: $ApiHost"
Write-Info "ProjectId: $ProjectId"
if (-not [string]::IsNullOrWhiteSpace($Event)) { Write-Info "Filter event: $Event" }
if (-not [string]::IsNullOrWhiteSpace($After)) { Write-Info "After: $After" }
if (-not [string]::IsNullOrWhiteSpace($Before)) { Write-Info "Before: $Before" }
Write-Info "Writing JSONL: $OutPath"

$written = 0
$page = 0

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$writer = New-Object System.IO.StreamWriter($OutPath, $false, $utf8NoBom)

try {
  while (-not [string]::IsNullOrWhiteSpace($nextUrl)) {
    $page++
    Write-Info "GET page $page"
    Write-Info "URL: $nextUrl"

    $resp = Invoke-RestMethod -Method Get -Uri $nextUrl -Headers $headers
    if ($null -eq $resp) { break }

    $results = $resp.results
    if ($null -eq $results) { $results = @() }

    foreach ($evt in $results) {
      $line = $evt | ConvertTo-Json -Depth 50 -Compress
      $writer.WriteLine($line)
      $written++

      if ($MaxEvents -gt 0 -and $written -ge $MaxEvents) {
        Write-Warn "Reached MaxEvents=$MaxEvents, stopping."
        $nextUrl = $null
        break
      }
    }

    $n = $resp.next
    if ($null -eq $n -or [string]::IsNullOrWhiteSpace([string]$n)) {
      $nextUrl = $null
    } elseif ([string]$n -match '^https?://') {
      $nextUrl = [string]$n
    } elseif ([string]$n -like '/*') {
      $nextUrl = "$ApiHost$([string]$n)"
    } else {
      # Unexpected format; try to treat as absolute.
      $nextUrl = [string]$n
    }

    if ($page -ge 1 -and ($written -gt 0) -and ($page % 10 -eq 0)) {
      Write-Info "Progress: $written events written so far"
    }
  }
}
catch {
  Write-Host "[ERROR] Export failed: $($_.Exception.Message)"
  if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
    Write-Host ("[ERROR] HTTP status: " + [int]$_.Exception.Response.StatusCode)
  }
  throw
}
finally {
  if ($writer) { $writer.Dispose() }
}

Write-Info "Done. Exported $written events."
exit 0

