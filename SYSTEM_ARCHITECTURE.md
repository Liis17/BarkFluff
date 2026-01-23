# Архитектура системы BarkFluff

## Содержание

1. [Обзор системы](#обзор-системы)
2. [Технологический стек](#технологический-стек)
3. [Архитектура микросервисов](#архитектура-микросервисов)
4. [Система аутентификации и безопасности](#система-аутентификации-и-безопасности)
5. [Хранение данных](#хранение-данных)
6. [Обмен данными и протоколы](#обмен-данными-и-протоколы)
7. [Клиентское приложение](#клиентское-приложение)
8. [Обработка файлов](#обработка-файлов)
9. [Real-time коммуникация](#real-time-коммуникация)
10. [Приватность и обработка данных пользователей](#приватность-и-обработка-данных-пользователей)

---

## Обзор системы

**BarkFluff** — это распределенная платформа для обмена сообщениями в реальном времени, построенная на микросервисной архитектуре. Система обеспечивает:

- **Приватный и групповой чат** с поддержкой текстовых сообщений, файлов, изображений, видео и голосовых сообщений
- **Управление пользователями** с профилями, аватарами, статусами и системой бейджей
- **Двухфакторную аутентификацию (2FA)** на основе TOTP
- **Real-time обновления** через gRPC streaming для мгновенной доставки сообщений
- **Отслеживание статуса онлайн** пользователей
- **Систему уведомлений** по email
- **Быструю аутентификацию по QR-коду**
- **Обнаружение серверов** через Navigator service

### Ключевые характеристики архитектуры

- **Распределенность**: 10+ независимых микросервисов
- **Протокол**: gRPC (HTTP/2) для межсервисной коммуникации
- **Асинхронность**: RabbitMQ с MassTransit для событий
- **Масштабируемость**: Каждый сервис масштабируется независимо
- **Отказоустойчивость**: Service discovery через Configuration service

---

## Технологический стек

### Backend

| Компонент | Технология | Версия |
|-----------|------------|--------|
| **Framework** | .NET | 9.0 |
| **API Protocol** | gRPC | HTTP/2 |
| **Message Broker** | RabbitMQ | Latest |
| **Message Framework** | MassTransit | Latest |
| **Database** | PostgreSQL | Latest |
| **ORM** | Entity Framework Core | 9.0 |
| **Cache** | Redis | Latest |
| **File Storage** | Minio (S3-compatible) | Latest |
| **Containerization** | Docker & Docker Compose | Latest |

### Frontend

| Компонент | Технология |
|-----------|------------|
| **Desktop Client** | WPF (Windows Presentation Foundation) |
| **UI Framework** | .NET 9.0 |
| **Local Database** | LiteDB (NoSQL) |
| **Image Processing** | ImageSharp |
| **Video Processing** | FFmpeg |
| **Audio Processing** | NAudio |

### Инфраструктура

- **Контейнеризация**: Docker, Docker Compose
- **Reverse Proxy**: Nginx (опционально)
- **Monitoring**: OpenTelemetry (OTLP metrics)
- **Logging**: Serilog

---

## Архитектура микросервисов

### Обзор сервисов

BarkFluff состоит из **12 микросервисов**, каждый из которых отвечает за конкретную бизнес-функцию:

| Сервис | Порт | Назначение |
|--------|------|------------|
| **Configuration** | 7003 | Централизованная конфигурация и service registry |
| **Beacon** | 7002 | Точка входа для клиентов, предоставляет информацию о сервисах |
| **Identity** | 7000 | Аутентификация, JWT токены, 2FA, сброс пароля, сессии |
| **Users** | 7001 | Профили пользователей, отношения, бейджи, аватары |
| **Messages** | 7007 | Чаты, сообщения, групповые чаты, статусы прочтения |
| **Files** | 7005 | Интеграция с Minio, загрузка/скачивание файлов, превью |
| **Updates** | 7015 | Real-time обновления через gRPC streaming |
| **Notification** | 7004 | Email уведомления через SMTP |
| **FastAuth** | 7008 | Быстрая аутентификация по QR-коду |
| **Navigator** | 7010 | Регистрация и обнаружение BarkFluff серверов |
| **Onliner** | 7009 | Отслеживание онлайн-статуса пользователей |
| **GrpcServer** | - | Общая библиотека для gRPC сервисов (XAuth, interceptors) |

### Детальное описание сервисов

#### Configuration Service (7003)

**Назначение**: Централизованное хранение конфигурации и service registry.

**Функции**:
- Хранит настройки для всех микросервисов
- Предоставляет service discovery (адреса других сервисов)
- Регистрирует все доступные сервисы в системе
- Управляет ServiceId enum для идентификации сервисов

**База данных**: PostgreSQL
- Таблица `Services`: ServiceId, Name, Host, Port, Status
- Таблица `Configurations`: Key-Value пары для настроек

**Взаимодействие**:
- Все сервисы при запуске обращаются к Configuration для получения своих настроек
- Использует метод `LoadConfiguration(ServiceId)` из `WebApplicationBuilderExtensions`

#### Beacon Service (7002)

**Назначение**: Точка входа для клиентов, предоставляет информацию о доступных сервисах.

**Функции**:
- Возвращает адреса всех микросервисов клиенту
- Предоставляет информацию о сервере (название, описание, цвета темы)
- Первый сервис, к которому подключается клиент

**API методы**:
```protobuf
service BeaconApi {
  rpc GetServerInfo(GetServerInfoRequest) returns (GetServerInfoResponse);
  rpc GetServiceEndpoints(Empty) returns (ServiceEndpointsResponse);
}
```

**Данные, передаваемые клиенту**:
- `ServerName`, `ServerDescription`
- `MainColor`, `LiteColor`, `HardColor` (цветовая схема)
- Список URL всех сервисов

#### Identity Service (7000)

**Назначение**: Управление аутентификацией и авторизацией пользователей.

**Функции**:
- Регистрация новых пользователей с email-подтверждением
- Аутентификация (логин/пароль)
- Генерация и обновление JWT токенов (Access + Refresh)
- Двухфакторная аутентификация (2FA) через TOTP (Google Authenticator)
- Сброс пароля через email
- Управление сессиями пользователей
- Хранение хешей паролей (BCrypt/Argon2)

**База данных**: PostgreSQL
- Таблица `Users`: UserId, Email, Username, PasswordHash, EmailVerified
- Таблица `TwoFactorAuth`: UserId, Secret (TOTP), IsEnabled
- Таблица `RefreshTokens`: Token, UserId, ExpiresAt, CreatedAt
- Таблица `PasswordResetTokens`: Token, UserId, ExpiresAt
- Таблица `Sessions`: SessionId, UserId, DeviceId, IpAddress, CreatedAt

**Типы токенов**:
1. **Access Token** (JWT, короткий срок жизни ~15 мин):
   - Содержит: UserId, Username, Role, DeviceId
   - Передается в заголовке `x-auth-token`
   - Используется для авторизации всех API запросов

2. **Refresh Token** (длительный срок жизни, дни/месяцы):
   - Используется для получения нового Access Token
   - Хранится в БД с привязкой к устройству

3. **Service Token** (для межсервисной коммуникации):
   - Специальный токен для связи между микросервисами
   - Более длительный срок жизни

**API методы**:
```protobuf
service IdentityApi {
  rpc CreateAccount(CreateAccountRequest) returns (CreateAccountResponse);
  rpc ConfirmEmail(ConfirmEmailRequest) returns (ConfirmEmailResponse);
  rpc Authorize(AuthorizeRequest) returns (AuthorizeResponse);
  rpc CreateToken(CreateTokenRequest) returns (CreateTokenResponse); // Refresh
  rpc Setup2FA(Setup2FARequest) returns (Setup2FAResponse);
  rpc Verify2FA(Verify2FARequest) returns (Verify2FAResponse);
  rpc RequestPasswordReset(PasswordResetRequest) returns (PasswordResetResponse);
  rpc ResetPassword(ResetPasswordRequest) returns (ResetPasswordResponse);
  rpc InvalidateSession(InvalidateSessionRequest) returns (Empty);
}
```

**Данные пользователя при регистрации**:
- Email (уникальный, требует подтверждения)
- Username (уникальный)
- FirstName, LastName
- Password (хешируется перед сохранением)

**Процесс 2FA**:
1. Пользователь включает 2FA
2. Генерируется TOTP Secret
3. Отображается QR-код для сканирования (Google Authenticator)
4. При последующих входах требуется OTP код (6 цифр)

#### Users Service (7001)

**Назначение**: Управление профилями пользователей и их взаимоотношениями.

**Функции**:
- Управление профилями (имя, фамилия, био, аватар)
- Система дружбы (запросы в друзья, принятие/отклонение)
- Черный список пользователей
- Система бейджей (достижения, роли)
- Поиск пользователей по имени/username
- Получение информации о пользователе по ID

**База данных**: PostgreSQL
- Таблица `UserProfiles`: UserId, FirstName, LastName, Bio, AvatarFileId, Status
- Таблица `Friendships`: UserId1, UserId2, Status (Pending/Accepted), CreatedAt
- Таблица `Blacklist`: UserId, BlockedUserId, CreatedAt
- Таблица `UserBadges`: UserId, BadgeId, AwardedAt
- Таблица `Badges`: BadgeId, Name, Description, IconUrl

**API методы**:
```protobuf
service UsersApi {
  rpc GetUserProfile(GetUserProfileRequest) returns (UserProfile);
  rpc UpdateProfile(UpdateProfileRequest) returns (UserProfile);
  rpc SearchUsers(SearchUsersRequest) returns (SearchUsersResponse);
  rpc SendFriendRequest(SendFriendRequestRequest) returns (Empty);
  rpc AcceptFriendRequest(AcceptFriendRequestRequest) returns (Empty);
  rpc GetFriends(GetFriendsRequest) returns (GetFriendsResponse);
  rpc BlockUser(BlockUserRequest) returns (Empty);
  rpc GetUserBadges(GetUserBadgesRequest) returns (GetUserBadgesResponse);
}
```

**Данные профиля**:
- FirstName, LastName
- Bio (текстовое описание)
- AvatarFileId (ссылка на файл в Files service)
- Status (текстовый статус пользователя)
- Badges (список значков)

#### Messages Service (7007)

**Назначение**: Управление чатами и сообщениями.

**Функции**:
- Создание и управление приватными чатами (1-на-1)
- Создание и управление групповыми чатами
- Отправка/получение сообщений
- Прикрепление файлов к сообщениям (через Files service)
- Отметки о прочтении (read receipts)
- История сообщений с пагинацией
- Удаление и редактирование сообщений
- Системные сообщения (уведомления о событиях в чате)

**База данных**: PostgreSQL
- Таблица `Chats`: ChatId, Title, IsGroup, CreatedAt, CreatedBy, PictureFileId
- Таблица `ChatMembers`: ChatId, UserId, Role (Admin/Member), JoinedAt
- Таблица `Messages`: MessageId, ChatId, SenderId, Text, SentAt, EditedAt, IsDeleted
- Таблица `MessageAttachments`: MessageId, FileId, Type, PreviewFileId, FileName, Size
- Таблица `MessageReadReceipts`: MessageId, UserId, ReadAt

**API методы**:
```protobuf
service MessagesApi {
  rpc ListChats(ListChatsRequest) returns (ListChatsResponse);
  rpc CreateChat(CreateChatRequest) returns (Chat);
  rpc GetChatMessages(GetChatMessagesRequest) returns (GetChatMessagesResponse);
  rpc SendMessage(SendMessageRequest) returns (SendMessageResponse);
  rpc EditMessage(EditMessageRequest) returns (Message);
  rpc DeleteMessage(DeleteMessageRequest) returns (Empty);
  rpc MarkMessagesAsRead(MarkMessagesAsReadRequest) returns (Empty);
  rpc AddChatMembers(AddChatMembersRequest) returns (Empty);
  rpc RemoveChatMember(RemoveChatMemberRequest) returns (Empty);
}
```

**Структура сообщения**:
```protobuf
message Message {
  int64 MessageId = 1;
  int64 ChatId = 2;
  int64 SenderId = 3;
  MessageContent Content = 4;
  google.protobuf.Timestamp SentAt = 5;
  google.protobuf.Timestamp EditedAt = 6;
  repeated int64 ReadBy = 7; // Список UserId прочитавших
  bool IsSystemMessage = 8;
}

message MessageContent {
  string Text = 1;
  repeated Attachment Attachments = 2;
}

message Attachment {
  string FileId = 1;
  AttachmentType Type = 2; // Image/Video/Document/Audio/Gif
  string PreviewFileId = 3;
  string FileName = 4;
  int64 Size = 5;
}
```

**События RabbitMQ** (публикуемые Messages service):
- `MessageSentEvent`: Новое сообщение отправлено
- `MessageEditedEvent`: Сообщение отредактировано
- `MessageDeletedEvent`: Сообщение удалено
- `MessageReadEvent`: Сообщение прочитано пользователем

Эти события потребляются **Updates service** для real-time доставки клиентам.

#### Files Service (7005)

**Назначение**: Управление загрузкой, хранением и доступом к файлам.

**Функции**:
- Интеграция с Minio (S3-совместимое хранилище)
- Генерация pre-signed URL для загрузки файлов
- Генерация pre-signed URL для скачивания файлов
- Дедупликация файлов по SHA256 хешу
- Создание превью для изображений и видео
- Управление квотами хранилища пользователей
- Проверка типов файлов и размеров

**База данных**: PostgreSQL
- Таблица `Files`: FileId, Hash (SHA256), Size, MimeType, UploadedBy, UploadedAt, MinioKey
- Таблица `FileMetadata`: FileId, Width, Height, Duration (для видео)
- Таблица `UserStorageQuotas`: UserId, UsedBytes, LimitBytes

**Minio (S3) структура**:
```
Buckets:
- user-avatars/           # Аватары пользователей
- message-attachments/    # Вложения в сообщения
  ├── images/
  ├── videos/
  ├── documents/
  ├── audio/
  └── gifs/
- previews/               # Превью для файлов
```

**API методы**:
```protobuf
service FilesApi {
  rpc GetUploadUrl(GetUploadUrlRequest) returns (GetUploadUrlResponse);
  rpc GetDownloadUrl(GetDownloadUrlRequest) returns (GetDownloadUrlResponse);
  rpc CheckFileHash(CheckFileHashRequest) returns (CheckFileHashResponse);
  rpc GetFileInfo(GetFileInfoRequest) returns (FileInfo);
  rpc DeleteFile(DeleteFileRequest) returns (Empty);
  rpc GetUserStorageInfo(GetUserStorageInfoRequest) returns (UserStorageInfo);
}
```

**Процесс загрузки файла**:
1. Клиент вычисляет SHA256 хеш файла
2. Клиент вызывает `CheckFileHash()` - если файл уже существует, возвращается FileId (дедупликация)
3. Если файл новый, клиент запрашивает `GetUploadUrl()`
4. Files service генерирует pre-signed URL в Minio (срок действия 15 мин)
5. Клиент загружает файл напрямую в Minio по pre-signed URL
6. Files service сохраняет метаданные файла в БД

**Типы файлов**:
- `MessageAttachmentImage`: JPEG, PNG, WebP (макс 10 МБ)
- `MessageAttachmentVideo`: MP4, AVI, MOV (макс 100 МБ)
- `MessageAttachmentDocument`: PDF, DOCX, XLSX (макс 50 МБ)
- `MessageAttachmentAudio`: MP3, WAV, OGG (макс 20 МБ)
- `MessageAttachmentGif`: GIF (макс 10 МБ)
- `UserProfilePicture`: JPEG, PNG (макс 5 МБ)

#### Updates Service (7015)

**Назначение**: Real-time доставка обновлений клиентам через gRPC streaming.

**Функции**:
- Стриминг новых сообщений клиентам
- Стриминг read receipts (отметок о прочтении)
- Подписка на обновления конкретных чатов
- Потребление событий из RabbitMQ
- Управление активными стримами

**Архитектура**:
```
RabbitMQ (MassTransit)
  ├── Queue: message-sent-events
  ├── Queue: message-edited-events
  ├── Queue: message-deleted-events
  └── Queue: message-read-events
       ↓
Updates Service (Consumer)
  ├── Держит открытые gRPC streams к клиентам
  ├── Фильтрует события по UserId
  └── Пушит события в активные streams
       ↓
Client (gRPC stream receiver)
```

**API методы**:
```protobuf
service UpdatesApi {
  rpc SubscribeNewMessages(Empty) returns (stream NewMessageEvent);
  rpc SubscribeMessagesRead(Empty) returns (stream MessageReadEvent);
}
```

**Структура события**:
```protobuf
message NewMessageEvent {
  int64 ChatId = 1;
  Message Message = 2;
}

message MessageReadEvent {
  int64 ChatId = 1;
  int64 MessageId = 2;
  repeated int64 NewReadBy = 3;
}
```

**Характеристики**:
- **Протокол**: gRPC Server Streaming (HTTP/2)
- **Переподключение**: Автоматическое с экспоненциальной задержкой (2s → 30s)
- **Heartbeat**: Ping каждые 30 секунд для поддержки соединения
- **Дедупликация**: Клиент отслеживает последние 1000 MessageId

#### Onliner Service (7009)

**Назначение**: Отслеживание онлайн-статуса пользователей в реальном времени.

**Функции**:
- Отслеживание статуса онлайн/оффлайн
- Время последней активности (last seen)
- Ping механизм для поддержания статуса
- Стриминг статусов конкретных пользователей

**База данных**: Redis (in-memory)
```
Key: user:{userId}:status
Value: {
  "IsOnline": true,
  "LastSeenAt": "2026-01-23T12:00:00Z",
  "DeviceId": "device-123"
}
TTL: 5 минут (автоматически оффлайн при истечении)
```

**API методы**:
```protobuf
service OnlinerApi {
  rpc Ping(PingRequest) returns (Empty);
  rpc SubscribeToOnlineStatus(SubscribeOnlineStatusRequest) returns (stream UserOnlineStatus);
  rpc GetOnlineStatus(GetOnlineStatusRequest) returns (UserOnlineStatus);
}
```

**Процесс работы**:
1. Клиент отправляет `Ping()` каждые 3 секунды
2. Onliner обновляет статус в Redis с TTL 5 минут
3. Другие клиенты подписываются на `SubscribeToOnlineStatus(userIds[])`
4. При изменении статуса Onliner пушит обновление в stream

**Данные статуса**:
```protobuf
message UserOnlineStatus {
  int64 UserId = 1;
  bool IsOnline = 2;
  google.protobuf.Timestamp LastSeenAt = 3;
}
```

#### Notification Service (7004)

**Назначение**: Отправка email уведомлений пользователям.

**Функции**:
- Email при регистрации (код подтверждения)
- Email при сбросе пароля (ссылка восстановления)
- Email при входе с нового устройства
- Email при включении 2FA

**База данных**: PostgreSQL
- Таблица `NotificationQueue`: NotificationId, UserId, Type, Status, Payload, CreatedAt
- Таблица `NotificationLog`: NotificationId, SentAt, Result, ErrorMessage

**SMTP конфигурация** (из docker-compose):
```yaml
SMTP:
  Host: smtp.gmail.com
  Port: 587
  Username: noreply@barkfluff.com
  Password: <app-password>
  UseTLS: true
```

**API методы**:
```protobuf
service NotificationApi {
  rpc SendEmailVerification(SendEmailRequest) returns (Empty);
  rpc SendPasswordReset(SendPasswordResetRequest) returns (Empty);
  rpc SendSecurityAlert(SendSecurityAlertRequest) returns (Empty);
}
```

**Шаблоны писем**:
- Верификация email: содержит 6-значный код
- Сброс пароля: содержит ссылку с токеном (срок 1 час)
- Оповещение о безопасности: информация об устройстве и IP

**Потребление событий из RabbitMQ**:
- `UserRegisteredEvent` → отправка verification email
- `PasswordResetRequestedEvent` → отправка reset email
- `NewDeviceLoginEvent` → отправка security alert

#### FastAuth Service (7008)

**Назначение**: Быстрая аутентификация по QR-коду.

**Функции**:
- Генерация QR-кода для входа
- Подтверждение входа с авторизованного устройства
- Короткий срок действия QR-кода (2 минуты)

**База данных**: Redis
```
Key: fastauth:{sessionId}
Value: {
  "SessionId": "uuid",
  "Status": "pending|approved|rejected",
  "UserId": null|123,
  "CreatedAt": "timestamp"
}
TTL: 2 минуты
```

**API методы**:
```protobuf
service FastAuthApi {
  rpc GenerateQRSession(Empty) returns (QRSessionResponse);
  rpc ApproveQRSession(ApproveQRSessionRequest) returns (Empty);
  rpc PollQRSession(PollQRSessionRequest) returns (QRSessionStatus);
}
```

**Процесс**:
1. Неавторизованный клиент вызывает `GenerateQRSession()` → получает SessionId
2. Клиент отображает QR-код с SessionId
3. Авторизованный клиент сканирует QR → вызывает `ApproveQRSession(SessionId)`
4. Неавторизованный клиент поллит `PollQRSession()` каждые 2 секунды
5. При одобрении FastAuth генерирует Refresh Token
6. Клиент получает токен и авторизуется

#### Navigator Service (7010)

**Назначение**: Регистрация и обнаружение публичных BarkFluff серверов.

**Функции**:
- Глобальный реестр BarkFluff серверов
- Регистрация нового сервера
- Поиск серверов по имени/тегам
- Heartbeat для проверки доступности серверов

**База данных**: PostgreSQL
- Таблица `Servers`: ServerId, Name, Host, Port, Description, Tags, RegisteredAt
- Таблица `ServerHeartbeats`: ServerId, LastPing, IsOnline

**API методы**:
```protobuf
service NavigatorApi {
  rpc RegisterServer(RegisterServerRequest) returns (ServerInfo);
  rpc SearchServers(SearchServersRequest) returns (SearchServersResponse);
  rpc GetServerInfo(GetServerInfoRequest) returns (ServerInfo);
  rpc Heartbeat(HeartbeatRequest) returns (Empty);
}
```

**Данные сервера**:
```protobuf
message ServerInfo {
  string ServerId = 1;
  string Name = 2;
  string Host = 3;
  int32 Port = 4;
  string Description = 5;
  repeated string Tags = 6;
  bool IsOnline = 7;
  int32 UserCount = 8;
}
```

**Публичный Navigator**:
- Хостится на `navigator.barkfluff.com:64646`
- Доступен всем BarkFluff клиентам для поиска серверов

---

## Система аутентификации и безопасности

### XAuth - Централизованная система авторизации

BarkFluff использует систему **XAuth** (из библиотеки `BarkFluff.GrpcServer`), которая обеспечивает единый механизм аутентификации для всех микросервисов.

#### Компоненты XAuth

**1. Серверная сторона (Middleware)**

```csharp
// В Program.cs каждого сервиса
builder.Services.AddXAuth(builder.Configuration);
app.UseXAuth();
```

**XAuth Middleware** выполняет:
- Извлечение JWT токена из заголовка `x-auth-token`
- Валидация токена (подпись, срок действия)
- Парсинг claims (UserId, Username, Role, TokenType)
- Добавление `ClaimsPrincipal` в HTTP контекст
- Авторизация на основе политик

**2. Политики авторизации**

```csharp
[Authorize(Policy = nameof(TokenType.User))]
public override Task<Response> Method(Request request, ServerCallContext context)
```

Две основные политики:
- **`TokenType.User`**: Требует User или Service токен (для пользовательских API)
- **`TokenType.Service`**: Требует только Service токен (для межсервисной коммуникации)

**3. Клиентские interceptors**

Все gRPC клиенты используют набор interceptors для добавления метаданных:

```csharp
builder.Services.AddGrpcClient<SomeApi.SomeApiClient>(o => {
    o.Address = new Uri(serviceUrl);
})
.AddInterceptor(() => new JwtClientInterceptor(accessToken))
.AddInterceptor(() => new XDeviceClientInterceptor(deviceId, deviceName))
.AddInterceptor(() => new XOsClientInterceptor(osName, osVersion))
.AddInterceptor(() => new XIpClientInterceptor(ipAddress))
.AddInterceptor(() => new XAppClientInterceptor(appName, appVersion))
.AddInterceptor(() => new ExceptionClientInterceptor());
```

### Заголовки запросов (Metadata Headers)

Каждый gRPC запрос от клиента содержит следующие заголовки:

| Заголовок | Пример значения | Описание |
|-----------|-----------------|----------|
| `x-auth-token` | `eyJhbGciOiJIUzI1NiIs...` | JWT Access Token |
| `x-device-id` | `550e8400-e29b-41d4-a716-446655440000` | Уникальный ID устройства (UUID) |
| `x-device-name` | `John's Desktop` | Имя устройства пользователя |
| `x-ip` | `192.168.1.100` | IP-адрес клиента |
| `x-os` | `Windows 11` | Операционная система |
| `x-app-name` | `BarkFluff.Client.WPF` | Название приложения |
| `x-app-version` | `1.0.0` | Версия приложения |

**Назначение метаданных**:
- **Аудит**: Логирование действий пользователя с контекстом устройства
- **Безопасность**: Обнаружение подозрительной активности (новое устройство, необычный IP)
- **Сессии**: Привязка Refresh Token к конкретному устройству
- **Уведомления**: Информирование пользователя о входе с нового устройства

### JWT токены - структура

**Access Token (пример payload)**:
```json
{
  "sub": "12345",                    // UserId
  "username": "john_doe",
  "role": "User",
  "token_type": "User",
  "device_id": "550e8400-e29b...",
  "iat": 1706001600,                 // Issued at
  "exp": 1706002500                  // Expires at (15 мин)
}
```

**Параметры токена**:
- **Алгоритм**: HS256 (HMAC-SHA256)
- **Secret**: Хранится в конфигурации Identity service
- **Срок жизни Access**: 15 минут
- **Срок жизни Refresh**: 30 дней (настраиваемый)

### Процесс аутентификации (полный flow)

#### 1. Регистрация

```
Client                    Identity Service          Notification Service
  |                              |                           |
  |--CreateAccount()----------->|                           |
  |  (email, username, password) |                           |
  |                              |---Hash password---------->|
  |                              |---Save user to DB-------->|
  |                              |---Generate verify code--->|
  |<----VerifyCode---------------|                           |
  |  (6-digit code)              |                           |
  |                              |---Publish UserRegistered->|
  |                              |                           |---Send email-->
  |                              |                           |
  |--ConfirmEmail(code)--------->|                           |
  |                              |---Verify code------------>|
  |<----RefreshToken-------------|                           |
```

**Данные, собираемые при регистрации**:
- Email (для входа и восстановления)
- Username (для входа и отображения)
- FirstName, LastName (отображение в профиле)
- Password (хешируется BCrypt/Argon2, никогда не хранится в открытом виде)

#### 2. Вход (Login)

```
Client                    Identity Service
  |                              |
  |--Authorize()---------------->|
  |  (email/username, password)  |
  |  + device metadata           |
  |                              |---Verify password hash--->|
  |                              |---Check 2FA enabled?----->|
  |<----getMeOtpCode-------------|  (if 2FA: true)
  |  (true)                      |
  |                              |
  |--Authorize(with OTP)-------->|
  |  (email, password, otpCode)  |
  |                              |---Verify TOTP code------->|
  |<----AccessToken + RefreshToken|
  |  + UserId, Username          |
  |                              |---Create Session DB------->|
  |                              |  (SessionId, UserId,       |
  |                              |   DeviceId, IP)            |
```

**Данные сессии (хранятся в Identity DB)**:
- SessionId (UUID)
- UserId
- DeviceId (из заголовка)
- DeviceName
- IpAddress
- OS, AppName, AppVersion
- CreatedAt, LastActivityAt

#### 3. Обновление токена (Token Refresh)

```
Client                    Identity Service
  |                              |
  |--CreateToken()-------------->|
  |  (RefreshToken)              |
  |  + device metadata           |
  |                              |---Validate RefreshToken-->|
  |                              |---Check DeviceId match--->|
  |<----New AccessToken----------|
  |                              |---Update Session.LastActivity|
```

**Безопасность Refresh Token**:
- Привязан к конкретному DeviceId (защита от кражи токена)
- Одноразовое использование: после обновления старый Refresh становится невалидным (опционально)
- Ротация токенов: каждое обновление генерирует новый Refresh Token

#### 4. Двухфакторная аутентификация (2FA)

**Настройка 2FA**:
```
Client                    Identity Service
  |                              |
  |--Setup2FA()----------------->|
  |                              |---Generate TOTP Secret--->|
  |<----Secret + QR URL----------|
  |  (otpauth://totp/...)        |
  |                              |
  | [User scans QR in Google Authenticator]
  |                              |
  |--Verify2FA(otpCode)--------->|
  |                              |---Verify code with Secret>|
  |<----Success------------------|
  |                              |---Enable2FA in DB-------->|
```

**TOTP параметры**:
- Алгоритм: SHA1
- Период: 30 секунд
- Длина кода: 6 цифр
- Secret: Base32 encoded (хранится в БД)

**Вход с 2FA**:
1. Пользователь вводит email + password
2. Сервер проверяет, включен ли 2FA
3. Если да, возвращает `getMeOtpCode: true`
4. Клиент запрашивает OTP код (из Google Authenticator)
5. Клиент повторяет запрос с `otpCode`
6. Сервер проверяет OTP (текущий код + 1 код назад/вперед для учета рассинхронизации времени)

### Безопасность паролей

**Хеширование**:
- Алгоритм: BCrypt с cost factor 12 (или Argon2id)
- Salt: Уникальный для каждого пользователя (генерируется автоматически)
- Никогда не логируется и не передается в открытом виде

**Политика паролей** (настраиваемая):
- Минимальная длина: 8 символов
- Требования: как минимум 1 цифра, 1 заглавная буква
- Проверка на компрометацию (опционально через Have I Been Pwned API)

**Сброс пароля**:
1. Пользователь запрашивает сброс через email
2. Identity генерирует одноразовый токен (UUID) со сроком 1 час
3. Notification отправляет email со ссылкой
4. Пользователь переходит по ссылке и вводит новый пароль
5. Identity проверяет токен, хеширует новый пароль, инвалидирует все сессии

### Межсервисная аутентификация

**Service Tokens**:
- Каждый микросервис имеет свой Service Token для вызова других сервисов
- Хранится в конфигурации (Configuration service)
- Никогда не истекает (или очень длинный TTL)
- Содержит claim `token_type: Service`

**Пример**:
```csharp
// Messages service вызывает Users service
builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o => {
    o.Address = new Uri(usersServiceUrl);
})
.AddInterceptor(() => new JwtClientInterceptor(messagesServiceToken));
```

**Защита от несанкционированного доступа**:
- Service Token имеет роль `Service` в claims
- API методы, доступные только сервисам, защищены `[Authorize(Policy = nameof(TokenType.Service))]`
- Клиентские User токены не могут вызывать эти методы

### Логирование и аудит

**Логируемые события** (в каждом сервисе):
- Успешные и неудачные попытки входа (с IP, DeviceId)
- Изменения пароля
- Включение/отключение 2FA
- Доступ к чувствительным данным (профили других пользователей)
- Изменения в настройках безопасности

**Формат логов** (Serilog):
```json
{
  "Timestamp": "2026-01-23T12:00:00Z",
  "Level": "Information",
  "MessageTemplate": "User {UserId} logged in from device {DeviceId}",
  "Properties": {
    "UserId": 12345,
    "Username": "john_doe",
    "DeviceId": "550e8400-...",
    "IpAddress": "192.168.1.100",
    "OS": "Windows 11",
    "Success": true
  }
}
```

---

## Хранение данных

### Базы данных PostgreSQL

Каждый микросервис имеет **собственную базу данных** (Database-per-Service pattern):

| Сервис | База данных | Основные таблицы |
|--------|-------------|------------------|
| **Identity** | `barkfluff_identity` | Users, RefreshTokens, TwoFactorAuth, Sessions, PasswordResetTokens |
| **Users** | `barkfluff_users` | UserProfiles, Friendships, Blacklist, UserBadges, Badges |
| **Messages** | `barkfluff_messages` | Chats, ChatMembers, Messages, MessageAttachments, MessageReadReceipts |
| **Files** | `barkfluff_files` | Files, FileMetadata, UserStorageQuotas |
| **Configuration** | `barkfluff_config` | Services, Configurations |
| **Navigator** | `barkfluff_navigator` | Servers, ServerHeartbeats |
| **Notification** | `barkfluff_notification` | NotificationQueue, NotificationLog |

**Entity Framework Core**:
- Code-First подход с миграциями
- Миграции применяются автоматически при старте сервиса (в `Program.cs`)
- Связи между сервисами только через API, не через прямые JOIN запросы

**Пример миграции**:
```bash
# Создание миграции
dotnet ef migrations add AddTwoFactorAuth --project Backend/BarkFluff.Identity

# Применение
dotnet ef database update --project Backend/BarkFluff.Identity
```

### Структура основных таблиц

#### Identity.Users
```sql
CREATE TABLE Users (
    UserId BIGSERIAL PRIMARY KEY,
    Email VARCHAR(255) UNIQUE NOT NULL,
    Username VARCHAR(50) UNIQUE NOT NULL,
    PasswordHash VARCHAR(255) NOT NULL,
    EmailVerified BOOLEAN DEFAULT FALSE,
    CreatedAt TIMESTAMP NOT NULL,
    UpdatedAt TIMESTAMP
);

CREATE INDEX idx_users_email ON Users(Email);
CREATE INDEX idx_users_username ON Users(Username);
```

#### Identity.RefreshTokens
```sql
CREATE TABLE RefreshTokens (
    TokenId BIGSERIAL PRIMARY KEY,
    Token VARCHAR(500) UNIQUE NOT NULL,
    UserId BIGINT NOT NULL REFERENCES Users(UserId),
    DeviceId UUID NOT NULL,
    ExpiresAt TIMESTAMP NOT NULL,
    CreatedAt TIMESTAMP NOT NULL,
    RevokedAt TIMESTAMP,
    IsRevoked BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_refresh_tokens_user ON RefreshTokens(UserId);
CREATE INDEX idx_refresh_tokens_device ON RefreshTokens(DeviceId);
```

#### Messages.Messages
```sql
CREATE TABLE Messages (
    MessageId BIGSERIAL PRIMARY KEY,
    ChatId BIGINT NOT NULL REFERENCES Chats(ChatId),
    SenderId BIGINT NOT NULL,  -- UserId из Identity service
    Text TEXT,
    SentAt TIMESTAMP NOT NULL,
    EditedAt TIMESTAMP,
    IsDeleted BOOLEAN DEFAULT FALSE,
    IsSystemMessage BOOLEAN DEFAULT FALSE
);

CREATE INDEX idx_messages_chat ON Messages(ChatId, SentAt DESC);
CREATE INDEX idx_messages_sender ON Messages(SenderId);
```

#### Messages.MessageReadReceipts
```sql
CREATE TABLE MessageReadReceipts (
    MessageId BIGINT NOT NULL REFERENCES Messages(MessageId),
    UserId BIGINT NOT NULL,
    ReadAt TIMESTAMP NOT NULL,
    PRIMARY KEY (MessageId, UserId)
);

CREATE INDEX idx_read_receipts_user ON MessageReadReceipts(UserId);
```

#### Files.Files
```sql
CREATE TABLE Files (
    FileId UUID PRIMARY KEY,
    Hash VARCHAR(64) UNIQUE NOT NULL,  -- SHA256
    Size BIGINT NOT NULL,
    MimeType VARCHAR(100),
    UploadedBy BIGINT NOT NULL,  -- UserId
    UploadedAt TIMESTAMP NOT NULL,
    MinioKey VARCHAR(500) NOT NULL  -- Путь в Minio
);

CREATE INDEX idx_files_hash ON Files(Hash);
CREATE INDEX idx_files_uploader ON Files(UploadedBy);
```

### Redis (In-Memory Cache)

**Использование**:
- **Onliner Service**: Хранение онлайн-статусов пользователей
- **FastAuth Service**: Временные QR-сессии
- **Configuration Service** (опционально): Кеширование конфигов

**Структура данных в Onliner**:
```
Key: user:12345:status
Value: {"IsOnline": true, "LastSeenAt": "2026-01-23T12:00:00Z", "DeviceId": "..."}
TTL: 300 секунд (5 минут)

Операции:
- SET с TTL при каждом Ping
- GET для получения статуса
- EXPIRE обновляется при активности
```

**Структура данных в FastAuth**:
```
Key: fastauth:550e8400-e29b-41d4-a716-446655440000
Value: {"Status": "pending", "UserId": null, "CreatedAt": "..."}
TTL: 120 секунд (2 минуты)

Операции:
- SET при GenerateQRSession
- GET+UPDATE при ApproveQRSession
- GET при PollQRSession
```

### Minio (S3-Compatible Object Storage)

**Buckets**:
```
barkfluff-storage/
├── user-avatars/
│   └── {userId}/{fileId}.jpg
├── message-attachments/
│   ├── images/{chatId}/{fileId}.jpg
│   ├── videos/{chatId}/{fileId}.mp4
│   ├── documents/{chatId}/{fileId}.pdf
│   ├── audio/{chatId}/{fileId}.mp3
│   └── gifs/{chatId}/{fileId}.gif
└── previews/
    ├── video-thumbnails/{fileId}.jpg
    └── image-thumbnails/{fileId}.jpg
```

**Конфигурация** (docker-compose):
```yaml
minio:
  image: minio/minio
  environment:
    MINIO_ROOT_USER: admin
    MINIO_ROOT_PASSWORD: <secure-password>
  volumes:
    - minio_data:/data
  command: server /data --console-address ":9001"
```

**Доступ**:
- **Internal**: `http://minio:9000` (для сервисов в Docker сети)
- **Console**: `http://localhost:9001` (веб-интерфейс)

**Pre-signed URLs**:
- Срок действия upload URL: 15 минут
- Срок действия download URL: 1 час
- Генерируются Files service с использованием Minio SDK

### Клиентское хранилище (WPF Client)

**Структура директории `datas/`**:
```
datas/
├── GlobalParam.json          # Зашифрованная конфигурация (AES-256)
├── cache.db                  # LiteDB: Кеш сообщений
├── file_cache.db             # LiteDB: Метаданные файлов
└── cache/                    # Файловый кеш
    ├── avatars/
    ├── images/
    ├── videos/
    ├── gifs/
    └── documents/
```

**1. GlobalParam.json (Зашифрованный)**

**Содержимое** (после расшифровки):
```json
{
  "UserId": 12345,
  "UserName": "john_doe",
  "Email": "john@example.com",
  "AccessToken": {
    "Value": "eyJhbGc...",
    "ExpiresAt": "2026-01-23T12:15:00Z"
  },
  "RefreshToken": {
    "Value": "550e8400-e29b-41d4-a716-446655440000"
  },
  "DeviceId": "abcd-1234-efgh-5678",
  "DeviceName": "John's Desktop",
  "PinCode": "1234",
  "ServerName": "Official BarkFluff Server",
  "SocketBeacon": "http://localhost:7002",
  "SocketIdentity": "http://localhost:7000",
  "SocketUsers": "http://localhost:7001",
  "SocketMessages": "http://localhost:7007",
  "SocketFiles": "http://localhost:7005",
  "SocketUpdates": "http://localhost:7015",
  "SocketOnliner": "http://localhost:7009",
  "SocketNavigator": "navigator.barkfluff.com:64646",
  "Colors": {
    "MainHex": "#2196F3",
    "LiteHex": "#BBDEFB",
    "HardHex": "#1565C0"
  }
}
```

**Шифрование**:
```
File Layout: [Salt(16 bytes)] + [IV(16 bytes)] + [AES-encrypted JSON]

Алгоритм: AES-256-CBC
Key Derivation: PBKDF2(PIN, salt, 100000 iterations, SHA256)
PIN: 4-значный код пользователя
```

**2. cache.db (LiteDB - Сообщения)**

**Коллекция `messages`**:
```json
{
  "_id": 1,
  "MessageId": 12345,
  "ChatId": 67,
  "SenderId": 123,
  "Text": "Hello!",
  "SentAt": "2026-01-23T12:00:00Z",
  "Attachments": [
    {
      "FileId": "uuid-...",
      "Type": "Image",
      "FileName": "photo.jpg",
      "Size": 1024000
    }
  ],
  "ReadBy": [123, 456],
  "IsSystemMessage": false
}
```

**Коллекция `chats`**:
```json
{
  "_id": 1,
  "ChatId": 67,
  "Title": "John & Alice",
  "ChatType": "Private",
  "Picture": "uuid-avatar",
  "LastMessageId": 12345,
  "LastMessageText": "Hello!",
  "LastMessageTime": "2026-01-23T12:00:00Z"
}
```

**Индексы**:
```csharp
collection.EnsureIndex("ChatId");
collection.EnsureIndex("MessageId", unique: true);
```

**3. file_cache.db (LiteDB - Файлы)**

**Коллекция `cached_files`**:
```json
{
  "_id": 1,
  "FileId": "uuid-...",
  "Hash": "sha256-...",
  "FileType": "Image",
  "LocalPath": "datas/cache/images/uuid-....jpg",
  "CachedAt": "2026-01-23T12:00:00Z",
  "Size": 1024000
}
```

**Индексы**:
```csharp
collection.EnsureIndex("FileId", unique: true);
collection.EnsureIndex("Hash");
```

### Политики хранения данных

#### Серверная сторона

**Messages**:
- Хранятся бессрочно (или до удаления пользователем)
- Soft delete: `IsDeleted = true` (сообщения не удаляются физически сразу)
- Cleanup job: Удаление помеченных сообщений через 30 дней

**Files**:
- Хранятся бессрочно
- Дедупликация по hash: один файл = одна запись в storage
- Удаление файла из Minio только если нет ссылок из Messages

**Sessions**:
- TTL: 30 дней неактивности
- Cleanup job: Удаление просроченных сессий ежедневно

**Read Receipts**:
- Хранятся бессрочно для истории

#### Клиентская сторона

**Message Cache**:
- Хранится локально бессрочно
- Пользователь может очистить вручную (Settings → Clear cache)

**File Cache**:
- Хранится локально бессрочно
- Пользователь может очистить вручную
- Lazy loading: файлы загружаются по требованию

**Encrypted Config**:
- Перезаписывается при каждом изменении
- Backup не создается

---

## Обмен данными и протоколы

### gRPC (HTTP/2)

BarkFluff использует **gRPC** для всех коммуникаций между клиентом и сервером, а также между микросервисами.

#### Преимущества gRPC

- **Производительность**: Binary протокол (Protocol Buffers) быстрее JSON
- **Типобезопасность**: Строго типизированные контракты
- **Streaming**: Поддержка server/client/bidirectional streaming
- **HTTP/2**: Multiplexing, header compression, server push
- **Code Generation**: Автоматическая генерация клиентов и серверов

#### Protocol Buffers (.proto файлы)

Все контракты определены в `Shared/BarkFluff.Proto/`:

**Структура**:
```
Shared/BarkFluff.Proto/
├── beacon_api.proto          # Beacon service API
├── identity_api.proto        # Identity service API
├── users_api.proto           # Users service API
├── messages_api.proto        # Messages service API
├── files_api.proto           # Files service API
├── updates_api.proto         # Updates service API (streaming)
├── onliner_api.proto         # Onliner service API
├── notification_api.proto    # Notification service API
├── fastauth_api.proto        # FastAuth service API
├── navigator_api.proto       # Navigator service API
└── common.proto              # Общие типы и модели
```

**Пример proto файла** (messages_api.proto):
```protobuf
syntax = "proto3";

package barkfluff.messages;

import "google/protobuf/timestamp.proto";
import "common.proto";

service MessagesApi {
  rpc ListChats(ListChatsRequest) returns (ListChatsResponse);
  rpc SendMessage(SendMessageRequest) returns (SendMessageResponse);
  rpc GetChatMessages(GetChatMessagesRequest) returns (GetChatMessagesResponse);
  rpc MarkMessagesAsRead(MarkMessagesAsReadRequest) returns (google.protobuf.Empty);
}

message SendMessageRequest {
  oneof recipient {
    int64 chat_id = 1;
    int64 user_id = 2;  // Для создания нового чата
  }
  MessageContent content = 3;
}

message MessageContent {
  string text = 1;
  repeated Attachment attachments = 2;
}

message Attachment {
  string file_id = 1;
  AttachmentType type = 2;
  string preview_file_id = 3;
  string file_name = 4;
  int64 size = 5;
}

enum AttachmentType {
  IMAGE = 0;
  VIDEO = 1;
  DOCUMENT = 2;
  AUDIO = 3;
  GIF = 4;
}
```

**Генерация кода**:
```csharp
// В .csproj сервиса
<ItemGroup>
  <Protobuf Include="..\..\Shared\BarkFluff.Proto\messages_api.proto" GrpcServices="Server" />
</ItemGroup>

// В .csproj клиента
<ItemGroup>
  <Protobuf Include="..\..\Shared\BarkFluff.Proto\messages_api.proto" GrpcServices="Client" />
</ItemGroup>
```

При сборке генерируются:
- **Server**: `MessagesApi.MessagesApiBase` (базовый класс для реализации)
- **Client**: `MessagesApi.MessagesApiClient` (клиент для вызовов)

#### gRPC Streaming

**Server Streaming** (Updates Service):
```protobuf
service UpdatesApi {
  // Клиент отправляет один запрос, сервер отправляет stream сообщений
  rpc SubscribeNewMessages(google.protobuf.Empty) returns (stream NewMessageEvent);
}
```

**Реализация на сервере**:
```csharp
public override async Task SubscribeNewMessages(
    Empty request,
    IServerStreamWriter<NewMessageEvent> responseStream,
    ServerCallContext context)
{
    var userId = context.GetUserId(); // Из JWT токена

    // Подписываемся на события из RabbitMQ
    await foreach (var messageEvent in _eventStream.SubscribeAsync(userId))
    {
        if (context.CancellationToken.IsCancellationRequested)
            break;

        await responseStream.WriteAsync(messageEvent);
    }
}
```

**Потребление на клиенте**:
```csharp
var call = updatesClient.SubscribeNewMessages(new Empty());

await foreach (var messageEvent in call.ResponseStream.ReadAllAsync())
{
    // Обработка нового сообщения
    OnNewMessage?.Invoke(messageEvent.Message);
}
```

#### Interceptors (Middleware для gRPC)

**Серверные interceptors** (в каждом сервисе):

1. **XAuthInterceptor** (встроен в `AddXAuth()`):
   - Извлекает JWT из `x-auth-token`
   - Валидирует токен
   - Добавляет ClaimsPrincipal в контекст

2. **ServerExceptionInterceptor**:
   - Перехватывает исключения
   - Конвертирует `BaseGrpcException` в gRPC статус-коды
   - Логирует ошибки

```csharp
public class ServerExceptionInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (BaseGrpcException ex)
        {
            throw new RpcException(new Status(ex.StatusCode, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in gRPC call");
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }
}
```

**Клиентские interceptors**:

1. **JwtClientInterceptor**: Добавляет `x-auth-token` заголовок
2. **XDeviceClientInterceptor**: Добавляет `x-device-id`, `x-device-name`
3. **XOsClientInterceptor**: Добавляет `x-os`
4. **XIpClientInterceptor**: Добавляет `x-ip`
5. **XAppClientInterceptor**: Добавляет `x-app-name`, `x-app-version`
6. **ExceptionClientInterceptor**: Обрабатывает ошибки от сервера

```csharp
public class JwtClientInterceptor : Interceptor
{
    private readonly string _token;

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var headers = new Metadata();
        if (!string.IsNullOrEmpty(_token))
        {
            headers.Add("x-auth-token", _token);
        }

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method,
            context.Host,
            context.Options.WithHeaders(headers));

        return continuation(request, newContext);
    }
}
```

### RabbitMQ + MassTransit (Event-Driven Architecture)

**Назначение**: Асинхронная доставка событий между микросервисами.

#### Архитектура событий

```
Producer Service                  RabbitMQ                    Consumer Service
(Messages Service)                                           (Updates Service)
      |                                                             |
      |---Publish: MessageSentEvent--->|                           |
      |                                 |---Queue: message-sent--->|
      |                                 |                           |
      |                                 |<---Consume----------------|
      |                                 |                           |
      |                                 |                           |---Push to clients-->
```

#### Настройка MassTransit

**Producer (Messages Service)**:
```csharp
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });

        cfg.ConfigureEndpoints(context);
    });
});
```

**Consumer (Updates Service)**:
```csharp
builder.Services.AddMassTransit(x =>
{
    // Регистрация consumer
    x.AddConsumer<MessageSentEventConsumer>();
    x.AddConsumer<MessageReadEventConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });

        cfg.ConfigureEndpoints(context);
    });
});
```

#### Типы событий

**1. MessageSentEvent**:
```csharp
public class MessageSentEvent
{
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public long SenderId { get; set; }
    public string Text { get; set; }
    public List<AttachmentDto> Attachments { get; set; }
    public DateTime SentAt { get; set; }
    public List<long> ChatMemberIds { get; set; } // Для фильтрации получателей
}
```

**Publisher** (Messages Service):
```csharp
public class SendMessageCommandHandler : IRequestHandler<SendMessageCommand, SendMessageResponse>
{
    private readonly IPublishEndpoint _publishEndpoint;

    public async Task<SendMessageResponse> Handle(SendMessageCommand command, CancellationToken ct)
    {
        // Сохранение сообщения в БД
        var message = await _messageRepository.SaveAsync(command);

        // Публикация события
        await _publishEndpoint.Publish(new MessageSentEvent
        {
            MessageId = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            Text = message.Text,
            Attachments = message.Attachments,
            SentAt = message.SentAt,
            ChatMemberIds = await _chatRepository.GetMemberIdsAsync(message.ChatId)
        }, ct);

        return new SendMessageResponse { MessageId = message.Id };
    }
}
```

**Consumer** (Updates Service):
```csharp
public class MessageSentEventConsumer : IConsumer<MessageSentEvent>
{
    private readonly IStreamManager _streamManager;

    public async Task Consume(ConsumeContext<MessageSentEvent> context)
    {
        var evt = context.Message;

        // Находим все активные streams для участников чата
        var streams = _streamManager.GetActiveStreamsForUsers(evt.ChatMemberIds);

        // Отправляем событие в каждый stream
        foreach (var stream in streams)
        {
            await stream.WriteAsync(new NewMessageEvent
            {
                ChatId = evt.ChatId,
                Message = MapToProtoMessage(evt)
            });
        }
    }
}
```

**2. MessageReadEvent**:
```csharp
public class MessageReadEvent
{
    public long MessageId { get; set; }
    public long ChatId { get; set; }
    public long UserId { get; set; } // Кто прочитал
    public DateTime ReadAt { get; set; }
    public List<long> NotifyUserIds { get; set; } // Кого уведомить (отправитель + участники)
}
```

**3. UserRegisteredEvent**:
```csharp
public class UserRegisteredEvent
{
    public long UserId { get; set; }
    public string Email { get; set; }
    public string Username { get; set; }
    public string VerificationCode { get; set; }
}
```

**Consumer** (Notification Service):
```csharp
public class UserRegisteredEventConsumer : IConsumer<UserRegisteredEvent>
{
    private readonly IEmailService _emailService;

    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        var evt = context.Message;

        await _emailService.SendVerificationEmailAsync(
            evt.Email,
            evt.Username,
            evt.VerificationCode
        );
    }
}
```

#### Queues и Exchanges в RabbitMQ

**Автоматически создаваемые MassTransit**:

```
Exchange: MessageSentEvent (fanout)
  └─ Queue: updates_service_messagesent (binding)

Exchange: MessageReadEvent (fanout)
  └─ Queue: updates_service_messageread (binding)

Exchange: UserRegisteredEvent (fanout)
  └─ Queue: notification_service_userregistered (binding)
```

**Dead Letter Queue** (для failed messages):
```
Queue: {queue_name}_error
  └─ Хранит сообщения, которые не удалось обработать после повторных попыток
```

#### Retry Policy

```csharp
cfg.UseMessageRetry(r => r.Incremental(
    retryLimit: 3,
    initialInterval: TimeSpan.FromSeconds(1),
    intervalIncrement: TimeSpan.FromSeconds(2)
));
```

Retry schedule: 1s, 3s, 5s → Dead Letter Queue

### Service Discovery через Configuration Service

**Процесс загрузки конфигурации**:

```csharp
// В Program.cs каждого сервиса
builder.LoadConfiguration(ServiceId.Messages);
```

**Реализация** (`WebApplicationBuilderExtensions.cs`):
```csharp
public static void LoadConfiguration(this WebApplicationBuilder builder, ServiceId serviceId)
{
    // 1. Создаем временный gRPC клиент для Configuration service
    var configServiceUrl = builder.Configuration["ConfigurationService:Url"];
    var channel = GrpcChannel.ForAddress(configServiceUrl);
    var client = new ConfigurationApi.ConfigurationApiClient(channel);

    // 2. Загружаем конфигурацию для текущего сервиса
    var response = await client.GetServiceConfigAsync(new GetServiceConfigRequest
    {
        ServiceId = (int)serviceId
    });

    // 3. Добавляем полученные настройки в IConfiguration
    foreach (var setting in response.Settings)
    {
        builder.Configuration[setting.Key] = setting.Value;
    }

    // 4. Загружаем адреса других сервисов (service discovery)
    var servicesResponse = await client.GetAllServicesAsync(new Empty());
    foreach (var service in servicesResponse.Services)
    {
        builder.Configuration[$"{service.Name}:Host"] = service.Host;
        builder.Configuration[$"{service.Name}:Port"] = service.Port.ToString();
    }
}
```

**Что загружается**:
- Database connection strings (PostgreSQL)
- Redis connection string
- RabbitMQ connection settings
- JWT secret для валидации токенов
- Адреса других микросервисов
- Service-specific настройки

**Пример настроек для Messages Service**:
```json
{
  "Database:ConnectionString": "Host=postgres;Database=barkfluff_messages;...",
  "RabbitMQ:Host": "rabbitmq",
  "RabbitMQ:Username": "guest",
  "RabbitMQ:Password": "guest",
  "UsersService:Host": "http://users-service:7001",
  "FilesService:Host": "http://files-service:7005",
  "JwtSettings:Secret": "your-secret-key",
  "JwtSettings:Issuer": "BarkFluff.Identity",
  "JwtSettings:Audience": "BarkFluff"
}
```

---

## Клиентское приложение

### WPF Desktop Client

**Технологии**:
- .NET 9.0 WPF
- XAML для UI
- Code-behind архитектура (без отдельных ViewModels)
- Reactive pattern для data binding

### Архитектура клиента

```
App.xaml.cs (Entry Point)
  ├── Bootstrap
  │   ├── Load GlobalParam (encrypted config)
  │   ├── Verify PIN code
  │   └── Initialize WebApi (gRPC clients)
  │
  ├── Navigation
  │   ├── If not authenticated → SelectServer / Login
  │   └── If authenticated → MessengerPage
  │
  └── Background Services
      ├── RealtimeUpdateService (message streaming)
      ├── OnlineStatusService (status streaming)
      └── UpdateService (auto-update checker)
```

### Главные компоненты

#### 1. WebApi.Core (gRPC Client Manager)

**Singleton класс**, управляющий всеми gRPC клиентами:

```csharp
public class WebApi
{
    // gRPC Clients
    public UsersApiClient UsersAC { get; private set; }
    public IdentityApiClient IdentityAC { get; private set; }
    public MessagesApiClient MessagesAC { get; private set; }
    public FilesApiClient FilesAC { get; private set; }
    public UpdatesApiClient UpdatesAC { get; private set; }
    public OnlinerApiClient OnlinerAC { get; private set; }

    // Managers (wrappers)
    public WebApiTokenManager TokenManager { get; private set; }
    public WebApiAuthManager AuthManager { get; private set; }
    public WebApiUserManager UserManager { get; private set; }
    public WebApiMessageManager MessageManager { get; private set; }
    public WebApiFileManager FileManager { get; private set; }

    public void CreateAC(GlobalParam gParam)
    {
        // Создание GrpcChannel для каждого сервиса
        var messagesChannel = GrpcChannel.ForAddress(gParam.SocketMessages);

        // Создание клиента с interceptors
        MessagesAC = new MessagesApiClient(messagesChannel);

        // ... аналогично для остальных сервисов
    }
}
```

**Использование в UI**:
```csharp
// В любой странице/компоненте
var chats = await App.WebApi.MessageManager.GetChatsAsync(App.GParam);
```

#### 2. GlobalParam (Глобальная конфигурация)

**Хранит**:
- Учетные данные (UserId, AccessToken, RefreshToken)
- Настройки сервера (URLs всех сервисов)
- Device metadata (DeviceId, DeviceName)
- UI preferences (Colors, ServerName)
- PIN code (для локальной защиты)

**Загрузка**:
```csharp
// При запуске приложения
var gParam = GlobalParam.Load("datas/GlobalParam.json", userPin);
App.GParam = gParam;
```

**Сохранение**:
```csharp
// После изменения настроек
App.GParam.Save("datas/GlobalParam.json", userPin);
```

#### 3. MessengerPage (Главная страница чата)

**115KB кода-за-кодом** с функциями:

**UI секции**:
- Sidebar: Список чатов с аватарами, последним сообщением, unread count
- Chat Area: Список сообщений текущего чата
- Input Area: Поле ввода текста, кнопки прикрепления файлов
- Profile Panel: Информация о собеседнике/группе

**Основные методы**:

```csharp
// Загрузка истории сообщений
private async Task LoadHistoryMessages()
{
    var messages = await App.WebApi.MessageManager.GetMessagesAsync(
        App.GParam,
        CurrentChatId,
        fromMessageId: OldestMessageId,
        limit: 30
    );

    // Вставка в UI (в начало списка)
    foreach (var msg in messages.Reverse())
    {
        MessageList.Insert(0, msg);
    }
}

// Отправка сообщения
private async Task SendMessage()
{
    var message = new SendMessageRequest
    {
        ChatId = CurrentChatId,
        Content = new MessageContent
        {
            Text = InputTextBox.Text,
            Attachments = _attachedFiles
        }
    };

    await App.WebApi.MessageManager.SendMessageAsync(App.GParam, message);

    InputTextBox.Clear();
    _attachedFiles.Clear();
}

// Подписка на real-time обновления
private void SubscribeToRealtimeUpdates()
{
    RealtimeUpdateService.Instance.NewMessageReceived += OnNewMessageReceived;
    RealtimeUpdateService.Instance.ReadReceiptReceived += OnReadReceiptReceived;
}

// Обработка нового сообщения
private void OnNewMessageReceived(NewMessageEvent evt)
{
    Dispatcher.Invoke(() =>
    {
        if (evt.ChatId == CurrentChatId)
        {
            MessageList.Add(MapToMessageModel(evt.Message));
            ScrollToBottom();
        }

        // Отметить как прочитанное, если чат открыт
        if (evt.ChatId == CurrentChatId && IsWindowActive)
        {
            MarkMessageAsRead(evt.Message.MessageId);
        }
    });
}
```

**Пагинация сообщений**:
- Загружается по 30 сообщений при открытии чата
- При скролле вверх (offset < 100px) загружаются еще 30 старых сообщений
- Используется `fromMessageId` для cursor-based pagination

**Кеширование**:
1. При загрузке чата сначала проверяется LiteDB cache
2. Если кеш пуст или старый → запрос к серверу
3. Новые сообщения сохраняются в кеш
4. При получении real-time события → сохранение в кеш

#### 4. CachedImage / CachedAvatar

**Умные компоненты для отображения изображений с кешированием**:

```csharp
public class CachedImage : UserControl
{
    public string FileId { get; set; }
    public string DownloadUrl { get; set; }

    private async void LoadImage()
    {
        // 1. Проверка LiteDB cache
        var cachedPath = FileCacheService.GetCachedFilePath(FileId);

        if (cachedPath != null)
        {
            // Загрузка из локального файла
            ImageSource.Source = new BitmapImage(new Uri(cachedPath));
        }
        else
        {
            // 2. Показать placeholder
            ImageSource.Source = PlaceholderImage;

            // 3. Асинхронная загрузка
            cachedPath = await FileCacheService.DownloadAndCacheAsync(FileId, DownloadUrl);

            // 4. Обновление UI
            ImageSource.Source = new BitmapImage(new Uri(cachedPath));
        }
    }
}
```

**Lazy loading**: Изображения загружаются только при отображении на экране (virtualization).

#### 5. RealtimeUpdateService (Singleton)

**Управление real-time обновлениями**:

```csharp
public class RealtimeUpdateService
{
    public event Action<NewMessageEvent> NewMessageReceived;
    public event Action<MessageReadEvent> ReadReceiptReceived;
    public event Action<bool> ConnectionStatusChanged;

    private CancellationTokenSource _cts;
    private HashSet<long> _processedMessageIds = new(1000);

    public void Start(GlobalParam gParam)
    {
        _cts = new CancellationTokenSource();

        Task.Run(() => StreamMessagesAsync(gParam, _cts.Token));
        Task.Run(() => StreamReadReceiptsAsync(gParam, _cts.Token));
    }

    private async Task StreamMessagesAsync(GlobalParam gParam, CancellationToken ct)
    {
        int retryDelay = 2000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var call = App.WebApi.UpdatesAC.SubscribeNewMessages(new Empty());
                ConnectionStatusChanged?.Invoke(true);
                retryDelay = 2000; // Reset

                await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
                {
                    // Дедупликация
                    if (_processedMessageIds.Contains(evt.Message.MessageId))
                        continue;

                    _processedMessageIds.Add(evt.Message.MessageId);
                    if (_processedMessageIds.Count > 1000)
                    {
                        // Удаление старых (LRU)
                        _processedMessageIds.Remove(_processedMessageIds.First());
                    }

                    // Сохранение в кеш
                    MessageCacheManager.SaveMessage(evt.ChatId, evt.Message);

                    // Уведомление UI
                    NewMessageReceived?.Invoke(evt);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
            {
                // Обновление токена
                await App.WebApi.TokenManager.RefreshTokenAsync(gParam);
            }
            catch (Exception ex)
            {
                ConnectionStatusChanged?.Invoke(false);

                // Exponential backoff
                await Task.Delay(retryDelay, ct);
                retryDelay = Math.Min(retryDelay * 2, 30000); // Max 30s
            }
        }
    }
}
```

**Характеристики**:
- Автоматическое переподключение с exponential backoff
- Дедупликация последних 1000 сообщений
- Автоматическое сохранение в локальный кеш
- Уведомление UI через события

#### 6. OnlineStatusService (Singleton)

**Отслеживание онлайн-статусов**:

```csharp
public class OnlineStatusService
{
    private Dictionary<long, UserOnlineStatus> _statusCache = new();
    public event Action<long, UserOnlineStatus> OnlineStatusChanged;

    private Timer _pingTimer;

    public void Start(GlobalParam gParam)
    {
        // Ping каждые 3 секунды
        _pingTimer = new Timer(3000);
        _pingTimer.Elapsed += async (s, e) => await PingAsync(gParam);
        _pingTimer.Start();

        // Стриминг статусов
        Task.Run(() => StreamOnlineStatusAsync(gParam));
    }

    private async Task PingAsync(GlobalParam gParam)
    {
        try
        {
            await App.WebApi.OnlinerAC.PingAsync(new PingRequest());
        }
        catch { /* Игнорируем ошибки ping */ }
    }

    private async Task StreamOnlineStatusAsync(GlobalParam gParam)
    {
        var call = App.WebApi.OnlinerAC.SubscribeToOnlineStatus(new SubscribeOnlineStatusRequest
        {
            UserIds = { GetRelevantUserIds() } // Друзья + участники активных чатов
        });

        await foreach (var status in call.ResponseStream.ReadAllAsync())
        {
            _statusCache[status.UserId] = status;
            OnlineStatusChanged?.Invoke(status.UserId, status);
        }
    }
}
```

**Debounced UI updates**: Обновления UI происходят не чаще чем раз в 500ms для предотвращения "мигания".

---

## Обработка файлов

### Архитектура файловой системы

```
Client                    Files Service              Minio S3
  |                             |                        |
  |--1. Compute SHA256--------->|                        |
  |                             |                        |
  |--2. CheckFileHash()-------->|                        |
  |<----FileId (if exists)------|                        |
  |     [Дедупликация]          |                        |
  |                             |                        |
  |--3. GetUploadUrl()--------->|                        |
  |                             |---Generate PreSigned-->|
  |<----PreSigned URL-----------|                        |
  |                             |                        |
  |--4. HTTP POST file---------------------------------->|
  |     (напрямую в Minio)                               |
  |<----Upload complete----------------------------------
  |                                                       |
  |--5. SendMessage(FileId)---->|                        |
```

### Дедупликация файлов

**Механизм**:
1. Клиент вычисляет SHA256 хеш файла перед загрузкой
2. Вызывает `CheckFileHash(hash)` на Files service
3. Если файл с таким хешем уже существует → возвращается существующий FileId
4. Загрузка пропускается, экономится bandwidth и storage

**Пример**:
```csharp
// Клиент
var fileBytes = File.ReadAllBytes(filePath);
var hash = SHA256.HashData(fileBytes);
var hashString = Convert.ToHexString(hash);

var checkResponse = await App.WebApi.FilesAC.CheckFileHashAsync(new CheckFileHashRequest
{
    Hash = hashString
});

if (checkResponse.Exists)
{
    // Файл уже загружен, используем существующий
    return checkResponse.FileId;
}
else
{
    // Загружаем файл
    return await UploadFileAsync(filePath, fileType);
}
```

**Сценарий**: Если пользователь A загружает популярную картинку, а затем пользователь B загружает ту же картинку, файл физически хранится один раз, но оба пользователя получают доступ к нему.

### Типы файлов и ограничения

| Тип файла | Расширения | Макс. размер | Обработка |
|-----------|------------|--------------|-----------|
| **Image** | JPG, PNG, WebP | 10 МБ | Сжатие, resize, оптимизация |
| **Video** | MP4, AVI, MOV | 100 МБ | H.264 кодирование, создание thumbnail |
| **Document** | PDF, DOCX, XLSX, TXT | 50 МБ | Нет обработки |
| **Audio** | MP3, WAV, OGG, M4A | 20 МБ | Waveform генерация для UI |
| **GIF** | GIF | 10 МБ | Оптимизация размера |
| **Avatar** | JPG, PNG | 5 МБ | Crop, resize 512x512, сжатие |

### Обработка изображений (клиент)

**ImageProcessor.cs** (использует ImageSharp):

```csharp
public static async Task<string> ProcessImageForUploadAsync(string filePath)
{
    using var image = await Image.LoadAsync(filePath);

    // 1. Максимальные размеры
    const int maxWidth = 2048;
    const int maxHeight = 2048;

    if (image.Width > maxWidth || image.Height > maxHeight)
    {
        var ratio = Math.Min(
            (double)maxWidth / image.Width,
            (double)maxHeight / image.Height
        );

        var newWidth = (int)(image.Width * ratio);
        var newHeight = (int)(image.Height * ratio);

        image.Mutate(x => x.Resize(newWidth, newHeight));
    }

    // 2. Конвертация в RGB (удаление альфа-канала)
    if (image.Metadata.TryGetFormatMetadata(PngFormat.Instance, out _))
    {
        image.Mutate(x => x.BackgroundColor(Color.White));
    }

    // 3. Сжатие JPEG с качеством 85%
    var tempPath = Path.GetTempFileName() + ".jpg";
    await image.SaveAsJpegAsync(tempPath, new JpegEncoder
    {
        Quality = 85
    });

    return tempPath;
}
```

**Обработка аватаров**:
```csharp
public static async Task<string> ProcessAvatarAsync(string filePath)
{
    using var image = await Image.LoadAsync(filePath);

    // Crop и resize в квадрат 512x512
    var size = Math.Min(image.Width, image.Height);
    image.Mutate(x => x
        .Crop(new Rectangle((image.Width - size) / 2, (image.Height - size) / 2, size, size))
        .Resize(512, 512)
    );

    var tempPath = Path.GetTempFileName() + ".jpg";
    await image.SaveAsJpegAsync(tempPath, new JpegEncoder { Quality = 90 });

    return tempPath;
}
```

### Обработка видео (клиент)

**FFmpegService.cs**:

```csharp
public class VideoCompressor
{
    public async Task<string> CompressVideoAsync(string inputPath, IProgress<double> progress)
    {
        var outputPath = Path.GetTempFileName() + ".mp4";

        var arguments = $"-i \"{inputPath}\" " +
                        "-c:v libx264 " +                 // H.264 codec
                        "-preset fast " +                  // Быстрое кодирование
                        "-crf 23 " +                       // Качество (18-28, где 23 = хорошее)
                        "-c:a aac " +                      // AAC audio codec
                        "-b:a 128k " +                     // Audio bitrate
                        "-movflags +faststart " +          // Оптимизация для веб
                        "-vf \"scale='min(1280,iw)':'min(720,ih)':force_original_aspect_ratio=decrease\" " +
                        $"\"{outputPath}\"";

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };

        process.Start();

        // Парсинг прогресса из stderr FFmpeg
        while (!process.StandardError.EndOfStream)
        {
            var line = await process.StandardError.ReadLineAsync();
            if (line.Contains("time="))
            {
                var timeMatch = Regex.Match(line, @"time=(\d{2}):(\d{2}):(\d{2})");
                if (timeMatch.Success)
                {
                    var seconds = int.Parse(timeMatch.Groups[1].Value) * 3600 +
                                  int.Parse(timeMatch.Groups[2].Value) * 60 +
                                  int.Parse(timeMatch.Groups[3].Value);

                    var percent = (double)seconds / _totalDuration * 100;
                    progress?.Report(percent);
                }
            }
        }

        await process.WaitForExitAsync();

        return outputPath;
    }

    public async Task<string> GenerateThumbnailAsync(string videoPath)
    {
        var thumbnailPath = Path.GetTempFileName() + ".jpg";

        var arguments = $"-i \"{videoPath}\" " +
                        "-ss 00:00:01 " +              // Кадр на 1 секунде
                        "-vframes 1 " +                // Один кадр
                        "-vf \"scale=320:180\" " +     // Размер превью
                        $"\"{thumbnailPath}\"";

        var process = Process.Start("ffmpeg", arguments);
        await process.WaitForExitAsync();

        return thumbnailPath;
    }
}
```

### Обработка аудио (клиент)

**AudioAnalyzer.cs** (использует NAudio):

```csharp
public class AudioAnalyzer
{
    public async Task<float[]> GenerateWaveformAsync(string audioPath)
    {
        using var reader = new AudioFileReader(audioPath);

        var samplesPerPixel = (int)(reader.TotalTime.TotalSeconds * 10); // 10 пикселей на секунду
        var waveform = new float[samplesPerPixel];

        var buffer = new float[1024];
        int read;
        int pixelIndex = 0;
        float maxSample = 0;

        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                var sample = Math.Abs(buffer[i]);
                if (sample > maxSample)
                    maxSample = sample;
            }

            if (pixelIndex < waveform.Length)
            {
                waveform[pixelIndex] = maxSample;
                maxSample = 0;
                pixelIndex++;
            }
        }

        return waveform;
    }
}
```

**Использование в UI**:
```csharp
// VoiceMessage.xaml.cs
var waveform = await AudioAnalyzer.GenerateWaveformAsync(audioFilePath);
WaveformCanvas.DrawWaveform(waveform); // Custom control для рисования
```

### Загрузка файлов с прогрессом

**WebApiFileManager.UploadFileAsync**:

```csharp
public async Task<string> UploadFileAsync(
    GlobalParam gParam,
    string filePath,
    FileType fileType,
    IProgress<double> progress)
{
    // 1. Предобработка
    string processedPath = filePath;
    if (fileType == FileType.Image)
    {
        processedPath = await ImageProcessor.ProcessImageForUploadAsync(filePath);
        progress?.Report(0.2); // 20% - обработка
    }
    else if (fileType == FileType.Video)
    {
        processedPath = await VideoCompressor.CompressVideoAsync(filePath,
            new Progress<double>(p => progress?.Report(0.2 + p * 0.2))); // 20-40% - сжатие
    }

    // 2. Вычисление хеша
    var hash = await ComputeFileHashAsync(processedPath);
    progress?.Report(0.4);

    // 3. Проверка дедупликации
    var checkResponse = await FilesAC.CheckFileHashAsync(new CheckFileHashRequest
    {
        Hash = hash
    });

    if (checkResponse.Exists)
    {
        progress?.Report(1.0);
        return checkResponse.FileId;
    }

    // 4. Получение upload URL
    var uploadUrlResponse = await FilesAC.GetUploadUrlAsync(new GetUploadUrlRequest
    {
        FileType = fileType
    });

    // 5. Загрузка в Minio
    using var httpClient = new HttpClient();
    using var fileStream = File.OpenRead(processedPath);

    var content = new MultipartFormDataContent();
    var streamContent = new StreamContent(fileStream);
    content.Add(streamContent, "file", Path.GetFileName(filePath));

    var uploadProgress = new Progress<long>(uploaded =>
    {
        var percent = 0.4 + (double)uploaded / fileStream.Length * 0.6; // 40-100%
        progress?.Report(percent);
    });

    var response = await httpClient.PostAsync(uploadUrlResponse.Url, content);
    response.EnsureSuccessStatusCode();

    progress?.Report(1.0);

    return uploadUrlResponse.FileId;
}
```

### Скачивание и кеширование файлов

**FileCacheService.cs**:

```csharp
public class FileCacheService
{
    private readonly LiteDatabase _db;
    private readonly SemaphoreSlim _downloadSemaphore = new(5); // Макс 5 одновременных загрузок

    public event Action<string> FileCached;
    public event Action<string, Exception> FileDownloadFailed;

    public async Task<string> GetCachedFilePathAsync(string fileId, string downloadUrl)
    {
        // 1. Проверка кеша
        var cached = _db.GetCollection<CachedFile>("cached_files")
            .FindOne(x => x.FileId == fileId);

        if (cached != null && File.Exists(cached.LocalPath))
        {
            return cached.LocalPath;
        }

        // 2. Асинхронная загрузка
        await _downloadSemaphore.WaitAsync();
        try
        {
            // Double-check (другой поток мог уже загрузить)
            cached = _db.GetCollection<CachedFile>("cached_files")
                .FindOne(x => x.FileId == fileId);
            if (cached != null && File.Exists(cached.LocalPath))
            {
                return cached.LocalPath;
            }

            // Получение download URL от Files service
            var response = await App.WebApi.FilesAC.GetDownloadUrlAsync(new GetDownloadUrlRequest
            {
                FileId = fileId
            });

            // Загрузка файла
            using var httpClient = new HttpClient();
            var fileBytes = await httpClient.GetByteArrayAsync(response.Url);

            // Определение типа и расширения
            var extension = Path.GetExtension(response.FileName) ?? ".bin";
            var localPath = Path.Combine("datas", "cache", GetCategoryFolder(response.FileType), $"{fileId}{extension}");

            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
            await File.WriteAllBytesAsync(localPath, fileBytes);

            // Сохранение в БД
            var cachedFile = new CachedFile
            {
                FileId = fileId,
                Hash = ComputeSHA256(fileBytes),
                FileType = response.FileType,
                LocalPath = localPath,
                CachedAt = DateTime.UtcNow,
                Size = fileBytes.Length
            };

            _db.GetCollection<CachedFile>("cached_files").Insert(cachedFile);

            FileCached?.Invoke(fileId);

            return localPath;
        }
        catch (Exception ex)
        {
            FileDownloadFailed?.Invoke(fileId, ex);
            return null;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    private string GetCategoryFolder(FileType type) => type switch
    {
        FileType.Avatar => "avatars",
        FileType.Image => "images",
        FileType.Video => "videos",
        FileType.Gif => "gifs",
        FileType.Document => "documents",
        FileType.Audio => "audio",
        _ => "other"
    };
}
```

### Превью файлов

**Thumbnail генерация** (на сервере Files service):

```csharp
public class ThumbnailGenerator
{
    public async Task<string> GenerateImageThumbnailAsync(string fileId, string minioKey)
    {
        // Загрузка оригинала из Minio
        var originalBytes = await _minioClient.GetObjectAsync(minioKey);

        using var image = Image.Load(originalBytes);

        // Resize до 320x240 (сохраняя пропорции)
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(320, 240),
            Mode = ResizeMode.Max
        }));

        // Сохранение thumbnail в Minio
        using var ms = new MemoryStream();
        await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 80 });
        ms.Position = 0;

        var thumbnailKey = $"previews/image-thumbnails/{fileId}.jpg";
        await _minioClient.PutObjectAsync(thumbnailKey, ms);

        return thumbnailKey;
    }

    public async Task<string> GenerateVideoThumbnailAsync(string fileId, string minioKey)
    {
        // Аналогично, но используя FFmpeg для извлечения кадра
        // ...
    }
}
```

**PreviewFileId** в MessageAttachment:
```protobuf
message Attachment {
  string file_id = 1;
  AttachmentType type = 2;
  string preview_file_id = 3;  // <- ID thumbnail для быстрой загрузки
  string file_name = 4;
  int64 size = 5;
}
```

Клиент сначала загружает `preview_file_id` (маленький размер), затем по клику пользователя загружает полный `file_id`.

---

## Real-time коммуникация

### gRPC Server Streaming

**Преимущества** по сравнению с WebSockets:

| Характеристика | gRPC Streaming | WebSockets |
|----------------|----------------|------------|
| **Протокол** | HTTP/2 (multiplexed) | HTTP/1.1 upgrade |
| **Типобезопасность** | Protobuf (строгая типизация) | JSON/текст (без типов) |
| **Сжатие** | gzip встроен | Требует настройки |
| **Бинарный формат** | Да (Protobuf) | Нет (по умолчанию) |
| **Code Generation** | Автоматическая | Ручная |
| **Load Balancing** | Поддержка HTTP/2 балансировщиков | Сложнее (sticky sessions) |

### Архитектура streaming в Updates Service

```
[Client 1] ──gRPC stream─┐
[Client 2] ──gRPC stream─┤
[Client 3] ──gRPC stream─┼───> [Updates Service] <─RabbitMQ─ [Messages Service]
[Client 4] ──gRPC stream─┤         │
[Client 5] ──gRPC stream─┘         │
                                   │
                        [StreamManager]
                         - Dictionary<UserId, List<Stream>>
                         - Фильтрация событий по UserId
                         - Broadcast к нужным клиентам
```

**StreamManager.cs** (внутри Updates service):

```csharp
public class StreamManager
{
    private readonly ConcurrentDictionary<long, List<IServerStreamWriter<NewMessageEvent>>> _activeStreams = new();

    public void RegisterStream(long userId, IServerStreamWriter<NewMessageEvent> stream)
    {
        _activeStreams.AddOrUpdate(userId,
            new List<IServerStreamWriter<NewMessageEvent>> { stream },
            (key, existing) =>
            {
                existing.Add(stream);
                return existing;
            });
    }

    public void UnregisterStream(long userId, IServerStreamWriter<NewMessageEvent> stream)
    {
        if (_activeStreams.TryGetValue(userId, out var streams))
        {
            streams.Remove(stream);
            if (streams.Count == 0)
            {
                _activeStreams.TryRemove(userId, out _);
            }
        }
    }

    public async Task BroadcastToUserAsync(long userId, NewMessageEvent evt)
    {
        if (_activeStreams.TryGetValue(userId, out var streams))
        {
            var tasks = streams.Select(stream => SafeWriteAsync(stream, evt));
            await Task.WhenAll(tasks);
        }
    }

    private async Task SafeWriteAsync(IServerStreamWriter<NewMessageEvent> stream, NewMessageEvent evt)
    {
        try
        {
            await stream.WriteAsync(evt);
        }
        catch (Exception ex)
        {
            // Логирование, stream возможно отключился
            _logger.LogWarning(ex, "Failed to write to stream");
        }
    }
}
```

**Реализация streaming endpoint**:

```csharp
public class UpdatesServiceImpl : UpdatesApi.UpdatesApiBase
{
    private readonly StreamManager _streamManager;

    public override async Task SubscribeNewMessages(
        Empty request,
        IServerStreamWriter<NewMessageEvent> responseStream,
        ServerCallContext context)
    {
        var userId = context.GetUserId(); // Из JWT токена

        _streamManager.RegisterStream(userId, responseStream);

        try
        {
            // Держим stream открытым до отключения клиента
            await context.CancellationToken.WaitAsync();
        }
        finally
        {
            _streamManager.UnregisterStream(userId, responseStream);
        }
    }
}
```

**Потребление событий из RabbitMQ**:

```csharp
public class MessageSentEventConsumer : IConsumer<MessageSentEvent>
{
    private readonly StreamManager _streamManager;

    public async Task Consume(ConsumeContext<MessageSentEvent> context)
    {
        var evt = context.Message;

        // Broadcast всем участникам чата
        foreach (var memberId in evt.ChatMemberIds)
        {
            await _streamManager.BroadcastToUserAsync(memberId, new NewMessageEvent
            {
                ChatId = evt.ChatId,
                Message = MapToProtoMessage(evt)
            });
        }
    }
}
```

### Heartbeat и Keep-Alive

**Клиентская сторона**:
```csharp
// Периодический ping для поддержания соединения
var pingTimer = new Timer(30000); // 30 секунд
pingTimer.Elapsed += async (s, e) =>
{
    try
    {
        // Пустой запрос для поддержания stream
        await OnlinerAC.PingAsync(new PingRequest());
    }
    catch { }
};
```

**Серверная сторона** (настройка gRPC):
```csharp
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = true;
    options.MaxReceiveMessageSize = 10 * 1024 * 1024; // 10 MB
    options.KeepAliveTime = TimeSpan.FromMinutes(2);
    options.KeepAliveTimeout = TimeSpan.FromSeconds(20);
});
```

### Обработка отключений и переподключений

**Клиентская логика**:

```csharp
private async Task StreamWithRetryAsync(CancellationToken ct)
{
    int retryDelay = 2000; // Начальная задержка 2 секунды
    const int maxRetryDelay = 30000; // Максимум 30 секунд

    while (!ct.IsCancellationRequested)
    {
        try
        {
            // Попытка подключения
            var call = UpdatesAC.SubscribeNewMessages(new Empty());
            OnConnectionStateChanged(true);
            retryDelay = 2000; // Сброс задержки при успешном подключении

            // Потребление stream
            await foreach (var evt in call.ResponseStream.ReadAllAsync(ct))
            {
                ProcessEvent(evt);
            }

            // Stream завершился штатно
            break;
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
        {
            // Сервер недоступен
            _logger.LogWarning("Updates service unavailable, retrying...");
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unauthenticated)
        {
            // Токен истёк, обновляем
            await RefreshTokenAsync();
            retryDelay = 0; // Немедленная попытка после обновления токена
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in message stream");
        }

        OnConnectionStateChanged(false);

        if (retryDelay > 0)
        {
            await Task.Delay(retryDelay, ct);
        }

        // Exponential backoff
        retryDelay = Math.Min(retryDelay * 2, maxRetryDelay);
    }
}
```

**Сценарии**:
1. **Сервер перезапускается**: Клиент автоматически переподключается через 2-4-8... секунд
2. **Сетевое отключение**: Клиент продолжает попытки подключения до восстановления связи
3. **Токен истёк**: Клиент обновляет токен через Identity service и продолжает работу

### Масштабирование streaming

**Проблема**: При большом количестве клиентов один экземпляр Updates service может не справиться.

**Решение: Redis Pub/Sub** для broadcast между экземплярами Updates service:

```
[Messages Service] ──RabbitMQ─┐
                               │
                               ↓
[Updates Instance 1] ←─Redis Pub/Sub─→ [Updates Instance 2]
   │                                         │
   ├─ Client A                              ├─ Client C
   └─ Client B                              └─ Client D
```

**Реализация**:

```csharp
public class RedisStreamBroadcaster
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ISubscriber _subscriber;

    public async Task BroadcastEventAsync(long userId, NewMessageEvent evt)
    {
        var channel = $"user:{userId}:messages";
        var serialized = JsonSerializer.Serialize(evt);

        await _subscriber.PublishAsync(channel, serialized);
    }

    public void SubscribeToEvents(long userId, Action<NewMessageEvent> handler)
    {
        var channel = $"user:{userId}:messages";

        _subscriber.Subscribe(channel, (ch, message) =>
        {
            var evt = JsonSerializer.Deserialize<NewMessageEvent>(message);
            handler(evt);
        });
    }
}
```

Теперь Events Service публикует события в Redis, и все экземпляры Updates service получают их и пушат своим подключенным клиентам.

---

## Приватность и обработка данных пользователей

### Собираемые данные пользователей

#### Данные учетной записи

| Данные | Назначение | Хранение | Удаление |
|--------|------------|----------|----------|
| **Email** | Вход, восстановление, уведомления | PostgreSQL (Identity DB) | При удалении аккаунта |
| **Username** | Вход, отображение | PostgreSQL (Identity DB) | При удалении аккаунта |
| **Password Hash** | Аутентификация | PostgreSQL (Identity DB) | При удалении аккаунта |
| **FirstName, LastName** | Отображение в профиле | PostgreSQL (Users DB) | При удалении аккаунта |
| **Bio** | Описание профиля | PostgreSQL (Users DB) | При удалении аккаунта |
| **Avatar** | Изображение профиля | Minio + PostgreSQL (Files DB) | При удалении аккаунта |

#### Метаданные устройства

| Данные | Назначение | Хранение | Удаление |
|--------|------------|----------|----------|
| **DeviceId (UUID)** | Идентификация устройства, привязка Refresh Token | PostgreSQL (Identity.Sessions) | При выходе / удалении аккаунта |
| **DeviceName** | Отображение в списке сессий ("John's iPhone") | PostgreSQL (Identity.Sessions) | При выходе |
| **IP Address** | Безопасность (обнаружение подозрительной активности) | PostgreSQL (Identity.Sessions) | Через 90 дней после закрытия сессии |
| **OS Name** | Аудит, совместимость | PostgreSQL (Identity.Sessions) | Через 90 дней |
| **App Version** | Совместимость, статистика | PostgreSQL (Identity.Sessions) | Через 90 дней |

**Важно**: IP-адреса не передаются третьим лицам и используются только для безопасности.

#### Данные сообщений

| Данные | Назначение | Хранение | Удаление |
|--------|------------|----------|----------|
| **Текст сообщения** | Коммуникация | PostgreSQL (Messages DB) | При удалении сообщения пользователем (soft delete → 30 дней → hard delete) |
| **Вложения (файлы)** | Обмен медиа | Minio + PostgreSQL (Files DB) | При удалении сообщения или по запросу |
| **Время отправки** | Сортировка, история | PostgreSQL (Messages DB) | При удалении сообщения |
| **Статус прочтения** | Уведомление о доставке | PostgreSQL (MessageReadReceipts) | При удалении сообщения |

**Важно**: Сообщения хранятся в зашифрованном виде на уровне БД (TDE - Transparent Data Encryption в PostgreSQL).

#### Данные активности

| Данные | Назначение | Хранение | Удаление |
|--------|------------|----------|----------|
| **Онлайн-статус** | Отображение "в сети" / "был в сети" | Redis (TTL 5 мин) | Автоматически через 5 мин неактивности |
| **LastSeenAt** | Отображение "был в сети X минут назад" | Redis (TTL 5 мин) | Автоматически через 5 мин |
| **Логи входов** | Аудит безопасности | PostgreSQL (Identity.AuditLog) | Через 180 дней |

### Шифрование данных

#### В состоянии покоя (at rest)

**PostgreSQL**:
- **TDE (Transparent Data Encryption)**: Все данные в БД зашифрованы на уровне storage
- **Конфигурация** (в docker-compose или Kubernetes):
```yaml
postgres:
  environment:
    POSTGRES_INITDB_ARGS: "-E UTF8 --data-checksums"
  volumes:
    - postgres_data:/var/lib/postgresql/data  # Encrypted volume
```

**Minio**:
- **Server-Side Encryption (SSE)**: Файлы шифруются при загрузке
- **Алгоритм**: AES-256-GCM
- **Ключи**: Управляются через KMS (Key Management Service) или локально

**Клиентское хранилище**:
- **GlobalParam.json**: AES-256-CBC с ключом, производным от PIN пользователя (PBKDF2)
- **LiteDB**: Не зашифрована (т.к. кеш сообщений, не содержит секретов)

#### В транзите (in transit)

**gRPC / HTTP/2**:
- **TLS 1.3** для всех соединений клиент ↔ сервер
- **Сертификаты**: Let's Encrypt или самоподписанные (для разработки)
- **Настройка** (в Production):
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(7000, listenOptions =>
    {
        listenOptions.UseHttps("/certs/server.pfx", "password");
        listenOptions.Protocols = HttpProtocols.Http2; // gRPC требует HTTP/2
    });
});
```

**RabbitMQ**:
- **TLS** для подключений между сервисами
- **Аутентификация**: Username/Password (хранятся в Configuration service)

#### End-to-End шифрование (E2EE)

**Текущее состояние**: НЕ реализовано.

**Возможная реализация** (для приватных чатов):
1. Каждый клиент генерирует пару ключей (Public/Private) при регистрации
2. Public key хранится на сервере (Users service)
3. Private key хранится только на устройстве клиента (зашифрованный)
4. При отправке сообщения:
   - Клиент генерирует симметричный ключ AES-256
   - Шифрует сообщение симметричным ключом
   - Шифрует симметричный ключ Public key получателя (RSA/ECDH)
   - Отправляет зашифрованное сообщение + зашифрованный ключ на сервер
5. Сервер не может прочитать содержимое (не имеет Private key)
6. Получатель расшифровывает ключ своим Private key, затем сообщение

**Недостатки E2EE**:
- Нельзя искать по содержимому на сервере
- Нельзя синхронизировать историю на новых устройствах без экспорта ключей
- Сложность управления ключами

### Права доступа к данным

#### Клиент может получить:

**Свои данные**:
- ✅ Свой профиль (FirstName, LastName, Bio, Avatar)
- ✅ Свои сообщения во всех чатах
- ✅ Список своих чатов
- ✅ Свои сессии (список устройств)
- ✅ Свои загруженные файлы

**Данные других пользователей**:
- ✅ Публичные профили (FirstName, LastName, Bio, Avatar)
- ✅ Онлайн-статус (если не скрыт настройками)
- ✅ Сообщения в общих чатах
- ❌ Email (скрыт)
- ❌ Сессии других пользователей
- ❌ Личные сообщения других пользователей

**Авторизация на уровне API**:
```csharp
[Authorize(Policy = nameof(TokenType.User))]
public override async Task<UserProfile> GetUserProfile(GetUserProfileRequest request, ServerCallContext context)
{
    var requestingUserId = context.GetUserId();
    var targetUserId = request.UserId;

    // Получение публичного профиля (доступно всем)
    var profile = await _userRepository.GetProfileAsync(targetUserId);

    // Email виден только самому пользователю
    if (requestingUserId != targetUserId)
    {
        profile.Email = null;
    }

    return profile;
}
```

### GDPR Compliance (Соответствие GDPR)

#### Право на доступ (Right to Access)

**API метод** (Users service):
```protobuf
service UsersApi {
  rpc ExportUserData(ExportUserDataRequest) returns (ExportUserDataResponse);
}
```

**Возвращает**:
- Профиль пользователя (JSON)
- Список всех сообщений (JSON)
- Список всех загруженных файлов (URLs)
- История входов (JSON)

#### Право на удаление (Right to Erasure)

**API метод** (Users service):
```protobuf
service UsersApi {
  rpc DeleteAccount(DeleteAccountRequest) returns (DeleteAccountResponse);
}
```

**Процесс удаления**:
1. **Мягкое удаление** (30 дней grace period):
   - Аккаунт помечается как `IsDeleted = true`
   - Пользователь не может войти
   - Данные не отображаются другим пользователям
   - Возможно восстановление по запросу support

2. **Жесткое удаление** (после 30 дней):
   - Удаление записи из Identity.Users
   - Удаление профиля из Users.UserProfiles
   - **Сохранение сообщений**: Сообщения остаются, но `SenderId` заменяется на `NULL` или `[Deleted User]`
   - Удаление сессий
   - Удаление файлов (аватары, личные файлы)

**Почему сообщения сохраняются?**
- Групповые чаты: Удаление сообщений нарушит историю для других участников
- Легальные требования: В некоторых юрисдикциях требуется хранить коммуникации

**Опция полного удаления**: Пользователь может запросить удаление всех своих сообщений через support.

#### Право на переносимость (Right to Data Portability)

**ExportUserData** возвращает данные в формате JSON, который можно импортировать в другие системы.

**Пример экспорта**:
```json
{
  "user": {
    "userId": 12345,
    "username": "john_doe",
    "email": "john@example.com",
    "firstName": "John",
    "lastName": "Doe",
    "createdAt": "2025-01-01T00:00:00Z"
  },
  "messages": [
    {
      "messageId": 1,
      "chatId": 5,
      "text": "Hello!",
      "sentAt": "2026-01-23T12:00:00Z",
      "attachments": [...]
    }
  ],
  "files": [
    {
      "fileId": "uuid-...",
      "fileName": "photo.jpg",
      "size": 1024000,
      "downloadUrl": "https://..."
    }
  ]
}
```

### Логирование и аудит

**Что логируется**:
- Успешные и неудачные попытки входа (IP, DeviceId, время)
- Изменения пароля
- Включение/отключение 2FA
- Создание/удаление аккаунта
- Доступ к чувствительным данным (экспорт данных, удаление аккаунта)

**Формат** (Serilog → PostgreSQL или файлы):
```json
{
  "Timestamp": "2026-01-23T12:00:00Z",
  "Level": "Information",
  "MessageTemplate": "User {UserId} logged in from device {DeviceId}",
  "Properties": {
    "UserId": 12345,
    "DeviceId": "uuid-...",
    "IpAddress": "192.168.1.100",
    "Success": true
  }
}
```

**Retention**: Логи хранятся 180 дней, затем удаляются.

### Политика конфиденциальности

**Рекомендуемые пункты** (для production развертывания):

1. **Какие данные собираются**: Email, username, сообщения, метаданные устройства
2. **Зачем собираются**: Предоставление сервиса, безопасность, улучшение UX
3. **Как хранятся**: Шифрование at rest и in transit, защищенные серверы
4. **С кем делятся**: Не делятся с третьими лицами (кроме legal требований)
5. **Права пользователей**: Доступ, удаление, экспорт данных (GDPR)
6. **Контакты**: Email для запросов по конфиденциальности

---

## Заключение

BarkFluff представляет собой современную микросервисную платформу для обмена сообщениями с акцентом на:

- **Производительность**: gRPC, HTTP/2, бинарный протокол
- **Масштабируемость**: Независимые микросервисы, event-driven архитектура
- **Безопасность**: JWT, 2FA, шифрование данных, аудит
- **User Experience**: Real-time обновления, офлайн-кеширование, умная обработка файлов
- **Приватность**: GDPR compliance, право на удаление и экспорт данных

Система готова к развертыванию как в облачных средах (Kubernetes), так и на локальных серверах (Docker Compose).

