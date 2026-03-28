---
name: memory
description: Two-layer memory system with grep-based recall.
always: true
---

# Memory

## Structure

- `memory/MEMORY.md` — Long-term facts (preferences, project context, relationships). Always loaded into your context.

## Search Session History

Sessions are stored as JSONL files in `sessions/` directory. Each file contains a conversation history that can be searched.

Choose the search method based on file size and content:

**Small sessions**: Use `read_file` to load the session, then search in-memory.

**Large sessions or multi-keyword search**: Use the `exec` tool for targeted search.

Examples:
- **Linux/macOS:** `grep -i "keyword" sessions/*.jsonl`
- **Cross-platform Python:** `python -c "from pathlib import Path; [print(l) for l in Path('sessions').glob('*.jsonl') for line in l.read_text().splitlines() if 'keyword' in line.lower()]"`

## When to Update MEMORY.md

Write important facts immediately using `edit_file` or `write_file`:
- User preferences ("I prefer dark mode")
- Project context ("The API uses OAuth2")
- Relationships ("Alice is the project lead")
