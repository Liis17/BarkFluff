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
| `Dockerfile.slim` | Продакшн-образ, используемый CI; устанавливает Docker CLI и монтирует `docker.sock`. |
| `dotnet-tools.json` | Манифест локальных .NET-инструментов проекта. |
| `SECURITY_AUDIT.md` | Полный аудит безопасности: критические уязвимости, рекомендации. |

---

## Data/

| Файл | Назначение |
|------|-----------|
| `TokenDbContext.cs` | LiteDB-контекст `db/tokens.db`. Коллекция `Tokens` с индексом по `LastActivity`. Singleton. Создаёт директорию при первом запуске. |
| `MetricsCacheDbContext.cs` | LiteDB-контекст `db/metrics_cache.db`. Коллекции: `HourlyStats` (события Seq за час), `HourlyTraffic` (трафик для графиков), `HourlyServiceMetrics` (CPU/memory/запросы по сервисам), `CompressionRuns` (история ежедневного сжатия логов-метрик). Singleton. |
| `RemoteDockerDbContext.cs` | LiteDB-контекст `db/remote_docker.db`: коллекции удалённых серверов и отслеживаемых контейнеров, индексы по имени сервера и серверу контейнера. |

---

## Endpoints/

Каждый файл — `static class` с extension-методом `Map{Name}Endpoints(this WebApplication app)`, вызываемым из `Program.cs`.

| Файл | Эндпоинт-группа | Что делает |
|------|----------------|-----------|
| `AuthEndpoints.cs` | `/api/auth` | Запрос входа через Telegram, polling статуса, me, logout, управление токенами (список, rename, delete). |
| `DockerEndpoints.cs` | `/api/docker` | Список контейнеров, start/stop/restart/pull конкретного, проверка обновления и self-restart/update AdminPanel, рестарт и обновление всей платформы. |
| `BadgesEndpoints.cs` | `/api/badges` | CRUD бейджей пользователей через gRPC Users + Files (загрузка изображения). |
| `StickersEndpoints.cs` | `/api/stickers` | Управление стикерпаками: CRUD пака, CRUD стикеров, смена обложки, прокси S3. gRPC Files. |
| `UsersEndpoints.cs` | `/api/users` | Поиск пользователей, полный профиль (параллельные gRPC-вызовы), назначение бейджей, лимит хранилища, отключение 2FA, аватар, удаление сессий. |
| `SeqEndpoints.cs` | `/api/seq` | Проксирование логов из Seq, KPI-дашборд, трафик, метрики сервисов, статус сервисов (Seq + Docker). |
| `LogsClearEndpoints.cs` | `/api/seq/clear` | Запуск и статус удаления логов из Seq (scope=all/old). |
| `LogsExportEndpoints.cs` | `/api/seq/export` | Запуск, статус и скачивание ZIP-архива экспорта логов. |
| `LogsCompressionEndpoints.cs` | `/api/seq/compress-metrics` | Ручной запуск ежедневного сжатия логов-метрик за конкретную дату, история прогонов. |
| `NotificationsEndpoints.cs` | `/api/notifications` | Публикация push-рассылок (всем устройствам / по списку deviceId) через MassTransit. |
| `RemoteDockerEndpoints.cs` | `/api/remote/servers` | CRUD сохранённых SSH-серверов, discovery контейнеров, список и действия над отслеживаемыми контейнерами; WebSocket-консоль принимает upgrade до открытия SSH, возвращает ready/error-сигналы и передаёт вывод PTY binary-фреймами. |
| `ConfigurationEndpoints.cs` | `/api/configuration` | Чтение и обновление S3-конфигурации бакетов через gRPC Configuration. |
| `S3BrowserEndpoints.cs` | `/api/s3` | Список бакетов, листинг объектов, получение presigned URL. |
| `ReservedNamesEndpoints.cs` | `/api/reserved-names` | CRUD зарезервированных имён пользователей через gRPC Configuration. |
| `MailEndpoints.cs` | `/api/mail` | Просмотр/отправка писем служебных почтовых ящиков (IMAP/SMTP через MailKit): аккаунты, список писем, письмо, вложения, inline-картинки, пометка прочитанным, отправка. |
| `BotsEndpoints.cs` | `/api/bots` | Управление ботами через gRPC [[Backend/Bots]]: список, создание системного бота, перегенерация токена, удаление. |

---

## Middleware/

| Файл | Назначение |
|------|-----------|
| `TokenAuthMiddleware.cs` | Проверяет cookie `auth_token` на каждый запрос. Публичные `/api/*`: `/api/auth/request`, `/api/auth/status`; для HTML без токена пропускаются `/`, `/Login.html`, `/assets/*`. Для `/api/*` → 401, для HTML без токена → redirect на `/` (корень отдаёт `Login.html`; отдельного `/login` нет). Обновляет `LastActivity`, удаляет истекшие токены. |

---

## Models/

| Файл | Назначение |
|------|-----------|
| `AuthToken.cs` | Сессионный токен: `Id`, `Name`, `CreatedAt`, `LastActivity`, `IpAddress`, `UserAgent`, `AdminUsername`, `ApprovedByTelegramUserId`. Методы `IsExpired()`, `IsVisibleToAdmin()`. |
| `PendingAuthRequest.cs` | In-memory запрос авторизации ожидающий подтверждения в Telegram. Статусы: Pending / Approved / Rejected / Expired. |
| `SeqSettings.cs` | POCO-конфиг Seq: `ServerUrl`, `ApiKey`. |
| `LogsClearJob.cs` | DTO для job'а удаления логов: `LogsClearScope` (All/Old), `LogsClearState`, счётчики. |
| `LogsExportJob.cs` | DTO для job'а экспорта логов: `LogsExportScope`, `LogsExportState`, путь к ZIP. |
| `LogsCompressionSettings.cs` | POCO-конфиг ежедневного сжатия логов-метрик: `Enabled`, `ScheduleUtcHour/Minute`, `MaxEventsPerRun`, `DryRun`, шаблоны Seq. |
| `MetricsCompressionRun.cs` | Запись о завершённом прогоне сжатия (хранится в `MetricsCacheDbContext.CompressionRuns`): `DayUtc`, `CompletedAtUtc`, `ServiceCount`, `SourceEventCount`, `DeletedCount`, `DryRun`. |
| `MailSettings.cs` | POCO-конфиг служебной почты: `ImapHost/Port/Security`, `SmtpHost/Port/Security`, `AcceptInvalidCertificates`, `Accounts[]` (address/password/displayName). |
| `Dtos/MailAccountDto.cs`, `MailMessageDto.cs`, `MailMessageDetailDto.cs`, `MailAttachmentDto.cs`, `SendMailRequest.cs` | DTO почтового модуля: список ящиков, письмо в списке, письмо целиком, вложение, тело запроса на отправку. |
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
| `SeqService.cs` | `HttpClient` к Seq REST API: получение событий, SQL-запросы, постраничная загрузка, список сигналов, удаление по фильтру (через пакет `Seq.Api`), запись событий в CLEF-формате (`POST /api/events/raw?clef`). |
| `LogsClearService.cs` | Singleton + Timer cleanup. Управляет job'ами удаления логов из Seq: подсчёт через SQL, удаление через `Seq.Api`. TTL 30 минут после завершения. |
| `LogsExportService.cs` | Singleton + Timer cleanup. Управляет job'ами экспорта: постраничная выгрузка событий в JSON-страницы, упаковка в ZIP в `/tmp/logs-export/`. |
| `MetricsLogCompressorService.cs` | `IHostedService` + Singleton. Ежедневно в 03:00 UTC агрегирует логи-метрики (`@MessageTemplate = 'ServiceMetrics {@Metrics}'`) за вчерашний день: один сводный CLEF-лог `MetricsDailySummary` на сервис (sum/avg/min/max/last/count по каждой метрике), затем удаление исходных через точный фильтр шаблона. Идемпотентность через `MetricsCacheDbContext.CompressionRuns`. |
| `S3BrowserService.cs` | AWS SDK S3. Кеширует `AmazonS3Client` по `bucketId`. Конфигурацию берёт из gRPC Configuration. Методы: листинг бакетов/объектов, presigned URL (5 мин). |
| `MetricsCollectorService.cs` | `IHostedService`. Запускается при старте + каждый час. Собирает события Seq за 24ч, группирует по часам/сервисам, сохраняет в `MetricsCacheDbContext`. Удаляет устаревшие данные (Stats >24ч, ServiceMetrics >12ч). |
| `MailService.cs` | `IAsyncDisposable`. IMAP/SMTP клиент (MailKit) для служебных ящиков: по одному `ImapClient`+`SemaphoreSlim` на аккаунт. Список писем, письмо, вложения (включая inline по Content-ID), пометка прочитанным, отправка через SMTP. |
| `RemoteDockerService.cs` | Управление Docker и интерактивной консолью на сохранённых в LiteDB SSH-серверах: проверка подключения, discovery, статусы, start/stop/restart, Compose-only pull + recreate. |
| `IRemoteSshClient.cs`, `SshNetRemoteSshClient.cs` | Тестируемая граница SSH-команд и интерактивного PTY; SSH.NET-реализация с password/keyboard-interactive auth и flush каждой записи ввода. |

---

## Pages/

Статические HTML-файлы. Копируются в `bin` при сборке (`CopyToOutputDirectory=Always`). Шаблонная переменная `{{SERVER_STARTED_AT_UTC}}`.

| Файл | Страница | Назначение |
|------|---------|-----------|
| `Login.html` | `/` (без токена) | Форма входа: поле nickname → polling статуса Telegram-подтверждения |
| `dashboard.html` | `/` | Главная: KPI, трафик, метрики сервисов из Seq-кеша |
| `services.html` | `/services` | Управление Docker-контейнерами: статус, start/stop/restart/update; интерактивная SSH-консоль для дополнительных серверов |
| `logs.html` | `/logs` | Просмотр логов Seq с фильтрацией по сервису, уровню, тексту |
| `badges.html` | `/badges` | CRUD бейджей: создание с картинкой, редактирование, удаление |
| `stickers.html` | `/stickers` | Управление стикерпаками: создание пака, загрузка стикеров. Встроенный редактор изображения (Cropper.js через CDN, `aspectRatio:1`): кроп, масштаб (слайдер + колесо), поворот картинки внутри макета, закругление углов (alpha-маска). Закрытие только с подтверждением (X/Esc/клик по фону → confirm; «Отмена» — явно). Финальный canvas 512×512 подменяет файл в `input.files`; сервер конвертирует в WebP. Точки входа: «Новый стикер», обложка пака (создание и смена). |
| `users.html` | `/users` | Поиск пользователей, профиль, бейджи, лимиты, 2FA, сессии |
| `s3-storage.html` | `/s3-storage` | Просмотр и редактирование S3/Minio конфигурации бакетов |
| `s3-browser.html` | `/s3-browser` | Файловый браузер S3: листинг объектов, presigned URL |
| `notifications.html` | `/notifications` | Публикация push-рассылок всем устройствам или по списку deviceId |
| `mail.html` | `/mail` | Просмотр/отправка писем служебных почтовых ящиков (IMAP/SMTP) |
| `restarting.html` | `/restarting` | Заглушка «сервер перезагружается» |
| `updating.html` | `/updating` | Заглушка «сервер обновляется» |
| `favicon.ico` | — | Иконка сайта |

Общие UI-ресурсы `Pages/v2/assets/`: `md3.css` содержит токены и размеры
иконок, `icons.js` — helper `bfIcon()`/`bfSetIcon()`, `sidebar.js` — общий
сайдбар. SVG-каталог из корневого `icons/` копируется в `assets/icons/` при
сборке AdminPanel; контейнерные иконки берутся из `icons/services`, специфичные
действия панели — из `icons/admin`, общие edit/delete/download — из
`icons/message-actions`.

---

## Properties/

| Файл | Назначение |
|------|-----------|
| `launchSettings.json` | Профили запуска для Visual Studio / `dotnet run`. |
| `PublishProfiles/FolderProfile.pubxml` | Профиль публикации в папку. |
