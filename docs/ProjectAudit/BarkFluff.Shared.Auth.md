# Аудит проекта: BarkFluff.Shared.Auth

> **Дата аудита:** 2025  
> **Проект:** `Shared\BarkFluff.Shared.Auth`  
> **Target Framework:** `net9.0`  
> **Зависимости:** `Grpc.Core.Api 2.71.0`

---

## Содержание

- [🔴 Безопасность](#-безопасность)
  - [SEC-01 — Клиент самостоятельно передаёт собственный IP-адрес](#sec-01--клиент-самостоятельно-передаёт-собственный-ip-адрес)
  - [SEC-02 — JWT-токен передаётся в произвольном метаданных-заголовке, а не в стандартном Authorization](#sec-02--jwt-токен-передаётся-в-произвольном-метаданных-заголовке-а-не-в-стандартном-authorization)
  - [SEC-03 — Отсутствует валидация входных данных в конструкторах интерсепторов](#sec-03--отсутствует-валидация-входных-данных-в-конструкторах-интерсепторов)
  - [SEC-04 — Base64 — не шифрование: ложное ощущение защиты данных](#sec-04--base64--не-шифрование-ложное-ощущение-защиты-данных)
  - [SEC-05 — Серверный приоритет IP из клиентского метаданных выше X-Forwarded-For](#sec-05--серверный-приоритет-ip-из-клиентского-метаданных-выше-x-forwarded-for)
- [🟡 Производительность](#-производительность)
  - [PERF-01 — Base64-кодирование выполняется на каждый вызов вместо кэширования](#perf-01--base64-кодирование-выполняется-на-каждый-вызов-вместо-кэширования)
  - [PERF-02 — Цепочка из 7 отдельных `.Intercept()` вместо одного составного интерсептора](#perf-02--цепочка-из-7-отдельных-intercept-вместо-одного-составного-интерсептора)
  - [PERF-03 — `metadata.FirstOrDefault` с линейным поиском на сервере при каждом запросе](#perf-03--metadatafirstordefault-с-линейным-поиском-на-сервере-при-каждом-запросе)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
  - [BUG-01 — Опечатка в переменной `osName` в `XDeviceIdInterceptor`](#bug-01--опечатка-в-переменной-osname-в-xdeviceidinterceptor)
  - [BUG-02 — Только `AsyncUnaryCall` переопределён — стриминговые методы gRPC не получают метаданные](#bug-02--только-asyncunarycall-переопределён--стриминговые-методы-grpc-не-получают-метаданные)
  - [BUG-03 — `MetadataKeys` — не `static`, можно создать экземпляр](#bug-03--metadatakeys--не-static-можно-создать-экземпляр)
  - [BUG-04 — Отсутствует обработка исключений при декодировании Base64 на сервере](#bug-04--отсутствует-обработка-исключений-при-декодировании-base64-на-сервере)
- [🔵 Архитектура и качество кода](#-архитектура-и-качество-кода)
  - [ARCH-01 — Дублирование кода: 5 почти идентичных интерсепторов](#arch-01--дублирование-кода-5-почти-идентичных-интерсепторов)
  - [ARCH-02 — Интерсепторы создаются через `new` вместо DI-контейнера](#arch-02--интерсепторы-создаются-через-new-вместо-di-контейнера)
  - [ARCH-03 — Несогласованное именование: `XDeviceIdInterceptor` vs `XDeviceClientInterceptor`](#arch-03--несогласованное-именование-xdeviceidinterceptor-vs-xdeviceclientinterceptor)

---

## 🔴 Безопасность

---

### SEC-01 — Клиент самостоятельно передаёт собственный IP-адрес

**Проблема / Описание**  
`XIpClientInterceptor` позволяет клиентскому приложению указывать произвольный IP-адрес в метаданных gRPC-запроса. Сервер (`RequestContextInterceptor`) берёт этот IP с **наивысшим приоритетом** — выше `X-Forwarded-For` и реального `RemoteIpAddress`.

**Конкретно в чём проблема**  
Любой клиент может подделать свой IP-адрес, передав произвольное значение. Это делает IP-based rate limiting, гео-блокировку, журналирование и расследование инцидентов ненадёжными.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\XIpClientInterceptor.cs` : 1–35  
**Связанный файл:** `Backend\BarkFluff.GrpcServer\Tracker\RequestContextInterceptor.cs` : 65–75

```csharp
// XIpClientInterceptor.cs
// ❌ Клиент сам формирует IP — сервер ему доверяет полностью
var ipAddress = Convert.ToBase64String(Encoding.UTF8.GetBytes(_ipAddr)); // может быть "1.1.1.1"
metadata.Add(MetadataKeys.IpAddress, ipAddress);

// RequestContextInterceptor.cs
// ❌ Клиентский IP имеет ВЫСШИЙ приоритет — это дыра в безопасности
var clientIp = GetMetadataValue(metadata, MetadataKeys.IpAddress);
if (!string.IsNullOrWhiteSpace(clientIp))
    return clientIp; // сервер доверяет тому, что сказал клиент
```

**Варианты решения**

1. **Полностью удалить `XIpClientInterceptor`** — IP должен определяться только на сервере из реального соединения.
2. **Понизить приоритет** клиентского IP до самого последнего (только как fallback или убрать вообще).

```csharp
// ✅ Вариант: удалить клиентский IP из метаданных,
// сервер определяет IP только из надёжных источников

private string? ResolveIpAddress(HttpContext? httpContext)
{
    // 1. X-Forwarded-For от доверенного reverse-proxy
    var forwardedFor = httpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwardedFor))
    {
        var firstIp = forwardedFor.Split(',')[0].Trim();
        if (!string.IsNullOrWhiteSpace(firstIp))
            return firstIp;
    }

    // 2. X-Real-IP от nginx
    var realIp = httpContext?.Request.Headers["X-Real-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(realIp))
        return realIp;

    // 3. Прямой IP соединения (самый надёжный)
    var remoteIp = httpContext?.Connection?.RemoteIpAddress;
    if (remoteIp == null) return null;
    if (remoteIp.IsIPv4MappedToIPv6) remoteIp = remoteIp.MapToIPv4();
    return remoteIp.ToString();
}
```

---

### SEC-02 — JWT-токен передаётся в произвольном метаданных-заголовке, а не в стандартном Authorization

**Проблема / Описание**  
Токен передаётся под ключом `x-auth-token` через `JwtClientInterceptor`. Это нестандартный заголовок, который не совместим с ASP.NET Core Bearer-аутентификацией, промежуточным ПО (middleware), инструментами аудита и стандартными gRPC auth-фреймворками.

**Конкретно в чём проблеме**  
- Нет проверки срока действия токена перед отправкой — протухший токен уйдёт на сервер.
- Токен передаётся даже при пустом значении (`string.Empty`), то есть неаутентифицированные запросы отправляют пустой заголовок вместо его отсутствия.
- Не используется `AuthorizationPolicy` и стандартный `Bearer`.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\JwtClientInterceptor.cs` : 1–30

```csharp
// ❌ Пустой токен всё равно добавляется в метаданные
var token = string.Empty; // когда AccessToken == null
// ...
metadata.Add(MetadataKeys.Token, _token); // отправляем пустую строку
```

**Варианты решения**

```csharp
// ✅ Не добавлять заголовок если токен пуст
// ✅ Использовать стандартный заголовок Authorization: Bearer <token>
public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
    TRequest request,
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
{
    if (string.IsNullOrWhiteSpace(_token))
        return continuation(request, context); // ← не добавляем заголовок совсем

    var metadata = context.Options.Headers ?? new Metadata();
    // ✅ Стандартный заголовок совместимый с ASP.NET Core Bearer middleware
    metadata.Add("authorization", $"Bearer {_token}");

    var newContext = new ClientInterceptorContext<TRequest, TResponse>(
        context.Method,
        context.Host,
        context.Options.WithHeaders(metadata));

    return continuation(request, newContext);
}
```

---

### SEC-03 — Отсутствует валидация входных данных в конструкторах интерсепторов

**Проблема / Описание**  
Все интерсепторы принимают строковые параметры в конструкторах без какой-либо валидации. Передача `null` вызовет `NullReferenceException` или `ArgumentNullException` в рантайме при первом же запросе, а не в момент конфигурации.

**Конкретно в чём проблема**  
Ошибка конфигурации проявится не при старте приложения, а в произвольный момент при выполнении gRPC-вызова, что затрудняет диагностику.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\JwtClientInterceptor.cs` : 9–13  
*(аналогично во всех остальных интерсепторах)*

```csharp
// ❌ null пройдёт в конструктор без исключения
public JwtClientInterceptor(string token)
{
    _token = token; // если token == null — упадёт позже при Convert.ToBase64String
}
```

**Варианты решения**

```csharp
// ✅ Явная guard-проверка при конструировании
public JwtClientInterceptor(string token)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(token, nameof(token));
    _token = token;
}

// ✅ Или для опциональных значений — использовать nullable + условная отправка:
public XDeviceClientInterceptor(string? deviceName)
{
    _deviceName = deviceName ?? string.Empty;
}
```

---

### SEC-04 — Base64 — не шифрование: ложное ощущение защиты данных

**Проблема / Описание**  
Строковые значения (`deviceName`, `osName`, `appName`, `appVersion`, `deviceId`, `ip`) кодируются в Base64 перед отправкой. Base64 — это **кодирование**, а не **шифрование**. Любой наблюдатель трафика (даже без TLS) тривиально декодирует эти значения.

**Конкретно в чём проблема**  
- Создаёт ложное ощущение защищённости.
- Добавляет лишний CPU-overhead без реальной пользы для безопасности.
- Если канал не использует TLS — данные открыты.
- Если использует TLS — Base64 бесполезен сверху.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\XDeviceClientInterceptor.cs` : 24  
*(аналогично во всех интерсепторах кроме `JwtClientInterceptor`)*

```csharp
// ❌ Base64 — не защита, это просто другой формат строки
var deviceName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_deviceName));
// Любой: Convert.FromBase64String("ZGVza3RvcA==") → "desktop"
```

**Варианты решения**

```csharp
// ✅ Вариант 1: Передавать строку напрямую (если канал защищён TLS)
metadata.Add(MetadataKeys.DeviceName, _deviceName);

// ✅ Вариант 2: Если Base64 нужен для совместимости с gRPC ASCII-заголовками
// (метаданные могут содержать не-ASCII символы) — документировать это явно
// и использовать суффикс "-bin" в ключе (стандарт gRPC для бинарных данных):
metadata.Add("x-device-name-bin", Encoding.UTF8.GetBytes(_deviceName)); // gRPC binary metadata
```

---

### SEC-05 — Серверный приоритет IP из клиентского метаданных выше X-Forwarded-For

> ⚠️ Связан с **SEC-01**. Описан отдельно т.к. касается серверной логики.

**Путь к файлу:** `Backend\BarkFluff.GrpcServer\Tracker\RequestContextInterceptor.cs` : 65–75

Подробное описание и решение — см. **SEC-01**.

---

## 🟡 Производительность

---

### PERF-01 — Base64-кодирование выполняется на каждый вызов вместо кэширования

**Проблема / Описание**  
Значения `deviceName`, `osName`, `appName`, `appVersion`, `deviceId`, `ip` **не меняются** на протяжении жизни интерсептора (хранятся как `readonly` поля). Однако `Convert.ToBase64String(Encoding.UTF8.GetBytes(...))` выполняется заново при **каждом gRPC-вызове**, производя лишние аллокации строк и нагружая GC.

**Конкретно в чём проблема**  
При интенсивном использовании (сотни запросов в секунду) это создаёт постоянное давление на GC без какого-либо функционального смысла — результат всегда одинаковый.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\XAppClientInterceptor.cs` : 25–26

```csharp
// ❌ Одинаковое вычисление на КАЖДЫЙ вызов — лишние аллокации
public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(...)
{
    var appName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_appName));    // лишнее
    var appVersion = Convert.ToBase64String(Encoding.UTF8.GetBytes(_appVersion)); // лишнее
    // ...
}
```

**Варианты решения**

```csharp
// ✅ Вычислить один раз в конструкторе и закэшировать
public class XAppClientInterceptor : Interceptor
{
    private readonly string _appNameEncoded;
    private readonly string _appVersionEncoded;

    public XAppClientInterceptor(string appName, string appVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName, nameof(appName));
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion, nameof(appVersion));

        // ✅ Вычисляется один раз при создании объекта
        _appNameEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(appName));
        _appVersionEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(appVersion));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add(MetadataKeys.AppName, _appNameEncoded);    // ✅ уже готово
        metadata.Add(MetadataKeys.AppVersion, _appVersionEncoded); // ✅ уже готово
        // ...
    }
}
```

---

### PERF-02 — Цепочка из 7 отдельных `.Intercept()` вместо одного составного интерсептора

**Проблема / Описание**  
В `WebApiClientManager` каждый канал оборачивается в **7 вложенных** вызовов `.Intercept()`, создавая глубокую цепочку делегирования. Каждый `Intercept` создаёт новый `InterceptingCallInvoker`. При 7 каналах это 49 объектов-обёрток, и каждый gRPC-вызов проходит через 7 уровней виртуальной диспетчеризации.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiClientManager.cs` : 147–152

```csharp
// ❌ 7 вложенных оберток на каждый из 7 каналов = 49 объектов
var identityInvoker = _webApi.IdentityChannel
    .Intercept(deviceInterceptor)
    .Intercept(deviceIdInterceptor)
    .Intercept(jwtInterceptor)
    .Intercept(osInterceptor)
    .Intercept(appInterceptor)
    .Intercept(errorInterceptor)
    .Intercept(ipInterceptor); // ← 7 уровней вложенности
```

**Варианты решения**

```csharp
// ✅ Использовать перегрузку Intercept принимающую params массив интерсепторов
// Grpc.Core.Api поддерживает: channel.Intercept(i1, i2, i3, ...)
var invoker = channel.Intercept(
    deviceInterceptor,
    deviceIdInterceptor,
    jwtInterceptor,
    osInterceptor,
    appInterceptor,
    errorInterceptor,
    ipInterceptor
);

// ✅ Или объединить все auth-интерсепторы в один CompositeAuthInterceptor
// который добавляет все метаданные за один проход:
public class CompositeAuthInterceptor : Interceptor
{
    private readonly string _tokenEncoded;
    private readonly string _deviceNameEncoded;
    private readonly string _deviceIdEncoded;
    private readonly string _osNameEncoded;
    private readonly string _appNameEncoded;
    private readonly string _appVersionEncoded;

    // конструктор принимает все параметры, кэширует Base64 один раз
    public CompositeAuthInterceptor(string token, string deviceName, string deviceId,
        string osName, string appName, string appVersion)
    {
        _tokenEncoded = token; // токен не кодируем — он уже JWT
        _deviceNameEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(deviceName));
        _deviceIdEncoded   = Convert.ToBase64String(Encoding.UTF8.GetBytes(deviceId));
        _osNameEncoded     = Convert.ToBase64String(Encoding.UTF8.GetBytes(osName));
        _appNameEncoded    = Convert.ToBase64String(Encoding.UTF8.GetBytes(appName));
        _appVersionEncoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(appVersion));
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request, ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();

        if (!string.IsNullOrWhiteSpace(_tokenEncoded))
            metadata.Add(MetadataKeys.Token, _tokenEncoded);

        metadata.Add(MetadataKeys.DeviceName,   _deviceNameEncoded);
        metadata.Add(MetadataKeys.DeviceId,     _deviceIdEncoded);
        metadata.Add(MetadataKeys.OsName,       _osNameEncoded);
        metadata.Add(MetadataKeys.AppName,      _appNameEncoded);
        metadata.Add(MetadataKeys.AppVersion,   _appVersionEncoded);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}
```

---

### PERF-03 — `metadata.FirstOrDefault` с линейным поиском на сервере при каждом запросе

**Проблема / Описание**  
Метод `GetMetadataValue` на сервере использует `FirstOrDefault` с `StringComparison.OrdinalIgnoreCase` — O(n) поиск по списку метаданных. Вызывается 6 раз на каждый запрос (для каждого ключа), итого 6 × n итераций.

**Путь к файлу:** `Backend\BarkFluff.GrpcServer\Tracker\RequestContextInterceptor.cs` : 106–116

```csharp
// ❌ Линейный поиск — вызывается 6 раз подряд для каждого запроса
private string? GetMetadataValue(Metadata metadata, string key)
{
    var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    // ...
}
```

**Варианты решения**

```csharp
// ✅ Обходим metadata один раз и заполняем словарь
private static Dictionary<string, string?> ParseMetadata(Metadata metadata)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (var entry in metadata)
    {
        if (!result.ContainsKey(entry.Key))
            result[entry.Key] = entry.Value;
    }
    return result;
}

// Использование в UnaryServerHandler:
var meta = ParseMetadata(context.RequestHeaders);
requestContext.DeviceName        = DecodeBase64(meta.GetValueOrDefault(MetadataKeys.DeviceName));
requestContext.OperationSystem   = DecodeBase64(meta.GetValueOrDefault(MetadataKeys.OsName));
requestContext.AppName           = DecodeBase64(meta.GetValueOrDefault(MetadataKeys.AppName));
requestContext.AppVersion        = DecodeBase64(meta.GetValueOrDefault(MetadataKeys.AppVersion));
requestContext.DeviceId          = DecodeBase64(meta.GetValueOrDefault(MetadataKeys.DeviceId));
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — Опечатка в переменной `osName` в `XDeviceIdInterceptor`

**Проблема / Описание**  
В `XDeviceIdInterceptor` локальная переменная, содержащая закодированный `deviceId`, названа `osName` — это явная опечатка, скопированная из другого интерсептора. Код работает корректно (значение всё равно добавляется под правильным ключом), но серьёзно вводит в заблуждение при чтении и поддержке.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\XDeviceIdInterceptor.cs` : 24–26

```csharp
// ❌ Переменная названа osName, но содержит deviceId — опечатка
var osName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_deviceId)); // ← WTF?
metadata.Add(MetadataKeys.DeviceId, osName); // добавляется правильно, но имя переменной — ложь
```

**Варианты решения**

```csharp
// ✅ Переименовать переменную корректно
var deviceId = Convert.ToBase64String(Encoding.UTF8.GetBytes(_deviceId));
metadata.Add(MetadataKeys.DeviceId, deviceId);
```

---

### BUG-02 — Только `AsyncUnaryCall` переопределён — стриминговые методы gRPC не получают метаданные

**Проблема / Описание**  
Все интерсепторы переопределяют **только** `AsyncUnaryCall`. Если в проекте используются или будут использоваться gRPC-стримы (`AsyncClientStreamingCall`, `AsyncServerStreamingCall`, `AsyncDuplexStreamingCall`), они **не получат** ни токена, ни device-метаданных. Это скрытая бомба при расширении API.

**Конкретно в чём проблема**  
Пример: `UpdatesAC` или `OnlinerAC` могут использовать серверный стриминг для получения обновлений в реальном времени — без аутентификации.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\JwtClientInterceptor.cs` : 14–29  
*(аналогично во всех интерсепторах)*

```csharp
// ❌ Только Unary — остальные типы вызовов не перехватываются
public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(...)
{
    // только этот метод реализован
}
// AsyncClientStreamingCall — НЕ переопределён → нет токена
// AsyncServerStreamingCall — НЕ переопределён → нет токена
// AsyncDuplexStreamingCall — НЕ переопределён → нет токена
```

**Варианты решения**

```csharp
// ✅ Переопределить все типы вызовов (или использовать CompositeAuthInterceptor из PERF-02)
public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
    TRequest request,
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
{
    var newContext = BuildContextWithAuth(context);
    return continuation(request, newContext);
}

public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
{
    var newContext = BuildContextWithAuth(context);
    return continuation(newContext);
}

public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
{
    var newContext = BuildContextWithAuth(context);
    return continuation(newContext);
}

// Вспомогательный метод для формирования контекста с метаданными:
private ClientInterceptorContext<TRequest, TResponse> BuildContextWithAuth<TRequest, TResponse>(
    ClientInterceptorContext<TRequest, TResponse> context)
    where TRequest : class where TResponse : class
{
    var metadata = context.Options.Headers ?? new Metadata();
    if (!string.IsNullOrWhiteSpace(_token))
        metadata.Add(MetadataKeys.Token, _token);
    return new ClientInterceptorContext<TRequest, TResponse>(
        context.Method, context.Host,
        context.Options.WithHeaders(metadata));
}
```

---

### BUG-03 — `MetadataKeys` — не `static`, можно создать экземпляр

**Проблема / Описание**  
Класс `MetadataKeys` содержит только `const` поля, но объявлен как обычный `class`. Это позволяет создать бессмысленный экземпляр `new MetadataKeys()`, что семантически некорректно для класса-контейнера констант.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\MetadataKeys.cs` : 3

```csharp
// ❌ Обычный класс с только константами — можно создать бесполезный экземпляр
public class MetadataKeys
{
    public const string Token = "x-auth-token";
    // ...
}

// Никто не запрещает: var keys = new MetadataKeys(); // бессмысленно
```

**Варианты решения**

```csharp
// ✅ Сделать статическим — невозможно создать экземпляр, намерение очевидно
public static class MetadataKeys
{
    public const string Token = "x-auth-token";
    public const string DeviceName = "x-device-name";
    public const string OsName = "x-os-name";
    public const string AppName = "x-app-name";
    public const string AppVersion = "x-app-version";
    public const string IpAddress = "x-ip-address";
    public const string DeviceId = "x-device-id";
}
```

---

### BUG-04 — Отсутствует обработка исключений при декодировании Base64 на сервере

**Проблема / Описание**  
Метод `GetMetadataValue` на сервере вызывает `Convert.FromBase64String(base64)` без try/catch. Если клиент (или злоумышленник) передаст невалидный Base64 в любом из заголовков, сервер выбросит необработанное `FormatException`, которое всплывёт в gRPC-обработчике как Internal Server Error (код 13).

**Конкретно в чём проблема**  
Это может использоваться для DoS-атаки или для сбора информации об ошибках сервера.

**Путь к файлу:** `Backend\BarkFluff.GrpcServer\Tracker\RequestContextInterceptor.cs` : 106–116

```csharp
// ❌ Нет защиты от невалидного Base64 — выбросит FormatException
private string? GetMetadataValue(Metadata metadata, string key)
{
    var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    var base64 = entry?.Value;
    if (string.IsNullOrEmpty(base64)) return null;

    return Encoding.UTF8.GetString(Convert.FromBase64String(base64)); // ← БОМБА
}
```

**Варианты решения**

```csharp
// ✅ Обернуть в try/catch с логированием и graceful-возвратом null
private string? GetMetadataValue(Metadata metadata, string key)
{
    var entry = metadata.FirstOrDefault(m => m.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    var base64 = entry?.Value;

    if (string.IsNullOrEmpty(base64)) return null;

    try
    {
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
    catch (FormatException ex)
    {
        // ✅ Логируем подозрительный запрос, но не падаем
        _logger.LogWarning(ex, "Невалидное Base64 значение в метаданных для ключа '{Key}'", key);
        return null;
    }
}
```

---

## 🔵 Архитектура и качество кода

---

### ARCH-01 — Дублирование кода: 5 почти идентичных интерсепторов

**Проблема / Описание**  
`XDeviceClientInterceptor`, `XDeviceIdInterceptor`, `XIpClientInterceptor`, `XOsClientInterceptor`, `XAppClientInterceptor` — по сути один и тот же шаблон кода (Base64 + добавить в metadata), повторённый 5 раз. Любое изменение логики требует правки в 5 местах. Это классическое нарушение принципа DRY.

**Путь к файлам:**  
- `Shared\BarkFluff.Shared.Auth\XDeviceClientInterceptor.cs`  
- `Shared\BarkFluff.Shared.Auth\XDeviceIdInterceptor.cs`  
- `Shared\BarkFluff.Shared.Auth\XIpClientInterceptor.cs`  
- `Shared\BarkFluff.Shared.Auth\XOsClientInterceptor.cs`  
- `Shared\BarkFluff.Shared.Auth\XAppClientInterceptor.cs`

```csharp
// ❌ XDeviceClientInterceptor.cs — строки 22-32
var deviceName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_deviceName));
metadata.Add(MetadataKeys.DeviceName, deviceName);

// ❌ XOsClientInterceptor.cs — строки 22-32 (ИДЕНТИЧНО)
var osName = Convert.ToBase64String(Encoding.UTF8.GetBytes(_osName));
metadata.Add(MetadataKeys.OsName, osName);

// ❌ XIpClientInterceptor.cs — строки 22-32 (ИДЕНТИЧНО)
var ipAddress = Convert.ToBase64String(Encoding.UTF8.GetBytes(_ipAddr));
metadata.Add(MetadataKeys.IpAddress, ipAddress);
// ... и ещё два таких же файла
```

**Варианты решения**

```csharp
// ✅ Единый CompositeAuthInterceptor (см. PERF-02) объединяет все в одном классе
// ✅ Или базовый абстрактный класс для single-value интерсепторов:

public abstract class SingleValueMetadataInterceptor : Interceptor
{
    private readonly string _encodedValue;
    private readonly string _metadataKey;

    protected SingleValueMetadataInterceptor(string value, string metadataKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
        _encodedValue = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        _metadataKey  = metadataKey;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var metadata = context.Options.Headers ?? new Metadata();
        metadata.Add(_metadataKey, _encodedValue);

        var newContext = new ClientInterceptorContext<TRequest, TResponse>(
            context.Method, context.Host,
            context.Options.WithHeaders(metadata));

        return continuation(request, newContext);
    }
}

// Тогда каждый конкретный интерсептор — 5 строк:
public class XDeviceClientInterceptor(string deviceName)
    : SingleValueMetadataInterceptor(deviceName, MetadataKeys.DeviceName);

public class XDeviceIdInterceptor(string deviceId)
    : SingleValueMetadataInterceptor(deviceId, MetadataKeys.DeviceId);

public class XOsClientInterceptor(string osName)
    : SingleValueMetadataInterceptor(osName, MetadataKeys.OsName);

public class XIpClientInterceptor(string ip)
    : SingleValueMetadataInterceptor(ip, MetadataKeys.IpAddress);
```

---

### ARCH-02 — Интерсепторы создаются через `new` вместо DI-контейнера

**Проблема / Описание**  
В `WebApiClientManager.AddInterceptor()` все 7 интерсепторов создаются через `new`. Это нарушает принцип инверсии зависимостей (DIP), делает невозможным unit-тестирование и мокирование, а также усложняет конфигурацию.

**Путь к файлу:** `Windows\BarkFluff.WebApi.Core\Managers\WebApiClientManager.cs` : 120–127

```csharp
// ❌ Жёсткое создание через new — нет DI, нет тестируемости
var deviceInterceptor   = new Shared.Auth.XDeviceClientInterceptor(deviceName: _deviceName);
var deviceIdInterceptor = new Shared.Auth.XDeviceIdInterceptor(_gParam.DeviceId);
var osInterceptor       = new Shared.Auth.XOsClientInterceptor(os);
var jwtInterceptor      = new Shared.Auth.JwtClientInterceptor(token);
var appInterceptor      = new Shared.Auth.XAppClientInterceptor(appName, appVersion);
var ipInterceptor       = new Shared.Auth.XIpClientInterceptor(ip);
```

**Варианты решения**

```csharp
// ✅ Фабричный метод или Options-паттерн через DI:
public class AuthInterceptorFactory
{
    public CompositeAuthInterceptor Create(AuthInterceptorOptions options) =>
        new CompositeAuthInterceptor(
            options.Token,
            options.DeviceName,
            options.DeviceId,
            options.OsName,
            options.AppName,
            options.AppVersion
        );
}

// Регистрация в DI:
services.AddSingleton<AuthInterceptorFactory>();

// Использование:
var authInterceptor = _authInterceptorFactory.Create(new AuthInterceptorOptions
{
    Token      = token,
    DeviceName = _deviceName,
    DeviceId   = _gParam.DeviceId,
    OsName     = os,
    AppName    = appName,
    AppVersion = appVersion
});
```

---

### ARCH-03 — Несогласованное именование: `XDeviceIdInterceptor` vs `XDeviceClientInterceptor`

**Проблема / Описание**  
Часть интерсепторов называется `X*ClientInterceptor` (`XDeviceClientInterceptor`, `XAppClientInterceptor`, `XIpClientInterceptor`, `XOsClientInterceptor`), а один называется просто `XDeviceIdInterceptor` — без суффикса `Client`. Это нарушает консистентность именования в пространстве имён.

**Путь к файлу:** `Shared\BarkFluff.Shared.Auth\XDeviceIdInterceptor.cs` : 8

```csharp
// ❌ Не соответствует конвенции именования остальных интерсепторов
public class XDeviceIdInterceptor : Interceptor   // должно быть XDeviceIdClientInterceptor
```

**Варианты решения**

```csharp
// ✅ Переименовать для консистентности (с обновлением всех мест использования)
public class XDeviceIdClientInterceptor : Interceptor
{
    // ...
}
```

---

## Сводная таблица

| ID | Категория | Серьёзность | Краткое описание |
|---|---|---|---|
| SEC-01 | 🔴 Безопасность | **Критическая** | Клиент подделывает IP-адрес |
| SEC-02 | 🔴 Безопасность | **Высокая** | Нестандартный auth-заголовок, пустой токен отправляется |
| SEC-03 | 🔴 Безопасность | **Средняя** | Нет валидации null в конструкторах |
| SEC-04 | 🔴 Безопасность | **Средняя** | Base64 — не защита, ложная безопасность |
| SEC-05 | 🔴 Безопасность | **Критическая** | Серверный приоритет клиентского IP (дубль SEC-01) |
| PERF-01 | 🟡 Производительность | **Средняя** | Base64 пересчитывается на каждый вызов |
| PERF-02 | 🟡 Производительность | **Низкая** | 7 вложенных `.Intercept()` на каждый канал |
| PERF-03 | 🟡 Производительность | **Низкая** | Линейный поиск по metadata 6 раз на запрос |
| BUG-01 | 🟠 Баг | **Низкая** | Опечатка `osName` в `XDeviceIdInterceptor` |
| BUG-02 | 🟠 Баг | **Высокая** | Стриминговые gRPC-вызовы не получают метаданные |
| BUG-03 | 🟠 Баг | **Низкая** | `MetadataKeys` не `static` |
| BUG-04 | 🟠 Баг | **Высокая** | Нет защиты от невалидного Base64 на сервере (DoS) |
| ARCH-01 | 🔵 Архитектура | **Средняя** | DRY-нарушение: 5 одинаковых интерсепторов |
| ARCH-02 | 🔵 Архитектура | **Средняя** | Создание через `new` вместо DI |
| ARCH-03 | 🔵 Архитектура | **Низкая** | Несогласованное именование классов |
