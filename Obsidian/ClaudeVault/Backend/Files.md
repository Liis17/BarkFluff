# BarkFluff.Files

Управление файлами. Метаданные в PostgreSQL, файлы в Minio (S3). Порт: **7005**.
Поддерживает дедупликацию по SHA256, сжатие изображений, генерацию превью, определение типа по magic bytes.

Расположение: `Backend/BarkFluff.Files/`

> 📁 Детальная карта всех файлов проекта: [[Backend/Files-ProjectMap]]

## Сборка

```bash
dotnet build BarkFluff.Files.csproj
dotnet ef migrations add <Name> --project .
```

Design-time factory: `FilesContextFactory` (подключение к `localhost`).

## Архитектура

### Два gRPC-сервиса

**FilesApiService** (`TokenType.User`) — клиентский API:
- `GetUploadUrl`, `GetTempDownloadUrl`, `CheckFileHash`, `GetUserStorageInfo`
- Стикеры (только чтение): `ListStickerPacks`, `GetStickerPack`

**FilesServerApiService** (`TokenType.Service`) — серверный API (включает админ-операции для [[Backend/AdminPanel]]):
- `GetFileData` / `GetFilesData` — метаданные файлов
- `UploadBadgeImage` — загрузка PNG бейджей (байты напрямую, без сжатия)
- `UploadAvatarServer` — аватар от имени пользователя
- `UploadPosterServer` — постер профиля (UserProfilePoster) от имени пользователя для админ-панели
- `UploadFileServer(data, filename, file_type, owner_user_id)` — загрузка файла от имени пользователя (для [[Backend/Bots]]); переиспользует полный пайплайн `UploadFileCommand`: детекция типа, компрессия, превью, дедупликация → `{file_id, preview_url, file_size}`
- `GetUserStorageInfoServer` — хранилище по userId
- Стикеры (управление): `CreateStickerPack`, `UpdateStickerPack`, `DeleteStickerPack`, `ListStickerPacks`, `GetStickerPack`, `GetStickers`, `AddSticker`, `UpdateSticker`, `RemoveSticker`

### REST-контроллер

- `POST /upload/{uploadId}` — загрузка файла (multipart, лимит 512 MB)
- `GET /download/{fileId}` — скачивание. `DownloadFileCommandHandler` отдаёт `FileName = file.Filename` (оригинальное имя), ASP.NET `File(stream, contentType, fileName)` ставит `Content-Disposition: attachment; filename*=UTF-8''…` (кириллица ок). Раньше отдавался `{file.Id}{extension}` — браузер сохранял файл с именем-GUID.

Поток: клиент получает uploadId через gRPC `GetUploadUrl`, затем загружает по HTTP.

### URL-маршрутизация (FileUrlHelper)

- Через nginx: `ExternalEndpoint:Host` + `/web`
- Локально: `RunSettings.Host:Http1Port`

### S3-инфраструктура

- **S3BucketRegistry** (singleton) — реестр бакетов, маппинг `UploadFileType → bucketId`, S3-клиенты с кешированием
- **S3BucketInitializer** — автосоздание бакетов при старте. Публичная политика чтения удалена (security fix, коммит `03a8dd5e`); ошибки S3 при старте логируются и не роняют сервис; 403 Forbidden при проверке/создании бакета трактуется как «бакет уже создан вручную» (нужно для R2-токенов с правами только на объекты)
- **S3Uploader** — upload/download через `IAmazonS3`

Бакеты: `profile-pictures`, `message-images`, `message-videos`, `message-documents`, `message-audio`, `chat-pictures`, `badge-images`, `barkfluff-uploads` (fallback).

> **Тип `UserProfilePoster = 10`** — постеры профиля (горизонтальный баннер 3:1). Хранятся в бакете `profile-pictures`. Разрешены к публичному скачиванию через `GET /download/{id}`. Android-клиент должен использовать `USER_PROFILE_POSTER` при вызове `GetUploadUrl`.

### Доменные сущности

| Сущность | Назначение |
|----------|-----------|
| `UploadFile` | Основная таблица. `Uploaders` (List\<long\>) — дедупликация (GIN-индекс). `PreviewId` — ссылка на превью (частичный индекс). `ExpiresAt` — TTL слота незагруженного файла (~2 ч, чистится `TempFileCleanupService`). `ImageWidth`/`ImageHeight` — размеры изображения в пикселях (nullable int, только для графических типов). |
| `TempFile` | Временные файлы с `ExpiresAt`. Индекс по `OriginalFileId`. |
| `FileHash` | SHA256-хеши для дедупликации. **Уникальный** индекс `IX_FileHashes_Hash` (с 2026-07-16). |
| `BadgeImage` | Отдельная таблица (не `UploadFile`). Бакет `badge-images`. PNG без сжатия. |
| `StickerPack` | `CreatorUserId`, `CoverStickerId`, связь 1:N со `Sticker`. |
| `Sticker` | `FileId`, `PreviewFileId`, `Emoji`. Макс. 1024px, макс. 12 МБ. |

### Сервисы

- **ImageCompressor** — SixLabors.ImageSharp. Превью: ресайз до 1024px, JPEG 75%. Оригинал: макс. 2500px, макс. 2 МБ, JPEG 90%.
- **FileTypeDetector** — тип по magic bytes (JPEG, PNG, WebP, GIF, MP4, WebM, AVI, MOV, MP3, WAV, FLAC, M4A, OGG). OGG → `Voice`, остальное аудио → `Audio`.
- **VideoThumbnailExtractor** — FFMpegCore, статический бинарь `ffmpeg`/`ffprobe` (`mwader/static-ffmpeg`, копируется в образ в `Dockerfile.slim`, путь `/usr/local/bin` через `GlobalFFOptions`/`Ffmpeg:BinaryFolder`). Для `MessageAttachmentVideo`: кадр на 5-й секунде (или середина, если короче) → тот же `ImageCompressor`-пайплайн превью (1024px JPEG). Видео всегда буферизуется на диск при загрузке (FFmpeg читает файл по пути, не по стриму). Длительность видео не извлекается и не хранится (нет поля в proto).

### Метаданные изображений

При загрузке файлов графических типов (`UserAvatar`, `MessageAttachmentImage`, `MessageAttachmentGif`, `ChatPicture`, `MessageAttachmentSticker`, `UserProfilePoster`) автоматически извлекаются и сохраняются размеры изображения через `Image.IdentifyAsync()` (SixLabors.ImageSharp). Для `MessageAttachmentVideo` `ImageWidth`/`ImageHeight` берутся из декодированного превью-кадра (см. VideoThumbnailExtractor выше). Значения `ImageWidth`/`ImageHeight` возвращаются во всех ответах `UploadFileInfo` (`GetFileData`, `GetFilesData`). Для не-графических файлов и старых записей возвращается `0`.

## Конфигурация

- `S3Buckets` — секция с настройками каждого бакета (может быть на разных S3-хранилищах)
- `FilesDb`, `UsersService:Host/Token`

## Proto

- `files_api.proto` — Server
- `users_api.proto` — Client (валидация аватара)
