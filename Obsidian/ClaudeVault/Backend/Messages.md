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
| `DeleteMessage` | Soft-delete своего сообщения (`IsDeleted=true`). Системные нельзя. Повторное удаление — idempotent no-op. Публикует `MessageDeletedEvent` |
| `ListChats` | Список непустых чатов с пагинацией по последнему неудалённому сообщению (`last_message.SentAt DESC`); имена/аватары из Redis или Users API (недостающие добираются одним батч-вызовом `ListByIds`, не GetById на каждый чат). Приватные Pending-чаты видны и приглашённому (по `PrivateUserLowId/HighId`, он ещё не member); Rejected — только инициатору. В proto `Chat.private_inviter_user_id=16` — инициатор инвайта (вычисляется: до Accept единственный реальный member = инициатор), клиент по нему определяет роль |
| `ListMessages` | Двунаправленная пагинация (до 50 в каждую сторону) |
| `CreateGroupChat` | Создание группы с системным сообщением |
| `KickUser` | Исключение с проверкой прав, системное сообщение |
| `AddUser` | Добавление участника в группу (зеркало `KickUser`): проверка прав по `GroupChatInfo.UsersCanKick`, проверка что не состоит (`UserAlreadyMemberChatException`), системное сообщение, рассылка членам + новому |
| `UpdateGroupChat` | Смена названия и/или аватара группы. Права по `GroupChatInfo.UsersCanKick`. Аватар валидируется через Files (`UploadFileType.ChatPicture`→URL). Системное сообщение, рассылка, возвращает обновлённый `Chat` |
| `MarkAsRead` | PostgreSQL array операции; публикует `MessageReadEvent` только при первом прочтении конкретным пользователем. `NewReadBy` сохраняет полный snapshot читателей для клиентов, `NewReaders` содержит только новых читателей для push-побочных эффектов. Повторный вызов идемпотентен и событие не создаёт |
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
