# 多 LLM Profile 配置功能实现报告

**日期**: 2026-02-28  
**状态**: ✅ 已完成

## 概述

实现了交互式多 LLM Profile 配置功能，支持在 `OnboardCommand` 和 `ConfigCommand` 中管理多个 LLM 配置。

## 实现内容

### 1. 创建共享服务 `LlmProfileConfigService`

**文件**: `src/NanoBot.Cli/Services/LlmProfileConfigService.cs`

提供以下功能：
- `ConfigureProfileInteractiveAsync()` - 交互式配置单个 LLM Profile
- `ManageProfilesInteractiveAsync()` - 交互式管理多个 LLM Profiles
- 支持的操作：
  - [A] 添加新 Profile
  - [E] 编辑现有 Profile
  - [D] 删除 Profile
  - [S] 设置默认 Profile
  - [Q] 保存并退出

### 2. 增强 `ConfigCommand`

**文件**: `src/NanoBot.Cli/Commands/ConfigCommand.cs`

新增功能：
- 添加 `--interactive` / `-i` 选项，启动交互式 LLM Profile 管理界面
- 保留原有的命令行配置方式（`--set`, `--get`, `--list`）

**使用方式**：

```bash
# 交互式管理 LLM Profiles
nbot config --interactive
nbot config -i

# 命令行方式配置（原有功能保留）
nbot config --set llm.profiles.gpt4.provider=openai
nbot config --set llm.profiles.gpt4.model=gpt-4
nbot config --set llm.profiles.claude.provider=anthropic

# 列出所有配置
nbot config --list
```

### 3. 重构 `OnboardCommand`

**文件**: `src/NanoBot.Cli/Commands/OnboardCommand.cs`

改进：
- 使用 `LlmProfileConfigService` 替代原有的重复代码
- 支持配置多个 LLM Profiles
- 在初始化时可以创建默认 Profile，并可选择添加更多 Profiles
- 支持设置默认 Profile

**交互流程**：
1. 首次运行时创建默认 Profile
2. 询问是否配置额外的 Profiles
3. 如果有多个 Profiles，可以选择默认 Profile

## 代码复用

通过创建 `LlmProfileConfigService`，实现了以下代码复用：
- `PromptProviderAsync()` - Provider 选择逻辑
- `PromptApiKeyAsync()` - API Key 输入逻辑
- `ConfigureProfileInteractiveAsync()` - Profile 配置逻辑
- `MaskApiKey()`, `MaskApiUrl()`, `ReadLineMasked()` - 辅助方法

**消除重复代码**：
- OnboardCommand 删除了约 100 行重复代码
- ConfigCommand 新增交互式功能，复用了 OnboardCommand 的逻辑

## 配置示例

### 命令行方式配置多个 Profiles

```bash
# 配置 GPT-4 Profile
nbot config --set llm.profiles.gpt4.provider=openai
nbot config --set llm.profiles.gpt4.model=gpt-4
nbot config --set llm.profiles.gpt4.apikey=sk-xxx

# 配置 Claude Profile
nbot config --set llm.profiles.claude.provider=anthropic
nbot config --set llm.profiles.claude.model=claude-3-5-sonnet-20241022
nbot config --set llm.profiles.claude.apikey=sk-ant-xxx

# 配置 DeepSeek Profile
nbot config --set llm.profiles.deepseek.provider=deepseek
nbot config --set llm.profiles.deepseek.model=deepseek-chat
nbot config --set llm.profiles.deepseek.apikey=sk-xxx

# 设置默认 Profile
nbot config --set llm.defaultprofile=gpt4
```

### 交互式方式配置

```bash
# 启动交互式配置
nbot config -i

# 或在 onboard 时配置
nbot onboard
```

## 配置文件格式

```json
{
  "name": "NanoBot",
  "llm": {
    "defaultProfile": "gpt4",
    "profiles": {
      "gpt4": {
        "name": "gpt4",
        "provider": "openai",
        "model": "gpt-4",
        "apiKey": "sk-xxx",
        "apiBase": "https://api.openai.com/v1",
        "temperature": 0.7,
        "maxTokens": 4096
      },
      "claude": {
        "name": "claude",
        "provider": "anthropic",
        "model": "claude-3-5-sonnet-20241022",
        "apiKey": "sk-ant-xxx",
        "apiBase": "https://api.anthropic.com/v1",
        "temperature": 0.7,
        "maxTokens": 4096
      },
      "deepseek": {
        "name": "deepseek",
        "provider": "deepseek",
        "model": "deepseek-chat",
        "apiKey": "sk-xxx",
        "apiBase": "https://api.deepseek.com/v1",
        "temperature": 0.7,
        "maxTokens": 4096
      }
    }
  },
  "workspace": {
    "path": ".nbot"
  }
}
```

## 测试验证

- ✅ 编译成功，无错误
- ✅ 代码复用，消除重复
- ✅ 保持向后兼容（原有命令行配置方式仍然可用）
- ✅ 支持交互式和命令行两种配置方式

## 后续建议

1. 添加单元测试覆盖 `LlmProfileConfigService`
2. 在文档中说明多 Profile 的使用场景
3. 考虑添加 Profile 切换命令（如 `nbot config --use-profile <name>`）
