# AdminPanel — Файлы проекта

Краткий справочник по каждому файлу `Backend/Barkfluff.AdminPanel/`.
Подробная архитектура: [[Backend/AdminPanel]] · Детальная карта классов и API: [[Backend/AdminPanel-ProjectMap]]

---

## Корень проекта

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа. DI-регистрация всех сервисов, gRPC-клиентов, middleware. Классы настроек: `TelegramSettings`, `TelegramProxySettings`, `AdminUser`, `TelegramSettingsExtensions`, `AuthSettings`. Шаблонизация HTML (`ServeHtmlFile`). Маршруты страниц. |
| `Barkfluff.AdminPanel.csproj` | Манифест проекта. `net10.0`, зависимости: LiteDB, Telegram.Bot, AWSSDK.S3, gRPC-клиенты. |
| `appsettings.json` | Конфигурация по умолчанию: Telegram, Auth, LiteDb, Seq, адреса сервисов. |
| `appsettings.Development.json` | Переопределения для локальной разработки. |
| `Dockerfile` | Продакшн-образ: сборка → runtime на `mcr.microsoft.com/dotnet/aspnet`, монтирует `docker.sock`. |
| `Dockerfile.slim` | Облегчённый вариант образа без лишних слоёв. |
| `dotnet-tools.json` | Манифест локальных .NET-инструментов проекта. |
| `SECURITY_AUDIT.md` | Полный аудит безопасности: критические уязвимости, рекомендации. |

---

## Data/

| Файл | Назначение |
|------|-----------|
| `TokenDbContext.cs` | LiteDB-контекст `db/tokens.db`. Коллекция `Tokens` с индексом по `LastActivity`. Singleton. Создаёт директорию при первом запуске. |
| `MetricsCacheDbContext.cs` | LiteDB-контекст `db/metrics_cache.db`. Коллекции: `HourlyStats` (события Seq за час), `HourlyTraffic` (трафик для графиков), `HourlyServiceMetrics` (CPU/memory/запросы по сервисам). Singleton. |

---

## Endpoints/

Каждый файл — `static class` с extension-методом `Map{Name}Endpoints(this WebApplication app)`, вызываемым из `Program.cs`.

| Файл | Эндпоинт-группа | Что делает |
|------|----------------|-----------|
| `AuthEndpoints.cs` | `/api/auth` | Запрос входа через Telegram, polling статуса, me, logout, управление токенами (список, rename, delete). |
| `DockerEndpoints.cs` | `/api/docker` | Список контейнеров, start/stop/restart/pull конкретного, рестарт и обновление всей платформы, self-restart/update AdminPanel. |
| `BadgesEndpoints.cs` | `/api/badges` | CRUD бейджей пользователей через gRPC Users + Files (загрузка изображения). |
| `StickersEndpoints.cs` | `/api/stickers` | Управление стикерпаками: CRUD пака, CRUD стикеров, смена обложки, прокси S3. gRPC Files. |
| `UsersEndpoints.cs` | `/api/users` | Поиск пользователей, полный профиль (параллельные gRPC-вызовы), назначение бейджей, лимит хранилища, отключение 2FA, аватар, удаление сессий. |
| `SeqEndpoints.cs` | `/api/seq` | Проксирование логов из Seq, KPI-дашборд, трафик, метрики сервисов, статус сервисов (Seq + Docker). |
| `ConfigurationEndpoints.cs` | `/api/configuration` | Чтение и обновление S3-конфигурации бакетов через gRPC Configuration. |
| `S3BrowserEndpoints.cs` | `/api/s3` | Список бакетов, листинг объектов, получение presigned URL. |
| `ReservedNamesEndpoints.cs` | `/api/reserved-names` | CRUD зарезервированных имён пользователей через gRPC Configuration. |

---

## Middleware/

| Файл | Назначение |
|------|-----------|
| `TokenAuthMiddleware.cs` | Проверяет cookie `auth_token` на каждый запрос. Публичные пути: `/login`, `/restarting`, `/updating`, `/favicon.ico`, `/api/auth/request`, `/api/auth/status`. Для `/api/*` → 401, для HTML → redirect `/login`. Обновляет `LastActivity`, удаляет истекшие токены. |

---

## Models/

| Файл | Назначение |
|------|-----------|
| `AuthToken.cs` | Сессионный токен: `Id`, `Name`, `CreatedAt`, `LastActivity`, `IpAddress`, `UserAgent`, `AdminUsername`, `ApprovedByTelegramUserId`. Методы `IsExpired()`, `IsVisibleToAdmin()`. |
| `PendingAuthRequest.cs` | In-memory запрос авторизации ожидающий подтверждения в Telegram. Статусы: Pending / Approved / Rejected / Expired. |
| `SeqSettings.cs` | POCO-конфиг Seq: `ServerUrl`, `ApiKey`. |
| `Dtos/AuthRequestDto.cs` | Входное тело `POST /api/auth/request`: nickname, tokenName, userAgent, browser, os, ipAddress. |
| `Dtos/AuthStatusResponse.cs` | Ответ polling `/api/auth/status/{id}`: статус, причина ошибки. |
| `Dtos/ContainerDtos.cs` | Docker DTO: `ContainerStatusDto`, `ContainerActionRequestDto`, `ContainerActionResponseDto`, `ImageInfoDto`. |

---

## Services/

| Файл | Назначение |
|------|-----------|
| `AuthService.cs` | Создаёт `PendingAuthRequest`, находит целевого админа в `TelegramSettings.ParsedAdmins`, делегирует отправку `TelegramBotService`. |
| `TokenService.cs` | CRUD токенов поверх `TokenDbContext`: создание, валидация + обновление LastActivity, удаление, переименование, очистка истекших. |
| `PendingAuthService.cs` | In-memory словарь pending-запросов. Таймер каждые 60 сек удаляет истёкшие (> `PendingRequestTimeoutMinutes`). |
| `TelegramBotService.cs` | `IHostedService` + Singleton. Инициализирует `TelegramBotClient` (optional proxy). Отправляет запрос подтверждения с кнопками Approve/Reject. Обрабатывает callback-кнопки и команды `/start`, `/tokens`, `/kill`, `/rename`, `/pending`. |
| `DockerService.cs` | Запускает `docker` и `docker compose` через `Process` с `ArgumentList` (защита от shell injection). Self-управление через ephemeral helper-контейнер с docker.sock. |
| `SeqService.cs` | `HttpClient` к Seq REST API: получение событий, SQL-запросы, постраничная загрузка, список сигналов. |
| `S3BrowserService.cs` | AWS SDK S3. Кеширует `AmazonS3Client` по `bucketId`. Конфигурацию берёт из gRPC Configuration. Методы: листинг бакетов/объектов, presigned URL (5 мин). |
| `MetricsCollectorService.cs` | `IHostedService`. Запускается при старте + каждый час. Собирает события Seq за 24ч, группирует по часам/сервисам, сохраняет в `MetricsCacheDbContext`. Удаляет устаревшие данные (Stats >24ч, ServiceMetrics >12ч). |

---

## Pages/

Статические HTML-файлы. Копируются в `bin` при сборке (`CopyToOutputDirectory=Always`). Шаблонная переменная `{{SERVER_STARTED_AT_UTC}}`.

| Файл | Страница | Назначение |
|------|---------|-----------|
| `Login.html` | `/login` | Форма входа: поле nickname → polling статуса Telegram-подтверждения |
| `dashboard.html` | `/` | Главная: KPI, трафик, метрики сервисов из Seq-кеша |
| `services.html` | `/services` | Управление Docker-контейнерами: статус, start/stop/restart/update |
| `logs.html` | `/logs` | Просмотр логов Seq с фильтрацией по сервису, уровню, тексту |
| `badges.html` | `/badges` | CRUD бейджей: создание с картинкой, редактирование, удаление |
| `stickers.html` | `/stickers` | Управление стикерпаками: создание пака, загрузка стикеров |
| `users.html` | `/users` | Поиск пользователей, профиль, бейджи, лимиты, 2FA, сессии |
| `s3-storage.html` | `/s3-storage` | Просмотр и редактирование S3/Minio конфигурации бакетов |
| `s3-browser.html` | `/s3-browser` | Файловый браузер S3: листинг объектов, presigned URL |
| `restarting.html` | `/restarting` | Заглушка «сервер перезагружается» |
| `updating.html` | `/updating` | Заглушка «сервер обновляется» |
| `favicon.ico` | — | Иконка сайта |

---

## Properties/

| Файл | Назначение |
|------|-----------|
| `launchSettings.json` | Профили запуска для Visual Studio / `dotnet run`. |
| `PublishProfiles/FolderProfile.pubxml` | Профиль публикации в папку. |
