#!/usr/bin/env bash
set -euo pipefail

backup_dir="${1:?usage: restore_fictional_backup_rehearsal.sh BACKUP_DIR}"
archive="$backup_dir/archive.tar.gpg"
key_file="$backup_dir/rehearsal.key"

now_ms() {
  date +%s%3N
}
restore_started_ms="$(now_ms)"

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

integrity_started_ms="$(now_ms)"
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
integrity_finished_ms="$(now_ms)"

# A restore target must be reconstructed from versioned source, never by
# copying the already-running source provider directory. The source workdir
# contains ignored Supabase runtime state (.temp/.branches) that can pin local
# API services to the wrong project/container and invalidate the rehearsal.
git archive --format=tar HEAD backend | tar -xf - -C "$target_root"

if [[ -e "$target_backend/supabase/.temp" || -e "$target_backend/supabase/.branches" ]]; then
  echo "Fresh restore target unexpectedly contains Supabase runtime state." >&2
  exit 1
fi

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

db_restore_started_ms="$(now_ms)"
docker cp "$extract_dir/database/xueqing.dump" "$db_container:/tmp/xueqing.dump"
while IFS= read -r table; do
  [[ -n "$table" ]] || continue
  docker exec "$db_container" pg_restore \
    -U postgres -d postgres \
    --data-only \
    --no-owner \
    --no-acl \
    --exit-on-error \
    --strict-names \
    --schema=public \
    --table="$table" \
    /tmp/xueqing.dump
done < "$restore_root/restore-order.txt"

target_table_state="$(bash tools/backup/snapshot_public_table_state.sh "$db_container")"
TABLE_STATE_JSON="$target_table_state" python3 - "$manifest" <<'PY'
import json
import os
import pathlib
import sys

manifest = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding="utf-8"))
target = json.loads(os.environ["TABLE_STATE_JSON"])
expected = {
    "row_counts": manifest["database"]["row_counts"],
    "table_fingerprints_sha256": manifest["database"]["table_fingerprints_sha256"],
}
if target["row_counts"] != expected["row_counts"]:
    all_tables = sorted(set(target["row_counts"]) | set(expected["row_counts"]))
    differences = {
        table: {
            "expected": expected["row_counts"].get(table),
            "actual": target["row_counts"].get(table),
        }
        for table in all_tables
        if target["row_counts"].get(table) != expected["row_counts"].get(table)
    }
    raise SystemExit(f"restored row-count mismatch: {differences}")
if target["table_fingerprints_sha256"] != expected["table_fingerprints_sha256"]:
    all_tables = sorted(
        set(target["table_fingerprints_sha256"])
        | set(expected["table_fingerprints_sha256"])
    )
    differences = [
        table
        for table in all_tables
        if target["table_fingerprints_sha256"].get(table)
        != expected["table_fingerprints_sha256"].get(table)
    ]
    raise SystemExit(
        "restored canonical table fingerprint mismatch: " + ", ".join(differences)
    )
PY

db_restore_finished_ms="$(now_ms)"

# Runtime isolation is part of restore acceptance. The source provider has
# already been stopped, so every still-running local Supabase service must
# belong to the fresh target project. Print only container names and fictional
# assignment state; never print runtime credentials.
mapfile -t running_supabase_containers < <(
  docker ps --format '{{.Names}}' | grep 'supabase_' | sort || true
)
printf 'Restore runtime containers:\n'
printf '  %s\n' "${running_supabase_containers[@]}"

unexpected_runtime="$(
  printf '%s\n' "${running_supabase_containers[@]}" \
    | grep -v 'xueqing-native-restore-rehearsal$' \
    || true
)"
if [[ -n "$unexpected_runtime" ]]; then
  echo 'Fresh restore runtime still contains containers from another Supabase project:' >&2
  printf '%s\n' "$unexpected_runtime" >&2
  exit 1
fi

mapfile -t running_db_containers < <(
  printf '%s\n' "${running_supabase_containers[@]}" | grep '^supabase_db_' || true
)
if [[ "${#running_db_containers[@]}" -ne 1 || "${running_db_containers[0]}" != "$db_container" ]]; then
  echo "Fresh restore expected exactly one target DB container ($db_container)." >&2
  printf 'Running DB containers: %s\n' "${running_db_containers[*]:-none}" >&2
  exit 1
fi

runtime_assignment_state="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select coalesce(json_agg(json_build_object('id', id, 'teacher_app_user_id', teacher_app_user_id, 'active', active) order by id)::text, '[]') from public.student_teacher_assignments"
)"
printf 'Target DB assignment state before Data API proof: %s\n' "$runtime_assignment_state"

actor_id='10000000-0000-0000-0000-000000000001'
actor_b_id='10000000-0000-0000-0000-000000000002'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
observation_operation="$(cat "$backup_dir/observation_operation_id")"
attachment_operation="$(cat "$backup_dir/attachment_operation_id")"
observation_id="$(cat "$backup_dir/observation_id")"
attachment_id="$(cat "$backup_dir/attachment_id")"
raw_text="$(cat "$backup_dir/raw_text")"

docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 \
  -v actor_id="$actor_id" \
  -v organization_id="$organization_id" \
  -v student_id="$student_id" \
  -v profile_id="$profile_id" \
  -v assignment_id="$assignment_id" \
  -v observation_id="$observation_id" \
  -v observation_operation="$observation_operation" \
  -v attachment_id="$attachment_id" \
  -v attachment_operation="$attachment_operation" >/dev/null <<'SQL'
select pg_catalog.set_config('xq.actor_id', :'actor_id', false);
select pg_catalog.set_config('xq.organization_id', :'organization_id', false);
select pg_catalog.set_config('xq.student_id', :'student_id', false);
select pg_catalog.set_config('xq.profile_id', :'profile_id', false);
select pg_catalog.set_config('xq.assignment_id', :'assignment_id', false);
select pg_catalog.set_config('xq.observation_id', :'observation_id', false);
select pg_catalog.set_config('xq.observation_operation', :'observation_operation', false);
select pg_catalog.set_config('xq.attachment_id', :'attachment_id', false);
select pg_catalog.set_config('xq.attachment_operation', :'attachment_operation', false);

do $xq$
begin
    if not exists (select 1 from public.app_users where id = pg_catalog.current_setting('xq.actor_id')::uuid) then
        raise exception 'XQ_RESTORE_APP_USER_ID_MISSING';
    end if;
    if not exists (select 1 from public.organizations where id = pg_catalog.current_setting('xq.organization_id')::uuid) then
        raise exception 'XQ_RESTORE_ORGANIZATION_ID_MISSING';
    end if;
    if not exists (select 1 from public.students where id = pg_catalog.current_setting('xq.student_id')::uuid) then
        raise exception 'XQ_RESTORE_STUDENT_ID_MISSING';
    end if;
    if not exists (
        select 1 from public.student_subject_profiles where id = pg_catalog.current_setting('xq.profile_id')::uuid
    ) then
        raise exception 'XQ_RESTORE_PROFILE_ID_MISSING';
    end if;
    if not exists (
        select 1 from public.student_teacher_assignments where id = pg_catalog.current_setting('xq.assignment_id')::uuid
    ) then
        raise exception 'XQ_RESTORE_ASSIGNMENT_ID_MISSING';
    end if;
    if not exists (
        select 1 from public.observations
         where id = pg_catalog.current_setting('xq.observation_id')::uuid
           and operation_id = pg_catalog.current_setting('xq.observation_operation')::uuid
    ) then
        raise exception 'XQ_RESTORE_OBSERVATION_ID_MISSING';
    end if;
    if not exists (
        select 1 from public.observation_attachments
         where id = pg_catalog.current_setting('xq.attachment_id')::uuid
           and operation_id = pg_catalog.current_setting('xq.attachment_operation')::uuid
    ) then
        raise exception 'XQ_RESTORE_ATTACHMENT_ID_MISSING';
    end if;
    if (
        select count(*)
          from public.operation_receipts
         where operation_id in (
             pg_catalog.current_setting('xq.observation_operation')::uuid,
             pg_catalog.current_setting('xq.attachment_operation')::uuid
         )
    ) <> 2 then
        raise exception 'XQ_RESTORE_OPERATION_RECEIPTS_MISSING';
    end if;
end;
$xq$;
SQL

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
object_restore_started_ms="$(now_ms)"
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
object_restore_finished_ms="$(now_ms)"

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
actor_email="restore-actor-a-$suffix@example.com"
actor_b_email="restore-actor-b-$suffix@example.com"
unlinked_email="restore-unlinked-$suffix@example.com"
actor_password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"
actor_b_password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"
unlinked_password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"
echo "::add-mask::$actor_password"
echo "::add-mask::$actor_b_password"
echo "::add-mask::$unlinked_password"

actor_identity="$(create_auth_identity "$actor_email" "$actor_password")"
IFS='|' read -r actor_provider_user actor_token actor_issuer actor_subject <<<"$actor_identity"
actor_b_identity="$(create_auth_identity "$actor_b_email" "$actor_b_password")"
IFS='|' read -r actor_b_provider_user actor_b_token actor_b_issuer actor_b_subject <<<"$actor_b_identity"
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
  echo "Fresh provider identity A unexpectedly mapped before explicit recovery relink: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

actor_b_prelink_operation='76000000-0000-4000-8000-000000000202'
actor_b_prelink_payload="{\"p_operation_id\":\"$actor_b_prelink_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Fresh provider identity B must not map implicitly.\",\"p_client_capture_metadata\":{\"fixture\":true}}"
rpc_call "$actor_b_token" create_observation "$actor_b_prelink_payload"
if [[ "$RPC_STATUS" =~ ^2 ]] || [[ "$RPC_BODY" != *"XQ_ACTOR_NOT_FOUND"* ]]; then
  echo "Fresh provider identity B unexpectedly mapped before explicit recovery relink: $RPC_STATUS $RPC_BODY" >&2
  exit 1
fi

docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 \
  -v actor_id="$actor_id" \
  -v actor_b_id="$actor_b_id" \
  -v new_issuer="$actor_issuer" \
  -v new_subject="$actor_subject" \
  -v actor_b_issuer="$actor_b_issuer" \
  -v actor_b_subject="$actor_b_subject" >/dev/null <<'SQL'
begin;
update public.identity_links
   set active = false
 where app_user_id in (:'actor_id'::uuid, :'actor_b_id'::uuid)
   and provider_key = 'supabase'
   and active;

insert into public.identity_links (
    app_user_id,
    provider_key,
    issuer,
    external_subject,
    active
) values
    (
        :'actor_id'::uuid,
        'supabase',
        :'new_issuer',
        :'new_subject',
        true
    ),
    (
        :'actor_b_id'::uuid,
        'supabase',
        :'actor_b_issuer',
        :'actor_b_subject',
        true
    );
commit;
SQL

active_link_count="$(
  docker exec "$db_container" psql -U postgres -d postgres -Atc \
    "select count(*) from public.identity_links where app_user_id in ('$actor_id'::uuid, '$actor_b_id'::uuid) and provider_key = 'supabase' and active"
)"
[[ "$active_link_count" == '2' ]] || {
  echo "Recovery relink expected exactly two active provider identities for A/B, got $active_link_count." >&2
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

make_legacy_tuple_jwt() {
  JWT_SUBJECT="$1" JWT_ISSUER="$2" python3 - <<'PY'
import base64
import hashlib
import hmac
import json
import os
import time

def b64url(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")

now = int(time.time())
header = {"alg": "HS256", "typ": "JWT"}
payload = {
    "iss": os.environ["JWT_ISSUER"],
    "sub": os.environ["JWT_SUBJECT"],
    "aud": "authenticated",
    "role": "authenticated",
    "iat": now,
    "exp": now + 3600,
}
encoded_header = b64url(json.dumps(header, separators=(",", ":")).encode())
encoded_payload = b64url(json.dumps(payload, separators=(",", ":")).encode())
signing_input = f"{encoded_header}.{encoded_payload}".encode()
signature = hmac.new(
    os.environ["JWT_SECRET"].encode(),
    signing_input,
    hashlib.sha256,
).digest()
print(f"{encoded_header}.{encoded_payload}.{b64url(signature)}")
PY
}

revoked_token="$(make_legacy_tuple_jwt 'a0000000-0000-0000-0000-000000000003' 'supabase-demo')"
echo "::add-mask::$revoked_token"

history_file="$backup_dir/history_fixture.json"
[[ -s "$history_file" ]] || {
  echo "Recovery history fixture metadata is missing." >&2
  exit 1
}

API_URL="$api_url" \
ANON_KEY="$ANON_KEY" \
SERVICE_ROLE_KEY="$SERVICE_ROLE_KEY" \
DB_CONTAINER="$db_container" \
ACTOR_TOKEN_A="$actor_token" \
ACTOR_TOKEN_B="$actor_b_token" \
REVOKED_TOKEN="$revoked_token" \
bash backend/supabase/tests/verify_fictional_recovery_history.sh "$history_file"

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

restore_finished_ms="$(now_ms)"
integrity_validation_duration_ms="$((integrity_finished_ms - integrity_started_ms))"
database_restore_duration_ms="$((db_restore_finished_ms - db_restore_started_ms))"
object_restore_duration_ms="$((object_restore_finished_ms - object_restore_started_ms))"
semantic_validation_duration_ms="$((restore_finished_ms - object_restore_finished_ms))"
restore_total_duration_ms="$((restore_finished_ms - restore_started_ms))"

INTEGRITY_VALIDATION_DURATION_MS="$integrity_validation_duration_ms" \
DATABASE_RESTORE_DURATION_MS="$database_restore_duration_ms" \
OBJECT_RESTORE_DURATION_MS="$object_restore_duration_ms" \
SEMANTIC_VALIDATION_DURATION_MS="$semantic_validation_duration_ms" \
RESTORE_TOTAL_DURATION_MS="$restore_total_duration_ms" \
python3 - "$backup_dir/restore_metrics.json" <<'PY'
import json
import os
import pathlib
import sys

metrics = {
    "integrity_validation_duration_ms": int(os.environ["INTEGRITY_VALIDATION_DURATION_MS"]),
    "database_restore_duration_ms": int(os.environ["DATABASE_RESTORE_DURATION_MS"]),
    "object_restore_duration_ms": int(os.environ["OBJECT_RESTORE_DURATION_MS"]),
    "semantic_validation_duration_ms": int(os.environ["SEMANTIC_VALIDATION_DURATION_MS"]),
    "restore_total_duration_ms": int(os.environ["RESTORE_TOTAL_DURATION_MS"]),
}
pathlib.Path(sys.argv[1]).write_text(
    json.dumps(metrics, indent=2, sort_keys=True) + "\n",
    encoding="utf-8",
)
PY

echo 'Fresh isolated restore rehearsal passed: database rows, private object bytes, digests, authorization and operation replay remained coherent.'
