# 文件编辑工具增强方案

本文档定义基于 opencode 实现经验的文件编辑工具（FileTools）增强设计。

**参考实现**：opencode (TypeScript), cline, google-gemini-cli

**影响范围**：`src/NanoBot.Tools/BuiltIn/Filesystem/`

---

## 背景与动机

### 现有实现的问题

当前 `FileTools.cs` 的 `edit_file` 工具使用简单的字符串替换，存在以下问题：

1. **精确匹配过于严格** - 缩进、空格、行尾差异导致编辑失败
2. **多匹配无提示** - 当 oldStr 出现多次时，可能替换错误位置
3. **错误信息不友好** - 匹配失败时无法给出有用的建议
4. **无文件大小保护** - 可能因读取大文件导致 OOM
5. **无行尾规范化** - CRLF/LF 混用导致问题

---

## 增强目标

| 目标 | 优先级 | 说明 |
|------|--------|------|
| 多策略匹配 | P0 | 支持多种匹配策略，适应各种代码风格 |
| 智能错误提示 | P0 | 匹配失败时提供模糊匹配建议 |
| 文件安全保护 | P1 | 大小限制、二进制检测 |
| 行尾规范化 | P1 | 自动处理 CRLF/LF |
| 并发安全 | P2 | 文件时间戳锁定 |
| 编辑预览 | P2 | 生成 diff 供确认 |

---

## 核心设计

### 1. 命名空间与目录结构

```
src/NanoBot.Tools/BuiltIn/Filesystem/
├── FileTools.cs                    # 现有文件（保持兼容）
├── Enhanced/
│   ├── Abstractions/               # 接口定义
│   │   ├── ITextReplacer.cs
│   │   ├── ILineEndingNormalizer.cs
│   │   └── IFuzzyMatcher.cs
│   ├── Models/                     # 数据模型
│   │   ├── MatchResult.cs
│   │   ├── ReadResult.cs
│   │   └── FileEditOptions.cs
│   ├── Replacers/                  # 替换策略实现
│   │   ├── ExactReplacer.cs
│   │   ├── LineTrimmedReplacer.cs
│   │   ├── BlockAnchorReplacer.cs
│   │   ├── WhitespaceNormalizedReplacer.cs
│   │   ├── IndentationFlexibleReplacer.cs
│   │   ├── EscapeNormalizedReplacer.cs
│   │   ├── TrimmedBoundaryReplacer.cs
│   │   └── ContextAwareReplacer.cs
│   ├── Helpers/
│   │   ├── LineEndingHelper.cs
│   │   ├── FuzzyMatchHelper.cs
│   │   └── DiffGenerator.cs
│   ├── EnhancedEditTool.cs         # 主入口
│   └── EnhancedFileReader.cs       # 增强读取
```

---

### 2. 接口定义

#### ITextReplacer

文本替换器接口，用于在文件内容中查找匹配文本。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Abstractions;

/// <summary>
/// 文本替换器接口 - 尝试在内容中查找匹配文本
/// </summary>
public interface ITextReplacer
{
    /// <summary>
    /// 替换器名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 尝试查找匹配文本
    /// </summary>
    /// <param name="content">文件内容</param>
    /// <param name="searchText">搜索文本</param>
    /// <returns>匹配结果枚举</returns>
    IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### ILineEndingNormalizer

行尾规范化接口。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Abstractions;

/// <summary>
/// 行尾规范化接口
/// </summary>
public interface ILineEndingNormalizer
{
    /// <summary>
    /// 检测行尾类型
    /// </summary>
    string DetectLineEnding(string text);

    /// <summary>
    /// 规范化行尾为 \n
    /// </summary>
    string Normalize(string text);

    /// <summary>
    /// 转换为指定行尾
    /// </summary>
    string ConvertTo(string text, string ending);
}
```

#### IFuzzyMatcher

模糊匹配接口，用于提供匹配失败时的建议。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Abstractions;

/// <summary>
/// 模糊匹配接口
/// </summary>
public interface IFuzzyMatcher
{
    /// <summary>
    /// 计算文本相似度
    /// </summary>
    double CalculateSimilarity(string[] expected, string[] actual);

    /// <summary>
    /// 查找最佳匹配
    /// </summary>
    (int StartLine, double Similarity) FindBestMatch(string searchText, string content);

    /// <summary>
    /// 生成建议信息
    /// </summary>
    string GenerateSuggestion(string searchText, string content, string filePath);
}
```

---

### 3. 数据模型

#### MatchResult

匹配结果。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Models;

/// <summary>
/// 匹配结果
/// </summary>
public readonly record struct MatchResult
{
    /// <summary>
    /// 匹配的文本
    /// </summary>
    public string MatchedText { get; init; }

    /// <summary>
    /// 起始索引
    /// </summary>
    public int StartIndex { get; init; }

    /// <summary>
    /// 匹配长度
    /// </summary>
    public int Length { get; init; }

    /// <summary>
    /// 相似度 (0.0 - 1.0)
    /// </summary>
    public double Similarity { get; init; }

    /// <summary>
    /// 使用的替换器名称
    /// </summary>
    public string ReplacerName { get; init; }
}
```

#### ReadResult

文件读取结果。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Models;

/// <summary>
/// 文件读取结果
/// </summary>
public record ReadResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 读取的行列表
    /// </summary>
    public IReadOnlyList<string> Lines { get; init; }

    /// <summary>
    /// 文件总行数
    /// </summary>
    public int TotalLines { get; init; }

    /// <summary>
    /// 是否有更多内容
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// 起始行号（1-based）
    /// </summary>
    public int StartLine { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? Error { get; init; }
}
```

#### FileEditOptions

文件编辑选项。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Models;

/// <summary>
/// 文件编辑选项
/// </summary>
public record FileEditOptions
{
    /// <summary>
    /// 替换所有匹配项
    /// </summary>
    public bool ReplaceAll { get; init; }

    /// <summary>
    /// 允许的目录（安全限制）
    /// </summary>
    public string? AllowedDirectory { get; init; }

    /// <summary>
    /// 模糊匹配阈值
    /// </summary>
    public double FuzzyMatchThreshold { get; init; } = 0.5;

    /// <summary>
    /// 是否启用模糊匹配建议
    /// </summary>
    public bool EnableFuzzyMatch { get; init; } = true;
}
```

---

### 4. 配置模型

#### FileToolsConfig

文件工具配置。

```csharp
namespace NanoBot.Core.Configuration;

/// <summary>
/// 文件工具配置
/// </summary>
public class FileToolsConfig
{
    /// <summary>
    /// 是否使用增强文件工具
    /// </summary>
    public bool UseEnhanced { get; set; } = false;

    /// <summary>
    /// 读取配置
    /// </summary>
    public FileReadConfig Read { get; set; } = new();

    /// <summary>
    /// 编辑配置
    /// </summary>
    public FileEditConfig Edit { get; set; } = new();
}

/// <summary>
/// 文件读取配置
/// </summary>
public class FileReadConfig
{
    /// <summary>
    /// 最大读取字符数（默认 128KB）
    /// </summary>
    public int MaxChars { get; set; } = 128_000;

    /// <summary>
    /// 单次最大字节数（默认 50KB）
    /// </summary>
    public int MaxBytes { get; set; } = 50 * 1024;

    /// <summary>
    /// 单行最大长度（默认 2000）
    /// </summary>
    public int MaxLineLength { get; set; } = 2000;

    /// <summary>
    /// 默认读取行数限制
    /// </summary>
    public int DefaultLineLimit { get; set; } = 2000;

    /// <summary>
    /// 是否启用二进制文件检测
    /// </summary>
    public bool EnableBinaryDetection { get; set; } = true;
}

/// <summary>
/// 文件编辑配置
/// </summary>
public class FileEditConfig
{
    /// <summary>
    /// 单候选相似度阈值（默认 0.0，即只要有锚点匹配就接受）
    /// </summary>
    public double SingleCandidateThreshold { get; set; } = 0.0;

    /// <summary>
    /// 多候选相似度阈值（默认 0.3）
    /// </summary>
    public double MultipleCandidatesThreshold { get; set; } = 0.3;

    /// <summary>
    /// 是否启用模糊匹配建议
    /// </summary>
    public bool EnableFuzzySuggestions { get; set; } = true;
}
```

---

### 5. 替换策略类

#### ExactReplacer

精确匹配策略。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 精确匹配替换器
/// </summary>
public class ExactReplacer : ITextReplacer
{
    public string Name => "exact";
    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### LineTrimmedReplacer

行修剪匹配策略（忽略行首尾空白）。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 行修剪匹配替换器
/// </summary>
public class LineTrimmedReplacer : ITextReplacer
{
    public string Name => "line-trimmed";
    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### BlockAnchorReplacer

块锚点匹配策略（首行+末行锚定，Levenshtein 相似度）。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 块锚点匹配替换器
/// </summary>
public class BlockAnchorReplacer : ITextReplacer
{
    public string Name => "block-anchor";

    /// <summary>
    /// 单候选相似度阈值
    /// </summary>
    public double SingleCandidateThreshold { get; set; } = 0.0;

    /// <summary>
    /// 多候选相似度阈值
    /// </summary>
    public double MultipleCandidatesThreshold { get; set; } = 0.3;

    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### WhitespaceNormalizedReplacer

空白规范化匹配策略。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 空白规范化匹配替换器
/// </summary>
public class WhitespaceNormalizedReplacer : ITextReplacer
{
    public string Name => "whitespace-normalized";
    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### IndentationFlexibleReplacer

缩进灵活匹配策略。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 缩进灵活匹配替换器
/// </summary>
public class IndentationFlexibleReplacer : ITextReplacer
{
    public string Name => "indentation-flexible";
    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### EscapeNormalizedReplacer

转义规范化匹配策略。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 转义规范化匹配替换器
/// </summary>
public class EscapeNormalizedReplacer : ITextReplacer
{
    public string Name => "escape-normalized";
    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### TrimmedBoundaryReplacer

边界修剪匹配策略。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 边界修剪匹配替换器
/// </summary>
public class TrimmedBoundaryReplacer : ITextReplacer
{
    public string Name => "trimmed-boundary";
    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

#### ContextAwareReplacer

上下文感知匹配策略。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Replacers;

/// <summary>
/// 上下文感知匹配替换器
/// </summary>
public class ContextAwareReplacer : ITextReplacer
{
    public string Name => "context-aware";

    /// <summary>
    /// 中间行匹配阈值（默认 0.5）
    /// </summary>
    public double ContextMatchThreshold { get; set; } = 0.5;

    public IEnumerable<MatchResult> FindMatches(string content, string searchText);
}
```

---

### 6. 工具主类

#### EnhancedEditTool

增强的编辑工具主类。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced;

/// <summary>
/// 增强的文件编辑工具
/// </summary>
public static class EnhancedEditTool
{
    /// <summary>
    /// 创建增强的 edit_file 工具
    /// </summary>
    public static AITool Create(FileToolsConfig? config = null);

    /// <summary>
    /// 创建增强的 edit_file 工具（兼容旧版）
    /// </summary>
    public static AITool Create(string? allowedDir);
}
```

#### EnhancedFileReader

增强的文件读取工具。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced;

/// <summary>
/// 增强的文件读取工具
/// </summary>
public static class EnhancedFileReader
{
    /// <summary>
    /// 创建 read_file 工具
    /// </summary>
    public static AITool CreateTool(FileToolsConfig? config = null);

    /// <summary>
    /// 读取文件内容
    /// </summary>
    public static Task<ReadResult> ReadAsync(
        string filePath,
        int? offset = null,
        int? limit = null,
        CancellationToken ct = default);

    /// <summary>
    /// 检测是否为二进制文件
    /// </summary>
    public static Task<bool> IsBinaryFileAsync(string filePath, int fileSize);
}
```

---

### 7. 辅助类

#### LineEndingHelper

行尾处理辅助类。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Helpers;

/// <summary>
/// 行尾处理辅助类
/// </summary>
public static class LineEndingHelper
{
    public static string DetectLineEnding(string text);
    public static string Normalize(string text);
    public static string ConvertTo(string text, string ending);
}
```

#### FuzzyMatchHelper

模糊匹配辅助类。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Helpers;

/// <summary>
/// 模糊匹配辅助类
/// </summary>
public static class FuzzyMatchHelper
{
    public static double CalculateSimilarity(string[] expected, string[] actual);
    public static (int StartLine, double Similarity) FindBestMatch(string searchText, string content);
    public static string GenerateSuggestion(string searchText, string content, string filePath);
    public static string GenerateUnifiedDiff(string[] expected, string[] actual, string filePath, int startLine);
}
```

#### DiffGenerator

差异生成辅助类。

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced.Helpers;

/// <summary>
/// 差异生成辅助类
/// </summary>
public static class DiffGenerator
{
    public static string Generate(string filePath, string oldContent, string newContent);
    public static string TrimDiff(string diff);
}
```

---

### 8. 服务注册

```csharp
namespace NanoBot.Tools.FileSystem.Enhanced;

/// <summary>
/// 服务集合扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加增强文件工具服务
    /// </summary>
    public static IServiceCollection AddEnhancedFileTools(
        this IServiceCollection services,
        FileToolsConfig? config = null);
}
```

---

## 替换策略优先级

| 优先级 | 策略 | 适用场景 |
|--------|------|----------|
| 1 | ExactReplacer | 完全一致的代码块 |
| 2 | LineTrimmedReplacer | 行首尾空白有差异 |
| 3 | BlockAnchorReplacer | 中间内容有轻微差异，首末行匹配 |
| 4 | WhitespaceNormalizedReplacer | 空格/Tab 混用 |
| 5 | IndentationFlexibleReplacer | 相对缩进不同 |
| 6 | EscapeNormalizedReplacer | 转义字符差异 |
| 7 | TrimmedBoundaryReplacer | 整体空白差异 |
| 8 | ContextAwareReplacer | 上下文匹配 |

---

## 迁移策略

### 阶段1：新增（向后兼容）

保持 `FileTools.cs` 不变，新增 `Enhanced/` 目录。

```csharp
// 新增方法到 FileTools.cs
public static class FileTools
{
    // 现有方法保持兼容
    public static AITool CreateReadFileTool(string? allowedDir = null);
    public static AITool CreateWriteFileTool(string? allowedDir = null);
    public static AITool CreateEditFileTool(string? allowedDir = null);
    public static AITool CreateListDirTool(string? allowedDir = null);

    // 新增增强版本
    public static AITool CreateEnhancedEditFileTool(FileToolsConfig? config = null);
    public static AITool CreateEnhancedReadFileTool(FileToolsConfig? config = null);
}
```

### 阶段2：配置控制

通过配置切换实现：

```csharp
// ToolProvider.cs
public static class ToolProvider
{
    public static Task<IReadOnlyList<AITool>> CreateDefaultToolsAsync(
        IServiceProvider services,
        AgentConfig config)
    {
        if (config.Tools?.FileTools?.UseEnhanced == true)
        {
            // 使用增强版本
        }
        else
        {
            // 使用现有版本
        }
    }
}
```

### 阶段3：默认启用

测试稳定后，将 `UseEnhanced` 默认值改为 `true`。

---

## 配置示例

```json
{
  "Tools": {
    "FileTools": {
      "UseEnhanced": true,
      "Read": {
        "MaxChars": 128000,
        "MaxBytes": 51200,
        "MaxLineLength": 2000,
        "DefaultLineLimit": 2000,
        "EnableBinaryDetection": true
      },
      "Edit": {
        "SingleCandidateThreshold": 0.0,
        "MultipleCandidatesThreshold": 0.3,
        "EnableFuzzySuggestions": true
      }
    }
  }
}
```

---

## 参考链接

- [cline diff-apply](https://github.com/cline/cline/blob/main/evals/diff-edits/diff-apply/)
- [google-gemini-cli editCorrector](https://github.com/google-gemini/gemini-cli/blob/main/packages/core/src/utils/editCorrector.ts)
- opencode `packages/opencode/src/tool/edit.ts`

---

*返回 [工具层设计](./Tools.md)*
