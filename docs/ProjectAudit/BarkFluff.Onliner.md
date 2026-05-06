# Аудит проекта: BarkFluff.Onliner

> **Дата:** 2026-05-06
> **Ветка:** dev
> **Описание сервиса:** Микросервис управления онлайн-статусами пользователей. In-memory хранилище + PostgreSQL, gRPC API, RabbitMQ consumer, два background service.

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟠 Оптимизация производительности](#-оптимизация-производительности)
- [🟡 Баги и логические ошибки](#-баги-и-логические-ошибки)
- [🔵 Прочее / Технический долг](#-прочее--технический-долг)

---

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

**Вариант B — с разделением по типу исключения:** разрешать только при временных сбоях, запрещать при критических.

```csharp
catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Unavailable)
{
    _logger.LogWarning("UsersService unavailable, defaulting to hidden");
    return false; // ✅ Сервис недоступен — скрываем
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error fetching privacy for user {UserId}", targetUserId);
    return false; // ✅ Любая неизвестная ошибка — скрываем
}
```

---

### SEC-02 — Нет лимита на количество userId в запросах

**Проблема / Описание**
`GetOnlineStatus` и `SubscribeToOnlineStatus` принимают неограниченный `List<long> UserIds`. Клиент может прислать миллион ID, что вызовет N×gRPC-запросов к UsersService (в `OnlineVisibilityFilter`) и поставит под угрозу всю платформу (DoS через легитимный запрос).

**Конкретно в чём проблема**
`GetVisibleUserIdsAsync` итерирует каждый ID отдельно в цикле `foreach`, без верхней границы.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs` : строки 22–42  
**Путь к файлу:** `Backend/BarkFluff.Onliner/Host/OnlinerApiService.cs` : строки 37–44

```csharp
// OnlinerApiService.cs
var query = new GetOnlineStatusQuery
{
    UserIds = request.UserIds.ToList() // ❌ Нет валидации размера списка
};

// OnlineVisibilityFilter.cs
foreach (var targetId in targetUserIds.Distinct()) // ❌ Может итерировать миллион ID
{
    if (await IsVisibleToCaller(targetId, cancellationToken)) // ❌ N gRPC-запросов
    ...
}
```

**Варианты решения**

```csharp
// В OnlinerApiService.cs — добавить валидацию
public override Task<GetOnlineStatusResponse> GetOnlineStatus(
    GetOnlineStatusRequest request,
    ServerCallContext context)
{
    const int MaxUserIds = 500; // ✅ Разумный лимит

    if (request.UserIds.Count > MaxUserIds)
        throw new RpcException(new Status(
            StatusCode.InvalidArgument,
            $"Too many user IDs. Maximum allowed: {MaxUserIds}"));

    var query = new GetOnlineStatusQuery
    {
        UserIds = request.UserIds.ToList()
    };
    return _mediator.Send(query, context.CancellationToken);
}
```

---

### SEC-03 — FRIENDS visibility трактуется как NONE

**Проблема / Описание**
Настройка приватности `FRIENDS` (только друзья видят статус) игнорируется и обрабатывается как `NONE` (никто не видит). Пользователи, выставившие `FRIENDS`, думают что их видят только друзья, но на деле их статус скрыт от всех — включая друзей. Это нарушение ожидаемого поведения с точки зрения пользователя.

**Конкретно в чём проблема**
Единственное условие видимости — `OnlineVisibility == All`. Всё остальное скрыто.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs` : строки 51–56

```csharp
// FRIENDS == NONE пока нет сервиса отношений.
// TODO: активировать FRIENDS, когда появится сервис отношений.
return response.Settings.OnlineVisibility == ProfileFieldVisibility.All;
// ❌ FRIENDS трактуется как скрытый, но пользователь этого не знает
```

**Варианты решения**

Минимальный вариант — пока нет сервиса отношений, хотя бы задокументировать поведение и, при необходимости, уведомлять клиент о том что FRIENDS = скрыто. В коде добавить явный лог:

```csharp
var visibility = response.Settings.OnlineVisibility;

if (visibility == ProfileFieldVisibility.Friends)
{
    // ✅ Явно логируем что FRIENDS пока = NONE, чтобы было заметно при ревью
    _logger.LogDebug(
        "User {UserId} has FRIENDS visibility — treated as NONE (no relationship service yet)",
        targetUserId);
    return false;
}

return visibility == ProfileFieldVisibility.All;
```

---

## 🟠 Оптимизация производительности

---

### PERF-01 — N+1 gRPC-запросов в OnlineVisibilityFilter

**Проблема / Описание**
Для каждого userId из запроса делается отдельный gRPC-вызов `GetUserPrivacy` к UsersService. При запросе статусов 100 пользователей — 100 последовательных (через `foreach`) gRPC-запросов. Это критически медленно и создаёт нагрузку на UsersService.

**Конкретно в чём проблема**
Нет batch-метода, цикл последовательный (не параллельный).

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs` : строки 28–41

```csharp
foreach (var targetId in targetUserIds.Distinct())
{
    if (targetId == callerUserId) { visible.Add(targetId); continue; }

    if (await IsVisibleToCaller(targetId, cancellationToken)) // ❌ Последовательный N gRPC-вызовов
    {
        visible.Add(targetId);
    }
}
```

**Варианты решения**

**Вариант A — Параллельные запросы** (быстрое улучшение):

```csharp
public async Task<HashSet<long>> GetVisibleUserIdsAsync(
    IEnumerable<long> targetUserIds,
    long callerUserId,
    CancellationToken cancellationToken = default)
{
    var ids = targetUserIds.Distinct().ToList();

    // ✅ Разделяем caller (всегда видимый) и остальных
    var selfId = ids.Where(id => id == callerUserId).ToHashSet();
    var othersIds = ids.Where(id => id != callerUserId).ToList();

    // ✅ Параллельные запросы (ограничиваем параллелизм)
    var semaphore = new SemaphoreSlim(10); // макс 10 одновременных
    var tasks = othersIds.Select(async id =>
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return (id, await IsVisibleToCaller(id, cancellationToken));
        }
        finally { semaphore.Release(); }
    });

    var results = await Task.WhenAll(tasks);
    var visible = selfId;
    foreach (var (id, isVisible) in results)
        if (isVisible) visible.Add(id);

    return visible;
}
```

**Вариант B — Batch gRPC endpoint** (кардинальное решение): добавить в UsersService метод `GetBulkPrivacy(List<long> userIds)` и получать все настройки одним запросом.

---

### PERF-02 — N+1 запросов к БД в DatabasePersistenceService

**Проблема / Описание**
При каждом цикле сохранения (каждые 10 минут) для каждого пользователя выполняется отдельный `FirstOrDefaultAsync` + `SaveChangesAsync` после каждой итерации (точнее один `SaveChangesAsync` в конце, но `FirstOrDefaultAsync` — по одному на каждый статус). При 10 000 пользователей онлайн — 10 000 SELECT-запросов.

**Конкретно в чём проблема**
Нет batch-upsert. Каждый статус — отдельный SELECT.

**Путь к файлу:** `Backend/BarkFluff.Onliner/BackgroundServices/DatabasePersistenceService.cs` : строки 70–89

```csharp
foreach (var status in allStatuses)
{
    var existing = await dbContext.UsersOnlineStatuses
        .FirstOrDefaultAsync(s => s.UserId == status.UserId, cancellationToken); // ❌ N SELECT-запросов

    if (existing != null) { existing.Status = ...; existing.LastSeen = ...; }
    else { dbContext.UsersOnlineStatuses.Add(status); }
}

await dbContext.SaveChangesAsync(cancellationToken); // Один bulk INSERT/UPDATE — это ок
```

**Варианты решения**

```csharp
private async Task SaveStatusesToDatabaseAsync(CancellationToken cancellationToken)
{
    var allStatuses = _storage.GetAllStatuses();
    if (allStatuses.Count == 0) return;

    using var scope = _serviceProvider.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();

    // ✅ Один запрос — получаем все существующие записи
    var userIds = allStatuses.Select(s => s.UserId).ToHashSet();
    var existingDict = await dbContext.UsersOnlineStatuses
        .Where(s => userIds.Contains(s.UserId))
        .ToDictionaryAsync(s => s.UserId, cancellationToken); // ✅ Один SELECT

    foreach (var status in allStatuses)
    {
        if (existingDict.TryGetValue(status.UserId, out var existing))
        {
            existing.Status = status.Status;
            existing.LastSeen = status.LastSeen;
        }
        else
        {
            dbContext.UsersOnlineStatuses.Add(status);
        }
    }

    await dbContext.SaveChangesAsync(cancellationToken); // ✅ Один batch
    _logger.LogInformation("Saved {Count} statuses to database", allStatuses.Count);
}
```

---

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

---

### BUG-01 — Race condition в UpdateStatus / SetOffline (мутация shared объекта)

**Проблема / Описание**
В `ConcurrentDictionary.AddOrUpdate` в update-factory функции напрямую мутируется объект `existing` (`existing.Status = ...`). Тот же объект может одновременно читаться другим потоком через `GetStatus`. `ConcurrentDictionary` гарантирует атомарность операций со словарём, но **не** атомарность чтения полей самого объекта. Это классический race condition на уровне объекта.

**Конкретно в чём проблема**
`UserOnlineStatus` — mutable class, объект разделяется между потоками.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Services/OnlineStatusStorage.cs` : строки 37–49, строки 73–88

```csharp
(_, existing) =>
{
    if (existing.Status != StatusTypeId.Online)
        statusChanged = true;

    existing.Status = StatusTypeId.Online;   // ❌ Мутация shared объекта
    existing.LastSeen = DateTime.UtcNow;     // ❌ Другой поток может читать в этот момент
    return existing;
}
```

**Варианты решения**

```csharp
// ✅ Всегда возвращать новый объект, не мутировать existing
(_, existing) =>
{
    if (existing.Status != StatusTypeId.Online)
        statusChanged = true;

    return new UserOnlineStatus // ✅ Новый иммутабельный объект — нет race condition
    {
        UserId = existing.UserId,
        Status = StatusTypeId.Online,
        LastSeen = DateTime.UtcNow
    };
}
```

Или сделать `UserOnlineStatus` record-ом:

```csharp
public record UserOnlineStatus
{
    [Key]
    public long UserId { get; init; }
    public StatusTypeId Status { get; init; }
    public DateTime LastSeen { get; init; }
}
```

---

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

### BUG-03 — OfflineDetectionService: двойное уведомление при restart

**Проблема / Описание**
При перезапуске сервиса `DatabasePersistenceService` читает из БД последний известный статус. Если пользователь был Online при остановке — при старте он снова окажется Online в памяти (после `ctx.Database.Migrate()` данные не загружаются в память автоматически). `OfflineDetectionService` через 5 секунд пометит его offline и уведомит подписчиков. Но подписчиков нет (они переподключаются), а статус в БД был Online — пользователь выглядит как внезапно вышедший offline.

**Конкретно в чём проблема**
После рестарта сервиса нет prewarm данных из БД в `OnlineStatusStorage`. Пользователи, бывшие online, не восстанавливают статус.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Program.cs` : строки 76–81

```csharp
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();
    ctx.Database.Migrate(); // ✅ Миграция есть
    // ❌ Нет prewarm: OnlineStatusStorage не заполняется данными из БД при старте
}
```

**Варианты решения**

```csharp
// ✅ После миграции — загружаем последние known статусы в память
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<OnlineStatusContext>();
    ctx.Database.Migrate();

    // Загружаем все статусы из БД в OnlineStatusStorage при старте
    var storage = scope.ServiceProvider.GetRequiredService<OnlineStatusStorage>();
    var statuses = await ctx.UsersOnlineStatuses.ToListAsync();

    foreach (var s in statuses)
    {
        // Помечаем всех как Offline при старте — клиенты переподключатся и поставят Online
        storage.InitializeOffline(s.UserId, s.LastSeen);
    }
}
```

---

### BUG-04 — ChangeUsersInSubscription бросает исключение при отсутствии подписки

**Проблема / Описание**
Если клиент вызывает `ChangeUsersInSubscription` до того как успела зарегистрироваться подписка (race condition между `SubscribeToOnlineStatus` и `ChangeUsersInSubscription`), сервер бросает `RpcException(FailedPrecondition)`. Клиент получает ошибку и должен обрабатывать её, хотя логически это нормальная ситуация гонки.

**Конкретно в чём проблема**
Жёсткий `throw` вместо мягкого ответа при отсутствии подписки — нарушение принципа idempotency для вспомогательной операции.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Features/ChangeUsersInSubscription/ChangeUsersInSubscriptionCommandHandler.cs` : строки 50–57

```csharp
if (updatedCount == 0)
{
    _logger.LogWarning("User {UserId} attempted to update subscriptions but has none active", userId);

    throw new RpcException(new Status( // ❌ Исключение при нормальной ситуации гонки
        StatusCode.FailedPrecondition,
        "No active subscriptions found"));
}
```

**Варианты решения**

```csharp
// ✅ Возвращать успешный ответ, логировать как debug/warning
if (updatedCount == 0)
{
    _logger.LogDebug(
        "User {UserId} called ChangeUsersInSubscription but has no active subscriptions — ignoring",
        userId);

    return new ChangeUsersInSubscriptionResponse(); // ✅ Идемпотентный успешный ответ
}
```

---

### BUG-05 — DatabasePersistenceService: первый save только через 10 минут после старта

**Проблема / Описание**
`ExecuteAsync` сначала делает `await Task.Delay(SaveInterval, ...)`, и только потом первый save. При неожиданном рестарте в первые 10 минут работы все накопленные статусы теряются (не записываются в БД).

**Конкретно в чём проблема**
`Task.Delay` стоит **до** первого сохранения, а не после.

**Путь к файлу:** `Backend/BarkFluff.Onliner/BackgroundServices/DatabasePersistenceService.cs` : строки 36–47

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    await Task.Delay(SaveInterval, stoppingToken); // ❌ Сначала ждём 10 минут

    try
    {
        await SaveStatusesToDatabaseAsync(stoppingToken); // ❌ Потом сохраняем
    }
    ...
}
```

**Варианты решения**

```csharp
// ✅ Сначала выполняем, потом ждём — первый save сразу при старте
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await SaveStatusesToDatabaseAsync(stoppingToken); // ✅ Сразу при первой итерации
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error during database persistence cycle");
    }

    await Task.Delay(SaveInterval, stoppingToken); // ✅ Потом ждём
}
```

---

## 🔵 Прочее / Технический долг

---

### TD-01 — SubscribeToOnlineStatusQueryHandler не реализует IRequestHandler

**Проблема / Описание**
Все остальные handlers используют MediatR (`IRequestHandler<TRequest, TResponse>`). `SubscribeToOnlineStatusQueryHandler` вынужденно выведен за рамки MediatR из-за streaming — он регистрируется как `AddScoped` и вызывается напрямую. Это нарушает единообразие архитектуры и затрудняет понимание кода новыми разработчиками.

**Конкретно в чём проблема**
Прямой вызов `_subscribeHandler.Handle(query)` вместо `_mediator.Send(...)`.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Host/OnlinerApiService.cs` : строки 62–70  
**Путь к файлу:** `Backend/BarkFluff.Onliner/Program.cs` : строка 44

```csharp
// DependencyInjection.cs
// SubscribeToOnlineStatusQueryHandler регистрируется отдельно
builder.Services.AddScoped<SubscribeToOnlineStatusQueryHandler>();

// OnlinerApiService.cs
return _subscribeHandler.Handle(query); // ❌ Не через MediatR
```

**Варианты решения**

```csharp
// ✅ Вариант: оставить как есть, но добавить комментарий почему streaming не через MediatR
/// <remarks>
/// Streaming RPC handlers cannot use MediatR because MediatR does not support
/// IServerStreamWriter lifetimes. This handler is called directly.
/// </remarks>
public override Task SubscribeToOnlineStatus(...) { ... }
```

---

### TD-02 — DateTime.MinValue как sentinel для "статус неизвестен"

**Проблема / Описание**
Когда статус пользователя неизвестен, в ответ возвращается `LastSeen = DateTime.MinValue`. Клиент должен знать об этом соглашении и обрабатывать `MinValue` особым образом. Это неявный контракт, который не отражён в proto-схеме.

**Конкретно в чём проблема**
Нет явного nullable поля или sentinel-значения на уровне proto.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Features/GetOnlineStatus/GetOnlineStatusQueryHandler.cs` : строки 87–94

```csharp
return new UserOnlineStatus
{
    UserId = userId,
    Status = ProtoStatusTypeId.Unknown,
    LastSeen = Timestamp.FromDateTime(DateTime.MinValue.ToUniversalTime()) // ❌ Неявный sentinel
};
```

**Варианты решения**

```csharp
// ✅ Вариант A: использовать optional поле в proto (proto3 supports optional)
// В onliner_api.proto:
// optional google.protobuf.Timestamp last_seen = 3; // null = неизвестно

// ✅ Вариант B: использовать Timestamp.FromDateTime(DateTime.UnixEpoch) как sentinel
// и задокументировать это явно в proto-комментарии
LastSeen = Timestamp.FromDateTime(DateTime.UnixEpoch) // ✅ Хотя бы валидная дата, не MinValue
```

---

### TD-03 — Нет graceful shutdown для DatabasePersistenceService

**Проблема / Описание**
При `ApplicationStopped` сервис получает `CancellationToken`. `Task.Delay` прерывается, но **текущий цикл сохранения не завершается** до конца — если сохранение шло в момент остановки, оно прервётся на середине транзакции. `SaveChangesAsync` получит `cancellationToken` и может откатить частичные изменения.

**Конкретно в чём проблема**
Нет `StopAsync` override с принудительным финальным сохранением перед остановкой.

**Путь к файлу:** `Backend/BarkFluff.Onliner/BackgroundServices/DatabasePersistenceService.cs` : строки 30–48

```csharp
// ❌ Нет override StopAsync — при SIGTERM финальное сохранение не гарантировано
public class DatabasePersistenceService : BackgroundService { ... }
```

**Варианты решения**

```csharp
public override async Task StopAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Database Persistence Service stopping — performing final save...");

    // ✅ Финальное сохранение без stoppingToken (используем внешний cancellationToken из StopAsync)
    try
    {
        await SaveStatusesToDatabaseAsync(cancellationToken);
        _logger.LogInformation("Final save completed successfully");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Final save failed during shutdown");
    }

    await base.StopAsync(cancellationToken);
}
```

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

---

### TD-05 — Dockerfile.slim и Dockerfile: нет non-root пользователя

**Проблема / Описание**
Контейнер запускается от `root` по умолчанию. Это нарушает принцип минимальных привилегий — уязвимость в приложении даёт атакующему root-права в контейнере.

**Конкретно в чём проблема**
В Dockerfile отсутствует `USER` директива.

**Путь к файлу:** `Backend/BarkFluff.Onliner/Dockerfile`  
**Путь к файлу:** `Backend/BarkFluff.Onliner/Dockerfile.slim`

```dockerfile
# ❌ Нет USER директивы — контейнер работает от root
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 7009
# ... ENTRYPOINT без USER
```

**Варианты решения**

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 7009

# ✅ Переключаемся на non-root пользователя
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

ENTRYPOINT ["dotnet", "BarkFluff.Onliner.dll"]
```

---

## Сводная таблица

| ID | Категория | Название | Приоритет |
|----|-----------|----------|-----------|
| SEC-01 | 🔴 Безопасность | Fail-open в OnlineVisibilityFilter | **КРИТИЧНО** |
| SEC-02 | 🔴 Безопасность | Нет лимита userId в запросах (DoS) | **ВЫСОКИЙ** |
| SEC-03 | 🔴 Безопасность | FRIENDS visibility трактуется как NONE | СРЕДНИЙ |
| PERF-01 | 🟠 Производительность | N+1 gRPC-запросов в VisibilityFilter | **ВЫСОКИЙ** |
| PERF-02 | 🟠 Производительность | N+1 SELECT в DatabasePersistenceService | **ВЫСОКИЙ** |
| PERF-03 | 🟠 Производительность | O(N×M) в GetStreamsTrackingUser | СРЕДНИЙ |
| PERF-04 | 🟠 Производительность | Метрика active_subscriptions только растёт | НИЗКИЙ |
| PERF-05 | 🟠 Производительность | GetAllStatuses копирует все объекты | НИЗКИЙ |
| BUG-01 | 🟡 Баг | Race condition: мутация shared объекта в AddOrUpdate | **ВЫСОКИЙ** |
| BUG-02 | 🟡 Баг | Race condition: двойное уведомление (statusChanged closure) | СРЕДНИЙ |
| BUG-03 | 🟡 Баг | Нет prewarm данных из БД при старте | СРЕДНИЙ |
| BUG-04 | 🟡 Баг | ChangeUsersInSubscription бросает исключение при гонке | НИЗКИЙ |
| BUG-05 | 🟡 Баг | Первый DB save только через 10 минут | СРЕДНИЙ |
| TD-01 | 🔵 Техдолг | SubscribeHandler вне MediatR без комментария | НИЗКИЙ |
| TD-02 | 🔵 Техдолг | DateTime.MinValue как неявный sentinel | НИЗКИЙ |
| TD-03 | 🔵 Техдолг | Нет graceful shutdown для DatabasePersistenceService | СРЕДНИЙ |
| TD-04 | 🔵 Техдолг | Метрики без дифференциации типов событий | НИЗКИЙ |
| TD-05 | 🔵 Техдолг | Docker: контейнер от root | СРЕДНИЙ |
