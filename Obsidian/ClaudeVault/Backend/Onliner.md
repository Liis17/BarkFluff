# BarkFluff.Onliner

Onliner отвечает за presence и typing. Текущие статусы хранятся в общем Redis, PostgreSQL сохраняет состояние, а RabbitMQ разносит изменения по локальным gRPC-подпискам. Пути к коду ниже указаны относительно `Backend/BarkFluff.Onliner/`; `Shared/` и `Tests/` — от корня репозитория.

## API и приватность

`Shared/BarkFluff.Proto/onliner_api.proto` задаёт пользовательские RPC для установки/чтения/подписки на online status, изменения списка отслеживаемых пользователей, typing и подписок на чаты. `Shared/BarkFluff.Proto/federation_internal_api.proto` добавляет внутренние операции удалённого presence/typing.

`SetOnlineStatus` записывает heartbeat через `RedisPresenceStore`. `OfflineDetectionService` снимает устаревшие статусы, используя распределённый single-runner; `DatabasePersistenceService` сохраняет снимки в PostgreSQL. `OnlineVisibilityFilter` получает privacy settings из Users. Значение `FRIENDS` трактуется как скрытый статус, потому что в Onliner нет графа отношений; при недоступности Users фильтр закрывает доступ.

Typing временный: `SetTypingStatus` проверяет членство через Messages (`ChatMembershipFilter`, fail-closed), публикует событие локальным подписчикам и best-effort передаёт его федерации. Неизвестное действие считается typing. Локальная доставка не зависит от доступности федерации.

## Несколько инстансов и федерация

Общий Redis sorted set `onliner:presence` хранит пользователя по времени heartbeat; `ZADD`/`ZREM` определяют единственный переход статуса между инстансами. `RedisRemotePresenceStore` кэширует удалённые UUID-статусы с TTL. Менеджеры подписок находятся в памяти, поскольку каждый gRPC-stream принадлежит одному инстансу; очереди RabbitMQ с `InstanceId.Current` разносят событие всем живым инстансам.

Internal `GetLocalPresence` применяет visibility на origin до передачи статуса Federation. `PresenceInterestReporter` периодически отправляет полный список отслеживаемых удалённых UUID; Federation объединяет интерес активных инстансов. Исходящий typing передаётся без долговременной очереди и повторов.

## Источники

- Регистрация и lifetimes: `Program.cs`, `DependencyInjection.cs`
- Redis и стримы: `Services/RedisPresenceStore.cs`, `Services/RedisRemotePresenceStore.cs`, `Services/OnlineStatusSubscriptionsManager.cs`, `Services/TypingSubscriptionsManager.cs`
- Приватность/членство: `Services/OnlineVisibilityFilter.cs`, `Services/ChatMembershipFilter.cs`
- Таймауты/федерация: `BackgroundServices/OfflineDetectionService.cs`, `BackgroundServices/DatabasePersistenceService.cs`, `BackgroundServices/PresenceInterestReporter.cs`, `Services/FederatedTypingSender.cs`

Настройки находятся в `ServiceId.Onliner`; `FederationService:Host/Token` необязательны. Метрики: [[Backend/Onliner-Metrics]]. Связанные заметки: [[Backend/Users]], [[Backend/Messages]], [[Backend/Federation]].
