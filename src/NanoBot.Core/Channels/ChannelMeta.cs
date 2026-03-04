namespace NanoBot.Core.Channels;

public class ChannelMeta
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SelectionLabel { get; set; } = string.Empty;
    public string DetailLabel { get; set; } = string.Empty;
    public string DocsPath { get; set; } = string.Empty;
    public string Blurb { get; set; } = string.Empty;
    public string SystemImage { get; set; } = string.Empty;
    public string? DocsLabel { get; set; }
    public string[]? Aliases { get; set; }
    public int? Order { get; set; }
}

public class ChannelUiMetaEntry
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DetailLabel { get; set; } = string.Empty;
    public string? SystemImage { get; set; }
}

public class ChannelUiCatalog
{
    public List<ChannelUiMetaEntry> Entries { get; set; } = new();
    public List<string> Order { get; set; } = new();
    public Dictionary<string, string> Labels { get; set; } = new();
    public Dictionary<string, string> DetailLabels { get; set; } = new();
    public Dictionary<string, string> SystemImages { get; set; } = new();
    public Dictionary<string, ChannelUiMetaEntry> ById { get; set; } = new();
}
