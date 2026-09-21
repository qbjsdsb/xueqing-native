#!/usr/bin/env bash
set -euo pipefail

db_container="${1:?usage: snapshot_public_table_state.sh DB_CONTAINER}"

mapfile -t tables < <(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select tablename from pg_catalog.pg_tables where schemaname = 'public' order by tablename"
)

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

for table in "${tables[@]}"; do
  if [[ ! "$table" =~ ^[a-z][a-z0-9_]*$ ]]; then
    echo "Unsafe public table name: $table" >&2
    exit 1
  fi

  count="$(
    docker exec "$db_container" psql -U postgres -d postgres -Atc \
      "select count(*) from public.\"$table\""
  )"

  digest="$(
    docker exec "$db_container" psql -U postgres -d postgres -Atc \
      "select pg_catalog.to_jsonb(row_value)::text
         from public.\"$table\" as row_value
        order by pg_catalog.to_jsonb(row_value)::text" \
      | sha256sum | awk '{print $1}'
  )"

  printf '%s\t%s\t%s\n' "$table" "$count" "$digest" >> "$tmp"
done

python3 - "$tmp" <<'PY'
import json
import pathlib
import sys

rows = {}
fingerprints = {}
for raw in pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").splitlines():
    table, count, digest = raw.split("\t")
    rows[table] = int(count)
    fingerprints[table] = digest

print(json.dumps(
    {
        "row_counts": rows,
        "table_fingerprints_sha256": fingerprints,
    },
    separators=(",", ":"),
    sort_keys=True,
))
PY
