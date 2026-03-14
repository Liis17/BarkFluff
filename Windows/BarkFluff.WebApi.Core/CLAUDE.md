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

`WebApi` — единая точка входа (~1500 строк), которая делегирует работу 12 специализированным менеджерам, наследующим `WebApiBase`.

```
WebApi (IDisposable, фасад)
├── WebApiClientManager   — создание gRPC каналов/клиентов с interceptors
├── WebApiTokenManager    — рефреш токенов + SafeCallAsync<T> (retry при 401)
├── WebApiUserManager     — профиль, устройства, сессии
├── WebApiAuthManager     — 2FA/OTP
├── WebApiRegistrationManager — регистрация аккаунта
├── WebApiPasswordManager — сброс и смена пароля
├── WebApiMessageManager  — сообщения, чаты, read receipts
├── WebApiSearchManager   — поиск пользователей
├── WebApiFileManager     — загрузка/скачивание файлов (singleton HttpClient)
├── WebApiServerManager   — информация о серверах
├── WebApiUpdateManager   — real-time streaming (gRPC async enumerables)
└── WebApiOnlinerManager  — онлайн-статусы
```

**Ключевые классы:**

| Класс | Описание |
|-------|----------|
| `WebApi` | Фасад, 8 gRPC каналов + 8 API клиентов, управляет жизненным циклом |
| `WebApiBase` | Абстрактный базовый класс, даёт доступ ко всем gRPC клиентам |
| `GlobalParam` | Состояние приложения (токены, URL сервера, профиль), шифрование AES-256-CBC / PBKDF2 |
| `ErrorReturner` | Результат операции `(bool IsSuccess, string ErrorMessage, int ErrorCode)` |
| `ImageProcessor` | Оптимизация изображений (JPEG, WebP, resize) через SixLabors.ImageSharp |

## Key Patterns

- **SafeCallAsync\<T\>** (`WebApiTokenManager`) — оборачивает любой gRPC вызов с автоматическим рефрешем токена при ошибке авторизации.
- **gRPC Interceptors** — в `WebApiClientManager` подключаются интерсепторы из `BarkFluff.Shared.Auth` (JWT, device ID, IP, OS, app version в metadata).
- **Real-time streaming** — `WebApiUpdateManager` и `WebApiOnlinerManager` используют `IAsyncEnumerable` для серверного стриминга.
- **GlobalParam encryption** — данные шифруются перед сохранением на диск; метод `GlobalParam.Encrypt()` / `GlobalParam.Decrypt()`.

## Proto Files

Proto-файлы берутся из `Shared/BarkFluff.Proto/`. В `.csproj` подключены как `GrpcServices="Client"` (или `"None"` для shared.proto). При изменении proto-файлов перестройка происходит автоматически через `Grpc.Tools`.

## Dependencies

- `Grpc.Net.Client` 2.71.0
- `Google.Protobuf` 3.32.0
- `SixLabors.ImageSharp` 3.1.12 — для обработки изображений (вместо `System.Drawing`)
- `BarkFluff.Shared.Auth`, `BarkFluff.Shared.Exceptions`, `BarkFluff.Shared.SecurityUtilities`
