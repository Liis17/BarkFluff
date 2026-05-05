# Аудит: BarkFluff.Shared.Queue

> **Дата:** 2025-07  
> **Версия проекта:** net9.0  
> **Расположение:** `Shared/BarkFluff.Shared.Queue/`  
> **Аудитор:** GitHub Copilot / BarkfluffAgent  

Библиотека содержит только POCO-классы событий для RabbitMQ (MassTransit). Несмотря на кажущуюся простоту, ряд архитектурных и типобезопасностных решений создаёт реальные дыры в безопасности, производительности и надёжности — особенно с учётом того, как эти события потребляются.

---

## Содержание

- [🔴 Безопасность](#безопасность)
- [🟡 Баги и недоработки](#баги-и-недоработки)
- [🔵 Оптимизация](#оптимизация)
- [⚪ Прочее / Качество кода](#прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Утечка чувствительных данных через `MessageText` в push-уведомлении

**Проблема / Описание**  
`PushNotificationEvent` содержит открытый текст сообщения (`MessageText`), который передаётся через RabbitMQ в CloudMessaging и далее отправляется в Firebase. Текст сообщения при этом не обрезается и не маскируется.

**В чём конкретно проблема**  
В чате может быть конфиденциальное сообщение (пароль, OTP-код, персональные данные). Оно проходит путь: `Messages → RabbitMQ → CloudMessaging → Firebase FCM → устройство`. На каждом из этих узлов текст хранится/логируется в открытом виде. При этом FCM хранит payload на серверах Google.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/PushNotificationEvent.cs` : строки 11

```csharp
// ⚠️ Полный текст сообщения передаётся в открытом виде
// через RabbitMQ → CloudMessaging → Firebase
public string? MessageText { get; set; }
```

**Варианты решения**

1. Обрезать текст до preview (например, 100 символов) **до** публикации события
2. Передавать только флаг наличия текста и показывать на клиенте «Новое сообщение»
3. Применять данные из события только для заголовка, а текст не передавать вовсе

```csharp
// ✅ Вариант: обрезка превью на стороне публикатора (PushNotificationSchedulerHandler)
// Вместо полного текста — только превью до N символов
private const int MaxPreviewLength = 80;

await publisher.Publish(new PushNotificationEvent
{
    // ...
    // Обрезаем до допустимой длины перед публикацией в очередь
    MessageText = notification.Message.Content?.Text is { } text
        ? (text.Length > MaxPreviewLength ? text[..MaxPreviewLength] + "…" : text)
        : null,
    // ...
});
```

---

### SEC-02 — `SenderAvatarUrl` и `ImagePreviewUrl` — неконтролируемые внешние URL в событии

**Проблема / Описание**  
`PushNotificationEvent` содержит URL аватара и превью изображения как произвольные строки (`string?`). Эти URL попадают в FCM payload и могут содержать ссылки на внешние ресурсы.

**В чём конкретно проблема**  
Злоумышленник, получив доступ к публикации событий (компрометация одного микросервиса), может подставить URL трекера (pixel tracking) или SSRF-цели. Android-клиент выполнит запрос к этому URL при получении уведомления.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/PushNotificationEvent.cs` : строки 16, 25

```csharp
// ⚠️ Нет никакой валидации — любой URL может быть вставлен
public string? SenderAvatarUrl { get; set; }
public string? ImagePreviewUrl { get; set; }
```

**Варианты решения**

```csharp
// ✅ Валидировать домен URL при публикации (на стороне PushNotificationSchedulerHandler)
// или при потреблении в CloudMessaging

private static readonly string[] _allowedHosts = ["cdn.barkfluff.app", "storage.barkfluff.app"];

private static string? ValidateUrl(string? url)
{
    if (url is null) return null;
    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
    // Разрешаем только доверенные домены
    return _allowedHosts.Contains(uri.Host) ? url : null;
}
```

---

### SEC-03 — `Notification.Payload` — `Dictionary<string, string>` без инициализации

**Проблема / Описание**  
Поле `Payload` в базовом классе `Notification` объявлено как `Dictionary<string, string>` без значения по умолчанию и без `nullable`-аннотации. При десериализации из RabbitMQ оно может быть `null`, что приводит к `NullReferenceException` в любом consumer'е, обращающемся к нему без проверки.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Notifications/Notification.cs` : строка 17

```csharp
// ⚠️ Нет инициализатора и нет nullable-аннотации
// При десериализации может быть null → NRE в consumer'е
public Dictionary<string, string> Payload { get; set; }
```

**Варианты решения**

```csharp
// ✅ Инициализировать значением по умолчанию
// и/или пометить как nullable для явного контракта
public Dictionary<string, string> Payload { get; set; } = new();

// Или, если null допустим:
public Dictionary<string, string>? Payload { get; set; }
```

---

## 🟡 Баги и недоработки

---

### BUG-01 — `ReadReceiptEvent` определён, но нигде не потребляется

**Проблема / Описание**  
`ReadReceiptEvent` — отдельный тип события для подтверждения прочтения с временной меткой `ReadAt`, флагом `IsLastMessage` и `ReadBy`. Однако в кодовой базе отсутствует какой-либо `IConsumer<ReadReceiptEvent>`. Событие существует, публикуется (согласно Obsidian-документации), но его потребление нигде не реализовано.

**В чём конкретно проблема**  
Сообщения публикуются, занимают место в очереди RabbitMQ, но ни один сервис их не забирает. Это приводит к: (1) накоплению необработанных сообщений в dead-letter queue, (2) потере подтверждений прочтения со временной меткой на клиентах.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/ReadReceiptEvent.cs` : строки 1–16

```csharp
// ⚠️ Этот тип события НИГДЕ не потребляется (IConsumer<ReadReceiptEvent> не найден)
// При этом MessageReadEvent обрабатывается ReadByConsumer — вероятно путаница между двумя типами
public class ReadReceiptEvent
{
    public Guid ChatId { get; set; }
    public long MessageId { get; set; }
    public List<long> ReadBy { get; set; }      // отличие: List, не NewReadBy
    public List<long> ChatMembers { get; set; }
    public bool IsLastMessage { get; set; }     // уникальное поле — не используется нигде
    public DateTime ReadAt { get; set; }        // уникальное поле — не используется нигде
}
```

**Варианты решения**

```csharp
// Вариант 1: реализовать consumer в BarkFluff.Updates
// если ReadReceiptEvent — это отдельный контракт (например, для синхронизации с клиентом)
public class ReadReceiptConsumer : IConsumer<ReadReceiptEvent>
{
    public async Task Consume(ConsumeContext<ReadReceiptEvent> context)
    {
        // Использовать ReadAt и IsLastMessage для обновления состояния клиента
        // ...
    }
}

// Вариант 2: удалить ReadReceiptEvent если он дублирует MessageReadEvent
// и объединить поля IsLastMessage / ReadAt в MessageReadEvent
```

---

### BUG-02 — `NewMessageEvent.Message` — `byte[]` без nullable и без инициализатора

**Проблема / Описание**  
Поле `Message` в `NewMessageEvent` объявлено как `byte[]` — ненулевой тип, без инициализатора и без `= null!`. В .NET при дефолтной десериализации System.Text.Json оно будет `null`, несмотря на тип без `?`. `NewMessageConsumer` вызовет `Message.Parser.ParseFrom(context.Message.Message)` — и получит `ArgumentNullException`.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/NewMessageEvent.cs` : строки 7, 9

```csharp
// ⚠️ Оба поля без инициализаторов — при неполной десериализации → NullReferenceException
public List<long> ChatMembers { get; set; }  // null при десериализации без значений
public byte[] Message { get; set; }          // null → ParseFrom() бросит исключение
```

**Варианты решения**

```csharp
// ✅ Явная инициализация предотвращает NRE при неполных сообщениях
public class NewMessageEvent
{
    public Guid ChatId { get; set; }
    public List<long> ChatMembers { get; set; } = [];
    public byte[] Message { get; set; } = [];   // или required + валидация
}
```

---

### BUG-03 — `UserChangedBio` использует блочный стиль namespace — несоответствие стилю

**Проблема / Описание**  
Все остальные файлы проекта используют file-scoped namespace (`namespace X.Y;`), а `UserChangedBio.cs` использует устаревший блочный стиль. Это само по себе не баг, но нарушает консистентность и создаёт лишние diff'ы при ревью.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Users/UserChangedBio.cs` : строки 1–9

```csharp
// ⚠️ Устаревший блочный namespace — весь остальной проект использует file-scoped
namespace BarkFluff.Shared.Queue.Users
{
    public class UserChangedBio
    {
        public long UserId { get; set; }
        public string NewBio { get; set; }  // ⚠️ + нет инициализатора
    }
}
```

**Варианты решения**

```csharp
// ✅ File-scoped namespace + инициализатор строки
namespace BarkFluff.Shared.Queue.Users;

public class UserChangedBio
{
    public long UserId { get; set; }
    public string NewBio { get; set; } = string.Empty;
}
```

---

### BUG-04 — `EmailNotification.Title` и `Address` без инициализаторов

**Проблема / Описание**  
`EmailNotification` наследует `Notification` и добавляет `Title` и `Address` как ненулевые строки без инициализаторов. `EmailQueueConsumer` использует оба поля без проверки на null. При неполно заполненном событии email-отправка упадёт с `NullReferenceException`.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Notifications/EmailNotification.cs` : строки 7–9

```csharp
// ⚠️ При десериализации из очереди оба поля могут быть null
public string Title { get; set; }    // используется в EmailQueueConsumer без проверки
public string Address { get; set; }  // используется как email-адрес получателя
```

**Варианты решения**

```csharp
// ✅ Инициализировать значениями по умолчанию и/или пометить required
public class EmailNotification : Notification
{
    public override TransportId TransportId => TransportId.Email;

    // Вариант A: required — компилятор заставит инициализировать при создании
    public required string Title { get; set; }
    public required string Address { get; set; }

    // Вариант B: значения по умолчанию (для совместимости с десериализацией)
    // public string Title { get; set; } = string.Empty;
    // public string Address { get; set; } = string.Empty;
}
```

---

### BUG-05 — `UserChangedAvatar.ProfilePictureUrl` и `ProfilePictureUrlPreview` без инициализаторов

**Проблема / Описание**  
Аналогично другим событиям — поля без инициализаторов. `UserChangedAvatarConsumer` вызывает `_chatCache.SetChatImage(chat.Id, personDm, profilePictureUrl)` напрямую с этим значением — потенциальный `null` попадёт в Redis-кеш как значение.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Users/UserChangedAvatar.cs` : строки 7–9

```csharp
// ⚠️ null может записаться в Redis-кеш как значение
public string ProfilePictureUrl { get; set; }
public string ProfilePictureUrlPreview { get; set; }
```

**Варианты решения**

```csharp
// ✅ Инициализировать или пометить nullable
public class UserChangedAvatar
{
    public long UserId { get; set; }
    public string ProfilePictureUrl { get; set; } = string.Empty;
    public string ProfilePictureUrlPreview { get; set; } = string.Empty;
}
```

---

### BUG-06 — `UserInfoQueueSender.UserBioChangedEvent` — параметр назван `newUsername` вместо `newBio`

**Проблема / Описание**  
Метод `UserBioChangedEvent(long userId, string newUsername)` принимает параметр `newUsername`, но передаёт его в `UserChangedBio.NewBio`. Это явная копипаста от `UsernameChangedEvent`. Имя параметра вводит в заблуждение и может привести к передаче username вместо bio при вызове.

**Путь к файлу:** `Backend/BarkFluff.Users/Infrastructure/UserInfoQueueSender.cs` : строки 63–72

```csharp
// ⚠️ Параметр назван newUsername, но передаётся как bio
// Риск передачи неверных данных при рефакторинге или copy-paste
public async Task UserBioChangedEvent(long userId, string newUsername)
{
    var usernameChangedEvent = new UserChangedBio()
    {
        NewBio = newUsername,  // ⚠️ переменная называется newUsername, но это bio
        UserId = userId
    };
    await _publishEndpoint.Publish(usernameChangedEvent);
}
```

**Варианты решения**

```csharp
// ✅ Переименовать параметр и локальную переменную
public async Task UserBioChangedEvent(long userId, string newBio)
{
    var bioChangedEvent = new UserChangedBio
    {
        NewBio = newBio,
        UserId = userId
    };
    await _publishEndpoint.Publish(bioChangedEvent);
}
```

---

## 🔵 Оптимизация

---

### OPT-01 — `PushNotificationEvent` дублирует данные, уже доступные в RabbitMQ-событии

**Проблема / Описание**  
`PushNotificationSchedulerHandler` вычисляет `SenderAvatarUrl`, `ChatTitle`, `ChatAvatarUrl`, `IsGroupChat` — но **не записывает** их в `PushNotificationEvent` перед публикацией. Вместо этого `PushNotificationConsumer` в CloudMessaging делает два дополнительных gRPC-вызова (`GetByIdAsync` и `GetChatInfoAsync`) для получения тех же данных.

**В чём конкретно проблема**  
Хотя поля в `PushNotificationEvent` **объявлены** (`SenderAvatarUrl`, `ChatTitle`, `ChatAvatarUrl`, `IsGroupChat`), в `PushNotificationSchedulerHandler` они **не заполняются**. CloudMessaging вместо чтения из события делает 2 дополнительных gRPC-вызова на каждый push → N push = 2N gRPC-запросов.

**Путь к файлу:**  
- `Shared/BarkFluff.Shared.Queue/Messages/PushNotificationEvent.cs` : строки 16–21  
- `Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs` : строки 84–94

```csharp
// ⚠️ В PushNotificationSchedulerHandler поля НЕ заполняются при публикации:
await publisher.Publish(new PushNotificationEvent
{
    ChatId = notification.ChatId,
    SenderId = notification.Message.SenderId,
    MessageId = notification.Message.Id,
    MessageText = notification.Message.Content?.Text,
    RecipientUserIds = [userId],
    ContentType = (int)attachmentType,
    ImagePreviewUrl = imagePreviewUrl,
    AttachmentCount = notification.Message.Content?.Attachments.Count ?? 0
    // ⚠️ SenderAvatarUrl, ChatTitle, ChatAvatarUrl, IsGroupChat — НЕ заполнены!
});

// Следствие: PushNotificationConsumer делает лишние gRPC-вызовы:
var senderCall = _usersClient.GetByIdAsync(...);       // ← избыточно
var chatInfoCall = _messagesClient.GetChatInfoAsync(...); // ← избыточно
```

**Варианты решения**

```csharp
// ✅ Заполнить поля при публикации, чтобы CloudMessaging не делал лишних gRPC-вызовов
// В PushNotificationSchedulerHandler нужен доступ к UsersClient или кешу

// Шаг 1: Добавить UsersClient/MessagesClient или кеш в PushNotificationSchedulerHandler
// Шаг 2: Заполнить все поля события
await publisher.Publish(new PushNotificationEvent
{
    ChatId = notification.ChatId,
    SenderId = notification.Message.SenderId,
    MessageId = notification.Message.Id,
    MessageText = notification.Message.Content?.Text,
    RecipientUserIds = [userId],
    ContentType = (int)attachmentType,
    ImagePreviewUrl = imagePreviewUrl,
    AttachmentCount = notification.Message.Content?.Attachments.Count ?? 0,
    // ✅ Заполняем из заранее полученных данных (один вызов на всех получателей)
    SenderAvatarUrl = senderAvatarPreviewUrl,
    IsGroupChat = isGroupChat,
    ChatTitle = chatTitle,
    ChatAvatarUrl = chatAvatarUrl
});
// CloudMessaging теперь не делает никаких gRPC-вызовов — просто читает из события
```

---

### OPT-02 — `UserChangedAvatarConsumer` и `UserChangedNameConsumer` обновляют кеш последовательно в цикле

**Проблема / Описание**  
Оба consumer'а получают список чатов и обновляют кеш в `foreach` — последовательно, без параллелизации. При большом числе личных чатов (активный пользователь) это создаёт линейную задержку.

**Путь к файлу:**  
- `Backend/BarkFluff.Messages/Consumers/UserChangedAvatarConsumer.cs` : строки 50–55  
- `Backend/BarkFluff.Messages/Consumers/UserChangedNameConsumer.cs` : строки 51–56

```csharp
// ⚠️ Последовательное обновление кеша — O(n) задержка по числу чатов
foreach (var chat in chatsWithUser)
{
    var personDm = chat.Members![0].UserId == userId ? chat.Members[1].UserId : chat.Members[0].UserId;
    await _chatCache.SetChatImage(chat.Id, personDm, profilePictureUrl); // ждём каждый вызов
}
```

**Варианты решения**

```csharp
// ✅ Параллельное обновление через Task.WhenAll
var updateTasks = chatsWithUser.Select(chat =>
{
    var personDm = chat.Members![0].UserId == userId
        ? chat.Members[1].UserId
        : chat.Members[0].UserId;
    return _chatCache.SetChatImage(chat.Id, personDm, profilePictureUrl);
});

await Task.WhenAll(updateTasks);
// Все обновления выполняются параллельно → задержка = max(один Redis-вызов)
```

---

### OPT-03 — `PendingPushTracker` не имеет ограничения размера словаря

**Проблема / Описание**  
`PendingPushTracker` хранит `CancellationTokenSource` в `ConcurrentDictionary<(long, long), CTS>`. При высокой нагрузке (много сообщений + много пользователей) словарь может неограниченно расти, особенно если `RemovePending` не вызывается в `finally` при краше задачи (хотя `finally` есть — но если `Task.Run` не запустится, записи останутся).

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/PushNotifications/PendingPushTracker.cs` : строки 11, 19–29

```csharp
// ⚠️ Нет ограничения размера — при аномальной нагрузке растёт бесконечно
private readonly ConcurrentDictionary<(long MessageId, long UserId), CancellationTokenSource> _pendingPushes = new();

// ⚠️ Нет TTL/очистки записей — если сервис перезапускается, 
// все CTS теряются без Cancel/Dispose → утечка ресурсов при рестарте
```

**Варианты решения**

```csharp
// ✅ Добавить фоновую очистку устаревших записей через IHostedService
// или использовать ограниченный кеш с TTL

// Вариант: фоновая очистка с timestamp
private readonly ConcurrentDictionary<(long MessageId, long UserId), (CancellationTokenSource Cts, DateTime CreatedAt)> _pendingPushes = new();

// В CleanupAsync (периодический hosted service):
var cutoff = DateTime.UtcNow.AddSeconds(-30); // записи старше 30 секунд — уже мертвы
foreach (var key in _pendingPushes.Keys)
{
    if (_pendingPushes.TryGetValue(key, out var entry) && entry.CreatedAt < cutoff)
    {
        if (_pendingPushes.TryRemove(key, out var removed))
        {
            removed.Cts.Cancel();
            removed.Cts.Dispose();
        }
    }
}
```

---

### OPT-04 — `NewMessageEvent.Message` — передача proto-bytes через RabbitMQ

**Проблема / Описание**  
`NewMessageEvent.Message` содержит сериализованное protobuf-сообщение (`byte[]`). Это означает двойную сериализацию: protobuf → bytes → JSON (MassTransit по умолчанию) → base64 в JSON. Итоговый размер сообщения в очереди увеличивается примерно в 1.33x раза от bytes + JSON-обёртка.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/NewMessageEvent.cs` : строка 9

```csharp
// ⚠️ byte[] внутри JSON-конверта MassTransit = двойная сериализация
// proto → bytes → base64(bytes) в JSON
public byte[] Message { get; set; }
```

**Варианты решения**

```csharp
// ✅ Вариант A: Настроить MassTransit на использование RawProtobuf-сериализатора
// вместо JSON — тогда bytes передаются напрямую

// ✅ Вариант B: Включить конфигурацию MessageData<T> для хранения больших payload-ов
// вне очереди (в object storage/Redis), передавая только ссылку

// ✅ Вариант C (минимальная инвазивность): распаковать нужные поля в отдельные свойства
// чтобы избежать передачи всего proto-объекта
public class NewMessageEvent
{
    public Guid ChatId { get; set; }
    public List<long> ChatMembers { get; set; } = [];
    // Вместо byte[] передаём только нужные поля:
    public long MessageId { get; set; }
    public long SenderId { get; set; }
    public string? TextContent { get; set; }
    // ... и т.д.
}
```

---

## ⚪ Прочее / Качество кода

---

### MISC-01 — Отсутствие базового класса или интерфейса для событий Messages и Users

**Проблема / Описание**  
События `NewMessageEvent`, `MessageReadEvent`, `ReadReceiptEvent`, `PushNotificationEvent`, `UserChangedAvatar` и другие не имеют общего базового класса или интерфейса. У всех из них есть `UserId` или `ChatId`, но нет контракта. Это затрудняет: логирование, трассировку (correlation ID), middleware-обработку и написание generic consumer'ов.

**Путь к файлам:**  
- `Shared/BarkFluff.Shared.Queue/Messages/` — все файлы  
- `Shared/BarkFluff.Shared.Queue/Users/` — все файлы

```csharp
// ⚠️ Нет общего контракта — correlation ID добавлять некуда
public class NewMessageEvent   { public Guid ChatId { get; set; } ... }
public class MessageReadEvent  { public Guid ChatId { get; set; } ... }
public class UserChangedAvatar { public long UserId { get; set; } ... }
// Трассировка между сервисами через RabbitMQ невозможна без изменения каждого класса
```

**Варианты решения**

```csharp
// ✅ Добавить базовый интерфейс с correlation ID
public interface IQueueEvent
{
    /// <summary>Идентификатор для трассировки запроса через все сервисы</summary>
    Guid CorrelationId { get; set; }

    /// <summary>Время создания события (UTC)</summary>
    DateTime OccurredAt { get; set; }
}

// Все события реализуют интерфейс:
public class NewMessageEvent : IQueueEvent
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public Guid ChatId { get; set; }
    public List<long> ChatMembers { get; set; } = [];
    public byte[] Message { get; set; } = [];
}
```

---

### MISC-02 — `ContentType` в `PushNotificationEvent` — нетипизированный `int`

**Проблема / Описание**  
Поле `ContentType` объявлено как `int` с комментарием `// MessageContentType enum value`. Это нарушает типобезопасность: на принимающей стороне (CloudMessaging) требуется ручное приведение, а IDE не подсказывает допустимые значения. Любое неверное значение пройдёт компиляцию.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/PushNotificationEvent.cs` : строка 24

```csharp
// ⚠️ Магическое число вместо типизированного enum
// Комментарий описывает намерение, но не обеспечивает безопасность
public int ContentType { get; set; } // MessageContentType enum value
```

**Варианты решения**

```csharp
// ✅ Вариант A: использовать enum напрямую (если enum доступен в shared-сборке)
// Перенести MessageAttachmentType в BarkFluff.Shared.Queue или BarkFluff.Shared.Identity

public MessageAttachmentType ContentType { get; set; }

// ✅ Вариант B: если enum нельзя перенести — использовать локальный enum в Shared.Queue
namespace BarkFluff.Shared.Queue.Messages;

public enum PushContentType
{
    Unknown = 0,
    Text = 1,
    Image = 2,
    File = 3,
    Audio = 4,
}

public class PushNotificationEvent
{
    // ...
    public PushContentType ContentType { get; set; }
}
```

---

### MISC-03 — `TransportId.Unknown = 0` используется как значение по умолчанию при десериализации

**Проблема / Описание**  
`TransportId.Unknown = 0` — значение по умолчанию для enum в C#. Если `Notification` будет создан без явного указания транспорта (или при ошибке десериализации), `TransportId` тихо станет `Unknown`. При этом `EmailQueueConsumer` не проверяет это поле — он получает `EmailNotification` через типизированный binding, поэтому сейчас это не критично. Но при добавлении нового транспорта и generic-обработки это станет багом.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Notifications/TransportId.cs` : строки 3–8

```csharp
// ⚠️ Unknown=0 как sentinel — стандартный паттерн, но без защиты на consumer-стороне
public enum TransportId
{
    Unknown = 0,  // значение по умолчанию при забытой инициализации
    Email = 1
}
```

**Варианты решения**

```csharp
// ✅ Добавить валидацию в base Notification при публикации
// или в generic consumer если появится routing по TransportId
public abstract class Notification
{
    public abstract TransportId TransportId { get; }

    // Метод валидации вызывается перед публикацией
    public virtual void Validate()
    {
        if (TransportId == TransportId.Unknown)
            throw new InvalidOperationException($"TransportId не может быть Unknown для {GetType().Name}");
        if (OwnerId is null && Type != NotificationType.Unknown)
            throw new InvalidOperationException("OwnerId обязателен для уведомлений с типом");
    }
}
```

---

### MISC-04 — `SessionRevokedEvent.AccessTokenExpiresAt` — `DateTime` без явного `Kind`

**Проблема / Описание**  
`SessionRevokedEvent.AccessTokenExpiresAt` — поле типа `DateTime` без явной временной зоны. MassTransit сериализует `DateTime` в JSON без timezone-info, что может привести к некорректному сравнению времени в `TokenRevocationCache` если один из сервисов работает в другом timezone (в Docker-контейнерах это актуально).

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Identity/SessionRevokedEvent.cs` : строка 9

```csharp
// ⚠️ DateTime без Kind = DateTimeKind.Utc
// При сериализации/десериализации теряется информация о timezone
public DateTime AccessTokenExpiresAt { get; set; }
```

**Варианты решения**

```csharp
// ✅ Использовать DateTimeOffset для явной привязки к timezone
public class SessionRevokedEvent
{
    public long UserId { get; set; }
    public string DeviceId { get; set; } = string.Empty;

    // DateTimeOffset содержит явный offset → безопасен для cross-timezone сравнений
    public DateTimeOffset AccessTokenExpiresAt { get; set; }
}
```

---

## Сводная таблица

| ID | Категория | Критичность | Файл | Краткое описание |
|----|-----------|-------------|------|-----------------|
| SEC-01 | Безопасность | 🔴 Высокая | `Messages/PushNotificationEvent.cs` | Полный текст сообщения через FCM |
| SEC-02 | Безопасность | 🟡 Средняя | `Messages/PushNotificationEvent.cs` | Неконтролируемые URL в событии |
| SEC-03 | Безопасность | 🟡 Средняя | `Notifications/Notification.cs` | `Payload` без инициализатора → NRE |
| BUG-01 | Баг | 🔴 Высокая | `Messages/ReadReceiptEvent.cs` | Событие не потребляется нигде |
| BUG-02 | Баг | 🔴 Высокая | `Messages/NewMessageEvent.cs` | `byte[]` и `List<long>` без инициализаторов → NRE |
| BUG-03 | Баг | ⚪ Низкая | `Users/UserChangedBio.cs` | Устаревший namespace-стиль |
| BUG-04 | Баг | 🟡 Средняя | `Notifications/EmailNotification.cs` | `Title`/`Address` без инициализаторов → NRE |
| BUG-05 | Баг | 🟡 Средняя | `Users/UserChangedAvatar.cs` | URL без инициализаторов → null в Redis |
| BUG-06 | Баг | 🟡 Средняя | `UserInfoQueueSender.cs` | Параметр `newUsername` вместо `newBio` |
| OPT-01 | Оптимизация | 🟡 Средняя | `PushNotificationEvent.cs` + `PushNotificationSchedulerHandler.cs` | Поля события не заполняются → 2 лишних gRPC-вызова |
| OPT-02 | Оптимизация | 🟡 Средняя | `UserChangedAvatarConsumer.cs` / `UserChangedNameConsumer.cs` | Последовательное обновление кеша |
| OPT-03 | Оптимизация | 🟡 Средняя | `PendingPushTracker.cs` | Неограниченный словарь без TTL |
| OPT-04 | Оптимизация | ⚪ Низкая | `Messages/NewMessageEvent.cs` | Двойная сериализация proto + JSON |
| MISC-01 | Качество | 🟡 Средняя | `Messages/` + `Users/` | Нет базового интерфейса / correlation ID |
| MISC-02 | Качество | ⚪ Низкая | `Messages/PushNotificationEvent.cs` | `ContentType` как нетипизированный `int` |
| MISC-03 | Качество | ⚪ Низкая | `Notifications/TransportId.cs` | `Unknown=0` без защиты от дефолтного значения |
| MISC-04 | Качество | 🟡 Средняя | `Identity/SessionRevokedEvent.cs` | `DateTime` без UTC-гарантии |
