# Аудит проекта: BarkFluff.Navigator

> **Дата аудита:** 2025  
> **Проект:** `Backend/BarkFluff.Navigator`  
> **Назначение:** Реестр доступных серверов BarkFluff. Принимает регистрации серверов, отдаёт список активных.  
> **Протокол:** gRPC (HTTP/2 plaintext), порт `7010`  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Оптимизация](#-оптимизация)
- [🟠 Баги](#-баги)
- [🔵 Прочее / Технический долг](#-прочее--технический-долг)

---

## 🔴 Безопасность

---

### SEC-01 — JWT Secret Key захардкожен в appsettings.json

**Проблема / Описание**  
Секретный ключ подписи JWT хранится в открытом тексте прямо в `appsettings.json`, который коммитится в git-репозиторий. Любой, кто имеет доступ к репозиторию, может подписывать произвольные токены и выдавать себя за любого пользователя.

**Конкретно в чём проблема**  
Секрет виден в репозитории, попадает в Docker-образ и во все среды одновременно.

**Путь к файлу:** `Backend/BarkFluff.Navigator/appsettings.json` : строки 12–16

```json
// ❌ Секрет в открытом виде в файле конфигурации
"JwtSettings": {
    "SecretKey": "JKASDFHJKKEF8w7728JHFDWHJJWEF23423489FJJFD7#&@93hHFHFF",
    "Issuer": "BarkFluffNavigator",
    "Audience": "BarkFluffMicroservices"
}
```

**Варианты решения**

1. Вынести в переменную окружения и читать через `Environment.GetEnvironmentVariable`
2. Использовать `.NET User Secrets` для dev-окружения (`dotnet user-secrets set`)
3. В prod — использовать `Docker Secrets` / `Vault` / `Azure Key Vault`

```json
// ✅ appsettings.json — только заглушка, без реального секрета
"JwtSettings": {
    "SecretKey": "",  // значение берётся из переменной окружения JWTsettings__SECRETKEY
    "Issuer": "BarkFluffNavigator",
    "Audience": "BarkFluffMicroservices"
}
```

```yaml
# ✅ docker-compose-dev.yml — передача через environment
environment:
  - JwtSettings__SecretKey=${NAVIGATOR_JWT_SECRET}
```

---

### SEC-02 — Открытый публичный эндпоинт RegisterServer без авторизации и rate limiting

**Проблема / Описание**  
Метод `RegisterServer` доступен анонимно. Атрибут `[Authorize]` отсутствует. Любой внешний клиент может зарегистрировать произвольный фейковый сервер, засорить реестр или организовать DoS-атаку на in-memory хранилище.

**Конкретно в чём проблема**  
Throttling реализован только per-key (по имени+хосту+порту), поэтому злоумышленник может регистрировать бесчисленное множество уникальных серверов без ограничений.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Host/NavigatorApiService.cs` : строки 31–51  
**Путь к файлу:** `Backend/BarkFluff.Navigator/Program.cs` : строки 41–43

```csharp
// ❌ Нет атрибута [Authorize], нет глобального rate limit
public override async Task<RegisterServerResponse> RegisterServer(
    RegisterServerRequest request, ServerCallContext context)
{
    // AddedBy = "Anonymous" если нет токена — любой может зарегистрировать что угодно
    AddedBy = _userContext.IsAuthenticated ? _userContext.UserId.ToString() : "Anonymous"
    ...
}
```

**Варианты решения**

1. Добавить `[Authorize]` на метод `RegisterServer` — регистрировать могут только авторизованные пользователи
2. Добавить глобальный IP-based rate limiting через `AspNetCoreRateLimit` или встроенный `RateLimiter` (.NET 7+)
3. Ввести роль/claim `server:register` для управления правами регистрации

```csharp
// ✅ Вариант 1: требовать авторизацию для регистрации
using Microsoft.AspNetCore.Authorization;

[Authorize] // только авторизованные пользователи
public override async Task<RegisterServerResponse> RegisterServer(
    RegisterServerRequest request, ServerCallContext context)
{ ... }
```

```csharp
// ✅ Вариант 2: добавить фиксированный rate limiter в Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("register", cfg =>
    {
        cfg.PermitLimit = 10;
        cfg.Window = TimeSpan.FromMinutes(1);
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        cfg.QueueLimit = 0;
    });
});
```

---

### SEC-03 — Нет валидации и санитизации строковых полей при регистрации сервера

**Проблема / Описание**  
Поля `BeaconHost`, `Name`, `Description`, `ServerPublicName`, `Location`, а также цвета принимаются без ограничения длины и без проверки содержимого. Это открывает вектор для хранения мусора/спама в памяти, а также потенциальных инъекций если данные когда-либо попадут в БД или UI.

**Конкретно в чём проблема**  
`RegisterServerCommandHandler` проверяет только `IsNullOrWhiteSpace`, но не длину и не допустимые символы.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Features/RegisterServer/RegisterServerCommandHandler.cs` : строки 31–65

```csharp
// ❌ Только проверка на пустоту, длина не ограничена
if (string.IsNullOrWhiteSpace(server.BeaconHost))
    throw new BeaconHostEmptyException();

// Нет проверки: server.Name.Length > 100? server.Description.Length > 2000?
// Нет проверки формата BeaconHost (hostname/IP regex)
// Нет проверки формата HEX-цветов (#RRGGBB)
```

**Варианты решения**

1. Добавить явные проверки максимальной длины строк
2. Добавить regex-валидацию `BeaconHost` (hostname или IP)
3. Добавить regex-валидацию HEX-цветов

```csharp
// ✅ Пример дополнительных проверок в RegisterServerCommandHandler
private static readonly Regex HexColorRegex = new(@"^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled);
private static readonly Regex HostnameRegex = new(
    @"^(([a-zA-Z0-9]|[a-zA-Z0-9][a-zA-Z0-9\-]*[a-zA-Z0-9])\.)*([A-Za-z0-9]|[A-Za-z0-9][A-Za-z0-9\-]*[A-Za-z0-9])$",
    RegexOptions.Compiled);

if (server.Name.Length > 64)
    throw new ArgumentException("Имя сервера не должно превышать 64 символа");

if (server.Description.Length > 512)
    throw new ArgumentException("Описание не должно превышать 512 символов");

if (!HostnameRegex.IsMatch(server.BeaconHost) && !IPAddress.TryParse(server.BeaconHost, out _))
    throw new ArgumentException("Некорректный формат BeaconHost");

if (!string.IsNullOrEmpty(server.ColorMainHex) && !HexColorRegex.IsMatch(server.ColorMainHex))
    throw new ArgumentException("Некорректный формат hex-цвета ColorMainHex");
```

---

### SEC-04 — Plaintext HTTP/2 на публичном эндпоинте

**Проблема / Описание**  
Согласно документации Obsidian (`Navigator.md`), публичный эндпоинт `navigator.barkfluff.com:64646` работает как **plaintext HTTP/2** без TLS. Данные между клиентом и сервисом передаются в открытом виде, что открывает возможность MITM-атаки — перехвата или подмены данных о серверах.

**Конкретно в чём проблема**  
В `Program.cs` Kestrel настраивается только с `HttpProtocols.Http2` без TLS. Nginx-прокси может завершать TLS, но это не документировано явно для Navigator.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Program.cs` : строки 14–22

```csharp
// ❌ Только HTTP/2, без TLS
options.ListenAnyIP(dynamicPort, o =>
{
    o.Protocols = HttpProtocols.Http2; // plaintext — нет шифрования
});
```

**Варианты решения**

1. Настроить TLS-терминацию на уровне Nginx (preferred) и явно задокументировать это
2. Включить TLS прямо в Kestrel через сертификат

```csharp
// ✅ Вариант: TLS в Kestrel (если нет Nginx-терминации)
options.ListenAnyIP(dynamicPort, o =>
{
    o.Protocols = HttpProtocols.Http2;
    o.UseHttps("/certs/navigator.pfx", "cert_password"); // или через IConfiguration
});
```

---

## 🟡 Оптимизация

---

### OPT-01 — `GetServers()` итерирует весь словарь при каждом запросе без кэширования результата

**Проблема / Описание**  
При каждом вызове `ListServers` метод `GetServers()` полностью итерирует `ConcurrentDictionary`, фильтрует по времени `lastSeen` и создаёт новый `List<ServerInfo>`. При большом количестве серверов или высокой частоте запросов это создаёт нагрузку на GC и CPU.

**Конкретно в чём проблема**  
Нет кэширования результата фильтрации. Каждый запрос `ListServers` — это полный проход по словарю.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs` : строки 26–40

```csharp
// ❌ Каждый вызов — полная итерация + LINQ + new List<>
public Task<List<ServerInfo>> GetServers()
{
    var now = DateTime.UtcNow;
    if (!_memoryCache.TryGetValue<ConcurrentDictionary<string, (ServerInfo server, DateTime lastSeen)>>(
        ServersCacheKey, out var servers))
    {
        return Task.FromResult(new List<ServerInfo>());
    }

    var activeServers = servers.Values          // итерация всего словаря
        .Where(s => (now - s.lastSeen) <= _serverActivePeriod)  // фильтрация
        .Select(s => s.server)
        .ToList();                              // аллокация нового списка

    return Task.FromResult(activeServers);
}
```

**Варианты решения**

1. Кэшировать результат `GetServers()` на короткое время (например, 5–10 секунд) через `IMemoryCache`
2. При `RegisterServer` инвалидировать кэш результата
3. Хранить отдельно список активных серверов и обновлять его только при изменениях

```csharp
// ✅ Кэширование результата списка активных серверов
private const string ActiveServersCacheKey = "ActiveServersResult";

public Task<List<ServerInfo>> GetServers()
{
    if (_memoryCache.TryGetValue<List<ServerInfo>>(ActiveServersCacheKey, out var cached))
        return Task.FromResult(cached!);

    var now = DateTime.UtcNow;
    if (!_memoryCache.TryGetValue<ConcurrentDictionary<string, (ServerInfo server, DateTime lastSeen)>>(
        ServersCacheKey, out var servers))
    {
        return Task.FromResult(new List<ServerInfo>());
    }

    var activeServers = servers!.Values
        .Where(s => (now - s.lastSeen) <= _serverActivePeriod)
        .Select(s => s.server)
        .ToList();

    // Кэшируем результат на 10 секунд
    _memoryCache.Set(ActiveServersCacheKey, activeServers, TimeSpan.FromSeconds(10));

    return Task.FromResult(activeServers);
}

// При регистрации — инвалидировать кэш результата
public void RegisterServer(ServerInfo server)
{
    ...
    _memoryCache.Remove(ActiveServersCacheKey); // сбросить кэш при изменении
}
```

---

### OPT-02 — `servers.Count()` вместо `servers.Count` на уже материализованном `List<>`

**Проблема / Описание**  
В `ListServersQueryHandler` вызывается `servers.Count()` (LINQ extension method) на объекте типа `List<ServerInfo>`, хотя `List<T>` реализует `.Count` как свойство O(1). Метод `Count()` без специальной оптимизации использует итерацию.

**Конкретно в чём проблема**  
Лишний вызов через LINQ вместо прямого свойства коллекции.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Features/ListServers/ListServersQueryHandler.cs` : строка 30

```csharp
// ❌ LINQ Count() на List<T> — избыточно
_logger.LogInformation(
    "Получен список серверов. Количество: {ServerCount}",
    servers.Count() // лишний вызов, List уже имеет .Count
);
```

**Варианты решения**

Использовать свойство `.Count` напрямую.

```csharp
// ✅ Прямое свойство List<T>.Count — O(1), без аллокаций
_logger.LogInformation(
    "Получен список серверов. Количество: {ServerCount}",
    servers.Count // свойство, не метод
);
```

---

### OPT-03 — `_lastRegistrationTimes` (`ConcurrentDictionary`) растёт неограниченно в памяти

**Проблема / Описание**  
Словарь `_lastRegistrationTimes` хранит время последней регистрации для каждого уникального ключа сервера. Он **никогда не очищается**. Со временем (особенно при наличии злонамеренных запросов с уникальными именами) словарь может бесконтрольно расти и исчерпать память сервиса.

**Конкретно в чём проблема**  
Нет TTL, нет ограничения размера, нет механизма очистки устаревших записей.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs` : строки 12, 44–54, 65

```csharp
// ❌ Словарь растёт вечно — нет очистки
private readonly ConcurrentDictionary<string, DateTime> _lastRegistrationTimes = new();

// При каждой регистрации добавляется запись:
_lastRegistrationTimes.AddOrUpdate(serverKey, now, (key, existing) => now);
// Но никогда не удаляется!
```

**Варианты решения**

1. Периодически запускать фоновую задачу (`IHostedService`) для очистки устаревших записей
2. Использовать `IMemoryCache` с TTL вместо `ConcurrentDictionary` для `_lastRegistrationTimes`

```csharp
// ✅ Вариант: хранить throttle-записи в IMemoryCache с автоматическим истечением
public void RegisterServer(ServerInfo server)
{
    var now = DateTime.UtcNow;
    var serverKey = $"{server.Name}:{server.BeaconHost}:{server.BeaconPort}";
    var throttleKey = $"throttle:{serverKey}";

    if (_memoryCache.TryGetValue<DateTime>(throttleKey, out var lastTime))
    {
        var remaining = _throttlePeriod - (now - lastTime);
        if (remaining > TimeSpan.Zero)
            throw new InvalidOperationException($"Слишком частая регистрация. Подождите {remaining.TotalSeconds:F0} секунд.");
    }

    // Запись автоматически удалится через _throttlePeriod
    _memoryCache.Set(throttleKey, now, _throttlePeriod);

    // ... регистрация сервера
}
```

---

### OPT-04 — `GetServers()` является псевдо-асинхронным методом (sync wrapped in Task)

**Проблема / Описание**  
Метод `GetServers()` объявлен как `Task<List<ServerInfo>>` и завершается через `Task.FromResult(...)`, однако внутри выполняет синхронную работу с `IMemoryCache`. Это не настоящая асинхронность — `await` в вызывающем коде бессмысленен.

**Конкретно в чём проблема**  
Ложная сигнатура асинхронного метода вводит в заблуждение при рефакторинге и не даёт никакого преимущества.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs` : строки 26–40

```csharp
// ❌ Притворяется async, на деле — sync
public Task<List<ServerInfo>> GetServers()
{
    // ... синхронный код ...
    return Task.FromResult(activeServers); // обёртка без пользы
}
```

**Варианты решения**

Либо сделать метод синхронным, либо оставить `Task`-сигнатуру с комментарием (на случай будущей миграции на реальное хранилище).

```csharp
// ✅ Вариант A: синхронный метод (честно)
public List<ServerInfo> GetServers()
{
    var now = DateTime.UtcNow;
    if (!_memoryCache.TryGetValue<ConcurrentDictionary<string, (ServerInfo, DateTime)>>(
        ServersCacheKey, out var servers))
        return [];

    return servers!.Values
        .Where(s => (now - s.lastSeen) <= _serverActivePeriod)
        .Select(s => s.server)
        .ToList();
}

// ✅ Вариант B: оставить Task<>, но явно пометить
/// <remarks>Синхронная реализация. Task-сигнатура зарезервирована для будущей миграции на persistent storage.</remarks>
public Task<List<ServerInfo>> GetServers() { ... }
```

---

## 🟠 Баги

---

### BUG-01 — Мусорные символы в строке исключения (проблема кодировки)

**Проблема / Описание**  
В методе `RegisterServer` при срабатывании throttle бросается исключение с сообщением, содержащим кириллические символы, которые закодированы некорректно — вероятно, файл был сохранён не в UTF-8. В тексте вместо кириллицы — нечитаемые символы (`����`).

**Конкретно в чём проблема**  
Файл `ServersStorage.cs` содержит строку с некорректной кодировкой, что приведёт к нечитаемым сообщениям об ошибках в логах и у клиента.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs` : строка 51

```csharp
// ❌ Строка с испорченной кодировкой (вместо кириллицы — мусор)
throw new InvalidOperationException(
    $"����������� ������� ������� ������. ��������� ������� ����� {(_throttlePeriod - (now - lastTime)).TotalSeconds:F0} ������."
);
```

**Варианты решения**

Пересохранить файл в UTF-8 (без BOM) и исправить строку.

```csharp
// ✅ Корректная строка
throw new InvalidOperationException(
    $"Слишком частая регистрация сервера. Повторная попытка возможна через {(_throttlePeriod - (now - lastTime)).TotalSeconds:F0} секунд."
);
```

---

### BUG-02 — `RegisterServerCommand.Server` не имеет `required` и может быть `null`

**Проблема / Описание**  
Свойство `Server` в `RegisterServerCommand` не помечено как `required` и не инициализировано. При создании команды без передачи `Server` оно будет `null`, что вызовет `NullReferenceException` в обработчике при обращении к `request.Server.BeaconHost` без предварительной проверки.

**Конкретно в чём проблема**  
В `NavigatorApiService.RegisterServer` маппинг берётся из `request.Server` с null-conditional `?.`, но затем `domainServer` передаётся в команду и хендлер напрямую обращается к свойствам без проверки.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Features/RegisterServer/RegisterServerCommand.cs` : строка 9  
**Путь к файлу:** `Backend/BarkFluff.Navigator/Features/RegisterServer/RegisterServerCommandHandler.cs` : строка 22

```csharp
// ❌ Server может быть null — нет required, нет инициализатора
public class RegisterServerCommand : IRequest<RegisterServerResponse>
{
    public ServerInfo Server { get; set; } // nullable warning, no guard
}

// В хендлере:
var server = request.Server; // если null → NRE на следующей строке
_logger.LogInformation("Регистрация сервера '{ServerName}'...", server.Name, ...);
```

**Варианты решения**

Пометить свойство как `required`.

```csharp
// ✅ Явное требование непустого Server
public class RegisterServerCommand : IRequest<RegisterServerResponse>
{
    public required ServerInfo Server { get; set; }
}
```

---

### BUG-03 — `AccountsCount` всегда возвращается как `0`

**Проблема / Описание**  
В proto-контракте `ServerInfo` есть поле `accounts_count`. В `ListServersQueryHandler` при маппинге оно всегда выставляется в `0`. Поле есть в proto, клиент его получает, но оно никогда не содержит реального значения — вводит клиентов в заблуждение.

**Конкретно в чём проблема**  
Либо поле не нужно и его следует убрать из proto, либо нужно его реально заполнять данными.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Features/ListServers/ListServersQueryHandler.cs` : строка 42  
**Путь к файлу:** `Shared/BarkFluff.Proto/navigator_api.proto` : строка 12

```csharp
// ❌ Поле заглушка — всегда 0, хотя отправляется клиенту
new ServerInfo
{
    ...
    AccountsCount = 0, // TODO? заглушка, вводит в заблуждение клиентов
    ...
}
```

**Варианты решения**

1. Убрать поле `accounts_count` из proto до тех пор, пока оно не реализовано
2. Заполнять значение через запрос к соответствующему сервису (Users/Onliner)
3. Задокументировать как "не реализовано" и скрыть на клиентах

```proto
// ✅ Вариант A: убрать поле из proto до реализации
message ServerInfo {
  string name = 1;
  string description = 2;
  // accounts_count убран — не реализовано
  ServiceEndpoint beacon_uri = 4;
  ...
}
```

---

### BUG-04 — `ServerInfo.Id` помечен `[Key]`, но хранилище in-memory и никогда не устанавливается

**Проблема / Описание**  
`Domain/ServerInfo.cs` содержит поле `Id` с атрибутом `[Key]` (EF Core). При этом по документации и коду БД **не используется** — данные хранятся в `IMemoryCache`. Поле `Id` никогда не устанавливается (`= 0` по умолчанию для всех серверов), хотя `[Key]` подразумевает уникальность.

**Конкретно в чём проблема**  
Атрибут `[Key]` вводит в заблуждение (намёк на EF Core), `Id` всегда `0` у всех объектов, `System.ComponentModel.DataAnnotations` подключён без нужды.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Domain/ServerInfo.cs` : строки 3, 7–8

```csharp
// ❌ [Key] без смысла — Id никогда не используется, БД нет
using System.ComponentModel.DataAnnotations;

public class ServerInfo
{
    [Key]          // атрибут EF Core — но EF не используется
    public long Id { get; set; } // всегда 0, никогда не устанавливается
    ...
}
```

**Варианты решения**

Убрать `[Key]` и `Id`, пока не будет реальной персистентности. Если в будущем планируется EF — задокументировать это явно.

```csharp
// ✅ Чистая доменная модель без лишних атрибутов
namespace BarkFluff.Navigator.Domain;

public class ServerInfo
{
    // Id и [Key] убраны — хранилище in-memory, EF не используется
    public required string BeaconHost { get; set; }
    public int BeaconPort { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public string ServerPublicName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ColorLiteHex { get; set; } = string.Empty;
    public string ColorMainHex { get; set; } = string.Empty;
    public string ColorHardHex { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public required string AddedBy { get; set; }
}
```

---

## 🔵 Прочее / Технический долг

---

### MISC-01 — `IMemoryCache` используется как обёртка над `ConcurrentDictionary` без смысла

**Проблема / Описание**  
`ServersStorage` использует `IMemoryCache` исключительно для хранения одного объекта `ConcurrentDictionary` с приоритетом `NeverRemove`. Это архитектурно избыточно: `IMemoryCache` добавляет накладные расходы на поиск по ключу, сериализацию настроек `MemoryCacheEntryOptions` и лишний уровень абстракции. Можно просто хранить `ConcurrentDictionary` как поле класса.

**Конкретно в чём проблема**  
Лишняя зависимость на `IMemoryCache` там, где достаточно поля.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs` : строки 10–11, 55–61

```csharp
// ❌ IMemoryCache ради хранения одного вечного словаря
var servers = _memoryCache.GetOrCreate(ServersCacheKey, entry =>
{
    entry.Priority = CacheItemPriority.NeverRemove; // NeverRemove = фактически поле класса
    return new ConcurrentDictionary<string, (ServerInfo, DateTime)>();
});
```

**Варианты решения**

Хранить `ConcurrentDictionary` напрямую как поле `ServersStorage`.

```csharp
// ✅ Прямое поле — проще, быстрее, понятнее
public class ServersStorage
{
    private readonly ConcurrentDictionary<string, (ServerInfo server, DateTime lastSeen)> _servers = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastRegistrationTimes = new();
    // IMemoryCache больше не нужен для основного хранилища

    public List<ServerInfo> GetServers()
    {
        var now = DateTime.UtcNow;
        return _servers.Values
            .Where(s => (now - s.lastSeen) <= _serverActivePeriod)
            .Select(s => s.server)
            .ToList();
    }
}
```

---

### MISC-02 — Валидация разбита между двумя слоями (Service и Handler) непоследовательно

**Проблема / Описание**  
Часть валидации выполняется в `NavigatorApiService` (маппинг с `?? string.Empty`, `?? 0`), а другая часть — в `RegisterServerCommandHandler`. Это нарушает принцип единственной ответственности: сервис подменяет null-значения заглушками вместо того, чтобы явно отвергать некорректный запрос, а хендлер потом всё равно проверяет на пустоту.

**Конкретно в чём проблема**  
`BeaconHost = protoServer?.BeaconUri?.Host ?? string.Empty` в сервисе скрывает отсутствие хоста, затем хендлер бросает `BeaconHostEmptyException`. Непрозрачный поток.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Host/NavigatorApiService.cs` : строки 33–44  
**Путь к файлу:** `Backend/BarkFluff.Navigator/Features/RegisterServer/RegisterServerCommandHandler.cs` : строки 31–65

```csharp
// ❌ В сервисе — тихая подстановка пустой строки
BeaconHost = protoServer?.BeaconUri?.Host ?? string.Empty, // скрывает null

// ❌ В хендлере — проверка на пустоту того, что уже было подменено
if (string.IsNullOrWhiteSpace(server.BeaconHost))
    throw new BeaconHostEmptyException(); // до сюда дойдёт из-за ?? string.Empty выше
```

**Варианты решения**

Вынести всю валидацию входящего proto-запроса в один слой — либо в сервис (ранняя валидация proto), либо целиком в хендлер. Рекомендуется ранняя валидация в сервисе с чёткими gRPC-статусами.

```csharp
// ✅ Ранняя валидация в NavigatorApiService с явным gRPC-статусом
if (protoServer?.BeaconUri == null || string.IsNullOrWhiteSpace(protoServer.BeaconUri.Host))
    throw new RpcException(new Status(StatusCode.InvalidArgument, "BeaconUri.Host обязателен"));

if (string.IsNullOrWhiteSpace(protoServer.Name))
    throw new RpcException(new Status(StatusCode.InvalidArgument, "Name обязателен"));

// Маппинг только после прохождения валидации
var domainServer = new ServerInfo
{
    BeaconHost = protoServer.BeaconUri.Host,
    BeaconPort = protoServer.BeaconUri.Port,
    Name = protoServer.Name,
    ...
};
```

---

### MISC-03 — gRPC Reflection включён в production-окружении

**Проблема / Описание**  
`AddGrpcReflection()` и `MapGrpcReflectionService()` зарегистрированы безусловно, без проверки среды. В production gRPC Reflection позволяет любому клиенту (например, `grpcurl`) автоматически обнаружить все методы сервиса, их сигнатуры и описания. Это облегчает разведку при атаке.

**Конкретно в чём проблема**  
Reflection не ограничен dev/staging окружением.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Program.cs` : строки 28, 40

```csharp
// ❌ Reflection включён всегда, в том числе в prod
builder.Services.AddGrpcReflection(); // регистрация

// ...

app.MapGrpcReflectionService(); // маппинг без условия
```

**Варианты решения**

Ограничить Reflection только non-production окружением.

```csharp
// ✅ Reflection только в dev/staging
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.MapGrpcReflectionService();
}
```

---

### MISC-04 — Отсутствует health check эндпоинт

**Проблема / Описание**  
Сервис не регистрирует никакого health check. В Docker Compose и оркестраторах (Kubernetes, Nomad) health check необходим для автоматического перезапуска при сбое и корректного управления трафиком. Без него оркестратор не знает о состоянии сервиса.

**Конкретно в чём проблема**  
В `Program.cs` нет `AddHealthChecks()` / `MapHealthChecks()`.

**Путь к файлу:** `Backend/BarkFluff.Navigator/Program.cs`  
**Путь к файлу:** `Backend/BarkFluff.Navigator/docker-compose-dev.yml`

```csharp
// ❌ Нет health check
var app = builder.Build();
app.MapGrpcReflectionService();
app.UseRouting();
app.UseXAuth();
app.MapGrpcService<NavigatorApiService>();
app.Run(); // нет MapHealthChecks
```

**Варианты решения**

```csharp
// ✅ Регистрация health check
builder.Services.AddHealthChecks(); // в секции Services

// ...

app.MapHealthChecks("/health"); // после app.Build()
```

```yaml
# ✅ docker-compose-dev.yml — healthcheck секция
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:7010/health"]
  interval: 30s
  timeout: 5s
  retries: 3
```

---

## Сводная таблица

| ID | Категория | Название | Критичность |
|----|-----------|----------|-------------|
| SEC-01 | 🔴 Безопасность | JWT Secret в appsettings.json | **Критическая** |
| SEC-02 | 🔴 Безопасность | RegisterServer без авторизации и rate limit | **Высокая** |
| SEC-03 | 🔴 Безопасность | Нет валидации длины и формата полей | **Средняя** |
| SEC-04 | 🔴 Безопасность | Plaintext HTTP/2 на публичном эндпоинте | **Средняя** |
| OPT-01 | 🟡 Оптимизация | Нет кэширования результата GetServers() | **Средняя** |
| OPT-02 | 🟡 Оптимизация | Count() вместо .Count на List<T> | **Низкая** |
| OPT-03 | 🟡 Оптимизация | _lastRegistrationTimes растёт неограниченно | **Средняя** |
| OPT-04 | 🟡 Оптимизация | GetServers() псевдо-асинхронный метод | **Низкая** |
| BUG-01 | 🟠 Баги | Мусорные символы в строке исключения | **Высокая** |
| BUG-02 | 🟠 Баги | RegisterServerCommand.Server не required | **Средняя** |
| BUG-03 | 🟠 Баги | AccountsCount всегда 0 | **Низкая** |
| BUG-04 | 🟠 Баги | [Key] и Id без смысла (нет БД) | **Низкая** |
| MISC-01 | 🔵 Техдолг | IMemoryCache как обёртка над полем | **Низкая** |
| MISC-02 | 🔵 Техдолг | Валидация размазана по двум слоям | **Средняя** |
| MISC-03 | 🔵 Техдолг | gRPC Reflection в production | **Средняя** |
| MISC-04 | 🔵 Техдолг | Нет health check эндпоинта | **Средняя** |
