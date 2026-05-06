# Аудит: Barkfluff.WebServer

> **Дата аудита:** 2025-07  
> **Ветка:** `dev`  
> **Порт:** 64641  
> **Стек:** .NET 9, ASP.NET Core MVC, gRPC, Telegram.Bot  

---

## Оглавление

- [🔴 Безопасность](#-безопасность)
- [🟡 Оптимизация производительности](#-оптимизация-производительности)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Прочее / Code Quality](#-прочее--code-quality)

---

## 🔴 Безопасность

---

### SEC-01 — Хардкод Admin Telegram ID

**Проблема / Описание:**  
`TelegramService` содержит захардкоженный `long _adminId = 495716470`. Это означает, что смена администратора требует перекомпиляции. Если по ошибке номер утечёт или аккаунт будет скомпрометирован — оперативно поменять нельзя без нового деплоя.

**В чём конкретно проблема:**  
Значение `_adminId` зашито прямо в исходник. Нет возможности сменить администратора через конфигурацию без пересборки образа.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/TelegramService.cs` : строки 13–14

```csharp
// ❌ Хардкод — нельзя поменять без пересборки
private readonly long _adminId = 495716470;
private readonly bool _isConfigured;
```

**Варианты решения:**  
Вынести `_adminId` в конфигурацию (`appsettings.json` / переменная окружения).

```csharp
// ✅ Читаем из конфигурации
public TelegramService(string? token, long adminId, SupportChatService chatService, ILogger<TelegramService> logger)
{
    _adminId = adminId; // передаётся из Program.cs через builder.Configuration["Telegram:AdminId"]
    _chatService = chatService;
    _logger = logger;
    _isConfigured = !string.IsNullOrEmpty(token);
    if (_isConfigured) _bot = new TelegramBotClient(token!);
}
```

В `Program.cs`:
```csharp
var adminId = long.Parse(builder.Configuration["Telegram:AdminId"] ?? "0");
return new TelegramService(token, adminId, chatService, logger);
```

---

### SEC-02 — Отсутствует Rate Limiting на API чата поддержки

**Проблема / Описание:**  
`POST /api/support/send` не имеет никакого ограничения частоты запросов. Любой анонимный клиент может слать бесконечное количество сообщений, перегружая Telegram-бот и заполняя `SupportChatService` в памяти.

**В чём конкретно проблема:**  
- Нет `[RateLimiter]` / middleware rate limiting.  
- `SupportChatService._chats` — `ConcurrentDictionary` без верхнего предела элементов.  
- Злоумышленник может создать миллионы ChatId (GUID формат проверяется, но новый GUID генерируется клиентом) и забить RAM.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/SupportChatController.cs` : строки 20–42

```csharp
[HttpPost("send")]
public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
{
    // ❌ Нет rate limiting — можно слать хоть 10000 запросов в секунду
    if (string.IsNullOrWhiteSpace(request.ChatId) || string.IsNullOrWhiteSpace(request.Message))
        return BadRequest(new { error = "ChatId and Message are required" });
    ...
}
```

**Варианты решения:**  
Подключить встроенный `RateLimiter` (.NET 7+):

```csharp
// Program.cs
builder.Services.AddRateLimiter(options =>
{
    // Фиксированное окно: 10 запросов в минуту с одного IP
    options.AddFixedWindowLimiter("support", o =>
    {
        o.PermitLimit = 10;
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

app.UseRateLimiter(); // до MapControllers()
```

```csharp
// SupportChatController.cs
[HttpPost("send")]
[EnableRateLimiting("support")] // ✅ применяем лимит
public async Task<IActionResult> Send([FromBody] SendMessageRequest request)
```

---

### SEC-03 — Path Traversal в `UserPageService` и `LegalPageService`

**Проблема / Описание:**  
`UserPageService.ProcessUserPage(path)` принимает `path` напрямую из URL (через `FallbackController`) и передаёт его в `string.Equals(path, "li_is", ...)`. Хотя здесь нет конкатенации с диском, в `LegalPageService` `fileName` берётся из switch-выражения, что защищено. Однако `UserPageService` **читает `userpage.html` каждый раз с диска** без проверки изменения `path` на содержание разделителей — при будущей модификации кода легко случайно открыть path traversal.

**В чём конкретно проблема:**  
`catchAll` из URL передаётся как `path` в сервис без санитизации (`..`, `/`, `\`). Сейчас критического traversal нет, но структура создаёт хрупкий код.

**Путь к файлу:**  
- `Backend/Barkfluff.WebServer/Controllers/FallbackController.cs` : строки 20, 44  
- `Backend/Barkfluff.WebServer/Services/UserPageService.cs` : строки 6–32

```csharp
// FallbackController.cs
[HttpGet("/{**catchAll}")]
public IActionResult HandleFallback(string catchAll)
{
    // ❌ catchAll идёт прямо в сервис без санитизации
    var userPageHtml = _userPageService.ProcessUserPage(catchAll);
    ...
}
```

**Варианты решения:**  
Добавить явную санитизацию перед передачей в сервис:

```csharp
// FallbackController.cs — добавить до вызова сервиса
// ✅ Убедимся, что path — простое имя без разделителей
var sanitizedPath = catchAll.Trim('/').Trim('\\');
if (sanitizedPath.Contains('/') || sanitizedPath.Contains('\\') || sanitizedPath.Contains(".."))
    return NotFound("Page not found");

var userPageHtml = _userPageService.ProcessUserPage(sanitizedPath);
```

---

### SEC-04 — Отсутствуют Security Headers

**Проблема / Описание:**  
Приложение не устанавливает стандартные HTTP security headers: `X-Content-Type-Options`, `X-Frame-Options`, `Content-Security-Policy`, `Referrer-Policy`. Публичный веб-сервер без этих заголовков уязвим к clickjacking, MIME sniffing, XSS через inline-скрипты.

**В чём конкретно проблема:**  
В `Program.cs` нет middleware для security headers. Ответы HTML-страниц не содержат защитных заголовков.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Program.cs` : строка 54 (после `app.UseRouting()`)

```csharp
// ❌ Нет security headers
app.UseRouting();
app.MapControllers();
```

**Варианты решения:**

```csharp
// ✅ Добавить middleware security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    // CSP настраивать под реальный контент страниц
    context.Response.Headers.Append("Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'");
    await next();
});

app.UseRouting();
app.MapControllers();
```

---

### SEC-05 — Отсутствует HTTPS-редирект / HSTS

**Проблема / Описание:**  
`Program.cs` не вызывает `app.UseHttpsRedirection()` и `app.UseHsts()`. Сервер принимает HTTP без принудительного перенаправления на HTTPS. Хотя в продакшне стоит Nginx-реверс прокси — сам сервис не защищён на уровне кода.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Program.cs` : строки 54–60

```csharp
// ❌ Нет HTTPS redirect и HSTS
var app = builder.Build();
app.UseRouting();
app.MapControllers();
```

**Варианты решения:**

```csharp
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts(); // ✅ HSTS в продакшне
}

app.UseHttpsRedirection(); // ✅ редирект HTTP → HTTPS
app.UseRouting();
app.MapControllers();
```

---

### SEC-06 — Telegram: исключение при `SendToAdmin` проглатывается молча

**Проблема / Описание:**  
В `SupportChatController.Send()` блок `catch (Exception)` при ошибке отправки в Telegram пуст — ни логирования, ни возврата предупреждения. Это скрывает сбои доставки от оператора.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/SupportChatController.cs` : строки 35–39

```csharp
catch (Exception)
{
    // ❌ Исключение молча проглочено, нет логирования
    // Message is still saved locally even if Telegram fails
}
```

**Варианты решения:**

```csharp
catch (Exception ex)
{
    // ✅ Логируем, чтобы видеть сбои Telegram
    _logger.LogError(ex, "Failed to forward support message to Telegram for chat {ChatId}", request.ChatId);
    // Возвращаем частичный успех — сообщение сохранено, но Telegram не уведомлён
    return Ok(new { success = true, warning = "Message saved, but admin notification failed" });
}
```

> Для этого нужно добавить `ILogger<SupportChatController>` в конструктор.

---

## 🟡 Оптимизация производительности

---

### PERF-01 — Чтение HTML с диска при каждом запросе

**Проблема / Описание:**  
`LegalPageService`, `UserPageService` читают HTML-файлы с диска (`File.ReadAllText`) на каждый HTTP-запрос. Файлы статичны и не меняются в runtime — нет смысла ходить на диск при каждом хите.

**В чём конкретно проблема:**  
- `LegalPageService.ProcessLegalPage()` → `File.ReadAllText(filePath)` — каждый вызов.  
- `UserPageService.ProcessUserPage()` → `File.ReadAllText(htmlPath)` — каждый вызов.  
- Нет кеширования на уровне сервисов.

**Путь к файлу:**  
- `Backend/Barkfluff.WebServer/Services/LegalPageService.cs` : строки 38, 51  
- `Backend/Barkfluff.WebServer/Services/UserPageService.cs` : строки 13, 21

```csharp
// LegalPageService.cs
// ❌ Диск на каждый запрос
return File.ReadAllText(filePath);
```

**Варианты решения:**  
Кешировать содержимое файлов в `Dictionary<string, string>` при старте сервиса:

```csharp
public class LegalPageService
{
    // ✅ Словарь: имя страницы → HTML, заполняется один раз в конструкторе
    private readonly Dictionary<string, string> _pageCache = new();
    private string _selfhostedHtml = string.Empty;

    public LegalPageService()
    {
        var assemblyLocation = AppContext.BaseDirectory;
        var legalPath = Path.Combine(assemblyLocation, "html", "legal");

        var pages = new[] { "privacy-policy", "terms-of-service", "account-deletion", "encryption" };
        var files = new[] { "privacy-policy.html", "terms-of-service.html", "account-deletion.html", "encryption.html" };

        for (int i = 0; i < pages.Length; i++)
        {
            var filePath = Path.Combine(legalPath, files[i]);
            if (File.Exists(filePath))
                _pageCache[pages[i]] = File.ReadAllText(filePath);
        }

        var selfhosted = Path.Combine(assemblyLocation, "html", "selfhosted.html");
        if (File.Exists(selfhosted))
            _selfhostedHtml = File.ReadAllText(selfhosted);
    }

    public string ProcessLegalPage(string pageName)
        => _pageCache.TryGetValue(pageName, out var html) ? html : string.Empty;

    public string ProcessSelfhostedPage() => _selfhostedHtml;
}
```

---

### PERF-02 — `DownloadController` читает весь EXE в память (`File.ReadAllBytes`)

**Проблема / Описание:**  
`DownloadController.GetInstaller()` загружает весь `Barkfluff.Updater.CLI.exe` в `byte[]` перед отдачей клиенту. Для бинарного файла инсталлятора это нецелесообразно — файл может весить десятки мегабайт, что создаёт пик потребления памяти при каждой загрузке.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/DownloadController.cs` : строки 18–20

```csharp
// ❌ Весь файл в RAM
var fileBytes = System.IO.File.ReadAllBytes(installerPath);
return File(fileBytes, "application/octet-stream", "Barkfluff.Updater.CLI.exe");
```

**Варианты решения:**  
Использовать `PhysicalFile` — ASP.NET Core сам стримит файл без буферизации в RAM:

```csharp
// ✅ Стриминг без загрузки в память
if (!System.IO.File.Exists(installerPath))
    return NotFound("Installer not found");

return PhysicalFile(installerPath, "application/octet-stream", "Barkfluff.Updater.CLI.exe");
```

---

### PERF-03 — `AssetsController` читает изображения в память (`File.ReadAllBytes`)

**Проблема / Описание:**  
Аналогично PERF-02: `AssetsController` использует `File.ReadAllBytes` для раздачи изображений. При нескольких одновременных запросах это создаёт излишнее давление на GC. Дополнительно — нет кеш-заголовков (`Cache-Control`, `ETag`), поэтому браузеры будут перезапрашивать статику при каждом визите.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/AssetsController.cs` : строки 24–26

```csharp
// ❌ В память + нет cache headers
var fileBytes = System.IO.File.ReadAllBytes(filePath);
return File(fileBytes, contentType);
```

**Варианты решения:**

```csharp
[HttpGet("/assets/{filename}")]
public IActionResult GetAsset(string filename)
{
    if (!_allowedFiles.TryGetValue(filename, out var contentType))
        return NotFound();

    var filePath = Path.Combine(AppContext.BaseDirectory, "files", filename);
    if (!System.IO.File.Exists(filePath))
        return NotFound();

    // ✅ PhysicalFile — стриминг без RAM-буферизации
    // ✅ Cache-Control — браузер кеширует на 1 день
    Response.Headers.CacheControl = "public, max-age=86400";
    return PhysicalFile(filePath, contentType);
}
```

---

### PERF-04 — `SupportChatService`: отсутствует TTL и лимит сессий чата

**Проблема / Описание:**  
`SupportChatService._chats` — `ConcurrentDictionary` без ограничений. Каждая новая сессия (новый GUID) добавляет запись в память и **никогда** не удаляется. При долгой работе сервиса или умышленном флуде RAM будет бесконечно расти.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/SupportChatService.cs` : строка 7

```csharp
// ❌ Никогда не чистится
private readonly ConcurrentDictionary<string, ChatSession> _chats = new();
```

**Варианты решения:**  
Добавить TTL через `MemoryCache` или периодическую очистку старых сессий:

```csharp
public class ChatSession
{
    public string ChatId { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = new();
    public object Lock { get; } = new();
    public DateTime LastActivity { get; set; } = DateTime.UtcNow; // ✅ трекаем активность
}

// В сервисе — периодическая очистка сессий старше 24 часов
private void CleanupOldSessions()
{
    var expiredKeys = _chats
        .Where(kv => DateTime.UtcNow - kv.Value.LastActivity > TimeSpan.FromHours(24))
        .Select(kv => kv.Key)
        .ToList();

    foreach (var key in expiredKeys)
        _chats.TryRemove(key, out _);
}
```

---

### PERF-05 — `VersionPollingService`: HTTP-клиент без таймаута

**Проблема / Описание:**  
В `PollAllAsync` создаётся `http = _httpFactory.CreateClient()` без явного таймаута. Если `storage.barkfluff.com` зависнет — запрос будет ждать дефолтные 100 секунд (таймаут `HttpClient` по умолчанию). При 6 эндпоинтах это может задержать цикл опроса на ~10 минут вместо нормального интервала.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/VersionPollingService.cs` : строки 48–51

```csharp
private async Task PollAllAsync(CancellationToken ct)
{
    // ❌ Нет таймаута на клиент — зависнет на 100 сек при недоступном хосте
    var http = _httpFactory.CreateClient();
    foreach (var (url, setter) in Endpoints)
        await FetchVersionAsync(http, url, setter, ct);
}
```

**Варианты решения:**  
Настроить именованный `HttpClient` с таймаутом в `Program.cs`:

```csharp
// Program.cs
builder.Services.AddHttpClient("versions", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10); // ✅ максимум 10 секунд на запрос
});
```

```csharp
// VersionPollingService.cs
var http = _httpFactory.CreateClient("versions"); // ✅ используем именованный клиент
```

---

### PERF-06 — `GrpcChannel` не настроен (`keepAlive`, переиспользование)

**Проблема / Описание:**  
`GrpcChannel.ForAddress(usersServiceHost)` создаётся с дефолтными настройками. Нет `KeepAlive` настроек, нет `MaxRetryAttempts`. При долгом idle канал может быть закрыт на стороне сервера, а следующий запрос получит `GOAWAY` без автоматического переподключения.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Program.cs` : строка 30

```csharp
// ❌ Канал без конфигурации — нет keepalive, нет retry
var usersChannel = GrpcChannel.ForAddress(usersServiceHost);
```

**Варианты решения:**

```csharp
// ✅ Настраиваем keepalive и retry policy
var usersChannel = GrpcChannel.ForAddress(usersServiceHost, new GrpcChannelOptions
{
    HttpHandler = new SocketsHttpHandler
    {
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
        EnableMultipleHttp2Connections = true,
    },
    ServiceConfig = new ServiceConfig
    {
        MethodConfigs = { new MethodConfig
        {
            Names = { MethodName.Default },
            RetryPolicy = new RetryPolicy
            {
                MaxAttempts = 3,
                InitialBackoff = TimeSpan.FromSeconds(1),
                MaxBackoff = TimeSpan.FromSeconds(5),
                BackoffMultiplier = 1.5,
                RetryableStatusCodes = { StatusCode.Unavailable }
            }
        }}
    }
});
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — `UserApiController`: 404 возвращает `200 OK` с `found: false`

**Проблема / Описание:**  
Когда пользователь не найден, контроллер возвращает `200 OK` с телом `{ found: false }`. Семантически корректный HTTP-ответ для "ресурс не существует" — это `404 Not Found`. Текущее поведение нарушает REST-семантику и может вводить в заблуждение клиентские приложения.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/UserApiController.cs` : строки 27–30

```csharp
if (profile is null)
{
    // ❌ 200 OK когда пользователь не найден — нарушение REST
    return Ok(new { found = false });
}
```

**Варианты решения:**

```csharp
if (profile is null)
{
    // ✅ 404 с телом для обратной совместимости
    return NotFound(new { found = false });
}
```

---

### BUG-02 — `SupportChatService`: `_telegramMessageMap` никогда не очищается

**Проблема / Описание:**  
`_telegramMessageMap` (маппинг Telegram message ID → chat GUID) пополняется при каждой отправке сообщения в Telegram и **никогда не очищается**. Со временем это словарь растёт бесконечно, особенно при большом количестве обращений в поддержку.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/SupportChatService.cs` : строка 8

```csharp
// ❌ Растёт вечно — нет TTL или лимита
private readonly ConcurrentDictionary<int, string> _telegramMessageMap = new();
```

**Варианты решения:**  
Использовать `MemoryCache` с TTL вместо `ConcurrentDictionary`:

```csharp
// ✅ Используем IMemoryCache с TTL 24 часа
private readonly IMemoryCache _telegramMessageCache;

public void TrackTelegramMessage(int telegramMessageId, string chatId)
    => _telegramMessageCache.Set($"tg:{telegramMessageId}", chatId, TimeSpan.FromHours(24));

public string? GetChatIdByTelegramMessage(int telegramMessageId)
    => _telegramMessageCache.TryGetValue($"tg:{telegramMessageId}", out string? id) ? id : null;
```

---

### BUG-03 — `FallbackController`: любой несуществующий путь обрабатывается как `username`

**Проблема / Описание:**  
`FallbackController` перехватывает **все** необработанные пути и передаёт их в `UserPageService`. Запросы к `/robots.txt`, `/sitemap.xml`, `/favicon.ico` (у него есть отдельный контроллер, но теоретически могут быть варианты) — всё попадает в `ProcessUserPage`. Это отдаёт HTML-шаблон вместо 404, и поисковые боты получают контент вместо правильного кода.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/FallbackController.cs` : строки 44–52

```csharp
// ❌ Любой путь → userpage.html, включая роботы и несуществующие ресурсы
var userPageHtml = _userPageService.ProcessUserPage(catchAll);

if (string.IsNullOrEmpty(userPageHtml))
{
    return NotFound("Page not found");
}
return Content(userPageHtml, "text/html");
```

**Варианты решения:**  
Добавить allowlist допустимых паттернов для username (буквы, цифры, `_`, `-`) перед передачей в сервис:

```csharp
// ✅ Только простые username — буквы, цифры, подчёркивание, дефис, 3-32 символа
if (!System.Text.RegularExpressions.Regex.IsMatch(catchAll, @"^[a-zA-Z0-9_\-]{3,32}$"))
    return NotFound("Page not found");

var userPageHtml = _userPageService.ProcessUserPage(catchAll);
```

---

### BUG-04 — `VersionApiController` возвращает пустые строки до первого опроса

**Проблема / Описание:**  
`VersionStore` инициализируется пустыми строками. `VersionPollingService` запускается асинхронно как `BackgroundService`. В промежутке между стартом приложения и первым успешным опросом `GET /api/versions` вернёт все версии как пустые строки — клиент не сможет отличить "данные ещё не готовы" от "версия действительно пустая".

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/VersionStore.cs` : строки 7–12

```csharp
// ❌ Пустые строки в начале — нет индикатора "данные не готовы"
private string _androidRelease = string.Empty;
private string _androidBeta = string.Empty;
private string _windowsRelease = string.Empty;
```

**Варианты решения:**  
Использовать `null` / nullable string или флаг `IsReady`:

```csharp
// ✅ null явно означает "ещё не загружено"
private string? _androidRelease = null;
...

public bool IsReady => _androidRelease != null; // флаг готовности
```

В `VersionApiController`:
```csharp
if (!_store.IsReady)
    return StatusCode(503, new { error = "Version data not yet available, try again shortly" });
```

---

### BUG-05 — `TelegramService.HandleMessage` — не `async`, но тип `Task`

**Проблема / Описание:**  
Метод `HandleMessage` имеет возвращаемый тип `Task`, но внутри нет ни одного `await`. Метод возвращает `Task.CompletedTask` вручную. Это не критический баг, но нарушает принятый в проекте стиль и подразумевает, что метод может стать async в будущем, но до этого момента создаёт путаницу.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/TelegramService.cs` : строки 48–80

```csharp
// ❌ Объявлен как Task, но не async — ручной return Task.CompletedTask
private Task HandleMessage(Message message, UpdateType type)
{
    ...
    return Task.CompletedTask;
}
```

**Варианты решения:**

```csharp
// ✅ Явно пометить как sync-метод с правильным именованием, или добавить async
private async Task HandleMessage(Message message, UpdateType type)
{
    await Task.CompletedTask; // заглушка до реальной async-логики
    // ... или убрать async и переименовать в void-обработчик
}
```

---

### BUG-06 — `UserPageService`: двойная точка с запятой и лишние пустые строки

**Проблема / Описание:**  
В `UserPageService.ProcessUserPage` есть синтаксическая ошибка оформления: двойная точка с запятой `;;` и три пустых строки подряд. Указывает на незавершённый рефакторинг.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/UserPageService.cs` : строки 24–27

```csharp
// ❌ Двойная ;; — следствие копипасты или незавершённого рефакторинга
var htmlContent = File.ReadAllText(htmlPath).Replace("%%username%%", $"{path}"); ;



return htmlContent;
```

**Варианты решения:**

```csharp
// ✅ Чистый код
var htmlContent = File.ReadAllText(htmlPath).Replace("%%username%%", path);
return htmlContent;
```

---

## 🔵 Прочее / Code Quality

---

### QA-01 — `appsettings.json`: `AllowedHosts: "*"` в продакшне

**Проблема / Описание:**  
`"AllowedHosts": "*"` разрешает Host header с любым значением. В продакшне следует ограничить список разрешённых хостов конкретными доменами, чтобы предотвратить Host Header Injection атаки.

**Путь к файлу:** `Backend/Barkfluff.WebServer/appsettings.json` : строка 8

```json
// ❌ Любой Host header принимается
"AllowedHosts": "*"
```

**Варианты решения:**

```json
// ✅ Только нужные домены
"AllowedHosts": "barkfluff.com;www.barkfluff.com;localhost"
```

---

### QA-02 — `UserProfileData` — модель данных в файле сервиса

**Проблема / Описание:**  
Класс `UserProfileData` объявлен в том же файле что и `UserProfileService`. Это нарушает Single Responsibility и затрудняет переиспользование модели в других местах.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Services/UserProfileService.cs` : строки 84–92

```csharp
// ❌ Модель данных в файле сервиса
public class UserProfileData
{
    public string FirstName { get; set; } = string.Empty;
    ...
}
```

**Варианты решения:**  
Вынести в отдельный файл `Models/UserProfileData.cs`.

---

### QA-03 — Отсутствует `CancellationToken` в `UserApiController`

**Проблема / Описание:**  
Метод `GetUserProfileAsync` принимает только `username`, без `CancellationToken`. При отмене запроса клиентом gRPC-вызов продолжит выполняться до конца, тратя ресурсы впустую.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Controllers/UserApiController.cs` : строка 18  
`Backend/Barkfluff.WebServer/Services/UserProfileService.cs` : строка 31

```csharp
// ❌ Нет CancellationToken — запрос не прерывается при дисконнекте клиента
public async Task<IActionResult> GetUserProfile(string username)
{
    var profile = await _userProfileService.GetUserProfileAsync(username);
```

**Варианты решения:**

```csharp
// ✅ Передаём токен отмены через HttpContext
public async Task<IActionResult> GetUserProfile(string username, CancellationToken ct)
{
    var profile = await _userProfileService.GetUserProfileAsync(username, ct);
```

```csharp
// UserProfileService.cs
public async Task<UserProfileData?> GetUserProfileAsync(string username, CancellationToken ct = default)
{
    ...
    var response = await _client.GetUserByUsernameAsync(
        new GetUserByUsernameRequest { Username = username },
        new CallOptions(metadata, cancellationToken: ct)); // ✅ передаём в gRPC
}
```

---

### QA-04 — Нет `robots.txt` и `sitemap.xml`

**Проблема / Описание:**  
Публичный веб-сайт не отдаёт `robots.txt`. Поисковые роботы будут индексировать страницы по умолчанию, включая пользовательские страницы профилей (если нежелательно). Нет контроля над индексацией.

**Варианты решения:**  
Добавить `RobotsController` или статический файл:

```csharp
[HttpGet("/robots.txt")]
public ContentResult GetRobots()
{
    return Content(
        "User-agent: *\nAllow: /\nDisallow: /api/\nDisallow: /assets/\n",
        "text/plain");
}
```

---

### QA-05 — `Program.cs`: `TelegramService` запускается вне DI lifecycle

**Проблема / Описание:**  
`telegramService.Start()` вызывается напрямую после `app.Build()`, вне стандартного жизненного цикла `IHostedService`. Это значит бот стартует сразу, но ASP.NET Core не может корректно остановить его при shutdown через `IHostApplicationLifetime`.

**Путь к файлу:** `Backend/Barkfluff.WebServer/Program.cs` : строки 62–64

```csharp
// ❌ Запуск вне lifecycle — не управляется хостом
var telegramService = app.Services.GetRequiredService<TelegramService>();
telegramService.Start();
app.Run();
```

**Варианты решения:**  
Реализовать `IHostedService` в `TelegramService` и зарегистрировать через `AddHostedService`:

```csharp
// TelegramService : IHostedService
public Task StartAsync(CancellationToken cancellationToken)
{
    Start(); // ✅ вызывается хостом при запуске
    return Task.CompletedTask;
}

public Task StopAsync(CancellationToken cancellationToken)
{
    // graceful shutdown бота
    _bot?.CloseAsync(cancellationToken);
    return Task.CompletedTask;
}
```

```csharp
// Program.cs
builder.Services.AddHostedService<TelegramService>(); // ✅ вместо ручного Start()
```

---

*Документ сгенерирован при аудите кодовой базы `Barkfluff.WebServer` на ветке `dev`.*
