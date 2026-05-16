# BarkFluff.ClientStorage

→ [[Backend/ClientStorage-ProjectMap]] — полная карта файлов проекта


Автономный микросервис для хранения и раздачи клиентских дистрибутивов BarkFluff (Windows, Android, macOS, iOS).
REST API на ASP.NET Core 10.0, файлы в S3/Minio, метаданные в SQLite.

**Не входит** в основную микросервисную инфраструктуру — нет gRPC, нет MassTransit, нет XAuth, нет Configuration service. Работает изолированно.

Расположение: `Backend/BarkFluff.ClientStorage/`

## Сборка

```bash
dotnet build BarkFluff.ClientStorage.csproj
docker-compose -f Backend/BarkFluff.ClientStorage/docker-compose-dev.yml up -d
dotnet ef migrations add <Name> --project BarkFluff.ClientStorage.csproj
```

Design-time factory: `Persistence/ClientStorageContextFactory.cs` (файл `clientstorage.db`).

## Переменные окружения

| Variable | Описание |
|----------|----------|
| `S3_ACCESS_KEY` | Ключ доступа |
| `S3_SECRET_KEY` | Секретный ключ |
| `S3_SERVICE_URL` | URL S3 (default: `http://localhost:9000`) |
| `S3_BUCKET_NAME` | Имя бакета (default: `client-storage`) |
| `UPLOAD_TOKEN` | **Обязательный** Bearer-токен для POST-эндпоинтов |

## API Endpoints

**Download (публичные):**
- `GET /get/barkfluff{windows|kotlin|macos|ios}[/{channel}]` — скачать клиент (streaming, поддержка Range)
- `GET /get/barkfluff{windows|kotlin|macos|ios}[/{channel}]/version` — версия
- `GET /get/barkfluffwindows[/{channel}]/bitsurl` — presigned URL + метаданные для BITS-задания (Windows)

**Upload (Bearer `UPLOAD_TOKEN`, заголовок `X-App-Version`):**
- `POST /set/barkfluff{windows|kotlin|macos|ios}[/{channel}]` — загрузить (multipart form, поле `file`, лимит 512 MB)

Каналы: `release` (default), `beta`.

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
- `Domain/` — `ClientFile`, `ClientType` (Windows/Kotlin/MacOS/iOS), `ReleaseChannel` (Release/Beta)
- `Infrastructure/S3StorageService` — AWS SDK, стриминг из S3
- `Infrastructure/LocalFileCache` — локальный дисковый кеш (`CACHE_DIR`, default `/app/cache`)
- `Infrastructure/HashingReadStream` — ~~класса не существует~~; SHA-256 вычисляется инлайн в контроллере через `IncrementalHash` (один проход до отправки в S3)
- `Services/CacheWarmupService` — `IHostedService`, прогревает кеш при старте контейнера
- `Middleware/TokenAuthMiddleware` — Bearer-токен только для `/set/*`
- `Persistence/` — EF Core + SQLite

## Детали реализации

### Локальный кеш
- При старте контейнера `CacheWarmupService` скачивает последние версии всех клиентов из S3 в `/app/cache/`
- Файлы именуются: `windows_release`, `windows_beta`, `kotlin_release`, и т.д.
- При загрузке нового файла (`/set/*`) кеш обновляется асинхронно в фоне
- Кеш **эфемерный** — очищается при перезапуске контейнера, прогревается снова

### Upload
- ASP.NET буферирует IFormFile в temp-файл при разборе multipart
- Контроллер пишет тело в собственный temp-файл (`Path.GetTempPath()/barkfluff-clientstorage-uploads/{guid}`) с попутным `IncrementalHash` SHA-256 — один проход по сети
- Temp-файл заливается в S3/MinIO через `Amazon.S3.Transfer.TransferUtility` (multipart, 16 MB parts) — устойчиво к таймаутам и сетевым флукам
- В `AmazonS3Config`: `Timeout=30 мин`, `RequestChecksumCalculation/ResponseChecksumValidation = WHEN_REQUIRED` — иначе SDK 4 шлёт MinIO trailing-чексуммы (CRC64NVME), которые MinIO не понимает и отвечает ошибкой
- В `finally` temp-файл удаляется
- После ответа клиенту: фоновая задача скачивает файл из S3 в локальный кеш

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

Serilog → только Console (`Application = "BarkFluff.ClientStorage"`). Seq sink и метрики (`MetricsCollector`/`MetricsReporterService`) удалены — сервис изолирован от наблюдаемости основной инфраструктуры.
