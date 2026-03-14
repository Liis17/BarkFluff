# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

BarkFluff.GrpcServer — shared library (.NET 9.0, net9.0), подключаемая всеми backend-микросервисами BarkFluff. Предоставляет единую инфраструктуру: аутентификацию (XAuth/JWT), gRPC interceptors, Serilog-логирование, метрики и загрузку конфигурации из Configuration service.

## Сборка

```bash
dotnet build Backend/BarkFluff.GrpcServer/BarkFluff.GrpcServer.csproj
```

Тестов нет — это shared library без собственного запуска.

## Архитектура и ключевые компоненты

### Startup-конвейер микросервиса (порядок вызовов в Program.cs потребителя)

```csharp
builder.LoadConfiguration(ServiceId.Xxx);       // 1. Загрузка конфига из Configuration service
builder.SetRunningAddress(builder.Configuration); // 2. Настройка Kestrel (порт, TLS, HTTP/2)
builder.AddBarkFluffSerilog("ServiceName");      // 3. Serilog + Seq (вызывать ПОСЛЕ LoadConfiguration)
builder.Services.AddBarkFluffMetrics("ServiceName"); // 4. Метрики
builder.Services.AddBarkFluffGrpc();             // 5. gRPC + interceptors
builder.Services.AddXAuth(builder.Configuration); // 6. JWT-аутентификация
// ...
app.UseXAuth();                                   // 7. Middleware auth
```

### XAuth (`XAuth/`)

JWT-аутентификация через заголовок `x-auth-token` (не стандартный Authorization). Две политики авторизации:
- `TokenType.User` — принимает User и Service токены
- `TokenType.Service` — только Service токены

`UserContext` — scoped-сервис, извлекает `UserId` и `TokenType` из ClaimsPrincipal.

### Interceptors (регистрируются через `AddBarkFluffGrpc()`)

- **ServerExceptionInterceptor** — ловит `BaseGrpcException` (бизнес-ошибки) и необработанные исключения, преобразует в `RpcException` с trailer `x-error-code`. Бизнес-ошибки → `StatusCode.FailedPrecondition`, неизвестные → `StatusCode.Unknown`.
- **RequestContextInterceptor** — извлекает клиентские metadata-заголовки (`x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`) в scoped `RequestContext`. Значения передаются в Base64.

### Конфигурация (`WebApplicationBuilderExtensions`)

- `LoadConfiguration(ServiceId)` — gRPC-вызов к Configuration service (адрес из `CONFIGURATION_SERVICE_URL` env или `ConfigurationServiceAddr` в appsettings). Результат добавляется в `IConfiguration` как in-memory collection.
- `SetRunningAddress()` — настраивает Kestrel из `RunSettings` (порт, опциональный HTTP/1 порт, опциональный TLS).

### Settings

- `AddSettings<T>(configuration, sectionName)` — generic-хелпер: регистрирует `IOptions<T>` и сам `T` как singleton.
- `RunSettings` — порт, опциональный Http1Port, опциональный TLS (filename + password).

### Метрики (`Metrics/`)

- `MetricsCollector` — thread-safe counters/gauges (`ConcurrentDictionary`). Counters сбрасываются при snapshot, gauges сохраняются.
- `MetricsReporterService` — `BackgroundService`, каждые 5 секунд пишет snapshot метрик в structured log.

### Логирование (`SerilogExtensions`)

`AddBarkFluffSerilog(serviceName)` — Serilog → Console + Seq. Enrichers: MachineName, EnvironmentName, ThreadId, Application. Microsoft/EF Core логи подавлены до Warning.

## Зависимости

- `BarkFluff.Shared.Exceptions` — `BaseGrpcException` и наследники
- `BarkFluff.Shared.Auth` — `MetadataKeys` (константы имён заголовков)
- `BarkFluff.Shared.Identity` — `ServiceId`, `TokenType`, `IdentityClaims`
- `configuration_api.proto` — gRPC-клиент для Configuration service
