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
| `new-encrypted-messages-updates-handler` | `NewEncryptedMessageConsumer` | `NewEncryptedMessageNotification` | `SubscribePrivateMessages` (user-scope) |
| `encrypted-messages-edited-updates-handler` | `EncryptedMessageEditedConsumer` | `EncryptedMessageEditedNotification` | `SubscribePrivateMessageEdits` (user-scope) |
| `encrypted-messages-deleted-updates-handler` | `EncryptedMessageDeletedConsumer` | `EncryptedMessageDeletedNotification` | `SubscribePrivateMessageDeletes` (user-scope) |
| `private-messages-read-updates-handler` | `PrivateMessagesReadConsumer` | `PrivateMessagesReadNotification` | `SubscribePrivateMessagesRead` (user-scope) |
| `private-chat-invites-updates-handler` | `PrivateChatInviteConsumer` | `PrivateChatInviteNotification` | `SubscribePrivateChatInvites` (user-scope, маршрутизация только на InviteeUserId) |
| `private-chat-invite-resolutions-updates-handler` | `PrivateChatInviteResolutionConsumer` | `PrivateChatInviteResolutionNotification` | `SubscribePrivateChatInviteResolutions` (user-scope, на InviterUserId) |
| `secret-chat-invites-updates-handler` | `SecretChatInviteConsumer` | `SecretChatInviteNotification` | `SubscribeSecretChatInvites` (**device-scope**, на RecipientDeviceId) |
| `secret-chat-invite-resolutions-updates-handler` | `SecretChatInviteResolutionConsumer` | `SecretChatInviteResolutionNotification` | `SubscribeSecretChatResolutions` (**device-scope**, на SenderDeviceId инициатора) |
| `new-secret-messages-updates-handler` | `NewSecretMessageConsumer` | `NewSecretMessageNotification` | `SubscribeSecretMessages` (**device-scope**, на RecipientDeviceId) |
| `session-revoked-updates` | `SessionRevokedConsumer` | — | — (инвалидация токена через `TokenRevocationCache`) |

### Схема прохождения события

```
Messages service → RabbitMQ → Consumer → IMediator.Publish() → Handler → StreamSubscriptionsManager → gRPC stream write
```

### StreamSubscriptionsManager (Singleton)

Существует **две формы** менеджера подписок:

**User-scope** (`UserStreamSubscriptionsBase<TEvent>` в `Features/Shared/`):
```csharp
ConcurrentDictionary<long userId, ConcurrentDictionary<Guid subscriptionId, IServerStreamWriter<T>>>
```
Используется для всех «обычных» стримов и приватных чатов: `SubscribeNewMessages`, `SubscribePrivateMessages`, `SubscribePrivateChatInvites` и т.д. Поддерживает несколько подключений одного пользователя (разные устройства).

**Device-scope** (`DeviceStreamSubscriptionsBase<TEvent>` в `Features/Shared/`):
```csharp
ConcurrentDictionary<(long userId, Guid deviceId), ConcurrentDictionary<Guid subscriptionId, IServerStreamWriter<T>>>
```
Используется для стримов **секретных чатов** (`SubscribeSecretChatInvites`, `SubscribeSecretChatResolutions`, `SubscribeSecretMessages`): событие маршрутизируется только в стрим конкретного устройства (recipient_device_id или sender_device_id для resolution). Если устройство оффлайн — событие в стрим не пишется (envelope/invite остаётся в Redis-буфере Messages-сервиса на 24ч + silent push). DeviceId извлекается из JWT-claim'а; если его нет — `Status.Unauthenticated`.

При разрыве `UpdatesApiService` удаляет подписку через `RemoveSubscription`.

### Push-уведомления (отложенная отправка)

`PushNotificationSchedulerHandler` обрабатывает `NewMessageNotification`:
1. Планирует push через 5 секунд (`Task.Delay(5s, cts.Token)`)
2. Если приходит `ReadByNotification` — `ReadByCancelPushHandler` → `PendingPushTracker.CancelPending()` → push отменяется
3. Иначе публикует `PushNotificationEvent` в RabbitMQ (забирает [[Backend/CloudMessaging]])

`PendingPushTracker` (Singleton) хранит `CancellationTokenSource` по ключу `(MessageId, UserId)`.

### Dismiss push после прочтения

`DismissPushPublisher` (`INotificationHandler<ReadByNotification>`) запускается параллельно с `ReadByCancelPushHandler` и использует delta `NewReaders`, не меняя snapshot-контракт `NewReadBy` для realtime-клиентов. `DismissPushDebouncer` (Singleton) объединяет события в trailing-окне 1 секунда по ключу `(UserId, ChatId)`, после чего публикуется один `DismissPushEvent { ChatId, UserId }` в RabbitMQ. Это схлопывает same-chat bulk `MarkAsRead`; межчатовые и межрепличные события остаются best-effort, а `QuotaExceeded` дополнительно обрабатывает [[Backend/CloudMessaging]]. Состояние process-local, как остальная push-логика Updates. Если push в 5-сек окне ещё не ушёл, `ReadByCancelPushHandler` его отменит, а dismiss станет no-op на клиенте.

### gRPC API

```protobuf
// User-scope (старые)
rpc SubscribeNewMessages(SubscribeNewMessagesRequest) returns (stream NewMessageEvent)
rpc SubscribeMessagesRead(SubscribeMessagesReadRequest) returns (stream MessageReadEvent)
rpc SubscribeMessagesEdited(SubscribeMessagesEditedRequest) returns (stream MessageEditedEvent)
rpc SubscribeMessagesDeleted(SubscribeMessagesDeletedRequest) returns (stream MessageDeletedEvent)
rpc SubscribeMessagesPinned(SubscribeMessagesPinnedRequest) returns (stream MessagePinnedEvent)
rpc SubscribeMessagesUnpinned(SubscribeMessagesUnpinnedRequest) returns (stream MessageUnpinnedEvent)
rpc SubscribeAllMessagesUnpinned(SubscribeAllMessagesUnpinnedRequest) returns (stream AllMessagesUnpinnedEvent)

// User-scope (приватные чаты, E2E через passphrase)
rpc SubscribePrivateMessages(...) returns (stream NewEncryptedMessageEvent)
rpc SubscribePrivateMessageEdits(...) returns (stream EncryptedMessageEditedEvent)
rpc SubscribePrivateMessageDeletes(...) returns (stream EncryptedMessageDeletedEvent)
rpc SubscribePrivateMessagesRead(...) returns (stream PrivateMessagesReadEvent)
rpc SubscribePrivateChatInvites(...) returns (stream PrivateChatInviteEvent)
rpc SubscribePrivateChatInviteResolutions(...) returns (stream PrivateChatInviteResolutionEvent)

// Device-scope (секретные чаты, Signal Double Ratchet) — требуют DeviceId в JWT
rpc SubscribeSecretChatInvites(...) returns (stream SecretChatInviteEvent)
rpc SubscribeSecretChatResolutions(...) returns (stream SecretChatInviteResolutionEvent)
rpc SubscribeSecretMessages(...) returns (stream NewSecretMessageEvent)
```

Все методы: регистрируют подписку → `await Task.Delay(Infinite, context.CancellationToken)` → при отключении удаляют подписку.
Защита: `[Authorize(Policy = nameof(TokenType.User))]`. Device-scope методы дополнительно требуют DeviceId в claim'ах JWT.

## Добавление нового типа событий

1. Добавить RabbitMQ-событие в [[Shared/Queue]]
2. Добавить Consumer в `Consumers/`
3. Добавить `INotification` в `Features/{FeatureName}/`
4. Добавить `INotificationHandler` в `Features/{FeatureName}/Handlers/`
5. Добавить `StreamSubscriptionsManager` (если нужен новый стрим)
6. Зарегистрировать Consumer в `Program.cs` (два места: `x.AddConsumer<>` и `cfg.ReceiveEndpoint`)
7. Добавить метод в `UpdatesApiService` и `updates_api.proto`
