# BarkFluff.Shared.Auth

gRPC client interceptors для добавления обязательных metadata-заголовков к каждому межсервисному вызову.
Используется всеми клиентами (backend-сервисы, WPF, Android) при регистрации gRPC-клиентов.

Расположение: `Shared/BarkFluff.Shared.Auth/`

## Содержимое

- **`MetadataKeys`** — константы имён заголовков: `x-auth-token`, `x-device-id`, `x-device-name`, `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version`
- **`JwtClientInterceptor`** — добавляет `x-auth-token` (plain string, без Base64)
- **`XDeviceIdInterceptor`** — добавляет `x-device-id` (Base64)
- **`XDeviceClientInterceptor`** — добавляет `x-device-name` (Base64)
- **`XIpClientInterceptor`** — добавляет `x-ip-address` (Base64)
- **`XOsClientInterceptor`** — добавляет `x-os-name` (Base64)
- **`XAppClientInterceptor`** — добавляет `x-app-name` и `x-app-version` (оба Base64)

Все interceptors переопределяют только `AsyncUnaryCall`. Кодирование: `Convert.ToBase64String(Encoding.UTF8.GetBytes(...))`.

## Паттерн использования

```csharp
builder.Services.AddGrpcClient<SomeApi.SomeApiClient>(o =>
    {
        o.Address = new Uri(config["SomeService:Host"]);
    })
    .AddInterceptor(() => new JwtClientInterceptor(config["SomeService:Token"]))
    .AddInterceptor(() => new ExceptionClientInterceptor()); // из BarkFluff.Shared.Exceptions
```

## Важные замечания

- Только `JwtClientInterceptor` передаёт значение **без Base64** — JWT идёт напрямую
- Серверная сторона проверяет через XAuth в [[Backend/GrpcServer]]
- Добавить новый заголовок: константа в `MetadataKeys` + новый interceptor по образцу
