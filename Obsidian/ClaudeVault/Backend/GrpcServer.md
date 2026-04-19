# BarkFluff.GrpcServer

Shared-библиотека (.NET 9.0), подключаемая всеми backend-микросервисами. Предоставляет единую инфраструктуру: аутентификацию (XAuth/JWT), gRPC interceptors, Serilog-логирование, метрики и загрузку конфигурации.

Расположение: `Backend/BarkFluff.GrpcServer/`

## Startup-конвейер (порядок вызовов в Program.cs)

```csharp
builder.LoadConfiguration(ServiceId.Xxx);            // 1. Загрузка конфига из Configuration service
builder.SetRunningAddress(builder.Configuration);    // 2. Настройка Kestrel (порт, TLS, HTTP/2)
builder.AddBarkFluffSerilog("ServiceName");          // 3. Serilog + Seq (ПОСЛЕ LoadConfiguration)
builder.Services.AddBarkFluffMetrics("ServiceName"); // 4. Метрики
builder.Services.AddBarkFluffGrpc();                 // 5. gRPC + interceptors
builder.Services.AddXAuth(builder.Configuration);    // 6. JWT-аутентификация
app.UseXAuth();                                       // 7. Middleware auth
```

## XAuth (`XAuth/`)

JWT-аутентификация через заголовок `x-auth-token` (не стандартный Authorization). Две политики:
- `TokenType.User` — принимает User и Service токены
- `TokenType.Service` — только Service токены

`UserContext` — scoped-сервис, извлекает `UserId` и `TokenType` из ClaimsPrincipal.

## Interceptors (через `AddBarkFluffGrpc()`)

- **ServerExceptionInterceptor** — ловит `BaseGrpcException` и необработанные исключения → `RpcException` с trailer `x-error-code`. Бизнес-ошибки → `StatusCode.FailedPrecondition`, неизвестные → `StatusCode.Unknown`.
- **RequestContextInterceptor** — извлекает клиентские metadata-заголовки (`x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`) в scoped `RequestContext`. Значения в Base64.

## Конфигурация (`WebApplicationBuilderExtensions`)

- `LoadConfiguration(ServiceId)` — gRPC-вызов к Configuration service (адрес из `CONFIGURATION_SERVICE_URL` env или `ConfigurationServiceAddr`). Результат в `IConfiguration` как in-memory collection.
- `SetRunningAddress()` — настраивает Kestrel из `RunSettings` (порт, опциональный HTTP/1 порт, TLS).
- `AddSettings<T>(configuration, sectionName)` — регистрирует `IOptions<T>` и `T` как singleton.

## Метрики (`Metrics/`)

- `MetricsCollector` — thread-safe counters/gauges (`ConcurrentDictionary`). Counters сбрасываются при snapshot, gauges сохраняются.
- `MetricsReporterService` — BackgroundService, каждые 5 секунд пишет snapshot в structured log.

## Логирование

`AddBarkFluffSerilog(serviceName)` — Serilog → Console + Seq. Enrichers: MachineName, EnvironmentName, ThreadId, Application. Microsoft/EF Core логи подавлены до Warning.

## Зависимости

- [[Shared/Exceptions]] — `BaseGrpcException`
- [[Shared/Auth]] — `MetadataKeys`
- [[Shared/Identity]] — `ServiceId`, `TokenType`, `IdentityClaims`
- `configuration_api.proto` — gRPC-клиент для Configuration service
