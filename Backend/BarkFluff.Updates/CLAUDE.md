# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Микросервис Updates

Сервис реального времени: доставляет события клиентам через gRPC server-side streaming. Не хранит состояние в БД — всё в памяти (ConcurrentDictionary).

Порт: **7015**

## Сборка и запуск

```bash
# Сборка
dotnet build BarkFluff.Updates.csproj

# Локальный запуск (требует Configuration service на 7003 и RabbitMQ)
dotnet run

# Docker (из корня Backend/)
docker-compose -f docker-compose-dev.yml up -d updates
```

Тестов нет.

## Архитектура

### Потоки событий

Два независимых потока, реализованных по одному паттерну:

| RabbitMQ очередь | Consumer | MediatR Notification | Handler | gRPC метод |
|---|---|---|---|---|
| `new-messages-updates-handler` | `NewMessageConsumer` | `NewMessageNotification` | `NewMessageNotificationHandler` | `SubscribeNewMessages` |
| `read-receipts-updates-handler` | `ReadByConsumer` | `ReadByNotification` | `ReadByNotificationHandler` | `SubscribeMessagesRead` |

### Схема прохождения события

```
Messages service → RabbitMQ → Consumer → IMediator.Publish() → Handler → StreamSubscriptionsManager → gRPC stream write
```

### StreamSubscriptionsManager (Singleton)

Два отдельных менеджера (по одному на тип потока) хранят активные gRPC-стримы:

```csharp
ConcurrentDictionary<long userId, ConcurrentDictionary<Guid subscriptionId, IServerStreamWriter<T>>>
```

Поддерживает несколько подключений одного пользователя (разные устройства). При разрыве соединения `UpdatesApiService` удаляет подписку через `RemoveSubscription`.

### Push-уведомления (отложенная отправка)

`PushNotificationSchedulerHandler` — обработчик `NewMessageNotification`, который:
1. Планирует push через 5 секунд (`Task.Delay(5s, cts.Token)`)
2. Если за 5 секунд приходит `ReadByNotification`, `ReadByCancelPushHandler` вызывает `PendingPushTracker.CancelPending()` — push отменяется
3. Если не отменён — публикует `PushNotificationEvent` в RabbitMQ (забирает `CloudMessaging`)

`PendingPushTracker` (Singleton) хранит `CancellationTokenSource` по ключу `(MessageId, UserId)`.

### gRPC API

```protobuf
// updates_api.proto
rpc SubscribeNewMessages(SubscribeNewMessagesRequest) returns (stream NewMessageEvent)
rpc SubscribeMessagesRead(SubscribeMessagesReadRequest) returns (stream MessageReadEvent)
```

Оба метода: регистрируют подписку → `await Task.Delay(Infinite, context.CancellationToken)` → при отключении удаляют подписку.

Защита: `[Authorize(Policy = nameof(TokenType.User))]` (XAuth).

## Добавление нового типа событий

1. Добавить RabbitMQ-событие в `Shared/BarkFluff.Shared.Queue/`
2. Добавить Consumer в `Consumers/` (реализует `IConsumer<TEvent>`)
3. Добавить `INotification` в `Features/{FeatureName}/`
4. Добавить `INotificationHandler` в `Features/{FeatureName}/Handlers/`
5. Добавить `StreamSubscriptionsManager` (если нужен новый тип стрима) в `Features/{FeatureName}/`
6. Зарегистрировать Consumer в `Program.cs` (два места: `x.AddConsumer<>` и `cfg.ReceiveEndpoint`)
7. Добавить метод в `UpdatesApiService` и в `updates_api.proto`
