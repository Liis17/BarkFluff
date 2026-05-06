# Аудит проекта: BarkFluff.ClientStorage

> **Дата аудита:** 2025  
> **Ветка:** `dev`  
> **Сервис:** `Backend/BarkFluff.ClientStorage`  
> **Назначение:** HTTP-сервис хранения и раздачи клиентских дистрибутивов (Windows, Kotlin/Android, macOS, iOS). Использует S3-совместимое хранилище (Minio) + локальный дисковый кеш + SQLite для метаданных.

---

## 🔴 Безопасность

---

### SEC-01 — Timing-атака на сравнение токена

**Проблема / Описание**  
Токен авторизации загрузки (`UPLOAD_TOKEN`) сравнивается оператором `!=` — обычным строковым сравнением. Это создаёт уязвимость timing-атаки: злоумышленник может статистически угадать токен, измеряя время ответа (короткое замыкание при первом несовпадающем символе).

**Конкретно в чём проблема**  
Стандартное `string !=` завершается сразу при первом отличном символе, поэтому для токена `"AAAA..."` ответ приходит быстрее, чем для `"ZAAA..."`. Это даёт атакующему возможность перебора посимвольно.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Middleware/TokenAuthMiddleware.cs` : строки 22–24

```csharp
// ❌ ПРОБЛЕМА: обычное строковое сравнение — уязвимо к timing-атаке
if (string.IsNullOrEmpty(authHeader)
    || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
    || authHeader["Bearer ".Length..] != _uploadToken)  // ← здесь
```

**Варианты решения**  
Использовать `CryptographicOperations.FixedTimeEquals` для сравнения за постоянное время.

```csharp
// ✅ РЕШЕНИЕ: константное время сравнения
using System.Security.Cryptography;
using System.Text;

public async Task InvokeAsync(HttpContext context)
{
    if (context.Request.Path.StartsWithSegments("/set"))
    {
        var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader)
            || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }

        var providedToken = authHeader["Bearer ".Length..];

        // Сравнение за константное время — не допускает timing-атаку
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(_uploadToken);

        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized");
            return;
        }
    }

    await _next(context);
}
```

---

### 

### SEC-04 — S3-креды могут быть пустой строкой без валидации

**Проблема / Описание**  
`S3_ACCESS_KEY` и `S3_SECRET_KEY` берутся из конфигурации с фолбэком `?? ""`. Если переменные окружения не установлены — сервис стартует с пустыми кредами и пытается подключиться к S3, что может привести к неожиданному успешному подключению (если Minio настроен без auth) или скрытой ошибке.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Infrastructure/S3StorageService.cs` : строки 40–43

```csharp
// ❌ ПРОБЛЕМА: пустые строки как фолбэк — молчаливый failover
var credentials = new BasicAWSCredentials(
    configuration["S3_ACCESS_KEY"] ?? "",   // ← пустой ключ не вызывает ошибку
    configuration["S3_SECRET_KEY"] ?? "");  // ← пустой секрет не вызывает ошибку
```

**Варианты решения**  
Бросать `InvalidOperationException` при отсутствии обязательных переменных, аналогично `UPLOAD_TOKEN`.

```csharp
// ✅ РЕШЕНИЕ: явная валидация обязательных конфигурационных значений
var accessKey = configuration["S3_ACCESS_KEY"]
    ?? throw new InvalidOperationException("S3_ACCESS_KEY environment variable is required");
var secretKey = configuration["S3_SECRET_KEY"]
    ?? throw new InvalidOperationException("S3_SECRET_KEY environment variable is required");

var credentials = new BasicAWSCredentials(accessKey, secretKey);
```

--- 

---

## 🔴 Баги

---

### 

### BUG-02 — CacheWarmupService не останавливается при shutdown

**Проблема / Описание**  
`StopAsync` возвращает `Task.CompletedTask` немедленно — прогрев кеша не прерывается при остановке приложения. Фоновая задача, запущенная через `Task.Run`, продолжает работать после получения сигнала shutdown, что может привести к `ObjectDisposedException` или записи в закрытый DbContext.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Services/CacheWarmupService.cs` : строки 20–24, 75

```csharp
// ❌ ПРОБЛЕМА: StopAsync не ждёт завершения фоновой задачи
public Task StartAsync(CancellationToken cancellationToken)
{
    _ = Task.Run(() => WarmUpAsync(cancellationToken), cancellationToken);  // задача не сохранена
    return Task.CompletedTask;
}

public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;  // ← ничего не делает
```

**Варианты решения**  
Сохранить `Task` в поле и дождаться его завершения в `StopAsync`.

```csharp
// ✅ РЕШЕНИЕ: сохраняем и ждём фоновую задачу при остановке
public class CacheWarmupService : IHostedService
{
    private Task? _warmupTask;
    private CancellationTokenSource? _cts;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _warmupTask = Task.Run(() => WarmUpAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts != null) await _cts.CancelAsync();
        if (_warmupTask != null)
        {
            try { await _warmupTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { /* нормально при shutdown */ }
        }
    }
}
```

---

### BUG-03 — FileStream в DownloadClient открывается без явного using

**Проблема / Описание**  
`FileStream fs = new FileStream(...)` открывается и передаётся в `FileStreamResult`. Если между созданием `FileStreamResult` и завершением запроса произойдёт исключение в middleware или в сериализации — стрим не будет закрыт, файловый дескриптор утечёт. Особенно опасно в сочетании с `LocalFileCache.UpdateAsync`, которая открывает тот же файл.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 277–283

```csharp
// ❌ ПРОБЛЕМА: FileStream без using — потенциальная утечка дескриптора
var fs = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
return new FileStreamResult(fs, clientFile.ContentType)  // если здесь бросит — fs не закроется
{
    FileDownloadName      = clientFile.OriginalFileName,
    EnableRangeProcessing = true
};
```

**Варианты решения**  
`FileStreamResult` сам вызывает `Dispose` на стриме после отправки ответа — это нормальное поведение ASP.NET. Однако для защиты от исключения до создания `FileStreamResult` стоит обернуть в try/catch.

```csharp
// ✅ РЕШЕНИЕ: защита от утечки при исключении до передачи стрима
FileStream? fs = null;
try
{
    fs = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    return new FileStreamResult(fs, GetSafeContentType(clientFile.ClientType))
    {
        FileDownloadName      = clientFile.OriginalFileName,
        EnableRangeProcessing = true
    };
    // FileStreamResult берёт на себя Dispose(fs) после отправки
}
catch
{
    fs?.Dispose();
    throw;
}
```

---

### BUG-04 — Кеш не проверяется на корректность (partial write при прошлом крэше)

**Проблема / Описание**  
`GetCachedFilePath` возвращает путь, если `File.Exists(path) == true`. Но если предыдущий запуск сервиса упал в момент `File.Move(tmp, path)` — на диске может остаться частично записанный файл. Клиент получит усечённый дистрибутив без какой-либо ошибки.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Infrastructure/LocalFileCache.cs` : строки 23–27

```csharp
// ❌ ПРОБЛЕМА: проверяем только существование, не целостность
public string? GetCachedFilePath(ClientType clientType, ReleaseChannel channel)
{
    var path = CachePath(clientType, channel);
    return File.Exists(path) ? path : null;  // файл может быть повреждён
}
```

**Варианты решения**  
Хранить рядом с кешем файл с ожидаемым размером или checksum, и валидировать при чтении. Минимально — проверять что `FileInfo.Length > 0`.

```csharp
// ✅ РЕШЕНИЕ вариант 1 (минимальный): проверка ненулевого размера
public string? GetCachedFilePath(ClientType clientType, ReleaseChannel channel)
{
    var path = CachePath(clientType, channel);
    if (!File.Exists(path)) return null;

    var info = new FileInfo(path);
    if (info.Length == 0)
    {
        _logger.LogWarning("Кеш-файл пустой, игнорируем: {Path}", path);
        return null;
    }

    return path;
}

// ✅ РЕШЕНИЕ вариант 2 (надёжный): сохранять .meta файл с ожидаемым размером
// При UpdateAsync после записи: File.WriteAllText(path + ".meta", fs.Length.ToString())
// При GetCachedFilePath: читать .meta, сравнивать с FileInfo.Length
```

--- 

### BUG-06 — TryParseRange не обрабатывает open-ended диапазон bytes=N-

**Проблема / Описание**  
HTTP стандарт [RFC 9110](https://www.rfc-editor.org/rfc/rfc9110#section-14.1) допускает open-ended диапазоны вида `bytes=1234-` (без конечной позиции, то есть «от N до конца файла»). `TryParseRange` в `S3StorageService` требует оба значения — `from` и `to` — и вернёт `false` для `bytes=1234-`. В итоге BITS/curl с `Range: bytes=N-` получит весь файл с начала вместо продолжения с нужной позиции.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Infrastructure/S3StorageService.cs` : строки 101–112

```csharp
// ❌ ПРОБЛЕМА: "bytes=1234-" (без конца) → to = 0 → to >= from → false → range игнорируется
private static bool TryParseRange(string header, out long from, out long to)
{
    from = 0; to = 0;
    if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
    var span = header.AsSpan(6);
    var dash = span.IndexOf('-');
    if (dash < 0) return false;
    return long.TryParse(span[..dash], out from)
        && long.TryParse(span[(dash + 1)..], out to)  // ← пустая строка → false
        && to >= from;
}
```

**Варианты решения**  
Обработать случай пустой правой части диапазона — передать `-1` как «до конца» (AWS SDK поддерживает `ByteRange` с `-1` как "до конца объекта").

```csharp
// ✅ РЕШЕНИЕ: поддержка open-ended диапазона bytes=N-
private static bool TryParseRange(string header, out long from, out long to)
{
    from = 0; to = -1; // -1 означает «до конца объекта»

    if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;

    var span = header.AsSpan(6);
    var dash = span.IndexOf('-');
    if (dash < 0) return false;

    if (!long.TryParse(span[..dash], out from)) return false;

    var toSpan = span[(dash + 1)..];
    if (toSpan.IsEmpty)
    {
        // bytes=N- : до конца файла
        to = -1;
        return true;
    }

    if (!long.TryParse(toSpan, out to)) return false;
    return to >= from;
}

// В DownloadAsync — учитываем to == -1:
if (TryParseRange(rangeHeader, out var from, out var to))
{
    request.ByteRange = to >= 0
        ? new ByteRange(from, to)
        : new ByteRange(from, long.MaxValue); // S3 SDK: до конца объекта
}
```
