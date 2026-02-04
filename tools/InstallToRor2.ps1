param(
    [Parameter(Mandatory = $true)]
    [string]$DllPath
)

$ErrorActionPreference = "Stop"

function Write-Info([string]$msg) { Write-Host "[InstallToRor2] $msg" }
function Write-Warn([string]$msg) { Write-Warning "[InstallToRor2] $msg" }

function Try-GetSteamPath {
    try {
        $p = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name "SteamPath" -ErrorAction Stop).SteamPath
        if ($p) { return $p }
    } catch {}
    try {
        $p = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name "InstallPath" -ErrorAction Stop).InstallPath
        if ($p) { return $p }
    } catch {}
    return $null
}

function Get-SteamLibraries([string]$steamPath) {
    $libs = New-Object System.Collections.Generic.List[string]
    if (-not $steamPath) { return $libs }

    $libs.Add($steamPath) | Out-Null
    $vdf = Join-Path $steamPath "steamapps\libraryfolders.vdf"
    if (-not (Test-Path $vdf)) { return $libs }

    $content = Get-Content -Path $vdf -ErrorAction Stop
    foreach ($line in $content) {
        # New format contains: "path"  "D:\\SteamLibrary"
        if ($line -match '^\s*"path"\s*"([^"]+)"') {
            $path = $Matches[1]
            if ($path -and -not $libs.Contains($path)) { $libs.Add($path) | Out-Null }
            continue
        }

        # Old format contains: "1"  "D:\\SteamLibrary"
        if ($line -match '^\s*"\d+"\s*"([^"]+)"') {
            $path = $Matches[1]
            if ($path -and -not $libs.Contains($path)) { $libs.Add($path) | Out-Null }
        }
    }

    return $libs
}

function Try-FindRor2Dir {
    if ($env:ROR2_GAME_PATH -and (Test-Path $env:ROR2_GAME_PATH)) { return $env:ROR2_GAME_PATH }
    if ($env:ROR2_DIR -and (Test-Path $env:ROR2_DIR)) { return $env:ROR2_DIR }
    if ($env:ROR2_PATH -and (Test-Path $env:ROR2_PATH)) { return $env:ROR2_PATH }

    $steam = Try-GetSteamPath
    if (-not $steam) {
        # Common fallback
        $steam = "C:\Program Files (x86)\Steam"
        if (-not (Test-Path $steam)) { return $null }
    }

    $libs = Get-SteamLibraries $steam
    foreach ($lib in $libs) {
        $candidate = Join-Path $lib "steamapps\common\Risk of Rain 2"
        if (Test-Path $candidate) { return $candidate }
    }

    return $null
}

try {
    if (-not (Test-Path $DllPath)) {
        Write-Warn "DLL not found: $DllPath"
        exit 0
    }

    # If user provides plugins path explicitly (e.g., Thunderstore profile), deploy there.
    if ($env:ROR2_PLUGINS_PATH -and (Test-Path $env:ROR2_PLUGINS_PATH)) {
        $destDir = Join-Path $env:ROR2_PLUGINS_PATH "GeneticsArtifact"
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
        $dest = Join-Path $destDir ([System.IO.Path]::GetFileName($DllPath))
        Copy-Item -Force -Path $DllPath -Destination $dest
        Write-Info "Installed (ROR2_PLUGINS_PATH): $dest"
        exit 0
    }

    $ror2 = Try-FindRor2Dir
    if (-not $ror2) {
        Write-Warn "RoR2 directory not found. Set ROR2_GAME_PATH/ROR2_DIR (game dir) or ROR2_PLUGINS_PATH (plugins dir) to enable auto-install."
        exit 0
    }

    $plugins = Join-Path $ror2 "BepInEx\plugins"
    if (-not (Test-Path $plugins)) {
        Write-Warn "BepInEx plugins folder not found: $plugins"
        exit 0
    }

    $destDir = Join-Path $plugins "GeneticsArtifact"
    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    }

    $dest = Join-Path $destDir ([System.IO.Path]::GetFileName($DllPath))
    Copy-Item -Force -Path $DllPath -Destination $dest

    Write-Info "Installed: $dest"
    exit 0
}
catch {
    Write-Warn ("Install failed: " + $_.Exception.Message)
    exit 0
}

