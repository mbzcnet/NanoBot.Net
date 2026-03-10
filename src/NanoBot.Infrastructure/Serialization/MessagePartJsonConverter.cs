using System.Text.Json;
using System.Text.Json.Serialization;
using NanoBot.Core.Messages;

namespace NanoBot.Infrastructure.Serialization;

/// <summary>
/// MessagePart JSON 转换器 - 支持多态序列化
/// </summary>
public class MessagePartJsonConverter : JsonConverter<MessagePart>
{
    public override MessagePart? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeProperty))
        {
            throw new JsonException("Missing 'type' property in MessagePart JSON");
        }

        var type = typeProperty.GetString();
        var json = root.GetRawText();

        return type?.ToLowerInvariant() switch
        {
            "text" => JsonSerializer.Deserialize<TextPart>(json, options),
            "tool" => JsonSerializer.Deserialize<ToolPart>(json, options),
            "reasoning" => JsonSerializer.Deserialize<ReasoningPart>(json, options),
            "file" => JsonSerializer.Deserialize<FilePart>(json, options),
            _ => throw new NotSupportedException($"Unknown part type: {type}")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        MessagePart value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

/// <summary>
/// ToolState JSON 转换器 - 支持多态序列化
/// </summary>
public class ToolStateJsonConverter : JsonConverter<ToolState>
{
    public override ToolState? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        if (!root.TryGetProperty("status", out var statusProperty))
        {
            throw new JsonException("Missing 'status' property in ToolState JSON");
        }

        var status = statusProperty.GetString();
        var json = root.GetRawText();

        return status?.ToLowerInvariant() switch
        {
            "pending" => JsonSerializer.Deserialize<PendingToolState>(json, options),
            "running" => JsonSerializer.Deserialize<RunningToolState>(json, options),
            "completed" => JsonSerializer.Deserialize<CompletedToolState>(json, options),
            "error" => JsonSerializer.Deserialize<ErrorToolState>(json, options),
            _ => throw new NotSupportedException($"Unknown tool status: {status}")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        ToolState value,
        JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
    }
}

/// <summary>
/// JSON 序列化选项扩展
/// </summary>
public static class JsonSerializationExtensions
{
    /// <summary>
    /// 添加 Part 系统序列化支持
    /// </summary>
    public static JsonSerializerOptions AddPartSerialization(this JsonSerializerOptions options)
    {
        options.Converters.Add(new MessagePartJsonConverter());
        options.Converters.Add(new ToolStateJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.WriteIndented = true;
        return options;
    }

    /// <summary>
    /// 获取支持 Part 序列化的默认选项
    /// </summary>
    public static JsonSerializerOptions GetPartSerializationOptions()
    {
        return new JsonSerializerOptions()
            .AddPartSerialization();
    }
}
