# BarkFluff.Messages — Карта проекта

Расположение: `Backend/BarkFluff.Messages/`
Порт: **7007** | База: PostgreSQL (`MessagesDb`) | Кеш: Redis (`Messages_`) | Очередь: RabbitMQ (MassTransit)

---

## Точка входа

| Файл | Что делает |
|------|-----------|
| `Program.cs` | Настройка DI, gRPC, EF Core, Redis, MassTransit (consumers), gRPC-клиенты Users/Files, Serilog, XAuth |
| `appsettings.json` / `appsettings.Development.json` | Конфигурация подключений (DB, Redis, RabbitMQ, сервисы) |
| `Dockerfile` / `Dockerfile.slim` | Образы для запуска сервиса в контейнере |
| `SECURITY_AUDIT.md` | Аудит безопасности сервиса |

---

## Host — gRPC-сервисы

| Файл | Авторизация | Что делает |
|------|------------|-----------|
| `Host/MessagesApiService.cs` | `TokenType.User` | Клиентский API: ListChats, ListMessages, SendMessage, EditMessage, DeleteMessage, MarkAsRead, CreateGroupChat, KickUser, ListChatMembers, ListChatAttachments, GetPersonChatId, GetChatInfo, PinMessage, UnpinMessage, ListPinnedMessages, UnpinAll |
| `Host/MessagesServerApiService.cs` | `TokenType.Service` | Межсервисный API: GetUserAllMessages (GDPR-экспорт) |

---

## Features — CQRS (MediatR)

| Папка / Файлы | Что делает |
|--------------|-----------|
| `Features/SendMessage/SendMessageCommand.cs` | Команда отправки сообщения (chatId или userId получателя) |
| `Features/SendMessage/SendMessageCommandHandler.cs` | Обработчик: авто-создаёт DM если нужно, сохраняет сообщение, публикует `NewMessageEvent` |
| `Features/SendMessage/OutgoingMessage.cs` | DTO исходящего сообщения (Text, FileIds) |
| `Features/EditMessage/EditMessageCommand.cs` | Команда редактирования сообщения (MessageId, Text, FileIds) |
| `Features/EditMessage/EditMessageCommandHandler.cs` | Обработчик: проверяет владельца/тип/IsDeleted, сохраняет forwarded-вложения, перезаписывает не-forward attachments, выставляет IsEdited+EditedAt, публикует `MessageEditedEvent` |
| `Features/DeleteMessage/DeleteMessageCommand.cs` | Команда soft-delete сообщения |
| `Features/DeleteMessage/DeleteMessageCommandHandler.cs` | Обработчик: проверки владельца/типа, idempotent no-op для повторного удаления, выставляет IsDeleted=true, публикует `MessageDeletedEvent` |
| `Features/ListChats/ListChatsCommand.cs` | Команда запроса списка чатов с пагинацией |
| `Features/ListChats/ListChatsCommandHandler.cs` | Загружает чаты, подтягивает имена/аватары из Redis или Users API |
| `Features/ListMessages/ListMessagesCommand.cs` | Команда загрузки сообщений чата |
| `Features/ListMessages/ListMessagesCommandHandler.cs` | Двунаправленная пагинация вокруг `fromMessageId` (до 50 в каждую сторону), подтягивает данные файлов из Files API |
| `Features/CreateGroupChat/CreateGroupChatCommand.cs` | Команда создания группового чата |
| `Features/CreateGroupChat/CreateGroupChatCommandHandler.cs` | Создаёт чат, добавляет участников, отправляет системное сообщение, публикует `NewMessageEvent` |
| `Features/KickUser/KickUserCommand.cs` | Команда исключения участника из группы |
| `Features/KickUser/KickUserCommandHandler.cs` | Проверяет права (создатель / `UsersCanKick`), удаляет из чата, системное сообщение, `NewMessageEvent` |
| `Features/MarkAsRead/MarkAsReadCommand.cs` | Команда отметки сообщения как прочитанного |
| `Features/MarkAsRead/MarkAsReadCommandHandler.cs` | PostgreSQL array-операция добавления userId в `ReadBy`, публикует `MessageReadEvent` |
| `Features/GetPersonChatId/GetPersonChatIdCommand.cs` | Команда получения ID личного чата с пользователем |
| `Features/GetPersonChatId/GetPersonChatIdCommandHandler.cs` | Возвращает или создаёт DM (поддерживает self-chat) |
| `Features/GetChatInfo/GetChatInfoCommand.cs` | Команда получения информации о чате |
| `Features/GetChatInfo/GetChatInfoCommandHandler.cs` | Возвращает счётчик непрочитанных, последнее сообщение, данные участников |
| `Features/ListChatMembers/ListChatMembersCommand.cs` | Команда списка участников чата |
| `Features/ListChatMembers/ListChatMembersCommandHandler.cs` | Пагинированный список участников с данными профилей из Users API |
| `Features/ListChatAttachments/ListChatAttachmentsCommand.cs` | Команда списка вложений в чате |
| `Features/ListChatAttachments/ListChatAttachmentsCommandHandler.cs` | Фильтрация по типу вложения, сортировка, подтягивает данные файлов из Files API |
| `Features/ExportData/GetUserAllMessagesQuery.cs` | Service-only запрос всех сообщений пользователя |
| `Features/ExportData/GetUserAllMessagesQueryHandler.cs` | GDPR-экспорт: возвращает все сообщения и чаты пользователя |
| `Features/PinMessage/PinMessageCommand.cs` + `Handler.cs` | Закрепление сообщения: проверки доступа, лимит 100, idempotent, системное сообщение, `MessagePinnedEvent` |
| `Features/UnpinMessage/UnpinMessageCommand.cs` + `Handler.cs` | Открепление сообщения: idempotent если не закреплено, системное сообщение, `MessageUnpinnedEvent` |
| `Features/ListPinnedMessages/ListPinnedMessagesQuery.cs` + `Handler.cs` | Пагинированный список закреплённых; sort by `PinnedAt DESC`, фильтр `!IsDeleted`, files data из Files API |
| `Features/UnpinAll/UnpinAllCommand.cs` + `Handler.cs` | Массовый откреп всех закрепов в чате, одно системное сообщение, `AllMessagesUnpinnedEvent` |

---

## Domain — модели данных

| Файл | Что описывает |
|------|--------------|
| `Domain/Chat.cs` | Чат: Id, Title, Picture, IsGroupChat, Members, LastMessage, CountUnread, FirstUnreadMessageId |
| `Domain/ChatMember.cs` | Участник чата: ChatId, UserId, JoinedAt |
| `Domain/GroupChatInfo.cs` | Доп. данные группы: создатель, UsersCanKick (PostgreSQL array) |
| `Domain/Message.cs` | Сообщение: Id (long), SenderId, ChatId, SentAt, Content, Type, ReadBy (PostgreSQL array) |
| `Domain/MessageContent.cs` | Owned-тип: Text, Attachments |
| `Domain/MessageAttachment.cs` | Вложение: FileId, Type, PreviewUrl, FileSize |
| `Domain/MessageAttachmentType.cs` | Enum: Unknown, Image, Video, Gif, Document, Audio, Voice, Sticker |
| `Domain/MessageContentType.cs` | Enum: Default, System (системные сообщения чата) |
| `Domain/PinnedMessage.cs` | Закреплённое сообщение: Id, ChatId, MessageId, PinnerUserId, PinnedAt |
| `Domain/ChatType.cs` | Enum: Regular=0, Private=1, Secret=2. Используется в `Chat.Type` |
| `Domain/EncryptedMessage.cs` | Шифрованное сообщение приватного чата (отдельная таблица): Id, ChatId, SenderId, SenderDeviceId, SentAt, Ciphertext/Nonce/AssociatedData (bytea), IsEdited, EditedAt, IsDeleted |

---

## Infrastructure — публикация событий

| Файл | Что делает |
|------|-----------|
| `Infrastructure/MessageQueueSender.cs` | Публикует `NewMessageEvent`, `MessageEditedEvent`, `MessageDeletedEvent`, `MessagePinnedEvent`, `MessageUnpinnedEvent`, `AllMessagesUnpinnedEvent` в RabbitMQ → [[Backend/Updates]] |
| `Infrastructure/ReadByQueueSender.cs` | Публикует `MessageReadEvent` в RabbitMQ → [[Backend/Updates]] |

---

## Consumers — RabbitMQ

| Файл | Очередь | Что делает |
|------|---------|-----------|
| `Consumers/UserChangedNameConsumer.cs` | `user-changed-name-messages` | Обновляет Redis-кеш имён чатов при смене имени пользователя |
| `Consumers/UserChangedAvatarConsumer.cs` | `user-changed-avatar-messages` | Обновляет Redis-кеш аватаров чатов при смене аватара |
| `Consumers/SessionRevokedConsumer.cs` | `session-revoked-messages` | Инвалидирует токен сессии через `TokenRevocationCache` (XAuth) |

---

## Mapping — преобразование в gRPC

| Файл | Что делает |
|------|-----------|
| `Mapping/ChatMapping.cs` | `Domain.Chat` → proto `Chat` (с вложением LastMessage и данными участников) |
| `Mapping/ChatMemberMapping.cs` | `Domain.ChatMember` → proto `ChatMember` |
| `Mapping/MessageMapping.cs` | `Domain.Message` → proto `Message` (принимает `filesInfoMap` для подстановки данных файлов) |
| `Mapping/MessageContentMapping.cs` | `Domain.MessageContent` → proto `MessageContent` с вложениями |
| `Mapping/PinnedMessageMapping.cs` | `Domain.PinnedMessage` + `Domain.Message` → proto `PinnedMessageInfo` (с подстановкой filesInfoMap) |

---

## Persistence — хранилище данных

| Файл | Что делает |
|------|-----------|
| `Persistence/MessagesContext.cs` | EF Core DbContext: Chats, Messages, GroupChatInfos |
| `Persistence/Services/ChatsStorage.cs` | Запросы к БД: GetUserChats (с CountUnread, FirstUnreadMessageId, LastMessage), CreatePersonChat, GetUserChatIdWithPerson и др. |
| `Persistence/Services/MessagesStorage.cs` | Запросы к БД: GetChatMessages, GetChatMessagesWithOffset (двунаправленная пагинация), SaveMessage, GetChatAttachments, GetMessagesByIdsInChatAsync (для ListPinnedMessages) |
| `Persistence/Services/PinnedMessagesStorage.cs` | CRUD по закреплённым сообщениям: GetPinByMessageIdAsync, ListByChatAsync, CountByChatAsync, AddAsync, Remove, RemoveAllByChatAsync, RemoveByMessageIdAsync |
| `Persistence/Services/ChatCache.cs` | Redis-кеш имён и аватаров чатов. Ключи: `chat_name_{chatId}_{userId}`, `chat_image_{chatId}_{userId}` |
| `Persistence/Services/Dtos/ChatAttachmentDto.cs` | DTO для запроса вложений (MessageId, SenderId, SentAt, AttachmentId, Type, FileId, PreviewUrl, FileSize) |
| `Persistence/Exceptions/FromMessageNotFoundException.cs` | Исключение — стартовое сообщение пагинации не найдено |
| `Persistence/Configurations/ChatConfiguration.cs` | EF Fluent API для Chat |
| `Persistence/Configurations/ChatMemberConfiguration.cs` | EF Fluent API для ChatMember (индекс ChatId+UserId, каскадное удаление) |
| `Persistence/Configurations/MessageConfiguration.cs` | EF Fluent API для Message (owned MessageContent, отдельная таблица MessageAttachments) |
| `Persistence/Configurations/PinnedMessageConfiguration.cs` | EF Fluent API для PinnedMessage (уникальный индекс ChatId+MessageId, FK на Chats и Messages с каскадным удалением) |
| `Persistence/Configurations/EncryptedMessageConfiguration.cs` | EF Fluent API для EncryptedMessage (PK Id, индексы ChatId и (ChatId, SentAt), bytea-поля с дефолтами) |
| `Persistence/Services/EncryptedMessagesStorage.cs` | CRUD шифрованных сообщений: AddAsync, GetByIdAsync, ListByChatAsync (двунаправленная пагинация), EditAsync (выставляет IsEdited+EditedAt), SoftDeleteAsync (обнуляет ciphertext/nonce/AAD) |
| `Persistence/Services/SecretMessageBuffer.cs` | Redis-буфер секретных envelope и инвайтов на 24ч. Использует `IConnectionMultiplexer` напрямую. EnqueueMessageAsync/EnqueueInviteAsync, ListPending*, AckMessageAsync, ConsumeInviteAsync (атомарно DEL) |
| `Persistence/Services/Dtos/SecretMessageRecord.cs` | DTO для буфера: MessageId, SenderUserId, SenderDeviceId, RecipientDeviceId, Envelope, SentAt |
| `Persistence/Services/Dtos/SecretInviteRecord.cs` | DTO для буфера: InviteId, Sender/Recipient User+Device, InitialEnvelope, SentAt |

---

## Migrations

| Файл | Что добавляет |
|------|--------------|
| `Persistence/Migrations/20250518190106_AddMessages` | Начальная схема: чаты, участники, сообщения |
| `Persistence/Migrations/20250518232855_AddedJoinedAtProperty` | Поле `JoinedAt` для `ChatMember` |
| `Persistence/Migrations/20250519230454_AddMessageContent` | Owned type `MessageContent`, таблица `MessageAttachments` |
| `Persistence/Migrations/20250525182402_AddGroupChats` | Таблица `GroupChatInfo`, поле `UsersCanKick` |
| `Migrations/20250720120941_AddAttachmentsPreview` | Поле `PreviewUrl` и `FileSize` для вложений (вне папки Persistence — возможно ошибка размещения) |
| `Persistence/Migrations/20260509011814_AddPinnedMessages` | Таблица `PinnedMessages` с FK на Chats и Messages, уникальный индекс `(ChatId, MessageId)` |

---

## Proto-контракты

| Файл | Роль |
|------|------|
| `Shared/BarkFluff.Proto/messages_api.proto` | Основной контракт сервиса (Server) |
| `Shared/BarkFluff.Proto/shared.proto` | Общие типы (PageRequest и др.) |
| `Shared/BarkFluff.Proto/users_api.proto` | Client — запросы к Users (GetById, ListByIds) |
| `Shared/BarkFluff.Proto/files_api.proto` | Client — запросы к Files (GetFilesData, GetFileData) |

---

## Замечания по актуальности документации

- ✅ Архитектура CQRS, gRPC-сервисы, RabbitMQ — соответствуют коду
- ✅ Redis-кеш ключи и структура — актуальны  
- ✅ Список Features — полный и актуальный
- ⚠️ **`SessionRevokedConsumer`** — в `Messages.md` не упомянут (очередь `session-revoked-messages`, инвалидирует сессии через XAuth)
- ⚠️ **Миграция `20250720120941_AddAttachmentsPreview`** — лежит вне папки `Persistence/Migrations` (в корне `Migrations/`), возможно перемещена ошибочно
- ⚠️ **Фильтрация пустых чатов** в `ChatsStorage.GetUserChats` — чаты без сообщений (`LastMessage == null`) не возвращаются (поведение не задокументировано)
