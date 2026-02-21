using System.Text.Json;
using System.Text.Json.Nodes;
using Cronos;
using Microsoft.Extensions.Logging;
using NanoBot.Core.Cron;

namespace NanoBot.Infrastructure.Cron;

public class CronService : ICronService, IDisposable
{
    private readonly string _storePath;
    private readonly Func<CronJob, Task<string?>>? _onJobCallback;
    private readonly ILogger<CronService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private List<CronJob> _jobs = new();
    private Timer? _timer;
    private bool _running;
    private bool _disposed;

    public event EventHandler<CronJobEventArgs>? JobExecuted;

    public CronService(
        string storePath,
        ILogger<CronService> logger,
        Func<CronJob, Task<string?>>? onJobCallback = null)
    {
        _storePath = storePath;
        _logger = logger;
        _onJobCallback = onJobCallback;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_running) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_running) return;

            _running = true;
            LoadStore();
            RecomputeNextRuns();
            SaveStore();
            ArmTimer();

            _logger.LogInformation("Cron service started with {JobCount} jobs", _jobs.Count);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_running) return;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!_running) return;

            _running = false;
            _timer?.Dispose();
            _timer = null;

            _logger.LogInformation("Cron service stopped");
        }
        finally
        {
            _lock.Release();
        }
    }

    public CronJob AddJob(CronJobDefinition definition)
    {
        _lock.Wait();
        try
        {
            ValidateSchedule(definition.Schedule);

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var job = new CronJob
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = definition.Name,
                Schedule = definition.Schedule,
                Message = definition.Message,
                Deliver = definition.Deliver,
                ChannelId = definition.ChannelId,
                TargetUserId = definition.TargetUserId,
                DeleteAfterRun = definition.DeleteAfterRun,
                Enabled = true,
                State = new CronJobState
                {
                    NextRunAtMs = ComputeNextRunTime(definition.Schedule, nowMs)
                },
                CreatedAtMs = nowMs,
                UpdatedAtMs = nowMs
            };

            _jobs.Add(job);
            SaveStore();
            ArmTimer();

            _logger.LogInformation("Added cron job '{JobName}' ({JobId})", job.Name, job.Id);
            return job;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void ValidateSchedule(CronSchedule schedule)
    {
        if (schedule.Kind == CronScheduleKind.Cron && !string.IsNullOrEmpty(schedule.TimeZone))
        {
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                throw new ArgumentException($"Invalid timezone: {schedule.TimeZone}");
            }
            catch (InvalidTimeZoneException ex)
            {
                throw new ArgumentException($"Invalid timezone: {schedule.TimeZone}", ex);
            }
        }
    }

    public bool RemoveJob(string jobId)
    {
        _lock.Wait();
        try
        {
            var removed = _jobs.RemoveAll(j => j.Id == jobId) > 0;
            if (removed)
            {
                SaveStore();
                ArmTimer();
                _logger.LogInformation("Removed cron job {JobId}", jobId);
            }
            return removed;
        }
        finally
        {
            _lock.Release();
        }
    }

    public CronJob? EnableJob(string jobId, bool enabled)
    {
        _lock.Wait();
        try
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null) return null;

            job.Enabled = enabled;
            job.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (enabled)
            {
                job.State.NextRunAtMs = ComputeNextRunTime(job.Schedule, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
            else
            {
                job.State.NextRunAtMs = null;
            }

            SaveStore();
            ArmTimer();
            return job;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RunJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        CronJob? job;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job == null || (!job.Enabled && !_running)) return false;
        }
        finally
        {
            _lock.Release();
        }

        await ExecuteJobAsync(job);
        
        _lock.Wait(cancellationToken);
        try
        {
            SaveStore();
            ArmTimer();
        }
        finally
        {
            _lock.Release();
        }

        return true;
    }

    public IReadOnlyList<CronJob> ListJobs(bool includeDisabled = false)
    {
        _lock.Wait();
        try
        {
            var jobs = includeDisabled ? _jobs.ToList() : _jobs.Where(j => j.Enabled).ToList();
            return jobs.OrderBy(j => j.State.NextRunAtMs ?? long.MaxValue).ToList().AsReadOnly();
        }
        finally
        {
            _lock.Release();
        }
    }

    public CronJob? GetJob(string jobId)
    {
        _lock.Wait();
        try
        {
            return _jobs.FirstOrDefault(j => j.Id == jobId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public CronServiceStatus GetStatus()
    {
        _lock.Wait();
        try
        {
            var nextWakeMs = GetNextWakeMs();
            return new CronServiceStatus
            {
                Running = _running,
                TotalJobs = _jobs.Count,
                EnabledJobs = _jobs.Count(j => j.Enabled),
                NextWakeAt = nextWakeMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(nextWakeMs.Value)
                    : null
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
        _lock.Dispose();
    }

    private void LoadStore()
    {
        if (!File.Exists(_storePath))
        {
            _jobs = new List<CronJob>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_storePath);
            var data = JsonNode.Parse(json);
            if (data == null)
            {
                _jobs = new List<CronJob>();
                return;
            }

            var jobsArray = data["jobs"] as JsonArray;
            if (jobsArray == null)
            {
                _jobs = new List<CronJob>();
                return;
            }

            _jobs = jobsArray
                .Select(ParseJob)
                .Where(j => j != null)
                .Cast<CronJob>()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cron store, starting fresh");
            _jobs = new List<CronJob>();
        }
    }

    private static CronJob? ParseJob(JsonNode? node)
    {
        if (node == null) return null;

        try
        {
            var schedule = ParseSchedule(node["schedule"]);
            var state = ParseState(node["state"]);
            var payload = node["payload"];

            return new CronJob
            {
                Id = node["id"]?.GetValue<string>() ?? string.Empty,
                Name = node["name"]?.GetValue<string>() ?? string.Empty,
                Enabled = node["enabled"]?.GetValue<bool>() ?? true,
                Schedule = schedule,
                Message = payload?["message"]?.GetValue<string>() ?? string.Empty,
                Deliver = payload?["deliver"]?.GetValue<bool>() ?? false,
                ChannelId = payload?["channel"]?.GetValue<string>(),
                TargetUserId = payload?["to"]?.GetValue<string>(),
                DeleteAfterRun = node["deleteAfterRun"]?.GetValue<bool>() ?? false,
                State = state,
                CreatedAtMs = node["createdAtMs"]?.GetValue<long>() ?? 0,
                UpdatedAtMs = node["updatedAtMs"]?.GetValue<long>() ?? 0
            };
        }
        catch
        {
            return null;
        }
    }

    private static CronSchedule ParseSchedule(JsonNode? node)
    {
        if (node == null) return new CronSchedule { Kind = CronScheduleKind.Every };

        var kindStr = node["kind"]?.GetValue<string>()?.ToLowerInvariant();
        var kind = kindStr switch
        {
            "at" => CronScheduleKind.At,
            "every" => CronScheduleKind.Every,
            "cron" => CronScheduleKind.Cron,
            _ => CronScheduleKind.Every
        };

        return new CronSchedule
        {
            Kind = kind,
            AtMs = node["atMs"]?.GetValue<long>(),
            EveryMs = node["everyMs"]?.GetValue<long>(),
            Expression = node["expr"]?.GetValue<string>(),
            TimeZone = node["tz"]?.GetValue<string>()
        };
    }

    private static CronJobState ParseState(JsonNode? node)
    {
        return new CronJobState
        {
            NextRunAtMs = node?["nextRunAtMs"]?.GetValue<long>(),
            LastRunAtMs = node?["lastRunAtMs"]?.GetValue<long>(),
            LastStatus = node?["lastStatus"]?.GetValue<string>(),
            LastError = node?["lastError"]?.GetValue<string>()
        };
    }

    private void SaveStore()
    {
        try
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var jobsArray = new JsonArray();
            foreach (var job in _jobs)
            {
                jobsArray.Add(new JsonObject
                {
                    ["id"] = job.Id,
                    ["name"] = job.Name,
                    ["enabled"] = job.Enabled,
                    ["schedule"] = new JsonObject
                    {
                        ["kind"] = job.Schedule.Kind.ToString().ToLowerInvariant(),
                        ["atMs"] = job.Schedule.AtMs,
                        ["everyMs"] = job.Schedule.EveryMs,
                        ["expr"] = job.Schedule.Expression,
                        ["tz"] = job.Schedule.TimeZone
                    },
                    ["payload"] = new JsonObject
                    {
                        ["kind"] = "agent_turn",
                        ["message"] = job.Message,
                        ["deliver"] = job.Deliver,
                        ["channel"] = job.ChannelId,
                        ["to"] = job.TargetUserId
                    },
                    ["state"] = new JsonObject
                    {
                        ["nextRunAtMs"] = job.State.NextRunAtMs,
                        ["lastRunAtMs"] = job.State.LastRunAtMs,
                        ["lastStatus"] = job.State.LastStatus,
                        ["lastError"] = job.State.LastError
                    },
                    ["createdAtMs"] = job.CreatedAtMs,
                    ["updatedAtMs"] = job.UpdatedAtMs,
                    ["deleteAfterRun"] = job.DeleteAfterRun
                });
            }

            var root = new JsonObject
            {
                ["version"] = 1,
                ["jobs"] = jobsArray
            };

            File.WriteAllText(_storePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save cron store");
        }
    }

    private void RecomputeNextRuns()
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var job in _jobs.Where(j => j.Enabled))
        {
            job.State.NextRunAtMs = ComputeNextRunTime(job.Schedule, nowMs);
        }
    }

    private long? ComputeNextRunTime(CronSchedule schedule, long nowMs)
    {
        return schedule.Kind switch
        {
            CronScheduleKind.At => schedule.AtMs > nowMs ? schedule.AtMs : null,
            CronScheduleKind.Every => schedule.EveryMs > 0 ? nowMs + schedule.EveryMs : null,
            CronScheduleKind.Cron => ComputeCronNextRun(schedule.Expression, schedule.TimeZone, nowMs),
            _ => null
        };
    }

    private long? ComputeCronNextRun(string? expression, string? timeZone, long nowMs)
    {
        if (string.IsNullOrEmpty(expression)) return null;

        try
        {
            var cron = CronExpression.Parse(expression);
            var tz = string.IsNullOrEmpty(timeZone)
                ? TimeZoneInfo.Local
                : TimeZoneInfo.FindSystemTimeZoneById(timeZone);

            var now = DateTimeOffset.FromUnixTimeMilliseconds(nowMs).UtcDateTime;
            var next = cron.GetNextOccurrence(now, tz);

            if (!next.HasValue) return null;

            var nextOffset = TimeZoneInfo.ConvertTime(new DateTimeOffset(next.Value, TimeSpan.Zero), tz);
            return nextOffset.ToUnixTimeMilliseconds();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute next run for cron expression: {Expression}", expression);
            return null;
        }
    }

    private long? GetNextWakeMs()
    {
        var times = _jobs
            .Where(j => j.Enabled && j.State.NextRunAtMs.HasValue)
            .Select(j => j.State.NextRunAtMs!.Value)
            .ToList();

        return times.Count > 0 ? times.Min() : null;
    }

    private void ArmTimer()
    {
        _timer?.Dispose();
        _timer = null;

        if (!_running) return;

        var nextWakeMs = GetNextWakeMs();
        if (!nextWakeMs.HasValue) return;

        var delayMs = Math.Max(0, nextWakeMs.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        _timer = new Timer(
            _ => _ = OnTimerTickAsync(),
            null,
            (int)Math.Min(delayMs, int.MaxValue),
            Timeout.Infinite);
    }

    private async Task OnTimerTickAsync()
    {
        if (!_running) return;

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dueJobs = _jobs
            .Where(j => j.Enabled && j.State.NextRunAtMs.HasValue && nowMs >= j.State.NextRunAtMs.Value)
            .ToList();

        foreach (var job in dueJobs)
        {
            await ExecuteJobAsync(job);
        }

        _lock.Wait();
        try
        {
            SaveStore();
            ArmTimer();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task ExecuteJobAsync(CronJob job)
    {
        var startMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _logger.LogInformation("Executing cron job '{JobName}' ({JobId})", job.Name, job.Id);

        string? response = null;
        string? error = null;
        bool success = false;

        try
        {
            if (_onJobCallback != null)
            {
                response = await _onJobCallback(job);
            }

            job.State.LastStatus = "ok";
            job.State.LastError = null;
            success = true;
            _logger.LogInformation("Cron job '{JobName}' completed", job.Name);
        }
        catch (Exception ex)
        {
            job.State.LastStatus = "error";
            job.State.LastError = ex.Message;
            error = ex.Message;
            _logger.LogError(ex, "Cron job '{JobName}' failed", job.Name);
        }

        job.State.LastRunAtMs = startMs;
        job.UpdatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (job.Schedule.Kind == CronScheduleKind.At)
        {
            if (job.DeleteAfterRun)
            {
                _jobs.RemoveAll(j => j.Id == job.Id);
            }
            else
            {
                job.Enabled = false;
                job.State.NextRunAtMs = null;
            }
        }
        else
        {
            job.State.NextRunAtMs = ComputeNextRunTime(job.Schedule, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        JobExecuted?.Invoke(this, new CronJobEventArgs
        {
            Job = job,
            Success = success,
            Response = response,
            Error = error
        });
    }
}
