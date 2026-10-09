#!/usr/bin/env bash
set -euo pipefail

REGISTRY="docker.barkfluff.com"

if [ "$GITHUB_REF_TYPE" != "branch" ]; then
  echo "Версии Docker-образов назначаются только для веток" >&2
  exit 1
fi

case "$GITHUB_REF_NAME" in
  dev)
    SOURCE_REPO="${IMAGE}-nightly"
    REPO="${IMAGE}-dev"
    ;;
  nightly)
    SOURCE_REPO="${IMAGE}-nightly"
    REPO="${IMAGE}-nightly"
    ;;
  master)
    SOURCE_REPO="${IMAGE}-dev"
    REPO="$IMAGE"
    ;;
  *)
    echo "Ветка $GITHUB_REF_NAME не привязана к репозиторию Docker-образов" >&2
    exit 1
    ;;
esac

RESPONSE_FILE=$(mktemp)
HEADERS_FILE="${RESPONSE_FILE}.headers"
trap 'status=$?; rm -f "$RESPONSE_FILE" "$HEADERS_FILE"; exit "$status"' EXIT

manifest_digest() {
  local tag="$1" status digest
  if ! status=$(curl -skS --connect-timeout 10 --max-time 30 --retry 3 --retry-delay 5 --retry-connrefused -u "$REG_USER:$REG_PASS" \
    --head -D "$HEADERS_FILE" -o /dev/null -w '%{http_code}' \
    -H 'Accept: application/vnd.oci.image.index.v1+json, application/vnd.oci.image.manifest.v1+json, application/vnd.docker.distribution.manifest.list.v2+json, application/vnd.docker.distribution.manifest.v2+json' \
    "https://$REGISTRY/v2/$SOURCE_REPO/manifests/$tag"); then
    echo "Не удалось получить digest $SOURCE_REPO:$tag" >&2
    return 1
  fi
  if [ "$status" != "200" ]; then
    echo "Не удалось получить digest $SOURCE_REPO:$tag: HTTP $status" >&2
    return 1
  fi
  digest=$(awk 'tolower($1) == "docker-content-digest:" { gsub("\r", "", $2); print $2; exit }' "$HEADERS_FILE")
  if ! [[ "$digest" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo "Некорректный digest $SOURCE_REPO:$tag" >&2
    return 1
  fi
  printf '%s\n' "$digest"
}

# -k: самоподписанный сертификат реестра.
FIRST_URL="https://$REGISTRY/v2/$SOURCE_REPO/tags/list"
TAGS_URL="$FIRST_URL"
SEEN_URLS=""
VERSIONS=""
while [ -n "$TAGS_URL" ]; do
  if [[ "$SEEN_URLS" == *"|$TAGS_URL|"* ]]; then
    echo "Повторная страница тегов $SOURCE_REPO" >&2
    exit 1
  fi
  SEEN_URLS="${SEEN_URLS}|${TAGS_URL}|"
  if ! HTTP_STATUS=$(curl -skS --connect-timeout 10 --max-time 30 --retry 3 --retry-delay 5 --retry-connrefused -u "$REG_USER:$REG_PASS" \
    -D "$HEADERS_FILE" -o "$RESPONSE_FILE" -w '%{http_code}' "$TAGS_URL"); then
    echo "Не удалось получить теги $SOURCE_REPO из реестра" >&2
    exit 1
  fi

  case "$HTTP_STATUS" in
    200)
      if ! jq -e --arg name "$SOURCE_REPO" '
        type == "object" and .name == $name and has("tags") and
        (.tags == null or ((.tags | type) == "array" and all(.tags[]; type == "string")))
      ' "$RESPONSE_FILE" >/dev/null; then
        echo "Некорректный ответ реестра для $SOURCE_REPO" >&2
        exit 1
      fi
      PAGE_VERSIONS=$(jq -r '.tags[]? | select(test("^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$"))' "$RESPONSE_FILE")
      VERSIONS="${VERSIONS}${VERSIONS:+$'\n'}${PAGE_VERSIONS}"
      ;;
    404)
      if [ "$GITHUB_REF_NAME" != "nightly" ] || [ "$TAGS_URL" != "$FIRST_URL" ] || ! jq -e '
        (.errors | type) == "array" and (.errors | length) > 0 and
        all(.errors[]; .code == "NAME_UNKNOWN")
      ' "$RESPONSE_FILE" >/dev/null; then
        echo "Исходный репозиторий $SOURCE_REPO недоступен: HTTP 404" >&2
        exit 1
      fi
      break
      ;;
    *)
      echo "Не удалось получить теги $SOURCE_REPO: HTTP $HTTP_STATUS" >&2
      exit 1
      ;;
  esac

  NEXT_PAGE=$(awk 'tolower($1) == "link:" && /rel="?next"?/ {
    start = index($0, "<"); end = index($0, ">");
    print substr($0, start + 1, end - start - 1); exit
  }' "$HEADERS_FILE")
  case "$NEXT_PAGE" in
    "") break ;;
    "/v2/$SOURCE_REPO/tags/list?"*) TAGS_URL="https://$REGISTRY$NEXT_PAGE" ;;
    "$FIRST_URL?"*) TAGS_URL="$NEXT_PAGE" ;;
    *) echo "Некорректная ссылка следующей страницы тегов $SOURCE_REPO" >&2; exit 1 ;;
  esac
done
VERSIONS=$(printf '%s\n' "$VERSIONS" | sed '/^$/d' | sort -Vru)
LAST=$(printf '%s\n' "$VERSIONS" | sed -n '1p')

if [ "$GITHUB_REF_NAME" = "nightly" ]; then
  if [ -z "$LAST" ]; then
    VERSION="1.0.0"
  else
    VERSION="${LAST%.*}.$(( ${LAST##*.} + 1 ))"
  fi
else
  if [ -z "$LAST" ]; then
    echo "В $SOURCE_REPO нет опубликованной SemVer-версии для $GITHUB_REF_NAME" >&2
    exit 1
  fi
  LATEST_DIGEST=$(manifest_digest latest)
  VERSION=""
  while IFS= read -r candidate; do
    CANDIDATE_DIGEST=$(manifest_digest "$candidate")
    if [ "$CANDIDATE_DIGEST" = "$LATEST_DIGEST" ]; then
      VERSION="$candidate"
      break
    fi
  done <<< "$VERSIONS"
  if [ -z "$VERSION" ]; then
    echo "В $SOURCE_REPO нет SemVer-тега текущего образа latest" >&2
    exit 1
  fi
fi

echo "Источник: $SOURCE_REPO (${LAST:-<нет>}), публикация: $REPO:$VERSION"

{
  echo "version=$VERSION"
  echo "tags=$REGISTRY/$REPO:$VERSION,$REGISTRY/$REPO:latest"
  echo "tag_version=$REGISTRY/$REPO:$VERSION"
} >> "$GITHUB_OUTPUT"
