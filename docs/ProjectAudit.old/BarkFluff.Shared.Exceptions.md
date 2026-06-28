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

### NEW-ARCH-01 — `ServerExceptionInterceptor` не обрабатывает серверные streaming-вызовы

**Файл:** `Backend\BarkFluff.GrpcServer\ServerExceptionInterceptor.cs`

**Проблема:** Переопределён только `UnaryServerHandler`. `ServerStreamingServerHandler`, `ClientStreamingServerHandler`, `DuplexStreamingServerHandler` не переопределены — исключения из стриминговых хендлеров не перехватываются и возвращают системный stack-trace клиенту (зеркало BUG-04 на серверной стороне).

**Решение:** Добавить аналогичные обёртки для трёх оставшихся методов.

---

## 
