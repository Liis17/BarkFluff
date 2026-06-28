# BarkFluff.Shared.Exceptions

Общая библиотека исключений. Используется как на сервере (бросает через gRPC trailers), так и на клиенте (перехватывает `RpcException` → типизированные исключения).

Расположение: `Shared/BarkFluff.Shared.Exceptions/`

> Полная карта файлов с кодами ошибок: [[Shared/Exceptions-ProjectMap]]

## Базовый класс

`BaseGrpcException` — базовый для всех исключений:
- `ErrorCode` — уникальный GUID-строка, передаётся в gRPC trailer `x-error-code`
- `ErrorMessage` — читаемое описание ошибки

## Исключения по доменам

- `Identity/` — аутентификация, OTP, сессии, пользователи (22 класса)
- `Messages/` — чаты, сообщения, приватные/секретные чаты, закреплённые сообщения (23 класса)
- `FastAuth/` — QR-авторизация устройств (4 класса)
- `Files/` — загрузка файлов (2 класса)
- `Navigator/` — регистрация серверов (5 классов)
- `Users/` — профили, папки чатов (5 классов)

## Известные коды ошибок

| Исключение | ErrorCode |
|-----------|-----------|
| OtpCodeNeedException | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| NotValidOtpCodeException | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| InvalidLoginOrPasswordException | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |
| UsernameInvalidFormatException | `E7A4C9D2-3B61-4F82-A5E0-9C1D8F2B6A47` |
| UserNotFoundException | `A4DAB334-1067-4838-A782-C4257DC838F7` |
| SessionNotFoundException | `011BF29A-2DE6-4A63-BF8D-3F36AE730D9D` |
| FastAuthSessionNotFoundException | `A5E94C7D-1B82-4F36-9CDE-78B1F4A7E2C5` |
| ChatNotFoundException | `7506386A-8940-4F3B-87B8-315DD0A7AB08` |
| NoAccessToChatException | `604DD334-0484-4C6B-8113-354B9D2FDF2A` |

Полный список — [[Shared/Exceptions-ProjectMap]]

## Контракт формата username

Сервер (`BarkFluff.Users`, `UsernameFormatValidator`) проверяет username по regex `^[a-zA-Z0-9_]{3,32}$` (латиница, цифры, подчёркивание; дефис запрещён). При нарушении методы `AddDraftUser` (регистрация) и `ChangeUsername` (смена) бросают `UsernameInvalidFormatException` (`E7A4C9D2-…`).

Все клиенты ([[Клиенты/Windows-WPF|WPF]], [[Клиенты/Android|Android]] V1/V2, [[Клиенты/iOS|iOS]], [[Клиенты/macOS|macOS]], [[Клиенты/Linux-Qt|Linux]], [[Клиенты/Developers-Web|Web]]) приводят клиентскую валидацию к этому же набору символов (дефис убран) и показывают понятное сообщение по коду `E7A4C9D2-…`. Клиенты на gRPC StatusCode (iOS/macOS/Linux) показывают серверный текст при `FailedPrecondition`.

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
