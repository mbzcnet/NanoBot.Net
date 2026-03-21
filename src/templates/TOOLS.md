# Tool Usage Notes

Tool signatures are provided automatically via function calling.
This file documents non-obvious constraints and usage patterns.

## exec — Safety Limits

- Commands have a configurable timeout (default 60s)
- Dangerous commands are blocked (rm -rf, format, dd, shutdown, etc.)
- Output is truncated at 10,000 characters
- `restrictToWorkspace` config can limit file access to the workspace

## cron — Scheduled Reminders

- Please refer to cron skill for usage.

## browser — Web Browsing

- Use `browser` for real webpage interaction (open, navigate, snapshot, act, content)
- **IMPORTANT**: The `browser` tool has a single function with `action` parameter
- Available actions:
  - `open`: Opens a new tab. Parameters: `url` or `targetUrl` (URL to open)
  - `navigate`: Navigate existing tab. Parameters: `targetId` (tab ID), `url` or `targetUrl`
  - `snapshot`: Get page structure. Parameters: `targetId`
  - `capture`: Take screenshot. Parameters: `targetId`
  - `content`: Extract text content. Parameters: `targetId`, `selector` (optional), `maxChars` (optional)
  - `act`: Click/hover/scroll. Parameters: `targetId`, `kind`, `reference`
  - `wait`: Wait for element. Parameters: `targetId`, `text` or `textGone`, `timeoutMs`
  - `close`: Close tab. Parameters: `targetId`
  - `status`: Check browser status. No parameters
- **Workflow**: open → snapshot → act → content
- **Note**: `open` returns a `targetId` which must be used for subsequent actions on that tab
