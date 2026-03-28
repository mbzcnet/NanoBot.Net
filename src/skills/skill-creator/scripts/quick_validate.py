#!/usr/bin/env python3
"""
quick_validate.py - Quick validation for skill directories.

Usage:
    python quick_validate.py <path/to/skill-folder>

Exit codes:
    0 - Validation passed
    1 - Validation failed
"""

import argparse
import sys
from pathlib import Path


def validate_skill(skill_dir: Path) -> tuple:
    """Validate a skill directory. Returns (passed, errors)."""
    errors = []

    if not skill_dir.exists():
        return False, [f"Directory does not exist: {skill_dir}"]

    if not skill_dir.is_dir():
        return False, [f"Path is not a directory: {skill_dir}"]

    # Check for SKILL.md
    skill_md = skill_dir / "SKILL.md"
    if not skill_md.exists():
        return False, ["Missing SKILL.md"]

    try:
        content = skill_md.read_text()

        # Check for YAML frontmatter
        if not content.startswith("---"):
            return False, ["SKILL.md must start with YAML frontmatter (---)"]

        # Find end of frontmatter
        frontmatter_end = content.find("---", 3)
        if frontmatter_end == -1:
            return False, ["YAML frontmatter not properly closed with ---"]

        frontmatter = content[3:frontmatter_end].strip()

        # Check for required fields
        if "name:" not in frontmatter:
            return False, ["Frontmatter missing 'name:' field"]

        if "description:" not in frontmatter:
            return False, ["Frontmatter missing 'description:' field"]

    except Exception as e:
        return False, [f"Failed to read SKILL.md: {e}"]

    # Check name format
    name = skill_dir.name
    valid_chars = set("abcdefghijklmnopqrstuvwxyz0123456789-_")
    if not all(c in valid_chars for c in name.lower()):
        return False, [f"Invalid name format: {name} (use lowercase letters, numbers, hyphens, underscores)"]

    return True, []


def main():
    parser = argparse.ArgumentParser(
        description="Quick validation for skill directories."
    )
    parser.add_argument("skill_path", type=Path, help="Path to the skill directory")
    parser.add_argument("-q", "--quiet", action="store_true", help="Suppress output, only return exit code")

    args = parser.parse_args()

    skill_dir = args.skill_path.resolve()
    passed, errors = validate_skill(skill_dir)

    if not args.quiet:
        if passed:
            print(f"OK: {skill_dir.name}")
        else:
            print(f"FAIL: {skill_dir.name}")
            for error in errors:
                print(f"  - {error}")

    sys.exit(0 if passed else 1)


if __name__ == "__main__":
    main()
