#!/usr/bin/env bash
set -euo pipefail

backup_dir="${1:?usage: restore_fictional_backup_rehearsal.sh BACKUP_DIR}"
archive="$backup_dir/archive.tar.gpg"
key_file="$backup_dir/rehearsal.key"

[[ -s "$archive" && -s "$key_file" ]] || {
  echo 'Encrypted rehearsal archive/key are missing.' >&2
  exit 1
}

restore_root="$(mktemp -d)"
extract_dir="$restore_root/extracted"
target_root="$restore_root/target"
target_backend="$target_root/backend"
mkdir -p "$extract_dir" "$target_root"

cleanup() {
  set +e
  if [[ -d "$target_backend" ]]; then
    supabase stop --workdir "$target_backend" >/dev/null 2>&1 || true
  fi
  rm -rf "$restore_root"
}
trap cleanup EXIT

gpg --batch --yes --pinentry-mode loopback \
  --passphrase-file "$key_file" \
  --decrypt --output "$restore_root/archive.tar" "$archive"
tar -C "$extract_dir" -xf "$restore_root/archive.tar"
rm -f "$restore_root/archive.tar"

manifest="$extract_dir/manifest.json"
python3 tools/backup/verify_backup_manifest.py "$manifest"

python3 - "$manifest" "$extract_dir" <<'PY'
import hashlib
import json
import pathlib
import sys

manifest_path = pathlib.Path(sys.argv[1])
root = pathlib.Path(sys.argv[2])
value = json.loads(manifest_path.read_text(encoding="utf-8"))

def verify(entry: dict) -> None:
    path = root / entry["archive_relative_path"]
    if not path.is_file():
        raise SystemExit(f"missing backup payload: {entry['archive_relative_path']}")
    content = path.read_bytes()
    if len(content) != entry["byte_length"]:
        raise SystemExit(f"byte-length mismatch: {entry['archive_relative_path']}")
    digest = hashlib.sha256(content).hexdigest()
    if digest != entry["sha256"]:
        raise SystemExit(f"digest mismatch: {entry['archive_relative_path']}")

verify(value["database"])
for item in value["storage"]["objects"]:
    verify(item)
PY

cp -a backend "$target_backend"
python3 - "$target_backend/supabase/config.toml" <<'PY'
import pathlib
import re
import sys

path = pathlib.Path(sys.argv[1])
text = path.read_text(encoding="utf-8")
text, count = re.subn(
    r'^project_id\s*=\s*"[^"]+"',
    'project_id = "xueqing-native-restore-rehearsal"',
    text,
    count=1,
    flags=re.MULTILINE,
)
if count != 1:
    raise SystemExit("target config project_id was not rewritten")
text, count = re.subn(
    r'(\[db\.seed\]\s*\nenabled\s*=\s*)true',
    r'\1false',
    text,
    count=1,
)
if count != 1:
    raise SystemExit("target config seed flag was not disabled")
path.write_text(text, encoding="utf-8")
PY

supabase start --workdir "$target_backend"
supabase db reset --workdir "$target_backend"

target_env="$(mktemp)"
supabase status --workdir "$target_backend" -o env > "$target_env"
set -a
# shellcheck disable=SC1090
source "$target_env"
set +a
rm -f "$target_env"

: "${API_URL:?target API_URL missing}"
: "${ANON_KEY:?target ANON_KEY missing}"
: "${SERVICE_ROLE_KEY:?target SERVICE_ROLE_KEY missing}"
: "${JWT_SECRET:?target JWT_SECRET missing}"

api_url="${API_URL%/}"
echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"

db_container="$(
  docker ps --filter 'name=supabase_db_xueqing-native-restore-rehearsal' \
    --format '{{.Names}}' | head -n 1
)"
if [[ -z "$db_container" ]]; then
  echo 'Fresh restore database container was not found.' >&2
  docker ps -a >&2 || true
  exit 1
fi

sequence_count="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select count(*) from pg_sequences where schemaname = 'public'"
)"
if [[ "$sequence_count" != '0' ]]; then
  echo "Public sequences require an explicit restore strategy; found $sequence_count." >&2
  exit 1
fi

docker exec -i "$db_container" psql -U postgres -d postgres -At > "$restore_root/restore-graph.json" <<'SQL'
select pg_catalog.json_build_object(
    'tables',
    coalesce(
        (
            select pg_catalog.json_agg(table_name order by table_name)
              from information_schema.tables
             where table_schema = 'public'
               and table_type = 'BASE TABLE'
        ),
        '[]'::json
    ),
    'foreign_keys',
    coalesce(
        (
            select pg_catalog.json_agg(
                pg_catalog.json_build_object(
                    'child', child.relname,
                    'parent', parent.relname
                )
                order by child.relname, parent.relname
            )
              from pg_catalog.pg_constraint as constraint_row
              join pg_catalog.pg_class as child
                on child.oid = constraint_row.conrelid
              join pg_catalog.pg_namespace as child_namespace
                on child_namespace.oid = child.relnamespace
              join pg_catalog.pg_class as parent
                on parent.oid = constraint_row.confrelid
              join pg_catalog.pg_namespace as parent_namespace
                on parent_namespace.oid = parent.relnamespace
             where constraint_row.contype = 'f'
               and child_namespace.nspname = 'public'
               and parent_namespace.nspname = 'public'
        ),
        '[]'::json
    )
)::text;
SQL

python3 tools/backup/compute_restore_order.py \
  "$restore_root/restore-graph.json" > "$restore_root/restore-order.txt"

docker cp "$extract_dir/database/xueqing.dump" "$db_container:/tmp/xueqing.dump"
while IFS= read -r table; do
  [[ -n "$table" ]] || continue
  docker exec "$db_container" pg_restore \
    -U postgres -d postgres \
    --data-only \
    --no-owner \
    --no-acl \
    --exit-on-error \
    --table="public.$table" \
    /tmp/xueqing.dump
done < "$restore_root/restore-order.txt"

python3 - "$manifest" > "$restore_root/row-counts.tsv" <<'PY'
import json
import pathlib
import re
import sys

value = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
for table, count in sorted(value["database"]["row_counts"].items()):
    if not re.fullmatch(r"[a-z][a-z0-9_]*", table):
        raise SystemExit(f"unsafe table name in manifest: {table}")
    print(f"{table}\t{count}")
PY

while IFS=$'\t' read -r table expected; do
  actual="$(
    docker exec "$db_container" psql -U postgres -d postgres -Atc \
      "select count(*) from public.$table"
  )"
  if [[ "$actual" != "$expected" ]]; then
    echo "Restored row count mismatch for $table: expected=$expected actual=$actual" >&2
    exit 1
  fi
done < "$restore_root/row-counts.tsv"

python3 - "$manifest" "$extract_dir" > "$restore_root/objects.tsv" <<'PY'
import json
import pathlib
import sys

value = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
root = pathlib.Path(sys.argv[2])
for item in value["storage"]["objects"]:
    path = root / item["archive_relative_path"]
    print("\t".join([
        item["logical_object_locator"],
        str(path),
        item["mime_type"],
        str(item["byte_length"]),
        item["sha256"],
    ]))
PY

bucket='teaching-attachments-v1'
while IFS=$'\t' read -r locator object_file mime expected_size expected_sha; do
  curl --fail-with-body --silent --show-error \
    -X POST "$api_url/storage/v1/object/$bucket/$locator" \
    -H "apikey: $SERVICE_ROLE_KEY" \
    -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
    -H "Content-Type: $mime" \
    --data-binary "@$object_file" >/dev/null

  downloaded="$(mktemp)"
  curl --fail-with-body --silent --show-error \
    -X GET "$api_url/storage/v1/object/authenticated/$bucket/$locator" \
    -H "apikey: $SERVICE_ROLE_KEY" \
    -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
    --output "$downloaded"

  actual_size="$(wc -c < "$downloaded" | tr -d ' ')"
  actual_sha="$(sha256sum "$downloaded" | awk '{print $1}')"
  rm -f "$downloaded"

  [[ "$actual_size" == "$expected_size" ]] || {
    echo "Restored object size mismatch for $locator" >&2
    exit 1
  }
  [[ "$actual_sha" == "$expected_sha" ]] || {
    echo "Restored object digest mismatch for $locator" >&2
    exit 1
  }

  anon_status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
      -X GET "$api_url/storage/v1/object/authenticated/$bucket/$locator" \
      -H "apikey: $ANON_KEY" || true
  )"
  if [[ "$anon_status" =~ ^2 ]]; then
    echo "Anonymous private Attachment read unexpectedly succeeded for $locator" >&2
    exit 1
  fi
done < "$restore_root/objects.tsv"

json_get() {
  local expression="$1"
  python3 -c '
import json,sys
expression=sys.argv[1]
current=json.load(sys.stdin)
for part in expression.split("."):
    current=current[int(part)] if isinstance(current,list) else current[part]
if isinstance(current,(dict,list)):
    print(json.dumps(current,ensure_ascii=False,separators=(",",":")))
elif current is None:
    print("")
else:
    print(current)
' "$expression"
}

jwt_claim() {
  local token="$1" claim="$2"
  python3 -c '
import base64,json,sys
token,claim=sys.argv[1],sys.argv[2]
payload=token.split(".")[1]
payload += "=" * (-len(payload)%4)
claims=json.loads(base64.urlsafe_b64decode(payload.encode()).decode())
value=claims.get(claim)
print("" if value is None else value)
' "$token" "$claim"
}

create_auth_identity() {
  local email="$1" password="$2"
  local user_response user_id session token issuer subject

  user_response="$(
    curl --fail-with-body --silent --show-error \
      -X POST "$api_url/auth/v1/admin/users" \
      -H "apikey: $SERVICE_ROLE_KEY" \
      -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
      -H 'Content-Type: application/json' \
      --data "{\"email\":\"$email\",\"password\":\"$password\",\"email_confirm\":true}"
  )"
  user_id="$(printf '%s' "$user_response" | json_get id)"

  session="$(
    curl --fail-with-body --silent --show-error \
      -X POST "$api_url/auth/v1/token?grant_type=password" \
      -H "apikey: $ANON_KEY" \
      -H 'Content-Type: application/json' \
      --data "{\"email\":\"$email\",\"password\":\"$password\"}"
  )"
  token="$(printf '%s' "$session" | json_get access_token)"
  issuer="$(jwt_claim "$token" iss)"
  subject="$(jwt_claim "$token" sub)"

  [[ -n "$user_id" && -n "$token" && -n "$issuer" && "$subject" == "$user_id" ]] || {
    echo "Fresh provider identity creation failed for $email." >&2
    exit 1
  }

  echo "::add-mask::$token" >&2
  printf '%s|%s|%s|%s\n' "$user_id" "$token" "$issuer" "$subject"
}

suffix="${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
actor_email="restore-actor-$suffix@example.com"
unlinked_email="restore-unlinked-$suffix@example.com"
actor_password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"
unlinked_password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"
echo "::add-mask::$actor_password"
echo "::add-mask::$unlinked_password"

actor_identity="$(create_auth_identity "$actor_email" "$actor_password")"
IFS='|' read -r actor_provider_user actor_token actor_issuer actor_subject <<<"$actor_identity"
unlinked_identity="$(create_auth_identity "$unlinked_email" "$unlinked_password")"
IFS='|' read -r unlinked_provider_user unlinked_token unlinked_issuer unlinked_subject <<<"$unlinked_identity"

observation_operation="$(cat "$backup_dir/observation_operation_id")"
attachment_operation="$(cat "$backup_dir/attachment_operation_id")"
observation_id="$(cat "$backup_dir/observation_id")"
attachment_id="$(cat "$backup_dir/attachment_id")"
raw_text="$(cat "$backup_dir/raw_text")"

actor_id='10000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'

rpc_call() {
  local token="$1" function_name="$2" payload="$3" out
  out="$(mktemp)"
  RPC_STATUS="$(
    curl --silent --show-error --output "$out" --write-out '%{http_code}' \
      -X POST "$api_url/rest/v1/rpc/$function_name" \
      -H "apikey: $ANON_KEY" \
      -H "Authorization: Bearer $token" \
      -H 'Content-Type: application/json' \
      --data "$payload" || true
  )"
  RPC_BODY="$(cat "$out")"
  rm -f "$out"
}

prelink_operation='76000000-0000-4000-8000-000000000200'
prelink_payload="{\"p_operation_id\":\"$prelink_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Fresh provider identity must not map implicitly.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
rpc_call "$actor_token" create_observation "$prelink_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_ACTOR_NOT_FOUND"* ]]; then
  echo "Fresh provider identity unexpectedly mapped before explicit recovery relink: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 \
  -v actor_id="$actor_id" \
  -v new_issuer="$actor_issuer" \
  -v new_subject="$actor_subject" >/dev/null <<'SQL'
begin;
update public.identity_links
   set active = false
 where app_user_id = :'actor_id'::uuid
   and provider_key = 'supabase'
   and active;

insert into public.identity_links (
    app_user_id,
    provider_key,
    issuer,
    external_subject,
    active
) values (
    :'actor_id'::uuid,
    'supabase',
    :'new_issuer',
    :'new_subject',
    true
);
commit;
SQL

active_link_count="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select count(*) from public.identity_links where app_user_id = '$actor_id'::uuid and provider_key = 'supabase' and active"
)"
[[ "$active_link_count" == '1' ]] || {
  echo "Recovery relink expected exactly one active provider identity, got $active_link_count." >&2
  exit 1
}

observation_payload="{\"p_operation_id\":\"$observation_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"$raw_text\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"backup-restore-rehearsal\"}}"
rpc_call "$actor_token" create_observation "$observation_payload"
[[ "$RPC_STATUS" =~ ^2 ]] || {
  echo "Restored CreateObservation same-operation replay failed: $RPC_STATUS $RPC_BODY" >&2
  exit 1
}
restored_observation_id="$(
  printf '%s' "$RPC_BODY" | python3 -c 'import json,sys; print(json.load(sys.stdin)["observation_id"])'
)"
[[ "$restored_observation_id" == "$observation_id" ]] || {
  echo 'CreateObservation replay returned a different observation id.' >&2
  exit 1
}

different_payload="{\"p_operation_id\":\"$observation_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Different payload must fail.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"backup-restore-rehearsal\"}}"
rpc_call "$actor_token" create_observation "$different_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_OPERATION_REUSED_WITH_DIFFERENT_PAYLOAD"* ]]; then
  echo "Different-payload replay did not fail closed: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

attachment_payload="{\"p_operation_id\":\"$attachment_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_id\"}"
rpc_call "$actor_token" commit_observation_attachment "$attachment_payload"
[[ "$RPC_STATUS" =~ ^2 ]] || {
  echo "Restored Attachment same-operation replay failed: $RPC_STATUS $RPC_BODY" >&2
  exit 1
}

unlinked_operation='76000000-0000-4000-8000-000000000201'
unlinked_payload="{\"p_operation_id\":\"$unlinked_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Unlinked actor must fail.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
rpc_call "$unlinked_token" create_observation "$unlinked_payload"
if [[ "$RPC_STATUS" =~ ^2 ]]; then
  echo 'Unlinked provider identity unexpectedly created a Teaching Fact after restore.' >&2
  exit 1
fi

observation_count="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select count(*) from public.observations where operation_id = '$observation_operation'::uuid"
)"
attachment_count="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select count(*) from public.observation_attachments where operation_id = '$attachment_operation'::uuid"
)"
if [[ "$observation_count" != '1' || "$attachment_count" != '1' ]]; then
  echo "Idempotency continuity failed after restore: observations=$observation_count attachments=$attachment_count" >&2
  exit 1
fi

echo 'Fresh isolated restore rehearsal passed: database rows, private object bytes, digests, authorization and operation replay remained coherent.'
