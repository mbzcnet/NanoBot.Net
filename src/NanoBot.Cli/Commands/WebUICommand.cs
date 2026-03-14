using System.CommandLine;
using System.Diagnostics;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Cli.Extensions;
using NanoBot.Cli.Services;
using NanoBot.Core.Configuration;
using NanoBot.Core.Configuration.Validators;

namespace NanoBot.Cli.Commands;

public class WebUICommand : ICliCommand
{
    public string Name => "web";
    public string Description => "Start WebUI server";

    public Command CreateCommand()
    {
        var portOption = new Option<int>(
            name: "--port",
            description: "Port to listen on",
            getDefaultValue: () => 18888
        );
        portOption.AddAlias("-p");

        var configOption = new Option<string?>(
            name: "--config",
            description: "Configuration file path"
        );
        configOption.AddAlias("-c");

        var noBrowserOption = new Option<bool>(
            name: "--no-browser",
            description: "Do not open browser automatically",
            getDefaultValue: () => false
        );

        var command = new Command(Name, Description)
        {
            portOption,
            configOption,
            noBrowserOption
        };

        command.SetHandler(async (context) =>
        {
            var port = context.ParseResult.GetValueForOption(portOption);
            var configPath = context.ParseResult.GetValueForOption(configOption);
            var noBrowser = context.ParseResult.GetValueForOption(noBrowserOption);
            var cancellationToken = context.GetCancellationToken();
            
            await ExecuteWebUIAsync(port, configPath, noBrowser, cancellationToken);
        });

        return command;
    }

    private async Task ExecuteWebUIAsync(
        int port,
        string? configPath,
        bool noBrowser,
        CancellationToken cancellationToken)
    {
        // 检查配置文件
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var actualConfigPath = configPath ?? Path.Combine(homeDir, ".nbot", "config.json");

        if (!File.Exists(actualConfigPath))
        {
            Console.WriteLine();
            Console.WriteLine("⚠️  Configuration file not found!");
            Console.WriteLine($"   Expected: {actualConfigPath}");
            Console.WriteLine();
            Console.WriteLine("Please run 'nbot onboard' first to create your configuration.");
            return;
        }

        // 加载配置
        AgentConfig? agentConfig;
        try
        {
            agentConfig = await ConfigurationLoader.LoadAsync(actualConfigPath, cancellationToken);
            if (agentConfig == null)
            {
                Console.WriteLine("❌ Failed to load configuration");
                return;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error loading configuration: {ex.Message}");
            return;
        }

        // 检查 LLM 配置
        var configCheck = await ConfigurationChecker.CheckAsync(actualConfigPath, cancellationToken);
        if (!configCheck.IsReady)
        {
            Console.WriteLine();
            Console.WriteLine("⚠️  LLM configuration is incomplete!");
            Console.WriteLine("   The following fields are missing:");
            foreach (var field in configCheck.MissingFields)
            {
                Console.WriteLine($"     • {field}");
            }
            Console.WriteLine();
            Console.WriteLine("Please run 'nbot onboard' to configure your LLM provider.");
            return;
        }

        // 验证 WebUI 配置
        var validationResult = WebUIConfigValidator.Validate(agentConfig.WebUI);
        if (!validationResult.IsValid)
        {
            Console.WriteLine("❌ WebUI配置验证失败:");
            Console.WriteLine(validationResult.GetSummary());
            return;
        }

        // 显示警告
        if (validationResult.Warnings.Count > 0)
        {
            Console.WriteLine("⚠️  WebUI配置警告:");
            Console.WriteLine(validationResult.GetSummary());
            Console.WriteLine();
        }

        // 确定最终端口（CLI参数优先）
        var finalPort = port != 18888 ? port : agentConfig.WebUI.Server.Port;
        var finalUrls = port != 18888 ? $"http://localhost:{finalPort}" : agentConfig.WebUI.Server.GetResolvedUrls();

        Console.WriteLine($"🐈 Starting NanoBot WebUI on port {finalPort}...");
        Console.WriteLine($"✓ Configuration loaded from: {actualConfigPath}");

        // 启动 WebUI
        var (webuiPath, source) = ResolveWebUIPath();
        if (string.IsNullOrEmpty(webuiPath) || !Directory.Exists(webuiPath))
        {
            Console.WriteLine();
            Console.WriteLine("⚠️  WebUI project not found!");
            Console.WriteLine("   Please ensure NanoBot.WebUI is built and available.");
            return;
        }

        Console.WriteLine($"✓ WebUI {(source == WebUISource.Packaged ? "bundle" : "project")} found: {webuiPath}");
        Console.WriteLine();
        Console.WriteLine($"Starting server on {finalUrls}");
        Console.WriteLine("Press Ctrl+C to stop the server");
        Console.WriteLine();

        var startInfo = CreateStartInfo(webuiPath, finalPort, finalUrls, source, actualConfigPath);

        using var process = new Process { StartInfo = startInfo };
        
        var serverStarted = false;
        var startupTimeout = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;
        var outputBuffer = new List<string>();
        
        // 设置输出处理来检测启动完成
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Console.WriteLine(e.Data);
                outputBuffer.Add(e.Data);
                // 检测ASP.NET Core启动完成的标志
                if (e.Data.Contains("Now listening on:") || 
                    e.Data.Contains("Application started.") ||
                    e.Data.Contains("Hosting environment:"))
                {
                    serverStarted = true;
                }
                // 检测编译完成的标志
                if (e.Data.Contains("正在生成...") || e.Data.Contains("Building..."))
                {
                    // 编译开始，重置启动状态
                    serverStarted = false;
                }
            }
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.Error.WriteLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        // 等待服务器启动完成
        var compilationCompleted = false;
        while (!serverStarted && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(500, cancellationToken);
            
            // 检查是否超时
            if (DateTime.UtcNow - startTime > startupTimeout)
            {
                Console.WriteLine("⚠️  服务器启动超时，但继续等待浏览器打开...");
                break;
            }
            
            // 检查编译是否完成
            if (!compilationCompleted && outputBuffer.Any(line => 
                line.Contains("info: Microsoft.Hosting.Lifetime[14]") ||
                line.Contains("Now listening on:") ||
                line.Contains("Application started.")))
            {
                compilationCompleted = true;
            }
            
            // 只有在编译完成后才认为服务器真正启动
            if (compilationCompleted && 
                (outputBuffer.Any(line => line.Contains("Now listening on:")) ||
                 DateTime.UtcNow - startTime > TimeSpan.FromSeconds(10)))
            {
                serverStarted = true;
            }
        }

        // 等待服务器实际可访问后再打开浏览器
        if (!noBrowser && serverStarted)
        {
            var url = finalUrls.Split(';', StringSplitOptions.RemoveEmptyEntries).First();
            await WaitForServerReadyAsync(url, cancellationToken);
        }

        // 打开浏览器
        if (!noBrowser)
        {
            try
            {
                var url = finalUrls.Split(';', StringSplitOptions.RemoveEmptyEntries).First();
                Console.WriteLine($"Opening browser at {url}...");
                OpenBrowser(url);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open browser: {ex.Message}");
            }
        }

        // 等待进程结束或取消
        await process.WaitForExitAsync(cancellationToken);
    }

    private static ProcessStartInfo CreateStartInfo(string webuiPath, int port, string urls, WebUISource source, string configPath)
    {
        if (source == WebUISource.Packaged)
        {
            var executable = GetPackagedWebUIExecutable(webuiPath);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException($"WebUI executable not found at {executable}");
            }

            return new ProcessStartInfo
            {
                FileName = executable,
                Arguments = $"--urls \"{urls}\" --config \"{configPath}\"",
                WorkingDirectory = webuiPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false
            };
        }

        return new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --urls \"{urls}\" --config \"{configPath}\"",
            WorkingDirectory = webuiPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };
    }

    private static (string? Path, WebUISource Source) ResolveWebUIPath()
    {
        var packagedPath = GetPackagedWebUIPath();
        if (!string.IsNullOrEmpty(packagedPath) && Directory.Exists(packagedPath))
        {
            return (packagedPath, WebUISource.Packaged);
        }

        var projectPath = GetWebUIProjectPath();
        if (!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath))
        {
            return (projectPath, WebUISource.SourceProject);
        }

        return (null, WebUISource.None);
    }

    private static string? GetPackagedWebUIPath()
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var packaged = Path.Combine(baseDir, "webui");
            return packaged;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetWebUIProjectPath()
    {
        // 尝试找到 WebUI 项目路径
        var currentDir = Directory.GetCurrentDirectory();

        var directoriesToProbe = new List<string?>
        {
            currentDir,
            Directory.GetParent(currentDir)?.FullName,
            Directory.GetParent(currentDir)?.Parent?.FullName
        };

        foreach (var baseDir in directoriesToProbe)
        {
            if (string.IsNullOrEmpty(baseDir))
                continue;

            var fromSrc = Path.Combine(baseDir, "src", "NanoBot.WebUI");
            if (Directory.Exists(fromSrc))
                return fromSrc;

            var direct = Path.Combine(baseDir, "NanoBot.WebUI");
            if (Directory.Exists(direct))
                return direct;
        }

        return null;
    }

    private static string GetPackagedWebUIExecutable(string webuiPath)
    {
        var executableName = OperatingSystem.IsWindows() ? "NanoBot.WebUI.exe" : "NanoBot.WebUI";
        return Path.Combine(webuiPath, executableName);
    }

    private enum WebUISource
    {
        None,
        Packaged,
        SourceProject
    }

    private static async Task WaitForServerReadyAsync(string url, CancellationToken cancellationToken)
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(2);

        var maxRetries = 20;
        var delay = TimeSpan.FromMilliseconds(500);

        for (int i = 0; i < maxRetries; i++)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Redirect)
                {
                    // 服务器已准备好
                    return;
                }
            }
            catch
            {
                // 忽略连接错误，继续重试
            }

            if (i < maxRetries - 1)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        // 如果超时，记录警告但继续执行（浏览器可能仍能打开）
        Console.WriteLine("⚠️  等待服务器就绪超时，继续打开浏览器...");
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // 忽略浏览器打开失败
        }
    }
}
