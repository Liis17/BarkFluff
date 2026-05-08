# Barkfluff.CloudMessaging

Background Worker для push-уведомлений через Firebase Cloud Messaging.
**Нет gRPC API** — потребляет `PushNotificationEvent` и `DismissPushEvent` из RabbitMQ.

Расположение: `Backend/Barkfluff.CloudMessaging/`

📁 [[Backend/CloudMessaging-ProjectMap|Карта проекта — все файлы и их назначение]]

## Сборка

```bash
dotnet build Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj
docker-compose -f Backend/docker-compose-dev.yml up -d cloudmessaging
```

## Архитектура

Компоненты:

1. **Program.cs** — startup: загрузка конфигурации (`ServiceId.CloudMessaging`), регистрация gRPC-клиентов (Users, Messages) и MassTransit consumers (`PushNotificationConsumer`, `DismissPushConsumer`).
2. **PushNotificationConsumer** — MassTransit consumer для `PushNotificationEvent`. Параллельно запрашивает данные отправителя (Users) и чата (Messages), рассылает push **батчем** на все устройства одним запросом к FCM. При ошибке логирует и не пробрасывает — сообщение считается обработанным (без MassTransit retry).
3. **DismissPushConsumer** — MassTransit consumer для `DismissPushEvent`. Берёт FCM-токены пользователя через `Users.GetDevicesWithFirebaseTokensAsync` и отправляет data-only команду `dismiss_chat_notifications`, чтобы клиенты убрали нотификацию чата на остальных устройствах. Best-effort, без retry.
4. **FirebaseService** — singleton над Firebase Admin SDK. Отправляет **data-only** сообщения (без `Notification` блока) — Android-клиент сам строит уведомления. Методы `SendNotificationBatchAsync` и `SendDismissBatchAsync` используют `SendEachForMulticastAsync` (один HTTP-запрос на до 500 токенов), обрабатывают `Unregistered`-ошибки для последующей очистки токенов.

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
