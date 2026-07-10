# Barkfluff.CloudMessaging — Карта проекта

Расположение: `Backend/Barkfluff.CloudMessaging/`

Назначение: **Background Worker** для отправки push-уведомлений на мобильные устройства через Firebase Cloud Messaging (FCM). Нет HTTP/gRPC API — работает исключительно как потребитель RabbitMQ.

---

## Файлы проекта

### `Program.cs`
Точка входа приложения. Отвечает за:
- Загрузку конфигурации через `builder.LoadConfiguration(ServiceId.CloudMessaging)` (централизованный Configuration-сервис)
- Регистрацию Serilog
- Регистрацию singleton `FirebaseService`
- Настройку gRPC-клиентов к `UsersService` и `MessagesService` с интерцепторами `JwtClientInterceptor` + `ExceptionClientInterceptor`
- Настройку MassTransit + RabbitMQ: очередь `push-notifications-handler` → `PushNotificationConsumer`

---

### `Consumers/PushNotificationConsumer.cs`
MassTransit-консьюмер события `PushNotificationEvent` из `BarkFluff.Shared.Queue`. Отвечает за:
- Получение события из RabbitMQ (chatId, messageId, senderId, recipientUserIds, messageText, contentType и др.)
- Параллельный запрос данных: `Users.GetById` (данные отправителя) и `Messages.GetChatInfo` (тип/название чата)
- Запрос FCM-токенов устройств получателей через `Users.GetDevicesWithFirebaseTokens`
- Последовательную отправку push-уведомлений на каждое устройство через `FirebaseService`
- Логирование всех ключевых шагов и ошибок

---

### `Services/FirebaseService.cs`
Singleton-сервис-обёртка над **Firebase Admin SDK**. Отвечает за:
- Инициализацию `FirebaseApp` из конфигурационных ключей (`Firebase:ProjectId`, `PrivateKey`, `ClientEmail` и др.) — строит JSON сервисного аккаунта на лету
- Graceful degradation: если Firebase не сконфигурирован — логирует предупреждение и продолжает работу без падения
- Отправку **data-only** FCM-сообщений (без блока `Notification`) с высоким приоритетом (`Priority.High`) через `AndroidConfig`
- Передачу в data-payload полей: `chat_id`, `sender_id`, `type`, `sender_name`, `avatar_url`, `chat_title`, `chat_avatar_url`, `is_group_chat`, `content_type`, `image_url`, `message_id`, `message_text` (обрезается до 100 символов), `attachment_count`
- Обработку ошибок FCM: отдельно — `MessagingErrorCode.Unregistered` (невалидный токен), общие ошибки

---

### `appsettings.json` / `appsettings.Development.json`
Конфигурационные файлы. Базовые ключи (заполняются через Configuration-сервис или переменные окружения):

| Ключ | Описание |
|------|----------|
| `Firebase:ProjectId` | ID проекта Firebase |
| `Firebase:PrivateKeyId` | ID приватного ключа |
| `Firebase:PrivateKey` | Приватный ключ сервисного аккаунта |
| `Firebase:ClientEmail` | Email сервисного аккаунта |
| `Firebase:ClientId` | Client ID |
| `UsersService:Host` | gRPC адрес Users-сервиса |
| `UsersService:Token` | Service-to-service JWT для Users |
| `MessagesService:Host` | gRPC адрес Messages-сервиса |
| `MessagesService:Token` | Service-to-service JWT для Messages |
| `RabbitMQ:Host` | Хост RabbitMQ |
| `RabbitMQ:Username` | Имя пользователя RabbitMQ |
| `RabbitMQ:Password` | Пароль RabbitMQ |

---

### `Dockerfile.slim`
Docker-образ для CI и деплоя.

### `Properties/launchSettings.json`
Настройки запуска для локальной разработки (Visual Studio / dotnet run).

### `Barkfluff.CloudMessaging.csproj`
Файл проекта. Зависимости:

| Пакет | Версия | Назначение |
|-------|--------|-----------|
| `FirebaseAdmin` | 3.2.0 | Firebase Admin SDK |
| `Grpc.Net.ClientFactory` | 2.71.0 | gRPC-клиенты |
| `Grpc.Tools` | 2.72.0 | Компиляция .proto |
| `MassTransit.RabbitMQ` | 8.5.2 | RabbitMQ consumer |

Proto-контракты (только Client):
- `users_api.proto` — `GrpcServices="Client"`
- `messages_api.proto` — `GrpcServices="Client"`
- `shared.proto` — `GrpcServices="None"`

---

## Event Flow (актуальный)

```
Messages service
  → публикует PushNotificationEvent → RabbitMQ
    → очередь: push-notifications-handler
      → PushNotificationConsumer.Consume()
        → параллельно:
            Users.GetById(senderId)          → имя, аватар отправителя
            Messages.GetChatInfo(chatId)     → тип чата, название, аватар
        → Users.GetDevicesWithFirebaseTokens(recipientUserIds) → FCM-токены
        → для каждого токена:
            FirebaseService.SendNotificationAsync() → FCM data-only push
```

> ⚠️ Задержки отправки **нет** — обработка происходит немедленно после получения события.
