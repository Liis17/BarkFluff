# Этап 1.2 — Signing-ключи, GetServerKeys, /.well-known/barkfluff

## Цель

Нода получает собственную Ed25519-идентичность: генерация и хранение ключей, отдача `GetServerKeys`, публикация подписанного discovery-документа `/.well-known/barkfluff`, ротация ключей. Подписи *запросов* (XFed) — следующий этап.

## Контекст

- Модель ключей: [../02-trust-and-certs.md](../02-trust-and-certs.md), «Слой 2» и «Ротация и компрометация».
- Формат well-known-документа и канонизация JCS: [../03-discovery.md](../03-discovery.md), «Источник 1».
- **Криптобиблиотека** — строго та, что выбрана в отчёте [../phase-0/step-0.5-report.md](../phase-0/step-0.5-report.md); сниппеты генерации/подписи оттуда — отправная точка. Ключи raw: 32 байта seed / 32 байта public (RFC 8032).

## Зафиксированное решение — хранение ключей в FederationDb

Док 02 рекомендовал хранить приватный ключ в Configuration-сервисе. **Решение фазы: хранить в `FederationDb`, таблица `SigningKeys`** — ключ не покидает Federation, ротация всё равно требует структурного хранилища (несколько ключей, сроки), обратный канал записи в Configuration не нужен. MVP — без шифрования приватного seed (уровень доверия тот же, что у секретов в общей конфиг-БД; вынос в защищённое хранилище — бэклог, как и было). В рамках этапа обнови доки:

- `../02-trust-and-certs.md`, раздел «Хранение приватного ключа»: заменить рекомендацию варианта 1 итогом («принято: FederationDb.SigningKeys, см. phase-1/step-1.2») — коротко, без переписывания раздела.
- `../09-problems-open-questions.md`, №33: секрет больше не в общей Configuration-БД → статус «решено», формулировку поправить.

## Изменение 1 — таблица SigningKeys

Миграция в `Persistence/Migrations/`:

```
SigningKeys
  KeyId           text PK        -- "ed25519:1", "ed25519:2", ...
  PublicKey       bytea NOT NULL     -- raw 32 байта
  PrivateKeySeed  bytea NOT NULL     -- raw 32 байта seed
  CreatedAt       timestamptz NOT NULL
  ExpiredAt       timestamptz NULL   -- null = активен
  RevokedAt       timestamptz NULL   -- отозван (компрометация; Фаза 2+ шлёт KeyRevoked)
```

## Изменение 2 — KeyService

`Services/SigningKeyService.cs` (имя — по вкусу стиля проекта):

- **Инициализация при старте**: если в таблице нет ни одного ключа с `ExpiredAt IS NULL AND RevokedAt IS NULL` — сгенерировать пару, записать как `ed25519:1`. Идемпотентно: рестарт не плодит ключи. Если `Federation:ServerName` пуст — ключ всё равно генерируется (безвредно), но лог-warning «федерация не сконфигурирована».
- API сервиса: получить активный ключ (для подписи), получить все ключи (для отдачи), `Sign(byte[]) → byte[64]`, `Verify(pub, data, sig)` — тонкие обёртки над библиотекой из отчёта 0.5.
- Нумерация ротации: `ed25519:{N+1}` от максимального существующего N.

## Изменение 3 — GetServerKeys (S2S)

Реализовать в `FederationS2SApiService`: отдать `server_name` + все неотозванные ключи (`key_id`, raw public, `expired_at`). **Этот RPC — bootstrap-канал и останется неподписанным** в 1.3 (единственное исключение XFed) — оставь комментарий в коде.

## Изменение 4 — RotateSigningKey (internal API)

В `Shared/BarkFluff.Proto/federation_internal_api.proto` добавить RPC (добавление обратно-совместимо):

```protobuf
  // Плановая ротация: создаёт новый ключ, старому проставляет expired_at = now + overlap
  rpc RotateSigningKey(RotateSigningKeyRequest) returns (RotateSigningKeyResponse);
```

```protobuf
message RotateSigningKeyRequest { }

message RotateSigningKeyResponse {
  string new_key_id = 1;
  string old_key_id = 2;
  google.protobuf.Timestamp old_key_expires_at = 3;
}
```

Реализация: новый ключ становится активным (им подписываем), старому `ExpiredAt = now + Federation:KeyRotationOverlapDays` (новый конфиг-ключ; читать с дефолтом 30 в коде, дефолт в populator можно не заводить). Well-known-документ после ротации публикует оба ключа и подписан **новым**.

## Изменение 5 — well-known-документ

`Services/WellKnownDocumentService.cs`: собирает JSON строго по схеме из [../03-discovery.md](../03-discovery.md):

- `server_name` — `Federation:ServerName`; `federation.endpoint` — `Federation:ExternalEndpoint`; `tls_spki_sha256` — новый конфиг-ключ `Federation:TlsSpkiSha256` (список через запятую; оператор заполняет отпечаток серта своего nginx; пусто — отдать пустой массив); `protocol_versions` — `[1]`; `signing_keys` — неотозванные ключи (public base64, `expired_at` ISO8601 или null); `public_name` — если у платформы есть готовый источник (посмотри, откуда Beacon берёт `ServerPublicName`) — используй его, иначе пустая строка.
- **Подпись**: канонизация JCS (RFC 8785) документа **без поля `signature`**, подпись активным ключом, поле `signature: { key_id, value }` добавляется после. Реализация канонизации: сначала поищи готовый NuGet-канонизатор (NuGet MCP; исторический кандидат — порт `json-canonicalization` от Cyberphone); если живого пакета нет — ручная каноническая сериализация допустима, потому что схема документа фиксирована и не содержит дробных чисел (отсортированные ключи, без пробелов, UTF-8, escaping по JCS) — задокументируй это ограничение в коде.
- Если `Federation:ServerName` или `ExternalEndpoint` пусты — endpoint отвечает `503` с телом-пояснением (нода не сконфигурирована), не отдаёт мусор.

**HTTP-endpoint**: gRPC-порт Kestrel сконфигурирован под HTTP/2 (h2c) — HTTP/1-GET на нём не живёт. Поднять **второй listener** HTTP/1 на порту из нового конфиг-ключа `Federation:WellKnownPort` (дефолт 7031 в коде; проверь по каталогу Settings, что 7031 никем не занят) и `app.MapGet("/.well-known/barkfluff", ...)`. Как именно `SetRunningAddress` настраивает Kestrel — прочитай в `Backend/BarkFluff.GrpcServer/` и добавь listener, не сломав gRPC. Nginx-проксирование apex-пути на этот порт — этап 1.6.

Кеширование: документ пересобирается при старте и после ротации; на GET отдаётся из памяти (без пересборки на каждый запрос).

## Чего НЕ делать

- Подпись/проверка S2S-запросов, SPKI-пиннинг — 1.3.
- Фетч чужих well-known, KnownServers — 1.4.
- Push `KeyRevoked` пирами — Фаза 2 (outbox ещё нет); `RevokedAt`-колонка пока просто хранится.
- UI ротации — 1.7.

## Критерии готовности

1. Первый старт создаёт ровно один ключ; повторный старт — ноль новых.
2. `grpcurl` `GetServerKeys` — ключ отдаётся, public 32 байта.
3. `GET http://localhost:7031/.well-known/barkfluff` — валидный JSON по схеме 03.
4. **Независимая верификация подписи**: скрипт на Python (`cryptography` или PyNaCl) в scratchpad — скачивает документ, удаляет `signature`, канонизирует по JCS (пакет `rfc8785` или ручная сортировка при фиксированной схеме), проверяет Ed25519-подпись публичным ключом из документа. Подпись сходится; порча одного байта документа — не сходится.
5. `RotateSigningKey` (через grpcurl с service-токеном): появляется `ed25519:2`, у `ed25519:1` проставлен `expired_at`; well-known содержит оба ключа и подписан новым; верификация скриптом снова проходит.
6. Доки 02 и 09 (№33) обновлены; Obsidian `Backend/Federation.md` дополнен (ключи, well-known, порт 7031).
7. Коммит: `feat(rearch-phase1): 1.2 — Ed25519-ключи, GetServerKeys, well-known`.
