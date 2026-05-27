# Аудит: BarkFluff.Shared.Exceptions

> **Дата:** 2025  
> **Последняя проверка:** 2026-05-18  
> **Проект:** `Shared/BarkFluff.Shared.Exceptions`  
> **Статус:** Требует доработки  
> **Reviewer:** GitHub Copilot (BarkfluffAgent)

## 🐛 Баги и недоработки

### BUG-04 — `ExceptionClientInterceptor` не обрабатывает `AsyncServerStreamingCall` и `AsyncClientStreamingCall`

> ✅ **Статус (2026-05-18):** Актуальна.

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



### MISC-02 — Опечатка в имени класса `XAppInfoIsRequiedException`

> ✅ **Статус (2026-05-18):** Актуальна.

**Проблема / Описание**  
Класс называется `XAppInfoIsRequie**d**Exception` — пропущена буква `r` (`Required` → `Requied`). Это публичный API-контракт библиотеки, опечатка в имени класса затрудняет понимание и поиск.

**Путь к файлу:** `Shared/BarkFluff.Shared.Exceptions/Identity/XAppInfoIsRequiedException.cs : 3`

```csharp
// ❌ ПРОБЛЕМА: опечатка в имени — "Requied" вместо "Required"
public class XAppInfoIsRequiedException : BaseGrpcException
//                         ^^^^^^ пропущена 'r'
```

**Варианты решения**  
Переименовать класс 

```csharp
// ✅ РЕШЕНИЕ: исправленное имя
public class XAppInfoIsRequiredException : BaseGrpcException
{
    public override string ErrorCode => "FFE79950-5668-4786-A834-6B490650FE62";
    public override string ErrorMessage => "Этот запрос требует передачи x-app-name и x-app-version";
}


```

---

### MISC-04 — Нет `ISerializable` / сериализационного конструктора

> ✅ **Статус (2026-05-18):** Актуальна.

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

---

## 🆕 Новые проблемы (обнаружены 2026-05-18)

---

### NEW-BUG-01 — Класс `ProfilePictureHasNotValidType` не оканчивается на "Exception"

**Файл:** `Shared\BarkFluff.Shared.Exceptions\Users\ProfilePictureHasNotValidType.cs : 3`

**Проблема:** Нарушает соглашение .NET о наименовании исключений (CA1058/CA2237) — суффикс `Exception` обязателен. Все остальные 57 классов следуют этому соглашению.

**Решение:** Переименовать в `ProfilePictureHasNotValidTypeException`.

---

### NEW-BUG-02 — Опечатка в ErrorMessage `EmailExistException`

**Файл:** `Shared\BarkFluff.Shared.Exceptions\Identity\EmailExistException.cs : 7`

**Проблема:** `"Пользователь с таким емейлом зарегристирован"` — слово `зарегристирован` вместо `зарегистрирован`. Орфографическая ошибка попадает к клиенту в ErrorMessage.

**Решение:** Исправить на `"Пользователь с таким email уже зарегистрирован"`.

---

### NEW-SEC-01 — Все ErrorMessage хардкодены на русском без i18n

**Файл:** Все 57 файлов исключений

**Проблема:** Сообщения `ErrorMessage` хардкодены на русском языке. Клиенты с другой локалью получают непонятные сообщения. Нет механизма локализации (`IStringLocalizer`, resource files).

**Решение:** Перенести `ErrorMessage` в `.resx` ресурсные файлы или возвращать только `ErrorCode`, выполняя локализацию на клиентской стороне (см. реестр кодов в Identity Notes).

---

### NEW-MISC-01 — Синтетические (non-random) GUID в ErrorCode

**Файлы:** Минимум 11 классов используют явно паттерновые GUID:

- `Navigator/NameEmptyException` — `1A2B3C4D-5E6F-7A8B-9C0D-1E2F3A4B5C6D`
- `Navigator/InvalidHexColorException` — `E1F2A3B4-5C6D-4E7F-8A9B-0C1D2E3F4A5B`
- `Navigator/BeaconPortEmptyException` — `F6E5D4C3-B2A1-4C5D-8B7A-9E0F1A2B3C4D`
- `Navigator/InvalidBeaconHostException` — `B7C4D8E2-3F1A-4D6B-9C7E-2A8B5D6F1C3E`
- `Messages/TooManyAttachmentsException` — `B3A4D7F2-5C6E-4A8B-9D1F-3E2C7B8A0F4D`
- `Messages/MessageTextTooLongException` — `9F8B5C2A-7F1D-4E5A-9C3B-1F0E2D4A8B6C`
- `Identity/UsernameReservedException` — `A3F1B2C4-7D8E-4F5A-9B6C-1E2D3F4A5B6C`
- `Identity/InvalidOldPasswordException` — `A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04`
- `FastAuth/FastAuthInvalidConfirmationCodeException` — `7B3F8E92-5D14-4C68-A7E2-1F9B6D3C8A45`
- `FastAuth/FastAuthInvalidStateException` — `3C8A1E5F-7D29-4B83-9E16-5A2C8F4B7D31`
- `Users/ChatFolderInvalidNameException` — `8C1A6F4D-1B22-4E1B-8E4D-7E9A5B6C2A11`

**Проблема:** Структура GUID показывает ручную генерацию по шаблону, а не вызов `Guid.NewGuid()`. Риск коллизии при добавлении новых кодов по той же схеме.

**Решение:** Перегенерировать через `Guid.NewGuid()` или IDE-генератор.

---

### NEW-ARCH-01 — `ServerExceptionInterceptor` не обрабатывает серверные streaming-вызовы

**Файл:** `Backend\BarkFluff.GrpcServer\ServerExceptionInterceptor.cs`

**Проблема:** Переопределён только `UnaryServerHandler`. `ServerStreamingServerHandler`, `ClientStreamingServerHandler`, `DuplexStreamingServerHandler` не переопределены — исключения из стриминговых хендлеров не перехватываются и возвращают системный stack-trace клиенту (зеркало BUG-04 на серверной стороне).

**Решение:** Добавить аналогичные обёртки для трёх оставшихся методов.

---

### Дополнительная информация: проверка ErrorCode

**Дубликатов GUID не обнаружено** — все 58 значений (57 исключений + базовый `BDF4009D-24D0-4E0C-A10C-AEF33E0D0022` в `BaseGrpcException`) уникальны.

---

## 
