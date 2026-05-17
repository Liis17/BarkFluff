# Аудит проекта: BarkFluff.Configuration

**Дата аудита:** 2026-07-01  
**Аудитор:** GitHub Copilot (BarkfluffAgent)  
**Ветка:** `dev`  
**Статус:** 🔴 Требует срочных исправлений

---

## 🔴 Безопасность

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

## 🟠 Баги и недоработки

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



## 

--- 

### 

### ---

```csharp
// ✅ Scoped — соответствует lifecycle DbContext
builder.Services.AddScoped<ConfigurationStorage>();
```

---

### O

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
