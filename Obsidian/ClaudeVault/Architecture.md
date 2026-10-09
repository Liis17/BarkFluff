# Архитектура BarkFluff

Parent: [[Index]]

BarkFluff — распределённый мессенджер: каталог нод, самостоятельные серверные контуры и нативные/веб-клиенты. Публичные контракты находятся в `Shared/BarkFluff.Proto/`; настройки и инфраструктура не заменяют авторизацию бизнес-API.

## Стек и границы

| Слой | Технологии и источники |
|------|-----------------------|
| Backend/Shared | .NET 10, ASP.NET Core/gRPC, MediatR, MassTransit/RabbitMQ, PostgreSQL/EF Core, Redis, MinIO; проекты `Backend/`, `Shared/` |
| SDK | `global.json`: 10.0.401, `rollForward: disable`, prerelease запрещён |
| Android | Kotlin 2.2.20, AGP 8.9.1, gRPC-OkHttp 1.60.0, ViewBinding и Compose; `Android/gradle/libs.versions.toml`, `Android/core/build.gradle.kts` |
| Windows | Legacy WPF, WPF V2 и WinUI 3; отдельный транспорт WebApi.Core; [[Clients/Windows-WPF]], [[Clients/Windows-WPF-V2]], [[Clients/Windows-WinUI]] |
| Apple | Swift/SwiftUI и общий BFCore; [[Clients/macOS]], [[Clients/iOS]] |
| Linux | Qt 6/C++20, CMake, gRPC; [[Clients/Linux-Qt]] |
| Web | Vanilla-JS SPA и gRPC-Web gateway; портал разработчиков — отдельный React/Vite проект; [[Clients/Web]], [[Clients/Developers-Web]] |

`Backend/BarkFluff.GrpcServer/` — библиотека общих startup, auth, tracing, metrics и health механизмов, а не самостоятельный хост. Доменные сервисы используют `Features/`/MediatR handlers, `Host/`, `Persistence/`, `Infrastructure/` и `Services/` по потребности; структура не является обязательной для всех проектов.

## Сервисы и порты

Порты ниже — дефолты/контейнерные listener-ы, а не локальные `launchSettings.json`. Конкретное развёртывание может переопределить их. Источники: `Backend/PORTS_CONFIGURATION.md`, сервисные `Program.cs`/`appsettings.json`, `Docker/nightly/barkfluff/docker-compose.yml`.

| Сервис | Listener | Ответственность |
|--------|----------|-----------------|
| [[Backend/Settings]] | 7003 | Централизованные настройки и compatibility/setup API |
| [[Backend/Setup]] | HTTP 7032 | Первичная настройка Settings |
| [[Backend/Beacon]] | 7002 | Метаданные ноды и адреса сервисов |
| [[Backend/Navigator]] | 7010 | Каталог и регистрация нод |
| [[Backend/Identity]] | 7000 | JWT, challenge auth, OTP, сессии |
| [[Backend/Users]] | 7001 | Профили, устройства, prekeys, папки чатов |
| [[Backend/Messages]] | 7007 | Regular/private/secret чаты, сообщения, поиск и федеративные операции |
| [[Backend/Files]] | gRPC 7005, HTTP/1 7006 | S3, upload/download, превью и стикеры |
| [[Backend/Updates]] | 7015 | Потоки событий и push-routing |
| [[Backend/Onliner]] | 7009 | Presence и typing/recording |
| [[Backend/FastAuth]] | 7008 | Авторизация устройств по QR |
| [[Backend/Notification]] | 7004 | RabbitMQ → SMTP; HTTP health, без публичного gRPC API |
| [[Backend/Developers]] | HTTP/2 7020, HTTP/1 7021 | Документация/API портала разработчиков |
| [[Backend/Calls]] | gRPC 7025, HTTP/1 7026 | Управление звонками; медиа через LiveKit SFU |
| [[Backend/Bots]] | gRPC 7027, HTTP/1 7028 | Bot API, BotFather и встроенные боты |
| [[Backend/Federation]] | 7030 | Межсерверная доставка и XFed |
| [[Backend/Web]] | 7016 | gRPC-Web gateway, файловые маршруты и статика |
| [[Backend/AdminPanel]] | HTTP 51888 | Управление, Seq/health, Docker и SSH-мост |
| [[Backend/WebServer]] | HTTP 64641 fallback | Публичный сайт/HTTP; `WEBSERVER_PORT` переопределяет порт |
| [[Backend/ClientStorage]] | `CLIENTSTORAGE_PORT` | Дистрибутивы клиентов; URL listener задаёт compose |
| [[Backend/CloudMessaging]] | ASP.NET listener из RunSettings | Firebase worker; HTTP routes/health не зарегистрированы |

Внешние TLS/subdomain маршруты задаёт [[Backend/Nginx]]; его адреса не равны внутренним портам сервисов.

## Настройки, запуск и discovery

1. Bootstrap `Docker/nightly/barkfluff/docker-compose.setup.yml` запускает PostgreSQL, Settings setup mode и Setup UI.
2. После настройки основной `docker-compose.yml` обращается к `SETTINGS_SERVICE_URL`; новые конфиги не должны использовать legacy `CONFIGURATION_SERVICE_URL`.
3. Настраиваемые через Settings хосты вызывают `LoadConfiguration(ServiceId.Xxx)`. Wire-имя `ConfigurationApi` сохранено для совместимости; `ServiceId` маршрутизирует запрос, а не хранится как универсальная запись настроек. Общие `GlobalSettings` перекрываются сервисными значениями по полному IConfiguration-пути. Детали и границы поддержки — [[Backend/Settings]].
4. Нативный клиент выбирает ноду через Navigator, получает service endpoints через Beacon и создаёт отдельные каналы. Межсервисные вызовы — прямой gRPC с Service JWT; асинхронные события — RabbitMQ.

Типовые общие регистрации: `SetRunningAddress`, `AddBarkFluffSerilog`, `AddBarkFluffMetrics`, `AddBarkFluffGrpc`; `AddXAuth`/`UseXAuth` нужны только использующим XAuth хостам. Reflection в gRPC хостах ограничен `IsDevelopment()`. Конкретную последовательность смотри в `Program.cs`, не копируй универсальный startup во все проекты.

## Авторизация и границы доверия

XAuth из `Backend/BarkFluff.GrpcServer/XAuth/` читает JWT из `x-auth-token`, проверяет подпись, issuer, audience и lifetime с нулевым clock skew. Политика User принимает User/Service claims, Service — только Service, Bot — только Bot. User JWT дополнительно проверяется по отзыву сессии в `TokenRevocationCache`.

Точные device metadata из `Shared/BarkFluff.Shared.Auth/MetadataKeys.cs`: `x-device-id`, `x-device-name`, `x-os-name`, `x-app-name`, `x-app-version`, `x-ip-address`. Токен не кодируется в Base64; правила device metadata и client interceptors — [[Shared/Auth]]. XAuth не подключён ко всем хостам: Settings/Setup/AdminPanel и gateway имеют собственные границы доступа, описанные в их заметках.

Identity high-risk RPC используют Redis `IIdentityAbuseGuard` с атомарными счётчиками/TTL и fail-closed при недоступности Redis. Доверенный IP берётся из proxy/TCP, а клиентский `x-ip-address` остаётся legacy-данными для логов/геолокации. Nginx отдельно ограничивает Identity; детали — [[Backend/Identity]], [[Backend/Nginx]]. Federation использует отдельный XFed-контур ([[Backend/Federation]]).

## События, outbox и масштабирование

События определяются в [[Shared/Queue]], публикуются через MassTransit. Именованная очередь означает competing consumers: событие получает один экземпляр. Для broadcast-инвалидации и локальных streaming-подписок нужна отдельная очередь на экземпляр; `InstanceId.Current` из `Backend/BarkFluff.GrpcServer/XAuth/InstanceId.cs` использует HOSTNAME или process GUID. Пример — session revocation; обоснование: `docs/scaling/_shared-token-revocation.md`.

SendMessage в Messages сохраняет сообщение и `NewMessageEvent` в одной PostgreSQL-транзакции через `MessagesStorage.AddMessageWithOutboxAsync`. Dispatcher claim-ит строки с lease и публикует со стабильным `EventId`; retry/reclaim дают **at-least-once**. Crash после publish до delivered допускает повтор; exactly-once consumer inbox не подразумевается. Область применения, lease/backoff/cleanup — [[Backend/Messages]]. Другие публикации нельзя автоматически считать transactional outbox.

## Браузерный поток

Браузер использует gRPC-Web через [[Backend/Web]], а не нативные per-service каналы:

- **Shell** раздаёт глобальную статику и проксирует только Navigator. Выбранный `ServerInfo.web_endpoint` сохраняется клиентом.
- **Node** обслуживает ноду: business gRPC-Web, file upload/media, long-lived streams, `/node-config.js` и закреплённую конфигурацию.
- **Proxy** проксирует в Web gateway выбранной ноды; разрешённые media-hosts контролируются отдельно.

После выбора ноды браузер обращается прямо к её origin; глобальный shell не пересылает business traffic. Beacon через gateway уточняет метаданные и `livekit_url`. Детали auth/reconnect/TLS UI — [[Clients/Web]] и [[Clients/Web-Network-Reliability]].

## Наблюдаемость и health

Общие HTTP-хосты, зарегистрировавшие `AddBarkFluffHealth`/`MapHealthEndpoints`, имеют анонимные `/ping` (`pong`), `/health/live` и `/health/ready`. Liveness проверяет процесс/listener. Readiness — кэш фоновых проверок зарегистрированных EF/RabbitMQ/Redis/S3 contributors с циклом 15 секунд; `degraded` означает частичный отказ, 503 выдаётся при `down`. Детали состояний и подключения — [[Backend/GrpcServer]]. Это не единый механизм абсолютно всех проектов: AdminPanel использует собственные health endpoints, CloudMessaging мониторится как worker.

В Node gateway `/ping/{service}` направляется на внутренний `/ping`. Navigator health использует `NavigatorService:HealthHost`, поскольку публичный host может быть gRPC-only; значение задаёт конфигурация окружения.

Serilog/Seq принимает межсервисные TraceId/SpanId/CorrelationId и RequestId; stdout уровень зависит от окружения. `MetricsCollector` накапливает counters/gauges; `MetricsReporterService` сбрасывает буферизованные изменения в ServiceMetrics не чаще раза в 10 секунд, без пустых idle snapshots. Это не Prometheus endpoint. Auto-MediatR метрики подключаются по сервису, а не подразумеваются для всех handlers; реестры — компонентные `*-Metrics` заметки.

## Клиентские границы

Android разделяет typed domain gateways, RPC registry, token refresh и media TLS. Regular chat имеет единый state/effect/intent контракт; SQLCipher Room v5 и private staging обеспечивают durable drafts/outbox. Secret-chat handshake заблокирован отсутствующими Kyber-полями сервера; наличие scaffold не означает работающий клиентский E2E. Подробности — [[Clients/Android]].

Apple-клиенты разделяют BFCore и SwiftUI UI; iOS adaptive navigation определяется доступным пространством, локализация — текущей environment locale. WPF использует ResourceDictionary/DynamicResource. Точные locale, secure-storage и platform ограничения описываются в клиентских заметках, а не дублируются здесь. Общие SVG находятся в `Icons/`; способы включения в приложения зависят от платформы.

## Развёртывание и CI

Канонический регистр каталога — `Docker/`. В репозитории deployment-конфиги находятся в `Docker/nightly/` и `Docker/setup/`; каталогов `Docker/dev/` и `Docker/master/` нет. CI публикует образы для каналов `dev`, `nightly`, `master` с суффиксами `-dev`, `-nightly`, без суффикса соответственно. Persistent host directories рядом с compose (`admindata/`, `data/`, `backups/`) не хранятся в Git. Конкретные mount/сети проверяй в compose; не меняй имена контейнеров только из-за image suffix.

Backend workflows: `.github/workflows/build-backend-*.yml`, каждый на `ubuntu-latest`. SDK устанавливает `actions/setup-dotnet@v6` по `global.json`; затем publish в `publish/` и build/push `Dockerfile.slim`. Web дополнительно генерирует JS proto в Docker stage. Queue отдельная на workflow/сервис, общая для трёх веток: `cancel-in-progress: false`, `queue: max`. Тесты в backend CI явно запускают Settings, Setup и Developers; наличие тестового проекта не означает его запуск в CI.

Версии задаёт `.github/actions/docker-version/compute-version.sh`:

- nightly: максимум числового SemVer среди всех страниц tags + patch; новый/корректно пустой repository начинает с 1.0.0;
- dev наследует SemVer, совпадающий по manifest digest с текущим nightly `latest`; master — с dev `latest`, без повышения;
- ошибка сети/HTTP/JSON или отсутствующая исходная версия останавливает продвижение. Читается `latest` на момент запуска, привязки к конкретному коммиту нет; исходный канал должен закончить публикацию до продвижения.

`.github/scripts/push-docker-image.sh` после workflow `PUSH_DELAY` выполняет login и `docker buildx build --push` с тремя дополнительными попытками. Buildx используется из-за документированного registry 401 при dockerd push; задержки распределяют нагрузку. Клиентские workflow отдельно собирают Android/macOS/WinUI и наследуют версии nightly → dev → master. Итоговые Telegram steps с `continue-on-error` не меняют результат сборки при ошибке доставки; approval job не требуется.

Локальные регрессионные проверки `.github/tests/test_docker_version.py`, `test_client_version.py`, `test_send_telegram.py` работают без публикации/отправки сообщений; для PowerShell-кейсов нужен pwsh. Не выдавай их наличие за успешный прогон или автоматическое CI-покрытие. Общие тестовые ограничения — [[Testing]].

## Добавление значимого компонента

Проверь необходимые `ServiceId`, Settings-каталог/Setup, proto, startup/dependencies, compose/Nginx и workflow. Набор зависит от типа компонента: не каждому worker/HTTP-приложению нужен gRPC или XAuth. Создай/обнови обзор в vault и ссылку в [[Index]]. Неизвестные причины архитектурного выбора отмечай как не задокументированные.
