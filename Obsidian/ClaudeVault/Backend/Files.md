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
- **S3BucketInitializer** — автосоздание бакетов при старте с политикой публичного чтения
- **S3Uploader** — upload/download через `IAmazonS3`

Бакеты: `profile-pictures`, `message-images`, `message-videos`, `message-documents`, `message-audio`, `chat-pictures`, `badge-images`, `barkfluff-uploads` (fallback).

> **Тип `UserProfilePoster = 10`** — постеры профиля (горизонтальный баннер 3:1). Хранятся в бакете `profile-pictures`. Разрешены к публичному скачиванию через `GET /download/{id}`. Android-клиент должен использовать `USER_PROFILE_POSTER` при вызове `GetUploadUrl`.

### Доменные сущности

| Сущность | Назначение |
|----------|-----------|
| `UploadFile` | Основная таблица. `Uploaders` (List\<long\>) — дедупликация. `PreviewId` — ссылка на превью. `ImageWidth`/`ImageHeight` — размеры изображения в пикселях (nullable int, только для графических типов). |
| `TempFile` | Временные файлы с `ExpiresAt`. Индекс по `OriginalFileId`. |
| `FileHash` | SHA256-хеши для дедупликации. Индекс по `Hash`. |
| `BadgeImage` | Отдельная таблица (не `UploadFile`). Бакет `badge-images`. PNG без сжатия. |
| `StickerPack` | `CreatorUserId`, `CoverStickerId`, связь 1:N со `Sticker`. |
| `Sticker` | `FileId`, `PreviewFileId`, `Emoji`. Макс. 1024px, макс. 12 МБ. |

### Сервисы

- **ImageCompressor** — SixLabors.ImageSharp. Превью: ресайз до 1024px, JPEG 75%. Оригинал: макс. 2500px, макс. 2 МБ, JPEG 90%.
- **FileTypeDetector** — тип по magic bytes (JPEG, PNG, WebP, GIF, MP4, WebM, AVI, MOV, MP3, WAV, FLAC, M4A, OGG). OGG → `Voice`, остальное аудио → `Audio`.

### Метаданные изображений

При загрузке файлов графических типов (`UserAvatar`, `MessageAttachmentImage`, `MessageAttachmentGif`, `ChatPicture`, `MessageAttachmentSticker`, `UserProfilePoster`) автоматически извлекаются и сохраняются размеры изображения через `Image.IdentifyAsync()` (SixLabors.ImageSharp). Значения `ImageWidth`/`ImageHeight` возвращаются во всех ответах `UploadFileInfo` (`GetFileData`, `GetFilesData`). Для не-графических файлов и старых записей возвращается `0`.

## Конфигурация

- `S3Buckets` — секция с настройками каждого бакета (может быть на разных S3-хранилищах)
- `FilesDb`, `UsersService:Host/Token`

## Proto

- `files_api.proto` — Server
- `users_api.proto` — Client (валидация аватара)
