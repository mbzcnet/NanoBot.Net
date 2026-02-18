using System.Reflection;
using Microsoft.Extensions.Logging;

namespace NanoBot.Infrastructure.Resources;

public class EmbeddedResourceLoader : IEmbeddedResourceLoader
{
    private readonly Assembly _assembly;
    private readonly string[] _resourceNames;
    private readonly ILogger<EmbeddedResourceLoader>? _logger;

    public EmbeddedResourceLoader(ILogger<EmbeddedResourceLoader>? logger = null)
    {
        _assembly = typeof(EmbeddedResourceLoader).Assembly;
        _resourceNames = _assembly.GetManifestResourceNames();
        _logger = logger;
    }

    public IReadOnlyList<string> GetWorkspaceResourceNames()
    {
        return _resourceNames
            .Where(n => n.StartsWith("workspace/"))
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<string> GetSkillsResourceNames()
    {
        return _resourceNames
            .Where(n => n.StartsWith("skills/"))
            .ToList()
            .AsReadOnly();
    }

    public async Task<string?> ReadResourceAsync(string resourceName, CancellationToken cancellationToken = default)
    {
        var fullName = _resourceNames.FirstOrDefault(n =>
            n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase) ||
            n.Equals(resourceName, StringComparison.OrdinalIgnoreCase));

        if (fullName == null)
        {
            _logger?.LogDebug("Resource not found: {ResourceName}", resourceName);
            return null;
        }

        using var stream = _assembly.GetManifestResourceStream(fullName);
        if (stream == null)
        {
            _logger?.LogWarning("Failed to get stream for resource: {ResourceName}", fullName);
            return null;
        }

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task ExtractAllResourcesAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        await ExtractWorkspaceResourcesAsync(targetDirectory, cancellationToken);
        await ExtractSkillsResourcesAsync(targetDirectory, cancellationToken);
    }

    public async Task ExtractWorkspaceResourcesAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        var workspaceResources = GetWorkspaceResourceNames();
        _logger?.LogInformation("Extracting {Count} workspace resources to {TargetDirectory}", workspaceResources.Count, targetDirectory);

        foreach (var resourceName in workspaceResources)
        {
            await ExtractResourceAsync(resourceName, targetDirectory, "workspace", cancellationToken);
        }
    }

    public async Task ExtractSkillsResourcesAsync(string targetDirectory, CancellationToken cancellationToken = default)
    {
        var skillsResources = GetSkillsResourceNames();
        _logger?.LogInformation("Extracting {Count} skills resources to {TargetDirectory}", skillsResources.Count, targetDirectory);

        foreach (var resourceName in skillsResources)
        {
            await ExtractResourceAsync(resourceName, targetDirectory, "skills", cancellationToken);
        }
    }

    private async Task ExtractResourceAsync(string resourceName, string targetDirectory, string prefix, CancellationToken cancellationToken)
    {
        var relativePath = ConvertResourceNameToPath(resourceName, prefix);
        var targetPath = Path.Combine(targetDirectory, relativePath);

        if (File.Exists(targetPath))
        {
            _logger?.LogDebug("Skipping existing file: {TargetPath}", targetPath);
            return;
        }

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _logger?.LogWarning("Failed to get stream for resource: {ResourceName}", resourceName);
            return;
        }

        using var fileStream = File.Create(targetPath);
        await stream.CopyToAsync(fileStream, cancellationToken);

        _logger?.LogDebug("Extracted resource: {ResourceName} -> {TargetPath}", resourceName, targetPath);
    }

    private static string ConvertResourceNameToPath(string resourceName, string prefix)
    {
        var name = resourceName;

        if (name.StartsWith(prefix + "/"))
        {
            name = name[(prefix.Length + 1)..];
        }

        return name.Replace('/', Path.DirectorySeparatorChar);
    }

    public IReadOnlyList<string> GetAllResourceNames()
    {
        return _resourceNames.ToList().AsReadOnly();
    }
}
