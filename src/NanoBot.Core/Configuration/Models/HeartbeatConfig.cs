namespace NanoBot.Core.Configuration;

public class HeartbeatConfig
{
    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 300;

    public string? Message { get; set; }
}
