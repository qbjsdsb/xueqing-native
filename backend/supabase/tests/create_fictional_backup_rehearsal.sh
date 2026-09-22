#!/usr/bin/env bash
set -euo pipefail

output_dir="${1:?usage: create_fictional_backup_rehearsal.sh OUTPUT_DIR}"
rm -rf "$output_dir"
mkdir -p "$output_dir/plain/database" "$output_dir/plain/objects"

status_env="$(mktemp)"
supabase status --workdir backend -o env > "$status_env"
set -a
# shellcheck disable=SC1090
source "$status_env"
set +a
rm -f "$status_env"

: "${API_URL:?API_URL missing from local Supabase status}"
: "${ANON_KEY:?ANON_KEY missing from local Supabase status}"
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY missing from local Supabase status}"
: "${JWT_SECRET:?JWT_SECRET missing from local Supabase status}"

api_url="${API_URL%/}"
bucket='teaching-attachments-v1'
actor_id='10000000-0000-0000-0000-000000000001'
auth_subject='a0000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
observation_operation='76000000-0000-4000-8000-000000000101'
attachment_id='76000000-0000-4000-8000-000000000102'
attachment_operation='76000000-0000-4000-8000-000000000103'
raw_text='Backup restore fictional observation.'

echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"

db_container='supabase_db_xueqing-native-local'
if ! docker ps --format '{{.Names}}' | grep -Fxq "$db_container"; then
  echo "Expected source database container is not running: $db_container" >&2
  echo 'Running local Supabase DB containers:' >&2
  docker ps --filter 'name=supabase_db_' --format '  {{.Names}}' >&2 || true
  exit 1
fi

make_jwt() {
  JWT_SUBJECT="$1" python3 - <<'PY'
import base64
import hashlib
import hmac
import json
import os
import time

def b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b'=').decode('ascii')

now = int(time.time())
header = {"alg": "HS256", "typ": "JWT"}
payload = {
    "iss": "supabase-demo",
    "sub": os.environ["JWT_SUBJECT"],
    "aud": "authenticated",
    "role": "authenticated",
    "iat": now,
    "exp": now + 3600,
}
encoded_header = b64url(json.dumps(header, separators=(',', ':')).encode())
encoded_payload = b64url(json.dumps(payload, separators=(',', ':')).encode())
signing_input = f"{encoded_header}.{encoded_payload}".encode()
signature = hmac.new(
    os.environ["JWT_SECRET"].encode(),
    signing_input,
    hashlib.sha256,
).digest()
print(f"{encoded_header}.{encoded_payload}.{b64url(signature)}")
PY
}

actor_token="$(make_jwt "$auth_subject")"
echo "::add-mask::$actor_token"

rpc() {
  local function_name="$1" payload="$2"
  curl --fail-with-body --silent --show-error \
    -X POST "$api_url/rest/v1/rpc/$function_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $actor_token" \
    -H 'Content-Type: application/json' \
    --data "$payload"
}

observation_response="$(
  rpc create_observation \
    "{\"p_operation_id\":\"$observation_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"$raw_text\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"backup-restore-rehearsal\"}}"
)"
observation_id="$(
  printf '%s' "$observation_response" | python3 -c \
    'import json,sys; value=json.load(sys.stdin); print(value["observation_id"])'
)"

object_name="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$attachment_id"
png_file="$(mktemp --suffix=.png)"
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII=' \
  | base64 -d > "$png_file"

curl --fail-with-body --silent --show-error \
  -X POST "$api_url/storage/v1/object/$bucket/$object_name" \
  -H "apikey: $ANON_KEY" \
  -H "Authorization: Bearer $actor_token" \
  -H 'Content-Type: image/png' \
  --data-binary "@$png_file" >/dev/null

attachment_response="$(
  rpc commit_observation_attachment \
    "{\"p_operation_id\":\"$attachment_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_id\"}"
)"
printf '%s' "$attachment_response" | python3 -c \
  'import json,sys; value=json.load(sys.stdin); assert value["bucket_id"]=="teaching-attachments-v1"'

API_URL="$api_url" \
ANON_KEY="$ANON_KEY" \
SERVICE_ROLE_KEY="$SERVICE_ROLE_KEY" \
ACTOR_TOKEN="$actor_token" \
DB_CONTAINER="$db_container" \
bash backend/supabase/tests/seed_fictional_recovery_history.sh \
  "$output_dir/history_fixture.json"

source_assignment_state="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select coalesce(json_agg(json_build_object('id', id, 'teacher_app_user_id', teacher_app_user_id, 'active', active) order by id)::text, '[]') from public.student_teacher_assignments"
)"
printf 'Source DB assignment state immediately before pg_dump: %s\n' "$source_assignment_state"

docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 >/dev/null <<'SQL'
do $xq$
begin
    if not exists (
        select 1
          from public.student_teacher_assignments
         where id = '50000000-0000-0000-0000-000000000001'::uuid
           and teacher_app_user_id = '10000000-0000-0000-0000-000000000001'::uuid
           and not active
    ) then
        raise exception 'XQ_BACKUP_SOURCE_FORMER_ASSIGNMENT_NOT_INACTIVE_AT_DUMP_BOUNDARY';
    end if;
    if not exists (
        select 1
          from public.student_teacher_assignments
         where id = '50000000-0000-0000-0000-000000000104'::uuid
           and teacher_app_user_id = '10000000-0000-0000-0000-000000000002'::uuid
           and active
    ) then
        raise exception 'XQ_BACKUP_SOURCE_CURRENT_ASSIGNMENT_NOT_ACTIVE_AT_DUMP_BOUNDARY';
    end if;
end;
$xq$;
SQL

db_started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
docker exec "$db_container" pg_dump \
  -U postgres -d postgres \
  --format=custom \
  --data-only \
  --schema=public \
  --no-owner \
  --no-acl > "$output_dir/plain/database/xueqing.dump"
db_finished_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

object_started_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
object_file="$output_dir/plain/objects/$attachment_id.bin"
curl --fail-with-body --silent --show-error \
  -X GET "$api_url/storage/v1/object/authenticated/$bucket/$object_name" \
  -H "apikey: $SERVICE_ROLE_KEY" \
  -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
  --output "$object_file"
object_finished_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

expected_size="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select byte_size from public.observation_attachments where id = '$attachment_id'::uuid"
)"
actual_size="$(wc -c < "$object_file" | tr -d ' ')"
if [[ "$expected_size" != "$actual_size" ]]; then
  echo "Backed-up object byte length mismatch: expected=$expected_size actual=$actual_size" >&2
  exit 1
fi

database_sha="$(sha256sum "$output_dir/plain/database/xueqing.dump" | awk '{print $1}')"
database_size="$(wc -c < "$output_dir/plain/database/xueqing.dump" | tr -d ' ')"
object_sha="$(sha256sum "$object_file" | awk '{print $1}')"
schema_sha="$(cat backend/supabase/migrations/*.sql | sha256sum | awk '{print $1}')"
source_commit="$(git rev-parse HEAD)"
created_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

mapfile -t migrations < <(
  find backend/supabase/migrations -maxdepth 1 -type f -name '*.sql' -printf '%f\n' \
    | sed 's/\.sql$//' \
    | sort
)

table_state_json="$(bash tools/backup/snapshot_public_table_state.sh "$db_container")"
migrations_json="$(
  printf '%s\n' "${migrations[@]}" | python3 -c '
import json,sys
print(json.dumps([line.strip() for line in sys.stdin if line.strip()], separators=(",",":")))
'
)"

SOURCE_COMMIT="$source_commit" CREATED_AT="$created_at" \
DB_STARTED_AT="$db_started_at" DB_FINISHED_AT="$db_finished_at" \
OBJECT_STARTED_AT="$object_started_at" OBJECT_FINISHED_AT="$object_finished_at" \
DATABASE_SHA="$database_sha" DATABASE_SIZE="$database_size" \
OBJECT_NAME="$object_name" OBJECT_SHA="$object_sha" OBJECT_SIZE="$actual_size" \
ATTACHMENT_ID="$attachment_id" SCHEMA_SHA="$schema_sha" \
MIGRATIONS_JSON="$migrations_json" TABLE_STATE_JSON="$table_state_json" \
python3 - "$output_dir/plain/manifest.json" <<'PY'
import json
import os
import pathlib
import sys
import uuid

target = pathlib.Path(sys.argv[1])
migrations = json.loads(os.environ["MIGRATIONS_JSON"])
table_state = json.loads(os.environ["TABLE_STATE_JSON"])
manifest = {
    "schema_version": 1,
    "archive_id": str(uuid.uuid4()),
    "created_at": os.environ["CREATED_AT"],
    "source": {
        "provider_id": "supabase-local-fictional",
        "environment_id": "backup-restore-source",
        "region": "ap-southeast-1",
        "source_commit": os.environ["SOURCE_COMMIT"],
        "migration_count": len(migrations),
        "migrations": migrations,
        "schema_fingerprint_sha256": os.environ["SCHEMA_SHA"],
    },
    "consistency": {
        "strategy": "quiesced-window",
        "boundary_id": "fictional-local-rehearsal",
        "database_started_at": os.environ["DB_STARTED_AT"],
        "database_finished_at": os.environ["DB_FINISHED_AT"],
        "objects_started_at": os.environ["OBJECT_STARTED_AT"],
        "objects_finished_at": os.environ["OBJECT_FINISHED_AT"],
        "maximum_data_loss_window_seconds": 0,
    },
    "database": {
        "format": "postgresql-custom",
        "tool": "pg_dump",
        "tool_version": "17-local-container",
        "archive_relative_path": "database/xueqing.dump",
        "byte_length": int(os.environ["DATABASE_SIZE"]),
        "sha256": os.environ["DATABASE_SHA"],
        "row_counts": table_state["row_counts"],
        "table_fingerprints_sha256": table_state["table_fingerprints_sha256"],
    },
    "storage": {
        "bucket_id": "teaching-attachments-v1",
        "private": True,
        "object_count": 1,
        "objects": [{
            "logical_object_locator": os.environ["OBJECT_NAME"],
            "archive_relative_path": f"objects/{os.environ['ATTACHMENT_ID']}.bin",
            "byte_length": int(os.environ["OBJECT_SIZE"]),
            "mime_type": "image/png",
            "sha256": os.environ["OBJECT_SHA"],
        }],
    },
    "security": {
        "archive_encrypted": True,
        "encryption_scheme": "gpg-symmetric-aes256-fictional-ci",
        "key_stored_separately": True,
        "production_material_in_public_ci": False,
    },
}
target.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
PY

python3 tools/backup/verify_backup_manifest.py "$output_dir/plain/manifest.json"

command -v gpg >/dev/null
key_file="$output_dir/rehearsal.key"
python3 -c 'import secrets; print(secrets.token_urlsafe(48))' > "$key_file"
chmod 600 "$key_file"

tar -C "$output_dir/plain" -cf "$output_dir/archive.tar" database objects manifest.json
gpg --batch --yes --pinentry-mode loopback \
  --passphrase-file "$key_file" \
  --symmetric --cipher-algo AES256 \
  --output "$output_dir/archive.tar.gpg" \
  "$output_dir/archive.tar"
rm -f "$output_dir/archive.tar"
rm -rf "$output_dir/plain"
rm -f "$png_file"

printf '%s\n' "$observation_operation" > "$output_dir/observation_operation_id"
printf '%s\n' "$attachment_operation" > "$output_dir/attachment_operation_id"
printf '%s\n' "$observation_id" > "$output_dir/observation_id"
printf '%s\n' "$attachment_id" > "$output_dir/attachment_id"
printf '%s\n' "$object_name" > "$output_dir/object_name"
printf '%s\n' "$raw_text" > "$output_dir/raw_text"

echo "Fictional backup rehearsal archive created and encrypted; no archive is uploaded."
