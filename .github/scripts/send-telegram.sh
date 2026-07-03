#!/usr/bin/env bash
set -euo pipefail

: "${TG_TOKEN:?TG_TOKEN is required}"
: "${TG_CHAT:?TG_CHAT is required}"
: "${TG_MESSAGE:?TG_MESSAGE is required}"
: "${TG_ACTION_URL:?TG_ACTION_URL is required}"

curl -sS -X POST "https://api.telegram.org/bot${TG_TOKEN}/sendMessage" \
  --data-urlencode "chat_id=${TG_CHAT}" \
  --data-urlencode "parse_mode=Markdown" \
  --data-urlencode "text=${TG_MESSAGE}" \
  --data-urlencode "reply_markup={\"inline_keyboard\":[[{\"text\":\"Открыть GitHub Action\",\"url\":\"${TG_ACTION_URL}\"}]]}"
