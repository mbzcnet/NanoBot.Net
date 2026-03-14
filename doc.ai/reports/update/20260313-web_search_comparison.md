# Web Search 工具实现对比报告

**生成日期**: 2026-03-13
**对比项目**: NanoBot.Net vs OpenCode vs OpenClaw

---

## 1. 概述

| 项目 | 语言 | 搜索提供商 | 认证方式 | 架构特点 |
|------|------|-----------|----------|----------|
| **NanoBot.Net** | C# (.NET 8) | DuckDuckGo | 无需 API Key | 简单直接，内嵌在工具层 |
| **OpenCode** | TypeScript | Exa.ai | 服务端 MCP 代理 | 基于 MCP 协议，权限控制完善 |
| **OpenClaw** | TypeScript | Brave / Perplexity | 需配置 API Key | 多提供商支持，配置灵活 |

---

## 2. 搜索提供商对比

### 2.1 NanoBot.Net
- **提供商**: DuckDuckGo (免费 API)
- **端点**: `https://api.duckduckgo.com/?q={query}&format=json&no_html=1`
- **特点**: 无需 API Key，但功能相对基础
- **限制**: 仅支持基本关键词搜索，无高级过滤选项

### 2.2 OpenCode
- **提供商**: Exa.ai (通过 MCP 协议)
- **端点**: `https://mcp.exa.ai/mcp`
- **特点**: 支持实时爬取 (livecrawl)、深度搜索 (deep)、快速搜索 (fast)
- **优势**: MCP 协议标准化，支持上下文长度配置

### 2.3 OpenClaw
- **提供商**:
  - **Brave Search**: `https://api.search.brave.com/res/v1/web/search`
  - **Perplexity**: `https://api.perplexity.ai` 或 OpenRouter 代理
- **特点**: 双提供商支持，可根据需求切换
- **优势**:
  - Brave: 支持地区、语言、时效性过滤
  - Perplexity: AI 合成答案，自动引用来源

---

## 3. 参数设计对比

### 3.1 参数列表

| 参数 | NanoBot.Net | OpenCode | OpenClaw (Brave) | OpenClaw (Perplexity) |
|------|-------------|----------|------------------|----------------------|
| `query` | ✅ | ✅ | ✅ | ✅ |
| `maxResults` / `count` | ✅ | ✅ (numResults) | ✅ | ✅ |
| `country` | ❌ | ❌ | ✅ | ✅ |
| `search_lang` | ❌ | ❌ | ✅ | ✅ |
| `ui_lang` | ❌ | ❌ | ✅ | ❌ |
| `freshness` | ❌ | ❌ | ✅ | ❌ |
| `livecrawl` | ❌ | ✅ | ❌ | ❌ |
| `type` (fast/deep) | ❌ | ✅ | ❌ | ❌ |
| `contextMaxCharacters` | ❌ | ✅ | ❌ | ❌ |

### 3.2 参数验证

**NanoBot.Net**:
```csharp
// 使用 AIFunctionFactory，无显式参数验证
AIFunctionFactory.Create(
    (string query, int maxResults) => WebSearchAsync(...),
    new AIFunctionFactoryOptions { Name = "web_search", ... }
);
```

**OpenCode**:
```typescript
// 使用 Zod Schema 验证
parameters: z.object({
  query: z.string().describe("Websearch query"),
  numResults: z.number().optional(),
  livecrawl: z.enum(["fallback", "preferred"]).optional(),
  type: z.enum(["auto", "fast", "deep"]).optional(),
})
```

**OpenClaw**:
```typescript
// 使用 Typebox Schema，功能最丰富
const WebSearchSchema = Type.Object({
  query: Type.String({ description: "..." }),
  count: Type.Optional(Type.Number({ minimum: 1, maximum: 10 })),
  country: Type.Optional(Type.String()),
  search_lang: Type.Optional(Type.String()),
  freshness: Type.Optional(Type.String()), // pd/pw/pm/py 或日期范围
});
```

---

## 4. 功能特性对比

### 4.1 缓存机制

| 项目 | 缓存支持 | 缓存键设计 | TTL 配置 |
|------|---------|-----------|---------|
| NanoBot.Net | ❌ 无 | - | - |
| OpenCode | ❌ 无 (依赖 MCP 服务端) | - | - |
| OpenClaw | ✅ 有 | `${provider}:${query}:${count}:${country}:${lang}...` | ✅ 可配置 |

**OpenClaw 缓存示例**:
```typescript
const SEARCH_CACHE = new Map<string, CacheEntry<...>>();
const cacheKey = normalizeCacheKey(`${provider}:${query}:${count}...`);
const cached = readCache(SEARCH_CACHE, cacheKey);
```

### 4.2 超时控制

| 项目 | 超时实现 | 默认超时 | 可配置 |
|------|---------|---------|--------|
| NanoBot.Net | ❌ 无 | HttpClient 默认 | ❌ |
| OpenCode | ✅ `abortAfterAny` | 25秒 | ❌ |
| OpenClaw | ✅ `AbortSignal` | 30秒 | ✅ |

### 4.3 权限控制

| 项目 | 权限检查 | 模式匹配 | 元数据传递 |
|------|---------|---------|-----------|
| NanoBot.Net | ❌ 无 | - | - |
| OpenCode | ✅ 有 | `patterns: [params.query]` | ✅ 完整参数 |
| OpenClaw | ❌ 无 | - | - |

**OpenCode 权限示例**:
```typescript
await ctx.ask({
  permission: "websearch",
  patterns: [params.query],
  always: ["*"],
  metadata: { query, numResults, livecrawl, ... }
});
```

### 4.4 错误处理

**NanoBot.Net**:
```csharp
try {
    // 搜索逻辑
} catch (Exception ex) {
    return $"Error searching web: {ex.Message}";
}
```
- 简单 try-catch，返回错误字符串

**OpenCode**:
```typescript
try {
  // 搜索逻辑
} catch (error) {
  clearTimeout();
  if (error instanceof Error && error.name === "AbortError") {
    throw new Error("Search request timed out");
  }
  throw error;
}
```
- 区分超时错误和其他错误
- 清理超时定时器

**OpenClaw**:
```typescript
if (!apiKey) {
  return jsonResult(missingSearchKeyPayload(provider));
  // 返回结构化错误: { error, message, docs }
}

if (!res.ok) {
  const detail = await readResponseText(res);
  throw new Error(`Brave Search API error (${res.status}): ${detail}`);
}
```
- 结构化错误返回
- 包含文档链接指引
- 区分不同错误类型 (missing_key, invalid_freshness, etc.)

---

## 5. 代码架构对比

### 5.1 工具定义方式

**NanoBot.Net** (AIFunctionFactory):
```csharp
public static AITool CreateWebSearchTool(HttpClient? httpClient = null, WebToolsConfig? config = null)
{
    return AIFunctionFactory.Create(
        (string query, int maxResults) => WebSearchAsync(query, maxResults, httpClient, config),
        new AIFunctionFactoryOptions { Name = "web_search", Description = "..." }
    );
}
```

**OpenCode** (Tool.define):
```typescript
export const WebSearchTool = Tool.define("websearch", async () => {
  return {
    get description() { return DESCRIPTION.replace("{{year}}", ...) },
    parameters: z.object({ ... }),
    async execute(params, ctx) { ... }
  }
});
```

**OpenClaw** (对象字面量):
```typescript
return {
  label: "Web Search",
  name: "web_search",
  description: "...",
  parameters: WebSearchSchema,
  execute: async (_toolCallId, args) => { ... }
};
```

### 5.2 配置管理

| 项目 | 配置来源 | 环境变量 | 配置文件 |
|------|---------|---------|---------|
| NanoBot.Net | `WebToolsConfig` 对象 | ❌ | 通过 DI 注入 |
| OpenCode | 硬编码 | ❌ | ❌ |
| OpenClaw | `OpenClawConfig` | ✅ BRAVE_API_KEY, PERPLEXITY_API_KEY | ✅ `tools.web.search` |

**OpenClaw 配置优先级**:
1. 配置文件 `tools.web.search.provider`
2. 环境变量 `BRAVE_API_KEY` / `PERPLEXITY_API_KEY`
3. 运行时参数

### 5.3 测试覆盖

| 项目 | 测试文件 | 测试重点 |
|------|---------|---------|
| NanoBot.Net | `WebToolsIntegrationTests.cs` | 集成测试 |
| OpenCode | `websearch.test.ts` | 工具执行测试 |
| OpenClaw | `web-search.test.ts` | 配置解析、日期验证、URL 推断 |

**OpenClaw 测试亮点**:
```typescript
// API Key 前缀检测测试
expect(inferPerplexityBaseUrlFromApiKey("pplx-123")).toBe("direct");
expect(inferPerplexityBaseUrlFromApiKey("sk-or-v1-123")).toBe("openrouter");

// 日期范围验证测试
expect(normalizeFreshness("2024-01-01to2024-01-31")).toBe("2024-01-01to2024-01-31");
expect(normalizeFreshness("2024-13-01to2024-01-31")).toBeUndefined();
```

---

## 6. 响应格式对比

### 6.1 NanoBot.Net
```
- {Text content}
  URL: {FirstURL}

- {Text content}
  URL: {FirstURL}
```
- 简单文本拼接

### 6.2 OpenCode
```typescript
return {
  output: data.result.content[0].text,
  title: `Web search: ${params.query}`,
  metadata: {},
}
```
- 结构化输出，包含标题

### 6.3 OpenClaw (Brave)
```typescript
return {
  query: params.query,
  provider: params.provider,
  count: mapped.length,
  tookMs: Date.now() - start,
  results: [
    { title, url, description, published, siteName }
  ]
}
```

### 6.4 OpenClaw (Perplexity)
```typescript
return {
  query: params.query,
  provider: params.provider,
  model: params.perplexityModel,
  tookMs: Date.now() - start,
  content: "AI 合成答案",
  citations: ["url1", "url2"]
}
```

---

## 7. 优缺点分析

### 7.1 NanoBot.Net

**优点**:
- ✅ 零配置，开箱即用
- ✅ 无需 API Key
- ✅ 代码简洁，易于理解
- ✅ 集成 .NET `AIFunctionFactory`

**缺点**:
- ❌ 无高级搜索选项
- ❌ 无缓存机制
- ❌ 无超时控制
- ❌ 依赖 DuckDuckGo 免费 API（可能有限制）
- ❌ 响应格式简单

### 7.2 OpenCode

**优点**:
- ✅ MCP 协议标准化
- ✅ 权限控制完善 (ctx.ask)
- ✅ 支持实时爬取 (livecrawl)
- ✅ 支持搜索类型选择 (fast/deep/auto)
- ✅ 响应使用 SSE 流式处理

**缺点**:
- ❌ 依赖 Exa.ai 服务端
- ❌ 无本地缓存
- ❌ 参数相对固定
- ❌ 超时不可配置

### 7.3 OpenClaw

**优点**:
- ✅ 双提供商支持 (Brave/Perplexity)
- ✅ 配置灵活（环境变量/配置文件/运行时）
- ✅ 完善的缓存机制
- ✅ 丰富的搜索参数（地区/语言/时效性）
- ✅ 结构化错误处理
- ✅ API Key 智能推断（根据前缀识别提供商）
- ✅ 性能指标（tookMs）

**缺点**:
- ❌ 需要配置 API Key
- ❌ 代码复杂度较高
- ❌ 学习成本较高

---

## 8. 实现建议

### 8.1 NanoBot.Net 可借鉴的改进

1. **添加缓存机制** (参考 OpenClaw)
   ```csharp
   // 使用 IMemoryCache 或自定义缓存
   _cache.GetOrCreate($"websearch:{query}", ...)
   ```

2. **增加超时控制** (参考 OpenCode)
   ```csharp
   using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
   await client.GetStringAsync(url, cts.Token);
   ```

3. **扩展搜索提供商支持** (参考 OpenClaw)
   - 添加 Brave Search 支持
   - 添加 Perplexity 支持

4. **改进错误处理** (参考 OpenClaw)
   ```csharp
   return JsonSerializer.Serialize(new { error, message, docs });
   ```

5. **添加更多搜索参数**
   - 地区/语言过滤
   - 时效性过滤
   - 结果数量限制验证

### 8.2 工具描述优化

当前 NanoBot.Net 描述:
```csharp
Description = "Search the web for information. Returns a list of search results with titles, URLs, and snippets."
```

可优化为 OpenCode 风格（动态年份）:
```csharp
// 从描述文件加载，支持 {{year}} 替换
Description = DESCRIPTION.Replace("{{year}}", DateTime.Now.Year.ToString())
```

---

## 9. 总结

| 维度 | 推荐方案 |
|------|---------|
| **快速启动** | NanoBot.Net (DuckDuckGo) |
| **生产环境** | OpenClaw (Brave/Perplexity) |
| **标准化** | OpenCode (MCP) |
| **功能丰富度** | OpenClaw > OpenCode > NanoBot.Net |
| **代码简洁度** | NanoBot.Net > OpenCode > OpenClaw |
| **可维护性** | OpenClaw > OpenCode > NanoBot.Net |

三个项目的 web_search 实现各有侧重：
- **NanoBot.Net** 追求简洁快速，适合原型开发
- **OpenCode** 追求标准化和权限控制，适合企业级应用
- **OpenClaw** 追求功能完备和配置灵活，适合高级用户

---

## 10. 参考代码位置

| 项目 | 文件路径 |
|------|---------|
| NanoBot.Net | `src/NanoBot.Tools/BuiltIn/Web/WebTools.cs` |
| OpenCode | `Temp/opencode/packages/opencode/src/tool/websearch.ts` |
| OpenClaw | `src/agents/tools/web-search.ts` |

---

*报告生成完成*
