using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;
using Microsoft.Extensions.Logging;

namespace NanoBot.Infrastructure.Storage;

public class FileStorageService : IFileStorageService
{
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(
        IWorkspaceManager workspaceManager,
        ILogger<FileStorageService> logger)
    {
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public async Task<string> SaveFileAsync(Stream sourceStream, string sessionId, string? extension = null, CancellationToken cancellationToken = default)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".png" : extension;
        
        var sessionUploadsRoot = Path.Combine(_workspaceManager.GetSessionsPath(), sessionId, "uploads");
        _workspaceManager.EnsureDirectory(sessionUploadsRoot);

        var fileName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
        var relativePath = Path.Combine(sessionId, "uploads", fileName);
        var fullPath = Path.Combine(_workspaceManager.GetSessionsPath(), relativePath);

        await using var writeStream = File.Create(fullPath);
        if (sourceStream.CanSeek)
        {
            sourceStream.Position = 0;
        }

        await sourceStream.CopyToAsync(writeStream, cancellationToken);
        
        _logger.LogDebug("File saved to {FilePath}", fullPath);
        return relativePath;
    }

    public Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.Combine(_workspaceManager.GetSessionsPath(), relativePath);
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogDebug("File deleted: {FilePath}", fullPath);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting file {RelativePath}", relativePath);
            return Task.FromResult(false);
        }
    }

    public Task<Stream?> GetFileAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPath = Path.Combine(_workspaceManager.GetSessionsPath(), relativePath);
            if (File.Exists(fullPath))
            {
                var stream = File.OpenRead(fullPath);
                return Task.FromResult<Stream?>(stream);
            }
            return Task.FromResult<Stream?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading file {RelativePath}", relativePath);
            return Task.FromResult<Stream?>(null);
        }
    }

    public bool FileExists(string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(_workspaceManager.GetSessionsPath(), relativePath);
            return File.Exists(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking file existence {RelativePath}", relativePath);
            return false;
        }
    }

    public string GetFileUrl(string relativePath)
    {
        return $"/api/files/sessions/{relativePath.Replace('\\', '/')}";
    }
}
