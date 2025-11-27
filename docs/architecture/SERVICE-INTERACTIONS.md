# Взаимодействие между микросервисами

## Обзор

В BarkFluff используется два основных паттерна взаимодействия между сервисами:

1. **Синхронное (gRPC)** - для немедленных запросов данных
2. **Асинхронное (RabbitMQ)** - для событий и уведомлений

## Граф зависимостей

### Configuration Service
**Роль**: Центральный конфигурационный сервис

```
Configuration Service
    ↑ (загрузка конфигурации при старте)
    ├── Identity Service
    ├── Users Service
    ├── Messages Service
    ├── Files Service
    ├── Updates Service
    ├── Notification Service
    ├── Beacon Service
    ├── FastAuth Service
    └── Navigator Service
```

**Все сервисы** зависят от Configuration при запуске через метод `LoadConfiguration()`.

### Identity Service → Users Service

**Тип**: gRPC (синхронное)

**Методы**:
- `FindByLoginAsync` - поиск пользователя при логине
- `AddDraftUserAsync` - создание черновика при регистрации
- `ConfirmUserAsync` - подтверждение аккаунта
- `GetByIdAsync` - получение данных пользователя
- `GetUserContactsAsync` - получение email для 2FA

**Пример** (Identity/Features/Auth/AuthCommandHandler.cs):
```csharp
var user = await _usersServerApiClient.FindByLoginAsync(new FindByLoginRequest
{
    Username = request.Username,
    Email = request.Email
});
```

### Messages Service → Users Service

**Тип**: gRPC (синхронное)

**Методы**:
- `GetByIdAsync` - получение имени/аватара отправителя
- `ListByIdsAsync` - массовое получение данных участников чата

**Использование**: Отображение имён пользователей в чатах

### Messages Service → Files Service

**Тип**: gRPC (синхронное)

**Методы**:
- `GetFilesDataAsync` - валидация и получение метаданных файлов
- `GetFileDataAsync` - проверка типа файла

**Использование**: Валидация вложений при отправке сообщения

### Users Service → Files Service

**Тип**: gRPC (синхронное)

**Методы**:
- `GetFileDataAsync` - валидация файла аватара

**Использование**: Проверка что загруженный файл - действительно аватар

## RabbitMQ Event Flow

### Identity → Notification

**События**:
```
Identity Service
    │
    ├─[ConfirmationRegistration]───→ RabbitMQ → Notification Service
    ├─[ConfirmationAuth]───────────→ RabbitMQ → Notification Service
    ├─[ConfirmationOtpEmail]───────→ RabbitMQ → Notification Service
    └─[ResetPassword]──────────────→ RabbitMQ → Notification Service
```

**Queue**: `notifications-email-handler`

**Payload**:
```json
{
  "Title": "Код подтверждения",
  "Address": "user@example.com",
  "Type": "ConfirmationRegistration",
  "Payload": {
    "confirmation_code": "123456",
    "username": "john_doe",
    "ip": "192.168.1.1",
    "devicename": "iPhone",
    "location": "USA, California"
  }
}
```

### Users → Messages & Updates

**События**:
```
Users Service
    │
    ├─[UserChangedName]────→ RabbitMQ ┬→ Messages Service (обновление кеша)
    │                                  └→ Updates Service (уведомление клиентов)
    │
    ├─[UserChangedUsername]→ RabbitMQ ┬→ Messages Service
    │                                  └→ Updates Service
    │
    ├─[UserChangedAvatar]──→ RabbitMQ ┬→ Messages Service (кеш аватара в чатах)
    │                                  └→ Updates Service
    │
    └─[UserChangedBio]─────→ RabbitMQ  → Updates Service
```

**Queue (Messages)**: `user-changed-name-messages`, `user-changed-avatar-messages`

**Использование**: Синхронизация имен/аватаров в личных чатах

### Messages → Updates

**События**:
```
Messages Service
    │
    └─[NewMessageEvent]────→ RabbitMQ → Updates Service
                                             │
                                             └→ gRPC Stream → Connected Clients
```

**Queue**: `new-messages-updates-handler`

**Payload**:
```json
{
  "ChatId": "uuid",
  "ChatMembers": [1, 2, 3],
  "Message": "<protobuf bytes>"
}
```

**Процесс**:
1. Пользователь отправляет сообщение
2. Messages сохраняет в БД
3. Messages публикует NewMessageEvent в RabbitMQ
4. Updates потребляет событие
5. Updates пушит через gRPC stream всем участникам чата

## Детальные потоки взаимодействия

### Поток 1: Регистрация пользователя

```
Client
  │
  ├─1─→ Identity.CreateAccount(email, username, firstName, lastName)
  │       │
  │       ├─2─→ Users.AddDraftUser()
  │       │       └─2.1─→ PostgreSQL (users_db)
  │       │
  │       ├─3─→ Генерация кода подтверждения
  │       │       └─3.1─→ PostgreSQL (identity_db.ConfirmationCodes)
  │       │
  │       └─4─→ RabbitMQ.Publish(ConfirmationRegistration)
  │               │
  │               └─5─→ Notification.SendEmail()
  │                       └─5.1─→ SMTP Server
  │
  ├─6─→ Identity.ConfirmAccount(codeId, code)
  │       │
  │       ├─7─→ Users.ConfirmUser()
  │       │       └─7.1─→ PostgreSQL (users_db: IsDraft = false)
  │       │
  │       └─8─→ Генерация Refresh Token
  │               └─8.1─→ PostgreSQL (identity_db.RefreshTokens)
  │
  └─9─← { refreshToken }
```

### Поток 2: Отправка сообщения

```
Client (userId: 1)
  │
  ├─1─→ Messages.SendMessage(chatId, text, fileIds)
  │       │
  │       ├─2─→ Проверка доступа к чату
  │       │       └─2.1─→ PostgreSQL (messages_db.ChatMembers)
  │       │
  │       ├─3─→ Files.GetFilesData(fileIds)
  │       │       └─3.1─→ PostgreSQL (files_db.UploadedFiles)
  │       │
  │       ├─4─→ Создание Message
  │       │       └─4.1─→ PostgreSQL (messages_db.Messages)
  │       │
  │       └─5─→ RabbitMQ.Publish(NewMessageEvent)
  │               │
  │               └─6─→ Updates.NewMessageConsumer()
  │                       │
  │                       ├─7─→ MediatR.Publish(NewMessageNotification)
  │                       │       │
  │                       │       └─7.1─→ StreamSubscriptionsManager
  │                       │                   │
  │                       │                   ├─→ Client A (gRPC Stream)
  │                       │                   └─→ Client B (gRPC Stream)
  │
  └─8─← { message }
```

### Поток 3: Загрузка файла

```
Client
  │
  ├─1─→ Files.GetUploadUrl(type=MessageAttachmentImage)
  │       │
  │       ├─2─→ Создание UploadFile (Etag=null)
  │       │       └─2.1─→ PostgreSQL (files_db.UploadedFiles)
  │       │
  │       └─3─← { url: "http://files:7005/upload/{fileId}", fileId }
  │
  ├─4─→ HTTP POST /upload/{fileId} (multipart/form-data)
  │       │
  │       ├─5─→ S3Uploader.Upload(bucket, fileId, stream)
  │       │       └─5.1─→ Minio S3
  │       │
  │       ├─6─→ ImageCompressor.Compress(stream, 1024px)
  │       │
  │       ├─7─→ S3Uploader.Upload(bucket, previewId, compressed)
  │       │       └─7.1─→ Minio S3
  │       │
  │       └─8─→ Обновление UploadFile (Etag, PreviewId, Size)
  │               └─8.1─→ PostgreSQL (files_db)
  │
  └─9─→ Messages.SendMessage(chatId, text, fileIds=[fileId])
```

### Поток 4: Real-time сообщения

```
Client A (userId: 1)
  │
  └─1─→ Updates.SubscribeNewMessages()
          │
          ├─2─→ Регистрация gRPC Stream
          │       └─2.1─→ StreamSubscriptionsManager.RegisterSubscription(1, stream)
          │
          └─3─→ Task.Delay(Timeout.Infinite) ──┐
                                                │ (держит соединение)
                                                │
... время проходит ...                         │
                                                │
Client B (userId: 2) отправляет сообщение      │
  │                                             │
  └─→ Messages.SendMessage()                   │
        └─→ RabbitMQ.Publish(NewMessageEvent)  │
              └─→ Updates.Consume()             │
                    │                           │
                    └─→ MediatR.Publish()       │
                          │                     │
                          └─→ Handler:          │
                                foreach member: │
                                  if (member == 1): ◄┘
                                    stream.WriteAsync(event)
                                      │
                                      ▼
Client A ◄──────────────────────────── NewMessageEvent { message, chatId }
```

## Паттерны взаимодействия

### 1. Request-Response (gRPC)

**Когда использовать**:
- Нужен немедленный ответ
- Критичная по времени операция
- Валидация перед действием

**Пример**: Messages → Users (получение имени пользователя)

### 2. Publish-Subscribe (RabbitMQ)

**Когда использовать**:
- Асинхронные уведомления
- Множественные подписчики
- Не критично по времени

**Пример**: Identity → Notification (email уведомления)

### 3. Event Sourcing (частично)

**Где используется**:
- Messages: хранение истории сообщений
- Users: события изменения профиля

### 4. Server Streaming (gRPC)

**Когда использовать**:
- Real-time обновления
- Push-уведомления
- Long-running connections

**Пример**: Updates → Clients (новые сообщения)

## Обработка ошибок

### gRPC ошибки

Все сервисы используют `ServerExceptionInterceptor`:

```csharp
try
{
    return await continuation(request, context);
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unhandled exception");
    throw new RpcException(new Status(
        StatusCode.Internal,
        ex.Message
    ));
}
```

### RabbitMQ ошибки

MassTransit автоматически:
- Повторяет неудачные сообщения
- Отправляет в error queue после N попыток
- Логирует ошибки потребления

## Circuit Breaker

*(Не реализовано)* - Рекомендуется добавить Polly для:
- Retry политик
- Circuit breaker для gRPC вызовов
- Timeout handling

## Мониторинг взаимодействий

### Метрики для отслеживания

1. **gRPC вызовы**:
   - Latency per method
   - Success/error rate
   - Request/response size

2. **RabbitMQ**:
   - Message publish rate
   - Consumer lag
   - Dead letter queue size

3. **Dependency health**:
   - Configuration service availability (критично!)
   - Database connection pool usage
   - Minio S3 availability

## Зависимости при запуске

### Критичные (сервис не запустится):
- **Configuration Service** - все сервисы
- **PostgreSQL** - все сервисы с БД
- **Users Service** - Identity service

### Необходимые (функциональность ограничена):
- **RabbitMQ** - события не доставляются
- **Redis** - кеш не работает (fallback к БД)
- **Minio** - загрузка файлов не работает

### Опциональные:
- **Navigator** - обнаружение серверов не работает
- **Notification** - email не отправляются
