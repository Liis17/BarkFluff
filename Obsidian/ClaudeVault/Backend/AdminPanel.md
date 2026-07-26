# Barkfluff.AdminPanel

Веб-дашборд администратора. Порт: **51888**.
ASP.NET Minimal APIs (.NET 10), vanilla HTML+JS+Tailwind, LiteDB.

Расположение: `Backend/Barkfluff.AdminPanel/`

## Сборка

```bash
dotnet build Barkfluff.AdminPanel.csproj
dotnet run --project Barkfluff.AdminPanel.csproj
```

## Архитектура

### Auth Flow (Telegram-based)

Вход без пароля — через подтверждение в Telegram:
1. Пользователь вводит username → `POST /api/auth/request`
2. `AuthService` находит админа по username в `TelegramSettings.ParsedAdmins`
3. `TelegramBotService` отправляет запрос подтверждения
4. Фронтенд поллит `GET /api/auth/status/{requestId}`
5. После approve — `TokenService` создаёт токен в LiteDB, устанавливает cookie `auth_token`

`TokenAuthMiddleware` проверяет cookie на каждый запрос. Публичные: `/api/auth/request`, `/api/auth/status`.

`Telegram:Admins` — строка формата `"userId1:username1,userId2:username2"`.

### Telegram-бот: сессии админ-панели

`/start` открывает меню с inline-кнопкой **«Мои сессии»**. Экран показывает только неистёкшие сессии текущего Telegram-админа, поддерживает пагинацию и обновление. Из карточки сессии можно увидеть имя, IP, время создания/последней активности/истечения и завершить её после отдельного подтверждения — GUID вручную вводить не требуется. Команды `/tokens`, `/kill` и `/rename` сохранены для обратной совместимости; `/sessions` — текстовый алиас интерактивного списка.

Запрос на вход в Telegram дополнительно показывает исходный IP, чтобы администратор мог осознанно подтвердить либо отклонить сессию.

Боковая панель MD3 берёт Telegram username и аватар из `/api/auth/me` и `/api/auth/me/avatar`; вторичной строкой отображается браузер/ОС сессии, а не дата её создания. Аватар проксируется через авторизованный endpoint, поэтому токен Telegram-бота не попадает в браузер.

Перед аутентификацией применяется `ForwardedHeadersMiddleware` для подсети docker bridge `172.16.0.0/12`: nginx передаёт `X-Forwarded-For` и `X-Forwarded-Proto`, а сервис использует `RemoteIpAddress` как исходный IP. Если действующий токен используется с иного IP или User-Agent, бот отправляет владельцу Telegram-уведомление с исходным и новым окружением. Для одинакового нового окружения повторное уведомление ограничено одним в сутки.

### Data Layer (LiteDB, не EF Core)

- `TokenDbContext` — auth-токены (`db/tokens.db`)
- `MetricsCacheDbContext` — кеш метрик из Seq: `HourlyStats`, `HourlyTraffic`, `HourlyServiceMetrics`, `CompressionRuns` (история ежедневного сжатия логов-метрик) (`db/metrics_cache.db`)

Оба — Singleton.

### gRPC-клиенты (подключается как клиент, не сервер)

| Клиент | Назначение |
|--------|-----------|
| `UsersServerApi` | Пользователи, бейджи |
| `FilesServerApi` | Файлы, S3 |
| `IdentityServerApi` | Авторизация |
| `ConfigurationApi` | Конфигурация |
| `BotsServerApi` | Боты ([[Backend/Bots]]): создание системных, токены, удаление |

Ключи: `{Service}Service:Host` и `{Service}Service:Token`.

### Frontend

Статические HTML-файлы в `Pages/` (`CopyToOutputDirectory=Always`). Шаблонизация: `{{SERVER_STARTED_AT_UTC}}` заменяется в `ServeHtmlFile()`. Маршруты страниц явно в `Program.cs`.

### Services

- `DockerService` — управление Docker-контейнерами
- `SeqService` — проксирование логов из Seq (HttpClient), удаление по фильтру (`Seq.Api`), запись событий в CLEF-формате
- `S3BrowserService` — браузер S3/Minio (AWSSDK.S3)
- `MetricsCollectorService` — каждые 5 минут строит почасовые rollup из `ServiceMetrics schema v2`: counters суммируются, gauges берутся последними. История витрины — 30 дней.
- `MetricsLogCompressorService` — фоновое сжатие логов-метрик в Seq: ежедневно в **03:00 UTC** один сводный CLEF-лог `MetricsDailySummary` на сервис (sum/avg/min/max/last/count) + удаление исходных `ServiceMetrics`-логов. Перед удалением проверяет, что все исходные service-hour уже сохранены в витрине; counters и gauges лежат в разных namespace архива. Идемпотентность через `CompressionRuns`. Ручной триггер: `POST /api/seq/compress-metrics/run?date=YYYY-MM-DD`.
- `TelegramBotService` — Telegram-бот для авторизации (IHostedService + Singleton)

### MassTransit (RabbitMQ publisher)

AdminPanel зарегистрирован как **publisher** в MassTransit (без consumers), чтобы публиковать админские RabbitMQ-события — например `AdminBroadcastNotificationEvent` (страница «Уведомления»). Конфигурация `RabbitMQ:Host/Username/Password` приходит из Configuration service. Используется через `IPublishEndpoint` в endpoint-методах.

### Endpoint Groups

Каждый файл в `Endpoints/` — extension method `Map{Name}Endpoints()`. Добавление: создать метод в существующем файле или новый файл + вызов в `Program.cs`.

`BotsEndpoints` зарегистрирован через `app.MapBotsEndpoints()` и проксирует `ListBots`, создание системного бота, перегенерацию токена и удаление в [[Backend/Bots|Bots]]. Сервис `BarkFluff.Bots` также входит в списки статусов контейнеров и метрик Seq на актуальных страницах `Pages/v2/services.html` и `Pages/v2/dashboard.html`.

`UsersEndpoints` и `SearchUsersServer` показывают только обычные аккаунты: боты не входят в `/api/users` и `totalCount`, а запросы к пользовательским операциям по bot ID отвечают 404. Управление ботами остаётся в `/bots` (`Pages/v2/bots.html`).

## Proto

- `users_api.proto`, `files_api.proto`, `identity_api.proto`, `bots_api.proto` — Client
- `shared.proto` — None (`GrpcServices="None"`)

## HTML-страницы (Pages/)

| Файл | Назначение |
|------|-----------|
| `Login.html` | Форма входа: nickname → polling статуса |
| `dashboard.html` | KPI, трафик, метрики сервисов из Seq |
| `services.html` | Управление Docker-контейнерами |
| `logs.html` | Просмотр логов Seq с фильтрацией |
| `badges.html` | CRUD бейджей |
| `stickers.html` | Управление стикерпаками |
| `users.html` | Управление пользователями (поиск, профили, 2FA, сессии) |
| `v2/bots.html` | Управление ботами: создание системного, токен (показ один раз), перегенерация, удаление |
| `notifications.html` | Рассылка push на Android: форма + Android-preview + send-all / send-by-deviceId |
| `s3-storage.html` | (мёртвый, не роутится) старая плоская страница конфигурации S3 |
| `s3-browser.html` | (мёртвый, не роутится) старый браузер S3-объектов |
| `Redesigned/` | (устарел, superseded) SPA-версия — index.html + app.js + screen-*.js, доступна только по `/v2/` |
| `v2/` | **Актуальная версия** (MD3-дизайн) — многостраничная, `dashboard.html`/`services.html`/`s3-storage.html`/и т.д. |

### Актуальная версия — Pages/v2 (MD3)

`Pages/v2/*.html` — то, что реально видит пользователь. Все именованные маршруты (`/`, `/services`, `/logs`, `/badges`, `/stickers`, `/users`, `/bots`, `/notifications`, `/mail`, `/configuration`, `/s3-storage`, `/s3-browser`, `/restarting`, `/updating`) отдают файлы из этой папки (`Program.cs:282-304`). Дизайн — Material Design 3 (классы `md-input-outlined`, `md-btn-filled`, иконки `msr`/Material Symbols). `assets/` (md3.css, sidebar.js) статикой на `/assets`.

**Любые доработки UI AdminPanel — только в `Pages/v2/`.**

Карточки и сетка стикерпаков используют полный файл стикера, а не его 64×64 preview. Бейджи выводят сохранённый постоянный URL исходного изображения.

#### Тема и акцент

Цветовая схема MD3 задаётся CSS-переменными в `assets/md3.css` (`:root`). Тёмной темы нет — только светлая. Акцент — **терракота** (seed `#8c351c`, из веб-версии мессенджера `BarkFluff.Web`): `--md-primary: #8c351c`, primary-container/secondary-container/tertiary и нейтральные поверхности перекрашены в тёплый нейтраль. Семантические цвета (`--md-error`/`--md-warning`/`--md-success`) не трогаются. Меняешь акцент — правишь токены в `:root`, всё остальное наследует через `var()`. Захардкоженный акцент есть только в графиках Chart.js `dashboard.html` (массив `metricColors[0]` и `mkDataset('Все события', …)`).

#### Мобильная адаптация

Вся адаптивная вёрстка — **только внутри `@media (max-width: …)`**; десктоп (≥ 840px) не меняется. Брейкпоинты: `840px` — боковое меню становится off-canvas drawer; `600px` — схлопывание контента (гриды → 1 колонка, широкие таблицы → `overflow-x`/скрытие второстепенных колонок, шапка → `flex-wrap`).

Off-canvas drawer реализован общим `assets/sidebar.js` (`initMobileNav()`): инъектит гамбургер `.md-nav-toggle` в `.md-app-bar` + `.md-nav-scrim` **внутрь `.md-app-shell`** (важно: scrim показывается селектором-потомком `.md-app-shell.nav-open .md-nav-scrim`, вставка в body его ломает). Toggle переключает класс `.nav-open` на shell; закрытие по scrim/пункту меню/Esc. No-op на standalone-страницах без shell/app-bar (Login/restarting/updating). CSS drawer/scrim — в конце `md3.css`.

### Устаревшие/мёртвые версии (не трогать)

- **`Pages/Redesigned/`** — старый SPA-вариант (screen-*.js модули), superseded веткой v2. Обслуживается только на `/v2/` (URL, не путать с папкой `Pages/v2/`) через `app.MapGet("/v2/", ...)` → `Redesigned/index.html` + `UseStaticFiles(RequestPath="/v2")` → `Pages/Redesigned/`. Живой код, но не актуальный UI — не дорабатывать.
- **`Pages/*.html`** (плоские файлы верхнего уровня — `s3-storage.html`, `s3-browser.html` и т.п.) — исторически первая версия. Ни один именованный маршрут на них больше не указывает; технически ещё раздаются как статика (`UseStaticFiles(RequestPath="")` на `Pages/`), но по прямому `.html`-URL, на который никто не ссылается. Мёртвый код — не дорабатывать.

Auth: `App.checkAuth()` дёргает `/api/auth/me`; при 401 → Telegram-флоу (`/api/auth/request` + polling `/api/auth/status`). Все версии используют одни и те же `/api/*` endpoints.

## Важные константы

| Параметр | Значение |
|----------|---------|
| Порт | 51888 |
| Token expiration | 3 дня |
| Pending timeout | 10 минут |
| Max gRPC file size | 20 МБ |
| Metrics interval | 5 минут (пересчёт текущего и предыдущего часа) |
| HourlyStats retention | 24 часа |
| HourlyServiceMetrics retention | 30 дней |
| Metrics compression schedule | ежедневно в 03:00 UTC (вчерашний UTC-день) |
| Sticker bucket | `message-documents` |

`Pages/v2/s3-storage.html` — редактор параметров бакетов включает поле `Region` (для Cloudflare R2 обычно `auto`, для MinIO/S3 необязательно). Соответствует `S3_REGION`/`AuthenticationRegion` в [[Backend/ClientStorage]] и [[Backend/Files]].

## Безопасность

Полный аудит в `SECURITY_AUDIT.md` (проект). Критические проблемы:
- Docker socket монтируется в контейнер → полный контроль над хостом
- Нет `HttpOnly` на cookie `auth_token`
- Отключение 2FA пользователя без аудита
- Нет разделения ролей

## Карта проекта

Детальный разбор всех файлов, классов и эндпоинтов → [[Backend/AdminPanel-ProjectMap]]

Краткое описание каждого файла проекта → [[Backend/AdminPanel-Files]]

## Ключевые зависимости

- `LiteDB 5.0.21` — embedded NoSQL
- `Telegram.Bot 22.10.0.1`
- `AWSSDK.S3`
- `MassTransit.RabbitMQ 8.5.9` — publisher для админских событий
- [[Backend/GrpcServer]] — LoadConfiguration
- [[Shared/Auth]] — JwtClientInterceptor
- [[Shared/Queue]] — события RabbitMQ (`AdminBroadcastNotificationEvent`)

## Вкладка «Конфигурация» (`/configuration`)

`Pages/v2/configuration.html` — просмотр и правка всех строк базы конфигурации [[Backend/Configuration]]. Группировка по сервису (раскрывающиеся блоки), клиентский поиск, маскировка секретов (ключ/секция содержит `Token|Secret|Password|AccessKey` → ••• с кнопкой «показать»), inline-редактирование **только Value** (Enter — сохранить, Esc — отмена). `EditedAt` ставит сервер, `EditedBy` = имя админа из сессии, `EditedFrom` = IP.

Endpoints (`Endpoints/ConfigurationEndpoints.cs`):

| Метод | Путь | Описание |
|-------|------|----------|
| GET | `/api/configuration/all` | Все строки через rpc `GetAllConfigurations` (+ `serviceName` из enum `ServiceId`) |
| POST | `/api/configuration/update` | `{ section, key, serviceId, value }` → rpc `UpdateConfiguration` |
| GET | `/api/configuration/s3-configuration` | Конфигурация S3-бакетов |
| POST | `/api/configuration/s3/update` | Обновление конфигурации S3 |

## REST API: Notifications

`Endpoints/NotificationsEndpoints.cs` — публикует `AdminBroadcastNotificationEvent` через `IPublishEndpoint`. Потребитель — [[Backend/CloudMessaging|CloudMessaging]] (`admin-broadcast-handler` очередь).

| Метод | Путь | Тело | Ответ |
|-------|------|------|-------|
| POST | `/api/notifications/broadcast/all` | `{ title, body, imageUrl?, confirm: true }` | `{ enqueued: true }` |
| POST | `/api/notifications/broadcast/devices` | `{ title, body, imageUrl?, deviceIds: string[] }` | `{ enqueued: true, deviceCount }` |

`deviceIds` — Guid из `UserDevices.Id`. Без `confirm=true` — `/all` отклоняется с 400.
