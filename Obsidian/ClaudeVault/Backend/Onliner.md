# BarkFluff.Onliner

Трекинг онлайн-статусов пользователей. Порт: **7009** (.NET 10).

Принцип: **presence в Redis** (общий источник истины для всех инстансов). Статусы — sorted set `onliner:presence` (member=userId, score=last-seen ms) через `IPresenceStore`/`RedisPresenceStore`. PostgreSQL — вторичное хранилище last-seen (обновляется в фоне). Real-time оповещения — gRPC server-streaming, доставка событий между инстансами через **RabbitMQ fan-out** (стрим подписчика живёт на одном инстансе). Масштабируется горизонтально (см. `docs/scaling/onliner.md`).

Помимо онлайн-статусов сервис обслуживает **индикаторы набора текста** (typing) — чистый ретранслятор без БД и фоновых сервисов (см. ниже).

Расположение: `Backend/BarkFluff.Onliner/`

📁 **[[Backend/Onliner-ProjectMap]]** — карта всех файлов и классов проекта

## Сборка

```bash
dotnet build BarkFluff.Onliner.csproj
```

Миграции применяются автоматически при старте.

> Этап 0.4 rearch: в `onliner_api.proto` добавлены параллельные `user_uuid`/`user_uuids`-поля (Subscribe/Change/Status/Typing) для будущих remote-пользователей + новый сервис `OnlinerServerApi` (UpsertRemoteStatus/InjectRemoteTyping, Фаза 4) — пока только контракт, RPC не реализованы, логика Onliner не менялась.

## Архитектура

### Поток смены статуса

```
Client → SetOnlineStatus (gRPC)
  → OnlinerApiService
  → MediatR: SetOnlineStatusCommand
  → SetOnlineStatusCommandHandler
      → OnlineStatusStorage.UpdateStatus()
      → OnlineStatusNotifier.NotifyStatusChanged()
          → SubscriptionsManager.GetStreamsTrackingUser()
          → IServerStreamWriter.WriteAsync()
```

### Пять синглтонов-сервисов (+ Scoped-фильтр)

| Класс | Назначение |
|-------|-----------|
| `IPresenceStore` / `RedisPresenceStore` | Общий presence-стор в Redis (sorted set `onliner:presence`). `MarkOnlineAsync` (ZADD, added=переход online), `SetOfflineAsync` (ZREM, атомарно дедуплицирует offline-переход между инстансами), `GetOnlineAsync`, `GetStaleUsersAsync`, `GetOnlineSnapshotAsync`. Online = член множества; offline-записей не хранит (offline last-seen — в БД) |
| `OnlineStatusSubscriptionsManager` | Реестр gRPC-стримов по userId и subscriptionId. Дополнительно держит обратный индекс `trackedUserId → (connectionId → Stream)`, благодаря чему `GetStreamsTrackingUser` работает за O(1) |
| `OnlineStatusNotifier` | Рассылает изменения по стримам, отслеживающим пользователя |
| `TypingSubscriptionsManager` | Реестр typing-стримов по `chatId` (прямой индекс `subscriberId → подписки` + обратный `chatId → стримы`). Аналог `OnlineStatusSubscriptionsManager`, но ключ — чат, а обратный индекс хранит `SubscriberId`, чтобы не слать набор обратно самому печатающему |
| `TypingNotifier` | Ретранслирует `TypingEvent` подписчикам чата (`Task.WhenAll`), исключая отправителя |
| `ChatMembershipFilter` (Scoped) | Фильтрует `chat_ids` по членству через gRPC-клиент `MessagesServerApi.CheckChatMembership`. Fail-closed: при ошибке — пустое множество. С этапа 4.1 возвращает `ChatMembershipResult` (чаты + `RequesterUuid` + `FederatedChats`), а не голый `HashSet<string>` |

### Приватность онлайн-статуса

`Services/OnlineVisibilityFilter` (Scoped) запрашивает у [[Backend/Users]] `UsersServerApi.GetUserPrivacy`. Пропускает только `OnlineVisibility.All` или самого вызывающего. `OnlineVisibility.Friends` трактуется как `None` (сервис отношений ещё не реализован). При ошибке gRPC-вызова — **fail-closed**: статус считается скрытым. Применяется в `GetOnlineStatusQueryHandler`, `SubscribeToOnlineStatusQueryHandler`, `ChangeUsersInSubscriptionCommandHandler`.

### Фоновые сервисы

- **OfflineDetectionService**: **single-runner** (Redis-лок `RedisSingleRunner`) — каждую секунду один инстанс находит в presence пользователей без активности >5 сек, снимает их (`ZREM`), пишет offline last-seen в БД и публикует fan-out событие подписчикам. Инкрементирует `offline_detection_runs`/`status_changes.offline`/`offline_detection_errors`
- **DatabasePersistenceService**: **single-runner** (Redis-лок) — каждые 10 минут один инстанс читает снимок онлайн-пользователей из presence-стора и апсертит их last-seen в PostgreSQL (через `Entry().CurrentValues.SetValues()`; свойства `init`-only). Offline last-seen пишет `OfflineDetectionService` в момент перехода. Метрики: `db_persistence_runs`, `db_persistence_errors`, `db_records_saved_total`
- **MetricsSnapshotService**: каждые 2 секунды снимает gauge-показатели (`active_subscriptions`, `tracked_unique_users` — per-instance; `online_users_count` — глобальный из Redis) в `MetricsCollector.Set`. Без него gauge-метрики через `Increment`/`Add(-1)` не работают, потому что `_counters` сбрасываются в `SnapshotAndReset` каждые 5 секунд

### Метрики

📄 Полный реестр метрик → [[Backend/Onliner-Metrics]]

Краткий список метрик:

- **gRPC counters:** `get_online_status_requests`, `get_online_status_user_ids_total` (кумулятивная сумма всех запрошенных user_ids — `_metrics.Add`, не Increment), `set_online_status_requests` (heartbeat), `subscribe_requests`, `change_users_in_subscription_requests`
- **Typing counters:** `set_typing_status_requests`, `typing_heartbeats`, `typing_heartbeats_rejected_by_membership`, `typing_subscribe_requests`, `change_chats_in_typing_subscription_requests`, `typing_subscriptions_registered`, `typing_subscriptions_disconnected`, `typing_subscriptions_hidden_by_membership`, `typing_notifications_sent`, `typing_notification_errors`
- **Membership filter:** `membership_checks`, `membership_check_errors`
- **Подписки:** `subscriptions_registered`, `subscriptions_disconnected`, `subscriptions_hidden_by_privacy`, gauges `active_subscriptions`, `tracked_unique_users`
- **Presence:** gauge `online_users_count` (глобальный, из Redis); counters `status_changes.online`, `status_changes.offline`
- **Notifier:** `status_notifications_sent`, `status_notification_errors`
- **Privacy filter:** `visibility_checks`, `visibility_check_errors`
- **BG-сервисы:** `offline_detection_runs/_errors`, `db_persistence_runs/_errors`, `db_records_saved_total`
- **Прочее:** `sessions_revoked`, gauge `service_started_unix`

### MassTransit Consumer

- **SessionRevokedConsumer**: слушает `SessionRevokedEvent`, добавляет токен в `TokenRevocationCache` (XAuth invalidation)

### gRPC-методы (все `TokenType.User`)

| Метод | Тип | Описание |
|-------|-----|---------|
| `SetOnlineStatus` | Unary | Heartbeat клиента |
| `GetOnlineStatus` | Unary | Статусы для списка user_ids |
| `SubscribeToOnlineStatus` | Server Stream | Подписка на изменения; соединение до отмены |
| `ChangeUsersInSubscription` | Unary | Обновить список отслеживаемых без переоткрытия потока |
| `SetTypingStatus` | Unary | Heartbeat «печатает» в `chat_id` (Guid-строка). `action` (TYPING/CANCELLED; UNKNOWN→TYPING). Проверяет членство отправителя, затем ретранслирует участникам чата |
| `SubscribeToTyping` | Server Stream | Подписка на `TypingEvent` по списку `chat_ids` (Guid-строки); соединение до отмены. Фильтрует чаты по членству |
| `ChangeChatsInTypingSubscription` | Unary | Обновить список отслеживаемых чатов без переоткрытия потока. Фильтрует по членству |

### Индикаторы набора текста (typing)

Клиентская интеграция: [[Клиенты/Android]] (V1) и [[Backend/Web]] — секции «Typing-индикатор».

**Relay-модель (Telegram-style):** сервер не хранит состояние набора и не имеет фонового экспайра. Клиент шлёт `SetTypingStatus(chatId)` периодически (~каждые 4-5 сек) пока печатает; сервер публикует `TypingChangedEvent` в **RabbitMQ fan-out**, консьюмер на каждом инстансе ретранслирует `TypingEvent` всем, кто подписан на этот `chatId` через `SubscribeToTyping`, **кроме самого печатающего** (фильтр по `SubscriberId` в обратном индексе на инстансе-владельце стрима). Индикатор гаснет **по локальному таймауту на клиенте** (~6 сек) или сразу при получении `action=CANCELLED` (клиент шлёт его при очистке поля ввода).

`chat_id` — **Guid-строка** (как везде в платформе; `Chat.Id` в Messages — `Guid`). Обратный индекс `TypingSubscriptionsManager` ключуется по этой строке.

**Почему без БД и фоновых сервисов:** typing эфемерен — нет смысла персистить. Это снимает необходимость в storage-классе и экспайр-воркере (в отличие от онлайн-статусов).

**Проверка членства в чате (`ChatMembershipFilter`, Scoped):** обе стороны защищены через `MessagesServerApi.CheckChatMembership` (батч-проверка, fail-closed при ошибке gRPC):
- **Приём** (`SubscribeToTyping` / `ChangeChatsInTypingSubscription`): список `chat_ids` фильтруется — в реестр попадают только чаты, где пользователь состоит. Закрывает утечку «кто печатает» от посторонних.
- **Отправка** (`SetTypingStatus`): не-участник не может инжектить набор в чужой чат — heartbeat отбрасывается (`typing_heartbeats_rejected_by_membership`).

Стоимость: проверка на приём — раз на открытие чата (дёшево); проверка на отправку — gRPC-вызов на каждый heartbeat (только пока пользователь печатает, индексированный `AnyAsync` по `(ChatId, UserId)`). На typing **не** распространяется `OnlineVisibility` — видимость набора определяется участием в чате, а не настройкой онлайн-приватности.

**Расширенный ответ фильтра (этап 4.1).** `GetMemberChatIdsAsync` возвращает `ChatMembershipResult`:

| Поле | Что несёт |
|------|-----------|
| `MemberChatIds` | как раньше — подмножество чатов, где пользователь состоит (вся фильтрация и fail-closed не изменились) |
| `RequesterUuid` | UUID запрашивающего из `ChatMembers` (пусто, если его нет) — нужен typing-мосту: через границу ноды уходит uuid, а не `long userId` |
| `FederatedChats` | `chat_id → remote-участники (uuid + server_name)`, только для активных fed-чатов; для локальных пуст |

Федеративный контекст здесь только «протянут»: маршрутизацию по нему делает этап 4.4, поведение локального typing не изменилось. См. [[Backend/Messages]], раздел про `CheckChatMembership`.

### CQRS

`SubscribeToOnlineStatusQueryHandler` — **не** через MediatR, вызывается напрямую из `OnlinerApiService`, зарегистрирован как Scoped.

## База данных

Одна таблица `UsersOnlineStatuses`: `UserId` (PK), `Status` (int, enum), `LastSeen` (timestamptz).
БД — вторичное хранилище (offline last-seen); источник истины о presence — Redis (`onliner:presence`).

## Конфигурация

- `OnlinerDb` — PostgreSQL connection string
- `Redis` — connection string общего presence-стора и распределённого single-runner (**новая зависимость** при масштабировании)
- `RabbitMQ:Host/Username/Password` — fan-out доставка статусов/typing между инстансами
- `UsersService:Host/Token` — gRPC-клиент для проверки `OnlineVisibility`
- `MessagesService:Host/Token` — gRPC-клиент для проверки членства в чате (typing). **Должны быть провижены в Configuration-сервисе** (новая зависимость Onliner → Messages)

## Proto

- `onliner_api.proto` (Server) — 7 методов. Типы: `UserOnlineStatus` (user_id, status, last_seen), `TypingEvent` (chat_id: string, user_id, action), enum `TypingAction` (UNKNOWN/TYPING/CANCELLED).
- `users_api.proto` (Client) — `GetUserPrivacy` для `OnlineVisibility`.
- `messages_api.proto` (Client) — `MessagesServerApi.CheckChatMembership` для typing.
