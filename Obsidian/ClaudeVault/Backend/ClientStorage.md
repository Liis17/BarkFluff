# BarkFluff.ClientStorage

→ [[Backend/ClientStorage-ProjectMap]] — полная карта файлов проекта


Автономный микросервис для хранения и раздачи клиентских дистрибутивов BarkFluff (Windows, WinUI, Android, macOS, iOS).
REST API на ASP.NET Core 10.0, файлы в S3/Minio, метаданные в SQLite.

**Не входит** в основную микросервисную инфраструктуру — нет gRPC, нет MassTransit, нет XAuth, нет Configuration service. Работает изолированно.

Расположение: `Backend/BarkFluff.ClientStorage/`

## Сборка

```bash
dotnet build BarkFluff.ClientStorage.csproj
docker-compose -f docker/msk/docker-compose-msk.yml up -d clientstorage-dev
dotnet ef migrations add <Name> --project BarkFluff.ClientStorage.csproj
```

Design-time factory: `Persistence/ClientStorageContextFactory.cs` (файл `clientstorage.db`).

## Переменные окружения

| Variable | Описание |
|----------|----------|
| `S3_ACCESS_KEY` | Ключ доступа |
| `S3_SECRET_KEY` | Секретный ключ |
| `S3_SERVICE_URL` | URL S3 (default: `http://localhost:9000`). **В продакшене — S3-совместимое хранилище от HostKey**, не MinIO; MinIO остаётся только локальным dev-хранилищем (на сервере его нет) |
| `S3_BUCKET_NAME` | Имя бакета (default: `client-storage`) |
| `S3_REGION` | Регион для SigV4; для Cloudflare R2 — `auto` |
| `UPLOAD_TOKEN` | **Обязательный** Bearer-токен для POST-эндпоинтов |
| `REGISTRY_STORAGE_S3_CHUNKSIZE` | Размер части multipart-загрузки в S3 (default 16 МБ; для Cloudflare R2 рекомендуется 100 МБ) |

## API Endpoints

**Download (публичные):**
- `GET /get/barkfluff{windows|winui|kotlin|macos|ios}[/{channel}]` — скачать клиент (streaming, поддержка Range)
- `GET /get/barkfluff{windows|winui|kotlin|macos|ios}[/{channel}]/version` — версия
- `GET /get/barkfluffwindows[/{channel}]/bitsurl` — presigned URL + метаданные для BITS-задания (Windows)

**Upload (Bearer `UPLOAD_TOKEN`, заголовок `X-App-Version`):**
- `POST /set/barkfluff{windows|winui|kotlin|macos|ios}[/{channel}]` — загрузить (multipart form, поле `file`, лимит 512 MB)

Каналы: `release` (default), `beta`, `dev`, `nightly`.

### Ответ `/bitsurl`
```json
{
  "url":        "https://...",       // presigned S3 URL, TTL 6 часов
  "fileName":   "BarkFluff.exe",
  "fileSize":   104857600,
  "checksum":   "abc123...",         // SHA-256 hex — для BitsJob.SetHashAndAlgorithm
  "version":    "1.2.3",
  "uploadedAt": "2026-03-24T..."
}
```

## Архитектура

- `Controllers/ClientStorageController` — GET (скачивание) и POST (загрузка)
- `Domain/` — `ClientFile`, `ClientType` (Windows/WinUI/Kotlin/MacOS/iOS), `ReleaseChannel` (Release/Beta/Dev/Nightly)
- `Infrastructure/S3StorageService` — AWS SDK, стриминг из S3
- `Infrastructure/LocalFileCache` — локальный дисковый кеш (`CACHE_DIR`, default `/app/cache`)
- `Infrastructure/HashingReadStream` — ~~класса не существует~~; SHA-256 вычисляется инлайн в контроллере через `IncrementalHash` (один проход до отправки в S3)
- `Services/CacheWarmupService` — `IHostedService`, прогревает кеш при старте контейнера
- `Services/OldVersionsCleanupService` — `IHostedService`, выполняет стартовую чистку файлов старше 7 дней, каждые 3 часа оставляет максимум 3 версии и ежедневно в 03:00 по Москве оставляет только последнюю версию по каждой паре `ClientType × ReleaseChannel`
- `Middleware/TokenAuthMiddleware` — Bearer-токен только для `/set/*`
- `Persistence/` — EF Core + SQLite

## Детали реализации

### Локальный кеш
- При старте контейнера `CacheWarmupService` скачивает последние версии всех клиентов из S3 в `/app/cache/`
- Файлы именуются: `windows_release`, `windows_beta`, `winui_nightly`, `kotlin_release`, и т.д.
- При загрузке нового файла (`/set/*`) кеш обновляется асинхронно в фоне
- Кеш персистентный — named volume `clientstorage-cache:/app/cache` в docker-compose
- Образ non-root (chiseled, UID 1654): каталог `/app/cache` создаётся в CI (`mkdir -p ./publish/cache` в workflow) и попадает в образ с `chown 1654:1654` через `COPY --chown`. Без этого Docker создаёт точку монтирования volume от root → `UnauthorizedAccessException` при записи кеша. existed-том, созданный до фикса, нужно один раз удалить (`docker volume rm`) — при первом монтировании свежего volume владелец наследуется из образа

### Upload
- ASP.NET буферирует IFormFile в temp-файл при разборе multipart
- Контроллер пишет тело в собственный temp-файл (`Path.GetTempPath()/barkfluff-clientstorage-uploads/{guid}`) с попутным `IncrementalHash` SHA-256 — один проход по сети
- Temp-файл заливается в S3/MinIO через `Amazon.S3.Transfer.TransferUtility` (multipart, 16 MB parts) — устойчиво к таймаутам и сетевым флукам
- В `AmazonS3Config`: `Timeout=30 мин`, `RequestChecksumCalculation/ResponseChecksumValidation = WHEN_REQUIRED` — иначе SDK 4 шлёт MinIO trailing-чексуммы (CRC64NVME), которые MinIO не понимает и отвечает ошибкой
- `S3_REGION` (env) → `AmazonS3Config.AuthenticationRegion`. Для Cloudflare R2 обязателен (значение `auto`), для MinIO/dev не задаётся
- `TransferUtilityUploadRequest.DisablePayloadSigning = true` — Cloudflare R2 не реализует chunked signing (`STREAMING-AWS4-HMAC-SHA256-PAYLOAD`); `UNSIGNED-PAYLOAD` совместим с R2/MinIO/S3, целостность обеспечивает TLS
- После успешной загрузки temp-файл передаётся фоновой задаче для обновления кеша и удаляется после неё; при ошибке загрузки удаляется сразу
- После ответа клиенту фоновая задача сначала обновляет кеш из temp-файла, а при неудаче скачивает файл из S3

### Автоочистка версий
- При старте контейнера удаляются записи SQLite и соответствующие S3-объекты с `UploadedAt` старше 7 дней. Стартовая очистка завершается до запуска прогрева локального кеша.
- Каждые 3 часа удаляются самые старые версии, если для одной пары `ClientType × ReleaseChannel` накопилось больше 3 записей; остаются 3 последние по `UploadedAt`.
- Ежедневно в 03:00 по московскому времени (00:00 UTC) удаляются все версии, кроме последней по каждой паре `ClientType × ReleaseChannel`.
- Очистка работает по записям SQLite и их `S3Key`; orphan-объекты в бакете без записи `ClientFiles` не сканируются и не удаляются.
- Retention и расписание заданы в коде `OldVersionsCleanupService`, отдельные переменные конфигурации для них не предусмотрены.

### Лимиты загрузки
- `Kestrel.Limits.MaxRequestBodySize = 512 MB`
- `FormOptions.MultipartBodyLengthLimit = 512 MB` (без него ASP.NET режет multipart по дефолтным 128 MB → 500)
- `[RequestSizeLimit]` + `[RequestFormLimits]` на каждом POST-эндпоинте дублируют глобальные лимиты
- nginx (`storage.barkfluff.com`): `client_max_body_size 512m`, `client_body_timeout 1800s`, `proxy_read/send_timeout 1800s`, `proxy_request_buffering off`
- Kestrel `MinRequestBodyDataRate` отключён, чтобы медленные клиенты не получали 408

### Download (кеш → S3)
- Приоритет: `LocalFileCache` (с диска контейнера) → стриминг из S3 как fallback
- В обоих случаях: `Accept-Ranges: bytes` + `EnableRangeProcessing = true`

### /bitsurl
- Возвращает URL самого ClientStorage (`/get/barkfluff{platform}/{channel}`)
- S3 и MinIO наружу не фигурируют
- Checksum (SHA-256 hex) и метаданные — для проверки целостности на клиенте
- BITS качает через ClientStorage (кешированный файл), поддержка Range встроена

## Логи

Serilog → Seq + Console (`Application = "BarkFluff.ClientStorage"`), метрики через [[Backend/GrpcServer]]: публикации и скачивания релизов, входящий/исходящий байтовый трафик, cache hit/miss и ошибки S3.
