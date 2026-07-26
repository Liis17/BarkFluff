# Двух-нодовый стенд XFed (этапы 1.3–1.6)

Проверяет подписанный `Ping` между двумя нодами Federation через nginx с self-signed сертами и
SPKI-пиннингом (этап 1.6). Discovery (KnownServers наполняется кодом) — этап 1.4; здесь пиры
сидируются вручную SQL-скриптом (`Source = Manual`).

Нода 1 — обычный dev-стек (`docker/backend/docker-compose-dev-backend.yml`, сервисы `postgres`/`configuration`/
`federation`). Нода 2 — `docker-compose.node2.yml` в этой папке (отдельные контейнеры, общая сеть
`barkfluff-network`). nginx перед каждой нодой — `docker-compose.nginx.yml`.

## 1. Сгенерировать серты и поднять стенд

Из `Backend/`:

```bash
bash dev-federation-testbed/certs/make-certs.sh   # выведет SPKI sha256 каждой ноды — сохрани для шага 4

docker compose -f docker-compose-dev.yml \
  -f dev-federation-testbed/docker-compose.node2.yml \
  -f dev-federation-testbed/docker-compose.nginx.yml \
  up -d postgres configuration federation postgres2 configuration2 federation2 nginx-node1 nginx-node2
```

## 2. Задать Federation:ServerName обеим нодам

Federation-сервис при пустом `Federation:ServerName` стартует, но не генерирует пригодную для
пиров идентичность полноценно (ключ создаётся, well-known отвечает 503). Задай имя ноды прямой
записью в БД Configuration (проще, чем через AdminPanel, для одноразового дев-стенда):

```bash
# Нода 1 — БД configuration (основной стек)
docker exec -it postgres_barkfluff psql -U "$POSTGRES_USER" -d barkfluff_configuration -c \
  "UPDATE \"Configurations\" SET \"Value\" = 'node1.test' WHERE \"ServiceId\" = 15 AND \"Section\" = 'Federation' AND \"Key\" = 'ServerName';"

# Нода 2 — БД configuration2
docker exec -it postgres2_barkfluff psql -U barkfluff -d barkfluff_configuration -c \
  "UPDATE \"Configurations\" SET \"Value\" = 'node2.test' WHERE \"ServiceId\" = 15 AND \"Section\" = 'Federation' AND \"Key\" = 'ServerName';"
```

Перезапусти `federation`/`federation2`, чтобы `LoadConfiguration` подхватил новое значение:

```bash
docker compose -f docker-compose-dev.yml -f dev-federation-testbed/docker-compose.node2.yml restart federation federation2
```

## 3. Получить публичные ключи нод (для сида)

Ed25519-ключ `ed25519:1` генерируется автоматически при первом старте (этап 1.2). Достать его:

```bash
docker exec -it postgres_barkfluff psql -U "$POSTGRES_USER" -d barkfluff_federation -c \
  "SELECT \"KeyId\", encode(\"PublicKey\", 'base64') FROM \"SigningKeys\";"

docker exec -it postgres2_barkfluff psql -U barkfluff -d barkfluff_federation -c \
  "SELECT \"KeyId\", encode(\"PublicKey\", 'base64') FROM \"SigningKeys\";"
```

(Имя БД федерации — значение из `FederationDb`, обычно `barkfluff_federation`; сверь, если у тебя
задано иначе.)

## 4. Засеять пиров

Подставь `{{NODE1_SERVER_NAME}}`/`{{NODE2_SERVER_NAME}}`/`{{NODE1_PUBLIC_KEY_BASE64}}`/
`{{NODE2_PUBLIC_KEY_BASE64}}` (шаги 2–3) и `{{NODE1_SPKI_SHA256}}`/`{{NODE2_SPKI_SHA256}}`
(вывод `make-certs.sh`, шаг 1) в `seed-peers.sql`, затем примени секции к соответствующим БД:

```bash
# Секция A — в БД federation ноды 1 (endpoint ноды 2 — https://nginx-node2)
docker exec -i postgres_barkfluff psql -U "$POSTGRES_USER" -d barkfluff_federation < seed-peers-node1-section.sql

# Секция B — в БД federation ноды 2 (endpoint ноды 1 — https://nginx-node1)
docker exec -i postgres2_barkfluff psql -U barkfluff -d barkfluff_federation < seed-peers-node2-section.sql
```

(Раздели `seed-peers.sql` на две секции по комментариям "Секция A"/"Секция B", либо примени файл
целиком к обеим БД — лишние INSERT для чужого ServerName безвредны благодаря `ON CONFLICT DO
NOTHING`, только не перепутай, какие плейсхолдеры для какой ноды.)

## 5. Прогнать fedping через nginx

`fedping/` — консолька вне `BarkFluff.sln` (порты `federation`/`federation2` наружу не публикуются).
Прогони её изнутри сети `barkfluff-network` через одноразовый SDK-контейнер, адресуясь к
`nginx-nodeX` (не напрямую к `federationX`):

```bash
# Достань приватный seed ноды 1 (только для этого дев-стенда — приватный ключ в норме
# никогда не покидает свою ноду):
docker exec -it postgres_barkfluff psql -U "$POSTGRES_USER" -d barkfluff_federation -c \
  "SELECT encode(\"PrivateKeySeed\", 'base64') FROM \"SigningKeys\" WHERE \"KeyId\" = 'ed25519:1';"

docker run --rm -it --network barkfluff-network \
  -v "$(pwd)/dev-federation-testbed/fedping:/app" -w /app \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run -- https://nginx-node2 node1.test node2.test ed25519:1 <SEED_NODE1_BASE64>
```

Ожидаемо: `OK: server_name=node2.test, server_time=..., protocol_versions=[1]` — SPKI-пин совпал,
`S2SChannelFactory`-эквивалент в fedping (`GrpcChannel.ForAddress` без явного пиннинга — fedping
не проверяет SPKI сам, это делает продовый `S2SChannelFactory`; fedping лишь показывает, что канал
через nginx с TLS в принципе отвечает) прошёл TLS-хендшейк с self-signed сертом.

Симметрично для node2 → node1 (`https://nginx-node1`, origin/destination поменяны местами, seed ноды 2).

## 6. Проверка well-known через nginx

```bash
curl -k https://<хост с nginx-node2>/.well-known/barkfluff
```

Ожидаемо: JSON-документ (см. схему [../../../docs/rearch/03-discovery.md](../../docs/rearch/03-discovery.md)).

## 7. Негативный тест SPKI-пиннинга

Перегенерируй серт ноды 2 БЕЗ обновления сида у ноды 1:

```bash
bash dev-federation-testbed/certs/make-certs.sh   # новый node2.crt.pem/key.pem, новый SPKI
docker compose -f docker-compose-dev.yml -f dev-federation-testbed/docker-compose.nginx.yml restart nginx-node2
```

Исходящий S2S от ноды 1 к ноде 2 (через `S2SChannelFactory`, не `fedping`) должен отклоняться на
TLS-этапе (`RemoteCertificateValidationCallback` вернёт `false`, т.к. `KnownServers.TlsSpkiSha256`
ноды 1 всё ещё хранит СТАРЫЙ отпечаток) — метрика `s2s_spki_pin_rejections` растёт.

## Негативные случаи XFed (см. также `Tests/BarkFluff.Federation.Tests`, зелёные без Docker)

- Без заголовков `x-bf-*` — `Unauthenticated`.
- Битая подпись (испорченный байт) — `Unauthenticated`.
- `destination`, не совпадающий с `Federation:ServerName` адресата, — `Unauthenticated`.
- `x-bf-timestamp` за окном `Federation:SignatureWindowSeconds` (дефолт 300с) — `Unauthenticated`
  + `x-error-code` ClockSkewDetected.
- Заблокированный origin (`UPDATE "KnownServers" SET "Status" = 'Blocked' ...`) — `PermissionDenied`.

## Статус проверки

Код (XFed-подпись, middleware сырых байт, канонизация, SPKI-пиннинг в `S2SChannelFactory`, discovery,
Navigator-персистентность) прогнан офлайн: BouncyCastle+JCS вручную, `dotnet test` на in-proc
`TestServer`/EF InMemory — 50/50 (Federation) + 37/37 (Navigator) зелёных, без Postgres/Docker.
Nginx-конфиги (`federation.conf`, apex well-known location, rate-limit зоны) прогнаны через
`dotnet build`-эквивалент невозможно — `nginx -t` не запускался (нет локального nginx на машине
ассистента, поднимать через Docker — вне задач ассистента в этой сессии). Сам двух-нодовый
docker-стенд (включая nginx/self-signed из этапа 1.6) не поднимался — прогнать вручную перед
переходом к следующим фазам.
