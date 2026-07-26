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

> Этап 0.4 rearch: в `onliner_api.proto` добавлены параллельные `user_uuid`/`user_uuids`-поля (Subscribe/Change/Status/Typing) для remote-пользователей + сервис `OnlinerServerApi`.
> **Этап 4.2 реализовал их** (`UpsertRemoteStatus`, `InjectRemoteTyping`, новый `GetLocalPresence`), добавил `user_uuids` в `GetOnlineStatusRequest` и uuid-ветку подписок — см. раздел «uuid-ветка presence и мост федерации» ниже.

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
| `IRemotePresenceStore` / `RedisRemotePresenceStore` | Кеш статусов remote-пользователей (этап 4.2): отдельные ключи `onliner:presence:remote:{uuid}` с TTL. Намеренно **не** в sorted set — его гасит `OfflineDetectionService`, а у remote heartbeat'ов нет |
| `OnlineStatusSubscriptionsManager` | Реестр gRPC-стримов по userId и subscriptionId. Дополнительно держит обратный индекс `trackedUserId → (connectionId → Stream)` и (с 4.2) параллельный `trackedUuid → …`, благодаря чему `GetStreamsTrackingUser`/`GetStreamsTrackingUuid` работают за O(1) |
| `OnlineStatusNotifier` | Рассылает изменения по стримам, отслеживающим пользователя (`NotifyStatusChanged`) или remote-uuid (`NotifyRemoteStatusChanged`, 4.2) |
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
- **Федерация (4.2/4.4):** `remote_status_upserts`, `remote_typing_injections`, `remote_snapshot_errors`, `presence_interest_reports`, `presence_interest_errors`, `federated_typing_sent`, `federated_typing_errors`, gauge `remote_tracked_uuids`
- **Прочее:** `sessions_revoked`, gauge `service_started_unix`

### MassTransit Consumer

- **SessionRevokedConsumer**: слушает `SessionRevokedEvent`, добавляет токен в `TokenRevocationCache` (XAuth invalidation)

### gRPC-методы (все `TokenType.User`)

| Метод | Тип | Описание |
|-------|-----|---------|
| `SetOnlineStatus` | Unary | Heartbeat клиента |
| `GetOnlineStatus` | Unary | Статусы для списка user_ids и (с 4.2) user_uuids |
| `SubscribeToOnlineStatus` | Server Stream | Подписка на изменения; соединение до отмены. С 4.2 принимает `user_uuids` и сразу отдаёт по ним начальный снимок |
| `ChangeUsersInSubscription` | Unary | Обновить список отслеживаемых (user_ids + user_uuids) без переоткрытия потока |
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

## uuid-ветка presence и мост федерации (этап 4.2, docs/rearch/phase-4/step-4.2-onliner-uuid-branch.md)

Onliner начинает работать с remote-пользователями, у которых нет `long userId`. Сетевого федеративного кода здесь нет — Onliner общается только со своей нодой, S2S-мост живёт в [[Backend/Federation]] (этапы 4.3/4.4).

### Кеш remote-статусов (`IRemotePresenceStore` / `RedisRemotePresenceStore`)

Ключ на пользователя `onliner:presence:remote:{uuid}`, значение `"{status}:{lastSeenUnixMs}"`, TTL `Onliner:RemotePresenceTtlSeconds` (дефолт 900) продлевается каждым обновлением.

**Почему отдельный ключ, а не `onliner:presence`:** sorted set обслуживает `OfflineDetectionService`, гасящий записи через 5 секунд без heartbeat. У remote-пользователей heartbeat'ов нет — истина на чужой ноде, они уходили бы в offline мгновенно. TTL-ключи заодно дают эвикцию uuid без подписчиков **без фонового сервиса** (И-7 из ревью плана).

**Изоляция от персиста жёсткая:** `DatabasePersistenceService` и `OfflineDetectionService` работают через `IPresenceStore` (sorted set) и remote-ключей не видят вовсе; `UsersOnlineStatuses` (PK `long UserId`) не расширяется. Remote-статусы не персистятся by design.

**Отсутствие ключа = `STATUS_TYPE_ID_UNKNOWN`, а не `OFFLINE`** — «нода-партнёр статус не отдаёт» и «человек не в сети» это разные состояния.

### Подписки по uuid

`OnlineStatusSubscriptionsManager` получил параллельный обратный индекс `trackedUuid → (connectionId → Stream)` рядом с существующим по `userId` (та же структура и потокобезопасность):

| Метод | Назначение |
|-------|-----------|
| `GetStreamsTrackingUuid(Guid)` | пара к `GetStreamsTrackingUser` |
| `GetTrackedUuids()` | снимок интереса этого инстанса — для `PresenceInterestReporter` |
| `RegisterSubscription` / `UpdateAllSubscriptions` | принимают список uuid; снятие подписки чистит **оба** индекса |

Клиентские RPC `SubscribeToOnlineStatus` / `ChangeUsersInSubscription` / `GetOnlineStatus` принимают `user_uuids` параллельно с `user_ids` (не `oneof` — наборы объединяются). Невалидный uuid молча отбрасывается (не валит вызов), лимит `PresenceUuids.MaxSubscriptionUuids = 500` → `InvalidArgument`; исторического лимита на `user_ids` нет и он не вводится.

**Privacy к remote не применяется.** `OnlineVisibilityFilter` работает только по локальным `user_ids`: origin-нода уже отфильтровала своё (инвариант №27), повторная фильтрация невозможна (их настроек у нас нет) и не нужна.

**Начальный снимок.** Подписка с `user_uuids` сразу получает в стрим текущее содержимое кеша (или `UNKNOWN` по неизвестным uuid) — иначе клиент не узнал бы состояние до первого изменения на чужой ноде. Для локальных снимок по-прежнему берётся отдельным `GetOnlineStatus`.

### `OnlinerServerApi` (`TokenType.Service`, зовёт только Federation своей ноды)

| RPC | Что делает |
|-----|-----------|
| `UpsertRemoteStatus(user_uuid, status, last_seen)` | пишет в кеш → публикует `OnlineStatusChangedEvent` (fan-out) → консюмер каждого инстанса рассылает `UserOnlineStatus` стримам, следящим за uuid. Вливает статусы [[Backend/Federation]] (`PresenceStreamManager`, этап 4.3) |
| `InjectRemoteTyping(chat_id, user_uuid, action)` | публикует `TypingChangedEvent` (fan-out) → `TypingNotifier` ретранслирует `TypingEvent` подписчикам чата |
| `GetLocalPresence(user_ids[])` | статусы **наших** пользователей для отдачи ноде-партнёру; **privacy применяется здесь** |

`GetLocalPresence` — ключевое место инварианта №27: правило «`OnlineVisibility != All` ⇒ скрыт» (`Friends` === `None` до сервиса отношений) живёт в одном месте, у владельца данных. Скрытому пользователю отдаётся `UNKNOWN`, а не настоящий статус, поэтому Federation не заводит копию privacy-логики и не может её обойти. Сбой Users → `UNKNOWN` (fail-closed, как в существующем фильтре).

Право вызывающего в этих RPC **не** проверяется: вызывает Federation своей ноды с service-токеном, а origin/членство проверяются до вызова (этапы 4.3/4.4).

### Расширение fan-out событий

`OnlineStatusChangedEvent` и `TypingChangedEvent` получили `Guid? UserUuid`. Заполнен только для remote; локальные пути не меняются (`null`). Консюмеры ветвятся по этому полю:

- `OnlineStatusChangedConsumer` → рассылка через uuid-индекс, `UserOnlineStatus.user_uuid` заполнен, `user_id = 0`;
- `TypingChangedConsumer` → `TypingEvent` с `user_uuid` и **без** фильтра «кроме отправителя» (`GetAllStreamsTrackingChat`): автор на чужой ноде, среди наших подписчиков его нет.

Событие **без** нового поля (инстанс старой версии — обновляются они не одновременно) идёт прежним путём; это закреплено тестами.

### Исходящий typing в федерацию (этап 4.4)

`SetTypingStatusCommandHandler` **после** существующей публикации `TypingChangedEvent` берёт федеративный контекст из `ChatMembershipResult` (этап 4.1) и, если чат федеративный, зовёт `FederationInternalApi.DeliverTypingOutbound(chat_id, requester_uuid, action, уникальные ноды peers)` — fire-and-forget, deadline 2с.

- Чат локальный или `RequesterUuid` пуст → выход без работы (подавляющее большинство вызовов).
- `FederatedTypingSender` резолвит клиента Federation через `IServiceProvider.GetService`: на ноде без федерации он не зарегистрирован, и обязательный параметр конструктора уронил бы контейнер.
- Ошибки — debug-лог + метрика `federated_typing_errors`, **не** warning: heartbeat идёт каждые 4–5с, недоступная федерация иначе засорила бы логи. Ретраев нет.
- **Локальный typing не изменился вовсе** — проверка членства, fan-out и «кроме отправителя» те же; недоступность федерации не ломает локальный путь (закреплено тестом).

Дальнейшая механика (coalescing, rate limit, валидация на приёме) — [[Backend/Federation]], раздел «Typing-мост».

### `PresenceInterestReporter` (BackgroundService)

Раз в `Onliner:PresenceInterestIntervalSeconds` (дефолт 20) шлёт `FederationInternalApi.SetPresenceInterest(instance_id, uuids[])` с **полным** набором `GetTrackedUuids()`.

**Почему полный набор, а не дельты:** Onliner масштабируется горизонтально, стримы подписчиков живут на разных инстансах, и свести «+uuid/−uuid» от нескольких инстансов без общего состояния невозможно. Federation объединяет наборы живых инстансов, протухшие выпадают по TTL — рестарт инстанса самолечится, ретраев не нужно.

Пустой набор тоже отправляется: это сигнал «за нами больше никто не следит», по которому Federation закрывает S2S-подписку. `Unimplemented` (до этапа 4.3) — debug-лог, не ошибка; недоступность Federation — warning + метрика, **без ретраев**.

**Гейт:** сервис и gRPC-клиент Federation регистрируются только при заданном `FederationService:Host` — нода без федерации не поднимает их вовсе и не льёт ошибки.

### Конфигурация (бакет `ServiceId.Onliner = 9`)

| Ключ | Дефолт | Смысл |
|------|--------|-------|
| `FederationService:Host` / `Token` | из populator | клиент Federation; ключ живёт в бакете **потребителя**, не вызываемого сервиса |
| `Onliner:RemotePresenceTtlSeconds` | 900 | TTL кеша remote-статусов = эвикция |
| `Onliner:PresenceInterestIntervalSeconds` | 20 | период heartbeat'а интереса |

Миграция — `20260727010000_AddOnlinerFederationPresenceConfiguration`.
