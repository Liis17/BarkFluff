#!/usr/bin/env bash
# Отправляет локально собранные образы в реестр: сначала пауза (чтобы одновременно
# запущенные сборки не грузили реестр и Cloudflare разом), затем вход и push
# с 3 дополнительными попытками при сбоях соединения.
#
# Вход (env): PUSH_DELAY — пауза в секундах, TAGS — теги через запятую,
#             REGISTRY_USERNAME, REGISTRY_PASSWORD.
set -euo pipefail

REGISTRY="docker.barkfluff.com"
RETRIES=3
RETRY_DELAY=10

retry() {
  local attempt=0
  until "$@"; do
    if [ "$attempt" -ge "$RETRIES" ]; then
      echo "Не удалось выполнить после $((RETRIES + 1)) попыток: $1 $2" >&2
      return 1
    fi
    attempt=$((attempt + 1))
    echo "Повтор $attempt/$RETRIES через ${RETRY_DELAY} с..." >&2
    sleep "$RETRY_DELAY"
  done
}

registry_login() {
  printf '%s' "$REGISTRY_PASSWORD" | docker login "$REGISTRY" -u "$REGISTRY_USERNAME" --password-stdin
}

echo "Пауза ${PUSH_DELAY} с перед отправкой в реестр"
sleep "$PUSH_DELAY"

retry registry_login

IFS=',' read -ra TAG_LIST <<< "$TAGS"
for tag in "${TAG_LIST[@]}"; do
  retry docker push "$tag"
done
