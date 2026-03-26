# Tool Usage Notes

Tool signatures are provided automatically via function calling.
This file documents non-obvious constraints and usage patterns.

## exec — Safety Limits

- Commands have a configurable timeout (default 60s)
- Dangerous commands are blocked (rm -rf, format, dd, shutdown, etc.)
- Output is truncated at 10,000 characters
- `restrictToWorkspace` config can limit file access to the workspace

## web_page — Web Information Retrieval

Use `web_page` for quick web information retrieval:

- `mode="search"`: Search DuckDuckGo for information
- `mode="fetch"`: Fetch and extract text from a URL

Parameters:
- `url`: Target URL (required for fetch mode)
- `mode`: "search" or "fetch" (default: "fetch")
- `query`: Search query (required when mode="search")
- `maxResults`: Maximum results (default: 5, for search mode only)

Examples:
- `web_page(url="https://example.com")` — fetches page content
- `web_page(mode="search", query="latest AI news", maxResults=3)` — searches the web

## browser — Web Automation

NanoBot provides multiple atomic browser tools for web interaction.

### Available Tools

| Tool | Purpose | Key Parameters |
|------|---------|----------------|
| `browser_tabs` | List open tabs | profile (optional) |
| `browser_open` | Open new tab | url (required), profile |
| `browser_navigate` | Navigate tab | targetId, url |
| `browser_close` | Close tab | targetId |
| `browser_snapshot` | Get page elements | targetId, format ("ai"/"raw") |
| `browser_interact` | Click, type, scroll | targetId, kind, reference/text |
| `browser_content` | Extract text | targetId, selector (optional) |
| `browser_screenshot` | Capture screenshot | targetId, format |

### Standard Workflow

```
1. browser_open(url="...") → returns targetId
2. browser_snapshot(targetId="...") → get element references
3. browser_interact(targetId="...", kind="click", reference="btn_0") → interact
4. browser_content(targetId="...") → extract text
```

### Key Concepts

- `targetId`: Unique tab identifier returned by `browser_open` or `browser_navigate`
- `reference`: Element identifier from `browser_snapshot` results
- `kind` (for browser_interact): "click", "hover", "scroll", "press", "type"

### Examples

```json
// Open and interact with a search form
browser_open(url="https://google.com")
browser_snapshot(targetId="tab_abc123")
browser_interact(targetId="tab_abc123", kind="type", reference="input_0", text="search query")
browser_interact(targetId="tab_abc123", kind="press", key="Enter")
browser_content(targetId="tab_abc123")
```

## cron — Scheduled Reminders

- Please refer to cron skill for usage.
