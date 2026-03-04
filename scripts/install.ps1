<#
.SYNOPSIS
    NanoBot.Net Installation Script for Windows

.DESCRIPTION
    Installs NanoBot.Net on Windows systems.
    Supports both x64 and ARM64 architectures.

.PARAMETER InstallDir
    Installation directory. Default: $env:USERPROFILE\.local\bin

.PARAMETER Version
    Version to install. Default: latest

.EXAMPLE
    irm get.nbot.ai | iex
    irm https://raw.githubusercontent.com/NanoBot/NanoBot.Net/main/scripts/install.ps1 | iex
#>

param(
    [string]$InstallDir = "$env:USERPROFILE\.local\bin",
    [string]$Version = "latest"
)

$ErrorActionPreference = "Stop"
$Repo = "mbzcnet/NanoBot.Net"
$BinaryName = "nbot"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] " -ForegroundColor Green -NoNewline
    Write-Host $Message
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] " -ForegroundColor Yellow -NoNewline
    Write-Host $Message
}

function Write-Error {
    param([string]$Message)
    Write-Host "[ERROR] " -ForegroundColor Red -NoNewline
    Write-Host $Message
    exit 1
}

function Get-Platform {
    $Arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
    
    switch ($Arch) {
        "X64" { $ArchStr = "x64" }
        "Arm64" { $ArchStr = "arm64" }
        default { Write-Error "Unsupported architecture: $Arch" }
    }
    
    $Platform = "win-$ArchStr"
    Write-Info "Detected platform: $Platform"
    return $Platform
}

function Get-LatestVersion {
    if ($Version -eq "latest") {
        $Release = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest"
        $Version = $Release.tag_name -replace "^v", ""
        if (-not $Version) {
            Write-Error "Failed to get latest version"
        }
    }
    Write-Info "Installing version: $Version"
    return $Version
}

function Install-NanoBot {
    param([string]$Platform, [string]$Version)
    
    $DownloadUrl = "https://github.com/$Repo/releases/download/v$Version/$BinaryName-$Platform.zip"
    $TempDir = New-TempDirectory
    $ZipFile = Join-Path $TempDir "$BinaryName.zip"
    
    Write-Info "Downloading from $DownloadUrl..."
    
    try {
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $ZipFile -UseBasicParsing
    }
    catch {
        Write-Error "Failed to download nbot: $_"
    }
    
    Write-Info "Extracting..."
    Expand-Archive -Path $ZipFile -DestinationPath $TempDir -Force
    
    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }
    
    $ExePath = Join-Path $TempDir "$BinaryName.exe"
    $DestPath = Join-Path $InstallDir "$BinaryName.exe"
    Move-Item -Path $ExePath -Destination $DestPath -Force
    
    Remove-Item -Path $TempDir -Recurse -Force
    
    Write-Info "Installed to $DestPath"
    return $DestPath
}

function New-TempDirectory {
    $TempPath = Join-Path $env:TEMP "nbot-install-$(Get-Random)"
    New-Item -ItemType Directory -Path $TempPath | Out-Null
    return $TempPath
}

function Add-ToPath {
    $PathEnv = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($PathEnv -notlike "*$InstallDir*") {
        Write-Warn "$InstallDir is not in PATH"
        Write-Host ""
        Write-Host "Adding to PATH..."
        [Environment]::SetEnvironmentVariable("Path", "$PathEnv;$InstallDir", "User")
        $env:Path += ";$InstallDir"
        Write-Info "Added to user PATH"
    }
}

function Verify-Installation {
    param([string]$ExePath)
    
    if (Test-Path $ExePath) {
        Write-Info "Installation successful!"
        & $ExePath --version
    }
    else {
        Write-Error "Installation failed"
    }
}

Write-Host ""
Write-Host "=========================================="
Write-Host "     NanoBot.Net Installer"
Write-Host "=========================================="
Write-Host ""

$Platform = Get-Platform
$Version = Get-LatestVersion
$ExePath = Install-NanoBot -Platform $Platform -Version $Version
Add-ToPath
Verify-Installation -ExePath $ExePath

Write-Host ""
Write-Host "Run 'nbot onboard' to get started!"
