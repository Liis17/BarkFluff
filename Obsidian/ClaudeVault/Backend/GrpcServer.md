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

`AddBarkFluffSerilog(serviceName)` — Serilog → Console + выбранный backend: `Logging:Backend=Seq` (по умолчанию) либо `Lognara`. Enrichers: MachineName, EnvironmentName, ThreadId, Application и `ActivityLogEnricher`. При наличии текущего W3C `Activity` каждый event получает `TraceId`, `SpanId`, доступный `ParentSpanId` и fallback `CorrelationId`; явно заданный бизнес-correlation ID не затирается. Console показывает trace/span ID. Microsoft/EF Core/HttpClient логи подавлены до Warning.

Seq сохраняет прежние структурированные свойства и дисковый буфер `logs/seq-buffer` (100 MiB, batch 100 событий, flush 2 секунды). Lognara получает JSON-массивы через HTTP в `Lognara:AgentUrl` (по умолчанию локальный агент `http://127.0.0.1:7400/v1/logs`), с буфером `logs/lognara-buffer` (10 файлов примерно по 10 MiB). `LognaraJsonFormatter` переносит захваченные ID события в канонические `trace_id`/`span_id`/`parent_span_id`/`request_id`; остальные свойства сохраняет в `attributes`, включая `Metrics` schema v2. Exception сохраняется как `exception.type`/`exception.stacktrace` в attributes. Lognara-agent настраивается отдельно; `LOGNARA_SERVICE`, `LOGNARA_SERVER`, `LOGNARA_BACKEND` задают источник. При выборе Lognara-only Seq-страницы логов и агрегация метрик в [[Backend/AdminPanel]] не получают новые сервисные события.

`AddBarkFluffSerilog` также регистрирует `AddBarkFluffTracing`: `RequestTraceStartupFilter` охватывает весь HTTP-конвейер, в том числе все формы gRPC и логи exception interceptors, добавляя `RequestId` и входящий `X-Correlation-ID`. Существующий ASP.NET Activity сохраняется; fallback создаётся только при его отсутствии. `TraceDiagnostics` включает Activities ASP.NET Core, HttpClient, gRPC client и MassTransit с sampling `PropagationData`, без exporter и сбора span payload. Так штатные .NET и MassTransit механизмы переносят контекст исходящих вызовов и очередей. `Tracing/TraceContext` восстанавливает parent/tracestate и business correlation для outbox; каждая попытка получает новый SpanId при прежнем TraceId, старые строки без контекста получают отдельную корневую Activity. Стартап и периодические сводки метрик не получают выдуманный request trace. Инструкция включения и ограничения: `Docs/Logging.md`.
