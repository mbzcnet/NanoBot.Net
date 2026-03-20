using System.Globalization;
using Cronos;
using Microsoft.Extensions.AI;
using NanoBot.Core.Cron;

namespace NanoBot.Tools.BuiltIn;

public static class CronTools
{
    public static AITool CreateCronTool(ICronService? cronService, string? defaultChannel = null, string? defaultChatId = null)
    {
        return AIFunctionFactory.Create(
            (string action, string? message, int? everySeconds, string? cronExpr, string? tz, string? at, string? jobId) =>
                ExecuteCronAsync(action, message, everySeconds, cronExpr, tz, at, jobId, cronService, defaultChannel, defaultChatId),
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
        string? tz,
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
                "add" => AddJob(message, everySeconds, cronExpr, tz, at, cronService, defaultChannel, defaultChatId),
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
        string? tz,
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

        if (!string.IsNullOrEmpty(tz) && string.IsNullOrEmpty(cronExpr))
        {
            return Task.FromResult("Error: tz can only be used with cron_expr");
        }

        if (!string.IsNullOrEmpty(tz))
        {
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(tz);
            }
            catch (TimeZoneNotFoundException)
            {
                return Task.FromResult($"Error: Unknown timezone '{tz}'");
            }
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
                CronExpression.Parse(cronExpr, CronFormat.IncludeSeconds);
                schedule = new CronSchedule { Kind = CronScheduleKind.Cron, Expression = cronExpr, TimeZone = tz };
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
            Deliver = true,
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

        var lines = jobs.Select(FormatJobDetails);
        return Task.FromResult("Scheduled jobs:\n" + string.Join("\n", lines));
    }

    private static string FormatJobDetails(CronJob job)
    {
        var timing = FormatTiming(job);
        var state = FormatState(job);
        return $"- {job.Name} (id: {job.Id})\n  {timing}\n  {state}";
    }

    private static string FormatTiming(CronJob job)
    {
        var schedule = job.Schedule;
        return schedule.Kind switch
        {
            CronScheduleKind.Cron => $"cron: {schedule.Expression}" + (string.IsNullOrEmpty(schedule.TimeZone) ? "" : $" ({schedule.TimeZone})"),
            CronScheduleKind.Every => $"every: {(schedule.EveryMs ?? 0) / 1000}s",
            CronScheduleKind.At => $"at: {FormatAtTimestamp(schedule.AtMs)}",
            _ => $"unknown: {schedule.Kind}"
        };
    }

    private static string FormatState(CronJob job)
    {
        var parts = new List<string> { $"enabled={job.Enabled}" };

        if (job.LastRunAt.HasValue)
        {
            parts.Add($"last_run={job.LastRunAt.Value:yyyy-MM-dd HH:mm:ss}");
            parts.Add($"last_status={job.State.LastStatus ?? "unknown"}");
            if (!string.IsNullOrEmpty(job.State.LastError))
            {
                parts.Add($"error={job.State.LastError}");
            }
        }

        if (job.NextRunAt.HasValue)
        {
            parts.Add($"next_run={job.NextRunAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        return string.Join(", ", parts);
    }

    private static string FormatAtTimestamp(long? atMs)
    {
        if (!atMs.HasValue)
            return "N/A";
        return DateTimeOffset.FromUnixTimeMilliseconds(atMs.Value).ToString("yyyy-MM-ddTHH:mm:ssZ");
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
