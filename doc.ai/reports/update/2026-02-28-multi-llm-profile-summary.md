# 多 LLM Profile 配置功能总结

## 实现完成 ✅

已成功实现交互式多 LLM Profile 配置功能，满足以下需求：

### 1. ConfigCommand 支持命令式多 LLM 配置

**命令行方式**（已有功能保留）：
```bash
# 列出所有 LLM Profiles
nbot config --list

# 配置指定 Profile
nbot config --set llm.profiles.gpt4.provider=openai
nbot config --set llm.profiles.gpt4.model=gpt-4
nbot config --set llm.profiles.gpt4.apikey=sk-xxx

# 设置默认 Profile
nbot config --set llm.defaultprofile=gpt4
```

**交互式方式**（新增功能）：
```bash
# 启动交互式 LLM Profile 管理
nbot config --interactive
nbot config -i
```

交互式界面支持：
- [A] 添加新 Profile
- [E] 编辑现有 Profile  
- [D] 删除 Profile
- [S] 设置默认 Profile
- [Q] 保存并退出

### 2. OnboardCommand 支持多 Profile 配置

重构后的 `nbot onboard` 流程：
1. 创建默认 LLM Profile
2. 询问是否配置额外的 Profiles
3. 如果有多个 Profiles，可选择默认 Profile

### 3. 代码复用 - 无重复造轮子

创建了共享服务 `LlmProfileConfigService`：
- **位置**: `src/NanoBot.Cli/Services/LlmProfileConfigService.cs`
- **复用逻辑**:
  - Provider 选择交互
  - API Key 输入（带掩码）
  - Profile 配置流程
  - Profile 管理操作

**OnboardCommand** 和 **ConfigCommand** 都调用同一个服务，避免了代码重复。

## 文件变更

### 新增文件
- `src/NanoBot.Cli/Services/LlmProfileConfigService.cs` (450+ 行)

### 修改文件
- `src/NanoBot.Cli/Commands/ConfigCommand.cs`
  - 添加 `--interactive` 选项
  - 集成 `LlmProfileConfigService`
  
- `src/NanoBot.Cli/Commands/OnboardCommand.cs`
  - 重构 LLM 配置部分
  - 支持多 Profile 配置
  - 删除重复代码（约 100 行）
  - 使用 `LlmProfileConfigService`

## 使用示例

### 场景 1: 初始化配置多个 LLM

```bash
nbot onboard
# 按提示配置默认 Profile
# 选择是否添加更多 Profiles（如 gpt4, claude, deepseek）
# 设置默认 Profile
```

### 场景 2: 后续管理 Profiles

```bash
# 交互式管理
nbot config -i

# 或命令行方式
nbot config --set llm.profiles.newmodel.provider=openai
nbot config --set llm.profiles.newmodel.model=gpt-4o
```

### 场景 3: 查看所有配置

```bash
nbot config --list
```

输出示例：
```
Configuration: /Users/xxx/.nbot/config.json

Name: NanoBot
Workspace: .nbot

LLM:
  Default Profile: gpt4

  Profile [gpt4]:
    Provider: openai
    Model: gpt-4
    API Key: sk-x...xxx
    API Base: api.openai.com/v1
    Temperature: 0.7
    MaxTokens: 4096

  Profile [claude]:
    Provider: anthropic
    Model: claude-3-5-sonnet-20241022
    API Key: sk-a...xxx
    API Base: api.anthropic.com/v1
    Temperature: 0.7
    MaxTokens: 4096
```

## 技术亮点

1. ✅ **代码复用**: 单一服务类处理所有 LLM Profile 配置逻辑
2. ✅ **向后兼容**: 保留原有命令行配置方式
3. ✅ **用户友好**: 提供交互式和命令行两种配置方式
4. ✅ **编译通过**: 无错误，仅有 2 个无关警告
5. ✅ **遵循规范**: 符合项目架构和命名规范

## 配置文件结构

```json
{
  "llm": {
    "defaultProfile": "gpt4",
    "profiles": {
      "gpt4": { "provider": "openai", "model": "gpt-4", ... },
      "claude": { "provider": "anthropic", "model": "claude-3-5-sonnet-20241022", ... },
      "deepseek": { "provider": "deepseek", "model": "deepseek-chat", ... }
    }
  }
}
```

## 验证结果

- ✅ 编译成功
- ✅ 代码复用实现
- ✅ 功能完整
- ✅ 符合需求
