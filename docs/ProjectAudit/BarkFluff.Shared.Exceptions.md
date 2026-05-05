# Аудит: BarkFluff.Shared.Exceptions

> **Дата:** 2025  
> **Проект:** `Shared/BarkFluff.Shared.Exceptions`  
> **Статус:** Требует доработки  
> **Reviewer:** GitHub Copilot (BarkfluffAgent)

---

## Содержание

1. [🔒 Безопасность](#-безопасность)
   - [SEC-01 — Утечка внутреннего сообщения исключения в gRPC ответ](#sec-01--утечка-внутреннего-сообщения-исключения-в-grpc-ответ)
   - [SEC-02 — Нет защиты от утечки стек-трейса через `ex.Message`](#sec-02--нет-защиты-от-утечки-стек-трейса-через-exmessage)
2. [⚡ Производительность](#-производительность)
   - [PERF-01 — Race Condition в инициализации `CachedExceptions`](#perf-01--race-condition-в-инициализации-cachedexceptions)
   - [PERF-02 — `Assembly.GetExecutingAssembly()` находит только исключения в текущей сборке](#perf-02--assemblygetexecutingassembly-находит-только-исключения-в-текущей-сборке)
   - [PERF-03 — `FirstOrDefault` линейный поиск по списку при каждом вызове](#perf-03--firstordefault-линейный-поиск-по-списку-при-каждом-вызове)
   - [PERF-04 — `Activator.CreateInstance` для каждого типа без проверки наличия конструктора](#perf-04--activatorcreateinstance-для-каждого-типа-без-проверки-наличия-конструктора)
3. [🐛 Баги и недоработки](#-баги-и-недоработки)
   - [BUG-01 — `BaseGrpcException` не вызывает `base(message)` — `Exception.Message` пустой](#bug-01--basegrpcexception-не-вызывает-basemessage--exceptionmessage-пустой)
   - [BUG-02 — `Files.FileNotFoundException` скрывает системный `System.IO.FileNotFoundException`](#bug-02--filesfilenotfoundexception-скрывает-системный-systemiofilenotfoundexception)
   - [BUG-03 — `CachedExceptions` — публичное статичное изменяемое поле (mutable public static)](#bug-03--cachedexceptions--публичное-статичное-изменяемое-поле-mutable-public-static)
   - [BUG-04 — `ExceptionClientInterceptor` не обрабатывает `AsyncServerStreamingCall` и `AsyncClientStreamingCall`](#bug-04--exceptionclientinterceptor-не-обрабатывает-asyncserverstreaming-и-asyncclientstreaming)
   - [BUG-05 — `ServerExceptionInterceptor` передаёт `ex.Message` неизвестного исключения в продакшн](#bug-05--serverexceptioninterceptor-передаёт-exmessage-неизвестного-исключения-в-продакшн)
4. [🧹 Прочее / Качество кода](#-прочее--качество-кода)
   - [MISC-01 — Непоследовательный стиль объявления namespace](#misc-01--непоследовательный-стиль-объявления-namespace)
   - [MISC-02 — Опечатка в имени класса `XAppInfoIsRequiedException`](#misc-02--опечатка-в-имени-класса-xappinfoisrequiedException)
   - [MISC-03 — Устаревший пакет `Grpc.Core` вместо `Grpc.Net.Client`](#misc-03--устаревший-пакет-grpccore-вместо-grpcnetclient)
   - [MISC-04 — Нет `ISerializable` / сериализационного конструктора](#misc-04--нет-iserializable--сериализационного-конструктора)

---

## 🔒 Безопасность

---

### SEC-01 — Утечка внутреннего сообщения исключения в gRPC ответ

**Проблема / Описание**  
В `ServerExceptionInterceptor` при поимке **неизвестного** (`Exception`) исключения в поле `Status.Detail` передаётся `ex.Message` — сырое сообщение из внутреннего исключения. Это может содержать пути к файлам, имена таблиц БД, connection strings и другую чувствительную информацию.

**Конкретно в чём проблема**  
Клиент получает `RpcException.Status.Detail` с содержимым `ex.Message`, которое может быть инфраструктурным (например, `"Connection refused: postgres://..."`).

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs : 69`

```csharp
// ❌ ПРОБЛЕМА: ex.Message из неизвестного исключения утекает клиенту
throw new RpcException(
    new Status(StatusCode.Unknown, ex.Message), // <-- ex.Message может содержать чувствительные данные
    trailers
);
```

**Варианты решения**  
Передавать клиенту нейтральное сообщение, а реальную ошибку логировать на сервере (уже делается через `_logger.LogError`).

```csharp
// ✅ РЕШЕНИЕ: клиенту — нейтральное сообщение, детали — в логах
throw new RpcException(
    new Status(StatusCode.Internal, "Внутренняя ошибка сервера"), // безопасное сообщение
    trailers
);
// ex.Message уже записан в _logger.LogError выше — информация не теряется
```

---

### SEC-02 — Нет защиты от утечки стек-трейса через `ex.Message`

**Проблема / Описание**  
`BaseGrpcException.ErrorMessage` содержит фиксированные строки — это безопасно. Однако `BaseGrpcException` не переопределяет `Exception.Message`, и стандартный `Exception.Message` остаётся пустым или дефолтным. При этом в `ServerExceptionInterceptor` при `BaseGrpcException` используется `ex.ErrorMessage` — это корректно. Но при попытке поймать `BaseGrpcException` как обычный `Exception` (например, во внешнем middleware) `ex.Message` будет пустым, что может привести к логированию пустых записей, маскируя реальную ошибку.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/BaseGrpcException.cs : 1-8`

```csharp
// ❌ ПРОБЛЕМА: Message у Exception не заполнен, ErrorMessage игнорируется
// стандартными инструментами (Serilog enrichers, APM агенты и т.д.)
public class BaseGrpcException : Exception
{
    public virtual string ErrorCode { get; } = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";
    public virtual string ErrorMessage { get; } = "Неизвестная ошибка";
    // Exception.Message == "" — потеря информации в стандартном стек-трейсе
}
```

**Варианты решения**  
Передавать `ErrorMessage` в базовый конструктор `Exception`.

```csharp
// ✅ РЕШЕНИЕ: Message = ErrorMessage, совместимость с экосистемой .NET
public class BaseGrpcException : Exception
{
    public virtual string ErrorCode { get; } = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";
    public virtual string ErrorMessage { get; } = "Неизвестная ошибка";

    public BaseGrpcException() : base("Неизвестная ошибка") { }

    // Позволяет наследникам также инициализировать base с их ErrorMessage
    protected BaseGrpcException(string message) : base(message) { }
}
```

---

## ⚡ Производительность

---

### PERF-01 — Race Condition в инициализации `CachedExceptions`

**Проблема / Описание**  
`CachedExceptions` — статическое поле без синхронизации. В многопоточной среде (gRPC сервер / клиент обрабатывают запросы параллельно) два потока могут одновременно пройти проверку `CachedExceptions is null or { Count: 0 }` и оба вызвать `LoadExceptions()`, что приведёт к двойной инициализации и состоянию гонки при записи.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs : 36-39`

```csharp
// ❌ ПРОБЛЕМА: нет синхронизации — два потока могут оба зайти в LoadExceptions()
if (CachedExceptions is null or { Count: 0 })
{
    CachedExceptions = LoadExceptions(); // <-- Race Condition при параллельных вызовах
}
```

**Варианты решения**  
Использовать `Lazy<T>` или `Interlocked` / `lock` для потокобезопасной инициализации.

```csharp
// ✅ РЕШЕНИЕ: Lazy<T> гарантирует однократную потокобезопасную инициализацию
private static readonly Lazy<IReadOnlyDictionary<string, BaseGrpcException>> _cachedExceptions =
    new(() => LoadExceptions(), LazyThreadSafetyMode.ExecutionAndPublication);

// Вместо списка — словарь для O(1) поиска (см. PERF-03)
private static IReadOnlyDictionary<string, BaseGrpcException> LoadExceptions()
{
    var baseType = typeof(BaseGrpcException);
    return AppDomain.CurrentDomain.GetAssemblies()
        .SelectMany(a => a.GetTypes())
        .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract)
        .Select(t => (BaseGrpcException)Activator.CreateInstance(t)!)
        .ToDictionary(e => e.ErrorCode, StringComparer.OrdinalIgnoreCase);
}
```

---

### PERF-02 — `Assembly.GetExecutingAssembly()` находит только исключения в текущей сборке

**Проблема / Описание**  
`LoadExceptions` сканирует только `Assembly.GetExecutingAssembly()` — то есть саму сборку `BarkFluff.Shared.Exceptions`. Если когда-либо исключение будет определено в другой сборке (например, в `BarkFluff.Shared.Auth` или сервисном проекте), оно **никогда** не будет найдено. Метод молча вернёт `null` вместо исключения, и оригинальный `RpcException` будет проброшен без конвертации.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs : 53-63`

```csharp
// ❌ ПРОБЛЕМА: сканируется только одна сборка
var types = Assembly.GetExecutingAssembly()
    .GetTypes()  // <-- только BarkFluff.Shared.Exceptions, другие сборки игнорируются
    .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);
```

**Варианты решения**  
Сканировать все загруженные сборки через `AppDomain.CurrentDomain.GetAssemblies()`.

```csharp
// ✅ РЕШЕНИЕ: сканируем все загруженные сборки домена
var baseType = typeof(BaseGrpcException);
var types = AppDomain.CurrentDomain
    .GetAssemblies()
    .Where(a => !a.IsDynamic) // исключаем динамически созданные сборки
    .SelectMany(a =>
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null)!; }
    })
    .Where(t => t.IsSubclassOf(baseType) && !t.IsAbstract);
```

---

### PERF-03 — `FirstOrDefault` линейный поиск по списку при каждом вызове

**Проблема / Описание**  
`FindExceptionByErrorCode` выполняет линейный поиск `O(n)` по `List<BaseGrpcException>` при каждом пришедшем gRPC-ответе с ошибкой. При наличии 47+ исключений это незначительно, но структура данных изначально выбрана неоптимально — список вместо словаря.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs : 65-68`

```csharp
// ❌ ПРОБЛЕМА: O(n) поиск по списку при каждом вызове
private BaseGrpcException? FindExceptionByErrorCode(string errorCode)
{
    return CachedExceptions.FirstOrDefault(e => e.ErrorCode == errorCode); // линейный перебор
}
```

**Варианты решения**  
Использовать `Dictionary<string, BaseGrpcException>` для поиска за `O(1)`.

```csharp
// ✅ РЕШЕНИЕ: словарь даёт O(1) lookup
private static readonly Lazy<Dictionary<string, BaseGrpcException>> _cache =
    new(() => LoadExceptions().ToDictionary(e => e.ErrorCode, StringComparer.OrdinalIgnoreCase));

private BaseGrpcException? FindExceptionByErrorCode(string? errorCode)
{
    if (string.IsNullOrEmpty(errorCode)) return null;
    return _cache.Value.TryGetValue(errorCode, out var ex) ? ex : null;
}
```

---

### PERF-04 — `Activator.CreateInstance` для каждого типа без проверки наличия конструктора

**Проблема / Описание**  
`Activator.CreateInstance(t)` при отсутствии публичного конструктора без параметров выбросит `MissingMethodException` в рантайме, не давая никакого намёка при добавлении нового исключения без дефолтного конструктора. Кроме того, рефлексивное создание экземпляров медленнее прямого вызова конструктора.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs : 61-62`

```csharp
// ❌ ПРОБЛЕМА: упадёт в рантайме если конструктора нет, без понятного сообщения
return types.Select(t => (BaseGrpcException)Activator.CreateInstance(t)!)
    .ToList();
```

**Варианты решения**  
Добавить проверку наличия конструктора или использовать фабрику с понятным сообщением об ошибке.

```csharp
// ✅ РЕШЕНИЕ: явная проверка + понятное исключение при нарушении контракта
return types
    .Where(t =>
    {
        if (t.GetConstructor(Type.EmptyTypes) != null) return true;
        // В Debug бросаем — это ошибка разработчика
        Debug.Fail($"Тип {t.FullName} не имеет публичного конструктора без параметров. " +
                   "Все наследники BaseGrpcException должны иметь конструктор по умолчанию.");
        return false;
    })
    .ToDictionary(
        t => ((BaseGrpcException)Activator.CreateInstance(t)!).ErrorCode,
        t => (BaseGrpcException)Activator.CreateInstance(t)!,
        StringComparer.OrdinalIgnoreCase
    );
```

---

## 🐛 Баги и недоработки

---

### BUG-01 — `BaseGrpcException` не вызывает `base(message)` — `Exception.Message` пустой

**Проблема / Описание**  
`BaseGrpcException` наследует `Exception`, но не передаёт `ErrorMessage` в базовый конструктор. Это означает что `exception.Message` возвращает дефолтное `"Exception of type 'BarkFluff.Shared.Exceptions.BaseGrpcException' was thrown."` вместо полезного текста. Все стандартные инструменты (.NET Runtime, Serilog, Application Insights, отладчик) используют `Exception.Message`, а не кастомное `ErrorMessage`.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/BaseGrpcException.cs : 3-8`

```csharp
// ❌ ПРОБЛЕМА: Exception.Message == стандартный placeholder, не ErrorMessage
public class BaseGrpcException : Exception
{
    public virtual string ErrorCode { get; } = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";
    public virtual string ErrorMessage { get; } = "Неизвестная ошибка";
    // Нет вызова base(ErrorMessage) — Message бесполезен
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: конструктор передаёт ErrorMessage в Exception.Message
public class BaseGrpcException : Exception
{
    public virtual string ErrorCode { get; } = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";
    public virtual string ErrorMessage { get; } = "Неизвестная ошибка";

    // Вызывается Activator.CreateInstance и прямым new
    public BaseGrpcException() : base("Неизвестная ошибка") { }

    // Наследники могут передать свой ErrorMessage
    protected BaseGrpcException(string errorMessage) : base(errorMessage) { }
}

// Наследники тогда выглядят так:
public class UserNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "A4DAB334-1067-4838-A782-C4257DC838F7";
    public override string ErrorMessage => "Пользователь не найден";

    // Передаём ErrorMessage в base — Exception.Message тоже будет "Пользователь не найден"
    public UserNotFoundException() : base("Пользователь не найден") { }
}
```

---

### BUG-02 — `Files.FileNotFoundException` скрывает системный `System.IO.FileNotFoundException`

**Проблема / Описание**  
Класс `BarkFluff.Shared.Exceptions.Files.FileNotFoundException` имеет **то же имя**, что и системный `System.IO.FileNotFoundException`. В файлах, где есть `using BarkFluff.Shared.Exceptions.Files` и `using System.IO`, возникнет конфликт имён. Компилятор выберет один из них или выдаст ошибку. Это затрудняет перехват системных `FileNotFoundException` (например, при работе с файловой системой).

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Files/FileNotFoundException.cs : 1-8`

```csharp
// ❌ ПРОБЛЕМА: имя конфликтует с System.IO.FileNotFoundException
namespace BarkFluff.Shared.Exceptions.Files;

public class FileNotFoundException : BaseGrpcException  // <-- такое же имя как у системного класса
{
    public override string ErrorCode => "91E25C73-FC80-43C1-893D-F26F39726F03";
    public override string ErrorMessage => "Файл не найден";
}
```

**Варианты решения**  
Переименовать класс, добавив доменный префикс.

```csharp
// ✅ РЕШЕНИЕ: добавляем префикс домена — нет конфликта имён
namespace BarkFluff.Shared.Exceptions.Files;

public class BarkFluffFileNotFoundException : BaseGrpcException
{
    public override string ErrorCode => "91E25C73-FC80-43C1-893D-F26F39726F03";
    public override string ErrorMessage => "Файл не найден";
}
```

---

### BUG-03 — `CachedExceptions` — публичное статичное изменяемое поле (mutable public static)

**Проблема / Описание**  
`CachedExceptions` объявлен как `public static List<BaseGrpcException>` — любой внешний код может присвоить ему `null`, заменить список или очистить его. Это нарушает инкапсуляцию и может привести к `NullReferenceException` или неожиданному поведению в многопоточной среде. Поле должно быть приватным.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs : 11`

```csharp
// ❌ ПРОБЛЕМА: публичное изменяемое статическое поле — любой может сломать кеш
public static List<BaseGrpcException> CachedExceptions;
// Снаружи: ExceptionClientInterceptor.CachedExceptions = null; — и всё упадёт
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: приватное статическое поле, инициализируемое через Lazy<T>
private static readonly Lazy<IReadOnlyDictionary<string, BaseGrpcException>> _cachedExceptions =
    new(LoadExceptions, LazyThreadSafetyMode.ExecutionAndPublication);
```

---

### BUG-04 — `ExceptionClientInterceptor` не обрабатывает `AsyncServerStreamingCall` и `AsyncClientStreamingCall`

**Проблема / Описание**  
Перехватчик переопределяет только `AsyncUnaryCall`. Потоковые вызовы (`AsyncServerStreamingCall`, `AsyncClientStreamingCall`, `AsyncDuplexStreamingCall`) не обрабатываются — `RpcException` из стриминговых вызовов **не конвертируется** в типизированные исключения и приходит к клиенту как `RpcException` напрямую, что нарушает единообразие обработки ошибок.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs : 13-26`

```csharp
// ❌ ПРОБЛЕМА: только AsyncUnaryCall — стриминги не обрабатываются
public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(...)
{
    // ... обработка
}
// AsyncServerStreamingCall — НЕ переопределён, ошибки не конвертируются
// AsyncClientStreamingCall — НЕ переопределён
// AsyncDuplexStreamingCall — НЕ переопределён
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: переопределить серверный стриминг (наиболее вероятный для BarkFluff)
public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
    TRequest request,
    ClientInterceptorContext<TRequest, TResponse> context,
    AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
{
    var call = continuation(request, context);

    return new AsyncServerStreamingCall<TResponse>(
        new WrappedAsyncStreamReader<TResponse>(call.ResponseStream, ConvertException),
        call.ResponseHeadersAsync,
        call.GetStatus,
        call.GetTrailers,
        call.Dispose);
}

// Вспомогательный враппер для стрим-ридера
private async IAsyncEnumerable<TResponse> WrapStream<TResponse>(
    IAsyncStreamReader<TResponse> reader)
{
    while (true)
    {
        try
        {
            if (!await reader.MoveNext(CancellationToken.None)) yield break;
        }
        catch (RpcException ex) when (ex.Trailers.Any(t => t.Key == "x-error-code"))
        {
            throw ConvertRpcException(ex);
        }
        yield return reader.Current;
    }
}
```

---

### BUG-05 — `ServerExceptionInterceptor` передаёт `ex.Message` неизвестного исключения в продакшн

*(Связан с SEC-01, отмечен отдельно как баг поведения)*

**Проблема / Описание**  
При попадании неизвестного исключения `StatusCode` устанавливается в `Unknown`, а не `Internal`. По gRPC-спецификации `Unknown` означает неопределённый статус, тогда как необработанная серверная ошибка должна возвращать `Internal`. Клиентский код, проверяющий статус ошибки, получит некорректный код.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs : 69`

```csharp
// ❌ ПРОБЛЕМА: StatusCode.Unknown вместо StatusCode.Internal
throw new RpcException(
    new Status(StatusCode.Unknown, ex.Message), // неверный status code
    trailers
);
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: StatusCode.Internal — правильный код для необработанных серверных ошибок
throw new RpcException(
    new Status(StatusCode.Internal, "Внутренняя ошибка сервера"),
    trailers
);
```

---

## 🧹 Прочее / Качество кода

---

### MISC-01 — Непоследовательный стиль объявления namespace

**Проблема / Описание**  
В проекте смешаны два стиля объявления `namespace`: file-scoped (`namespace X.Y;`) и block-scoped (`namespace X.Y { }`). Это снижает читаемость и нарушает единообразие кодовой базы. Современный стандарт C# 10+ — file-scoped.

**Путь к файлу:**  
- `Identity/ResetIdHasIsApprovedException.cs : 1` — block-scoped  
- `Identity/ResetIdNotFoundException.cs : 1` — block-scoped  
- Все остальные файлы — file-scoped

```csharp
// ❌ БЫЛО: block-scoped namespace (старый стиль)
namespace BarkFluff.Shared.Exceptions.Identity
{
    public class ResetIdHasIsApprovedException : BaseGrpcException
    {
        ...
    }
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: file-scoped namespace — единообразно со всем проектом
namespace BarkFluff.Shared.Exceptions.Identity;

public class ResetIdHasIsApprovedException : BaseGrpcException
{
    public override string ErrorCode => "BE708516-BF40-44F9-A6D1-A7F30AB02BED";
    public override string ErrorMessage => "Невозможно повторно сбросить пароль по этому идентификатору сброса";
}
```

---

### MISC-02 — Опечатка в имени класса `XAppInfoIsRequiedException`

**Проблема / Описание**  
Класс называется `XAppInfoIsRequie**d**Exception` — пропущена буква `r` (`Required` → `Requied`). Это публичный API-контракт библиотеки, опечатка в имени класса затрудняет понимание и поиск.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Identity/XAppInfoIsRequiedException.cs : 3`

```csharp
// ❌ ПРОБЛЕМА: опечатка в имени — "Requied" вместо "Required"
public class XAppInfoIsRequiedException : BaseGrpcException
//                         ^^^^^^ пропущена 'r'
```

**Варианты решения**  
Переименовать класс и добавить `[Obsolete]`-алиас для обратной совместимости на переходный период.

```csharp
// ✅ РЕШЕНИЕ: исправленное имя
public class XAppInfoIsRequiredException : BaseGrpcException
{
    public override string ErrorCode => "FFE79950-5668-4786-A834-6B490650FE62";
    public override string ErrorMessage => "Этот запрос требует передачи x-app-name и x-app-version";
}

// Алиас для обратной совместимости (временно, до обновления всех потребителей)
[Obsolete("Use XAppInfoIsRequiredException instead. This alias will be removed in a future version.")]
public class XAppInfoIsRequiedException : XAppInfoIsRequiredException { }
```

---

### MISC-03 — Устаревший пакет `Grpc.Core` вместо `Grpc.Net.Client`

**Проблема / Описание**  
В `.csproj` используется пакет `Grpc.Core` версии `2.46.6` — это **устаревший C-core gRPC binding**, официально переведённый в режим обслуживания. Для .NET 9 рекомендован `Grpc.Net.Client` (managed gRPC) как часть `dotnet/grpc`. `Grpc.Core` не получает новых функций и может иметь проблемы совместимости с будущими версиями .NET.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/BarkFluff.Shared.Exceptions.csproj : 13`

```xml
<!-- ❌ ПРОБЛЕМА: устаревший C-core binding -->
<PackageReference Include="Grpc.Core" Version="2.46.6" />
```

**Варианты решения**

```xml
<!-- ✅ РЕШЕНИЕ: современный managed gRPC для .NET 9 -->
<!-- Для типов Interceptor, Metadata, StatusCode достаточно Grpc.Core.Api -->
<PackageReference Include="Grpc.Core.Api" Version="2.67.0" />

<!-- Если нужен полный клиент — используй Grpc.Net.Client -->
<!-- <PackageReference Include="Grpc.Net.Client" Version="2.67.0" /> -->
```

---

### MISC-04 — Нет `ISerializable` / сериализационного конструктора

**Проблема / Описание**  
`BaseGrpcException` и все его наследники не реализуют `ISerializable` и не имеют защищённого конструктора `(SerializationInfo, StreamingContext)`. По стандарту `CA2237`, пользовательские исключения, наследующие `Exception`, должны быть сериализуемыми для корректной работы в сценариях cross-domain, remoting и некоторых логгеров.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/BaseGrpcException.cs : 3-8`

```csharp
// ❌ ПРОБЛЕМА: нет атрибута [Serializable] и сериализационного конструктора
public class BaseGrpcException : Exception
// Нарушение CA2229, CA2237
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: добавить атрибут и конструктор сериализации
[Serializable]
public class BaseGrpcException : Exception
{
    public virtual string ErrorCode { get; } = "BDF4009D-24D0-4E0C-A10C-AEF33E0D0022";
    public virtual string ErrorMessage { get; } = "Неизвестная ошибка";

    public BaseGrpcException() : base("Неизвестная ошибка") { }
    protected BaseGrpcException(string message) : base(message) { }

    // Конструктор для сериализации (требуется для ISerializable)
    [Obsolete("Serialization constructor — required for ISerializable pattern")]
    protected BaseGrpcException(
        System.Runtime.Serialization.SerializationInfo info,
        System.Runtime.Serialization.StreamingContext context) : base(info, context) { }
}
```

---

## Сводная таблица

| ID | Категория | Серьёзность | Краткое описание |
|----|-----------|-------------|------------------|
| SEC-01 | 🔒 Безопасность | 🔴 Высокая | `ex.Message` утекает клиенту в `StatusCode.Unknown` |
| SEC-02 | 🔒 Безопасность | 🟡 Средняя | `Exception.Message` пустой — маскирует ошибки в логах |
| PERF-01 | ⚡ Производительность | 🔴 Высокая | Race Condition в инициализации `CachedExceptions` |
| PERF-02 | ⚡ Производительность | 🟡 Средняя | `GetExecutingAssembly` — исключения из других сборок не найдутся |
| PERF-03 | ⚡ Производительность | 🟢 Низкая | Линейный поиск O(n) вместо O(1) словаря |
| PERF-04 | ⚡ Производительность | 🟢 Низкая | Нет проверки конструктора перед `Activator.CreateInstance` |
| BUG-01 | 🐛 Баг | 🟡 Средняя | `Exception.Message` не заполнен из `ErrorMessage` |
| BUG-02 | 🐛 Баг | 🟡 Средняя | `FileNotFoundException` конфликтует с `System.IO.FileNotFoundException` |
| BUG-03 | 🐛 Баг | 🔴 Высокая | `CachedExceptions` — публичное изменяемое статическое поле |
| BUG-04 | 🐛 Баг | 🟡 Средняя | Стриминговые gRPC-вызовы не обрабатываются в перехватчике |
| BUG-05 | 🐛 Баг | 🟡 Средняя | `StatusCode.Unknown` вместо `StatusCode.Internal` |
| MISC-01 | 🧹 Качество | 🟢 Низкая | Смешанный стиль объявления namespace |
| MISC-02 | 🧹 Качество | 🟢 Низкая | Опечатка в имени класса `XAppInfoIsRequiedException` |
| MISC-03 | 🧹 Качество | 🟡 Средняя | Устаревший пакет `Grpc.Core` 2.46.6 |
| MISC-04 | 🧹 Качество | 🟢 Низкая | Нет `[Serializable]` и сериализационного конструктора |

---

*Документ создан автоматически на основе анализа исходного кода проекта `BarkFluff.Shared.Exceptions`.*
