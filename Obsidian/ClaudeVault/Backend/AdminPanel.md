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
| `s3-storage.html` | Конфигурация S3/Minio бакетов |
| `s3-browser.html` | Браузер S3-объектов с presigned URL |
| `restarting.html` | Заглушка на время перезагрузки |
| `updating.html` | Заглушка на время обновления |

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
- [[Backend/GrpcServer]] — LoadConfiguration
- [[Shared/Auth]] — JwtClientInterceptor
