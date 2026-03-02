namespace NanoBot.Core.Storage;

public interface IFileStorageService
{
    Task<string> SaveFileAsync(Stream sourceStream, string sessionId, string? extension = null, CancellationToken cancellationToken = default);
    
    Task<bool> DeleteFileAsync(string relativePath, CancellationToken cancellationToken = default);
    
    Task<Stream?> GetFileAsync(string relativePath, CancellationToken cancellationToken = default);
    
    bool FileExists(string relativePath);
    
    string GetFileUrl(string relativePath);
}
