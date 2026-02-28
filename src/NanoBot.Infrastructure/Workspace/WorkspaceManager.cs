using NanoBot.Core.Configuration;
using NanoBot.Core.Workspace;
using NanoBot.Infrastructure.Resources;
using Microsoft.Extensions.Logging;

namespace NanoBot.Infrastructure.Workspace;

public class WorkspaceManager : IWorkspaceManager
{
    private readonly WorkspaceConfig _config;
    private readonly IEmbeddedResourceLoader? _resourceLoader;
    private readonly ILogger<WorkspaceManager>? _logger;
    private bool _initialized;

    public WorkspaceManager(
        WorkspaceConfig config,
        IEmbeddedResourceLoader? resourceLoader = null,
        ILogger<WorkspaceManager>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _resourceLoader = resourceLoader;
        _logger = logger;
    }

    public string GetWorkspacePath() => _config.GetResolvedPath();

    public string GetMemoryPath() => _config.GetMemoryPath();

    public string GetSkillsPath() => _config.GetSkillsPath();

    public string GetSessionsPath() => _config.GetSessionsPath();

    public string GetAgentsFile() => _config.GetAgentsFile();

    public string GetSoulFile() => _config.GetSoulFile();

    public string GetToolsFile() => _config.GetToolsFile();

    public string GetUserFile() => _config.GetUserFile();

    public string GetHeartbeatFile() => _config.GetHeartbeatFile();

    public string GetMemoryFile() => _config.GetMemoryFile();

    public string GetHistoryFile() => _config.GetHistoryFile();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        var workspacePath = GetWorkspacePath();

        EnsureDirectory(workspacePath);
        EnsureDirectory(GetMemoryPath());
        EnsureDirectory(GetSkillsPath());
        EnsureDirectory(GetSessionsPath());

        if (_resourceLoader != null)
        {
            await ExtractDefaultFilesFromResourcesAsync(cancellationToken);
        }
        else
        {
            await CreateDefaultFilesAsync(cancellationToken);
        }

        _initialized = true;
        _logger?.LogInformation("Workspace initialized at {WorkspacePath}", workspacePath);
    }

    private async Task ExtractDefaultFilesFromResourcesAsync(CancellationToken cancellationToken)
    {
        var workspaceResources = _resourceLoader!.GetWorkspaceResourceNames();
        _logger?.LogDebug("Found {Count} workspace resources", workspaceResources.Count);

        foreach (var resourceName in workspaceResources)
        {
            var relativePath = ConvertResourceNameToRelativePath(resourceName, "templates");
            var targetPath = Path.Combine(GetWorkspacePath(), relativePath);

            if (File.Exists(targetPath))
            {
                _logger?.LogDebug("Skipping existing file: {TargetPath}", targetPath);
                continue;
            }

            var content = await _resourceLoader.ReadResourceAsync(resourceName, cancellationToken);
            if (content != null)
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(targetPath, content, cancellationToken);
                _logger?.LogDebug("Extracted resource: {ResourceName} -> {TargetPath}", resourceName, targetPath);
            }
        }

        var skillsResources = _resourceLoader.GetSkillsResourceNames();
        _logger?.LogDebug("Found {Count} skills resources", skillsResources.Count);

        foreach (var resourceName in skillsResources)
        {
            var relativePath = ConvertResourceNameToRelativePath(resourceName, "skills");
            var targetPath = Path.Combine(GetSkillsPath(), relativePath);

            if (File.Exists(targetPath))
            {
                _logger?.LogDebug("Skipping existing file: {TargetPath}", targetPath);
                continue;
            }

            var content = await _resourceLoader.ReadResourceAsync(resourceName, cancellationToken);
            if (content != null)
            {
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(targetPath, content, cancellationToken);
                _logger?.LogDebug("Extracted skill resource: {ResourceName} -> {TargetPath}", resourceName, targetPath);
            }
        }
    }

    private static string ConvertResourceNameToRelativePath(string resourceName, string prefix)
    {
        var name = resourceName;

        if (name.StartsWith(prefix + "/"))
        {
            name = name[(prefix.Length + 1)..];
        }

        return name.Replace('/', Path.DirectorySeparatorChar);
    }

    private async Task CreateDefaultFilesAsync(CancellationToken cancellationToken)
    {
        var defaultFiles = new Dictionary<string, string>
        {
            [GetAgentsFile()] = GetDefaultAgentsContent(),
            [GetSoulFile()] = GetDefaultSoulContent(),
            [GetToolsFile()] = GetDefaultToolsContent(),
            [GetUserFile()] = GetDefaultUserContent(),
            [GetHeartbeatFile()] = GetDefaultHeartbeatContent(),
            [GetMemoryFile()] = string.Empty,
            [GetHistoryFile()] = string.Empty
        };

        foreach (var (path, content) in defaultFiles)
        {
            if (!File.Exists(path))
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                await File.WriteAllTextAsync(path, content, cancellationToken);
            }
        }
    }

    private static string GetDefaultAgentsContent() => @"# Agent Instructions

You are a helpful AI assistant. Be concise, accurate, and friendly.

## Guidelines

- Always explain what you're doing before taking actions
- Ask for clarification when the request is ambiguous
- Use tools to help accomplish tasks
- Remember important information in your memory files

## Tools Available

You have access to:
- File operations (read, write, edit, list)
- Shell commands (exec)
- Web access (search, fetch)
- Messaging (message)
- Background tasks (spawn)

## Memory

- `memory/MEMORY.md` — long-term facts (preferences, context, relationships)
- `memory/HISTORY.md` — append-only event log, search with grep to recall past events

## Scheduled Reminders

When user asks for a reminder at a specific time, use `exec` to run:
```
nanobot cron add --name ""reminder"" --message ""Your message"" --at ""YYYY-MM-DDTHH:MM:SS"" --deliver --to ""USER_ID"" --channel ""CHANNEL""
```
Get USER_ID and CHANNEL from the current session (e.g., `8281248569` and `telegram` from `telegram:8281248569`).

**Do NOT just write reminders to MEMORY.md** — that won't trigger actual notifications.

## Heartbeat Tasks

`HEARTBEAT.md` is checked every 30 minutes. You can manage periodic tasks by editing this file:

- **Add a task**: Use `edit_file` to append new tasks to `HEARTBEAT.md`
- **Remove a task**: Use `edit_file` to remove completed or obsolete tasks
- **Rewrite tasks**: Use `write_file` to completely rewrite the task list

Task format examples:
```
- [ ] Check calendar and remind of upcoming events
- [ ] Scan inbox for urgent emails
- [ ] Check weather forecast for today
```

When the user asks you to add a recurring/periodic task, update `HEARTBEAT.md` instead of creating a one-time reminder. Keep the file small to minimize token usage.";

    private static string GetDefaultSoulContent() => @"# Soul

I am nanobot 🐈, a personal AI assistant.

## Personality

- Helpful and friendly
- Concise and to the point
- Curious and eager to learn

## Values

- Accuracy over speed
- User privacy and safety
- Transparency in actions

## Communication Style

- Be clear and direct
- Explain reasoning when helpful
- Ask clarifying questions when needed";

    private static string GetDefaultToolsContent() => @"# Available Tools

This document describes the tools available to nanobot.

## File Operations

### read_file
Read the contents of a file.
```
read_file(path: str) -> str
```

### write_file
Write content to a file (creates parent directories if needed).
```
write_file(path: str, content: str) -> str
```

### edit_file
Edit a file by replacing specific text.
```
edit_file(path: str, old_text: str, new_text: str) -> str
```

### list_dir
List contents of a directory.
```
list_dir(path: str) -> str
```

## Shell Execution

### exec
Execute a shell command and return output.
```
exec(command: str, working_dir: str = None) -> str
```

**Safety Notes:**
- Commands have a configurable timeout (default 60s)
- Dangerous commands are blocked (rm -rf, format, dd, shutdown, etc.)
- Output is truncated at 10,000 characters
- Optional `restrictToWorkspace` config to limit paths

## Web Access

### web_search
Search the web using Brave Search API.
```
web_search(query: str, count: int = 5) -> str
```

Returns search results with titles, URLs, and snippets. Requires `tools.web.search.apiKey` in config.

### web_fetch
Fetch and extract main content from a URL.
```
web_fetch(url: str, extractMode: str = ""markdown"", maxChars: int = 50000) -> str
```

**Notes:**
- Content is extracted using readability
- Supports markdown or plain text extraction
- Output is truncated at 50,000 characters by default

## Communication

### message
Send a message to the user (used internally).
```
message(content: str, channel: str = None, chat_id: str = None) -> str
```

## Background Tasks

### spawn
Spawn a subagent to handle a task in the background.
```
spawn(task: str, label: str = None) -> str
```

Use for complex or time-consuming tasks that can run independently. The subagent will complete the task and report back when done.";

    private static string GetDefaultUserContent() => @"# User Profile

Information about the user to help personalize interactions.

## Basic Information

- **Name**: (your name)
- **Timezone**: (your timezone, e.g., UTC+8)
- **Language**: (preferred language)

## Preferences

### Communication Style

- [ ] Casual
- [ ] Professional
- [ ] Technical

### Response Length

- [ ] Brief and concise
- [ ] Detailed explanations
- [ ] Adaptive based on question

### Technical Level

- [ ] Beginner
- [ ] Intermediate
- [ ] Expert

## Work Context

- **Primary Role**: (your role, e.g., developer, researcher)
- **Main Projects**: (what you're working on)
- **Tools You Use**: (IDEs, languages, frameworks)

## Topics of Interest

- 
- 
- 

## Special Instructions

(Any specific instructions for how the assistant should behave)

---
*Edit this file to customize nanobot's behavior for your needs.*";

    private static string GetDefaultHeartbeatContent() => @"# Heartbeat Tasks

This file is checked every 30 minutes by your nanobot agent.
Add tasks below that you want the agent to work on periodically.

If this file has no tasks (only headers and comments), the agent will skip the heartbeat.

## Active Tasks

<!-- Add your periodic tasks below this line -->


## Completed

<!-- Move completed tasks here or delete them -->";

    public void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public bool FileExists(string relativePath)
    {
        var fullPath = Path.Combine(GetWorkspacePath(), relativePath);
        return File.Exists(fullPath);
    }

    public async Task<string?> ReadFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(GetWorkspacePath(), relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }
        return await File.ReadAllTextAsync(fullPath, cancellationToken);
    }

    public async Task WriteFileAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(GetWorkspacePath(), relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);
    }

    public async Task AppendFileAsync(string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.Combine(GetWorkspacePath(), relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.AppendAllTextAsync(fullPath, content, cancellationToken);
    }
}
