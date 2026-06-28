# Barkfluff.CloudMessaging

Background Worker для push-уведомлений через Firebase Cloud Messaging.
**Нет gRPC API** — потребляет `PushNotificationEvent`, `DismissPushEvent`, `AdminBroadcastNotificationEvent`, `IncomingCallPushEvent` и `CallDismissPushEvent` из RabbitMQ.

Расположение: `Backend/Barkfluff.CloudMessaging/`

📁 [[Backend/CloudMessaging-ProjectMap|Карта проекта — все файлы и их назначение]]

## Сборка

```bash
dotnet build Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj
docker-compose -f Backend/docker-compose-dev.yml up -d cloudmessaging
```

## Архитектура

Компоненты:

1. **Program.cs** — startup: загрузка конфигурации (`ServiceId.CloudMessaging`), регистрация gRPC-клиентов (Users, Messages) и MassTransit consumers (`PushNotificationConsumer`, `DismissPushConsumer`, `AdminBroadcastConsumer`, `IncomingCallPushConsumer`, `CallDismissPushConsumer`).
2. **PushNotificationConsumer** — MassTransit consumer для `PushNotificationEvent`. Параллельно запрашивает данные отправителя (Users) и чата (Messages), рассылает push **батчем** на все устройства одним запросом к FCM. При ошибке логирует и не пробрасывает — сообщение считается обработанным (без MassTransit retry).
3. **DismissPushConsumer** — MassTransit consumer для `DismissPushEvent`. Берёт FCM-токены пользователя через `Users.GetDevicesWithFirebaseTokensAsync` и отправляет data-only команду `dismiss_chat_notifications`, чтобы клиенты убрали нотификацию чата на остальных устройствах. Best-effort, без retry.
4. **AdminBroadcastConsumer** — MassTransit consumer для `AdminBroadcastNotificationEvent` (рассылка от админ-панели). Если `TargetDeviceIds` пуст — запрашивает все FCM-токены через `Users.GetAllDevicesWithFirebaseTokensAsync`, иначе — `Users.GetDevicesWithFirebaseTokensByDeviceIdsAsync`. Шлёт через `FirebaseService.SendAdminBroadcastBatchAsync` (с native Notification-блоком). Best-effort, без retry.
5. **IncomingCallPushConsumer** — consumer для `IncomingCallPushEvent` из [[Backend/Calls]]. Резолвит имя звонящего/аватар (`Users.GetById`) и заголовок чата для группового (`Messages.GetChatInfo`), берёт токены получателей и шлёт `FirebaseService.SendIncomingCallBatchAsync` (`type=incoming_call`). Best-effort, без retry.
6. **CallDismissPushConsumer** — consumer для `CallDismissPushEvent`. Берёт токены получателей и шлёт `FirebaseService.SendCallDismissBatchAsync` (`type=dismiss_call`, `reason`) — гасит нотификацию входящего звонка. Best-effort, без retry.
7. **FirebaseService** — singleton над Firebase Admin SDK.
   - `SendNotificationBatchAsync` / `SendDismissBatchAsync` / `SendIncomingCallBatchAsync` / `SendCallDismissBatchAsync` — **data-only** сообщения (без `Notification` блока), Android-клиент сам рисует уведомления.
   - `SendAdminBroadcastBatchAsync` — **native Notification-блок** (title/body/imageUrl) для админ-рассылок, чтобы Android-система показала уведомление без вовлечения клиентского кода. Чанкование по 500 токенов (FCM лимит на multicast).
   Все методы используют `SendEachForMulticastAsync` и обрабатывают `Unregistered`-ошибки для последующей очистки токенов.

## Event Flow

```
Messages service → PushNotificationEvent → RabbitMQ
  → push-notifications-handler queue
  → PushNotificationConsumer
  → parallel: Users.GetById + Messages.GetChatInfo
  → Users.GetDevicesWithFirebaseTokens
  → FirebaseService.SendNotificationBatchAsync (один батч-запрос на все токены)
```

```
Updates (DismissPushPublisher) → DismissPushEvent → RabbitMQ
  → dismiss-push-handler queue
  → DismissPushConsumer
  → Users.GetDevicesWithFirebaseTokens(userId)
  → FirebaseService.SendDismissBatchAsync(tokens, chatId)
```

```
AdminPanel (POST /api/notifications/broadcast/*) → AdminBroadcastNotificationEvent → RabbitMQ
  → admin-broadcast-handler queue
  → AdminBroadcastConsumer
  → Users.GetAllDevicesWithFirebaseTokens()   ← если TargetDeviceIds пуст
    или
    Users.GetDevicesWithFirebaseTokensByDeviceIds(deviceIds)
  → FirebaseService.SendAdminBroadcastBatchAsync(tokens, title, body, imageUrl)
```

```
Calls (InitiateCall) → IncomingCallPushEvent → RabbitMQ
  → incoming-call-push-handler queue
  → IncomingCallPushConsumer
  → Users.GetById (имя/аватар) + Messages.GetChatInfo (заголовок группы)
  → Users.GetDevicesWithFirebaseTokens(recipients)
  → FirebaseService.SendIncomingCallBatchAsync (type=incoming_call)

Calls (accept/reject/end/timeout/room_finished) → CallDismissPushEvent → RabbitMQ
  → call-dismiss-push-handler queue
  → CallDismissPushConsumer
  → Users.GetDevicesWithFirebaseTokens(recipients)
  → FirebaseService.SendCallDismissBatchAsync (type=dismiss_call, reason)
```

> ⚠️ Задержки отправки **нет** — обработка немедленная после получения события из очереди.

## FirebaseService — data payload

Отправляются **data-only** сообщения (без блока `Notification`), `Priority.High`. Android-клиент сам строит / убирает уведомления, диспатч по полю `type`.

### `type = "new_message"` (push нового сообщения)

| Ключ | Значение |
|------|---------|
| `chat_id` | ID чата |
| `sender_id` | ID отправителя |
| `sender_name` | Имя отправителя |
| `avatar_url` | URL аватара отправителя |
| `chat_title` | Название чата (для групп) |
| `chat_avatar_url` | Аватар чата |
| `is_group_chat` | `"true"` / `"false"` |
| `content_type` | Тип контента сообщения |
| `image_url` | Превью изображения |
| `message_id` | ID сообщения |
| `message_text` | Текст (обрезается до 100 символов) |
| `attachment_count` | Количество вложений |

### `type = "dismiss_chat_notifications"` (скрыть нотификацию чата)

Шлётся, когда пользователь прочитал сообщение на другом устройстве — чтобы остальные FCM-устройства убрали уже доставленное уведомление.

| Ключ | Значение |
|------|---------|
| `chat_id` | ID чата, нотификацию которого нужно убрать |

### `type = "incoming_call"` (входящий звонок)

Шлётся из [[Backend/Calls]] при инициации звонка, чтобы ринг дошёл при background/killed app. `Priority.High`.

| Ключ | Значение |
|------|---------|
| `call_id` | ID звонка |
| `caller_user_id` | ID звонящего |
| `chat_id` | ID чата (пусто для личного) |
| `media_type` | `1` audio / `2` video |
| `started_at` | unix-секунды начала |
| `caller_name` | Имя звонящего |
| `avatar_url` | Аватар звонящего |
| `chat_title` | Заголовок чата (для группового) |

### `type = "dismiss_call"` (погасить нотификацию звонка)

Шлётся при завершении ринга (accept/reject/end/timeout) — убирает нотификацию входящего звонка на остальных устройствах.

| Ключ | Значение |
|------|---------|
| `call_id` | ID звонка |
| `reason` | `accepted` / `rejected` / `ended` / `timeout` / `busy` |

### `type = "admin_broadcast"` (админ-рассылка)

В отличие от остальных push, **включает блок `Notification`** (title/body/imageUrl) — Android-система отображает уведомление сама, без участия клиентского кода. Триггерится из AdminPanel-страницы «Уведомления».

| Ключ | Значение |
|------|---------|
| `type` | `"admin_broadcast"` (маркер для возможной кастомизации в будущем) |

Поля native-блока FCM: `Notification.Title`, `Notification.Body`, `Notification.ImageUrl`, `AndroidConfig.Priority = High`, `AndroidNotification.ImageUrl` (для BigPictureStyle).

## Конфигурация

| Ключ | Описание |
|------|----------|
| `Firebase:ProjectId/PrivateKeyId/PrivateKey/ClientEmail/ClientId` | Firebase Admin SDK |
| `UsersService:Host/Token` | gRPC Users |
| `MessagesService:Host/Token` | gRPC Messages |
| `RabbitMQ:*` | RabbitMQ |

## Proto

- `users_api.proto` — Client
- `messages_api.proto` — Client
- `shared.proto` — None

## Зависимости

- `FirebaseAdmin 3.2.0`
