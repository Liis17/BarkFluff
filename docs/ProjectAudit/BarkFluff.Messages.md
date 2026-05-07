# Аудит проекта: BarkFluff.Messages

> **Дата аудита:** 2026-05-06
> **Ветка:** `dev`
> **Ревьювер:** BarkfluffAgent / GitHub Copilot
> **Расположение проекта:** `Backend/BarkFluff.Messages/`

---

---

## 🔴 Безопасность

### SEC-03 · Отсутствие ограничения на размер контента сообщения ✅ ИСПРАВЛЕНО

**Описание:**
При отправке сообщения (`SendMessage`) нет проверки максимальной длины текста (`request.Message.Text`). Злоумышленник может отправить сообщение с текстом в несколько мегабайт, что нагружает PostgreSQL, RabbitMQ (сериализация сообщения в `NewMessageEvent`), а также все клиенты, получающие это сообщение через стриминг.

**В чём конкретно проблема:**
В `SendMessageCommandHandler.Handle()` нет `MaxLength`-валидации на текст. В доменной модели `MessageContent.Text` не имеет ограничения длины, и в `MessageConfiguration` нет `.HasMaxLength(...)`.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs` : **66–76** и `Persistence/Configurations/MessageConfiguration.cs`

```csharp
// ❌ ПРОБЛЕМА: нет проверки длины текста
if (request.Message is null ||
    request.Message.Text is null &&
    request.Message.FileIds is null &&
    request.Message.ForwardedMessageId is null)
{
    throw new MessageNotContainContextException();
}
// text может быть строкой в 10 МБ — никакой проверки нет
```

```csharp
// ❌ MessageConfiguration — нет HasMaxLength для Text
contentBuilder.Property(c => c.Text); // без ограничения
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: валидация в handler'е
private const int MaxTextLength = 4096;

if (request.Message.Text is { Length: > MaxTextLength })
{
    throw new MessageTextTooLongException(); // добавить в BarkFluff.Shared.Exceptions
}

//в wpf клиенте было реализованно ограничение на одно сообщение, посмотри в нем это огранчение и примени такое же на бекенде
```

```csharp
// ✅ РЕШЕНИЕ: ограничение на уровне конфигурации EF Core
contentBuilder.Property(c => c.Text).HasMaxLength(4096);
```

---

### SEC-04 · Отсутствие ограничения на количество вложений в одном сообщении ✅ ИСПРАВЛЕНО

**Описание:**
Нет лимита на количество `FileIds` в одном сообщении. Пользователь может передать несколько сотен ID файлов, что вызовет одновременный gRPC-запрос к Files-сервису с огромным списком и потенциально положит оба сервиса.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs` : **160–192**

```csharp
// ❌ ПРОБЛЕМА: нет проверки числа вложений
if (request.Message.FileIds != null && request.Message.FileIds.Any())
{
    // может прийти 500+ FileIds — запрос к Files без ограничения
    var filesInfo = await _filesServerApiClient.GetFilesDataAsync(
        new GetFilesDataRequest { FileIds = { request.Message.FileIds.Select(x => x.ToString()) } });
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: проверка лимита вложений до запроса к Files
private const int MaxAttachmentsPerMessage = 10;

if (request.Message.FileIds is { } fileIds && fileIds.Count() > MaxAttachmentsPerMessage)
{
    throw new TooManyAttachmentsException();
}
```

---

## 🟡 Оптимизация

### OPT-02 · `CheckAccessToChat` загружает всю сущность `Chat` с `Include(Members)` — избыточный SELECT ✅ ИСПРАВЛЕНО

**Описание:**
`CheckAccessToChat` делает `Include(x => x.Members)` и загружает весь объект чата только для того, чтобы проверить наличие `userId` в списке участников. Для чатов с тысячами участников это существенная нагрузка.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/ChatsStorage.cs` : **80–90**

```csharp
// ❌ ПРОБЛЕМА: загружает всю сущность Chat + всех Members ради булевой проверки
public async Task<bool> CheckAccessToChat(Guid chatId, long userId)
{
    var chat = await _context.Chats
        .Include(x => x.Members)  // ❌ загружает все ChatMember-записи
        .FirstOrDefaultAsync(x => x.Id == chatId);

    if (chat is null) return false;

    return chat.Members.Any(x => x.UserId == userId);
}
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: EXISTS-запрос по ChatMembers без загрузки Chat
public async Task<bool> CheckAccessToChat(Guid chatId, long userId)
{
    return await _context.ChatMembers
        .AnyAsync(m => m.ChatId == chatId && m.UserId == userId);
}
//перепроверить что логика исправления верная

// Генерирует: SELECT EXISTS(SELECT 1 FROM "ChatMembers" WHERE "ChatId"=@chatId AND "UserId"=@userId)
// Индекс (ChatId, UserId) уже существует согласно конфигурации
```

---

 

### OPT-08 · `GetUserAllMessages` (ExportData): загрузка всех сообщений пользователя в память без стриминга ✅ ИСПРАВЛЕНО

**Описание:**
`GetUserAllMessagesQueryHandler` загружает все сообщения из всех чатов пользователя одним `ToListAsync()`. Для активного пользователя с историей в несколько лет — это десятки тысяч строк в памяти одновременно.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/ExportData/GetUserAllMessagesQueryHandler.cs` : **37–40**

```csharp
// ❌ ПРОБЛЕМА: все сообщения сразу в памяти — OOM при больших данных
var messages = await _context.Messages
    .Where(m => userChatIds.Contains(m.ChatId))
    .OrderBy(m => m.SentAt)
    .ToListAsync(cancellationToken); // может вернуть 100k+ записей
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: использовать серверный стриминг gRPC или чанковую обработку
// Вариант — чанки по N записей:
const int ChunkSize = 500;
int offset = 0;
List<Message> chunk;

do
{
    chunk = await _context.Messages
        .Where(m => userChatIds.Contains(m.ChatId))
        .OrderBy(m => m.SentAt)
        .Skip(offset)
        .Take(ChunkSize)
        .ToListAsync(cancellationToken);

    foreach (var message in chunk)
    {
        response.Messages.Add(MapToExport(message));
    }
    offset += ChunkSize;
}
while (chunk.Count == ChunkSize);
```

---

## 🔵 Баги

---

### BUG-01 · Race condition при создании личного чата: возможен дублирующий чат при конкурентных запросах ⏭ ПРОПУЩЕНО

> **Решение требует отдельной задачи.** Вариант с `Serializable` транзакцией хрупкий (нужен retry при serialization failure), вариант с `INSERT ON CONFLICT` затруднён текущей схемой `ChatMembers`. Надёжный фикс — добавить колонки `DmUserAId/DmUserBId` в `Chat` с partial unique index, плюс бэкфилл существующих DM-чатов миграцией. Это нетривиально и требует отдельного планирования.

**Описание:**
`GetPersonChatIdCommandHandler` и `SendMessageCommandHandler` проверяют наличие чата (`GetUserChatIdWithPerson`), затем создают его (`CreatePersonChat`). Между проверкой и созданием нет транзакционной блокировки — при двух одновременных запросах от разных клиентов одного пользователя создадутся два одинаковых личных чата.

**В чём конкретно проблема:**
TOCTOU (Time of Check — Time of Use) — нет `SELECT FOR UPDATE` или уникального constraint.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/GetPersonChatId/GetPersonChatIdCommandHandler.cs` : **44–68** и `Features/SendMessage/SendMessageCommandHandler.cs` : **117–154**

```csharp
// ❌ ПРОБЛЕМА: check-then-act без транзакции

// Поток A: existingChatId = null (чата нет)
// Поток B: existingChatId = null (чата нет)
// Поток A: CreatePersonChat → новый чат #1
// Поток B: CreatePersonChat → новый чат #2  ← дубликат!

var existingChatId = await _chatsStorage.GetUserChatIdWithPerson(
    personResponse.User.Id, _userContext.UserId);

if (existingChatId is null)
{
    var createdChat = await _chatsStorage.CreatePersonChat(
        _userContext.UserId, personResponse.User.Id);
}
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ A: уникальный constraint в БД + обработка конфликта
// В миграции:
// CREATE UNIQUE INDEX idx_unique_dm_chat ON "ChatMembers" (LEAST("UserId1", "UserId2"), GREATEST(...))
// Это сложно с текущей схемой, поэтому:

// ✅ РЕШЕНИЕ B: PostgreSQL INSERT ... ON CONFLICT DO NOTHING + повторный SELECT
public async Task<Chat> GetOrCreatePersonChat(long userId, long personId)
{
    // Попытка создать — если конкурентно создан другой — поймаем конфликт
    using var tx = await _context.Database.BeginTransactionAsync(
        System.Data.IsolationLevel.Serializable);
    try
    {
        var existing = await GetUserChatIdWithPerson(userId, personId);
        if (existing.HasValue)
        {
            await tx.CommitAsync();
            return await GetChat(existing.Value);
        }

        var chat = await CreatePersonChat(userId, personId);
        await tx.CommitAsync();
        return chat;
    }
    catch
    {
        await tx.RollbackAsync();
        // При конкурентном создании — повторно читаем
        var fallbackId = await GetUserChatIdWithPerson(userId, personId);
        return await GetChat(fallbackId!.Value);
    }
}
```

---

### BUG-02 · `KickUser`: участник чата идентифицируется по `ChatMember.Id`, а не по `UserId` ✅ ИСПРАВЛЕНО

**Описание:**
В `KickUserCommandHandler` поиск участника происходит по `chatMember.Id == request.UserId`. Но `ChatMember.Id` — это суррогатный ключ (PK), а `request.UserId` — это `long` ID пользователя. Фактически, `KickUserRequest.UserId` и `ChatMember.Id` — разные поля. Если значения случайно совпадают — могут быть исключены не те пользователи.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/KickUser/KickUserCommandHandler.cs` : **61–66**

```csharp
// ❌ ПРОБЛЕМА: chatMember.Id (суррогатный PK) сравнивается с request.UserId (ID пользователя)
var chatMember = chatInfo.Members!.FirstOrDefault(x => x.Id == request.UserId);
//                                                      ^^^^ это long PK ChatMember,
//                                                           но request.UserId — это ID пользователя!
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: искать по UserId, а не по суррогатному Id
var chatMember = chatInfo.Members!.FirstOrDefault(x => x.UserId == request.UserId);
//                                                      ^^^^^^^^ правильное поле
```

---

### BUG-04 · `KickUser`: системное сообщение отправляется в очередь до сохранения в БД ✅ ИСПРАВЛЕНО

**Описание:**
В `KickUserCommandHandler` сначала вызывается `_messageQueueSender.SendMessage(kickSystemMessage, ...)`, и только потом `_messagesStorage.AddMessage(kickSystemMessage)`. Если `AddMessage` завершится с ошибкой — сообщение уже ушло в RabbitMQ и появится у клиентов, но в базе его не будет.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/KickUser/KickUserCommandHandler.cs` : **114–117**

```csharp
// ❌ ПРОБЛЕМА: publish в очередь ПЕРЕД сохранением в БД
await _messageQueueSender.SendMessage(kickSystemMessage, request.ChatId,
    chatInfo.Members!.Select(x => x.UserId).ToList());

await _messagesStorage.AddMessage(kickSystemMessage); // ❌ если упадёт — клиенты видят призрак
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: сначала сохранить в БД, потом публиковать (как в SendMessageCommandHandler)
kickSystemMessage = await _messagesStorage.AddMessage(kickSystemMessage);

await _messageQueueSender.SendMessage(kickSystemMessage, request.ChatId,
    chatInfo.Members!.Select(x => x.UserId).ToList());
```

---

## ⚪ Прочее / Код-стайл

---

### MISC-01 · `ChatCache`: поглощение исключений без логирования типа исключения ✅ ИСПРАВЛЕНО

**Описание:**
В `SetChatName` и `SetChatImage` блоки `catch` перехватывают все исключения, но не логируют `exception`-объект. Если Redis недоступен — сервис молча падает обратно на БД, не давая оператору сигнала о проблеме.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/ChatCache.cs` : **30–38, 54–63**

```csharp
// ❌ ПРОБЛЕМА: исключение поглощается молча
catch (Exception e)
{
    // пусто — никто не узнает, что Redis упал
}
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: логировать предупреждение с деталями ошибки
catch (Exception ex)
{
    _logger.LogWarning(ex, "Не удалось записать имя чата {ChatId} в Redis-кэш", chatId);
}
```

> Для этого в `ChatCache` нужно добавить инъекцию `ILogger<ChatCache>`.

---

### MISC-02 · `GetChatMessages` использует синхронный `FirstOrDefault` без `await` ✅ ИСПРАВЛЕНО

**Описание:**
В `GetChatMessages` поиск стартового сообщения выполняется через `_context.Messages.FirstOrDefault(...)` без `await`. Это синхронный вызов к EF Core в async-методе — блокирует поток до получения ответа от БД.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/MessagesStorage.cs` : **29**

```csharp
// ❌ ПРОБЛЕМА: синхронный вызов к БД в async-методе
var startMessage = _context.Messages.FirstOrDefault(m => m.Id == fromMessageId && m.ChatId == chatId);
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: асинхронный вариант
var startMessage = await _context.Messages
    .FirstOrDefaultAsync(m => m.Id == fromMessageId && m.ChatId == chatId);
```

---

### MISC-04 · `CreateGroupChatCommandHandler`: системное сообщение содержит пробелы вокруг названия чата ✅ ИСПРАВЛЕНО

**Описание:**
Системное сообщение при создании группы формируется со случайными пробелами вокруг `request.Title`. Клиенты увидят `Создан групповой чат " My Group "`.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/CreateGroupChat/CreateGroupChatCommandHandler.cs` : **105**

```csharp
// ❌ ПРОБЛЕМА: лишние пробелы внутри кавычек
Text = $"Создан групповой чат \" {request.Title} \""
//                             ^^ пробел ^^
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: убрать пробелы
Text = $"Создан групповой чат \"{request.Title}\""
```
