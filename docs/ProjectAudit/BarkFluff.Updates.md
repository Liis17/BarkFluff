# Аудит проекта: BarkFluff.Updates

> **Дата создания:** 2025  
> **Последняя проверка актуальности:** 2026-05-18  
> **Ветка:** `dev`  
> **Покрытие:** все файлы проекта `Backend/BarkFluff.Updates`  
> **Категории:** 🔴 Безопасность · 🟡 Оптимизация · 🟠 Баги · 🔵 Прочее

## Сводка по статусу актуальности (2026-05-18)

- 🔄 **Контекст изменился:** OPT-02 — в проекте появились generic-базы `Features/Shared/UserStreamSubscriptionsBase<T>` и `DeviceStreamSubscriptionsBase<T>`, но конкретные `StreamSubscriptionsManager` (для `NewMessageEvent`, `MessageReadEvent` и ещё 14 типов событий) от них **не наследуются** — дублирование сохраняется. MISC-03 — асимметрия `record vs class` сохраняется (`NewMessageNotification` — record, `ReadByNotification` — class).
- ⚠️ **Остаётся:** SEC-01 (RabbitMQ без валидации, `Program.cs:53-56`), SEC-02 (нет лимита подписок), SEC-03 (`AddGrpcReflection` без проверки Environment — `Program.cs:25, 142`), OPT-01 (`Task.Run` в `NewMessageNotificationHandler.cs:47-73`, `ReadByNotificationHandler.cs:51-79`), OPT-03/04, OPT-05 (gauge добавлен через `Set()`, но монотонные счётчики `active_subscriptions`/`active_subscriptions_removed` сохраняются для совместимости), BUG-01 (race condition сохраняется), BUG-02, BUG-03 (`PushNotificationSchedulerHandler.cs:58, 129`), BUG-04, BUG-05, MISC-01, MISC-02 (`PushNotificationSchedulerHandler.cs:63`), MISC-04.
- ℹ️ **Структура расширилась:** в `Features/` появились новые папки для подписок (`SubscribeMessageEdited`, `SubscribePinnedMessages`, `SubscribeSecretChats`, `SubscribeEncryptedMessages` и др.), каждая со своим `StreamSubscriptionsManager` — это умножает дубль из OPT-02.

---

## Содержание

- [🔴 Безопасность](#-безопасность)
  - [SEC-01 — Захардкоженные credentials RabbitMQ без валидации](#sec-01--захардкоженные-credentials-rabbitmq-без-валидации)
  - [SEC-02 — Отсутствие лимита подписок на одного пользователя](#sec-02--отсутствие-лимита-подписок-на-одного-пользователя)
  - [SEC-03 — GrpcReflection включён в продакшне](#sec-03--grpcreflection-включён-в-продакшне)
- [🟡 Оптимизация](#-оптимизация)
  - [OPT-01 — Task.Run для каждого стрима: накладные расходы ThreadPool](#opt-01--taskrun-для-каждого-стрима-накладные-расходы-threadpool)
  - [OPT-02 — Дублирование кода двух StreamSubscriptionsManager](#opt-02--дублирование-кода-двух-streamsubscriptionsmanager)
  - [OPT-03 — .Count() на IEnumerable вместо .Count на List](#opt-03--count-на-ienumerable-вместо-count-на-list)
  - [OPT-04 — Двойной перебор Attachments в PushNotificationSchedulerHandler](#opt-04--двойной-перебор-attachments-в-pushnotificationschedulerhandler)
  - [OPT-05 — Метрики active_subscriptions не декрементируют, а инкрементируют отдельный счётчик](#opt-05--метрики-active_subscriptions-не-декрементируют-а-инкрементируют-отдельный-счётчик)
- [🟠 Баги](#-баги)
  - [BUG-01 — Race condition при очистке пустой записи в StreamSubscriptionsManager](#bug-01--race-condition-при-очистке-пустой-записи-в-streamsubscriptionsmanager)
  - [BUG-02 — CancellationTokenSource утечка в PendingPushTracker при аварийном завершении](#bug-02--cancellationtokensource-утечка-в-pendingpushtracker-при-аварийном-завершении)
  - [BUG-03 — fire-and-forget Task.Run без отслеживания в PushNotificationSchedulerHandler](#bug-03--fire-and-forget-taskrun-без-отслеживания-в-pushnotificationschedulerhandler)
  - [BUG-04 — ReadByNotification использует class вместо record: потенциальные null-поля](#bug-04--readbynotification-использует-class-вместо-record-потенциальные-null-поля)
  - [BUG-05 — NewMessageConsumer: исключение при re-throw приводит к повторной обработке сообщения](#bug-05--newmessageconsumer-исключение-при-re-throw-приводит-к-повторной-обработке-сообщения)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)
  - [MISC-01 — Отсутствует graceful shutdown для активных gRPC стримов](#misc-01--отсутствует-graceful-shutdown-для-активных-grpc-стримов)
  - [MISC-02 — Задержка push-уведомления захардкожена в 5 секунд](#misc-02--задержка-push-уведомления-захардкожена-в-5-секунд)
  - [MISC-03 — Асимметрия стиля записи нотификаций (record vs class)](#misc-03--асимметрия-стиля-записи-нотификаций-record-vs-class)
  - [MISC-04 — Метод Handle в ReadByCancelPushHandler возвращает Task.CompletedTask синхронно](#misc-04--метод-handle-в-readbycancelpushhandler-возвращает-taskcompleted-синхронно)

---

## 🔴 Безопасность

---

### SEC-01 — Захардкоженные credentials RabbitMQ без валидации

**Проблема / Описание**  
В `Program.cs` конфигурация подключения к RabbitMQ читается напрямую из `IConfiguration` без какой-либо проверки на `null` и без использования `Options`-паттерна с валидацией. Если переменная окружения или значение конфига отсутствует — `Username`/`Password` будут `null`, что приводит к неочевидному сбою при старте или к попытке подключения с пустыми кредами.

**Конкретно в чём проблема**  
Нет проверки наличия значений, нет использования `IOptions<T>` с `[Required]`, нет защиты от старта сервиса с пустыми учётными данными.

**Путь к файлу:** `Backend/BarkFluff.Updates/Program.cs` : строки 39–43

```csharp
cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
{
    // ⚠️ Нет проверки на null — при отсутствии конфига сервис стартует с пустыми кредами
    h.Username(builder.Configuration["RabbitMQ:Username"]);
    h.Password(builder.Configuration["RabbitMQ:Password"]);
});
```

**Варианты решения**  
Использовать `Options`-паттерн с `[Required]` и `ValidateOnStart`, что гарантирует падение при старте с понятным сообщением об ошибке.

```csharp
// RabbitMqOptions.cs
public class RabbitMqOptions
{
    [Required] public string Host { get; set; } = null!;
    [Required] public string Username { get; set; } = null!;
    [Required] public string Password { get; set; } = null!;
}

// Program.cs — регистрация
builder.Services
    .AddOptions<RabbitMqOptions>()
    .BindConfiguration("RabbitMQ")
    .ValidateDataAnnotations()
    .ValidateOnStart(); // ✅ Падение при старте с понятным сообщением

// Использование в UsingRabbitMq
x.UsingRabbitMq((context, cfg) =>
{
    var opts = context.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
    cfg.Host(opts.Host, "/", h =>
    {
        h.Username(opts.Username);
        h.Password(opts.Password);
    });
    // ...
});
```

---

### SEC-02 — Отсутствие лимита подписок на одного пользователя

**Проблема / Описание**  
`StreamSubscriptionsManager` позволяет зарегистрировать **неограниченное** количество подписок на одного пользователя. Это открывает возможность для DoS-атаки: злоумышленник с валидным токеном может создать тысячи одновременных gRPC-соединений, переполнив словарь и истощив память сервера.

**Конкретно в чём проблема**  
Нет никакой проверки на максимальное количество активных подписок per-user.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/StreamSubscriptionsManager.cs` : строки 14–19

```csharp
public Guid RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
{
    var subscriptionId = Guid.NewGuid();
    // ⚠️ Нет ограничения — пользователь может создать бесконечно много подписок
    var userStreams = _userSubscriptions.GetOrAdd(userId, _ => new ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>());
    userStreams[subscriptionId] = responseStream;
    return subscriptionId;
}
```

**Варианты решения**  
Добавить ограничение максимального числа подписок и бросать `RpcException` при превышении.

```csharp
private const int MaxSubscriptionsPerUser = 10; // настраивается через IOptions

public Guid RegisterSubscription(long userId, IServerStreamWriter<NewMessageEvent> responseStream)
{
    var userStreams = _userSubscriptions.GetOrAdd(userId,
        _ => new ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>());

    // ✅ Защита от DoS — лимит подписок на пользователя
    if (userStreams.Count >= MaxSubscriptionsPerUser)
        throw new RpcException(new Status(StatusCode.ResourceExhausted,
            $"Max subscriptions per user exceeded ({MaxSubscriptionsPerUser})"));

    var subscriptionId = Guid.NewGuid();
    userStreams[subscriptionId] = responseStream;
    return subscriptionId;
}
```

---

### SEC-03 — GrpcReflection включён в продакшне

**Проблема / Описание**  
`AddGrpcReflection()` и `app.MapGrpcReflectionService()` зарегистрированы безусловно, без проверки окружения. В продакшне gRPC-рефлексия позволяет любому клиенту (даже без авторизации на уровне сети) обнаружить все методы и proto-схемы сервиса, что упрощает разведку для атакующего.

**Конкретно в чём проблема**  
Рефлексия не ограничена dev-окружением.

**Путь к файлу:** `Backend/BarkFluff.Updates/Program.cs` : строки 24, 63

```csharp
builder.Services.AddGrpcReflection(); // строка 24 — без условия

// ...
app.MapGrpcReflectionService(); // строка 63 — без условия
```

**Варианты решения**  
Включать рефлексию только в `Development`.

```csharp
// Регистрация
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddGrpcReflection(); // ✅ Только для разработки
}

// Маппинг
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService(); // ✅ Только для разработки
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — Task.Run для каждого стрима: накладные расходы ThreadPool

**Проблема / Описание**  
В `NewMessageNotificationHandler` и `ReadByNotificationHandler` для каждой записи в стрим создаётся отдельная задача через `Task.Run(...)`. При большом числе пользователей с несколькими подписками это создаёт чрезмерное давление на ThreadPool. Запись в gRPC-стрим — уже асинхронная операция I/O, оборачивать её в `Task.Run` нет смысла.

**Конкретно в чём проблема**  
`Task.Run` добавляет планирование в ThreadPool там, где достаточно прямого `await`.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs` : строки 43–64

```csharp
// ⚠️ Task.Run для каждого стрима — лишнее переключение на ThreadPool
sendTasks.Add(Task.Run(async () =>
{
    try
    {
        await stream.WriteAsync(newMessageEvent, cancellationToken);
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to send message ...");
    }
}, cancellationToken));
```

**Варианты решения**  
Создавать `Task` через прямой вызов `async`-лямбды без `Task.Run`.

```csharp
// ✅ Прямой вызов async-лямбды — нет лишнего переключения на ThreadPool
static async Task SendSafe(
    IServerStreamWriter<NewMessageEvent> stream,
    NewMessageEvent evt,
    CancellationToken ct,
    ILogger logger,
    long msgId,
    long memberId)
{
    try
    {
        await stream.WriteAsync(evt, ct);
        logger.LogDebug("Sent message {MessageId} to user {UserId}", msgId, memberId);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to send message {MessageId} to user {UserId}", msgId, memberId);
    }
}

// В Handle():
sendTasks.Add(SendSafe(stream, newMessageEvent, cancellationToken, _logger, message.Id, memberId));
await Task.WhenAll(sendTasks);
```

---

### OPT-02 — Дублирование кода двух StreamSubscriptionsManager 🔄 ЧАСТИЧНО (2026-05-18)

> **Статус 2026-05-18:** В проекте появились базовые классы `Features/Shared/UserStreamSubscriptionsBase<T>` (≈59 строк) и `DeviceStreamSubscriptionsBase<T>`. Они **не используются** конкретными `StreamSubscriptionsManager` — те остаются standalone дубликатами. Количество дубликатов выросло до ≈16 (новые подписки добавились: `SubscribeMessageEdited`, `SubscribePinnedMessages`, `SubscribeSecretChats`, `SubscribeEncryptedMessages` и др.). Достаточно заменить тело каждого менеджера на `class StreamSubscriptionsManager : UserStreamSubscriptionsBase<NewMessageEvent>;`.

**Проблема / Описание**  
`Features/SubscribeNewMessages/StreamSubscriptionsManager.cs` и `Features/SubscribeMessagesRead/StreamSubscriptionsManager.cs` — **идентичны** по логике, отличаются только типом generic-параметра (`NewMessageEvent` vs `MessageReadEvent`). Любое изменение логики (например, добавление лимита из SEC-02) нужно вносить дважды.

**Конкретно в чём проблема**  
100% дублирование кода, нарушение DRY.

**Путь к файлам:**  
- `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/StreamSubscriptionsManager.cs`  
- `Backend/BarkFluff.Updates/Features/SubscribeMessagesRead/StreamSubscriptionsManager.cs`

```csharp
// SubscribeNewMessages/StreamSubscriptionsManager.cs
private readonly ConcurrentDictionary<long,
    ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>> _userSubscriptions = new();

// SubscribeMessagesRead/StreamSubscriptionsManager.cs — та же структура, другой тип
private readonly ConcurrentDictionary<long,
    ConcurrentDictionary<Guid, IServerStreamWriter<MessageReadEvent>>> _userSubscriptions = new();
// ⚠️ Полный дубль: RegisterSubscription, RemoveSubscription, GetUserStreams — идентичны
```

**Варианты решения**  
Вынести в generic базовый класс.

```csharp
// StreamSubscriptionsManager<TEvent>.cs
public class StreamSubscriptionsManager<TEvent> where TEvent : class
{
    private readonly ConcurrentDictionary<long,
        ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>> _userSubscriptions = new();

    public Guid RegisterSubscription(long userId, IServerStreamWriter<TEvent> responseStream)
    {
        var subscriptionId = Guid.NewGuid();
        var userStreams = _userSubscriptions.GetOrAdd(userId,
            _ => new ConcurrentDictionary<Guid, IServerStreamWriter<TEvent>>());
        userStreams[subscriptionId] = responseStream;
        return subscriptionId;
    }

    public void RemoveSubscription(long userId, Guid subscriptionId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        {
            userStreams.TryRemove(subscriptionId, out _);
            if (userStreams.IsEmpty)
                _userSubscriptions.TryRemove(userId, out _);
        }
    }

    public IReadOnlyList<IServerStreamWriter<TEvent>> GetUserStreams(long userId)
    {
        if (_userSubscriptions.TryGetValue(userId, out var userStreams))
            return userStreams.Values.ToList();
        return [];
    }
}

// DependencyInjection.cs
services.AddSingleton<StreamSubscriptionsManager<NewMessageEvent>>();
services.AddSingleton<StreamSubscriptionsManager<MessageReadEvent>>();
```

---

### OPT-03 — .Count() на IEnumerable вместо .Count на List

**Проблема / Описание**  
В `ReadByNotificationHandler` метод `streams.Count()` вызывается на результате `GetUserStreams()`, который возвращает `IEnumerable`. Хотя внутри это `List` (через `.ToList()`), компилятор видит `IEnumerable<T>` и вызывает LINQ `.Count()` с полным перебором. Это происходит только для логирования, но это лишняя аллокация и перебор.

**Конкретно в чём проблема**  
Лишний LINQ-перебор коллекции ради логирования.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/SubscribeMessagesRead/Handlers/ReadByNotificationHandler.cs` : строки 34–40

```csharp
var streams = _subscriptionsManager.GetUserStreams(memberId);

_logger.LogDebug(
    "Отправка события прочтения пользователю {UserId}. Активных потоков: {StreamCount}",
    memberId,
    streams.Count() // ⚠️ LINQ .Count() — полный перебор IEnumerable для логирования
);
```

**Варианты решения**  
Изменить сигнатуру `GetUserStreams` на возврат `IReadOnlyList<T>`, тогда `.Count` будет O(1) свойством.

```csharp
// StreamSubscriptionsManager.cs
// ✅ Возвращаем IReadOnlyList вместо IEnumerable
public IReadOnlyList<IServerStreamWriter<MessageReadEvent>> GetUserStreams(long userId)
{
    if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        return userStreams.Values.ToList();
    return [];
}

// ReadByNotificationHandler.cs
var streams = _subscriptionsManager.GetUserStreams(memberId);
_logger.LogDebug("... Активных потоков: {StreamCount}", memberId, streams.Count); // ✅ O(1)
```

---

### OPT-04 — Двойной перебор Attachments в PushNotificationSchedulerHandler

**Проблема / Описание**  
В `PushNotificationSchedulerHandler` коллекция `Attachments` перебирается дважды: сначала для поиска `IMAGE`-вложения, затем снова для получения первого любого вложения как fallback. При большом количестве вложений это двойная работа.

**Конкретно в чём проблема**  
Два вызова `FirstOrDefault` на одной коллекции вложений.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs` : строки 67–82

```csharp
var imageAttachment = notification.Message.Content?.Attachments
    .FirstOrDefault(a => a.Type == MessageAttachmentType.Image); // перебор #1

// ...

var attachmentType = imageAttachment?.Type
    ?? notification.Message.Content?.Attachments.FirstOrDefault()?.Type // ⚠️ перебор #2 — снова FirstOrDefault
    ?? MessageAttachmentType.Unknown;
```

**Варианты решения**  
Однократно забрать первый элемент и кэшировать.

```csharp
var attachments = notification.Message.Content?.Attachments;
// ✅ Один проход: ищем image, и сразу запоминаем первый любой
var imageAttachment = attachments?.FirstOrDefault(a => a.Type == MessageAttachmentType.Image);
var firstAttachment = (imageAttachment is null && attachments?.Count > 0) ? attachments[0] : null;

string? imagePreviewUrl = imageAttachment?.PreviewUrl;
if (imageAttachment != null && string.IsNullOrEmpty(imagePreviewUrl))
{
    _logger.LogWarning("Image attachment has no PreviewUrl for message {MessageId}", notification.Message.Id);
}

// ✅ Нет второго FirstOrDefault
var attachmentType = imageAttachment?.Type
    ?? firstAttachment?.Type
    ?? MessageAttachmentType.Unknown;
```

---

### OPT-05 — Метрики active_subscriptions не декрементируют, а инкрементируют отдельный счётчик 🔄 ЧАСТИЧНО (2026-05-18)

> **Статус 2026-05-18:** В `UpdatesApiService.cs` уже появились gauge-метрики через `_metrics.Set(...)` (см. строки 115-116, 143-144), но монотонные `_metrics.Increment("active_subscriptions")` (строка 114) и `_metrics.Increment("active_subscriptions_removed")` (строка 129) сохраняются — рекомендуется удалить устаревшие счётчики, оставив только gauge.

**Проблема / Описание**  
В `UpdatesApiService` при подключении инкрементируется `"active_subscriptions"`, а при отключении — `"active_subscriptions_removed"`. Это два независимых монотонных счётчика, а не один gauge. Посмотрев только на `active_subscriptions`, нельзя узнать реальное число активных соединений.

**Конкретно в чём проблема**  
Нет gauge-метрики реального количества активных подписок.

**Путь к файлу:** `Backend/BarkFluff.Updates/Host/UpdatesApiService.cs` : строки 49, 64 (и 75, 90)

```csharp
_metrics.Increment("active_subscriptions");          // +1 при подключении

// ... в finally:
_metrics.Increment("active_subscriptions_removed");  // ⚠️ +1 в отдельный счётчик, а не -1 от первого
```

**Варианты решения**  
Использовать декремент или отдельный gauge-счётчик.

```csharp
// ✅ Вариант 1 — если MetricsCollector поддерживает Decrement:
_metrics.Increment("active_subscriptions");
// ... в finally:
_metrics.Decrement("active_subscriptions"); // реальный gauge

// ✅ Вариант 2 — читаемые имена с явным смыслом:
_metrics.Increment("subscriptions_total");       // counter — всего создано
// ... в finally:
_metrics.Increment("subscriptions_closed_total"); // counter — всего закрыто
// Реальное число активных = subscriptions_total - subscriptions_closed_total
```

---

## 🟠 Баги

---

### BUG-01 — Race condition при очистке пустой записи в StreamSubscriptionsManager

**Проблема / Описание**  
В `RemoveSubscription` после удаления подписки из `userStreams` выполняется проверка `userStreams.IsEmpty` и при пустоте — удаление из родительского словаря. Между этими двумя операциями другой поток может добавить новую подписку через `RegisterSubscription`, и тогда `TryRemove` удалит из родительского словаря уже **непустую** запись. Новая подписка окажется в "висящем" `ConcurrentDictionary`, который никогда не будет найден при `GetUserStreams`.

**Конкретно в чём проблема**  
Non-atomic check-then-act между `IsEmpty` и `TryRemove` родительского словаря.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/StreamSubscriptionsManager.cs` : строки 22–33

```csharp
public void RemoveSubscription(long userId, Guid subscriptionId)
{
    if (_userSubscriptions.TryGetValue(userId, out var userStreams))
    {
        userStreams.TryRemove(subscriptionId, out _);

        // ⚠️ Race condition: между IsEmpty и TryRemove другой поток мог добавить подписку
        if (userStreams.IsEmpty)
        {
            _userSubscriptions.TryRemove(userId, out _); // удалит живую запись!
        }
    }
}
```

**Варианты решения**  
Использовать перегрузку `TryRemove(key, value)` для атомарного удаления только если значение совпадает, или вовсе не удалять пустые записи (небольшой overhead от хранения пустых словарей — приемлем).

```csharp
public void RemoveSubscription(long userId, Guid subscriptionId)
{
    if (!_userSubscriptions.TryGetValue(userId, out var userStreams))
        return;

    userStreams.TryRemove(subscriptionId, out _);

    // ✅ Атомарное удаление родительской записи: удаляем только если словарь
    // всё ещё тот же объект и он пуст — используем TryRemove с проверкой значения
    if (userStreams.IsEmpty)
    {
        // Передаём конкретный экземпляр — если тем временем добавили новую подписку,
        // словарь будет другим объектом или содержать элементы, и удаление не произойдёт
        (_userSubscriptions as ICollection<KeyValuePair<long, ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>>>)
            .Remove(new KeyValuePair<long, ConcurrentDictionary<Guid, IServerStreamWriter<NewMessageEvent>>>(userId, userStreams));
    }
}

// ✅ Более чистый вариант — просто не удалять пустые записи,
// небольшой overhead от пустых ConcurrentDictionary на практике незначителен:
public void RemoveSubscription(long userId, Guid subscriptionId)
{
    if (_userSubscriptions.TryGetValue(userId, out var userStreams))
        userStreams.TryRemove(subscriptionId, out _);
    // пустые записи не удаляем — избегаем race condition
}
```

---

### BUG-02 — CancellationTokenSource утечка в PendingPushTracker при аварийном завершении

**Проблема / Описание**  
Если сервис завершается аварийно (crash, kill -9 и т.п.), все зарегистрированные в `_pendingPushes` объекты `CancellationTokenSource` не будут disposed. Это не critical для managed heap (GC соберёт), но `CancellationTokenSource` содержит unmanaged ресурсы (kernel wait handle при регистрации callback'ов), что создаёт реальную утечку. Кроме того, `PendingPushTracker` не реализует `IDisposable`.

**Конкретно в чём проблема**  
`PendingPushTracker` не реализует `IDisposable`, нет cleanup при shutdown.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/PushNotifications/PendingPushTracker.cs` : строки 9–57

```csharp
// ⚠️ Нет IDisposable — при shutdown все CTS в словаре не будут disposed
public class PendingPushTracker
{
    private readonly ConcurrentDictionary<(long MessageId, long UserId),
        CancellationTokenSource> _pendingPushes = new();

    // ... нет метода Dispose()
}
```

**Варианты решения**  
Реализовать `IDisposable` с очисткой всех оставшихся `CancellationTokenSource`.

```csharp
public class PendingPushTracker : IDisposable
{
    private readonly ConcurrentDictionary<(long MessageId, long UserId),
        CancellationTokenSource> _pendingPushes = new();
    private bool _disposed;

    // ... существующие методы без изменений ...

    /// <summary>✅ Корректный cleanup при shutdown сервиса</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var (key, cts) in _pendingPushes)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch { /* best effort */ }
        }
        _pendingPushes.Clear();
    }
}

// DependencyInjection.cs — регистрация остаётся Singleton,
// ASP.NET Core автоматически вызовет Dispose() при остановке
services.AddSingleton<PendingPushTracker>();
```

---

### BUG-03 — fire-and-forget Task.Run без отслеживания в PushNotificationSchedulerHandler

**Проблема / Описание**  
В `PushNotificationSchedulerHandler.Handle()` для каждого получателя запускается `_ = Task.Run(...)`. Возвращаемая задача игнорируется (`_ =`). Это означает:
1. Если задача завершится с необработанным исключением — оно потеряется (несмотря на внутренний `catch`, любое исключение в создании scope до `try` будет потеряно).
2. При shutdown сервиса нет возможности дождаться завершения всех запущенных задач.
3. Тест покрытия невозможен — метод `Handle` завершается до реальной отправки push.

**Конкретно в чём проблема**  
Задачи запускаются и забываются — нет lifecycle management.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs` : строки 53, 121

```csharp
// ⚠️ fire-and-forget: задача не отслеживается, shutdown не дождётся завершения
_ = Task.Run(async () =>
{
    try { ... }
    catch (OperationCanceledException) { ... }
    catch (Exception ex) { ... } // внутренний catch есть, но создание scope — вне него
    finally { _pendingPushTracker.RemovePending(...); }
}, CancellationToken.None);
```

**Варианты решения**  
Использовать `IHostedService` с `BackgroundService` или отслеживать задачи через `ConcurrentBag` с graceful drain при shutdown.

```csharp
// ✅ Вариант: регистрировать задачи в трекере для graceful shutdown
public class PushNotificationSchedulerHandler : INotificationHandler<NewMessageNotification>
{
    private readonly ConcurrentBag<Task> _activeTasks = new();

    public async Task Handle(NewMessageNotification notification, CancellationToken cancellationToken)
    {
        foreach (var userId in recipients)
        {
            var cts = _pendingPushTracker.RegisterPending(notification.Message.Id, userId);

            var task = Task.Run(async () =>
            {
                try { /* логика push */ }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.LogError(ex, "..."); }
                finally { _pendingPushTracker.RemovePending(notification.Message.Id, userId); }
            }, CancellationToken.None);

            _activeTasks.Add(task); // ✅ отслеживаем задачу
        }
    }

    // ✅ Вызывается при shutdown (если реализовать IAsyncDisposable или через IHostApplicationLifetime)
    public async Task DrainAsync()
    {
        await Task.WhenAll(_activeTasks.ToArray());
    }
}
```

---

### BUG-04 — ReadByNotification использует class вместо record: потенциальные null-поля

**Проблема / Описание**  
`ReadByNotification` объявлена как `class` со свойствами без инициализаторов и без конструктора. Свойства `NewReadBy` и `ChatMembers` объявлены как `List<long>` без `= null!` или `= []`. Создание объекта без инициализации всех свойств вызовет `NullReferenceException` при итерации (например, в `ReadByCancelPushHandler.Handle` — `foreach (var userId in notification.NewReadBy)`).

**Конкретно в чём проблема**  
Нет защиты от null-значений в коллекциях нотификации.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/SubscribeMessagesRead/ReadByNotification.cs` : строки 5–14

```csharp
public class ReadByNotification : INotification
{
    public Guid ChatId { get; set; }
    public long MessageId { get; set; }

    // ⚠️ Нет инициализатора — если забыли присвоить, NullReferenceException при foreach
    public List<long> NewReadBy { get; set; }
    public List<long> ChatMembers { get; set; }
}
```

**Варианты решения**  
Преобразовать в `record` с обязательными параметрами конструктора (как `NewMessageNotification`), либо добавить инициализаторы.

```csharp
// ✅ Вариант 1 — record (консистентно с NewMessageNotification)
public record ReadByNotification(
    Guid ChatId,
    long MessageId,
    IReadOnlyList<long> NewReadBy,    // immutable
    IReadOnlyList<long> ChatMembers   // immutable
) : INotification;

// ✅ Вариант 2 — class с инициализаторами (если нужна мутабельность)
public class ReadByNotification : INotification
{
    public Guid ChatId { get; set; }
    public long MessageId { get; set; }
    public List<long> NewReadBy { get; set; } = [];   // ✅ никогда не null
    public List<long> ChatMembers { get; set; } = []; // ✅ никогда не null
}
```

---

### BUG-05 — NewMessageConsumer: исключение при re-throw приводит к повторной обработке сообщения

**Проблема / Описание**  
В `NewMessageConsumer.Consume()` при ошибке парсинга или публикации через MediatR выбрасывается исключение (`throw`). MassTransit по умолчанию при необработанном исключении **повторно помещает сообщение в очередь** (retry policy). Если сообщение изначально некорректно (например, невалидный protobuf), сервис будет бесконечно его перепроцессировать, забивая логи и RabbitMQ.

**Конкретно в чём проблема**  
Нет разграничения между transient-ошибками (retry уместен) и permanent-ошибками (сообщение невалидно → нужно в dead-letter queue).

**Путь к файлу:** `Backend/BarkFluff.Updates/Consumers/NewMessageConsumer.cs` : строки 57–66

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Ошибка при обработке события нового сообщения для чата {ChatId}", context.Message.ChatId);
    throw; // ⚠️ MassTransit повторит доставку — при невалидном protobuf будет бесконечный retry
}
```

**Варианты решения**  
Разграничить типы ошибок: ошибки парсинга — не retryable (publish fault / skip), остальные — retryable.

```csharp
public async Task Consume(ConsumeContext<NewMessageEvent> context)
{
    _metrics.Increment("rabbitmq_events_consumed");

    Message message;
    try
    {
        // ✅ Ошибки парсинга — permanent failure, не retryable
        message = Message.Parser.ParseFrom(context.Message.Message);
    }
    catch (InvalidProtocolBufferException ex)
    {
        _logger.LogError(ex, "Невалидный protobuf для чата {ChatId} — сообщение будет пропущено", context.Message.ChatId);
        // Не бросаем исключение — MassTransit считает обработку успешной,
        // сообщение не вернётся в очередь (или настраиваем dead-letter отдельно)
        return;
    }

    try
    {
        // ✅ Ошибки публикации — transient, retry уместен
        await _mediator.Publish(new NewMessageNotification(message, context.Message.ChatMembers, context.Message.ChatId));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Ошибка при публикации уведомления для сообщения {MessageId}", message.Id);
        throw; // transient — пусть MassTransit retries
    }
}
```

---

## 🔵 Прочее / Качество кода

---

### MISC-01 — Отсутствует graceful shutdown для активных gRPC стримов

**Проблема / Описание**  
При остановке сервиса (SIGTERM) активные gRPC-стримы в `UpdatesApiService` просто обрываются без уведомления клиентов. Клиенты получат ошибку соединения вместо чистого завершения стрима. Нет регистрации на `IHostApplicationLifetime.ApplicationStopping` для отправки клиентам сигнала о завершении.

**Конкретно в чём проблема**  
Клиенты не уведомляются о graceful shutdown сервиса.

**Путь к файлу:** `Backend/BarkFluff.Updates/Host/UpdatesApiService.cs` : весь класс

```csharp
// ⚠️ Нет обработки ApplicationStopping — клиенты получат обрыв соединения
public class UpdatesApiService : UpdatesApiBase
{
    // При SIGTERM: Task.Delay отменяется через context.CancellationToken,
    // но CancellationToken gRPC context не связан с ApplicationStopping
}
```

**Варианты решения**  
Связать `CancellationToken` запроса с `ApplicationStopping` токеном.

```csharp
public override async Task SubscribeNewMessages(
    SubscribeNewMessagesRequest request,
    IServerStreamWriter<NewMessageEvent> responseStream,
    ServerCallContext context)
{
    long userId = _userContext.UserId;
    var subscriptionId = _newMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
    _metrics.Increment("active_subscriptions");

    // ✅ Объединяем токен запроса с токеном остановки приложения
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        context.CancellationToken,
        _applicationLifetime.ApplicationStopping); // IHostApplicationLifetime через DI

    try
    {
        await Task.Delay(Timeout.Infinite, linkedCts.Token);
    }
    catch (OperationCanceledException) { }
    finally
    {
        _newMessagesSubscriptionsManager.RemoveSubscription(userId, subscriptionId);
        _metrics.Decrement("active_subscriptions");
    }
}
```

---

### MISC-02 — Задержка push-уведомления захардкожена в 5 секунд

**Проблема / Описание**  
Значение `TimeSpan.FromSeconds(5)` жёстко вшито в код обработчика. Изменение задержки требует перекомпиляции сервиса. В production это может потребоваться менять без деплоя.

**Конкретно в чём проблема**  
Magic number в бизнес-логике без конфигурации.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs` : строка 58

```csharp
// ⚠️ Магическое число — нельзя изменить без перекомпиляции
await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
```

**Варианты решения**  
Вынести в конфигурацию через `IOptions<T>`.

```csharp
// PushNotificationOptions.cs
public class PushNotificationOptions
{
    /// <summary>Задержка перед отправкой push (сообщение считается непрочитанным)</summary>
    public TimeSpan PushDelay { get; set; } = TimeSpan.FromSeconds(5); // default
}

// DependencyInjection.cs
services.AddOptions<PushNotificationOptions>()
    .BindConfiguration("PushNotifications")
    .ValidateDataAnnotations();

// PushNotificationSchedulerHandler.cs
private readonly PushNotificationOptions _options;

// В Handle():
await Task.Delay(_options.PushDelay, cts.Token); // ✅ конфигурируемо
```

---

### MISC-03 — Асимметрия стиля записи нотификаций (record vs class)

**Проблема / Описание**  
`NewMessageNotification` объявлена как `record` (immutable, value semantics), а `ReadByNotification` — как `class` (mutable, reference semantics). Это создаёт несогласованность в кодовой базе, которая может запутать разработчиков.

**Конкретно в чём проблема**  
Нет единого стиля для DTO/notification объектов.

**Путь к файлам:**  
- `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/NewMessageNotification.cs` : строка 7
- `Backend/BarkFluff.Updates/Features/SubscribeMessagesRead/ReadByNotification.cs` : строка 5

```csharp
// NewMessageNotification.cs
public record NewMessageNotification(...) : INotification; // record ✅

// ReadByNotification.cs
public class ReadByNotification : INotification { ... }    // ⚠️ class — несогласованность
```

**Варианты решения**  
Привести оба типа к `record` (см. BUG-04).

```csharp
// ✅ Единый стиль — оба notification как record
public record ReadByNotification(
    Guid ChatId,
    long MessageId,
    IReadOnlyList<long> NewReadBy,
    IReadOnlyList<long> ChatMembers
) : INotification;
```

---

### MISC-04 — Метод Handle в ReadByCancelPushHandler возвращает Task.CompletedTask синхронно

**Проблема / Описание**  
`ReadByCancelPushHandler.Handle()` объявлен как `Task`, но возвращает `Task.CompletedTask` синхронно. Это не баг, но при этом сигнатура метода `public Task Handle(...)` вводит в заблуждение — создаётся впечатление асинхронной операции. Лучше явно это задокументировать или использовать синхронный паттерн если MediatR его поддерживает.

**Конкретно в чём проблема**  
Псевдо-асинхронный метод без `async`/`await` — косметически вводит в заблуждение.

**Путь к файлу:** `Backend/BarkFluff.Updates/Features/PushNotifications/ReadByCancelPushHandler.cs` : строки 24–37

```csharp
// ⚠️ Метод объявлен как Task, но синхронный — нет async/await
public Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
{
    foreach (var userId in notification.NewReadBy)
    {
        _pendingPushTracker.CancelPending(notification.MessageId, userId);
        // ...
    }
    return Task.CompletedTask; // синхронный возврат
}
```

**Варианты решения**  
Добавить `async` и убрать явный `return Task.CompletedTask`, или оставить как есть с комментарием.

```csharp
// ✅ Вариант 1 — явная async-сигнатура (компилятор оптимизирует):
public async Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
{
    foreach (var userId in notification.NewReadBy)
    {
        _pendingPushTracker.CancelPending(notification.MessageId, userId);
        _logger.LogDebug("Отменено push-уведомление для сообщения {MessageId} пользователю {UserId}",
            notification.MessageId, userId);
    }
    // async без await — компилятор предупредит, но семантика правильная
}

// ✅ Вариант 2 — оставить Task.CompletedTask, добавить комментарий:
/// <remarks>Операция синхронная, Task.CompletedTask возвращается для соответствия интерфейсу INotificationHandler.</remarks>
public Task Handle(ReadByNotification notification, CancellationToken cancellationToken)
{
    // ...
    return Task.CompletedTask;
}
```

---

## Сводная таблица

| ID | Категория | Критичность | Файл | Описание |
|---|---|---|---|---|
| SEC-01 | 🔴 Безопасность | Высокая | `Program.cs` | Нет валидации credentials RabbitMQ |
| SEC-02 | 🔴 Безопасность | Высокая | `StreamSubscriptionsManager.cs` | DoS: нет лимита подписок на пользователя |
| SEC-03 | 🔴 Безопасность | Средняя | `Program.cs` | gRPC Reflection доступен в продакшне |
| OPT-01 | 🟡 Оптимизация | Средняя | `NewMessageNotificationHandler.cs` / `ReadByNotificationHandler.cs` | Лишний `Task.Run` для I/O операций |
| OPT-02 | 🟡 Оптимизация | Средняя | Оба `StreamSubscriptionsManager.cs` | 100% дублирование кода |
| OPT-03 | 🟡 Оптимизация | Низкая | `ReadByNotificationHandler.cs` | `IEnumerable.Count()` вместо `IReadOnlyList.Count` |
| OPT-04 | 🟡 Оптимизация | Низкая | `PushNotificationSchedulerHandler.cs` | Двойной перебор коллекции Attachments |
| OPT-05 | 🟡 Оптимизация | Средняя | `UpdatesApiService.cs` | Метрики не дают реальный gauge активных подписок |
| BUG-01 | 🟠 Баг | Высокая | Оба `StreamSubscriptionsManager.cs` | Race condition при очистке пустой записи |
| BUG-02 | 🟠 Баг | Средняя | `PendingPushTracker.cs` | Нет `IDisposable` — утечка CTS при shutdown |
| BUG-03 | 🟠 Баг | Высокая | `PushNotificationSchedulerHandler.cs` | fire-and-forget без lifecycle management |
| BUG-04 | 🟠 Баг | Средняя | `ReadByNotification.cs` | `null`-поля без инициализаторов → NRE |
| BUG-05 | 🟠 Баг | Высокая | `NewMessageConsumer.cs` | Невалидный protobuf → бесконечный retry |
| MISC-01 | 🔵 Прочее | Средняя | `UpdatesApiService.cs` | Нет graceful shutdown для стримов |
| MISC-02 | 🔵 Прочее | Низкая | `PushNotificationSchedulerHandler.cs` | Задержка push захардкожена |
| MISC-03 | 🔵 Прочее | Низкая | `ReadByNotification.cs` | `class` вместо `record` — несогласованность |
| MISC-04 | 🔵 Прочее | Низкая | `ReadByCancelPushHandler.cs` | Псевдо-async метод без `async`/`await` |
