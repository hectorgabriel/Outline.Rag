#!/usr/bin/env bash
#
# Smoke test for the DevStudio AI server gate (Ollama behind Caddy + Bearer auth).
# Setup details, model list and certificate installation: docs/devstudio-ai-server.md
#
# Usage:
#   export SERVIDOR_IA_CLAVE="<the 64-char key>"
#   ./test-ai-gate.sh
#
# Checks performed:
#   1. TLS handshake + gate reachable (expects 401 unauthenticated)
#   2. Bearer key accepted                   (/api/version)
#   3. Native model list                     (/api/tags)
#   4. OpenAI-compatible model list          (/v1/models)
#   5. Tiny generation through the gate      (/v1/chat/completions)
#
# Optional environment variables:
#   BASE_URL         Server URL (default: https://10.97.0.19:8443)
#   TEST_MODEL       Model for the generation test (default: qwen2.5-coder:14b)
#   AI_CA_FILE       Path to root.crt, if the CA is not trusted system-wide
#   TIMEOUT          Request timeout in seconds (default: 10)
#   SKIP_GENERATION  Set to 1 to skip check #5
#
# Exit code: 0 = all checks passed, 1 = at least one failure.
# The key is never written anywhere; it is only read from the environment.

set -u

BASE_URL="${BASE_URL:-https://10.97.0.19:8443}"
TEST_MODEL="${TEST_MODEL:-qwen2.5-coder:14b}"
TIMEOUT="${TIMEOUT:-10}"
fails=0

if [ -z "${SERVIDOR_IA_CLAVE:-}" ]; then
  echo "ERROR: SERVIDOR_IA_CLAVE is not set."
  echo "       export SERVIDOR_IA_CLAVE=\"<the 64-char key from the platform team>\""
  exit 1
fi

auth_header="Authorization: Bearer ${SERVIDOR_IA_CLAVE}"

curl_args=(-sS -m "$TIMEOUT")
if [ -n "${AI_CA_FILE:-}" ]; then
  curl_args=(--cacert "$AI_CA_FILE" "${curl_args[@]}")
fi

pass() { printf 'PASS  %s\n' "$1"; }
fail() { printf 'FAIL  %s\n' "$1"; fails=$((fails + 1)); }
expect_code() { # description, expected, actual
  if [ "$2" = "$3" ]; then pass "$1 (HTTP $3)"; else fail "$1 (expected HTTP $2, got $3)"; fi
}

echo "== Test gate: $BASE_URL =="

# 1) TLS + reachability: unauthenticated request must reach the gate and return 401.
code=$(curl "${curl_args[@]}" -o /dev/null -w '%{http_code}' "$BASE_URL/" 2>/dev/null)
rc=$?
if [ "$rc" = "60" ]; then
  fail "TLS: certificate not trusted - install root.crt (see docs/devstudio-ai-server.md) or set AI_CA_FILE"
elif [ "$rc" != "0" ]; then
  fail "connect: curl exit code $rc"
else
  expect_code "gate reachable, unauthenticated -> 401" 401 "$code"
fi

# 2) Auth + version (native Ollama API).
body=$(curl "${curl_args[@]}" -H "$auth_header" "$BASE_URL/api/version" 2>/dev/null)
code=$(curl "${curl_args[@]}" -o /dev/null -w '%{http_code}' -H "$auth_header" "$BASE_URL/api/version" 2>/dev/null)
expect_code "Bearer key accepted (/api/version)" 200 "$code"
[ -n "$body" ] && echo "      -> $body"

# 3) Models (native API).
code=$(curl "${curl_args[@]}" -o /dev/null -w '%{http_code}' -H "$auth_header" "$BASE_URL/api/tags" 2>/dev/null)
expect_code "model list (/api/tags)" 200 "$code"
if command -v jq >/dev/null 2>&1; then
  curl "${curl_args[@]}" -H "$auth_header" "$BASE_URL/api/tags" 2>/dev/null \
    | jq -r '.models[].name' 2>/dev/null | sed 's/^/      model: /'
fi

# 4) Models (OpenAI-compatible API).
code=$(curl "${curl_args[@]}" -o /dev/null -w '%{http_code}' -H "$auth_header" "$BASE_URL/v1/models" 2>/dev/null)
expect_code "OpenAI-compatible list (/v1/models)" 200 "$code"

# 5) Tiny generation through the gate.
if [ "${SKIP_GENERATION:-0}" != "1" ]; then
  out=$(curl "${curl_args[@]}" -m 60 -H "$auth_header" -H 'Content-Type: application/json' \
    "$BASE_URL/v1/chat/completions" \
    -d "{\"model\":\"$TEST_MODEL\",\"messages\":[{\"role\":\"user\",\"content\":\"Responde con una sola linea: PRUEBA OK\"}],\"max_tokens\":24}" \
    -w '\n%{http_code}' 2>/dev/null)
  code=$(printf '%s\n' "$out" | tail -n 1)
  body=$(printf '%s\n' "$out" | sed '$d')
  expect_code "generation ($TEST_MODEL)" 200 "$code"
  if [ "$code" = "200" ] && command -v jq >/dev/null 2>&1; then
    printf '%s' "$body" | jq -r '.choices[0].message.content // empty' 2>/dev/null | sed 's/^/      -> /'
  fi
fi

echo
if [ "$fails" -eq 0 ]; then
  echo "ALL CHECKS PASSED"
  exit 0
fi
echo "$fails CHECK(S) FAILED"
echo "Hints: TLS errors -> install root.crt or set AI_CA_FILE; 401 responses -> check SERVIDOR_IA_CLAVE."
exit 1
