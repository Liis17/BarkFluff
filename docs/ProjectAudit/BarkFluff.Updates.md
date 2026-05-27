# Аудит проекта: BarkFluff.Updates

> **Дата создания:** 2025  
> **Последняя проверка актуальности:** 2026-05-18  
> **Ветка:** `dev`  
> **Покрытие:** все файлы проекта `Backend/BarkFluff.Updates`  
> **Категории:** 🔴 Безопасность · 🟡 Оптимизация · 🟠 Баги · 🔵 Прочее

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

p.s проверить что это действительно что то сделать в лучшую сторону

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
