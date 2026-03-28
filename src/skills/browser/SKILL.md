---
name: browser
description: Playwright-based web automation for scraping and interacting with web pages
---

# Browser Skill

Use the `browser` tool for real webpage interaction when tasks require clicking, waiting, scrolling, or reading dynamic content.

## Typical Flow

1. `open` / `navigate` — Start browsing
2. `snapshot` — Get element detection
3. `act` — Interact (click, type, scroll)
4. `content` — Extract data

## Actions Reference

| Action | Purpose | Key Parameters |
|--------|---------|----------------|
| `status` | Get browser state | — |
| `start` | Launch browser | `profile` |
| `stop` | Close browser | — |
| `tabs` | List open tabs | — |
| `open` | New tab | `url`, `profile` |
| `navigate` | Go to URL | `targetId`, `url` |
| `close` | Close tab | `targetId` |
| `snapshot` | Get elements | `targetId`, `format` ("ai" or "raw") |
| `capture` | Screenshot | `targetId` |
| `content` | Extract text | `targetId`, `selector`, `maxChars` |
| `act` | Interact | `targetId`, `kind`, `reference`, `text`, `key` |
| `wait` | Pause | `targetId`, `text`, `textGone`, `timeoutMs` |

## snapshot Format

- `format: "ai"` (default) — AI-friendly element detection with coordinates
- `format: "raw"` — Full HTML dump

## act Actions

| Kind | Parameters | Description |
|------|------------|-------------|
| `click` | `reference` | Click element by reference |
| `hover` | `reference` | Hover over element |
| `scroll` | `scrollBy` | Scroll by pixels |
| `type` | `reference`, `text` | Type into input |
| `press` | `key` | Press keyboard key |

## loadState Options

- `load` — Wait for full page load (default)
- `domcontentloaded` — Faster, for static pages
- `networkidle` — Wait for network idle

## When to Use vs web_fetch

| Use `browser` | Use `web_fetch` |
|---------------|-----------------|
| Clicking/interacting | Quick static content |
| Waiting for JS | No interaction needed |
| Scrolling required | Simple URL fetch |
| Authenticated pages | Known static pages |

## Screenshot Note

Browser `capture` captures **web page screenshots**. For **desktop screenshots**, use the `rpa` tool with OmniParser.
