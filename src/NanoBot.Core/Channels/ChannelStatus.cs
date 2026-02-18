namespace NanoBot.Core.Channels;

public record ChannelStatus
{
    public bool Enabled { get; init; }
    public bool Running { get; init; }
}
