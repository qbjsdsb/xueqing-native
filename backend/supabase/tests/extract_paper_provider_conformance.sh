#!/usr/bin/env bash
set -euo pipefail

bucket='teaching-attachments-v1'
actor_a='10000000-0000-0000-0000-000000000001'
actor_b='10000000-0000-0000-0000-000000000002'
organization_id='20000000-0000-0000-0000-000000000001'
student_id='30000000-0000-0000-0000-000000000001'
profile_id='40000000-0000-0000-0000-000000000001'
assignment_a='50000000-0000-0000-0000-000000000001'

obs_op='99700000-0000-0000-0000-000000000001'
att1_id='99700000-0000-0000-0000-000000000011'
att1_op='99700000-0000-0000-0000-000000000012'
att2_id='99700000-0000-0000-0000-000000000021'
att2_op='99700000-0000-0000-0000-000000000022'
att3_id='99700000-0000-0000-0000-000000000023'
att3_op='99700000-0000-0000-0000-000000000024'
paper1_create_op='99700000-0000-0000-0000-000000000031'
paper1_extract_op='99700000-0000-0000-0000-000000000032'
paper1_confirm_op='99700000-0000-0000-0000-000000000033'
paper1_retry_op='99700000-0000-0000-0000-000000000034'
paper2_create_op='99700000-0000-0000-0000-000000000041'
paper2_extract_op='99700000-0000-0000-0000-000000000042'

status_env="$(mktemp)"
function_log="$(mktemp)"
function_env="$(mktemp)"
mock_log="$(mktemp)"
mock_requests="$(mktemp)"
mock_port_file="$(mktemp)"
tmp_png="$(mktemp --suffix=.png)"
function_pid=''
mock_pid=''
user_a=''
user_b=''
object_1=''
object_2=''
object_3=''

supabase status --workdir backend -o env > "$status_env"
set -a
# shellcheck disable=SC1090
source "$status_env"
set +a

: "${API_URL:?API_URL missing from local Supabase status}"
: "${ANON_KEY:?ANON_KEY missing from local Supabase status}"
: "${SERVICE_ROLE_KEY:?SERVICE_ROLE_KEY missing from local Supabase status}"
api_url="${API_URL%/}"

echo "::add-mask::$ANON_KEY"
echo "::add-mask::$SERVICE_ROLE_KEY"

db_container="$(docker ps --filter 'name=supabase_db_' --format '{{.Names}}' | head -n 1)"
test -n "$db_container"

psql_db() {
  docker exec -i "$db_container" psql -U postgres -d postgres -v ON_ERROR_STOP=1 "$@"
}

cleanup() {
  set +e
  if [[ -n "$function_pid" ]]; then
    kill "$function_pid" >/dev/null 2>&1 || true
    wait "$function_pid" >/dev/null 2>&1 || true
  fi
  if [[ -n "$mock_pid" ]]; then
    kill "$mock_pid" >/dev/null 2>&1 || true
    wait "$mock_pid" >/dev/null 2>&1 || true
  fi
  psql_db >/dev/null 2>&1 <<SQL
reset role;
delete from public.paper_mistake_items
 where paper_submission_id in (
   select id from public.paper_submissions
    where operation_id in ('$paper1_create_op'::uuid, '$paper2_create_op'::uuid)
 );
delete from public.paper_pages
 where paper_submission_id in (
   select id from public.paper_submissions
    where operation_id in ('$paper1_create_op'::uuid, '$paper2_create_op'::uuid)
 );
delete from public.paper_submissions
 where operation_id in ('$paper1_create_op'::uuid, '$paper2_create_op'::uuid);
delete from public.observation_attachments
 where operation_id in ('$att1_op'::uuid, '$att2_op'::uuid, '$att3_op'::uuid);
delete from public.operation_receipts
 where operation_id in (
   '$obs_op'::uuid,
   '$att1_op'::uuid,
   '$att2_op'::uuid,
   '$att3_op'::uuid,
   '$paper1_create_op'::uuid,
   '$paper1_extract_op'::uuid,
   '$paper1_confirm_op'::uuid,
   '$paper1_retry_op'::uuid,
   '$paper2_create_op'::uuid,
   '$paper2_extract_op'::uuid
 );
delete from public.observations where operation_id = '$obs_op'::uuid;
delete from public.identity_links
 where provider_key = 'supabase'
   and external_subject in ('$user_a', '$user_b');
SQL

  for path in "$object_1" "$object_2" "$object_3"; do
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

  rm -f "$status_env" "$function_log" "$function_env" "$mock_log" "$mock_requests" "$mock_port_file" "$tmp_png"
}
trap cleanup EXIT

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

create_auth_session() {
  local label="$1" actor_id="$2"
  local email="extract-paper-$label-${GITHUB_RUN_ID:-local}-${GITHUB_RUN_ATTEMPT:-0}@example.com"
  local password
  password="$(python3 -c 'import secrets,string; a=string.ascii_letters+string.digits; print("Xq!"+"".join(secrets.choice(a) for _ in range(24)))')"
  echo "::add-mask::$password" >&2

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
  echo "::add-mask::$token" >&2

  psql_db >/dev/null <<SQL
insert into public.identity_links (
  app_user_id, provider_key, issuer, external_subject, active
) values (
  '$actor_id'::uuid, 'supabase', '$issuer', '$subject', true
)
on conflict (provider_key, issuer, external_subject)
do update set app_user_id = excluded.app_user_id, active = true;
SQL

  printf '%s|%s\n' "$user_id" "$token"
}

rpc() {
  local token="$1" name="$2" body="$3" out status
  out="$(mktemp)"
  status="$(
    curl --silent --show-error \
      --output "$out" \
      --write-out '%{http_code}' \
      -X POST "$api_url/rest/v1/rpc/$name" \
      -H "apikey: $ANON_KEY" \
      -H "Authorization: Bearer $token" \
      -H 'Content-Type: application/json' \
      --data "$body"
  )"
  if [[ ! "$status" =~ ^2 ]]; then
    echo "RPC $name failed with HTTP $status: $(cat "$out")" >&2
    rm -f "$out"
    return 1
  fi
  cat "$out"
  rm -f "$out"
}

printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Y9Z0xkAAAAASUVORK5CYII=' \
  | base64 -d > "$tmp_png"

session_a="$(create_auth_session a "$actor_a")"
user_a="${session_a%%|*}"
token_a="${session_a#*|}"
session_b="$(create_auth_session b "$actor_b")"
user_b="${session_b%%|*}"
token_b="${session_b#*|}"

cases_before="$(psql_db -Atc 'select count(*) from public.learning_cases')"

obs_resp="$(rpc "$token_a" create_observation \
  "{\"p_operation_id\":\"$obs_op\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_raw_text\":\"Fictional paper extraction E2E observation.\",\"p_client_capture_metadata\":{\"fixture\":true}}")"
observation_id="$(printf '%s' "$obs_resp" | json_get observation_id)"

object_1="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$att1_id"
object_2="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$att2_id"
object_3="v1/org/$organization_id/student/$student_id/profile/$profile_id/observation/$observation_id/attachment/$att3_id"

for pair in "$att1_id|$att1_op|$object_1" "$att2_id|$att2_op|$object_2" "$att3_id|$att3_op|$object_3"; do
  IFS='|' read -r aid aop obj <<< "$pair"
  curl --fail-with-body --silent --show-error \
    -X POST "$api_url/storage/v1/object/$bucket/$obj" \
    -H "apikey: $ANON_KEY" \
    -H "Authorization: Bearer $token_a" \
    -H 'Content-Type: image/png' \
    --data-binary "@$tmp_png" >/dev/null
  rpc "$token_a" commit_observation_attachment \
    "{\"p_operation_id\":\"$aop\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_observation_id\":\"$observation_id\",\"p_attachment_id\":\"$aid\"}" >/dev/null
done

today_utc="$(date -u +%Y-%m-%d)"

paper1_resp="$(rpc "$token_a" create_paper_submission_v1 \
  "{\"p_operation_id\":\"$paper1_create_op\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_paper_date\":\"$today_utc\",\"p_paper_kind\":\"unit_test\",\"p_title\":\"虚构语文单元测\",\"p_source_observation_id\":\"$observation_id\",\"p_pages\":[{\"page_index\":1,\"attachment_id\":\"$att1_id\",\"header_redacted\":true},{\"page_index\":2,\"attachment_id\":\"$att2_id\",\"header_redacted\":false}]}")"
paper1_id="$(printf '%s' "$paper1_resp" | json_get paper_submission_id)"

paper2_resp="$(rpc "$token_a" create_paper_submission_v1 \
  "{\"p_operation_id\":\"$paper2_create_op\",\"p_organization_id\":\"$organization_id\",\"p_student_id\":\"$student_id\",\"p_subject_profile_id\":\"$profile_id\",\"p_assignment_id\":\"$assignment_a\",\"p_paper_date\":\"$today_utc\",\"p_paper_kind\":\"homework\",\"p_title\":\"MALFORMED_FIXTURE_PAPER\",\"p_source_observation_id\":\"$observation_id\",\"p_pages\":[{\"page_index\":1,\"attachment_id\":\"$att3_id\",\"header_redacted\":true}]}")"
paper2_id="$(printf '%s' "$paper2_resp" | json_get paper_submission_id)"

# Start local mock Gemini Interactions API v1 server reachable from Docker bridge
python3 - "$mock_port_file" "$mock_requests" >"$mock_log" 2>&1 <<'PY' &
import json
import pathlib
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

port_file = pathlib.Path(sys.argv[1])
req_log_file = pathlib.Path(sys.argv[2])

class MockGeminiHandler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        sys.stdout.write(fmt % args + "\n")
        sys.stdout.flush()

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        raw_body = self.rfile.read(length).decode("utf-8")
        payload = json.loads(raw_body)

        record = {
            "path": self.path,
            "api_key": self.headers.get("x-goog-api-key"),
            "model": payload.get("model"),
            "store": payload.get("store"),
            "has_response_format": isinstance(payload.get("response_format"), dict),
            "input_types": [item.get("type") for item in payload.get("input", []) if isinstance(item, dict)],
            "prompt_text": next((item.get("text", "") for item in payload.get("input", []) if isinstance(item, dict) and item.get("type") == "text"), ""),
        }
        with req_log_file.open("a", encoding="utf-8") as fh:
            fh.write(json.dumps(record, ensure_ascii=False) + "\n")

        if self.path != "/v1/interactions":
            self.send_response(404)
            self.end_headers()
            return

        if "MALFORMED_FIXTURE_PAPER" in record["prompt_text"]:
            response_body = {
                "id": "interaction_malformed_1",
                "status": "completed",
                "steps": [
                    {
                        "type": "model_output",
                        "content": [{"type": "text", "text": "{not valid json!!!"}],
                    }
                ],
            }
        else:
            extracted = {
                "grading_source": "teacher_marked",
                "title": "语文第三单元阅读与古诗默写测验",
                "question_count": 18,
                "mistake_count": 2,
                "score_earned": 88.5,
                "score_total": 100,
                "duration_minutes": 45,
                "extraction_summary": "整体基础题稳定，两处失分集中在古诗易错字与后半段阅读主旨概括。",
                "mistakes": [
                    {
                        "page_index": 1,
                        "question_label": "二(3)",
                        "topic_tags": ["古诗默写", "易错字"],
                        "error_root_cause": "concept_gap",
                        "paper_section_position": "early",
                        "points_lost": 2.5,
                        "corrected_on_paper": True,
                        "summary": "默写中出现形近字误写，卷面已用红笔订正。",
                    },
                    {
                        "page_index": 2,
                        "question_label": "阅读第4题",
                        "topic_tags": ["阅读理解", "主旨概括"],
                        "error_root_cause": "misread_condition",
                        "paper_section_position": "late",
                        "points_lost": 9,
                        "corrected_on_paper": False,
                        "summary": "漏答结尾段情感转折，答要点不完整。",
                    },
                ],
            }
            response_body = {
                "id": "interaction_ok_1",
                "status": "completed",
                "steps": [
                    {
                        "type": "model_output",
                        "content": [{"type": "text", "text": json.dumps(extracted, ensure_ascii=False)}],
                    }
                ],
            }

        encoded = json.dumps(response_body, ensure_ascii=False).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(encoded)))
        self.end_headers()
        self.wfile.write(encoded)

server = ThreadingHTTPServer(("0.0.0.0", 0), MockGeminiHandler)
port_file.write_text(str(server.server_address[1]), encoding="utf-8")
server.serve_forever()
PY
mock_pid="$!"

for _ in $(seq 1 30); do
  if [[ -s "$mock_port_file" ]]; then
    break
  fi
  sleep 0.2
done
mock_port="$(cat "$mock_port_file")"
test -n "$mock_port"

docker_gateway="$(docker inspect -f '{{range .NetworkSettings.Networks}}{{.Gateway}}{{"\n"}}{{end}}' "$db_container" | awk 'NF {print; exit}')"
test -n "$docker_gateway"

cat > "$function_env" <<EOF
XUEQING_LOCAL_REFERENCE_MODE=1
GEMINI_API_KEY=mock-gemini-test-key
XUEQING_GEMINI_API_BASE_URL=http://${docker_gateway}:${mock_port}
XUEQING_GEMINI_MODEL=gemini-4-hydrogen
EOF

supabase functions serve extract-paper-v1 \
  --workdir backend \
  --env-file "$function_env" \
  >"$function_log" 2>&1 &
function_pid="$!"

function_url="$api_url/functions/v1/extract-paper-v1"
ready='false'
for _ in $(seq 1 60); do
  status="$(
    curl --silent --output /dev/null --write-out '%{http_code}' \
      -X OPTIONS "$function_url" \
      -H "apikey: $ANON_KEY" || true
  )"
  if [[ "$status" == '200' ]]; then
    ready='true'
    break
  fi
  if ! kill -0 "$function_pid" >/dev/null 2>&1; then
    cat "$function_log" >&2
    echo 'extract-paper-v1 exited before becoming ready.' >&2
    exit 1
  fi
  sleep 1
done
if [[ "$ready" != 'true' ]]; then
  cat "$function_log" >&2
  echo 'extract-paper-v1 did not become ready.' >&2
  exit 1
fi

invoke_extract() {
  local token="$1" payload="$2" out
  local -a auth_args=()
  if [[ -n "$token" ]]; then
    auth_args=(-H "Authorization: Bearer $token")
  fi
  out="$(mktemp)"
  extract_status="$(
    curl --silent --show-error \
      --output "$out" \
      --write-out '%{http_code}' \
      -X POST "$function_url" \
      -H "apikey: $ANON_KEY" \
      "${auth_args[@]}" \
      -H 'Content-Type: application/json' \
      --data "$payload"
  )"
  extract_body="$(cat "$out")"
  rm -f "$out"
}

# 1. Unauthenticated caller must fail closed with 401 (retry brief local Kong 502 while Deno worker finishes booting)
for attempt in $(seq 1 10); do
  invoke_extract "" "{\"operation_id\":\"$paper1_extract_op\",\"organization_id\":\"$organization_id\",\"student_id\":\"$student_id\",\"subject_profile_id\":\"$profile_id\",\"paper_submission_id\":\"$paper1_id\"}"
  if [[ "$extract_status" == '401' ]]; then
    break
  fi
  if [[ "$extract_status" != '502' || "$attempt" == '10' ]]; then
    break
  fi
  sleep 1
done
if [[ "$extract_status" != '401' ]]; then
  cat "$function_log" >&2
  echo "Expected 401 for unauthenticated call, got $extract_status: $extract_body" >&2
  exit 1
fi

# 2. Unassigned teacher B must fail closed with 403 and NEVER call Gemini
invoke_extract "$token_b" "{\"operation_id\":\"$paper1_extract_op\",\"organization_id\":\"$organization_id\",\"student_id\":\"$student_id\",\"subject_profile_id\":\"$profile_id\",\"paper_submission_id\":\"$paper1_id\"}"
if [[ "$extract_status" != '403' ]]; then
  cat "$function_log" >&2
  echo "Expected 403 for unassigned teacher B, got $extract_status: $extract_body" >&2
  exit 1
fi
if [[ -s "$mock_requests" ]]; then
  echo "Unauthorized caller triggered Gemini upstream request!" >&2
  cat "$mock_requests" >&2
  exit 1
fi

# 3. Assigned teacher A extracts 2-page paper -> persists draft_ready + 2 mistakes
invoke_extract "$token_a" "{\"operation_id\":\"$paper1_extract_op\",\"organization_id\":\"$organization_id\",\"student_id\":\"$student_id\",\"subject_profile_id\":\"$profile_id\",\"paper_submission_id\":\"$paper1_id\",\"expected_version\":1}"
if [[ "$extract_status" != '200' ]]; then
  cat "$function_log" >&2
  cat "$mock_log" >&2
  echo "Expected 200 for authorized paper extraction, got $extract_status: $extract_body" >&2
  exit 1
fi

printf '%s' "$extract_body" | PAPER_ID="$paper1_id" python3 -c '
import json, os, sys
body = json.load(sys.stdin)
assert body.get("ok") is True, body
assert body.get("contract") == "extract_paper_v1", body
assert body.get("paper_submission_id") == os.environ["PAPER_ID"], body
assert body.get("model") == "gemini-4-hydrogen", body
assert body.get("pages_analyzed") == 2, body
assert body.get("confirmation_state") == "draft_ready", body
assert body.get("grading_source") == "teacher_marked", body
assert body.get("version") == 2, body
assert body.get("mistake_items_saved") == 2, body
'

# Verify Gemini Interactions v1 request contract and minors-privacy anonymization
python3 - "$mock_requests" <<'PY'
import json, pathlib, sys
lines = [line for line in pathlib.Path(sys.argv[1]).read_text(encoding="utf-8").splitlines() if line.strip()]
assert len(lines) == 1, f"Expected exactly 1 Gemini call so far, got {len(lines)}"
req = json.loads(lines[0])
assert req["path"] == "/v1/interactions", req
assert req["api_key"] == "mock-gemini-test-key", req
assert req["model"] == "gemini-4-hydrogen", req
assert req["store"] is False, "Gemini Interactions request MUST pass store=false for minors privacy"
assert req["has_response_format"] is True, req
assert req["input_types"] == ["text", "image", "image"], req
prompt = req["prompt_text"]
for forbidden in ("虚构学生甲", "虚构机构甲", "虚构教师甲"):
    assert forbidden not in prompt, f"PII leaked into Gemini prompt: {forbidden}"
PY

# 4. Malformed Gemini response fails closed (502) and leaves paper2 untouched at version 1
invoke_extract "$token_a" "{\"operation_id\":\"$paper2_extract_op\",\"organization_id\":\"$organization_id\",\"student_id\":\"$student_id\",\"subject_profile_id\":\"$profile_id\",\"paper_submission_id\":\"$paper2_id\"}"
if [[ "$extract_status" != '502' ]]; then
  cat "$function_log" >&2
  echo "Expected 502 for malformed Gemini output, got $extract_status: $extract_body" >&2
  exit 1
fi
printf '%s' "$extract_body" | python3 -c '
import json, sys
body = json.load(sys.stdin)
assert body.get("error") == "XQ_PAPER_EXTRACTION_MALFORMED_OUTPUT", body
'

paper2_state="$(psql_db -Atc "select confirmation_state || '|' || version from public.paper_submissions where id = '$paper2_id'::uuid")"
if [[ "$paper2_state" != 'awaiting_extraction|1' ]]; then
  echo "Malformed extraction mutated paper2 state: $paper2_state" >&2
  exit 1
fi

# 5. Human-confirmed paper MUST be blocked (409 XQ_PAPER_ALREADY_CONFIRMED) BEFORE calling Gemini
rpc "$token_a" save_paper_extraction_v1 \
  "{\"p_operation_id\":\"$paper1_confirm_op\",\"p_paper_submission_id\":\"$paper1_id\",\"p_expected_version\":2,\"p_mode\":\"human_confirm\",\"p_grading_source\":\"teacher_marked\",\"p_title\":\"语文第三单元测验（已确认）\",\"p_question_count\":18,\"p_mistake_count\":2,\"p_score_earned\":88.5,\"p_score_total\":100,\"p_extraction_summary\":\"教师已核对确认。\",\"p_mistakes\":[]}" >/dev/null

calls_before_confirmed="$(wc -l < "$mock_requests" | tr -d ' ')"
invoke_extract "$token_a" "{\"operation_id\":\"$paper1_retry_op\",\"organization_id\":\"$organization_id\",\"student_id\":\"$student_id\",\"subject_profile_id\":\"$profile_id\",\"paper_submission_id\":\"$paper1_id\"}"
if [[ "$extract_status" != '409' ]]; then
  echo "Expected 409 for already human_confirmed paper, got $extract_status: $extract_body" >&2
  exit 1
fi
printf '%s' "$extract_body" | python3 -c '
import json, sys
body = json.load(sys.stdin)
assert body.get("error") == "XQ_PAPER_ALREADY_CONFIRMED", body
'
calls_after_confirmed="$(wc -l < "$mock_requests" | tr -d ' ')"
if [[ "$calls_before_confirmed" != "$calls_after_confirmed" ]]; then
  echo "Human-confirmed paper unexpectedly invoked Gemini upstream ($calls_before_confirmed -> $calls_after_confirmed)" >&2
  exit 1
fi

cases_after="$(psql_db -Atc 'select count(*) from public.learning_cases')"
if [[ "$cases_before" != "$cases_after" ]]; then
  echo "Paper extraction unexpectedly modified learning_cases ($cases_before -> $cases_after)" >&2
  exit 1
fi

echo "Paper extraction provider E2E passed: zero service_role, store=false, anonymous prompt, draft_ready persistence, fail-closed malformed output, and human_confirmed immutability verified."
