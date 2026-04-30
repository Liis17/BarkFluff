# BarkFluff.Onliner — Карта проекта

Подробная карта всех файлов и классов сервиса онлайн-статусов.
← Назад к [[Backend/Onliner]]

---

## Точка входа

| Файл | Класс | Описание |
|------|-------|---------|
| `Program.cs` | `Program` | Настройка хоста: gRPC, EF Core, XAuth, MassTransit, gRPC-клиент UsersServerApi, регистрация сервисов |

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

---

## Services (Singleton / Scoped)

| Файл | Класс | Lifetime | Описание |
|------|-------|----------|---------|
| `Services/OnlineStatusStorage.cs` | `OnlineStatusStorage` | Singleton | In-memory `ConcurrentDictionary<long, UserOnlineStatus>`. Методы: `UpdateStatus`, `SetOffline`, `GetAll`, `GetStatus`. Источник истины |
| `Services/OnlineStatusSubscriptionsManager.cs` | `OnlineStatusSubscriptionsManager` | Singleton | Реестр подписок: `ConcurrentDictionary<long (subscriberId), ConcurrentDictionary<Guid (connectionId), SubscriptionData>>`. Методы: `RegisterSubscription`, `RemoveSubscription`, `GetStreamsTrackingUser`, `UpdateTrackedUsers` |
| `Services/OnlineStatusNotifier.cs` | `OnlineStatusNotifier` | Singleton | Рассылает изменения статусов по зарегистрированным стримам параллельно через `Task.WhenAll` |
| `Services/OnlineVisibilityFilter.cs` | `OnlineVisibilityFilter` | Scoped | Фильтрует userId по настройке `OnlineVisibility` через gRPC-клиент `UsersServerApi`. `FRIENDS` трактуется как `NONE` (до появления сервиса отношений). Всегда пропускает самого вызывающего |

---

## Background Services

| Файл | Класс | Описание |
|------|-------|---------|
| `BackgroundServices/OfflineDetectionService.cs` | `OfflineDetectionService` | Каждую **1 секунду** проверяет пользователей: `LastSeen > 5 сек` → переводит в `Offline`, уведомляет через `OnlineStatusNotifier`. Использует `MetricsCollector` |
| `BackgroundServices/DatabasePersistenceService.cs` | `DatabasePersistenceService` | Каждые **10 минут** сохраняет все статусы из in-memory в PostgreSQL через `OnlineStatusContext` |

---

## Consumers (MassTransit / RabbitMQ)

| Файл | Класс | Событие | Описание |
|------|-------|---------|---------|
| `Consumers/SessionRevokedConsumer.cs` | `SessionRevokedConsumer` | `SessionRevokedEvent` | При отзыве сессии добавляет токен в `TokenRevocationCache` (XAuth) |

---

## Domain

| Файл | Класс | Описание |
|------|-------|---------|
| `Domain/Entities/UserOnlineStatus.cs` | `UserOnlineStatus` | Entity: `UserId` (long), `Status` (StatusTypeId), `LastSeen` (DateTime) |
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
| `Dockerfile` | — | Образ для продакшена |
| `Dockerfile.slim` | — | Облегчённый образ |

---

## Proto (shared)

| Файл | Описание |
|------|---------|
| `Shared/BarkFluff.Proto/onliner_api.proto` | 4 метода: `SetOnlineStatus`, `GetOnlineStatus`, `SubscribeToOnlineStatus`, `ChangeUsersInSubscription`. Тип `UserOnlineStatus` |
| `Shared/BarkFluff.Proto/shared.proto` | Общие типы платформы |
| `Shared/BarkFluff.Proto/users_api.proto` | Используется для вызова `UsersServerApi` (проверка `OnlineVisibility`) |
