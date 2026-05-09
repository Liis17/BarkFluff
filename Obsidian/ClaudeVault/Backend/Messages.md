# BarkFluff.Messages

Управление чатами, сообщениями и вложениями. Порт: **7007**.

Расположение: `Backend/BarkFluff.Messages/`

📁 **Карта файлов проекта:** [[Backend/Messages-ProjectMap]]
📊 **Реестр метрик:** [[Backend/Messages-Metrics]]
📌 **Клиентский гайд по закреплённым сообщениям:** [[Backend/Messages-PinnedMessages-ClientGuide]] — какие RPC и события вызывать/слушать в Android/WPF/Web/iOS/macOS/Linux

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
| `EditMessage` | Правка своего сообщения: текст и/или список вложений. Forward-снапшот не редактируется. Системные нельзя. Выставляет `IsEdited`+`EditedAt`, публикует `MessageEditedEvent` |
| `DeleteMessage` | Soft-delete своего сообщения (`IsDeleted=true`). Системные нельзя. Повторное удаление — idempotent no-op. Публикует `MessageDeletedEvent` |
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
| `PinMessage` | Закрепление сообщения в чате. Любой участник чата. Лимит: 100 закрепов на чат. Системное сообщение + `MessagePinnedEvent`. Idempotent при повторе |
| `UnpinMessage` | Открепление сообщения. Любой участник. Системное сообщение + `MessageUnpinnedEvent`. Idempotent: noop если не был закреплён |
| `ListPinnedMessages` | Пагинированный список закреплённых сообщений (sort by `PinnedAt DESC`). Soft-deleted фильтруются |
| `UnpinAll` | Снять все закрепы в чате одним вызовом. Одно системное сообщение + `AllMessagesUnpinnedEvent` |

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
- `NewMessageEvent` → [[Backend/Updates]] (отправка сообщения, создание группы, kick, pin/unpin/unpin-all системные сообщения)
- `MessageReadEvent` → [[Backend/Updates]] (MarkAsRead)
- `MessageEditedEvent` → [[Backend/Updates]] (EditMessage)
- `MessageDeletedEvent` → [[Backend/Updates]] (DeleteMessage)
- `MessagePinnedEvent` → [[Backend/Updates]] (PinMessage)
- `MessageUnpinnedEvent` → [[Backend/Updates]] (UnpinMessage; также при DeleteMessage если сообщение было закреплено)
- `AllMessagesUnpinnedEvent` → [[Backend/Updates]] (UnpinAll)

**Потребляет:**
- `user-changed-name-messages` → `UserChangedNameConsumer` → Redis-кеш имён
- `user-changed-avatar-messages` → `UserChangedAvatarConsumer` → Redis-кеш аватаров
- `session-revoked-messages` → `SessionRevokedConsumer` → инвалидация токена сессии (`TokenRevocationCache`, XAuth)

### Redis-кеш

`ChatCache` (Scoped). Ключи: `chat_name_{chatId}_{userId}`, `chat_image_{chatId}_{userId}`. Префикс: `Messages_`.

### Redis-буфер секретных чатов

`SecretMessageBuffer` (Singleton, через `IConnectionMultiplexer` напрямую — не через IDistributedCache). TTL 24 часа.

| Ключ | Назначение |
|------|-----------|
| `secret_msg:{recipientDeviceId}:{messageId}` | Сериализованный `SecretMessageRecord` (envelope + sender info) |
| `secret_msgs:{recipientDeviceId}` | Redis SET с messageId — индекс pending сообщений устройства |
| `secret_invite:{recipientDeviceId}:{inviteId}` | Сериализованный `SecretInviteRecord` (initial X3DH envelope) |
| `secret_invites:{recipientDeviceId}` | Redis SET с inviteId — индекс pending инвайтов |

API: `EnqueueMessageAsync` / `AckMessageAsync` / `ListPendingMessagesAsync` (со cleanup'ом expired); `EnqueueInviteAsync` / `ConsumeInviteAsync` (атомарно: GET → DEL+SREM) / `ListPendingInvitesAsync`.

Сериализация — System.Text.Json (byte[] → Base64). После Ack или Consume ключи удаляются из обоих STRING-ключа и SET-индекса.

## База данных

| Сущность | Важные детали |
|----------|---------------|
| `Chat` | `LastMessage`, `CountUnread`, `FirstUnreadMessageId` — вычисляются в рантайме, не в БД. `Type` (enum ChatType: Regular/Private/Secret, default=Regular). `KdfSalt` и `PassphraseVerifier` — bytea nullable, заполняются только для `Type=Private`. Чаты с `Type=Secret` сервер не материализует — поле существует только для совместимости proto |
| `ChatMember` | Индекс `(ChatId, UserId)`, каскадное удаление |
| `Message` | `Content` — owned type, `ReadBy` — PostgreSQL array, `IsDeleted`/`IsEdited` (bool, default=false), `EditedAt` (timestamptz nullable) |
| `EncryptedMessage` | Шифрованное сообщение приватного чата. Отдельная таблица `EncryptedMessages` (НЕ join с `Messages`). Поля: `Id` (bigserial), `ChatId`, `SenderId`, `SenderDeviceId` (Guid), `SentAt`, `Ciphertext`/`Nonce`/`AssociatedData` (bytea), `IsEdited`, `EditedAt`, `IsDeleted`. Soft-delete очищает все 3 bytea-поля. Индексы по `ChatId` и `(ChatId, SentAt)` |
| `MessageAttachment` | Owned collection в отдельной таблице `MessageAttachments` |
| `MessageAttachmentType` | Unknown, Image, Video, Gif, Document, Audio, Voice, Sticker, ForwardedMessage |
| `ForwardedMessageAttachment` | Owned collection в таблице `ForwardedMessageAttachments`; вложения внутри пересланного сообщения (без ForwardedMessage рекурсии) |
| `GroupChatInfo` | `UsersCanKick` — PostgreSQL array |
| `PinnedMessage` | Отдельная таблица: `Id`, `ChatId`, `MessageId`, `PinnerUserId`, `PinnedAt`. Уникальный индекс `(ChatId, MessageId)`. FK с каскадным удалением на `Chats` и `Messages` |

## Важные нюансы

- **Пересланные сообщения**: `MessageAttachmentType.ForwardedMessage` (8) — снапшот оригинала. `ForwardedAuthorName`, `ForwardedOriginalMessageId`, `ForwardedText` хранятся в `MessageAttachments`; вложения оригинала — в `ForwardedMessageAttachments`
- **Reply ≡ Forward на бэке**: отдельного поля `reply_to_message_id` нет. Клиенты (Android, WPF) различают reply/forward только на UI-уровне по эвристике "оригинал есть в текущей загруженной истории чата" (см. [[Клиенты/Android]] и [[Клиенты/Windows-WPF]]). Для бэкенда — это всегда `OutgoingMessage.forwarded_message_id`
- **ListChatAttachments без фильтра**: тип 8 (ForwardedMessage) исключён из медиа-галереи автоматически
- **Системные сообщения**: `MessageContentType.System` — для событий чата (создание группы, кик)
- **Маппинг файлов**: `MessageMapping.ToGrpc(filesInfoMap?)` — словарь `fileId → FileData` для вложений. Если не передан — поля preview/filename пустые
- **Пагинация**: `GetChatMessagesWithOffset` — двунаправленная загрузка вокруг `fromMessageId` (по 50 в каждую сторону)
- **Права кика**: только создатель группы и `GroupChatInfo.UsersCanKick`
- **Self-chat**: `CreatePersonChat(userId, userId)` — личный чат с самим собой поддерживается
- **Soft-delete**: удалённые сообщения остаются в БД, но скрыты везде в выдаче (`MessagesStorage`, `ChatsStorage` — фильтр `!IsDeleted`). `MarkAsRead` пропускает удалённые. Чат с единственным удалённым сообщением исчезает из `ListChats`
- **Edit-семантика**: при правке forward-вложения сохраняются как есть (Telegram-style), не-forward attachments полностью пересоздаются по новому списку `FileIds`. Forward-снапшоты не обновляются автоматически
- **Pin-права**: любой участник чата может закреплять/откреплять любые сообщения (общая для чата доска). Авторизация — `[Authorize(Policy = nameof(TokenType.User))]` + `CheckAccessToChat`. Лимит: 100 закрепов на чат → `TooManyPinnedMessagesException`
- **Pin + Soft-delete**: при `DeleteMessage` запись из `PinnedMessages` удаляется автоматически и публикуется `MessageUnpinnedEvent`. `ListPinnedMessages` дополнительно фильтрует через `!IsDeleted` (защита от рассинхрона)
- **Pin системные сообщения**: при pin/unpin/unpin-all создаётся системное сообщение `MessageContentType.System` с текстом «Пользователь {имя} закрепил/открепил…», публикуется `NewMessageEvent` параллельно с pin-событием

## Конфигурация

```
MessagesDb, Redis, RabbitMQ:*, UsersService:Host/Token, FilesService:Host/Token
```

## Proto

- `messages_api.proto` — Server
- `shared.proto` — None
- `users_api.proto` — Client
- `files_api.proto` — Client
