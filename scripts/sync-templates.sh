#!/bin/bash
#
# Sync agent template files from src/templates to ~/.nbot/workspace
# This replaces the old versions in the workspace with fresh templates
#

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
TEMPLATES_DIR="$PROJECT_ROOT/src/templates"
WORKSPACE_DIR="$HOME/.nbot/workspace"

# Files to sync
TEMPLATE_FILES=(
    "AGENTS.md"
    "SOUL.md"
    "USER.md"
    "TOOLS.md"
    "HEARTBEAT.md"
)

echo "Syncing template files to workspace..."
echo "Templates: $TEMPLATES_DIR"
echo "Workspace: $WORKSPACE_DIR"
echo

# Sync root-level template files
for file in "${TEMPLATE_FILES[@]}"; do
    src="$TEMPLATES_DIR/$file"
    dest="$WORKSPACE_DIR/$file"
    
    if [[ -f "$src" ]]; then
        cp -f "$src" "$dest"
        echo "  ✓ Synced $file"
    else
        echo "  ✗ Source file not found: $src"
    fi
done

# Sync memory/MEMORY.md
src_mem="$TEMPLATES_DIR/memory/MEMORY.md"
dest_mem="$WORKSPACE_DIR/memory/MEMORY.md"

if [[ -f "$src_mem" ]]; then
    mkdir -p "$WORKSPACE_DIR/memory"
    cp -f "$src_mem" "$dest_mem"
    echo "  ✓ Synced memory/MEMORY.md"
else
    echo "  ✗ Source file not found: $src_mem"
fi

echo
echo "Sync complete!"
