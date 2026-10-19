# BarkFluff.Onliner — карта проекта

Сервис presence и typing с локальными gRPC subscriptions и RabbitMQ fan-out между инстансами. Пути к компоненту ниже отсчитываются от `Backend/BarkFluff.Onliner/`; `Shared/` и `Tests/` — от корня репозитория.

## Точки входа

- `Program.cs` настраивает gRPC/XAuth, PostgreSQL persistence, общий Redis и MassTransit.
- `Host/OnlinerApiService.cs` — пользовательские RPC; `Host/OnlinerServerApiService.cs` — internal federation RPC.
- `DependencyInjection.cs` регистрирует production `IPresenceStore` как `RedisPresenceStore`, remote cache, managers/notifiers и hosted services. In-memory структуры хранят локальные streams, не являются источником истины статуса.

## Presence и подписки

- `Services/RedisPresenceStore.cs` хранит online users в Redis sorted set `onliner:presence`; обновление и удаление атомарно определяют переход статуса.
- `OnlineStatusSubscriptionsManager` и `TypingSubscriptionsManager` хранят локальные gRPC streams и обратные индексы для fan-out. `OnlineVisibilityFilter` применяет privacy через Users; `ChatMembershipFilter` проверяет typing membership через Messages и закрывает доступ при ошибке.
- `SetOnlineStatus` обновляет presence; `OfflineDetectionService` обрабатывает устаревшие heartbeat; `DatabasePersistenceService` записывает статусы в PostgreSQL.
- `SetTypingStatus` допускает heartbeat только участника чата и публикует fan-out событие; федеративная отправка выполняется best-effort.

## Распределение и федерация

- `Program.cs` создаёт autodelete очереди с `InstanceId.Current`, чтобы каждый инстанс доставлял события своим локальным streams.
- `OnlineStatusChangedConsumer` и `TypingChangedConsumer` рассылают события локально.
- `RedisRemotePresenceStore`, `PresenceInterestReporter` и `FederatedTypingSender` обслуживают удалённые UUID presence/typing; Federation gRPC-клиент регистрируется только при заданном `FederationService:Host`.

Контракты: `Shared/BarkFluff.Proto/onliner_api.proto`, `Shared/BarkFluff.Proto/federation_internal_api.proto`. Настройки — `appsettings.json` и Settings service; тесты — `Tests/BarkFluff.Onliner.Tests`.

← [[Backend/Onliner]]
