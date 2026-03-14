# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Purpose

`BarkFluff.Shared.Auth` — библиотека gRPC client interceptors для добавления обязательных метаданных к каждому межсервисному вызову. Используется всеми клиентами (backend-сервисы, WPF, Android) при регистрации gRPC-клиентов.

## Build

```bash
dotnet build BarkFluff.Shared.Auth.csproj
```

## Architecture

Библиотека содержит только interceptors и константы, никакой бизнес-логики:

- **`MetadataKeys`** — константы имён gRPC-заголовков (`x-auth-token`, `x-device-id`, `x-device-name`, `x-ip-address`, `x-os-name`, `x-app-name`, `x-app-version`)
- **`JwtClientInterceptor`** — добавляет `x-auth-token` как есть (plain string)
- **`XDeviceIdInterceptor`** — добавляет `x-device-id` (Base64)
- **`XDeviceClientInterceptor`** — добавляет `x-device-name` (Base64)
- **`XIpClientInterceptor`** — добавляет `x-ip-address` (Base64)
- **`XOsClientInterceptor`** — добавляет `x-os-name` (Base64)
- **`XAppClientInterceptor`** — добавляет `x-app-name` и `x-app-version` (оба Base64)

Все interceptors переопределяют только `AsyncUnaryCall`. Строковые значения (кроме JWT-токена) кодируются в Base64 через `Convert.ToBase64String(Encoding.UTF8.GetBytes(...))`.

## Usage Pattern

```csharp
builder.Services.AddGrpcClient<SomeApi.SomeApiClient>(o =>
    {
        o.Address = new Uri(config["SomeService:Host"]);
    })
    .AddInterceptor(() => new JwtClientInterceptor(config["SomeService:Token"]))
    .AddInterceptor(() => new XDeviceIdInterceptor(deviceId))
    .AddInterceptor(() => new XDeviceClientInterceptor(deviceName))
    .AddInterceptor(() => new XIpClientInterceptor(ipAddress))
    .AddInterceptor(() => new XOsClientInterceptor(osName))
    .AddInterceptor(() => new XAppClientInterceptor(appName, appVersion))
    .AddInterceptor(() => new ExceptionClientInterceptor()); // из BarkFluff.Shared.Exceptions
```

## Important Notes

- Только `JwtClientInterceptor` передаёт значение без Base64 — JWT-токен идёт напрямую
- Если нужно добавить новый обязательный заголовок — добавить константу в `MetadataKeys` и создать новый interceptor по образцу существующих
- Серверная сторона проверяет эти заголовки через XAuth в `BarkFluff.GrpcServer`
