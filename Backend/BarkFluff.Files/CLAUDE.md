# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Описание

Микросервис `Files` (порт 7005) — управление файлами в BarkFluff. Хранит метаданные в PostgreSQL, файлы — в Minio (S3). Поддерживает дедупликацию по SHA256-хешу, автоматическое сжатие изображений, генерацию превью и определение типа файла по magic bytes.

## Сборка и миграции

```bash
dotnet build BarkFluff.Files.csproj

# Миграции применяются автоматически при старте (Program.cs → ctx.Database.Migrate())
# Создание новой миграции:
dotnet ef migrations add <Name> --project .
```

Для миграций используется `FilesContextFactory` (design-time factory) с дефолтным подключением к `localhost`.

## Архитектура

### Два gRPC-сервиса

- **FilesApiService** (`TokenType.User`) — клиентский API:
  - `GetUploadUrl` — получить URL для загрузки файла
  - `GetTempDownloadUrl` — временные URL для скачивания (batch по списку FileIds)
  - `CheckFileHash` — проверка хеша для дедупликации
  - `GetUserStorageInfo` — информация о хранилище пользователя

- **FilesServerApiService** (`TokenType.Service`) — серверный API (межсервисные вызовы):
  - `GetFileData` / `GetFilesData` — метаданные файлов
  - `UploadBadgeImage` — загрузка изображений бейджей (принимает байты напрямую)
  - `UploadAvatarServer` — загрузка аватара от имени пользователя
  - `GetUserStorageInfoServer` — хранилище по userId

### REST-контроллер (FilesController)

- `POST /upload/{uploadId}` — загрузка файла (multipart, лимит 512 MB)
- `GET /download/{fileId}` — скачивание файла

Поток загрузки: клиент получает uploadId через gRPC `GetUploadUrl`, затем загружает файл по HTTP на `/upload/{uploadId}`.

### URL-маршрутизация (FileUrlHelper)

- Через nginx: `ExternalEndpoint:Host` + `/web` (nginx маршрутизирует `/web/` → files:порт)
- Локально: `RunSettings.Host:Http1Port` (прямой доступ)

### S3-инфраструктура

Каждый бакет может находиться на отдельном S3-хранилище со своими credentials. Конфигурация — секция `S3Buckets` в appsettings.

- **S3BucketRegistry** (singleton) — реестр бакетов, маппинг `UploadFileType → bucketId`, создание S3-клиентов с кэшированием по уникальным параметрам подключения
- **S3BucketInitializer** — автосоздание бакетов при старте с политикой публичного чтения
- **S3Uploader** — upload/download через `IAmazonS3`

Бакеты по типам: `profile-pictures`, `message-images`, `message-videos`, `message-documents`, `message-audio`, `chat-pictures`, `badge-images`, `barkfluff-uploads` (fallback).

### Доменные сущности (Domain/)

| Сущность | Назначение |
|----------|-----------|
| `UploadFile` | Основная таблица. `Uploaders` (List\<long\>) — ID пользователей-загрузчиков (дедупликация). `PreviewId` — ссылка на превью. |
| `TempFile` | Временные файлы с `ExpiresAt`. Индекс по `OriginalFileId`. |
| `FileHash` | SHA256-хеши файлов для дедупликации. Индекс по `Hash`. |
| `BadgeImage` | Отдельная таблица для изображений бейджей (не `UploadFile`). Бакет `badge-images`. |

### Сервисы (Services/)

- **ImageCompressor** — SixLabors.ImageSharp. Превью: ресайз до 1024px, JPEG 75%. Принудительное сжатие оригинала: макс. 2500px, макс. 2 МБ, JPEG 90%.
- **FileTypeDetector** — определение типа по magic bytes (JPEG, PNG, WebP, GIF, MP4, WebM, AVI, MOV, MP3, WAV, FLAC, M4A, OGG). OGG → `Voice`, остальное аудио → `Audio`.

### CQRS-команды (Features/)

Каждая feature — пара `{Xxx}Command.cs` + `{Xxx}CommandHandler.cs` через MediatR:

- `GetUploadUrl` — создаёт TempFile, возвращает URL загрузки
- `UploadFile` — загрузка в S3, сжатие изображений, генерация превью, сохранение хеша
- `DownloadFile` — скачивание из S3
- `GetTempDownloadUrl` — генерация временных URL
- `CheckFileHash` — поиск существующего файла по хешу
- `GetFileData` / `GetFilesData` — метаданные
- `GetUserStorageInfo` / `GetUserStorageInfoServer` — статистика хранилища
- `UploadBadgeImage` — загрузка PNG бейджа без сжатия
- `UploadAvatarServer` — загрузка аватара серверным вызовом

### Persistence (Storage-классы)

Scoped-сервисы обёртки над `FilesContext`: `UploadedFilesStorage`, `TempFilesStorage`, `FileHashesStorage`, `BadgeImagesStorage`.

## Зависимости от других сервисов

- **Configuration** — загрузка конфигурации при старте (`LoadConfiguration(ServiceId.Files)`)
- **Users** — gRPC-клиент `UsersServerApiClient` (с `JwtClientInterceptor`)
