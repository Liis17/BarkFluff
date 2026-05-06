# Аудит проекта: BarkFluff.Beacon

**Дата аудита:** _2026_  
**Ветка:** `dev`  
**Target Framework:** `net9.0`  
**Аудитор:** GitHub Copilot / BarkfluffAgent

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

### ---

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

### 

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

## 🔵 Прочее / Качество кода

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
