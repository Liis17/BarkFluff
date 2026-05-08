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
│   ├── LogsExportEndpoints.cs         ← /api/seq/export/*
│   ├── LogsClearEndpoints.cs          ← /api/seq/clear/*
│   ├── ConfigurationEndpoints.cs      ← /api/configuration/*
│   ├── S3BrowserEndpoints.cs          ← /api/s3/*
│   └── ReservedNamesEndpoints.cs      ← /api/reserved-names/*
├── Middleware/
│   └── TokenAuthMiddleware.cs         ← cookie-аутентификация
├── Models/
│   ├── AuthToken.cs                   ← сессионный токен
│   ├── PendingAuthRequest.cs          ← запрос подтверждения (in-memory)
│   ├── SeqSettings.cs                 ← конфиг Seq
│   ├── LogsExportJob.cs               ← состояние job экспорта логов
│   ├── LogsClearJob.cs                ← состояние job очистки логов
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
│   ├── LogsExportService.cs           ← async job: pull Seq → JSON → ZIP, TTL-cleanup
│   ├── LogsClearService.cs            ← async job: count → DELETE Seq events, TTL-cleanup
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
| `GetEventsAsync(filter, count, fromUtc, afterId, toUtc)` | `GET /api/events` |
| `RunSqlQueryAsync(query, dates)` | `GET /api/sqlquery?q=` |
| `GetAllEventsListAsync(filter, fromUtc, maxEvents)` | постраничная загрузка |
| `GetSignalsAsync()` | `GET /api/signals` |
| `CountEventsAsync(fromUtc, toUtc)` | `GET /api/sqlquery?q=select count(*) ...` (через `RunSqlQueryAsync`) |
| `DeleteEventsAsync(fromUtc, toUtc)` | Удаляет события через NuGet `Seq.Api` (`SeqConnection.Events.DeleteAsync`). Пакет сам делает HATEOAS-discovery URL через `/api/events/resources` → `Links["DeleteInSignal"]` (RFC 6570). Возвращает `Task` (Seq не отдаёт количество удалённых — `DeleteResultPart` пустой). |
| `static ExtractEventsArray(JsonElement)` | парсит как массив, так и `{Events:[...]}` |

### LogsExportService
**Singleton.** In-memory `ConcurrentDictionary<Guid, LogsExportJob>` + фоновый `Timer` для TTL-cleanup. Использует `IServiceScopeFactory` чтобы достать scoped `SeqService` внутри фоновой Task.

| Метод | Описание |
|-------|---------|
| `StartExport(scope)` | Создать job, запустить `Task.Run(RunExportAsync)`, вернуть `Guid jobId` |
| `GetJob(jobId)` | Получить состояние |
| `TryDeleteJobFiles(jobId)` | Удалить zip + temp-dir + убрать job (вызывается из download endpoint в `Response.OnCompleted`) |

**Pipeline (`RunExportAsync`):**
1. State `Downloading`. Цикл: `seq.GetEventsAsync(count=1000, afterId=...)` → парсим через `SeqService.ExtractEventsArray` → пишем `page-NNNNN.json` (массив `JsonElement` через `Utf8JsonWriter`) → берём `Id` последнего события как `afterId` для следующей страницы. Для `Scope.Old` дополнительно передаётся `toDateUtc = UtcNow.AddDays(-14)`. Цикл прерывается, когда страница неполная (< 1000) или нет `Id`.
2. State `Compressing`. `ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false)` — однопоточно, "среднее" сжатие. JSON-папка удаляется после ZIP.
3. State `Ready`, выставляется `ZipPath` и `ZipSizeBytes`.

**TTL cleanup:** `Timer` каждые 5 минут проходит по `_jobs`; если job в состоянии `Ready`/`Error` и `UpdatedAtUtc` старше 30 минут — удаляет zip + dir + запись.

**Корневая папка:** `Path.Combine(Path.GetTempPath(), "logs-export")` — внутри контейнера это `/tmp/logs-export/`.

### LogsClearService
**Singleton.** In-memory `ConcurrentDictionary<Guid, LogsClearJob>` + фоновый `Timer` для TTL-cleanup. `IServiceScopeFactory` для получения scoped `SeqService` внутри фоновой Task. Зеркало `LogsExportService`, но без файлов и ZIP — только обращения к Seq API.

| Метод | Описание |
|-------|---------|
| `StartClear(scope)` | Создать job, запустить `Task.Run(RunClearAsync)`, вернуть `Guid jobId` |
| `GetJob(jobId)` | Получить состояние |

**Pipeline (`RunClearAsync`):**
1. Stage `Counting`: `seq.CountEventsAsync(toDateUtc)` — через SQL `select count(*) from stream`. Для `Scope.Old` `toDateUtc = UtcNow.AddDays(-14)`, для `All` — `null` (без ограничений). Результат записывается в `job.TotalCount`.
2. Stage `Deleting`: `seq.DeleteEventsAsync(toDateUtc)` — через `Seq.Api` NuGet (`SeqConnection.Events.DeleteAsync`). После завершения `DeletedCount` приравнивается к `TotalCount` (Seq не возвращает фактическое число удалённых — `DeleteResultPart` пустой).
3. State `Done` либо `Error` (с текстом исключения).

**TTL cleanup:** `Timer` каждые 5 минут удаляет job'ы в состоянии `Done`/`Error` старше 30 минут.

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

### /api/seq/export — LogsExportEndpoints.cs

Async-job экспорт логов в zip-архив. Все 3 ручки требуют валидный `AuthToken`.

| Метод | Путь | Тело / Query | Описание |
|-------|------|-------------|---------|
| POST | `/api/seq/export/start` | `{ "scope": "all" \| "old" }` | Создаёт job, возвращает `{ jobId }`. `old` = логи старше 14 дней (`toDateUtc = UtcNow - 14d`). |
| GET | `/api/seq/export/{jobId}/status` | — | `{ state: queued\|downloading\|compressing\|ready\|error, totalDownloaded, currentPage, zipSizeBytes, error }` |
| GET | `/api/seq/export/{jobId}/download` | — | Стрим zip через `Results.Stream(FileStream)` (без полной загрузки в RAM). После отправки `Response.OnCompleted` → `TryDeleteJobFiles(jobId)`. |

### /api/seq/clear — LogsClearEndpoints.cs

Async-job очистка логов в Seq. Обе ручки требуют валидный `AuthToken`.

| Метод | Путь | Тело / Query | Описание |
|-------|------|-------------|---------|
| POST | `/api/seq/clear/start` | `{ "scope": "all" \| "old" }` | Создаёт job, возвращает `{ jobId }`. `old` = логи старше 14 дней (`toDateUtc = UtcNow - 14d`); `all` = без ограничения по времени. |
| GET | `/api/seq/clear/{jobId}/status` | — | `{ state: queued\|counting\|deleting\|done\|error, scope, totalCount, deletedCount, error }` |

Авторизация в Seq при `DELETE /api/events` — через существующий `SeqSettings.ApiKey` (header `X-Seq-ApiKey`). Если ключ не задан / нет admin-прав — Seq вернёт 401/403 и job переходит в `error` с пробросом текста ответа.

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

## UI Pages — поведение фронтенда

### `/logs` (`Pages/logs.html`)

Просмотр событий Seq с фильтрами (сервис / уровень / поиск) и серверной пагинацией через `afterId`.

**Структура таблицы (5 колонок):** chevron / Время / Уровень / Сервис / Сообщение.

**Inline accordion с деталями** — при клике на строку под ней разворачивается детальная панель в формате как в Seq UI:
- `Message` — `RenderedMessage` события на белом фоне.
- `Template` — `MessageTemplate` (показывается только если отличается от Message).
- `Properties` — таблица `key → value`, отсортированная по имени; объекты сериализуются `JSON.stringify`.
- `Exception` — `<pre>` на красном фоне (только если поле непустое).
- Кнопка `Copy JSON` — копирует полный объект события через `navigator.clipboard.writeText(JSON.stringify(evt, null, 2))`.

Реализация:
- На каждое событие рендерятся **две** `<tr>`: основная (`.log-row` с `data-idx`) и скрытая `.log-detail-row` с `colspan="5"`.
- Один обработчик кликов через **делегирование** на `<tbody id="logsTableBody">` ловит как открытие/закрытие строки, так и клик по кнопке `.copy-json-btn`.
- Содержимое деталей рендерится **lazily** — только при первом раскрытии (`detail.dataset.rendered = '1'`).
- `dataset.idx` рассчитывается как `allLogs.length + index` ДО конкатенации в `allLogs`, чтобы при `append` (load more) индексы не пересекались.

**Backend нового endpoint не понадобился** — `/api/seq/events` уже отдаёт полное событие (`SeqService.GetEventsAsync` зовёт Seq с `render=true`), включая `Properties`, `Exception`, `MessageTemplate`.

**Экспорт логов в шапке.** Кнопка `Экспорт логов` справа от заголовка открывает модалку с 4 состояниями:

| State | UI |
|-------|----|
| `choice` | Две кнопки выбора: «Только старые логи (старше 2 недель)» и «Все логи». Крестик доступен. |
| `running` | Спиннер + текст этапа («Скачивание из Seq...» / «Сжатие архива...») + счётчик `Получено N логов`. **Крестика нет, backdrop не закрывает** — пока полнится job, модалку нельзя закрыть. |
| `done` | Текст «Архив скачан. Рекомендуется перезапустить...» + кнопки `Позже` / `Перезапустить`. Крестик доступен. |
| `error` | Текст ошибки + кнопка `Закрыть`. Крестик доступен. |

JS-flow:
1. Click `startExport(scope)` → `POST /api/seq/export/start` → получить `jobId`, перейти в state `running`.
2. `setInterval(pollExportStatus, 2000)` опрашивает `/status`. Текст этапа и счётчик обновляются по `state`/`totalDownloaded`.
3. На `state=ready` polling останавливается, `triggerDownload(jobId)` создаёт скрытую `<a href="/api/seq/export/{jobId}/download">` и кликает её — браузер начинает скачивание; модалка переходит в `done`.
4. Кнопка `Перезапустить` в `done` → `POST /api/docker/containers/admin-panel/restart-own` → `window.location = '/restarting'` (тот же flow, что у кнопки `Перезапустить` на `/services`).

Конкурирующих экспортов в одной вкладке быть не может — переменная `currentExportJobId` очищается при закрытии модалки и при переходе в done/error.

**Очистка логов в шапке.** Кнопка `Очистить логи` (красная) рядом с `Экспорт логов` открывает модалку с 5 состояниями:

| State | UI |
|-------|----|
| `choice` | Две кнопки: «Удалить старые логи (старше 2 недель)» и «Удалить все логи» (красная). Крестик доступен. |
| `confirm` | Красный заголовок «Подтверждение» + текст с предупреждением о необратимости + кнопки `Отмена` / `Удалить` (красная). Крестик доступен. |
| `running` | Спиннер + текст этапа («Подсчёт логов...» → «Удаление N логов...»). **Крестика нет, backdrop не закрывает.** |
| `done` | Текст «Удалено N из M логов» + кнопка `Закрыть`. Крестик доступен. |
| `error` | Текст ошибки от Seq + кнопка `Закрыть`. Крестик доступен. |

JS-flow:
1. Click `askClearConfirm(scope)` → state `confirm` с текстом, специфичным для scope. `clearConfirmBtn.onclick` пересоздаётся под выбранный scope.
2. Click `Удалить` → `startClear(scope)` → `POST /api/seq/clear/start` → state `running`, `setInterval(pollClearStatus, 1500)`.
3. `/status` возвращает state `counting` → `deleting` → `done`/`error`. На `deleting` показывается «Удаление {totalCount} логов...».
4. `done`: «Удалено {deletedCount} из {totalCount} логов». В отличие от экспорта, **перезапуск админ-панели не предлагается** — кеш UI логов обновится по обычному рефрешу.

Конкурирующих очисток в одной вкладке быть не может — переменная `currentClearJobId` сбрасывается при закрытии модалки.

### `/` (`Pages/dashboard.html`) — метрики сервисов

`knownServices` (список сервисов в карусели метрик) — **12 сервисов**:
`Identity, Users, Messages, Files, Updates, Notification, Beacon, FastAuth, Onliner, Configuration, Web, ClientStorage`.
Backend `KnownServices` (`SeqEndpoints.cs`) шире (включает CloudMessaging, Developers и инфраструктуру) — он используется для `/api/seq/services` и статуса контейнеров; для метрик dashboard сознательно урезан.

**Группировка метрик по типу.** В каждой карточке сервиса — 4 таба, между которыми переключаются мини-графики Chart.js:

| Tab | Что попадает |
|-----|-------------|
| Counters | всё, что не классифицировано как одна из остальных (по умолчанию) |
| Errors | имя содержит `error`, `_failed`, `_fail`, `rejected` |
| Gauges | заканчивается на `_unix`, содержит `uptime`, начинается с `active_` или заканчивается `_active`/`_healthy`/`_current`/`_size`/`_subscriptions`/`_streams`/`_connections`, либо содержит `_gauge` |
| Latency | содержит `duration`, `latency`, `_ms` (либо заканчивается на `_ms`) |

Классификация делается на фронте функцией `classifyMetric(name)` в `dashboard.html`. Если группа пуста — соответствующий tab disabled (серый). Если все группы пусты или ответ Seq пустой — placeholder `Нет данных`. Активным по умолчанию становится первый tab с непустой группой (Counters → Errors → Gauges → Latency).

Tab-переключатель — общий обработчик через делегирование на `#service-metrics-container`.

---

## Безопасность (сводка из SECURITY_AUDIT.md)

Полный аудит: `Backend/Barkfluff.AdminPanel/SECURITY_AUDIT.md`

Критические проблемы:
- Docker socket = полный контроль над хостом
- Отключение 2FA у произвольного пользователя без аудита
- Cookie `auth_token` без `HttpOnly` флага
- Telegram-токен в открытом виде в `appsettings.json`
- Нет разделения ролей (super-admin / operator / viewer)
