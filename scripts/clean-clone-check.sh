#!/usr/bin/env bash
# AC15 / AC1 gate: clone the COMMITTED state into an empty temp dir, copy only .env.example to .env, start the whole
# stack with Docker Compose and run the end-to-end path: register -> email destination -> alert -> admin simulates a
# matching event -> one email in Mailpit and the notification `Sent`. Always tears the stack down (and removes its
# volume). Uncommitted changes are NOT part of the check.
#
# Usage:  bash scripts/clean-clone-check.sh
# Needs:  docker (running), git, curl, awk. Uses its own Compose project and the host ports below, so a running dev
#         stack on the default ports is not touched. The default ports of .env.example are therefore not tested here: a
#         clash with a local service on 5432, 8080, 5080 or 8025 would show only in a real "docker compose up".
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK="$(mktemp -d)"
PROJECT="sonrisa-clean"
export WEB_PORT="${CLEAN_WEB_PORT:-18080}" API_PORT="${CLEAN_API_PORT:-15080}"
export POSTGRES_PORT="${CLEAN_PG_PORT:-15432}" MAILPIT_UI_PORT="${CLEAN_MAILPIT_PORT:-18025}"
WEB="http://localhost:${WEB_PORT}"
MAILPIT="http://localhost:${MAILPIT_UI_PORT}"
STEP=0
FAILED=0

# Every request has a time limit, so a port that accepts but never answers cannot hang the script.
curl() { command curl --max-time 15 "$@"; }

say()  { printf '\n== %s\n' "$*"; }
ok()   { printf '   ok: %s\n' "$*"; }
fail() { printf '   FAIL: %s\n' "$*"; FAILED=1; }

cleanup() {
  say "Tearing down"
  if [ -d "$WORK/clone" ]; then
    if ! (cd "$WORK/clone" && docker compose -p "$PROJECT" down -v --remove-orphans --rmi local >/dev/null 2>&1); then
      printf '   WARNING: teardown failed; check: docker compose -p %s ps -a\n' "$PROJECT"
    fi
  fi
  rm -rf "$WORK"
}
trap cleanup EXIT

die() { fail "$*"; exit 1; }

field() { # field <name> : first string value of a JSON key from stdin
  sed -nE "s/.*\"$1\":\"([^\"]*)\".*/\1/p" | head -1
}

wait_for() { # wait_for <seconds> <description> <command...>
  local limit="$1" what="$2"; shift 2
  local i=0
  until "$@" >/dev/null 2>&1; do
    i=$((i + 2)); [ "$i" -ge "$limit" ] && return 1
    sleep 2
  done
  ok "$what (${i}s)"
}

say "Clone of the committed state"
git clone --quiet "$ROOT" "$WORK/clone" || die "git clone failed"
cd "$WORK/clone" || die "no clone"
ok "HEAD $(git rev-parse --short HEAD)"

say "Only .env.example is copied"
cp .env.example .env || die "no .env.example"
# The ports of this script win over the file (the file keeps its defaults; this only avoids a clash with a dev stack).
PORTS=("$WEB_PORT" "$API_PORT" "$POSTGRES_PORT" "$MAILPIT_UI_PORT")
# shellcheck disable=SC1091
set -a; . ./.env; set +a
WEB_PORT="${PORTS[0]}"; API_PORT="${PORTS[1]}"; POSTGRES_PORT="${PORTS[2]}"; MAILPIT_UI_PORT="${PORTS[3]}"
export WEB_PORT API_PORT POSTGRES_PORT MAILPIT_UI_PORT

say "docker compose up --build"
# A run that was killed (no trap) can leave this project's volume behind; start from nothing.
docker compose -p "$PROJECT" down -v --remove-orphans >/dev/null 2>&1
docker compose -p "$PROJECT" up --build -d || die "docker compose up failed"

say "Waiting for the stack"
wait_for 300 "web answers" curl -fsS -o /dev/null "$WEB/" || die "web did not answer in time"
wait_for 120 "API answers through nginx (admin login works)" \
  curl -fsS -X POST "$WEB/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$SEED_ADMIN_EMAIL\",\"password\":\"$SEED_ADMIN_PASSWORD\"}" || die "seeded admin cannot log in"

ADMIN=$(curl -fsS -X POST "$WEB/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$SEED_ADMIN_EMAIL\",\"password\":\"$SEED_ADMIN_PASSWORD\"}" | field accessToken)
[ -n "$ADMIN" ] || die "no admin token"

say "Admin area"
code=$(curl -s -o /dev/null -w '%{http_code}' "$WEB/api/admin/events" -H "Authorization: Bearer $ADMIN")
[ "$code" = 200 ] && ok "GET /api/admin/events -> 200" || fail "GET /api/admin/events -> $code"

say "End to end (AC1): register, destination, alert, simulate, deliver"
EMAIL="clean-$(date +%s)@example.test"; PASS='Clean-Check-Pass1!'
curl -fsS -o /dev/null -X POST "$WEB/api/auth/register" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}" || die "register failed"
USER=$(curl -fsS -X POST "$WEB/api/auth/login" -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASS\"}" | field accessToken)
[ -n "$USER" ] || die "no user token"
DEST=$(curl -fsS -X POST "$WEB/api/destinations" -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"channelKey":"email","displayName":"Inbox"}' | grep -oE '"id":[0-9]+' | head -1 | cut -d: -f2)
SLACK=$(curl -fsS -X POST "$WEB/api/destinations" -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"channelKey":"slack","displayName":"Team"}' | grep -oE '"id":[0-9]+' | head -1 | cut -d: -f2)
[ -n "$DEST" ] && [ -n "$SLACK" ] || die "destinations not created"
WORD="cleancheck$(date +%s)"
curl -fsS -o /dev/null -X POST "$WEB/api/alerts" -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d "{\"name\":\"Clean check\",\"categoryKey\":\"news\",\"rule\":{\"keywords\":[\"$WORD\"]},\"destinationIds\":[$DEST,$SLACK]}" \
  || die "alert not created"
curl -fsS -o /dev/null -X POST "$WEB/api/admin/events/simulate" -H "Authorization: Bearer $ADMIN" \
  -H 'Content-Type: application/json' -d "{\"categoryKey\":\"news\",\"title\":\"Breaking: $WORD\",\"data\":{}}" \
  || die "simulate failed"
ok "alert created, matching event simulated"

sent_count() {
  curl -fsS "$WEB/api/notifications?pageSize=20" -H "Authorization: Bearer $USER" | grep -o '"status":"Sent"' | wc -l
}
for _ in $(seq 1 30); do [ "$(sent_count)" -ge 2 ] && break; sleep 2; done
[ "$(sent_count)" -eq 2 ] && ok "Notifications page: 2 rows (email + Slack) are Sent" || fail "expected 2 Sent rows, got $(sent_count)"

mails=$(curl -fsS "$MAILPIT/api/v1/search?query=$(printf '%s' "to:$EMAIL")" | grep -oE '"messages_count":[0-9]+' | cut -d: -f2)
[ "${mails:-0}" = 1 ] && ok "Mailpit holds exactly 1 email for $EMAIL" || fail "Mailpit emails for $EMAIL: ${mails:-none}"

# Count messages, not lines: the list is one JSON line, one object per message starting with {"id":.
slack=$(curl -fsS "$WEB/api/admin/slack-mock?pageSize=50" -H "Authorization: Bearer $ADMIN" \
  | awk -v w="$WORD" 'BEGIN { RS = "[{]\"id\":" } index($0, w) { n++ } END { print n + 0 }')
[ "$slack" -eq 1 ] && ok "Slack mock list holds exactly 1 message with the keyword" \
  || fail "Slack mock messages with the keyword: $slack (expected 1)"

say "Sources are polled (the poller started; feed results are not checked here)"
if docker compose -p "$PROJECT" logs api 2>&1 | grep -E 'Polling [0-9]+ sources' >/dev/null; then ok "sources are polled"; else fail "no polling line in the API log"; fi

echo
if [ "$FAILED" -eq 0 ]; then echo "CLEAN-CLONE CHECK: PASSED"; else echo "CLEAN-CLONE CHECK: FAILED"; exit 1; fi
