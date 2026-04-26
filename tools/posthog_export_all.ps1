param(
  [string]$PersonalApiKey = "",
  [string]$ApiHost = "https://us.posthog.com",
  [int]$ProjectId = 0,

  # Pagination / throttling
  [int]$Limit = 200,
  [int]$SleepMsBetweenPages = 0,
  [int]$MaxRetries = 5,

  # What to export
  [switch]$IncludePersons = $true,

  # Output
  [string]$OutDir = "",
  [string]$SecretsPropsPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[posthog_export_all] $msg" }
function Write-Warn([string]$msg) { Write-Warning "[posthog_export_all] $msg" }

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

if ([string]::IsNullOrWhiteSpace($PersonalApiKey)) {
  $secrets = Try-LoadSecrets $SecretsPropsPath
  if ($secrets -and $secrets["TelemetryPostHogPersonalApiKey"]) {
    $PersonalApiKey = $secrets["TelemetryPostHogPersonalApiKey"]
  }
}

if ($ProjectId -le 0) {
  $secrets = Try-LoadSecrets $SecretsPropsPath
  if ($secrets -and $secrets["TelemetryPostHogProjectId"]) {
    $tmp = 0
    if ([int]::TryParse($secrets["TelemetryPostHogProjectId"], [ref]$tmp)) { $ProjectId = $tmp }
  }
}

if ([string]::IsNullOrWhiteSpace($PersonalApiKey)) {
  Write-Host "[ERROR] Missing Personal API key. Pass -PersonalApiKey or set env POSTHOG_PERSONAL_API_KEY."
  Write-Host "[ERROR] Note: this is NOT the ingest token (phc_...). Create a Personal API key in PostHog UI."
  Write-Host "[ERROR] You can also put it into TelemetrySecrets.props as <TelemetryPostHogPersonalApiKey>...</TelemetryPostHogPersonalApiKey>."
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

if ([string]::IsNullOrWhiteSpace($OutDir)) {
  $OutDir = Join-Path $PSScriptRoot "posthog_exports"
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$eventsPath = Join-Path $OutDir "ALL_events_${stamp}.jsonl"
$personsPath = Join-Path $OutDir "ALL_persons_${stamp}.jsonl"

$headers = @{ Authorization = "Bearer $PersonalApiKey" }

function Invoke-GetWithRetry([string]$url) {
  $attempt = 0
  while ($true) {
    try {
      return Invoke-RestMethod -Method Get -Uri $url -Headers $headers
    }
    catch {
      $attempt++
      $status = $null
      $retryAfter = $null

      try {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
          $status = [int]$_.Exception.Response.StatusCode
        }
        if ($_.Exception.Response -and $_.Exception.Response.Headers -and $_.Exception.Response.Headers["Retry-After"]) {
          $retryAfter = [int]$_.Exception.Response.Headers["Retry-After"]
        }
      } catch {}

      if ($attempt -gt $MaxRetries) { throw }

      $sleepSec = 0
      if ($retryAfter -and $retryAfter -gt 0) {
        $sleepSec = $retryAfter
      } else {
        $sleepSec = [Math]::Min(60, [Math]::Pow(2, $attempt))
      }

      Write-Warn "GET failed (status=$status). Retry $attempt/$MaxRetries after ${sleepSec}s"
      Start-Sleep -Seconds $sleepSec
    }
  }
}

function Export-PaginatedJsonl(
  [string]$title,
  [string]$firstUrl,
  [string]$outPath
) {
  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  $writer = New-Object System.IO.StreamWriter($outPath, $false, $utf8NoBom)
  $written = 0
  $page = 0

  try {
    $nextUrl = $firstUrl
    while (-not [string]::IsNullOrWhiteSpace($nextUrl)) {
      $page++
      Write-Info "${title}: page $page"
      Write-Info "URL: $nextUrl"

      $resp = Invoke-GetWithRetry $nextUrl
      if ($null -eq $resp) { break }

      $results = $resp.results
      if ($null -eq $results) { $results = @() }

      foreach ($item in $results) {
        $writer.WriteLine(($item | ConvertTo-Json -Depth 80 -Compress))
        $written++
      }

      $n = $resp.next
      if ($null -eq $n -or [string]::IsNullOrWhiteSpace([string]$n)) {
        $nextUrl = $null
      } elseif ([string]$n -match '^https?://') {
        $nextUrl = [string]$n
      } elseif ([string]$n -like '/*') {
        $nextUrl = "$ApiHost$([string]$n)"
      } else {
        $nextUrl = [string]$n
      }

      if ($SleepMsBetweenPages -gt 0) {
        Start-Sleep -Milliseconds $SleepMsBetweenPages
      }

      if ($page % 10 -eq 0) {
        Write-Info "${title}: written so far = $written"
      }
    }
  }
  finally {
    if ($writer) { $writer.Dispose() }
  }

  Write-Info "${title}: done. Exported $written items to $outPath"
}

Write-Info "API host: $ApiHost"
Write-Info "ProjectId: $ProjectId"
Write-Info "OutDir: $OutDir"

$eventsUrl = "$ApiHost/api/projects/$ProjectId/events/?limit=$Limit"
Export-PaginatedJsonl -title "events" -firstUrl $eventsUrl -outPath $eventsPath

if ($IncludePersons) {
  $personsUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"
  Export-PaginatedJsonl -title "persons" -firstUrl $personsUrl -outPath $personsPath
}

Write-Info "All exports finished."
exit 0

