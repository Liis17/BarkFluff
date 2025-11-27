# Navigator Microservice

## Назначение

Сервис Navigator отвечает за **обнаружение и регистрацию BarkFluff серверов** в системе. Он управляет:

- 🌐 Регистрацией BarkFluff серверов в глобальном реестре
- 🔍 Поиском доступных серверов для подключения
- 📋 Списком всех публичных серверов с метаданными
- 🏷️ Категоризацией серверов (публичный, приватный, региональный)
- ⏱️ Мониторингом доступности зарегистрированных серверов

**Порт**: 7010
**База данных**: PostgreSQL (`navigator_db`)
**Зависимости**: Configuration service

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Entity Framework Core**: ORM
- **PostgreSQL**: База данных реестра серверов
- **HttpClient**: Health checking серверов

## Архитектура

```
┌─────────────────────────────────────────────┐
│          Navigator Service                   │
├─────────────────────────────────────────────┤
│  ┌──────────────┐      ┌─────────────────┐ │
│  │  gRPC API    │─────►│  Server         │ │
│  │              │      │  Registry       │ │
│  └──────────────┘      └────────┬────────┘ │
│                                 │          │
│         ┌───────────────────────┘          │
│         │                                  │
│         ↓                                  │
│  ┌──────────────┐      ┌─────────────────┐ │
│  │ Health Check │      │   PostgreSQL    │ │
│  │   Service    │      │ (Servers DB)    │ │
│  └──────────────┘      └─────────────────┘ │
└─────────────────────────────────────────────┘

Global Registry:
┌────────────────────────────────────┐
│        Navigator Service            │
│  (Central Discovery Server)         │
└─────────────┬──────────────────────┘
              │
      ┌───────┼───────┬─────────┐
      │       │       │         │
┌─────▼──┐ ┌──▼───┐ ┌▼─────┐ ┌─▼──────┐
│Server 1│ │Server│ │Server│ │Server N│
│ (US)   │ │ (EU) │ │(Asia)│ │(Custom)│
└────────┘ └──────┘ └──────┘ └────────┘
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **RegisteredServers** | Реестр BarkFluff серверов |

### Основные сущности

#### RegisteredServer

```csharp
public class RegisteredServer
{
    public long Id { get; set; }
    public string Name { get; set; }             // Название сервера
    public string Endpoint { get; set; }         // gRPC endpoint (http://host:port)
    public string? Description { get; set; }     // Описание сервера
    public string? IconUrl { get; set; }         // URL иконки сервера
    public ServerType Type { get; set; }         // Public, Private, Regional
    public string? Region { get; set; }          // US, EU, Asia, etc.
    public int? UserCount { get; set; }          // Количество пользователей (опционально)
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public bool IsOnline { get; set; }           // Результат последнего health check
}
```

**Уникальный индекс**: `Endpoint` (один endpoint = один сервер)

### ServerType Enum

```csharp
public enum ServerType
{
    Public = 1,      // Открытый для всех
    Private = 2,     // Приватный (по приглашениям)
    Regional = 3     // Региональный
}
```

## Ключевые функции

### 1. Регистрация сервера

**gRPC Method**: `RegisterServer`

**Request**:
```protobuf
message RegisterServerRequest {
  string name = 1;              // Название сервера
  string endpoint = 2;          // http://host:port
  string description = 3;       // Описание (опционально)
  string icon_url = 4;          // URL иконки (опционально)
  ServerType type = 5;          // Public/Private/Regional
  string region = 6;            // Регион (опционально)
  int32 user_count = 7;         // Количество пользователей (опционально)
}
```

**Response**:
```protobuf
message RegisterServerResponse {
  int64 server_id = 1;          // ID зарегистрированного сервера
}
```

**Реализация** (Features/RegisterServer/RegisterServerCommandHandler.cs):
```csharp
public class RegisterServerCommandHandler
    : IRequestHandler<RegisterServerCommand, RegisterServerResponse>
{
    private readonly INavigatorStorage _storage;
    private readonly ILogger<RegisterServerCommandHandler> _logger;

    public async Task<RegisterServerResponse> Handle(
        RegisterServerCommand request,
        CancellationToken cancellationToken)
    {
        // Проверка существующего сервера
        var existingServer = await _storage.RegisteredServers
            .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);

        if (existingServer != null)
        {
            // Обновление существующего
            existingServer.Name = request.Name;
            existingServer.Description = request.Description;
            existingServer.IconUrl = request.IconUrl;
            existingServer.Type = request.Type;
            existingServer.Region = request.Region;
            existingServer.UserCount = request.UserCount;
            existingServer.LastHealthCheck = DateTime.UtcNow;
            existingServer.IsOnline = true;

            await _storage.SaveChangesAsync();

            _logger.LogInformation(
                "Updated server registration: {Name} ({Endpoint})",
                request.Name,
                request.Endpoint
            );

            return new RegisterServerResponse
            {
                ServerId = existingServer.Id
            };
        }

        // Создание нового сервера
        var server = new RegisteredServer
        {
            Name = request.Name,
            Endpoint = request.Endpoint,
            Description = request.Description,
            IconUrl = request.IconUrl,
            Type = request.Type,
            Region = request.Region,
            UserCount = request.UserCount,
            RegisteredAt = DateTime.UtcNow,
            LastHealthCheck = DateTime.UtcNow,
            IsOnline = true
        };

        await _storage.RegisteredServers.AddAsync(server, cancellationToken);
        await _storage.SaveChangesAsync();

        _logger.LogInformation(
            "Registered new server: {Name} ({Endpoint})",
            request.Name,
            request.Endpoint
        );

        return new RegisterServerResponse
        {
            ServerId = server.Id
        };
    }
}
```

**Использование**:
```csharp
// При старте Beacon service
var navigatorClient = new NavigatorApiClient(channel);

await navigatorClient.RegisterServerAsync(new RegisterServerRequest
{
    Name = configuration["ServerInfo:Name"],
    Endpoint = configuration["ServerInfo:Endpoint"],
    Description = configuration["ServerInfo:Description"],
    IconUrl = configuration["ServerInfo:IconUrl"],
    Type = ServerType.Public,
    Region = "US"
});
```

### 2. Список серверов

**gRPC Method**: `ListServers`

**Request**:
```protobuf
message ListServersRequest {
  ServerType? type = 1;         // Фильтр по типу (опционально)
  string region = 2;            // Фильтр по региону (опционально)
  bool only_online = 3;         // Только онлайн серверы
}
```

**Response**:
```protobuf
message ListServersResponse {
  repeated ServerInfo servers = 1;
}

message ServerInfo {
  int64 id = 1;
  string name = 2;
  string endpoint = 3;
  string description = 4;
  string icon_url = 5;
  ServerType type = 6;
  string region = 7;
  int32 user_count = 8;
  bool is_online = 9;
}
```

**Реализация** (Features/ListServers/ListServersQueryHandler.cs):
```csharp
public class ListServersQueryHandler
    : IRequestHandler<ListServersQuery, ListServersResponse>
{
    private readonly INavigatorStorage _storage;

    public async Task<ListServersResponse> Handle(
        ListServersQuery request,
        CancellationToken cancellationToken)
    {
        var query = _storage.RegisteredServers.AsQueryable();

        // Фильтры
        if (request.Type.HasValue)
        {
            query = query.Where(s => s.Type == request.Type.Value);
        }

        if (!string.IsNullOrEmpty(request.Region))
        {
            query = query.Where(s => s.Region == request.Region);
        }

        if (request.OnlyOnline)
        {
            query = query.Where(s => s.IsOnline);
        }

        var servers = await query
            .OrderByDescending(s => s.UserCount)  // Сортировка по популярности
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

        var response = new ListServersResponse();

        foreach (var server in servers)
        {
            response.Servers.Add(new ServerInfo
            {
                Id = server.Id,
                Name = server.Name,
                Endpoint = server.Endpoint,
                Description = server.Description ?? "",
                IconUrl = server.IconUrl ?? "",
                Type = server.Type,
                Region = server.Region ?? "",
                UserCount = server.UserCount ?? 0,
                IsOnline = server.IsOnline
            });
        }

        return response;
    }
}
```

**Использование на клиенте**:
```csharp
// Получение списка всех публичных серверов
var response = await navigatorClient.ListServersAsync(new ListServersRequest
{
    Type = ServerType.Public,
    OnlyOnline = true
});

foreach (var server in response.Servers)
{
    Console.WriteLine($"{server.Name} - {server.Endpoint} ({server.UserCount} users)");
}
```

### 3. Health Checking серверов

**Background Service** (Services/ServerHealthCheckService.cs):
```csharp
public class ServerHealthCheckService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServerHealthCheckService> _logger;
    private readonly HttpClient _httpClient;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<INavigatorStorage>();

            var servers = await storage.RegisteredServers.ToListAsync();

            foreach (var server in servers)
            {
                var isOnline = await CheckServerHealthAsync(server.Endpoint);

                server.LastHealthCheck = DateTime.UtcNow;
                server.IsOnline = isOnline;

                _logger.LogInformation(
                    "Health check for {Name}: {Status}",
                    server.Name,
                    isOnline ? "Online" : "Offline"
                );
            }

            await storage.SaveChangesAsync();
        }
    }

    private async Task<bool> CheckServerHealthAsync(string endpoint)
    {
        try
        {
            // Попытка подключения к Beacon service
            var channel = GrpcChannel.ForAddress(endpoint);
            var beaconClient = new BeaconApiClient(channel);

            var response = await beaconClient.GetServerInfoAsync(
                new GetServerInfoRequest(),
                deadline: DateTime.UtcNow.AddSeconds(5)
            );

            return response != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Health check failed for endpoint {Endpoint}",
                endpoint
            );
            return false;
        }
    }
}
```

**Периодичность**: Каждые 5 минут

### 4. Отмена регистрации сервера

**gRPC Method**: `UnregisterServer`

**Request**:
```protobuf
message UnregisterServerRequest {
  string endpoint = 1;      // Endpoint сервера для удаления
}
```

**Response**:
```protobuf
message UnregisterServerResponse {
  bool success = 1;
}
```

**Реализация** (Features/UnregisterServer/UnregisterServerCommandHandler.cs):
```csharp
public async Task<UnregisterServerResponse> Handle(
    UnregisterServerCommand request,
    CancellationToken cancellationToken)
{
    var server = await _storage.RegisteredServers
        .FirstOrDefaultAsync(s => s.Endpoint == request.Endpoint, cancellationToken);

    if (server == null)
    {
        throw new RpcException(new Status(
            StatusCode.NotFound,
            "Server not found"
        ));
    }

    _storage.RegisteredServers.Remove(server);
    await _storage.SaveChangesAsync();

    _logger.LogInformation(
        "Unregistered server: {Name} ({Endpoint})",
        server.Name,
        server.Endpoint
    );

    return new UnregisterServerResponse
    {
        Success = true
    };
}
```

## Сценарии использования

### Сценарий 1: Пользователь ищет сервер

```
Client App Start:
  │
  ├─1─→ Navigator.ListServers(type=Public, onlyOnline=true)
  │       └─→ [
  │           { name: "Main Server", endpoint: "http://main:7004", users: 1500 },
  │           { name: "EU Server", endpoint: "http://eu:7004", users: 800 },
  │           { name: "Asia Server", endpoint: "http://asia:7004", users: 600 }
  │         ]
  │
  ├─2─→ Отображение списка серверов пользователю
  │
  ├─3─→ Пользователь выбирает сервер
  │
  ├─4─→ Подключение к Beacon выбранного сервера
  │       Beacon.GetServerInfo()
  │
  └─5─→ Получение эндпоинтов всех сервисов и начало работы
```

### Сценарий 2: Автоматическая регистрация сервера

```
Beacon Service Startup:
  │
  ├─1─→ LoadConfiguration()
  │       └─→ Получение Navigator endpoint
  │
  ├─2─→ Navigator.RegisterServer(
  │       name: "My Server",
  │       endpoint: "http://myserver:7004",
  │       type: Public
  │     )
  │
  └─3─→ Сервер теперь доступен в глобальном списке

Background:
  │
  └─→ Каждые 5 минут Navigator проверяет health
      └─→ Если сервер offline, помечается IsOnline = false
```

## Зависимости

### Configuration Service (gRPC)

**Методы**:
- `LoadConfiguration` - загрузка настроек при старте

**Настройки**:
```json
{
  "NavigatorDb": "Host=postgres;Database=navigator_db;...",
  "HealthCheck": {
    "IntervalMinutes": 5,
    "TimeoutSeconds": 5
  }
}
```

## API Reference

### gRPC Methods (NavigatorApi)

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `RegisterServer` | ❌ Нет | Регистрация BarkFluff сервера |
| `ListServers` | ❌ Нет | Получение списка серверов |
| `UnregisterServer` | ❌ Нет | Отмена регистрации сервера |

**Публичный доступ**: Все методы публичные для обеспечения decentralized discovery.

## Конфигурация

### appsettings.json

```json
{
  "NavigatorDb": "Host=postgres;Database=navigator_db;Username=postgres;Password=postgres",
  "HealthCheck": {
    "IntervalMinutes": 5,
    "TimeoutSeconds": 5,
    "RetryCount": 3
  },
  "Server": {
    "Host": "0.0.0.0",
    "Port": 7010
  }
}
```

### Переменные окружения

- `NavigatorDb` - строка подключения PostgreSQL
- `HealthCheck:IntervalMinutes` - интервал health check
- `HealthCheck:TimeoutSeconds` - таймаут для проверки

## Безопасность

### Spam Prevention

**Проблема**: Возможна регистрация большого количества фейковых серверов.

**Решение**: Rate limiting по IP:
```csharp
[RateLimit(Requests = 10, Period = "1h")]
public override Task<RegisterServerResponse> RegisterServer(...)
```

### Verification

**Рекомендация**: Добавить верификацию серверов:
```csharp
public class RegisteredServer
{
    public bool IsVerified { get; set; }      // Подтверждён администратором
    public string? VerificationToken { get; set; }  // Токен для подтверждения
}
```

**Процесс**:
1. Сервер регистрируется с `IsVerified = false`
2. Администратор Navigator проверяет сервер
3. Устанавливает `IsVerified = true`
4. Только verified серверы показываются в публичном списке

## Производительность

### Caching

**Рекомендация**: Кеширование списка серверов:
```csharp
private readonly IMemoryCache _cache;

public async Task<ListServersResponse> GetCachedServersAsync()
{
    return await _cache.GetOrCreateAsync("servers-list", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return await _storage.GetAllServersAsync();
    });
}
```

### Indexing

**SQL индексы**:
```sql
CREATE INDEX idx_type ON RegisteredServers(Type);
CREATE INDEX idx_region ON RegisteredServers(Region);
CREATE INDEX idx_online ON RegisteredServers(IsOnline);
CREATE INDEX idx_user_count ON RegisteredServers(UserCount DESC);
```

## Мониторинг

### Ключевые метрики

- **Total Registered Servers**: Количество серверов в реестре
- **Online Servers**: Количество доступных серверов
- **Health Check Success Rate**: Процент успешных проверок
- **Average Response Time**: Среднее время ответа ListServers
- **Registrations/day**: Новые регистрации

### Логи

**Примеры**:
```
[2025-11-23 10:00:01] INFO: Registered new server: Main Server (http://main:7004)
[2025-11-23 10:05:00] INFO: Health check for Main Server: Online
[2025-11-23 10:05:02] WARNING: Health check failed for Old Server: Timeout
[2025-11-23 12:30:15] INFO: Unregistered server: Test Server (http://test:7004)
```

## Известные проблемы

### 🟡 Средние

1. **Отсутствие верификации серверов**
   - Любой может зарегистрировать сервер
   - **Рекомендация**: Добавить verification process

2. **Нет защиты от DDoS**
   - Health checking может быть использован для DDoS
   - **Рекомендация**: Rate limiting для RegisterServer

3. **Статичный health check**
   - Проверка только доступности Beacon
   - **Рекомендация**: Более глубокие health checks

### 🟢 Низкие

4. **Отсутствие аналитики**
   - Нет статистики по использованию серверов
   - **Рекомендация**: Логирование подключений

5. **Нет geographic routing**
   - Клиент не знает ближайший сервер
   - **Рекомендация**: GeoIP для автоматического выбора

## Troubleshooting

### Проблема: Сервер не появляется в списке

**Причина**: Регистрация не прошла или сервер offline.

**Решение**:
```sql
-- Проверить регистрацию
SELECT * FROM RegisteredServers WHERE Endpoint = 'http://myserver:7004';

-- Проверить статус
SELECT Name, IsOnline, LastHealthCheck
FROM RegisteredServers
WHERE Endpoint = 'http://myserver:7004';
```

### Проблема: Все серверы показываются как offline

**Причина**: Health check service не работает или firewall блокирует.

**Решение**:
1. Проверить логи Navigator на ошибки health check
2. Убедиться, что Beacon endpoints доступны
3. Проверить firewall rules

## Примеры использования

### Пример 1: Регистрация сервера при старте

```csharp
// Beacon Service - Program.cs
var navigatorEndpoint = configuration["NavigatorService:Endpoint"];

if (!string.IsNullOrEmpty(navigatorEndpoint))
{
    var navigatorChannel = GrpcChannel.ForAddress(navigatorEndpoint);
    var navigatorClient = new NavigatorApiClient(navigatorChannel);

    try
    {
        await navigatorClient.RegisterServerAsync(new RegisterServerRequest
        {
            Name = configuration["ServerInfo:Name"],
            Endpoint = configuration["ServerInfo:PublicEndpoint"],
            Description = configuration["ServerInfo:Description"],
            IconUrl = configuration["ServerInfo:IconUrl"],
            Type = ServerType.Public,
            Region = configuration["ServerInfo:Region"] ?? "Unknown"
        });

        _logger.LogInformation("Successfully registered with Navigator");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to register with Navigator");
    }
}
```

### Пример 2: Клиент выбирает сервер

```csharp
public async Task<List<ServerInfo>> DiscoverServersAsync()
{
    var navigatorClient = new NavigatorApiClient(
        GrpcChannel.ForAddress("http://navigator.barkfluff.com:7010")
    );

    var response = await navigatorClient.ListServersAsync(new ListServersRequest
    {
        Type = ServerType.Public,
        OnlyOnline = true
    });

    return response.Servers.ToList();
}

public async Task ConnectToServerAsync(ServerInfo server)
{
    // Подключение к Beacon выбранного сервера
    var beaconClient = new BeaconApiClient(
        GrpcChannel.ForAddress(server.Endpoint)
    );

    var serverInfo = await beaconClient.GetServerInfoAsync(
        new GetServerInfoRequest()
    );

    // Сохранение эндпоинтов и начало работы
    _currentServer = serverInfo;
}
```

## Будущие улучшения

### 1. Geographic Routing

Автоматический выбор ближайшего сервера:
```csharp
public async Task<ServerInfo> GetNearestServerAsync(string userIp)
{
    var userLocation = await _geoIpService.GetLocationAsync(userIp);

    var servers = await ListServersAsync(new ListServersRequest
    {
        OnlyOnline = true
    });

    return servers
        .OrderBy(s => CalculateDistance(userLocation, s.Region))
        .FirstOrDefault();
}
```

### 2. Server Ratings

Рейтинги и отзывы серверов:
```protobuf
message ServerRating {
  int64 server_id = 1;
  int64 user_id = 2;
  int32 rating = 3;      // 1-5 stars
  string comment = 4;
}
```

### 3. Advanced Metrics

Детальная статистика серверов:
```csharp
public class ServerMetrics
{
    public int ActiveUsers { get; set; }
    public int MessagesPerDay { get; set; }
    public double AverageResponseTime { get; set; }
    public double Uptime { get; set; }
}
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Navigator/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/NavigatorApiService.cs` - gRPC endpoints
- `Features/RegisterServer/` - регистрация сервера
- `Features/ListServers/` - список серверов
- `Features/UnregisterServer/` - отмена регистрации
- `Services/ServerHealthCheckService.cs` - health checking
- `Persistence/NavigatorDbContext.cs` - EF Core контекст
