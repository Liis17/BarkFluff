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

> Этап 0.4 rearch: `MessagesServerApi` получил 7 RPC-заглушек федеративного импорта/экспорта (`ImportFederatedChat/Message`, `ApplyFederatedEdit/Delete/Read`, `ExportChatEvents`, `CheckFileFederationAccess`) — реализация в Фазе 2, сейчас `Unimplemented`. `SendMessageRequest.source_id`/`GetPersonChatIdRequest` получили параллельные `user_uuid`-поля.

## Архитектура

### CQRS через MediatR

| Feature | Описание |
|---------|----------|
| `SendMessage` | Отправка в чат или DM (авто-создаёт личный чат). Лимиты: текст ≤ 4096 символов, ≤ 10 вложений |
| `EditMessage` | Правка своего сообщения: текст и/или список вложений. Forward-снапшот не редактируется. Системные нельзя. Выставляет `IsEdited`+`EditedAt`, публикует `MessageEditedEvent` |
| `DeleteMessage` | Soft-delete своего сообщения (`IsDeleted=true`) с очисткой `Content.Text` и записей `MessageAttachments`; файлы в [[Backend/Files]] остаются. Системные нельзя. Повторное удаление — idempotent no-op. Публикует `MessageDeletedEvent` |
| `ListChats` | Список непустых чатов с пагинацией по последнему неудалённому сообщению (`last_message.SentAt DESC`); имена/аватары из Redis или Users API (недостающие добираются одним батч-вызовом `ListByIds`, не GetById на каждый чат). Приватные Pending-чаты видны и приглашённому (по `PrivateUserLowId/HighId`, он ещё не member); Rejected — только инициатору. В proto `Chat.private_inviter_user_id=16` — инициатор инвайта (вычисляется: до Accept единственный реальный member = инициатор), клиент по нему определяет роль |
| `ListMessages` | Двунаправленная пагинация (до 50 в каждую сторону) |
| `CreateGroupChat` | Создание группы с системным сообщением |
| `KickUser` | Исключение с проверкой прав, системное сообщение |
| `AddUser` | Добавление участника в группу (зеркало `KickUser`): проверка прав по `GroupChatInfo.UsersCanKick`, проверка что не состоит (`UserAlreadyMemberChatException`), системное сообщение, рассылка членам + новому |
| `UpdateGroupChat` | Смена названия и/или аватара группы любым её участником. `GroupChatInfo.UsersCanKick` используется только для добавления и исключения участников. Аватар валидируется через Files (`UploadFileType.ChatPicture`→URL). Системное сообщение, рассылка, возвращает обновлённый `Chat` |
| `MarkAsRead` | PostgreSQL array операции; публикует `MessageReadEvent` только при первом прочтении конкретным пользователем. `NewReadBy` сохраняет полный snapshot читателей для клиентов, `NewReaders` содержит только новых читателей для push-побочных эффектов. Повторный вызов идемпотентен и событие не создаёт |
| `GetPersonChatId` | Получить или создать обычный личный чат (поддерживает self-chat); при дублях выбирает чат с самым свежим неудалённым сообщением |
| `GetChatInfo` | Счётчик непрочитанных, последнее сообщение |
| `ListChatMembers` | Пагинированный список с данными из Users API |
| `ListChatAttachments` | Вложения по типу, фильтрация + сортировка |
| `GetUserAllMessages` | Service-only: экспорт данных пользователя (GDPR) |
| `CheckChatMembership` | Service-only: батч-проверка членства (`user_id` **либо** `user_uuid` + `chat_ids[]` → подмножество, где состоит) + федеративный контекст чатов и `requester_uuid` (этап 4.1). Невалидные Guid отбрасываются. Использует `ChatsStorage.GetMembershipContext`. Потребители — [[Backend/Onliner]] (typing) и [[Backend/Federation]] (валидация входящего typing) |
| `CheckFederatedPresenceAccess` | Service-only: подмножество наших `user_uuids`, чей presence разрешено отдавать ноде `requesting_server` (этап 4.1). Зовёт только [[Backend/Federation]] своей ноды |
| `CheckFileFederationAccess` | Service-only: разрешено ли отдать файл ноде `requesting_server` (этап 3.2) — file_id должен быть во вложении активного fed-чата с этой нодой. Только НАШИ файлы (`OriginServer == null`). Зовёт только [[Backend/Federation]] своей ноды |
| `CheckFedFileUserAccess` | Service-only: вправе ли ПОЛЬЗОВАТЕЛЬ скачать federated-вложение (этап 3.3) + снапшот метаданных. Ищет и в forwarded-вложениях. Зовёт [[Backend/Files]] при выдаче capability-ссылки |
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
| `GetChatDraft` / `UpsertChatDraft` / `DeleteChatDraft` | Кросс-клиентский черновик обычного чата: текст ≤4096 и выбранный reply. Хранится по `(ChatId, UserId)`; Upsert создаёт новую revision, Delete удаляет только совпавшую версию после отправки. Private/Secret исключены |

### gRPC-сервисы

| Сервис | Авторизация |
|--------|-------------|
| `MessagesApiService` | `TokenType.User` — клиентский API |
| `MessagesServerApiService` | `TokenType.Service` — межсервисный API |

`MessagesServerApi.SendMessageServer(sender_user_id, oneof chat_id/user_id, OutgoingMessage, allow_chat_creation)` — отправка от имени пользователя (вызывает [[Backend/Bots]]). `SendMessageCommand.SenderId` параметризует отправителя (null = клиентский путь из UserContext); переиспользуется вся логика (вложения, лимиты, `NewMessageEvent`). В серверном пути **авто-создание личного чата запрещено** (бот не пишет первым), кроме `allow_chat_creation=true` (системные боты, login-notifier). Членство отправителя в чате проверяется как обычно (`CheckAccessToChat` с senderId).

`MessagesServerApi.EditMessageServer(sender_user_id, message_id, text, files_ids)` и `DeleteMessageServer(sender_user_id, message_id)` — правка и удаление от имени пользователя (вызывает [[Backend/Bots]]). Тот же приём, что у `SendMessageServer`: `EditMessageCommand.SenderId`/`DeleteMessageCommand.SenderId` параметризуют автора (null = клиентский путь из UserContext), вся остальная логика переиспользуется. **Проверка авторства не ослабляется** — `message.SenderId != senderId` по-прежнему даёт `NoPermissionException`, поэтому по чужому `message_id` бот ничего не сделает и `chat_id` в запросе не нужен.

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
- `PrivateMessagesReadEvent` → [[Backend/Updates]] (MarkPrivateMessagesAsRead; user-scope)
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
| `Chat` | `LastMessage`, `CountUnread`, `FirstUnreadMessageId` — вычисляются в рантайме, не в БД. `Type` (enum ChatType: Regular/Private/Secret, default=Regular). `KdfSalt` и `PassphraseVerifier` — bytea nullable, заполняются только для `Type=Private`. Чаты с `Type=Secret` сервер не материализует — поле существует только для совместимости proto. `CreatedAt` (timestamptz, default=UtcNow). `PrivateUserLowId`/`PrivateUserHighId` — нормализованная пара участников приватного чата (уникальный индекс). `PrivateInviteState` (enum: Pending/Accepted/Rejected, default=Pending) |
| `ChatMember` | Индекс `(ChatId, UserId)`, каскадное удаление. `UserUuid` (Guid?, nullable) — фаза 0 федерации, пока никем не заполняется. Proto `ChatMember` получил параллельные `user_uuid`(5)/`server_name`(6) — этап 0.4, пока не маппится из домена |
| `Message` | `Content` — owned type, `ReadBy` — PostgreSQL array, `IsDeleted`/`IsEdited` (bool, default=false), `EditedAt` (timestamptz nullable). Индекс `(ChatId, SentAt)` — обязателен: все выборки сообщений фильтруют по `ChatId` и сортируют по `SentAt`, без него seq scan. **Фаза 0 федерации**: `LastChangeAt` (timestamptz, NOT NULL) — единая UTC-метка последнего изменения, основа будущего LWW; ставится в `MessagesStorage.AddMessage` (=`SentAt`, покрывает все пути создания — обычные и системные сообщения), в `EditMessageCommandHandler` (=`EditedAt`), в `DeleteMessageCommandHandler` (=`UtcNow`); наружу пока не отдаётся. `FederatedId`/`SenderUuid` (Guid?, nullable) — пассивны, пока никем не заполняются; уникальный частичный индекс `(ChatId, FederatedId) WHERE FederatedId IS NOT NULL` — под будущую идемпотентность импорта |
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
- **Soft-delete**: удалённые сообщения остаются в БД, но скрыты везде в выдаче (`MessagesStorage`, `ChatsStorage` — фильтр `!IsDeleted`); при удалении очищается `Content.Text` и удаляются записи `MessageAttachments`, а файлы в [[Backend/Files]] не затрагиваются. `MarkAsRead` пропускает удалённые. Чат с единственным удалённым сообщением исчезает из `ListChats`. Пустые чаты отфильтровываются до пагинации, чтобы страницы списка чатов были стабильными для всех клиентов.
- **Edit-семантика**: при правке forward-вложения сохраняются как есть (Telegram-style), не-forward attachments полностью пересоздаются по новому списку `FileIds`. Forward-снапшоты не обновляются автоматически
- **Pin-права**: любой участник чата может закреплять/откреплять любые сообщения (общая для чата доска). Авторизация — `[Authorize(Policy = nameof(TokenType.User))]` + `CheckAccessToChat`. Лимит: 100 закрепов на чат → `TooManyPinnedMessagesException`
- **Pin + Soft-delete**: при `DeleteMessage` запись из `PinnedMessages` удаляется автоматически и публикуется `MessageUnpinnedEvent`. `ListPinnedMessages` дополнительно фильтрует через `!IsDeleted` (защита от рассинхрона)
- **Pin системные сообщения**: при pin/unpin/unpin-all создаётся системное сообщение `MessageContentType.System` с текстом «Пользователь {имя} закрепил/открепил…», публикуется `NewMessageEvent` параллельно с pin-событием

## Конфигурация

```
MessagesDb, Redis, RabbitMQ:*, UsersService:Host/Token, FilesService:Host/Token
```

## Флаг muted в chat-info

`Chat` (ListChats) и `GetChatInfoResponse` (GetChatInfo) содержат `muted` (+ `muted_until`) — отключены ли уведомления чата для текущего пользователя. Заполняется через батч-вызов `UsersServerApi.GetMutedChatIds(userId, chatIds)` (см. [[Backend/Users]] → Per-chat mute). При временных gRPC-ошибках `Unavailable`/`DeadlineExceeded`/`ResourceExhausted` `ListChats` деградирует до `Muted=false`, чтобы недоступность Users не ломала выдачу чатов. Для сервисного токена (вызов из CloudMessaging) флаг не считается. Клиенты используют флаг, чтобы не показывать локальное уведомление.

## Proto

- `messages_api.proto` — Server
- `shared.proto` — None
- `users_api.proto` — Client
- `files_api.proto` — Client

## Федеративные DM (этап 2.3, docs/rearch/05-chat-replication.md)

Каждый сервер хранит свою копию fed-DM (1-на-1 между нодами), синхронизируемую доставкой событий. Локальные чаты работают как раньше — все fed-ветки под `IsFederated=true`.

### Схема

- `Chats.IsFederated bool`, `Chats.FederatedStatus int` (Active=0/Rejected=1/Merged=2), `Chats.FederatedUuidLow/High uuid` — нормализованная пара UUID участников fed-DM (low < high по строковой форме `"D"` lowercase, ordinal — единый победитель на обеих нодах). Уникальный индекс пары только для `IsFederated AND FederatedStatus=0` (анти-дубль одновременного создания).
- `Messages.SenderId long NULL` (remote-автор не имеет локального аккаунта), `Messages.SenderUuid uuid NULL` (появился в 0.3, теперь заполняется при импорте).
- `ChatMembers.UserId long NULL` (remote-участник fed-DM), `ChatMembers.ServerName text NULL` (домен remote-участника; NULL для локальных).
- `FederatedMessageEvents(ChatId, FederatedId, EventBytes, ReceivedAt, OriginServer, EventId)` — wire-байты последнего применённого state-event (для catch-up 2.6: отдаётся с той же подписью origin). `OriginServer`/`EventId` (этап 2.4) — метка последнего применённого события для LWW tie-break последующих правок/удалений.
- `FederatedReadStates(ChatId, UserUuid, LastReadFederatedMessageId, ReadAt)` — этап 2.4: прочтения remote-участников fed-DM («прочитано до X»); локальные читатели остаются в `Message.ReadBy`.
- `ChatDrafts(ChatId, UserId, Text, ReplyToMessageId, UpdatedAt, Revision)` — серверные черновики обычных чатов с составным PK `(ChatId, UserId)` и каскадным удалением вместе с чатом. `ListChats` возвращает `Chat.has_draft` пакетно.

### Импорт (входящие fed-события)

- `MessagesServerApi.ImportFederatedChat` (Federation → Messages при `ChatCreatedPayload`): валидации → upsert initiator через `UsersServerApi.UpsertRemoteUsers` → анти-дубль UUID-пары → создание копии чата (`ChatsStorage.CreateFederatedChatAsync`).
- `MessagesServerApi.ImportFederatedMessage` (`NewMessagePayload`): проверки sender ∈ remote-members, clamp `origin_ts_ms`, лимиты контента → вставка `Message(SenderId=NULL, SenderUuid, FederatedId, LastChangeAt=origin_ts)` → запись `FederatedMessageEvents` → публикация `NewMessageEvent` (IsFederated=true, без remote-рассылки) для локальных Updates/CloudMessaging.

Валидации — в `Features.Federation.FederationImportValidator` (clamp метки, текст ≤ 4096, вложения ≤ 10, 512 МБ лимит). Все Fed-исключения в `Shared.Exceptions/Messages/` (`TimestampInFutureException`, `UnknownInviteeException`, `DuplicateFederatedDmException`, `ChatUnknownException`, `FederatedChatNotActiveException`, `RemoteUserNotResolvedException`, `FederatedGroupsNotSupported`, `RemoteProfileRejectedException`).

`ChatUnknownException` имеет `StatusCode.NotFound` → Federation мапит на RETRY (catch-up 2.6 дотянет) — для чата, которого ещё нет локально. `FederatedChatNotActiveException` (code-review rearch-phase2) — отдельно для чата, который ЕСТЬ, но `FederatedStatus != Active` (Rejected/Merged): permanent, `FailedPrecondition` → REJECTED, ретраить бессмысленно (статус уже не станет Active). Остальные валидационные — тоже `FailedPrecondition` → REJECTED (permanent).

### Исходящий путь

- `SendMessage(user_uuid)` / `GetPersonChatId(user_uuid)`: резолв через `UsersServerApi.GetUsersByUuid` — если remote, fed-ветка; если локальный → fallback на ordinary personal-чат.
- Fed-ветка: находит существующий fed-DM по UUID-паре или создаёт (`IsFirstMessageInChat=true` для консюмера). `Message` сохраняется с `FederatedId/SenderUuid/LastChangeAt`.
- `MessageQueueSender.SendFederatedMessage` публикует `NewMessageEvent` с расширенными полями 2.2 (`IsFederated=true`, `RemoteParticipants`, `FederatedId`, `SenderUuid`, `IsFirstMessageInChat`, `InitiatorUuid/InviteeUuid`, `SenderFid`).
- `NewMessageFederationConsumer` (Federation) парсит `byte[] Message` (proto `barkfluff.shared.Message`) и кладёт `Text` в `NewMessagePayload` (раньше в 2.2 был заглушкой). `ChatCreated` + `NewMessage` идут в outbox на ноду-партнёра.

### Маршрутизация Federation

`FederationS2SApiService.RouteToInternalAsync`: `ChatCreated` → `ImportFederatedChat`, `NewMessage` → `ImportFederatedMessage` (2.3); `MessageEdited` → `ApplyFederatedEdit`, `MessageDeleted` → `ApplyFederatedDelete`, `MessagesRead` → `ApplyFederatedRead` (2.4). gRPC-клиент `MessagesServerApi.MessagesServerApiClient` зарегистрирован в Program.cs Federation (`MessagesService:Host/Token`). Маппинг `RpcException.StatusCode → EventStatus`: `FailedPrecondition/InvalidArgument/PermissionDenied/AlreadyExists` → REJECTED; `NotFound/Unavailable/DeadlineExceeded/Aborted/Cancelled` → RETRY.

`ProfileChanged`/`UserDeactivated` → RETRY до 2.9.

### Выдача

- `Mapping.MessageMapping` отдаёт `federated_id`/`sender_uuid` (раньше поля в proto были, но не заполнялись); `federated_read_by` (repeated string uuid, этап 2.4) — remote-читатели, объединённые из `FederatedReadStates` (см. ниже).
- `Mapping.ChatMemberMapping` отдаёт `user_uuid`/`server_name` (для remote-участника; `user_id=0` если `UserId=NULL`).
- Все хендлеры, работающие с `chat.Members`, фильтруют remote-участников через расширение `ChatMemberExtensions.LocalUserIds()` (remote не получает внутренних Queue-событий).

## Edit/Delete/Read федеративных DM + LWW (этап 2.4, docs/rearch/05-chat-replication.md, docs/rearch/phase-2/step-2.4-edit-delete-read-lww.md)

### LWW-разрешение конфликтов

`Features.Federation.LwwResolver` — чистые функции без побочных эффектов (юнит-тесты в `LwwResolverTests`):
- `ShouldApplyMessageChange(...)` — для правки/удаления: `event.origin_ts_ms > local.LastChangeAt` → применить; меньше → игнор (ответ OK, не ошибка); равно → tie-break лексикографически `(origin_server, event_id)`; **если сообщение уже удалено локально — терминально, любое дальнейшее событие (правка ИЛИ повторное удаление) игнорируется независимо от меток**.
- `ShouldApplyRead(currentReadAt, incomingOriginTs)` — для прочтений: монотонное "более старое не откатывает более новое" (read-события идемпотентны по природе, полноценный tie-break не нужен).

Метка (origin_server, event_id) последнего применённого к сообщению события хранится в `FederatedMessageEvents.OriginServer/EventId` (обновляется на каждое успешное применение edit/delete) — источник для сравнения при следующем входящем событии.

### `ApplyFederatedEdit` / `ApplyFederatedDelete` (P2-02)

Обработчики (`Features.ApplyFederatedEdit`, `Features.ApplyFederatedDelete`) вызываются только Federation:
1. Чат неизвестен → `ChatUnknownException` (RETRY, catch-up 2.6 дотянет); чат есть, но `FederatedStatus != Active` → `FederatedChatNotActiveException` (permanent, не RETRY — статус уже не станет Active); сообщение по `(ChatId, FederatedId)` не найдено → `FederatedMessageUnknownException` (RETRY, catch-up 2.6 дотянет).
2. **P2-02**: payload `MessageEditedPayload`/`MessageDeletedPayload` не несёт identity автора (намеренно — он был бы attacker-controlled). Проверка — локально: `FederationImportValidator.ResolveHomeServer(chat, message.SenderUuid, ownServer)` резолвит домашнюю ноду автора ПРАВИМОГО сообщения по `ChatMember` этого же чата (свой сервер, если `ChatMember.ServerName` пусто — это локальный member; иначе `ChatMember.ServerName`) и сверяет с `ApplyFederatedEditRequest.origin_server` (заполняет Federation из уже проверенного XFed `x-bf-origin`, НЕ из payload). Несовпадение → `FederatedOriginMismatchException` (REJECTED). Закрывает оба вектора: чужая нода правит сообщение локального автора, и партнёрская нода правит сообщение не своего пользователя.
3. `LwwResolver.ShouldApplyMessageChange` → применить или проигнорировать (Applied=false в ответе, не исключение).
4. Обновление `FederatedMessageEvents` (событие-победитель заменяет предыдущее: `EventBytes`/`OriginServer`/`EventId`/`ReceivedAt`).
5. Публикация `MessageEditedEvent`/`MessageDeletedEvent` для локального Updates-фан-аута — `RemoteParticipants=[]` осознанно (событие пришло с той ноды, повторно отправлять его туда же не нужно; `NewMessageFederationConsumer`-семейство консюмеров игнорирует `RemoteParticipants.Count == 0`). Удаление дополнительно снимает локальный pin (`PinnedMessagesStorage.RemoveByMessageIdAsync` + `MessageUnpinnedEvent`) — pin не федерируется, но не может пережить удаление сообщения.

### `ApplyFederatedRead`

`Features.ApplyFederatedRead`: чат неизвестен → RETRY; чат есть, но не Active → `FederatedChatNotActiveException` (permanent); `reader_uuid` обязан быть remote-участником ЭТОГО чата и его домашняя нода == `origin_server` (тот же `ResolveHomeServer`, что и для edit/delete) → иначе `FederatedOriginMismatchException`. Upsert `FederatedReadStates` идемпотентно и монотонно (`FederatedReadStatesStorage.UpsertAsync` → `LwwResolver.ShouldApplyRead`). Если сообщение "до которого" ещё не импортировано локально (дыра, дотянется catch-up 2.6) — прочтение всё равно сохраняется, просто локальная рассылка `MessageReadEvent` пропускается (нечего показать).

### Исходящий путь (локальные edit/delete/read в fed-чате)

- `EditMessageCommandHandler`/`DeleteMessageCommandHandler`: если `message.FederatedId.HasValue` — собирают `RemoteParticipants` из `ChatMembers` чата и передают в `MessageQueueSender.SendEdited/SendDeleted` (те же методы, что и для локального фан-аута — федеративные поля теперь опциональные параметры, а не отдельные методы).
- `SendMessageCommandHandler`: путь "существующий fed-DM через `chat_id`" (не только явный `user_uuid` первого сообщения) теперь тоже помечает сообщение `FederatedId`/`SenderUuid` и строит `RemoteParticipants` — иначе второе и последующие сообщения переписки не федерировались бы вовсе. Признак fed-чата — наличие среди `ChatMembers` записи с `ServerName` (remote-участник).
- `MarkAsReadCommandHandler`: прочтение федерируется как "прочитано до X" (`up_to_federated_message_id`), а не по сообщению — среди нескольких прочитанных за раз в одном fed-чате выбирается одно сообщение-якорь (максимальный `Id`), только для него `ReadByQueueSender.SendEvent` получает fed-поля (`ReaderUuid`=UUID локального читателя в этом чате, `UpToFederatedMessageId`, `RemoteParticipants`).
- `MessageEditedFederationConsumer` (Federation) теперь извлекает `NewText` из `byte[] Message` (парсит `barkfluff.shared.Message`, как и `NewMessageFederationConsumer`) — раньше было заглушкой.

### Новые исключения

`FederatedOriginMismatchException` (`Shared.Exceptions/Messages/`, default `FailedPrecondition` → REJECTED) — P2-02: событие говорит не за своих.

## Privacy DenyFederatedDm + отказ до отправителя (этап 2.5, docs/rearch/phase-2/step-2.5-privacy-antispam.md)

- `ImportFederatedChatCommandHandler`: после идемпотентности (чат уже импортирован — пропускаем
  privacy-проверку, флаг влияет только на НОВЫЕ чаты) проверяет `invitee.DenyFederatedDm` (из
  `UsersServerApi.GetUsersByUuid`, см. [[Backend/Users]]) → `FederatedDmRejectedException`.
- `FederatedDmRejectedException` (`Shared.Exceptions/Messages/`) — единственное исключение в проекте
  с ErrorCode-**строкой**, а не GUID: `"FederatedDmRejected"` (не `default FailedPrecondition` по коду,
  сам код совпадает с `x-error-code`, который сверяет `OutboxDispatcher` origin-ноды — см.
  [[Backend/Federation]]).
- `FederatedChatRejectedConsumer` (MassTransit, очередь `federated-chat-rejected-messages`): получает
  `FederatedChatRejectedEvent` от Federation → `ChatsStorage.MarkFederatedChatRejectedAsync` ставит
  `Chat.FederatedStatus = Rejected` (idempotent: no-op, если чат не найден или уже не Active).
- `SendMessageCommandHandler`: для существующего чата дополнительно проверяет
  `ChatsStorage.GetFederatedStatusAsync` — `Rejected` → `FederatedDmRejectedException` (та же ошибка,
  что и при первичном отказе; повторная отправка в отклонённый чат не уходит в бесконечный RETRY через
  Federation, а падает сразу на этой ноде).

## Федеративный контекст членства и доступ к presence (этап 4.1, docs/rearch/phase-4/step-4.1-membership-federated.md)

Фаза 4 (presence/typing через границу ноды) требует от Messages двух вещей: понять, *куда* маршрутизировать typing, и решить, *кому* можно показывать наши онлайн-статусы. Оба ответа отдаёт `MessagesServerApi` — новых round-trip'ов в hot-path не добавилось.

### `CheckChatMembership` — расширение, а не второй RPC

Вызов и так делается на **каждом** typing-heartbeat, поэтому федеративная маршрутизация приезжает тем же ответом:

```protobuf
CheckChatMembershipRequest  { int64 user_id; repeated string chat_ids; string user_uuid; }
CheckChatMembershipResponse { repeated string member_chat_ids;
                              repeated FederatedChatContext federated_chats;
                              string requester_uuid; }
FederatedChatContext { string chat_id; repeated FederatedChatPeer peers; }
FederatedChatPeer    { string user_uuid; string server_name; }
```

- **Ветка идентификатора.** Заполнен `user_uuid` → членство ищется по `ChatMembers.UserUuid` (так проверяется remote-участник); иначе — по `UserId`, как раньше. Оба пусты → `InvalidArgument` (ошибка вызывающего, не «пустой ответ»). Валидацию и парсинг делает `MessagesServerApiService`, `CheckChatMembershipQuery` несёт уже `long? UserId` / `Guid? UserUuid`.
- **`federated_chats` только для активных fed-чатов** (`IsFederated && FederatedStatus == Active`). Нефедеративные в список не попадают вовсе: пустой список = «все чаты локальные» — рабочий случай подавляющего большинства вызовов. `Rejected`/`Merged` чат членство отдаёт (чат существует), но контекста для него нет — маршрутизировать typing туда уже нельзя.
- **`requester_uuid`** нужен typing-мосту (этап 4.4): Onliner знает только `long userId` печатающего, а через границу ноды уходит uuid. Для `user_id`-ветки берётся из `ChatMembers.UserUuid` найденного членства (у локального участника fed-чата он заполнен с 2.3), для `user_uuid`-ветки — эхо запрошенного. В Users за ним **не ходим** — это hot-path.
- **Обратная совместимость.** Старый вызов (`user_id` + `chat_ids`, чтение `member_chat_ids`) даёт ровно прежний результат; закреплено тестом `MessagesServerApiServiceTests.CheckChatMembership_OnlyUserId_MapsAsBefore`.

### `ChatsStorage.GetMembershipContext` — стоимость запроса

Заменяет `GetMemberChatIds` в этом пути. Запросов ровно столько, сколько нужно:

1. членство запрашивающего в указанных чатах + флаги `IsFederated`/`FederatedStatus` чата (по стоимости равен прежнему `GetMemberChatIds`);
2. remote-участники — **только** если среди чатов нашёлся активный федеративный.

Локальные и групповые чаты второй запрос не оплачивают, N+1 по чатам не возникает. Кеша членства нет намеренно: кеш без инвалидации на смене состава чата даёт утечку доступа.

### `CheckFederatedPresenceAccess` — проверка отношений (риск №42)

Без неё любая нода сети массово мониторит presence чужих пользователей, зная только UUID. Правило: uuid попадает в `allowed_user_uuids`, если существует чат с `IsFederated && FederatedStatus == Active`, где есть **наш** участник с этим `UserUuid` (`ServerName == NULL`) **и** участник с `ServerName == requesting_server`.

- Отсутствующий uuid и uuid без общего чата **неразличимы** — существование аккаунтов не светим (молча не включаем, не `PermissionDenied`).
- Батч: один SQL на весь список (`ChatsStorage.GetUuidsSharingFederatedChatWithServer`), без цикла по uuid.
- Лимит входа — константа `CheckFederatedPresenceAccessQuery.MaxUserUuids = 500`, превышение → `InvalidArgument`. Это вторая линия: основной лимит подписки живёт в [[Backend/Federation]] (этап 4.3).
- `requesting_server` канонизируется (`Trim().ToLowerInvariant()`) — `ChatMember.ServerName` хранится уже в канонической форме (2.3).

Это **не** privacy-фильтр: privacy (`OnlineVisibility`) применяет владелец данных — [[Backend/Onliner]], этап 4.2.

## Снапшот метаданных federated-вложений (этап 3.1, docs/rearch/phase-3/step-3.1-file-ref-snapshot.md)

**Файлы между нодами не реплицируются** — байты живут только на origin-ноде. Реплицируется снапшот метаданных, чтобы принимающая нода отрисовала сообщение (имя, размер, тип, превью, размеры картинки) **без единого сетевого похода** на чужую ноду. Сами байты тянутся отдельно и только когда пользователь реально открывает вложение (этапы 3.2/3.3).

### Колонки `MessageAttachments`

| Колонка | Смысл |
|---------|-------|
| `OriginServer` | NULL = локальный файл (прежнее поведение), NOT NULL = байты на origin |
| `FileName` | снапшот имени; у локальных filename по-прежнему из Files при рендере |
| `PreviewFileId`, `ImageWidth`, `ImageHeight` | остальной снапшот |

Плюс индекс `IX_MessageAttachments_FileId` — проверки доступа к fed-файлу (3.2/3.3) ищут вложение по `FileId`, без него это seq scan по всем вложениям ноды на каждое скачивание. Миграция — `20260728010000_AddFederatedAttachmentSnapshot`, backfill не нужен (существующие строки локальные, новые колонки NULL).

### Исходящий путь

`SendMessageCommandHandler` / `EditMessageCommandHandler` для fed-чата собирают `FederatedFileRefInfo[]` через `Features.Federation.FederatedAttachmentMapper` — **переиспользуя уже полученный ответ `GetFilesData`**, второго вызова ради федерации не делается. Список едет в `NewMessageEvent.FederatedAttachments` / `MessageEditedEvent.FederatedAttachments`, оттуда консюмеры [[Backend/Federation]] маппят его в `FederatedFileRef` S2S-события. Локальные `MessageAttachments` при отправке не меняются: снапшот-колонки заполняются только на приёме.

Forwarded-вложения в снапшот не попадают: forward-структура федерируется внутри самого сообщения и отдельным файловым ref'ом не является.

### Импорт

`FederatedAttachmentImporter` валидирует снапшот и превращает его в строки `MessageAttachments`. Снапшот приходит с чужой ноды, поэтому доверять ему нельзя — всё, что не проходит проверку, отклоняется **permanent** (`FederatedAttachmentInvalidException` → REJECTED), а не RETRY: повторная доставка того же битого события ничего не исправит и только зациклит outbox отправителя.

Проверяется: количество ≤ 10, `origin_server` непустой, `file_id`/`preview_file_id` парсятся как Guid (пустой preview допустим), `0 ≤ size_bytes ≤ 512 МБ`, `attachment_type` — известное значение enum, `filename` ≤ 255 символов.

`ApplyFederatedEdit` **пересоздаёт список целиком** (как локальная правка): старые строки очищаются in-place, новые вставляются. Валидация снапшота идёт до LWW-разрешения — битый снапшот отклоняется независимо от того, выиграет ли эта правка.

### Выдача

`MessageContentMapping`: вложение с `OriginServer != null` собирается **из снапшота**, `filesInfoMap` для него игнорируется даже если там случайно нашёлся тот же `file_id`. `preview_url` не заполняется — превью тянется с origin по требованию, контракт ссылки появится в 3.3.

Батч `GetFilesData` во **всех** путях выдачи фильтрует remote-вложения (`OriginServer == null`): `ListMessages`, `ListChats`, `ListChatAttachments`, `ListPinnedMessages`, `PinMessage`. Files о federated-файлах не спрашивают вообще — их там нет.

`shared.proto`: `MessageAttachment.origin_server = 11` (пусто = локальный) — нужен клиентам (Фаза 5) и temp-выдаче (3.3).

> Побочное наблюдение (не чинилось в этом этапе): при рендере **локальных** вложений `ImageWidth`/`ImageHeight` из `UploadFileInfo` теряются в маппинге. Дефект несвязанный и существовал до федерации.

### `CheckFileFederationAccess` — авторизация файла на уровне ноды (этап 3.2)

Знание `file_id` само по себе прав не даёт. Файл отдаётся ноде-партнёру, только если он вложен в чат с `IsFederated && FederatedStatus == Active`, среди участников которого есть `ChatMember.ServerName == requesting_server`.

- **Только наши файлы** (`OriginServer == null`): файл, пришедший с чужой ноды, мы не реэкспортируем — за ним следует идти на его origin.
- **Удалённые сообщения исключены**: после репликации delete (2.4) партнёр за таким файлом и не придёт.
- «Файла нет», «файл только в локальном чате» и «чат с другой нодой» снаружи **неразличимы** — иначе перебором file_id можно было бы выяснять, что у нас есть.
- `requesting_server` канонизируется (`Trim().ToLowerInvariant()`), запрос — `ChatsStorage.IsFileSharedWithServerAsync`, опирается на индекс `IX_MessageAttachments_FileId` из 3.1.
- Аватары этим RPC **не обслуживаются**: `UserAvatar` не является вложением сообщения, у него своя ветка по `AvatarVisibility` (этап 3.4).

Это первый из двух независимых уровней доступа; второй — проверка пользователя на принимающей ноде (этап 3.3). Ни один не доверяет другому.

### `CheckFedFileUserAccess` — авторизация файла на уровне пользователя (этап 3.3)

Второй, независимый от origin уровень: origin решает «этой ноде можно» (`CheckFileFederationAccess`, 3.2), мы — «этому пользователю можно». Ни один не доверяет другому.

Право даёт участие в чате, где лежит вложение. Ищем в двух местах:

1. обычные вложения — по паре `(OriginServer, FileId)`; совпадение `file_id` с локальным файлом доступа **не** даёт;
2. **форварднутые** — форварднувший пользователь легитимно видел файл, значит получатель форварда должен уметь его открыть.

Вместе с ответом отдаётся снапшот (3.1): имя для `Content-Disposition`, размер для отсечения по объёму — чтобы скачивание не ходило в Messages второй раз. У форварднутой копии имени нет (снапшот имени хранится только у оригинала).

Удалённые сообщения исключены.

> **Отклонения от плана 3.3.** (1) План предполагал, что forwarded-вложения лежат в jsonb и их придётся искать seq scan'ом — фактически это обычная таблица `ForwardedMessageAttachment` с FK, запрос обычный. (2) Форварднутая копия не несла `OriginServer` (3.1 форварды намеренно не трогал), из-за чего точное сопоставление forwarded fed-вложения с его origin было невозможно — колонка добавлена в 3.3 (`20260728030000_AddForwardedAttachmentOriginServer`) и заполняется при форварде.
