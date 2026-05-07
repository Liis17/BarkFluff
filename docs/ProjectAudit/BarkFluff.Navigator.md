# Аудит проекта: BarkFluff.Navigator

> **Дата аудита:** 2025  
> **Проект:** `Backend/BarkFluff.Navigator`  
> **Назначение:** Реестр доступных серверов BarkFluff. Принимает регистрации серверов, отдаёт список активных.  
> **Протокол:** gRPC (HTTP/2 plaintext), порт `7010`  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

---

---

## 🔴 Безопасность

---

### 

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

## 🟡 Оптимизация

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
