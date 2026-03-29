# NanoBot.Agent.Tests

Integration and unit tests for NanoBot.Agent.

## Running Tests

### All Tests

```bash
dotnet test tests/NanoBot.Agent.Tests
```

### Unit Tests Only

```bash
dotnet test tests/NanoBot.Agent.Tests --filter "FullyQualifiedName!~Integration"
```

### Integration Tests Only

```bash
dotnet test tests/NanoBot.Agent.Tests --filter "FullyQualifiedName~Integration"
```

## Integration Test Configuration

All integration tests run unconditionally. If required services (like Ollama) are not available, tests will fail with connection errors.

### Ollama Integration Tests

The `OllamaQwenIntegrationTests` require a running Ollama instance. By default, they connect to:

- **API Base**: `http://172.16.3.220:11435/v1`
- **Model**: `qwen3.5:4b`

Override via environment variables:

```bash
export OLLAMA_API_BASE=http://localhost:11435/v1
export OLLAMA_MODEL=qwen3.5:4b
dotnet test tests/NanoBot.Agent.Tests --filter "FullyQualifiedName~Ollama"
```

### Real Model Integration Tests

The `RealModelIntegrationTests` require API keys for the target provider. Set environment variables:

```bash
export REAL_MODEL_PROVIDER=openai
export REAL_MODEL_NAME=gpt-4o-mini
export OPENAI_API_KEY=sk-...
dotnet test tests/NanoBot.Agent.Tests --filter "FullyQualifiedName~RealModel"
```

### Vision Recognition Tests

The `VisionRecognitionTests` require a vision-capable model. Set environment variables:

```bash
export OPENAI_API_KEY=sk-...
dotnet test tests/NanoBot.Agent.Tests --filter "FullyQualifiedName~Vision"
```

### Configured Agent Tests

The `ConfiguredAgentIntegrationTests` load configuration from:

1. Environment variable `NBOT_CONFIG_PATH` (if set)
2. `~/.nbot/config.json` (default)
3. `config.json` in current directory

Override any configuration via environment variables:

```bash
export NBOT_CONFIG_PATH=/path/to/config.json
export NBOT_LLM_PROVIDER=ollama
export NBOT_LLM_MODEL=qwen3.5:4b
export NBOT_LLM_API_BASE=http://localhost:11435/v1
export NBOT_LLM_API_KEY=ollama
dotnet test tests/NanoBot.Agent.Tests --filter "FullyQualifiedName~Configured"
```

## Environment Variables Summary

| Variable | Description | Default |
|----------|-------------|---------|
| `OLLAMA_API_BASE` | Ollama API base URL | `http://172.16.3.220:11435/v1` |
| `OLLAMA_MODEL` | Ollama model name | `qwen3.5:4b` |
| `OLLAMA_API_KEY` | Ollama API key (usually "ollama") | `ollama` |
| `REAL_MODEL_PROVIDER` | Provider for RealModelIntegrationTests | `openai` |
| `REAL_MODEL_NAME` | Model name for RealModelIntegrationTests | `gpt-4o-mini` |
| `{PROVIDER}_API_KEY` | API key for specific provider | Required if using |
| `{PROVIDER}_API_BASE` | API base for specific provider | Optional |
| `NBOT_CONFIG_PATH` | Path to config file | `~/.nbot/config.json` |
| `NBOT_LLM_PROVIDER` | Override LLM provider | From config |
| `NBOT_LLM_MODEL` | Override model | From config |
| `NBOT_LLM_API_BASE` | Override API base URL | From config |
| `NBOT_LLM_API_KEY` | Override API key | From config |

## Test Structure

```
tests/NanoBot.Agent.Tests/
├── Integration/
│   ├── AgentRuntimeDiagnosticTests.cs      # AgentRuntime with Ollama
│   ├── NanoBotAgentDiagnosticTests.cs     # NanoBotAgent with Ollama
│   ├── OllamaQwenIntegrationTests.cs      # Qwen model tests
│   ├── ConfiguredAgentIntegrationTests.cs # Tests with local config
│   ├── RealModelIntegrationTests.cs        # Generic model tests
│   ├── VisionRecognitionTests.cs          # Vision model tests
│   ├── ToolCallingIntegrationTests.cs     # Tool calling tests
│   ├── MessageHistoryIntegrationTests.cs  # History management
│   └── AgentBrowserSnapshotIntegrationTests.cs
├── Context/
│   ├── ChatHistoryProviderTests.cs         # Session history
│   └── ProviderTests.cs                   # Context providers
├── Tools/
│   └── SpawnToolTests.cs                  # Subagent spawning
├── AgentRuntimeTests.cs                   # Runtime unit tests
├── NanoBotAgentFactoryTests.cs           # Factory unit tests
├── SessionManagerTests.cs                # Session management
├── SessionLifecycleTests.cs              # Session lifecycle
├── SessionMessageTests.cs                 # Message persistence
├── JsonSerializationTests.cs              # JSON serialization
└── ToolHintFormatterTests.cs             # Tool formatting
```

## Ollama Setup

### macOS

```bash
brew install ollama
ollama serve
ollama pull qwen3.5:4b
```

### Linux

```bash
curl -fsSL https://ollama.com/install.sh | sh
ollama serve
ollama pull qwen3.5:4b
```

### Docker

```bash
docker run -d -p 11435:11434 ollama/ollama
docker exec ollama ollama pull qwen3.5:4b
```

## Writing New Tests

### Unit Tests

Use Moq for mocking dependencies:

```csharp
var chatClientMock = new Mock<IChatClient>();
var metadata = new ChatClientMetadata("test");
chatClientMock.Setup(c => c.GetService(typeof(ChatClientMetadata), null))
    .Returns(metadata);
```

### Integration Tests

For real LLM testing, inherit from `IDisposable` and clean up resources:

```csharp
public class MyIntegrationTests : IDisposable
{
    private readonly string _testDirectory;

    public MyIntegrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"nanobot_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
    }
}
```

### Important: No Skip Logic

All tests must run unconditionally. Do NOT add:
- `[Fact(Skip = "...")]` attributes
- `throw new SkipTestException(...)` 
- Conditional early returns based on configuration

If a test requires external services (like an LLM API), it should:
1. Throw `InvalidOperationException` if required configuration is missing (constructor/factory)
2. Let the test fail naturally if the service is unavailable during execution
