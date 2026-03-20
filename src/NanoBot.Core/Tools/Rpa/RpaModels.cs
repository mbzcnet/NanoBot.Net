namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// RPA 操作类型枚举
/// </summary>
public enum RpaActionType
{
    /// <summary>移动鼠标</summary>
    Move,

    /// <summary>点击（默认左键）</summary>
    Click,

    /// <summary>双击</summary>
    DoubleClick,

    /// <summary>右键点击</summary>
    RightClick,

    /// <summary>拖拽</summary>
    Drag,

    /// <summary>输入文本</summary>
    Type,

    /// <summary>按键</summary>
    Press,

    /// <summary>组合键</summary>
    Hotkey,

    /// <summary>等待</summary>
    Wait,

    /// <summary>截图（用于 OmniParser 分析）</summary>
    Screenshot,

    /// <summary>滚轮滚动</summary>
    Scroll
}

/// <summary>
/// 鼠标按钮枚举
/// </summary>
public enum RpaMouseButton
{
    Left,
    Middle,
    Right
}

/// <summary>
/// RPA 操作基类
/// </summary>
public abstract class RpaAction
{
    /// <summary>操作类型</summary>
    public required RpaActionType Type { get; init; }

    /// <summary>执行后延迟（毫秒）</summary>
    public int? DelayAfterMs { get; init; }
}

/// <summary>
/// 移动操作
/// </summary>
public class RpaMoveAction : RpaAction
{
    public required int X { get; init; }
    public required int Y { get; init; }

    /// <summary>移动动画时长（毫秒），null 表示瞬移</summary>
    public int? DurationMs { get; init; }
}

/// <summary>
/// 点击操作
/// </summary>
public class RpaClickAction : RpaAction
{
    /// <summary>X 坐标，null 表示当前鼠标位置</summary>
    public int? X { get; init; }

    /// <summary>Y 坐标，null 表示当前鼠标位置</summary>
    public int? Y { get; init; }

    /// <summary>鼠标按钮</summary>
    public RpaMouseButton Button { get; init; } = RpaMouseButton.Left;
}

/// <summary>
/// 双击操作
/// </summary>
public class RpaDoubleClickAction : RpaAction
{
    public int? X { get; init; }
    public int? Y { get; init; }
    public RpaMouseButton Button { get; init; } = RpaMouseButton.Left;
}

/// <summary>
/// 右键点击操作
/// </summary>
public class RpaRightClickAction : RpaAction
{
    public int? X { get; init; }
    public int? Y { get; init; }
}

/// <summary>
/// 文本输入操作
/// </summary>
public class RpaTypeAction : RpaAction
{
    /// <summary>输入的文本，支持 UTF-16/emoji</summary>
    public required string Text { get; init; }
}

/// <summary>
/// 按键操作
/// </summary>
public class RpaPressAction : RpaAction
{
    /// <summary>按键码</summary>
    public required string Key { get; init; }
}

/// <summary>
/// 组合键操作
/// </summary>
public class RpaHotkeyAction : RpaAction
{
    /// <summary>组合键列表</summary>
    public required string[] Keys { get; init; }
}

/// <summary>
/// 等待操作
/// </summary>
public class RpaWaitAction : RpaAction
{
    /// <summary>等待时长（毫秒）</summary>
    public required int DurationMs { get; init; }
}

/// <summary>
/// 截图操作
/// </summary>
public class RpaScreenshotAction : RpaAction
{
    /// <summary>输出引用名，用于后续引用解析结果</summary>
    public required string OutputRef { get; init; }
}

/// <summary>
/// 拖拽操作
/// </summary>
public class RpaDragAction : RpaAction
{
    public required int FromX { get; init; }
    public required int FromY { get; init; }
    public required int ToX { get; init; }
    public required int ToY { get; init; }

    /// <summary>拖拽动画时长（毫秒）</summary>
    public int? DurationMs { get; init; }
}

/// <summary>
/// 滚轮滚动操作
/// </summary>
public class RpaScrollAction : RpaAction
{
    /// <summary>水平滚动量</summary>
    public int DeltaX { get; init; }

    /// <summary>垂直滚动量</summary>
    public int DeltaY { get; init; }
}

/// <summary>
/// RPA 操作流程请求
/// </summary>
public class RpaFlowRequest
{
    /// <summary>操作流程数组</summary>
    public required RpaAction[] Flows { get; init; }

    /// <summary>是否启用 OmniParser 分析</summary>
    public bool EnableVision { get; init; }

    /// <summary>截图保存路径（调试用）</summary>
    public string? ScreenshotPath { get; init; }
}

/// <summary>
/// RPA 操作流程结果
/// </summary>
public record RpaFlowResult
{
    /// <summary>是否全部成功</summary>
    public bool Success { get; init; }

    /// <summary>错误信息</summary>
    public string? Error { get; init; }

    /// <summary>已完成的步骤数</summary>
    public int CompletedSteps { get; init; }

    /// <summary>Vision 解析结果，key 为引用名</summary>
    public Dictionary<string, OmniParserResult>? VisionResults { get; init; }
}

/// <summary>
/// OmniParser 解析结果
/// </summary>
public class OmniParserResult
{
    /// <summary>带标注的图像（Base64 编码）</summary>
    public string? AnnotatedImage { get; init; }

    /// <summary>解析出的元素列表</summary>
    public List<OmniParserElement> ParsedContent { get; init; } = [];
}

/// <summary>
/// OmniParser 解析出的元素
/// </summary>
public class OmniParserElement
{
    /// <summary>边界框 [x1, y1, x2, y2]</summary>
    public required int[] Bbox { get; init; }

    /// <summary>标签描述</summary>
    public required string Label { get; init; }

    /// <summary>元素类型（input, button, icon）</summary>
    public required string Type { get; init; }

    /// <summary>文本内容（如输入框的占位符文本）</summary>
    public string? Text { get; init; }

    /// <summary>置信度</summary>
    public double Confidence { get; init; }
}
