# BarkFluff.Onliner — Карта проекта

Подробная карта всех файлов и классов сервиса онлайн-статусов.
← Назад к [[Backend/Onliner]]

---

## Точка входа

| Файл | Класс | Описание |
|------|-------|---------|
| `Program.cs` | `Program` | Настройка хоста: gRPC, EF Core, XAuth, MassTransit, gRPC-клиенты UsersServerApi + MessagesServerApi (typing), регистрация сервисов |

---

## Host

| Файл | Класс | Описание |
|------|-------|---------|
| `Host/OnlinerApiService.cs` | `OnlinerApiService` | gRPC-сервис, реализует `OnlinerApiBase`. Все методы требуют `TokenType.User`. Использует MediatR для unary-методов и прямой вызов `SubscribeToOnlineStatusQueryHandler` для стриминга. Инъектирует `MetricsCollector` |

---

## Features (CQRS)

### SetOnlineStatus
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/SetOnlineStatus/SetOnlineStatusCommand.cs` | `SetOnlineStatusCommand` | Команда heartbeat (без параметров — userId берётся из gRPC-контекста) |
| `Features/SetOnlineStatus/SetOnlineStatusCommandHandler.cs` | `SetOnlineStatusCommandHandler` | Обновляет статус в `OnlineStatusStorage`, при изменении вызывает `OnlineStatusNotifier` |

### GetOnlineStatus
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/GetOnlineStatus/GetOnlineStatusQuery.cs` | `GetOnlineStatusQuery` | Запрос статусов для списка `UserIds` |
| `Features/GetOnlineStatus/GetOnlineStatusQueryHandler.cs` | `GetOnlineStatusQueryHandler` | Читает из `OnlineStatusStorage`, фильтрует через `OnlineVisibilityFilter` |

### SubscribeToOnlineStatus
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/SubscribeToOnlineStatus/SubscribeToOnlineStatusQuery.cs` | `SubscribeToOnlineStatusQuery` | Параметры стрим-подписки: `UserIds`, `ResponseStream`, `CancellationToken` |
| `Features/SubscribeToOnlineStatus/SubscribeToOnlineStatusQueryHandler.cs` | `SubscribeToOnlineStatusQueryHandler` | **Scoped, не через MediatR**. Регистрирует подписку в `OnlineStatusSubscriptionsManager`, блокирует до отмены, удаляет при отключении. Применяет `OnlineVisibilityFilter` |

### ChangeUsersInSubscription
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/ChangeUsersInSubscription/ChangeUsersInSubscriptionCommand.cs` | `ChangeUsersInSubscriptionCommand` | Команда обновления списка отслеживаемых userId без переоткрытия стрима |
| `Features/ChangeUsersInSubscription/ChangeUsersInSubscriptionCommandHandler.cs` | `ChangeUsersInSubscriptionCommandHandler` | Обновляет `TrackedUserIds` в `OnlineStatusSubscriptionsManager`. Применяет `OnlineVisibilityFilter` |

### SetTypingStatus
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/SetTypingStatus/SetTypingStatusCommand.cs` | `SetTypingStatusCommand` | Команда heartbeat набора: `ChatId` (string), `Action` (userId из gRPC-контекста) |
| `Features/SetTypingStatus/SetTypingStatusCommandHandler.cs` | `SetTypingStatusCommandHandler` | Проверяет членство отправителя (`ChatMembershipFilter`); не-участник → отброс. UNKNOWN-action → TYPING, ретранслирует через `TypingNotifier`. Без БД |

### SubscribeToTyping
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/SubscribeToTyping/SubscribeToTypingQuery.cs` | `SubscribeToTypingQuery` | Параметры стрим-подписки: `ChatIds` (List<string>), `ResponseStream`, `CancellationToken` |
| `Features/SubscribeToTyping/SubscribeToTypingQueryHandler.cs` | `SubscribeToTypingQueryHandler` | **Scoped, не через MediatR**. Фильтрует `chat_ids` через `ChatMembershipFilter`, регистрирует подписку в `TypingSubscriptionsManager`, блокирует до отмены, чистит при отключении |

### ChangeChatsInTypingSubscription
| Файл | Класс | Описание |
|------|-------|---------|
| `Features/ChangeChatsInTypingSubscription/ChangeChatsInTypingSubscriptionCommand.cs` | `ChangeChatsInTypingSubscriptionCommand` | Команда обновления списка отслеживаемых chatId (List<string>) без переоткрытия стрима |
| `Features/ChangeChatsInTypingSubscription/ChangeChatsInTypingSubscriptionCommandHandler.cs` | `ChangeChatsInTypingSubscriptionCommandHandler` | Фильтрует по членству (`ChatMembershipFilter`), обновляет `TrackedChatIds` в `TypingSubscriptionsManager`. `FailedPrecondition` если нет активных подписок |

---

## Services (Singleton / Scoped)

| Файл | Класс | Lifetime | Описание |
|------|-------|----------|---------|
| `Services/OnlineStatusStorage.cs` | `OnlineStatusStorage` | Singleton | In-memory `ConcurrentDictionary<long, UserOnlineStatus>` (значения — иммутабельные `record`). Обновление через CAS-цикл `TryGetValue` + `TryUpdate`/`TryAdd`. Методы: `UpdateStatus`, `SetOffline`, `GetAllStatuses`, `GetStatus`, `GetOnlineUsersOlderThan`. Источник истины |
| `Services/OnlineStatusSubscriptionsManager.cs` | `OnlineStatusSubscriptionsManager` | Singleton | Прямой индекс `subscriberId → (connectionId → SubscriptionData)` + обратный индекс `trackedUserId → (connectionId → Stream)` для O(1) выборки в `GetStreamsTrackingUser`. Методы: `RegisterSubscription`, `RemoveSubscription`, `GetStreamsTrackingUser`, `UpdateAllSubscriptions` |
| `Services/OnlineStatusNotifier.cs` | `OnlineStatusNotifier` | Singleton | Рассылает изменения статусов по зарегистрированным стримам параллельно через `Task.WhenAll` |
| `Services/OnlineVisibilityFilter.cs` | `OnlineVisibilityFilter` | Scoped | Фильтрует userId по настройке `OnlineVisibility` через gRPC-клиент `UsersServerApi`. `FRIENDS` трактуется как `NONE` (до появления сервиса отношений). Всегда пропускает самого вызывающего |
| `Services/TypingSubscriptionsManager.cs` | `TypingSubscriptionsManager` | Singleton | Реестр typing-стримов по `chatId`. Прямой индекс `subscriberId → (connectionId → SubscriptionData)` + обратный `chatId → (connectionId → ReverseEntry{Stream, SubscriberId})`. `GetStreamsTrackingChat(chatId, exceptSubscriberId)` исключает печатающего. Методы: `RegisterSubscription`, `RemoveSubscription`, `UpdateAllSubscriptions` |
| `Services/TypingNotifier.cs` | `TypingNotifier` | Singleton | Ретранслирует `TypingEvent` подписчикам чата параллельно через `Task.WhenAll` |
| `Services/ChatMembershipFilter.cs` | `ChatMembershipFilter` | Scoped | `GetMemberChatIdsAsync` — батч-проверка членства через gRPC-клиент `MessagesServerApi.CheckChatMembership`. Fail-closed при ошибке |

---

## Background Services

| Файл | Класс | Описание |
|------|-------|---------|
| `BackgroundServices/OfflineDetectionService.cs` | `OfflineDetectionService` | Каждую **1 секунду** проверяет пользователей: `LastSeen > 5 сек` → переводит в `Offline`, уведомляет через `OnlineStatusNotifier`. Использует `MetricsCollector` |
| `BackgroundServices/DatabasePersistenceService.cs` | `DatabasePersistenceService` | Каждые **10 минут** сохраняет все статусы из in-memory в PostgreSQL через `OnlineStatusContext` |
| `BackgroundServices/MetricsSnapshotService.cs` | `MetricsSnapshotService` | Каждые **2 секунды** снимает gauge-показатели (`active_subscriptions`, `tracked_unique_users`, `online_users_count`, `storage_total_count`) в `MetricsCollector.Set`. Без него gauge-метрики обнулялись бы каждые 5 сек в `SnapshotAndReset` |

---

## Consumers (MassTransit / RabbitMQ)

| Файл | Класс | Событие | Описание |
|------|-------|---------|---------|
| `Consumers/SessionRevokedConsumer.cs` | `SessionRevokedConsumer` | `SessionRevokedEvent` | При отзыве сессии добавляет токен в `TokenRevocationCache` (XAuth) |

---

## Domain

| Файл | Класс | Описание |
|------|-------|---------|
| `Domain/Entities/UserOnlineStatus.cs` | `UserOnlineStatus` | Иммутабельный `record class`: `UserId` (long), `Status` (StatusTypeId), `LastSeen` (DateTime). Все свойства `init`-only |
| `Domain/Enums/StatusTypeId.cs` | `StatusTypeId` | `Unknown = 0`, `Online = 1`, `Offline = 2` |

---

## Persistence

| Файл | Класс | Описание |
|------|-------|---------|
| `Persistence/Contexts/OnlineStatusContext.cs` | `OnlineStatusContext` | EF Core DbContext, таблица `UsersOnlineStatuses` |
| `Persistence/Contexts/OnlineStatusContextFactory.cs` | `OnlineStatusContextFactory` | `IDesignTimeDbContextFactory` для миграций |
| `Persistence/Migrations/20260104163833_Initial.cs` | — | Начальная миграция: таблица `UsersOnlineStatuses` |

---

## Конфигурация и инфраструктура

| Файл | Класс | Описание |
|------|-------|---------|
| `DependencyInjection.cs` | `DependencyInjection` | Extension `AddOnlinerServices()`: регистрирует 3 singleton-сервиса, Scoped `OnlineVisibilityFilter`, фоновые сервисы, MediatR |
| `appsettings.json` | — | Базовая конфигурация (порт 7009) |
| `appsettings.Development.json` | — | Локальная конфигурация для разработки |
| `Properties/launchSettings.json` | — | Профили запуска VS/dotnet |
| `Dockerfile.slim` | — | Образ для CI и production |

---

## Proto (shared)

| Файл | Описание |
|------|---------|
| `Shared/BarkFluff.Proto/onliner_api.proto` | 7 методов: `SetOnlineStatus`, `GetOnlineStatus`, `SubscribeToOnlineStatus`, `ChangeUsersInSubscription`, `SetTypingStatus`, `SubscribeToTyping`, `ChangeChatsInTypingSubscription`. Типы `UserOnlineStatus`, `TypingEvent`, enum `TypingAction` |
| `Shared/BarkFluff.Proto/shared.proto` | Общие типы платформы |
| `Shared/BarkFluff.Proto/users_api.proto` | Используется для вызова `UsersServerApi` (проверка `OnlineVisibility`) |
| `Shared/BarkFluff.Proto/messages_api.proto` | Client — вызов `MessagesServerApi.CheckChatMembership` (проверка членства для typing) |
