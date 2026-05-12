param(
  [string]$PersonalApiKey = "",
  [string]$ApiHost = "https://us.posthog.com",
  [int]$ProjectId = 0,

  [int]$Limit = 1000,
  [int]$SleepMsBetweenPages = 0,
  [int]$MaxRetries = 8,

  [switch]$IncludePersons = $true,
  # When set (e.g. schema-filtered batches), skips persons entirely; avoids flaky -IncludePersons:$false quoting from cmd.exe.
  [switch]$ExcludePersons = $false,

  # Only events with telemetry_schema_version >= this (PostHog gte). 0 = no filter.
  [int]$TelemetrySchemaVersion = 0,

  [switch]$NoFastJson = $false,

  [string]$OutDir = "",
  [string]$SecretsPropsPath = "",

  # 1 = every page; larger = quieter; 0 = first and last page only per stream
  [int]$ProgressIntervalPages = 1,

  [switch]$LogUrls = $false,

  # Resume a failed export: JSON from a previous run (.export_state.json next to the jsonl).
  [string]$ResumeFromStateFile = "",

  # PostHog disables offset > 50000 on deprecated GET /events; rewind pagination using `before` when next URL hits this.
  [int]$OffsetRewindThreshold = 50000,

  # Also rewind after this many successful pages in one offset chain (PostHog Cloud may omit offset= in `next`).
  [int]$MaxPagesPerOffsetChain = 50,

  # Nudge rewind `before=` earlier by this many milliseconds (reduces PostHog 500 on the next page chain).
  [int]$BeforeSubtractMilliseconds = 1,

  # After retries still fail on GET …/events/ with 5xx: try recovery URLs (strip offset=, shift before= back N min, optionally lower limit). 0 disables that part.
  [int]$On500BeforeSkipMinutes = 10,
  [int]$On500ReducedLimit = 100
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[posthog_export_all] $msg" }
function Write-Warn([string]$msg) { Write-Warning "[posthog_export_all] $msg" }

function Format-DataSize([long]$bytes) {
  if ($bytes -lt 1024) { return "$bytes B" }
  $kb = $bytes / 1024.0
  if ($kb -lt 1024) { return ("{0:N1} KiB" -f $kb) }
  $mb = $kb / 1024.0
  if ($mb -lt 1024) { return ("{0:N2} MiB" -f $mb) }
  $gb = $mb / 1024.0
  return ("{0:N2} GiB" -f $gb)
}

function Get-JsonlOutFileSize([string]$path) {
  try {
    if (-not (Test-Path -LiteralPath $path)) { return 0 }
    return [long](Get-Item -LiteralPath $path).Length
  } catch {
    return 0
  }
}

function Write-StreamProgressLine(
  [string]$streamTitle,
  [int]$page,
  [int]$rowsThisPage,
  [int]$totalRows,
  [string]$outPath,
  [bool]$hasMore
) {
  $sizeB = Get-JsonlOutFileSize $outPath
  $sizeTxt = Format-DataSize $sizeB
  $moreTxt = if ($hasMore) { "yes" } else { "no (last page)" }
  Write-Info ('{0} | page={1} | +{2} rows (this request) | total_rows={3} | file_size={4} ({5} bytes) | more_pages={6}' -f `
      $streamTitle, $page, $rowsThisPage, $totalRows, $sizeTxt, $sizeB, $moreTxt)
  $displayPath = $outPath
  try {
    $displayPath = (Resolve-Path -LiteralPath $outPath -ErrorAction Stop).Path
  } catch {
  }
  Write-Info ('{0} | file: {1}' -f $streamTitle, $displayPath)
}

function Test-JsonDocumentAvailable {
  try {
    $null = [System.Text.Json.JsonDocument]
    return $true
  } catch {
    return $false
  }
}

function Test-JavaScriptSerializerAvailable {
  try {
    Add-Type -AssemblyName System.Web.Extensions -ErrorAction Stop | Out-Null
    $null = [System.Web.Script.Serialization.JavaScriptSerializer]
    return $true
  } catch {
    return $false
  }
}

function New-JavaScriptSerializerForExport {
  Add-Type -AssemblyName System.Web.Extensions -ErrorAction Stop | Out-Null
  $jss = New-Object System.Web.Script.Serialization.JavaScriptSerializer
  # Default cap is too small for 1k event payloads.
  $jss.MaxJsonLength = [int]::MaxValue
  $jss.RecursionLimit = 512
  return $jss
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

# Windows PowerShell 5.1: declare all functions before any executable statements in this script.

function Invoke-GetWithRetry {
  param(
    [string]$Url,
    [hashtable]$RequestHeaders
  )
  $attempt = 0
  while ($true) {
    try {
      return Invoke-RestMethod -Method Get -Uri $Url -Headers $RequestHeaders
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
        $baseEx = $_.Exception.GetBaseException()
        if ($null -eq $status -and $baseEx.Message -match '\((\d+)\)') {
          $status = [int]$Matches[1]
        }
      } catch {}

      if ($attempt -gt $MaxRetries) { throw }

      $sleepSec = 0
      if ($retryAfter -and $retryAfter -gt 0) {
        $sleepSec = $retryAfter
      } else {
        $sleepSec = [Math]::Min(120, [Math]::Pow(2, $attempt))
        if ($status -eq 500 -or $status -eq 502 -or $status -eq 503) {
          $sleepSec = [Math]::Max($sleepSec, 15)
        }
      }

      Write-Warn "GET failed (status=$status). Retry $attempt/$MaxRetries after ${sleepSec}s"
      Start-Sleep -Seconds $sleepSec
    }
  }
}

function Ensure-SystemNetHttpAssembly {
  # Windows PowerShell 5.1 often has no System.Net.Http loaded until we ask; HttpClient lives in that assembly.
  try {
    $null = [System.Net.Http.HttpClientHandler]
    return
  } catch {
  }
  try {
    Add-Type -AssemblyName System.Net.Http -ErrorAction Stop | Out-Null
  } catch {
    try {
      [void][System.Reflection.Assembly]::Load(
        "System.Net.Http, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
      )
    } catch {
      Write-Warn "Could not load System.Net.Http: $($_.Exception.Message). HTTP export may require .NET Framework 4.5+."
      throw
    }
  }
}

function New-ExportHttpClient([string]$bearer) {
  Ensure-SystemNetHttpAssembly
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
        if ($null -eq $status -and $baseEx.Message -match 'status code does not indicate success: \((\d+)\)') {
          $status = [int]$Matches[1]
        }
      } catch {}

      if ($attempt -gt $MaxRetries) {
        $snippet = $url
        if ($snippet.Length -gt 300) { $snippet = $snippet.Substring(0, 300) + '...' }
        Write-Warn "HTTP GET failed after $MaxRetries attempts (last status=$status). Failing URL (truncated): $snippet"
        throw
      }

      $sleepSec = 0
      if ($retryAfter -and $retryAfter -gt 0) {
        $sleepSec = $retryAfter
      } else {
        $sleepSec = [Math]::Min(120, [Math]::Pow(2, $attempt))
        if ($status -eq 500 -or $status -eq 502 -or $status -eq 503) {
          $sleepSec = [Math]::Max($sleepSec, 15)
        }
      }

      Write-Warn "HTTP GET failed (status=$status). Retry $attempt/$MaxRetries after ${sleepSec}s"
      Start-Sleep -Seconds $sleepSec
    }
  }
}

function Rewrite-EventsUrlQueryFor500Recovery(
  [string]$url,
  [int]$skipBackMinutes,
  [int]$setLimit
) {
  if ([string]::IsNullOrWhiteSpace($url)) { return $null }
  try {
    $uri = [System.Uri]$url
    $q = $uri.Query.TrimStart('?')
    if ([string]::IsNullOrWhiteSpace($q)) { return $null }
    $out = New-Object System.Collections.Generic.List[string]
    $hadLimit = $false
    foreach ($part in $q.Split([char[]]@('&'), [StringSplitOptions]::RemoveEmptyEntries)) {
      $eq = $part.IndexOf('=')
      if ($eq -lt 0) {
        [void]$out.Add($part)
        continue
      }
      $kRaw = $part.Substring(0, $eq)
      $vRaw = if (($eq + 1) -lt $part.Length) { $part.Substring($eq + 1) } else { '' }
      $k = [System.Uri]::UnescapeDataString($kRaw)
      $v = [System.Uri]::UnescapeDataString($vRaw)
      if ($k -eq 'offset') { continue }
      if ($k -eq 'before' -and $skipBackMinutes -gt 0) {
        try {
          $dto = [datetimeoffset]::Parse($v, [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
          $dto = $dto.AddMinutes(-$skipBackMinutes)
          $v = $dto.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff", [cultureinfo]::InvariantCulture) + 'Z'
        } catch {
          try {
            $dt = [datetime]::Parse($v, [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime().AddMinutes(-$skipBackMinutes)
            $v = $dt.ToString("yyyy-MM-ddTHH:mm:ss.fff", [cultureinfo]::InvariantCulture) + 'Z'
          } catch {
            # keep original $v
          }
        }
      }
      if ($k -eq 'limit' -and $setLimit -gt 0) {
        $v = "$setLimit"
        $hadLimit = $true
      }
      [void]$out.Add("$([System.Uri]::EscapeDataString($k))=$([System.Uri]::EscapeDataString($v))")
    }
    if ($setLimit -gt 0 -and -not $hadLimit) {
      [void]$out.Add("limit=$([System.Uri]::EscapeDataString("$setLimit"))")
    }
    $newQuery = $out -join '&'
    $basePath = $uri.GetLeftPart([System.UriPartial]::Path)
    return "${basePath}?${newQuery}"
  } catch {
    return $null
  }
}

function Get-EventsListRecoveryUrlCandidates(
  [string]$url,
  [int]$on500BeforeSkipMinutes,
  [int]$on500ReducedLimit
) {
  $list = New-Object System.Collections.Generic.List[string]
  if ($on500BeforeSkipMinutes -gt 0 -and $url -match '[?&]before=') {
    $r = Rewrite-EventsUrlQueryFor500Recovery -url $url -skipBackMinutes $on500BeforeSkipMinutes -setLimit 0
    if ($r) { [void]$list.Add($r) }
    $r2 = Rewrite-EventsUrlQueryFor500Recovery -url $url -skipBackMinutes ($on500BeforeSkipMinutes * 2) -setLimit 0
    if ($r2) { [void]$list.Add($r2) }
  }
  if ($on500ReducedLimit -gt 0) {
    $r3 = Rewrite-EventsUrlQueryFor500Recovery -url $url -skipBackMinutes 0 -setLimit $on500ReducedLimit
    if ($r3) { [void]$list.Add($r3) }
    if ($on500BeforeSkipMinutes -gt 0) {
      $uSk = Rewrite-EventsUrlQueryFor500Recovery -url $url -skipBackMinutes $on500BeforeSkipMinutes -setLimit 0
      if ($uSk) {
        $r4 = Rewrite-EventsUrlQueryFor500Recovery -url $uSk -skipBackMinutes 0 -setLimit $on500ReducedLimit
        if ($r4) { [void]$list.Add($r4) }
      }
      $uSk2 = Rewrite-EventsUrlQueryFor500Recovery -url $url -skipBackMinutes ($on500BeforeSkipMinutes * 2) -setLimit 0
      if ($uSk2) {
        $r5 = Rewrite-EventsUrlQueryFor500Recovery -url $uSk2 -skipBackMinutes 0 -setLimit $on500ReducedLimit
        if ($r5) { [void]$list.Add($r5) }
      }
    }
  }
  return $list
}

function Invoke-HttpGetEventsPageWithRecovery(
  [System.Net.Http.HttpClient]$httpClient,
  [string]$url,
  [bool]$enableEventsRecovery,
  [int]$on500BeforeSkipMinutes,
  [int]$on500ReducedLimit
) {
  if (-not $enableEventsRecovery -or $url -notmatch '/api/projects/\d+/events') {
    return Invoke-HttpGetStringWithRetry $httpClient $url
  }

  try {
    return Invoke-HttpGetStringWithRetry $httpClient $url
  } catch {
    $lastEx = $_
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$seen.Add($url)
    Write-Warn "Primary events GET failed after $MaxRetries attempts; trying recovery URLs (offset stripped; optional before= shift / lower limit per -On500*)."
    $alts = Get-EventsListRecoveryUrlCandidates -url $url -on500BeforeSkipMinutes $on500BeforeSkipMinutes -on500ReducedLimit $on500ReducedLimit
    foreach ($alt in $alts) {
      if ([string]::IsNullOrWhiteSpace($alt)) { continue }
      if ($seen.Contains($alt)) { continue }
      [void]$seen.Add($alt)
      $snippet = $alt
      if ($snippet.Length -gt 220) { $snippet = $snippet.Substring(0, 220) + '...' }
      Write-Warn "Recovery GET: $snippet"
      try {
        return Invoke-HttpGetStringWithRetry $httpClient $alt
      } catch {
        $lastEx = $_
        Write-Warn "Recovery URL also failed after $MaxRetries attempts."
      }
    }
    throw $lastEx
  }
}

function Invoke-GetEventsPageWithRecovery(
  [string]$url,
  [hashtable]$RequestHeaders,
  [bool]$enableEventsRecovery,
  [int]$on500BeforeSkipMinutes,
  [int]$on500ReducedLimit
) {
  if (-not $enableEventsRecovery -or $url -notmatch '/api/projects/\d+/events') {
    return Invoke-GetWithRetry -Url $url -RequestHeaders $RequestHeaders
  }

  try {
    return Invoke-GetWithRetry -Url $url -RequestHeaders $RequestHeaders
  } catch {
    $lastEx = $_
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    [void]$seen.Add($url)
    Write-Warn "Primary events GET failed after $MaxRetries attempts; trying recovery URLs (offset stripped; optional before= shift / lower limit per -On500*)."
    $alts = Get-EventsListRecoveryUrlCandidates -url $url -on500BeforeSkipMinutes $on500BeforeSkipMinutes -on500ReducedLimit $on500ReducedLimit
    foreach ($alt in $alts) {
      if ([string]::IsNullOrWhiteSpace($alt)) { continue }
      if ($seen.Contains($alt)) { continue }
      [void]$seen.Add($alt)
      $snippet = $alt
      if ($snippet.Length -gt 220) { $snippet = $snippet.Substring(0, 220) + '...' }
      Write-Warn "Recovery GET: $snippet"
      try {
        return Invoke-GetWithRetry -Url $alt -RequestHeaders $RequestHeaders
      } catch {
        $lastEx = $_
        Write-Warn "Recovery URL also failed after $MaxRetries attempts."
      }
    }
    throw $lastEx
  }
}

function Normalize-NextUrl([string]$n) {
  if ($null -eq $n -or [string]::IsNullOrWhiteSpace($n)) { return $null }
  if ($n -match '^https?://') { return $n }
  if ($n -like '/*') { return "$ApiHost$n" }
  return $n
}

function Get-UrlQueryOffset([string]$url) {
  if ([string]::IsNullOrWhiteSpace($url)) { return $null }
  $m = [regex]::Match($url, '[?&]offset=(\d+)', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
  if (-not $m.Success) { return $null }
  return [int]$m.Groups[1].Value
}

function Format-PostHogBeforeParam([object]$tsVal) {
  if ($null -eq $tsVal) { return $null }
  try {
    if ($tsVal -is [datetime]) {
      return $tsVal.ToUniversalTime().ToString("o")
    }
    $s = [string]$tsVal
    if ([string]::IsNullOrWhiteSpace($s)) { return $null }
    return $s
  } catch {
    return $null
  }
}

function Get-EventTimestampFromRow([object]$item) {
  if ($null -eq $item) { return $null }
  try {
    if ($item -is [System.Collections.IDictionary]) {
      foreach ($k in @('timestamp', 'Timestamp')) {
        if ($item.Keys -contains $k) {
          return (Format-PostHogBeforeParam $item[$k])
        }
      }
      return $null
    }
    foreach ($propName in @('timestamp', 'Timestamp')) {
      $prop = $item.PSObject.Properties[$propName]
      if ($null -ne $prop) {
        return (Format-PostHogBeforeParam $prop.Value)
      }
    }
  } catch {
  }
  return $null
}

function Get-JsonElementTimestampForBefore([System.Text.Json.JsonElement]$el) {
  try {
    $p = $el.GetProperty('timestamp')
    if ($p.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
      return $p.GetString()
    }
    if ($p.ValueKind -ne [System.Text.Json.JsonValueKind]::Undefined -and $p.ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
      return $p.GetRawText().Trim([char]34)
    }
  } catch {
  }
  return $null
}

function Normalize-BeforeTimestampForPostHogApi([string]$beforeRaw, [int]$subtractMs = 1) {
  # PostHog /events before= accepts ISO; long fractional + offset forms sometimes trigger 500 on follow-up pages.
  if ([string]::IsNullOrWhiteSpace($beforeRaw)) { return $beforeRaw }
  $trim = $beforeRaw.Trim()
  try {
    $dto = [datetimeoffset]::Parse($trim, [cultureinfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
    $utc = $dto.UtcDateTime
  } catch {
    try {
      $utc = [datetime]::Parse($trim, $null, [System.Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
    } catch {
      return $trim
    }
  }
  if ($subtractMs -gt 0) {
    $utc = $utc.AddMilliseconds(-$subtractMs)
  }
  return $utc.ToString("yyyy-MM-ddTHH:mm:ss.fff", [cultureinfo]::InvariantCulture) + "Z"
}

function Build-EventsListUrl([int]$limit, [int]$schemaVersion, [string]$Before = "", [int]$beforeSubtractMs = 1) {
  $base = "$ApiHost/api/projects/$ProjectId/events/"
  $pairs = New-Object System.Collections.Generic.List[string]
  [void]$pairs.Add("limit=$limit")
  if ($schemaVersion -gt 0) {
    $filterObj = [ordered]@{
      key      = "telemetry_schema_version"
      value    = $schemaVersion
      operator = "gte"
      type     = "event"
    }
    $propsJson = ConvertTo-Json -InputObject @($filterObj) -Compress -Depth 4
    [void]$pairs.Add("properties=$([System.Uri]::EscapeDataString($propsJson))")
  }
  if (-not [string]::IsNullOrWhiteSpace($Before)) {
    $beforeApi = Normalize-BeforeTimestampForPostHogApi $Before $beforeSubtractMs
    [void]$pairs.Add("before=$([System.Uri]::EscapeDataString($beforeApi))")
  }
  return "${base}?$(($pairs -join '&'))"
}

function Save-ExportState([string]$statePath, [hashtable]$obj) {
  try {
    ($obj | ConvertTo-Json -Depth 6 -Compress) | Set-Content -LiteralPath $statePath -Encoding utf8
  } catch {
    Write-Warn "Could not write state file: $($_.Exception.Message)"
  }
}

function ExportPostHogJsonl_Http(
  [System.Net.Http.HttpClient]$httpClient,
  [string]$title,
  [string]$firstUrl,
  [string]$outPath,
  [int]$progressEvery,
  [string]$statePath = "",
  [long]$initialWritten = 0,
  [bool]$append = $false,
  [int]$eventsLimit = 1000,
  [int]$eventsSchemaVersion = 0,
  [int]$offsetRewindThreshold = 50000,
  [bool]$offsetRewindEnabled = $false,
  [int]$maxPagesPerOffsetChain = 50,
  [int]$initialOffsetChainPages = 0,
  [int]$beforeSubtractMs = 1,
  [bool]$enableEventsHttp500Recovery = $false,
  [int]$on500BeforeSkipMinutes = 10,
  [int]$on500ReducedLimit = 100
) {
  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  $writer = New-Object System.IO.StreamWriter($outPath, $append, $utf8NoBom)
  [long]$written = $initialWritten
  $page = 0
  $rewindPhases = 0
  [int]$chainPages = $initialOffsetChainPages

  try {
    [bool]$exportStoppedEarly = $false
    $nextUrl = $firstUrl
    while (-not [string]::IsNullOrWhiteSpace($nextUrl)) {
      $page++
      if ($LogUrls) {
        Write-Info "${title}: page $page - URL: $nextUrl"
      }

      $raw = Invoke-HttpGetEventsPageWithRecovery -httpClient $httpClient -url $nextUrl `
        -enableEventsRecovery $enableEventsHttp500Recovery -on500BeforeSkipMinutes $on500BeforeSkipMinutes -on500ReducedLimit $on500ReducedLimit
      $doc = [System.Text.Json.JsonDocument]::Parse($raw)
      $pageCount = 0
      $lastEventTimestamp = $null
      try {
        $root = $doc.RootElement
        try {
          # Single-quoted names: typographic double-quotes here broke PS 5.1 parsing of this whole function.
          $resultsEl = $root.GetProperty('results')
        } catch {
          Write-Warn "${title}: response has no 'results'; stopping."
          break
        }
        foreach ($el in $resultsEl.EnumerateArray()) {
          $writer.WriteLine($el.GetRawText())
          $written++
          $pageCount++
          $ts = Get-JsonElementTimestampForBefore $el
          if ($null -ne $ts) {
            $lastEventTimestamp = $ts
          }
        }

        $nextUrl = $null
        try {
          $nextEl = $root.GetProperty('next')
          if ($nextEl.ValueKind -eq [System.Text.Json.JsonValueKind]::String) {
            $nextUrl = Normalize-NextUrl $nextEl.GetString()
          }
        } catch {
          $nextUrl = $null
        }

        if ($offsetRewindEnabled -and -not [string]::IsNullOrWhiteSpace($nextUrl)) {
          if ($pageCount -gt 0) {
            $chainPages++
          }
          $nextOff = Get-UrlQueryOffset $nextUrl
          $badOff = ($null -ne $nextOff -and $nextOff -ge $offsetRewindThreshold)
          $tooManyInChain = ($chainPages -ge $maxPagesPerOffsetChain)
          if ($badOff -or $tooManyInChain) {
            if ([string]::IsNullOrWhiteSpace($lastEventTimestamp)) {
              Write-Warn "${title}: rewind needed (offset>=$offsetRewindThreshold or chain pages>=$maxPagesPerOffsetChain) but no timestamp on last row; stopping."
              $nextUrl = $null
              $exportStoppedEarly = $true
            } else {
              $rewindPhases++
              $reason = if ($badOff) { "next offset=$nextOff" } else { "offset chain length=$chainPages (next URL may hide offset)" }
              Write-Warn "${title}: Rewind #$rewindPhases ($reason). New chain with before=$lastEventTimestamp"
              $nextUrl = Build-EventsListUrl -limit $eventsLimit -schemaVersion $eventsSchemaVersion -Before $lastEventTimestamp -beforeSubtractMs $beforeSubtractMs
              $chainPages = 0
            }
          }
        }
      }
      finally {
        $doc.Dispose()
      }

      $writer.Flush()
      $hasMore = -not [string]::IsNullOrWhiteSpace($nextUrl)
      $logThis = $false
      if ($progressEvery -eq -1) {
        $logThis = ($page -eq 1) -or (-not $hasMore)
      } elseif ($progressEvery -le 1) {
        $logThis = $true
      } else {
        $logThis = (($page % $progressEvery) -eq 0) -or (-not $hasMore)
      }
      if ($logThis) {
        Write-StreamProgressLine -streamTitle $title -page $page -rowsThisPage $pageCount -totalRows $written `
          -outPath $outPath -hasMore $hasMore
      }

      if ($statePath -and -not [string]::IsNullOrWhiteSpace($statePath)) {
        Save-ExportState $statePath @{
          stream            = $title
          outPath           = $outPath
          written           = $written
          page              = $page
          nextUrl           = $nextUrl
          rewindPhases      = $rewindPhases
          offsetChainPages  = $chainPages
          apiHost           = $ApiHost
          projectId         = $ProjectId
          eventsSchemaVer   = $eventsSchemaVersion
          updatedUtc        = (Get-Date).ToUniversalTime().ToString("o")
        }
      }

      if ($SleepMsBetweenPages -gt 0) {
        Start-Sleep -Milliseconds $SleepMsBetweenPages
      }
    }

    if ($statePath -and (Test-Path -LiteralPath $statePath) -and (-not $exportStoppedEarly)) {
      Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
      Write-Info "${title}: removed completed state file $(Split-Path -Leaf $statePath)"
    }
  }
  finally {
    if ($writer) { $writer.Dispose() }
  }

  $finalBytes = Get-JsonlOutFileSize $outPath
  if ($rewindPhases -gt 0) {
    Write-Info ('{0}: pagination rewind phases (PostHog offset limit): {1}' -f $title, $rewindPhases)
  }
  Write-Info ('{0}: done. total_rows={1} file={2} ({3} bytes) path={4}' -f $title, $written, (Format-DataSize $finalBytes), $finalBytes, $outPath)
}

function ExportPostHogJsonl_HttpJs(
  [System.Net.Http.HttpClient]$httpClient,
  [System.Web.Script.Serialization.JavaScriptSerializer]$jsonSerializer,
  [string]$title,
  [string]$firstUrl,
  [string]$outPath,
  [int]$progressEvery,
  [string]$statePath = "",
  [long]$initialWritten = 0,
  [bool]$append = $false,
  [int]$eventsLimit = 1000,
  [int]$eventsSchemaVersion = 0,
  [int]$offsetRewindThreshold = 50000,
  [bool]$offsetRewindEnabled = $false,
  [int]$maxPagesPerOffsetChain = 50,
  [int]$initialOffsetChainPages = 0,
  [int]$beforeSubtractMs = 1,
  [bool]$enableEventsHttp500Recovery = $false,
  [int]$on500BeforeSkipMinutes = 10,
  [int]$on500ReducedLimit = 100
) {
  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  $writer = New-Object System.IO.StreamWriter($outPath, $append, $utf8NoBom)
  [long]$written = $initialWritten
  $page = 0
  $rewindPhases = 0
  [int]$chainPages = $initialOffsetChainPages

  try {
    [bool]$exportStoppedEarly = $false
    $nextUrl = $firstUrl
    while (-not [string]::IsNullOrWhiteSpace($nextUrl)) {
      $page++
      if ($LogUrls) {
        Write-Info "${title}: page $page - URL: $nextUrl"
      }

      $raw = Invoke-HttpGetEventsPageWithRecovery -httpClient $httpClient -url $nextUrl `
        -enableEventsRecovery $enableEventsHttp500Recovery -on500BeforeSkipMinutes $on500BeforeSkipMinutes -on500ReducedLimit $on500ReducedLimit
      $payload = $jsonSerializer.DeserializeObject($raw)
      $pageCount = 0
      $lastEventTimestamp = $null
      if ($null -eq $payload -or $payload -isnot [System.Collections.IDictionary]) {
        Write-Warn "${title}: could not parse response as object; stopping."
        break
      }
      $dict = [System.Collections.IDictionary]$payload
      $results = $null
      try {
        $results = $dict['results']
      } catch {
        $results = $null
      }
      if ($null -eq $results) {
        $results = @()
      }
      foreach ($item in $results) {
        $writer.WriteLine($jsonSerializer.Serialize($item))
        $written++
        $pageCount++
        $ts = Get-EventTimestampFromRow $item
        if ($null -ne $ts) {
          $lastEventTimestamp = $ts
        }
      }

      $nextUrl = $null
      try {
        $n = $dict['next']
        if ($null -ne $n -and -not [string]::IsNullOrWhiteSpace([string]$n)) {
          $nextUrl = Normalize-NextUrl ([string]$n)
        }
      } catch {
        $nextUrl = $null
      }

      if ($offsetRewindEnabled -and -not [string]::IsNullOrWhiteSpace($nextUrl)) {
        if ($pageCount -gt 0) {
          $chainPages++
        }
        $nextOff = Get-UrlQueryOffset $nextUrl
        $badOff = ($null -ne $nextOff -and $nextOff -ge $offsetRewindThreshold)
        $tooManyInChain = ($chainPages -ge $maxPagesPerOffsetChain)
        if ($badOff -or $tooManyInChain) {
          if ([string]::IsNullOrWhiteSpace($lastEventTimestamp)) {
            Write-Warn "${title}: rewind needed (offset>=$offsetRewindThreshold or chain pages>=$maxPagesPerOffsetChain) but no timestamp on last row; stopping."
            $nextUrl = $null
            $exportStoppedEarly = $true
          } else {
            $rewindPhases++
            $reason = if ($badOff) { "next offset=$nextOff" } else { "offset chain length=$chainPages (next URL may hide offset)" }
            Write-Warn "${title}: Rewind #$rewindPhases ($reason). New chain with before=$lastEventTimestamp"
            $nextUrl = Build-EventsListUrl -limit $eventsLimit -schemaVersion $eventsSchemaVersion -Before $lastEventTimestamp -beforeSubtractMs $beforeSubtractMs
            $chainPages = 0
          }
        }
      }

      $writer.Flush()
      $hasMore = -not [string]::IsNullOrWhiteSpace($nextUrl)
      $logThis = $false
      if ($progressEvery -eq -1) {
        $logThis = ($page -eq 1) -or (-not $hasMore)
      } elseif ($progressEvery -le 1) {
        $logThis = $true
      } else {
        $logThis = (($page % $progressEvery) -eq 0) -or (-not $hasMore)
      }
      if ($logThis) {
        Write-StreamProgressLine -streamTitle $title -page $page -rowsThisPage $pageCount -totalRows $written `
          -outPath $outPath -hasMore $hasMore
      }

      if ($statePath -and -not [string]::IsNullOrWhiteSpace($statePath)) {
        Save-ExportState $statePath @{
          stream            = $title
          outPath           = $outPath
          written           = $written
          page              = $page
          nextUrl           = $nextUrl
          rewindPhases      = $rewindPhases
          offsetChainPages  = $chainPages
          apiHost           = $ApiHost
          projectId         = $ProjectId
          eventsSchemaVer   = $eventsSchemaVersion
          updatedUtc        = (Get-Date).ToUniversalTime().ToString("o")
        }
      }

      if ($SleepMsBetweenPages -gt 0) {
        Start-Sleep -Milliseconds $SleepMsBetweenPages
      }
    }

    if ($statePath -and (Test-Path -LiteralPath $statePath) -and (-not $exportStoppedEarly)) {
      Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
      Write-Info "${title}: removed completed state file $(Split-Path -Leaf $statePath)"
    }
  }
  finally {
    if ($writer) { $writer.Dispose() }
  }

  $finalBytes = Get-JsonlOutFileSize $outPath
  if ($rewindPhases -gt 0) {
    Write-Info ('{0}: pagination rewind phases (PostHog offset limit): {1}' -f $title, $rewindPhases)
  }
  Write-Info ('{0}: done. total_rows={1} file={2} ({3} bytes) path={4}' -f $title, $written, (Format-DataSize $finalBytes), $finalBytes, $outPath)
}

function ExportPostHogJsonl_Rest(
  [string]$title,
  [string]$firstUrl,
  [string]$outPath,
  [int]$progressEvery,
  [hashtable]$RequestHeaders,
  [string]$statePath = "",
  [long]$initialWritten = 0,
  [bool]$append = $false,
  [int]$eventsLimit = 1000,
  [int]$eventsSchemaVersion = 0,
  [int]$offsetRewindThreshold = 50000,
  [bool]$offsetRewindEnabled = $false,
  [int]$maxPagesPerOffsetChain = 50,
  [int]$initialOffsetChainPages = 0,
  [int]$beforeSubtractMs = 1,
  [bool]$enableEventsHttp500Recovery = $false,
  [int]$on500BeforeSkipMinutes = 10,
  [int]$on500ReducedLimit = 100
) {
  $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
  $writer = New-Object System.IO.StreamWriter($outPath, $append, $utf8NoBom)
  [long]$written = $initialWritten
  $page = 0
  $rewindPhases = 0
  [int]$chainPages = $initialOffsetChainPages

  try {
    [bool]$exportStoppedEarly = $false
    $nextUrl = $firstUrl
    while (-not [string]::IsNullOrWhiteSpace($nextUrl)) {
      $page++
      if ($LogUrls) {
        Write-Info "${title}: page $page - URL: $nextUrl"
      }

      $resp = Invoke-GetEventsPageWithRecovery -url $nextUrl -RequestHeaders $RequestHeaders `
        -enableEventsRecovery $enableEventsHttp500Recovery -on500BeforeSkipMinutes $on500BeforeSkipMinutes -on500ReducedLimit $on500ReducedLimit
      if ($null -eq $resp) { break }

      $results = $resp.results
      if ($null -eq $results) { $results = @() }

      $pageCount = 0
      $lastEventTimestamp = $null
      foreach ($item in $results) {
        $writer.WriteLine(($item | ConvertTo-Json -Depth 80 -Compress))
        $written++
        $pageCount++
        $ts = Get-EventTimestampFromRow $item
        if ($null -ne $ts) {
          $lastEventTimestamp = $ts
        }
      }

      $n = $resp.next
      if ($null -eq $n -or [string]::IsNullOrWhiteSpace([string]$n)) {
        $nextUrl = $null
      } else {
        $nextUrl = Normalize-NextUrl ([string]$n)
      }

      if ($offsetRewindEnabled -and -not [string]::IsNullOrWhiteSpace($nextUrl)) {
        if ($pageCount -gt 0) {
          $chainPages++
        }
        $nextOff = Get-UrlQueryOffset $nextUrl
        $badOff = ($null -ne $nextOff -and $nextOff -ge $offsetRewindThreshold)
        $tooManyInChain = ($chainPages -ge $maxPagesPerOffsetChain)
        if ($badOff -or $tooManyInChain) {
          if ([string]::IsNullOrWhiteSpace($lastEventTimestamp)) {
            Write-Warn "${title}: rewind needed (offset>=$offsetRewindThreshold or chain pages>=$maxPagesPerOffsetChain) but no timestamp on last row; stopping."
            $nextUrl = $null
            $exportStoppedEarly = $true
          } else {
            $rewindPhases++
            $reason = if ($badOff) { "next offset=$nextOff" } else { "offset chain length=$chainPages (next URL may hide offset)" }
            Write-Warn "${title}: Rewind #$rewindPhases ($reason). New chain with before=$lastEventTimestamp"
            $nextUrl = Build-EventsListUrl -limit $eventsLimit -schemaVersion $eventsSchemaVersion -Before $lastEventTimestamp -beforeSubtractMs $beforeSubtractMs
            $chainPages = 0
          }
        }
      }

      $writer.Flush()
      $hasMore = -not [string]::IsNullOrWhiteSpace($nextUrl)
      $logThis = $false
      if ($progressEvery -eq -1) {
        $logThis = ($page -eq 1) -or (-not $hasMore)
      } elseif ($progressEvery -le 1) {
        $logThis = $true
      } else {
        $logThis = (($page % $progressEvery) -eq 0) -or (-not $hasMore)
      }
      if ($logThis) {
        Write-StreamProgressLine -streamTitle $title -page $page -rowsThisPage $pageCount -totalRows $written `
          -outPath $outPath -hasMore $hasMore
      }

      if ($statePath -and -not [string]::IsNullOrWhiteSpace($statePath)) {
        Save-ExportState $statePath @{
          stream            = $title
          outPath           = $outPath
          written           = $written
          page              = $page
          nextUrl           = $nextUrl
          rewindPhases      = $rewindPhases
          offsetChainPages  = $chainPages
          apiHost           = $ApiHost
          projectId         = $ProjectId
          eventsSchemaVer   = $eventsSchemaVersion
          updatedUtc        = (Get-Date).ToUniversalTime().ToString("o")
        }
      }

      if ($SleepMsBetweenPages -gt 0) {
        Start-Sleep -Milliseconds $SleepMsBetweenPages
      }
    }

    if ($statePath -and (Test-Path -LiteralPath $statePath) -and (-not $exportStoppedEarly)) {
      Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
      Write-Info "${title}: removed completed state file $(Split-Path -Leaf $statePath)"
    }
  }
  finally {
    if ($writer) { $writer.Dispose() }
  }

  $finalBytes = Get-JsonlOutFileSize $outPath
  if ($rewindPhases -gt 0) {
    Write-Info ('{0}: pagination rewind phases (PostHog offset limit): {1}' -f $title, $rewindPhases)
  }
  Write-Info ('{0}: done. total_rows={1} file={2} ({3} bytes) path={4}' -f $title, $written, (Format-DataSize $finalBytes), $finalBytes, $outPath)
}

# --- Executable script body (PS 5.1): only after all functions above. ---

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

if ($ExcludePersons) { $IncludePersons = $false }

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

if ($ProgressIntervalPages -lt 0) {
  Write-Host "[ERROR] -ProgressIntervalPages must be >= 0 (0 = only start/end lines per stream)"
  exit 1
}

if ($OffsetRewindThreshold -lt 1 -or $OffsetRewindThreshold -gt 50000) {
  Write-Host "[ERROR] -OffsetRewindThreshold must be in 1..50000 (PostHog disables offset>50000)."
  exit 1
}

if ($MaxPagesPerOffsetChain -lt 1 -or $MaxPagesPerOffsetChain -gt 50) {
  Write-Host "[ERROR] -MaxPagesPerOffsetChain must be in 1..50 (matches ~50k-row cap on /events offset pagination)."
  exit 1
}

if ($BeforeSubtractMilliseconds -lt 0 -or $BeforeSubtractMilliseconds -gt 3600000) {
  Write-Host "[ERROR] -BeforeSubtractMilliseconds must be in 0..3600000 (0 disables subtraction)."
  exit 1
}

if ($On500BeforeSkipMinutes -lt 0 -or $On500BeforeSkipMinutes -gt 10080) {
  Write-Host "[ERROR] -On500BeforeSkipMinutes must be in 0..10080 (0 = do not shift before= on GET /events 500 recovery)."
  exit 1
}

if ($On500ReducedLimit -lt 0 -or $On500ReducedLimit -gt 1000) {
  Write-Host "[ERROR] -On500ReducedLimit must be in 0..1000 (0 = skip reduced-limit recovery passes)."
  exit 1
}

$ApiHost = $ApiHost.Trim().Trim('"').Trim("'").TrimEnd("/")

if ([string]::IsNullOrWhiteSpace($OutDir)) {
  $OutDir = Join-Path $PSScriptRoot "posthog_exports"
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }

[long]$eventsInitialWritten = 0
[int]$eventsInitialOffsetChainPages = 0
$eventsAppend = $false
$eventsUrl = $null
$eventsStatePath = $null

if (-not [string]::IsNullOrWhiteSpace($ResumeFromStateFile)) {
  if (-not (Test-Path -LiteralPath $ResumeFromStateFile)) {
    Write-Host "[ERROR] -ResumeFromStateFile not found: $ResumeFromStateFile"
    exit 1
  }
  $resumeObj = Get-Content -LiteralPath $ResumeFromStateFile -Raw -Encoding utf8 | ConvertFrom-Json
  if ($resumeObj.stream -and [string]$resumeObj.stream -ne "events") {
    Write-Host "[ERROR] Resume state is for stream '$($resumeObj.stream)'; only 'events' is supported."
    exit 1
  }
  $eventsPath = [string]$resumeObj.outPath
  $eventsUrl = [string]$resumeObj.nextUrl
  $eventsInitialWritten = [long]$resumeObj.written
  if ($null -ne $resumeObj.offsetChainPages) {
    $eventsInitialOffsetChainPages = [int]$resumeObj.offsetChainPages
  }
  $eventsAppend = $true
  $eventsStatePath = $ResumeFromStateFile
  if ([string]::IsNullOrWhiteSpace($eventsUrl)) {
    Write-Host "[ERROR] Resume state has empty nextUrl (nothing to fetch; remove state file if export already completed)."
    exit 1
  }
  $stamp = "resume"
  $personsPath = Join-Path $OutDir "ALL_persons_${stamp}.jsonl"
  Write-Info "Resume: continuing events at written=$eventsInitialWritten -> $eventsPath"
  Write-Warn "Resume: persons export is skipped (run a full export without -ResumeFromStateFile if you need persons)."
  $IncludePersons = $false
} else {
  $stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
  $eventsFilePrefix = if ($TelemetrySchemaVersion -gt 0) { "ALL_events_schema_ge${TelemetrySchemaVersion}_" } else { "ALL_events_" }
  $eventsPath = Join-Path $OutDir "${eventsFilePrefix}${stamp}.jsonl"
  $personsPath = Join-Path $OutDir "ALL_persons_${stamp}.jsonl"
  $eventsUrl = Build-EventsListUrl -limit $Limit -schemaVersion $TelemetrySchemaVersion
  $eventsStatePath = "${eventsPath}.export_state.json"
}

Write-Info "PostHog GET /api/projects/.../events (analytics events, not org activity_log): offset>=50000 is disabled (posthog.com/docs/api/events). Rewind via before=<oldest row timestamp> when next hits -OffsetRewindThreshold ($OffsetRewindThreshold) or after -MaxPagesPerOffsetChain ($MaxPagesPerOffsetChain) pages (Cloud may hide offset= in next)."

$progressEveryResolved = $ProgressIntervalPages
if ($progressEveryResolved -eq 0) {
  $progressEveryResolved = -1
}

$headers = @{ Authorization = "Bearer $PersonalApiKey" }

$useFast = (-not $NoFastJson) -and (Test-JsonDocumentAvailable)
$useJsHttp = (-not $useFast) -and (-not $NoFastJson) -and (Test-JavaScriptSerializerAvailable)

if ($useFast) {
  Write-Info "Using fast JSON path (HttpClient + System.Text.Json JsonDocument)."
} elseif ($useJsHttp) {
  Write-Info "Using compat fast path (HttpClient + JavaScriptSerializer; avoids per-row ConvertTo-Json on Windows PowerShell 5.1)."
} else {
  Write-Info "Using legacy JSON path (Invoke-RestMethod + ConvertTo-Json). Install PowerShell 7 (pwsh) or use a full .NET Framework host for faster export."
  if ($NoFastJson) { Write-Info "(-NoFastJson set.)" }
}

Write-Info "API host: $ApiHost"
Write-Info "ProjectId: $ProjectId"
Write-Info "OutDir: $OutDir"
Write-Info "Page size (-Limit): $Limit"
if ($On500BeforeSkipMinutes -gt 0 -or $On500ReducedLimit -gt 0) {
  Write-Info "After repeated 5xx on GET /events/, try recovery URLs: -On500BeforeSkipMinutes=$On500BeforeSkipMinutes, -On500ReducedLimit=$On500ReducedLimit (strip offset=; shift before= / lower limit)."
}
if ($TelemetrySchemaVersion -gt 0) {
  Write-Info "Events filter: telemetry_schema_version >= $TelemetrySchemaVersion (PostHog properties[], operator gte)."
  if ($IncludePersons) {
    Write-Warn "Person export is not filtered by telemetry_schema_version; use -ExcludePersons (or telemetry filter in batch) if you only need events with schema >= ${TelemetrySchemaVersion}."
  }
} else {
  Write-Info "Events filter: none (all schema versions)."
}

Write-Info "Output files (will grow during download):"
Write-Info "  events  -> $eventsPath"
if ($IncludePersons) {
  Write-Info "  persons -> $personsPath"
} else {
  Write-Info "  persons -> (skipped, events-only mode)"
}
if ($ProgressIntervalPages -eq 0) {
  Write-Info "Progress: first + last page per stream only (-ProgressIntervalPages 0). PostHog does not return total event count upfront."
} elseif ($ProgressIntervalPages -eq 1) {
  Write-Info "Progress: every page. PostHog does not return total event count upfront; watch total_rows and file_size grow."
} else {
  Write-Info "Progress: every $ProgressIntervalPages page(s), plus last page of each stream. PostHog does not return total event count upfront."
}

$savedFiles = New-Object System.Collections.Generic.List[string]

if ($useFast) {
  $httpClient = New-ExportHttpClient $PersonalApiKey
  try {
    ExportPostHogJsonl_Http -httpClient $httpClient -title "events" -firstUrl $eventsUrl -outPath $eventsPath `
      -progressEvery $progressEveryResolved `
      -statePath $eventsStatePath -initialWritten $eventsInitialWritten -append $eventsAppend `
      -eventsLimit $Limit -eventsSchemaVersion $TelemetrySchemaVersion `
      -offsetRewindThreshold $OffsetRewindThreshold -offsetRewindEnabled $true `
      -maxPagesPerOffsetChain $MaxPagesPerOffsetChain -initialOffsetChainPages $eventsInitialOffsetChainPages `
      -beforeSubtractMs $BeforeSubtractMilliseconds `
      -enableEventsHttp500Recovery $true -on500BeforeSkipMinutes $On500BeforeSkipMinutes -on500ReducedLimit $On500ReducedLimit
    [void]$savedFiles.Add($eventsPath)

    if ($IncludePersons) {
      $personsUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"
      ExportPostHogJsonl_Http -httpClient $httpClient -title "persons" -firstUrl $personsUrl -outPath $personsPath `
        -progressEvery $progressEveryResolved -statePath "" -offsetRewindEnabled $false
      [void]$savedFiles.Add($personsPath)
    }
  }
  finally {
    $httpClient.Dispose()
  }
} elseif ($useJsHttp) {
  $httpClient = New-ExportHttpClient $PersonalApiKey
  $jsonSer = New-JavaScriptSerializerForExport
  try {
    ExportPostHogJsonl_HttpJs -httpClient $httpClient -jsonSerializer $jsonSer -title "events" -firstUrl $eventsUrl -outPath $eventsPath `
      -progressEvery $progressEveryResolved `
      -statePath $eventsStatePath -initialWritten $eventsInitialWritten -append $eventsAppend `
      -eventsLimit $Limit -eventsSchemaVersion $TelemetrySchemaVersion `
      -offsetRewindThreshold $OffsetRewindThreshold -offsetRewindEnabled $true `
      -maxPagesPerOffsetChain $MaxPagesPerOffsetChain -initialOffsetChainPages $eventsInitialOffsetChainPages `
      -beforeSubtractMs $BeforeSubtractMilliseconds `
      -enableEventsHttp500Recovery $true -on500BeforeSkipMinutes $On500BeforeSkipMinutes -on500ReducedLimit $On500ReducedLimit
    [void]$savedFiles.Add($eventsPath)

    if ($IncludePersons) {
      $personsUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"
      ExportPostHogJsonl_HttpJs -httpClient $httpClient -jsonSerializer $jsonSer -title "persons" -firstUrl $personsUrl -outPath $personsPath `
        -progressEvery $progressEveryResolved -statePath "" -offsetRewindEnabled $false
      [void]$savedFiles.Add($personsPath)
    }
  }
  finally {
    $httpClient.Dispose()
  }
} else {
  ExportPostHogJsonl_Rest -title "events" -firstUrl $eventsUrl -outPath $eventsPath `
    -progressEvery $progressEveryResolved -RequestHeaders $headers `
    -statePath $eventsStatePath -initialWritten $eventsInitialWritten -append $eventsAppend `
    -eventsLimit $Limit -eventsSchemaVersion $TelemetrySchemaVersion `
    -offsetRewindThreshold $OffsetRewindThreshold -offsetRewindEnabled $true `
    -maxPagesPerOffsetChain $MaxPagesPerOffsetChain -initialOffsetChainPages $eventsInitialOffsetChainPages `
    -beforeSubtractMs $BeforeSubtractMilliseconds `
    -enableEventsHttp500Recovery $true -on500BeforeSkipMinutes $On500BeforeSkipMinutes -on500ReducedLimit $On500ReducedLimit
  [void]$savedFiles.Add($eventsPath)

  if ($IncludePersons) {
    $personsUrl = "$ApiHost/api/projects/$ProjectId/persons/?limit=$Limit"
    ExportPostHogJsonl_Rest -title "persons" -firstUrl $personsUrl -outPath $personsPath `
      -progressEvery $progressEveryResolved -RequestHeaders $headers -statePath "" -offsetRewindEnabled $false
    [void]$savedFiles.Add($personsPath)
  }
}

Write-Info "========== Export summary: saved files =========="
foreach ($p in $savedFiles) {
  if (-not (Test-Path -LiteralPath $p)) {
    Write-Warn "missing: $p"
    continue
  }
  $fi = Get-Item -LiteralPath $p
  Write-Info ('  {0}' -f $fi.FullName)
  Write-Info ('  size: {0} ({1} bytes)' -f (Format-DataSize $fi.Length), $fi.Length)
}
Write-Info "All exports finished."
exit 0
