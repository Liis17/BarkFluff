# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

`BarkFluff.WebApi.Core` — gRPC-клиентская библиотека для WPF-клиента BarkFluff (`.NET 10, windows10.0.26100`). Предоставляет единый фасад для взаимодействия со всеми микросервисами бэкенда.

## Build

```bash
dotnet build Windows/BarkFluff.WebApi.Core/BarkFluff.WebApi.Core.csproj
```

## Architecture

**Паттерн: Manager-based Facade**

`WebApi` — единая точка входа, которая делегирует работу 12 специализированным менеджерам, наследующим `WebApiBase`.

```
WebApi (IDisposable, фасад)
├── WebApiClientManager      — создание gRPC каналов/клиентов с interceptors
├── WebApiTokenManager       — рефреш токенов + SafeCallAsync<T> (retry при 401)
├── WebApiUserManager        — профиль, устройства, сессии, приватность
├── WebApiAuthManager        — 2FA/OTP (включение, отключение, статус)
├── WebApiRegistrationManager — регистрация аккаунта
├── WebApiPasswordManager    — сброс и смена пароля
├── WebApiMessageManager     — сообщения, чаты, вложения, участники
├── WebApiSearchManager      — поиск пользователей
├── WebApiFileManager        — загрузка/скачивание файлов (singleton HttpClient)
├── WebApiServerManager      — информация о серверах
├── WebApiUpdateManager      — real-time streaming (gRPC async enumerables)
└── WebApiOnlinerManager     — онлайн-статусы
```

**Ключевые классы:**

| Класс | Описание |
|-------|----------|
| `WebApi` | Фасад, 8 gRPC каналов + 8 API клиентов, управляет жизненным циклом |
| `WebApiBase` | Абстрактный базовый класс, даёт доступ ко всем gRPC клиентам |
| `GlobalParam` | Состояние приложения (токены, URL сервера, профиль), шифрование AES-256-CBC / PBKDF2 |
| `ErrorReturner` | Результат операции `(bool IsSuccess, string? ErrorMessage, int ErrorCode)` |
| `ImageProcessor` | Оптимизация изображений (JPEG, WebP, resize) через SixLabors.ImageSharp |

## Key Patterns

- **SafeCallAsync\<T\>** (`WebApiTokenManager`) — оборачивает любой gRPC вызов с автоматическим рефрешем токена при ошибке авторизации.
- **gRPC Interceptors** — в `WebApiClientManager` подключаются интерсепторы из `BarkFluff.Shared.Auth` (JWT, device ID, IP, OS, app version в metadata).
- **Real-time streaming** — `WebApiUpdateManager` и `WebApiOnlinerManager` используют `IAsyncEnumerable` для серверного стриминга.
- **GlobalParam encryption** — данные шифруются перед сохранением на диск через AES-256-CBC + PBKDF2-SHA256.

## Proto Files

Proto-файлы берутся из `Shared/BarkFluff.Proto/`. В `.csproj` подключены как `GrpcServices="Client"` (или `"None"` для shared.proto). При изменении proto-файлов перестройка происходит автоматически через `Grpc.Tools`.

## Adding New API Methods

1. Добавить метод в соответствующий менеджер (`Managers/WebApiXxxManager.cs`) с паттерном:
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
           return new ErrorReturner(false, "Локализованное сообщение");
       }
       catch (Exception)
       {
           return new ErrorReturner(false, "Общая ошибка");
       }
   }
   ```
2. Добавить делегирующую строку в `WebApi.cs` в соответствующий `#region`.

## Dependencies

- `Grpc.Net.Client` 2.71.0
- `Google.Protobuf` 3.32.0
- `SixLabors.ImageSharp` 3.1.12 — для обработки изображений (вместо `System.Drawing`)
- `BarkFluff.Shared.Auth`, `BarkFluff.Shared.Exceptions`, `BarkFluff.Shared.SecurityUtilities`
