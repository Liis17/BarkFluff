# Аудит проекта: BarkFluff.GrpcServer

> **Дата аудита:** 2026-05-06
> **Проект:** `Backend/BarkFluff.GrpcServer`
> **Target Framework:** `net9.0`
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

---

## 🟡 Оптимизация

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

## 🔵 Прочее / Качество кода

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
> 
> - `RequestContextInterceptor.cs` строка 35: `requestContext.OperationSystem = ...`
