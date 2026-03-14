# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Service Overview

**BarkFluff.Onliner** — микросервис отслеживания онлайн-статусов пользователей, порт **7009** (.NET 10).

Основной принцип: **in-memory first**. Все статусы хранятся в `ConcurrentDictionary` в `OnlineStatusStorage` (синглтон). PostgreSQL обновляется в фоне каждые 10 минут через `DatabasePersistenceService`. Реалтайм-оповещения через gRPC server-streaming без RabbitMQ.

## Build

```bash
dotnet build BarkFluff.Onliner.csproj
```

Миграции применяются автоматически при старте через `Program.cs`.

## Architecture

### Поток данных при смене статуса

```
Client → SetOnlineStatus (gRPC)
  → OnlinerApiService
  → MediatR: SetOnlineStatusCommand
  → SetOnlineStatusCommandHandler
      → OnlineStatusStorage.UpdateStatus()     # обновить in-memory
      → OnlineStatusNotifier.NotifyStatusChanged()  # если статус изменился
          → SubscriptionsManager.GetStreamsTrackingUser()  # найти подписчиков
          → IServerStreamWriter.WriteAsync()   # отправить в каждый поток
```

### Три синглтона-сервиса

| Класс | Назначение |
|-------|-----------|
| `OnlineStatusStorage` | In-memory кэш `ConcurrentDictionary<long, UserOnlineStatus>` |
| `OnlineStatusSubscriptionsManager` | Реестр gRPC-стримов: `ConcurrentDictionary<long, ConcurrentDictionary<Guid, SubscriptionData>>` |
| `OnlineStatusNotifier` | Рассылает изменения статусов по всем стримам, отслеживающим пользователя |

### Фоновые сервисы

- **`OfflineDetectionService`**: каждую секунду ищет пользователей без активности >5 секунд → устанавливает Offline
- **`DatabasePersistenceService`**: каждые 10 минут сохраняет все статусы из памяти в PostgreSQL

### gRPC методы (все требуют `TokenType.User`)

| Метод | Тип | Описание |
|-------|-----|---------|
| `SetOnlineStatus` | Unary | Клиент сообщает о себе как онлайн (heartbeat) |
| `GetOnlineStatus` | Unary | Получить статусы для списка user_ids (память → БД → Unknown) |
| `SubscribeToOnlineStatus` | Server Stream | Подписка на изменения статусов; соединение держится до отмены |
| `ChangeUsersInSubscription` | Unary | Обновить список отслеживаемых пользователей в активной подписке |

### CQRS (MediatR)

Хендлеры в `Features/` для: `SetOnlineStatus`, `GetOnlineStatus`, `ChangeUsersInSubscription`.
`SubscribeToOnlineStatusQueryHandler` — **не** через MediatR, вызывается напрямую из `OnlinerApiService`, зарегистрирован как Scoped.

## Конфигурация

Ключи конфигурации (получаются из Configuration service при старте):
- `OnlinerDb` — строка подключения к PostgreSQL

## База данных

Одна таблица `UsersOnlineStatuses`: `UserId` (PK, bigint), `Status` (int, enum), `LastSeen` (timestamptz).
БД — вторичное хранилище; источник истины — in-memory `OnlineStatusStorage`.

## Proto

`Shared/BarkFluff.Proto/onliner_api.proto` — 4 метода сервиса, тип `UserOnlineStatus` с полями `user_id`, `status`, `last_seen`.
