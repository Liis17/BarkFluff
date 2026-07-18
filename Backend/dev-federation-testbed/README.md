# Двух-нодовый стенд XFed (этап 1.3)

Проверяет подписанный `Ping` между двумя нодами Federation без nginx/TLS (plaintext `http://`,
без SPKI-пиннинга в бою — это добавляет этап 1.6). Discovery (KnownServers наполняется кодом) —
этап 1.4; здесь пиры сидируются вручную SQL-скриптом.

Нода 1 — обычный dev-стек (`Backend/docker-compose-dev.yml`, сервисы `postgres`/`configuration`/
`federation`). Нода 2 — `docker-compose.node2.yml` в этой папке (отдельные контейнеры, общая сеть
`barkfluff-network`).

## 1. Поднять стенд

Из `Backend/`:

```bash
docker compose -f docker-compose-dev.yml -f dev-federation-testbed/docker-compose.node2.yml \
  up -d postgres configuration federation postgres2 configuration2 federation2
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
`{{NODE2_PUBLIC_KEY_BASE64}}` в `seed-peers.sql` значениями из шагов 2–3, затем:

```bash
# Секция A — в БД federation ноды 1
docker exec -i postgres_barkfluff psql -U "$POSTGRES_USER" -d barkfluff_federation < seed-peers-node1-section.sql

# Секция B — в БД federation ноды 2
docker exec -i postgres2_barkfluff psql -U barkfluff -d barkfluff_federation < seed-peers-node2-section.sql
```

(Раздели `seed-peers.sql` на две секции по комментариям "Секция A"/"Секция B", либо примени файл
целиком к обеим БД — лишние INSERT для чужого ServerName безвредны благодаря `ON CONFLICT DO
NOTHING`, только не перепутай, какие плейсхолдеры для какой ноды.)

## 5. Прогнать fedping

`fedping/` — консолька вне `BarkFluff.sln` (не имеет доступа к контейнерам с хоста напрямую, порты
`federation`/`federation2` наружу не публикуются). Прогони её изнутри сети `barkfluff-network`
через одноразовый SDK-контейнер:

```bash
# Достань приватный seed ноды 1 (только для этого дев-стенда — приватный ключ в норме
# никогда не покидает свою ноду):
docker exec -it postgres_barkfluff psql -U "$POSTGRES_USER" -d barkfluff_federation -c \
  "SELECT encode(\"PrivateKeySeed\", 'base64') FROM \"SigningKeys\" WHERE \"KeyId\" = 'ed25519:1';"

docker run --rm -it --network barkfluff-network \
  -v "$(pwd)/dev-federation-testbed/fedping:/app" -w /app \
  mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet run -- http://federation2:7030 node1.test node2.test ed25519:1 <SEED_NODE1_BASE64>
```

Ожидаемо: `OK: server_name=node2.test, server_time=..., protocol_versions=[1]`.

Симметрично для node2 → node1 (`http://federation:7030`, origin/destination поменяны местами,
seed ноды 2).

## Негативные случаи (см. также `Tests/BarkFluff.Federation.Tests`, зелёные без Docker)

- Без заголовков `x-bf-*` — `Unauthenticated`.
- Битая подпись (испорченный байт) — `Unauthenticated`.
- `destination`, не совпадающий с `Federation:ServerName` адресата, — `Unauthenticated`.
- `x-bf-timestamp` за окном `Federation:SignatureWindowSeconds` (дефолт 300с) — `Unauthenticated`
  + `x-error-code` ClockSkewDetected.
- Заблокированный origin (`UPDATE "KnownServers" SET "Status" = 'Blocked' ...`) — `PermissionDenied`.

## Статус проверки

Код (XFed-подпись, middleware сырых байт, канонизация, SPKI-пиннинг в `S2SChannelFactory`) прогнан
офлайн (BouncyCastle+JCS вручную, `dotnet test` на in-proc `TestServer` — 13/13 зелёных, без
Postgres/Docker). Сам двух-нодовый docker-стенд из этого README не поднимался в рамках сессии,
где писался этот код — Docker вне задач ассистента (см. правило проекта); прогнать вручную перед
переходом к этапу 1.4/1.6.
