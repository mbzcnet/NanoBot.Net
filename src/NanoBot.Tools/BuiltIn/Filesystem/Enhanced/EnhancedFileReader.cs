using Microsoft.Extensions.AI;
using NanoBot.Core.Configuration;
using NanoBot.Tools.BuiltIn.Filesystem.Enhanced.Models;
using System.Text;

namespace NanoBot.Tools.BuiltIn.Filesystem.Enhanced;

/// <summary>
/// 增强的文件读取工具
/// </summary>
public static class EnhancedFileReader
{
    /// <summary>
    /// 创建 read_file 工具
    /// </summary>
    public static AITool CreateTool(FileToolsConfig? config = null)
    {
        var readConfig = config?.Read ?? new FileReadConfig();

        return AIFunctionFactory.Create(
            async (string filePath, int? offset, int? limit, CancellationToken ct)
                => await ReadAsync(filePath, offset, limit, readConfig, ct),
            new AIFunctionFactoryOptions
            {
                Name = "read_file",
                Description = "Read the contents of a file at the given path. Returns the file content as a string with line numbers."
            });
    }

    private static async Task<string> ReadAsync(
        string filePath,
        int? offset,
        int? limit,
        FileReadConfig config,
        CancellationToken ct)
    {
        try
        {
            var path = ResolvePath(filePath, null);

            if (!File.Exists(path))
                return $"Error: File not found: {filePath}";

            var fileInfo = new FileInfo(path);

            if (fileInfo.Length > config.MaxChars * 4)
            {
                return $"Error: File too large ({fileInfo.Length:N0} bytes). " +
                       "Use exec tool with head/tail/grep to read portions.";
            }

            if (config.EnableBinaryDetection && await IsBinaryFileAsync(path, (int)fileInfo.Length))
            {
                return $"Error: Cannot read binary file: {filePath}";
            }

            var result = await ReadWithLimitsAsync(path, offset, limit, config, ct);

            if (!result.Success)
                return $"Error: {result.Error}";

            var sb = new StringBuilder();
            sb.AppendLine($"<path>{path}</path>");
            sb.AppendLine("<type>file</type>");
            sb.AppendLine("<content>");

            for (int i = 0; i < result.Lines.Count; i++)
            {
                sb.AppendLine($"{result.StartLine + i}: {result.Lines[i]}");
            }

            if (result.HasMore)
            {
                var nextOffset = result.StartLine + result.Lines.Count;
                sb.AppendLine($"\n(Showing lines {result.StartLine}-{nextOffset - 1} of {result.TotalLines}. " +
                             $"Use offset={nextOffset} to continue.)");
            }
            else
            {
                sb.AppendLine($"\n(End of file - total {result.TotalLines} lines)");
            }

            sb.AppendLine("</content>");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    private static async Task<ReadResult> ReadWithLimitsAsync(
        string filePath,
        int? offset,
        int? limit,
        FileReadConfig config,
        CancellationToken ct)
    {
        var lines = new List<string>();
        int bytes = 0;
        int startLine = (offset ?? 1) - 1;
        int lineLimit = limit ?? config.DefaultLineLimit;
        int totalLines = 0;
        bool hasMore = false;

        await using var stream = File.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            totalLines++;

            if (totalLines <= startLine)
                continue;

            if (lines.Count >= lineLimit)
            {
                hasMore = true;
                continue;
            }

            if (line.Length > config.MaxLineLength)
                line = line[..config.MaxLineLength] + $"... (line truncated to {config.MaxLineLength} chars)";

            var lineBytes = Encoding.UTF8.GetByteCount(line);
            if (bytes + lineBytes > config.MaxBytes)
            {
                hasMore = true;
                break;
            }

            lines.Add(line);
            bytes += lineBytes;
        }

        return ReadResult.SuccessResult(lines, totalLines, hasMore, startLine + 1);
    }

    private static async Task<bool> IsBinaryFileAsync(string filePath, int fileSize)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var binaryExts = new HashSet<string>
        {
            ".zip", ".tar", ".gz", ".exe", ".dll", ".so", ".class", ".jar", ".war",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".bin", ".dat", ".obj", ".o", ".a", ".lib", ".wasm", ".pyc"
        };

        if (binaryExts.Contains(ext))
            return true;

        if (fileSize == 0)
            return false;

        await using var fs = File.OpenRead(filePath);
        var sampleSize = Math.Min(4096, fileSize);
        var buffer = new byte[sampleSize];
        var read = await fs.ReadAsync(buffer, 0, sampleSize);

        int nonPrintable = 0;
        for (int i = 0; i < read; i++)
        {
            if (buffer[i] == 0)
                return true;

            if (buffer[i] < 9 || (buffer[i] > 13 && buffer[i] < 32))
                nonPrintable++;
        }

        return (double)nonPrintable / read > 0.3;
    }

    private static string ResolvePath(string path, string? allowedDir)
    {
        var resolved = Path.IsPathRooted(path)
            ? path
            : Path.Combine(Directory.GetCurrentDirectory(), path);

        resolved = Path.GetFullPath(resolved);

        if (allowedDir != null)
        {
            var allowedFull = Path.GetFullPath(allowedDir);
            if (!resolved.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    $"Path '{path}' is outside allowed directory '{allowedDir}'");
            }
        }

        return resolved;
    }
}
