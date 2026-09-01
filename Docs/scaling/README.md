# Горизонтальное масштабирование backend-сервисов BarkFluff

Анализ: можно ли поднять **несколько экземпляров** каждого микросервиса за балансировщиком
(Docker DNS round-robin через nginx; sticky-сессии сейчас не настроены) для распределения нагрузки,
не нарушая корректность. Для сервисов, которые так **не могли**, в этой папке лежал план
реализации — что доработать.

> Статус: планы **реализованы** (2026-08-14). Файлы в этой папке сохранены как описание решений:
> разделы «Как было (блокеры до реализации)» объясняют мотивацию, «Что сделано» — итог.
> Продакшн-код уже содержит все изменения.

## Метод

Каждый сервис проверялся на типовые блокеры stateless-масштабирования:

- **In-memory состояние как источник истины** — `Singleton` + `ConcurrentDictionary`/`MemoryCache`,
  которое не реплицируется между экземплярами (реестры gRPC-стримов, кэши, счётчики rate-limit,
  сессии).
- **gRPC server-streaming** — долгоживущее соединение клиента живёт на **одном** экземпляре; событие
  для этого клиента должно прийти именно туда.
- **MassTransit-топология** — фиксированное именованное `ReceiveEndpoint` = *competing consumer*:
  при N экземплярах событие получает только **один**. Для доставки в стримы и для broadcast-инвалидации
  нужен **fan-out** (каждый экземпляр получает каждое событие).
- **Дублируемые фоновые задачи** (`BackgroundService`/`IHostedService`/таймеры) — выполнятся N раз.
- **Локальное состояние** — embedded-БД (SQLite/LiteDB), локальные файлы/кэш.
- **In-process блокировки** (`lock`/`SemaphoreSlim`) — защищают только внутри процесса.

## Сводка

### ✅ Могут масштабироваться сейчас

| Сервис | Почему |
|--------|--------|
| Notification | MassTransit competing-consumer (`notifications-email-handler`), без состояния |
| CloudMessaging | Background-consumer RabbitMQ (competing), без локального состояния |
| Web | YARP reverse-proxy, полностью stateless |
| Developers | gRPC + PostgreSQL, без in-memory состояния и фоновых задач |
| Updates | Все 17 стрим-эндпоинтов — fan-out (`{name}-{InstanceId}`, AutoDelete); локальные реестры стримов корректны при fan-out — [updates.md](updates.md) |
| Onliner | Presence в Redis (ZSET `onliner:presence`) + fan-out + `OfflineDetectionService` single-runner — [onliner.md](onliner.md) |
| Calls | Fan-out доставки событий + durable-таймаут ринга (`CallRingTimeoutSweeper`, БД-опрос + атомарный захват вместо in-memory таймера) — [calls.md](calls.md) |
| Bots | Rate-limit/polling-guard в Redis, notifier/registry-cache — fan-out, отзыв сессий — fan-out — [bots.md](bots.md) |
| FastAuth | QR-сессии в Redis (Lua-переходы) + pub/sub wake-up стримов — [fastauth.md](fastauth.md) |
| Federation | Outbox — claim `FOR UPDATE SKIP LOCKED` + reclaim, janitor — single-runner, discovery-cooldown и ротация ключей — глобальные — [federation.md](federation.md) |
| Files | Постоянные данные в общем S3; локальный temp — эфемерный per-request (осознанный компромисс); отзыв сессий — fan-out — [files.md](files.md) |
| Identity | PostgreSQL, stateless; отзыв сессий — fan-out — [identity.md](identity.md) |
| Users | PostgreSQL, stateless; отзыв сессий — fan-out — [users.md](users.md) |
| Messages | PostgreSQL + Redis (`SecretMessageBuffer`, `PrivateChatInviteStore`); отзыв сессий — fan-out — [messages.md](messages.md) |

### 🔒 Сознательно single-instance — масштабировать не планируем

Эти сервисы по решению команды работают в **одном** экземпляре; план масштабирования для них не
пишется. Их локальное/in-memory состояние указано для полноты картины (при желании масштабировать
в будущем — те же приёмы, что и выше).

| Сервис | Причина / состояние |
|--------|---------------------|
| Navigator | Работает на отдельном ПК, всегда один экземпляр. Локальная SQLite + in-memory `RegistrationThrottle` |
| Settings | Один экземпляр (по решению). Технически stateless на PostgreSQL |
| ClientStorage | Один экземпляр. Локальная SQLite + локальный файловый кэш (`LocalFileCache`) + 2 воркера |
| AdminPanel | Один экземпляр. LiteDB (файловая БД) + in-memory job/pending-dicts + singleton Telegram-бот |
| Beacon | Один экземпляр. `ServerRegistrationService` регистрирует сервер в Navigator |
| WebServer | Один экземпляр. In-memory `VersionStore` + `VersionPollingService` + singleton Telegram-бот |

## Сквозной блокер: отзыв сессий (TokenRevocation) — РЕШЁН

`TokenRevocationCache` (`Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs`) остаётся
per-instance `ConcurrentDictionary` (это корректно), но наполняется теперь **fan-out'ом**:
эндпоинт `SessionRevokedConsumer` в каждом сервисе использует пер-instance очередь
`session-revoked-{svc}-{InstanceId.Current}` с `AutoDelete = true; Durable = false`
(см. [_shared-token-revocation.md](_shared-token-revocation.md), вариант A). Каждый экземпляр
получает каждое событие отзыва — дыры «отозванный токен проходит на другом инстансе» больше нет.

## Общие приёмы решения

1. **Fan-out вместо competing-consumer** — уникальная очередь на экземпляр, каждый получает каждое
   событие. Нужен для доставки в gRPC-стримы (Updates/Onliner/Calls) и для broadcast-инвалидации
   (TokenRevocation). In-memory реестр стримов при этом остаётся корректным: стрим живёт на одном
   экземпляре, fan-out-событие туда придёт.
2. **Redis как общий backplane/хранилище** — в проекте уже используется (образец —
   `Backend/BarkFluff.Messages/Persistence/Services/SecretMessageBuffer.cs`,
   `PrivateChatInviteStore.cs`, `IConnectionMultiplexer`). Для presence, сессий, распределённых
   счётчиков/локов, pub/sub-сигналов.
3. **Single-runner для фоновых задач** — распределённый лок / leader election (Redis) или
   `FOR UPDATE SKIP LOCKED` (PostgreSQL), чтобы таймер отработал один экземпляр.
4. **Durable-планировщик** вместо in-memory-таймеров — БД-опрос с атомарным захватом
   (Calls) либо отложенные сообщения.
