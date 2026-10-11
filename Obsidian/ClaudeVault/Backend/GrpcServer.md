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
