#!/usr/bin/env python3
"""Render one exact-source profile and stage identical bytes for native release builds."""

from __future__ import annotations

import hashlib
import json
import pathlib
import sys

from render_deployment_profile import render_profile


def encode_profile(value: dict) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def stage_profile(
    template_path: pathlib.Path,
    source_commit: str,
    output_paths: list[pathlib.Path],
) -> str:
    if not output_paths:
        raise ValueError("at least one deployment profile output is required")

    template = json.loads(template_path.read_text(encoding="utf-8"))
    rendered = render_profile(template, source_commit)
    payload = encode_profile(rendered)

    for output_path in output_paths:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_bytes(payload)

    return hashlib.sha256(payload).hexdigest()


def main() -> None:
    if len(sys.argv) < 4:
        raise SystemExit(
            "usage: stage_deployment_profile.py TEMPLATE.json SOURCE_COMMIT OUTPUT.json [OUTPUT.json ...]"
        )

    template_path = pathlib.Path(sys.argv[1])
    source_commit = sys.argv[2]
    output_paths = [pathlib.Path(value) for value in sys.argv[3:]]

    try:
        digest = stage_profile(template_path, source_commit, output_paths)
    except ValueError as exc:
        raise SystemExit(f"deployment profile staging failed: {exc}") from exc

    print(f"deployment profile staged identically: sha256={digest}")


if __name__ == "__main__":
    main()
