# BarkFluff.Federation

Сервис межсерверной федерации (S2S). Порт: **7030** (.NET 10). Единственная точка входа/выхода федеративного трафика ноды.

Контекст решений — [[../../../docs/rearch/04-federation-service|docs/rearch/04-federation-service.md]] и остальные доки `docs/rearch/`; планы реализации по этапам — `docs/rearch/phase-1/`.

Расположение: `Backend/BarkFluff.Federation/`

gRPC Reflection доступен только при `ASPNETCORE_ENVIRONMENT=Development`; в Production, Nightly и Master endpoint не публикуется.

## Доставка (этап 2.2)

Надёжная доставка федеративных событий из [[04-federation-service|docs/rearch/04]].

### Схема FederationDb (новые таблицы с этапа 2.2)

- `FederationOutbox (Id bigserial PK, Destination, ChatId NULL, EventId uuid, EventType, PayloadBytes bytea, CreatedAt, Attempts, NextAttemptAt, Status int, LastError)` — индексы `(Status, NextAttemptAt)` и `(Destination, ChatId, Id)`.
- `ProcessedEvents (EventId uuid PK, OriginServer, ReceivedAt)` — идемпотентность входящих, TTL 14 дней.

### Outbox-диспетчер (`BackgroundServices/OutboxDispatcher.cs`)

`BackgroundService` с циклом 5 секунд:

- **claim-then-send (масштабирование, docs/scaling/federation.md)**: в начале прохода reclaim возвращает застрявшие после крэша строки в Pending (`Processing` с истёкшим lease), затем батч атомарно «застолблен» статусом `Processing` (lease 2 мин в `NextAttemptAt`) — на PostgreSQL raw-SQL `SELECT … ORDER BY … LIMIT … FOR UPDATE SKIP LOCKED` в короткой транзакции (LINQ-расширения `ForUpdate()/SkipLocked()` в EFCore.PG 10 отсутствуют; precedent — `PrekeyStorage`), отправка — вне блокировки строк. Несколько инстансов делят работу без дублей забора;
- выбирает `Pending` с `NextAttemptAt <= now`, группирует по `Destination`;
- **упорядочивание per-chat**: событие чата попадает в батч только если у `(Destination, ChatId)` нет более раннего (меньший `Id`) недоставленного события — Pending **или** Processing (голова в полёте на другом инстансе тоже блокирует очередь чата); `ChatId = NULL` (профильные, 2.9) — без ограничений; между чатами — независимо (без head-of-line blocking);
- батч ≤ 100 событий и ≤ 1 МБ на вызов `DeliverEvents`;
- вызов S2C `DeliverEvents` подписанным клиентом (1.3) через `ServerResolver` (1.4) и `S2SChannelFactory`;
- per-event классификация ответа: `OK`/`ALREADY_PROCESSED` → Delivered; `REJECTED` → DeadLetter немедленно (`LastError = error_code`), очередь чата продолжает ехать; `RETRY` или транспортная ошибка → backoff всем событиям батча;
- backoff: 30s → 2m → 10m → 1h → 6h (далее кап 6h); `MaxAttempts` (дефолт 20 ≈ 7 суток окна, конфиг `Federation:OutboxMaxAttempts`) → DeadLetter;
- gauge `outbox_pending` после цикла; метрика `outbox_reclaimed` на reclaim.

### Janitor (`BackgroundServices/OutboxJanitor.cs`)

Раз в час: `Delivered` старше 7 дней (конфиг `Federation:OutboxDeliveredTtlHours`) и `ProcessedEvents` старше 14 дней (конфиг `Federation:ProcessedEventsTtlHours`) — удаляются. **Single-runner** (масштабирование): чистку выполняет один инстанс под Redis-локом `federation:lock:outbox-janitor` (`ISingleRunner`/`RedisSingleRunner`, TTL 2 ч с продлением лидером); DELETE идемпотентен, best-effort-лок достаточен.

### EventSigner (`Services/EventSigner.cs`)

Канонизация FederationEvent: сериализация с **очищенными** `origin_signature`/`origin_key_id` (C# protobuf — детерминирована по номерам полей); отправитель подписывает приватным ключом и проставляет поля, получатель очищает и пере-сериализует для проверки. См. [[02-trust-and-certs|docs/rearch/02-trust-and-certs.md]] — раздел «Подпись FederationEvent» (добавлен этапом 2.2).

### OutboxWriter (`Infrastructure/OutboxWriter.cs`)

Общая точка записи: строит wire-bytes из FederationEvent, подписывает активным ключом, ставит строки в outbox для каждой ноды из `destinations` (свой `Federation:ServerName` исключается). Используется RabbitMQ-консюмерами и internal-RPC `EnqueueOutbound`.

### Консюмеры RabbitMQ (`Consumers/`)

Очереди (образец — Updates/CloudMessaging):

| Очередь | Событие | Действие |
|---|---|---|
| `new-messages-federation-handler` | [[Shared/Queue#NewMessageEvent]] | Если `IsFederated=true` — построить `ChatCreated` (если `IsFirstMessageInChat`) + `NewMessage` (текст парсится из `byte[] Message`, этап 2.3), подписать, в outbox для каждого ServerName из `RemoteParticipants`. Оттуда же берутся пересылки (`FederatedForwardMapper.FromWireMessage`) — второго источника не нужно; ответ едет `reply_to_federated_message_id` из `NewMessageEvent` |
| `messages-edited-federation-handler` | `MessageEditedEvent` | `MessageEdited` в outbox (`NewText` — из `byte[] Message`, этап 2.4) |
| `messages-deleted-federation-handler` | `MessageDeletedEvent` | `MessageDeleted` в outbox |
| `read-receipts-federation-handler` | `MessageReadEvent` | `MessagesRead` в outbox |
| `federated-chat-rejected-messages` (Messages, не Federation) | `FederatedChatRejectedEvent` | См. «Квота и privacy-отказ (этап 2.5)» ниже — публикуется этой нодой, потребляется [[Backend/Messages]] |
| `session-revoked-federation-{InstanceId}` (fan-out, autodelete) | [[Shared/Queue#SessionRevokedEvent]] | Стандартная инвалидация `TokenRevocationCache` (по образцу Users/Messages/Updates) |
| `signing-key-rotated-federation-{InstanceId}` (fan-out, autodelete) | [[Shared/Queue#SigningKeyRotatedEvent]] | Перезагрузка `ActiveSigningKeyCache` + well-known на каждом инстансе после ротации — иначе подписи старым ключом до рестарта |

Консюмеры игнорируют события при `Federation:Enabled=false` или `IsFederated=false` — нефедеративные чаты не порождают outbox-записей.

### DeliverEvents — серверный пайплайн (этап 2.2)

Реализация `FederationS2SApi.DeliverEvents` в `Host/FederationS2SApiService.cs`. Для каждого события:

1. `origin_server` события == `x-bf-origin` (XFed проверил подпись запроса) → иначе `REJECTED`.
2. `ProcessedEvents` содержит `event_id` → `ALREADY_PROCESSED`.
3. Проверка `origin_signature` ключом `origin_key_id` из `KnownServerKeys` → иначе `REJECTED`.
4. «Нода говорит только за своих»: `author.server_name` внутри payload == origin → иначе `REJECTED` (для `MessageEdited`/`MessageDeleted`/`MessagesRead` в payload identity автора нет намеренно — проверка P2-02 делается локально в Messages, см. [[Backend/Messages]]).
5. Маршрутизация по типу (`RouteToInternalAsync`) → внутренний вызов `MessagesServerApi`: `ChatCreated`→`ImportFederatedChat` (2.3, с квотой per-origin — ниже), `NewMessage`→`ImportFederatedMessage` (2.3), `MessageEdited/Deleted/MessagesRead`→`ApplyFederatedEdit/Delete/Read` (2.4). Профильные payload'ы (`ProfileChanged`/`UserDeactivated`) — RETRY до 2.9.
6. Успех → запись в `ProcessedEvents` + `OK`. RETRY не индексируется в ProcessedEvents (повторная доставка валидна).

### Цитаты через границу ноды

`NewMessagePayload` дополнен `reply_to_federated_message_id` (7) и `repeated FederatedForward` (8).

- **Ответ едет uuid, а не локальным id**: у копии сообщения на каждой ноде свой `Messages.Id`.
  На приёме uuid резолвится через `MessagesStorage.GetByFederatedIdAsync`.
- **Оригинал ещё не импортирован → сохраняем сообщение БЕЗ цитаты, не RETRY.** Дыру дотянет
  catch-up (2.6); цитата не должна задерживать доставку самого сообщения.
- **Пересылка едет снапшотом целиком**: оригинал может лежать в чате, которого у ноды-получателя
  нет вовсе. `original_message_id` намеренно не передаётся — он локален для origin.
- Снапшот с чужой ноды проверяет `FederatedForwardImporter` (≤20 пересылок, author_name ≤255,
  text ≤4096, ≤10 вложений на пересылку) → `FederatedForwardInvalidException`, **permanent**
  (REJECTED), по образцу `FederatedAttachmentImporter`.

### EnqueueOutbound — internal API

`FederationInternalApi.EnqueueOutbound(event, destinations[])` — прямая постановка подписанного события в outbox. Для ручных тестов и профильных событий (2.9). Консюмеры RabbitMQ используют `OutboxWriter` напрямую, не этот RPC.

### Метрики

- `outbox_pending` (gauge, снимается в конце цикла диспетчера)
- `outbox_delivered`, `outbox_retry`, `outbox_dispatch_errors`
- `outbox_deadletter.rejected` / `.max_attempts` / `.federated_dm_rejected`
- `outbox_deliver_duration_ms_total`
- `outbox_enqueued_total`
- `events_received.{type}` — при `OK`
- `events_duplicate`, `events_rejected.{origin_mismatch|unknown_key|invalid_signature|author_not_origin}`
- `chatcreated_quota_exceeded.{origin}` — этап 2.5, см. ниже
- `federated_chat_rejected_consumed` — Messages, консюмер `FederatedChatRejectedEvent`

## Квота ChatCreated и privacy-отказ (этап 2.5, docs/rearch/phase-2/step-2.5-privacy-antispam.md)

### Квота ChatCreated per-origin

`Services/ChatCreatedQuotaLimiter` (`IChatCreatedQuotaLimiter`) — защита от спам-волны создания
чатов одной нодой. Redis-счётчик `fed:chatcreated:{origin}:{yyyyMMddHH}` (часовое окно), инкремент
+ TTL 1ч на первый инкремент. Лимит — `Federation:ChatCreatedHourlyLimit` (конфиг, default 100).

Списание идемпотентно по `eventId` (rearch-phase2, code-review): `TryConsumeAsync(origin, eventId)`
сначала выставляет redis-маркер `fed:chatcreated:charged:{eventId}` (`SET NX EX 1ч`) — если маркер
уже стоял, квота уже была учтена этим же событием раньше и повторный инкремент не делается. Без
этого ретраи ещё не обработанного `ChatCreated` (OutboxDispatcher.ApplyRetry — событие не
индексируется в ProcessedEvents, пока не получит `Ok`) списывали квоту на каждую попытку, и
временная недоступность Messages сама по себе исчерпывала лимит origin.

Проверяется в `FederationS2SApiService.RouteToInternalAsync`, `case ChatCreated`, **до** вызова
`ImportFederatedChat` — превышение → `EventStatus.Retry` (троттлинг — временное состояние, не порча
события) + метрика `chatcreated_quota_exceeded.{origin}` + warning-лог; `ImportFederatedChat` не
вызывается вовсе (Messages не видит спам-трафик).

Federation впервые использует Redis — конфиг `Redis` заведён в ServiceId=15 (свой бакет; было
пропущено, заводить пришлось этим же этапом).

### FederatedDmRejected → FederatedChatRejectedEvent

Privacy-отказ (`invitee.DenyFederatedDm=true` на удалённой ноде, см. [[Backend/Users]]) долетает
обратно до отправителя через `OutboxDispatcher`:

1. `ImportFederatedChat` на принимающей ноде бросает `FederatedDmRejectedException` (`ErrorCode =
   "FederatedDmRejected"` — литеральная строка, а не GUID, единственное такое исключение в проекте).
2. `DeliverEvents` мапит `FailedPrecondition` → `EventStatus.Rejected`, `error_code` в ответе =
   `"FederatedDmRejected"`.
3. На origin-ноде `OutboxDispatcher` ставит строку в `DeadLetter`; если `result.ErrorCode ==
   "FederatedDmRejected"` **и** `row.ChatId` задан — публикует `FederatedChatRejectedEvent { ChatId,
   Reason }` (namespace `Shared.Queue.Federation`).
4. [[Backend/Messages]] консюмирует событие → `Chat.FederatedStatus = Rejected` на origin-ноде;
   дальнейшая отправка в этот чат падает понятной ошибкой `FederatedDmRejectedException` без
   повторных бесплодных попыток через федерацию.

 + S2S-профиль (этап 2.1) + outbox (этап 2.2)

Нода умеет находить пиров всеми тремя способами (well-known → Navigator → manual), наполняет `KnownServers`/`KnownServerKeys` кодом, защищена от SSRF, фоново рефрешит ключи. Внутренний API управления пирами реализован полностью. Из S2S-RPC реализованы `Ping`/`GetServerKeys`/`GetUserProfile`/`DeliverEvents`, внутренние — `ResolveRemoteUser`/`EnqueueOutbound` + управление пирами. Остальные S2S-RPC (catch-up, presence, typing) отвечают `Unimplemented`.

Федерация по умолчанию выключена (`Federation:Enabled = false`); при пустом `Federation:ServerName` сервис стартует нормально, но ключ всё равно генерируется (безвредно, лог-warning).

Единый выключатель — `FederationSwitch` (`IsActive = Enabled && ServerName задан`, P1-04). Гейчит **все** federation-пути: входящий S2S (XFed-интерсептор, до IsExempt — покрывает и bootstrap `GetServerKeys`, P1-05), публикацию well-known (`503`), фоновый peer-refresh и исходящий outbox-диспетчер. Internal API (`GetFederationStatus` и пр.) остаётся доступным оператору независимо. При неактивной ноде S2S отвечает `FailedPrecondition` (`FederationNotConfigured`). Чтобы нода федерировала — задать `Federation:Enabled=true` в Settings.

## Сборка

```bash
dotnet build Backend/BarkFluff.Federation/BarkFluff.Federation.csproj
```

Миграции (`FederationContext`) применяются автоматически при старте (`Database.Migrate()`).

## Структура кода

- `Host/` содержит только gRPC-адаптеры: `FederationInternalApiService` и
  `FederationS2SApiService` сопоставляют RPC с прикладными обработчиками.
- `Features/FederationInternalApi/FederationInternalApiHandler` реализует use-cases
  внутреннего API: управление пирами и ключами, presence, исходящий typing,
  федеративное скачивание и outbox.
- `Features/FederationS2SApi/FederationS2SApiHandler` реализует S2S use-cases:
  presence и file streaming, typing, ключи, профиль и доставку событий.

Стриминговые RPC намеренно вызывают handler напрямую: это соответствует подходу
[[Backend/Onliner]] для долгоживущих server-streaming операций и не создаёт
искусственных MediatR-команд, несущих `IServerStreamWriter`.

## Ed25519-ключи (`SigningKeyService`)

- Библиотека — `BouncyCastle.Cryptography` 2.6.2 (managed, снимает chiseled-риск конструктивно; выбор и бенчмарки — [[../../../docs/rearch/phase-0/step-0.5-report|docs/rearch/phase-0/step-0.5-report.md]]).
- Таблица `SigningKeys` в `FederationDb`: `KeyId` (PK, `"ed25519:N"`), `PublicKey`/`PrivateKeySeed` (raw 32 байта), `CreatedAt`, `ExpiredAt`, `RevokedAt`. Приватный ключ хранится без шифрования (MVP, тот же уровень доверия, что у прочих секретов в конфиг-БД соседей) — **отличие от исходной рекомендации дока 02**: не Settings-сервис, а `FederationDb` (см. правки доков ниже).
- При старте: если нет ключа с `ExpiredAt IS NULL AND RevokedAt IS NULL` — генерируется `ed25519:1`. Идемпотентно (рестарт не плодит ключи).
- `RotateSigningKey` (internal RPC, `TokenType.Service`): новый ключ `ed25519:{N+1}` становится активным, у старого `ExpiredAt = now + Federation:KeyRotationOverlapDays` (дефолт 30 дней в коде). Well-known после ротации публикует оба ключа, подписан новым. Публикуется `SigningKeyRotatedEvent` → fan-out-очередь на каждый инстанс → каждый перезагружает `ActiveSigningKeyCache` и well-known (масштабирование, docs/scaling/federation.md — иначе остальные инстансы подписывают исходящие старым ключом до рестарта).

## Well-known-документ

- `Services/WellKnownDocumentService.cs`: JSON по схеме [[../../../docs/rearch/03-discovery|docs/rearch/03-discovery.md]] («Источник 1»), подписан активным ключом. Канонизация — JCS/RFC 8785 через NuGet-пакет `JsonCanonicalizer` 1.0.0 (управляемый порт `Org.Webpki.JsonCanonicalizer` от cyberphone/json-canonicalization, проверено по исходникам GitHub).
- Кеш в памяти: пересобирается при старте и после `RotateSigningKey`, на GET отдаётся без пересборки.
- **Второй Kestrel-листенер HTTP/1** на порту `Federation:WellKnownPort` (дефолт 7031 в коде — свободен в каталоге Settings; тот же механизм, что `RunSettings:Http1Port` у Bots/Calls/Files, но отдельный конфиг-ключ по плану этапа). Основной gRPC-порт 7030 настроен под h2c и HTTP/1-GET не принимает.
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
- Discovery-на-лету — в `XFedServerInterceptor`: неизвестный `(origin, key_id)` → `ServerResolver.ResolveAsync` → повторная проверка один раз. `Services/RedisDiscoveryTriggerRateLimiter.cs` (`IDiscoveryTriggerRateLimiter`) — не чаще раза в 5 минут per-server, cooldown глобальный через Redis `SET NX EX 300` на ключе `fed:discovery:{server}` (масштабирование: in-memory вариант позволял запустить discovery с каждого инстанса), иначе флуд случайными key_id заставляет ноду долбить чужой well-known.
- `BackgroundServices/PeerRefreshBackgroundService.cs` — раз в час проверяет due-пиров (не-Manual, Active/Unreachable): рефреш раз в сутки; после 3 неудач подряд → `Unreachable`, дальше экспоненциальный backoff (2^N часов, кап 24ч); успех возвращает `Active`. Счётчик неудач — in-memory (как троттлинг регистрации в Navigator), потеря при рестарте безвредна. Кандидаты грузятся с `Include(s => s.Keys)` — иначе `HasTrustedContinuity` в `RefreshManualPeerAsync` видит пустую коллекцию и manual-рефреш ключей молча никогда не применяется (баг, найден юнит-тестом `Refresh_ManualPeerDue_RefreshesKeysWithTrustedContinuity`).
- Internal-RPC (`FederationInternalApiService`): `GetKnownServers`, `UpsertManualPeer` (валидация только синтаксиса, без проверки диапазонов), `SetServerBlocked`, `GetFederationStatus` (outbox-счётчики — честные нули до Фазы 2).

## Тесты

`Tests/BarkFluff.Federation.Tests/` — ~200 тестов, все зелёные без Docker/Postgres (EF InMemory + SQLite in-memory + `Microsoft.AspNetCore.TestHost` + loopback-Kestrel): каноническая строка (fixed vector), Sign/Verify roundtrip + негативы, in-proc `TestServer`-хост гоняет реальный `FederationS2SApiService`+`XFedServerInterceptor`+`XFedRawBytesMiddleware` — валидный `Ping` OK, отсутствие заголовков/битая подпись/чужой `destination` → `Unauthenticated`, просроченный `timestamp` → `Unauthenticated` + `x-error-code=ClockSkewDetected`, заблокированный origin → `PermissionDenied`, `GetServerKeys` без подписи — OK. Плюс (1.4): `ServernameValidatorTests` (таблица IP-литерал/localhost/punycode-гомограф/приватные диапазоны/http-исключение для manual), `ServerResolverTests` (фейковые `IWellKnownClient`/`INavigatorClient` — порядок фолбэков, кросс-сверка, ServerNotFound, Manual не трогается, кеш, блоклист, доверенная цепочка при ротации).

Полное покрытие остального кода сервиса (кроме `Program.cs`, миграций и сущностей):

- **Services**: `FederationSwitchTests`, `DiscoveryTriggerRateLimiterTests`, `ActiveSigningKeyCacheTests`, `WellKnownDocumentServiceTests` (JCS-проверка подписи документа, expired_at, SPKI), `XFedClientInterceptorTests` (заголовки + верифицируемая подпись, отсутствие активного ключа), `XFedRawBytesMiddlewareTests` (gRPC-фрейминг, exempt GetServerKeys), `NavigatorClientTests`, `WellKnownClientTests` (ветки до сети: синтаксис/анти-SSRF/DNS).
- **Infrastructure**: `OutboxWriterTests` (подпись активным ключом, исключение own-ноды, distinct-адресаты).
- **Consumers**: все 5 — гейтинг `Federation:Enabled`/`IsFederated`, порядок `ChatCreated`→`NewMessage`, поля payload'ов, `SessionRevokedConsumer` → `TokenRevocationCache`.
- **BackgroundServices**: `OutboxDispatcherTests` (backoff 30s, maxAttempts → DeadLetter, per-chat упорядочивание без head-of-line blocking, лимиты 100 событий/1 МБ, transport_error, per-event классификация OK/ALREADY_PROCESSED/REJECTED/RETRY/no_result — через `LoopbackS2SServer`: реальный Kestrel h2c на 127.0.0.1:0 + подмена DNS виртуальным `ServernameValidator.ResolveAndValidateAsync`), `OutboxJanitorTests` (TTL-очистка на **SQLite in-memory** — EF InMemory не поддерживает `ExecuteDeleteAsync`; подкласс контекста добавляет конвертер для `string[] TlsSpkiSha256`), `PeerRefreshBackgroundServiceTests` (континуитет manual-рефреша, 3 неудачи → Unreachable перезапуском сервиса, восстановление → Active).
- **Host**: `DeliverEventsTests` (весь конвейер: missing_origin/invalid_event_id/origin_mismatch/дедуп/unknown_key/revoked/expired/invalid_signature/author_not_origin/RETRY не индексируется/неизвестный payload → REJECTED; `ServerCallContext` — конкретный `TestServerCallContext`, т.к. Moq не настраивает невиртуальные свойства — расширяемость через protected `*Core`-члены), `GetUserProfileS2STests` (маппинг username/uuid, avatar→`FederatedFileRef`), `FederationInternalApiServiceTests` (ротация с обновлением кешей, CRUD пиров + reconcile ключей, блок/разблок, статус-счётчики, `EnqueueOutbound`, `ResolveRemoteUser` — включая успех и RpcException через loopback-пир).

## Двух-нодовый стенд

`Backend/dev-federation-testbed/` — `docker-compose.node2.yml` (вторая нода поверх основного dev-стека), `seed-peers.sql` (шаблон ручного сида KnownServers/KnownServerKeys), `fedping/` (мини-CLI вне `BarkFluff.sln`, дублирует канонизацию+подпись, шлёт `Ping` изнутри `barkfluff-network`), подробный README. Сборка/офлайн-крипта проверены (`dotnet build`/сигнатура вручную), сам docker-стенд не поднимался в этой сессии — Docker вне задач ассистента.

## gRPC API

- `FederationS2SApi` (`federation_api.proto`) — S2S-трафик, авторизация — XFed (не XAuth). Реализованы `Ping`, `GetServerKeys`, `GetUserProfile` (этап 2.1: профиль локального пользователя через `UsersServerApi.GetFederatedProfile` с privacy-фильтрацией).
- `FederationInternalApi` (`federation_internal_api.proto`) — внутренний API (для AdminPanel и других сервисов ноды), XAuth `TokenType.Service`. Реализованы `RotateSigningKey`, `GetKnownServers`, `UpsertManualPeer`, `SetServerBlocked`, `GetFederationStatus`, `ResolveRemoteUser` (этап 2.1: parse FID/UUID → `ServerResolver` → подписанный S2S `GetUserProfile` на ноду-владельца). Federation не хранит пользовательское состояние — кеш remote-профилей ведёт [[Backend/Users]] (через `UpsertRemoteUsers`).

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
- `UsersService:Host/Token` — ключи для gRPC-клиента к [[Backend/Users]] (нужен с этапа 2.1 для `GetUserProfile` → `GetFederatedProfile`). Глобальные (ServiceId=0), уже раздаются каталогом Settings.

## Метрики

- XFed: `s2s_requests_in`/`s2s_requests_out`, `s2s_signature_failures`, `s2s_clock_skew_rejections`, `s2s_spki_pin_rejections`.
- Discovery: `discovery_lookups.{wellknown|navigator|manual|cache}`, `discovery_failures`, `known_servers_active` (gauge, снимается `PeerRefreshBackgroundService`), `wellknown_signature_failures`, `crosscheck_mismatches`.

## Nginx (этап 1.6)

- `docker/nginx/federation.conf` — субдомен `federation.barkfluff.com`, по образцу `users.conf`: `grpc_pass grpc://federation:7030`, но таймауты `3600s` (как у `calls.conf`) — `SubscribePresence`/`FetchFile` (Фазы 3-4) долгоживущие. У `ngx_http_grpc_module` нет директивы отключения буферизации ответа (в отличие от `proxy_buffering` для HTTP-проксирования — проверено по официальной документации nginx); DATA-фреймы HTTP/2 и так прокидываются по мере поступления. Rate-limit `federation_s2s` (30r/s) — см. [[Backend/Nginx]].
- `/.well-known/barkfluff` отдаётся apex-сервером (`barkfluff.single-server.conf`), не `federation.conf` — `location = /.well-known/barkfluff` проксирует на `federation:7031`. Apex для публичных нод требует CA-валидный серт (Let's Encrypt) — это bootstrap-канал; своя rate-limit-зона `federation_wellknown` (5r/s, жёстче).
- Двух-нодовый стенд (`Backend/dev-federation-testbed/`) расширен: `certs/make-certs.sh` (self-signed + вывод SPKI-отпечатка), `docker-compose.nginx.yml` (nginx-node1/nginx-node2 перед каждой federation-нодой), `nginx/node1.conf`/`node2.conf`, `seed-peers.sql` обновлён на `https://nginx-nodeX` + реальные SPKI. Негативный тест пиннинга — README стенда, раздел 7.

## AdminPanel (этап 1.7)

Страница «Федерация» (`/federation` → `Pages/v2/federation.html`, только `v2/` — актуальный UI) — статус ноды (server_name/enabled/known_servers_active/outbox-нули), таблица ключей (ротация по кнопке с confirm), таблица пиров (блок/разблок, форма «Добавить пир»). Backend — `Endpoints/FederationEndpoints.cs` (`/api/federation/status|peers|keys/rotate`), gRPC-клиент `FederationInternalApi.FederationInternalApiClient` (`FederationService:Host/Token`, без интерцептора кроме `JwtClientInterceptor` — как у соседей). Детали — [[Backend/AdminPanel-ProjectMap]].

**Отклонение от плана**: таблица ключей не показывает колонку "created" — `SigningKey` (wire-сообщение в `federation_api.proto`) не несёт `created_at`, только `key_id`/`public_key`/`expired_at`; расширение публичного S2S-контракта ради косметической колонки в админке не входит в скоуп 1.7.

## Планы дальнейших этапов

Фаза 1 завершена (1.1–1.7). Этап 2.1 завершён (S2S `GetUserProfile` + внутренний `ResolveRemoteUser`). Следующий шаг — Фаза 2 (outbox, доставка событий, MassTransit/RabbitMQ) по `docs/rearch/10-roadmap.md`, планы по каждому этапу в `docs/rearch/phase-2/`.

## Presence-мост (этап 4.3, docs/rearch/phase-4/step-4.3-federation-presence.md)

Статусы пересекают границу ноды. Транспортный профиль принципиально другой, чем у сообщений: **не через outbox**, а живыми S2S-стримами. Потеря события допустима by design, персистентности нет, ретраев по событиям нет — вместо них реконнект и периодический ресинк.

### Обе стороны одной картинкой

```
Onliner(B) × N инстансов
  └─ SetPresenceInterest(instance_id, ПОЛНЫЙ набор uuid)   каждые 20с
       └─ PresenceInterestRegistry (union живых инстансов, TTL 60с)
            └─ RemoteUserServerCache: uuid → server_name (через Users.GetUsersByUuid, TTL 1ч)
                 └─ PresenceStreamManager: ОДИН S2S-стрим на ноду
                      → SubscribePresence(user_uuids[])  ───────────────┐
                                                                        │
                      ← PresenceEvent(uuid, status, last_seen)          │
                      └─ OnlinerServerApi.UpsertRemoteStatus            │
                                                                        │
Federation(A), origin-сторона  ─────────────────────────────────────────┘
  1. FederationSwitch.IsActive        → иначе FederationNotConfigured
  2. origin в блоклисте               → PermissionDenied
  3. |user_uuids| > лимита            → ResourceExhausted
  4. Messages.CheckFederatedPresenceAccess(origin, uuids)   ← риск №42
  5. Users.GetUsersByUuid → uuid → локальный user_id (только НАШИ)
  6. IncomingPresenceRegistry.Add → начальный снимок (MarkAllDirty)
  7. цикл: RabbitMQ-события помечают «грязных» → Onliner.GetLocalPresence → в стрим
```

### Ключевые решения

- **Один агрегированный стрим на пару нод**, а не стрим на подписку: подписчиков много, нод — единицы.
- **Обновление набора = переоткрытие стрима.** Control-сообщений в v1 контракта нет, набор передаётся в самом `SubscribePresenceRequest`. Против флаппинга — дебаунс `Federation:PresenceResubscribeMinSeconds`.
- **Интерес приходит полным набором, а не дельтами.** Onliner масштабируется горизонтально; свести «+uuid/−uuid» от нескольких инстансов без общего состояния невозможно. Протухшие наборы выпадают по TTL — рестарт инстанса самолечится.
- **Изменения статусов Federation берёт из RabbitMQ**, а не отдельным стримом из Onliner: `OnlineStatusChangedEvent` уже публикуется fan-out'ом для межинстансной доставки, Federation заводит свою per-instance очередь (`presence-status-changed-federation-{instance}`, autodelete). Долгоживущий внутренний gRPC-стрим не нужен вовсе.
  - Контракт события переехал в `Shared/BarkFluff.Shared.Queue/Onliner/`, но **namespace остался `BarkFluff.Onliner.Messages`** — MassTransit выводит URN из namespace, и его смена разорвала бы совместимость между инстансами разных версий во время выкатки.
- **Privacy Federation не дублирует.** Консюмер только помечает пользователя «грязным»; сам статус перечитывается у Onliner (`GetLocalPresence`) в момент отправки, и privacy применяет он — владелец данных (инвариант №27). Побочный эффект приятный: в стрим физически не может уйти состояние из устаревшего события.
- **Проверка отношений обязательна** (риск №42): без активного федеративного чата с нодой-подписчиком статус не отдаётся. Пустой результат → стрим **открывается и молчит**, а не `PermissionDenied`: иначе подписчик узнал бы, какие uuid у нас существуют.
- **Обрыв стрима гасит статусы.** Все uuid этой ноды получают `UpsertRemoteStatus(UNKNOWN)` — «залипший онлайн» хуже отсутствия данных. Затем реконнект с экспоненциальным backoff (кап — минута: presence эфемерен).
- **`UNKNOWN` неоднозначен намеренно.** «Скрыт privacy» и «статуса нет» снаружи неразличимы — иначе privacy утекала бы по каналу метаданных.
- **Keepalive — транспортный (HTTP/2), не прикладной.** В `PresenceEvent` нет keepalive-типа, и выдумывать его нельзя: это сломало бы сторонние реализации протокола.

### Coalescing

`IncomingPresenceSubscription` держит множество «грязных» пользователей и время последней отправки на каждого. Цикл тикает часто, но отправляет только тех, у кого истекло окно `Federation:PresenceCoalesceSeconds`. N изменений подряд стоят одной отправки, и она несёт последнее состояние — потому что статус перечитывается в момент отправки. Раз в `Federation:PresenceResyncSeconds` помечаются все — страховка от пропущенного fan-out-события.

### Capability `presence`

`Ping` объявляет `"presence"` при активной федерации. `PeerCapabilityCache` кеширует ответ пира (10 мин при успехе, 1 мин при сбое — fail-closed). Партнёр без capability не опрашивается вовсе: метрика `presence_peer_unsupported`. Это ответ на риск «асимметрия ожиданий».

### Конфигурация (бакет `ServiceId.Federation = 15`)

| Ключ | Дефолт | Смысл |
|------|--------|-------|
| `OnlinerService:Host` / `Token` | populator | клиент Onliner (`GetLocalPresence`, `UpsertRemoteStatus`) |
| `Federation:MaxPresenceSubscriptionSize` | 500 | лимит uuid в подписке (обе стороны) |
| `Federation:PresenceInterestTtlSeconds` | 60 | TTL записи интереса ≈ 3 × интервала репортера Onliner |
| `Federation:PresenceReconcileSeconds` | 10 | период сверки желаемых подписок с фактическими |
| `Federation:PresenceResubscribeMinSeconds` | 5 | дебаунс переоткрытия стрима |
| `Federation:PresenceCoalesceSeconds` | 5 | окно coalescing на пару (пользователь, стрим) |
| `Federation:PresenceResyncSeconds` | 300 | период полного ресинка снимка |

Миграция — `20260727020000_AddFederationPresenceConfiguration`.

### Метрики

`presence_streams_out` / `presence_streams_opened` / `presence_streams_closed`, `presence_events_out` / `presence_events_in`, `presence_subscribe_rejected.{blocked|limit|not_resolved}`, `presence_access_denied_uuids`, `presence_resubscribes`, `presence_stream_errors`, `presence_peer_unsupported`, `presence_peer_ping_errors`, `presence_subscription_truncated`, `presence_local_changes_observed`, `presence_interest_uuid_unknown`, `presence_interest_resolve_errors`, gauge `presence_interest_uuids`.

## Typing-мост (этап 4.4, docs/rearch/phase-4/step-4.4-typing-bridge.md)

«Печатает…» доходит до собеседника на другой ноде. Транспорт — **unary fire-and-forget**, а не стрим: контракт такой by design, отдельное соединение под индикатор набора не нужно.

```
Onliner(A).SetTypingStatus  (локальный fan-out отработал ПЕРВЫМ и от федерации не зависит)
  └─ FederatedTypingSender → FederationInternalApi.DeliverTypingOutbound
       └─ Federation(A): coalescing → ServerResolver → capability "typing"
            └─ S2S DeliverTyping ──────────────────────────────┐
                                                               │
Federation(B): свитч → блоклист → rate limit → валидация ──────┘
  └─ OnlinerServerApi.InjectRemoteTyping → fan-out → подписчики чата
```

### Никаких ретраев, никакого outbox

Потеря индикатора набора некритична: он и так гаснет по клиентскому таймауту. Ошибка отправки → метрика `typing_out.error`, и всё. Ретрай стоил бы дороже пользы, а outbox превратил бы эфемерное событие в персистентное.

### Onliner(A) — исходящая ветка

`SetTypingStatusCommandHandler` **после** локальной публикации `TypingChangedEvent` берёт федеративный контекст из расширенного ответа `ChatMembershipFilter` (этап 4.1):

- контекста нет (чат локальный) → выход без единой аллокации — это подавляющее большинство вызовов;
- `RequesterUuid` пуст → выход (федерировать нечего);
- иначе `DeliverTypingOutbound(chat_id, sender_uuid, action, уникальные ноды из peers)` с deadline 2с.

`FederatedTypingSender` резолвит клиента Federation через `IServiceProvider.GetService`, а не параметром конструктора: на ноде без федерации он не зарегистрирован, и обязательный параметр уронил бы построение контейнера. Ошибки — debug-лог, не warning: heartbeat приходит каждые 4–5 секунд, и недоступная федерация иначе засорила бы логи.

**Локальный typing не изменился вовсе**: проверка членства, fan-out, «кроме отправителя» — как были.

### Federation(A) — coalescing

`TypingCoalescer`: не чаще одной отправки в `Federation:TypingCoalesceSeconds` (дефолт 2) на ключ `(chat_id, sender_uuid, destination)`. In-memory с ленивой чисткой — состояние живёт секунды, persistent-хранилище тут избыточность.

**Исключение: `CANCELLED` проходит всегда** — иначе индикатор у собеседника гас бы только по клиентскому таймауту.

Свою ноду в списке назначений игнорируем (симметрично `OutboxWriter`). Партнёр без capability `typing` не опрашивается.

### Federation(B) — приём и защита

Порядок проверок неслучаен — сначала дешёвые, потом дорогие, иначе спам оплачивался бы нашими Users/Messages:

1. `FederationSwitch.IsActive` → `FailedPrecondition`; origin в блоклисте → `PermissionDenied`.
2. **Rate limit per-origin** (`TypingRateLimiter`, Redis-счётчик, ключ `fed:typing:{origin}:{yyyyMMddHHmm}`), лимит `Federation:TypingRateLimitPerOriginPerMinute` (дефолт 600 — при coalescing 2с это ~20 одновременно печатающих пар). Превышение → `ResourceExhausted` + метрика `typing_rate_limited.{origin}`. **Алертов не требует**: typing дешёвый, всплеск не инцидент.
3. **Валидация авторства**: `server_name` отправителя (из `Users.GetUsersByUuid`) обязан совпасть с origin — «нода говорит только за своих».
4. **Валидация членства**: `Messages.CheckChatMembership(user_uuid = sender_uuid, chat_ids = [chat_id])` — uuid-ветка из 4.1. Знание `chat_id` само по себе прав не даёт.
5. **Кеш проверок 3–4** (`TypingValidationCache`, ключ `(origin, sender_uuid, chat_id)`), TTL `Federation:TypingValidationCacheSeconds` (дефолт 30). Отрицательный результат кешируется тоже, но **вдвое короче** — иначе спамящая нода бесплатно нагружала бы Users/Messages на каждом heartbeat'е.
6. Успех → `OnlinerServerApi.InjectRemoteTyping`.

### Конфигурация (бакет `ServiceId.Federation = 15`)

| Ключ | Дефолт | Смысл |
|------|--------|-------|
| `Federation:TypingCoalesceSeconds` | 2 | окно coalescing на (чат, отправитель, нода) |
| `Federation:TypingDeadlineMs` | 2000 | deadline S2S-вызова typing |
| `Federation:TypingRateLimitPerOriginPerMinute` | 600 | лимит входящих typing per-origin |
| `Federation:TypingValidationCacheSeconds` | 30 | TTL кеша валидации (отрицательный — вдвое короче) |

Миграция — `20260727030000_AddFederationTypingConfiguration`.

### Метрики

`typing_out.{ok|error|coalesced|not_resolved|not_configured}`, `typing_in.ok`, `typing_rate_limited.{origin}`, `typing_rejected.{author_not_origin|not_member}`, `typing_peer_unsupported`.

## Скачивание federated-файлов: транспорт и авторизация ноды (этап 3.2, docs/rearch/phase-3/step-3.2-fetchfile-access.md)

Файлы не реплицируются: байты живут только на origin, принимающая нода **проксирует** их по запросу. Клиентского пути здесь ещё нет (3.3) — этап строит транспорт и авторизацию уровня ноды.

```
Files(B) ──FetchRemoteFile──▶ Federation(B) ──S2S FetchFile──▶ Federation(A)
                                                                  │
                              свитч → блоклист → rate limit ──────┤
                              Messages(A).CheckFileFederationAccess│  ← авторизация НОДЫ
                                                                  ▼
                                                   Files(A).FetchFileStream (Range в S3)
```

### Два независимых уровня доступа

- **Origin авторизует НОДУ**: file_id должен фигурировать во вложении активного fed-чата, участником которого является запрашивающая нода (`CheckFileFederationAccess`, [[Backend/Messages]]).
- **Принимающая нода авторизует ПОЛЬЗОВАТЕЛЯ** при выдаче ссылки (этап 3.3).

Ни один уровень не доверяет другому.

### Origin-сторона (`FetchFile`)

Порядок проверок — от дешёвых к дорогим, иначе флуд оплачивался бы нашими Messages и S3:

1. `FederationSwitch.IsActive` → `FailedPrecondition`.
2. Origin в блоклисте → `PermissionDenied`.
3. **Rate limit per-origin** (`FetchFileRateLimiter`, Redis, минутное окно, `Federation:FetchFileRateLimitPerOrigin`, дефолт 30). Бакет отдельный и строже прочих: каждый запрос — это чтение из S3 и исходящий трафик (риски №20/21).
4. `CheckFileFederationAccess` → отказ **до начала стрима**: он должен быть статусом, а не оборванным потоком.
5. `Files.FetchFileStream` → перекладывание чанков в S2S-стрим.

**Ветка аватара (этап 3.4).** Перед chat-проверкой `FetchFile` спрашивает `Files.GetFileData` о типе: `UserAvatar` → `Files.CheckFedAvatarAccess` (приватность владельца), остальное → `Messages.CheckFileFederationAccess` (общий чат). Один вызов на старт стрима; кеш намеренно не вводится — приватность должна действовать немедленно, а не через TTL.

### Принимающая сторона (`FetchRemoteFile`)

- `ServerResolver`: нода неизвестна/заблокирована → `PermissionDenied`.
- **Deadline на весь вызов не ставится** — стрим большого файла законно долгий. Вместо него: connect-timeout канала (`Federation:S2SConnectTimeout`, дефолт 10с) и **idle-надзор** (`Federation:RemoteFileIdleTimeout`, дефолт 60с), перезаряжаемый на каждом полученном чанке. Медленный, но живой origin допустим; замолчавший — нет.
- **Защита №44 (первый уровень):** первый чанк несёт `total_size`; как только сумма полученных байт его превысила — стрим рвётся `Aborted` + метрика `remote_file_size_mismatch`. Точная сверка со снапшотом — в 3.3.
- Маппинг ошибок: `PermissionDenied`/`NotFound` от origin пробрасываются как есть (вызывающий отличает «нельзя» от «нет файла»); сеть/таймаут → `Unavailable` (HTTP-код подберёт 3.5).

### Конфигурация (бакет `ServiceId.Federation = 15`)

| Ключ | Дефолт | Смысл |
|------|--------|-------|
| `FilesService:Host` / `Token` | populator | клиент Files (`FetchFileStream`) |
| `Federation:FetchFileRateLimitPerOrigin` | 30 | запросов файлов с одной ноды в минуту |
| `Federation:S2SConnectTimeout` | 10 | таймаут установления S2S-соединения |
| `Federation:RemoteFileIdleTimeout` | 60 | максимальное молчание origin внутри стрима |

Миграция — `20260728020000_AddFederationFileConfiguration`.

### Метрики

`fetchfile_requests.{ok|denied|rate_limited}`, `fetchfile_bytes_out`, `remote_file_fetches.{ok|denied|error|idle_timeout|not_resolved}`, `remote_file_bytes_in`, `remote_file_size_mismatch`.

## Circuit breaker скачивания (этап 3.5, docs/rearch/phase-3/step-3.5-origin-down-ux.md)

Лежащая нода не должна съедать connect-timeout на каждом обращении: после `Federation:RemoteFileCircuitFailures` (дефолт 3) подряд идущих **транспортных** неудач `FetchRemoteFile` к этой ноде отвечает `Unavailable` (`origin_circuit_open`) сразу, не заходя в сеть, на `Federation:RemoteFileCircuitOpenSeconds` (дефолт 60).

**Что считается сбоем.** Только транспорт: сеть, TLS, резолв, connect-timeout, idle-таймаут. Отказ **живой** ноды (`PermissionDenied`, `NotFound`) сбоем **не** является — приватный аватар или чужой файл не значат, что нода недоступна, и не должны блокировать скачивание остальных файлов оттуда же.

**Half-open без отдельного состояния:** по истечении окна `TryEnter` снова пропускает запрос — он и есть пробный. Успех закрывает circuit, неудача открывает его на новое окно.

In-memory, per-instance, без БД: состояние живёт секунды-минуты, рестарт с чистым circuit'ом стоит максимум одного лишнего похода в сеть.

Метрики: `remote_file_circuit_open.{server}`, `remote_file_fetches.circuit_open`.
