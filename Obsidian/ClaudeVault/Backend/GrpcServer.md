# BarkFluff.GrpcServer

Shared-библиотека (.NET 10.0), подключаемая всеми backend-микросервисами. Предоставляет единую инфраструктуру: аутентификацию (XAuth/JWT), gRPC interceptors, Serilog-логирование, метрики и загрузку параметров из Settings.

Расположение: `Backend/BarkFluff.GrpcServer/`

→ [[Backend/GrpcServer-ProjectMap|Карта проекта — все файлы и их назначение]]

## Liveness / Readiness endpoints

`MapPingEndpoint()` регистрирует анонимный `GET /ping` на listener(ах) сервиса. При доступном процессе возвращает `200 text/plain` с телом `pong`. Endpoint проверяет только доступность listener и не является readiness-проверкой зависимостей.

`MapHealthEndpoints()` = `/ping` + `/health/live` + `/health/ready` (пара к `builder.Services.AddBarkFluffHealth()`). Live отвечает `{status:"alive", instanceId}`. Ready отдаёт кэш фонового `ReadinessMonitorService` (цикл 15 c, без сетевых вызовов на запрос): `{status: healthy|degraded|down|starting, checkedAtUtc, checks:[{name, status, latencyMs, error}], instanceId}`; HTTP 503 только при `down`. Зависимости обнаруживаются из DI автоматически: EF Core DbContext'и (по загруженным сборкам — `AddDbContext` регистрирует только конкретный тип), `IBusControl` (RabbitMQ), `IConnectionMultiplexer` (Redis), `IAmazonS3`, а сервисы могут добавить проверки через optional `IBarkFluffReadinessContributor`. `degraded` = есть деградировавшая/частично недоступная зависимость, `down` = все проверки вернули down. Подключён всем сервисам с HTTP listener ([[Backend/CloudMessaging]] — worker без listener, мониторится [[Backend/AdminPanel]] по docker-state и Seq). См. [[Backend/AdminPanel]] — health-обзор панели и [[Backend/Settings]] — проверка manual-полей.

## Startup-конвейер (порядок вызовов в Program.cs)

```csharp
builder.LoadConfiguration(ServiceId.Xxx);            // 1. Загрузка параметров из Settings service
builder.SetRunningAddress(builder.Configuration);    // 2. Настройка Kestrel (порт, TLS, HTTP/2)
builder.AddBarkFluffSerilog("ServiceName");          // 3. Serilog + Seq (ПОСЛЕ LoadConfiguration)
builder.Services.AddBarkFluffMetrics("ServiceName"); // 4. Метрики
builder.Services.AddBarkFluffGrpc();                 // 5. gRPC + interceptors
builder.Services.AddXAuth(builder.Configuration);    // 6. JWT-аутентификация
app.UseXAuth();                                       // 7. Middleware auth
```

### gRPC Reflection

Во всех активных .NET gRPC-сервисах `AddGrpcReflection()` и `MapGrpcReflectionService()`
вызываются только при `Environment.IsDevelopment()`. В Production, Nightly и Master
endpoint reflection не публикуется.

## XAuth (`XAuth/`)

JWT-аутентификация через заголовок `x-auth-token` (не стандартный Authorization). Две политики:
- `TokenType.User` — принимает User и Service токены
- `TokenType.Service` — только Service токены

`UserContext` — scoped-сервис, извлекает `UserId`, `TokenType` и `DeviceId` из ClaimsPrincipal. Свойство `IsAuthenticated` = true если `UserId != 0` и `TokenType != Unknown`.

### Отзыв сессий (TokenRevocationCache)

`TokenRevocationCache` — singleton in-memory кэш отозванных сессий. При валидации токена (тип `User`) проверяет, не отозвана ли сессия по ключу `{userId}:{deviceId}`. `TokenRevocationCleanupService` — фоновый сервис, каждые 5 минут очищает истёкшие записи.

## Interceptors (через `AddBarkFluffGrpc()`)

- **ServerExceptionInterceptor** — ловит `BaseGrpcException` и необработанные исключения → `RpcException` с trailer `x-error-code`. Уже сформированные `RpcException` сохраняют исходный status/trailers: ожидаемые клиентские статусы (`FailedPrecondition`, `Cancelled`, `InvalidArgument`, `NotFound`, `AlreadyExists`, `PermissionDenied`, `Unauthenticated`, `OutOfRange`) логируются как Warning, инфраструктурные gRPC-статусы — как Error без маркировки «критическая ошибка». Бизнес-ошибки → их доменный `StatusCode`, неизвестные исключения → `StatusCode.Unknown` с фиксированным `BaseGrpcException.ErrorMessage` без передачи `ex.Message` клиенту. Инкрементирует метрики `grpc_requests_total`, `grpc_requests_failed`, `grpc_requests_errors`.
- **RequestContextInterceptor** — извлекает клиентские metadata-заголовки (`x-device-id`, `x-device-name`, `x-ip-address`, `x-os`, `x-app-name`, `x-app-version`) в scoped `RequestContext`. Значения Base64-декодируются. `IpAddress` для legacy-логики резолвится с учётом `x-ip-address`, а `TrustedIpAddress` для security-ключей — только из `X-Real-IP`, `X-Forwarded-For` или `RemoteIpAddress` TCP-соединения; клиентский `x-ip-address` в trusted-адрес не попадает.

## Конфигурация (`WebApplicationBuilderExtensions`)

- `LoadConfiguration(ServiceId)` — перед wire-compatible gRPC-вызовом к Settings проверяет внутреннюю готовность, описанную в [[Backend/Settings]]. Адрес выбирается по `SETTINGS_SERVICE_URL`, `CONFIGURATION_SERVICE_URL`, `SettingsServiceAddr`, `ConfigurationServiceAddr`, затем `http://localhost:7003`. Результат добавляется в `IConfiguration` как in-memory collection.
- `ConfigurationStartupLoader` выполняет до пяти HTTP/2-проверок: первая сразу, четыре паузы по 5 секунд, таймаут запроса 5 секунд. HTTP 200 разрешает загрузку; сетевые ошибки, таймауты и HTTP 5xx повторяются; прочие HTTP-ответы и исчерпание попыток останавливают запуск. Ожидание занимает до примерно 45 секунд с учётом таймаутов. Логи попыток не содержат конфигурации или текста исключений.
- `LoadConfiguration(ServiceId, requireValidSettings: false)` используется только [[Backend/AdminPanel]] для ожидания доступности данных без проверки ручных значений. Остальные 15 startup-потребителей требуют полной валидности. Navigator, ClientStorage, Setup и Web Shell/Proxy не используют этот загрузчик.
- После успешной проверки `GetConfiguration` имеет deadline 5 секунд; его ошибка завершает запуск без повторения цикла readiness. Legacy Proto и общий `/health/ready` не меняются. Settings с новым endpoint должен быть развёрнут до обновлённых потребителей.
- `SetRunningAddress()` — настраивает Kestrel из `RunSettings` (порт, опциональный HTTP/1 порт, TLS).
- `AddSettings<T>(configuration, sectionName)` — регистрирует `IOptions<T>` и `T` как singleton.

## Метрики (`Metrics/`)

- `MetricsCollector` — thread-safe counters/gauges (`ConcurrentDictionary`) с профилем экспорта. Низкочастотные бизнес-события уходят отдельной дельтой сразу; counters высоконагруженных сервисов и все байтовые показатели копятся атомарно. `SetMany()` обновляет связанную группу gauges под одной блокировкой, а exporter забирает её тем же критическим участком — снимок не смешивает стадии разных операций (используется [[Backend/Files|Files]] для длительностей upload).
- `MetricsReporterService` — экспортирует `ServiceMetrics` **schema v2**: `Counters` — дельты, `Gauges` — состояние. Буфер flush-ится раз в 10 секунд только при ненулевых counters или изменившихся gauges; в простое событий Seq нет. Профили high-throughput заданы для сообщений, файлов, realtime/fan-out, трафика, HTTP/gRPC и S2S.

## Логирование

`AddBarkFluffSerilog(serviceName)` — Serilog → Console + Seq. Enrichers: MachineName, EnvironmentName, ThreadId, Application и `ActivityLogEnricher`. При наличии текущего W3C `Activity` каждый event получает `TraceId`, `SpanId` и fallback `CorrelationId`; явно заданный бизнес-correlation ID не затирается. `RequestContextInterceptor` добавляет `RequestId` (`HttpContext.TraceIdentifier`) в gRPC-логи и принимает явный `X-Correlation-ID`. Microsoft/EF Core/HttpClient логи подавлены до Warning. Seq sink дуральный с файловым буфером (`logs/seq-buffer`, лимит 100MB, batch 100 событий, flush каждые 2 секунды).

## Зависимости

- [[Shared/Exceptions]] — `BaseGrpcException`
- [[Shared/Auth]] — `MetadataKeys`
- [[Shared/Identity]] — `ServiceId`, `TokenType`, `IdentityClaims`
- `configuration_api.proto` — wire-compatible gRPC-клиент для Settings service
