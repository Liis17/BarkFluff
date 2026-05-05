# Аудит проекта: BarkFluff.Configuration

**Дата аудита:** 2026-07-01  
**Аудитор:** GitHub Copilot (BarkfluffAgent)  
**Ветка:** `dev`  
**Статус:** 🔴 Требует срочных исправлений

---

## Содержание

1. [🔴 Безопасность](#-безопасность)
2. [🟠 Баги и недоработки](#-баги-и-недоработки)
3. [🟡 Оптимизация](#-оптимизация)
4. [🔵 Архитектура и качество кода](#-архитектура-и-качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Отсутствие аутентификации и авторизации на всех gRPC-методах

**Описание:**  
Сервис `ConfigurationApiService` реализует публичный gRPC API. Ни один из методов не защищён атрибутом `[Authorize]`, нет middleware авторизации в `Program.cs`. Любой клиент во внутренней сети (или снаружи, если порт открыт) может прочитать или изменить конфигурацию любого сервиса, включая строки подключения к БД, JWT-секреты и токены межсервисного взаимодействия.

**CWE:** CWE-306 (Missing Authentication for Critical Function)  
**Severity:** 🔴 Критическая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Host\ConfigurationApiService.cs` : 17–81  
`Backend\BarkFluff.Configuration\Program.cs` : 39–43, 124–127

```csharp
// ❌ Нет авторизации — любой может вызвать эти методы
public class ConfigurationApiService : ConfigurationApi.ConfigurationApiBase
{
    // GetConfiguration, UpdateConfiguration, Get/Add/Update/DeleteReservedName
    // — все без [Authorize]
    public override Task<GetConfigurationResponse> GetConfiguration(
        GetConfigurationRequest request, ServerCallContext context)
    {
        return _mediator.Send(new GetConfigurationCommand { ServiceId = (ServiceId)request.ServiceId });
    }
}

// Program.cs — нет app.UseAuthentication() / app.UseAuthorization()
app.MapGrpcReflectionService();
app.UseRouting();
app.MapGrpcService<ConfigurationApiService>(); // без политик
```

**Варианты решения:**

**Вариант A — Политика TokenType.Service на класс + `app.UseAuthorization()`:**

```csharp
// Program.cs — добавить перед app.Build()
builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options => { /* настройки JWT */ });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(nameof(TokenType.Service), policy =>
        policy.RequireClaim(IdentityClaims.TokenType, nameof(TokenType.Service)));
});

// После app.Build()
app.UseAuthentication();     // ✅
app.UseAuthorization();      // ✅

// ConfigurationApiService.cs
[Authorize(Policy = nameof(TokenType.Service))]
public class ConfigurationApiService : ConfigurationApi.ConfigurationApiBase
{
    // Методы доступны только сервисам с валидным Service-токеном
}
```

---

### SEC-02 — IDOR: любой сервис может запросить конфигурацию чужого ServiceId

**Описание:**  
`GetConfiguration` принимает `ServiceId` как параметр запроса и возвращает конфигурацию этого сервиса без проверки, совпадает ли `ServiceId` из токена с запрашиваемым. Злоумышленник с любым валидным Service-токеном может получить конфиг любого другого сервиса.

**CWE:** CWE-639 (Authorization Bypass Through User-Controlled Key)  
**Severity:** 🔴 Критическая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Host\ConfigurationApiService.cs` : 28–37  
`Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 17–25

```csharp
// ❌ request.ServiceId не верифицируется относительно токена вызывающего
public override Task<GetConfigurationResponse> GetConfiguration(
    GetConfigurationRequest request, ServerCallContext context)
{
    var command = new GetConfigurationCommand
    {
        ServiceId = (ServiceId)request.ServiceId // берётся из тела запроса без проверки
    };
    return _mediator.Send(command);
}
```

**Вариант решения:**

```csharp
// ✅ Извлекаем ServiceId из верифицированного JWT-токена, а не из тела
public override Task<GetConfigurationResponse> GetConfiguration(
    GetConfigurationRequest request, ServerCallContext context)
{
    // Читаем claim из проверенного токена
    var serviceNameClaim = context.GetHttpContext().User
        .FindFirst("service-name")?.Value;

    if (!Enum.TryParse<ServiceId>(serviceNameClaim, out var callerServiceId))
        throw new RpcException(new Status(StatusCode.PermissionDenied, "Invalid service identity"));

    // Используем ServiceId из токена — игнорируем request.ServiceId
    var command = new GetConfigurationCommand { ServiceId = callerServiceId };
    return _mediator.Send(command);
}
```

---

### SEC-03 — Хранение секретов в открытом виде в БД

**Описание:**  
`ConfigurationDefaultsPopulator` записывает в колонку `Value` таблицы `Configurations`: строки подключения к PostgreSQL с паролем, JWT SecretKey, MinIO AccessKey/SecretKey, межсервисные Bearer-токены. Всё хранится plaintext. Достаточно чтения одной таблицы для компрометации всей системы.

**CWE:** CWE-311 (Missing Encryption of Sensitive Data)  
**Severity:** 🔴 Критическая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 248–327

```csharp
// ❌ Пароль PostgreSQL в строке подключения — plaintext в БД
return $"Host={_postgresHost};Database={dbName};Username={_postgresUsername};Password={_postgresPassword}";

// ❌ MinIO секрет — plaintext
"SecretKey" => "minioadmin",

// ❌ JWT SecretKey — plaintext
var secret = GenerateRandomKey(64);
secretConfig.Value = secret; // прямо в БД без шифрования
```

**Вариант решения:**

```csharp
// ✅ Вариант A: шифрование AES-256 через DataProtection перед сохранением
// В ConfigurationStorage.UpdateConfigurationAsync:
private readonly IDataProtector _protector;

public async Task UpdateConfigurationAsync(string section, string key, string value, ...)
{
    var storedValue = IsSensitiveKey(key, section)
        ? _protector.Protect(value)   // шифруем чувствительные
        : value;
    // ... сохраняем storedValue
}

// При чтении:
var rawValue = item.Value;
var displayValue = IsSensitiveKey(item.Key, item.Section)
    ? _protector.Unprotect(rawValue)
    : rawValue;

// ✅ Вариант B (рекомендуется для prod): использовать HashiCorp Vault / Azure Key Vault
// и хранить в БД только ссылки типа "vault://secret/barkfluff/identity/db"
```

---

### SEC-04 — Межсервисные токены с временем жизни 10 лет

**Описание:**  
Метод `GenerateServiceToken` выпускает JWT с `expires: DateTime.UtcNow.AddYears(10)`. Такой токен невозможно отозвать без смены JWT-секрета. Компрометация одного токена — постоянный доступ на 10 лет.

**CWE:** CWE-613 (Insufficient Session Expiration)  
**Severity:** 🟠 Высокая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 348–357

```csharp
// ❌ Токен живёт 10 лет и никогда не ротируется
var token = new JwtSecurityToken(
    issuer: issuer,
    audience: audience,
    claims: claims,
    expires: DateTime.UtcNow.AddYears(10), // 🚨 слишком долго
    signingCredentials: credentials
);
```

**Вариант решения:**

```csharp
// ✅ Разумный срок + механизм ротации при перезапуске
var token = new JwtSecurityToken(
    issuer: issuer,
    audience: audience,
    claims: claims,
    expires: DateTime.UtcNow.AddDays(90), // 90 дней
    signingCredentials: credentials
);

// ✅ Дополнительно: при PopulateDefaultsAsync() всегда перегенерировать токены,
// срок действия которых истёк — не только когда Value == null/""
```

---

### SEC-05 — Пароли RabbitMQ по умолчанию `guest/guest` записываются в БД

**Описание:**  
При первичном заполнении в конфигурацию записывается `Username = "guest"`, `Password = "guest"` для RabbitMQ. Это дефолтные учётные данные, известные всем. Если попытки сменить их пропущены — любой может подключиться к брокеру.

**CWE:** CWE-798 (Use of Hard-coded Credentials)  
**Severity:** 🟠 Высокая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 222–232

```csharp
// ❌ Дефолтные учётные данные RabbitMQ
if (config.Section == "RabbitMQ")
{
    return config.Key switch
    {
        "Host" => "rabbitmq",
        "Username" => "guest",     // ❌ дефолтный логин
        "Password" => "guest",     // ❌ дефолтный пароль
        "VirtualHost" => "/",
        _ => null
    };
}
```

**Вариант решения:**

```csharp
// ✅ Генерировать случайный пароль при первом запуске, не использовать guest
"Username" => "barkfluff",
"Password" => GenerateRandomKey(32), // случайный пароль при старте
```

---

### SEC-06 — gRPC Reflection включён безусловно (утечка схемы API)

**Описание:**  
`AddGrpcReflection()` и `MapGrpcReflectionService()` включены в `Program.cs` без условия окружения. В production это позволяет любому с доступом к порту перечислить все методы и сообщения через `grpcurl` или аналоги.

**Severity:** 🟡 Средняя  
**Путь к файлу:** `Backend\BarkFluff.Configuration\Program.cs` : 44, 124

```csharp
// ❌ Reflection в проде открывает полную схему API
builder.Services.AddGrpcReflection();
// ...
app.MapGrpcReflectionService(); // всегда, даже в production
```

**Вариант решения:**

```csharp
// ✅ Только в Development
if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — `Thread.Sleep` блокирует поток при retry-логике миграций

**Описание:**  
В `Program.cs` retry-цикл использует `Thread.Sleep(delay)` — блокирующий вызов в async-совместимой среде ASP.NET Core. Это замораживает поток пула на до 32 секунд (сумма геометрической прогрессии: 2+4+8+16 с). В контейнерной среде с лимитами потоков это критично.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Program.cs` : 118

```csharp
// ❌ Блокирует поток
Thread.Sleep(delay);
delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
```

**Вариант решения:**

```csharp
// ✅ Используем Task.Delay в асинхронном контексте
// Program.cs Main → async Main
public static async Task Main(string[] args)
{
    // ...
    await Task.Delay(delay); // не блокирует поток
    delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
}
```

---

### BUG-02 — `GetAwaiter().GetResult()` внутри цикла миграций (deadlock-риск)

**Описание:**  
`PopulateDefaultsAsync()` — async-метод, вызываемый через `.GetAwaiter().GetResult()`. При наличии `SynchronizationContext` (например, в некоторых хост-средах) это может привести к deadlock. Вдобавок это противоречит async-первой модели .NET.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Program.cs` : 98

```csharp
// ❌ Синхронный вызов асинхронного метода — потенциальный deadlock
populator.PopulateDefaultsAsync().GetAwaiter().GetResult();
```

**Вариант решения:**

```csharp
// ✅ Сделать Main асинхронным и await-ить везде
await populator.PopulateDefaultsAsync();
```

---

### BUG-03 — `UpdateConfigurationAsync` не обновляет `ServiceId` при upsert и молча создаёт дубли

**Описание:**  
При вызове `UpdateConfiguration` если запись с таким `(section, key, serviceId)` не найдена — создаётся новая. Однако `ServiceId` в новой записи берётся из параметра, который в gRPC-методе передаётся как `int32` без валидации допустимого значения. Невалидный `ServiceId` создаст «мусорную» запись, которая никогда не будет прочитана, но займёт место.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 27–54  
`Backend\BarkFluff.Configuration\Host\ConfigurationApiService.cs` : 39–53

```csharp
// ❌ ServiceId не валидируется — можно передать int = 9999
var command = new UpdateConfigurationCommand
{
    ServiceId = request.ServiceId, // int32 из proto, нет проверки enum
    // ...
};

// В Storage.cs создаётся запись с невалидным ServiceId:
var newItem = new ConfigurationItem
{
    ServiceId = serviceId, // приходит без валидации
    // ...
};
```

**Вариант решения:**

```csharp
// ✅ Валидация в CommandHandler
if (!Enum.IsDefined(typeof(ServiceId), request.ServiceId))
    return new UpdateConfigurationResponse
    {
        Success = false,
        Message = $"Неизвестный ServiceId: {request.ServiceId}"
    };
```

---

### BUG-04 — Reserved Names хранятся как CSV в одной строке — Race Condition при конкурентных запросах

**Описание:**  
Вся коллекция зарезервированных имён хранится в одной строке таблицы как `"admin,support,help,..."`. При одновременных вызовах `AddReservedNameAsync` / `DeleteReservedNameAsync` из двух потоков: оба читают одну строку, оба парсят, оба изменяют и сохраняют — последний `SaveChanges` перетирает изменения первого. EF Core не выполняет row-level locking при `FirstOrDefaultAsync` + `SaveChangesAsync`.

**CWE:** CWE-362 (Race Condition / TOCTOU)  
**Severity:** 🟠 Высокая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 88–163

```csharp
// ❌ Read-modify-write без транзакции с блокировкой
var row = await GetReservedNamesRow();       // 1. читаем
var names = ParseNames(row.Value);           // 2. парсим
names.Add(normalized);                       // 3. изменяем (в памяти)
row.Value = string.Join(",", names);         // 4. другой поток уже перезаписал!
await _context.SaveChangesAsync();           // 5. затираем чужие изменения
```

**Вариант решения:**

```csharp
// ✅ Вариант A: SQL-транзакция с pessimistic lock
using var transaction = await _context.Database.BeginTransactionAsync(
    System.Data.IsolationLevel.Serializable);
try
{
    // FromSqlRaw с FOR UPDATE (PostgreSQL)
    var row = await _context.Configurations
        .FromSqlRaw(
            "SELECT * FROM \"Configurations\" " +
            "WHERE \"Section\" = {0} AND \"Key\" = {1} FOR UPDATE",
            ReservedNamesSection, ReservedNamesKey)
        .FirstOrDefaultAsync();

    // ... изменения ...
    await _context.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch { await transaction.RollbackAsync(); throw; }

// ✅ Вариант B (архитектурный, рекомендуется):
// Нормализовать схему — хранить каждое имя отдельной строкой
// Это решит и проблему конкурентности, и упростит индексацию
```

---

### BUG-05 — `GetOrGenerateJwtSecret` генерирует новый секрет при каждом перезапуске, если запись уже существует в `emptyConfigs`

**Описание:**  
Если `JwtSettings.SecretKey` существует в БД, но значение пустое — каждый перезапуск сервиса генерирует новый случайный ключ, что инвалидирует все ранее выданные JWT-токены, включая межсервисные. После перезапуска все сервисы перестают работать до следующего рестарта конфигурации.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 143–164

```csharp
// ❌ При каждом рестарте новый секрет — все токены инвалидируются
private async Task<string> GetOrGenerateJwtSecret(List<ConfigurationItem> emptyConfigs)
{
    var secretConfig = emptyConfigs.FirstOrDefault(
        c => c.Section == "JwtSettings" && c.Key == "SecretKey");

    if (secretConfig != null)
    {
        // emptyConfigs = записи с Value == "" — т.е. ВСЕГДА при чистой БД
        var secret = GenerateRandomKey(64); // новый ключ при каждом запуске!
        secretConfig.Value = secret;
        return secret;
    }
    // ...
}
```

Это поведение само по себе верно для первичного заполнения, но **проблема** возникает если `SaveChangesAsync` упал после установки `secretConfig.Value` но до коммита — при следующем запуске снова генерируется другой ключ.

**Вариант решения:**

```csharp
// ✅ Идемпотентность: проверять транзакционность записи
// Использовать UPSERT (INSERT ... ON CONFLICT DO NOTHING) или
// обернуть весь PopulateDefaultsAsync в одну транзакцию:
await using var transaction = await _context.Database.BeginTransactionAsync();
// ... все изменения ...
await _context.SaveChangesAsync();
await transaction.CommitAsync(); // либо всё, либо ничего
```

---

### BUG-06 — `ConfigurationContext` не имеет FluentAPI-конфигурации — нет индексов и ограничений уникальности

**Описание:**  
`ConfigurationContext` содержит только `DbSet<ConfigurationItem>` без `OnModelCreating`. Отсутствуют:
- Уникальный индекс на `(ServiceId, Section, Key)` — возможны дубли при конкурентных inserts
- Индексы для часто запрашиваемых полей — каждый `GetConfiguration` делает full table scan по `ServiceId`
- NOT NULL ограничения на `Section`, `Key`, `Value` — возможны null-значения в БД

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationContext.cs` : 1–12

```csharp
// ❌ Нет конфигурации модели
public class ConfigurationContext : DbContext
{
    public ConfigurationContext(DbContextOptions<ConfigurationContext> options) : base(options) { }
    public DbSet<ConfigurationItem> Configurations { get; set; }
    // OnModelCreating отсутствует
}
```

**Вариант решения:**

```csharp
// ✅ Добавить FluentAPI конфигурацию
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<ConfigurationItem>(entity =>
    {
        // Уникальность комбинации (ServiceId, Section, Key)
        entity.HasIndex(e => new { e.ServiceId, e.Section, e.Key })
              .IsUnique()
              .HasDatabaseName("IX_Configurations_ServiceId_Section_Key");

        // Индекс для GetConfiguration (основной запрос)
        entity.HasIndex(e => e.ServiceId)
              .HasDatabaseName("IX_Configurations_ServiceId");

        // NOT NULL
        entity.Property(e => e.Section).IsRequired().HasMaxLength(256);
        entity.Property(e => e.Key).IsRequired().HasMaxLength(256);
        entity.Property(e => e.Value).IsRequired(false); // может быть null при создании
        entity.Property(e => e.EditedBy).IsRequired().HasMaxLength(256);
        entity.Property(e => e.EditedFrom).IsRequired().HasMaxLength(256);
    });
}
```

---

## 🟡 Оптимизация

---

### OPT-01 — `GetConfiguration` загружает все записи в память для GroupBy/фильтрации

**Описание:**  
`ConfigurationStorage.GetConfiguration` возвращает `List<ConfigurationItem>`, а затем в `GetConfigurationCommandHandler` выполняется `GroupBy` + `OrderByDescending` **в памяти**. При большом числе конфигурационных записей это неэффективно — весь набор данных переносится из БД на сервер приложения.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Features\GetConfiguration\GetConfigurationCommandHandler.cs` : 27–32  
`Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 17–25

```csharp
// ❌ GroupBy выполняется на стороне приложения (in-memory)
var configurations = await _configurationStorage.GetConfiguration(request.ServiceId);
// ^ возвращает ALL конфиги для serviceId + Unknown

var filteredConfigurations = configurations
    .GroupBy(c => new { c.Section, c.Key })       // in-memory
    .Select(group => group
        .OrderByDescending(c => c.ServiceId == request.ServiceId) // in-memory
        .First())
    .ToList();
```

**Вариант решения:**

```csharp
// ✅ Перенести логику приоритизации в SQL с помощью DISTINCT ON (PostgreSQL)
// или двух запросов с объединением в памяти только финального результата

public async Task<List<ConfigurationItem>> GetConfiguration(ServiceId serviceId)
{
    // Сначала записи сервиса (специфичные), затем — только те из Unknown, которых нет у сервиса
    var serviceSpecific = await _context.Configurations
        .AsNoTracking()
        .Where(x => x.ServiceId == serviceId)
        .ToListAsync();

    var serviceKeys = serviceSpecific
        .Select(x => new { x.Section, x.Key })
        .ToHashSet();

    var globalDefaults = await _context.Configurations
        .AsNoTracking()
        .Where(x => x.ServiceId == ServiceId.Unknown
                 && !_context.Configurations.Any(s =>
                        s.ServiceId == serviceId
                     && s.Section == x.Section
                     && s.Key == x.Key))
        .ToListAsync();

    return serviceSpecific.Concat(globalDefaults).ToList();
}
```

---

### OPT-02 — Отсутствие кэширования — каждый запрос конфигурации идёт в БД

**Описание:**  
`GetConfiguration` — наиболее часто вызываемый метод (каждый сервис запрашивает конфиги при старте и периодически). Нет никакого кэширования (`IMemoryCache`, `IDistributedCache`). При N сервисах, каждый из которых делает запросы при старте, это N параллельных запросов в PostgreSQL.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Features\GetConfiguration\GetConfigurationCommandHandler.cs` : 21–53

```csharp
// ❌ Каждый вызов — запрос в БД
public async Task<GetConfigurationResponse> Handle(GetConfigurationCommand request, CancellationToken cancellationToken)
{
    var configurations = await _configurationStorage.GetConfiguration(request.ServiceId); // всегда DB
    // ...
}
```

**Вариант решения:**

```csharp
// ✅ IMemoryCache с коротким TTL (конфиги меняются редко)
private readonly IMemoryCache _cache;
private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

public async Task<GetConfigurationResponse> Handle(GetConfigurationCommand request, CancellationToken cancellationToken)
{
    var cacheKey = $"config:{request.ServiceId}";

    if (!_cache.TryGetValue(cacheKey, out List<ConfigurationItem> configurations))
    {
        configurations = await _configurationStorage.GetConfiguration(request.ServiceId);
        _cache.Set(cacheKey, configurations, CacheTtl);
    }
    // ...
}

// ✅ При UpdateConfiguration — инвалидировать кэш для затронутого ServiceId
_cache.Remove($"config:{serviceId}");
```

---

### OPT-03 — `ConfigurationStorage` зарегистрирован как `Transient` — создаётся новый экземпляр на каждый запрос

**Описание:**  
В `Program.cs` `ConfigurationStorage` зарегистрирован через `AddTransient`. Сам класс stateless и не хранит ресурсы, но это лишнее выделение памяти на каждый gRPC-вызов. `Scoped` — правильный lifecycle для зависимости от `DbContext` (который `Scoped`).

**Путь к файлу:** `Backend\BarkFluff.Configuration\Program.cs` : 67

```csharp
// ❌ Transient для класса, зависящего от Scoped DbContext
builder.Services.AddTransient<ConfigurationStorage>();
```

**Вариант решения:**

```csharp
// ✅ Scoped — соответствует lifecycle DbContext
builder.Services.AddScoped<ConfigurationStorage>();
```

---

### OPT-04 — `emptyConfigs.Any()` вместо `emptyConfigs.Count == 0` (незначительно, но паттерн)

**Описание:**  
В `PopulateDefaultsAsync` используется `!emptyConfigs.Any()` для проверки пустого списка. Для `List<T>` более эффективно сравнение с `.Count`, т.к. `.Any()` использует итератор.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 111

```csharp
// ❌ Использует итератор для List<T>
if (!emptyConfigs.Any())
    return;
```

**Вариант решения:**

```csharp
// ✅ O(1) проверка длины списка
if (emptyConfigs.Count == 0)
    return;
```

---

## 🔵 Архитектура и качество кода

---

### ARCH-01 — Reserved Names хранятся как CSV — антипаттерн «хранение списка в строке»

**Описание:**  
Весь список зарезервированных имён сериализован в одну строку `"admin,support,help,..."` в поле `Value`. Это нарушает первую нормальную форму (1NF). Проблемы:
- Race condition при конкурентных обновлениях (см. BUG-04)
- Нет индекса — поиск по имени требует `LIKE` или полной загрузки строки
- Ограничение по длине `Value` (если есть) — неконтролируемый рост
- Сложность парсинга, вероятность ошибок с именами, содержащими запятые (сейчас не защищено)

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 60–76

```csharp
// ❌ CSV в одной строке — нарушение 1NF
private const string ReservedNamesSection = "ReservedNames";
private const string ReservedNamesKey = "Usernames";

// Хранится как: "admin,support,help,barkfluff,..."
private static List<string> ParseNames(string value)
{
    return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)...
}
```

**Вариант решения:**

```csharp
// ✅ Отдельная таблица / отдельная сущность
public class ReservedName
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty; // уже нормализованное (lowercase)

    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

// В ConfigurationContext:
public DbSet<ReservedName> ReservedNames { get; set; }

// Индекс уникальности:
entity.HasIndex(e => e.Name).IsUnique();
```

---

### ARCH-02 — `ConfigurationItem.Value` — нет явного различия между «не задано» и «пустая строка»

**Описание:**  
`ConfigurationDefaultsPopulator` проверяет `c.Value == "" || c.Value == null` как признак «пустой» конфигурации. При этом `ConfigurationItem.Value` — `string` (nullable в C#), не отмечен `[Required]`. Если сервис легитимно хочет задать пустое значение конфига — система воспримет это как «не заполнено» и перезапишет при следующем старте.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 107–109  
`Backend\BarkFluff.Configuration\Domain\ConfigurationItem.cs` : 16

```csharp
// ❌ Нет разграничения: null (не задано) vs "" (явно пустое)
var emptyConfigs = await _context.Configurations
    .Where(c => c.Value == "" || c.Value == null) // смешивает два семантически разных состояния
    .ToListAsync();
```

**Вариант решения:**

```csharp
// ✅ Добавить флаг IsPopulated или использовать null как «не задано», "" как валидное пустое
public class ConfigurationItem
{
    // ...
    public string? Value { get; set; }          // null = не задано
    public bool IsPopulated { get; set; }        // true = значение установлено (даже если "")
}

// В PopulateDefaultsAsync:
var emptyConfigs = await _context.Configurations
    .Where(c => !c.IsPopulated)  // только реально незаполненные
    .ToListAsync();
```

---

### ARCH-03 — `ConfigurationApiService.GetConfiguration` не использует `CancellationToken` из `ServerCallContext`

**Описание:**  
gRPC `ServerCallContext` предоставляет `CancellationToken` (`.CancellationToken`), который сигнализирует о разрыве соединения клиентом. Ни один метод `ConfigurationApiService` не пробрасывает этот токен в MediatR и далее в EF Core. При отмене запроса БД-операция продолжится до завершения.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Host\ConfigurationApiService.cs` : 28–37, 39–53

```csharp
// ❌ CancellationToken из контекста не используется
public override Task<GetConfigurationResponse> GetConfiguration(
    GetConfigurationRequest request, ServerCallContext context) // context.CancellationToken игнорируется
{
    return _mediator.Send(command); // нет cancellationToken
}
```

**Вариант решения:**

```csharp
// ✅ Передаём токен отмены в MediatR и далее в EF Core
public override Task<GetConfigurationResponse> GetConfiguration(
    GetConfigurationRequest request, ServerCallContext context)
{
    _metrics.Increment("config_requests");
    var command = new GetConfigurationCommand { ServiceId = (ServiceId)request.ServiceId };
    return _mediator.Send(command, context.CancellationToken); // ✅
}
```

---

### ARCH-04 — `Program.cs`: логика миграции и заполнения данных — ответственность не по месту

**Описание:**  
`Program.cs` содержит 50+ строк бизнес-логики запуска: retry-цикл миграций, парсинг хоста из connection string, создание `ConfigurationDefaultsPopulator`. Это нарушает Single Responsibility и делает `Main` трудно тестируемым.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Program.cs` : 71–122

**Вариант решения:**

```csharp
// ✅ Вынести в IHostedService или extension-метод
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ConfigurationContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<DatabaseInitializer>>();

        await MigrateWithRetryAsync(ctx, logger);
        await PopulateDefaultsAsync(ctx, logger, config);
    }
}

// Program.cs — чисто:
await DatabaseInitializer.InitializeAsync(app.Services, builder.Configuration);
app.Run();
```

---

## Сводная таблица проблем

| ID | Категория | Название | Severity | Статус |
|----|-----------|----------|----------|--------|
| SEC-01 | Безопасность | Отсутствие аутентификации/авторизации | 🔴 Критическая | ⏳ Открыта |
| SEC-02 | Безопасность | IDOR — доступ к чужому ServiceId | 🔴 Критическая | ⏳ Открыта |
| SEC-03 | Безопасность | Секреты в открытом виде в БД | 🔴 Критическая | ⏳ Открыта |
| SEC-04 | Безопасность | Токены с TTL 10 лет | 🟠 Высокая | ⏳ Открыта |
| SEC-05 | Безопасность | Дефолтные учётные данные RabbitMQ | 🟠 Высокая | ⏳ Открыта |
| SEC-06 | Безопасность | gRPC Reflection в production | 🟡 Средняя | ⏳ Открыта |
| BUG-01 | Баг | `Thread.Sleep` блокирует поток | 🟠 Высокая | ⏳ Открыта |
| BUG-02 | Баг | `GetAwaiter().GetResult()` — deadlock-риск | 🟠 Высокая | ⏳ Открыта |
| BUG-03 | Баг | Невалидный ServiceId создаёт мусорные записи | 🟡 Средняя | ⏳ Открыта |
| BUG-04 | Баг | Race Condition в Reserved Names (CSV + no lock) | 🟠 Высокая | ⏳ Открыта |
| BUG-05 | Баг | Перегенерация JWT-секрета при неатомарном старте | 🟠 Высокая | ⏳ Открыта |
| BUG-06 | Баг | Нет индексов и unique constraint в EF модели | 🟠 Высокая | ⏳ Открыта |
| OPT-01 | Оптимизация | GroupBy/фильтр в памяти вместо SQL | 🟡 Средняя | ⏳ Открыта |
| OPT-02 | Оптимизация | Нет кэширования конфигураций | 🟡 Средняя | ⏳ Открыта |
| OPT-03 | Оптимизация | `ConfigurationStorage` Transient вместо Scoped | 🟢 Низкая | ⏳ Открыта |
| OPT-04 | Оптимизация | `.Any()` вместо `.Count == 0` для List | 🟢 Низкая | ⏳ Открыта |
| ARCH-01 | Архитектура | Reserved Names как CSV — нарушение 1NF | 🟠 Высокая | ⏳ Открыта |
| ARCH-02 | Архитектура | Нет разграничения null vs "" для Value | 🟡 Средняя | ⏳ Открыта |
| ARCH-03 | Архитектура | CancellationToken не пробрасывается | 🟡 Средняя | ⏳ Открыта |
| ARCH-04 | Архитектура | Логика инициализации в Program.Main | 🟢 Низкая | ⏳ Открыта |

---

*Аудит выполнен на основе полного анализа исходного кода проекта `BarkFluff.Configuration` (ветка `dev`).*  
*Предыдущий аудит безопасности: `Backend\BarkFluff.Configuration\SECURITY_AUDIT.md` (март 2026)*
