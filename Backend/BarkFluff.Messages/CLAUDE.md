# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Описание

Микросервис `Messages` управляет чатами, сообщениями и вложениями в BarkFluff. Порт: **7007**.

## Сборка и запуск

```bash
# Сборка
dotnet build BarkFluff.Messages.csproj

# Запуск через Docker (из корня Backend/)
docker-compose -f docker-compose-dev.yml up -d messages

# Миграции применяются автоматически при старте через Program.cs
```

## Архитектура

### CQRS через MediatR

Каждая операция — отдельная папка в `Features/`:
- `SendMessage` — отправка в чат или DM (авто-создаёт личный чат)
- `ListChats` — список чатов с пагинацией, имена/аватары из Redis или Users API
- `ListMessages` — двунаправленная пагинация (до 50 сообщений в каждую сторону)
- `CreateGroupChat` — создание группы с системным сообщением
- `KickUser` — исключение с проверкой прав, системное сообщение
- `MarkAsRead` — PostgreSQL array операции, публикует `MessageReadEvent`
- `GetPersonChatId` — получить или создать личный чат (поддерживает self-chat)
- `GetChatInfo` — счётчик непрочитанных, последнее сообщение
- `ListChatMembers` — пагинированный список с данными из Users API
- `ListChatAttachments` — вложения по типу, фильтрация + сортировка
- `GetUserAllMessages` (service-only) — экспорт данных пользователя (GDPR)

### gRPC сервисы

| Сервис | Авторизация | Назначение |
|--------|-------------|------------|
| `MessagesApiService` | `TokenType.User` | Клиентский API (10 методов) |
| `MessagesServerApiService` | `TokenType.Service` | Межсервисный API (экспорт данных) |

### Исходящие gRPC клиенты

- **Users** (`UsersServerApi.UsersServerApiClient`) — `GetByIdAsync`, `ListByIdsAsync` для имён/аватаров
- **Files** (`FilesServerApi.FilesServerApiClient`) — `GetFilesDataAsync`, `GetFileDataAsync` для метаданных вложений

Токены: `UsersService:Token`, `FilesService:Token`

### RabbitMQ

**Публикует:**
- `NewMessageEvent` → потребляет микросервис `Updates` (при отправке сообщения, создании группы, kick)
- `MessageReadEvent` → потребляет микросервис `Updates` (при `MarkAsRead`)

**Потребляет:**
- `user-changed-name-messages` → `UserChangedNameConsumer` → обновляет Redis-кеш имён
- `user-changed-avatar-messages` → `UserChangedAvatarConsumer` → обновляет Redis-кеш аватаров

### Redis-кеш

`ChatCache` (Scoped) хранит имена и аватары для DM-чатов:
- Ключи: `chat_name_{chatId}_{userId}`, `chat_image_{chatId}_{userId}`
- Префикс инстанса: `Messages_`

## База данных

**DbContext**: `MessagesContext` (PostgreSQL)

| Сущность | Важные детали |
|----------|---------------|
| `Chat` | `LastMessage`, `CountUnread`, `FirstUnreadMessageId` — игнорируются в БД, вычисляются в рантайме |
| `ChatMember` | Составной индекс `(ChatId, UserId)`, каскадное удаление |
| `Message` | `Content` — owned type, `ReadBy` — PostgreSQL array |
| `MessageAttachment` | Owned collection в отдельной таблице `MessageAttachments` |
| `MessageAttachmentType` | Unknown, Image, Video, Gif, Document, Audio, Voice, Sticker |
| `GroupChatInfo` | Хранит создателя и список `UsersCanKick` (PostgreSQL array) |

## Конфигурация

Все параметры загружаются из Configuration service (`ServiceId.Messages`):

```
MessagesDb              # PostgreSQL connection string
Redis                   # Redis host
RabbitMQ:Host/Username/Password
UsersService:Host       # e.g. https://users:7001
UsersService:Token
FilesService:Host       # e.g. https://files:7005
FilesService:Token
```

## Proto файлы

- `messages_api.proto` — `GrpcServices="Server"` (основной интерфейс)
- `shared.proto` — `GrpcServices="None"` (общие типы)
- `users_api.proto` — `GrpcServices="Client"`
- `files_api.proto` — `GrpcServices="Client"`

## Важные нюансы

- **Системные сообщения**: `MessageContentType.System` используется для событий чата (создание группы, кик). Создаются внутри хендлеров, не через gRPC.
- **Маппинг с файлами**: `MessageMapping.ToGrpc(filesInfoMap?)` принимает словарь `fileId → FileData` для обогащения вложений метаданными. Если словарь не передан — поля preview/filename пустые.
- **Пагинация сообщений**: `GetChatMessagesWithOffset` поддерживает двунаправленную загрузку вокруг опорного `fromMessageId` (по 50 в каждую сторону).
- **Права кика**: Только создатель группы и пользователи из `GroupChatInfo.UsersCanKick` могут удалять участников.
- **Self-chat**: `CreatePersonChat(userId, userId)` — личный чат с самим собой — поддерживается.
