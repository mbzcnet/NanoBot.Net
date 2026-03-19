namespace NanoBot.Core.Tools.Rpa;

/// <summary>
/// 输入模拟器接口（封装 SharpHook）
/// </summary>
public interface IInputSimulator
{
    /// <summary>
    /// 移动鼠标到指定位置
    /// </summary>
    /// <param name="x">X 坐标</param>
    /// <param name="y">Y 坐标</param>
    /// <param name="durationMs">移动动画时长（毫秒），null 表示瞬移</param>
    Task MoveMouseAsync(int x, int y, int? durationMs = null);

    /// <summary>
    /// 执行鼠标点击
    /// </summary>
    /// <param name="button">鼠标按钮</param>
    Task ClickAsync(RpaMouseButton button = RpaMouseButton.Left);

    /// <summary>
    /// 执行鼠标双击
    /// </summary>
    /// <param name="button">鼠标按钮</param>
    Task DoubleClickAsync(RpaMouseButton button = RpaMouseButton.Left);

    /// <summary>
    /// 执行右键点击
    /// </summary>
    Task RightClickAsync();

    /// <summary>
    /// 执行拖拽操作
    /// </summary>
    /// <param name="fromX">起始 X 坐标</param>
    /// <param name="fromY">起始 Y 坐标</param>
    /// <param name="toX">目标 X 坐标</param>
    /// <param name="toY">目标 Y 坐标</param>
    /// <param name="durationMs">拖拽动画时长（毫秒）</param>
    Task DragAsync(int fromX, int fromY, int toX, int toY, int? durationMs = null);

    /// <summary>
    /// 输入文本
    /// </summary>
    /// <param name="text">文本内容，支持 UTF-16/emoji</param>
    Task TypeTextAsync(string text);

    /// <summary>
    /// 按下指定按键
    /// </summary>
    /// <param name="key">按键码（如 "Enter", "Escape", "A"）</param>
    Task PressKeyAsync(string key);

    /// <summary>
    /// 按下组合键
    /// </summary>
    /// <param name="keys">组合键列表（如 ["Ctrl", "C"]）</param>
    Task PressHotkeyAsync(params string[] keys);

    /// <summary>
    /// 执行滚轮滚动
    /// </summary>
    /// <param name="deltaX">水平滚动量</param>
    /// <param name="deltaY">垂直滚动量</param>
    Task ScrollAsync(int deltaX, int deltaY);

    /// <summary>
    /// 获取当前鼠标位置
    /// </summary>
    (int X, int Y) GetCursorPosition();

    /// <summary>
    /// 获取屏幕尺寸
    /// </summary>
    (int Width, int Height) GetScreenSize();
}
