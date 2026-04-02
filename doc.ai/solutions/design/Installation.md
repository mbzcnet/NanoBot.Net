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
  homepage "https://github.com/mbzcnet/NanoBot.Net"
  version "0.1.0"
  license "MIT"

  on_macos do
    on_intel do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nanobot-osx-x64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
    on_arm do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nanobot-osx-arm64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
  end

  on_linux do
    on_intel do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nanobot-linux-x64.tar.gz"
      sha256 "TODO: 计算实际值"
    end
    on_arm do
      url "https://github.com/mbzcnet/NanoBot.Net/releases/download/v#{version}/nanobot-linux-arm64.tar.gz"
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
brew tap mbzcnet/tap

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
PublisherUrl: https://github.com/mbzcnet
PublisherSupportUrl: https://github.com/mbzcnet/NanoBot.Net/issues
PackageName: NanoBot
PackageUrl: https://github.com/mbzcnet/NanoBot.Net
License: MIT
LicenseUrl: https://github.com/mbzcnet/NanoBot.Net/blob/main/LICENSE
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
ReleaseNotesUrl: https://github.com/mbzcnet/NanoBot.Net/releases/tag/v0.1.0
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
    InstallerUrl: https://github.com/mbzcnet/NanoBot.Net/releases/download/v0.1.0/nanobot-win-x64.zip
    InstallerSha256: TODO: 计算实际值
    NestedInstallerType: portable
    NestedInstallerFiles:
      - RelativeFilePath: nanobot.exe
        PortableCommandAlias: nanobot
  - Architecture: arm64
    InstallerUrl: https://github.com/mbzcnet/NanoBot.Net/releases/download/v0.1.0/nanobot-win-arm64.zip
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
PackageUrl: https://github.com/mbzcnet/NanoBot.Net
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

**文件位置**: `install/install.sh`（已实现）

一行命令安装：

```bash
curl -sSL https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.sh | bash
```

**功能**：

- 自动检测 OS + CPU 架构，下载对应平台的 `nbot-<rid>.tar.gz`
- SHA256 校验（自动下载 `.sha256` 文件）
- 安装到 `~/.local/bin`（可通过 `--dir` 覆盖）
- 自动追加 PATH 配置到 `~/.bashrc` / `~/.zshrc`
- 安装后自动运行 `nbot configure` 配置向导
- 支持 `--version X.Y.Z` / `--skip-config` / `--no-verify` 等选项

> 详见仓库中的 `install/install.sh`。

### 3.2 Windows PowerShell 安装脚本

**文件位置**: `install/install.ps1`（已实现）

一行命令安装：

```powershell
irm https://raw.githubusercontent.com/mbzcnet/NanoBot.Net/main/install/install.ps1 | iex
```

**功能**：

- 自动检测 `win-x64` / `win-arm64`
- 下载并解压 `nbot-<rid>.zip`
- SHA256 校验
- 安装到 `%LOCALAPPDATA%\nanobot\bin`（可通过 `-InstallDir` 覆盖）
- 自动追加到用户 PATH（修改注册表，对当前会话立即生效）
- 安装后自动运行 `nbot configure` 配置向导
- 支持 `-Version X.Y.Z` / `-SkipConfig` / `-NoVerify` 等参数

> 详见仓库中的 `install/install.ps1`。

### 3.3 Windows CMD 批处理脚本 (可选)

**文件位置**: `install/install.cmd` (未实现，可使用 PowerShell 脚本替代)

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
    <PackageProjectUrl>https://github.com/mbzcnet/NanoBot.Net</PackageProjectUrl>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <RepositoryUrl>https://github.com/mbzcnet/NanoBot.Net.git</RepositoryUrl>
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

**文件位置**: `scripts/publish.sh`（已实现）

完整脚本请参考仓库中的 `scripts/publish.sh`，主要功能：

- **版本更新**：自动修改 `Directory.Build.props` 中的所有版本号
- **多平台构建**：支持 6 个平台组合 (`osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64`)
- **NativeAOT 支持**：通过 `--aot` 标志优先使用 NativeAOT 编译，失败时自动 fallback 到 self-contained
- **WebUI 打包**：同时构建并打包 NanoBot.WebUI
- **归档**：Unix 平台输出 `.tar.gz`，Windows 平台输出 `.zip`
- **Homebrew Formula**：自动下载 SHA256 并更新 Formula
- **NuGet**：打包并推送到 nuget.org
- **Git Tag**：创建并推送 `v*.*.*` 标签，触发 GitHub Actions CI

**使用示例**：

```bash
./scripts/publish.sh 0.1.4              # 仅构建 6 平台 self-contained
./scripts/publish.sh 0.1.4 --aot        # 优先 NativeAOT
./scripts/publish.sh 0.1.4 --tag         # 构建 + 推送 tag
./scripts/publish.sh 0.1.4 --nuget       # 构建 + 推送 NuGet
./scripts/publish.sh 0.1.4 --all         # 完整发布流程
NUGET_API_KEY=xxx ./scripts/publish.sh 0.1.4 --nuget
```

> **注意**：脚本会修改 `Directory.Build.props` 中的版本号，请确保在干净的工作目录下运行。

### 5.2 GitHub Actions 工作流

**文件位置**: `.github/workflows/release.yml`（已实现）

CI/CD 由标签触发（`v*`），包含三个并行 Job：

1. **`build`** — 在 6 个平台上并行构建，构建 CLI + WebUI，输出归档文件（`nbot-<rid>.tar.gz` / `.zip`）
2. **`release`** — 合并所有平台归档，通过 `softprops/action-gh-release` 发布到 GitHub Releases
3. **`nuget`** — 打包并推送到 nuget.org + GitHub Packages

**环境变量**：

| 变量 | 值 | 说明 |
|------|----|------|
| `DOTNET_VERSION` | `10.0.x` | .NET SDK 版本 |
| `BINARY_NAME` | `nbot` | 产物文件名 |

> 详见仓库中的 `.github/workflows/release.yml`。

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
brew untap mbzcnet/tap  # 可选，移除 Tap
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
├── install/
│   ├── install.sh          # Unix 安装脚本
│   ├── install.ps1         # Windows PowerShell 安装脚本
│   ├── publish.sh          # 发布构建脚本
│   └── homebrew-nanobot/   # Homebrew Tap 仓库
│       ├── Formula/
│       │   └── nanobot.rb
│       └── README.md
│
└── .github/
    └── workflows/
        └── release.yml     # 发布工作流
```

---

## 九、NativeAOT 多平台发布与 fallback 策略

### 9.1 AOT/Trim 警告梳理与影响

| 模块 | 告警类型 | 影响 | 处置策略 |
|------|----------|------|-----------|
| `NanoBot.Core/Configuration/Extensions/ConfigurationLoader.cs` | IL2026 / IL3050 (`JsonSerializer.Serialize/Deserialize`) | 依赖反射生成多态配置，NativeAOT 可能裁剪模型导致配置读取失败，Trimming 也会发出警告。 | 引入 `System.Text.Json` Source Generator（定义 `JsonSerializerContext`），或者在 `TrimmerRootDescriptor` 中保留配置模型。 |
| `NanoBot.Agent/SessionManager.cs` | IL2026 / IL3050 / IL3051 (`JsonSerializer`、`JsonNode`) | 会话缓存序列化、记忆快照使用 `JsonNode`，需要动态代码。 | 优先改为强类型模型 + Source Generator；短期可通过 `DynamicDependency`/`DynamicallyAccessedMembers` 保留类型。 |
| `NanoBot.Channels/*Channel.cs` (Discord/Slack/WhatsApp/Feishu) | IL2026 / IL3050 | 各通道编解码 payload 时需要完整模型。 | 按通道定义 `JsonSerializable` 类型列表，或为不常用通道在构建时禁用，避免 AOT 失败。 |
| `NanoBot.Infrastructure` / `NanoBot.Tools` | IL2104 / IL3053 | 表示当前装配仍有 Trim/AOT 警告未收敛。 | 在收敛计划中逐项消除；若某依赖无法兼容，触发 fallback。 |
| `Microsoft.Playwright` (测试依赖) | IL3000 / IL3002 | 单文件场景 `Assembly.Location` 永远为空，AOT 运行时可能抛异常。CLI 正常运行无需 Playwright，建议将其限定在测试项目中。 | 将 Playwright 限制为 `tests/*` 或条件引用，不参与 CLI 发布即可规避。 |

> 结论：macOS arm64 AOT 构建已经跑通（`dotnet publish ... -r osx-arm64 -p:PublishAot=true`，生成于 `dist-aot/osx-arm64`），但需按上表逐步收敛 JSON 相关警告，并在生产构建中开启 `WarningsAsErrors` 防止回归。

### 9.2 平台发布命令（优先 NativeAOT）

| 平台 | 命令示例 | 备注 |
|------|----------|------|
| macOS arm64 | `dotnet publish src/NanoBot.Cli/NanoBot.Cli.csproj -c Release -r osx-arm64 -p:PublishAot=true -p:InvariantGlobalization=true -p:StripSymbols=true -o dist-aot/osx-arm64` | 已验证可构建。Archive：`tar -C dist-aot -czvf nbot-osx-arm64-aot.tar.gz osx-arm64`. |
| macOS x64 | 同上但 `-r osx-x64`，需在 macOS x64 Runner 上执行 | Apple Silicon 不能交叉编译 x64 AOT，CI 需配置 Intel runner。 |
| Linux x64 | `dotnet publish ... -r linux-x64 -p:PublishAot=true ... -o dist-aot/linux-x64` （在 Linux 主机/runner） | Archive：`tar -C dist-aot -czvf nbot-linux-x64-aot.tar.gz linux-x64`. |
| Linux arm64 | 同上 `-r linux-arm64`（推荐使用 arm64 容器 runner） | 可在 GitHub Actions arm64 self-host runner 执行。 |
| Windows x64 | `dotnet publish ... -r win-x64 -p:PublishAot=true ... -o dist-aot/win-x64` （在 Windows runner） | Archive：`Compress-Archive -Path dist-aot/win-x64/* -DestinationPath nbot-win-x64-aot.zip`. |
| Windows arm64 | `-r win-arm64`（Windows arm64 runner） | 同 x64 打包逻辑。 |

**校验方式**：

```bash
# Unix 平台
shasum -a 256 nbot-<rid>-aot.tar.gz > nbot-<rid>-aot.tar.gz.sha256

# Windows 平台（PowerShell）
Get-FileHash .\nbot-win-<arch>-aot.zip -Algorithm SHA256 | Out-File nbot-win-<arch>-aot.zip.sha256
```

所有校验文件与压缩包一同上传 Release，并写入 Homebrew/Winget manifest。

### 9.3 安装体验（单包 + 配置向导）

1. 用户下载对应平台压缩包或运行“一行脚本”（`curl ... | bash` / `irm ... | iex`）。
2. 安装脚本自动检测 `osx-x64/osx-arm64/linux-x64/linux-arm64/win-x64/win-arm64`，下载匹配包，并将 `nbot`/`nbot.exe` 放入 `~/.local/bin` 或 `%USERPROFILE%\.local\bin`。
3. 解压后首次运行 `./nbot configure`（或 `nbot onboard`）进入配置向导，完成 API Key、工作区路径等设置，向导依赖 CLI 本身，无需额外 UI。
4. 高阶用户可通过 Homebrew/Winget 获取同样的 NativeAOT/Self-contained 产物；脚本仅作为快捷入口。

### 9.4 NativeAOT → Self-contained fallback

当出现以下任一条件时，切换到 Self-contained（SingleFile + Trim）产物：

1. 关键依赖（如某个 Channel 或 Provider）仍需 `System.Reflection.Emit`、`JsonSerializer` 动态生成且短期无法改造。
2. CI 环境缺少目标 RID 的 AOT Toolchain（例如无法获得 macOS Intel runner）。
3. 第三方库（如 Playwright）必须保留在 CLI 中且与 `PublishAot` 冲突。
4. 需要开启调试符号或降低构建时间的临时版本。

Fallback 命令模板：

```bash
dotnet publish src/NanoBot.Cli/NanoBot.Cli.csproj \
  -c Release \
  -r <rid> \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:PublishTrimmed=true \
  -p:InvariantGlobalization=true \
  -o dist-sc/<rid>
```

输出命名建议 `nbot-<rid>-sc.tar.gz` / `.zip`，与 AOT 产物并列，供 Homebrew/Winget/脚本按优先级选择：优先下载 AOT，若检测失败则退回 Self-contained。

---

## 十、实施计划

| 阶段 | 任务 | 状态 | 说明 |
|------|------|------|------|
| 1 | 实现安装脚本 (`install.sh`, `install.ps1`) | ✅ 已完成 | `install/` 目录下已实现。`install.cmd` 可选（PowerShell 脚本已覆盖 Windows）。 |
| 2 | 配置 GitHub Actions 发布流程 | ✅ 已完成 | `.github/workflows/release.yml` 已实现 6 平台 + release + nuget job。升级到 .NET 10。 |
| 3 | 创建 Homebrew Formula | ✅ 已完成 | `install/homebrew-nanobot/Formula/nbot.rb` 已创建。需创建 Tap 仓库（`mbzcnet/homebrew-tap`）并推送 formula。 |
| 4 | 提交 Winget manifest | ⏳ 待处理 | 需创建 manifest 文件并提交到 `microsoft/winget-pkgs`。 |
| 5 | 发布到 NuGet (dotnet tool) | ✅ 已完成 | `dotnet pack` + `dotnet nuget push` 已集成到 CI 和 `publish.sh`。 |
| 6 | 文档更新 (README 安装说明) | ⏳ 待处理 | README 需添加一行安装命令。 |
| 7 | NativeAOT 正式启用 | ⏳ 待处理 | 需收敛 JSON AOT 警告后，`publish.sh --aot` 可正式启用。 |

### 下一步：创建 Homebrew Tap

Homebrew Tap 仓库需独立创建（不能与主仓库同一目录）：

```bash
# 1. 在 GitHub 创建 mbzcnet/homebrew-tap 仓库
# 2. Clone 并推送初始 formula
git clone https://github.com/mbzcnet/homebrew-tap.git
cp NanoBot.Net/install/homebrew-nanobot/Formula/nbot.rb homebrew-tap/Formula/
cd homebrew-tap
git add Formula/nbot.rb
git commit -m "Add nbot formula"
git push origin main
# 3. 之后 release 时由 publish.sh 自动更新 sha256 并推送
```

---

*返回 [索引文档](./Index.md)*
