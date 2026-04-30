# BarkFluff.Updates — Карта файлов проекта

Расположение: `Backend/BarkFluff.Updates/`
Порт: **7015** | Без БД — всё в памяти.

Связанный файл: [[Backend/Updates]]

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка запуска сервиса. Регистрирует gRPC, XAuth, MassTransit (3 consumer-а), Serilog, Metrics. Монтирует `UpdatesApiService`. |
| `DependencyInjection.cs` | Extension-метод `AddUpdatesServices()`. Регистрирует оба `StreamSubscriptionsManager` и `PendingPushTracker` как Singleton, подключает MediatR. |
| `appsettings.json` | Базовая конфигурация: порт `7015`, адрес Configuration-сервиса. |
| `appsettings.Development.json` | Переопределения для разработки. |
| `Properties/launchSettings.json` | Профили запуска (IDE). |
| `Dockerfile` / `Dockerfile.slim` | Образы для Docker-деплоя. |

---

## Host (gRPC-сервис)

| Файл | Назначение |
|------|-----------|
| `Host/UpdatesApiService.cs` | gRPC-сервис (`UpdatesApiBase`). Два метода: `SubscribeNewMessages` и `SubscribeMessagesRead`. Регистрирует подписку в `StreamSubscriptionsManager`, ждёт `CancellationToken`, при отключении удаляет подписку. Собирает метрики (`active_subscriptions`, `active_subscriptions_removed`). Защищён `[Authorize(Policy = TokenType.User)]`. |

---

## Consumers (MassTransit / RabbitMQ)

| Файл | Queue | Назначение |
|------|-------|-----------|
| `Consumers/NewMessageConsumer.cs` | `new-messages-updates-handler` | Получает `NewMessageEvent` из RabbitMQ. Парсит бинарный protobuf-объект `Message`, публикует `NewMessageNotification` через MediatR. |
| `Consumers/ReadByConsumer.cs` | `read-receipts-updates-handler` | Получает `MessageReadEvent` из RabbitMQ. Публикует `ReadByNotification` через MediatR. |
| `Consumers/SessionRevokedConsumer.cs` | `session-revoked-updates` | Получает `SessionRevokedEvent` из RabbitMQ. Вызывает `TokenRevocationCache.Revoke()` — принудительно инвалидирует токен сессии для пары `(UserId, DeviceId)`. |

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
| `Shared/BarkFluff.Proto/updates_api.proto` | gRPC-контракт сервиса. Определяет `UpdatesApi` с двумя server-streaming методами: `SubscribeNewMessages` → `stream NewMessageEvent`, `SubscribeMessagesRead` → `stream MessageReadEvent`. |
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
