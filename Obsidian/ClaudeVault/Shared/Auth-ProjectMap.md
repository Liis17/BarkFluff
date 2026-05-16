# BarkFluff.Shared.Auth — Карта проекта

Shared-библиотека gRPC client interceptors для передачи обязательных metadata-заголовков при межсервисных вызовах.

**Расположение:** `Shared/BarkFluff.Shared.Auth/`
**Target Framework:** `net10.0`
**Зависимости:** `Grpc.Core.Api 2.71.0`

---

## Файлы проекта

| Файл | Класс | Назначение |
|------|-------|-----------|
| `BarkFluff.Shared.Auth.csproj` | — | Файл проекта. net10.0, Nullable enable, зависимость Grpc.Core.Api |
| `MetadataKeys.cs` | `MetadataKeys` | Константы имён gRPC metadata-заголовков: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version` |
| `JwtClientInterceptor.cs` | `JwtClientInterceptor` | Добавляет `x-auth-token` — JWT-токен **без Base64**, передаётся как plain string |
| `XDeviceIdInterceptor.cs` | `XDeviceIdInterceptor` | Добавляет `x-device-id` — уникальный идентификатор устройства (Base64) |
| `XDeviceClientInterceptor.cs` | `XDeviceClientInterceptor` | Добавляет `x-device-name` — название устройства (Base64) |
| `XIpClientInterceptor.cs` | `XIpClientInterceptor` | Добавляет `x-ip-address` — IP-адрес клиента (Base64) |
| `XOsClientInterceptor.cs` | `XOsClientInterceptor` | Добавляет `x-os-name` — название операционной системы (Base64) |
| `XAppClientInterceptor.cs` | `XAppClientInterceptor` | Добавляет `x-app-name` и `x-app-version` — название и версия приложения (оба Base64) |

---

## Паттерн кодирования

- **`JwtClientInterceptor`** — единственный, кто передаёт значение **без кодирования** (plain string)
- **Все остальные interceptors** — кодируют значение через `Convert.ToBase64String(Encoding.UTF8.GetBytes(...))`
- Каждый interceptor переопределяет только `AsyncUnaryCall<TRequest, TResponse>`

---

## Примечания

- Серверная сторона читает и проверяет эти заголовки через XAuth в [[Backend/GrpcServer]]
- Используется всеми клиентами платформы: backend-сервисами, WPF, Android, macOS
- Общее описание и паттерн использования: [[Shared/Auth]]
