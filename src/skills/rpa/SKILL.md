---
name: rpa
description: Desktop RPA automation with OmniParser vision analysis
---

# RPA Skill

Use the `rpa` tool for desktop automation — controlling mouse, keyboard, and capturing screenshots of any application.

## Prerequisites

- OmniParser must be installed/configured for vision analysis
- Desktop environment required (not for headless servers)

## Flow Structure

```json
{
  "flows": [
    { "type": "step-type", "param": "value" }
  ],
  "enableVision": true
}
```

Steps execute sequentially. Use `screenshot` early to understand the current state.

## Flow Step Types

### Mouse

| Type | Parameters | Description |
|------|------------|-------------|
| `move` | `x`, `y` | Move cursor to coordinates |
| `click` | — | Left click |
| `double-click` | — | Double click |
| `right-click` | — | Context menu |
| `drag` | `fromX`, `fromY`, `toX`, `toY` | Drag operation |

### Keyboard

| Type | Parameters | Description |
|------|------------|-------------|
| `type` | `text` | Type text |
| `press` | `key` | Press single key |
| `hotkey` | `keys[]` | Key combo (e.g., `["Ctrl", "C"]`) |

### Screen

| Type | Parameters | Description |
|------|------------|-------------|
| `screenshot` | `outputRef` | Capture for vision analysis |
| `wait` | `durationMs` | Pause execution |

## Example: Open Finder and Navigate

```json
{
  "flows": [
    { "type": "screenshot", "outputRef": "initial" },
    { "type": "hotkey", "keys": ["Cmd", "Space"] },
    { "type": "wait", "durationMs": 500 },
    { "type": "type", "text": "Finder" },
    { "type": "press", "key": "Enter" },
    { "type": "wait", "durationMs": 1000 },
    { "type": "screenshot", "outputRef": "finder_open" }
  ],
  "enableVision": true
}
```

## Browser vs RPA — Screenshots

| Aspect | Browser | RPA |
|--------|---------|-----|
| **Target** | Web pages only | Any desktop app |
| **Use case** | Web automation | Desktop GUI control |
| **Vision** | Built-in AI detection | OmniParser required |
| **Example** | Fill web form | Click system dialog |

Use `browser` for web tasks; use `rpa` for desktop automation.
