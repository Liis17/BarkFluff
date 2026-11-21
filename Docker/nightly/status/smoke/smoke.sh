#!/usr/bin/env bash
# CI-only HTTPS-smoke страницы статуса. Тестовый CA выдаёт сертификат фронтам статуса и тестовой ноды;
# статус проверяет mock-компоненты обычной проверкой сертификата (SSL_CERT_FILE), федерация — по SPKI-пину.
# Вход (env): STATUS_IMAGE — образ статуса (по умолчанию barkfluff-status:ci), GITHUB_RUN_ID — имя проекта.
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
status_dir="$(cd "$here/.." && pwd)"
public_host="status.example.com"
work="$(mktemp -d)"
chmod 755 "$work"
project="status-smoke-${GITHUB_RUN_ID:-local}"
export STATUS_IMAGE="${STATUS_IMAGE:-barkfluff-status:ci}"

compose() {
  docker compose -p "$project" --env-file "$work/smoke.env" \
    -f "$status_dir/docker-compose.yml" -f "$here/docker-compose.smoke.yml" "$@"
}

cleanup() {
  local status=$?
  if [ "$status" -ne 0 ]; then
    compose logs --no-color status nginx node-front mock-node || true
  fi
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
  exit "$status"
}
trap cleanup EXIT

fetch() {
  local path="$1"
  shift
  curl -fsS --max-time 10 --resolve "$public_host:443:127.0.0.1" --cacert "$work/ca.pem" "$@" "https://$public_host$path"
}

echo "== Compose: отдельное развёртывание со стандартным .env.example"
docker compose -f "$status_dir/docker-compose.yml" --env-file "$status_dir/.env.example" config -q

echo "== Тестовый CA и сертификаты"
mkdir -p "$work/certs"
chmod 755 "$work/certs"
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -keyout "$work/ca.key" -out "$work/ca.pem" \
  -subj "/CN=BarkFluff status smoke CA" \
  -addext "basicConstraints=critical,CA:TRUE" -addext "keyUsage=critical,keyCertSign,cRLSign" 2>/dev/null
openssl req -newkey rsa:2048 -nodes -keyout "$work/certs/privkey.pem" -out "$work/leaf.csr" \
  -subj "/CN=$public_host" 2>/dev/null
printf '%s\n' \
  "subjectAltName=DNS:*.smoke.test,DNS:$public_host" \
  "basicConstraints=CA:FALSE" \
  "keyUsage=critical,digitalSignature,keyEncipherment" \
  "extendedKeyUsage=serverAuth" > "$work/leaf.ext"
openssl x509 -req -in "$work/leaf.csr" -CA "$work/ca.pem" -CAkey "$work/ca.key" -CAcreateserial \
  -out "$work/certs/leaf.pem" -days 2 -sha256 -extfile "$work/leaf.ext" 2>/dev/null
cat "$work/certs/leaf.pem" "$work/ca.pem" > "$work/certs/fullchain.pem"
chmod 644 "$work/certs/privkey.pem" "$work/certs/fullchain.pem" "$work/ca.pem"
pin="$(openssl x509 -in "$work/certs/leaf.pem" -pubkey -noout | openssl pkey -pubin -outform DER \
  | openssl dgst -sha256 -binary | base64 -w0)"

cat > "$work/smoke.env" <<EOF
NODE_DOMAIN=smoke.test
STATUS_SERVER_NAME=$public_host
STATUS_HTTPS_PORT=443
STATUS_CERTS_DIR=$work/certs
FEDERATION_SPKI_PIN=$pin
STATUS_DISABLED_SERVICES=
STATUS_IMAGE=$STATUS_IMAGE
STATUS_SMOKE_WORK=$work
EOF

echo "== Compose: смоук-наложение"
compose config -q

echo "== Запуск тестовой ноды и TLS-фронта (до статуса: первый цикл не должен увидеть недостижимые фикстуры)"
compose up -d --no-build mock-node node-front
front_port="$(compose port node-front 443)"
front_port="${front_port##*:}"

# Ожидаемые HTTP-коды реальных HTTPS-ответов фронта: 503 onliner приходит через grpc_pass, 301 fast-auth — не выполняется.
declare -A expected=(
  [beacon]=200 [bots]=200 [calls]=200 [developers]=200 [fast-auth]=301 [federation]=200 [files]=200
  [identity]=200 [messages]=200 [onliner]=503 [updates]=200 [users]=200 [web]=200
)
preflight() {
  local name code ok=1 line=""
  for name in beacon bots calls developers fast-auth federation files identity messages onliner updates users web; do
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 --http2 \
      --resolve "$name.smoke.test:$front_port:127.0.0.1" --cacert "$work/ca.pem" \
      "https://$name.smoke.test:$front_port/health/ready" || true)"
    line+="$name=$code "
    [ "$code" = "${expected[$name]}" ] || ok=0
  done
  echo "$line"
  [ "$ok" = 1 ]
}
echo "== Готовность фикстур по HTTPS /health/ready"
deadline=$((SECONDS + 180))
until last="$(preflight)"; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "Фикстуры не дали ожидаемых кодов за 180 с: $last"
    exit 1
  fi
  sleep 3
done
echo "$last"

echo "== Запуск статуса и nginx"
compose up -d --no-build status nginx

echo "== Ожидание первого полного цикла и проверка состояний"
deadline=$((SECONDS + 180))
until fetch /api/status -o "$work/report.json" 2>/dev/null \
  && python3 -I "$here/verify.py" states "$work/report.json" >/dev/null; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "Состояния не сошлись за 180 с:"
    if [ -f "$work/report.json" ]; then python3 -I "$here/verify.py" states "$work/report.json" || true; fi
    exit 1
  fi
  sleep 3
done
python3 -I "$here/verify.py" states "$work/report.json"

echo "== Страница и методы"
fetch / -o "$work/index.html"
grep -q 'Статус сервисов' "$work/index.html"
code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 10 --resolve "$public_host:443:127.0.0.1" \
  --cacert "$work/ca.pem" -X POST "https://$public_host/api/status" || true)"
if [ "$code" = 200 ]; then
  echo "POST /api/status не должен отвечать 200"
  exit 1
fi
echo "POST /api/status -> $code"

echo "== Пересоздание статуса (только status): история в томе должна сохраниться, фронт переподключиться"
cp "$work/report.json" "$work/before.json"
compose up -d --no-build --no-deps --force-recreate status
docker volume inspect "${project}_status-data" >/dev/null
deadline=$((SECONDS + 120))
# Во время пересоздания nginx может отвечать 502: повторяем запрос до успеха, пока не истечёт срок.
until fetch /api/status -o "$work/after.json" 2>/dev/null; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "API не ответил после пересоздания"
    exit 1
  fi
  sleep 2
done
python3 -I "$here/verify.py" restart "$work/before.json" "$work/after.json"
python3 -I "$here/verify.py" states "$work/after.json"

echo "Smoke страницы статуса: PASS"
