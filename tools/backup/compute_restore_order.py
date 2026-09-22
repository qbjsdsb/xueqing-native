#!/usr/bin/env python3
import argparse
import json
from pathlib import Path
import re
import sys

TABLE_RE = re.compile(r"^[a-z][a-z0-9_]*$")


def fail(message: str) -> None:
    raise ValueError(message)


def compute(value: dict) -> list[str]:
    tables = value.get("tables")
    edges = value.get("foreign_keys")
    if not isinstance(tables, list) or not all(isinstance(x, str) for x in tables):
        fail("tables must be a string array")
    if not isinstance(edges, list):
        fail("foreign_keys must be an array")
    if len(tables) != len(set(tables)):
        fail("tables must be unique")
    for table in tables:
        if TABLE_RE.fullmatch(table) is None:
            fail(f"unsafe table name: {table}")

    known = set(tables)
    parents: dict[str, set[str]] = {table: set() for table in tables}
    children: dict[str, set[str]] = {table: set() for table in tables}

    for edge in edges:
        if not isinstance(edge, dict) or set(edge) != {"child", "parent"}:
            fail("foreign key entry must contain child and parent")
        child = edge["child"]
        parent = edge["parent"]
        if child not in known or parent not in known:
            fail(f"foreign key references unknown table: {child}->{parent}")
        if child == parent:
            fail(f"self-referential table requires explicit restore strategy: {child}")
        parents[child].add(parent)
        children[parent].add(child)

    ready = sorted(table for table in tables if not parents[table])
    ordered: list[str] = []
    while ready:
        table = ready.pop(0)
        ordered.append(table)
        for child in sorted(children[table]):
            parents[child].discard(table)
            if not parents[child] and child not in ordered and child not in ready:
                ready.append(child)
                ready.sort()

    if len(ordered) != len(tables):
        remaining = sorted(table for table in tables if table not in ordered)
        fail("foreign-key cycle requires explicit restore strategy: " + ", ".join(remaining))

    return ordered


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("graph", type=Path)
    args = parser.parse_args()
    try:
        value = json.loads(args.graph.read_text(encoding="utf-8"))
        if not isinstance(value, dict):
            fail("restore graph root must be an object")
        for table in compute(value):
            print(table)
    except (OSError, json.JSONDecodeError, ValueError) as exc:
        print(f"restore-order computation failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
