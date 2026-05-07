# Аудит проекта: BarkFluff.FastAuth

> **Дата:** 2026-03-04  
> **Статус сервиса:** 🟡 В разработке — продакшен-развёртывание возможно с устранением критических проблем  
> **Аудитор:** GitHub Copilot — автоматический анализ кода

---

## 🔴 Безопасность

---

### SEC-01 — SubscribeFastAuthResult доступен без авторизации

**Описание:**  
gRPC-метод `SubscribeFastAuthResult` помечен `[AllowAnonymous]`. Это значит, что любой, кто знает (или угадал) `fast_auth_id`, может подписаться на стрим и получить токены доступа в момент их выдачи.

**В чём проблема:**  
`fast_auth_id` — это `Guid.NewGuid().ToString()`, т.е. 36 символов UUID. Он передаётся клиенту открыто в ответе `GenerateFastAuthToken`. Если злоумышленник перехватит `fast_auth_id` (например, через незащищённое соединение или утечку), он получит полноценные `AccessToken` + `RefreshToken` жертвы.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Host\FastAuthApiService.cs` : строки 32–44

```csharp
[AllowAnonymous]                              // ⚠️ ПРОБЛЕМА: нет никакой авторизации
public override Task SubscribeFastAuthResult(
    SubscribeFastAuthResultRequest request,
    IServerStreamWriter<FastAuthResult> responseStream,
    ServerCallContext context)
{
    return subscribeHandler.Handle(new SubscribeFastAuthResultQuery
    {
        FastAuthId = request.FastAuthId,      // ⚠️ любой, знающий ID, получит токены
        ResponseStream = responseStream,
        CancellationToken = context.CancellationToken
    });
}
```

**Варианты решения:**

**Вариант A** — Привязать стрим к короткоживущему `subscribe_token`, выдаваемому вместе с `fast_auth_id`:  
При генерации сессии создавать отдельный одноразовый `SubscribeToken` (криптостойкий, 32 байта), который передаётся клиенту. Только он может открыть стрим.

**Вариант B** — Авторизовать подписчика через `[Authorize(Policy = nameof(TokenType.FastAuth))]` (если такой тип токена предусмотрен протоколом).

```csharp
// Вариант A — добавить SubscribeToken в сессию
public class FastAuthSession
{
    // ... существующие поля ...

    /// <summary>Одноразовый токен для открытия стрима. Выдаётся один раз вместе с fast_auth_id.</summary>
    public string SubscribeToken { get; } = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
}

// В FastAuthApiService.cs
[AllowAnonymous]
public override Task SubscribeFastAuthResult(
    SubscribeFastAuthResultRequest request,
    IServerStreamWriter<FastAuthResult> responseStream,
    ServerCallContext context)
{
    // Валидируем subscribe_token перед передачей в handler
    return subscribeHandler.Handle(new SubscribeFastAuthResultQuery
    {
        FastAuthId = request.FastAuthId,
        SubscribeToken = request.SubscribeToken, // ✅ новое поле в proto
        ResponseStream = responseStream,
        CancellationToken = context.CancellationToken
    });
}

// В SubscribeFastAuthResultQueryHandler.cs
var session = sessions.TryGet(request.FastAuthId)
    ?? throw new FastAuthSessionNotFoundException();

// ✅ Проверяем что токен совпадает — защита от перебора
if (session.SubscribeToken != request.SubscribeToken)
    throw new FastAuthInvalidStateException();
```

---

### SEC-02 — Отсутствует Rate Limiting на генерацию сессий

**Описание:**  
`GenerateFastAuthToken` помечен `[AllowAnonymous]` и не имеет никаких ограничений. Каждый вызов создаёт новый объект `FastAuthSession` в `ConcurrentDictionary` и запускает Unbounded Channel.

**В чём проблема:**  
Злоумышленник может отправить тысячи запросов в секунду, переполняя память процесса (DoS через исчерпание памяти). Ограничения на количество сессий от одного IP нет вообще.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Host\FastAuthApiService.cs` : строки 22–30  
`Backend\BarkFluff.FastAuth\Infrastructure\FastAuthSessionsManager.cs` : строки 16–34

```csharp
[AllowAnonymous]
public override Task<GenerateFastAuthTokenResponse> GenerateFastAuthToken(
    GenerateFastAuthTokenRequest request, ServerCallContext context)
{
    // ⚠️ Нет rate limiting — любой может создать бесконечно много сессий
    return mediator.Send(new GenerateFastAuthTokenCommand
    {
        Format = request.Format
    });
}
```

**Вариант решения** — ASP.NET Core Rate Limiting (встроен с .NET 7+):

```csharp
// В Program.cs — добавить rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("fastauth_generate", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;              // макс 10 запросов
        limiterOptions.Window = TimeSpan.FromMinutes(1); // за 1 минуту
        limiterOptions.QueueLimit = 0;                // очередь не нужна
    });
});

// Применить в pipeline
app.UseRateLimiter();

// В FastAuthApiService.cs — добавить атрибут или middleware для gRPC
// Через gRPC interceptor:
public class RateLimitingInterceptor : Interceptor
{
    private readonly IRateLimiter _limiter;

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var ipAddress = context.GetHttpContext().Connection.RemoteIpAddress?.ToString();
        using var lease = await _limiter.AcquireAsync(permitCount: 1);

        if (!lease.IsAcquired)
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Too many requests"));

        return await continuation(request, context);
    }
}
```

---

### SEC-03 — Токены (AccessToken, RefreshToken) пишутся в Channel без шифрования в памяти

**Описание:**  
`FastAuthResult` содержит `AccessToken` и `RefreshToken` в виде plain-text строк и хранится в `UnboundedChannel<FastAuthResult>` до тех пор, пока подписчик не прочитает событие. Если подписчик отключился — событие остаётся в канале навсегда, пока сессия не будет удалена (до 30 секунд после финализации).

**В чём проблема:**  
Heap dump / memory snapshot откроет токены в открытом виде. Cannel не очищает данные из памяти после чтения.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Domain\FastAuthSession.cs` : строки 10–11, 84

```csharp
// ⚠️ Unbounded channel — данные живут в heap до GC
private readonly Channel<FastAuthResult> _events = Channel.CreateUnbounded<FastAuthResult>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

// ...
_events.Writer.TryWrite(acceptedResult); // ⚠️ AccessToken/RefreshToken в plain-text в памяти
_events.Writer.TryComplete();
```

**Вариант решения** — Закрывать канал и очищать токены сразу после доставки:

```csharp
// В SubscribeFastAuthResultQueryHandler.cs
await foreach (var evt in session.Events.ReadAllAsync(request.CancellationToken))
{
    await request.ResponseStream.WriteAsync(evt, request.CancellationToken);

    // ✅ Если событие финальное — прерываем итерацию (канал уже завершён TryComplete())
    // Channel.ReadAllAsync сам завершится, но явный break улучшает читаемость
    if (evt.Status is FastAuthStatus.Accepted 
        or FastAuthStatus.Rejected 
        or FastAuthStatus.Expired)
    {
        break; // ✅ не ждём следующей итерации после финального события
    }
}
```

---

### SEC-04 — Валидация входных строковых полей отсутствует

**Описание:**  
`DeviceName`, `OperationSystem`, `AppName`, `AppVersion`, `IpAddress` принимаются из заголовков gRPC без какой-либо валидации длины или содержимого и сохраняются в сессию, а затем возвращаются в `ScanFastAuthResponse`.

**В чём проблема:**  

- Поле `DeviceName` длиной в 1 МБ будет храниться в памяти до удаления сессии
- При отображении в UI возможен XSS если эти поля рендерятся без экранирования
- `IpAddress` не валидируется — можно подменить произвольной строкой

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Features\GenerateFastAuthToken\GenerateFastAuthTokenCommandHandler.cs` : строки 23–43

```csharp
// Есть проверка на null/empty, но нет проверки на длину и содержимое
if (string.IsNullOrEmpty(requestContext.DeviceName))
    throw new XDeviceNameIsRequiredException();

// ⚠️ DeviceName может быть строкой длиной 10 МБ — валидации нет
var session = sessions.Create(
    deviceName: requestContext.DeviceName!,   // ⚠️ неограниченная длина
    operationSystem: requestContext.OperationSystem!,
    appName: requestContext.AppName!,
    appVersion: requestContext.AppVersion!,
    ipAddress: requestContext.IpAddress ?? string.Empty); // ⚠️ не валидируется как IP
```

**Вариант решения:**

```csharp
// Добавить константы ограничений
private const int MaxDeviceNameLength = 128;
private const int MaxOsNameLength = 64;
private const int MaxAppNameLength = 128;
private const int MaxAppVersionLength = 32;

// Валидация в CommandHandler
var deviceName = requestContext.DeviceName!;
if (deviceName.Length > MaxDeviceNameLength)
    throw new ValidationException($"DeviceName exceeds {MaxDeviceNameLength} characters");

var osName = requestContext.OperationSystem!;
if (osName.Length > MaxOsNameLength)
    throw new ValidationException($"OperationSystem exceeds {MaxOsNameLength} characters");

// ✅ Также валидировать IpAddress
if (!string.IsNullOrEmpty(requestContext.IpAddress) 
    && !System.Net.IPAddress.TryParse(requestContext.IpAddress, out _))
{
    // Логируем подозрительный IP, но не бросаем — берём пустой
    logger.LogWarning("Invalid IP address format: {Ip}", requestContext.IpAddress);
    ipAddress = string.Empty;
}
```

---

### SEC-05 — Двойная проверка состояния сессии (TOCTOU) в AcceptFastAuth и RejectFastAuth

**Описание:**  
В `AcceptFastAuthCommandHandler` и `RejectFastAuthCommandHandler` статус сессии и `ConfirmationCode` проверяются **дважды**: сначала напрямую через `session.Status` (строки 25–38), и затем внутри `session.TryAccept()` под локом. Первая проверка — без лока, что создаёт классическое состояние гонки (TOCTOU — Time-Of-Check-Time-Of-Use).

**В чём проблема:**  
Между первой (незащищённой) проверкой и фактическим вызовом `TryAccept()` другой поток может изменить статус. Это не приведёт к некорректному результату (т.к. `TryAccept` защищён локом), но создаёт **ложные исключения** и **непоследовательную логику обработки ошибок**: один и тот же невалидный запрос может получить разные исключения в зависимости от timing.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Features\AcceptFastAuth\AcceptFastAuthCommandHandler.cs` : строки 25–66

```csharp
// ⚠️ TOCTOU: проверки ниже — без синхронизации
if (session.Status == Proto.FastAuth.FastAuthStatus.Expired)
    throw new FastAuthSessionExpiredException();

if (session.Status != Proto.FastAuth.FastAuthStatus.Scanned)
    throw new FastAuthInvalidStateException();

// Проверка UserId и ConfirmationCode — тоже без лока
if (session.UserId != userContext.UserId
    || string.IsNullOrEmpty(session.ConfirmationCode)
    || session.ConfirmationCode != request.ConfirmationCode)
{
    throw new FastAuthInvalidConfirmationCodeException();
}

// ✅ Только здесь — под локом. Но ошибка может уже быть выброшена выше
if (!session.TryAccept(request.ConfirmationCode, userContext.UserId, acceptedResult))
    throw new FastAuthInvalidStateException();
```

**Вариант решения** — Перенести всю логику валидации в `TryAccept()` / `TryReject()` с расширенными кодами результата:

```csharp
// В FastAuthSession.cs — расширить результат
public enum AcceptOutcome { Ok, NotScanned, Expired, WrongUser, WrongCode }

public (AcceptOutcome Outcome, bool SessionCreated) TryAccept(
    string confirmationCode, long userId, FastAuthResult acceptedResult)
{
    lock (_gate)
    {
        if (Status == FastAuthStatus.Expired) return (AcceptOutcome.Expired, false);
        if (Status != FastAuthStatus.Scanned) return (AcceptOutcome.NotScanned, false);
        if (UserId != userId) return (AcceptOutcome.WrongUser, false);
        if (ConfirmationCode != confirmationCode) return (AcceptOutcome.WrongCode, false);

        Status = FastAuthStatus.Accepted;
        FinalizedAt = DateTime.UtcNow;
        _events.Writer.TryWrite(acceptedResult);
        _events.Writer.TryComplete();
        return (AcceptOutcome.Ok, true);
    }
}

// В AcceptFastAuthCommandHandler.cs — одна точка проверки
var (outcome, _) = session.TryAccept(request.ConfirmationCode, userContext.UserId, acceptedResult);
switch (outcome)
{
    case AcceptOutcome.Expired:     throw new FastAuthSessionExpiredException();
    case AcceptOutcome.WrongUser:
    case AcceptOutcome.WrongCode:   throw new FastAuthInvalidConfirmationCodeException();
    case AcceptOutcome.NotScanned:  throw new FastAuthInvalidStateException();
}
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — Утечка токенов при отключении подписчика до Accept

**Описание:**  
Если клиент открыл стрим `SubscribeFastAuthResult`, затем закрыл соединение (ушёл в background, потерял сеть), а потом кто-то вызвал `AcceptFastAuth` — `FastAuthResult` с токенами будет записан в `Channel`, но **никем не прочитан**. Канал завершён (`TryComplete()`), подписчика нет, событие зависнет до удаления сессии через `FinalRetention` (30 секунд).

**В чём проблема:**  
Клиент не получит токены. Авторизация провалится «молча» — без ошибки с точки зрения сервера. Пользователю придётся начинать процесс заново, но сессия уже в статусе `Accepted` и больше не позволит повторный `Scan`.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Domain\FastAuthSession.cs` : строки 74–88  
`Backend\BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs` : строки 25–35

```csharp
// В FastAuthSession.TryAccept() — токены записываются в канал
_events.Writer.TryWrite(acceptedResult); // ⚠️ но подписчика уже нет
_events.Writer.TryComplete();

// В SubscribeFastAuthResultQueryHandler — OperationCanceledException просто логируется
catch (OperationCanceledException)
{
    // ⚠️ Сессия остаётся в статусе Pending/Scanned, 
    // но _hasSubscriber = true — повторно подписаться нельзя!
    logger.LogInformation("FastAuth subscription on session {Id} cancelled by client", session.Id);
}
```

**Вариант решения** — Сбрасывать `_hasSubscriber` при отключении клиента, позволяя переподключиться:

```csharp
// В FastAuthSession.cs — добавить метод DetachSubscriber
public void DetachSubscriber()
{
    lock (_gate)
    {
        // ✅ Разрешаем переподключение только если сессия ещё не финализирована
        if (!IsFinal)
            _hasSubscriber = false;
    }
}

// В SubscribeFastAuthResultQueryHandler.cs
catch (OperationCanceledException)
{
    logger.LogInformation("FastAuth subscription on session {Id} cancelled by client", session.Id);
    session.DetachSubscriber(); // ✅ Клиент сможет переподключиться
}
```

---

### BUG-02 — Метрики active_subscriptions не декрементируются при ошибке до подписки

**Описание:**  
В `SubscribeFastAuthResultQueryHandler` метрика `active_subscriptions` инкрементируется после `TryAttachSubscriber()`. При нормальном завершении `active_subscriptions_closed` инкрементируется в `finally`. Но если до `metrics.Increment("active_subscriptions")` выбросится исключение — метрика закрытия тоже не вызовется, счётчик останется корректным. Однако если исключение произойдёт **после** инкремента `active_subscriptions`, но до входа в `try` — `finally` не выполнится.

**В чём проблема:**  
Маловероятно, но если `logger.LogInformation` выбросит исключение (OOM и т.д.) между инкрементом и `try` — счётчик `active_subscriptions` «протечёт», метрики разойдутся.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs` : строки 22–39

```csharp
metrics.Increment("active_subscriptions");
logger.LogInformation(...); // ⚠️ если здесь OOM — try/finally не выполнится

try
{
    await foreach (var evt in session.Events.ReadAllAsync(request.CancellationToken))
    { ... }
}
finally
{
    metrics.Increment("active_subscriptions_closed"); // ⚠️ может не выполниться
}
```

**Вариант решения** — Включить инкремент метрики внутрь `try`:

```csharp
// ✅ Все операции под try/finally
try
{
    metrics.Increment("active_subscriptions");
    logger.LogInformation("FastAuth subscription attached to session {Id}", session.Id);

    await foreach (var evt in session.Events.ReadAllAsync(request.CancellationToken))
    {
        await request.ResponseStream.WriteAsync(evt, request.CancellationToken);
    }
}
catch (OperationCanceledException)
{
    logger.LogInformation("FastAuth subscription on session {Id} cancelled by client", session.Id);
}
finally
{
    metrics.Decrement("active_subscriptions"); // ✅ или использовать Gauge вместо отдельных счётчиков
}
```

---

### BUG-03 — QrCodeGenerator создаёт новый QRCodeGenerator на каждый вызов

**Описание:**  
В `QrCodeGenerator.GeneratePngBase64()` на каждый вызов создаётся новый экземпляр `QRCodeGenerator` (библиотека QRCoder). Это `IDisposable`, утилизируется через `using`, но объект содержит внутренние словари и таблицы — это тяжёлый объект для такого паттерна использования.

**В чём проблема:**  
При высокой нагрузке (много параллельных генераций) создаётся давление на GC. Сам `QrCodeGenerator`-сервис является Singleton, но внутренний объект пересоздаётся постоянно.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Infrastructure\QrCodeGenerator.cs` : строки 10–17

```csharp
public string GeneratePngBase64(string payload)
{
    using var qrGenerator = new QRCodeGenerator(); // ⚠️ создаётся на каждый вызов
    using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
    var pngQrCode = new PngByteQRCode(qrCodeData);
    var bytes = pngQrCode.GetGraphic(20);
    return Convert.ToBase64String(bytes);
}
```

**Вариант решения** — Кэшировать экземпляр `QRCodeGenerator` как поле класса:

```csharp
public class QrCodeGenerator
{
    // ✅ Создаём один раз — класс Singleton в DI
    private readonly QRCodeGenerator _generator = new();

    public string GeneratePngBase64(string payload)
    {
        // ✅ Не пересоздаём генератор на каждый вызов
        using var qrCodeData = _generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(qrCodeData);
        var bytes = pngQrCode.GetGraphic(20);
        return Convert.ToBase64String(bytes);
    }
}
```

> ⚠️ Проверить thread-safety `QRCodeGenerator` в документации библиотеки. Если не потокобезопасен — использовать `ThreadLocal<QRCodeGenerator>` или `ObjectPool<QRCodeGenerator>`.

---

### BUG-04 — Отсутствует верхний лимит на количество сессий в памяти

**Описание:**  
`FastAuthSessionsManager` использует `ConcurrentDictionary` без каких-либо ограничений. `FastAuthExpirationService` очищает сессии каждые 30 секунд — это значит, что за 30 секунд злоумышленник может создать неограниченное количество сессий.

**В чём проблема:**  
При нагрузке в 1000 RPS * 30 секунд = 30 000 сессий в словаре. Каждая сессия содержит `Channel`, строки и объекты. Это потенциальный OOM.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Infrastructure\FastAuthSessionsManager.cs` : строки 14–34

```csharp
private readonly ConcurrentDictionary<string, FastAuthSession> _sessions = new();
// ⚠️ Нет ограничения на количество сессий

public FastAuthSession Create(...)
{
    // ⚠️ Создаём бесконечно
    _sessions[session.Id] = session;
    return session;
}
```

**Вариант решения:**

```csharp
private const int MaxSessions = 10_000; // ✅ Разумный лимит

public FastAuthSession? Create(...)
{
    // ✅ Проверяем лимит перед созданием
    if (_sessions.Count >= MaxSessions)
        return null; // Сигнал для handler — бросить RpcException(ResourceExhausted)

    // ... создание сессии
    _sessions[session.Id] = session;
    return session;
}
```

---

### BUG-05 — FinalRetention не учитывает сессии без подписчика

**Описание:**  
`FinalRetention = 30 секунд` — сессия удаляется через 30 секунд после финализации. Это время рассчитано на то, чтобы подписчик успел получить финальное событие из Channel. Но если подписчика **никогда не было** (клиент не вызвал `SubscribeFastAuthResult`), финальное событие в Channel висит без читателя.

**В чём проблема:**  
Если сессия истекла без подписчика — через 30 секунд она удаляется из словаря, но `_hasSubscriber = false`. Следовательно, любой, кто позвонит `SubscribeFastAuthResult` с этим ID после удаления, получит `FastAuthSessionNotFoundException` — корректно. Но между `TryExpire()` и удалением (30 секунд) кто-то **ещё может подписаться**, получит `Expired`-событие, и потратит ресурсы на стрим который немедленно завершится.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Infrastructure\FastAuthExpirationService.cs` : строки 51–57

```csharp
// ⚠️ FinalRetention одинаков для всех случаев — 
// с подписчиком и без подписчика
if (session.IsFinal && session.FinalizedAt.HasValue
    && now - session.FinalizedAt.Value > FastAuthSessionsManager.FinalRetention)
{
    manager.Remove(session.Id);
}
```

**Вариант решения:**

```csharp
// ✅ Для сессий без подписчика — удалять сразу после финализации
var retention = session.HasHadSubscriber 
    ? FastAuthSessionsManager.FinalRetention 
    : TimeSpan.Zero; // Немедленно убираем — некому доставлять

if (session.IsFinal && session.FinalizedAt.HasValue
    && now - session.FinalizedAt.Value > retention)
{
    manager.Remove(session.Id);
}
```

---

## 🔵 Оптимизация

---

### OPT-01 — Sweep() в ExpirationService итерирует все сессии O(n)

**Описание:**  
`FastAuthExpirationService.Sweep()` вызывает `manager.Snapshot()`, который создаёт `ToList()` всего `ConcurrentDictionary` — полная копия в памяти каждые 30 секунд.

**В чём проблема:**  
При 10 000 активных сессий это аллокация списка из 10 000 объектов каждые 30 секунд. Scan + итерация — O(n). Для большинства сценариев приемлемо, но при масштабировании становится узким местом.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Infrastructure\FastAuthSessionsManager.cs` : строка 43  
`Backend\BarkFluff.FastAuth\Infrastructure\FastAuthExpirationService.cs` : строка 40

```csharp
// FastAuthSessionsManager.cs
public IReadOnlyCollection<FastAuthSession> Snapshot() 
    => _sessions.Values.ToList(); // ⚠️ полная копия коллекции

// FastAuthExpirationService.cs
foreach (var session in manager.Snapshot()) // ⚠️ O(n) с аллокацией
```

**Вариант решения** — Итерировать напрямую без копирования:

```csharp
// FastAuthSessionsManager.cs
// ✅ Возвращаем IEnumerable напрямую из словаря — без ToList()
public IEnumerable<FastAuthSession> GetAll() => _sessions.Values;

// FastAuthExpirationService.cs
foreach (var session in manager.GetAll()) // ✅ Без копирования
```

> `ConcurrentDictionary.Values` возвращает snapshot внутренних значений, но без аллокации промежуточного `List<T>`. Это достаточно безопасно для read-only итерации.

---

### OPT-02 — GenerateFastAuthTokenCommandHandler.Handle() не является async, но возвращает Task

**Описание:**  
Метод `Handle()` в `GenerateFastAuthTokenCommandHandler` объявлен как `Task<>` но использует `Task.FromResult()` — т.е. синхронный. При этом `QrCodeGenerator.GeneratePngBase64()` может быть CPU-интенсивным при генерации QR-кода.

**В чём проблема:**  
Генерация QR с `ECCLevel.Q` и размером `20` блокирует поток ThreadPool на всё время работы. При параллельных запросах это ведёт к thread pool starvation.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Features\GenerateFastAuthToken\GenerateFastAuthTokenCommandHandler.cs` : строки 21–69

```csharp
// ⚠️ Метод синхронный, но CPU-интенсивный
public Task<GenerateFastAuthTokenResponse> Handle(
    GenerateFastAuthTokenCommand request, CancellationToken cancellationToken)
{
    // ...
    var tokenValue = format switch
    {
        TokenFormat.Qr => qrGenerator.GeneratePngBase64(session.Id), // ⚠️ CPU-bound блокировка
        _ => session.Id
    };

    return Task.FromResult(new GenerateFastAuthTokenResponse { ... }); // ⚠️ синхронный возврат
}
```

**Вариант решения** — Вынести CPU-bound работу в `Task.Run()`:

```csharp
public async Task<GenerateFastAuthTokenResponse> Handle(
    GenerateFastAuthTokenCommand request, CancellationToken cancellationToken)
{
    // ... валидация и создание сессии ...

    var tokenValue = format switch
    {
        // ✅ CPU-bound работа вынесена из потока обработки запроса
        TokenFormat.Qr => await Task.Run(
            () => qrGenerator.GeneratePngBase64(session.Id), cancellationToken),
        _ => session.Id
    };

    return new GenerateFastAuthTokenResponse { ... };
}
```

---

### OPT-03 — string-based Guid для Id сессий

**Описание:**  
`FastAuthSession.Id` хранится как `string` (результат `Guid.NewGuid().ToString()`), и словарь `ConcurrentDictionary<string, FastAuthSession>` использует строковые ключи с string-сравнением.

**В чём проблема:**  
`Guid` как `string` занимает 72 байта на heap (object header + length + 36 chars × 2 bytes). При использовании `Guid` как ключа словаря — сравнение строк медленнее чем сравнение 16-байтных `Guid`-структур.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Infrastructure\FastAuthSessionsManager.cs` : строки 14, 22

```csharp
private readonly ConcurrentDictionary<string, FastAuthSession> _sessions = new(); // ⚠️ string-ключ

public FastAuthSession Create(...)
{
    var session = new FastAuthSession
    {
        Id = Guid.NewGuid().ToString(), // ⚠️ Guid → string — лишняя аллокация
```

**Вариант решения:**

```csharp
// ✅ Использовать Guid как ключ
private readonly ConcurrentDictionary<Guid, FastAuthSession> _sessions = new();

public FastAuthSession Create(...)
{
    var session = new FastAuthSession
    {
        Id = Guid.NewGuid(), // ✅ структура, без аллокации на heap
        // ...
    };
}

// ✅ При необходимости отдавать клиенту как строку — форматировать только на выходе
public string IdString => Id.ToString("N"); // без дефисов — короче
```

---

## 🟡 Прочее / Качество кода

---

### MISC-01 — SubscribeFastAuthResultQueryHandler не реализует IRequestHandler

**Описание:**  
Остальные handlers реализуют `IRequestHandler<TRequest, TResponse>` из MediatR и регистрируются автоматически. `SubscribeFastAuthResultQueryHandler` — нет, он регистрируется вручную через `AddScoped<>` и инжектируется напрямую в `FastAuthApiService`.

**В чём проблема:**  

- Нарушение единообразия архитектуры (одни handlers через MediatR, другой — напрямую)
- Нет pipeline behaviors для этого handler (логирование, трейсинг, exception handling)
- `AddScoped` при Singleton-зависимостях (`FastAuthSessionsManager`) — `SubscribeFastAuthResultQueryHandler` создаётся на каждый gRPC-запрос, хотя мог бы быть Singleton

**Путь к файлу:** `Backend\BarkFluff.FastAuth\DependencyInjection.cs` : строка 14  
`Backend\BarkFluff.FastAuth\Host\FastAuthApiService.cs` : строки 17–19

```csharp
// DependencyInjection.cs
services.AddScoped<SubscribeFastAuthResultQueryHandler>(); // ⚠️ отдельная регистрация

// FastAuthApiService.cs
public class FastAuthApiService(
    IMediator mediator,
    SubscribeFastAuthResultQueryHandler subscribeHandler) // ⚠️ прямая инжекция, не через MediatR
```

**Вариант решения** — Выделить стриминговые handlers в отдельный интерфейс или регистрировать как Singleton:

```csharp
// DependencyInjection.cs
// ✅ Singleton безопасен — зависит только от других Singleton
services.AddSingleton<SubscribeFastAuthResultQueryHandler>();

// Или, если нужна единообразность с MediatR — использовать IStreamRequestHandler
// и отдельный запрос типа IStreamRequest<FastAuthResult>
```

---

### MISC-02 — Опечатка в имени исключения XAppInfoIsRequiedException

**Описание:**  
В `GenerateFastAuthTokenCommandHandler` используется исключение `XAppInfoIsRequiedException` — пропущена буква `r` в слове `Required`.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Features\GenerateFastAuthToken\GenerateFastAuthTokenCommandHandler.cs` : строка 35

```csharp
throw new XAppInfoIsRequiedException(); // ⚠️ Опечатка: "Requied" вместо "Required"
```

**Вариант решения:**

```csharp
throw new XAppInfoIsRequiredException(); // ✅ Правильное написание
```

---

### MISC-03 — IpAddress не передаётся в логах AcceptFastAuth

**Описание:**  
При успешном `Accept` в лог пишется `UserId` и `DeviceId`, но не пишется `IpAddress` сессии. При расследовании инцидентов IP-адрес инициатора важен для корреляции.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Features\AcceptFastAuth\AcceptFastAuthCommandHandler.cs` : строки 70–72

```csharp
logger.LogInformation(
    "FastAuth session {Id} accepted by user {UserId}, new device {DeviceId} provisioned",
    session.Id, userContext.UserId, newDeviceId);
// ⚠️ IpAddress сессии не логируется
```

**Вариант решения:**

```csharp
logger.LogInformation(
    "FastAuth session {Id} accepted by user {UserId}, new device {DeviceId} provisioned from IP {IpAddress}",
    session.Id, userContext.UserId, newDeviceId, session.IpAddress); // ✅
```

---

### MISC-04 — OperationSystem вместо OperatingSystem

**Описание:**  
Поле `OperationSystem` во всех классах проекта написано с грамматической ошибкой — должно быть `OperatingSystem` (или `OsName`). Это публичный API (proto-контракт), исправление потребует версионирования.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Domain\FastAuthSession.cs` : строка 19  
`Backend\BarkFluff.FastAuth\Infrastructure\FastAuthSessionsManager.cs` : строка 17

```csharp
public required string OperationSystem { get; init; } // ⚠️ Грамматическая ошибка в имени поля
```

**Вариант решения:**

```csharp
// ✅ Исправить в следующей версии proto-контракта
public required string OperatingSystem { get; init; }
// или
public required string OsName { get; init; }
```

---

### MISC-05 — FastAuthServerApiService.GetFastAuthInfo не задокументирован как TODO

**Описание:**  
Метод `GetFastAuthInfo` выбрасывает `RpcException(Unimplemented)`. Это оставлено как «точка расширения», но нет задачи/тикета, нет документации что именно должен возвращать этот метод.

**Путь к файлу:** `Backend\BarkFluff.FastAuth\Host\FastAuthServerApiService.cs` : строки 13–18

```csharp
public override Task<GetFastAuthInfoResponse> GetFastAuthInfo(
    GetFastAuthInfoRequest request, ServerCallContext context)
{
    // ⚠️ Нет TODO-тикета, нет спецификации что должен возвращать метод
    throw new RpcException(new Status(StatusCode.Unimplemented, "GetFastAuthInfo is not implemented yet"));
}
```

**Вариант решения:**

```csharp
// ✅ Явный TODO с указанием что нужно реализовать
// TODO(#ISSUE-XXX): Реализовать GetFastAuthInfo для AdminPanel —
// должен возвращать: список активных сессий, статистику, deviceName, IP, статус
throw new RpcException(new Status(StatusCode.Unimplemented, "GetFastAuthInfo is not implemented yet"));
```

---

## Сводная таблица проблем

| ID      | Категория       | Проблема                                                 | Критичность    |
| ------- | --------------- | -------------------------------------------------------- | -------------- |
| SEC-01  | 🔴 Безопасность | SubscribeFastAuthResult без авторизации — утечка токенов | 🔴 Критическая |
| SEC-02  | 🔴 Безопасность | Нет Rate Limiting на GenerateFastAuthToken — DoS         | 🔴 Критическая |
| SEC-03  | 🔴 Безопасность | Токены в plain-text в памяти (Channel)                   | 🟠 Высокая     |
| SEC-04  | 🔴 Безопасность | Нет валидации длины/содержимого входных строк            | 🟠 Высокая     |
| SEC-05  | 🔴 Безопасность | TOCTOU в AcceptFastAuth / RejectFastAuth                 | 🟡 Средняя     |
| BUG-01  | 🟠 Баг          | Утечка токенов при отключении подписчика до Accept       | 🔴 Критическая |
| BUG-02  | 🟠 Баг          | Метрика active_subscriptions может «протечь»             | 🟡 Низкая      |
| BUG-03  | 🟠 Баг          | QRCodeGenerator пересоздаётся на каждый запрос           | 🟡 Средняя     |
| BUG-04  | 🟠 Баг          | Нет лимита сессий в памяти — OOM при нагрузке            | 🔴 Критическая |
| BUG-05  | 🟠 Баг          | FinalRetention не оптимален для сессий без подписчика    | 🟡 Низкая      |
| OPT-01  | 🔵 Оптимизация  | Snapshot() создаёт полную копию словаря O(n)             | 🟡 Средняя     |
| OPT-02  | 🔵 Оптимизация  | QR-генерация CPU-bound блокирует thread pool             | 🟡 Средняя     |
| OPT-03  | 🔵 Оптимизация  | Guid хранится как string — лишние аллокации              | 🟢 Низкая      |
| MISC-01 | 🟡 Качество     | SubscribeHandler вне MediatR pipeline                    | 🟡 Средняя     |
| MISC-02 | 🟡 Качество     | Опечатка: XAppInfoIsRequiedException                     | 🟢 Низкая      |
| MISC-03 | 🟡 Качество     | IpAddress не пишется в лог при Accept                    | 🟢 Низкая      |
| MISC-04 | 🟡 Качество     | Опечатка в имени поля: OperationSystem                   | 🟢 Низкая      |
| MISC-05 | 🟡 Качество     | GetFastAuthInfo без TODO и спецификации                  | 🟢 Низкая      |

---

*Аудит выполнен на основе статического анализа кода. Для полноты рекомендуется дополнить динамическим тестированием (нагрузочные тесты, penetration testing).*
