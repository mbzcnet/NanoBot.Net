using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Configuration;
using NanoBot.Core.Cron;
using NanoBot.Infrastructure.Extensions;

namespace NanoBot.Cli.Commands;

public class CronCommand : ICliCommand
{
    public string Name => "cron";
    public string Description => "Scheduled task management";

    public Command CreateCommand()
    {
        var listCommand = new Command("list", "List scheduled jobs");
        var allOption = new Option<bool>(
            name: "--all",
            description: "Include disabled jobs",
            getDefaultValue: () => false
        );
        allOption.AddAlias("-a");
        listCommand.Add(allOption);
        listCommand.SetHandler(async (context) =>
        {
            var all = context.ParseResult.GetValueForOption(allOption);
            var cancellationToken = context.GetCancellationToken();
            await ListJobsAsync(all, cancellationToken);
        });

        var addCommand = new Command("add", "Add a scheduled job");
        var nameOption = new Option<string>(
            name: "--name",
            description: "Job name"
        );
        nameOption.AddAlias("-n");
        nameOption.IsRequired = true;

        var messageOption = new Option<string>(
            name: "--message",
            description: "Message for agent"
        );
        messageOption.AddAlias("-m");
        messageOption.IsRequired = true;

        var everyOption = new Option<int?>(
            name: "--every",
            description: "Run every N seconds"
        );
        everyOption.AddAlias("-e");

        var cronExprOption = new Option<string?>(
            name: "--cron",
            description: "Cron expression (e.g. '0 9 * * *')"
        );
        cronExprOption.AddAlias("-c");

        var tzOption = new Option<string?>(
            name: "--tz",
            description: "IANA timezone for cron (e.g. 'America/Vancouver')"
        );

        var atOption = new Option<string?>(
            name: "--at",
            description: "Run once at time (ISO format)"
        );

        var deliverOption = new Option<bool>(
            name: "--deliver",
            description: "Deliver response to channel",
            getDefaultValue: () => false
        );
        deliverOption.AddAlias("-d");

        var toOption = new Option<string?>(
            name: "--to",
            description: "Recipient for delivery"
        );

        var channelOption = new Option<string?>(
            name: "--channel",
            description: "Channel for delivery (e.g. 'telegram', 'whatsapp')"
        );

        addCommand.Add(nameOption);
        addCommand.Add(messageOption);
        addCommand.Add(everyOption);
        addCommand.Add(cronExprOption);
        addCommand.Add(tzOption);
        addCommand.Add(atOption);
        addCommand.Add(deliverOption);
        addCommand.Add(toOption);
        addCommand.Add(channelOption);

        addCommand.SetHandler(async (context) =>
        {
            var name = context.ParseResult.GetValueForOption(nameOption)!;
            var message = context.ParseResult.GetValueForOption(messageOption)!;
            var every = context.ParseResult.GetValueForOption(everyOption);
            var cronExpr = context.ParseResult.GetValueForOption(cronExprOption);
            var tz = context.ParseResult.GetValueForOption(tzOption);
            var at = context.ParseResult.GetValueForOption(atOption);
            var deliver = context.ParseResult.GetValueForOption(deliverOption);
            var to = context.ParseResult.GetValueForOption(toOption);
            var channel = context.ParseResult.GetValueForOption(channelOption);
            var cancellationToken = context.GetCancellationToken();
            await AddJobAsync(name, message, every, cronExpr, tz, at, deliver, to, channel, cancellationToken);
        });

        var removeCommand = new Command("remove", "Remove a scheduled job");
        var jobIdArg = new Argument<string>("job-id", "Job ID to remove");
        removeCommand.Add(jobIdArg);
        removeCommand.SetHandler(async (context) =>
        {
            var jobId = context.ParseResult.GetValueForArgument(jobIdArg);
            var cancellationToken = context.GetCancellationToken();
            await RemoveJobAsync(jobId, cancellationToken);
        });

        var enableCommand = new Command("enable", "Enable a job");
        var enableJobIdArg = new Argument<string>("job-id", "Job ID");
        var disableOption = new Option<bool>(
            name: "--disable",
            description: "Disable instead of enable",
            getDefaultValue: () => false
        );
        enableCommand.Add(enableJobIdArg);
        enableCommand.Add(disableOption);
        enableCommand.SetHandler(async (context) =>
        {
            var jobId = context.ParseResult.GetValueForArgument(enableJobIdArg);
            var disable = context.ParseResult.GetValueForOption(disableOption);
            var cancellationToken = context.GetCancellationToken();
            await EnableJobAsync(jobId, !disable, cancellationToken);
        });

        var runCommand = new Command("run", "Manually run a job");
        var runJobIdArg = new Argument<string>("job-id", "Job ID to run");
        var forceOption = new Option<bool>(
            name: "--force",
            description: "Run even if disabled",
            getDefaultValue: () => false
        );
        forceOption.AddAlias("-f");
        runCommand.Add(runJobIdArg);
        runCommand.Add(forceOption);
        runCommand.SetHandler(async (context) =>
        {
            var jobId = context.ParseResult.GetValueForArgument(runJobIdArg);
            var cancellationToken = context.GetCancellationToken();
            await RunJobAsync(jobId, cancellationToken);
        });

        var command = new Command(Name, Description);
        command.AddCommand(listCommand);
        command.AddCommand(addCommand);
        command.AddCommand(removeCommand);
        command.AddCommand(enableCommand);
        command.AddCommand(runCommand);

        return command;
    }

    private static async Task ListJobsAsync(bool includeDisabled, CancellationToken cancellationToken)
    {
        var cronService = CreateCronService();

        var jobs = cronService.ListJobs(includeDisabled);

        if (jobs.Count == 0)
        {
            Console.WriteLine("No scheduled jobs.");
            return;
        }

        Console.WriteLine("Scheduled Jobs:\n");
        Console.WriteLine($"{"ID",-36} {"Name",-20} {"Schedule",-25} {"Status",-10} {"Next Run"}");
        Console.WriteLine(new string('-', 110));

        foreach (var job in jobs)
        {
            var schedule = FormatSchedule(job.Schedule);
            var status = job.Enabled ? "enabled" : "disabled";
            var nextRun = job.State?.NextRunAtMs != null
                ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).LocalDateTime.ToString("yyyy-MM-dd HH:mm")
                : "-";

            Console.WriteLine($"{job.Id,-36} {job.Name,-20} {schedule,-25} {status,-10} {nextRun}");
        }

        Console.WriteLine($"\nTotal: {jobs.Count} job(s)");
    }

    private static Task AddJobAsync(
        string name,
        string message,
        int? every,
        string? cronExpr,
        string? tz,
        string? at,
        bool deliver,
        string? to,
        string? channel,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(tz) && string.IsNullOrEmpty(cronExpr))
        {
            Console.WriteLine("Error: --tz can only be used with --cron");
            return Task.CompletedTask;
        }

        CronSchedule schedule;

        if (every.HasValue)
        {
            schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = every.Value * 1000 };
        }
        else if (!string.IsNullOrEmpty(cronExpr))
        {
            schedule = new CronSchedule { Kind = CronScheduleKind.Cron, Expression = cronExpr, TimeZone = tz };
        }
        else if (!string.IsNullOrEmpty(at))
        {
            var dt = DateTime.Parse(at);
            schedule = new CronSchedule { Kind = CronScheduleKind.At, AtMs = new DateTimeOffset(dt).ToUnixTimeMilliseconds() };
        }
        else
        {
            Console.WriteLine("Error: Must specify --every, --cron, or --at");
            return Task.CompletedTask;
        }

        var cronService = CreateCronService();

        var definition = new CronJobDefinition
        {
            Name = name,
            Schedule = schedule,
            Message = message,
            Deliver = deliver,
            TargetUserId = to,
            ChannelId = channel
        };

        var job = cronService.AddJob(definition);

        Console.WriteLine($"✓ Added job '{job.Name}' ({job.Id})");
        return Task.CompletedTask;
    }

    private static Task RemoveJobAsync(string jobId, CancellationToken cancellationToken)
    {
        var cronService = CreateCronService();

        if (cronService.RemoveJob(jobId))
        {
            Console.WriteLine($"✓ Removed job {jobId}");
        }
        else
        {
            Console.WriteLine($"Job {jobId} not found");
        }

        return Task.CompletedTask;
    }

    private static Task EnableJobAsync(string jobId, bool enabled, CancellationToken cancellationToken)
    {
        var cronService = CreateCronService();

        var job = cronService.EnableJob(jobId, enabled);
        if (job != null)
        {
            var status = enabled ? "enabled" : "disabled";
            Console.WriteLine($"✓ Job '{job.Name}' {status}");
        }
        else
        {
            Console.WriteLine($"Job {jobId} not found");
        }

        return Task.CompletedTask;
    }

    private static async Task RunJobAsync(string jobId, CancellationToken cancellationToken)
    {
        var cronService = CreateCronService();

        var success = await cronService.RunJobAsync(jobId, cancellationToken);

        if (success)
        {
            Console.WriteLine($"✓ Job executed");
        }
        else
        {
            Console.WriteLine($"Failed to run job {jobId}");
        }
    }

    private static ICronService CreateCronService()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var storePath = Path.Combine(homeDir, ".nbot", "cron", "jobs.json");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCronServices(storePath);
        var serviceProvider = services.BuildServiceProvider();

        return serviceProvider.GetRequiredService<ICronService>();
    }

    private static string FormatSchedule(CronSchedule schedule)
    {
        return schedule.Kind switch
        {
            CronScheduleKind.Every => $"every {(schedule.EveryMs ?? 0) / 1000}s",
            CronScheduleKind.Cron => !string.IsNullOrEmpty(schedule.TimeZone)
                ? $"{schedule.Expression} ({schedule.TimeZone})"
                : schedule.Expression ?? "",
            CronScheduleKind.At => "one-time",
            _ => schedule.Kind.ToString()
        };
    }
}
