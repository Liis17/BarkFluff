# BarkFluff.Federation

Сервис межсерверной федерации (S2S). Порт: **7030** (.NET 10). Единственная точка входа/выхода федеративного трафика ноды.

Контекст решений — [[../../../docs/rearch/04-federation-service|docs/rearch/04-federation-service.md]] и остальные доки `docs/rearch/`; планы реализации по этапам — `docs/rearch/phase-1/`.

Расположение: `Backend/BarkFluff.Federation/`

## Текущее состояние: discovery (этап 1.4)

Нода умеет находить пиров всеми тремя способами (well-known → Navigator → manual), наполняет `KnownServers`/`KnownServerKeys` кодом, защищена от SSRF, фоново рефрешит ключи. Внутренний API управления пирами реализован полностью. S2S-RPC, кроме `Ping`/`GetServerKeys`, по-прежнему отвечают `Unimplemented` — тела появятся в Фазе 2 (доставка событий), XFed+discovery уже покрывают их как класс методов.

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
- Таблицы `KnownServers`/`KnownServerKeys` (см. [[../../../docs/rearch/03-discovery|docs/rearch/03-discovery.md]]) — записываются `ServerResolver` (1.4); в 1.3 только читались.

**Отклонение от плана, зафиксированное в коммите**: `BaseGrpcException` (Shared/BarkFluff.Shared.Exceptions) получил виртуальное свойство `StatusCode` (дефолт `FailedPrecondition` — 100% обратная совместимость со всеми существующими исключениями), `ServerExceptionInterceptor` использует его вместо жёсткого `FailedPrecondition`. Понадобилось, т.к. глобальные интерсепторы gRPC оборачивают per-service (Context7 aspnetcore.docs: «globally-configured interceptors run before service-specific ones») — `XFedServerInterceptor` бросает типизированные `BaseGrpcException`-потомки (`Shared/BarkFluff.Shared.Exceptions/Federation/`: `XFedUnauthenticatedException`, `ClockSkewDetectedException`, `FederationServerBlockedException`, `FederationNotConfiguredException`) вместо хендкодинга сырых `RpcException` (что рискованно конфликтует с общим catch-блоком `ServerExceptionInterceptor`, который иначе переписал бы статус на `Unknown`).

## Discovery (этап 1.4)

- `Services/ServernameValidator.cs` — анти-SSRF, единая точка перед ЛЮБЫМ исходящим запросом (well-known-фетч и будущие gRPC-эндпоинты): punycode-нормализация к A-label (`IdnMapping`, ловит гомограф-атаки типа кириллической «а»), запрет IP-литералов/`localhost`, DNS-резолв + отклонение приватных/зарезервированных диапазонов (RFC 1918, loopback, link-local, ULA, multicast) — кроме `Source = Manual`. Anti-rebinding: `SocketsHttpHandler.ConnectCallback` коннектится по уже провалидированному IP, не по повторному резолву.
- `Services/WellKnownClient.cs` — источник 1: `GET https://{servername}/.well-known/barkfluff` по CA-валидному HTTPS (без trust-all — bootstrap-канал), лимит 64 КБ/10с, проверка `server_name` + JCS-канонизация + Ed25519-verify ключом из самого документа (self-certifying). Dev-флаг `Federation:Insecure:AllowUntrustedWellKnownTls` отключает CA-валидацию — читается только при `ASPNETCORE_ENVIRONMENT=Development`.
- `Services/NavigatorClient.cs` — источник 2: `NavigatorApi.GetServerByName` (публичный, без XAuth, как у Beacon). Реализация RPC на стороне Navigator — [[Backend/Navigator]], этап 1.5 (готово).
- `Services/ServerResolver.cs` — алгоritm резолва дословно по [[../../../docs/rearch/03-discovery|docs/rearch/03-discovery.md]]: KnownServers свежий(<24ч)/Manual → как есть; иначе well-known → фолбэк Navigator → `null` (ServerNotFound). Кросс-сверка ключей обязательна при первом контакте, если оба источника отвечают (расхождение → отказ, метрика `crosscheck_mismatches`). Смена ключей у известной ноды принимается, только если новый документ всё ещё содержит хотя бы один ранее доверенный ключ (`HasTrustedContinuity`) — иначе запись не трогаем.
- Discovery-на-лету — в `XFedServerInterceptor`: неизвестный `(origin, key_id)` → `ServerResolver.ResolveAsync` → повторная проверка один раз. `Services/DiscoveryTriggerRateLimiter.cs` — не чаще раза в 5 минут per-server (in-memory), иначе флуд случайными key_id заставляет ноду долбить чужой well-known.
- `BackgroundServices/PeerRefreshBackgroundService.cs` — раз в час проверяет due-пиров (не-Manual, Active/Unreachable): рефреш раз в сутки; после 3 неудач подряд → `Unreachable`, дальше экспоненциальный backoff (2^N часов, кап 24ч); успех возвращает `Active`. Счётчик неудач — in-memory (как троттлинг регистрации в Navigator), потеря при рестарте безвредна.
- Internal-RPC (`FederationInternalApiService`): `GetKnownServers`, `UpsertManualPeer` (валидация только синтаксиса, без проверки диапазонов), `SetServerBlocked`, `GetFederationStatus` (outbox-счётчики — честные нули до Фазы 2).

## Тесты

`Tests/BarkFluff.Federation.Tests/` — 50 тестов, все зелёные без Docker/Postgres (EF InMemory + `Microsoft.AspNetCore.TestHost`): каноническая строка (fixed vector), Sign/Verify roundtrip + негативы, in-proc `TestServer`-хост гоняет реальный `FederationS2SApiService`+`XFedServerInterceptor`+`XFedRawBytesMiddleware` — валидный `Ping` OK, отсутствие заголовков/битая подпись/чужой `destination` → `Unauthenticated`, просроченный `timestamp` → `Unauthenticated` + `x-error-code=ClockSkewDetected`, заблокированный origin → `PermissionDenied`, `GetServerKeys` без подписи — OK. Плюс (1.4): `ServernameValidatorTests` (таблица IP-литерал/localhost/punycode-гомограф/приватные диапазоны/http-исключение для manual), `ServerResolverTests` (фейковые `IWellKnownClient`/`INavigatorClient` — порядок фолбэков, кросс-сверка, ServerNotFound, Manual не трогается, кеш, блоклист, доверенная цепочка при ротации).

## Двух-нодовый стенд

`Backend/dev-federation-testbed/` — `docker-compose.node2.yml` (вторая нода поверх основного dev-стека), `seed-peers.sql` (шаблон ручного сида KnownServers/KnownServerKeys), `fedping/` (мини-CLI вне `BarkFluff.sln`, дублирует канонизацию+подпись, шлёт `Ping` изнутри `barkfluff-network`), подробный README. Сборка/офлайн-крипта проверены (`dotnet build`/сигнатура вручную), сам docker-стенд не поднимался в этой сессии — Docker вне задач ассистента.

## gRPC API

- `FederationS2SApi` (`federation_api.proto`) — S2S-трафик, авторизация — XFed (не XAuth). Реализованы `Ping`, `GetServerKeys`.
- `FederationInternalApi` (`federation_internal_api.proto`) — внутренний API (для AdminPanel и других сервисов ноды), XAuth `TokenType.Service`. Реализованы `RotateSigningKey`, `GetKnownServers`, `UpsertManualPeer`, `SetServerBlocked`, `GetFederationStatus`.

## Конфигурация

- `FederationDb` — PostgreSQL connection string.
- `Federation:Enabled` — bool, дефолт `false`.
- `Federation:ServerName` / `Federation:ExternalEndpoint` — пустые по умолчанию, оператор ноды задаёт сам.
- `Federation:TlsSpkiSha256` — SPKI sha256-отпечатки TLS-серта ноды через запятую (заполняет оператор, нужно для 1.6); пусто → в well-known пустой массив.
- `Federation:WellKnownPort` — порт HTTP/1-листенера well-known, дефолт 7031 в коде.
- `Federation:KeyRotationOverlapDays` — окно перекрытия при ротации, дефолт 30 в коде.
- `Federation:SignatureWindowSeconds` — окно анти-replay XFed, дефолт 300 в коде.
- `Federation:Insecure:AllowUntrustedWellKnownTls` — dev-флаг отключения CA-валидации well-known-фетча, действует только при `ASPNETCORE_ENVIRONMENT=Development`.
- `NavigatorUrl` — адрес Navigator (`http://navigator:7010` по умолчанию); ключ раньше был только у Beacon (ServiceId=3), этапом 1.4 заведён и для Federation (ServiceId=15).
- `FederationService:Host/Token` — ключи для клиентов сервиса Federation (populator, этап 0.1).

## Метрики

- XFed: `s2s_requests_in`/`s2s_requests_out`, `s2s_signature_failures`, `s2s_clock_skew_rejections`, `s2s_spki_pin_rejections`.
- Discovery: `discovery_lookups.{wellknown|navigator|manual|cache}`, `discovery_failures`, `known_servers_active` (gauge, снимается `PeerRefreshBackgroundService`), `wellknown_signature_failures`, `crosscheck_mismatches`.

## Nginx (этап 1.6)

- `Backend/nginx/federation.conf` — субдомен `federation.barkfluff.com`, по образцу `users.conf`: `grpc_pass grpc://federation:7030`, но таймауты `3600s` (как у `calls.conf`) — `SubscribePresence`/`FetchFile` (Фазы 3-4) долгоживущие. У `ngx_http_grpc_module` нет директивы отключения буферизации ответа (в отличие от `proxy_buffering` для HTTP-проксирования — проверено по официальной документации nginx); DATA-фреймы HTTP/2 и так прокидываются по мере поступления. Rate-limit `federation_s2s` (30r/s) — см. [[Backend/Nginx]].
- `/.well-known/barkfluff` отдаётся apex-сервером (`barkfluff.single-server.conf`), не `federation.conf` — `location = /.well-known/barkfluff` проксирует на `federation:7031`. Apex для публичных нод требует CA-валидный серт (Let's Encrypt) — это bootstrap-канал; своя rate-limit-зона `federation_wellknown` (5r/s, жёстче).
- Двух-нодовый стенд (`Backend/dev-federation-testbed/`) расширен: `certs/make-certs.sh` (self-signed + вывод SPKI-отпечатка), `docker-compose.nginx.yml` (nginx-node1/nginx-node2 перед каждой federation-нодой), `nginx/node1.conf`/`node2.conf`, `seed-peers.sql` обновлён на `https://nginx-nodeX` + реальные SPKI. Негативный тест пиннинга — README стенда, раздел 7.

## Планы дальнейших этапов

См. `docs/rearch/phase-1/README.md` — 1.7 (AdminPanel).
