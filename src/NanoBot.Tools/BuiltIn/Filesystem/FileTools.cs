using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NanoBot.Tools.BuiltIn;

public static class FileTools
{
    public static AITool CreateReadFileTool(string? allowedDir = null)
    {
        return AIFunctionFactory.Create(
            (string path, int? startLine, int? endLine, CancellationToken cancellationToken) =>
                ReadFileAsync(path, startLine, endLine, allowedDir, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "read_file",
                Description = "Read the contents of a file at the given path. Returns the file content as a string."
            });
    }

    public static AITool CreateWriteFileTool(string? allowedDir = null)
    {
        return AIFunctionFactory.Create(
            (string path, string content, CancellationToken cancellationToken) =>
                WriteFileAsync(path, content, allowedDir, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "write_file",
                Description = "Write content to a file at the given path. Creates the file if it doesn't exist, overwrites if it does."
            });
    }

    public static AITool CreateEditFileTool(string? allowedDir = null)
    {
        return AIFunctionFactory.Create(
            (string path, string oldStr, string newStr, CancellationToken cancellationToken) =>
                EditFileAsync(path, oldStr, newStr, allowedDir, cancellationToken),
            new AIFunctionFactoryOptions
            {
                Name = "edit_file",
                Description = "Edit a file by replacing a specific string with a new string. The oldStr must be an exact match."
            });
    }

    public static AITool CreateListDirTool(string? allowedDir = null)
    {
        return AIFunctionFactory.Create(
            (string path, bool recursive) =>
                ListDir(path, recursive, allowedDir),
            new AIFunctionFactoryOptions
            {
                Name = "list_dir",
                Description = "List files and directories in the given path. Returns a formatted list of entries."
            });
    }

    private static async Task<string> ReadFileAsync(
        string path,
        int? startLine,
        int? endLine,
        string? allowedDir,
        CancellationToken cancellationToken)
    {
        try
        {
            var filePath = ResolvePath(path, allowedDir);

            if (!File.Exists(filePath))
            {
                return $"Error: File not found: {path}";
            }

            string content;
            if (startLine.HasValue || endLine.HasValue)
            {
                var lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
                var start = startLine.HasValue ? Math.Max(0, startLine.Value - 1) : 0;
                var end = endLine.HasValue ? Math.Min(lines.Length, endLine.Value) : lines.Length;
                content = string.Join("\n", lines[start..end]);
            }
            else
            {
                content = await File.ReadAllTextAsync(filePath, cancellationToken);
            }

            return content;
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    private static async Task<string> WriteFileAsync(
        string path,
        string content,
        string? allowedDir,
        CancellationToken cancellationToken)
    {
        try
        {
            var filePath = ResolvePath(path, allowedDir);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, content, cancellationToken);
            return $"Successfully wrote to {path}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    private static async Task<string> EditFileAsync(
        string path,
        string oldStr,
        string newStr,
        string? allowedDir,
        CancellationToken cancellationToken)
    {
        try
        {
            var filePath = ResolvePath(path, allowedDir);

            if (!File.Exists(filePath))
            {
                return $"Error: File not found: {path}";
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (!content.Contains(oldStr))
            {
                return $"Error: Could not find the string to replace in {path}";
            }

            var newContent = content.Replace(oldStr, newStr);
            await File.WriteAllTextAsync(filePath, newContent, cancellationToken);
            return $"Successfully edited {path}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }

    private static string ListDir(string path, bool recursive, string? allowedDir)
    {
        try
        {
            var dirPath = ResolvePath(path, allowedDir);

            if (!Directory.Exists(dirPath))
            {
                return $"Error: Directory not found: {path}";
            }

            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var entries = Directory.GetFileSystemEntries(dirPath, "*", searchOption);

            var result = new System.Text.StringBuilder();
            foreach (var entry in entries.OrderBy(e => e))
            {
                var relativePath = entry.Substring(dirPath.Length).TrimStart(Path.DirectorySeparatorChar);
                var isDir = Directory.Exists(entry);
                result.AppendLine($"{(isDir ? "DIR " : "FILE")} {relativePath}");
            }

            return result.ToString();
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Error: {ex.Message}";
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    private static string ResolvePath(string path, string? allowedDir)
    {
        var resolved = Path.IsPathRooted(path) ? path : Path.Combine(Directory.GetCurrentDirectory(), path);
        resolved = Path.GetFullPath(resolved);

        if (allowedDir != null)
        {
            var allowedFull = Path.GetFullPath(allowedDir);
            if (!resolved.StartsWith(allowedFull, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException($"Path '{path}' is outside allowed directory '{allowedDir}'");
            }
        }

        return resolved;
    }
}
