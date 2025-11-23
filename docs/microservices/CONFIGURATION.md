# Configuration Microservice

## Назначение

Сервис Configuration отвечает за **централизованное управление конфигурацией** всех микросервисов в системе BarkFluff. Он управляет:

- ⚙️ Хранением настроек всех микросервисов в единой базе данных
- 🔧 Предоставлением конфигурации при старте каждого сервиса
- 🔐 Управлением JWT настройками (единый источник правды)
- 🗄️ Строками подключения к базам данных
- 🌐 Эндпоинтами всех сервисов для взаимодействия
- 📊 Бизнес-настройками (лимиты, ограничения и т.д.)

**Порт**: 7003
**База данных**: PostgreSQL (`configuration_db`)
**Зависимости**: Нет (первый сервис, который должен стартовать)

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Entity Framework Core**: ORM
- **PostgreSQL**: База данных конфигурации
- **JSON**: Формат хранения настроек

## Архитектура

```
┌─────────────────────────────────────────────┐
│        Configuration Service                 │
├─────────────────────────────────────────────┤
│  ┌──────────────┐      ┌─────────────────┐ │
│  │  gRPC API    │─────►│  Storage        │ │
│  │              │      │ (EF Core)       │ │
│  └──────────────┘      └────────┬────────┘ │
│                                 │          │
│                                 ↓          │
│                        ┌─────────────────┐ │
│                        │  PostgreSQL     │ │
│                        │ (Config Store)  │ │
│                        └─────────────────┘ │
└─────────────────────────────────────────────┘
              ↑
              │ LoadConfiguration()
              │
    ┌─────────┴─────────┬──────────┬──────────┐
    │                   │          │          │
┌───┴────┐      ┌───────┴──┐   ┌──┴──────┐  │
│Identity│      │  Users   │   │Messages │ ...
└────────┘      └──────────┘   └─────────┘
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **ServiceConfigurations** | Конфигурация каждого микросервиса |

### Основные сущности

#### ServiceConfiguration

```csharp
public class ServiceConfiguration
{
    public long Id { get; set; }
    public ServiceId ServiceId { get; set; }    // Enum: Identity, Users, Messages, etc.
    public string Key { get; set; }             // Ключ настройки (например, "JwtSettings:SecretKey")
    public string Value { get; set; }           // Значение настройки
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

**Unique Constraint**: `(ServiceId, Key)` - каждый сервис имеет уникальные ключи.

**Пример данных**:
```sql
-- JWT настройки (общие для всех сервисов)
INSERT INTO ServiceConfigurations (ServiceId, Key, Value)
VALUES
  ('Identity', 'JwtSettings:SecretKey', 'your-secret-key-256-bit'),
  ('Identity', 'JwtSettings:Issuer', 'BarkFluff.Identity'),
  ('Identity', 'JwtSettings:Audience', 'BarkFluff'),
  ('Identity', 'JwtSettings:ExpiryMinutes', '60');

-- Database connection strings
INSERT INTO ServiceConfigurations (ServiceId, Key, Value)
VALUES
  ('Identity', 'IdentityDb', 'Host=postgres;Database=identity_db;Username=postgres;Password=postgres'),
  ('Users', 'UsersDb', 'Host=postgres;Database=users_db;Username=postgres;Password=postgres');

-- Service endpoints для взаимодействия
INSERT INTO ServiceConfigurations (ServiceId, Key, Value)
VALUES
  ('Identity', 'UsersService:Host', 'http://users:7002'),
  ('Identity', 'UsersService:Token', 'service-token-here'),
  ('Messages', 'UsersService:Host', 'http://users:7002'),
  ('Messages', 'FilesService:Host', 'http://files:7005');
```

### ServiceId Enum

```csharp
public enum ServiceId
{
    Configuration = 1,
    Beacon = 2,
    Identity = 3,
    Users = 4,
    Files = 5,
    Messages = 6,
    Updates = 7,
    Notification = 8,
    FastAuth = 9,
    Navigator = 10
}
```

## Ключевые функции

### 1. LoadConfiguration

**gRPC Method**: `LoadConfiguration`

**Описание**: Главный метод для загрузки конфигурации сервиса при его старте.

**Request**:
```protobuf
message LoadConfigurationRequest {
  ServiceId service_id = 1;    // Какой сервис запрашивает конфигурацию
}
```

**Response**:
```protobuf
message LoadConfigurationResponse {
  repeated ConfigurationSetting settings = 1;
}

message ConfigurationSetting {
  string key = 1;      // Ключ настройки
  string value = 2;    // Значение настройки
}
```

**Реализация** (Features/LoadConfiguration/LoadConfigurationQueryHandler.cs):
```csharp
public class LoadConfigurationQueryHandler
    : IRequestHandler<LoadConfigurationQuery, LoadConfigurationResponse>
{
    private readonly IConfigurationStorage _storage;
    private readonly ILogger<LoadConfigurationQueryHandler> _logger;

    public async Task<LoadConfigurationResponse> Handle(
        LoadConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Loading configuration for service: {ServiceId}",
            request.ServiceId
        );

        var configurations = await _storage.ServiceConfigurations
            .Where(c => c.ServiceId == request.ServiceId)
            .ToListAsync(cancellationToken);

        if (!configurations.Any())
        {
            _logger.LogWarning(
                "No configuration found for service: {ServiceId}",
                request.ServiceId
            );
        }

        var response = new LoadConfigurationResponse();

        foreach (var config in configurations)
        {
            response.Settings.Add(new ConfigurationSetting
            {
                Key = config.Key,
                Value = config.Value
            });
        }

        _logger.LogInformation(
            "Loaded {Count} configuration settings for {ServiceId}",
            configurations.Count,
            request.ServiceId
        );

        return response;
    }
}
```

### 2. Использование в других сервисах

**Типичный процесс старта микросервиса**:

```csharp
// Program.cs любого микросервиса
var builder = WebApplication.CreateBuilder(args);

// 1. Подключение к Configuration Service
var configServiceEndpoint = builder.Configuration["ConfigurationService:Endpoint"]
    ?? "http://configuration:7003";

var configChannel = GrpcChannel.ForAddress(configServiceEndpoint);
var configClient = new ConfigurationApiClient(configChannel);

// 2. Загрузка конфигурации
var configResponse = await configClient.LoadConfigurationAsync(
    new LoadConfigurationRequest
    {
        ServiceId = ServiceId.Identity  // или Users, Messages, etc.
    }
);

// 3. Добавление настроек в IConfiguration
var configDictionary = configResponse.Settings
    .ToDictionary(s => s.Key, s => s.Value);

builder.Configuration.AddInMemoryCollection(configDictionary);

// 4. Теперь можно использовать настройки через IConfiguration
var jwtSecretKey = builder.Configuration["JwtSettings:SecretKey"];
var databaseConnectionString = builder.Configuration["IdentityDb"];

// 5. Запуск сервиса
var app = builder.Build();
app.Run();
```

## Категории настроек

### 1. JWT Settings (общие для всех)

**Ключи**:
```
JwtSettings:SecretKey       - Секретный ключ для подписи токенов (256-bit)
JwtSettings:Issuer          - Издатель токена (обычно "BarkFluff.Identity")
JwtSettings:Audience        - Аудитория токена (обычно "BarkFluff")
JwtSettings:ExpiryMinutes   - Время жизни access token (обычно 60)
```

**Важно**: Все сервисы ДОЛЖНЫ иметь одинаковые JWT настройки для корректной валидации токенов.

### 2. Database Connection Strings

**Ключи** (по сервису):
```
IdentityDb      - PostgreSQL для Identity service
UsersDb         - PostgreSQL для Users service
MessagesDb      - PostgreSQL для Messages service
FilesDb         - PostgreSQL для Files service
ConfigurationDb - PostgreSQL для Configuration service (самореференс)
```

**Формат**:
```
Host=postgres;Database=identity_db;Username=postgres;Password=postgres;Include Error Detail=true
```

### 3. Service Endpoints

**Паттерн**: `{TargetService}Service:Host`

**Примеры**:
```
UsersService:Host       - http://users:7002
FilesService:Host       - http://files:7005
MessagesService:Host    - http://messages:7006
UpdatesService:Host     - http://updates:7015
```

**Использование**: Для service-to-service gRPC вызовов.

### 4. Service Tokens

**Паттерн**: `{TargetService}Service:Token`

**Примеры**:
```
UsersService:Token      - service-token-for-users
FilesService:Token      - service-token-for-files
```

**Назначение**: Service-to-Service аутентификация через JwtClientInterceptor.

### 5. RabbitMQ Settings

**Ключи**:
```
RabbitMQ:Host       - rabbitmq://rabbitmq
RabbitMQ:Username   - guest
RabbitMQ:Password   - guest
```

### 6. Redis Settings

**Ключи**:
```
Redis:Host          - redis:6379
Redis:Password      - (опционально)
Redis:Database      - 0
```

### 7. Minio (S3) Settings

**Ключи** (для Files service):
```
Minio:Endpoint      - minio:9000
Minio:AccessKey     - admin
Minio:SecretKey     - password
Minio:UseSSL        - false
```

### 8. SMTP Settings

**Ключи** (для Notification service):
```
SmtpSettings:Host       - smtp.gmail.com
SmtpSettings:Port       - 587
SmtpSettings:Username   - noreply@barkfluff.com
SmtpSettings:Password   - app-password
SmtpSettings:FromEmail  - noreply@barkfluff.com
SmtpSettings:FromName   - BarkFluff
```

### 9. Server Metadata

**Ключи** (для Beacon service):
```
ServerInfo:Name         - BarkFluff Main Server
ServerInfo:Version      - 1.0.0
ServerInfo:IconUrl      - https://cdn.barkfluff.com/icon.png
```

### 10. Business Settings

**Примеры**:
```
Limits:MaxMessageLength         - 5000
Limits:MaxFileSize              - 104857600  (100 MB)
Limits:MaxGroupChatMembers      - 200
Features:EnableGroupChats       - true
Features:Enable2FA              - true
```

## Зависимости

**Важно**: Configuration service НЕ имеет зависимостей от других микросервисов.

Это единственный сервис, который стартует полностью автономно, используя только `appsettings.json` для подключения к PostgreSQL.

## API Reference

### gRPC Methods (ConfigurationApi)

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `LoadConfiguration` | ❌ Нет | Загрузка конфигурации для указанного сервиса |

**Публичный доступ**: Метод публичный, так как сервисы стартуют без токенов и нуждаются в конфигурации для их получения.

## Конфигурация

### appsettings.json

```json
{
  "ConnectionStrings": {
    "ConfigurationDb": "Host=postgres;Database=configuration_db;Username=postgres;Password=postgres"
  },
  "Server": {
    "Host": "0.0.0.0",
    "Port": 7003
  }
}
```

**Важно**: Configuration service - единственный сервис, который не загружает свою конфигурацию из себя самого (очевидно).

### Переменные окружения

- `ConnectionStrings:ConfigurationDb` - строка подключения к PostgreSQL
- `Server:Port` - порт сервиса

## Инициализация данных

### Seed Data

**Процесс** (Persistence/ConfigurationDbContextSeed.cs):
```csharp
public static class ConfigurationDbContextSeed
{
    public static async Task SeedAsync(ConfigurationDbContext context)
    {
        if (await context.ServiceConfigurations.AnyAsync())
            return; // Уже инициализировано

        // JWT Settings (общие для всех сервисов)
        await SeedJwtSettingsAsync(context);

        // Database connection strings
        await SeedDatabaseSettingsAsync(context);

        // Service endpoints
        await SeedServiceEndpointsAsync(context);

        // RabbitMQ settings
        await SeedRabbitMqSettingsAsync(context);

        await context.SaveChangesAsync();
    }

    private static async Task SeedJwtSettingsAsync(ConfigurationDbContext context)
    {
        var services = new[]
        {
            ServiceId.Identity,
            ServiceId.Users,
            ServiceId.Messages,
            ServiceId.Files,
            ServiceId.Updates,
            ServiceId.Beacon
        };

        foreach (var serviceId in services)
        {
            context.ServiceConfigurations.AddRange(
                new ServiceConfiguration
                {
                    ServiceId = serviceId,
                    Key = "JwtSettings:SecretKey",
                    Value = "your-secret-key-must-be-256-bits-long",
                    CreatedAt = DateTime.UtcNow
                },
                new ServiceConfiguration
                {
                    ServiceId = serviceId,
                    Key = "JwtSettings:Issuer",
                    Value = "BarkFluff.Identity",
                    CreatedAt = DateTime.UtcNow
                },
                new ServiceConfiguration
                {
                    ServiceId = serviceId,
                    Key = "JwtSettings:Audience",
                    Value = "BarkFluff",
                    CreatedAt = DateTime.UtcNow
                },
                new ServiceConfiguration
                {
                    ServiceId = serviceId,
                    Key = "JwtSettings:ExpiryMinutes",
                    Value = "60",
                    CreatedAt = DateTime.UtcNow
                }
            );
        }
    }
}
```

**Запуск seed**:
```csharp
// Program.cs
var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ConfigurationDbContext>();
    await context.Database.MigrateAsync();
    await ConfigurationDbContextSeed.SeedAsync(context);
}

app.Run();
```

## Управление конфигурацией

### Обновление настроек

**SQL**:
```sql
-- Обновление JWT secret key для всех сервисов
UPDATE ServiceConfigurations
SET Value = 'new-secret-key-256-bit', UpdatedAt = NOW()
WHERE Key = 'JwtSettings:SecretKey';

-- Обновление endpoint конкретного сервиса
UPDATE ServiceConfigurations
SET Value = 'http://new-users-host:7002', UpdatedAt = NOW()
WHERE ServiceId = 'Messages' AND Key = 'UsersService:Host';
```

**Важно**: После обновления необходимо **перезапустить** все затронутые сервисы.

### Добавление новой настройки

```sql
INSERT INTO ServiceConfigurations (ServiceId, Key, Value, CreatedAt)
VALUES
  ('Messages', 'NewFeature:Enabled', 'true', NOW());
```

### Удаление устаревшей настройки

```sql
DELETE FROM ServiceConfigurations
WHERE Key = 'OldFeature:Setting';
```

## Безопасность

### Хранение секретов

**Проблема**: Секреты (пароли, ключи) хранятся в plain text в БД.

**Решение 1: Encryption at Rest**
```csharp
public class EncryptedConfigurationSetting
{
    public string Key { get; set; }
    public string EncryptedValue { get; set; }  // AES-256 encrypted

    public string GetDecryptedValue(string encryptionKey)
    {
        return AesEncryption.Decrypt(EncryptedValue, encryptionKey);
    }
}
```

**Решение 2: Интеграция с HashiCorp Vault**
```csharp
public async Task<string> GetSecretAsync(string key)
{
    // Чтение из Vault вместо БД для секретных значений
    if (key.Contains("Password") || key.Contains("SecretKey"))
    {
        return await _vaultClient.ReadSecretAsync(key);
    }

    // Обычные настройки из БД
    return await _storage.GetConfigurationAsync(key);
}
```

### Доступ к Configuration API

**Текущее состояние**: Публичный доступ (без аутентификации).

**Риск**: Любой может получить конфигурацию всех сервисов.

**Рекомендация**: Добавить аутентификацию через service tokens:
```csharp
[Authorize(Policy = nameof(TokenType.Service))]
public override async Task<LoadConfigurationResponse> LoadConfiguration(...)
{
    // Проверить, что запрашивающий сервис имеет право получить эту конфигурацию
    var requestingService = context.GetServiceId();
    var requestedService = request.ServiceId;

    if (!CanAccessConfiguration(requestingService, requestedService))
    {
        throw new RpcException(new Status(
            StatusCode.PermissionDenied,
            "Service not authorized to access this configuration"
        ));
    }

    // ... остальная логика
}
```

## Производительность

### Caching

**Проблема**: Каждый запрос LoadConfiguration идёт в БД.

**Решение**: In-memory caching с invalidation:
```csharp
public class CachedConfigurationStorage : IConfigurationStorage
{
    private readonly IMemoryCache _cache;
    private readonly ConfigurationDbContext _context;

    public async Task<List<ServiceConfiguration>> GetConfigurationAsync(
        ServiceId serviceId)
    {
        var cacheKey = $"config:{serviceId}";

        if (_cache.TryGetValue(cacheKey, out List<ServiceConfiguration> cached))
        {
            return cached;
        }

        var configurations = await _context.ServiceConfigurations
            .Where(c => c.ServiceId == serviceId)
            .ToListAsync();

        _cache.Set(cacheKey, configurations, TimeSpan.FromMinutes(60));

        return configurations;
    }

    public async Task InvalidateCacheAsync(ServiceId serviceId)
    {
        _cache.Remove($"config:{serviceId}");
    }
}
```

## Мониторинг

### Ключевые метрики

- **LoadConfiguration Requests/sec**: Частота запросов (высокая при рестартах)
- **Configuration Load Time**: Время ответа
- **Cache Hit Rate**: Процент попаданий в кеш
- **Configuration Changes/day**: Частота обновлений

### Логи

**Примеры**:
```
[2025-11-23 10:00:01] INFO: Loading configuration for service: Identity
[2025-11-23 10:00:01] INFO: Loaded 25 configuration settings for Identity
[2025-11-23 15:30:12] WARNING: No configuration found for service: NewService
[2025-11-23 18:45:33] INFO: Configuration updated: ServiceId=Messages, Key=UsersService:Host
```

## Известные проблемы

### 🔴 Критичные

1. **Секреты хранятся в plain text**
   - Пароли, ключи доступны в БД
   - **Рекомендация**: Encryption at rest или Vault

2. **Отсутствие аутентификации**
   - Любой может запросить конфигурацию
   - **Рекомендация**: Service token authentication

### 🟡 Средние

3. **Нет версионирования настроек**
   - Невозможно откатиться к предыдущей версии
   - **Рекомендация**: Добавить таблицу ConfigurationHistory

4. **Отсутствие cache invalidation**
   - Изменения требуют рестарта сервисов
   - **Рекомендация**: Push-уведомления через RabbitMQ о изменениях

### 🟢 Низкие

5. **Нет UI для управления конфигурацией**
   - Изменения только через SQL
   - **Рекомендация**: Admin panel для управления

## Troubleshooting

### Проблема: Сервис не может подключиться к Configuration

**Причина**: Configuration service не запущен или недоступен.

**Решение**:
```bash
# Проверить статус
docker ps | grep configuration

# Проверить логи
docker logs barkfluff-configuration

# Проверить доступность
grpcurl -plaintext localhost:7003 list
```

### Проблема: "No configuration found"

**Причина**: Не выполнен seed данных или неправильный ServiceId.

**Решение**:
```sql
-- Проверить наличие данных
SELECT * FROM ServiceConfigurations WHERE ServiceId = 'Identity';

-- Если пусто, запустить seed
-- или добавить данные вручную
```

### Проблема: JWT validation fails

**Причина**: Разные JWT настройки у разных сервисов.

**Решение**:
```sql
-- Проверить единообразие JWT настроек
SELECT ServiceId, Key, Value
FROM ServiceConfigurations
WHERE Key LIKE 'JwtSettings:%'
ORDER BY Key, ServiceId;

-- Убедиться, что SecretKey, Issuer, Audience одинаковые у всех
```

## Примеры использования

### Пример 1: Добавление нового сервиса

```sql
-- 1. JWT настройки (копировать из существующего сервиса)
INSERT INTO ServiceConfigurations (ServiceId, Key, Value, CreatedAt)
SELECT 'NewService', Key, Value, NOW()
FROM ServiceConfigurations
WHERE ServiceId = 'Identity' AND Key LIKE 'JwtSettings:%';

-- 2. Database connection
INSERT INTO ServiceConfigurations (ServiceId, Key, Value, CreatedAt)
VALUES ('NewService', 'NewServiceDb', 'Host=postgres;Database=newservice_db;...', NOW());

-- 3. RabbitMQ settings
INSERT INTO ServiceConfigurations (ServiceId, Key, Value, CreatedAt)
VALUES
  ('NewService', 'RabbitMQ:Host', 'rabbitmq://rabbitmq', NOW()),
  ('NewService', 'RabbitMQ:Username', 'guest', NOW()),
  ('NewService', 'RabbitMQ:Password', 'guest', NOW());
```

### Пример 2: Миграция на новый SMTP сервер

```sql
-- Обновить SMTP настройки для Notification service
UPDATE ServiceConfigurations
SET Value = 'smtp.sendgrid.net', UpdatedAt = NOW()
WHERE ServiceId = 'Notification' AND Key = 'SmtpSettings:Host';

UPDATE ServiceConfigurations
SET Value = '587', UpdatedAt = NOW()
WHERE ServiceId = 'Notification' AND Key = 'SmtpSettings:Port';

UPDATE ServiceConfigurations
SET Value = 'apikey', UpdatedAt = NOW()
WHERE ServiceId = 'Notification' AND Key = 'SmtpSettings:Username';

UPDATE ServiceConfigurations
SET Value = 'SG.xxxx', UpdatedAt = NOW()
WHERE ServiceId = 'Notification' AND Key = 'SmtpSettings:Password';

-- Перезапустить Notification service
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Configuration/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/ConfigurationApiService.cs` - gRPC endpoints
- `Persistence/ConfigurationDbContext.cs` - EF Core контекст
- `Persistence/ConfigurationDbContextSeed.cs` - seed данных
- `Persistence/Storage/ConfigurationStorage.cs` - репозиторий
- `Migrations/` - EF Core миграции
