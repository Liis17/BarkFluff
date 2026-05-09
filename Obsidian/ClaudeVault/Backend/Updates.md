# BarkFluff.Updates

Real-time сервис: доставляет события клиентам через gRPC server-side streaming. **Без БД** — всё в памяти. Порт: **7015**.

📄 Детальная карта файлов: [[Backend/Updates-ProjectMap]]
📊 Реестр метрик: [[Backend/Updates-Metrics]]

Расположение: `Backend/BarkFluff.Updates/`

## Сборка

```bash
dotnet build BarkFluff.Updates.csproj
docker-compose -f docker-compose-dev.yml up -d updates
```

## Архитектура

### Потоки событий

| RabbitMQ | Consumer | MediatR Notification | gRPC метод |
|----------|----------|----------------------|-----------|
| `new-messages-updates-handler` | `NewMessageConsumer` | `NewMessageNotification` | `SubscribeNewMessages` |
| `read-receipts-updates-handler` | `ReadByConsumer` | `ReadByNotification` | `SubscribeMessagesRead` |
| `messages-edited-updates-handler` | `MessageEditedConsumer` | `MessageEditedNotification` | `SubscribeMessagesEdited` |
| `messages-deleted-updates-handler` | `MessageDeletedConsumer` | `MessageDeletedNotification` | `SubscribeMessagesDeleted` |
| `messages-pinned-updates-handler` | `MessagePinnedConsumer` | `MessagePinnedNotification` | `SubscribeMessagesPinned` |
| `messages-unpinned-updates-handler` | `MessageUnpinnedConsumer` | `MessageUnpinnedNotification` | `SubscribeMessagesUnpinned` |
| `all-messages-unpinned-updates-handler` | `AllMessagesUnpinnedConsumer` | `AllMessagesUnpinnedNotification` | `SubscribeAllMessagesUnpinned` |
| `session-revoked-updates` | `SessionRevokedConsumer` | — | — (инвалидация токена через `TokenRevocationCache`) |

### Схема прохождения события

```
Messages service → RabbitMQ → Consumer → IMediator.Publish() → Handler → StreamSubscriptionsManager → gRPC stream write
```

### StreamSubscriptionsManager (Singleton)

```csharp
ConcurrentDictionary<long userId, ConcurrentDictionary<Guid subscriptionId, IServerStreamWriter<T>>>
```

Поддерживает несколько подключений одного пользователя (разные устройства). При разрыве `UpdatesApiService` удаляет подписку через `RemoveSubscription`.

### Push-уведомления (отложенная отправка)

`PushNotificationSchedulerHandler` обрабатывает `NewMessageNotification`:
1. Планирует push через 5 секунд (`Task.Delay(5s, cts.Token)`)
2. Если приходит `ReadByNotification` — `ReadByCancelPushHandler` → `PendingPushTracker.CancelPending()` → push отменяется
3. Иначе публикует `PushNotificationEvent` в RabbitMQ (забирает [[Backend/CloudMessaging]])

`PendingPushTracker` (Singleton) хранит `CancellationTokenSource` по ключу `(MessageId, UserId)`.

### Dismiss push после прочтения

`DismissPushPublisher` (`INotificationHandler<ReadByNotification>`) запускается параллельно с `ReadByCancelPushHandler` и для каждого `userId` из `NewReadBy` публикует `DismissPushEvent { ChatId, UserId }` в RabbitMQ. Его потребляет [[Backend/CloudMessaging]] и шлёт data-only FCM `type=dismiss_chat_notifications` — клиент скрывает нотификацию чата на остальных устройствах. Если push в 5-сек окне ещё не ушёл, `ReadByCancelPushHandler` его отменит, а dismiss станет no-op на клиенте.

### gRPC API

```protobuf
rpc SubscribeNewMessages(SubscribeNewMessagesRequest) returns (stream NewMessageEvent)
rpc SubscribeMessagesRead(SubscribeMessagesReadRequest) returns (stream MessageReadEvent)
rpc SubscribeMessagesEdited(SubscribeMessagesEditedRequest) returns (stream MessageEditedEvent)
rpc SubscribeMessagesDeleted(SubscribeMessagesDeletedRequest) returns (stream MessageDeletedEvent)
rpc SubscribeMessagesPinned(SubscribeMessagesPinnedRequest) returns (stream MessagePinnedEvent)
rpc SubscribeMessagesUnpinned(SubscribeMessagesUnpinnedRequest) returns (stream MessageUnpinnedEvent)
rpc SubscribeAllMessagesUnpinned(SubscribeAllMessagesUnpinnedRequest) returns (stream AllMessagesUnpinnedEvent)
```

Оба метода: регистрируют подписку → `await Task.Delay(Infinite, context.CancellationToken)` → при отключении удаляют подписку.
Защита: `[Authorize(Policy = nameof(TokenType.User))]`.

## Добавление нового типа событий

1. Добавить RabbitMQ-событие в [[Shared/Queue]]
2. Добавить Consumer в `Consumers/`
3. Добавить `INotification` в `Features/{FeatureName}/`
4. Добавить `INotificationHandler` в `Features/{FeatureName}/Handlers/`
5. Добавить `StreamSubscriptionsManager` (если нужен новый стрим)
6. Зарегистрировать Consumer в `Program.cs` (два места: `x.AddConsumer<>` и `cfg.ReceiveEndpoint`)
7. Добавить метод в `UpdatesApiService` и `updates_api.proto`
