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
| `ListChats` | Список непустых чатов с пагинацией по последнему неудалённому сообщению (`last_message.SentAt DESC`); имена/аватары из Redis или Users API (недостающие добираются одним батч-вызовом `ListByIds`, не GetById на каждый чат). Приватные Pending-чаты видны и приглашённому (по `PrivateUserLowId/HighId`, он ещё не member); Rejected — только инициатору. В proto `Chat.private_inviter_user_id=16` — инициатор инвайта (вычисляется: до Accept единственный реальный member = инициатор), клиент по нему определяет роль |
| `ListMessages` | Двунаправленная пагинация (до 50 в каждую сторону) |
| `CreateGroupChat` | Создание группы с системным сообщением |
| `KickUser` | Исключение с проверкой прав, системное сообщение |
| `AddUser` | Добавление участника в группу (зеркало `KickUser`): проверка прав по `GroupChatInfo.UsersCanKick`, проверка что не состоит (`UserAlreadyMemberChatException`), системное сообщение, рассылка членам + новому |
| `UpdateGroupChat` | Смена названия и/или аватара группы. Права по `GroupChatInfo.UsersCanKick`. Аватар валидируется через Files (`UploadFileType.ChatPicture`→URL). Системное сообщение, рассылка, возвращает обновлённый `Chat` |
| `MarkAsRead` | PostgreSQL array операции, публикует `MessageReadEvent` |
| `GetPersonChatId` | Получить или создать обычный личный чат (поддерживает self-chat); при дублях выбирает чат с самым свежим неудалённым сообщением |
| `GetChatInfo` | Счётчик непрочитанных, последнее сообщение |
| `ListChatMembers` | Пагинированный список с данными из Users API |
| `ListChatAttachments` | Вложения по типу, фильтрация + сортировка |
| `GetUserAllMessages` | Service-only: экспорт данных пользователя (GDPR) |
| `CheckChatMembership` | Service-only: батч-проверка членства (`user_id` + `chat_ids[]` → подмножество, где состоит). Невалидные Guid отбрасываются. Использует `ChatsStorage.GetMemberChatIds`. Потребитель — [[Backend/Onliner]] (typing) |
| `GetChatMemberIds` | Service-only: все `UserId` участников чата по `ChatId`. Невалидный Guid → пустой список. Потребитель — [[Backend/Calls]] (ринг группового звонка) |
| `PostCallSystemMessage` | Service-only: пишет системное сообщение об итоге звонка (`CallSystemResult`: Ended/Missed/Rejected) в существующий чат — групповой по `ChatId` или личный по паре Caller/Callee (чат не создаётся, если его нет). Публикует `NewMessageEvent`. Потребитель — [[Backend/Calls]] |
| `PinMessage` | Закрепление сообщения в чате. Любой участник чата. Лимит: 100 закрепов на чат. Системное сообщение + `MessagePinnedEvent`. Idempotent при повторе |
| `UnpinMessage` | Открепление сообщения. Любой участник. Системное сообщение + `MessageUnpinnedEvent`. Idempotent: noop если не был закреплён |
| `ListPinnedMessages` | Пагинированный список закреплённых сообщений (sort by `PinnedAt DESC`). Soft-deleted фильтруются |
| `UnpinAll` | Снять все закрепы в чате одним вызовом. Одно системное сообщение + `AllMessagesUnpinnedEvent` |
| `CreatePrivateChat` | Создать приватный чат (E2E через passphrase). Сохраняет KdfSalt+PassphraseVerifier, добавляет инициатора участником, кладёт invitee в Redis (`PrivateChatInviteStore`), публикует `PrivateChatInviteEvent`. Валидация salt 16-64Б, verifier 16-128Б |
| `AcceptPrivateChat` | Приглашённый присоединяется к приватному чату: добавляет себя в `ChatMembers`, ставит `PrivateInviteState=Accepted`, удаляет Redis-invite, публикует `PrivateChatInviteResolutionEvent(accepted=true)` |
| `RejectPrivateChat` | Отклонить инвайт: ставит `PrivateInviteState=Rejected` (чат сохраняется — инициатор видит «запрос отклонён», у приглашённого исчезает из списка), удаляет Redis-invite, публикует `PrivateChatInviteResolutionEvent(accepted=false)` |
| `SendPrivateMessage` | Отправить шифротекст в приватный чат через `EncryptedMessagesStorage`. Требует DeviceId в JWT. Лимиты: ciphertext 1Б-64КиБ, nonce 12-32Б, AAD ≤ 4КиБ. Публикует `NewEncryptedMessageEvent` всем участникам чата |
| `ListPrivateMessages` | Двунаправленная пагинация шифрованных сообщений (до 50 в каждую сторону) через `EncryptedMessagesStorage.ListByChatAsync`. Soft-deleted отдаются с пустым ciphertext |
| `EditPrivateMessage` | Перезаписывает ciphertext+nonce+AAD своего сообщения, выставляет IsEdited+EditedAt. Публикует `EncryptedMessageEditedEvent` |
| `DeletePrivateMessage` | Soft-delete своего шифрованного сообщения (физически очищает все 3 bytea-поля). Публикует `EncryptedMessageDeletedEvent`. Idempotent |
| `MarkPrivateMessagesAsRead` | Сохраняет per-user `LastReadMessageId` для приватного чата и публикует `PrivateMessagesReadEvent`; ciphertext и plaintext не передаются |
| `SendSecretChatInvite` | Отправить инвайт секретного чата конкретному устройству. Кладёт opaque PreKeySignalMessage в `SecretMessageBuffer.EnqueueInviteAsync` (Redis 24ч), публикует `SecretChatInviteEvent` + silent push. Лимит envelope 32Б-16КиБ |
| `AcceptSecretChatInvite` | Принять инвайт на устройстве-получателе: атомарно `ConsumeInviteAsync`, публикует `SecretChatInviteResolutionEvent(accepted=true)` инициатору, опционально вкладывает первое ответное SignalMessage |
| `RejectSecretChatInvite` | Отклонить инвайт: `ConsumeInviteAsync`, публикует `SecretChatInviteResolutionEvent(accepted=false)` |
| `SendSecretMessage` | Отправить opaque envelope конкретному устройству через `SecretMessageBuffer.EnqueueMessageAsync` (Redis 24ч). Публикует `NewSecretMessageEvent` + silent push. Лимит envelope 16Б-16КиБ |
| `AckSecretMessage` | Подтвердить доставку секретного сообщения — `SecretMessageBuffer.AckMessageAsync(deviceId, messageId)`. Idempotent |

### gRPC-сервисы

| Сервис | Авторизация |
|--------|-------------|
| `MessagesApiService` | `TokenType.User` — клиентский API |
| `MessagesServerApiService` | `TokenType.Service` — межсервисный API |

`MessagesServerApi.SendMessageServer(sender_user_id, oneof chat_id/user_id, OutgoingMessage, allow_chat_creation)` — отправка от имени пользователя (вызывает [[Backend/Bots]]). `SendMessageCommand.SenderId` параметризует отправителя (null = клиентский путь из UserContext); переиспользуется вся логика (вложения, лимиты, `NewMessageEvent`). В серверном пути **авто-создание личного чата запрещено** (бот не пишет первым), кроме `allow_chat_creation=true` (системные боты, login-notifier). Членство отправителя в чате проверяется как обычно (`CheckAccessToChat` с senderId).

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
- `NewEncryptedMessageEvent` → [[Backend/Updates]] (SendPrivateMessage; user-scope)
- `EncryptedMessageEditedEvent` → [[Backend/Updates]] (EditPrivateMessage; user-scope)
- `EncryptedMessageDeletedEvent` → [[Backend/Updates]] (DeletePrivateMessage; user-scope)
- `PrivateChatInviteEvent` → [[Backend/Updates]] (CreatePrivateChat; адресовано приглашённому)
- `PrivateChatInviteResolutionEvent` → [[Backend/Updates]] (AcceptPrivateChat / RejectPrivateChat; адресовано инициатору)
- `NewSecretMessageEvent` → [[Backend/Updates]] (SendSecretMessage; **device-scope**)
- `SecretChatInviteEvent` → [[Backend/Updates]] (SendSecretChatInvite; **device-scope**)
- `SecretChatInviteResolutionEvent` → [[Backend/Updates]] (Accept/Reject секретного инвайта; **device-scope** инициатора)
- `PushNotificationEvent` → CloudMessaging (silent push при SendSecretChatInvite/SendSecretMessage — без content)

**Потребляет:**
- `user-changed-name-messages` → `UserChangedNameConsumer` → Redis-кеш имён
- `user-changed-avatar-messages` → `UserChangedAvatarConsumer` → Redis-кеш аватаров
- `session-revoked-messages` → `SessionRevokedConsumer` → инвалидация токена сессии (`TokenRevocationCache`, XAuth)

### Redis-кеш

`ChatCache` (Scoped). Ключи: `chat_name_{chatId}_{userId}`, `chat_image_{chatId}_{userId}`. Префикс: `Messages_`.

### Redis-стор pending-инвайтов приватных чатов

`PrivateChatInviteStore` (Singleton, через `IConnectionMultiplexer`). Хранит «кому отправлен invite, который ещё не принят». Один ключ STRING, без TTL.

| Ключ | Значение |
|------|----------|
| `private_invite:{chatId}` | UserId приглашённого (long) |

API: `SetAsync(chatId, inviteeUserId)` / `GetInviteeAsync(chatId)` / `RemoveAsync(chatId)`.

После Accept invitee добавляется в `ChatMembers` и ключ удаляется. После Reject — ключ удаляется и сам Chat удаляется через `ChatsStorage.DeleteChat`.

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
| `Message` | `Content` — owned type, `ReadBy` — PostgreSQL array, `IsDeleted`/`IsEdited` (bool, default=false), `EditedAt` (timestamptz nullable). Индекс `(ChatId, SentAt)` — обязателен: все выборки сообщений фильтруют по `ChatId` и сортируют по `SentAt`, без него seq scan |
| `EncryptedMessage` | Шифрованное сообщение приватного чата. Отдельная таблица `EncryptedMessages` (НЕ join с `Messages`). Поля: `Id` (bigserial), `ChatId`, `SenderId`, `SenderDeviceId` (Guid), `SentAt`, `Ciphertext`/`Nonce`/`AssociatedData` (bytea), `IsEdited`, `EditedAt`, `IsDeleted`. Soft-delete очищает все 3 bytea-поля. Индексы по `ChatId` и `(ChatId, SentAt)` |
| `PrivateChatReadState` | Составной ключ `(ChatId, UserId)`, хранит только ID последнего прочитанного зашифрованного сообщения; используется для `count_unread` без раскрытия содержимого |
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
- **Личные чаты**: `GetPersonChatId` и отправка по `user_id` ищут только `ChatType.Regular` DM с ровно двумя участниками. Если в БД есть дубли обычных DM между теми же пользователями, выбирается чат с самым свежим неудалённым сообщением.
- **Приватные чаты**: уникальны по нормализованной паре пользователей на уровне БД. Повторный `CreatePrivateChat` возвращает тот же `Chat` с `created=false`. `ListChats` включает приватные чаты (в том числе пустые pending), сортирует их по последнему `EncryptedMessage` или времени создания и не возвращает plaintext-превью.
- **Soft-delete**: удалённые сообщения остаются в БД, но скрыты везде в выдаче (`MessagesStorage`, `ChatsStorage` — фильтр `!IsDeleted`). `MarkAsRead` пропускает удалённые. Чат с единственным удалённым сообщением исчезает из `ListChats`. Пустые чаты отфильтровываются до пагинации, чтобы страницы списка чатов были стабильными для всех клиентов.
- **Edit-семантика**: при правке forward-вложения сохраняются как есть (Telegram-style), не-forward attachments полностью пересоздаются по новому списку `FileIds`. Forward-снапшоты не обновляются автоматически
- **Pin-права**: любой участник чата может закреплять/откреплять любые сообщения (общая для чата доска). Авторизация — `[Authorize(Policy = nameof(TokenType.User))]` + `CheckAccessToChat`. Лимит: 100 закрепов на чат → `TooManyPinnedMessagesException`
- **Pin + Soft-delete**: при `DeleteMessage` запись из `PinnedMessages` удаляется автоматически и публикуется `MessageUnpinnedEvent`. `ListPinnedMessages` дополнительно фильтрует через `!IsDeleted` (защита от рассинхрона)
- **Pin системные сообщения**: при pin/unpin/unpin-all создаётся системное сообщение `MessageContentType.System` с текстом «Пользователь {имя} закрепил/открепил…», публикуется `NewMessageEvent` параллельно с pin-событием

## Конфигурация

```
MessagesDb, Redis, RabbitMQ:*, UsersService:Host/Token, FilesService:Host/Token
```

## Флаг muted в chat-info

`Chat` (ListChats) и `GetChatInfoResponse` (GetChatInfo) содержат `muted` (+ `muted_until`) — отключены ли уведомления чата для текущего пользователя. Заполняется через батч-вызов `UsersServerApi.GetMutedChatIds(userId, chatIds)` (см. [[Backend/Users]] → Per-chat mute). Для сервисного токена (вызов из CloudMessaging) флаг не считается. Клиенты используют флаг, чтобы не показывать локальное уведомление.

## Proto

- `messages_api.proto` — Server
- `shared.proto` — None
- `users_api.proto` — Client
- `files_api.proto` — Client
