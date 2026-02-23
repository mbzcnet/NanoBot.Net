# 测试方案设计

本文档定义 NanoBot.Net 的测试策略和测试用例设计，参考原 nanobot 项目的测试结构，基于 .NET 测试框架实现。

**测试框架**：xUnit + Moq + FluentAssertions

---

## 测试策略

### 测试层次

| 层次 | 测试类型 | 工具 | 覆盖范围 |
|------|----------|------|----------|
| **单元测试** | 接口实现测试 | xUnit + Moq | 各模块核心逻辑 |
| **集成测试** | 模块交互测试 | xUnit + TestHost | 模块间协作 |
| **端到端测试** | CLI 命令测试 | xUnit + Process | 完整工作流 |
| **Docker 测试** | 容器化测试 | Shell Script | 部署验证 |

### 测试命名规范

```
[Method]_[Scenario]_[ExpectedResult]
```

示例：
- `ProcessTurnAsync_WithValidRequest_ReturnsResponse`
- `ValidateParameters_WithMissingRequired_ReturnsErrors`

---

## 测试项目结构

```
tests/
├── NanoBot.Core.Tests/           # 核心层单元测试
│   ├── Agents/
│   │   ├── AgentTests.cs
│   │   └── AgentContextTests.cs
│   ├── Memory/
│   │   └── MemoryStoreTests.cs
│   └── Sessions/
│       └── SessionManagerTests.cs
│
├── NanoBot.Tools.Tests/          # 工具层单元测试
│   ├── ToolRegistryTests.cs
│   ├── ToolValidationTests.cs
│   ├── FileToolsTests.cs
│   ├── ShellToolTests.cs
│   └── WebToolsTests.cs
│
├── NanoBot.Providers.Tests/      # 提供商层单元测试
│   ├── ProviderRegistryTests.cs
│   └── OpenAIProviderTests.cs
│
├── NanoBot.Channels.Tests/       # 通道层单元测试
│   ├── ChannelManagerTests.cs
│   ├── TelegramChannelTests.cs
│   ├── DiscordChannelTests.cs
│   └── EmailChannelTests.cs
│
├── NanoBot.Infrastructure.Tests/ # 基础设施层单元测试
│   ├── MessageBusTests.cs
│   ├── CronServiceTests.cs
│   ├── HeartbeatServiceTests.cs
│   └── SkillsLoaderTests.cs
│
├── NanoBot.Cli.Tests/            # CLI 命令测试
│   ├── OnboardCommandTests.cs
│   ├── AgentCommandTests.cs
│   └── StatusCommandTests.cs
│
├── NanoBot.Integration.Tests/    # 集成测试
│   ├── AgentLoopIntegrationTests.cs
│   └── EndToEndTests.cs
│
└── scripts/
    └── test-docker.sh            # Docker 测试脚本
```

---

## 单元测试设计

### Agent 核心层测试

#### AgentTests.cs

```csharp
namespace NanoBot.Core.Tests.Agents;

public class AgentTests
{
    [Fact]
    public async Task ProcessTurnAsync_WithValidRequest_ReturnsResponse()
    {
    }

    [Fact]
    public async Task RunLoopAsync_WithToolCalls_ExecutesToolsAndContinues()
    {
    }

    [Fact]
    public async Task RunLoopAsync_ExceedsMaxIterations_StopsAndReturns()
    {
    }

    [Fact]
    public async Task ProcessTurnAsync_WithCancellationToken_CancelsGracefully()
    {
    }
}
```

#### AgentContextTests.cs

```csharp
namespace NanoBot.Core.Tests.Agents;

public class AgentContextTests
{
    [Fact]
    public async Task BuildSystemPromptAsync_IncludesMemoryAndSkills()
    {
    }

    [Fact]
    public async Task GetHistoryAsync_WithMaxMessages_ReturnsCorrectCount()
    {
    }

    [Fact]
    public async Task AppendMessageAsync_AddsToHistory()
    {
    }
}
```

#### SessionManagerTests.cs

参考原 `test_consolidate_offset.py` 设计：

```csharp
namespace NanoBot.Core.Tests.Sessions;

public class SessionManagerTests
{
    [Fact]
    public void GetOrCreate_WithNewKey_CreatesNewSession()
    {
    }

    [Fact]
    public void GetOrCreate_WithExistingKey_ReturnsCachedSession()
    {
    }

    [Fact]
    public void Save_PersistsSessionToJsonl()
    {
    }

    [Fact]
    public void Invalidate_ClearsCache()
    {
    }
}

public class SessionTests
{
    [Fact]
    public void Constructor_InitializesWithZeroLastConsolidated()
    {
        var session = new Session { Key = "test:initial" };
        Assert.Equal(0, session.LastConsolidated);
    }

    [Fact]
    public void LastConsolidated_PersistsAcrossSaveLoad()
    {
    }

    [Fact]
    public void Clear_ResetsLastConsolidatedToZero()
    {
    }

    [Fact]
    public void Messages_AppendOnly_NeverModifiesExisting()
    {
    }

    [Theory]
    [InlineData(50, 25, 25)]
    [InlineData(60, 25, 35)]
    [InlineData(100, 25, 75)]
    public void GetOldMessages_ReturnsCorrectRange(int total, int keepCount, int expectedOld)
    {
    }

    [Fact]
    public void GetHistory_ReturnsMostRecentMessages()
    {
    }

    [Fact]
    public void GetHistory_StableForSameMaxMessages()
    {
    }
}
```

---

### 工具层测试

#### ToolValidationTests.cs

参考原 `test_tool_validation.py` 设计：

```csharp
namespace NanoBot.Tools.Tests;

public class ToolValidationTests
{
    private class SampleTool : ITool
    {
        public string Name => "sample";
        public string Description => "sample tool";
        public JsonElement Parameters => JsonDocument.Parse(/* schema */).RootElement;

        public Task<ToolResult> ExecuteAsync(JsonElement args, IToolContext ctx, CancellationToken ct = default)
            => Task.FromResult(new ToolResult { Output = "ok" });
    }

    [Fact]
    public void ValidateParameters_WithMissingRequired_ReturnsErrors()
    {
    }

    [Fact]
    public void ValidateParameters_WithTypeMismatch_ReturnsErrors()
    {
    }

    [Fact]
    public void ValidateParameters_WithOutOfRange_ReturnsErrors()
    {
    }

    [Fact]
    public void ValidateParameters_WithInvalidEnum_ReturnsErrors()
    {
    }

    [Fact]
    public void ValidateParameters_WithNestedObject_ReturnsErrors()
    {
    }

    [Fact]
    public void ValidateParameters_WithValidParams_ReturnsSuccess()
    {
    }

    [Fact]
    public void ValidateParameters_IgnoresUnknownFields()
    {
    }
}
```

#### FileToolsTests.cs

```csharp
namespace NanoBot.Tools.Tests;

public class FileToolsTests
{
    [Fact]
    public async Task ReadFile_WithValidPath_ReturnsContent()
    {
    }

    [Fact]
    public async Task ReadFile_WithInvalidPath_ReturnsError()
    {
    }

    [Fact]
    public async Task ReadFile_WithLineRange_ReturnsPartialContent()
    {
    }

    [Fact]
    public async Task WriteFile_CreatesParentDirectories()
    {
    }

    [Fact]
    public async Task EditFile_ReplacesText()
    {
    }

    [Fact]
    public async Task EditFile_WithNotFoundText_ReturnsError()
    {
    }

    [Fact]
    public async Task ListDir_ReturnsDirectoryContents()
    {
    }

    [Fact]
    public async Task FileOperations_RespectAllowedDirs()
    {
    }
}
```

#### ShellToolTests.cs

```csharp
namespace NanoBot.Tools.Tests;

public class ShellToolTests
{
    [Fact]
    public async Task Exec_WithValidCommand_ReturnsOutput()
    {
    }

    [Fact]
    public async Task Exec_WithTimeout_TerminatesProcess()
    {
    }

    [Fact]
    public async Task Exec_WithDeniedPattern_ReturnsError()
    {
    }

    [Fact]
    public async Task Exec_RespectsWorkingDirectory()
    {
    }
}
```

---

### 通道层测试

#### EmailChannelTests.cs

参考原 `test_email_channel.py` 设计：

```csharp
namespace NanoBot.Channels.Tests;

public class EmailChannelTests
{
    [Fact]
    public async Task FetchNewMessages_ParsesUnseenAndMarksSeen()
    {
    }

    [Fact]
    public async Task FetchNewMessages_DeduplicatesByUid()
    {
    }

    [Fact]
    public void ExtractTextBody_FallsBackToHtml()
    {
    }

    [Fact]
    public async Task Start_WithoutConsent_ReturnsImmediately()
    {
    }

    [Fact]
    public async Task Send_UsesSmtpAndReplySubject()
    {
    }

    [Fact]
    public async Task Send_SkipsWhenAutoReplyDisabled()
    {
    }

    [Fact]
    public async Task Send_SkipsWhenConsentNotGranted()
    {
    }

    [Fact]
    public async Task FetchMessagesBetweenDates_UsesImapSearch()
    {
    }
}
```

#### TelegramChannelTests.cs

```csharp
namespace NanoBot.Channels.Tests;

public class TelegramChannelTests
{
    [Fact]
    public async Task Start_ConnectsToTelegramApi()
    {
    }

    [Fact]
    public async Task SendMessage_SendsToCorrectChat()
    {
    }

    [Fact]
    public async Task MessageReceived_RaisesEvent()
    {
    }

    [Fact]
    public async Task ConvertMarkdownToHtml_HandlesFormatting()
    {
    }
}
```

---

### 基础设施层测试

#### MessageBusTests.cs

```csharp
namespace NanoBot.Infrastructure.Tests;

public class MessageBusTests
{
    [Fact]
    public async Task PublishInbound_ConsumeInbound_ReturnsSameMessage()
    {
    }

    [Fact]
    public async Task SubscribeOutbound_ReceivesMessages()
    {
    }

    [Fact]
    public async Task Dispatcher_RoutesToCorrectChannel()
    {
    }

    [Fact]
    public void InboundSize_ReturnsCorrectCount()
    {
    }

    [Fact]
    public async Task Stop_PreventsNewMessages()
    {
    }
}
```

#### CronServiceTests.cs

```csharp
namespace NanoBot.Infrastructure.Tests;

public class CronServiceTests
{
    [Fact]
    public async Task AddJob_SchedulesCorrectly()
    {
    }

    [Fact]
    public async Task RemoveJob_CancelsScheduledJob()
    {
    }

    [Fact]
    public async Task EnableJob_TogglesJobState()
    {
    }

    [Fact]
    public async Task RunJobAsync_ExecutesImmediately()
    {
    }

    [Fact]
    public void ListJobs_ReturnsAllJobs()
    {
    }
}
```

---

### CLI 命令测试

#### OnboardCommandTests.cs

参考原 `test_commands.py` 设计：

```csharp
namespace NanoBot.Cli.Tests;

public class OnboardCommandTests
{
    [Fact]
    public async Task Onboard_FreshInstall_CreatesConfigAndWorkspace()
    {
    }

    [Fact]
    public async Task Onboard_ExistingConfig_RefreshesWhenDeclined()
    {
    }

    [Fact]
    public async Task Onboard_ExistingConfig_OverwritesWhenConfirmed()
    {
    }

    [Fact]
    public async Task Onboard_ExistingWorkspace_CreatesMissingTemplates()
    {
    }
}
```

#### StatusCommandTests.cs

```csharp
namespace NanoBot.Cli.Tests;

public class StatusCommandTests
{
    [Fact]
    public async Task Status_ReturnsAgentInfo()
    {
    }

    [Fact]
    public async Task Status_WithJsonFlag_ReturnsJsonOutput()
    {
    }

    [Fact]
    public async Task Status_ShowsChannelStates()
    {
    }
}
```

---

## 集成测试设计

### AgentLoopIntegrationTests.cs

```csharp
namespace NanoBot.Integration.Tests;

public class AgentLoopIntegrationTests : IAsyncLifetime
{
    private IServiceProvider _services;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddNanoBotCore();
        services.AddMockProviders();
        services.AddMockChannels();
        _services = services.BuildServiceProvider();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AgentLoop_ProcessesMessage_EndToEnd()
    {
    }

    [Fact]
    public async Task AgentLoop_WithToolCall_ExecutesAndResponds()
    {
    }

    [Fact]
    public async Task AgentLoop_PersistsSession_AcrossRequests()
    {
    }
}
```

---

## Docker 测试脚本

参考原 `test_docker.sh` 设计：

```bash
#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.." || exit 1

IMAGE_NAME="nanobot-net-test"

echo "=== Building Docker image ==="
docker build -t "$IMAGE_NAME" .

echo ""
echo "=== Running 'nanobot onboard' ==="
docker run --name nanobot-test-run "$IMAGE_NAME" onboard

echo ""
echo "=== Running 'nanobot status' ==="
STATUS_OUTPUT=$(docker commit nanobot-test-run nanobot-net-onboarded > /dev/null && \
    docker run --rm nanobot-net-onboarded status 2>&1) || true

echo "$STATUS_OUTPUT"

echo ""
echo "=== Validating output ==="
PASS=true

check() {
    if echo "$STATUS_OUTPUT" | grep -q "$1"; then
        echo "  PASS: found '$1'"
    else
        echo "  FAIL: missing '$1'"
        PASS=false
    fi
}

check "NanoBot Status"
check "Config:"
check "Workspace:"
check "Model:"
check "Provider:"

echo ""
if $PASS; then
    echo "=== All checks passed ==="
else
    echo "=== Some checks FAILED ==="
    exit 1
fi

echo ""
echo "=== Cleanup ==="
docker rm -f nanobot-test-run 2>/dev/null || true
docker rmi -f nanobot-net-onboarded 2>/dev/null || true
docker rmi -f "$IMAGE_NAME" 2>/dev/null || true
echo "Done."
```

---

## 测试配置

### xunit.json

```json
{
  "parallelizeAssembly": true,
  "parallelizeTestCollections": true,
  "maxParallelThreads": 4
}
```

### Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit" Version="2.6.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.0" />
    <PackageReference Include="Moq" Version="4.20.0" />
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
  </ItemGroup>
</Project>
```

---

## 测试覆盖率目标

| 模块 | 目标覆盖率 | 说明 |
|------|-----------|------|
| Agent 核心层 | 80% | 核心逻辑必须充分测试 |
| 工具层 | 85% | 参数验证和边界条件 |
| 提供商层 | 75% | API 调用模拟 |
| 通道层 | 70% | 依赖外部服务，重点测试接口 |
| 基础设施层 | 80% | 消息路由和任务调度 |
| CLI 命令层 | 75% | 命令解析和执行 |

---

## 测试运行命令

```bash
# 运行所有测试
dotnet test

# 运行特定项目测试
dotnet test tests/NanoBot.Core.Tests

# 运行并生成覆盖率报告
dotnet test --collect:"XPlat Code Coverage"

# 运行 Docker 测试
./tests/scripts/test-docker.sh
```

---

## Mock 策略

### ILLMProvider Mock

```csharp
var mockProvider = new Mock<ILLMProvider>();
mockProvider.Setup(p => p.CompleteAsync(It.IsAny<LLMRequest>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(new LLMResponse { Content = "Test response" });
```

### IChannel Mock

```csharp
var mockChannel = new Mock<IChannel>();
mockChannel.Setup(c => c.IsConnected).Returns(true);
mockChannel.Setup(c => c.SendMessageAsync(It.IsAny<OutboundMessage>(), It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

### IMessageBus Mock

```csharp
var mockBus = new Mock<IMessageBus>();
var inboundChannel = Channel<InboundMessage>.CreateUnbounded();
mockBus.Setup(b => b.ConsumeInboundAsync(It.IsAny<CancellationToken>()))
    .Returns((CancellationToken ct) => inboundChannel.Reader.ReadAsync(ct).AsValueTask());
```

---

*返回 [概览文档](./NanoBot.Net-Overview.md)*
