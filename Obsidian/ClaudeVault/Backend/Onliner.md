# BarkFluff.Onliner

Трекинг онлайн-статусов пользователей. Порт: **7009** (.NET 10).

Принцип: **in-memory first**. Все статусы в `ConcurrentDictionary`. PostgreSQL обновляется в фоне каждые 10 минут. Real-time оповещения через gRPC server-streaming без RabbitMQ.

Расположение: `Backend/BarkFluff.Onliner/`

📁 **[[Backend/Onliner-ProjectMap]]** — карта всех файлов и классов проекта

## Сборка

```bash
dotnet build BarkFluff.Onliner.csproj
```

Миграции применяются автоматически при старте.

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

### Три синглтона-сервиса

| Класс | Назначение |
|-------|-----------|
| `OnlineStatusStorage` | In-memory кэш `ConcurrentDictionary<long, UserOnlineStatus>`. `UserOnlineStatus` — иммутабельный `record`; `UpdateStatus`/`SetOffline` обновляют запись через CAS (`TryGetValue` + `TryUpdate`/`TryAdd`), не мутируя существующий объект |
| `OnlineStatusSubscriptionsManager` | Реестр gRPC-стримов по userId и subscriptionId. Дополнительно держит обратный индекс `trackedUserId → (connectionId → Stream)`, благодаря чему `GetStreamsTrackingUser` работает за O(1) |
| `OnlineStatusNotifier` | Рассылает изменения по стримам, отслеживающим пользователя |

### Приватность онлайн-статуса

`Services/OnlineVisibilityFilter` (Scoped) запрашивает у [[Backend/Users]] `UsersServerApi.GetUserPrivacy`. Пропускает только `OnlineVisibility.All` или самого вызывающего. `OnlineVisibility.Friends` трактуется как `None` (сервис отношений ещё не реализован). При ошибке gRPC-вызова — **fail-closed**: статус считается скрытым. Применяется в `GetOnlineStatusQueryHandler`, `SubscribeToOnlineStatusQueryHandler`, `ChangeUsersInSubscriptionCommandHandler`.

### Фоновые сервисы

- **OfflineDetectionService**: каждую секунду — пользователи без активности >5 сек → Offline. Инкрементирует `status_changes.offline` через `MetricsCollector`
- **DatabasePersistenceService**: каждые 10 минут — сохранение всех статусов в PostgreSQL. Update существующих записей идёт через `Entry().CurrentValues.SetValues()` (свойства `UserOnlineStatus` — `init`-only)

### Метрики

- `active_subscriptions` — `Increment` при подключении SubscribeToOnlineStatus, `Add(-1)` в `finally` при отключении (декремент через `Add` за неимением `Decrement` в `MetricsCollector`)
- `status_changes.online` — инкрементируется в `SetOnlineStatusCommandHandler` только при реальном переходе Offline/Unknown → Online
- `status_changes.offline` — инкрементируется в `OfflineDetectionService` только при реальном переходе Online → Offline

### MassTransit Consumer

- **SessionRevokedConsumer**: слушает `SessionRevokedEvent`, добавляет токен в `TokenRevocationCache` (XAuth invalidation)

### gRPC-методы (все `TokenType.User`)

| Метод | Тип | Описание |
|-------|-----|---------|
| `SetOnlineStatus` | Unary | Heartbeat клиента |
| `GetOnlineStatus` | Unary | Статусы для списка user_ids |
| `SubscribeToOnlineStatus` | Server Stream | Подписка на изменения; соединение до отмены |
| `ChangeUsersInSubscription` | Unary | Обновить список отслеживаемых без переоткрытия потока |

### CQRS

`SubscribeToOnlineStatusQueryHandler` — **не** через MediatR, вызывается напрямую из `OnlinerApiService`, зарегистрирован как Scoped.

## База данных

Одна таблица `UsersOnlineStatuses`: `UserId` (PK), `Status` (int, enum), `LastSeen` (timestamptz).
БД — вторичное хранилище; источник истины — in-memory.

## Конфигурация

- `OnlinerDb` — PostgreSQL connection string
- `UsersService:Host/Token` — gRPC-клиент для проверки `OnlineVisibility`

## Proto

`onliner_api.proto` — 4 метода, тип `UserOnlineStatus` (user_id, status, last_seen).
