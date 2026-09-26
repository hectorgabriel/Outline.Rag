#!/usr/bin/env bash
# Starts the demo Outline stack and fills it with real content, all through Outline's HTTP API:
#   1. installation.create  -> workspace "Acme Demo" + admin user (only works while the instance has no team)
#   2. apiKeys.create       -> API key for the RAG services
#   3. collections.create + documents.create -> one collection per Markdown source (git repos below)
#   4. webhookSubscriptions.create -> document events to the RAG Api on the host
#
# Credentials go to docker/outline-demo/.env.rag (git-ignored). Pass --set-user-secrets to also store
# them in the Api/Worker user-secrets store (overwrites Outline:ApiToken and Outline:WebhookSigningSecret).
#
# Usage: docker/outline-demo/seed.sh [--set-user-secrets]
# Reset: docker compose -f docker/outline-demo/docker-compose.yml down -v && rm docker/outline-demo/.env.rag
set -euo pipefail

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$DIR/../.." && pwd)"
OUTLINE_URL="${OUTLINE_URL:-http://127.0.0.1:3000}"
WEBHOOK_URL="${WEBHOOK_URL:-http://host.docker.internal:5157/webhooks/outline}"
ADMIN_EMAIL="${ADMIN_EMAIL:-admin@example.com}"
CREDENTIALS="$DIR/.env.rag"
CACHE="$DIR/.cache"

# "git repo|collection name|description". Any repo of Markdown files works; the first "# " line becomes the title.
SOURCES=(
  "https://github.com/basecamp/handbook.git|Employee Handbook|The public 37signals (Basecamp) employee handbook: benefits, policies, rituals, career paths."
)

SET_USER_SECRETS=false
[[ "${1:-}" == "--set-user-secrets" ]] && SET_USER_SECRETS=true

for tool in docker curl jq git openssl; do
  command -v "$tool" >/dev/null || { echo "Missing required tool: $tool" >&2; exit 1; }
done

compose() { docker compose -f "$DIR/docker-compose.yml" "$@"; }

# POST /api/<method> with the API key; prints .data, fails loudly on { ok: false }.
api() {
  local method="$1" body="$2" resp
  resp="$(curl -sS -H "Authorization: Bearer $API_TOKEN" -H 'Content-Type: application/json' \
    --data-binary "$body" "$OUTLINE_URL/api/$method")"
  if [[ "$(jq -r '.ok // false' <<<"$resp")" != "true" ]]; then
    echo "Outline API $method failed: $resp" >&2
    return 1
  fi
  jq '.data' <<<"$resp"
}

if [[ ! -f "$DIR/.env" ]]; then
  echo "Generating $DIR/.env (SECRET_KEY, UTILS_SECRET)"
  printf 'SECRET_KEY=%s\nUTILS_SECRET=%s\n' "$(openssl rand -hex 32)" "$(openssl rand -hex 32)" >"$DIR/.env"
fi

echo "Starting the demo stack..."
compose up -d
printf 'Waiting for Outline'
for _ in $(seq 1 100); do
  curl -fs "$OUTLINE_URL/_health" >/dev/null 2>&1 && break
  printf '.'; sleep 3
done
curl -fs "$OUTLINE_URL/_health" >/dev/null || { echo " Outline did not become healthy; see: compose logs outline" >&2; exit 1; }
echo " up."

if [[ -f "$CREDENTIALS" ]]; then
  echo "Reusing credentials from $CREDENTIALS"
  # shellcheck disable=SC1090
  source "$CREDENTIALS"
  API_TOKEN="$OUTLINE_API_TOKEN"
else
  echo "Creating workspace and admin user ($ADMIN_EMAIL)..."
  jar="$(mktemp)"; trap 'rm -f "$jar"' EXIT
  status="$(curl -sS -o /dev/null -w '%{http_code}' -c "$jar" -H 'Content-Type: application/json' \
    -d "$(jq -n --arg e "$ADMIN_EMAIL" '{teamName:"Acme Demo", userName:"Demo Admin", userEmail:$e}')" \
    "$OUTLINE_URL/api/installation.create")"
  session="$(awk '$6=="accessToken"{print $7}' "$jar")"
  if [[ -z "$session" ]]; then
    echo "installation.create failed (HTTP $status). The instance already has a workspace but $CREDENTIALS is missing." >&2
    echo "Reset with: docker compose -f $DIR/docker-compose.yml down -v" >&2
    exit 1
  fi
  # The session JWT works as a bearer token, which also sidesteps the cookie CSRF check.
  API_TOKEN="$session"
  OUTLINE_API_TOKEN="$(api apiKeys.create '{"name":"outline-rag (local demo)"}' | jq -r '.value')"
  API_TOKEN="$OUTLINE_API_TOKEN"
  OUTLINE_WEBHOOK_SECRET="$(openssl rand -hex 32)"
  umask 077
  printf 'OUTLINE_API_TOKEN=%s\nOUTLINE_WEBHOOK_SECRET=%s\n' "$OUTLINE_API_TOKEN" "$OUTLINE_WEBHOOK_SECRET" >"$CREDENTIALS"
fi

existing_collections="$(api collections.list '{"limit":100}' | jq -r '.[].name')"
mkdir -p "$CACHE"

for source in "${SOURCES[@]}"; do
  IFS='|' read -r repo name description <<<"$source"
  if grep -Fxq "$name" <<<"$existing_collections"; then
    echo "Collection '$name' already exists, skipping."
    continue
  fi

  checkout="$CACHE/$(basename "$repo" .git)"
  [[ -d "$checkout/.git" ]] || git clone -q --depth 1 "$repo" "$checkout"

  collection_id="$(api collections.create "$(jq -n --arg n "$name" --arg d "$description" \
    '{name:$n, description:$d, permission:"read_write"}')" | jq -r '.id')"
  echo "Importing '$name' from $repo"

  count=0
  while IFS= read -r -d '' file; do
    title="$(grep -m1 '^# ' "$file" | sed 's/^# *//' || true)"
    [[ -n "$title" ]] || title="$(basename "$file" .md | tr '-' ' ')"
    # Outline stores the title separately, so drop the H1 it came from.
    body="$(awk 'BEGIN{done=0} !done && /^# /{done=1; next} {print}' "$file")"
    api documents.create "$(jq -n --arg t "${title:0:100}" --arg x "$body" --arg c "$collection_id" \
      '{title:$t, text:$x, collectionId:$c, publish:true}')" >/dev/null
    count=$((count + 1))
    echo "  + $title"
  done < <(find "$checkout" -name '*.md' -not -path '*/.git/*' -print0 | sort -z)
  echo "  $count documents"
done

# Registered after the import so seeding doesn't fire deliveries at an Api that isn't running yet.
if ! api webhookSubscriptions.list '{}' | jq -e --arg u "$WEBHOOK_URL" 'any(.[]; .url == $u)' >/dev/null; then
  api webhookSubscriptions.create "$(jq -n --arg u "$WEBHOOK_URL" --arg s "$OUTLINE_WEBHOOK_SECRET" \
    '{name:"Outline RAG (local)", url:$u, secret:$s, events:["documents"]}')" >/dev/null
  echo "Webhook subscription -> $WEBHOOK_URL"
fi

if $SET_USER_SECRETS; then
  dotnet user-secrets set "Outline:ApiToken" "$OUTLINE_API_TOKEN" --project "$REPO_ROOT/src/Outline.Rag.Api" >/dev/null
  dotnet user-secrets set "Outline:WebhookSigningSecret" "$OUTLINE_WEBHOOK_SECRET" --project "$REPO_ROOT/src/Outline.Rag.Api" >/dev/null
  echo "Stored Outline:ApiToken and Outline:WebhookSigningSecret in user secrets (shared by Api and Worker)."
fi

cat <<EOF

Demo wiki ready: http://localhost:3000
  Web sign-in: enter $ADMIN_EMAIL, then open the magic link in Mailpit at http://localhost:8025
  Credentials: $CREDENTIALS
EOF
$SET_USER_SECRETS || cat <<EOF
  To configure the RAG services, rerun with --set-user-secrets, or:
    dotnet user-secrets set Outline:ApiToken "\$(grep ^OUTLINE_API_TOKEN= $CREDENTIALS | cut -d= -f2)" --project src/Outline.Rag.Api
    dotnet user-secrets set Outline:WebhookSigningSecret "\$(grep ^OUTLINE_WEBHOOK_SECRET= $CREDENTIALS | cut -d= -f2)" --project src/Outline.Rag.Api
EOF
