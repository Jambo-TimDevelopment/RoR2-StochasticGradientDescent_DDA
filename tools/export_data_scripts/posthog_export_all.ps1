param(
  [string]$PersonalApiKey = "",
  [string]$ApiHost = "https://us.posthog.com",
  [int]$ProjectId = 0,

  # Pagination / throttling (PostHog allows limit up to 1000 — fewer pages = faster)
  [int]$Limit = 1000,
  [int]$SleepMsBetweenPages = 0,
  [int]$MaxRetries = 5,

  # What to export
  [switch]$IncludePersons = $true,

  # Only events with event property telemetry_schema_version >= this value (PostHog operator gte). 0 = no filter.
  # Matches the field in TelemetrySampleBuilder / analyze_hypotheses_h1_h4.py (sess_schema, like --min-schema-version).
  [int]$TelemetrySchemaVersion = 0,

  # Prefer raw JSON write (PS 7+ / System.Text.Json). Disable to force legacy path.
  [switch]$NoFastJson = $false,

  # Output
  [string]$OutDir = "",
  [string]$SecretsPropsPath = ""
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[posthog_export_all] $msg" }
function Write-Warn([string]$msg) { Write-Warning "[posthog_export_all] $msg" }

function Test-JsonDocumentAvailable {
  try {
    $null = [System.Text.Json.JsonDocument]
    return $true
  } catch {
    return $false
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
  $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
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
$eventsFilePrefix = if ($TelemetrySchemaVersion -gt 0) { "ALL_events_schema_ge${TelemetrySchemaVersion}_" } else { "ALL_events_" }
$eventsPath = Join-Path $OutDir "${eventsFilePrefix}${stamp}.jsonl"
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

function New-ExportHttpClient([string]$bearer) {
  $handler = New-Object System.Net.Http.HttpClientHandler
  $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
  $client = New-Object System.Net.Http.HttpClient($handler)
  $client.DefaultRequestHeaders.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", $bearer)
  $client.DefaultRequestHeaders.UserAgent.ParseAdd("posthog_export_all/2")
  $client.Timeout = [TimeSpan]::FromMinutes(30)
  return $client
}

function Invoke-HttpGetStringWithRetry([System.Net.Http.HttpClient]$client, [string]$url) {
  $attempt = 0
  while ($true) {
    try {
      $task = $client.GetStringAsync($url)
      return $task.GetAwaiter().GetResult()
    }
    catch {
      $attempt++
      $status = $null
      $retryAfter = $null
      try {
        $baseEx = $_.Exception.GetBaseException()
        if ($baseEx -is [System.Net.Http.HttpRequestException] -and $baseEx.Data.Contains("StatusCode")) {
          $status = [int]$baseEx.Data["StatusCode"]
        }
      } catch {}

      if ($attempt -gt $MaxRetries) { throw }

      $sleepSec = 0
      if ($retryAfter -and $retryAfter -gt 0) {
        $sleepSec = $retryAfter
      } else {
        $sleepSec = [Math]::Min(60, [Math]::Pow(2, $attempt))
      }

      Write-Warn "HTTP GET failed (status=$status). Retry $attempt/$MaxRetries after ${sleepSec}s"
      Start-Sleep -Seconds $sleepSec
    }
  }
}

function Normalize-NextUrl([string]$n) {
  if ($null -eq $n -or [string]::IsNullOrWhiteSpace($n)) { return $null }
  if ($n -match '^https?://') { return $n }
  if ($n -like '/*') { return "$ApiHost$n" }
  return $n
}

function Build-EventsListUrl([int]$limit, [int]$schemaVersion) {
  $base = "$ApiHost/api/projects/$ProjectId/events/"
  $pairs = New-Object System.Collections.Generic.List[string]
  [void]$pairs.Add("limit=$limit")
  if ($schemaVersion -gt 0) {
    # PostHog REST: properties = JSON array of { key, value, operator, type } (see PropertyItemSerializer).
    $filterObj = [ordered]@{
      key      = "telemetry_schema_version"
      value    = $schemaVersion
      operator = "gte"
      type     = "event"
    }
    $propsJson = ConvertTo-Json -InputObject @($filterObj) -Compress -Depth 4
    [void]$pairs.Add("properties=$([System.Uri]::EscapeDataString($propsJson))")
  }
  return "${base}?$(($pairs -join '&'))"
}

function Export-PaginatedJsonl-Fast(
  [System.Net.Http.HttpClient]$httpClient,
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

      $raw = Invoke-HttpGetStringWithRetry $httpClient $nextUrl
      $doc = [System.Text.Json.JsonDocument]::Parse($raw)
      try {
        $root = $doc.RootElement
        try {
          $resultsEl = $root.GetProperty("results")
        } catch {
          Write-Warn "${title}: response has no 'results'; stopping."
          break
        }
        foreach ($el in $resultsEl.EnumerateArray()) {
          $writer.WriteLine($el.GetRawText())
          $written++
        }

        $nextUrl = $null
        try {
          $nextEl = $root.GetProperty("next")
          if ($nextEl.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
            $nextUrl = Normalize-NextUrl $nextEl.GetString()
          }
        } catch {
          $nextUrl = $null
        }
      }
      finally {
        $doc.Dispose()
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

function Export-PaginatedJsonl-Legacy(
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
      } else {
        $nextUrl = Normalize-NextUrl ([string]$n)
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

$useFast = (-not $NoFastJson) -and (Test-JsonDocumentAvailable)
if ($useFast) {
  Write-Info "Using fast JSON path (HttpClient + JsonDocument; no per-row ConvertTo-Json)."
} else {
  Write-Info "Using legacy JSON path. For much faster exports install PowerShell 7+ or run with pwsh."
  if ($NoFastJson) { Write-Info "(-NoFastJson set.)" }
}

Write-Info "API host: $ApiHost"
Write-Info "ProjectId: $ProjectId"
Write-Info "OutDir: $OutDir"
Write-Info "Page size (-Limit): $Limit"
if ($TelemetrySchemaVersion -gt 0) {
  Write-Info "Events filter: telemetry_schema_version >= $TelemetrySchemaVersion (PostHog properties[], operator gte)."
  if ($IncludePersons) {
    Write-Warn "Person export is not filtered by telemetry_schema_version; omit -IncludePersons if you only need events with schema >= ${TelemetrySchemaVersion}."
   }
} else {
  Write-Info "Events filter: none (all schema versions)."
}

$eventsUrl = Build-EventsListUrl -limit $Limit -schemaVersion $TelemetrySchemaVersion

if ($useFast) {
  $httpClient = New-ExportHttpClient $PersonalApiKey
  try {
    Export-PaginatedJsonl-Fast -httpClient $httpClient -title "events" -firstUrl $eventsUrl -outPath $eventsPath

    if ($IncludePersons) {
      $personsUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"
      Export-PaginatedJsonl-Fast -httpClient $httpClient -title "persons" -firstUrl $personsUrl -outPath $personsPath
    }
  }
  finally {
    $httpClient.Dispose()
  }
} else {
  Export-PaginatedJsonl-Legacy -title "events" -firstUrl $eventsUrl -outPath $eventsPath

  if ($IncludePersons) {
    $personsUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"
    Export-PaginatedJsonl-Legacy -title "persons" -firstUrl $personsUrl -outPath $personsPath
  }
}

Write-Info "All exports finished."
exit 0
