# BarkFluff.Messages

Управление чатами, сообщениями и вложениями. Порт: **7007**.

Расположение: `Backend/BarkFluff.Messages/`

📁 **Карта файлов проекта:** [[Backend/Messages-ProjectMap]]
📊 **Реестр метрик:** [[Backend/Messages-Metrics]]

## Сборка

```bash
dotnet build BarkFluff.Messages.csproj
docker-compose -f docker-compose-dev.yml up -d messages
```

Миграции применяются автоматически при старте.

## Архитектура

### CQRS через MediatR

| Feature | Описание |
|---------|----------|
| `SendMessage` | Отправка в чат или DM (авто-создаёт личный чат). Лимиты: текст ≤ 4096 символов, ≤ 10 вложений |
| `ListChats` | Список чатов с пагинацией, имена/аватары из Redis или Users API |
| `ListMessages` | Двунаправленная пагинация (до 50 в каждую сторону) |
| `CreateGroupChat` | Создание группы с системным сообщением |
| `KickUser` | Исключение с проверкой прав, системное сообщение |
| `MarkAsRead` | PostgreSQL array операции, публикует `MessageReadEvent` |
| `GetPersonChatId` | Получить или создать личный чат (поддерживает self-chat) |
| `GetChatInfo` | Счётчик непрочитанных, последнее сообщение |
| `ListChatMembers` | Пагинированный список с данными из Users API |
| `ListChatAttachments` | Вложения по типу, фильтрация + сортировка |
| `GetUserAllMessages` | Service-only: экспорт данных пользователя (GDPR) |

### gRPC-сервисы

| Сервис | Авторизация |
|--------|-------------|
| `MessagesApiService` | `TokenType.User` — клиентский API |
| `MessagesServerApiService` | `TokenType.Service` — межсервисный API |

### Исходящие gRPC-клиенты

- **Users** (`UsersServerApiClient`) — `GetByIdAsync`, `ListByIdsAsync` для имён/аватаров (токен `UsersService:Token`)
- **Files** (`FilesServerApiClient`) — `GetFilesDataAsync`, `GetFileDataAsync` для вложений (токен `FilesService:Token`)

### RabbitMQ

**Публикует:**
- `NewMessageEvent` → [[Backend/Updates]] (отправка сообщения, создание группы, kick)
- `MessageReadEvent` → [[Backend/Updates]] (MarkAsRead)

**Потребляет:**
- `user-changed-name-messages` → `UserChangedNameConsumer` → Redis-кеш имён
- `user-changed-avatar-messages` → `UserChangedAvatarConsumer` → Redis-кеш аватаров
- `session-revoked-messages` → `SessionRevokedConsumer` → инвалидация токена сессии (`TokenRevocationCache`, XAuth)

### Redis-кеш

`ChatCache` (Scoped). Ключи: `chat_name_{chatId}_{userId}`, `chat_image_{chatId}_{userId}`. Префикс: `Messages_`.

## База данных

| Сущность | Важные детали |
|----------|---------------|
| `Chat` | `LastMessage`, `CountUnread`, `FirstUnreadMessageId` — вычисляются в рантайме, не в БД |
| `ChatMember` | Индекс `(ChatId, UserId)`, каскадное удаление |
| `Message` | `Content` — owned type, `ReadBy` — PostgreSQL array |
| `MessageAttachment` | Owned collection в отдельной таблице `MessageAttachments` |
| `MessageAttachmentType` | Unknown, Image, Video, Gif, Document, Audio, Voice, Sticker, ForwardedMessage |
| `ForwardedMessageAttachment` | Owned collection в таблице `ForwardedMessageAttachments`; вложения внутри пересланного сообщения (без ForwardedMessage рекурсии) |
| `GroupChatInfo` | `UsersCanKick` — PostgreSQL array |

## Важные нюансы

- **Пересланные сообщения**: `MessageAttachmentType.ForwardedMessage` (8) — снапшот оригинала. `ForwardedAuthorName`, `ForwardedOriginalMessageId`, `ForwardedText` хранятся в `MessageAttachments`; вложения оригинала — в `ForwardedMessageAttachments`
- **Reply ≡ Forward на бэке**: отдельного поля `reply_to_message_id` нет. Клиенты (Android, WPF) различают reply/forward только на UI-уровне по эвристике "оригинал есть в текущей загруженной истории чата" (см. [[Клиенты/Android]] и [[Клиенты/Windows-WPF]]). Для бэкенда — это всегда `OutgoingMessage.forwarded_message_id`
- **ListChatAttachments без фильтра**: тип 8 (ForwardedMessage) исключён из медиа-галереи автоматически
- **Системные сообщения**: `MessageContentType.System` — для событий чата (создание группы, кик)
- **Маппинг файлов**: `MessageMapping.ToGrpc(filesInfoMap?)` — словарь `fileId → FileData` для вложений. Если не передан — поля preview/filename пустые
- **Пагинация**: `GetChatMessagesWithOffset` — двунаправленная загрузка вокруг `fromMessageId` (по 50 в каждую сторону)
- **Права кика**: только создатель группы и `GroupChatInfo.UsersCanKick`
- **Self-chat**: `CreatePersonChat(userId, userId)` — личный чат с самим собой поддерживается

## Конфигурация

```
MessagesDb, Redis, RabbitMQ:*, UsersService:Host/Token, FilesService:Host/Token
```

## Proto

- `messages_api.proto` — Server
- `shared.proto` — None
- `users_api.proto` — Client
- `files_api.proto` — Client
