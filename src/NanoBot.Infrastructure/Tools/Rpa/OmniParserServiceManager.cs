using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Core.Tools.Rpa;

namespace NanoBot.Infrastructure.Tools.Rpa;

/// <summary>
/// OmniParser 服务生命周期管理器
/// </summary>
public class OmniParserServiceManager : IOmniParserClient
{
    private readonly string _installPath;
    private readonly int _port;
    private readonly ILogger<OmniParserServiceManager>? _logger;
    private Process? _serverProcess;
    private OmniParserClient? _client;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public OmniParserServiceManager(
        string installPath,
        int port,
        ILogger<OmniParserServiceManager>? logger = null)
    {
        _installPath = installPath;
        _port = port;
        _logger = logger;
        _client = new OmniParserClient("127.0.0.1", port, null);
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (_client != null)
        {
            return await _client.IsAvailableAsync(ct);
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<OmniParserResult> ParseAsync(byte[] screenshot, CancellationToken ct = default)
    {
        EnsureStarted();
        return await _client!.ParseAsync(screenshot, ct);
    }

    /// <summary>
    /// 启动 OmniParser 服务
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_serverProcess != null && !_serverProcess.HasExited)
            {
                _logger?.LogDebug("OmniParser service already running");
                return;
            }

            var venvPython = GetPythonPath();
            var serverScript = Path.Combine(_installPath, "server.py");

            if (!File.Exists(venvPython))
            {
                throw new FileNotFoundException($"Python not found at {venvPython}");
            }

            if (!File.Exists(serverScript))
            {
                throw new FileNotFoundException($"Server script not found at {serverScript}");
            }

            _logger?.LogInformation("Starting OmniParser service...");

            _serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = venvPython,
                    Arguments = $"\"{serverScript}\"",
                    WorkingDirectory = _installPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _serverProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger?.LogDebug("[OmniParser] {Data}", e.Data);
                }
            };

            _serverProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    _logger?.LogWarning("[OmniParser Error] {Data}", e.Data);
                }
            };

            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            await WaitForServiceReadyAsync(ct);

            _logger?.LogInformation("OmniParser service started successfully on port {Port}", _port);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 停止 OmniParser 服务
    /// </summary>
    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_serverProcess == null || _serverProcess.HasExited)
            {
                _logger?.LogDebug("OmniParser service not running");
                return;
            }

            _logger?.LogInformation("Stopping OmniParser service...");

            try
            {
                _serverProcess.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error killing OmniParser process");
            }

            if (!_serverProcess.WaitForExit(5000))
            {
                _logger?.LogWarning("OmniParser service did not stop within timeout");
            }

            _serverProcess.Dispose();
            _serverProcess = null;

            _logger?.LogInformation("OmniParser service stopped");
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EnsureStarted()
    {
        if (_client == null)
        {
            throw new InvalidOperationException("OmniParser client not initialized");
        }
    }

    private async Task WaitForServiceReadyAsync(CancellationToken ct)
    {
        var maxAttempts = 30;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var response = await httpClient.GetAsync($"http://127.0.0.1:{_port}/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            if (_serverProcess?.HasExited == true)
            {
                throw new InvalidOperationException("OmniParser service crashed during startup");
            }

            attempt++;
            await Task.Delay(1000, ct);
        }

        throw new TimeoutException($"OmniParser service did not become ready within {maxAttempts} seconds");
    }

    private string GetPythonPath()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return Path.Combine(_installPath, "venv", "bin", "python");
        }
        else
        {
            return Path.Combine(_installPath, "venv", "Scripts", "python.exe");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        StopAsync().GetAwaiter().GetResult();
        _client?.Dispose();
        _lock.Dispose();

        _disposed = true;
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

