#!/usr/bin/env bash
# The whole service against the Compose stack, the way a calling system uses it:
# email and in-app delivery, idempotency, preferences, a dead letter and its replay, and the traces in Jaeger.
# Usage: docker compose up --build -d && tests/e2e/flow.sh
set -euo pipefail

API=${API:-http://localhost:8080}
MAILPIT=${MAILPIT:-http://localhost:8025}
JAEGER=${JAEGER:-http://localhost:16686}
ADMIN_KEY=${ADMIN_KEY:-ns_localadm_local-admin-key-change-me-in-the-env-file}
RUN=$(date +%s)

step() { printf '\n== %s\n' "$*"; }
fail() { printf 'FAILED: %s\n' "$*" >&2; exit 1; }

# Waits until a notification reaches a status, printing its history when it does not.
wait_for() {
  local key=$1 id=$2 want=$3 seconds=${4:-30} status=""
  for _ in $(seq 1 $((seconds * 2))); do
    status=$(curl -sf "$API/api/notifications/$id" -H "X-Api-Key: $key" | jq -r .status)
    [ "$status" = "$want" ] && return 0
    sleep 0.5
  done
  curl -sf "$API/api/notifications/$id" -H "X-Api-Key: $key" | jq -c '[.history[].status]' >&2
  fail "notification $id is $status, expected $want"
}

send() {
  local key=$1 idem=$2 body=$3
  curl -sf -X POST "$API/api/notifications" -H "X-Api-Key: $key" -H "Idempotency-Key: $idem" \
    -H 'Content-Type: application/json' -d "$body"
}

order_shipped() {
  jq -nc --arg email "$1" --arg rid "$2" '{
    recipient: {id: $rid, email: $email}, channel: "Email", template: "order-shipped", language: "tr",
    variables: {name: "Ayşe", orderNo: "SIP-1001", carrier: "Yurtiçi Kargo", trackingNo: "YK123456789"}}'
}

step "The stack is up"
for _ in $(seq 1 60); do curl -sf "$API/health/ready" > /dev/null && break; sleep 2; done
curl -sf "$API/health/ready" | grep -q Healthy || fail "API not ready"

step "An admin creates a key for the shop"
KEY=$(curl -sf -X POST "$API/api/clients" -H "X-Api-Key: $ADMIN_KEY" -H 'Content-Type: application/json' \
  -d "{\"name\":\"Shop backend $RUN\"}" | jq -r .apiKey)
[ -n "$KEY" ] && [ "$KEY" != null ] || fail "no key"

step "An email in Turkish reaches the mailbox"
EMAIL="ayse-$RUN@example.com"
ID=$(send "$KEY" "order-$RUN" "$(order_shipped "$EMAIL" "customer-$RUN")" | jq -r .id)
wait_for "$KEY" "$ID" Delivered
SUBJECT=$(curl -sf "$MAILPIT/api/v1/search?query=to:%22$EMAIL%22" | jq -r '.messages[0].Subject')
[ "$SUBJECT" = "Siparişiniz yola çıktı: SIP-1001" ] || fail "subject was '$SUBJECT'"
echo "Delivered: $SUBJECT"

step "The same request again returns the same notification"
HEADERS=$(mktemp)
AGAIN=$(curl -sf -D "$HEADERS" -X POST "$API/api/notifications" -H "X-Api-Key: $KEY" -H "Idempotency-Key: order-$RUN" \
  -H 'Content-Type: application/json' -d "$(order_shipped "$EMAIL" "customer-$RUN")" | jq -r .id)
[ "$AGAIN" = "$ID" ] || fail "a second notification was created"
grep -qi '^idempotent-replayed: true' "$HEADERS" || fail "no Idempotent-Replayed header"
[ "$(curl -sf "$MAILPIT/api/v1/search?query=to:%22$EMAIL%22" | jq ".messages | length")" = 1 ] || fail "the email was sent twice"

step "An in-app notification lands in the inbox and is read"
INAPP=$(send "$KEY" "password-$RUN" "$(jq -nc --arg rid "customer-$RUN" '{recipient: {id: $rid}, channel: "InApp",
  template: "password-changed", language: "en", variables: {name: "Ayşe", changedAt: "29 Oct 2026 10:15"}}')" | jq -r .id)
wait_for "$KEY" "$INAPP" Delivered
[ "$(curl -sf "$API/api/recipients/customer-$RUN/inbox" -H "X-Api-Key: $KEY" | jq .unread)" = 1 ] || fail "inbox not unread"
curl -sf -X POST "$API/api/recipients/customer-$RUN/inbox/$INAPP/read" -H "X-Api-Key: $KEY" > /dev/null
[ "$(curl -sf "$API/api/recipients/customer-$RUN/inbox" -H "X-Api-Key: $KEY" | jq .unread)" = 0 ] || fail "still unread"

step "A switched-off channel is suppressed"
curl -sf -X PATCH "$API/api/recipients/quiet-$RUN/preferences" -H "X-Api-Key: $KEY" -H 'Content-Type: application/json' \
  -d '{"email": false}' > /dev/null
QUIET=$(send "$KEY" "quiet-$RUN" "$(order_shipped "quiet-$RUN@example.com" "quiet-$RUN")" | jq -r .id)
wait_for "$KEY" "$QUIET" Suppressed

step "The mail server refuses everything: retries, then a dead letter"
curl -sf -X PUT "$MAILPIT/api/v1/chaos" -H 'Content-Type: application/json' \
  -d '{"Recipient": {"ErrorCode": 451, "Probability": 100}}' > /dev/null
DEAD=$(send "$KEY" "dead-$RUN" "$(order_shipped "later-$RUN@example.com" "later-$RUN")" | jq -r .id)
wait_for "$KEY" "$DEAD" DeadLettered 90
curl -sf "$API/api/notifications/$DEAD" -H "X-Api-Key: $KEY" | jq -c '{attempts, lastError, history: [.history[].status]}'
curl -sf "$API/api/dead-letters" -H "X-Api-Key: $ADMIN_KEY" | jq -e --arg id "$DEAD" '.items | any(.id == $id)' > /dev/null \
  || fail "not in the dead letters"

step "The mail server recovers and an admin replays the dead letter"
curl -sf -X PUT "$MAILPIT/api/v1/chaos" -H 'Content-Type: application/json' \
  -d '{"Recipient": {"ErrorCode": 451, "Probability": 0}}' > /dev/null
curl -sf -X POST "$API/api/dead-letters/$DEAD/replay" -H "X-Api-Key: $ADMIN_KEY" > /dev/null
wait_for "$KEY" "$DEAD" Delivered

step "The traces reached Jaeger"
for _ in $(seq 1 30); do
  SERVICES=$(curl -sf "$JAEGER/api/services" | jq -c '.data // []')
  echo "$SERVICES" | grep -q notification-worker && echo "$SERVICES" | grep -q notification-api && break
  sleep 2
done
echo "$SERVICES" | grep -q notification-worker || fail "no worker traces in Jaeger: $SERVICES"
SPANS=$(curl -sf "$JAEGER/api/traces?service=notification-worker&limit=20&lookback=1h" | jq '[.data[].spans[]] | length')
[ "$SPANS" -gt 0 ] || fail "no spans"
echo "Services in Jaeger: $SERVICES"

printf '\nAll steps passed.\n'
