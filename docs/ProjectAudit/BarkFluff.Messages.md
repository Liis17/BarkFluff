# Аудит проекта: BarkFluff.Messages

> **Дата аудита:** 2026-05-06
> **Ветка:** `dev`
> **Ревьювер:** BarkfluffAgent / GitHub Copilot
> **Расположение проекта:** `Backend/BarkFluff.Messages/`

---

## Содержание

- [🔴 Безопасность](#безопасность)
- [🟡 Оптимизация](#оптимизация)
- [🔵 Баги](#баги)
- [⚪ Прочее / Код-стайл](#прочее--код-стайл)

---

## 🔴 Безопасность

---

### SEC-01 · SQL-инъекция через конкатенацию строк в `GetChatAttachmentsAsync`

**Описание:**
В методе формирования SQL-запроса для получения вложений чата происходит прямая конкатенация непроверенных переменных в строку SQL-запроса. Несмотря на то что `attachmentTypeFilter` формируется из `int`-значения enum, сам паттерн опасен: при любом рефакторинге или добавлении нового параметра риск инъекции резко возрастает.

**В чём конкретно проблема:**
Переменные `attachmentTypeFilter` и `sortOrder` вставляются через интерполяцию строк `$@"..."` напрямую в SQL без параметризации, что является классическим антипаттерном при работе с raw SQL.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/MessagesStorage.cs` : **160–202**

```csharp
// ❌ ПРОБЛЕМА: строковая конкатенация в SQL — опасный антипаттерн
string attachmentTypeFilter;
if (attachmentType.HasValue && attachmentType.Value != Domain.MessageAttachmentType.Unknown)
{
    var typeValue = (int)attachmentType.Value;
    // значение typeValue вставляется напрямую в строку запроса
    attachmentTypeFilter = "AND a.\"Type\" = " + typeValue;
}
else
{
    // магическое число 8 хардкодится прямо в SQL
    attachmentTypeFilter = "AND a.\"Type\" != 8";
}

var sortOrder = sortDescending ? "DESC" : "ASC"; // строка вставляется в SQL без параметра

var countSql = $@"
    SELECT COUNT(*)
    FROM ""Messages"" m
    INNER JOIN ""MessageAttachments"" a ON m.""Id"" = a.""MessageId""
    WHERE m.""ChatId"" = @chatId
    {attachmentTypeFilter}";  // ❌ прямая интерполяция
```

**Варианты решения:**

**Вариант A — параметризованный запрос (рекомендуется):**
```csharp
// ✅ РЕШЕНИЕ: использовать NpgsqlParameter для типа вложения, whitelist для сортировки
var parameters = new List<NpgsqlParameter>
{
    new("@chatId", chatId)
};

string attachmentTypeFilter;
if (attachmentType.HasValue && attachmentType.Value != Domain.MessageAttachmentType.Unknown)
{
    // безопасно: тип передаётся как параметр
    attachmentTypeFilter = "AND a.\"Type\" = @attachmentType";
    parameters.Add(new NpgsqlParameter("@attachmentType", (int)attachmentType.Value));
}
else
{
    // безопасно: константа типа ForwardedMessage из enum, не из внешних данных
    attachmentTypeFilter = $"AND a.\"Type\" != @forwardedType";
    parameters.Add(new NpgsqlParameter("@forwardedType", (int)Domain.MessageAttachmentType.ForwardedMessage));
}

// whitelist для направления сортировки — строки "ASC"/"DESC" никогда не приходят снаружи
var sortOrder = sortDescending ? "DESC" : "ASC"; // допустимо, т.к. это bool-флаг, не внешняя строка
```

**Вариант B — переписать через EF Core LINQ (полностью безопасно):**
```csharp
// ✅ РЕШЕНИЕ: EF Core строит параметризованный запрос автоматически
var query = _context.Messages
    .Where(m => m.ChatId == chatId)
    .SelectMany(m => m.Content!.Attachments!, (m, a) => new { m, a });

if (attachmentType.HasValue && attachmentType.Value != Domain.MessageAttachmentType.Unknown)
    query = query.Where(x => x.a.Type == attachmentType.Value);
else
    query = query.Where(x => x.a.Type != Domain.MessageAttachmentType.ForwardedMessage);

var totalCount = await query.CountAsync();
var attachments = await query
    .OrderBy(x => sortDescending
        ? (object)EF.Property<object>(x.m, "SentAt") // используйте нужный ключ
        : (object)EF.Property<object>(x.m, "SentAt"))
    .Skip(skip).Take(take)
    .Select(x => new ChatAttachmentDto { ... })
    .ToListAsync();
```

---

### SEC-02 · Нет авторизации при проверке `MarkAsRead`: чужие сообщения можно пометить прочитанными

**Описание:**
При вызове `MarkAsRead` сервис проверяет, состоит ли пользователь в чате, которому *принадлежат* сообщения. Но он не проверяет, принадлежат ли сообщения из `request.MessageIds` именно тем чатам, к которым у пользователя есть доступ. Если злоумышленник отправит `MessageIds` из разных чатов, сначала получит список уникальных `chatIds`, и если хотя бы к одному из них есть доступ — бросит `NoAccessToChatException` только для недоступного. Однако в итоге `MarkMessagesAsRead` выполняется через raw SQL по *всем* `messageIds` без дополнительной фильтрации по чатам, к которым доступ подтверждён.

**В чём конкретно проблема:**
`MarkMessagesAsRead` принимает список messageIds и выполняет `UPDATE` без проверки принадлежности сообщений разрешённым чатам.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/MarkAsRead/MarkAsReadCommandHandler.cs` : **46–84** и `Persistence/Services/MessagesStorage.cs` : **135–147**

```csharp
// ❌ ПРОБЛЕМА: после проверки доступа к chatIds — нет сверки,
// что каждый messageId действительно принадлежит одному из разрешённых chatIds

var messages = await _messagesStorage.GetMessagesByIds(request.MessageIds);
var chatIds = messages.Select(m => m.ChatId).Distinct().ToList();

foreach (var chatId in chatIds)
{
    var hasAccess = await _chatsStorage.CheckAccessToChat(chatId, _userContext.UserId);
    if (!hasAccess) throw new NoAccessToChatException(); // бросаем, но обработка идёт дальше
}

// ❌ тут messageIds могут включать сообщения из чатов,
// к которым доступ не был подтверждён (если исключение не бросилось раньше)
await _messagesStorage.MarkMessagesAsRead(request.MessageIds, _userContext.UserId);
```

```sql
-- MessagesStorage.MarkMessagesAsRead
-- ❌ нет WHERE chatId IN (разрешённые чаты)
UPDATE "Messages"
SET "ReadBy" = ...
WHERE "Id" = ANY(@messageIds)
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: фильтруем только те сообщения, chatId которых реально доступен текущему пользователю

var messages = await _messagesStorage.GetMessagesByIds(request.MessageIds);
var chatIds = messages.Select(m => m.ChatId).Distinct().ToList();

// собираем только разрешённые chatIds
var allowedChatIds = new HashSet<Guid>();
foreach (var chatId in chatIds)
{
    var hasAccess = await _chatsStorage.CheckAccessToChat(chatId, _userContext.UserId);
    if (!hasAccess) throw new NoAccessToChatException();
    allowedChatIds.Add(chatId);
}

// оставляем только сообщения из разрешённых чатов
var allowedMessageIds = messages
    .Where(m => allowedChatIds.Contains(m.ChatId))
    .Select(m => m.Id)
    .ToList();

await _messagesStorage.MarkMessagesAsRead(allowedMessageIds, _userContext.UserId);
```

---

### SEC-03 · Отсутствие ограничения на размер контента сообщения

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
```

```csharp
// ✅ РЕШЕНИЕ: ограничение на уровне конфигурации EF Core
contentBuilder.Property(c => c.Text).HasMaxLength(4096);
```

---

### SEC-04 · Отсутствие ограничения на количество вложений в одном сообщении

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

---

### OPT-01 · N+1 запросов в `ListChats`: последовательные вызовы Users API для каждого личного чата

**Описание:**
В `ListChatsCommandHandler` для каждого личного чата, у которого нет кэша имени, вызывается `LoadNameAndImageChat`, который делает отдельный gRPC-вызов `GetByIdAsync` к Users-сервису. При списке в 50 чатов без кэша это 50 последовательных сетевых запросов.

**В чём конкретно проблема:**
Цикл `foreach` с `await` внутри — каждая итерация ждёт завершения предыдущей.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/ListChats/ListChatsCommandHandler.cs` : **54–69**

```csharp
// ❌ ПРОБЛЕМА: последовательные await в foreach = N+1 запросов к Users API
foreach (var chat in chats.Where(x => !x.IsGroupChat))
{
    var chatName = await _chatCache.GetChatName(chat.Id, _userContext.UserId);

    if (chatName is null)
    {
        // ❌ каждый вызов ждёт предыдущего
        await LoadNameAndImageChat(chat);
    }
    else { ... }
}
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: собрать все UserIds без кэша → один батч-запрос ListByIdsAsync

var chatsWithoutCache = new List<(Chat chat, long memberId)>();

foreach (var chat in chats.Where(x => !x.IsGroupChat))
{
    var chatName = await _chatCache.GetChatName(chat.Id, _userContext.UserId);
    if (chatName is not null)
    {
        chat.Title = chatName;
        chat.Picture = await _chatCache.GetChatImage(chat.Id, _userContext.UserId);
    }
    else
    {
        var memberId = chat.Members![0].UserId == _userContext.UserId
            ? chat.Members[1].UserId
            : chat.Members[0].UserId;
        chatsWithoutCache.Add((chat, memberId));
    }
}

if (chatsWithoutCache.Count > 0)
{
    // ✅ один запрос вместо N
    var userIds = chatsWithoutCache.Select(x => x.memberId).Distinct().ToList();
    var usersResponse = await _usersServerApiClient.ListByIdsAsync(
        new ListByIdsRequest { Ids = { userIds } });

    var usersMap = usersResponse.Users.ToDictionary(u => u.Id);

    foreach (var (chat, memberId) in chatsWithoutCache)
    {
        if (!usersMap.TryGetValue(memberId, out var user)) continue;

        chat.Title = $"{user.FirstName} {user.LastName}";
        chat.Picture = user.ProfilePicture;

        // кэшируем результат
        await _chatCache.SetChatName(chat.Id, _userContext.UserId, chat.Title);
        await _chatCache.SetChatImage(chat.Id, _userContext.UserId, chat.Picture);
    }
}
```

---

### OPT-02 · `CheckAccessToChat` загружает всю сущность `Chat` с `Include(Members)` — избыточный SELECT

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
// Генерирует: SELECT EXISTS(SELECT 1 FROM "ChatMembers" WHERE "ChatId"=@chatId AND "UserId"=@userId)
// Индекс (ChatId, UserId) уже существует согласно конфигурации
```

---

### OPT-03 · `GetUserChats` выполняет три коррелированных подзапроса для каждого чата в одном SELECT

**Описание:**
Запрос `GetUserChats` использует три коррелированных подзапроса (`COUNT`, `MIN`, `FirstOrDefault`) прямо в `Select`. При выборке 50 чатов PostgreSQL выполняет 150 дополнительных сканирований таблицы `Messages`. Это классический производительный антипаттерн.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/ChatsStorage.cs` : **29–55**

```csharp
// ❌ ПРОБЛЕМА: 3 коррелированных подзапроса к Messages для каждой строки Chat
.Select(c => new Chat
{
    ...
    // три отдельных обращения к Messages внутри одного SELECT
    CountUnread = _context.Messages.Count(x => x.ChatId == c.Id && !x.ReadBy.Contains(userId)),
    FirstUnreadMessageId = _context.Messages
        .Where(m => m.ChatId == c.Id && !m.ReadBy.Contains(userId))
        .Min(m => (long?)m.Id),
    LastMessage = _context.Messages
        .Where(m => m.ChatId == c.Id)
        .OrderByDescending(m => m.SentAt)
        .FirstOrDefault()  // ❌ FirstOrDefault() в подзапросе EF — неэффективно
})
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: денормализовать LastMessageId / LastMessageSentAt в таблицу Chats
// Обновлять при каждом AddMessage. Тогда LastMessage не нужно вычислять подзапросом.

// Альтернатива: вынести CountUnread и FirstUnreadMessageId в отдельный запрос
// и объединить результаты в памяти, чтобы основной JOIN был проще.

// Краткосрочное улучшение — убедиться что индекс Messages(ChatId, SentAt DESC) существует:
// CREATE INDEX idx_messages_chatid_sentat ON "Messages" ("ChatId", "SentAt" DESC);
// CREATE INDEX idx_messages_readby ON "Messages" USING GIN ("ReadBy");
```

---

### OPT-04 · `GetTotalUserChats` — дублирующий запрос к базе (уже выполнен `GetUserChats`)

**Описание:**
В `ListChatsCommandHandler` сначала выполняется `GetUserChats` (с пагинацией), затем отдельно `GetTotalUserChats` — второй полный `COUNT` по таблице Chats. Оба запроса идут синхронно один за другим.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/ListChats/ListChatsCommandHandler.cs` : **52, 76**

```csharp
// ❌ ПРОБЛЕМА: два отдельных запроса к БД, второй можно запустить параллельно
var chats = await _chatsStorage.GetUserChats(_userContext.UserId, request.Skip, request.Size);
// ... долгая обработка кэша ...
var totalCount = await _chatsStorage.GetTotalUserChats(_userContext.UserId); // ❌ ждёт окончания обработки выше
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: запускать оба запроса к БД параллельно
var chatsTask = _chatsStorage.GetUserChats(_userContext.UserId, request.Skip, request.Size);
var totalCountTask = _chatsStorage.GetTotalUserChats(_userContext.UserId);
await Task.WhenAll(chatsTask, totalCountTask);

var chats = chatsTask.Result;
var totalCount = totalCountTask.Result;
```

---

### OPT-05 · `GetChatMessagesWithOffset` выполняет три отдельных запроса к БД вместо одного

**Описание:**
Метод двунаправленной пагинации делает три последовательных `await` к PostgreSQL: запрос референс-сообщения, запрос сообщений до него, запрос сообщений после. Можно объединить в один запрос через UNION или оконные функции.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/MessagesStorage.cs` : **59–118**

```csharp
// ❌ ПРОБЛЕМА: три отдельных round-trip к PostgreSQL
var referenceMessage = await _context.Messages
    .FirstOrDefaultAsync(m => m.Id == fromMessageId && m.ChatId == chatId);

var messagesBefore = await _context.Messages
    .Where(x => x.ChatId == chatId && x.SentAt < referenceMessage.SentAt)
    .OrderByDescending(m => m.SentAt).Take(offsetBefore).ToListAsync();

var messagesAfter = await _context.Messages
    .Where(x => x.ChatId == chatId && x.SentAt > referenceMessage.SentAt)
    .OrderBy(m => m.SentAt).Take(offsetAfter).ToListAsync();
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: один запрос через оконную функцию (raw SQL) или параллельный запуск Before/After

// Краткосрочно — параллельный запуск двух запросов после получения referenceMessage:
var beforeTask = _context.Messages
    .Where(x => x.ChatId == chatId && x.SentAt < referenceMessage.SentAt)
    .OrderByDescending(m => m.SentAt).Take(offsetBefore).ToListAsync();

var afterTask = _context.Messages
    .Where(x => x.ChatId == chatId && x.SentAt > referenceMessage.SentAt)
    .OrderBy(m => m.SentAt).Take(offsetAfter).ToListAsync();

await Task.WhenAll(beforeTask, afterTask);

var result = new List<Message>(beforeTask.Result) { referenceMessage };
result.AddRange(afterTask.Result);
return result.OrderBy(m => m.SentAt).ToList();
```

---

### OPT-06 · `ChatCache`: отсутствие `AbsoluteExpiration` / `SlidingExpiration` — ключи никогда не инвалидируются

**Описание:**
`SetChatName` и `SetChatImage` записывают значения в Redis без TTL. Удалённые или заблокированные аккаунты будут показывать устаревшие имена/аватары вечно. При большой нагрузке Redis будет бесконечно расти.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/ChatCache.cs` : **28–64**

```csharp
// ❌ ПРОБЛЕМА: нет expiration — запись живёт вечно
await _cache.SetStringAsync($"chat_name_{chatId}_{userId}", name);
await _cache.SetStringAsync($"chat_image_{chatId}_{userId}", image);
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: установить sliding expiration
private static readonly DistributedCacheEntryOptions CacheOptions = new()
{
    SlidingExpiration = TimeSpan.FromHours(24),
    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7)
};

await _cache.SetStringAsync($"chat_name_{chatId}_{userId}", name, CacheOptions);
await _cache.SetStringAsync($"chat_image_{chatId}_{userId}", image, CacheOptions);
```

---

### OPT-07 · `MarkAsRead`: `GetChatMembers(chatId, 0, int.MaxValue)` — загрузка всех участников без лимита

**Описание:**
В `MarkAsReadCommandHandler` для каждого уникального `chatId` вызывается `GetChatMembers(chatId, 0, int.MaxValue)`. В групповом чате с тысячами участников это загрузит все записи `ChatMember` в память одним запросом.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/MarkAsRead/MarkAsReadCommandHandler.cs` : **79–80**

```csharp
// ❌ ПРОБЛЕМА: int.MaxValue как лимит — загружает всю таблицу ChatMembers для чата
var chatMembers = await _chatsStorage.GetChatMembers(chatId, 0, int.MaxValue);
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: добавить в ChatsStorage метод только для UserIds без пагинации
public async Task<List<long>> GetChatMemberIds(Guid chatId)
{
    return await _context.ChatMembers
        .Where(m => m.ChatId == chatId)
        .Select(m => m.UserId)
        .ToListAsync();
}

// В handler:
var chatMemberIds = await _chatsStorage.GetChatMemberIds(chatId);
chatMembersCache[chatId] = chatMemberIds;
```

---

### OPT-08 · `GetUserAllMessages` (ExportData): загрузка всех сообщений пользователя в память без стриминга

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

### BUG-01 · Race condition при создании личного чата: возможен дублирующий чат при конкурентных запросах

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

### BUG-02 · `KickUser`: участник чата идентифицируется по `ChatMember.Id`, а не по `UserId`

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

### BUG-03 · `GetChatMessages` использует `SentAt` для пагинации — коллизии при одинаковом времени

**Описание:**
Метод `GetChatMessages` фильтрует по `x.SentAt <= startDate`. Если два сообщения отправлены в одну миллисекунду (что реально при высокой нагрузке или bulk-отправке), при следующем запросе одно из них может пропасть или задвоиться.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/MessagesStorage.cs` : **22–47**

```csharp
// ❌ ПРОБЛЕМА: SentAt не уникален — пагинация по нему ненадёжна
var startMessage = _context.Messages.FirstOrDefault(m => m.Id == fromMessageId && m.ChatId == chatId);
startDate = startMessage.SentAt;

var messages = await _context.Messages
    .OrderByDescending(m => m.SentAt)
    .Where(x => x.ChatId == chatId && x.SentAt <= startDate) // ❌ может вернуть одно и то же сообщение
    .Take(count)
    .ToListAsync();
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: курсорная пагинация по Id (уникальный, монотонный)
public async Task<List<Message>> GetChatMessages(Guid chatId, long? fromMessageId, int count)
{
    var query = _context.Messages
        .Where(x => x.ChatId == chatId);

    if (fromMessageId.HasValue)
    {
        // Id монотонно возрастает — безопасный cursor
        query = query.Where(m => m.Id <= fromMessageId.Value);
    }

    return await query
        .OrderByDescending(m => m.Id) // стабильная сортировка по суррогатному ключу
        .Take(count)
        .ToListAsync();
}
```

---

### BUG-04 · `KickUser`: системное сообщение отправляется в очередь до сохранения в БД

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

### BUG-05 · `ListChatsCommandHandler`: запрос `GetTotalUserChats` не учитывает фильтрацию пустых чатов

**Описание:**
`GetUserChats` фильтрует чаты без сообщений (`.Where(x => x.LastMessage != null)`), но `GetTotalUserChats` также учитывает эти чаты через отдельный `CountAsync` с собственным условием `_context.Messages.Any(m => m.ChatId == x.Id)`. Формально оба фильтра исключают пустые чаты, но условия написаны независимо и могут разойтись при рефакторинге.

**Путь к файлу:** `Backend/BarkFluff.Messages/Persistence/Services/ChatsStorage.cs` : **26–99**

```csharp
// ❌ ПРОБЛЕМА: логика фильтрации дублируется в двух разных методах — риск рассинхрона

// GetUserChats:
return chats.Where(x => x.LastMessage != null).ToList(); // фильтрация в памяти

// GetTotalUserChats:
var count = await _context.Chats.CountAsync(
    x => x.Members.Any(c => c.UserId == userId)
    && _context.Messages.Any(m => m.ChatId == x.Id)); // фильтрация в SQL
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: вынести условие фильтрации в отдельный метод/выражение
private static Expression<Func<Chat, bool>> HasMessages(MessagesContext context) =>
    chat => context.Messages.Any(m => m.ChatId == chat.Id);

// Использовать одно и то же выражение в обоих методах
```

---

## ⚪ Прочее / Код-стайл

---

### MISC-01 · `ChatCache`: поглощение исключений без логирования типа исключения

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

### MISC-02 · `GetChatMessages` использует синхронный `FirstOrDefault` без `await`

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

### MISC-03 · `ListChatsCommandHandler`: мутация `request`-объекта внутри обработчика

**Описание:**
В `ListChatsCommandHandler.Handle()` и `ListMessagesCommandHandler.Handle()` напрямую модифицируются поля `request.Size` и `request.Count`. Command-объекты в CQRS должны быть иммутабельны — мутация усложняет отладку и тестирование.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/ListChats/ListChatsCommandHandler.cs` : **46–50** и `Features/ListMessages/ListMessagesCommandHandler.cs` : **80–84**

```csharp
// ❌ ПРОБЛЕМА: мутация command-объекта
if (request.Size > 50)
{
    request.Size = 50; // изменяем входной объект
}
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: использовать локальную переменную
var size = Math.Min(request.Size, 50);
var chats = await _chatsStorage.GetUserChats(_userContext.UserId, request.Skip, size);
```

---

### MISC-04 · `CreateGroupChatCommandHandler`: системное сообщение содержит пробелы вокруг названия чата

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

---

### MISC-05 · `SendMessageCommandHandler`: двойной вызов `GetByIdAsync` при создании нового личного чата

**Описание:**
При создании нового личного чата (`chatIdWithPerson is null`) делаются два последовательных gRPC-вызова `GetByIdAsync`: первый — для целевого пользователя (строка 115), второй — для текущего (строка 131). Первый запрос уже был сделан для валидации (строка 115), значит второй вызов можно выполнить параллельно с первым.

**Путь к файлу:** `Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs` : **115, 131**

```csharp
// ❌ ПРОБЛЕМА: два последовательных запроса к Users API
var personRepose = await _usersServerApiClient.GetByIdAsync(...); // запрос 1

if (chatIdWithPerson is null)
{
    var createdChat = await _chatsStorage.CreatePersonChat(...);
    var userResponse = await _usersServerApiClient.GetByIdAsync(...); // запрос 2 — мог бы быть параллельным
```

**Варианты решения:**

```csharp
// ✅ РЕШЕНИЕ: параллельный запуск обоих запросов заранее
var personTask = _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = request.UserId!.Value });
var selfTask   = _usersServerApiClient.GetByIdAsync(new GetByIdRequest { UserId = _userContext.UserId });

await Task.WhenAll(personTask.ResponseAsync, selfTask.ResponseAsync);

var personResponse = await personTask;
var selfResponse   = await selfTask;
```

---

*Документ сформирован автоматически в рамках аудита кодовой базы BarkFluff.Messages. Все проблемы основаны на статическом анализе исходного кода без запуска среды.*
