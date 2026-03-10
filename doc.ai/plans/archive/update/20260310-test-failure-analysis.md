# Test Failure Analysis and Solutions

**Date**: 2026-03-10
**Summary**: Analysis of 16 test failures across the NanoBot.Net test suite with recommended solutions.

---

## Test Results Overview

| Test Project | Total | Passed | Failed | Skipped |
|--------------|-------|--------|--------|---------|
| NanoBot.Core.Tests | 50 | 48 | 2 | 0 |
| NanoBot.Providers.Tests | 56 | 55 | 1 | 0 |
| NanoBot.Channels.Tests | 40 | 40 | 0 | 0 |
| NanoBot.Agent.Tests | 89 | 77 | 9 | 3 |
| NanoBot.Tools.Tests | 65 | 64 | 1 | 0 |
| NanoBot.Infrastructure.Tests | 145 | 144 | 1 | 0 |
| NanoBot.Cli.Tests | 23 | 21 | 2 | 0 |
| **Total** | **468** | **449** | **16** | **3** |

---

## Category 1: Configuration Default Value Tests (2 failures)

### Failed Tests
- `NanoBot.Core.Tests.Configuration.ConfigurationTests.LlmConfig_ShouldHaveDefaultValues`
- `NanoBot.Core.Tests.Configuration.ConfigurationTests.LlmProfile_ShouldHaveDefaultValues`

### Root Cause
The `LlmProfile.Name` property and `LlmConfig.DefaultProfile` property were changed to use `string.Empty` or `null` as defaults instead of `"default"`.

**Source Code** (`src/NanoBot.Core/Configuration/Models/LlmConfig.cs`):
```csharp
public class LlmProfile
{
    public string Name { get; set; } = string.Empty;  // Should be "default"
    // ...
}

public class LlmConfig
{
    public string? DefaultProfile { get; set; }  // Should be "default"
}
```

**Test Expectations** (`tests/NanoBot.Core.Tests/Configuration/ConfigurationTests.cs`):
```csharp
profile.Name.Should().Be("default");  // Line 146
llm.DefaultProfile.Should().Be("default");  // Line 134
```

### Solution Options

**Option A: Fix the Source Code** (Recommended)
Update the default values in `LlmConfig.cs` to match test expectations:

```csharp
public class LlmProfile
{
    public string Name { get; set; } = "default";
    // ...
}

public class LlmConfig
{
    public string? DefaultProfile { get; set; } = "default";
}
```

**Option B: Update the Tests**
If the empty/null defaults are intentional design changes, update the test expectations to match the new behavior.

---

## Category 2: Tool Hint Formatter Tests (9 failures)

### Failed Tests
All tests in `NanoBot.Agent.Tests.ToolHintFormatterTests`:
- `FormatToolHint_SingleToolWithShortArgument_ReturnsFormattedHint`
- `FormatToolHint_SingleToolWithLongArgument_TruncatesArgument`
- `FormatToolHint_MultipleTools_ReturnsCommaSeparated`
- `FormatToolHint_ToolWithNoArguments_ReturnsToolNameOnly`
- `FormatToolHint_ToolWithNonStringArgument_ReturnsToolNameOnly`
- `FormatToolHint_ToolWithEmptyStringArgument_ReturnsToolNameOnly`
- `FormatToolHint_EmptyList_ReturnsEmptyString`
- `FormatToolHint_ExactlyFortyCharacters_NoTruncation`
- `FormatToolHint_FortyOneCharacters_Truncates`

### Root Cause
The `ToolHintFormatter.FormatToolHint` method was enhanced to add a tool emoji (🔧) and newlines for UI display, but the tests weren't updated to match the new output format.

**Source Code** (`src/NanoBot.Agent/ToolHintFormatter.cs`):
```csharp
public static string FormatToolHint(IEnumerable<FunctionCallContent> toolCalls)
{
    var hints = toolCalls.Select(FormatSingleToolCall);
    return $"\n🔧 {string.Join(", ", hints)}\n";  // Now includes emoji and newlines
}
```

**Test Expectations** (example):
```csharp
Assert.Equal("web_search(\"test query\")", result);  // Expected plain text
// Actual: "\n🔧 web_search(\"test query\")\n"
```

### Solution Options

**Option A: Fix the Tests** (Recommended)
Update test expectations to match the enhanced formatter output:

```csharp
// Example update:
Assert.Equal("\n🔧 web_search(\"test query\")\n", result);
Assert.Equal("\n🔧 search\n", result);  // for empty argument case
Assert.Equal("\n🔧 \n", result);  // for empty list case (may need special handling)
```

**Option B: Add Parameter to Control Formatting**
Add an optional parameter to control whether to include formatting:

```csharp
public static string FormatToolHint(IEnumerable<FunctionCallContent> toolCalls, bool includeFormatting = true)
{
    var hints = toolCalls.Select(FormatSingleToolCall);
    var hint = string.Join(", ", hints);
    return includeFormatting ? $"\n🔧 {hint}\n" : hint;
}
```

Then update tests to call `FormatToolHint(toolCalls, false)`.

---

## Category 3: Message Sanitizer Null Handling (1 failure)

### Failed Test
- `NanoBot.Providers.Tests.MessageSanitizerTests.SanitizeMessages_Null_ReturnsNull`

### Root Cause
The `SanitizeMessages` method returns an empty list instead of null when given null input.

**Source Code** (`src/NanoBot.Providers/SanitizingChatClient.cs` lines 99-102):
```csharp
public static IList<ChatMessage> SanitizeMessages(IList<ChatMessage> messages)
{
    if (messages == null || messages.Count == 0)
        return new List<ChatMessage>();  // Returns empty list, not null
    // ...
}
```

**Test Expectation**:
```csharp
var result = MessageSanitizer.SanitizeMessages(null!);
Assert.Null(result);  // Expects null
```

### Solution Options

**Option A: Fix the Source Code**
Change the null handling to return null:

```csharp
public static IList<ChatMessage> SanitizeMessages(IList<ChatMessage>? messages)
{
    if (messages == null)
        return null!;  // or change return type to allow null
    if (messages.Count == 0)
        return new List<ChatMessage>();
    // ...
}
```

**Option B: Fix the Test** (Recommended)
The current behavior (returning empty list) is safer. Update the test:

```csharp
[Fact]
public void SanitizeMessages_Null_ReturnsEmptyList()
{
    var result = MessageSanitizer.SanitizeMessages(null!);
    Assert.Empty(result);  // Expect empty list instead of null
}
```

---

## Category 4: Subagent Manager - GetSubagent Returns Null (1 failure)

### Failed Test
- `NanoBot.Infrastructure.Tests.Subagents.SubagentManagerTests.GetSubagent_ReturnsInfoForExistingId`

### Root Cause
The `SubagentManager.GetSubagent` method returns null because the subagent is removed from `_subagents` dictionary in the `finally` block before the test can retrieve it.

**Source Code** (`src/NanoBot.Infrastructure/Subagents/SubagentManager.cs` lines 155-178):
```csharp
finally
{
    lock (_lock)
    {
        _cancellationTokens.Remove(id);
        _subagents.Remove(id);  // Subagent is removed immediately
        // ...
    }
}
```

**Test Code** (`tests/NanoBot.Infrastructure.Tests/Subagents/SubagentManagerTests.cs` lines 246-258):
```csharp
var result = await manager.SpawnAsync("Test task", null, "telegram", "chat123");
var info = manager.GetSubagent(result.Id);  // Returns null because already removed
Assert.NotNull(info);  // Fails here
```

### Solution Options

**Option A: Keep Completed Subagents** (Recommended)
Modify the manager to retain completed subagents for a period of time:

```csharp
// Add field to track completed subagents
private readonly Dictionary<string, SubagentInfo> _completedSubagents = new();
private readonly TimeSpan _retentionTime = TimeSpan.FromMinutes(5);

// In finally block, move to completed instead of removing:
finally
{
    lock (_lock)
    {
        _cancellationTokens.Remove(id);
        if (_subagents.TryGetValue(id, out var info))
        {
            _subagents.Remove(id);
            _completedSubagents[id] = info;  // Keep for retrieval
        }
        // ...
    }
}

// In GetSubagent, check both dictionaries:
public SubagentInfo? GetSubagent(string id)
{
    lock (_lock)
    {
        if (_subagents.TryGetValue(id, out var info))
            return info;
        if (_completedSubagents.TryGetValue(id, out var completedInfo))
            return completedInfo;
        return null;
    }
}
```

**Option B: Don't Remove Subagents on Completion**
Simply remove the `_subagents.Remove(id)` line from the finally block. Subagents will be retained indefinitely.

**Option C: Update Test to Check During Execution**
Modify the test to check for the subagent while it's still running (using a slow mock).

---

## Category 5: Service Registration Tests (2 failures)

### Failed Tests
- `NanoBot.Cli.Tests.ServiceCollectionTests.AddNanoBot_ShouldRegisterAllServices`
- `NanoBot.Cli.Tests.ServiceCollectionTests.AddNanoBotCli_ShouldBuildConfigurationAndRegisterServices`

### Root Cause
The `AddNanoBotCli()` method doesn't properly register `IConfiguration`, or there's a DI registration issue.

**Test Code** (`tests/NanoBot.Cli.Tests/ServiceCollectionTests.cs` lines 164-175):
```csharp
[Fact]
public void AddNanoBotCli_ShouldBuildConfigurationAndRegisterServices()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddNanoBotCli();  // Extension method

    var serviceProvider = services.BuildServiceProvider();
    Assert.NotNull(serviceProvider.GetService<IConfiguration>());  // Returns null
}
```

### Solution Options

**Option A: Investigate ServiceCollectionExtensions**
Check `src/NanoBot.Cli/Extensions/ServiceCollectionExtensions.cs` (or similar) to ensure `IConfiguration` is properly registered:

```csharp
public static IServiceCollection AddNanoBotCli(this IServiceCollection services)
{
    // Ensure configuration is registered
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    services.AddSingleton<IConfiguration>(configuration);
    // ... rest of registrations
    return services;
}
```

**Option B: Fix Test Setup**
The test may need to set up configuration before calling `AddNanoBotCli()`.

---

## Category 6: Playwright Browser Not Installed (1 failure)

### Failed Test
- `NanoBot.Tools.Tests.BrowserToolsTests.BrowserService_StartOpenContentStop_UsesRealPlaywright`

### Root Cause
Playwright browsers are not installed on the test machine.

**Error Message**:
```
Microsoft.Playwright.PlaywrightException : Executable doesn't exist at /home/victor/.cache/ms-playwright/chromium_headless_shell-1179/chrome-linux/headless_shell

Looks like Playwright was just installed or updated.
Please run the following command to download new browsers:
    pwsh bin/Debug/netX/playwright.ps1 install
```

### Solution Options

**Option A: Skip Test in CI/Non-Interactive Environments** (Recommended)
Add a conditional fact to skip when Playwright is not installed:

```csharp
[Fact]
public async Task BrowserService_StartOpenContentStop_UsesRealPlaywright()
{
    // Check if Playwright browsers are installed
    try
    {
        var browserService = new BrowserService();
        await browserService.StartAsync();
    }
    catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist"))
    {
        Skip.If(true, "Playwright browsers not installed. Run 'pwsh bin/Debug/net10.0/playwright.ps1 install'");
        return;
    }
    // ... rest of test
}
```

Or use environment variable:

```csharp
[Fact]
[Trait("Category", "Playwright")]
public async Task BrowserService_StartOpenContentStop_UsesRealPlaywright()
{
    if (Environment.GetEnvironmentVariable("SKIP_PLAYWRIGHT_TESTS") == "1")
    {
        Skip.If(true, "Skipping Playwright tests");
        return;
    }
    // ...
}
```

**Option B: Mock Playwright in Tests**
Use a mock/fake implementation of the browser service for unit tests.

**Option C: Install Playwright in CI/CD**
Add Playwright installation step to CI/CD pipeline:
```bash
pwsh src/NanoBot.Tools.Tests/bin/Debug/net10.0/playwright.ps1 install
```

---

## Summary of Recommended Fixes

| Category | Count | Recommended Fix |
|----------|-------|-----------------|
| Configuration Defaults | 2 | Option A - Fix source code to use "default" |
| Tool Hint Formatter | 9 | Option A - Update tests to match new output format |
| Message Sanitizer | 1 | Option B - Update test to expect empty list |
| Subagent Manager | 1 | Option A - Keep completed subagents for retrieval |
| Service Registration | 2 | Option A - Investigate and fix DI registration |
| Playwright | 1 | Option A - Add conditional skip for missing browsers |

---

## Implementation Priority

1. **High**: Configuration Defaults (2 tests) - Simple fix, affects core functionality
2. **High**: Service Registration (2 tests) - DI issues may affect runtime
3. **Medium**: Tool Hint Formatter (9 tests) - UI-related, tests need updating
4. **Medium**: Message Sanitizer (1 test) - Minor behavior clarification
5. **Medium**: Subagent Manager (1 test) - Design decision needed
6. **Low**: Playwright (1 test) - Environment setup issue
