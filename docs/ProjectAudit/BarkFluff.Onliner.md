# Аудит проекта: BarkFluff.Onliner

> **Дата:** 2026-05-06
> **Ветка:** dev
> **Описание сервиса:** Микросервис управления онлайн-статусами пользователей. In-memory хранилище + PostgreSQL, gRPC API, RabbitMQ consumer, два background service

## 🔴 Безопасность

---

### SEC-01 — Fail-open в OnlineVisibilityFilter

**Проблема / Описание**
При любой ошибке gRPC-запроса к UsersService (сеть, таймаут, сервис недоступен) фильтр видимости возвращает `true` — т.е. статус считается публичным. Злоумышленник, вызвав нестабильность UsersService, получит доступ к статусам пользователей, скрывших их.

**Конкретно в чём проблема**
Catch-блок при ошибке подставляет `return true` (видимо), что означает «показать статус всем», вместо «скрыть при неопределённости».

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs` : строки 49–63

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex,
        "Failed to fetch privacy for user {UserId}, defaulting to visible", // ⚠️ Небезопасный default
        targetUserId);
    return true; // ❌ При недоступности UsersService — статус раскрывается всем
}
```

**Варианты решения**

**Вариант A — Fail-closed (рекомендуется):** при ошибке скрывать статус.

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex,
        "Failed to fetch privacy for user {UserId}, defaulting to hidden (fail-closed)",
        targetUserId);
    return false; // ✅ Безопасный default: при неопределённости — скрыть
}
```

## 🟠 Оптимизация производительности

---

### 

### 

### PERF-03 — GetStreamsTrackingUser: O(N×M) при каждом изменении статуса

**Проблема / Описание**
При каждом изменении онлайн-статуса любого пользователя `OnlineStatusNotifier` вызывает `GetStreamsTrackingUser`, которая итерирует **все** подписки **всех** подписчиков и проверяет `HashSet.Contains`. При 1000 подписчиков с 200 отслеживаемыми пользователями — 200 000 операций на каждое изменение статуса.

**Конкретно в чём проблема**
Обратный индекс отсутствует: `userId → List<Stream>`.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineStatusSubscriptionsManager.cs` : строки 72–90

```csharp
public List<IServerStreamWriter<UserOnlineStatus>> GetStreamsTrackingUser(long userId)
{
    var streams = new List<IServerStreamWriter<UserOnlineStatus>>();

    foreach (var subscriberKvp in _subscriptions) // ❌ Перебираем ВСЕХ подписчиков
    {
        foreach (var subscriptionKvp in subscriberKvp.Value) // ❌ Все подключения каждого
        {
            if (subscription.TrackedUserIds.Contains(userId)) // HashSet.Contains — O(1), но цикл O(N×M)
                streams.Add(subscription.Stream);
        }
    }
    return streams;
}
```

**Варианты решения**

Добавить инвертированный индекс `trackedUserId → List<connectionId>`:

```csharp
// В OnlineStatusSubscriptionsManager добавить второй индекс:
// TrackedUserId -> Set<ConnectionId>
private readonly ConcurrentDictionary<long, ConcurrentHashSet<Guid>> _reverseIndex = new();

// При RegisterSubscription — добавлять в reverseIndex
public Guid RegisterSubscription(long subscriberId, List<long> trackedUserIds, ...)
{
    var connectionId = Guid.NewGuid();
    // ... обычная регистрация ...

    // ✅ Обновляем обратный индекс
    foreach (var trackedId in trackedUserIds)
    {
        _reverseIndex
            .GetOrAdd(trackedId, _ => new ConcurrentHashSet<Guid>())
            .Add(connectionId);
    }
    return connectionId;
}

// GetStreamsTrackingUser — O(1) lookup вместо O(N×M)
public List<IServerStreamWriter<UserOnlineStatus>> GetStreamsTrackingUser(long userId)
{
    if (!_reverseIndex.TryGetValue(userId, out var connectionIds))
        return [];

    // ✅ Только нужные подписки по connectionId
    return connectionIds
        .Select(cid => FindStreamByConnectionId(cid))
        .Where(s => s != null)
        .ToList();
}
```

---

### PERF-04 — Метрики: счётчик active_subscriptions только увеличивается

**Проблема / Описание**
В `OnlinerApiService` при каждом вызове `SubscribeToOnlineStatus` метрика `active_subscriptions` инкрементируется, но **никогда не декрементируется** при отключении клиента. Метрика показывает накопленное количество подключений за всё время работы, а не текущее.

**Конкретно в чём проблема**
`Increment` без парного `Decrement` при завершении подписки.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Host/OnlinerApiService.cs` : строки 57–70

```csharp
public override Task SubscribeToOnlineStatus(...)
{
    _metrics.Increment("active_subscriptions"); // ❌ Только увеличивается, никогда не уменьшается

    var query = new SubscribeToOnlineStatusQuery { ... };
    return _subscribeHandler.Handle(query);
}
```

**Варианты решения**

```csharp
public override async Task SubscribeToOnlineStatus(
    SubscribeToOnlineStatusRequest request,
    IServerStreamWriter<UserOnlineStatus> responseStream,
    ServerCallContext context)
{
    _metrics.Increment("active_subscriptions"); // ✅ Увеличиваем при подключении
    try
    {
        var query = new SubscribeToOnlineStatusQuery { ... };
        await _subscribeHandler.Handle(query);
    }
    finally
    {
        _metrics.Decrement("active_subscriptions"); // ✅ Уменьшаем при отключении
    }
}
```

---

### PERF-05 — GetAllStatuses создаёт полную копию всех статусов каждые 10 минут

**Проблема / Описание**
`OnlineStatusStorage.GetAllStatuses()` создаёт новый `List<UserOnlineStatus>` с копией каждого объекта через `.Select(s => new UserOnlineStatus {...})`. При большом количестве пользователей (например, 100k) это создаёт значительное давление на GC каждые 10 минут.

**Конкретно в чём проблема**
Полное копирование данных без необходимости — `DatabasePersistenceService` только читает, не модифицирует.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineStatusStorage.cs` : строки 112–120

```csharp
public List<UserOnlineStatus> GetAllStatuses()
{
    return _statuses.Values
        .Select(s => new UserOnlineStatus // ❌ Создаём N новых объектов
        {
            UserId = s.UserId,
            Status = s.Status,
            LastSeen = s.LastSeen
        })
        .ToList();
}
```

**Варианты решения**

```csharp
// ✅ Возвращаем IReadOnlyCollection — без копирования объектов
// Поскольку DatabasePersistenceService только читает данные, копировать не нужно
public IReadOnlyCollection<UserOnlineStatus> GetAllStatuses()
{
    // ConcurrentDictionary.Values — уже snapshot ключей, объекты те же
    return _statuses.Values.ToList(); // ✅ Без Select new — один shallow snapshot
}
```

Если нужна защита от модификации извне — использовать `record` для `UserOnlineStatus` (иммутабельность по дизайну).

---

## 🟡 Баги и логические ошибки

### BUG-02 — statusChanged всегда false при AddOrUpdate race condition

**Проблема / Описание**
Переменная `statusChanged` захватывается через closure в factory-функции `AddOrUpdate`. Если два потока одновременно вызывают `UpdateStatus` для одного пользователя, оба могут прочитать `existing.Status != Online` как `true` и оба установят `statusChanged = true`. В результате будут отправлены два уведомления об одном событии подписчикам.

**Конкретно в чём проблема**
Closure переменная `statusChanged` и операция проверки-изменения не атомарны по отношению друг к другу.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineStatusStorage.cs` : строки 22–52

```csharp
bool statusChanged = false; // ❌ Захватывается в closure

_statuses.AddOrUpdate(
    userId,
    _ => { statusChanged = true; return new UserOnlineStatus { ... }; }, // ❌ Closure write
    (_, existing) =>
    {
        if (existing.Status != StatusTypeId.Online)
            statusChanged = true; // ❌ Не атомарно — два потока могут войти сюда одновременно
        ...
    }
);
```

**Варианты решения**

```csharp
// ✅ Возвращать информацию об изменении через возвращаемое значение или Interlocked
// Более надёжный подход — использовать специальный tuple-возврат

private (UserOnlineStatus status, bool changed) CreateOrUpdateOnline(long userId)
{
    bool changed = false;
    var newStatus = _statuses.AddOrUpdate(
        userId,
        _ => { changed = true; return new UserOnlineStatus { UserId = userId, Status = StatusTypeId.Online, LastSeen = DateTime.UtcNow }; },
        (_, existing) =>
        {
            // ✅ Сравниваем старый статус ДО изменения через Interlocked или новый объект
            changed = existing.Status != StatusTypeId.Online;
            return new UserOnlineStatus { UserId = userId, Status = StatusTypeId.Online, LastSeen = DateTime.UtcNow };
        }
    );
    return (newStatus, changed);
}
```

Полностью устранить проблему позволит переход на `ImmutableDictionary` + `Interlocked.CompareExchange` или использование `Channel<T>` для последовательной обработки изменений.

---

### 

## 🔵 Прочее / Технический долг

---

### TD-04 — Метрики: счётчик status_changes не разделён по типу перехода

**Проблема / Описание**
`_metrics.Increment("status_changes")` вызывается только в `SetOnlineStatus` (переход → Online). Переходы → Offline (из `OfflineDetectionService`) не считаются вообще. Нет разделения `online_events` / `offline_events`. Метрики не дают реальной картины активности.

**Конкретно в чём проблема**
Неполный охват метриками, нет дифференциации типов событий.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Host/OnlinerApiService.cs` : строка 53  
**Путь к файлу:** `Backend/BarkFluff.Onliner/BackgroundServices/OfflineDetectionService.cs` : строка 73

```csharp
// OnlinerApiService.cs
_metrics.Increment("status_changes"); // ❌ Только Online-события

// OfflineDetectionService.cs
_metrics.Increment("offline_detections"); // Считается отдельно, но не "status_changes"
// ❌ Нет единой метрики с тегами type=online / type=offline
```

**Варианты решения**

```csharp
// ✅ Использовать теги/labels для разделения типов
_metrics.Increment("status_changes", tags: new { type = "online" });  // в SetOnlineStatus
_metrics.Increment("status_changes", tags: new { type = "offline" }); // в OfflineDetectionService
```
