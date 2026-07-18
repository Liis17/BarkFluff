# BarkFluff.Federation

Сервис межсерверной федерации (S2S). Порт: **7030** (.NET 10). Единственная точка входа/выхода федеративного трафика ноды.

Контекст решений — [[../../../docs/rearch/04-federation-service|docs/rearch/04-federation-service.md]] и остальные доки `docs/rearch/`; планы реализации по этапам — `docs/rearch/phase-1/`.

Расположение: `Backend/BarkFluff.Federation/`

## Текущее состояние: ключи + well-known (этап 1.2)

`Ping` и `GetServerKeys` (S2S API) реализованы без подписи — `GetServerKeys` останется неподписанным навсегда (bootstrap-канал), `Ping` закроется подписью в 1.3 (XFed). Остальные S2S-RPC отвечают `Unimplemented`. Внутренний API реализует только `RotateSigningKey`; discovery/пиры — 1.4.

Федерация по умолчанию выключена (`Federation:Enabled = false`); при пустом `Federation:ServerName` сервис стартует нормально, но ключ всё равно генерируется (безвредно, лог-warning), а well-known отвечает `503`.

## Сборка

```bash
dotnet build Backend/BarkFluff.Federation/BarkFluff.Federation.csproj
```

Миграции (`FederationContext`) применяются автоматически при старте (`Database.Migrate()`).

## Ed25519-ключи (`SigningKeyService`)

- Библиотека — `BouncyCastle.Cryptography` 2.6.2 (managed, снимает chiseled-риск конструктивно; выбор и бенчмарки — [[../../../docs/rearch/phase-0/step-0.5-report|docs/rearch/phase-0/step-0.5-report.md]]).
- Таблица `SigningKeys` в `FederationDb`: `KeyId` (PK, `"ed25519:N"`), `PublicKey`/`PrivateKeySeed` (raw 32 байта), `CreatedAt`, `ExpiredAt`, `RevokedAt`. Приватный ключ хранится без шифрования (MVP, тот же уровень доверия, что у прочих секретов в конфиг-БД соседей) — **отличие от исходной рекомендации дока 02**: не Configuration-сервис, а `FederationDb` (см. правки доков ниже).
- При старте: если нет ключа с `ExpiredAt IS NULL AND RevokedAt IS NULL` — генерируется `ed25519:1`. Идемпотентно (рестарт не плодит ключи).
- `RotateSigningKey` (internal RPC, `TokenType.Service`): новый ключ `ed25519:{N+1}` становится активным, у старого `ExpiredAt = now + Federation:KeyRotationOverlapDays` (дефолт 30 дней в коде). Well-known после ротации публикует оба ключа, подписан новым.

## Well-known-документ

- `Services/WellKnownDocumentService.cs`: JSON по схеме [[../../../docs/rearch/03-discovery|docs/rearch/03-discovery.md]] («Источник 1»), подписан активным ключом. Канонизация — JCS/RFC 8785 через NuGet-пакет `JsonCanonicalizer` 1.0.0 (управляемый порт `Org.Webpki.JsonCanonicalizer` от cyberphone/json-canonicalization, проверено по исходникам GitHub).
- Кеш в памяти: пересобирается при старте и после `RotateSigningKey`, на GET отдаётся без пересборки.
- **Второй Kestrel-листенер HTTP/1** на порту `Federation:WellKnownPort` (дефолт 7031 в коде — свободен, проверено по `ConfigurationDefaultsPopulator`; тот же механизм, что `RunSettings:Http1Port` у Bots/Calls/Files, но отдельный конфиг-ключ по плану этапа). Основной gRPC-порт 7030 настроен под h2c и HTTP/1-GET не принимает.
- `GET /.well-known/barkfluff` → `200` с документом, либо `503` с телом-пояснением, если `Federation:ServerName`/`ExternalEndpoint` пусты.
- `public_name` пока всегда пустая строка: источник (`ServerProps:PublicName`) принадлежит Beacon — другому `ServiceId`, вне конфиг-скоупа Federation; кросс-сервисное чтение не входит в этап 1.2.
- Независимая проверка (JCS-канонизация без `signature` + Ed25519-verify) — офлайн-прогон BouncyCastle+JsonCanonicalizer подтвердил корректность (roundtrip + порча байта ловится); Python-скрипт `verify-wellknown.py` для проверки на живом стенде лежит в scratchpad сессии этапа 1.2, на живом инстансе не прогонялся (нужен docker-стек, вне скоупа ассистента в этой сессии).

## gRPC API

- `FederationS2SApi` (`federation_api.proto`) — S2S-трафик, авторизация вне XAuth (Ed25519-подпись, XFed — этап 1.3). Реализованы `Ping`, `GetServerKeys`.
- `FederationInternalApi` (`federation_internal_api.proto`) — внутренний API (для AdminPanel и других сервисов ноды), XAuth `TokenType.Service`. Реализован `RotateSigningKey`; остальное — `Unimplemented` до 1.4.

## Конфигурация

- `FederationDb` — PostgreSQL connection string.
- `Federation:Enabled` — bool, дефолт `false`.
- `Federation:ServerName` / `Federation:ExternalEndpoint` — пустые по умолчанию, оператор ноды задаёт сам.
- `Federation:TlsSpkiSha256` — SPKI sha256-отпечатки TLS-серта ноды через запятую (заполняет оператор, нужно для 1.6); пусто → в well-known пустой массив.
- `Federation:WellKnownPort` — порт HTTP/1-листенера well-known, дефолт 7031 в коде.
- `Federation:KeyRotationOverlapDays` — окно перекрытия при ротации, дефолт 30 в коде.
- `FederationService:Host/Token` — ключи для клиентов сервиса Federation (populator, этап 0.1).

## Планы дальнейших этапов

См. `docs/rearch/phase-1/README.md` — 1.3 (XFed-подписи, SPKI-пиннинг), 1.4 (discovery, KnownServers), 1.6 (nginx), 1.7 (AdminPanel).
