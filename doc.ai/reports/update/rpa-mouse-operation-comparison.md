# RPA 鼠标操作实现方案

**生成日期**: 2026-03-18  
**选定方案**: SharpHook

---

## 1. 选定方案: SharpHook

经过对多个 RPA/鼠标自动化方案的调研，最终选定 **SharpHook** 作为 NanoBot.Net 的鼠标/键盘操作底层库。

### 1.1 选型理由

| 考量因素 | 评估结果 |
|----------|----------|
| **跨平台** | ✅ 支持 Windows、macOS、Linux |
| **.NET 兼容性** | ✅ 支持 .NET 8+、.NET Framework 4.7.2+、.NET Standard 2.0 |
| **功能完整** | ✅ 全局钩子 + 事件模拟 + 文本输入 |
| **活跃度** | ✅ 487 Stars，持续维护 (v7.1.1) |
| **可扩展性** | ✅ 支持 Reactive 和 R3 扩展 |

### 1.2 替代方案排除理由

| 方案 | 排除原因 |
|------|----------|
| **Robot Framework** | 仅支持 Web 浏览器，无法操作桌面应用 |
| **OpenRPA** | 仅支持 Windows，架构过于复杂 |
| **Desktop.Robot** | 功能基础，无全局钩子，多显示器不支持 |
| **FlaUI** | 仅 Windows，无事件模拟能力 |
| **Win32 P/Invoke** | 仅 Windows，跨平台不兼容 |

---

## 2. SharpHook 能力概览

### 2.1 核心能力

- **全局键盘钩子**: 捕获系统级键盘事件
- **全局鼠标钩子**: 捕获系统级鼠标事件
- **鼠标事件模拟**: 移动、点击、拖拽、滚轮
- **键盘事件模拟**: 按键、组合键、热键
- **文本输入模拟**: UTF-16 完整支持（包括 emoji）
- **事件序列**: 批量模拟多个事件

### 2.2 平台支持

| 平台 | 架构支持 |
|------|----------|
| **Windows** | x86, x64, Arm64 |
| **macOS** | x64, Arm64 |
| **Linux** | x64, Arm32, Arm64 |

### 2.3 平台约束

- **Windows**: Windows 10/11
- **macOS**: 10.15+ (需要 Accessibility API 权限)
- **Linux**: X11 (Wayland 暂不支持)

---

## 3. NanoBot.Net 集成规划

### 3.1 工具层设计

NanoBot.Existing 工具系统已有以下能力：
- File, Shell, Web, Message, Cron, Spawn
- MCP 集成

**新增工具**：
- `Mouse` - 鼠标操作
- `Keyboard` - 键盘操作

### 3.2 功能规划

#### Phase 1: 基础能力

- 鼠标移动到坐标
- 鼠标点击（左/中/右）
- 鼠标双击
- 键盘按键输入
- 文本输入

#### Phase 2: 高级能力

- 鼠标拖拽
- 组合键（Ctrl+C, Alt+Tab 等）
- 鼠标滚轮滚动
- 事件序列执行

#### Phase 3: 增强能力

- 键盘/鼠标钩子（监听用户输入）
- 区分模拟事件与真实事件
- 取消操作钩子

### 3.3 配置项

- 超时设置
- 移动速度（动画 vs 瞬移）
- 错误处理策略

---

## 4. 参考资料

- [SharpHook GitHub](https://github.com/TolikPylypchuk/SharpHook)
- [SharpHook 文档](https://sharphook.tolik.io/)

---

*报告生成工具: Claude Code*
