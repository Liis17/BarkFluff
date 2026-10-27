# BarkFluff.GrpcServer

Общая ASP.NET/gRPC инфраструктура. Подключение компонентов задаётся каждым host Program.cs; наличие библиотеки само по себе не включает XAuth или health routes. Карта модулей: [[Backend/GrpcServer-ProjectMap]].

## XAuth и request context

XAuth читает JWT из plaintext metadata x-auth-token, проверяет подпись, issuer, audience и lifetime без ClockSkew; User-токены также сверяются с кэшем отозванных device sessions. Политики: Service принимает только Service, User принимает User и Service, Bot принимает только Bot. Endpoints должны явно требовать нужную policy.

Текущие host apps с UseXAuth: Bots, Calls, FastAuth, Federation, Files, Identity, Messages, Navigator, Onliner, Updates, Users и Developers. Это не означает, что каждый RPC защищён одинаково.

Client metadata: x-device-name, x-os-name, x-app-name, x-app-version, x-ip-address и x-device-id содержат Base64 от UTF-8. XAuth token не Base64. RequestContextInterceptor разбирает эти значения. IpAddress может включать присланный клиентом x-ip-address; TrustedIpAddress берёт proxy headers/socket peer, без клиентского metadata. Используйте trusted value для security decisions.

## Health

AddBarkFluffHealth() и MapHealthEndpoints() — opt-in пара. Сейчас её используют Beacon, Bots, Calls, FastAuth, Federation, Files, Identity, Messages, Navigator, Notification, Onliner, Settings, Updates, Users, Web и Developers. Она отображает GET /ping, анонимные /health/live и /health/ready.

ReadinessMonitorService обновляет кэш раз в 15 секунд, проверяет найденные в DI EF DbContext, RabbitMQ, Redis, S3 и IBarkFluffReadinessContributor. Запрос /health/ready не запускает сетевую пробу: healthy/degraded/starting/unknown возвращают 200, down — 503. Сборщик AdminPanel — отдельный механизм. Setup также имеет самостоятельный /health/live.

## Метрики, ошибки, конфигурация

MetricsCollector публикует counters и gauges в структурированный лог ServiceMetrics со SchemaVersion=2. Профили сервисов задают немедленную отправку бизнес-событий либо буферизацию; buffered counters и изменённые gauges flush не чаще раза в 10 секунд. Idle snapshots не отправляются.

AddBarkFluffGrpc() регистрирует ServerExceptionInterceptor и RequestContextInterceptor. LoadConfiguration(ServiceId) получает строки из Settings при запуске, SetRunningAddress() настраивает listener по RunSettings/TLS; не предполагайте live reload. gRPC Reflection по правилам хоста включается только в Development.

Источники: Backend/BarkFluff.GrpcServer/XAuth/, Tracker/, Metrics/, HealthEndpointExtensions.cs, HealthServiceCollectionExtensions.cs, WebApplicationBuilderExtensions.cs.

## Ожидание конфигурации при запуске

`LoadConfiguration(ServiceId)` перед загрузкой конфигурации проверяет внутреннюю готовность [[Backend/Settings]].
Адрес выбирается по `SETTINGS_SERVICE_URL`, `CONFIGURATION_SERVICE_URL`, `SettingsServiceAddr`,
`ConfigurationServiceAddr`, затем `http://localhost:7003`. Результат добавляется в `IConfiguration`.

`ConfigurationStartupLoader` выполняет до пяти HTTP/2-проверок: первая сразу, четыре паузы по 5 секунд,
таймаут запроса 5 секунд. HTTP 200 разрешает загрузку; сетевые ошибки, таймауты и HTTP 5xx повторяются;
прочие ответы и исчерпание попыток останавливают запуск. Ожидание занимает до примерно 45 секунд.
Логи попыток не содержат конфигурации или текста исключений.

`LoadConfiguration(ServiceId, requireValidSettings: false)` используется только [[Backend/AdminPanel]]
для ожидания доступности данных без проверки ручных значений. Остальные 15 startup-потребителей требуют
полной валидности. Navigator, ClientStorage, Setup и Web Shell/Proxy не используют этот загрузчик.

После успешной проверки `GetConfiguration` имеет deadline 5 секунд; его ошибка завершает запуск без
повторения readiness. Legacy Proto и общий `/health/ready` сохраняются. Settings с новым endpoint
должен быть развёрнут до обновлённых потребителей.

## Логирование и trace-контекст

`AddBarkFluffSerilog(serviceName)` подключает независимые sinks по root-level flags `ConsoleOutput` (default `true`), `SeqEnable` (default `true`) и `LognaraEnable` (default `false`). Settings хранит их отдельно в 15 service scopes, без наследования из `GlobalSettings`. Приоритет для flags и параметров агента: CLI → environment → соответствующие Settings values → local configuration. Settings, Setup, Navigator, ClientStorage, WebServer и AdminPanel используют локальные flags при старте; AdminPanel загружает `ServiceId.Unknown`. Enrichers: MachineName, EnvironmentName, ThreadId, Application и `ActivityLogEnricher`. При наличии текущего W3C `Activity` каждый event получает `TraceId`, `SpanId`, доступный `ParentSpanId` и fallback `CorrelationId`; явно заданный бизнес-correlation ID не затирается. Console показывает trace/span ID; minimum level — Information, Microsoft/EF Core/HttpClient logs подавлены до Warning.

Settings инициализирует logger до готовности базы; его startup events используют local configuration
и CLI/environment overrides и не могут быть изменены задним числом значениями из Settings.

Seq сохраняет structured properties и disk buffer `logs/seq-buffer` (100 MiB, batch 100 событий, flush 2 секунды). Lognara использует `Serilog.Sinks.Lognara` 0.1.2: события передаются в фоне с retries, batch — до 100 событий и целевого payload 1 MiB, `logs/lognara-buffer` ограничен четырьмя файлами примерно по 16 MiB плюс служебные файлы, события больше 256 KiB отбрасываются. Последнее событие может немного превысить размер файла, а при достижении лимита удаляются старые файлы. Canonical `trace_id` и `span_id` находятся в top-level JSON; parent span, request ID и business correlation ID — в `attributes`, как и `Metrics` schema v2 в `attributes.Metrics`. Exception сериализуется top-level string. `Lognara:AgentUrl` по умолчанию `http://127.0.0.1:7400/v1/logs`. Настроенный через конфигурацию процесса `Lognara:AgentExecutable` запускает HTTP-only agent на loopback в том же контейнере; при этом `AgentUrl` должен оставаться loopback endpoint. Без executable приложение использует внешний agent по `AgentUrl`. Embedded agent получает `LOGNARA_RELAY_URL` и `LOGNARA_RELAY_TOKEN`; по умолчанию relay URL — `http://lognara-relay:7401/v1/batches`. `LOGNARA_SERVICE`, `LOGNARA_SERVER`, `LOGNARA_BACKEND` и `LOGNARA_ENVIRONMENT` задают source identity. `Logging:Backend` больше не выбирает sink; прежняя строка Settings и её history сохраняются. При Lognara-only Seq-страницы логов и rollups метрик в [[Backend/AdminPanel]] не получают новые service events.

При включённом Lognara и настроенном `Lognara:AgentExecutable` отсутствие `LOGNARA_RELAY_TOKEN`
или ошибка запуска/readiness агента останавливает старт host; startup cleanup завершает уже созданный
процесс агента. Неожиданный exit embedded agent останавливает host с exit code 1 даже при включённых
Seq/Console. При нормальном shutdown сначала завершаются workers, затем Serilog сбрасывает очередь, после
чего агенту отправляется SIGTERM.
Истечение 10-секундного readiness deadline сообщает `TimeoutException`; отмена startup вызывающим token
сохраняет `OperationCanceledException`.

`AddBarkFluffSerilog` также регистрирует `AddBarkFluffTracing`: `RequestTraceStartupFilter` охватывает весь HTTP-конвейер, в том числе все формы gRPC и логи exception interceptors, добавляя `RequestId` и входящий `X-Correlation-ID`. Существующий ASP.NET Activity сохраняется; fallback создаётся только при его отсутствии. `TraceDiagnostics` включает Activities ASP.NET Core, HttpClient, gRPC client и MassTransit с sampling `PropagationData`, без exporter и сбора span payload. Так штатные .NET и MassTransit механизмы переносят контекст исходящих вызовов и очередей. `Tracing/TraceContext` восстанавливает parent/tracestate и business correlation для outbox; каждая попытка получает новый SpanId при прежнем TraceId, старые строки без контекста получают отдельную корневую Activity. Стартап и периодические сводки метрик не получают выдуманный request trace. Инструкция включения и ограничения: `Docs/Logging.md`.
