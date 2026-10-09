#!/usr/bin/env bash
# Собирает образ и отправляет его в реестр: сначала пауза (чтобы одновременно
# запущенные сборки не грузили реестр и Cloudflare разом), затем вход и
# `docker buildx build --push` с 3 дополнительными попытками при сбоях соединения.
# Пуш выполняет buildkit: push через dockerd реестр отклоняет с 401.
#
# Вход (env): PUSH_DELAY — пауза в секундах, DOCKERFILE, TAGS — теги через запятую,
#             VERSION, REGISTRY_USERNAME, REGISTRY_PASSWORD.
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
TAG_ARGS=()
for tag in "${TAG_LIST[@]}"; do
  TAG_ARGS+=(-t "$tag")
done

retry docker buildx build --push -f "$DOCKERFILE" "${TAG_ARGS[@]}" \
  --label "org.opencontainers.image.version=$VERSION" .
