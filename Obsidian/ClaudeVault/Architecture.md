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

## Ограничение и удаление аккаунта

AdminPanel (`users.block`/`users.delete`, SecurityAdmin + Owner, причина и Telegram step-up) вызывает Identity `SetAccountRestrictionServer`; Users остаётся источником статуса и маскирования профиля. Block/PendingDeletion закрывают вход, Identity удаляет refresh tokens/устройства и публикует отзыв сессий; Messages и Calls запрещают новые личные взаимодействия, Bots сохраняет owner restriction и отказывает токенам/обновлениям. `PendingDeletion` длится 7 дней. Identity worker под Redis lease забирает только просроченные записи через условный терминальный переход в Users, затем очищает Identity, профильные файлы Files и ботов; сообщения, вложения и история собеседников сохраняются. Развёртывай Notification раньше Identity: старая версия Notification не знает `NotificationType` 11–14.

Остаточные риски: Users не имеет outbox для `UserChangedName`/`UserChangedAvatar`, а запись статуса и публикация `UserAccountStatusChanged` в Identity не атомарны. Ошибка публикации возвращается в RPC; восстановление — идемпотентный повтор того же действия администратором. Кеш статуса собеседника для DM в каждом Messages-инстансе инвалидируется broadcast-событием; периодической сверки нет, но после потери события запись истекает через 45 секунд и обновляется при следующей проверке. Bots сверяет статусы владельцев при старте и раз в минуту одним batch-запросом. `TokenRevocationCache` хранится в памяти каждого сервиса; после перезапуска уже выданный JWT заблокированного пользователя может работать до истечения, не дольше `JwtSettings.ExpiryMinutes` (по умолчанию 60 минут). Статус аккаунта между нодами федерации не распространяется.

## Браузерный поток

Браузер использует gRPC-Web через [[Backend/Web]], а не нативные per-service каналы:

- **Shell** раздаёт глобальную статику и проксирует только Navigator. Выбранный `ServerInfo.web_endpoint` сохраняется клиентом.
- **Node** обслуживает ноду: business gRPC-Web, file upload/media, long-lived streams, `/node-config.js` и закреплённую конфигурацию.
- **Proxy** проксирует в Web gateway выбранной ноды; разрешённые media-hosts контролируются отдельно.

После выбора ноды браузер обращается прямо к её origin; глобальный shell не пересылает business traffic. Beacon через gateway уточняет метаданные и `livekit_url`. Детали auth/reconnect/TLS UI — [[Clients/Web]] и [[Clients/Web-Network-Reliability]].

## Наблюдаемость и health

Общие HTTP-хосты, зарегистрировавшие `AddBarkFluffHealth`/`MapHealthEndpoints`, имеют анонимные `/ping` (`pong`), `/health/live` и `/health/ready`. Liveness проверяет процесс/listener. Readiness — кэш фоновых проверок зарегистрированных EF/RabbitMQ/Redis/S3 contributors с циклом 15 секунд; `degraded` означает частичный отказ, 503 выдаётся при `down`. Детали состояний и подключения — [[Backend/GrpcServer]]. Это не единый механизм абсолютно всех проектов: AdminPanel использует собственные health endpoints, CloudMessaging мониторится как worker.

В Node gateway `/ping/{service}` направляется на внутренний `/ping`. Navigator health использует `NavigatorService:HealthHost`, поскольку публичный host может быть gRPC-only; значение задаёт конфигурация окружения.

`AddBarkFluffSerilog` подключает независимые sinks Console, Seq и Lognara. Root-level flags `ConsoleOutput=true`, `SeqEnable=true`, `LognaraEnable=false` хранятся в соответствующих service `ServiceId`; глобального наследования logging flags нет, а хосты с локальным стартом используют локальные значения. В каждом .NET-контейнере можно запустить отдельный HTTP-only Lognara agent. Middleware охватывает HTTP и все формы gRPC; listener включает штатную передачу W3C-контекста .NET/MassTransit без exporter. Outbox Messages/Federation сохраняет parent context отдельно от payload и восстанавливает его на каждой попытке. Форматы, конфигурация и lifecycle агента — [[Backend/GrpcServer]], эксплуатация — `Docs/Logging.md`. Собственные логи [[Backend/AdminPanel]] используют общий logger, но поиск/сжатие логов и кеш метрик панели остаются Seq-интеграцией; для Lognara-only используется Dock. `MetricsCollector` накапливает counters/gauges; `MetricsReporterService` сбрасывает буферизованные изменения в ServiceMetrics не чаще раза в 10 секунд, без пустых idle snapshots. Это не Prometheus endpoint. Auto-MediatR метрики подключаются по сервису, а не подразумеваются для всех handlers; реестры — компонентные `*-Metrics` заметки.

## Клиентские границы

Android разделяет typed domain gateways, RPC registry, token refresh и media TLS. Regular chat имеет единый state/effect/intent контракт; SQLCipher Room v5 и private staging обеспечивают durable drafts/outbox. Secret-chat handshake заблокирован отсутствующими Kyber-полями сервера; наличие scaffold не означает работающий клиентский E2E. Подробности — [[Clients/Android]].

Apple-клиенты разделяют BFCore и SwiftUI UI; iOS adaptive navigation определяется доступным пространством, локализация — текущей environment locale. WPF использует ResourceDictionary/DynamicResource. Точные locale, secure-storage и platform ограничения описываются в клиентских заметках, а не дублируются здесь. Общие SVG находятся в `Icons/`; способы включения в приложения зависят от платформы.

## BFT Proxy маршрут

Для выбранных BFT service IDs маршрут: клиент → Relay (опционально) → Exit → HTTPS/Cloudflare → основной Nginx → сервисный upstream. Exit может принимать прямые подключения и отдельный listener только для доверенных Relay. Relay знает адрес клиента и next-hop Exit и пересылает зашифрованный поток, не читая HTTP/gRPC содержимое. Exit терминирует BFT и внутренний TLS, видит клиентский IP, метку ключа, service ID и запросы, затем создаёт HTTPS-запрос к настроенному upstream. Развёртывание Exit и Relay описано в `Docker/nightly/bft-proxy/README.md`; compose-файлы самостоятельны и не входят в compose ноды.

Exit удаляет пришедшие от клиента `X-BFT-*`, `X-Forwarded-*`, `X-Real-IP`, `Forwarded`, `CF-Connecting-IP` и `True-Client-IP`, затем задаёт `X-BFT-Client-IP`, `X-BFT-Proxy-Id` и `X-BFT-Proxy-Secret`. Основной Nginx доверяет BFT только при совпадении Exit IP после real-ip обработки, секрета и корректного client IP. Он использует client IP в `X-Real-IP`, `X-Forwarded-For` и rate-limit ключах, удаляет client IP и secret BFT-заголовки перед приложением, а проверенный ProxyId передаёт дальше. `RequestContextInterceptor` добавляет ProxyId только в unary gRPC вызовах. См. [[Backend/BftProxy]], [[Backend/Nginx]] и [[Shared/Bft]].

LiveKit WebSocket/WebRTC и медиа звонков идут напрямую к LiveKit и обходят BFT Proxy; через Exit идут только service IDs с настроенным upstream.

## Развёртывание и CI

Канонический регистр каталога — `Docker/`. В репозитории deployment-конфиги находятся в `Docker/nightly/` и `Docker/setup/`; каталогов `Docker/dev/` и `Docker/master/` нет. CI публикует образы для каналов `dev`, `nightly`, `master` с суффиксами `-dev`, `-nightly`, без суффикса соответственно. Persistent host directories рядом с compose (`admindata/`, `data/`, `backups/`) не хранятся в Git. Конкретные mount/сети проверяй в compose; не меняй имена контейнеров только из-за image suffix. BFT Proxy использует отдельные compose Exit/Relay в `Docker/nightly/bft-proxy/`, не входит в compose ноды и выкатывается владельцем вручную после публикации образа; см. [[Backend/BftProxy]].

Конфигурацию отдельной self-hosted ноды из deployment-шаблонов генерирует [[Tools/Builder|BarkFluff Builder]]. Он пишет только принадлежащие ему compose, `.env`, setup, nginx и связанные файлы; каталоги постоянных данных не принадлежат генератору.

Весь CI — один workflow `.github/workflows/ci.yml` с параллельными jobs; reusable `test-backend.yml`, `test-clients.yml`, `test-scripts.yml` вызываются из него. Поток веток: разработка только через PR в `nightly`; в `dev` — только PR из `nightly`, в `master` — только PR из `dev` того же репозитория. Тесты выполняются только до попадания кода в `nightly`; push в ветки только собирает и публикует.

PR (`opened`, `synchronize`, `reopened`, `edited` — смена целевой ветки перепроверяется): job `guard` проверяет ветку-источник по схеме выше. Для PR в `nightly` `.github/scripts/pr_tests.py` выбирает затронутые наборы: backend, клиенты (Windows/Android/macOS), скрипты CI, frontend (ESLint/Prettier Web, сборки Developers и Navigator Admin) и Builder (тесты на Windows, проверка сгенерированных Compose/nginx и PostgreSQL bootstrap). Android-only PR не запускает backend/Windows/macOS; Shared и сам механизм выбора (`ci.yml`, `pr_tests.py`, `ci_plan.py`) требуют всех наборов; docs-only PR проходит gate без сборок. PR в `dev`/`master` тесты не запускают: `Tests passed` требует только успеха `guard`. Для PR в `nightly` `Tests passed` требует успешного выбора, успеха выбранных наборов и skip остальных. PR не публикует образы/клиенты и не получает deployment secrets. Ручной запуск `mode: tests` прогоняет все наборы. Подробности покрытия — [[Testing]].

Push/ручной запуск `mode: build`: job `plan` (`.github/scripts/ci_plan.py`) по diff `before..sha` выбирает компоненты; ручной запуск, новая ветка или изменение `ci.yml`/`ci_plan.py` собирают всё. Каталог backend-сервисов (образ, проект, Dockerfile, frontend, исходные пути) хранится в `SERVICES` этого файла. Изменения только тестов, `.github/tests` и test workflows ничего не публикуют.

- nightly: matrix backend-образов (`fail-fast: false`, `max-parallel: 6` ограничивает одновременные push в реестр) — `dotnet publish` в `publish/` и `docker buildx build --push` (`Dockerfile.slim`, у BftProxy `Dockerfile`; Web генерирует JS proto в Docker stage). Образ получает метки `org.opencontainers.image.version` и `org.opencontainers.image.revision` (коммит).
- dev/master: образы не пересобираются. `.github/scripts/promote_backend.py` для каждого сервиса копирует `latest` предыдущего канала по digest (`docker buildx imagetools create`), если он отличается от текущего `latest`. Копирование разрешено, только если коммит из метки revision входит в ветку и исходные пути сервиса не менялись после него; иначе сервис падает (nightly-сборка актуального коммита отсутствует или упала). Это требует merge commit при продвижении.
- Клиенты Android/macOS/WinUI и Builder собираются по своим путям во всех трёх ветках (flavor/identity зависят от канала). Версии клиентов вычисляет `.github/scripts/client-version.sh` (WinUI — без закреплённого CA, Android/macOS — с Cloudflare Origin CA). Загрузка в ClientStorage повторяется до 6 раз с паузой 15 с. Builder создаёт release `latest=false` только в `master`; детали — [[Tools/Builder|BarkFluff Builder]].
- Раннеры закреплены явно: `macos-26`, `windows-2025-vs2026`; actions закреплены по SHA. Concurrency: одна очередь на ветку для push, PR отменяет предыдущий запуск.

Уведомления в Telegram (`.github/scripts/ci_notify.py` → `send-telegram.sh`, `continue-on-error`): для PR — итог проверок, номер, ветки источник → цель и результат наборов; для push — одно сообщение с веткой и списком собранных/упавших компонентов с версиями. Итоги jobs передаются через artifacts `ci-result-*` (`.github/actions/ci-result`). PR из fork секретов не получает, уведомление пропускается.

Запрет merge задаётся отдельно в GitHub: `.github/rulesets/required-tests.json` (`nightly`: обязательный PR, strict `Tests passed`) и `promotion.json` (`dev`, `master`: обязательный PR, только merge commit, non-strict `Tests passed`), оба без bypass actors, с запретом удаления и force push; check `Tests passed` от GitHub Actions (App ID 15368). JSON в Git сам не применяет правила: администратор запускает `pwsh -File .github/scripts/set-required-tests.ps1`; скрипт проверяет, что во всех трёх ветках есть workflow с этим check (`ci.yml` или прежний `pr-tests.yml`), и только затем создаёт/обновляет rulesets. При откате сначала отключи rulesets в GitHub, затем удаляй workflows, иначе PR останутся заблокированы. Существующий ruleset `main rules` (`dev`/`master`: ревью и linear history) действует дополнительно; linear history несовместим с merge commit, поэтому продвижение проходит только через его bypass.

Версии образов задаёт `.github/actions/docker-version/compute-version.sh`:

- nightly: максимум числового SemVer среди всех страниц tags + patch; новый/корректно пустой repository начинает с 1.0.0;
- dev наследует SemVer, совпадающий по manifest digest с текущим nightly `latest`; master — с dev `latest`, без повышения; для продвижения также выдаётся `source_image` (репозиторий@digest);
- ошибка сети/HTTP/JSON или отсутствующая исходная версия останавливает продвижение. TLS реестра проверяется.

`.github/scripts/push-docker-image.sh` выполняет login и `docker buildx build --push`; при сбое ждёт 15 с, всего до 6 попыток. Buildx используется из-за документированного registry 401 при dockerd push. `.dockerignore` — allowlist: в контекст сборки попадают только `publish/`, `Docker/Lognara/agent|runtime`, `Shared/BarkFluff.Proto`, `Frontend/Developers` и файлы Web, которые копируют Dockerfile; новый COPY из другого пути требует исключения в `.dockerignore`.

Регрессионные проверки `.github/tests/` (`test_docker_version.py`, `test_client_version.py`, `test_send_telegram.py`, `test_pr_tests.py`, `test_ci_plan.py`) работают без публикации/отправки сообщений; часть требует Linux bash, PowerShell-кейсы — pwsh. Их запускает `test-scripts.yml`; локальный прогон не заменяет проверку GitHub Actions. Общие тестовые ограничения — [[Testing]].

## Добавление значимого компонента

Проверь необходимые `ServiceId`, Settings-каталог/Setup, proto, startup/dependencies, compose/Nginx и workflow. Набор зависит от типа компонента: не каждому worker/HTTP-приложению нужен gRPC или XAuth. Создай/обнови обзор в vault и ссылку в [[Index]]. Неизвестные причины архитектурного выбора отмечай как не задокументированные.
