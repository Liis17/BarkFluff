# BarkFluff.Shared.Auth — Карта проекта

Shared-библиотека gRPC client interceptors для передачи token и client metadata.

**Расположение:** `Shared/BarkFluff.Shared.Auth/`
**Target Framework:** `net10.0`
**Зависимости:** `Grpc.Core.Api 2.71.0`

---

## Файлы проекта

| Файл | Класс | Назначение |
|------|-------|-----------|
| `BarkFluff.Shared.Auth.csproj` | — | Файл проекта. net10.0, Nullable enable, зависимость Grpc.Core.Api |
| `MetadataKeys.cs` | `MetadataKeys` | Константы имён gRPC metadata-заголовков: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version` |
| `JwtClientInterceptor.cs` | `JwtClientInterceptor` | Добавляет plain `x-auth-token` в unary и streaming calls |
| `XDeviceIdInterceptor.cs`, `XDeviceClientInterceptor.cs` | client metadata | Добавляют `x-device-id`, `x-device-name` |
| `XIpClientInterceptor.cs`, `XOsClientInterceptor.cs`, `XAppClientInterceptor.cs` | client metadata | Добавляют `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version` |

---

## Паттерн кодирования

- Только `JwtClientInterceptor` не кодирует значение; он переопределяет все unary/streaming call types.
- Client metadata interceptors кодируют UTF-8 через Base64 и переопределяют только `AsyncUnaryCall<TRequest, TResponse>`; это не шифрование.

---

## Примечания

- Серверная сторона читает и проверяет эти заголовки через XAuth в [[Backend/GrpcServer]]
- Direct ProjectReference есть у `Backend/BarkFluff.FastAuth/`, `Backend/BarkFluff.GrpcServer/`, `Backend/Barkfluff.CloudMessaging/`, `Backend/Barkfluff.AdminPanel/`, `Backend/BarkFluff.Calls/`, `Backend/BarkFluff.Bots/`, `Backend/BarkFluff.Messages/` и `Windows/BarkFluff.WebApi.Core/`; наличие ссылки не означает, что каждый interceptor включён в каждый client.
- Общее описание и паттерн использования: [[Shared/Auth]]
