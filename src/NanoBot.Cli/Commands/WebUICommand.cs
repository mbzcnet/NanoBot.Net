using System.CommandLine;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Cli.Extensions;
using NanoBot.Cli.Services;
using NanoBot.Core.Configuration;

namespace NanoBot.Cli.Commands;

public class WebUICommand : ICliCommand
{
    public string Name => "webui";
    public string Description => "Start WebUI server";

    public Command CreateCommand()
    {
        var portOption = new Option<int>(
            name: "--port",
            description: "Port to listen on",
            getDefaultValue: () => 5000
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
        Console.WriteLine($"🐈 Starting NanoBot WebUI on port {port}...");

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

        Console.WriteLine($"✓ Configuration loaded from: {actualConfigPath}");

        // 启动 WebUI
        var webuiPath = GetWebUIProjectPath();
        if (string.IsNullOrEmpty(webuiPath) || !Directory.Exists(webuiPath))
        {
            Console.WriteLine();
            Console.WriteLine("⚠️  WebUI project not found!");
            Console.WriteLine("   Please ensure NanoBot.WebUI is built and available.");
            return;
        }

        Console.WriteLine($"✓ WebUI project found: {webuiPath}");
        Console.WriteLine();
        Console.WriteLine($"Starting server on http://localhost:{port}");
        Console.WriteLine("Press Ctrl+C to stop the server");
        Console.WriteLine();

        // 启动 dotnet run
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --urls http://localhost:{port}",
            WorkingDirectory = webuiPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = false
        };

        using var process = new Process { StartInfo = startInfo };
        
        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.WriteLine(e.Data);
        };
        
        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                Console.Error.WriteLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 等待服务器启动
        await Task.Delay(3000, cancellationToken);

        // 打开浏览器
        if (!noBrowser)
        {
            try
            {
                var url = $"http://localhost:{port}";
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

    private static string? GetWebUIProjectPath()
    {
        // 尝试找到 WebUI 项目路径
        var currentDir = Directory.GetCurrentDirectory();
        
        // 检查是否在项目根目录
        var webuiPath = Path.Combine(currentDir, "src", "NanoBot.WebUI");
        if (Directory.Exists(webuiPath))
            return webuiPath;
        
        // 检查是否在 src 目录
        webuiPath = Path.Combine(currentDir, "NanoBot.WebUI");
        if (Directory.Exists(webuiPath))
            return webuiPath;
        
        // 检查父目录
        var parentDir = Directory.GetParent(currentDir)?.FullName;
        if (parentDir != null)
        {
            webuiPath = Path.Combine(parentDir, "src", "NanoBot.WebUI");
            if (Directory.Exists(webuiPath))
                return webuiPath;
        }
        
        return null;
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
