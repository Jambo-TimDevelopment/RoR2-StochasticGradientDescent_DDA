param(
  [string]$PersonalApiKey = "",
  [string]$ApiHost = "https://us.posthog.com",
  [int]$ProjectId = 0,

  # Batch / throttling
  [int]$Limit = 200,
  [int]$SleepMsBetweenDeletes = 50,
  [int]$SleepMsBetweenBatches = 200,
  [int]$MaxRetries = 6,

  # Safety
  [int]$MaxPersonsToDelete = 0,
  [switch]$DryRun = $false,

  # Diagnostics
  [switch]$VerboseHttpErrors = $true,

  # Secrets
  [string]$SecretsPropsPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[posthog_delete_all_persons] $msg" }
function Write-Warn([string]$msg) { Write-Warning "[posthog_delete_all_persons] $msg" }

function Ensure-Tls12() {
  # Windows PowerShell 5.x on older .NET Framework may not default to TLS 1.2,
  # which breaks modern HTTPS endpoints (including PostHog Cloud).
  try {
    $sp = [Net.ServicePointManager]::SecurityProtocol
    $tls12 = [Net.SecurityProtocolType]::Tls12
    if (($sp -band $tls12) -eq 0) {
      [Net.ServicePointManager]::SecurityProtocol = $sp -bor $tls12
    }
  } catch {
    # If not supported, just continue; newer PowerShell/.NET will be fine.
  }
}

function Get-ErrorBodyFromException($ex) {
  try {
    if ($null -eq $ex -or $null -eq $ex.Response) { return $null }
    $resp = $ex.Response
    $stream = $resp.GetResponseStream()
    if ($null -eq $stream) { return $null }
    $reader = New-Object System.IO.StreamReader($stream)
    $body = $reader.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($body)) { return $null }
    return $body
  } catch {
    return $null
  }
}

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

Ensure-Tls12

$ApiHost = $ApiHost.Trim().Trim('"').Trim("'").TrimEnd("/")
$headers = @{ Authorization = "Bearer $PersonalApiKey" }

function Get-RetryAfterSecondsFromException($ex) {
  try {
    if ($ex.Response -and $ex.Response.Headers -and $ex.Response.Headers["Retry-After"]) {
      $ra = [int]$ex.Response.Headers["Retry-After"]
      if ($ra -gt 0) { return $ra }
    }
  } catch {}
  return $null
}

function Get-HttpStatusFromException($ex) {
  try {
    if ($ex.Response -and $ex.Response.StatusCode) { return [int]$ex.Response.StatusCode }
  } catch {}
  return $null
}

function Invoke-GetWithRetry([string]$url) {
  $attempt = 0
  while ($true) {
    try {
      return Invoke-RestMethod -Method Get -Uri $url -Headers $headers
    } catch {
      $attempt++
      $status = Get-HttpStatusFromException $_.Exception
      $retryAfter = Get-RetryAfterSecondsFromException $_.Exception
      if ($VerboseHttpErrors) {
        $body = Get-ErrorBodyFromException $_.Exception
        if ($body) { Write-Warn "GET error body: $body" }
        else { Write-Warn ("GET error: " + $_.Exception.Message) }
      }
      if ($attempt -gt $MaxRetries) { throw }

      $sleepSec =
        if ($retryAfter) { $retryAfter }
        else { [int][Math]::Min(60, [Math]::Pow(2, $attempt)) }

      Write-Warn "GET failed (status=$status). Retry $attempt/$MaxRetries after ${sleepSec}s"
      Start-Sleep -Seconds $sleepSec
    }
  }
}

function Invoke-DeleteWithRetry([string]$url) {
  $attempt = 0
  while ($true) {
    try {
      Invoke-RestMethod -Method Delete -Uri $url -Headers $headers | Out-Null
      return $true
    } catch {
      $attempt++
      $status = Get-HttpStatusFromException $_.Exception
      $retryAfter = Get-RetryAfterSecondsFromException $_.Exception
      if ($VerboseHttpErrors) {
        $body = Get-ErrorBodyFromException $_.Exception
        if ($body) { Write-Warn "DELETE error body: $body" }
        else { Write-Warn ("DELETE error: " + $_.Exception.Message) }
      }

      # Ignore "already gone" and "no access" style errors as non-retriable.
      if ($status -eq 404) { return $true }
      if ($status -eq 401 -or $status -eq 403) { throw }

      if ($attempt -gt $MaxRetries) { throw }

      $sleepSec =
        if ($retryAfter) { $retryAfter }
        else { [int][Math]::Min(60, [Math]::Pow(2, $attempt)) }

      Write-Warn "DELETE failed (status=$status). Retry $attempt/$MaxRetries after ${sleepSec}s"
      Start-Sleep -Seconds $sleepSec
    }
  }
}

$personsListUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"

Write-Info "API host: $ApiHost"
Write-Info "ProjectId: $ProjectId"
Write-Info "Limit: $Limit"
if ($DryRun) { Write-Warn "DryRun enabled: will fetch ONE batch and exit (no deletes)." }
if ($MaxPersonsToDelete -gt 0) { Write-Info "MaxPersonsToDelete: $MaxPersonsToDelete" }

$deleted = 0
$batches = 0

if ($DryRun) {
  $resp = Invoke-GetWithRetry $personsListUrl
  $count = 0
  try { $count = @($resp.results).Count } catch { $count = 0 }
  Write-Info "DryRun: persons in first batch = $count"
  exit 0
}

while ($true) {
  $batches++
  Write-Info "Fetching persons batch #$batches"
  $resp = Invoke-GetWithRetry $personsListUrl
  if ($null -eq $resp -or $null -eq $resp.results) {
    Write-Info "No response/results; stopping."
    break
  }

  $results = @($resp.results)
  if ($results.Count -eq 0) {
    Write-Info "No persons left. Done."
    break
  }

  Write-Info "Batch size: $($results.Count)"

  foreach ($p in $results) {
    if ($MaxPersonsToDelete -gt 0 -and $deleted -ge $MaxPersonsToDelete) {
      Write-Warn "Reached MaxPersonsToDelete=$MaxPersonsToDelete. Stopping."
      Write-Info "Deleted total: $deleted"
      exit 0
    }

    $id = $null
    try { $id = [string]$p.id } catch { $id = $null }
    if ([string]::IsNullOrWhiteSpace($id)) {
      Write-Warn "Skipping a person without id field."
      continue
    }

    $deleteUrl = "$ApiHost/api/projects/$ProjectId/persons/$id/?delete_events=true"

    if (-not $DryRun) {
      Invoke-DeleteWithRetry $deleteUrl | Out-Null
    }

    $deleted++

    if ($deleted % 50 -eq 0) {
      Write-Info "Deleted so far: $deleted"
    }

    if ($SleepMsBetweenDeletes -gt 0) {
      Start-Sleep -Milliseconds $SleepMsBetweenDeletes
    }
  }

  if ($SleepMsBetweenBatches -gt 0) {
    Start-Sleep -Milliseconds $SleepMsBetweenBatches
  }
}

Write-Info "Finished. Deleted total: $deleted"
exit 0

