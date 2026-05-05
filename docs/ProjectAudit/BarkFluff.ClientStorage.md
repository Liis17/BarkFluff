# Аудит проекта: BarkFluff.ClientStorage

> **Дата аудита:** 2025  
> **Ветка:** `dev`  
> **Сервис:** `Backend/BarkFluff.ClientStorage`  
> **Назначение:** HTTP-сервис хранения и раздачи клиентских дистрибутивов (Windows, Kotlin/Android, macOS, iOS). Использует S3-совместимое хранилище (Minio) + локальный дисковый кеш + SQLite для метаданных.

---

## Содержание

- [🔴 Безопасность](#-безопасность)
  - [SEC-01 — Timing-атака на сравнение токена](#sec-01--timing-атака-на-сравнение-токена)
  - [SEC-02 — Доверие всем Forwarded-заголовкам без ограничений](#sec-02--доверие-всем-forwarded-заголовкам-без-ограничений)
  - [SEC-03 — Имя файла от клиента сохраняется без санитизации](#sec-03--имя-файла-от-клиента-сохраняется-без-санитизации)
  - [SEC-04 — S3-креды могут быть пустой строкой без валидации](#sec-04--s3-креды-могут-быть-пустой-строкой-без-валидации)
  - [SEC-05 — Content-Type от клиента сохраняется и возвращается без валидации](#sec-05--content-type-от-клиента-сохраняется-и-возвращается-без-валидации)
  - [SEC-06 — SQLite connection string захардкожена в Program.cs](#sec-06--sqlite-connection-string-захардкожена-в-programcs)
- [🟠 Производительность и оптимизация](#-производительность-и-оптимизация)
  - [PERF-01 — Файл читается дважды при загрузке (хеш + S3)](#perf-01--файл-читается-дважды-при-загрузке-хеш--s3)
  - [PERF-02 — Блокирующий вызов async в Program.cs при старте](#perf-02--блокирующий-вызов-async-в-programcs-при-старте)
  - [PERF-03 — Дублирующийся DB-запрос в каждом handler'е](#perf-03--дублирующийся-db-запрос-в-каждом-handlerе)
  - [PERF-04 — Нет ограничения скорости (rate limiting) на /get/* эндпоинтах](#perf-04--нет-ограничения-скорости-rate-limiting-на-get-эндпоинтах)
  - [PERF-05 — Нет CancellationToken в контроллере](#perf-05--нет-cancellationtoken-в-контроллере)
- [🔴 Баги](#-баги)
  - [BUG-01 — Fire-and-forget UpdateCacheInBackgroundAsync без контроля конкурентности](#bug-01--fire-and-forget-updatecacheinbackgroundasync-без-контроля-конкурентности)
  - [BUG-02 — CacheWarmupService не останавливается при shutdown](#bug-02--cachewarmupservice-не-останавливается-при-shutdown)
  - [BUG-03 — FileStream в DownloadClient открывается без явного using](#bug-03--filestream-в-downloadclient-открывается-без-явного-using)
  - [BUG-04 — Кеш не проверяется на корректность (partial write при прошлом крэше)](#bug-04--кеш-не-проверяется-на-корректность-partial-write-при-прошлом-крэше)
  - [BUG-05 — Старые записи ClientFile в БД никогда не удаляются](#bug-05--старые-записи-clientfile-в-бд-никогда-не-удаляются)
  - [BUG-06 — TryParseRange не обрабатывает open-ended диапазон bytes=N-](#bug-06--tryparserange-не-обрабатывает-open-ended-диапазон-bytesn-)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)
  - [MISC-01 — Нет health check эндпоинта](#misc-01--нет-health-check-эндпоинта)
  - [MISC-02 — Нет Swagger / OpenAPI](#misc-02--нет-swagger--openapi)
  - [MISC-03 — Version из заголовка X-App-Version не валидируется](#misc-03--version-из-заголовка-x-app-version-не-валидируется)

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

### SEC-02 — Доверие всем Forwarded-заголовкам без ограничений

**Проблема / Описание**  
В `Program.cs` производится полная очистка `KnownNetworks` и `KnownProxies`. Это означает, что сервис принимает `X-Forwarded-For`, `X-Forwarded-Host` и `X-Forwarded-Proto` **от любого клиента**. Злоумышленник может подделать IP-адрес или схему запроса, напрямую обратившись к сервису.

**Конкретно в чём проблема**  
`KnownNetworks.Clear()` + `KnownProxies.Clear()` без явного добавления доверенного прокси означает: принять заголовки от кого угодно. Если `BuildPublicUrl` или логирование использует `Request.Host` — он может быть подменён.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Program.cs` : строки 27–32

```csharp
// ❌ ПРОБЛЕМА: очищаем trusted networks, но ничего не добавляем взамен
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();   // ← принимаем от всех
    options.KnownProxies.Clear();    // ← принимаем от всех
});
```

**Варианты решения**  
Добавить IP Nginx/прокси в `KnownProxies`, либо ограничить подсетью Docker-сети.

```csharp
// ✅ РЕШЕНИЕ: указываем конкретный IP прокси или доверенную подсеть
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;

    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();

    // Добавить IP Nginx из env или конфига
    var proxyIp = builder.Configuration["TRUSTED_PROXY_IP"];
    if (!string.IsNullOrEmpty(proxyIp) && System.Net.IPAddress.TryParse(proxyIp, out var ip))
        options.KnownProxies.Add(ip);
    else
        // Fallback: доверять только Docker bridge подсети 172.16.0.0/12
        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(
            System.Net.IPAddress.Parse("172.16.0.0"), 12));
});
```

---

### SEC-03 — Имя файла от клиента сохраняется без санитизации

**Проблема / Описание**  
`file.FileName` из `IFormFile` берётся напрямую и сохраняется в БД как `OriginalFileName`, а затем возвращается клиентам в заголовке `Content-Disposition`. Имя может содержать путь (`../../etc/passwd`), управляющие символы или слишком длинную строку.

**Конкретно в чём проблема**  
Браузер или Windows-клиент при скачивании файла может получить имя с path-traversal символами. Некоторые загрузчики (BITS, wget) интерпретируют `Content-Disposition: filename` буквально.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 344–345

```csharp
// ❌ ПРОБЛЕМА: имя файла от клиента без очистки
var clientFile = new ClientFile
{
    ...
    OriginalFileName = file.FileName,  // ← может быть "../../evil" или "a\x00b"
    ...
};
```

**Варианты решения**  
Санитизировать имя файла через `Path.GetFileName` (убирает путь) и ограничить длину.

```csharp
// ✅ РЕШЕНИЕ: санитизация имени файла
private static string SanitizeFileName(string fileName)
{
    // Убираем path-traversal компоненты
    var name = Path.GetFileName(fileName);

    // Убираем недопустимые символы для имени файла
    var invalidChars = Path.GetInvalidFileNameChars();
    name = string.Concat(name.Where(c => !invalidChars.Contains(c)));

    // Ограничиваем длину
    if (name.Length > 255)
        name = name[..255];

    return string.IsNullOrWhiteSpace(name) ? "client.bin" : name;
}

// В UploadClient:
OriginalFileName = SanitizeFileName(file.FileName),
```

---

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

### SEC-05 — Content-Type от клиента сохраняется и возвращается без валидации

**Проблема / Описание**  
`file.ContentType` принимается как есть и сохраняется в БД. При скачивании браузер получает этот MIME-тип. Злоумышленник может загрузить файл с `Content-Type: text/html` или `application/javascript` — браузер может исполнить его как скрипт (если у сервиса нет CSP).

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 347, 291

```csharp
// ❌ ПРОБЛЕМА: ContentType от клиента сохраняется без проверки
ContentType = file.ContentType ?? "application/octet-stream",

// ...и затем используется напрямую при отдаче файла:
return new FileStreamResult(fs, clientFile.ContentType)  // ← может быть text/html
```

**Варианты решения**  
Принудительно устанавливать безопасный `Content-Type` в зависимости от `ClientType`, игнорируя значение от клиента.

```csharp
// ✅ РЕШЕНИЕ: принудительный Content-Type по типу клиента
private static string GetSafeContentType(ClientType clientType) => clientType switch
{
    ClientType.Windows => "application/octet-stream",  // .exe / .msix
    ClientType.Kotlin  => "application/vnd.android.package-archive",  // .apk
    ClientType.MacOS   => "application/octet-stream",  // .dmg
    ClientType.iOS     => "application/octet-stream",  // .ipa
    _                  => "application/octet-stream"
};

// В UploadClient — при сохранении в БД:
ContentType = GetSafeContentType(clientType),

// В DownloadClient — при отдаче:
return new FileStreamResult(fs, GetSafeContentType(clientFile.ClientType))
{
    ...
};
```

---

### SEC-06 — SQLite connection string захардкожена в Program.cs

**Проблема / Описание**  
Путь к базе данных `/app/data/clientstorage.db` прошит прямо в `Program.cs`, а не читается из конфигурации. Это затрудняет тестирование, изменение пути в разных средах и противоречит подходу, используемому для `CACHE_DIR`.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Program.cs` : строка 25

```csharp
// ❌ ПРОБЛЕМА: захардкоженный путь к БД
builder.Services.AddDbContext<ClientStorageContext>(options =>
    options.UseSqlite("Data Source=/app/data/clientstorage.db"));
```

**Варианты решения**  
Вынести путь в переменную окружения `DB_PATH` с разумным дефолтом.

```csharp
// ✅ РЕШЕНИЕ: путь к БД из конфигурации
var dbPath = builder.Configuration["DB_PATH"] ?? "/app/data/clientstorage.db";
builder.Services.AddDbContext<ClientStorageContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
```

---

## 🟠 Производительность и оптимизация

---

### PERF-01 — Файл читается дважды при загрузке (хеш + S3)

**Проблема / Описание**  
В `UploadClient` поток файла сначала полностью читается для вычисления SHA-256 хеша, затем `sourceStream.Position = 0` (ресет), и файл снова читается полностью при загрузке в S3. Для файлов размером 500 МБ это означает двойное чтение из памяти/диска — в сумме 1 ГБ I/O.

**Конкретно в чём проблема**  
`IFormFile` при больших файлах буферируется на диск во временный файл. Двойное чтение = двойная нагрузка на дисковый I/O и время ответа увеличивается вдвое.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 318–328

```csharp
// ❌ ПРОБЛЕМА: двойное чтение потока
await using var sourceStream = file.OpenReadStream();

// Первый проход — читаем всё для хеша
using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
var buffer = new byte[81920];
int bytesRead;
while ((bytesRead = await sourceStream.ReadAsync(buffer)) > 0)
    incrementalHash.AppendData(buffer, 0, bytesRead);
checksum = Convert.ToHexStringLower(incrementalHash.GetHashAndReset());

sourceStream.Position = 0;  // ← ресет
await _s3.UploadAsync(s3Key, sourceStream, ...);  // Второй проход — снова всё читаем
```

**Варианты решения**  
Использовать кастомный `HashingStream` — обёртку, которая считает хеш на лету во время одного прохода записи в S3.

```csharp
// ✅ РЕШЕНИЕ: HashingStream — хеш считается одновременно с загрузкой в S3

/// <summary>Поток-обёртка, вычисляющий SHA-256 на лету при чтении.</summary>
public sealed class HashingStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public HashingStream(Stream inner) => _inner = inner;

    public string GetChecksum() => Convert.ToHexStringLower(_hash.GetHashAndReset());

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        var read = await _inner.ReadAsync(buffer, ct);
        if (read > 0) _hash.AppendData(buffer.Span[..read]);
        return read;
    }

    // остальные члены Stream делегируются к _inner...
    public override bool CanRead  => _inner.CanRead;
    public override bool CanSeek  => false;
    public override bool CanWrite => false;
    public override long Length   => _inner.Length;
    public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
    public override void Flush()  => _inner.Flush();
    public override int  Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin)       => throw new NotSupportedException();
    public override void SetLength(long value)                      => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)=> throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
}

// В UploadClient — один проход:
await using var sourceStream = file.OpenReadStream();
await using var hashingStream = new HashingStream(sourceStream);

await _s3.UploadAsync(s3Key, hashingStream, file.ContentType ?? "application/octet-stream");
checksum = hashingStream.GetChecksum();  // хеш уже посчитан
```

---

### PERF-02 — Блокирующий вызов async в Program.cs при старте

**Проблема / Описание**  
`s3.InitializeBucketAsync().GetAwaiter().GetResult()` — синхронная блокировка async-метода в startup. Это может вызвать deadlock в некоторых контекстах синхронизации и нарушает рекомендации Microsoft по async-friendly startup.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Program.cs` : строка 50

```csharp
// ❌ ПРОБЛЕМА: блокирующий вызов async при инициализации
s3.InitializeBucketAsync().GetAwaiter().GetResult();  // может вызвать дедлок
```

**Варианты решения**  
Вынести инициализацию S3 в `IHostedService` или использовать `app.Lifetime.ApplicationStarted`.

```csharp
// ✅ РЕШЕНИЕ: перенести в IHostedService
public class S3BucketInitializerService : IHostedService
{
    private readonly S3StorageService _s3;
    public S3BucketInitializerService(S3StorageService s3) => _s3 = s3;

    public Task StartAsync(CancellationToken ct) => _s3.InitializeBucketAsync();
    public Task StopAsync(CancellationToken ct)  => Task.CompletedTask;
}

// В Program.cs:
builder.Services.AddHostedService<S3BucketInitializerService>();
// Убрать блок using (var scope) { s3.InitializeBucketAsync()... }
```

---

### PERF-03 — Дублирующийся DB-запрос в каждом handler'е

**Проблема / Описание**  
`GetVersion`, `GetBitsUrl` и `DownloadClient` выполняют **идентичный** LINQ-запрос к SQLite для получения `ClientFile`. Нет никакого кеширования метаданных — каждый вызов `GET /version` и `GET /bitsurl` дважды ходит в БД, если вызываются подряд.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 217–220, 240–243, 263–266

```csharp
// ❌ ПРОБЛЕМА: идентичный запрос повторяется в трёх методах без кеширования
var clientFile = await _db.ClientFiles
    .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
    .OrderByDescending(f => f.UploadedAt)
    .FirstOrDefaultAsync();
```

**Варианты решения**  
Вынести запрос в приватный метод и добавить `IMemoryCache` для кеширования метаданных с инвалидацией при загрузке.

```csharp
// ✅ РЕШЕНИЕ: кеширование метаданных через IMemoryCache

// В Program.cs: builder.Services.AddMemoryCache();

// В контроллере:
private readonly IMemoryCache _metaCache;

private async Task<ClientFile?> GetLatestFileAsync(ClientType clientType, ReleaseChannel channel)
{
    var cacheKey = $"meta:{clientType}:{channel}";
    if (_metaCache.TryGetValue(cacheKey, out ClientFile? cached))
        return cached;

    var file = await _db.ClientFiles
        .Where(f => f.ClientType == clientType && f.ReleaseChannel == channel)
        .OrderByDescending(f => f.UploadedAt)
        .FirstOrDefaultAsync();

    if (file != null)
        _metaCache.Set(cacheKey, file, TimeSpan.FromMinutes(5));

    return file;
}

// При загрузке нового файла — инвалидировать:
_metaCache.Remove($"meta:{clientType}:{releaseChannel}");
```

---

### PERF-04 — Нет ограничения скорости (rate limiting) на /get/* эндпоинтах

**Проблема / Описание**  
Эндпоинты `/get/*` доступны без авторизации и без rate limiting. Любой клиент может бесконечно скачивать файлы размером до 512 МБ, что может привести к исчерпанию пропускной способности или переполнению S3 (когда кеш не прогрет).

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Program.cs` и `Controllers/ClientStorageController.cs`

```csharp
// ❌ ПРОБЛЕМА: нет rate limiting, любой может бесконечно скачивать
[HttpGet("/get/barkfluffwindows")]
public Task<IActionResult> GetWindows()
    => DownloadClient(ClientType.Windows, ReleaseChannel.Release);
```

**Варианты решения**  
Добавить `RateLimiter` из `System.Threading.RateLimiting` (встроен в .NET 7+).

```csharp
// ✅ РЕШЕНИЕ: фиксированный rate limit по IP для /get/* эндпоинтов
// В Program.cs:
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("download", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit           = 10,           // 10 запросов
                Window                = TimeSpan.FromMinutes(1),
                QueueProcessingOrder  = QueueProcessingOrder.OldestFirst,
                QueueLimit            = 0
            }));
});

app.UseRateLimiter();

// На контроллере или роутах:
[HttpGet("/get/barkfluffwindows")]
[EnableRateLimiting("download")]
public Task<IActionResult> GetWindows() => ...
```

---

### PERF-05 — Нет CancellationToken в контроллере

**Проблема / Описание**  
Все async-методы контроллера (`DownloadClient`, `GetVersion`, `UploadClient`, `GetBitsUrl`) не принимают и не пробрасывают `CancellationToken`. Если клиент разрывает соединение, ASP.NET не может отменить DB-запрос или загрузку из S3 — ресурсы продолжают тратиться.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 238, 261, 308, 215

```csharp
// ❌ ПРОБЛЕМА: нет CancellationToken — запрос в БД не отменяется при дисконнекте клиента
private async Task<IActionResult> GetVersion(ClientType clientType, ReleaseChannel releaseChannel)
{
    var clientFile = await _db.ClientFiles
        ...
        .FirstOrDefaultAsync();  // ← нет ct
```

**Варианты решения**  
Добавить `CancellationToken` через `[FromCancellationToken]` или `HttpContext.RequestAborted`.

```csharp
// ✅ РЕШЕНИЕ: пробрасываем токен отмены во все async-операции
private async Task<IActionResult> GetVersion(
    ClientType clientType,
    ReleaseChannel releaseChannel,
    CancellationToken ct = default)  // ← ASP.NET заполнит автоматически
{
    var clientFile = await _db.ClientFiles
        .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
        .OrderByDescending(f => f.UploadedAt)
        .FirstOrDefaultAsync(ct);  // ← теперь отменяется при дисконнекте

    ...
}
```

---

## 🔴 Баги

---

### BUG-01 — Fire-and-forget UpdateCacheInBackgroundAsync без контроля конкурентности

**Проблема / Описание**  
После загрузки файла вызывается `_ = UpdateCacheInBackgroundAsync(...)` без `await`. Если за короткое время загружают два файла для одного и того же `ClientType + Channel` — оба фоновых таска начнут писать в один и тот же кеш-файл через `LocalFileCache.UpdateAsync`, которая не имеет никакой блокировки. Результат: повреждённый кеш-файл.

**Конкретно в чём проблема**  
`LocalFileCache.UpdateAsync` пишет через `.tmp` → `File.Move`, но два параллельных таска оба создадут `.tmp` и оба вызовут `File.Move` — последний перезапишет, но первый уже начал читать `.tmp` из S3, пока второй его перезаписывает.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строка 358  
`Backend/BarkFluff.ClientStorage/Infrastructure/LocalFileCache.cs` : строки 29–45

```csharp
// ❌ ПРОБЛЕМА: fire-and-forget без синхронизации
_ = UpdateCacheInBackgroundAsync(s3Key, clientType, releaseChannel);
// Если вызвать дважды подряд — оба пишут в один файл
```

**Варианты решения**  
Добавить в `LocalFileCache` `SemaphoreSlim` на каждый ключ `(ClientType, ReleaseChannel)`.

```csharp
// ✅ РЕШЕНИЕ: per-key семафор в LocalFileCache

public class LocalFileCache
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    private SemaphoreSlim GetLock(ClientType clientType, ReleaseChannel channel)
        => _locks.GetOrAdd($"{clientType}_{channel}", _ => new SemaphoreSlim(1, 1));

    public async Task UpdateAsync(ClientType clientType, ReleaseChannel channel, Stream source)
    {
        var sem = GetLock(clientType, channel);
        await sem.WaitAsync();
        try
        {
            var path = CachePath(clientType, channel);
            var tmp  = path + ".tmp";
            try
            {
                await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(fs);
                File.Move(tmp, path, overwrite: true);
                _logger.LogInformation("Кеш обновлён: {ClientType} {Channel}", clientType, channel);
            }
            catch
            {
                try { File.Delete(tmp); } catch { /* ignore */ }
                throw;
            }
        }
        finally
        {
            sem.Release();
        }
    }
}
```

---

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

### BUG-05 — Старые записи ClientFile в БД никогда не удаляются

**Проблема / Описание**  
При каждом `UploadClient` создаётся новая запись `ClientFile` в БД. Предыдущие записи никогда не удаляются. При этом S3-объекты с их ключами также остаются в хранилище. Со временем: безграничный рост БД + накопление мёртвых объектов в S3.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строки 354–355

```csharp
// ❌ ПРОБЛЕМА: только добавляем, никогда не удаляем старые записи
_db.ClientFiles.Add(clientFile);
await _db.SaveChangesAsync();
// Старая запись + её S3 объект остаются навсегда
```

**Варианты решения**  
Перед сохранением новой записи — получить старую, удалить её S3-объект и удалить из БД.

```csharp
// ✅ РЕШЕНИЕ: ротация — удаляем старый файл из S3 и БД при загрузке нового
private async Task<IActionResult> UploadClient(
    IFormFile? file, ClientType clientType, ReleaseChannel releaseChannel)
{
    // ... загрузка в S3, вычисление checksum ...

    // Получаем текущий файл для последующего удаления
    var existingFile = await _db.ClientFiles
        .Where(f => f.ClientType == clientType && f.ReleaseChannel == releaseChannel)
        .OrderByDescending(f => f.UploadedAt)
        .FirstOrDefaultAsync();

    var clientFile = new ClientFile { ... };
    _db.ClientFiles.Add(clientFile);
    await _db.SaveChangesAsync();

    // Удаляем старый файл из S3 (после успешного сохранения нового)
    if (existingFile != null)
    {
        try
        {
            await _s3.DeleteAsync(existingFile.S3Key);
            _db.ClientFiles.Remove(existingFile);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Не критично — фоновая задача или ручная очистка разберётся
            _logger.LogWarning(ex, "Не удалось удалить старый файл {S3Key}", existingFile.S3Key);
        }
    }

    // ... UpdateCacheInBackgroundAsync ...
}
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

---

## 🔵 Прочее / Качество кода

---

### MISC-01 — Нет health check эндпоинта

**Проблема / Описание**  
Сервис не реализует `/health` или `/ready` эндпоинт. Docker/Kubernetes не могут определить готовность сервиса, оркестраторы не знают когда перезапускать контейнер.

**Варианты решения**

```csharp
// ✅ В Program.cs:
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ClientStorageContext>("sqlite")
    .AddCheck("s3", () =>
    {
        // упрощённая проверка: просто наличие клиента
        return HealthCheckResult.Healthy();
    });

app.MapHealthChecks("/health");
```

---

### MISC-02 — Нет Swagger / OpenAPI

**Проблема / Описание**  
API не документировано через OpenAPI/Swagger. Разработчики клиентов (Windows WPF, Android) не имеют машиночитаемого описания эндпоинтов. Также используется `.http` файл для тестирования, который придётся поддерживать вручную.

**Варианты решения**

```csharp
// ✅ В Program.cs (.NET 9 минимальный вариант):
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

---

### MISC-03 — Version из заголовка X-App-Version не валидируется

**Проблема / Описание**  
Заголовок `X-App-Version` принимается как произвольная строка и сохраняется в `ClientFile.Version` без валидации формата. Можно загрузить файл с версией `"'; DROP TABLE ClientFiles;--"` или строкой в 10 000 символов.

**Путь к файлу:** `Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs` : строка 339

```csharp
// ❌ ПРОБЛЕМА: версия без валидации
var version = Request.Headers["X-App-Version"].FirstOrDefault();
// Может быть null, пустой строкой, SQL-инъекцией или 10 КБ мусора
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: валидация формата версии
private static readonly System.Text.RegularExpressions.Regex VersionRegex =
    new(@"^\d{1,5}\.\d{1,5}\.\d{1,5}(\.\d{1,5})?$", RegexOptions.Compiled);

var rawVersion = Request.Headers["X-App-Version"].FirstOrDefault();
var version = rawVersion != null && VersionRegex.IsMatch(rawVersion)
    ? rawVersion
    : null; // null допустим — поле nullable
```

---

*Аудит выполнен автоматизированным анализом кодовой базы. Все проблемы требуют ревью перед применением решений.*
