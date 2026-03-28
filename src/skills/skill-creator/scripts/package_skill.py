#!/usr/bin/env python3
"""
package_skill.py - Package a skill into a distributable .skill file.

Usage:
    python package_skill.py <path/to/skill-folder> [output-directory]

The .skill file is a zip archive with a .skill extension.
"""

import argparse
import shutil
import sys
import zipfile
from pathlib import Path


def validate_skill(skill_dir: Path) -> list:
    """Validate a skill directory and return list of errors."""
    errors = []

    if not skill_dir.exists():
        errors.append(f"Skill directory does not exist: {skill_dir}")
        return errors

    if not skill_dir.is_dir():
        errors.append(f"Path is not a directory: {skill_dir}")
        return errors

    # Check for SKILL.md
    skill_md = skill_dir / "SKILL.md"
    if not skill_md.exists():
        errors.append("Missing SKILL.md file")

    if skill_md.exists():
        try:
            content = skill_md.read_text()
            # Check for YAML frontmatter
            if not content.startswith("---"):
                errors.append("SKILL.md must start with YAML frontmatter (---)")
            # Check for required fields
            if "name:" not in content:
                errors.append("SKILL.md missing 'name:' field in frontmatter")
            if "description:" not in content:
                errors.append("SKILL.md missing 'description:' field in frontmatter")
        except Exception as e:
            errors.append(f"Failed to read SKILL.md: {e}")

    # Check name format
    name = skill_dir.name
    if not name.isidentifier().replace("-", "").replace("_", "").isalnum():
        errors.append(f"Invalid skill name format: {name} (use lowercase, numbers, hyphens, underscores only)")

    # Check for nested skills directories (skills should be flat)
    for item in skill_dir.iterdir():
        if item.is_dir() and item.name.endswith("-skill"):
            errors.append(f"Found nested skill directory: {item.name} (skills should be flat)")

    return errors


def package_skill(skill_dir: Path, output_dir: Path) -> Path:
    """Package a skill directory into a .skill file."""
    skill_name = skill_dir.name
    output_file = output_dir / f"{skill_name}.skill"

    # Create zip file
    with zipfile.ZipFile(output_file, 'w', zipfile.ZIP_DEFLATED) as zf:
        for file_path in skill_dir.rglob("*"):
            if file_path.is_file():
                arcname = file_path.relative_to(skill_dir)
                zf.write(file_path, arcname)

    return output_file


def main():
    parser = argparse.ArgumentParser(
        description="Package a skill into a distributable .skill file."
    )
    parser.add_argument("skill_path", type=Path, help="Path to the skill directory")
    parser.add_argument("output_dir", type=Path, nargs="?", help="Output directory (default: current directory)")

    args = parser.parse_args()

    skill_dir = args.skill_path.resolve()
    output_dir = (args.output_dir or Path.cwd()).resolve()

    if not output_dir.exists():
        output_dir.mkdir(parents=True)

    print(f"Validating skill: {skill_dir}")

    errors = validate_skill(skill_dir)
    if errors:
        print("Validation FAILED:")
        for error in errors:
            print(f"  - {error}")
        sys.exit(1)

    print("Validation passed.")

    print(f"Packaging skill to: {output_dir}")
    output_file = package_skill(skill_dir, output_dir)

    print(f"Successfully created: {output_file}")
    print(f"File size: {output_file.stat().st_size:,} bytes")


if __name__ == "__main__":
    main()
