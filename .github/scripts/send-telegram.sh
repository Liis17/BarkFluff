#!/usr/bin/env bash
set -euo pipefail

: "${TG_TOKEN:?TG_TOKEN is required}"
: "${TG_CHAT:?TG_CHAT is required}"
: "${TG_MESSAGE:?TG_MESSAGE is required}"
: "${TG_ACTION_URL:?TG_ACTION_URL is required}"

tmp_dir=$(mktemp -d)
trap 'rm -rf "$tmp_dir"' EXIT

message_file="$tmp_dir/message.txt"
reply_markup_file="$tmp_dir/reply_markup.json"

printf '%s' "$TG_MESSAGE" > "$message_file"

button_text='Открыть GitHub Action'
button_url="$TG_ACTION_URL"
if [[ -n "${TG_DOWNLOAD_URL:-}" ]]; then
  button_text='Скачать последнюю версию'
  button_url="$TG_DOWNLOAD_URL"
fi

json_escape() {
  local value="$1"
  value=${value//\\/\\\\}
  value=${value//\"/\\\"}
  value=${value//$'\n'/\\n}
  value=${value//$'\r'/\\r}
  printf '%s' "$value"
}

printf '{"inline_keyboard":[[{"text":"%s","url":"%s"}]]}' \
  "$(json_escape "$button_text")" \
  "$(json_escape "$button_url")" > "$reply_markup_file"

curl_message_file="$message_file"
curl_reply_markup_file="$reply_markup_file"
if command -v cygpath >/dev/null 2>&1; then
  curl_message_file=$(cygpath -w "$message_file")
  curl_reply_markup_file=$(cygpath -w "$reply_markup_file")
fi

curl -sS -X POST "https://api.telegram.org/bot${TG_TOKEN}/sendMessage" \
  --data-urlencode "chat_id=${TG_CHAT}" \
  --data-urlencode "parse_mode=Markdown" \
  --data-urlencode "text@${curl_message_file}" \
  --data-urlencode "reply_markup@${curl_reply_markup_file}"
