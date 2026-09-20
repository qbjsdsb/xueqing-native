#!/usr/bin/env bash
set -euo pipefail

bucket='teaching-attachments-v1'
actor_id='10000000-0000-0000-0000-000000000001'
legacy_auth_subject='a0000000-0000-0000-0000-000000000001'
decoy_auth_subject='d0000000-0000-0000-0000-000000000001'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_id='50000000-0000-0000-0000-000000000001'
observation_operation='9b000000-0000-4000-8000-00000000c001'
attachment_a='9b100000-0000-4000-8000-00000000c001'
attachment_b='9b100000-0000-4000-8000-00000000c002'
attachment_after_revoke='9b100000-0000-4000-8000-00000000c003'
commit_a_operation='9b200000-0000-4000-8000-00000000c001'
commit_b_operation='9b200000-0000-4000-8000-00000000c002'

suffix="${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}"
email_a="provider-storage-a-$suffix@example.com"
email_b="provider-storage-b-$suffix@example.com"
password_a="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+"" .join(secrets.choice(a) for _ in range(24)))' 2>/dev/null || python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"
password_b="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+ "".join(secrets.choice(a) for _ in range(24)))')"

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
api_url="${API_URL%/}"

echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"
echo "::add-mask::$password_a"
echo "::add-mask::$password_b"

db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
if [[ -z "$db_container" ]]; then
  echo 'Local Supabase database container was not found.' >&2
  exit 1
fi

psql_db() {
  docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 "$@"
}

# Inspect final migrated definitions rather than immutable historical migrations.
psql_db >/dev/null <<'SQL'
do $xq$
declare
    v_signature text;
    v_definition text;
begin
    foreach v_signature in array array[
        'xq_internal.can_upload_observation_attachment_v1(text)',
        'xq_internal.can_read_observation_attachment_v1(text)'
    ]
    loop
        select pg_catalog.pg_get_functiondef(v_signature::pg_catalog.regprocedure)
          into v_definition;

        if v_definition ~* 'auth[.](jwt|uid)[[:space:]]*[(]' then
            raise exception 'XQ_PROVIDER_STORAGE_DIRECT_AUTH_DEPENDENCY: %', v_signature;
        end if;
    end loop;
end;
$xq$;
SQL

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

  user_response="$(curl -fsS -X POST "$api_url/auth/v1/admin/users" \
    -H "apikey: $SERVICE_ROLE_KEY" \
    -H "Authorization: Bearer $SERVICE_ROLE_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$email\",\"password\":\"$password\",\"email_confirm\":true}")"
  user_id="$(printf '%s' "$user_response" | json_get id)"

  session="$(curl -fsS -X POST "$api_url/auth/v1/token?grant_type=password" \
    -H "apikey: $ANON_KEY" \
    -H 'Content-Type: application/json' \
    --data "{\"email\":\"$email\",\"password\":\"$password\"}")"
  token="$(printf '%s' "$session" | json_get access_token)"
  issuer="$(jwt_claim "$token" iss)"
  subject="$(jwt_claim "$token" sub)"

  [[ -n "$user_id" && -n "$token" && -n "$issuer" && "$subject" == "$user_id" ]] || {
    echo "Provider identity creation failed for $email." >&2
    exit 1
  }
  echo "::add-mask::$token" >&2
  printf '%s|%s|%s|%s\n' "$user_id" "$token" "$issuer" "$subject"
}

rpc_status=''
rpc_body=''
call_rpc() {
  local token="$1" function_name="$2" payload="$3" out
  out="$(mktemp)"
  rpc_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X POST "$api_url/rest/v1/rpc/$function_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: application/json' \
    --data "$payload")"
  rpc_body="$(cat "$out")"
  rm -f "$out"
}

expect_rpc_success() {
  local label="$1"
  if [[ ! "$rpc_status" =~ ^2 ]]; then
    echo "$label expected success, got HTTP $rpc_status: $rpc_body" >&2
    exit 1
  fi
}

storage_status=''
storage_body=''
upload_object() {
  local token="$1" object_name="$2" file="$3" out
  out="$(mktemp)"
  storage_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X POST "$api_url/storage/v1/object/$bucket/$object_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token" \
    -H 'Content-Type: image/png' \
    --data-binary "@$file")"
  storage_body="$(cat "$out")"
  rm -f "$out"
}

download_object() {
  local token="$1" object_name="$2" out
  out="$(mktemp)"
  storage_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X GET "$api_url/storage/v1/object/authenticated/$bucket/$object_name" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token")"
  storage_body="$(cat "$out" 2>/dev/null || true)"
  rm -f "$out"
}

download_object_anon() {
  local object_name="$1" out
  out="$(mktemp)"
  storage_status="$(curl -sS -o "$out" -w '%{http_code}' \
    -X GET "$api_url/storage/v1/object/authenticated/$bucket/$object_name" \
    -H "apikey: $ANON_KEY")"
  storage_body="$(cat "$out" 2>/dev/null || true)"
  rm -f "$out"
}

expect_storage_success() {
  local label="$1"
  if [[ ! "$storage_status" =~ ^2 ]]; then
    echo "$label expected Storage success, got HTTP $storage_status: $storage_body" >&2
    exit 1
  fi
}

expect_storage_denied() {
  local label="$1"
  if [[ "$storage_status" =~ ^2 ]]; then
    echo "$label unexpectedly succeeded: $storage_body" >&2
    exit 1
  fi
}

assert_business_receipt_clean() {
  local body="$1"
  printf '%s' "$body" | \
    SUBJECT_A="$subject_a" SUBJECT_B="$subject_b" ISSUER_A="$issuer_a" ISSUER_B="$issuer_b" \
    python3 -c '
import json,os,sys
value=json.load(sys.stdin)
serialized=json.dumps(value,ensure_ascii=False,sort_keys=True)
for name in ("SUBJECT_A","SUBJECT_B","ISSUER_A","ISSUER_B"):
    marker=os.environ.get(name,"")
    if marker and marker in serialized:
        raise SystemExit(f"provider identity leaked into business receipt: {name}")
'
}

user_a=''
user_b=''
token_a=''
token_b=''
issuer_a=''
issuer_b=''
subject_a=''
subject_b=''
object_a=''
object_b=''
object_after_revoke=''
tmp_png="$(mktemp --suffix=.png)"

cleanup() {
  set +e
  psql_db >/dev/null 2>&1 <<SQL
reset role;
delete from public.observation_attachments
 where id in ('$attachment_a'::uuid, '$attachment_b'::uuid);
delete from public.operation_receipts
 where operation_id in (
   '$observation_operation'::uuid,
   '$commit_a_operation'::uuid,
   '$commit_b_operation'::uuid
 );
delete from public.observations where operation_id = '$observation_operation'::uuid;
delete from public.identity_links
 where provider_key = 'supabase'
   and external_subject in ('$subject_a', '$subject_b')
   and app_user_id = '$actor_id'::uuid;
update public.app_users
   set auth_subject = '$legacy_auth_subject'::uuid,
       enabled = true
 where id = '$actor_id'::uuid;
SQL

  for path in "$object_a" "$object_b" "$object_after_revoke"; do
    if [[ -n "$path" ]]; then
      curl -sS -o /dev/null -X DELETE \
        "$api_url/storage/v1/object/$bucket/$path" \
        -H "apikey: $SERVICE_ROLE_KEY" \
        -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
    fi
  done

  for id in "$user_a" "$user_b"; do
    if [[ -n "$id" ]]; then
      curl -sS -o /dev/null -X DELETE "$api_url/auth/v1/admin/users/$id" \
        -H "apikey: $SERVICE_ROLE_KEY" \
        -H "Authorization: Bearer $SERVICE_ROLE_KEY" || true
    fi
  done

  rm -f "$tmp_png"
}
trap cleanup EXIT

printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII=' \
  | base64 -d > "$tmp_png"

identity_a="$(create_auth_identity "$email_a" "$password_a")"
IFS='|' read -r user_a token_a issuer_a subject_a <<<"$identity_a"
identity_b="$(create_auth_identity "$email_b" "$password_b")"
IFS='|' read -r user_b token_b issuer_b subject_b <<<"$identity_b"

psql_db >/dev/null <<SQL
insert into public.identity_links (
  app_user_id, provider_key, issuer, external_subject, active
) values
  ('$actor_id'::uuid, 'supabase', '$issuer_a', '$subject_a', true),
  ('$actor_id'::uuid, 'supabase', '$issuer_b', '$subject_b', true);

update public.app_users
   set auth_subject = '$decoy_auth_subject'::uuid,
       enabled = true
 where id = '$actor_id'::uuid;
SQL

call_rpc "$token_a" create_observation \
  "{\"p_operation_id\":\"$observation_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_raw_text\":\"Provider Storage conformance fictional observation.\",\"p_client_capture_metadata\":{\"fixture\":true,\"source\":\"provider-storage-conformance\"}}"
expect_rpc_success 'Identity A CreateObservation'
observation_id="$(printf '%s' "$rpc_body" | json_get observation_id)"
[[ -n "$observation_id" ]] || { echo 'CreateObservation returned no observation id.' >&2; exit 1; }

object_a="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$attachment_a"
object_b="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$attachment_b"
object_after_revoke="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$attachment_after_revoke"

upload_object "$token_a" "$object_a" "$tmp_png"
expect_storage_success 'Identity A canonical upload'

upload_object "$token_b" "$object_b" "$tmp_png"
expect_storage_success 'Identity B canonical upload through same AppUser authority'

call_rpc "$token_b" commit_observation_attachment \
  "{\"p_operation_id\":\"$commit_a_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_a\"}"
expect_rpc_success 'Identity B commits identity A upload'
[[ "$(printf '%s' "$rpc_body" | json_get actor_app_user_id)" == "$actor_id" ]] || {
  echo 'Attachment A receipt actor did not resolve to application-owned AppUser.' >&2
  exit 1
}
[[ "$(printf '%s' "$rpc_body" | json_get object_name)" == "$object_a" ]] || {
  echo 'Attachment A receipt object identity mismatch.' >&2
  exit 1
}
assert_business_receipt_clean "$rpc_body"

call_rpc "$token_a" commit_observation_attachment \
  "{\"p_operation_id\":\"$commit_b_operation\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_id\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$attachment_b\"}"
expect_rpc_success 'Identity A commits identity B upload'
[[ "$(printf '%s' "$rpc_body" | json_get actor_app_user_id)" == "$actor_id" ]] || {
  echo 'Attachment B receipt actor did not resolve to application-owned AppUser.' >&2
  exit 1
}
[[ "$(printf '%s' "$rpc_body" | json_get object_name)" == "$object_b" ]] || {
  echo 'Attachment B receipt object identity mismatch.' >&2
  exit 1
}
assert_business_receipt_clean "$rpc_body"

metadata_count="$(psql_db -Atc "select count(*) from public.observation_attachments where id in ('$attachment_a'::uuid, '$attachment_b'::uuid)")"
receipt_count="$(psql_db -Atc "select count(*) from public.operation_receipts where operation_id in ('$commit_a_operation'::uuid, '$commit_b_operation'::uuid)")"
if [[ "$metadata_count" != '2' || "$receipt_count" != '2' ]]; then
  echo "Provider Storage cross-identity commit mismatch: metadata=$metadata_count receipts=$receipt_count" >&2
  exit 1
fi

for token in "$token_a" "$token_b"; do
  download_object "$token" "$object_a"
  expect_storage_success 'Linked identity reads committed attachment A'
  download_object "$token" "$object_b"
  expect_storage_success 'Linked identity reads committed attachment B'
done

download_object_anon "$object_a"
expect_storage_denied 'Private attachment rejects unauthenticated read'

psql_db -Atc "update public.identity_links set active = false where provider_key = 'supabase' and issuer = '$issuer_a' and external_subject = '$subject_a'" >/dev/null

download_object "$token_a" "$object_a"
expect_storage_denied 'Revoked IdentityLink A loses committed attachment read'

upload_object "$token_a" "$object_after_revoke" "$tmp_png"
expect_storage_denied 'Revoked IdentityLink A loses attachment upload'

download_object "$token_b" "$object_a"
expect_storage_success 'Sibling active IdentityLink B retains attachment read'

download_object "$token_b" "$object_b"
expect_storage_success 'Sibling active IdentityLink B retains equivalent attachment authority'

echo 'Provider Storage conformance passed: private Attachment Storage authority resolves through IdentityLink to stable AppUser semantics and remains fail-closed on provider-identity revoke.'
