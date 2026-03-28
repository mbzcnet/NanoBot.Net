---
name: clawhub
description: Search and install agent skills from ClawHub, the public skill registry. Use when the user asks to find a skill, search for skills, install a skill, or manage skill packages.
homepage: https://clawhub.ai
metadata: {"nanobot":{"emoji":"🦞"}}
---

# ClawHub

Public skill registry for AI agents. Search by natural language (vector search).

## Prerequisites

- Node.js is required (npx comes with Node.js)
- No API key needed for search and install

## When to Use

Use this skill when the user asks any of:
- "find a skill for …"
- "search for skills"
- "install a skill"
- "what skills are available?"
- "update my skills"
- "list installed skills"

## Search

Search for skills in the public registry:

```bash
npx --yes clawhub@latest search "web scraping" --limit 5
```

## Install

Install a skill to the workspace:

```bash
npx --yes clawhub@latest install <slug> --workdir ~/.nanobot/workspace
```

Replace `<slug>` with the skill name from search results. This places the skill into `~/.nanobot/workspace/skills/`.

**Important**: Always include `--workdir` to install to the correct location.

## Update

Update all installed skills:

```bash
npx --yes clawhub@latest update --all --workdir ~/.nanobot/workspace
```

## List Installed

List skills installed in the workspace:

```bash
npx --yes clawhub@latest list --workdir ~/.nanobot/workspace
```

## Notes

- Login (`npx --yes clawhub@latest login`) is only required for publishing skills
- After install, remind the user to start a new session to load the skill
- Skills are cached locally; use `--force` flag to reinstall
