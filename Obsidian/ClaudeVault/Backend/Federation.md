# BarkFluff.Federation

Сервис межсерверной федерации (S2S). Порт: **7030** (.NET 10). Единственная точка входа/выхода федеративного трафика ноды.

Контекст решений — [[../../../docs/rearch/04-federation-service|docs/rearch/04-federation-service.md]] и остальные доки `docs/rearch/`; планы реализации по этапам — `docs/rearch/phase-1/`.

Расположение: `Backend/BarkFluff.Federation/`

## Текущее состояние: XFed-подписи (этап 1.3)

Весь S2S-трафик, кроме `GetServerKeys` (bootstrap, останется неподписанным навсегда), проверяется по Ed25519-подписи (XFed). Остальные S2S-RPC (кроме `Ping`) по-прежнему отвечают `Unimplemented` — реализация тела появится вместе с проверкой в 1.4+, но XFed уже покрывает их как класс методов (`UnaryServerHandler`/`ServerStreamingServerHandler`). Внутренний API реализует `RotateSigningKey`; discovery/пиры — 1.4.

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

## XFed (подпись/проверка S2S)

- Каноническая строка (`Services/XFedCanonicalString.cs`, docs/rearch/02-trust-and-certs.md): `{origin}\n{destination}\n{timestamp}\n{grpc-method-full-name}\n{hex(sha256(request-bytes))}`. Все S2S-RPC v1 — унарные запросы (стримы только в ответах), поэтому один механизм покрывает любой RPC.
- Заголовки `x-bf-origin/destination/timestamp/key-id/signature` (`Services/XFedHeaders.cs`).
- **Сырые wire-байты запроса** снимаются ДО protobuf-десериализации в `Services/XFedRawBytesMiddleware.cs` (ASP.NET Core middleware, не gRPC-интерсептор — интерсепторы видят уже распарсенное сообщение). Разбирает gRPC message framing вручную (1 байт compressed-flag + 4 байта big-endian длина + Message, `grpc/PROTOCOL-HTTP2.md`); per-message compression в платформе нигде не включён, поэтому compressed-flag всегда 0. Байты кладутся в `HttpContext.Items`, тело реконструируется для штатного парсинга.
- Проверка — `Host/XFedServerInterceptor.cs`, per-service интерсептор (`AddServiceOptions<FederationS2SApiService>`), порядок проверок по доку 02: заголовки → `destination` → окно времени (`Federation:SignatureWindowSeconds`, дефолт 300с в коде) → ключ пира в `KnownServerKeys` → подпись → блоклист (`KnownServers.Status`). Успех кладёт origin в `context.UserState["xfed-origin"]`.
- Подпись исходящих — `Services/XFedClientInterceptor.cs` (по образцу `JwtClientInterceptor`), активный ключ берёт из `Services/ActiveSigningKeyCache.cs` (синглтон-кеш, обновляется при старте и после `RotateSigningKey` — сам ключ не читается из БД на каждый вызов).
- `Services/S2SChannelFactory.cs` — единственный путь исходящих S2S: кеш `CallInvoker` per-destination, `SocketsHttpHandler.SslOptions.RemoteCertificateValidationCallback` проверяет SPKI (`X509Certificate2.PublicKey.ExportSubjectPublicKeyInfo()` → SHA256 → сравнение с `KnownServers.TlsSpkiSha256`), цепочка CA игнорируется намеренно (self-signed). Пустой список пинов → fail-closed (TLS отклоняется). Plaintext (`http://`) — только для стенда, TLS/nginx — этап 1.6.
- Таблицы `KnownServers`/`KnownServerKeys` (см. [[../../../docs/rearch/03-discovery|docs/rearch/03-discovery.md]]) в 1.3 только читаются; наполнение — этап 1.4 (сейчас сидируются вручную SQL на стенде).

**Отклонение от плана, зафиксированное в коммите**: `BaseGrpcException` (Shared/BarkFluff.Shared.Exceptions) получил виртуальное свойство `StatusCode` (дефолт `FailedPrecondition` — 100% обратная совместимость со всеми существующими исключениями), `ServerExceptionInterceptor` использует его вместо жёсткого `FailedPrecondition`. Понадобилось, т.к. глобальные интерсепторы gRPC оборачивают per-service (Context7 aspnetcore.docs: «globally-configured interceptors run before service-specific ones») — `XFedServerInterceptor` бросает типизированные `BaseGrpcException`-потомки (`Shared/BarkFluff.Shared.Exceptions/Federation/`: `XFedUnauthenticatedException`, `ClockSkewDetectedException`, `FederationServerBlockedException`, `FederationNotConfiguredException`) вместо хендкодинга сырых `RpcException` (что рискованно конфликтует с общим catch-блоком `ServerExceptionInterceptor`, который иначе переписал бы статус на `Unknown`).

## Тесты

`Tests/BarkFluff.Federation.Tests/` — 13 тестов, все зелёные без Docker/Postgres (EF InMemory + `Microsoft.AspNetCore.TestHost`): каноническая строка (fixed vector), Sign/Verify roundtrip + негативы, in-proc `TestServer`-хост гоняет реальный `FederationS2SApiService`+`XFedServerInterceptor`+`XFedRawBytesMiddleware` — валидный `Ping` OK, отсутствие заголовков/битая подпись/чужой `destination` → `Unauthenticated`, просроченный `timestamp` → `Unauthenticated` + `x-error-code=ClockSkewDetected`, заблокированный origin → `PermissionDenied`, `GetServerKeys` без подписи — OK.

## Двух-нодовый стенд

`Backend/dev-federation-testbed/` — `docker-compose.node2.yml` (вторая нода поверх основного dev-стека), `seed-peers.sql` (шаблон ручного сида KnownServers/KnownServerKeys), `fedping/` (мини-CLI вне `BarkFluff.sln`, дублирует канонизацию+подпись, шлёт `Ping` изнутри `barkfluff-network`), подробный README. Сборка/офлайн-крипта проверены (`dotnet build`/сигнатура вручную), сам docker-стенд не поднимался в этой сессии — Docker вне задач ассистента.

## gRPC API

- `FederationS2SApi` (`federation_api.proto`) — S2S-трафик, авторизация — XFed (не XAuth). Реализованы `Ping`, `GetServerKeys`.
- `FederationInternalApi` (`federation_internal_api.proto`) — внутренний API (для AdminPanel и других сервисов ноды), XAuth `TokenType.Service`. Реализован `RotateSigningKey`; остальное — `Unimplemented` до 1.4.

## Конфигурация

- `FederationDb` — PostgreSQL connection string.
- `Federation:Enabled` — bool, дефолт `false`.
- `Federation:ServerName` / `Federation:ExternalEndpoint` — пустые по умолчанию, оператор ноды задаёт сам.
- `Federation:TlsSpkiSha256` — SPKI sha256-отпечатки TLS-серта ноды через запятую (заполняет оператор, нужно для 1.6); пусто → в well-known пустой массив.
- `Federation:WellKnownPort` — порт HTTP/1-листенера well-known, дефолт 7031 в коде.
- `Federation:KeyRotationOverlapDays` — окно перекрытия при ротации, дефолт 30 в коде.
- `Federation:SignatureWindowSeconds` — окно анти-replay XFed, дефолт 300 в коде.
- `FederationService:Host/Token` — ключи для клиентов сервиса Federation (populator, этап 0.1).

## Метрики XFed

`s2s_requests_in`/`s2s_requests_out`, `s2s_signature_failures`, `s2s_clock_skew_rejections`, `s2s_spki_pin_rejections` — через `MetricsCollector`.

## Планы дальнейших этапов

См. `docs/rearch/phase-1/README.md` — 1.4 (discovery, KnownServers), 1.6 (nginx), 1.7 (AdminPanel).
