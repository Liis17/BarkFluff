# Beacon Microservice

## Назначение

Сервис Beacon отвечает за **центральную точку входа и service discovery** в системе BarkFluff. Он управляет:

- 🏠 Предоставлением информации о сервере для клиентов
- 🗺️ Service discovery - предоставление эндпоинтов всех микросервисов
- 📋 Метаданными сервера (название, версия, иконка)
- 🔍 Проверкой доступности основных сервисов
- 🚪 Первым контактом клиента с сервером

**Порт**: 7004
**База данных**: Не используется (stateless)
**Зависимости**: Configuration service

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Configuration Service**: Источник данных о сервере

## Архитектура

```
┌─────────────────────────────────────────────┐
│             Beacon Service                   │
├─────────────────────────────────────────────┤
│  ┌──────────────┐      ┌─────────────────┐ │
│  │  gRPC API    │─────►│  Server Info    │ │
│  │ (BeaconApi)  │      │    Provider     │ │
│  └──────────────┘      └────────┬────────┘ │
│                                 │          │
│                                 ↓          │
│                        ┌─────────────────┐ │
│                        │ Configuration   │ │
│                        │    Service      │ │
│                        └─────────────────┘ │
└─────────────────────────────────────────────┘
```

## Основные концепции

### ServerInfo

**Назначение**: Единый DTO, содержащий всю информацию о сервере для клиента.

**Proto Definition** (shared.proto):
```protobuf
message ServerInfo {
  string name = 1;                  // Название сервера (из конфигурации)
  string version = 2;               // Версия сервера (из конфигурации)
  string icon_url = 3;              // URL иконки сервера
  repeated ServiceEndpoint services = 4;  // Эндпоинты всех сервисов
}

message ServiceEndpoint {
  string service_name = 1;          // Название сервиса (Identity, Users, etc.)
  string endpoint = 2;              // gRPC endpoint (http://host:port)
}
```

**Пример данных**:
```json
{
  "name": "BarkFluff Main Server",
  "version": "1.0.0",
  "icon_url": "https://cdn.barkfluff.com/server-icon.png",
  "services": [
    { "service_name": "Identity", "endpoint": "http://localhost:7001" },
    { "service_name": "Users", "endpoint": "http://localhost:7002" },
    { "service_name": "Messages", "endpoint": "http://localhost:7006" },
    { "service_name": "Files", "endpoint": "http://localhost:7005" },
    { "service_name": "Updates", "endpoint": "http://localhost:7015" }
  ]
}
```

## Ключевые функции

### 1. GetServerInfo

**gRPC Method**: `GetServerInfo`

**Описание**: Единственный метод Beacon service. Возвращает полную информацию о сервере.

**Request**:
```protobuf
message GetServerInfoRequest {
  // Пустой запрос - не требует параметров
}
```

**Response**:
```protobuf
message GetServerInfoResponse {
  ServerInfo server_info = 1;
}
```

**Реализация** (Host/BeaconApiService.cs):
```csharp
public class BeaconApiService : BeaconApi.BeaconApiBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<BeaconApiService> _logger;

    public override Task<GetServerInfoResponse> GetServerInfo(
        GetServerInfoRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("GetServerInfo requested");

        var serverInfo = new ServerInfo
        {
            Name = _configuration["ServerInfo:Name"] ?? "BarkFluff Server",
            Version = _configuration["ServerInfo:Version"] ?? "1.0.0",
            IconUrl = _configuration["ServerInfo:IconUrl"] ?? ""
        };

        // Добавление эндпоинтов всех сервисов
        AddServiceEndpoint(serverInfo, "Identity", "IdentityService:Host");
        AddServiceEndpoint(serverInfo, "Users", "UsersService:Host");
        AddServiceEndpoint(serverInfo, "Messages", "MessagesService:Host");
        AddServiceEndpoint(serverInfo, "Files", "FilesService:Host");
        AddServiceEndpoint(serverInfo, "Updates", "UpdatesService:Host");
        AddServiceEndpoint(serverInfo, "FastAuth", "FastAuthService:Host");
        AddServiceEndpoint(serverInfo, "Navigator", "NavigatorService:Host");

        return Task.FromResult(new GetServerInfoResponse
        {
            ServerInfo = serverInfo
        });
    }

    private void AddServiceEndpoint(
        ServerInfo serverInfo,
        string serviceName,
        string configKey)
    {
        var endpoint = _configuration[configKey];

        if (!string.IsNullOrEmpty(endpoint))
        {
            serverInfo.Services.Add(new ServiceEndpoint
            {
                ServiceName = serviceName,
                Endpoint = endpoint
            });
        }
        else
        {
            _logger.LogWarning(
                "Service endpoint not configured: {ServiceName} ({ConfigKey})",
                serviceName,
                configKey
            );
        }
    }
}
```

### 2. Загрузка конфигурации при старте

**Процесс** (Program.cs):
```csharp
// 1. Подключение к Configuration Service
var configServiceEndpoint = builder.Configuration["ConfigurationService:Endpoint"];
var configClient = new ConfigurationApiClient(
    GrpcChannel.ForAddress(configServiceEndpoint)
);

// 2. Загрузка конфигурации для Beacon
var beaconConfig = await configClient.LoadConfigurationAsync(
    new LoadConfigurationRequest
    {
        ServiceId = ServiceId.Beacon
    }
);

// 3. Сохранение конфигурации в IConfiguration
foreach (var setting in beaconConfig.Settings)
{
    builder.Configuration[setting.Key] = setting.Value;
}

// 4. Запуск сервиса с загруженной конфигурацией
```

**Важно**: Beacon зависит от Configuration service и не может стартовать без него.

## Типичные сценарии использования

### Сценарий 1: Первое подключение клиента

```
Client Application Starts
  │
  ├─1─→ Ввод адреса сервера (http://server.barkfluff.com:7004)
  │
  ├─2─→ Beacon.GetServerInfo()
  │       │
  │       └─→ Получение:
  │           - Название сервера
  │           - Версия сервера
  │           - Иконка сервера
  │           - Эндпоинты всех сервисов
  │
  ├─3─→ Сохранение эндпоинтов локально
  │
  ├─4─→ Отображение экрана логина/регистрации
  │
  └─5─→ Использование Identity.Auth() для входа
          (используя endpoint из ServerInfo)
```

**Код на клиенте**:
```csharp
// 1. Подключение к Beacon
var beaconChannel = GrpcChannel.ForAddress("http://server.barkfluff.com:7004");
var beaconClient = new BeaconApiClient(beaconChannel);

// 2. Получение информации о сервере
var serverInfoResponse = await beaconClient.GetServerInfoAsync(
    new GetServerInfoRequest()
);

var serverInfo = serverInfoResponse.ServerInfo;

// 3. Сохранение эндпоинтов
var identityEndpoint = serverInfo.Services
    .FirstOrDefault(s => s.ServiceName == "Identity")?.Endpoint;

var usersEndpoint = serverInfo.Services
    .FirstOrDefault(s => s.ServiceName == "Users")?.Endpoint;

// 4. Создание клиентов для других сервисов
var identityClient = new IdentityApiClient(
    GrpcChannel.ForAddress(identityEndpoint)
);

var usersClient = new UsersApiClient(
    GrpcChannel.ForAddress(usersEndpoint)
);
```

### Сценарий 2: Обнаружение нового сервера через Navigator

```
Client
  │
  ├─1─→ Navigator.ListServers()
  │       └─→ [
  │           { name: "Main Server", endpoint: "http://main:7004" },
  │           { name: "EU Server", endpoint: "http://eu:7004" }
  │         ]
  │
  ├─2─→ Для каждого сервера:
  │       Beacon.GetServerInfo()
  │       └─→ Детали сервера (название, версия, иконка)
  │
  └─3─→ Отображение списка серверов пользователю
```

## Зависимости

### Configuration Service (gRPC)

**Методы**:
- `LoadConfiguration` - загрузка настроек при старте

**Критичность**: Высокая. Beacon не может запуститься без Configuration.

**Загружаемые настройки**:
```json
{
  "ServerInfo": {
    "Name": "BarkFluff Main Server",
    "Version": "1.0.0",
    "IconUrl": "https://cdn.barkfluff.com/icon.png"
  },
  "IdentityService": {
    "Host": "http://identity:7001"
  },
  "UsersService": {
    "Host": "http://users:7002"
  },
  "MessagesService": {
    "Host": "http://messages:7006"
  },
  "FilesService": {
    "Host": "http://files:7005"
  },
  "UpdatesService": {
    "Host": "http://updates:7015"
  }
}
```

## API Reference

### gRPC Methods (BeaconApi)

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `GetServerInfo` | ❌ Нет | Получение информации о сервере и эндпоинтах |

**ВАЖНО**: GetServerInfo - публичный метод. Не требует аутентификации.

## Конфигурация

### appsettings.json

```json
{
  "ConfigurationService": {
    "Endpoint": "http://configuration:7003"
  },
  "Server": {
    "Host": "0.0.0.0",
    "Port": 7004
  }
}
```

**Примечание**: Большинство настроек загружается из Configuration service при старте.

### Переменные окружения

- `ConfigurationService:Endpoint` - адрес Configuration service
- `Server:Port` - порт Beacon service

## Безопасность

### Публичный доступ

**GetServerInfo** - публичный метод, доступный без аутентификации.

**Обоснование**:
- Необходим для первого подключения клиента
- Содержит только метаданные сервера
- Не раскрывает чувствительную информацию

### Что НЕ раскрывается

- Версии внутренних зависимостей
- Database connection strings
- Внутренние IP адреса (если правильно настроен reverse proxy)
- Количество пользователей
- Статистика использования

### Rate Limiting

**Рекомендация**: Добавить rate limiting для предотвращения злоупотреблений.

**Пример** (using AspNetCoreRateLimit):
```csharp
services.AddInMemoryRateLimiting();

services.Configure<IpRateLimitOptions>(options =>
{
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "GetServerInfo",
            Limit = 10,
            Period = "1m"
        }
    };
});
```

## Производительность

### Caching

**Текущая реализация**: Данные не кешируются. При каждом запросе читаются из IConfiguration.

**Улучшение**:
```csharp
public class BeaconApiService : BeaconApi.BeaconApiBase
{
    private readonly Lazy<ServerInfo> _cachedServerInfo;

    public BeaconApiService(IConfiguration configuration)
    {
        _cachedServerInfo = new Lazy<ServerInfo>(() =>
            BuildServerInfo(configuration)
        );
    }

    public override Task<GetServerInfoResponse> GetServerInfo(...)
    {
        return Task.FromResult(new GetServerInfoResponse
        {
            ServerInfo = _cachedServerInfo.Value
        });
    }
}
```

**Преимущества**:
- Снижение нагрузки на IConfiguration
- Быстрее response time
- Меньше аллокаций памяти

## Мониторинг

### Ключевые метрики

- **GetServerInfo Requests/sec**: Частота запросов
- **Response Time**: Время ответа (должно быть < 10ms)
- **Error Rate**: Процент ошибок
- **Unique IPs**: Количество уникальных клиентов

### Логи

**Примеры логов**:
```
[2025-11-23 15:30:45] INFO: GetServerInfo requested from IP 192.168.1.100
[2025-11-23 15:30:45] INFO: Returned server info: Name=BarkFluff Main Server, Services=7
[2025-11-23 15:35:12] WARNING: Service endpoint not configured: FastAuth (FastAuthService:Host)
```

## Известные проблемы

### 🟡 Средние

1. **Отсутствие health checks для сервисов**
   - Возвращает эндпоинты даже если сервис недоступен
   - **Рекомендация**: Добавить ping/health check для каждого сервиса

2. **Нет версионирования API**
   - Изменения в ServerInfo могут сломать старых клиентов
   - **Рекомендация**: Добавить версионирование (v1, v2)

### 🟢 Низкие

3. **Статичная конфигурация**
   - Требует перезапуска для обновления эндпоинтов
   - **Рекомендация**: Периодическая перезагрузка из Configuration service

4. **Отсутствие метаданных о возможностях сервера**
   - Нет информации о поддержке 2FA, групповых чатов и т.д.
   - **Рекомендация**: Добавить `ServerCapabilities`

## Troubleshooting

### Проблема: "Service endpoint not configured"

**Причина**: Отсутствует настройка в Configuration service.

**Решение**:
```bash
# Проверить Configuration service
curl http://configuration:7003/api/configuration/Beacon

# Убедиться, что есть настройки типа:
# "IdentityService:Host": "http://identity:7001"
```

### Проблема: Клиент не может подключиться к Beacon

**Диагностика**:
1. Проверить доступность Beacon:
   ```bash
   grpcurl -plaintext localhost:7004 list
   ```

2. Проверить, что порт 7004 открыт:
   ```bash
   netstat -tulpn | grep 7004
   ```

3. Проверить логи Beacon на ошибки запуска

### Проблема: Возвращаются некорректные эндпоинты

**Причина**: Configuration service вернул неправильные данные.

**Решение**:
1. Проверить Configuration database
2. Обновить настройки для Beacon
3. Перезапустить Beacon service

## Примеры использования

### Пример 1: Клиент получает информацию о сервере

```csharp
// Desktop Client
public async Task<ServerInfo> DiscoverServerAsync(string serverAddress)
{
    var channel = GrpcChannel.ForAddress(serverAddress);
    var beaconClient = new BeaconApiClient(channel);

    try
    {
        var response = await beaconClient.GetServerInfoAsync(
            new GetServerInfoRequest()
        );

        _logger.LogInformation(
            "Connected to server: {Name} v{Version}",
            response.ServerInfo.Name,
            response.ServerInfo.Version
        );

        return response.ServerInfo;
    }
    catch (RpcException ex)
    {
        _logger.LogError(ex, "Failed to connect to server {Address}", serverAddress);
        throw new ServerConnectionException("Unable to connect to server", ex);
    }
}
```

### Пример 2: Динамическое создание клиентов

```csharp
public class ServiceClientFactory
{
    private readonly ServerInfo _serverInfo;

    public ServiceClientFactory(ServerInfo serverInfo)
    {
        _serverInfo = serverInfo;
    }

    public IdentityApiClient CreateIdentityClient()
    {
        var endpoint = GetServiceEndpoint("Identity");
        var channel = GrpcChannel.ForAddress(endpoint);
        return new IdentityApiClient(channel);
    }

    public UsersApiClient CreateUsersClient()
    {
        var endpoint = GetServiceEndpoint("Users");
        var channel = GrpcChannel.ForAddress(endpoint);
        return new UsersApiClient(channel);
    }

    private string GetServiceEndpoint(string serviceName)
    {
        var service = _serverInfo.Services
            .FirstOrDefault(s => s.ServiceName == serviceName);

        if (service == null)
        {
            throw new ServiceNotFoundException(
                $"Service '{serviceName}' not found in server info"
            );
        }

        return service.Endpoint;
    }
}
```

### Пример 3: Health Check всех сервисов

```csharp
public async Task<Dictionary<string, bool>> CheckAllServicesHealthAsync()
{
    var healthStatuses = new Dictionary<string, bool>();

    foreach (var service in _serverInfo.Services)
    {
        try
        {
            // Попытка подключения
            var channel = GrpcChannel.ForAddress(service.Endpoint);

            // Для проверки можно использовать gRPC Health Checking Protocol
            var healthClient = new Health.HealthClient(channel);
            var response = await healthClient.CheckAsync(new HealthCheckRequest());

            healthStatuses[service.ServiceName] = response.Status == HealthCheckResponse.Types.ServingStatus.Serving;
        }
        catch
        {
            healthStatuses[service.ServiceName] = false;
        }
    }

    return healthStatuses;
}
```

## Будущие улучшения

### 1. Dynamic Service Discovery

Вместо статичной конфигурации использовать реестр сервисов:

```csharp
public interface IServiceRegistry
{
    Task RegisterServiceAsync(string serviceName, string endpoint);
    Task UnregisterServiceAsync(string serviceName);
    Task<IEnumerable<ServiceEndpoint>> GetActiveServicesAsync();
}
```

### 2. Server Capabilities

Добавить информацию о возможностях сервера:

```protobuf
message ServerCapabilities {
  bool supports_2fa = 1;
  bool supports_group_chats = 2;
  bool supports_video_calls = 3;
  int32 max_file_size_mb = 4;
  repeated string supported_file_types = 5;
}
```

### 3. Health Aggregation

Агрегация health checks всех сервисов:

```csharp
public override async Task<GetServerHealthResponse> GetServerHealth(...)
{
    var servicesHealth = await _healthAggregator.CheckAllAsync();

    return new GetServerHealthResponse
    {
        OverallStatus = servicesHealth.All(h => h.IsHealthy)
            ? HealthStatus.Healthy
            : HealthStatus.Degraded,
        Services = servicesHealth
    };
}
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Beacon/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/BeaconApiService.cs` - gRPC endpoints
- `appsettings.json` - базовые настройки
