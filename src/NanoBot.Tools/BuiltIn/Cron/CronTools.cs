using Cronos;
using Microsoft.Extensions.AI;
using NanoBot.Core.Cron;

namespace NanoBot.Tools.BuiltIn;

public static class CronTools
{
    public static AITool CreateCronTool(ICronService? cronService, string? defaultChannel = null, string? defaultChatId = null)
    {
        return AIFunctionFactory.Create(
            (string action, string? message, int? everySeconds, string? cronExpr, string? at, string? jobId) =>
                ExecuteCronAsync(action, message, everySeconds, cronExpr, at, jobId, cronService, defaultChannel, defaultChatId),
            new AIFunctionFactoryOptions
            {
                Name = "cron",
                Description = "Schedule reminders and recurring tasks. Actions: add, list, remove."
            });
    }

    private static Task<string> ExecuteCronAsync(
        string action,
        string? message,
        int? everySeconds,
        string? cronExpr,
        string? at,
        string? jobId,
        ICronService? cronService,
        string? defaultChannel,
        string? defaultChatId)
    {
        try
        {
            if (cronService == null)
            {
                return Task.FromResult("Error: Cron service not available");
            }

            return action.ToLowerInvariant() switch
            {
                "add" => AddJob(message, everySeconds, cronExpr, at, cronService, defaultChannel, defaultChatId),
                "list" => ListJobs(cronService),
                "remove" => RemoveJob(jobId, cronService),
                _ => Task.FromResult($"Error: Unknown action: {action}")
            };
        }
        catch (Exception ex)
        {
            return Task.FromResult($"Error: {ex.Message}");
        }
    }

    private static Task<string> AddJob(
        string? message,
        int? everySeconds,
        string? cronExpr,
        string? at,
        ICronService cronService,
        string? defaultChannel,
        string? defaultChatId)
    {
        if (string.IsNullOrEmpty(message))
        {
            return Task.FromResult("Error: message is required for add");
        }

        if (string.IsNullOrEmpty(defaultChannel) || string.IsNullOrEmpty(defaultChatId))
        {
            return Task.FromResult("Error: No session context (channel/chat_id)");
        }

        CronSchedule schedule;
        bool deleteAfter = false;

        if (everySeconds.HasValue)
        {
            schedule = new CronSchedule { Kind = CronScheduleKind.Every, EveryMs = everySeconds.Value * 1000L };
        }
        else if (!string.IsNullOrEmpty(cronExpr))
        {
            try
            {
                CronExpression.Parse(cronExpr);
                schedule = new CronSchedule { Kind = CronScheduleKind.Cron, Expression = cronExpr };
            }
            catch (CronFormatException)
            {
                return Task.FromResult($"Error: Invalid cron expression: {cronExpr}");
            }
        }
        else if (!string.IsNullOrEmpty(at))
        {
            if (!DateTime.TryParse(at, out var dt))
            {
                return Task.FromResult($"Error: Invalid datetime format: {at}");
            }
            schedule = new CronSchedule { Kind = CronScheduleKind.At, AtMs = new DateTimeOffset(dt).ToUnixTimeMilliseconds() };
            deleteAfter = true;
        }
        else
        {
            return Task.FromResult("Error: Either every_seconds, cron_expr, or at is required");
        }

        var job = cronService.AddJob(new CronJobDefinition
        {
            Name = message.Length > 30 ? message[..30] : message,
            Schedule = schedule,
            Message = message,
            ChannelId = defaultChannel,
            TargetUserId = defaultChatId,
            DeleteAfterRun = deleteAfter
        });

        return Task.FromResult($"Created job '{job.Name}' (id: {job.Id})");
    }

    private static Task<string> ListJobs(ICronService cronService)
    {
        var jobs = cronService.ListJobs();
        if (jobs.Count == 0)
        {
            return Task.FromResult("No scheduled jobs.");
        }

        var lines = jobs.Select(j => $"- {j.Name} (id: {j.Id}, {j.Schedule.Kind})");
        return Task.FromResult("Scheduled jobs:\n" + string.Join("\n", lines));
    }

    private static Task<string> RemoveJob(string? jobId, ICronService cronService)
    {
        if (string.IsNullOrEmpty(jobId))
        {
            return Task.FromResult("Error: job_id is required for remove");
        }

        var removed = cronService.RemoveJob(jobId);
        return Task.FromResult(removed
            ? $"Removed job {jobId}"
            : $"Error: Job {jobId} not found");
    }
}
