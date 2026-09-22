#!/usr/bin/env python3
"""Fail closed on shell patterns that can make backup/restore SQL silently no-op."""

from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
SCRIPTS = [
    ROOT / "backend/supabase/tests/create_fictional_backup_rehearsal.sh",
    ROOT / "backend/supabase/tests/seed_fictional_recovery_history.sh",
    ROOT / "backend/supabase/tests/restore_fictional_backup_rehearsal.sh",
    ROOT / "backend/supabase/tests/verify_fictional_recovery_history.sh",
]

DO_BLOCK = re.compile(
    r"\bdo\s+\$(?P<tag>[A-Za-z0-9_]*)\$(?P<body>.*?)\$(?P=tag)\$\s*;",
    re.IGNORECASE | re.DOTALL,
)
PSQL_VAR = re.compile(r":'[A-Za-z_][A-Za-z0-9_]*'")


def fail(message: str) -> None:
    raise SystemExit(message)


for path in SCRIPTS:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()

    # A heredoc reaches psql inside a container only when docker exec keeps
    # stdin open. Missing -i previously produced a dangerous false-positive:
    # psql exited 0 without receiving or executing the SQL body.
    for index, line in enumerate(lines):
        if "<<'SQL'" not in line:
            continue
        start = max(0, index - 12)
        invocation = "\n".join(lines[start : index + 1])
        docker_pos = invocation.rfind("docker exec")
        if docker_pos < 0:
            continue
        invocation = invocation[docker_pos:]
        if "psql" not in invocation:
            continue
        if not re.search(r"\bdocker\s+exec\s+-[^\n]*i|\bdocker\s+exec\s+-i\b", invocation):
            fail(f"{path.relative_to(ROOT)}:{index + 1}: docker exec psql heredoc must keep stdin open with -i")

    # psql variables are not interpolated inside a dollar-quoted DO body.
    # Values must be transferred before DO (for example with set_config) and
    # read inside PL/pgSQL via current_setting.
    for match in DO_BLOCK.finditer(text):
        body = match.group("body")
        bad = PSQL_VAR.search(body)
        if bad:
            line = text.count("\n", 0, match.start()) + 1
            fail(
                f"{path.relative_to(ROOT)}:{line}: psql variable {bad.group(0)} "
                "must not appear inside a dollar-quoted DO body"
            )

print("backup rehearsal shell guards passed")
