#Requires -Version 5.1
<#
.SYNOPSIS
    NanoBot.Net installer for Windows (PowerShell).

.DESCRIPTION
    Detects OS architecture, downloads the correct binary from GitHub releases,
    verifies SHA256, and installs to the user's chosen directory.

.PARAMETER Version
    Specific version to install (default: latest).

.PARAMETER InstallDir
    Target installation directory (default: $env:LOCALAPPDATA\nanobot\bin).

.PARAMETER AddToPath
    Add installation directory to system PATH (default: true).

.PARAMETER NoVerify
    Skip SHA256 verification.

.PARAMETER SkipConfig
    Skip post-install configuration wizard.

.EXAMPLE
    irm https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.ps1 | iex

.EXAMPLE
    .\install.ps1 -Version 0.1.4 -InstallDir "$env:ProgramFiles\nanobot"
#>

[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $Version,

    [ValidateScript({ Test-Path $_ -IsValid })]
    [string] $InstallDir = "$env:LOCALAPPDATA\nanobot\bin",

    [bool] $AddToPath = $true,

    [switch] $NoVerify,

    [switch] $SkipConfig
)

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'  # suppress slow-progress bars

# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
$Repo        = "mbzcnet/NanoBot.Net"
$BinaryName  = "nbot"
$DefaultVer  = "0.1.4"
$TempDir     = Join-Path $env:TEMP "nbot-install-$( [guid]::NewGuid().ToString('N') )"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Info   ($msg) { Write-Host "[INFO]  $msg" -ForegroundColor Green }
function Write-Step   ($msg) { Write-Host "==> $msg" -ForegroundColor Blue }
function Write-Warn   ($msg) { Write-Host "[WARN]  $msg" -ForegroundColor Yellow 2>&1 }
function Write-Err    ($msg) { Write-Host "[ERROR] $msg" -ForegroundColor Red; throw $msg }
function Write-Success($msg) { Write-Host "[OK]    $msg" -ForegroundColor Green }

# ---------------------------------------------------------------------------
# Platform detection
# ---------------------------------------------------------------------------
function Get-Platform {
    $arch = $env:PROCESSOR_ARCHITECTURE
    if ($arch -eq 'ARM64') { $rid = "win-arm64" }
    elseif ([Environment]::Is64BitOperatingSystem) { $rid = "win-x64" }
    else { Write-Err "Only 64-bit Windows is supported." }

    Write-Info "Detected platform: $rid"
    return $rid
}

# ---------------------------------------------------------------------------
# Prerequisites
# ---------------------------------------------------------------------------
function Test-Prerequisites {
    Write-Step "Checking prerequisites..."

    # PowerShell 5.1+ is checked by #Requires above.
    # 'curl' is aliased to Invoke-WebRequest in PowerShell.
    if (-not (Get-Command -Name Invoke-WebRequest -ErrorAction SilentlyContinue)) {
        Write-Err "Invoke-WebRequest (PowerShell) is required but not available."
    }

    if (-not (Get-Command -Name Expand-Archive -ErrorAction SilentlyContinue)) {
        Write-Err "Expand-Archive is required (PowerShell 5.1+)."
    }

    Write-Success "Prerequisites OK"
}

# ---------------------------------------------------------------------------
# Resolve version
# ---------------------------------------------------------------------------
function Get-LatestVersion {
    Write-Step "Detecting latest version..."
    $url = "https://api.github.com/repos/$Repo/releases/latest"
    $tag = (Invoke-GHApi $url).tag_name -replace '^v', ''
    if ([string]::IsNullOrEmpty($tag)) {
        Write-Err "Failed to detect latest version."
    }
    Write-Info "Latest version: $tag"
    return $tag
}

function Invoke-GHApi {
    param([string] $Uri)
    try {
        $resp = Invoke-RestMethod -Uri $Uri -UserAgent "nbot-install" -TimeoutSec 15
        return $resp
    }
    catch { return $null }
}

# ---------------------------------------------------------------------------
# Download & verify
# ---------------------------------------------------------------------------
function Get-NbotArtifact {
    param([string] $Rid, [string] $Ver)

    Write-Step "Downloading nbot ${Ver} for ${Rid}..."

    $url      = "https://github.com/$Repo/releases/download/v${Ver}/${BinaryName}-${Rid}.zip"
    $archive  = Join-Path $TempDir "${BinaryName}-${Rid}.zip"

    New-Item -ItemType Directory -Force -Path $TempDir | Out-Null
    Remove-Item $archive -Force -ErrorAction SilentlyContinue

    try {
        Invoke-WebRequest -Uri $url -OutFile $archive `
            -UserAgent "nbot-install" `
            -MaximumRedirection 2 `
            -ErrorAction Stop
    }
    catch { Write-Err "Download failed: $url`n$_" }

    Write-Success "Downloaded: $archive"
    return $archive
}

function Test-Checksum {
    param([string] $ArchivePath, [string] $Rid, [string] $Ver)

    if ($NoVerify) {
        Write-Warn "Skipping SHA256 verification (--NoVerify)."
        return
    }

    Write-Step "Verifying SHA256 checksum..."

    $shaUrl  = "https://github.com/$Repo/releases/download/v${Ver}/${BinaryName}-${Rid}.zip.sha256"
    $expected = $null

    try {
        $shaResp = Invoke-WebRequest -Uri $shaUrl -UseBasicParsing -TimeoutSec 10
        $expected = ($shaResp.Content -split '\s')[0].Trim()
    }
    catch {
        Write-Warn "Checksum file not found at $shaUrl — skipping verification."
        return
    }

    $actual = (Get-FileHash -Path $ArchivePath -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $expected.ToLower()) {
        Write-Err "Checksum mismatch!`n  Expected: $expected`n  Actual:   $actual"
    }

    Write-Success "Checksum verified"
}

# ---------------------------------------------------------------------------
# Install
# ---------------------------------------------------------------------------
function Install-NbotBinary {
    param([string] $ArchivePath, [string] $Rid, [string] $DestDir)

    Write-Step "Installing to $DestDir..."

    New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

    # Expand-Archive overwrites without warning
    Expand-Archive -Path $ArchivePath -DestinationPath $TempDir -Force

    $extractedDir = Join-Path $TempDir $Rid
    $binary       = Get-ChildItem -Path $extractedDir -Filter "${BinaryName}.exe" -Recurse | Select-Object -First 1

    if (-not $binary) {
        Write-Err "Binary '${BinaryName}.exe' not found in archive."
    }

    Copy-Item $binary.FullName -Destination $DestDir -Force
    # Ensure executable flag (NTFS doesn't track it, but good practice)
    # nbot.exe on Windows runs directly.

    # Copy WebUI assets
    $webuiSrc = Join-Path $extractedDir "webui"
    if (Test-Path $webuiSrc) {
        $webuiDest = Join-Path $DestDir "webui"
        Remove-Item $webuiDest -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item $webuiSrc -Destination $webuiDest -Recurse -Force
        Write-Info "WebUI assets installed to $webuiDest"
    }

    Write-Success "Installed: $(Join-Path $DestDir ${BinaryName}.exe)"
}

# ---------------------------------------------------------------------------
# PATH setup
# ---------------------------------------------------------------------------
function Add-ToPath {
    param([string] $InstallPath)

    $pathEntries = [Environment]::GetEnvironmentVariable("Path", "User") -split ';'

    # Normalise and check duplicates
    $normPath = $pathEntries | ForEach-Object { $_.TrimEnd('\') } | Where-Object { $_ -ne '' }
    if ($normPath -contains $InstallPath) {
        Write-Info "Installation directory is already in user PATH."
        return
    }

    Write-Step "Adding $InstallPath to user PATH..."

    $newPath = "$InstallPath;" + ($normPath -join ';')
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")

    # Update for current session
    $env:Path = "$InstallPath;$env:Path"

    Write-Info "PATH updated. Restart any open terminals for changes to take effect."
}

# ---------------------------------------------------------------------------
# Post-install config
# ---------------------------------------------------------------------------
function Invoke-PostInstall {
    Write-Step "Running post-install configuration..."

    $nbotPath = Join-Path $InstallDir "${BinaryName}.exe"

    if (-not (Test-Path $nbotPath)) {
        Write-Warn "Binary not found at $nbotPath — cannot run post-install."
        return
    }

    if (-not $SkipConfig) {
        Write-Info "Launching configuration wizard..."
        & $nbotPath configure
    }
    else {
        Write-Info "Skipped — run 'nbot configure' manually to set up API keys and preferences."
    }
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
function Main {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "   NanoBot.Net Installer (Windows)" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""

    Test-Prerequisites

    if ([string]::IsNullOrEmpty($Version)) {
        $Version = Get-LatestVersion
    }
    Write-Host "  Version:    $Version"
    Write-Host "  Repository: https://github.com/$Repo"
    Write-Host "  Install to: $InstallDir"
    Write-Host ""

    $Rid = Get-Platform

    $archive = Get-NbotArtifact -Rid $Rid -Ver $Version
    Test-Checksum -ArchivePath $archive -Rid $Rid -Ver $Version
    Install-NbotBinary -ArchivePath $archive -Rid $Rid -DestDir $InstallDir

    if ($AddToPath) {
        Add-ToPath $InstallDir
    }

    Invoke-PostInstall

    # Cleanup
    Remove-Item $TempDir -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "  NanoBot.Net ${Version} installed!" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Binary:  $(Join-Path $InstallDir ${BinaryName}.exe)"
    Write-Host "  Platform: $Rid"
    Write-Host ""
    Write-Host "  Get started:"
    Write-Host "    nbot configure   # First-time setup"
    Write-Host "    nbot agent       # Start the agent"
    Write-Host "    nbot --help      # Full command list"
    Write-Host ""
}

Main
