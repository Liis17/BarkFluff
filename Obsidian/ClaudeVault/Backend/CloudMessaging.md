# Barkfluff.CloudMessaging

Background Worker для push-уведомлений через Firebase Cloud Messaging.
**Нет gRPC API** — потребляет `PushNotificationEvent` из RabbitMQ.

Расположение: `Backend/Barkfluff.CloudMessaging/`

📁 [[Backend/CloudMessaging-ProjectMap|Карта проекта — все файлы и их назначение]]

## Сборка

```bash
dotnet build Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj
docker-compose -f Backend/docker-compose-dev.yml up -d cloudmessaging
```

## Архитектура

Три компонента:

1. **Program.cs** — startup: загрузка конфигурации (`ServiceId.CloudMessaging`), регистрация gRPC-клиентов (Users, Messages) и MassTransit consumer.
2. **PushNotificationConsumer** — MassTransit consumer для `PushNotificationEvent`. Параллельно запрашивает данные отправителя (Users) и чата (Messages), рассылает push **батчем** на все устройства одним запросом к FCM. При ошибке логирует и не пробрасывает — сообщение считается обработанным (без MassTransit retry).
3. **FirebaseService** — singleton над Firebase Admin SDK. Отправляет **data-only** сообщения (без `Notification` блока) — Android-клиент сам строит уведомления. Метод `SendNotificationBatchAsync` использует `SendEachForMulticastAsync` (один HTTP-запрос на до 500 токенов), обрабатывает `Unregistered`-ошибки для последующей очистки токенов.

## Event Flow

```
Messages service → PushNotificationEvent → RabbitMQ
  → push-notifications-handler queue
  → PushNotificationConsumer
  → parallel: Users.GetById + Messages.GetChatInfo
  → Users.GetDevicesWithFirebaseTokens
  → FirebaseService.SendNotificationBatchAsync (один батч-запрос на все токены)
```

> ⚠️ Задержки отправки **нет** — обработка немедленная после получения события из очереди.

## FirebaseService — data payload

Отправляются **data-only** сообщения (без блока `Notification`), `Priority.High`. Android-клиент сам строит уведомления.

| Ключ | Значение |
|------|---------|
| `type` | `"new_message"` |
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
