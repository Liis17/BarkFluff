# BarkFluff.ClientStorage

Автономный микросервис для хранения и раздачи клиентских дистрибутивов BarkFluff (Windows, Android, macOS, iOS).
REST API на ASP.NET 9.0, файлы в S3/Minio, метаданные в SQLite.

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
- `GET /get/barkfluff{windows|kotlin|macos|ios}[/{channel}]` — скачать клиент
- `GET /get/barkfluff{windows|kotlin|macos|ios}[/{channel}]/version` — версия

**Upload (Bearer `UPLOAD_TOKEN`, заголовок `X-App-Version`):**
- `POST /set/barkfluff{windows|kotlin|macos|ios}[/{channel}]` — загрузить (multipart form, поле `file`, лимит 512 MB)

Каналы: `release` (default), `beta`.

## Архитектура

- `Controllers/ClientStorageController` — GET (скачивание) и POST (загрузка)
- `Domain/` — `ClientFile`, `ClientType` (Windows/Kotlin), `ReleaseChannel` (Release/Beta)
- `Infrastructure/S3StorageService` — AWS SDK
- `Middleware/TokenAuthMiddleware` — Bearer-токен только для `/set/*`
- `Persistence/` — EF Core + SQLite

## Детали реализации

- Файлы → временный файл → SHA256 → S3 с GUID-ключом → запись в SQLite
- При скачивании — последний файл по `UploadedAt` для данного `ClientType` + `ReleaseChannel`
- S3 бакет создаётся автоматически при старте
- `Kestrel MaxRequestBodySize` = 512 MB
