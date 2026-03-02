using Microsoft.AspNetCore.Mvc;
using NanoBot.Core.Storage;
using NanoBot.Core.Workspace;

namespace NanoBot.WebUI.Controllers;

[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly IFileStorageService _fileStorageService;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger<FilesController> _logger;

    public FilesController(
        IFileStorageService fileStorageService,
        IWorkspaceManager workspaceManager,
        ILogger<FilesController> logger)
    {
        _fileStorageService = fileStorageService;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    [HttpGet("sessions/{**relativePath}")]
    public async Task<IActionResult> GetSessionFile(string relativePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return BadRequest("File path is required");
            }

            var stream = await _fileStorageService.GetFileAsync(relativePath);
            if (stream == null)
            {
                return NotFound();
            }

            var contentType = GetContentType(relativePath);
            return File(stream, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving file {RelativePath}", relativePath);
            return StatusCode(500, "Internal server error");
        }
    }

    private static string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
