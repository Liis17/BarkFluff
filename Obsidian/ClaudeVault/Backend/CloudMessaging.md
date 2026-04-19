# Barkfluff.CloudMessaging

Background Worker для push-уведомлений через Firebase Cloud Messaging.
**Нет gRPC API** — потребляет `PushNotificationEvent` из RabbitMQ.

Расположение: `Backend/Barkfluff.CloudMessaging/`

## Сборка

```bash
dotnet build Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj
docker-compose -f Backend/docker-compose-dev.yml up -d cloudmessaging
```

## Архитектура

Три компонента:

1. **Program.cs** — startup: загрузка конфигурации (`ServiceId.CloudMessaging`), регистрация gRPC-клиентов (Users, Messages) и MassTransit consumer.
2. **PushNotificationConsumer** — MassTransit consumer для `PushNotificationEvent`. Параллельно запрашивает данные отправителя (Users) и чата (Messages), рассылает push на все устройства через FirebaseService.
3. **FirebaseService** — singleton над Firebase Admin SDK. Отправляет **data-only** сообщения (без `Notification` блока) — Android-клиент сам строит уведомления.

## Event Flow

```
Messages service → PushNotificationEvent → RabbitMQ
  → push-notifications-handler queue
  → PushNotificationConsumer
  → parallel: Users.GetById + Messages.GetChatInfo
  → Users.GetDevicesWithFirebaseTokens
  → FirebaseService.SendNotificationAsync (per device)
```

Отправка задерживается на 5 сек через [[Backend/Updates]] (отменяется если прочитано).

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
