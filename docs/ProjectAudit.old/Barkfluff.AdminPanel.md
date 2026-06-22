# Аудит проекта: Barkfluff.AdminPanel

> **Первичный аудит:** 2025-07
> **Ревизия:** 2026-05-18
> **Ветка:** `dev`
> **Путь к проекту:** `Backend/Barkfluff.AdminPanel/`
> **Аудитор:** GitHub Copilot (BarkfluffAgent) + Claude Opus 4.7 (ревизия)
> **Статус:** 🔴 Большинство проблем актуально, найдены новые

---

## Содержание

- [Статус ранее найденных проблем](#статус-ранее-найденных-проблем)
- [🔴 Безопасность](#-безопасность)
- [🟡 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Производительность](#-производительность)
- [⚪ Качество кода / Прочее](#-качество-кода--прочее)
- [🆕 Новые проблемы (ревизия 2026-05-18)](#-новые-проблемы-ревизия-2026-05-18)
- [Сводная таблица](#сводная-таблица)

---

## Статус ранее найденных проблем

| ID      | Статус        | Комментарий                                                                 |
| ------- | ------------- | --------------------------------------------------------------------------- |
| SEC-01  | ❌ Актуально  | Реальный Telegram BotToken остаётся в `appsettings.json:10-11`              |
| SEC-02  | ❌ Актуально  | Shell injection в `DockerService.cs:630` не исправлен                       |
| SEC-03  | ❌ Актуально  | Rate limiting отсутствует                                                   |
| SEC-04  | ❌ Актуально  | Cookie удаляется без флагов в `TokenAuthMiddleware.cs:86`                   |
| SEC-05  | ⚠️ Частично   | `UseHttpsRedirection()` остаётся, TLS терминируется на Nginx — не критично |
| SEC-06  | ⚠️ Частично   | Legacy-метод существует, но прямого пути вызова нет                         |
| BUG-01  | ❌ Актуально  | `Timer` в `PendingAuthService` не освобождается                             |
| BUG-02  | ❌ Актуально  | `Task.Run` без `CancellationToken`                                          |
| BUG-03  | ❌ Актуально  | Двойной `FindById` в `TokenService.ValidateToken`                           |
| BUG-04  | ❌ Актуально  | `token.IsExpired(3)` захардкожен                                            |
| BUG-05  | ❌ Актуально  | `StopAsync` не ждёт `ReceiveAsync`                                          |
| BUG-06  | ❌ Актуально  | `bool _initialized` без `volatile`                                          |
| PERF-01 | ⚠️ Частично   | Частичное кэширование добавлено, полное покрытие отсутствует                |
| PERF-02 | ❌ Актуально  | `Dictionary` создаётся локально                                             |
| PERF-03 | ❌ Актуально  | `GetContainerStatusAsync` грузит весь список                                |
| PERF-04 | ❌ Актуально  | Фиксированные `Task.Delay`                                                  |
| CODE-01 | ❌ Актуально  | Nullable warnings в `ParseUserAgent`                                        |
| CODE-02 | ❌ Актуально  | Несколько классов в одном файле                                             |
| CODE-03 | ❌ Актуально  | Нет глобального exception handler                                           |
| CODE-04 | ⚠️ Частично   | `[Obsolete]` свойство публичное, но не вызывается                           |

**Итого:** 17 актуально, 3 частично, 0 исправлено. См. [новые проблемы](#-новые-проблемы-ревизия-2026-05-18).

---

## 🔴 Безопасность

---

### SEC-01 — Реальные секреты в appsettings.json (в репозитории)

**Описание**  
`appsettings.json` содержит действующий Telegram Bot Token и Telegram User ID администратора в открытом виде. Файл коммитится в git — любой, кто имеет доступ к репозиторию, получает полный контроль над ботом и знает ID администратора.

**В чём конкретно проблема**  
- Bot Token позволяет отправлять сообщения от имени бота, перехватывать входящие обновления (вытеснить webhook/polling), управлять ботом.  
- Admin TelegramUserId — раскрывает личность администратора.

**Файл:** `Backend/Barkfluff.AdminPanel/appsettings.json` — строки 9–11

```json
// ❌ ПРОБЛЕМА: реальный токен и ID в коммите
"Telegram": {
  "BotToken": "8539569051:AAHMs6TwTKOpYqcA8XkWTB7p6w8CO1RWQwQ",
  "Admins": "495716470:admin_nick"
}
```

**Варианты решения**

1. Вынести в переменные окружения / Docker secrets, из `appsettings.json` убрать.
2. Использовать `appsettings.json` только с заглушками-примерами, а реальные значения передавать через `Telegram__BotToken` env var.
3. Добавить файл в `.gitignore` или использовать `appsettings.Production.json` (не коммитить).
4. **Немедленно:** отозвать скомпрометированный токен через `@BotFather` (`/revoke`).

```json
// ✅ РЕШЕНИЕ: только примеры в appsettings.json
"Telegram": {
  "BotToken": "",   // Задаётся через env: Telegram__BotToken
  "Admins": ""      // Задаётся через env: Telegram__Admins  (формат: "userId:username")
}
```

---

### SEC-02 — Shell Injection в `UpdateAdminPanelAsync`

**Описание**  
В методе `UpdateAdminPanelAsync` пути `envFile` и `composeFile`, полученные из `docker inspect`, интерполируются **напрямую в строку shell-команды**, которая передаётся аргументом `-c` к `sh`. Если путь на хосте содержит пробелы, кавычки или метасимволы (`; | && $(...)`), это приведёт к выполнению произвольного кода внутри контейнера от имени `root`.

**В чём конкретно проблема**  
Основные аргументы Docker передаются безопасно через `ArgumentList`, но последний аргумент — это shell-скрипт в виде строки с неэкранированными переменными.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/DockerService.cs` — строка 630

```csharp
// ❌ ПРОБЛЕМА: envFile и composeFile вставляются в shell-строку без экранирования
await RunDockerCommandAsync(
    "run", "-d", "--rm",
    "--name", "admin-panel-updater",
    "--user", "root",
    "-v", $"{dockerSock}:/var/run/docker.sock",
    "-v", $"{composeFile}:{composeFile}:ro",
    "-v", $"{envFile}:{envFile}:ro",
    "--entrypoint", "sh",
    helperImage,
    // ↓ если composeFile = "/path/'; rm -rf / #" — RCE
    "-c", $"sleep 2 && docker compose --project-name barkfluff --env-file {envFile} -f {composeFile} pull admin-panel && ..."
);
```

**Варианты решения**

1. Экранировать пути для shell с помощью одинарных кавычек (заменяя `'` → `'\''`).
2. Передавать пути через переменные окружения контейнера (`-e`) и обращаться к ним в shell как `$VAR`.

```csharp
// ✅ РЕШЕНИЕ: передаём пути через env-переменные, не интерполируем в скрипт
await RunDockerCommandAsync(
    "run", "-d", "--rm",
    "--name", "admin-panel-updater",
    "--user", "root",
    "-v", $"{dockerSock}:/var/run/docker.sock",
    "-v", $"{composeFile}:{composeFile}:ro",
    "-v", $"{envFile}:{envFile}:ro",
    "-e", $"COMPOSE_FILE={composeFile}",  // ← путь через env
    "-e", $"ENV_FILE={envFile}",
    "--entrypoint", "sh",
    helperImage,
    // ↓ обращаемся к переменным — безопасно
    "-c", "sleep 2 && docker compose --project-name barkfluff --env-file \"$ENV_FILE\" -f \"$COMPOSE_FILE\" pull admin-panel && docker compose --project-name barkfluff --env-file \"$ENV_FILE\" -f \"$COMPOSE_FILE\" up --force-recreate -d admin-panel && docker image prune -f"
);
```

---

### SEC-03 — Отсутствие Rate Limiting на публичных auth-эндпоинтах

**Описание**  
Эндпоинты `/api/auth/request` и `/api/auth/status` доступны без аутентификации. Нет никакого ограничения частоты запросов. Злоумышленник может:
- Спамить `/api/auth/request` с любым никнеймом — каждый раз Telegram-боту будет отправляться уведомление (Telegram API abuse).
- Перебирать `requestId` на `/api/auth/status` для enumerating активных сессий (requestId — `Guid`, что снижает риск, но не устраняет).

**Файл:** `Backend/Barkfluff.AdminPanel/Middleware/TokenAuthMiddleware.cs` — строки 36–41  
**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/AuthEndpoints.cs` — строки 16–56

```csharp
// ❌ ПРОБЛЕМА: публичный эндпоинт без rate limiting
if (path.StartsWith("/api/auth/request", StringComparison.OrdinalIgnoreCase) ||
    path.StartsWith("/api/auth/status", StringComparison.OrdinalIgnoreCase))
{
    await _next(context); // ← нет защиты от флуда
    return;
}
```

**Варианты решения**

1. Подключить `AspNetCoreRateLimit` или встроенный `Microsoft.AspNetCore.RateLimiting` (.NET 7+).
2. Ограничить по IP: не более N запросов в минуту.

```csharp
// ✅ РЕШЕНИЕ: добавить rate limiter в Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", o =>
    {
        o.PermitLimit = 5;           // 5 запросов
        o.Window = TimeSpan.FromMinutes(1); // в минуту
        o.QueueLimit = 0;
    });
});

// В middleware pipeline:
app.UseRateLimiter();

// На группе эндпоинтов auth:
var group = app.MapGroup("/api/auth")
    .RequireRateLimiting("auth");  // ← применить лимитер
```

---

### SEC-04 — Cookie `auth_token` без защитных флагов

**Описание**  
В `TokenAuthMiddleware` при удалении cookie используется `context.Response.Cookies.Delete("auth_token")`, но при создании (в `TelegramBotService` → `HandleCallbackQueryAsync` → вероятно через JS на фронтенде) cookie устанавливается без `HttpOnly`, `Secure`, и `SameSite=Strict`. Это открывает вектор XSS-кражи cookie и CSRF.

**Файл:** `Backend/Barkfluff.AdminPanel/Middleware/TokenAuthMiddleware.cs` — строки 74–91  
*(Установка cookie производится на фронтенде в `Login.html` — нужно проверить также `Pages/Login.html`)*

```csharp
// ❌ ПРОБЛЕМА: cookie удаляется без параметров — браузер может не принять Delete
// если при создании использовались флаги Path/Domain
context.Response.Cookies.Delete("auth_token");

// И при создании cookie (фронтенд или здесь) нет HttpOnly / Secure / SameSite
```

**Варианты решения**

Устанавливать cookie серверно с явными флагами безопасности:

```csharp
// ✅ РЕШЕНИЕ: серверная установка cookie с флагами
var cookieOptions = new CookieOptions
{
    HttpOnly = true,        // ← недоступна JS (защита от XSS)
    Secure = true,          // ← только HTTPS
    SameSite = SameSiteMode.Strict, // ← защита от CSRF
    Expires = DateTimeOffset.UtcNow.AddDays(settings.Value.TokenExpirationDays),
    Path = "/"
};
context.Response.Cookies.Append("auth_token", tokenId.ToString(), cookieOptions);

// При удалении — те же параметры:
context.Response.Cookies.Delete("auth_token", new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Strict,
    Path = "/"
});
```

---

### SEC-05 — `UseHttpsRedirection()` без HTTPS конфигурации

**Описание**  
В `Program.cs` вызывается `app.UseHttpsRedirection()`, но сервер явно привязан только к HTTP (`http://0.0.0.0:51888`). HTTPS-порт не настроен. Редирект никогда не срабатывает — middleware создаёт ложное ощущение защищённости, реально не работая.

**Файл:** `Backend/Barkfluff.AdminPanel/Program.cs` — строки 25, 142

```csharp
builder.WebHost.UseUrls("http://0.0.0.0:51888"); // ← только HTTP

// ...

app.UseHttpsRedirection(); // ← никогда не работает — HTTPS не настроен
```

**Варианты решения**

1. Убрать `UseHttpsRedirection()` если TLS терминируется на Nginx reverse proxy (рекомендуется).
2. Если нужен прямой HTTPS — добавить HTTPS-порт и сертификат.

```csharp
// ✅ РЕШЕНИЕ A: TLS на Nginx (текущая архитектура BarkFluff)
// Убрать UseHttpsRedirection() из Program.cs — Nginx уже обеспечивает TLS

// ✅ РЕШЕНИЕ B: если нужен прямой TLS
builder.WebHost.UseUrls("http://0.0.0.0:51888", "https://0.0.0.0:51889");
// + настроить сертификат в appsettings.json -> Kestrel:Certificates
```

---

### SEC-06 — Legacy метод `CreateAuthRequestAsyncLegacy` обходит проверку администратора

**Описание**  
Метод `CreateAuthRequestAsyncLegacy` создаёт запрос на авторизацию **без** проверки, что `nickname` соответствует известному администратору. Запрос рассылается всем администраторам с любым произвольным никнеймом. Метод помечен как legacy, но нигде не удалён и остаётся вызываемым.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/AuthService.cs` — строки 95–110

```csharp
// ❌ ПРОБЛЕМА: нет проверки nickname → adminList, рассылает всем
public async Task<string> CreateAuthRequestAsyncLegacy(AuthRequestDto dto)
{
    var (browser, os) = ParseUserAgent(dto.UserAgent);

    // ← nickname не проверяется по списку ParsedAdmins
    var request = _pendingAuthService.CreateRequest(
        dto.IpAddress,
        browser,
        os,
        dto.UserAgent,
        dto.TokenName ?? "Web Session",
        dto.Nickname);          // ← любое значение

    await _telegramBotService.SendAuthRequestAsync(request); // ← рассылка всем
    return request.RequestId;
}
```

**Варианты решения**

Удалить метод либо добавить в него ту же валидацию, что и в `CreateAuthRequestAsync`:

```csharp
// ✅ РЕШЕНИЕ: удалить legacy-метод или добавить валидацию
[Obsolete("Use CreateAuthRequestAsync instead. Will be removed in next release.")]
public async Task<string> CreateAuthRequestAsyncLegacy(AuthRequestDto dto)
{
    // Та же валидация, что и в основном методе
    var nickname = dto.Nickname?.Trim();
    if (string.IsNullOrEmpty(nickname))
        throw new ArgumentException("Nickname is required");

    var targetAdmin = _telegramSettings.Value.GetAdminByUsername(nickname);
    if (targetAdmin == null)
        throw new ArgumentException($"Unknown admin: {nickname}");

    // ... остальная логика
}
```

---

## 🟡 Баги и недоработки

---

### BUG-01 — `PendingAuthService` не освобождает `Timer` (`IDisposable` не реализован)

**Описание**  
`PendingAuthService` создаёт `System.Threading.Timer` в конструкторе, но не реализует `IDisposable`. При перезапуске DI-контейнера или в тестах таймер не освобождается, что ведёт к утечке ресурсов.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/PendingAuthService.cs` — строки 13–23

```csharp
// ❌ ПРОБЛЕМА: Timer создаётся, но никогда не диспоузится
public class PendingAuthService
{
    private readonly Timer _cleanupTimer; // ← утечка

    public PendingAuthService(IOptions<AuthSettings> settings)
    {
        _cleanupTimer = new Timer(
            CleanupExpiredRequests, null,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(10));
        // Dispose() не вызывается никогда
    }
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: реализовать IDisposable
public class PendingAuthService : IDisposable
{
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    // ... конструктор без изменений

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cleanupTimer?.Dispose();
    }
}
```

---

### BUG-02 — `Task.Run` в `UpdateRequestStatus` — неконтролируемый fire-and-forget

**Описание**  
После завершения запроса авторизации (approve/reject) запускается `Task.Run` с задержкой 5 минут для удаления записи из словаря. Задача не привязана к `CancellationToken` приложения — при shutdown ASP.NET Core она будет прервана на середине либо зависнет. Также исключения внутри этой задачи будут проглочены.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/PendingAuthService.cs` — строки 76–80

```csharp
// ❌ ПРОБЛЕМА: orphan Task без CancellationToken и обработки ошибок
_ = Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromMinutes(5)); // ← нет ct
    _requests.TryRemove(requestId, out _);
});
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: принимать CancellationToken (из IHostApplicationLifetime) и логировать ошибки
// В конструкторе:
private readonly CancellationToken _appStopping;

public PendingAuthService(IOptions<AuthSettings> settings, IHostApplicationLifetime lifetime)
{
    _appStopping = lifetime.ApplicationStopping;
    // ... таймер
}

// В UpdateRequestStatus:
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(TimeSpan.FromMinutes(5), _appStopping);
        _requests.TryRemove(requestId, out _);
    }
    catch (OperationCanceledException) { /* shutdown — нормально */ }
}, _appStopping);
```

---

### BUG-03 — `TokenService.UpdateActivity` — двойной `FindById` на каждый запрос

**Описание**  
`ValidateToken` вызывает `FindById` → затем вызывает `UpdateActivity`, которая делает ещё один `FindById` + `Update`. Итого **3 обращения к LiteDB** на каждый HTTP-запрос к любому API-эндпоинту. При интенсивном использовании это существенная нагрузка на I/O.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/TokenService.cs` — строки 50–73

```csharp
// ❌ ПРОБЛЕМА: ValidateToken → FindById(1) → UpdateActivity → FindById(2) + Update(3)
public AuthToken? ValidateToken(Guid tokenId)
{
    var token = _db.Tokens.FindById(tokenId); // запрос 1
    if (token == null) return null;

    if (token.IsExpired(_settings.Value.TokenExpirationDays))
    {
        _db.Tokens.Delete(tokenId);
        return null;
    }

    UpdateActivity(tokenId); // ← вызов метода, который делает FindById снова
    return token;
}

public void UpdateActivity(Guid tokenId)
{
    var token = _db.Tokens.FindById(tokenId); // запрос 2 — дублирование!
    if (token != null)
    {
        token.LastActivity = DateTime.UtcNow;
        _db.Tokens.Update(token); // запрос 3
    }
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: обновлять уже загруженный объект прямо в ValidateToken
public AuthToken? ValidateToken(Guid tokenId)
{
    var token = _db.Tokens.FindById(tokenId); // единственный FindById
    if (token == null) return null;

    if (token.IsExpired(_settings.Value.TokenExpirationDays))
    {
        _db.Tokens.Delete(tokenId);
        return null;
    }

    // Обновляем уже загруженный объект — без лишнего FindById
    token.LastActivity = DateTime.UtcNow;
    _db.Tokens.Update(token);

    return token;
}
```

---

### BUG-04 — Hardcoded срок жизни токена в `TelegramBotService`

**Описание**  
В `HandleMessageAsync` команды `/tokens` при отображении списка токенов используется захардкоженное значение `token.IsExpired(3)` вместо `_settings.Value.TokenExpirationDays`. Если администратор изменит `Auth:TokenExpirationDays` в конфигурации — бот будет отображать некорректный статус истечения.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/TelegramBotService.cs` — строка 463

```csharp
// ❌ ПРОБЛЕМА: 3 дня захардкожено, игнорирует конфигурацию
var isExpired = token.IsExpired(3); // ← должен быть _settings.Value.TokenExpirationDays
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: использовать конфигурацию
// В UpdateHandler добавить IOptions<AuthSettings>:
private readonly IOptions<AuthSettings> _authSettings;

// В конструкторе добавить параметр и сохранить в поле.

// В использовании:
var isExpired = token.IsExpired(_authSettings.Value.TokenExpirationDays);
```

---

### BUG-05 — `TelegramBotService.StopAsync` не дожидается остановки polling

**Описание**  
`StopAsync` отменяет `_cts`, но сразу возвращает `Task.CompletedTask`. Polling (`_botClient.ReceiveAsync`) запущен в fire-and-forget (`_ = _botClient.ReceiveAsync(...)`), поэтому при shutdown приложения фоновая задача не ожидается — возможна обработка входящих обновлений после начала teardown.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/TelegramBotService.cs` — строки 117–142

```csharp
// ❌ ПРОБЛЕМА: задача не сохраняется, StopAsync не ждёт завершения
_ = _botClient.ReceiveAsync(         // ← fire-and-forget
    updateHandler: updateHandler,
    receiverOptions: receiverOptions,
    cancellationToken: _cts.Token);

public async Task StopAsync(CancellationToken cancellationToken)
{
    _cts.Cancel();
    await Task.CompletedTask; // ← не ждём реальной остановки
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: сохранить Task и ждать его в StopAsync
private Task? _receivingTask;

public Task StartAsync(CancellationToken cancellationToken)
{
    _receivingTask = _botClient.ReceiveAsync(
        updateHandler: updateHandler,
        receiverOptions: receiverOptions,
        cancellationToken: _cts.Token);
    // Не await — пусть выполняется в фоне
    return Task.CompletedTask;
}

public async Task StopAsync(CancellationToken cancellationToken)
{
    _logger.LogInformation("Stopping Telegram Bot Service...");
    _cts.Cancel();
    if (_receivingTask != null)
    {
        try { await _receivingTask.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { /* ожидаемо */ }
    }
}
```

---

### BUG-06 — `S3BrowserService._initialized` — потенциальный race condition

**Описание**  
Поле `_initialized` объявлено как обычный `bool` без `volatile`. Теоретически на многоядерных системах CPU кэш может вернуть устаревшее значение `false` после того как другой поток установил `true`, что приведёт к повторной инициализации. Несмотря на наличие `SemaphoreSlim`, внешняя проверка `if (_initialized) return;` находится вне лока.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/S3BrowserService.cs` — строки 16, 24–39

```csharp
// ❌ ПРОБЛЕМА: bool без volatile — ранняя проверка без барьера памяти
private bool _initialized; // ← должно быть volatile

private async Task EnsureInitializedAsync()
{
    if (_initialized) return; // ← читается без memory barrier

    await _initLock.WaitAsync();
    // ...
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: volatile обеспечивает видимость между потоками
private volatile bool _initialized;
```

---

## 🔵 Производительность

---

### PERF-01 — `ServeHtmlFile` читает файл с диска на каждый запрос

**Описание**  
Каждое обращение к HTML-странице (dashboard, services, logs и др.) читает файл с диска через `File.ReadAllTextAsync`. HTML-файлы статичны (за исключением подстановки `{{SERVER_STARTED_AT_UTC}}`, которая не меняется). Нет кэширования ни на уровне памяти, ни HTTP-заголовков.

**Файл:** `Backend/Barkfluff.AdminPanel/Program.cs` — строки 226–242

```csharp
// ❌ ПРОБЛЕМА: чтение с диска на каждый запрос
private static async Task ServeHtmlFile(HttpContext context, string fileName)
{
    var filePath = Path.Combine(pagesPath, fileName);
    // ...
    var content = await File.ReadAllTextAsync(filePath); // ← disk I/O каждый раз
    content = content.Replace("{{SERVER_STARTED_AT_UTC}}", StartedAtUtc.ToString("o"));
    await context.Response.WriteAsync(content);
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: кэшировать результат в памяти (один раз при первом обращении)
private static readonly ConcurrentDictionary<string, string> _htmlCache = new();

private static async Task ServeHtmlFile(HttpContext context, string fileName)
{
    var filePath = Path.Combine(AppContext.BaseDirectory, "Pages", fileName);

    if (!File.Exists(filePath))
    {
        context.Response.StatusCode = 404;
        await context.Response.WriteAsync($"Page not found: {fileName}");
        return;
    }

    // Подстановка SERVER_STARTED_AT_UTC не меняется — кэшируем навсегда
    var content = _htmlCache.GetOrAdd(fileName, _ =>
    {
        var raw = File.ReadAllText(filePath);
        return raw.Replace("{{SERVER_STARTED_AT_UTC}}", StartedAtUtc.ToString("o"));
    });

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(content);
}
```

---

### PERF-02 — `ConvertContainerNameToServiceName` создаёт `Dictionary` при каждом вызове

**Описание**  
Метод `ConvertContainerNameToServiceName` объявляет и заполняет `new Dictionary<string, string>` локально при каждом вызове. Словарь константен — он должен быть статическим полем класса.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/DockerService.cs` — строки 213–237

```csharp
// ❌ ПРОБЛЕМА: новый Dictionary создаётся на каждый вызов метода
private string ConvertContainerNameToServiceName(string containerName)
{
    var containerToServiceMap = new Dictionary<string, string> // ← выделение памяти каждый раз
    {
        { "beacon", "beacon" },
        // ... 15 записей
    };
    return containerToServiceMap.GetValueOrDefault(containerName, containerName);
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: статическое readonly поле
private static readonly Dictionary<string, string> ContainerToServiceMap =
    new(StringComparer.OrdinalIgnoreCase)
    {
        { "beacon",              "beacon" },
        { "configuration",       "configuration" },
        { "files",               "files" },
        { "identity",            "identity" },
        { "messages",            "messages" },
        { "notification",        "notification" },
        { "users",               "users" },
        { "fast-auth",           "fast-auth" },
        { "updates",             "updates" },
        { "onliner",             "onliner" },
        { "web",                 "web" },
        { "seq",                 "seq" },
        { "minio",               "minio" },
        { "rabbitmq",            "rabbitmq" },
        { "redis",               "redis" },
        { "postgres_barkfluff",  "postgres" },
        { "admin-panel",         "admin-panel" }
    };

private string ConvertContainerNameToServiceName(string containerName)
    => ContainerToServiceMap.GetValueOrDefault(containerName, containerName);
```

---

### PERF-03 — `GetContainerStatusAsync` загружает весь список контейнеров для поиска одного

**Описание**  
`GetContainerStatusAsync` вызывает `GetContainersAsync()` (выполняет `docker ps --all`), а затем делает LINQ `FirstOrDefault` по результату. При наличии большого количества контейнеров — это избыточно. Docker позволяет получить информацию об одном контейнере напрямую.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/DockerService.cs` — строки 63–79

```csharp
// ❌ ПРОБЛЕМА: загружаем все контейнеры чтобы найти один
public async Task<ContainerStatusDto?> GetContainerStatusAsync(string containerName)
{
    var containers = await GetContainersAsync(); // ← docker ps --all
    return containers.FirstOrDefault(c =>
        c.Name == containerName || ...);
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: запрашивать только нужный контейнер через docker ps --filter
public async Task<ContainerStatusDto?> GetContainerStatusAsync(string containerName)
{
    try
    {
        var json = await RunDockerCommandAsync(
            "ps", "--all",
            "--filter", $"name=^{containerName}$",
            "--format", "{{json .}}");

        var containers = ParseDockerPsOutput(json);
        return containers.FirstOrDefault();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Ошибка получения статуса контейнера {ContainerName}", containerName);
        throw;
    }
}
```

---

### PERF-04 — Жёстко прошитые задержки `Task.Delay` в `RestartAllServicesAsync` / `UpdateAllServicesAsync`

**Описание**  
При массовом перезапуске/обновлении сервисов используются фиксированные паузы (2–3 секунды) вместо реальной проверки готовности. Если сервис стартует быстрее — время теряется впустую; если медленнее — следующий сервис запускается раньше готовности предыдущего и может упасть.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/DockerService.cs` — строки 486, 552

```csharp
// ❌ ПРОБЛЕМА: фиксированная пауза без проверки готовности
await Task.Delay(2000); // ← 2 секунды для restart
await Task.Delay(3000); // ← 3 секунды для update
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: poll docker inspect до статуса "running" с таймаутом
private async Task WaitForContainerRunningAsync(string containerName, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            var status = await GetContainerStatusAsync(containerName);
            if (status?.State == "running") return;
        }
        catch { /* контейнер ещё не поднялся */ }

        await Task.Delay(500);
    }
    _logger.LogWarning("Контейнер {Name} не перешёл в running за {Timeout}", containerName, timeout);
}

// Использование:
await RunDockerCommandAsync("restart", "-t", "30", serviceName);
await WaitForContainerRunningAsync(serviceName, TimeSpan.FromSeconds(30));
```

---

## ⚪ Качество кода / Прочее

---

### CODE-01 — Nullable warning: `string browser = null` в `ParseUserAgent`

**Описание**  
Метод `ParseUserAgent` объявляет `string browser = null` и `string os = null`, что вызывает предупреждения компилятора в контексте Nullable Reference Types (`#nullable enable`). Несмотря на то что значения всегда перезаписываются в ветках `else`, компилятор это не гарантирует.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/AuthService.cs` — строки 151–152

```csharp
// ❌ ПРОБЛЕМА: nullable warning, потенциально проблемный стиль
string browser = null; // CS8600
string os = null;
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: инициализировать значением по умолчанию
string browser = "Unknown";
string os = "Unknown";

// Убрать else-ветки с присвоением "Unknown" — они теперь избыточны
```

---

### CODE-02 — Несколько классов в `Program.cs` и `TelegramBotService.cs`

**Описание**  
`Program.cs` содержит 6 классов помимо `Program`: `TelegramSettings`, `TelegramProxySettings`, `AdminUser`, `TelegramSettingsExtensions`, `AuthSettings`, `LiteDbSettings`. `TelegramBotService.cs` содержит класс `UpdateHandler`. Это нарушает принцип Single Responsibility и затрудняет навигацию.

**Файл:** `Backend/Barkfluff.AdminPanel/Program.cs` — строки 245–355  
**Файл:** `Backend/Barkfluff.AdminPanel/Services/TelegramBotService.cs` — строки 268–607

**Варианты решения**

```
// ✅ РЕШЕНИЕ: разнести по отдельным файлам:
Models/
  TelegramSettings.cs        ← TelegramSettings, TelegramProxySettings, TelegramSettingsExtensions
  AdminUser.cs               ← AdminUser
  AuthSettings.cs            ← AuthSettings (уже есть Models/AuthToken.cs — рядом)
Services/
  TelegramUpdateHandler.cs   ← class UpdateHandler : IUpdateHandler
```

---

### CODE-03 — Отсутствие глобального exception handling middleware

**Описание**  
Нет `app.UseExceptionHandler()` или `app.UseProblemDetails()`. Необработанные исключения в Minimal API-хендлерах вернут 500 с полным стектрейсом в теле ответа в development-режиме — это утечка внутренней информации. В некоторых эндпоинтах исключения не перехватываются вовсе.

**Файл:** `Backend/Barkfluff.AdminPanel/Program.cs` — строки 139–160 (pipeline setup)

```csharp
// ❌ ПРОБЛЕМА: нет глобального обработчика ошибок
var app = builder.Build();
app.UseHttpsRedirection();
// ← здесь должен быть UseExceptionHandler
app.UseTokenAuth();
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: добавить глобальный обработчик перед всем остальным
var app = builder.Build();

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var feature = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var ex = feature?.Error;
    // Не раскрываем детали в продакшене
    var msg = app.Environment.IsDevelopment() ? ex?.Message : "Internal server error";
    await ctx.Response.WriteAsJsonAsync(new { error = msg });
}));

// ... остальной pipeline
```

---

### CODE-04 — `[Obsolete]` свойство `AdminUserIds` в публичном API `TelegramSettings`

**Описание**  
Свойство `AdminUserIds` помечено `[Obsolete]`, но остаётся публичным и доступным. Это создаёт путаницу — новые разработчики могут использовать его по ошибке. Если оно нигде не используется — стоит удалить.

**Файл:** `Backend/Barkfluff.AdminPanel/Program.cs` — строки 265–266

```csharp
// ❌ ПРОБЛЕМА: публичное Obsolete-свойство в модели настроек
[Obsolete("Use ParsedAdmins instead")]
public List<long> AdminUserIds => ParsedAdmins.Select(a => a.TelegramUserId).ToList();
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ A: удалить полностью если нигде не используется
// (проверить через Find All References)

// ✅ РЕШЕНИЕ B: сделать internal если нужно для обратной совместимости внутри сборки
[Obsolete("Use ParsedAdmins instead. Will be removed in v2.0")]
internal List<long> AdminUserIds => ParsedAdmins.Select(a => a.TelegramUserId).ToList();
```

---

## 🆕 Новые проблемы (ревизия 2026-05-18)

---

### NEW-SEC-01 — Path Traversal в S3BrowserEndpoints

**Описание:**
Параметр `prefix` передаётся напрямую в `ListObjectsAsync` без валидации/нормализации. Атакующий-администратор может попытаться обойти ожидаемое префиксование (например, через `../` или абсолютный путь, либо через знак ` ` для обхода фильтров).

**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/S3BrowserEndpoints.cs` : 45–47

**Вариант решения:**
- Жёсткий whitelist разрешённых корневых префиксов (бакеты сервиса).
- Запрет символов `..`, ` `, `/` в начале.
- Если эндпоинт сам по себе только для админов — добавить аудит-лог при подозрительных префиксах.

---

### NEW-SEC-02 — IP Spoofing через `X-Forwarded-For`

**Описание:**
В `Endpoints/AuthEndpoints.cs:204-209` IP пользователя резолвится из `X-Forwarded-For` без указания доверенных прокси через `ForwardedHeadersOptions.KnownProxies` / `KnownNetworks`. Если фронт не за Nginx или Nginx не очищает заголовок — IP подделывается.

**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/AuthEndpoints.cs` : 204–209

**Вариант решения:**

```csharp
// ✅ В Program.cs
builder.Services.Configure<ForwardedHeadersOptions>(opts =>
{
    opts.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    opts.KnownProxies.Add(IPAddress.Parse("127.0.0.1")); // адрес Nginx
});
app.UseForwardedHeaders();
```

И использовать `HttpContext.Connection.RemoteIpAddress` вместо ручного парсинга заголовка.

---

### NEW-BUG-01 — `IndexOutOfRangeException` при парсинге callback-данных

**Описание:**
`data.Split(':')` в обработчике callback-кнопок Telegram не проверяет длину массива перед обращением к индексам. Некорректный callback (`auth:123` без action) приведёт к необработанному исключению в `UpdateHandler`.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/TelegramBotService.cs` : 328–332

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ: проверка длины перед обращением
var parts = data.Split(':');
if (parts.Length < 3)
{
    _logger.LogWarning("Некорректный callback data: {Data}", data);
    return;
}
var action = parts[2];
```

---

### NEW-BUG-02 — `HttpClientHandler` не диспоузится при исключении

**Описание:**
В `CreateBotClient` создаётся `new HttpClientHandler()` и передаётся в `new HttpClient(handler, disposeHandler: true)`. Если между созданием `handler` и созданием `HttpClient` бросается исключение, `handler` остаётся неосвобождённым.

**Файл:** `Backend/Barkfluff.AdminPanel/Services/TelegramBotService.cs` : 58–68

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ: try/catch с явным Dispose
HttpClientHandler? handler = null;
try
{
    handler = new HttpClientHandler { /* ... */ };
    return new HttpClient(handler, disposeHandler: true);
}
catch
{
    handler?.Dispose();
    throw;
}
```

---

### NEW-CODE-01 — Нет валидации длины при rename токена

**Описание:**
`dto.Name` в эндпоинте переименования токена не имеет ограничения по длине. Можно сохранить строку в 10 МБ — она поместится в LiteDB и сломает UI/выдачу токенов.

**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/AuthEndpoints.cs` : 118–159

**Вариант решения:**

Добавить `[StringLength(64)]` в DTO или проверку в эндпоинте:

```csharp
if (string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 64)
    return Results.BadRequest("Name must be 1..64 chars");
```

---

### NEW-CODE-02 — `GetIpAddress` может вернуть пустую строку

**Описание:**
`X-Forwarded-For` после `Split(',').FirstOrDefault()?.Trim()` может вернуть пустую строку (при заголовке `", "`), но дальше код трактует её как валидный IP. Тесно связано с [NEW-SEC-02](#new-sec-02--ip-spoofing-через-x-forwarded-for).

**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/AuthEndpoints.cs` : 204–209

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ
var ip = forwarded?.Split(',').FirstOrDefault()?.Trim();
return string.IsNullOrWhiteSpace(ip)
    ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
    : ip;
```

---

## Сводная таблица

| ID | Категория | Серьёзность | Файл | Краткое описание |
|---|---|---|---|---|
| SEC-01 | 🔴 Безопасность | **Критично** | `appsettings.json:10` | Реальный Telegram BotToken в репозитории |
| SEC-02 | 🔴 Безопасность | **Критично** | `DockerService.cs:630` | Shell Injection в UpdateAdminPanelAsync |
| SEC-03 | 🔴 Безопасность | **Высокая** | `TokenAuthMiddleware.cs:36` | Нет rate limiting на /api/auth/* |
| SEC-04 | 🔴 Безопасность | **Высокая** | `TokenAuthMiddleware.cs:86` | Cookie без HttpOnly/Secure/SameSite |
| SEC-05 | 🔴 Безопасность | **Средняя** | `Program.cs:142` | UseHttpsRedirection без HTTPS конфигурации |
| SEC-06 | 🔴 Безопасность | **Средняя** | `AuthService.cs:95` | Legacy метод обходит проверку администратора |
| BUG-01 | 🟡 Баг | **Средняя** | `PendingAuthService.cs:13` | Timer не освобождается (IDisposable) |
| BUG-02 | 🟡 Баг | **Средняя** | `PendingAuthService.cs:76` | fire-and-forget Task без CancellationToken |
| BUG-03 | 🟡 Баг | **Средняя** | `TokenService.cs:50` | Двойной FindById на каждый запрос |
| BUG-04 | 🟡 Баг | **Низкая** | `TelegramBotService.cs:463` | Hardcoded срок жизни токена (3 дня) |
| BUG-05 | 🟡 Баг | **Низкая** | `TelegramBotService.cs:140` | StopAsync не ждёт завершения polling |
| BUG-06 | 🟡 Баг | **Низкая** | `S3BrowserService.cs:16` | bool _initialized без volatile |
| PERF-01 | 🔵 Перф | **Средняя** | `Program.cs:239` | Чтение HTML с диска на каждый запрос |
| PERF-02 | 🔵 Перф | **Низкая** | `DockerService.cs:216` | Dictionary создаётся на каждый вызов |
| PERF-03 | 🔵 Перф | **Низкая** | `DockerService.cs:63` | Весь список контейнеров ради одного |
| PERF-04 | 🔵 Перф | **Низкая** | `DockerService.cs:486` | Фиксированные задержки вместо health-check |
| CODE-01 | ⚪ Качество | **Низкая** | `AuthService.cs:151` | Nullable warning в ParseUserAgent |
| CODE-02 | ⚪ Качество | **Низкая** | `Program.cs:245` | Много классов в одном файле |
| CODE-03 | ⚪ Качество | **Средняя** | `Program.cs:139` | Нет глобального exception handler |
| CODE-04 | ⚪ Качество | **Низкая** | `Program.cs:265` | Публичное [Obsolete] свойство |
| NEW-SEC-01 | 🔴 Безопасность | **Средняя** | `S3BrowserEndpoints.cs:45` | Path traversal через `prefix` |
| NEW-SEC-02 | 🔴 Безопасность | **Средняя** | `AuthEndpoints.cs:204` | IP spoofing через `X-Forwarded-For` без trusted proxies |
| NEW-BUG-01 | 🟡 Баг | **Средняя** | `TelegramBotService.cs:328` | `IndexOutOfRangeException` при парсинге callback |
| NEW-BUG-02 | 🟡 Баг | **Низкая** | `TelegramBotService.cs:58` | `HttpClientHandler` не диспоузится при исключении |
| NEW-CODE-01 | ⚪ Качество | **Низкая** | `AuthEndpoints.cs:118` | Нет валидации длины при rename токена |
| NEW-CODE-02 | ⚪ Качество | **Низкая** | `AuthEndpoints.cs:204` | `GetIpAddress` может вернуть пустую строку |

---

*Ревизия 2026-05-18: исходный аудит — 17/20 проблем актуально, 3 частично, 0 закрыто. Добавлено 6 новых проблем. Приоритет: SEC-01 (отозвать BotToken), SEC-02 (RCE), SEC-03/SEC-04 (rate limit + cookie flags), затем BUG-01..06 и новые SEC.*
