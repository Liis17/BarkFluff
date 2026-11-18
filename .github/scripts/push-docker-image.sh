#!/usr/bin/env bash
# Собирает образ и отправляет его в реестр через `docker buildx build --push`
# (push через dockerd реестр отклоняет с 401). При сбое соединения ждёт 15 с
# и повторяет, всего до 6 попыток; потом шаг падает.
#
# Вход (env): DOCKERFILE, TAGS — теги через запятую, VERSION, REVISION — коммит
#             исходников, REGISTRY_USERNAME, REGISTRY_PASSWORD.
set -euo pipefail

REGISTRY="docker.barkfluff.com"
ATTEMPTS="${PUSH_ATTEMPTS:-6}"
RETRY_DELAY="${PUSH_RETRY_DELAY:-15}"

retry() {
  local attempt=1
  until "$@"; do
    if [ "$attempt" -ge "$ATTEMPTS" ]; then
      echo "Не удалось выполнить после $ATTEMPTS попыток: $1 $2" >&2
      return 1
    fi
    echo "Попытка $attempt/$ATTEMPTS не удалась, повтор через ${RETRY_DELAY} с..." >&2
    attempt=$((attempt + 1))
    sleep "$RETRY_DELAY"
  done
}

registry_login() {
  printf '%s' "$REGISTRY_PASSWORD" | docker login "$REGISTRY" -u "$REGISTRY_USERNAME" --password-stdin
}

retry registry_login

IFS=',' read -ra TAG_LIST <<< "$TAGS"
TAG_ARGS=()
for tag in "${TAG_LIST[@]}"; do
  TAG_ARGS+=(-t "$tag")
done

retry docker buildx build --push -f "$DOCKERFILE" "${TAG_ARGS[@]}" \
  --label "org.opencontainers.image.version=$VERSION" \
  --label "org.opencontainers.image.revision=$REVISION" .
