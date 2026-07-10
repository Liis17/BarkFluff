# BarkFluff.Updates — Карта файлов проекта

Расположение: `Backend/BarkFluff.Updates/`
Порт: **7015** | Без БД — всё в памяти.

Связанный файл: [[Backend/Updates]]

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка запуска сервиса. Регистрирует gRPC, XAuth, MassTransit (16 consumer-ов), Serilog, Metrics. Монтирует `UpdatesApiService`. |
| `DependencyInjection.cs` | Extension-метод `AddUpdatesServices()`. Регистрирует **15 `StreamSubscriptionsManager`** (7 базовых + 5 user-scope для приватных + 3 device-scope для секретных) и `PendingPushTracker` как Singleton, подключает MediatR. |
| `appsettings.json` | Базовая конфигурация: порт `7015`, адрес Configuration-сервиса. |
| `appsettings.Development.json` | Переопределения для разработки. |
| `Properties/launchSettings.json` | Профили запуска (IDE). |
| `Dockerfile.slim` | Образ для Docker-деплоя, используемый CI. |

---

## Host (gRPC-сервис)

| Файл | Назначение |
|------|-----------|
| `Host/UpdatesApiService.cs` | gRPC-сервис (`UpdatesApiBase`). 15 методов: 7 user-scope обычных (`SubscribeNewMessages`, `SubscribeMessagesRead`/`Edited`/`Deleted`/`Pinned`/`Unpinned`/`AllUnpinned`), 5 user-scope приватных (`SubscribePrivateMessages`/`MessageEdits`/`MessageDeletes`/`ChatInvites`/`ChatInviteResolutions`), 3 device-scope секретных (`SubscribeSecretChatInvites`/`SecretChatResolutions`/`SecretMessages`). Каждый регистрирует подписку в соответствующем `StreamSubscriptionsManager`, ждёт `CancellationToken`, при отключении удаляет подписку. Device-scope методы дополнительно требуют DeviceId через `RequireDeviceId()` (иначе `Status.Unauthenticated`). Собирает метрики (`*_subscriptions_opened/closed/active`, `subscriptions_active_total`). Защищён `[Authorize(Policy = TokenType.User)]`. |

---

## Consumers (MassTransit / RabbitMQ)

| Файл | Queue | Назначение |
|------|-------|-----------|
| `Consumers/NewMessageConsumer.cs` | `new-messages-updates-handler` | Получает `NewMessageEvent` из RabbitMQ. Парсит бинарный protobuf-объект `Message`, публикует `NewMessageNotification` через MediatR. |
| `Consumers/ReadByConsumer.cs` | `read-receipts-updates-handler` | Получает `MessageReadEvent` из RabbitMQ. Публикует `ReadByNotification` через MediatR. |
| `Consumers/MessageEditedConsumer.cs` | `messages-edited-updates-handler` | Получает `MessageEditedEvent` из RabbitMQ. Парсит бинарный protobuf `Message`, публикует `MessageEditedNotification` через MediatR. |
| `Consumers/MessageDeletedConsumer.cs` | `messages-deleted-updates-handler` | Получает `MessageDeletedEvent` из RabbitMQ. Публикует `MessageDeletedNotification` (содержит только `MessageId`+`ChatId`+`Members`) через MediatR. |
| `Consumers/SessionRevokedConsumer.cs` | `session-revoked-updates` | Получает `SessionRevokedEvent` из RabbitMQ. Вызывает `TokenRevocationCache.Revoke()` — принудительно инвалидирует токен сессии для пары `(UserId, DeviceId)`. |
| `Consumers/NewEncryptedMessageConsumer.cs` | `new-encrypted-messages-updates-handler` | Парсит proto `EncryptedMessage` и публикует `NewEncryptedMessageNotification` (приватные чаты, user-scope). |
| `Consumers/EncryptedMessageEditedConsumer.cs` | `encrypted-messages-edited-updates-handler` | Парсит proto и публикует `EncryptedMessageEditedNotification`. |
| `Consumers/EncryptedMessageDeletedConsumer.cs` | `encrypted-messages-deleted-updates-handler` | Публикует `EncryptedMessageDeletedNotification` (только MessageId). |
| `Consumers/PrivateChatInviteConsumer.cs` | `private-chat-invites-updates-handler` | Публикует `PrivateChatInviteNotification` (приглашённому). |
| `Consumers/PrivateChatInviteResolutionConsumer.cs` | `private-chat-invite-resolutions-updates-handler` | Публикует `PrivateChatInviteResolutionNotification` (инициатору, accepted/rejected). |
| `Consumers/SecretChatInviteConsumer.cs` | `secret-chat-invites-updates-handler` | Публикует `SecretChatInviteNotification` (device-scope). |
| `Consumers/SecretChatInviteResolutionConsumer.cs` | `secret-chat-invite-resolutions-updates-handler` | Публикует `SecretChatInviteResolutionNotification` (device-scope, на устройство-инициатор). |
| `Consumers/NewSecretMessageConsumer.cs` | `new-secret-messages-updates-handler` | Публикует `NewSecretMessageNotification` (device-scope). Если recipient_device оффлайн — handler не пишет в стрим, envelope остаётся в `SecretMessageBuffer` Messages-сервиса. |

---

## Features / SubscribeNewMessages

| Файл | Назначение |
|------|-----------|
| `Features/SubscribeNewMessages/NewMessageNotification.cs` | MediatR `INotification`. Передаёт объект сообщения (`Message`), список участников чата (`Members`) и `ChatId`. |
| `Features/SubscribeNewMessages/StreamSubscriptionsManager.cs` | Singleton. Хранит `ConcurrentDictionary<userId, ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>>`. Поддерживает несколько устройств одного пользователя. Методы: `RegisterSubscription`, `RemoveSubscription`, `GetUserStreams`. |
| `Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs` | MediatR-обработчик `NewMessageNotification`. Параллельно рассылает `NewMessageEvent` во все активные gRPC-стримы всех участников чата. |

---

## Features / SubscribeMessagesRead

| Файл | Назначение |
|------|-----------|
| `Features/SubscribeMessagesRead/ReadByNotification.cs` | MediatR `INotification`. Содержит `ChatId`, `MessageId`, `NewReadBy` (кто прочитал), `ChatMembers` (все участники чата). |
| `Features/SubscribeMessagesRead/StreamSubscriptionsManager.cs` | Аналог менеджера подписок для `MessageReadEvent`. Та же структура `ConcurrentDictionary`, но тип стрима `IServerStreamWriter<MessageReadEvent>`. |
| `Features/SubscribeMessagesRead/Handlers/ReadByNotificationHandler.cs` | MediatR-обработчик `ReadByNotification`. Параллельно рассылает `MessageReadEvent` всем участникам чата через активные стримы. |

---

## Features / SubscribeMessagesEdited

| Файл | Назначение |
|------|-----------|
| `Features/SubscribeMessagesEdited/MessageEditedNotification.cs` | MediatR `INotification`. Содержит обновлённый объект `Message`, список участников и `ChatId`. |
| `Features/SubscribeMessagesEdited/StreamSubscriptionsManager.cs` | Singleton-менеджер подписок типизированный `IServerStreamWriter<MessageEditedEvent>`. |
| `Features/SubscribeMessagesEdited/Handlers/MessageEditedNotificationHandler.cs` | Параллельная рассылка `MessageEditedEvent` всем участникам чата с активными стримами. |

---

## Features / SubscribeMessagesDeleted

| Файл | Назначение |
|------|-----------|
| `Features/SubscribeMessagesDeleted/MessageDeletedNotification.cs` | MediatR `INotification`. Содержит `MessageId`, `Members`, `ChatId` (без полного тела сообщения — клиент удаляет по id). |
| `Features/SubscribeMessagesDeleted/StreamSubscriptionsManager.cs` | Singleton-менеджер подписок типизированный `IServerStreamWriter<MessageDeletedEvent>`. |
| `Features/SubscribeMessagesDeleted/Handlers/MessageDeletedNotificationHandler.cs` | Параллельная рассылка `MessageDeletedEvent` всем участникам чата с активными стримами. |

---

## Features / Shared (generic базы менеджеров)

| Файл | Назначение |
|------|-----------|
| `Features/Shared/UserStreamSubscriptionsBase.cs` | Generic `UserStreamSubscriptionsBase<TEvent>`. Хранит `ConcurrentDictionary<userId, ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>>`. Методы `RegisterSubscription/RemoveSubscription/GetUserStreams/ActiveCount`. Все user-scope менеджеры (5 для приватных) наследуются от него. |
| `Features/Shared/DeviceStreamSubscriptionsBase.cs` | Generic `DeviceStreamSubscriptionsBase<TEvent>`. Хранит `ConcurrentDictionary<(userId, deviceId), ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>>`. Методы `RegisterSubscription(userId, deviceId, stream)/RemoveSubscription/GetDeviceStreams/HasActiveStreams/ActiveCount`. Все 3 device-scope менеджера секретных чатов наследуются от него. |

---

## Features / Subscribe* (приватные чаты — user-scope, секретные — device-scope)

| Папка | Manager наследует | Notification | Handler |
|-------|-------------------|--------------|---------|
| `Features/SubscribePrivateMessages/` | `UserStreamSubscriptionsBase<NewEncryptedMessageEvent>` | `NewEncryptedMessageNotification(EncryptedMessage, Members, ChatId)` | Рассылает proto-объект всем участникам чата |
| `Features/SubscribePrivateMessageEdits/` | `UserStreamSubscriptionsBase<EncryptedMessageEditedEvent>` | `EncryptedMessageEditedNotification` | То же для edits |
| `Features/SubscribePrivateMessageDeletes/` | `UserStreamSubscriptionsBase<EncryptedMessageDeletedEvent>` | `EncryptedMessageDeletedNotification(ChatId, MessageId, Members)` | Рассылает только MessageId |
| `Features/SubscribePrivateChatInvites/` | `UserStreamSubscriptionsBase<PrivateChatInviteEvent>` | `PrivateChatInviteNotification` (с KdfSalt+Verifier) | Шлёт **только** на InviteeUserId |
| `Features/SubscribePrivateChatInviteResolutions/` | `UserStreamSubscriptionsBase<PrivateChatInviteResolutionEvent>` | `PrivateChatInviteResolutionNotification` | Шлёт **только** на InviterUserId |
| `Features/SubscribeSecretChatInvites/` | `DeviceStreamSubscriptionsBase<SecretChatInviteEvent>` | `SecretChatInviteNotification` | Маршрутизация на `(RecipientUserId, RecipientDeviceId)`. Если устройство оффлайн — `secret_chat_invites_buffered_only` метрика, в стрим не пишет |
| `Features/SubscribeSecretChatResolutions/` | `DeviceStreamSubscriptionsBase<SecretChatInviteResolutionEvent>` | `SecretChatInviteResolutionNotification` | Маршрутизация на `(SenderUserId, SenderDeviceId)` инициатора |
| `Features/SubscribeSecretMessages/` | `DeviceStreamSubscriptionsBase<NewSecretMessageEvent>` | `NewSecretMessageNotification` | Маршрутизация на `(RecipientUserId, RecipientDeviceId)`. Если оффлайн — `secret_messages_buffered_only`, envelope остаётся в Redis-буфере Messages |

---

## Features / PushNotifications

| Файл | Назначение |
|------|-----------|
| `Features/PushNotifications/PendingPushTracker.cs` | Singleton. Хранит `ConcurrentDictionary<(MessageId, UserId), CancellationTokenSource>`. Методы: `RegisterPending` (создаёт/заменяет CTS), `CancelPending` (отменяет при прочтении), `RemovePending` (удаляет после отправки). |
| `Features/PushNotifications/PushNotificationSchedulerHandler.cs` | MediatR-обработчик `NewMessageNotification`. Для каждого получателя запускает фоновую задачу с `Task.Delay(5s)`. Если не отменена — публикует `PushNotificationEvent` в RabbitMQ (подхватывает [[Backend/CloudMessaging]]). Определяет тип вложения и `PreviewUrl` для FCM BigPicture. |
| `Features/PushNotifications/ReadByCancelPushHandler.cs` | MediatR-обработчик `ReadByNotification`. Для каждого пользователя из `NewReadBy` вызывает `PendingPushTracker.CancelPending()` — отменяет запланированный push. |

---

## Shared Proto

| Файл | Назначение |
|------|-----------|
| `Shared/BarkFluff.Proto/updates_api.proto` | gRPC-контракт сервиса. Определяет `UpdatesApi` с четырьмя server-streaming методами: `SubscribeNewMessages` → `stream NewMessageEvent`, `SubscribeMessagesRead` → `stream MessageReadEvent`, `SubscribeMessagesEdited` → `stream MessageEditedEvent`, `SubscribeMessagesDeleted` → `stream MessageDeletedEvent`. |
| `Shared/BarkFluff.Proto/shared.proto` | Общий контракт (используется для типа `Message`). |

---

## Схема потока событий

```
Messages → RabbitMQ → NewMessageConsumer → MediatR.Publish(NewMessageNotification)
                                                ├── NewMessageNotificationHandler → StreamSubscriptionsManager → gRPC stream write (все устройства)
                                                └── PushNotificationSchedulerHandler → Task.Delay(5s) → RabbitMQ PushNotificationEvent → CloudMessaging

Messages → RabbitMQ → ReadByConsumer → MediatR.Publish(ReadByNotification)
                                            ├── ReadByNotificationHandler → StreamSubscriptionsManager → gRPC stream write
                                            └── ReadByCancelPushHandler → PendingPushTracker.CancelPending() → отмена push

Identity → RabbitMQ → SessionRevokedConsumer → TokenRevocationCache.Revoke() → принудительный logout
```
