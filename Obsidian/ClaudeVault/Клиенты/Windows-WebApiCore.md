# BarkFluff.WebApi.Core

gRPC-клиентская библиотека для WPF-клиента. .NET 10, windows10.0.26100.

Расположение: `Windows/BarkFluff.WebApi.Core/`

> Полная карта файлов и внутреннего строения: [[Windows-WebApiCore-ProjectMap]]

## Сборка

```bash
dotnet build Windows/BarkFluff.WebApi.Core/BarkFluff.WebApi.Core.csproj
```

## Архитектура: Manager-based Facade

`WebApi` — единая точка входа, делегирует 12 специализированным менеджерам (`WebApiBase`):

```
WebApi (IDisposable, фасад)
├── WebApiClientManager      — gRPC каналы/клиенты + interceptors
├── WebApiTokenManager       — рефреш токенов + SafeCallAsync<T>
├── WebApiUserManager        — профиль, устройства, сессии, приватность
├── WebApiAuthManager        — 2FA/OTP
├── WebApiRegistrationManager — регистрация
├── WebApiPasswordManager    — сброс/смена пароля
├── WebApiMessageManager     — сообщения, чаты, вложения
├── WebApiSearchManager      — поиск
├── WebApiFileManager        — загрузка/скачивание (singleton HttpClient)
├── WebApiServerManager      — информация о серверах
├── WebApiUpdateManager      — real-time streaming (IAsyncEnumerable)
└── WebApiOnlinerManager     — онлайн-статусы
```

## Ключевые классы

| Класс | Описание |
|-------|----------|
| `WebApi` | Фасад, 8 gRPC каналов + 8 API клиентов |
| `WebApiBase` | Абстрактный базовый, доступ ко всем gRPC клиентам |
| `GlobalParam` | Состояние (токены, URL, профиль), AES-256-CBC / PBKDF2 |
| `ErrorReturner` | `(bool IsSuccess, string? ErrorMessage, int ErrorCode)` |
| `ImageProcessor` | JPEG, WebP, resize через SixLabors.ImageSharp |

## Паттерн добавления API-метода

```csharp
public async Task<ErrorReturner> MethodName(params..., GlobalParam globalParam)
{
    try
    {
        return await _webApi.TokenManager.SafeCallAsync(async () =>
        {
            await XxxAC!.XxxAsync(new Proto.Xxx.XxxRequest { ... });
            return new ErrorReturner(true);
        }, globalParam);
    }
    catch (BarkFluff.Shared.Exceptions.Xxx.SpecificException)
    {
        return new ErrorReturner(false, "Сообщение");
    }
}
```

`SafeCallAsync<T>` — автоматический рефреш токена при ошибке авторизации.

## Interceptors

В `WebApiClientManager` подключаются interceptors из [[Shared/Auth]]: JWT, device ID, IP, OS, app version в metadata.

## Real-time Streaming

`WebApiUpdateManager` и `WebApiOnlinerManager` — `IAsyncEnumerable` для серверного стриминга.

## Proto

Из `Shared/BarkFluff.Proto/`, подключены как `GrpcServices="Client"` (или `"None"` для shared.proto).

## Зависимости

- `Grpc.Net.Client 2.71.0`
- `Google.Protobuf 3.32.0`
- `SixLabors.ImageSharp 3.1.12`
- [[Shared/Auth]], [[Shared/Exceptions]], [[Shared/SecurityUtilities]]
