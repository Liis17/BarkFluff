# AdminPanel — Project Map

Детальная карта всех файлов, классов, эндпоинтов и взаимодействий.
Сервис: `Backend/Barkfluff.AdminPanel/` → [[Backend/AdminPanel]]

---

## Структура файлов

```
Barkfluff.AdminPanel/
├── Program.cs                         ← точка входа, DI, маршруты
├── Barkfluff.AdminPanel.csproj
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
├── Dockerfile.slim                        ← облегчённый образ
├── dotnet-tools.json                      ← манифест .NET инструментов
├── Data/
│   ├── TokenDbContext.cs              ← LiteDB: auth-токены
│   └── MetricsCacheDbContext.cs       ← LiteDB: кеш метрик
├── Endpoints/
│   ├── AuthEndpoints.cs               ← /api/auth/*
│   ├── DockerEndpoints.cs             ← /api/docker/*
│   ├── BadgesEndpoints.cs             ← /api/badges/*
│   ├── StickersEndpoints.cs           ← /api/stickers/*
│   ├── UsersEndpoints.cs              ← /api/users/*
│   ├── SeqEndpoints.cs                ← /api/seq/*
│   ├── ConfigurationEndpoints.cs      ← /api/configuration/*
│   ├── S3BrowserEndpoints.cs          ← /api/s3/*
│   └── ReservedNamesEndpoints.cs      ← /api/reserved-names/*
├── Middleware/
│   └── TokenAuthMiddleware.cs         ← cookie-аутентификация
├── Models/
│   ├── AuthToken.cs                   ← сессионный токен
│   ├── PendingAuthRequest.cs          ← запрос подтверждения (in-memory)
│   ├── SeqSettings.cs                 ← конфиг Seq
│   └── Dtos/
│       ├── ContainerDtos.cs           ← Docker DTO
│       ├── AuthRequestDto.cs          ← запрос авторизации
│       └── AuthStatusResponse.cs      ← статус запроса
├── Services/
│   ├── AuthService.cs                 ← создание auth-запросов
│   ├── TokenService.cs                ← CRUD токенов в LiteDB
│   ├── PendingAuthService.cs          ← in-memory очередь запросов
│   ├── TelegramBotService.cs          ← Telegram-бот (IHostedService)
│   ├── DockerService.cs               ← управление Docker
│   ├── SeqService.cs                  ← HTTP-клиент Seq
│   ├── S3BrowserService.cs            ← AWS SDK S3
│   ├── MetricsCollectorService.cs     ← фоновый сбор метрик (IHostedService)
│   └── ConfigurationService.cs        ← gRPC-клиент конфигурации
├── Pages/
│   ├── Login.html                     ← форма входа
│   ├── dashboard.html                 ← KPI, трафик, метрики
│   ├── services.html                  ← управление контейнерами
│   ├── logs.html                      ← просмотр логов Seq
│   ├── badges.html                    ← CRUD бейджей
│   ├── stickers.html                  ← управление стикерпаками
│   ├── users.html                     ← управление пользователями
│   ├── s3-storage.html                ← конфигурация S3
│   ├── s3-browser.html                ← браузер S3 объектов
│   ├── restarting.html                ← заглушка перезагрузки
│   └── updating.html                  ← заглушка обновления
└── Properties/
    └── launchSettings.json
```

---

## Program.cs — ключевые классы

| Класс | Назначение |
|-------|-----------|
| `TelegramSettings` | Конфиг Telegram-бота: `BotToken`, `Admins` (строка `"id:name,id:name"`) |
| `TelegramProxySettings` | Proxy для бота: `Url`, `Username`, `Password` |
| `AdminUser` | Парсированный админ: `TelegramUserId` (long), `Username` |
| `TelegramSettingsExtensions` | `IsAdmin(userId)`, `GetAdminByUsername(name)`, `GetAdminByTelegramId(id)`, `GetUsername(id)` |
| `AuthSettings` | `TokenExpirationDays` (default 3), `PendingRequestTimeoutMinutes` (10) |
| `Program.StartedAtUtc` | `static DateTime` — метка запуска, подставляется в HTML |

**ServeHtmlFile()** — заменяет `{{SERVER_STARTED_AT_UTC}}` в HTML-ответах.

**Маршруты HTML-страниц** (статические):
```
GET /                  → dashboard.html
GET /services          → services.html
GET /logs              → logs.html
GET /users             → users.html
GET /badges            → badges.html
GET /stickers          → stickers.html
GET /s3-storage        → s3-storage.html
GET /s3-browser        → s3-browser.html
GET /login             → Login.html (публичный)
GET /restarting        → restarting.html (публичный)
GET /updating          → updating.html (публичный)
```

---

## Данные (Data/)

### TokenDbContext
- LiteDB, путь: `db/tokens.db` (из `LiteDbSettings.Path`)
- Коллекция `Tokens` с индексом по `LastActivity`
- Singleton, создаёт директорию если не существует

### MetricsCacheDbContext
- LiteDB, путь: `db/metrics_cache.db` (отдельный файл)
- Коллекции:
  - `HourlyStats` — агрегированные события за час (`TotalEvents`, `ErrorCount`, `WarningCount`, `PerService`)
  - `HourlyTraffic` — трафик для графиков
  - `HourlyServiceMetrics` — CPU/memory/requests по сервисам

---

## Модели

### AuthToken
```csharp
Guid Id
string Name              // "Web Session" или пользовательское
DateTime CreatedAt
DateTime LastActivity
string IpAddress
string UserAgent
string AdminUsername
long? ApprovedByTelegramUserId
bool IsExpired(int expirationDays)
bool IsVisibleToAdmin(long adminId, string username)
```

### PendingAuthRequest
```csharp
Guid RequestId
string IpAddress, Browser, Os, UserAgent
string Nickname
DateTime CreatedAt
PendingStatus Status     // Pending / Approved / Rejected / Expired
int? TelegramMessageId
Guid? TokenId
string? TokenName
long? ApprovedByTelegramUserId
long? TargetTelegramUserId
DateTime? CompletedAt
```

### Docker DTOs (ContainerDtos.cs)
```
ContainerStatusDto       Name, Id, Image, State, Status, Ports, CreatedAt
ContainerActionRequestDto ContainerName, Action (start/stop/restart/pull)
ContainerActionResponseDto Success, Message, ErrorDetails
ImageInfoDto             Id, Repository, Tag, Size, CreatedAt
```

---

## Middleware

### TokenAuthMiddleware
- Читает GUID из cookie `auth_token`
- Публичные пути: `/login`, `/restarting`, `/updating`, `/api/auth/request`, `/api/auth/status`
- Для `/api/*` → 401 если токен невалиден
- Для HTML → редирект на `/login`
- Обновляет `LastActivity` при каждом валидном запросе
- Удаляет истекшие токены при проверке

---

## Сервисы

### AuthService
Создаёт `PendingAuthRequest`, находит цель-админа в `TelegramSettings.ParsedAdmins`, делегирует отправку `TelegramBotService`.

### TokenService
CRUD поверх `TokenDbContext.Tokens`.

| Метод | Описание |
|-------|---------|
| `CreateToken(name, ip, ua, adminUsername, telegramId)` | Создать токен |
| `ValidateToken(tokenId)` | Проверить + обновить LastActivity |
| `DeleteToken(tokenId)` | Удалить |
| `DeleteTokenByAdmin(tokenId, telegramUserId)` | Удалить с проверкой принадлежности |
| `RenameToken(tokenId, name, telegramUserId)` | Переименовать |
| `GetAllTokens()` | Все токены |
| `GetTokensByAdmin(telegramUserId)` | Токены конкретного админа |
| `CleanupExpiredTokens()` | Удалить истекшие |

### PendingAuthService
In-memory хранилище `PendingAuthRequest`. Таймер каждые 60 сек вызывает `CleanupExpiredRequests()` (удаляет старше `PendingRequestTimeoutMinutes`).

### TelegramBotService (IHostedService)
- Инициализирует `TelegramBotClient` (с optional proxy)
- `SendAuthRequestAsync(request, targetTelegramUserId)` — отправляет сообщение с кнопками Approve/Reject
- `CheckBotReachabilityAsync(adminId)` — проверяет доступность бота для конкретного админа

**UpdateHandler.HandleCallbackQueryAsync():**
- Кнопка "Разрешить" → `TokenService.CreateToken()` + `PendingAuthService.UpdateRequestStatus(Approved)` + редактирует сообщение Telegram
- Кнопка "Отклонить" → `UpdateRequestStatus(Rejected)` + редактирует сообщение

**UpdateHandler.HandleMessageAsync() — команды бота:**
| Команда | Действие |
|---------|---------|
| `/start` | Справка по командам |
| `/tokens` | Список активных токенов |
| `/kill <guid>` | Отозвать токен |
| `/rename <guid> <name>` | Переименовать токен |
| `/pending` | Список ожидающих запросов |

### DockerService
Выполняет `docker` и `docker compose` через `Process` с `ArgumentList` (защита от shell injection).

| Метод | Docker-команда |
|-------|--------------|
| `GetContainersAsync()` | `docker ps --format json` |
| `StartContainerAsync(name)` | `docker start {name}` |
| `StopContainerAsync(name)` | `docker stop {name}` |
| `RestartContainerAsync(name)` | `docker restart {name}` |
| `PullImageAndRecreateContainerAsync(name)` | pull + compose up -d |
| `RestartAdminPanelAsync()` | helper-контейнер с docker.sock |
| `UpdateAdminPanelAsync()` | pull + helper-контейнер |
| `RestartAllServicesAsync()` | compose restart barkfluff |
| `UpdateAllServicesAsync()` | pull + compose up -d |

### SeqService
`HttpClient` → Seq REST API.

| Метод | URL |
|-------|-----|
| `GetEventsAsync(filter, count, fromUtc, afterId)` | `GET /api/events` |
| `RunSqlQueryAsync(query, dates)` | `GET /api/sqlquery?q=` |
| `GetAllEventsListAsync(filter, fromUtc, maxEvents)` | постраничная загрузка |
| `GetSignalsAsync()` | `GET /api/signals` |

### S3BrowserService
AWS SDK S3. Кеширует `AmazonS3Client` по `bucketId`. Конфигурацию берёт из `ConfigurationService` (gRPC).

| Метод | Описание |
|-------|---------|
| `GetBucketNamesAsync()` | Список бакетов с display names |
| `ListObjectsAsync(bucketId, prefix, token, maxKeys)` | ListObjectsV2 |
| `GetPresignedUrlAsync(bucketId, key)` | Presigned URL на 5 минут |

### MetricsCollectorService (IHostedService)
Запускается при старте + каждый час. Собирает события Seq за 24 часа, группирует по часам/сервисам, сохраняет в `MetricsCacheDbContext`. Удаляет данные старше 24ч (HourlyStats) и 12ч (HourlyServiceMetrics).

---

## Endpoints — полная таблица API

### /api/auth — AuthEndpoints.cs

| Метод | Путь | Auth | Тело / Query |
|-------|------|------|-------------|
| POST | `/api/auth/request` | ❌ Public | `{ nickname, tokenName, userAgent, ipAddress, browser, os }` |
| GET | `/api/auth/status/{requestId}` | ❌ Public | — |
| GET | `/api/auth/me` | ✅ Token | — |
| POST | `/api/auth/logout` | ✅ Token | — |
| GET | `/api/auth/tokens` | ✅ Token | — |
| POST | `/api/auth/tokens/{id}/rename` | ✅ Token | `{ name }` |
| DELETE | `/api/auth/tokens/{id}` | ✅ Token | — |

### /api/docker — DockerEndpoints.cs

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/docker/containers` | Список всех контейнеров |
| GET | `/api/docker/containers/{name}/status` | Статус контейнера |
| POST | `/api/docker/containers/{name}/start` | Запустить |
| POST | `/api/docker/containers/{name}/stop` | Остановить |
| POST | `/api/docker/containers/{name}/restart` | Перезагрузить |
| POST | `/api/docker/containers/{name}/pull` | Обновить образ |
| POST | `/api/docker/containers/admin-panel/restart-own` | Перезагрузить саму панель |
| POST | `/api/docker/containers/admin-panel/update-own` | Обновить панель |
| POST | `/api/docker/containers/restart-all` | Рестарт всех BarkFluff-сервисов |
| POST | `/api/docker/containers/update-all` | Обновить все сервисы |

### /api/badges — BadgesEndpoints.cs

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/badges` | Все бейджи (включая неактивные) |
| POST | `/api/badges` | Создать (multipart: name, description, isActive, image) |
| PUT | `/api/badges/{id}` | Обновить (multipart или JSON, image опционально) |
| DELETE | `/api/badges/{id}` | Удалить |

### /api/stickers — StickersEndpoints.cs

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/stickers/file/{fileId}` | Прокси S3 (redirect) |
| GET | `/api/stickers/packs` | Список стикерпаков |
| POST | `/api/stickers/packs` | Создать пак (multipart: name, description, image) |
| GET | `/api/stickers/packs/{id}` | Пак с содержимым |
| PUT | `/api/stickers/packs/{id}` | Обновить (name, description, coverStickerId) |
| PUT | `/api/stickers/packs/{id}/cover` | Сменить обложку (multipart: image) |
| DELETE | `/api/stickers/packs/{id}` | Удалить пак |
| POST | `/api/stickers/packs/{id}/stickers` | Добавить стикер (multipart: image, emoji) |
| PUT | `/api/stickers/{stickerId}` | Обновить emoji |
| DELETE | `/api/stickers/{stickerId}` | Удалить стикер |

### /api/users — UsersEndpoints.cs

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/users?query=&offset=&size=` | Поиск пользователей |
| GET | `/api/users/{id}` | Полный профиль (параллельно: Search, Contacts, Devices, Storage, OTP, Sessions) |
| POST | `/api/users/{id}/badges` | Назначить бейдж `{ badgeId, priority }` |
| DELETE | `/api/users/{id}/badges/{badgeId}` | Удалить бейдж |
| PUT | `/api/users/{id}/storage-limit` | Изменить лимит `{ storageLimitGb: 1-250 }` |
| POST | `/api/users/{id}/2fa/disable` | Отключить 2FA `{ otpType }` |
| POST | `/api/users/{id}/avatar` | Загрузить аватар (multipart: avatar) |
| DELETE | `/api/users/{id}/sessions/{deviceId}` | Удалить сессию пользователя |

### /api/seq — SeqEndpoints.cs

| Метод | Путь | Query | Описание |
|-------|------|-------|---------|
| GET | `/api/seq/events` | application, count, fromUtc, level, search, afterId | Логи из Seq |
| GET | `/api/seq/services` | — | Известные сервисы (KnownServices) |
| GET | `/api/seq/dashboard/kpis` | hours (def 24) | KPI из кеша или Seq |
| GET | `/api/seq/dashboard/traffic` | hours, interval | `{ all, errors, warnings }` |
| GET | `/api/seq/dashboard/metrics` | — | ServiceMetrics логи |
| GET | `/api/seq/dashboard/service-metrics` | hours (def 12) | Метрики по сервисам |
| GET | `/api/seq/dashboard/service-metrics/{name}` | hours | Метрики одного сервиса |
| GET | `/api/seq/services/status` | — | Статус сервисов (Seq + Docker) |

### /api/configuration — ConfigurationEndpoints.cs

| Метод | Путь | Описание |
|-------|------|---------|
| GET | `/api/configuration/s3-configuration` | S3 конфиг всех бакетов |
| POST | `/api/configuration/s3/update` | Обновить конфиг бакета `{ bucketId, parameters }` |

### /api/s3 — S3BrowserEndpoints.cs

| Метод | Путь | Query |
|-------|------|-------|
| GET | `/api/s3/buckets` | — |
| GET | `/api/s3/buckets/{bucketId}/objects` | prefix, continuationToken, maxKeys |
| GET | `/api/s3/buckets/{bucketId}/presign` | key |

### /api/reserved-names — ReservedNamesEndpoints.cs

| Метод | Путь | Тело |
|-------|------|------|
| GET | `/api/reserved-names/` | — |
| POST | `/api/reserved-names/` | `{ name }` |
| PUT | `/api/reserved-names/` | `{ oldName, newName }` |
| DELETE | `/api/reserved-names/{name}` | — |

---

## gRPC-клиенты и вызовы

### UsersServerApiClient
| gRPC-метод | Вызывается из |
|-----------|-------------|
| `SearchUsersServerAsync()` | UsersEndpoints |
| `GetAllBadgesAsync()` | BadgesEndpoints |
| `CreateBadgeAsync()` | BadgesEndpoints |
| `UpdateBadgeAsync()` | BadgesEndpoints |
| `DeleteBadgeAsync()` | BadgesEndpoints |
| `AssignUserBadgeAsync()` | UsersEndpoints |
| `RemoveUserBadgeAsync()` | UsersEndpoints |
| `UpdateStorageLimitAsync()` | UsersEndpoints |

### FilesServerApiClient
| gRPC-метод | Вызывается из |
|-----------|-------------|
| `UploadBadgeImageAsync()` | BadgesEndpoints |
| `UploadAvatarServerAsync()` | UsersEndpoints |
| `ListStickerPacksAsync()` | StickersEndpoints |
| `CreateStickerPackAsync()` | StickersEndpoints |
| `AddStickerToPackAsync()` | StickersEndpoints |
| `DeleteStickerPackAsync()` | StickersEndpoints |
| `GetPresignedUrlAsync()` | StickersEndpoints |
| `GetUserStorageInfoServerAsync()` | UsersEndpoints |

### IdentityServerApiClient
| gRPC-метод | Вызывается из |
|-----------|-------------|
| `ListOtpVerificationServerAsync()` | UsersEndpoints |
| `DisableOtpVerificationServerAsync()` | UsersEndpoints |
| `GetActiveSessionsServerAsync()` | UsersEndpoints |
| `RemoveActiveSessionServerAsync()` | UsersEndpoints |

### ConfigurationApiClient
| gRPC-метод | Вызывается из |
|-----------|-------------|
| `GetConfigurationAsync()` | ConfigurationEndpoints, S3BrowserService |
| `UpdateConfigurationAsync()` | ConfigurationEndpoints |
| `GetReservedNamesAsync()` | ReservedNamesEndpoints |
| `AddReservedNameAsync()` | ReservedNamesEndpoints |
| `RenameReservedNameAsync()` | ReservedNamesEndpoints |
| `DeleteReservedNameAsync()` | ReservedNamesEndpoints |

---

## Конфигурация (appsettings.json)

```json
{
  "Telegram": {
    "BotToken": "...",
    "Admins": "495716470:admin_nick"
  },
  "Auth": {
    "TokenExpirationDays": 3,
    "PendingRequestTimeoutMinutes": 10
  },
  "LiteDb": {
    "Path": "db/tokens.db"
  },
  "Seq": {
    "ServerUrl": "http://seq:80"
  },
  "UsersService": { "Host": "...", "Token": "..." },
  "FilesService":  { "Host": "...", "Token": "..." },
  "IdentityService": { "Host": "...", "Token": "..." },
  "ConfigurationService": { "Host": "...", "Token": "..." }
}
```

---

## Docker — особенности

Dockerfile монтирует `/var/run/docker.sock`, устанавливает Docker CLI.
`DockerService` запускает команды через `Process` с `ArgumentList` (не string — защита от injection).

**Self-управление** (restart-own, update-own): AdminPanel запускает ephemeral helper-контейнер, который рестартует или обновляет саму панель. Это обходит проблему "убить себя".

---

## Безопасность (сводка из SECURITY_AUDIT.md)

Полный аудит: `Backend/Barkfluff.AdminPanel/SECURITY_AUDIT.md`

Критические проблемы:
- Docker socket = полный контроль над хостом
- Отключение 2FA у произвольного пользователя без аудита
- Cookie `auth_token` без `HttpOnly` флага
- Telegram-токен в открытом виде в `appsettings.json`
- Нет разделения ролей (super-admin / operator / viewer)
