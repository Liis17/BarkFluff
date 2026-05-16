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

### Data Layer (LiteDB, не EF Core)

- `TokenDbContext` — auth-токены (`db/tokens.db`)
- `MetricsCacheDbContext` — кеш метрик из Seq: HourlyStats, HourlyTraffic, HourlyServiceMetrics (`db/metrics_cache.db`)

Оба — Singleton.

### gRPC-клиенты (подключается как клиент, не сервер)

| Клиент | Назначение |
|--------|-----------|
| `UsersServerApi` | Пользователи, бейджи |
| `FilesServerApi` | Файлы, S3 |
| `IdentityServerApi` | Авторизация |
| `ConfigurationApi` | Конфигурация |

Ключи: `{Service}Service:Host` и `{Service}Service:Token`.

### Frontend

Статические HTML-файлы в `Pages/` (`CopyToOutputDirectory=Always`). Шаблонизация: `{{SERVER_STARTED_AT_UTC}}` заменяется в `ServeHtmlFile()`. Маршруты страниц явно в `Program.cs`.

### Services

- `DockerService` — управление Docker-контейнерами
- `SeqService` — проксирование логов из Seq (HttpClient)
- `S3BrowserService` — браузер S3/Minio (AWSSDK.S3)
- `MetricsCollectorService` — фоновый сбор метрик (IHostedService)
- `TelegramBotService` — Telegram-бот для авторизации (IHostedService + Singleton)

### MassTransit (RabbitMQ publisher)

AdminPanel зарегистрирован как **publisher** в MassTransit (без consumers), чтобы публиковать админские RabbitMQ-события — например `AdminBroadcastNotificationEvent` (страница «Уведомления»). Конфигурация `RabbitMQ:Host/Username/Password` приходит из Configuration service. Используется через `IPublishEndpoint` в endpoint-методах.

### Endpoint Groups

Каждый файл в `Endpoints/` — extension method `Map{Name}Endpoints()`. Добавление: создать метод в существующем файле или новый файл + вызов в `Program.cs`.

## Proto

- `users_api.proto`, `files_api.proto`, `identity_api.proto` — Client
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
| `notifications.html` | Рассылка push на Android: форма + Android-preview + send-all / send-by-deviceId |
| `s3-storage.html` | Конфигурация S3/Minio бакетов |
| `s3-browser.html` | Браузер S3-объектов с presigned URL |
| `restarting.html` | Заглушка на время перезагрузки |
| `updating.html` | Заглушка на время обновления |
| `Redesigned/` | Новая SPA-версия (`/v2`) — index.html + app.js + screen-*.js + styles.css |

### SPA-экраны (Pages/Redesigned)

| `data-screen` | Группа | Файл | Назначение |
|---------------|--------|------|-----------|
| `login` | Auth | `screen-login.js` | Telegram-вход |
| `dashboard` | Observability | `screen-dashboard.js` | KPI, трафик |
| `services` | Observability | `screen-services.js` | Docker-сервисы |
| `logs` | Observability | `screen-logs.js` | Логи Seq |
| `badges` | Content | `screen-content.js` | Бейджи |
| `stickers` | Content | `screen-content.js` | Стикерпаки |
| `users` | Content | `screen-content.js` | Пользователи |
| `notifications` | Engagement | `screen-notifications.js` | Рассылка push на Android (forму + Android-preview) |
| `s3-storage` | Storage | `screen-s3.js` | Конфигурация бакетов |
| `s3-browser` | Storage | `screen-s3.js` | Файлы в бакете |

### Параллельная UI v2 (Redesigned)

В `Pages/Redesigned/` живёт SPA-вариант админки (одна страница со screen-*.js модулями). Обслуживается на маршруте `/v2`:
- `app.MapGet("/v2", ...)` отдаёт `Pages/Redesigned/index.html` через `ServeHtmlFile` (placeholder `{{SERVER_STARTED_AT_UTC}}` подставляется).
- Второй `UseStaticFiles` с `RequestPath = "/v2"` маппится на `Pages/Redesigned/`.
- Cookie `ui_version=v2` на корневом `/`: при наличии — редирект на `/v2`. На /v2 можно зайти и без cookie (если есть auth_token).
- Кнопки переключения: «Новая версия» в шапке `dashboard.html` ставит cookie + редирект на /v2; «Старая версия» в topbar redesigned ui стирает cookie + редирект на /.
- Старые маршруты (`/services`, `/logs`, `/badges`, `/stickers`, `/users`, `/s3-storage`, `/s3-browser`) продолжают работать независимо.

Auth внутри SPA: на старте `App.checkAuth()` дёргает `/api/auth/me`; при 401 → `screen-login` (Telegram-флоу `/api/auth/request` + polling `/api/auth/status`). Все экраны используют те же `/api/*` endpoints что и старая версия.

## Важные константы

| Параметр | Значение |
|----------|---------|
| Порт | 51888 |
| Token expiration | 3 дня |
| Pending timeout | 10 минут |
| Max gRPC file size | 20 МБ |
| Metrics interval | 1 час |
| HourlyStats retention | 24 часа |
| HourlyServiceMetrics retention | 12 часов |
| Sticker bucket | `message-documents` |

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
- `Telegram.Bot 22.0.2`
- `AWSSDK.S3`
- `MassTransit.RabbitMQ 8.5.2` — publisher для админских событий
- [[Backend/GrpcServer]] — LoadConfiguration
- [[Shared/Auth]] — JwtClientInterceptor
- [[Shared/Queue]] — события RabbitMQ (`AdminBroadcastNotificationEvent`)

## REST API: Notifications

`Endpoints/NotificationsEndpoints.cs` — публикует `AdminBroadcastNotificationEvent` через `IPublishEndpoint`. Потребитель — [[Backend/CloudMessaging|CloudMessaging]] (`admin-broadcast-handler` очередь).

| Метод | Путь | Тело | Ответ |
|-------|------|------|-------|
| POST | `/api/notifications/broadcast/all` | `{ title, body, imageUrl?, confirm: true }` | `{ enqueued: true }` |
| POST | `/api/notifications/broadcast/devices` | `{ title, body, imageUrl?, deviceIds: string[] }` | `{ enqueued: true, deviceCount }` |

`deviceIds` — Guid из `UserDevices.Id`. Без `confirm=true` — `/all` отклоняется с 400.
