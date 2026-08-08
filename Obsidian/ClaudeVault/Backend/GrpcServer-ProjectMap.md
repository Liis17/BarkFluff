# BarkFluff.GrpcServer — Карта проекта

Shared-библиотека инфраструктуры. Подключается всеми backend-микросервисами.
Расположение: `Backend/BarkFluff.GrpcServer/`

← [[Backend/GrpcServer|Документация BarkFluff.GrpcServer]]

---

## Структура файлов

### Корень (`BarkFluff.GrpcServer/`)

| Файл | Назначение |
|------|-----------|
| `WebApplicationBuilderExtensions.cs` | Extension-методы для `WebApplicationBuilder`: `LoadConfiguration(ServiceId)` — загружает конфиг из Configuration service по gRPC; `SetRunningAddress()` — настраивает Kestrel (порт, TLS, HTTP/2, опциональный HTTP/1). |
| `PingEndpointExtensions.cs` | `MapPingEndpoint()` — регистрирует анонимный `GET /ping`, возвращающий `200 text/plain` с телом `pong`. |
| `ServiceCollectionExtensions.cs` | Extension-методы для `IServiceCollection`: `AddSettings<T>()` — регистрирует `IOptions<T>` и `T` как singleton; `AddBarkFluffGrpc()` — регистрирует gRPC с двумя interceptors (`ServerExceptionInterceptor`, `RequestContextInterceptor`), scoped `IRequestContextAccessor` (writer) и scoped-фабрику `RequestContext`, делегирующую accessor'у. |
| `SerilogExtensions.cs` | Extension-методы для Serilog: `AddBarkFluffSerilog(serviceName)` — настраивает Serilog → Console + Seq (дуральный sink с файловым буфером `logs/seq-buffer`, лимит 100MB); `AddBarkFluffMetrics(serviceName)` — регистрирует `MetricsCollector` (singleton) и `MetricsReporterService` (HostedService). |
| `ServerExceptionInterceptor.cs` | gRPC Unary interceptor. Перехватывает `BaseGrpcException` → `RpcException(FailedPrecondition)` с trailer `x-error-code`; необработанные исключения → `RpcException(Unknown)`. Инкрементирует метрики `grpc_requests_total`, `grpc_requests_failed`, `grpc_requests_errors`. |

---

### `Metrics/`

| Файл | Назначение |
|------|-----------|
| `MetricsCollector.cs` | Потокобезопасный сборщик метрик (`ConcurrentDictionary`). **Counters**: `Increment(name)`, `Add(name, value)` — сбрасываются при `SnapshotAndReset()`. **Gauges**: `Set(name, value)` — сохраняются между снапшотами. |
| `MetricsReporterService.cs` | `BackgroundService`. Тикает каждые 5 секунд: `SnapshotAndReset(out hadCounterActivity)`. При активности (ненулевые counters) пишет полный `ServiceMetrics {@Metrics}` (ServiceName, Metrics, Timestamp). В простое (только статичные gauges) — gauge-heartbeat не чаще раза в 5 минут (`IdleHeartbeatEveryTicks = 60`), чтобы не спамить Seq, но сохранить uptime/`db_healthy` в AdminPanel. |

---

### `Settings/`

| Файл | Назначение |
|------|-----------|
| `RunSettings.cs` | POCO-модель конфигурации запуска: `Host`, `Port` (int, обязательный), `Http1Port` (int?, опциональный), `Tls` (TlsSettings?). Раздел конфига: `RunSettings`. |
| `TlsSettings.cs` | POCO-модель TLS: `Filename` (путь к сертификату), `Password`. Вложен в `RunSettings`. |

---

### `Tracker/`

| Файл | Назначение |
|------|-----------|
| `RequestContext.cs` | Иммутабельный POCO-контейнер метаданных входящего запроса: `OperationSystem`, `IpAddress`, `DeviceName`, `AppName`, `AppVersion`, `DeviceId`. Все свойства `init`-only. Создаётся interceptor'ом, читается бизнес-кодом. |
| `IRequestContextAccessor.cs` | Scoped-accessor: `Current` возвращает текущий `RequestContext` (бросает `InvalidOperationException`, если ещё не инициализирован), `Set(...)` вызывается из interceptor'а один раз за scope. Бизнес-код инжектит `RequestContext` напрямую через scoped-фабрику в DI. |
| `RequestContextInterceptor.cs` | gRPC Unary interceptor. Создаёт новый `RequestContext` по metadata-заголовкам (Base64) и регистрирует через `IRequestContextAccessor.Set()`. IP-адрес резолвится по приоритету: 1) `x-ip-address` из gRPC metadata, 2) `X-Forwarded-For` HTTP-заголовок (первый IP), 3) `X-Real-IP` (nginx), 4) `RemoteIpAddress` TCP-соединения. |

---

### `XAuth/`

| Файл | Назначение |
|------|-----------|
| `XAuthExtensions.cs` | Extension-методы `AddXAuth(configuration)` и `UseXAuth()`. Настраивает JWT через заголовок `x-auth-token` (не стандартный `Authorization`). Регистрирует две политики авторизации: `User` (принимает User + Service токены), `Service` (только Service). Подключает `TokenRevocationCache`, `TokenRevocationCleanupService`, `UserContext`. |
| `UserContext.cs` | Scoped-сервис. Извлекает из `ClaimsPrincipal` текущего HTTP-контекста: `UserId` (long), `TokenType` (enum), `DeviceId` (string?). Свойство `IsAuthenticated` — true если `UserId != 0` и `TokenType != Unknown`. |
| `TokenRevocationCache.cs` | Singleton in-memory кэш отозванных сессий (`ConcurrentDictionary<string, DateTime>`). Ключ: `{userId}:{deviceId}`. Хранит дату истечения токена. `Revoke()`, `IsRevoked()`, `Cleanup()` (удаляет просроченные записи). |
| `TokenRevocationCleanupService.cs` | `BackgroundService`. Каждые 5 минут вызывает `TokenRevocationCache.Cleanup()` для очистки истёкших отозванных сессий. |

---

## Зависимости библиотеки

| Зависимость | Используется для |
|-------------|-----------------|
| [[Shared/Exceptions]] | `BaseGrpcException` в `ServerExceptionInterceptor` |
| [[Shared/Auth]] | `MetadataKeys` в `RequestContextInterceptor` |
| [[Shared/Identity]] | `ServiceId`, `TokenType`, `IdentityClaims` в XAuth и UserContext |
| `configuration_api.proto` | gRPC-клиент для загрузки конфигурации из Configuration service |
| `Serilog`, `Serilog.Sinks.Seq` | Структурированное логирование |
| `Grpc.AspNetCore` | gRPC сервер + interceptors |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT аутентификация |
