#!/usr/bin/env python3
"""Render the public production deployment profile for one exact source commit."""

from __future__ import annotations

import json
import pathlib
import re
import sys

from verify_deployment_profile import validate_profile

COMMIT_RE = re.compile(r"^[0-9a-f]{40}$")
PLACEHOLDER = "__SOURCE_COMMIT__"


def render_profile(template: object, source_commit: str) -> dict:
    if not isinstance(template, dict):
        raise ValueError("deployment profile template must be an object")
    if COMMIT_RE.fullmatch(source_commit) is None:
        raise ValueError("source commit must be a 40-char lowercase SHA")

    source = template.get("source")
    if not isinstance(source, dict):
        raise ValueError("deployment profile template source must be an object")
    if source.get("commit") != PLACEHOLDER:
        raise ValueError("deployment profile template must contain the exact source-commit placeholder")

    rendered = json.loads(json.dumps(template))
    rendered["source"]["commit"] = source_commit
    validate_profile(rendered)
    return rendered


def main() -> None:
    if len(sys.argv) != 4:
        raise SystemExit(
            "usage: render_deployment_profile.py TEMPLATE.json OUTPUT.json SOURCE_COMMIT"
        )

    template_path = pathlib.Path(sys.argv[1])
    output_path = pathlib.Path(sys.argv[2])
    source_commit = sys.argv[3]

    template = json.loads(template_path.read_text(encoding="utf-8"))
    try:
        rendered = render_profile(template, source_commit)
    except ValueError as exc:
        raise SystemExit(f"deployment profile render failed: {exc}") from exc

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(rendered, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"deployment profile rendered for source commit {source_commit}")


if __name__ == "__main__":
    main()
