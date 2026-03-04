# Agent 系统文件夹访问限制机制

本文档总结 NanoBot.Net 中 Agent 的系统文件夹访问限制实现机制。

## 概述

Agent 的文件系统访问安全通过多层机制实现，核心设计原则是**默认拒绝、最小权限**。

## 核心限制机制

### 1. 配置层

**文件**: `src/NanoBot.Core/Configuration/Models/SecurityConfig.cs`

```csharp
public class SecurityConfig
{
    public IReadOnlyList<string> AllowedDirs { get; set; } = Array.Empty<string>();  // 额外允许的目录
    public IReadOnlyList<string> DenyCommandPatterns { get; set; } = Array.Empty<string>();  // 危险命令模式
    public bool RestrictToWorkspace { get; set; } = true;  // 默认限制在工作区
    public int ShellTimeout { get; set; } = 60;
}
```

- `RestrictToWorkspace`：默认开启，限制 Agent 只能访问工作区目录
- `AllowedDirs`：可配置额外允许访问的目录白名单

### 2. 文件系统层

**文件**: `src/NanoBot.Tools/BuiltIn/Filesystem/FileTools.cs`

```csharp
private static string ResolvePath(string path, string? allowedDir)
{
    var resolved = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
    resolved = Path.GetFullPath(resolved);  // 解析为绝对路径

    if (allowedDir != null)
    {
        var allowedFull = Path.GetFullPath(allowedDir);
        // 检查路径是否在允许目录内
        if (!resolved.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Path '{path}' is outside allowed directory '{allowedDir}'");
        }
    }
    return resolved;
}
```

### 3. Shell 命令层

**文件**: `src/NanoBot.Tools/BuiltIn/Shell/ShellTools.cs`

#### 路径遍历检测

```csharp
if (options.RestrictToWorkspace)
{
    // 检测 ../ 和 ..\ 路径遍历
    if (command.Contains("..\\") || command.Contains("../"))
    {
        return "Error: Command blocked by safety guard (path traversal detected)";
    }
    
    // 检查绝对路径是否在工作区外
    var workspacePath = Path.GetFullPath(options.WorkingDirectory ?? cwd);
    var absolutePaths = ExtractAbsolutePaths(command);
    foreach (var path in absolutePaths)
    {
        var resolvedPath = Path.GetFullPath(path);
        if (!resolvedPath.StartsWith(workspacePath, StringComparison.OrdinalIgnoreCase))
        {
            return "Error: Command blocked by safety guard (path outside working directory)";
        }
    }
}
```

#### 危险命令拦截（正则匹配）

```csharp
public static readonly string[] DefaultDenyPatterns =
{
    @"\brm\s+-[rf]{1,2}\b",          // rm -r, rm -rf
    @"\bdel\s+/[fq]\b",              // del /f, del /q
    @"\brmdir\s+/s\b",               // rmdir /s
    @"(?:^|[;&|]\s*)format\b",       // format
    @"\b(mkfs|diskpart)\b",          // 磁盘操作
    @"\bdd\s+if=",                   // dd
    @">\s*/dev/sd",                  // 写入磁盘
    @"\b(shutdown|reboot|poweroff)\b",  // 系统电源
    @":\(\)\s*\{.*\};\s*:",          // fork bomb
};
```

### 4. 配置验证层

**文件**: `src/NanoBot.Core/Configuration/Validators/ConfigurationValidator.cs`

```csharp
private static void ValidateSecurity(AgentConfig config, List<string> warnings)
{
    if (!config.Security.RestrictToWorkspace && config.Security.AllowedDirs.Count == 0)
    {
        warnings.Add("RestrictToWorkspace is disabled but no AllowedDirs specified. Agent may access any directory.");
    }
}
```

## 安全策略总结

| 层级 | 机制 | 作用 |
|------|------|------|
| 配置 | `RestrictToWorkspace` | 总开关，默认开启 |
| 配置 | `AllowedDirs` | 白名单机制 |
| 文件操作 | 路径解析验证 | 阻止目录穿越 |
| Shell | 路径遍历检测 | 拦截 `../` 命令 |
| Shell | 危险命令正则 | 拦截 rm -rf 等 |
| 验证 | 配置检查 | 提醒不安全配置 |

## 相关文件

- `src/NanoBot.Core/Configuration/Models/SecurityConfig.cs`
- `src/NanoBot.Tools/BuiltIn/Filesystem/FileTools.cs`
- `src/NanoBot.Tools/BuiltIn/Shell/ShellTools.cs`
- `src/NanoBot.Core/Configuration/Validators/ConfigurationValidator.cs`
