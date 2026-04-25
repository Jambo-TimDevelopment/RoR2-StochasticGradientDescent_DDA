param(
  [string]$ApiKey = "",
  [string]$IngestHost = "https://us.i.posthog.com",
  [string]$DistinctId = "debug_user_001",
  [string]$Event = "manual_test_event"
)

if ([string]::IsNullOrWhiteSpace($ApiKey)) {
  Write-Host "[ERROR] ApiKey is empty. Set -ApiKey 'phc_...'"
  exit 1
}

$sha = [System.Security.Cryptography.SHA256]::Create()
$hashBytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($ApiKey))
$hashHex = -join ($hashBytes | ForEach-Object { $_.ToString("x2") })

$IngestHost = $IngestHost.TrimEnd("/")
$uri = "$IngestHost/batch/"

$payload = @{
  api_key = $ApiKey
  batch   = @(
    @{
      event      = $Event
      properties = @{
        distinct_id = $DistinctId
        source      = "powershell_test"
      }
    }
  )
} | ConvertTo-Json -Depth 6 -Compress

Write-Host "[INFO] POST $uri"
Write-Host "[INFO] event=$Event distinct_id=$DistinctId"
Write-Host "[INFO] api_key sha256=$hashHex"

try {
  $resp = Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body $payload
  Write-Host "[OK] Response:"
  $resp | ConvertTo-Json -Depth 20
  exit 0
}
catch {
  Write-Host "[ERROR] Request failed:"
  Write-Host $_.Exception.Message
  if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
    Write-Host ("[ERROR] HTTP status: " + [int]$_.Exception.Response.StatusCode)
  }
  exit 2
}

