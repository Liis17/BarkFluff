# Горизонтальное масштабирование backend-сервисов BarkFluff

Анализ: можно ли поднять **несколько экземпляров** каждого микросервиса за балансировщиком
(Docker DNS round-robin через nginx; sticky-сессии сейчас не настроены) для распределения нагрузки,
не нарушая корректность. Для сервисов, которые так пока **не могут**, в этой папке лежит план
реализации — что доработать.

> Статус: это **план**, не реализация. Продакшн-код здесь не менялся.

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

### ✅ Могут масштабироваться сейчас — доработок не нужно

| Сервис | Почему |
|--------|--------|
| Notification | MassTransit competing-consumer (`notifications-email-handler`), без состояния |
| CloudMessaging | Background-consumer RabbitMQ (competing), без локального состояния |
| Web | YARP reverse-proxy, полностью stateless |
| Developers | gRPC + PostgreSQL, без in-memory состояния и фоновых задач |

### 🔒 Сознательно single-instance — масштабировать не планируем

Эти сервисы по решению команды работают в **одном экземпляре**; план масштабирования для них не
пишется. Их локальное/in-memory состояние указано для полноты картины (при желании масштабировать
в будущем — те же приёмы, что и ниже).

| Сервис | Причина / состояние |
|--------|---------------------|
| Navigator | Работает на отдельном ПК, всегда один экземпляр. Локальная SQLite + in-memory `RegistrationThrottle` |
| Configuration | Один экземпляр (по решению). Технически stateless на PostgreSQL |
| ClientStorage | Один экземпляр. Локальная SQLite + локальный файловый кэш (`LocalFileCache`) + 2 воркера |
| AdminPanel | Один экземпляр. LiteDB (файловая БД) + in-memory job/pending-dicts + singleton Telegram-бот |
| Beacon | Один экземпляр. `ServerRegistrationService` регистрирует сервер в Navigator |
| WebServer | Один экземпляр. In-memory `VersionStore` + `VersionPollingService` + singleton Telegram-бот |

### ❌ Пока не могут, но масштабирование нужно — см. план

| Сервис | Главный блокер | План |
|--------|----------------|------|
| Updates | 16 in-memory `StreamSubscriptionsManager` + named-queue consumers | [updates.md](updates.md) |
| Onliner | in-memory реестры стримов + дублируемый `OfflineDetectionService` | [onliner.md](onliner.md) |
| Calls | in-memory реестр стримов + `CallTimeoutScheduler` | [calls.md](calls.md) |
| Bots | `BotRateLimiter` / `BotUpdateNotifier` / `BotRegistryCache` / `BotPollingGuard` | [bots.md](bots.md) |
| FastAuth | in-memory `FastAuthSessionsManager` + `FastAuthExpirationService` | [fastauth.md](fastauth.md) |
| Federation | дублируемые `OutboxDispatcher`/`OutboxJanitor` + in-memory rate-limiter | [federation.md](federation.md) |
| Files | локальная ФС для temp + shared TokenRevocation | [files.md](files.md) |
| Identity | shared TokenRevocation | [identity.md](identity.md) |
| Users | shared TokenRevocation | [users.md](users.md) |
| Messages | shared TokenRevocation | [messages.md](messages.md) |

## Сквозной блокер: отзыв сессий (TokenRevocation)

`TokenRevocationCache` (`Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs`) —
`ConcurrentDictionary` в памяти каждого экземпляра. Наполняется через `SessionRevokedConsumer`, но
его `ReceiveEndpoint` именованный и фиксированный (`session-revoked-users`, `session-revoked-updates`,
…) → *competing consumer*. При N экземплярах событие отзыва получает лишь один; на остальных
отозванный токен продолжает проходить до истечения access-token — **дыра в безопасности при
масштабировании**.

Это единственный блокер для Identity/Users/Messages и один из блокеров для Files/Bots/Updates/
Onliner/Calls. Общий план исправления — [_shared-token-revocation.md](_shared-token-revocation.md).

## Общие приёмы решения

1. **Fan-out вместо competing-consumer** — уникальная очередь на экземпляр, каждый получает каждое
   событие. Нужен для доставки в gRPC-стримы (Updates/Onliner/Calls) и для broadcast-инвалидации
   (TokenRevocation). In-memory реестр стримов при этом остаётся корректным: стрим живёт на одном
   экземпляре, fan-out-событие туда придёт.
2. **Redis как общий backplane/хранилище** — в проекте уже используется (образец —
   `Backend/BarkFluff.Messages/Infrastructure/SecretMessageBuffer.cs`,
   `PrivateChatInviteStore.cs`, `IConnectionMultiplexer`). Для presence, сессий, распределённых
   счётчиков/локов, pub/sub-сигналов.
3. **Single-runner для фоновых задач** — распределённый лок / leader election (Redis) или
   `FOR UPDATE SKIP LOCKED` (PostgreSQL), чтобы таймер отработал один экземпляр.
4. **Durable-планировщик** вместо in-memory-таймеров — отложенные сообщения MassTransit.
