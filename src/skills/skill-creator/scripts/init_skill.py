#!/usr/bin/env python3
"""
init_skill.py - Initialize a new skill directory with template structure.

Usage:
    python init_skill.py <skill-name> --path <output-directory> [--resources scripts,references,assets] [--examples]
"""

import argparse
import os
import re
from pathlib import Path


SKILL_TEMPLATE = """---
name: {name}
description: TODO: Write a clear description of what this skill does.
---

# {title}

TODO: Write the skill documentation here.

## Usage

TODO: Explain when and how to use this skill.

## Examples

TODO: Provide usage examples.
"""


def to_kebab_case(name: str) -> str:
    """Convert name to kebab-case."""
    # Replace underscores and spaces with hyphens
    name = name.replace("_", "-").replace(" ", "-")
    # Handle CamelCase
    name = re.sub(r"([a-z])([A-Z])", r"\1-\2", name)
    return name.lower()


def create_skill_dir(name: str, output_path: Path, resources: list, include_examples: bool) -> None:
    """Create the skill directory and files."""
    kebab_name = to_kebab_case(name)
    skill_dir = output_path / kebab_name

    if skill_dir.exists():
        print(f"Error: Skill directory already exists: {skill_dir}")
        return

    skill_dir.mkdir(parents=True)
    print(f"Created skill directory: {skill_dir}")

    # Create SKILL.md
    skill_md = skill_dir / "SKILL.md"
    title = " ".join(word.capitalize() for word in kebab_name.split("-"))
    skill_md.write_text(SKILL_TEMPLATE.format(name=kebab_name, title=title))
    print(f"Created: {skill_md}")

    # Create resource directories
    for resource in resources:
        resource_dir = skill_dir / resource
        resource_dir.mkdir(exist_ok=True)
        print(f"Created: {resource_dir}/")

        if include_examples and resource == "scripts":
            # Create a placeholder script
            script_path = resource_dir / f"{kebab_name}.py"
            script_path.write_text("#!/usr/bin/env python3\n# TODO: Implement script\n")
            print(f"Created: {script_path}")

        if include_examples and resource == "references":
            # Create a placeholder reference
            ref_path = resource_dir / "README.md"
            ref_path.write_text("# Reference\n\nTODO: Add reference documentation.\n")
            print(f"Created: {ref_path}")

    if include_examples and "assets" in resources:
        assets_dir = skill_dir / "assets"
        assets_dir.mkdir(exist_ok=True)
        print(f"Created: {assets_dir}/")


def main():
    parser = argparse.ArgumentParser(
        description="Initialize a new skill directory with template structure."
    )
    parser.add_argument("skill_name", help="Name of the skill to create")
    parser.add_argument(
        "--path",
        type=Path,
        default=Path("."),
        help="Output directory for the skill (default: current directory)"
    )
    parser.add_argument(
        "--resources",
        default="",
        help="Comma-separated list of resources to create (scripts,references,assets)"
    )
    parser.add_argument(
        "--examples",
        action="store_true",
        help="Include example files in the generated resources"
    )

    args = parser.parse_args()

    # Parse resources
    resources = []
    if args.resources:
        resources = [r.strip() for r in args.resources.split(",") if r.strip()]
        valid_resources = {"scripts", "references", "assets"}
        for r in resources:
            if r not in valid_resources:
                print(f"Warning: Unknown resource '{r}'. Valid options: {', '.join(sorted(valid_resources))}")

    create_skill_dir(args.skill_name, args.path, resources, args.examples)


if __name__ == "__main__":
    main()
