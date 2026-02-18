# NanoBot.Net 安装程序设计

本文档定义 NanoBot.Net 的安装与分发方案，确保用户能够便捷地在各平台上安装和使用。

---

## 概述

NanoBot.Net 作为 .NET 应用程序，支持多种安装方式，覆盖 macOS、Linux、Windows（包括 WSL）等平台。

### 安装方式一览

| 平台 | 推荐方式 | 备选方式 |
|------|----------|----------|
| macOS | Homebrew | dotnet tool、安装脚本 |
| Linux | Homebrew | dotnet tool、安装脚本 |
| Windows | winget | PowerShell 脚本、dotnet tool |
| WSL | Homebrew | dotnet tool、安装脚本 |

---

## 一、Homebrew 安装 (macOS / Linux / WSL)

### 1.1 Formula 设计

创建 Homebrew Formula，发布到自定义 Tap。

**文件位置**: `homebrew-nanobot/Formula/nanobot.rb`

```ruby
class Nanobot < Formula
  desc "A lightweight personal AI assistant framework (.NET)"
  homepage "https://github.com/NanoBot/NanoBot.Net"
  version "0.1.0"
  license "MIT"

  on_macos do
    on_intel do
      url "https://github.com/NanoBot/NanoBot.Net/releases/download/v#{version}/nanobot-osx-x64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
    on_arm do
      url "https://github.com/NanoBot/NanoBot.Net/releases/download/v#{version}/nanobot-osx-arm64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
  end

  on_linux do
    on_intel do
      url "https://github.com/NanoBot/NanoBot.Net/releases/download/v#{version}/nanobot-linux-x64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
    on_arm do
      url "https://github.com/NanoBot/NanoBot.Net/releases/download/v#{version}/nanobot-linux-arm64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
  end

  def install
    bin.install "nanobot"
    pkgshare.install "workspace"
  end

  test do
    assert_match "NanoBot", shell_output("#{bin}/nanobot --version")
  end
end
```

### 1.2 Tap 仓库结构

```
homebrew-nanobot/
├── Formula/
│   └── nanobot.rb
├── README.md
└── .github/
    └── workflows/
        └── update-formula.yml  # 自动更新 formula
```

### 1.3 用户安装命令

```bash
# 添加 Tap
brew tap NanoBot/tap

# 安装
brew install nanobot

# 验证安装
nanobot --version
```

### 1.4 依赖处理

```ruby
# 如果需要 .NET 运行时
depends_on "dotnet" => :runtime

# 或使用单文件发布（推荐，无需运行时依赖）
# 发布时使用: dotnet publish -r osx-arm64 --self-contained true
```

---

## 二、Winget 安装 (Windows)

### 2.1 Manifest 设计

Winget 使用 YAML manifest 文件，需要三个文件：

**文件位置**: `winget-pkgs/manifests/n/NanoBot/NanoBot/0.1.0/`

#### 2.1.1 NanoBot.NanoBot.yaml

```yaml
PackageIdentifier: NanoBot.NanoBot
PackageVersion: 0.1.0
PackageLocale: en-US
Publisher: NanoBot
PublisherUrl: https://github.com/NanoBot
PublisherSupportUrl: https://github.com/NanoBot/NanoBot.Net/issues
PackageName: NanoBot
PackageUrl: https://github.com/NanoBot/NanoBot.Net
License: MIT
LicenseUrl: https://github.com/NanoBot/NanoBot.Net/blob/main/LICENSE
Copyright: Copyright (c) NanoBot
ShortDescription: A lightweight personal AI assistant framework
Description: |
  NanoBot.Net is a lightweight personal AI assistant framework ported from nanobot.
  It provides a modular architecture for building AI agents with support for
  multiple LLM providers, channels, and tools.
Moniker: nanobot
Tags:
  - ai
  - assistant
  - llm
  - agent
  - dotnet
ReleaseNotesUrl: https://github.com/NanoBot/NanoBot.Net/releases/tag/v0.1.0
```

#### 2.1.2 NanoBot.NanoBot.installer.yaml

```yaml
PackageIdentifier: NanoBot.NanoBot
PackageVersion: 0.1.0
InstallerLocale: en-US
InstallerType: zip
InstallModes:
  - silent
  - silentWithProgress
Commands:
  - nanobot
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/NanoBot/NanoBot.Net/releases/download/v0.1.0/nanobot-win-x64.zip
    InstallerSha256: TODO: 计算实际值
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: nanobot.exe
        PortableCommandAlias: nanobot
  - Architecture: arm64
    InstallerUrl: https://github.com/NanoBot/NanoBot.Net/releases/download/v0.1.0/nanobot-win-arm64.zip
    InstallerSha256: TODO: 计算实际值
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: nanobot.exe
        PortableCommandAlias: nanobot
ManifestType: installer
ManifestVersion: 1.6.0
```

#### 2.1.3 NanoBot.NanoBot.locale.en-US.yaml

```yaml
PackageIdentifier: NanoBot.NanoBot
PackageVersion: 0.1.0
PackageLocale: en-US
Publisher: NanoBot
PackageName: NanoBot
PackageUrl: https://github.com/NanoBot/NanoBot.Net
License: MIT
ShortDescription: A lightweight personal AI assistant framework
Description: |
  NanoBot.Net is a lightweight personal AI assistant framework ported from nanobot.
  It provides a modular architecture for building AI agents with support for
  multiple LLM providers, channels, and tools.
ManifestType: defaultLocale
ManifestVersion: 1.6.0
```

### 2.2 用户安装命令

```powershell
# 安装
winget install NanoBot.NanoBot

# 验证安装
nanobot --version
```

---

## 三、安装脚本

### 3.1 Unix 安装脚本 (macOS / Linux / WSL)

**文件位置**: `scripts/install.sh`

```bash
#!/bin/bash
#
# NanoBot.Net Installation Script
# Supports: macOS, Linux, WSL
#
# Usage:
#   curl -fsSL https://get.nanobot.ai | bash
#   or
#   curl -fsSL https://raw.githubusercontent.com/NanoBot/NanoBot.Net/main/scripts/install.sh | bash
#

set -e

REPO="NanoBot/NanoBot.Net"
BINARY_NAME="nanobot"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/bin}"
VERSION="${VERSION:-latest}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

error() {
    echo -e "${RED}[ERROR]${NC} $1"
    exit 1
}

# Detect OS and Architecture
detect_platform() {
    OS="$(uname -s)"
    ARCH="$(uname -m)"

    case "$OS" in
        Darwin*)
            OS="osx"
            ;;
        Linux*)
            OS="linux"
            ;;
        MINGW*|MSYS*|CYGWIN*)
            error "Please use install.ps1 for Windows. For WSL, run this script in WSL environment."
            ;;
        *)
            error "Unsupported OS: $OS"
            ;;
    esac

    case "$ARCH" in
        x86_64|amd64)
            ARCH="x64"
            ;;
        aarch64|arm64)
            ARCH="arm64"
            ;;
        *)
            error "Unsupported architecture: $ARCH"
            ;;
    esac

    PLATFORM="${OS}-${ARCH}"
    info "Detected platform: $PLATFORM"
}

# Get latest version from GitHub
get_latest_version() {
    if [ "$VERSION" = "latest" ]; then
        VERSION=$(curl -s "https://api.github.com/repos/$REPO/releases/latest" | grep '"tag_name":' | sed -E 's/.*"v([^"]+)".*/\1/')
        if [ -z "$VERSION" ]; then
            error "Failed to get latest version"
        fi
    fi
    info "Installing version: $VERSION"
}

# Download and install
install_nanobot() {
    DOWNLOAD_URL="https://github.com/$REPO/releases/download/v$VERSION/${BINARY_NAME}-${PLATFORM}.tar.gz"
    TEMP_DIR=$(mktemp -d)
    ARCHIVE_FILE="$TEMP_DIR/${BINARY_NAME}.tar.gz"

    info "Downloading from $DOWNLOAD_URL..."

    if ! curl -fsSL "$DOWNLOAD_URL" -o "$ARCHIVE_FILE"; then
        error "Failed to download NanoBot"
    fi

    info "Extracting..."
    tar -xzf "$ARCHIVE_FILE" -C "$TEMP_DIR"

    # Create install directory if not exists
    mkdir -p "$INSTALL_DIR"

    # Move binary
    mv "$TEMP_DIR/$BINARY_NAME" "$INSTALL_DIR/$BINARY_NAME"
    chmod +x "$INSTALL_DIR/$BINARY_NAME"

    # Cleanup
    rm -rf "$TEMP_DIR"

    info "Installed to $INSTALL_DIR/$BINARY_NAME"
}

# Add to PATH if needed
add_to_path() {
    if ! echo "$PATH" | grep -q "$INSTALL_DIR"; then
        warn "$INSTALL_DIR is not in PATH"
        echo ""
        echo "Add the following to your shell profile (~/.bashrc, ~/.zshrc, etc.):"
        echo ""
        echo "    export PATH=\"\$PATH:$INSTALL_DIR\""
        echo ""
        echo "Then run: source ~/.bashrc  (or ~/.zshrc)"
    fi
}

# Verify installation
verify_installation() {
    if [ -x "$INSTALL_DIR/$BINARY_NAME" ]; then
        info "Installation successful!"
        "$INSTALL_DIR/$BINARY_NAME" --version
    else
        error "Installation failed"
    fi
}

main() {
    echo ""
    echo "=========================================="
    echo "     NanoBot.Net Installer"
    echo "=========================================="
    echo ""

    detect_platform
    get_latest_version
    install_nanobot
    add_to_path
    verify_installation

    echo ""
    echo "Run 'nanobot onboard' to get started!"
}

main "$@"
```

### 3.2 Windows PowerShell 安装脚本

**文件位置**: `scripts/install.ps1`

```powershell
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
    irm get.nanobot.ai | iex
    irm https://raw.githubusercontent.com/NanoBot/NanoBot.Net/main/scripts/install.ps1 | iex
#>

param(
    [string]$InstallDir = "$env:USERPROFILE\.local\bin",
    [string]$Version = "latest"
)

$ErrorActionPreference = "Stop"
$Repo = "NanoBot/NanoBot.Net"
$BinaryName = "nanobot"

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
        Write-Error "Failed to download NanoBot: $_"
    }
    
    Write-Info "Extracting..."
    Expand-Archive -Path $ZipFile -DestinationPath $TempDir -Force
    
    # Create install directory if not exists
    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }
    
    # Move binary
    $ExePath = Join-Path $TempDir "$BinaryName.exe"
    $DestPath = Join-Path $InstallDir "$BinaryName.exe"
    Move-Item -Path $ExePath -Destination $DestPath -Force
    
    # Cleanup
    Remove-Item -Path $TempDir -Recurse -Force
    
    Write-Info "Installed to $DestPath"
    return $DestPath
}

function New-TempDirectory {
    $TempPath = Join-Path $env:TEMP "nanobot-install-$(Get-Random)"
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

# Main
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
Write-Host "Run 'nanobot onboard' to get started!"
```

### 3.3 Windows CMD 批处理脚本

**文件位置**: `scripts/install.cmd`

```batch
@echo off
REM NanoBot.Net Installation Script for Windows CMD
REM
REM Usage:
REM   curl -fsSL https://get.nanobot.ai/cmd -o install.cmd && install.cmd

setlocal enabledelayedexpansion

set REPO=NanoBot/NanoBot.Net
set BINARY_NAME=nanobot
set INSTALL_DIR=%USERPROFILE%\.local\bin
set VERSION=latest

echo.
echo ==========================================
echo      NanoBot.Net Installer
echo ==========================================
echo.

REM Detect architecture
if "%PROCESSOR_ARCHITECTURE%"=="AMD64" (
    set ARCH=x64
) else if "%PROCESSOR_ARCHITECTURE%"=="ARM64" (
    set ARCH=arm64
) else (
    echo [ERROR] Unsupported architecture: %PROCESSOR_ARCHITECTURE%
    exit /b 1
)

set PLATFORM=win-%ARCH%
echo [INFO] Detected platform: %PLATFORM%

REM Get latest version
if "%VERSION%"=="latest" (
    echo [INFO] Fetching latest version...
    for /f "tokens=2 delims=:," %%a in ('curl -s "https://api.github.com/repos/%REPO%/releases/latest" ^| findstr "tag_name"') do (
        set VERSION=%%a
        set VERSION=!VERSION:"=!
        set VERSION=!VERSION:v=!
        set VERSION=!VERSION: =!
    )
)

echo [INFO] Installing version: %VERSION%

REM Create temp directory
set TEMP_DIR=%TEMP%\nanobot-install-%RANDOM%
mkdir "%TEMP_DIR%"

REM Download
set DOWNLOAD_URL=https://github.com/%REPO%/releases/download/v%VERSION%/%BINARY_NAME%-%PLATFORM%.zip
set ZIP_FILE=%TEMP_DIR%\%BINARY_NAME%.zip

echo [INFO] Downloading from %DOWNLOAD_URL%...
curl -fsSL "%DOWNLOAD_URL%" -o "%ZIP_FILE%"
if errorlevel 1 (
    echo [ERROR] Failed to download NanoBot
    exit /b 1
)

REM Extract using PowerShell
echo [INFO] Extracting...
powershell -Command "Expand-Archive -Path '%ZIP_FILE%' -DestinationPath '%TEMP_DIR%' -Force"

REM Create install directory
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

REM Move binary
move "%TEMP_DIR%\%BINARY_NAME%.exe" "%INSTALL_DIR%\%BINARY_NAME%.exe" >nul

REM Cleanup
rmdir /s /q "%TEMP_DIR%"

echo [INFO] Installed to %INSTALL_DIR%\%BINARY_NAME%.exe

REM Check PATH
echo %PATH% | findstr /c:"%INSTALL_DIR%" >nul
if errorlevel 1 (
    echo [WARN] %INSTALL_DIR% is not in PATH
    echo.
    echo Add the following to your PATH environment variable:
    echo     %INSTALL_DIR%
    echo.
    echo Or run this command (requires admin):
    echo     setx PATH "%%PATH%%;%INSTALL_DIR%"
)

echo.
echo [INFO] Installation successful!
"%INSTALL_DIR%\%BINARY_NAME%.exe" --version

echo.
echo Run 'nanobot onboard' to get started!

endlocal
```

---

## 四、dotnet tool 安装 (全局工具)

### 4.1 打包配置

**文件位置**: `src/NanoBot.Cli/NanoBot.Cli.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>nanobot</ToolCommandName>
    <PackageOutputPath>./nupkg</PackageOutputPath>
    <PackageId>NanoBot.Cli</PackageId>
    <PackageVersion>0.1.0</PackageVersion>
    <Authors>NanoBot</Authors>
    <Description>A lightweight personal AI assistant framework</Description>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://github.com/NanoBot/NanoBot.Net</PackageProjectUrl>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/NanoBot/NanoBot.Net.git</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
  </PropertyGroup>

  <ItemGroup>
    <None Include="..\..\README.md" Pack="true" PackagePath="\"/>
  </ItemGroup>

</Project>
```

### 4.2 用户安装命令

```bash
# 从 NuGet 安装
dotnet tool install --global NanoBot.Cli

# 验证安装
nanobot --version

# 卸载
dotnet tool uninstall --global NanoBot.Cli
```

---

## 五、发布配置

### 5.1 发布脚本

**文件位置**: `scripts/publish.sh`

```bash
#!/bin/bash
#
# Build and publish NanoBot.Net for all platforms
#

set -e

VERSION="${1:-0.1.0}"
PROJECT="src/NanoBot.Cli/NanoBot.Cli.csproj"
OUTPUT_DIR="dist"

PLATFORMS=(
    "osx-x64"
    "osx-arm64"
    "linux-x64"
    "linux-arm64"
    "win-x64"
    "win-arm64"
)

echo "Building NanoBot.Net v$VERSION..."

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

for PLATFORM in "${PLATFORMS[@]}"; do
    echo "Building for $PLATFORM..."
    
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$PLATFORM" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:PublishTrimmed=true \
        -p:Version="$VERSION" \
        -o "$OUTPUT_DIR/$PLATFORM"
    
    # Create archive
    cd "$OUTPUT_DIR"
    if [[ "$PLATFORM" == win-* ]]; then
        zip -r "nanobot-$PLATFORM.zip" "$PLATFORM"
    else
        tar -czvf "nanobot-$PLATFORM.tar.gz" "$PLATFORM"
    fi
    cd ..
done

echo "Build complete. Artifacts in $OUTPUT_DIR/"
ls -la "$OUTPUT_DIR"/*.{tar.gz,zip} 2>/dev/null || true
```

### 5.2 GitHub Actions 工作流

**文件位置**: `.github/workflows/release.yml`

```yaml
name: Release

on:
  push:
    tags:
      - 'v*'

env:
  DOTNET_VERSION: '8.0.x'

jobs:
  build:
    runs-on: ${{ matrix.os }}
    strategy:
      matrix:
        include:
          - os: ubuntu-latest
            rid: linux-x64
            archive: tar.gz
          - os: ubuntu-latest
            rid: linux-arm64
            archive: tar.gz
          - os: macos-latest
            rid: osx-x64
            archive: tar.gz
          - os: macos-latest
            rid: osx-arm64
            archive: tar.gz
          - os: windows-latest
            rid: win-x64
            archive: zip
          - os: windows-latest
            rid: win-arm64
            archive: zip

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Build
        run: |
          dotnet publish src/NanoBot.Cli/NanoBot.Cli.csproj \
            -c Release \
            -r ${{ matrix.rid }} \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:PublishTrimmed=true \
            -o dist

      - name: Create archive (Unix)
        if: matrix.archive == 'tar.gz'
        run: |
          cd dist
          tar -czvf nanobot-${{ matrix.rid }}.tar.gz *

      - name: Create archive (Windows)
        if: matrix.archive == 'zip'
        run: |
          Compress-Archive -Path dist/* -DestinationPath dist/nanobot-${{ matrix.rid }}.zip

      - name: Upload artifact
        uses: actions/upload-artifact@v4
        with:
          name: nanobot-${{ matrix.rid }}
          path: dist/nanobot-${{ matrix.rid }}.*

  release:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - uses: actions/checkout@v4

      - name: Download artifacts
        uses: actions/download-artifact@v4
        with:
          path: artifacts

      - name: Create Release
        uses: softprops/action-gh-release@v1
        with:
          files: artifacts/**/*
          generate_release_notes: true

  nuget:
    needs: build
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: ${{ env.DOTNET_VERSION }}

      - name: Pack
        run: |
          dotnet pack src/NanoBot.Cli/NanoBot.Cli.csproj \
            -c Release \
            -p:PackageVersion=${{ github.ref_name }} \
            -o nupkg

      - name: Push to NuGet
        run: dotnet nuget push nupkg/*.nupkg --source https://api.nuget.org/v3/index.json --api-key ${{ secrets.NUGET_API_KEY }}

      - name: Push to GitHub Packages
        run: dotnet nuget push nupkg/*.nupkg --source https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json --api-key ${{ secrets.GITHUB_TOKEN }}
```

---

## 六、安装验证

### 6.1 安装后验证流程

安装完成后，执行以下命令验证：

```bash
# 检查版本
nanobot --version

# 检查帮助
nanobot --help

# 初始化工作目录
nanobot onboard

# 检查状态
nanobot status
```

### 6.2 环境检查

```bash
# 检查 PATH 配置
which nanobot        # Unix
where nanobot        # Windows

# 检查执行权限 (Unix)
ls -l $(which nanobot)
```

---

## 七、卸载方案

### 7.1 Homebrew 卸载

```bash
brew uninstall nanobot
brew untap NanoBot/tap  # 可选，移除 Tap
```

### 7.2 Winget 卸载

```powershell
winget uninstall NanoBot.NanoBot
```

### 7.3 脚本安装卸载

**Unix**:
```bash
rm -f ~/.local/bin/nanobot
```

**Windows**:
```powershell
Remove-Item "$env:USERPROFILE\.local\bin\nanobot.exe"
```

### 7.4 dotnet tool 卸载

```bash
dotnet tool uninstall --global NanoBot.Cli
```

---

## 八、目录结构

安装相关文件目录结构：

```
NanoBot.Net/
├── scripts/
│   ├── install.sh          # Unix 安装脚本
│   ├── install.ps1         # Windows PowerShell 安装脚本
│   ├── install.cmd         # Windows CMD 安装脚本
│   ├── publish.sh          # 发布构建脚本
│   └── uninstall.sh        # 卸载脚本
│
├── homebrew-nanobot/       # Homebrew Tap 仓库
│   ├── Formula/
│   │   └── nanobot.rb
│   └── README.md
│
├── winget-pkgs/            # Winget manifest (提交到 microsoft/winget-pkgs)
│   └── manifests/
│       └── n/
│           └── NanoBot/
│               └── NanoBot/
│                   └── 0.1.0/
│                       ├── NanoBot.NanoBot.yaml
│                       ├── NanoBot.NanoBot.installer.yaml
│                       └── NanoBot.NanoBot.locale.en-US.yaml
│
└── .github/
    └── workflows/
        └── release.yml     # 发布工作流
```

---

## 九、实施计划

| 阶段 | 任务 | 优先级 |
|------|------|--------|
| 1 | 实现安装脚本 (install.sh, install.ps1, install.cmd) | 高 |
| 2 | 配置 GitHub Actions 发布流程 | 高 |
| 3 | 创建 Homebrew Tap 和 Formula | 中 |
| 4 | 提交 Winget manifest 到 microsoft/winget-pkgs | 中 |
| 5 | 发布到 NuGet (dotnet tool) | 中 |
| 6 | 文档更新 (README 安装说明) | 低 |

---

*返回 [索引文档](./Index.md)*
