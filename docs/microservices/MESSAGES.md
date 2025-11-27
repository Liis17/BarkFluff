# Messages Microservice

## Назначение

Сервис Messages - это **ядро мессенджера**, отвечающее за:

- 💬 Личные чаты (DM - Direct Messages)
- 👥 Групповые чаты
- 📨 Отправку и получение сообщений
- 📎 Вложения (изображения, видео, GIF, документы)
- ✓✓ Статус прочтения (read receipts)
- 🔔 Подсчёт непрочитанных сообщений
- 👤 Управление участниками групповых чатов

**Порт**: 7006
**База данных**: PostgreSQL (`messages_db`)
**Кеш**: Redis (для имён/аватаров в DM)
**Зависимости**: Users service, Files service, Updates service (через RabbitMQ)

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Entity Framework Core 9.0.8**: ORM
- **PostgreSQL**: База данных (с массивами)
- **Redis** (StackExchange.Redis): Кеширование
- **RabbitMQ** (MassTransit): Pub/Sub
- **MediatR**: CQRS pattern

## Архитектура

```
┌────────────────────────────────────────────────┐
│          Messages Service                       │
├────────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐ │
│  │Features  │→ │ Storage  │→ │  PostgreSQL  │ │
│  │(7 шт.)   │  │          │  │  + Redis     │ │
│  └──────────┘  └──────────┘  └──────────────┘ │
│       │                                         │
│       ├─→ MessageQueueSender → RabbitMQ        │
│       │                                         │
│       └─→ Consumers (2 шт.) ← RabbitMQ         │
└────────────────────────────────────────────────┘
         │              │               │
         ↓              ↓               ↓
   ┌─────────┐   ┌─────────┐   ┌──────────┐
   │  Users  │   │  Files  │   │ Updates  │
   │(профили)│   │(вложения│   │(realtime)│
   └─────────┘   └─────────┘   └──────────┘
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **Chats** | Чаты (личные и групповые) |
| **Messages** | Сообщения в чатах |
| **MessageAttachments** | Вложения к сообщениям |
| **ChatMembers** | Участники чатов |
| **GroupChatInfos** | Метаданные групповых чатов (creator, admins) |

### Основные сущности

#### Chat
```csharp
public class Chat
{
    public Guid Id { get; set; }                    // UUID чата
    public string? Title { get; set; }              // Название (только для групповых)
    public string? Picture { get; set; }            // Обложка (только для групповых)
    public bool IsGroupChat { get; set; }           // Флаг группового чата
    public List<ChatMember> Members { get; set; }   // Участники

    // Computed properties (не в БД):
    public Message? LastMessage { get; set; }       // Последнее сообщение
    public int CountUnread { get; set; }            // Кол-во непрочитанных
}
```

#### Message
```csharp
public class Message
{
    public long Id { get; set; }                    // Auto-increment ID
    public long SenderId { get; set; }              // ID отправителя
    public List<long> ReadBy { get; set; }          // PostgreSQL array [1, 5, 7]
    public Guid ChatId { get; set; }                // UUID чата
    public DateTime SentAt { get; set; }            // Timestamp
    public MessageContent Content { get; set; }     // Owned entity
    public MessageContentType Type { get; set; }    // Generic/System
}
```

#### MessageContent (Owned Entity)
```csharp
public class MessageContent
{
    public string? Text { get; set; }                        // Текст сообщения
    public List<MessageAttachment>? Attachments { get; set; } // Вложения
}
```

**EF Core mapping**: Embedded в таблицу Messages (не отдельная таблица)

#### MessageAttachment
```csharp
public class MessageAttachment
{
    public long Id { get; set; }
    public MessageAttachmentType Type { get; set; }  // Image/Video/Gif/Document
    public string FileId { get; set; }               // Ссылка на Files service
    public string? PreviewUrl { get; set; }          // URL превью
    public long FileSize { get; set; }               // Размер в байтах
}
```

#### ChatMember
```csharp
public class ChatMember
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public Guid ChatId { get; set; }
    public DateTime JoinedAt { get; set; }
}
```

**Index**: Composite на `(ChatId, UserId)`

#### GroupChatInfo
```csharp
public class GroupChatInfo
{
    public long Id { get; set; }
    public Guid ChatId { get; set; }
    public long Creator { get; set; }                // Создатель группы
    public List<long> UsersCanKick { get; set; }     // Админы (PostgreSQL array)
    public DateTime CreatedAt { get; set; }
}
```

## Ключевые функции

### 1. Отправка сообщения

**Endpoint**: `SendMessage(chatId/userId, message)`

**Два режима**:
1. **В существующий чат**: `SendMessage(chatId="uuid", message)`
2. **Создание DM при необходимости**: `SendMessage(userId=123, message)`

**Процесс (режим 1)**:
```
1. Client → SendMessage(chatId, { text, fileIds })
2. Messages → Проверка доступа: userId in ChatMembers?
3. Messages → Files.GetFilesData(fileIds)  // Валидация вложений
4. Messages → Создание Message {
       SenderId: currentUserId,
       ReadBy: [currentUserId],  // Отправитель уже прочитал
       ChatId: chatId,
       SentAt: DateTime.UtcNow,
       Content: { text, attachments }
   }
5. Messages → PostgreSQL: INSERT INTO Messages
6. Messages → RabbitMQ.Publish(NewMessageEvent {
       ChatId, ChatMembers: [1,2,3], Message: <protobuf bytes>
   })
7. Client ← { message }
```

**Процесс (режим 2 - Auto-create DM)**:
```
1. Client → SendMessage(userId=123, message)
2. Messages → Users.GetById(123)  // Проверка существования
3. Messages → Поиск чата: GetUserChatIdWithPerson(123, currentUserId)
4. IF chat not found:
   ├─→ CreatePersonChat(currentUserId, 123)
   ├─→ Redis.Set("chat_name_{chatId}_{currentUserId}", "{FirstName} {LastName}")
   └─→ Redis.Set("chat_image_{chatId}_{currentUserId}", profilePictureUrl)
5. Messages → Создание сообщения (как в режиме 1)
```

### 2. Личные чаты (DM) vs Групповые чаты

#### Direct Messages

**Характеристики**:
- `IsGroupChat = false`
- Ровно 2 участника
- `Title` и `Picture` = null (вычисляются динамически)
- Нет GroupChatInfo

**Отображение**:
```
DM между User1 и User2:
┌─────────────────────┐
│ User1 видит:        │
│ Title = "User2"     │  ← из Users.GetById(User2)
│ Picture = avatar2   │  ← кешируется в Redis
└─────────────────────┘

┌─────────────────────┐
│ User2 видит:        │
│ Title = "User1"     │  ← из Users.GetById(User1)
│ Picture = avatar1   │  ← кешируется в Redis
└─────────────────────┘
```

**Redis кеш ключи**:
- `Messages_chat_name_{chatId}_{userId}` → "Иван Петров"
- `Messages_chat_image_{chatId}_{userId}` → "https://cdn.../avatar.jpg"

**Cache invalidation**: Через RabbitMQ consumers (UserChangedName, UserChangedAvatar)

#### Групповые чаты

**Характеристики**:
- `IsGroupChat = true`
- 2+ участников
- `Title` и `Picture` хранятся в БД
- Есть GroupChatInfo

**Создание**:
```
1. Client → CreateGroupChat(userIds=[2,3,4], title, pictureFileId)
2. Messages → Валидация: currentUserId in userIds?
3. Messages → Files.GetFileData(pictureFileId)  // Проверка типа
4. Messages → CreateGroupChat() + CreateGroupChatInfo() {
       Creator: currentUserId,
       UsersCanKick: [currentUserId]  // Создатель = админ
   }
5. Messages → Системное сообщение: "Создан групповой чат \"{title}\""
6. Messages → RabbitMQ.Publish(NewMessageEvent)
7. Client ← { createdChat }
```

**Удаление участника** (только админы):
```
1. Client → KickUser(chatId, userId)
2. Messages → Проверка: currentUserId in GroupChatInfo.UsersCanKick?
3. Messages → DELETE FROM ChatMembers WHERE UserId = userId
4. Messages → Системное сообщение:
       "Администратор {adminName} исключил пользователя {userName} из чата"
5. Messages → RabbitMQ.Publish(NewMessageEvent)
```

### 3. Вложения (Attachments)

**Поддерживаемые типы**:
```csharp
public enum MessageAttachmentType
{
    Unknown = 0,
    Image = 1,      // Изображения (JPEG, PNG, etc.)
    Video = 2,      // Видео (MP4, AVI, etc.)
    Gif = 3,        // Анимированные GIF
    Document = 4,   // Документы (PDF, DOCX, etc.)
}
```

**Процесс отправки с вложениями**:
```
1. Client → Files.GetUploadUrl(type=MessageAttachmentImage)
2. Client → HTTP POST /upload/{fileId} (загрузка файла)
3. Client → SendMessage(chatId, { text, fileIds: [fileId1, fileId2] })
4. Messages → Files.GetFilesData([fileId1, fileId2])
5. Messages → Валидация типов файлов
6. Messages → Создание MessageAttachment[] {
       { FileId: fileId1, Type: Image, FileSize: 2048576, PreviewUrl: "..." },
       { FileId: fileId2, Type: Document, FileSize: 1048576, PreviewUrl: null }
   }
7. Messages → INSERT INTO MessageAttachments
```

**Mapping типов** (Files → Messages):
```csharp
UploadFileType.MessageAttachmentImage  → MessageAttachmentType.Image
UploadFileType.MessageAttachmentVideo  → MessageAttachmentType.Video
UploadFileType.MessageAttachmentGif    → MessageAttachmentType.Gif
UploadFileType.MessageAttachmentDocument → MessageAttachmentType.Document
```

### 4. Статус прочтения (Read Receipts)

**Хранение**: PostgreSQL array в поле `Message.ReadBy`

```sql
-- Пример
Message { Id: 100, ReadBy: [1, 5, 7] }  -- Прочитали пользователи 1, 5, 7
```

**Отметка как прочитанное**:
```
Client → MarkAsRead(messageIds=[100, 101, 102])
         ↓
Messages → Проверка доступа к чатам
         → ExecuteUpdate:
           UPDATE Messages
           SET ReadBy = CASE
               WHEN ReadBy @> ARRAY[@userId]::bigint[]  -- Уже в массиве
               THEN ReadBy
               ELSE ReadBy || @userId  -- Добавить в массив
           END
           WHERE Id IN (100, 101, 102)
```

**Автоматическое прочтение**: Отправитель автоматически добавляется в `ReadBy`

**Подсчёт непрочитанных**:
```sql
SELECT COUNT(*) FROM Messages
WHERE ChatId = @chatId
  AND NOT (ReadBy @> ARRAY[@userId]::bigint[])  -- userId НЕ в массиве
```

### 5. Список чатов

**Endpoint**: `ListChats(pagination)`

**Возвращаемые данные**:
- Список чатов пользователя
- Последнее сообщение в каждом чате
- Количество непрочитанных
- Для DM: имя и аватар собеседника

**Процесс**:
```
1. Client → ListChats(offset=0, size=20)
2. Messages → SELECT Chats JOIN ChatMembers
              WHERE ChatMembers.UserId = @userId
              ORDER BY LastMessage.SentAt DESC
              LIMIT 20 OFFSET 0
3. Messages → Для каждого чата:
   ├─→ Вычисление CountUnread
   └─→ Получение LastMessage
4. Messages → Для DM чатов:
   ├─→ Redis.Get("chat_name_{chatId}_{userId}")
   ├─→ IF cache miss:
   │   ├─→ Определить собеседника (другой участник)
   │   ├─→ Users.GetById(собеседник)
   │   ├─→ Redis.Set("chat_name_...", "{FirstName} {LastName}")
   │   └─→ Redis.Set("chat_image_...", profilePictureUrl)
5. Client ← { chats[], totalCount }
```

## RabbitMQ Integration

### События, которые Messages **публикует**

#### NewMessageEvent
```json
{
  "ChatId": "uuid",
  "ChatMembers": [1, 2, 3, 5],  // Все участники чата
  "Message": "<protobuf bytes>"  // Сериализованное сообщение
}
```

**Когда**:
- Отправка сообщения (SendMessage)
- Создание группового чата (системное сообщение)
- Исключение из группового чата (системное сообщение)

**Кому**: Updates service → gRPC streaming → Connected clients

### События, которые Messages **потребляет**

#### 1. UserChangedName
**Queue**: `user-changed-name-messages`

**Обработка**:
```csharp
public async Task Consume(ConsumeContext<UserChangedName> context)
{
    // Найти все DM чаты с этим пользователем
    var chatsWithUser = await _chatsStorage.GetDmChatsWithUser(context.Message.UserId);

    foreach (var chat in chatsWithUser)
    {
        // Определить собеседника
        var opponent = chat.Members[0].UserId == context.Message.UserId
            ? chat.Members[1].UserId
            : chat.Members[0].UserId;

        // Обновить кеш имени для собеседника
        await _chatCache.SetChatName(chat.Id, opponent,
            $"{context.Message.NewFirstName} {context.Message.NewLastName}");
    }
}
```

#### 2. UserChangedAvatar
**Queue**: `user-changed-avatar-messages`

**Обработка**: Аналогично, обновление кеша аватара

## Зависимости

### Users Service (gRPC)

**Методы**:
- `GetByIdAsync(userId)` - получение имени и аватара для DM
- `ListByIdsAsync(userIds)` - массовое получение для ChatMembers

**Использование**:
- Отображение имён в чатах
- Валидация при отправке сообщения новому пользователю
- Системные сообщения (имена админа и kicked user)

### Files Service (gRPC)

**Методы**:
- `GetFilesDataAsync(fileIds)` - валидация вложений
- `GetFileDataAsync(fileId)` - проверка обложки группового чата

**Валидация**:
```csharp
// Проверка вложений
var filesInfo = await _filesServerApiClient.GetFilesDataAsync(...);
if (filesInfo.FilesInfos.Any(x => !_attachmentMap.ContainsKey(x.Type)))
    throw new FileNotSupportedException();

// Проверка обложки группы
var fileInfo = await _filesServerApiClient.GetFileDataAsync(...);
if (fileInfo.FileInfo.Type != UploadFileType.ChatPicture)
    throw new FileHasNotGroupPictureTypeException();
```

### Updates Service (RabbitMQ)

**Направление**: Messages → RabbitMQ → Updates → Clients

**Поток**:
```
Messages.SendMessage()
  → MessageQueueSender.Publish(NewMessageEvent)
    → RabbitMQ
      → Updates.NewMessageConsumer()
        → StreamSubscriptionsManager
          → gRPC Stream.WriteAsync()
            → Connected Clients
```

## Конфигурация

### appsettings.json

```json
{
  "MessagesDb": "Host=postgres;Database=messages_db;...",
  "Redis": "redis:6379",
  "UsersService": {
    "Host": "http://users:7002",
    "Token": "service-token"
  },
  "FilesService": {
    "Host": "http://files:7005",
    "Token": "service-token"
  },
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  }
}
```

### Redis Cache Prefix

`Messages_` - автоматически добавляется к всем ключам

## API Reference

| Метод | Auth | Описание |
|-------|------|----------|
| `SendMessage(chatId/userId, message)` | ✅ User | Отправить сообщение |
| `ListMessages(chatId, fromMessageId, count)` | ✅ User | Получить историю (макс. 50) |
| `ListChats(pagination)` | ✅ User | Список чатов (макс. 50) |
| `ListChatMembers(chatId, pagination)` | ✅ User | Участники чата (макс. 50) |
| `CreateGroupChat(userIds, title, pictureFileId)` | ✅ User | Создать группу |
| `KickUser(chatId, userId)` | ✅ User | Исключить из группы (admin) |
| `MarkAsRead(messageIds)` | ✅ User | Отметить как прочитанное |

## Известные проблемы

### 🟡 Средние

1. **Pagination без курсора в ListChats**
   - Использует offset/limit
   - **Рекомендация**: Cursor-based pagination

2. **Нет удаления сообщений**
   - Отсутствует DeleteMessage
   - **Рекомендация**: Soft delete

3. **Нет редактирования сообщений**
   - **Рекомендация**: EditMessage с историей

## Troubleshooting

### Проблема: "No access to chat exception"

**Причина**: Пользователь не является участником чата

**Решение**: Проверить ChatMembers перед отправкой

### Проблема: DM чат показывает неправильное имя

**Причина**: Устаревший кеш в Redis

**Решение**: Проверить работу UserChangedNameConsumer

### Проблема: "From message not found exception"

**Причина**: Передан несуществующий fromMessageId

**Решение**: Использовать ID сообщения из этого чата

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Messages/`

**Ключевые файлы**:
- `Host/MessagesApiService.cs` - gRPC API
- `Features/*/` - 7 CQRS handlers
- `Persistence/Services/ChatsStorage.cs` - Chats repository
- `Persistence/Services/MessagesStorage.cs` - Messages repository
- `Persistence/Services/ChatCache.cs` - Redis cache
- `Infrastructure/MessageQueueSender.cs` - RabbitMQ publisher
- `Consumers/UserChangedNameConsumer.cs` - Name update consumer
- `Consumers/UserChangedAvatarConsumer.cs` - Avatar update consumer
