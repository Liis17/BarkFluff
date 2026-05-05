# Аудит проекта: BarkFluff.GrpcServer

> **Дата аудита:** 2026-05-06
> **Проект:** `Backend/BarkFluff.GrpcServer`
> **Target Framework:** `net9.0`
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Оптимизация](#-оптимизация)
- [🟠 Баги](#-баги)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Доверие клиентскому IP без валидации

**Проблема / Описание:**
Сервер принимает IP-адрес из gRPC-метаданных `x-ip-address`, переданных клиентом, и ставит их на первое место по приоритету. Любой клиент может подделать произвольный IP, например `127.0.0.1` или IP администратора.

**Конкретно в чём проблема:**
Первым в цепочке `ResolveIpAddress` стоит значение из метаданных, отправленных клиентом — это данные, которым нельзя доверять без верификации источника.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs` : строки 63–71

```csharp
private string? ResolveIpAddress(Metadata metadata, HttpContext? httpContext)
{
    // ❌ ПРОБЛЕМА: клиент сам передаёт свой IP — это поле можно подделать
    var clientIp = GetMetadataValue(metadata, MetadataKeys.IpAddress);
    if (!string.IsNullOrWhiteSpace(clientIp))
    {
        _logger.LogDebug("IP определён из метаданных клиента: {IpAddress}", clientIp);
        return clientIp; // ← любой клиент может написать сюда что угодно
    }
    // ...
}
```

**Варианты решения:**

1. Использовать клиентский IP только как дополнительный атрибут (для логов), но не как основной источник истины.
2. Переместить `RemoteIpAddress` на первое место приоритета, а клиентские метаданные — последними или вовсе убрать.
3. Валидировать формат IP (через `IPAddress.TryParse`) и разрешать доверять метаданным только для внутренних адресов / доверенных сетей.

```csharp
private string? ResolveIpAddress(Metadata metadata, HttpContext? httpContext)
{
    // ✅ Сначала — реальный IP соединения (нельзя подделать на уровне TCP)
    var remoteIp = httpContext?.Connection?.RemoteIpAddress;
    if (remoteIp != null)
    {
        if (remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        _logger.LogDebug("IP из соединения: {IpAddress}", remoteIp);
        return remoteIp.ToString();
    }

    // ✅ Затем — заголовки прокси (только если сервер за reverse proxy)
    var forwardedFor = httpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
    {
        var firstIp = forwardedFor.Split(',')[0].Trim();
        if (System.Net.IPAddress.TryParse(firstIp, out _)) // ← валидируем формат
            return firstIp;
    }

    // ℹ️ Клиентские метаданные — только для аудита, не как источник истины
    return GetMetadataValue(metadata, MetadataKeys.IpAddress);
}
```

---

### SEC-02 — Утечка внутреннего сообщения исключения в gRPC-ответ

**Проблема / Описание:**
При необработанном исключении в `ServerExceptionInterceptor` в поле `detail` gRPC-статуса передаётся `ex.Message` — внутреннее сообщение исключения. Это может содержать чувствительные данные: пути к файлам, строки подключения к БД, имена внутренних классов.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` : строка 69

```csharp
catch (Exception ex)
{
    _metrics?.Increment("grpc_requests_errors");

    _logger.LogError(ex, "КРИТИЧЕСКАЯ ОШИБКА при вызове {Method}...", methodName, ex.GetType().Name);

    var baseExcetion = new BaseGrpcException();
    var trailers = new Metadata { { "x-error-code", baseExcetion.ErrorCode } };

    // ❌ ПРОБЛЕМА: ex.Message отправляется клиенту — может содержать внутренние детали
    throw new RpcException(new Status(StatusCode.Unknown, ex.Message), trailers);
}
```

**Варианты решения:**

Заменить `ex.Message` на обобщённое сообщение, детали оставить только в логах.

```csharp
catch (Exception ex)
{
    _metrics?.Increment("grpc_requests_errors");

    _logger.LogError(ex, "КРИТИЧЕСКАЯ ОШИБКА при вызове {Method}. Тип: {ExceptionType}",
        methodName, ex.GetType().Name);

    var baseException = new BaseGrpcException();
    var trailers = new Metadata { { "x-error-code", baseException.ErrorCode } };

    // ✅ Клиент получает только обобщённое сообщение без внутренних деталей
    throw new RpcException(new Status(StatusCode.Internal, baseException.ErrorMessage), trailers);
}
```

---

### SEC-03 — Секретный ключ JWT читается напрямую без проверки на null/пустоту

**Проблема / Описание:**
`configuration["JwtSettings:SecretKey"]!` — использование `!` (null-forgiving оператора) вместо явной проверки. Если ключ отсутствует в конфигурации, `Encoding.ASCII.GetBytes(null)` выбросит `ArgumentNullException` при старте, и сервер не запустится с неинформативной ошибкой. Хуже: если ключ — пустая строка, JWT будет подписан пустым ключом, что делает подпись предсказуемой.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs` : строка 26

```csharp
// ❌ ПРОБЛЕМА: null-forgiving оператор скрывает отсутствие ключа;
// пустая строка создаёт слабый/предсказуемый ключ подписи
IssuerSigningKey = new SymmetricSecurityKey(
    Encoding.ASCII.GetBytes(configuration["JwtSettings:SecretKey"]!)),
```

**Варианты решения:**

```csharp
// ✅ Явная проверка с информативным сообщением об ошибке
var secretKey = configuration["JwtSettings:SecretKey"];
if (string.IsNullOrWhiteSpace(secretKey))
    throw new InvalidOperationException(
        "JwtSettings:SecretKey не задан в конфигурации. " +
        "Укажите переменную окружения или значение в appsettings.json.");

if (secretKey.Length < 32)
    throw new InvalidOperationException(
        $"JwtSettings:SecretKey слишком короткий ({secretKey.Length} символов). " +
        "Минимальная длина — 32 символа для HMAC-SHA256.");

IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
// ✅ Используем UTF8, а не ASCII — ASCII обрезает символы > 127
```

---

### SEC-04 — TLS-пароль хранится в открытом виде в конфигурации

**Проблема / Описание:**
`TlsSettings.Password` — обычная строка, которая может попасть в `appsettings.json`, переменные окружения или логи конфигурации. Пароль к сертификату — чувствительные данные и должен считываться из защищённого хранилища.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Settings/TlsSettings.cs` : строки 5–7

```csharp
public class TlsSettings
{
    public string Filename { get; set; }
    // ❌ ПРОБЛЕМА: пароль в открытом виде в модели конфигурации
    public string Password { get; set; }
}
```

**Варианты решения:**

1. Загружать пароль через `SecretManager` / `Azure Key Vault` / переменную окружения с пометкой `[Sensitive]`.
2. Не хранить пароль в `appsettings.json` вообще — только через `DOTNET_` / `TLS__PASSWORD` env-переменные.
3. Рассмотреть использование сертификата без пароля в production (с ограниченными правами на файл).

```csharp
// ✅ Явное указание что поле чувствительное + валидация при старте
public class TlsSettings
{
    public string Filename { get; set; } = string.Empty;

    /// <summary>
    /// Пароль к сертификату. Загружать ТОЛЬКО из переменных окружения или Secret Manager,
    /// не хранить в appsettings.json в открытом виде.
    /// </summary>
    public string Password { get; set; } = string.Empty;
}
```

---

### SEC-05 — `x-auth-token` передаётся без проверки формата перед присвоением

**Проблема / Описание:**
В `OnMessageReceived` значение заголовка `x-auth-token` присваивается напрямую в `context.Token` без какой-либо валидации. Формат не проверяется — при определённых конфигурациях это может вызвать необработанные исключения внутри библиотеки `JwtBearer`.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs` : строки 37–45

```csharp
OnMessageReceived = context =>
{
    if (context.Request.Headers.TryGetValue("x-auth-token", out var token))
    {
        // ❌ Нет проверки: пустая строка, whitespace, слишком длинное значение
        context.Token = token;
    }
    return Task.CompletedTask;
},
```

**Варианты решения:**

```csharp
OnMessageReceived = context =>
{
    if (context.Request.Headers.TryGetValue("x-auth-token", out var token))
    {
        var tokenValue = token.ToString().Trim();
        // ✅ Присваиваем только если значение непустое и имеет разумный размер
        if (!string.IsNullOrEmpty(tokenValue) && tokenValue.Length <= 8192)
        {
            context.Token = tokenValue;
        }
    }
    return Task.CompletedTask;
},
```

---

## 🟡 Оптимизация

---

### OPT-01 — `GetMetadataValue` использует LINQ `FirstOrDefault` в горячем пути

**Проблема / Описание:**
Метод `GetMetadataValue` вызывается 6 раз на каждый входящий gRPC-запрос. Внутри используется `metadata.FirstOrDefault(m => m.Key.Equals(...))` — линейный поиск O(n) по всем заголовкам плюс выделение делегата. При высоком RPS это создаёт нагрузку на GC.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs` : строки 107–116

```csharp
private string? GetMetadataValue(Metadata metadata, string key)
{
    // ❌ LINQ с лямбдой на каждый вызов — 6 вызовов × N заголовков на запрос
    var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    var base64 = entry?.Value;

    if (string.IsNullOrEmpty(base64))
        return null;

    return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
```

**Варианты решения:**

```csharp
private string? GetMetadataValue(Metadata metadata, string key)
{
    // ✅ Прямой цикл без LINQ — меньше аллокаций, быстрее
    foreach (var entry in metadata)
    {
        if (!entry.IsBinary && entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            var base64 = entry.Value;
            if (string.IsNullOrEmpty(base64))
                return null;

            // ✅ Span-based декодирование уменьшает аллокации
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
    }
    return null;
}
```

---

### OPT-02 — `MetricsCollector.Add` использует лямбду без `static`

**Проблема / Описание:**
Метод `Add` использует лямбду `(_, oldValue) => oldValue + value` без ключевого слова `static`, что вызывает захват `value` из замыкания и создаёт объект на каждый вызов. В `Increment` это уже исправлено через `static`.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Metrics/MetricsCollector.cs` : строка 27

```csharp
public void Add(string metricName, long value)
{
    // ❌ Лямбда без static — захват value создаёт heap-аллокацию на каждый вызов
    _counters.AddOrUpdate(metricName, value, (_, oldValue) => oldValue + value);
}
```

**Варианты решения:**

```csharp
public void Add(string metricName, long value)
{
    // ✅ Используем перегрузку с factoryArgument чтобы избежать замыкания
    _counters.AddOrUpdate(metricName, value, static (_, oldValue, addValue) => oldValue + addValue, value);
}
```

---

### OPT-03 — `SnapshotAndReset` итерируется по ключам отдельно от значений

**Проблема / Описание:**
В `SnapshotAndReset` сначала берётся `_counters.Keys`, затем для каждого ключа вызывается `TryRemove`. Это два прохода: сбор ключей (аллокация IEnumerable) + удаление. Между итерацией и удалением другой поток может добавить запись — она будет потеряна.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Metrics/MetricsCollector.cs` : строки 42–50

```csharp
foreach (var key in _counters.Keys) // ❌ Снимок ключей — отдельная коллекция
{
    var value = _counters.TryRemove(key, out var v) ? v : 0;
    if (value != 0)
        snapshot[key] = value;
}
```

**Варианты решения:**

```csharp
// ✅ ToArray() + TryRemove — атомарно для каждой пары, меньше промежуточных аллокаций
foreach (var kvp in _counters.ToArray())
{
    if (_counters.TryRemove(kvp.Key, out var value) && value != 0)
        snapshot[kvp.Key] = value;
}
```

---

### OPT-04 — `MetricsReporterService` использует `DateTime.UtcNow` внутри логируемого объекта

**Проблема / Описание:**
В `LogInformation` передаётся анонимный объект с `Timestamp = DateTime.UtcNow`. Serilog и так добавляет временную метку к каждому событию. Это создаёт дублирование данных и дополнительную аллокацию анонимного типа каждые 5 секунд.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Metrics/MetricsReporterService.cs` : строки 35–36

```csharp
// ❌ Анонимный объект + дублирующийся Timestamp
_logger.LogInformation("ServiceMetrics {@Metrics}",
    new { ServiceName = _serviceName, Metrics = snapshot, Timestamp = DateTime.UtcNow });
```

**Варианты решения:**

```csharp
// ✅ Убираем дублирующийся Timestamp, используем структурированное логирование
_logger.LogInformation(
    "ServiceMetrics {ServiceName} {@Metrics}",
    _serviceName,
    snapshot);
```

---

### OPT-05 — `GrpcChannel` в `LoadConfiguration` не диспозится

**Проблема / Описание:**
В `LoadConfiguration` создаётся `GrpcChannel` и gRPC-клиент для одного синхронного вызова. Канал не освобождается (`Dispose`), что приводит к удержанию HTTP/2-соединения и связанных ресурсов до финализации GC.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/WebApplicationBuilderExtensions.cs` : строки 69–72

```csharp
// ❌ Channel создаётся, используется один раз, но никогда не Dispose'ится
var channel = GrpcChannel.ForAddress(configurationServiceAddress);
var configurationApiClient = new ConfigurationApi.ConfigurationApiClient(channel);
var config = configurationApiClient.GetConfiguration(...);
```

**Варианты решения:**

```csharp
// ✅ using — автоматический Dispose после использования
using var channel = GrpcChannel.ForAddress(configurationServiceAddress);
var configurationApiClient = new ConfigurationApi.ConfigurationApiClient(channel);
var config = configurationApiClient.GetConfiguration(
    new GetConfigurationRequest { ServiceId = (int)serviceId });
```

---

## 🟠 Баги

---

### BUG-01 — Опечатка в имени переменной: `baseExcetion` вместо `baseException`

**Проблема / Описание:**
В блоке `catch (Exception ex)` создаётся переменная с опечаткой в названии. Это не влияет на поведение, но снижает читаемость и может стать причиной путаницы при отладке и ревью.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` : строка 63

```csharp
// ❌ Опечатка: baseExcetion вместо baseException
var baseExcetion = new BaseGrpcException();

var trailers = new Metadata
{
    { "x-error-code", baseExcetion.ErrorCode }
};
```

**Варианты решения:**

```csharp
// ✅ Правильное именование
var baseException = new BaseGrpcException();

var trailers = new Metadata
{
    { "x-error-code", baseException.ErrorCode }
};
```

---

### BUG-02 — `UserContext.UserId` парсится через `long.Parse` без защиты от исключения

**Проблема / Описание:**
`long.Parse(... ?? "0")` выбросит `FormatException` если клейм `UserId` содержит нечисловое значение (повреждённый токен, ручная подделка). Это приведёт к необработанному исключению внутри конструктора Scoped-сервиса и падению запроса без информативного сообщения.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/UserContext.cs` : строка 23

```csharp
// ❌ Если клейм не является числом — FormatException без контекста
UserId = long.Parse(principal.FindFirst(IdentityClaims.UserId)?.Value ?? "0");
```

**Варианты решения:**

```csharp
// ✅ TryParse — безопасный разбор, 0 при ошибке
var userIdStr = principal.FindFirst(IdentityClaims.UserId)?.Value;
UserId = long.TryParse(userIdStr, out var parsedId) ? parsedId : 0;
```

---

### BUG-03 — `GetMetadataValue` может выбросить `FormatException` при невалидном Base64

**Проблема / Описание:**
Если клиент передаст метаданные, которые не являются валидным Base64 (ошибка кодирования, ручной запрос), `Convert.FromBase64String` выбросит `FormatException`. Исключение всплывёт в `RequestContextInterceptor`, что приведёт к падению всего запроса вместо игнорирования некорректного заголовка.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs` : строка 115

```csharp
private string? GetMetadataValue(Metadata metadata, string key)
{
    var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    var base64 = entry?.Value;

    if (string.IsNullOrEmpty(base64))
        return null;

    // ❌ FormatException если base64 — не валидная строка Base64
    return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
}
```

**Варианты решения:**

```csharp
private string? GetMetadataValue(Metadata metadata, string key)
{
    foreach (var entry in metadata)
    {
        if (!entry.IsBinary && entry.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            var base64 = entry.Value;
            if (string.IsNullOrEmpty(base64))
                return null;

            // ✅ Безопасная декодировка — возвращаем null при невалидном Base64
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            }
            catch (FormatException)
            {
                _logger.LogWarning("Некорректный Base64 в метаданных '{Key}': {Value}", key, base64);
                return null;
            }
        }
    }
    return null;
}
```

---

### BUG-04 — `TokenRevocationCleanupService` не логирует ошибки при очистке

**Проблема / Описание:**
`ExecuteAsync` вызывает `_cache.Cleanup()` без `try/catch`. Если в `Cleanup` возникнет исключение (например, при будущем изменении реализации кэша), `BackgroundService` завершится молча — без записи в лог и без перезапуска.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCleanupService.cs` : строки 17–24

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        // ❌ Нет try/catch — исключение убьёт BackgroundService без предупреждения
        _cache.Cleanup();
    }
}
```

**Варианты решения:**

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        try
        {
            _cache.Cleanup();
            _logger.LogDebug("TokenRevocationCache: очистка выполнена");
        }
        catch (Exception ex)
        {
            // ✅ Логируем, но не прерываем цикл — сервис продолжит работу
            _logger.LogError(ex, "Ошибка при очистке TokenRevocationCache");
        }
    }
}
```

---

### BUG-05 — `RunSettings.Host` объявлен, но нигде не используется

**Проблема / Описание:**
В `RunSettings` есть поле `Host`, однако в `SetRunningAddress` вместо него используется `ListenAnyIP` — т.е. сервер всегда слушает на всех интерфейсах, независимо от значения `Host`. Поле создаёт ложное ощущение что привязка к конкретному хосту работает.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Settings/RunSettings.cs` : строка 5; `Backend/BarkFluff.GrpcServer/WebApplicationBuilderExtensions.cs` : строки 34–43

```csharp
// RunSettings.cs
public string? Host { get; set; } // ❌ Объявлено, но игнорируется

// WebApplicationBuilderExtensions.cs
options.ListenAnyIP(runSettings.Port, listenOptions => // ← Host никогда не используется
{
    // ...
});
```

**Варианты решения:**

Вариант А — убрать неиспользуемое поле:
```csharp
public class RunSettings
{
    // ✅ Убрали Host, раз используется ListenAnyIP
    public int Port { get; set; }
    public int? Http1Port { get; set; }
    public TlsSettings? Tls { get; set; }
}
```

Вариант Б — реализовать использование Host:
```csharp
if (!string.IsNullOrWhiteSpace(runSettings.Host))
    options.Listen(System.Net.IPAddress.Parse(runSettings.Host), runSettings.Port, listenOptions => { ... });
else
    options.ListenAnyIP(runSettings.Port, listenOptions => { ... });
```

---

### BUG-06 — `LoadConfiguration` — синхронный gRPC-вызов на старте без таймаута и retry

**Проблема / Описание:**
Вызов `configurationApiClient.GetConfiguration(...)` — синхронный (блокирующий) gRPC-запрос без таймаута и без retry-логики. Если `ConfigurationService` недоступен при старте, приложение зависнет навсегда или упадёт с неинформативным исключением `RpcException`. В контейнерной среде (Docker Compose) зависимые сервисы могут стартовать позже.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/WebApplicationBuilderExtensions.cs` : строки 69–72

```csharp
// ❌ Блокирующий вызов без таймаута — повесит старт при недоступности сервиса
var channel = GrpcChannel.ForAddress(configurationServiceAddress);
var configurationApiClient = new ConfigurationApi.ConfigurationApiClient(channel);
var config = configurationApiClient.GetConfiguration(
    new GetConfigurationRequest { ServiceId = (int)serviceId });
```

**Варианты решения:**

```csharp
// ✅ Дедлайн + retry + using
using var channel = GrpcChannel.ForAddress(configurationServiceAddress);
var configurationApiClient = new ConfigurationApi.ConfigurationApiClient(channel);

const int maxRetries = 5;
for (int attempt = 1; attempt <= maxRetries; attempt++)
{
    try
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var config = configurationApiClient.GetConfiguration(
            new GetConfigurationRequest { ServiceId = (int)serviceId },
            deadline: deadline);
        // ... обработка config
        break;
    }
    catch (RpcException ex) when (attempt < maxRetries)
    {
        Console.WriteLine($"[LoadConfiguration] Попытка {attempt}/{maxRetries} неудачна: {ex.Status}. Повтор через 3с...");
        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}
```

---

## 🔵 Прочее / Качество кода

---

### QA-01 — `TlsSettings` имеет non-nullable поля без инициализаторов (CS8618)

**Проблема / Описание:**
`TlsSettings.Filename` и `TlsSettings.Password` объявлены как `string` без дефолтного значения и без `required`. При `Nullable enable` это вызывает предупреждение CS8618. Если конфигурация не содержит TLS-секции, значения будут `null` несмотря на non-nullable тип.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Settings/TlsSettings.cs` : строки 5–7

```csharp
public class TlsSettings
{
    public string Filename { get; set; }  // ❌ CS8618, нет инициализатора
    public string Password { get; set; }  // ❌ CS8618, нет инициализатора
}
```

**Варианты решения:**

```csharp
public class TlsSettings
{
    public required string Filename { get; init; }  // ✅ required — обязателен при привязке
    public required string Password { get; init; }  // ✅
}
```

---

### QA-02 — `MetricsReporterService` получает `serviceName` через конструктор вручную, а не через `IOptions`

**Проблема / Описание:**
`serviceName` передаётся через ручную фабрику в `AddBarkFluffMetrics`. Это нарушает DI-паттерн и усложняет тестирование. При изменении конфигурации потребуется изменять фабрику.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/SerilogExtensions.cs` : строки 53–57

```csharp
// ❌ Ручная фабрика — обходит стандартный DI
services.AddHostedService(sp =>
    new MetricsReporterService(
        sp.GetRequiredService<MetricsCollector>(),
        sp.GetRequiredService<ILogger<MetricsReporterService>>(),
        serviceName)); // ← жёстко захваченное значение из параметра метода
```

**Варианты решения:**

```csharp
// ✅ Выделить Options-класс
public class MetricsOptions
{
    public string ServiceName { get; set; } = string.Empty;
}

// В регистрации:
services.Configure<MetricsOptions>(o => o.ServiceName = serviceName);
services.AddSingleton<MetricsCollector>();
services.AddHostedService<MetricsReporterService>();

// В конструкторе MetricsReporterService:
public MetricsReporterService(
    MetricsCollector collector,
    ILogger<MetricsReporterService> logger,
    IOptions<MetricsOptions> options)
{
    _serviceName = options.Value.ServiceName;
    // ...
}
```

---

### QA-03 — `RequestContext` не является иммутабельным, несмотря на Scoped DI

**Проблема / Описание:**
`RequestContext` — Scoped-сервис, но все его свойства — settable публичные поля. Любой сервис в цепочке может случайно перезаписать IP, DeviceId или другие поля. Это особенно критично при логировании аудита.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContext.cs` : строки 3–16

```csharp
public class RequestContext
{
    public string? OperationSystem { get; set; }  // ❌ публичный setter
    public string? IpAddress { get; set; }        // ❌ публичный setter
    public string? DeviceName { get; set; }       // ❌ публичный setter
    // ...
}
```

**Варианты решения:**

```csharp
// ✅ Init-only свойства — устанавливаются один раз в интерцепторе
public class RequestContext
{
    public string? OperationSystem { get; init; }
    public string? IpAddress { get; init; }
    public string? DeviceName { get; init; }
    public string? AppName { get; init; }
    public string? AppVersion { get; init; }
    public string? DeviceId { get; init; }
}

// В интерцепторе — создаём новый объект и регистрируем через фабрику,
// либо используем метод инициализации:
// requestContext = new RequestContext { IpAddress = ..., DeviceName = ... };
```

---

### QA-04 — Опечатка в имени поля: `OperationSystem` вместо `OperatingSystem`

**Проблема / Описание:**
Имя свойства `OperationSystem` является опечаткой — правильное написание `OperatingSystem`. Это создаёт несоответствие с общепринятой терминологией и стандартным BCL-типом `System.OperatingSystem`.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContext.cs` : строка 5

```csharp
// ❌ Опечатка
public string? OperationSystem { get; set; }
```

**Варианты решения:**

```csharp
// ✅ Правильное название
public string? OperatingSystem { get; init; }
```

> ⚠️ При переименовании потребуется обновить все места использования:
> - `RequestContextInterceptor.cs` строка 35: `requestContext.OperationSystem = ...`

---

### QA-05 — `StatusCode.Unknown` вместо `StatusCode.Internal` для серверных ошибок

**Проблема / Описание:**
gRPC-конвенция определяет `StatusCode.Internal` для неожиданных серверных ошибок. `StatusCode.Unknown` семантически означает «статус неизвестен», что может сбить с толку клиентский код при обработке ошибок.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` : строка 69

```csharp
// ❌ Unknown — некорректный статус для серверной ошибки
throw new RpcException(new Status(StatusCode.Unknown, ex.Message), trailers);
```

**Варианты решения:**

```csharp
// ✅ Internal — правильный статус для необработанных серверных исключений
throw new RpcException(new Status(StatusCode.Internal, baseException.ErrorMessage), trailers);
```

---

*Документ сгенерирован на основе статического анализа исходного кода. Приоритет исправлений: 🔴 Безопасность → 🟠 Баги → 🟡 Оптимизация → 🔵 Качество.*
