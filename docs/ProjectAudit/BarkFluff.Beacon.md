# Аудит проекта: BarkFluff.Beacon

**Дата аудита:** _2026_  
**Ветка:** `dev`  
**Target Framework:** `net9.0`  
**Аудитор:** GitHub Copilot / BarkfluffAgent

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Оптимизация](#-оптимизация)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Отсутствие авторизации на gRPC-эндпоинте

**Проблема:**  
gRPC-метод `GetServerInfo` полностью открыт для любого клиента без какой-либо проверки авторизации. Эндпоинт возвращает конфигурации всех внутренних микросервисов (хосты, порты, TLS-статусы). Любой, кто имеет сетевой доступ к Beacon, может получить полную карту инфраструктуры.

**Конкретно:**  
Нет атрибута `[Authorize]` на методе и нет middleware авторизации в `Program.cs`.

**Файл:** `Backend/BarkFluff.Beacon/Host/BeaconApiService.cs` : строки 22–28  
**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строки 59–65

```csharp
// ❌ BeaconApiService.cs — нет никакой авторизации
public override Task<GetServerInfoResponse> GetServerInfo(
    GetServerInfoRequest request,
    ServerCallContext context)
{
    _metrics.Increment("server_info_requests");
    var command = new GetServerInfoCommand();
    return _mediator.Send(command); // ← любой может вызвать
}

// ❌ Program.cs — нет UseAuthorization() и нет политик
var app = builder.Build();
app.MapGrpcReflectionService();
app.UseRouting();
app.MapGrpcService<BeaconApiService>(); // ← не защищён
```

**Варианты решения:**

**Вариант A — Service Token авторизация:**
```csharp
// Program.cs — добавить политику и middleware
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ServicePolicy", policy =>
        policy.RequireClaim("token_type", "service"));
});

// ...
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization(); // ← добавить
app.MapGrpcService<BeaconApiService>();
```

```csharp
// BeaconApiService.cs — добавить атрибут
[Authorize(Policy = "ServicePolicy")] // ← защита метода
public override Task<GetServerInfoResponse> GetServerInfo(
    GetServerInfoRequest request,
    ServerCallContext context)
{
    _metrics.Increment("server_info_requests");
    return _mediator.Send(new GetServerInfoCommand());
}
```

---

### SEC-02 — Утечка внутренней инфраструктуры через GetServerInfoResponse

**Проблема:**  
`GetServerInfoCommandHandler` запрашивает у `ConfigurationApi` конфигурации **всех** семи микросервисов (Identity, Users, Files, Messages, Updates, Onliner, FastAuth) и возвращает их в ответе. Это раскрывает полную топологию инфраструктуры внешнему клиенту: хосты, порты, TLS-статусы.

**Конкретно:**  
Поле `Status = ServiceStatus.Healthy` всегда возвращается как `Healthy` — это фиктивные данные, которые вводят клиента в заблуждение и не несут реальной ценности.

**Файл:** `Backend/BarkFluff.Beacon/Features/GetServerInfo/GetServerInfoCommandHandler.cs` : строки 86–93, 117–119

```csharp
// ❌ Возвращаем хосты всех внутренних сервисов без проверки прав клиента
return new GetServerInfoResponse
{
    Files    = ParseService(ServiceId.Files,    filesSettings.Configurations.ToList()),
    Identity = ParseService(ServiceId.Identity, identitySettings.Configurations.ToList()),
    Users    = ParseService(ServiceId.Users,    usersSettings.Configurations.ToList()),
    // ... и так далее — полная карта инфраструктуры
};

// ❌ ParseService — статус всегда Healthy, данные фиктивные
return new Service
{
    Status     = ServiceStatus.Healthy, // ← не проверяется реально
    TlsEnabled = true                   // ← хардкод, не из конфига
};
```

**Варианты решения:**

```csharp
// ✅ Вариант: возвращать только публичные поля, без хостов
// Если клиент — внешний, убрать Endpoint из ответа proto
// Если клиент — внутренний сервис, проверять через авторизацию (см. SEC-01)

// ParseService — статус брать реально или убрать поле
return new Service
{
    Name = id.ToString(),
    // Endpoint — только если клиент авторизован как сервис
    Status = ServiceStatus.Unknown, // ← честнее, чем фиктивный Healthy
    TlsEnabled = true
};
```

---

### SEC-03 — Нешифрованные (HTTP) адреса зависимостей в конфигурации

**Проблема:**  
В `appsettings.json` адреса Navigator и ConfigurationService заданы по HTTP без шифрования. Трафик между микросервисами передаётся открыто, что критично для внутренних сетей с gRPC (gRPC по HTTP/2 без TLS — незашифрованный канал).

**Файл:** `Backend/BarkFluff.Beacon/appsettings.json` : строки 12–13

```json
// ❌ Нешифрованные адреса
{
  "NavigatorUrl": "http://localhost:7010",
  "ConfigurationServiceAddr": "http://localhost:7003"
}
```

**Варианты решения:**

```json
// ✅ Для production — HTTPS или gRPC с TLS
{
  "NavigatorUrl": "https://navigator.internal:443",
  "ConfigurationServiceAddr": "https://configuration.internal:443"
}
```

```csharp
// ✅ Для docker-среды (HTTP/2 без TLS — только явно разрешённый insecure):
// Program.cs
builder.Services.AddGrpcClient<NavigatorApi.NavigatorApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["NavigatorUrl"]!);
})
.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    // Только для dev/internal docker network:
    ServerCertificateCustomValidationCallback = 
        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
});
```

---

### SEC-04 — Отсутствие Rate Limiting

**Проблема:**  
Нет ограничений на количество запросов к `GetServerInfo`. Эндпоинт делает 7 синхронных (последовательных) gRPC-вызовов наружу — злоумышленник может вызвать каскадную нагрузку на ConfigurationApi путём флуда.

**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строка 63  
**Файл:** `Backend/BarkFluff.Beacon/Host/BeaconApiService.cs` : строка 22

```csharp
// ❌ Нет никакого rate limiting
app.MapGrpcService<BeaconApiService>(); // ← неограниченный доступ
```

**Варианты решения:**

```csharp
// ✅ Program.cs — добавить rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("grpc_beacon", limiter =>
    {
        limiter.PermitLimit         = 60;
        limiter.Window              = TimeSpan.FromMinutes(1);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit          = 5;
    });
});

// ...
app.UseRateLimiter();
```

---

### SEC-05 — AllowedHosts: "*" в production-конфигурации

**Проблема:**  
`"AllowedHosts": "*"` разрешает запросы с любого хоста. Для gRPC-сервиса это некритично само по себе, но в сочетании с отсутствием авторизации (SEC-01) создаёт открытую поверхность атаки.

**Файл:** `Backend/BarkFluff.Beacon/appsettings.json` : строка 8

```json
// ❌ Нет ограничения хостов
{
  "AllowedHosts": "*"
}
```

**Варианты решения:**

```json
// ✅ Ограничить конкретными хостами для production
{
  "AllowedHosts": "beacon.barkfluff.com;*.barkfluff.internal"
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — 7 последовательных await-вызовов вместо параллельных

**Проблема:**  
`GetServerInfoCommandHandler.Handle` выполняет 7 запросов к `ConfigurationApi` **последовательно** через отдельные `await`. Каждый вызов — отдельный gRPC round-trip. Суммарное время ответа = сумма всех 7 задержек. При задержке 10ms на запрос — это 70ms минимум, при 50ms — 350ms.

**Файл:** `Backend/BarkFluff.Beacon/Features/GetServerInfo/GetServerInfoCommandHandler.cs` : строки 33–66

```csharp
// ❌ Последовательные вызовы — каждый ждёт предыдущего
var identitySettings  = await _configurationApiClient.GetConfigurationAsync(...);
var usersSettings     = await _configurationApiClient.GetConfigurationAsync(...);
var filesSettings     = await _configurationApiClient.GetConfigurationAsync(...);
var messagesSettings  = await _configurationApiClient.GetConfigurationAsync(...);
var updatesSettings   = await _configurationApiClient.GetConfigurationAsync(...);
var onlinerSettings   = await _configurationApiClient.GetConfigurationAsync(...);
var fastAuthSettings  = await _configurationApiClient.GetConfigurationAsync(...);
// Время = latency × 7
```

**Варианты решения:**

```csharp
// ✅ Параллельные вызовы через Task.WhenAll — время = max(latency)
var tasks = new[]
{
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Identity  }).ResponseAsync,
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Users     }).ResponseAsync,
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Files     }).ResponseAsync,
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Messages  }).ResponseAsync,
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Updates   }).ResponseAsync,
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.Onliner   }).ResponseAsync,
    _configurationApiClient.GetConfigurationAsync(new GetConfigurationRequest { ServiceId = (int)ServiceId.FastAuth  }).ResponseAsync,
};

var results = await Task.WhenAll(tasks);

var (identitySettings, usersSettings, filesSettings,
     messagesSettings, updatesSettings, onlinerSettings, fastAuthSettings)
    = (results[0], results[1], results[2], results[3], results[4], results[5], results[6]);
```

---

### OPT-02 — Отсутствие кэширования конфигурации сервисов

**Проблема:**  
Каждый вызов `GetServerInfo` делает 7 gRPC-запросов к `ConfigurationApi`. Конфигурации сервисов — статичные данные, которые меняются редко. Нет никакого кэша — при высокой нагрузке это создаёт избыточный трафик на ConfigurationApi и высокую латентность.

**Файл:** `Backend/BarkFluff.Beacon/Features/GetServerInfo/GetServerInfoCommandHandler.cs` : строки 33–93

```csharp
// ❌ Каждый вызов Handle() заново запрашивает конфигурации
public async Task<GetServerInfoResponse> Handle(...)
{
    // 7 gRPC-вызовов при каждом запросе клиента
    var identitySettings = await _configurationApiClient.GetConfigurationAsync(...);
    // ...
}
```

**Варианты решения:**

```csharp
// ✅ Вариант: IMemoryCache с коротким TTL
public class GetServerInfoCommandHandler : IRequestHandler<GetServerInfoCommand, GetServerInfoResponse>
{
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);
    private const string CacheKey = "server_info_response";

    // ... конструктор

    public async Task<GetServerInfoResponse> Handle(GetServerInfoCommand request, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(CacheKey, out GetServerInfoResponse? cached) && cached is not null)
            return cached; // ← мгновенно, без gRPC-запросов

        var response = await BuildResponseAsync(cancellationToken);

        _cache.Set(CacheKey, response, CacheTtl);
        return response;
    }
}
```

---

### OPT-03 — Лишний `.ToList()` в ParseService при каждом вызове

**Проблема:**  
В `ParseService` каждый вызов `settings.Configurations.ToList()` создаёт новый `List<ConfigurationItem>` только для того, чтобы вызвать `FirstOrDefault` — который работает с любым `IEnumerable` и не требует материализации в List.

**Файл:** `Backend/BarkFluff.Beacon/Features/GetServerInfo/GetServerInfoCommandHandler.cs` : строки 86–92, 96

```csharp
// ❌ Лишняя аллокация List<T> перед FirstOrDefault
Files    = ParseService(ServiceId.Files,    filesSettings.Configurations.ToList()),
Identity = ParseService(ServiceId.Identity, identitySettings.Configurations.ToList()),
// ...

private Service ParseService(ServiceId id, List<ConfigurationItem> settings)
{
    var externalHost = settings
        .FirstOrDefault(x => x.Section == "ExternalEndpoint" && x.Key == "Host")?.Value;
    // List не нужен — FirstOrDefault работает с IEnumerable
}
```

**Варианты решения:**

```csharp
// ✅ Передавать IEnumerable — без лишней аллокации
Files    = ParseService(ServiceId.Files,    filesSettings.Configurations),
Identity = ParseService(ServiceId.Identity, identitySettings.Configurations),

// Изменить сигнатуру метода:
private Service ParseService(ServiceId id, IEnumerable<ConfigurationItem> settings)
{
    var list = settings as IList<ConfigurationItem> ?? settings.ToList(); // один ToList если нужно
    var externalHost = list.FirstOrDefault(x => x.Section == "ExternalEndpoint" && x.Key == "Host")?.Value;
    // ...
}
```

---

### OPT-04 — Создание нового DI-scope при каждой итерации в ServerRegistrationService

**Проблема:**  
`ServerRegistrationService` создаёт `IServiceScope` при **каждой** итерации (раз в 5 минут) и резолвит зависимости заново. `NavigatorApi.NavigatorApiClient` — это gRPC-клиент, зарегистрированный через `AddGrpcClient`, и он является singleton-friendly. Создание scope каждые 5 минут — избыточная операция.

**Файл:** `Backend/BarkFluff.Beacon/Features/RegisterServer/ServerRegistrationService.cs` : строки 29–33

```csharp
// ❌ Каждую итерацию создаём scope и резолвим зависимости заново
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        using var scope = _serviceProvider.CreateScope(); // ← каждые 5 минут
        var navigatorClient = scope.ServiceProvider.GetRequiredService<NavigatorApi.NavigatorApiClient>();
        var config          = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var serverProps     = scope.ServiceProvider.GetRequiredService<ServerPropsSettings>();
        var colorSettings   = scope.ServiceProvider.GetRequiredService<ServerColorSettings>();
        // ...
    }
}
```

**Варианты решения:**

```csharp
// ✅ Инжектировать зависимости напрямую в конструктор (они singleton/transient-safe)
public class ServerRegistrationService : BackgroundService
{
    private readonly NavigatorApi.NavigatorApiClient _navigatorClient;
    private readonly IConfiguration _config;
    private readonly ServerPropsSettings _serverProps;
    private readonly ServerColorSettings _colorSettings;

    public ServerRegistrationService(
        NavigatorApi.NavigatorApiClient navigatorClient,
        IConfiguration config,
        ServerPropsSettings serverProps,
        ServerColorSettings colorSettings,
        ILogger<ServerRegistrationService> logger,
        MetricsCollector metrics)
    {
        _navigatorClient = navigatorClient;
        _config          = config;
        _serverProps     = serverProps;
        _colorSettings   = colorSettings;
        // ...
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Используем поля напрямую — без создания scope
            await RegisterAsync(stoppingToken);
            await Task.Delay(_interval, stoppingToken);
        }
    }
}
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — Двойная регистрация MediatR

**Проблема:**  
`AddMediatR` вызывается **дважды** подряд с одинаковыми параметрами в `Program.cs`. Это приводит к двойной регистрации всех обработчиков в DI-контейнере, что может вызвать непредсказуемое поведение при диспетчеризации команд через MediatR (двойное выполнение обработчиков, исключения при резолве).

**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строки 44 и 46

```csharp
// ❌ MediatR регистрируется дважды — дубликат строки
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>()); // строка 44
builder.Services.AddSettings<ServerColorSettings>(builder.Configuration, "ServerColor");
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>()); // строка 46 — лишняя!
```

**Варианты решения:**

```csharp
// ✅ Убрать дубликат — одна регистрация
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
builder.Services.AddSettings<ServerColorSettings>(builder.Configuration, "ServerColor");
builder.Services.AddSettings<ServerPropsSettings>(builder.Configuration, "ServerProps");
// AddMediatR больше не повторяется
```

---

### BUG-02 — Статус сервисов всегда Healthy (фиктивные данные)

**Проблема:**  
В `ParseService` поле `Status` жёстко установлено в `ServiceStatus.Healthy` без какой-либо реальной проверки доступности сервиса. Клиенты, получающие `GetServerInfoResponse`, будут видеть все сервисы "живыми" даже если они упали. Это может вводить в заблуждение системы мониторинга и UI.

**Файл:** `Backend/BarkFluff.Beacon/Features/GetServerInfo/GetServerInfoCommandHandler.cs` : строки 117–119

```csharp
// ❌ Статус никогда не меняется — всегда Healthy
return new Service
{
    Name       = id.ToString(),
    Endpoint   = new ServiceEndpoint { Host = externalHost, Port = 443 },
    Status     = ServiceStatus.Healthy, // ← хардкод, реальной проверки нет
    TlsEnabled = true
};
```

**Варианты решения:**

```csharp
// ✅ Вариант A — честный Unknown пока нет health-check логики
return new Service
{
    Name       = id.ToString(),
    Endpoint   = new ServiceEndpoint { Host = externalHost, Port = 443 },
    Status     = ServiceStatus.Unknown, // ← честнее, чем фиктивный Healthy
    TlsEnabled = true
};

// ✅ Вариант B — реальный health-check (если конфигурация его содержит)
var isHealthy = settings.Any(x => x.Key == "HealthStatus" && x.Value == "Healthy");
return new Service
{
    Status = isHealthy ? ServiceStatus.Healthy : ServiceStatus.Unknown,
    // ...
};
```

---

### BUG-03 — Нет начальной задержки перед первой регистрацией в Navigator

**Проблема:**  
`ServerRegistrationService` начинает регистрацию в Navigator **немедленно** при старте приложения, не давая сервису полностью инициализироваться. Если Navigator недоступен в момент старта — первая попытка провалится, залогируется ошибка, и следующая попытка будет лишь через 5 минут. Это создаёт "слепое окно" в 5 минут после старта.

**Файл:** `Backend/BarkFluff.Beacon/Features/RegisterServer/ServerRegistrationService.cs` : строки 23–25

```csharp
// ❌ Нет начальной задержки — попытка сразу при старте
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            // Первая попытка немедленно — сервис мог не стартовать полностью
            await navigatorClient.RegisterServerAsync(request, cancellationToken: stoppingToken);
        }
        // ...
        await Task.Delay(_interval, stoppingToken); // ← следующая попытка через 5 минут
    }
}
```

**Варианты решения:**

```csharp
// ✅ Добавить короткую начальную задержку + retry-логику при старте
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // Дать приложению 5 секунд на полную инициализацию
    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await RegisterAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке RegisterServer, следующая попытка через {Interval}", _interval);
        }

        await Task.Delay(_interval, stoppingToken);
    }
}
```

---

### BUG-04 — CancellationToken не передаётся в Handle из gRPC-контекста

**Проблема:**  
`BeaconApiService.GetServerInfo` не передаёт `CancellationToken` из `ServerCallContext` в `_mediator.Send()`. Если gRPC-клиент отменил запрос (таймаут, disconnect), обработчик продолжит выполнять все 7 gRPC-запросов к ConfigurationApi впустую.

**Файл:** `Backend/BarkFluff.Beacon/Host/BeaconApiService.cs` : строки 22–28

```csharp
// ❌ CancellationToken из контекста игнорируется
public override Task<GetServerInfoResponse> GetServerInfo(
    GetServerInfoRequest request,
    ServerCallContext context)
{
    _metrics.Increment("server_info_requests");
    var command = new GetServerInfoCommand();
    return _mediator.Send(command); // ← нет context.CancellationToken!
}
```

**Варианты решения:**

```csharp
// ✅ Передать CancellationToken из gRPC контекста
public override Task<GetServerInfoResponse> GetServerInfo(
    GetServerInfoRequest request,
    ServerCallContext context)
{
    _metrics.Increment("server_info_requests");
    var command = new GetServerInfoCommand();
    return _mediator.Send(command, context.CancellationToken); // ← добавить токен
}
```

---

### BUG-05 — Порт 443 жёстко захардкожен в ParseService и ServerRegistrationService

**Проблема:**  
Порт `443` захардкожен в двух местах: в `ParseService` и в `ServerRegistrationService`. Если инфраструктура изменится (нестандартный порт, staging-среда), эти значения не будут корректными. В `ServerRegistrationService` порт берётся из конфигурации только для хоста, но не для порта.

**Файл:** `Backend/BarkFluff.Beacon/Features/GetServerInfo/GetServerInfoCommandHandler.cs` : строка 116  
**Файл:** `Backend/BarkFluff.Beacon/Features/RegisterServer/ServerRegistrationService.cs` : строка 54

```csharp
// ❌ GetServerInfoCommandHandler.cs — порт всегда 443
return new Service
{
    Endpoint = new ServiceEndpoint
    {
        Host = externalHost,
        Port = 443 // ← захардкожен
    }
};

// ❌ ServerRegistrationService.cs — порт всегда 443
BeaconUri = new ServiceEndpoint
{
    Host = externalHost,
    Port = 443 // ← захардкожен
}
```

**Варианты решения:**

```csharp
// ✅ Брать порт из конфигурации
// В appsettings.json:
// "ExternalEndpoint": { "Host": "beacon.barkfluff.com", "Port": 443 }

var portStr = settings.FirstOrDefault(x => x.Section == "ExternalEndpoint" && x.Key == "Port")?.Value;
var port    = int.TryParse(portStr, out var p) ? p : 443; // fallback на 443

return new Service
{
    Endpoint = new ServiceEndpoint { Host = externalHost, Port = port }
};
```

---

### BUG-06 — Незавершённая логика AccountsCount

**Проблема:**  
В `ServerRegistrationService` поле `AccountsCount` жёстко установлено в `0` с комментарием `// Можно доработать, если нужно`. Navigator получает заведомо неверное количество аккаунтов при каждой регистрации.

**Файл:** `Backend/BarkFluff.Beacon/Features/RegisterServer/ServerRegistrationService.cs` : строка 50

```csharp
// ❌ Заглушка — реальные данные никогда не отправляются
var serverInfo = new ServerInfo
{
    // ...
    AccountsCount = 0, // Можно доработать, если нужно ← TODO без тикета
};
```

**Варианты решения:**

```csharp
// ✅ Вариант A — убрать поле если оно не нужно (проверить proto)
// ✅ Вариант B — запрашивать из сервиса Users или хранить счётчик локально

// Например, через IServiceProvider получить IUsersCountProvider:
var usersCountProvider = scope.ServiceProvider.GetService<IUsersCountProvider>();
var accountsCount = usersCountProvider is not null
    ? await usersCountProvider.GetCountAsync(stoppingToken)
    : 0;

var serverInfo = new ServerInfo
{
    AccountsCount = accountsCount // ← реальные данные
};
```

---

## 🔵 Прочее / Качество кода

---

### MISC-01 — Сломанная кодировка в комментарии (Program.cs)

**Проблема:**  
Комментарий к классу `Program` содержит битые символы вместо кириллицы: `/// ����� ����� � ����������`. Это артефакт неправильной кодировки файла. Комментарий нечитаем.

**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строки 12–14

```csharp
// ❌ Битая кодировка
/// <summary>
/// ����� ����� � ����������
/// </summary>
public class Program
```

**Варианты решения:**

```csharp
// ✅ Исправить комментарий (или убрать — Program.cs очевиден)
/// <summary>
/// Точка входа в приложение BarkFluff.Beacon.
/// </summary>
public class Program
```

---

### MISC-02 — Переменная окружения CONFIGURATION_SERVICE_URL не используется в коде

**Проблема:**  
В `launchSettings.json` задана переменная окружения `CONFIGURATION_SERVICE_URL`, однако в `Program.cs` адрес Configuration Service читается из `builder.Configuration["ConfigurationServiceAddr"]`. Переменная окружения никогда не применяется, что вводит в заблуждение разработчиков при настройке среды.

**Файл:** `Backend/BarkFluff.Beacon/Properties/launchSettings.json` : строки 11, 21  
**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строка 55

```json
// ❌ launchSettings.json — переменная задана, но не используется
"environmentVariables": {
  "CONFIGURATION_SERVICE_URL": "https://configuration.barkfluff.com:443"
}
```

```csharp
// ❌ Program.cs — читает другой ключ конфигурации
builder.Services.AddGrpcClient<ConfigurationApi.ConfigurationApiClient>(o =>
{
    o.Address = new Uri(builder.Configuration["ConfigurationServiceAddr"]); // ← не из env var
});
```

**Варианты решения:**

```csharp
// ✅ Либо унифицировать ключи конфигурации:
// В launchSettings.json переименовать в "ConfigurationServiceAddr"
// Либо в Program.cs читать через Environment:

var configAddr = Environment.GetEnvironmentVariable("CONFIGURATION_SERVICE_URL")
    ?? builder.Configuration["ConfigurationServiceAddr"]
    ?? throw new InvalidOperationException("ConfigurationServiceAddr не задан");

builder.Services.AddGrpcClient<ConfigurationApi.ConfigurationApiClient>(o =>
{
    o.Address = new Uri(configAddr);
});
```

---

### MISC-03 — Отсутствие Health Checks эндпоинта

**Проблема:**  
В сервисе нет endpoint'а `/health` или gRPC Health Check Protocol. Docker / Kubernetes не может корректно определить готовность и живость сервиса. При деплое в K8s без `livenessProbe` / `readinessProbe` сервис не будет автоматически перезапускаться при зависании.

**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строки 59–65

```csharp
// ❌ Нет health check — платформа не знает о состоянии сервиса
var app = builder.Build();
app.MapGrpcReflectionService();
app.UseRouting();
app.MapGrpcService<BeaconApiService>();
// Нет app.MapHealthChecks("/health")
```

**Варианты решения:**

```csharp
// ✅ Добавить health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddGrpcHealthChecks(); // из пакета Grpc.HealthCheck

// ...
var app = builder.Build();
app.MapGrpcService<BeaconApiService>();
app.MapGrpcHealthChecksService(); // ← gRPC health protocol
app.MapHealthChecks("/health");   // ← HTTP health для K8s
```

---

### MISC-04 — Отсутствие трассировки (OpenTelemetry Tracing)

**Проблема:**  
В сервисе есть метрики (`MetricsCollector`), но нет трассировки (traces). Диагностика проблем с latency между микросервисами без distributed tracing крайне затруднена. `GetServerInfoCommandHandler` делает 7 внешних вызовов — без spans невозможно понять, где именно возникает задержка.

**Файл:** `Backend/BarkFluff.Beacon/Program.cs` : строка 43

```csharp
// ❌ Только метрики, трассировки нет
builder.Services.AddBarkFluffMetrics("BarkFluff.Beacon");
// Нет AddBarkFluffTracing() или аналога
```

**Варианты решения:**

```csharp
// ✅ Добавить OpenTelemetry tracing (если есть shared extension):
builder.Services.AddBarkFluffMetrics("BarkFluff.Beacon");
builder.Services.AddBarkFluffTracing("BarkFluff.Beacon"); // ← добавить

// Или через OpenTelemetry напрямую:
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddGrpcClientInstrumentation()
        .AddOtlpExporter());
```

---

### MISC-05 — Non-nullable поля конфигурации без инициализации (NullReferenceException)

**Проблема:**  
`ServerColorSettings` и `ServerPropsSettings` объявляют свойства типа `string` без `?` и без значений по умолчанию. При отсутствии секции в конфигурации они будут `null`, несмотря на то что Nullable context включён (`<Nullable>enable</Nullable>`). Компилятор предупреждает, но присвоение null в рантайме не предотвращается.

**Файл:** `Backend/BarkFluff.Beacon/Configurations/ServerColorSettings.cs` : строки 5–9  
**Файл:** `Backend/BarkFluff.Beacon/Configurations/ServerPropsSettings.cs` : строки 5–11

```csharp
// ❌ Non-nullable string без инициализации — null в рантайме при отсутствии конфига
public class ServerColorSettings
{
    public string Lite { get; set; }  // ← CS8618: может быть null
    public string Main { get; set; }
    public string Hard { get; set; }
}
```

**Варианты решения:**

```csharp
// ✅ Вариант A — required + init (C# 11+)
public class ServerColorSettings
{
    public required string Lite { get; init; }
    public required string Main { get; init; }
    public required string Hard { get; init; }
}

// ✅ Вариант B — nullable + значения по умолчанию
public class ServerColorSettings
{
    public string Lite { get; set; } = string.Empty;
    public string Main { get; set; } = string.Empty;
    public string Hard { get; set; } = string.Empty;
}
```

---

## Сводная таблица

| ID | Категория | Название | Приоритет |
|----|-----------|----------|-----------|
| SEC-01 | 🔴 Безопасность | Отсутствие авторизации на gRPC-эндпоинте | **Critical** |
| SEC-02 | 🔴 Безопасность | Утечка конфигураций всех сервисов | **High** |
| SEC-03 | 🔴 Безопасность | HTTP-адреса зависимостей (нет TLS) | **High** |
| SEC-04 | 🔴 Безопасность | Отсутствие Rate Limiting | **Medium** |
| SEC-05 | 🔴 Безопасность | AllowedHosts: "*" | **Low** |
| OPT-01 | 🟡 Оптимизация | 7 последовательных await вместо Task.WhenAll | **High** |
| OPT-02 | 🟡 Оптимизация | Нет кэширования конфигурации | **Medium** |
| OPT-03 | 🟡 Оптимизация | Лишний .ToList() в ParseService | **Low** |
| OPT-04 | 🟡 Оптимизация | Лишнее создание DI-scope каждые 5 минут | **Low** |
| BUG-01 | 🟠 Баги | Двойная регистрация MediatR | **High** |
| BUG-02 | 🟠 Баги | Status всегда Healthy (фиктивно) | **Medium** |
| BUG-03 | 🟠 Баги | Нет начальной задержки в ServerRegistrationService | **Medium** |
| BUG-04 | 🟠 Баги | CancellationToken не передаётся в MediatR | **Medium** |
| BUG-05 | 🟠 Баги | Порт 443 захардкожен в двух местах | **Medium** |
| BUG-06 | 🟠 Баги | AccountsCount всегда 0 (незавершённая логика) | **Low** |
| MISC-01 | 🔵 Прочее | Битая кодировка в комментарии | **Low** |
| MISC-02 | 🔵 Прочее | CONFIGURATION_SERVICE_URL не используется | **Low** |
| MISC-03 | 🔵 Прочее | Нет Health Checks эндпоинта | **Medium** |
| MISC-04 | 🔵 Прочее | Нет OpenTelemetry Tracing | **Low** |
| MISC-05 | 🔵 Прочее | Non-nullable поля конфигурации без инициализации | **Medium** |
