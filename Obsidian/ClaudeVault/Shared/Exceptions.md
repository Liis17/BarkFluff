# BarkFluff.Shared.Exceptions

Общая библиотека исключений. Используется как на сервере (бросает через gRPC trailers), так и на клиенте (перехватывает `RpcException` → типизированные исключения).

Расположение: `Shared/BarkFluff.Shared.Exceptions/`

## Базовый класс

`BaseGrpcException` — базовый для всех исключений:
- `ErrorCode` — уникальный GUID-строка, передаётся в gRPC trailer `x-error-code`
- `ErrorMessage` — читаемое описание ошибки

## Исключения по доменам

- `Identity/` — аутентификация, OTP, сессии, пользователи
- `Messages/` — чаты и сообщения
- `Files/` — загрузка файлов
- `Users/` — профили
- `Navigator/` — регистрация серверов

## Известные коды ошибок

| Исключение | ErrorCode |
|-----------|-----------|
| OtpCodeNeedException | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| NotValidOtpCodeException | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| InvalidLoginOrPasswordException | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |

## ExceptionClientInterceptor

`Interceptors/ExceptionClientInterceptor.cs` — gRPC клиентский interceptor:
1. При получении `RpcException` с trailer `x-error-code`
2. Загружает все наследники `BaseGrpcException` через рефлексию (кешируется в `CachedExceptions`)
3. Находит совпадение по `ErrorCode`
4. Бросает типизированное исключение вместо `RpcException`

Подключение: `.AddInterceptor(() => new ExceptionClientInterceptor())`.

## Добавление нового исключения

```csharp
public class MyNewException : BaseGrpcException
{
    public override string ErrorCode => "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"; // новый GUID
    public override string ErrorMessage => "Описание ошибки";
}
```

1. Создать класс в папке нужного домена
2. На сервере — бросать исключение; [[Backend/GrpcServer]] `ServerExceptionInterceptor` упакует его
3. На клиенте — `ExceptionClientInterceptor` автоматически поймает
