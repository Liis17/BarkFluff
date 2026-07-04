# BarkFluff.Files — Карта проекта

Подробная карта всех файлов сервиса. Основная документация: [[Backend/Files]].

Расположение: `Backend/BarkFluff.Files/`  
Порт: **7005** | База: PostgreSQL (`FilesDb`) | Хранилище: Minio/S3

---

## Точка входа

| Файл | Описание |
|------|----------|
| `Program.cs` | Точка входа. Регистрирует gRPC (лимит 20 МБ), MediatR, XAuth, EF Core, MassTransit (RabbitMQ), S3, контроллеры REST. При старте выполняет миграции БД и инициализирует S3-бакеты. |
| `appsettings.json` | Базовая конфигурация (порты, секции S3Buckets, FilesDb, UsersService, RabbitMQ). |
| `appsettings.Development.json` | Конфигурация для локальной разработки. |

---

## Host — точки входа запросов

| Файл | Описание |
|------|----------|
| `Host/FilesApiService.cs` | gRPC-сервис для клиентов (`TokenType.User`). Методы: `GetUploadUrl`, `GetTempDownloadUrl`, `CheckFileHash`, `GetUserStorageInfo`, `ListStickerPacks`, `GetStickerPack`. |
| `Host/FilesServerApiService.cs` | gRPC-сервис для внутренних сервисов (`TokenType.Service`). Методы: `GetFileData`, `GetFilesData`, `UploadBadgeImage`, `UploadAvatarServer`, `GetUserStorageInfoServer`, полный CRUD стикерпаков и стикеров, `UploadStickerImage`. |
| `Host/FilesController.cs` | REST-контроллер. `POST /upload/{uploadId}` — загрузка файла multipart (лимит 512 МБ). `GET /download/{fileId}` — скачивание файла из S3. Инструментирован метриками. |

---

## Features — CQRS-команды (MediatR)

### Загрузка и получение файлов

| Папка | Описание |
|-------|----------|
| `Features/GetUploadUrl/` | Создаёт запись `UploadFile` в БД, возвращает URL для POST-загрузки и `uploadId`. |
| `Features/UploadFile/` | Принимает поток файла, определяет тип по magic bytes (`FileTypeDetector`), сжимает изображение и создаёт превью (`ImageCompressor`), сохраняет в S3 (`S3Uploader`), обновляет метаданные (размер, ETag, `ImageWidth`/`ImageHeight`, `PreviewId`). |
| `Features/DownloadFile/` | Скачивает файл из S3 по `fileId`, возвращает поток с `ContentType` и именем файла. |
| `Features/GetTempDownloadUrl/` | Создаёт временные записи `TempFile` и возвращает временные ссылки на скачивание для списка файлов. |
| `Features/CheckFileHash/` | Проверяет SHA256-хэш — возвращает `fileId` если файл уже загружен (дедупликация). |
| `Features/GetFileData/` | Возвращает метаданные одного файла (`UploadFileInfo`) по `fileId`. Используется другими сервисами. |
| `Features/GetFilesData/` | Пакетный запрос метаданных по списку `fileId`. |
| `Features/GetUserStorageInfo/` | Возвращает суммарный размер файлов текущего пользователя. |
| `Features/GetUserStorageInfoServer/` | То же, но принимает `userId` явно (для серверного вызова от AdminPanel). |

### Загрузка специальных изображений

| Папка | Описание |
|-------|----------|
| `Features/UploadAvatarServer/` | Загрузка аватара от имени пользователя (серверный вызов, байты напрямую). Сохраняет в бакет `profile-pictures`. |
| `Features/UploadBadgeImage/` | Загрузка PNG-изображения бейджа (без сжатия). Сохраняет в отдельную таблицу `BadgeImage` и бакет `badge-images`. |
| `Features/UploadStickerImage/` | Загрузка изображения стикера. Ресайз до 1024px, макс. 12 МБ. Генерирует превью. |

### Стикерпаки и стикеры

| Папка | Описание |
|-------|----------|
| `Features/CreateStickerPack/` | Создаёт новый стикерпак с `CreatorUserId`, `Name`, `Description`. |
| `Features/UpdateStickerPack/` | Обновляет мета-данные стикерпака (имя, описание, обложка). |
| `Features/DeleteStickerPack/` | Удаляет стикерпак и связанные стикеры. |
| `Features/ListStickerPacks/` | Постраничный список всех стикерпаков (пагинация `Offset/Limit`). |
| `Features/GetStickerPack/` | Возвращает один стикерпак по `packId` с полными данными. |
| `Features/AddSticker/` | Добавляет стикер в пак (`PackId`, `FileId`, `Emoji`). |
| `Features/RemoveSticker/` | Удаляет стикер из пака. |
| `Features/UpdateSticker/` | Обновляет emoji стикера. |
| `Features/GetStickers/` | Возвращает данные стикеров по списку `stickerIds`. |

---

## Domain — сущности

| Файл | Описание |
|------|----------|
| `Domain/UploadFile.cs` | Основная сущность файла. Поля: `Id` (Guid), `Uploaders` (List\<long\> — дедупликация), `CreatedAt`, `UploadedAt`, `Etag`, `Type`, `Filename`, `PreviewId`, `Size`, `ImageWidth`, `ImageHeight`. |
| `Domain/TempFile.cs` | Временный файл с `ExpiresAt` и ссылкой на `OriginalFileId`. Используется для временных download-ссылок. |
| `Domain/FileHash.cs` | SHA256-хэш файла. Индекс по `Hash`. Используется для дедупликации при загрузке. |
| `Domain/BadgeImage.cs` | Отдельная сущность для PNG-изображений бейджей (не `UploadFile`). Бакет `badge-images`. |
| `Domain/StickerPack.cs` | Стикерпак: `CreatorUserId`, `CoverStickerId`, связь 1:N со `Sticker`. |
| `Domain/Sticker.cs` | Стикер: `FileId`, `PreviewFileId`, `Emoji`, ссылка на `StickerPack`. |
| `Domain/UploadFileType.cs` | Enum типов файлов: `Unknown(0)`, `UserAvatar(1)`, `MessageAttachmentImage(2)`, `MessageAttachmentVideo(3)`, `MessageAttachmentGif(4)`, `MessageAttachmentDocument(5)`, `ChatPicture(6)`, `MessageAttachmentAudio(7)`, `MessageAttachmentVoice(8)`, `MessageAttachmentSticker(9)`, `UserProfilePoster(10)`. |

---

## Infrastructure — S3

| Файл | Описание |
|------|----------|
| `Infrastructure/S3BucketRegistry.cs` | Singleton. Реестр S3-бакетов: маппинг `UploadFileType → bucketId`, создание и кеширование `IAmazonS3` клиентов. Каждый бакет может быть на отдельном S3-хранилище. Бакеты: `profile-pictures`, `message-images`, `message-videos`, `message-documents`, `message-audio`, `chat-pictures`, `badge-images`, `barkfluff-uploads`. |
| `Infrastructure/S3BucketInitializer.cs` | Singleton. При старте создаёт бакеты если не существуют. Публичная политика чтения удалена (security fix); ошибки логируются и не роняют старт сервиса; 403 Forbidden при работе с R2-токеном без прав администрирования бакетов трактуется как «бакет создан заранее вручную». |
| `Infrastructure/S3Uploader.cs` | Transient. Upload/download файлов через `IAmazonS3`. Используется в handlers загрузки. |

---

## Persistence — БД (EF Core + PostgreSQL)

| Файл | Описание |
|------|----------|
| `Persistence/FilesContext.cs` | DbContext. Таблицы: `UploadedFiles`, `TempFiles`, `FileHashes`, `BadgeImages`, `StickerPacks`, `Stickers`. |
| `Persistence/FilesContextFactory.cs` | Design-time factory для `dotnet ef migrations` (подключение к `localhost`). |
| `Persistence/UploadedFilesStorage.cs` | Репозиторий для `UploadFile`: создание, поиск по id, обновление после загрузки. |
| `Persistence/TempFilesStorage.cs` | Репозиторий для `TempFile`: создание временных записей, поиск по `OriginalFileId`. |
| `Persistence/FileHashesStorage.cs` | Репозиторий для `FileHash`: поиск по SHA256, сохранение новых хэшей. |
| `Persistence/BadgeImagesStorage.cs` | Репозиторий для `BadgeImage`: сохранение и поиск записей бейджей. |
| `Persistence/StickerPacksStorage.cs` | Репозиторий для `StickerPack`: CRUD операции над стикерпаками. |
| `Persistence/StickersStorage.cs` | Репозиторий для `Sticker`: добавление, удаление, обновление, поиск стикеров. |

### Миграции (`Persistence/Migrations/`)

| Файл | Описание |
|------|----------|
| `20250509120516_AddFiles` | Начальная схема: таблица `UploadedFiles`. |
| `20250524130804_AddTempFiles` | Добавление таблицы `TempFiles`. |
| `20251214114821_AddFileHashesAndMultipleUploaders` | Таблица `FileHashes`, поле `Uploaders` (дедупликация). |
| `20260221215526_AddBadgeImages` | Таблица `BadgeImages`. |
| `20260316191241_AddStickerPacks` | Таблицы `StickerPacks` и `Stickers`. |
| `20260430000000_AddImageDimensions` | Поля `ImageWidth`, `ImageHeight` в `UploadedFiles`. |
| `FilesContextModelSnapshot.cs` | Снапшот модели EF Core. |

> ⚠️ `Migrations/20250720120530_AddFilesPreview.cs` — артефакт в корне проекта (вне `Persistence/Migrations/`), не подключён к `FilesContext`. Вероятно, устаревший файл.

---

## Services — бизнес-логика

| Файл | Описание |
|------|----------|
| `Services/ImageCompressor.cs` | Сжатие изображений через SixLabors.ImageSharp. Превью: ресайз до 1024px, JPEG 75%, белый фон (совместимость без альфа). Оригинал: макс. 2500px, макс. 2 МБ, JPEG 90%. Поддержка WebP. |
| `Services/FileTypeDetector.cs` | Singleton. Определяет тип файла по magic bytes (сигнатуры). Поддерживает: JPEG, PNG, BMP, WebP, HEIC/HEIF/AVIF, TIFF, GIF, MP4, WebM, AVI, MOV/QuickTime, MP3, WAV, FLAC, M4A, OGG, Opus. OGG/Opus → `Voice`, остальное аудио → `Audio`. |

---

## Consumers — RabbitMQ

| Файл | Описание |
|------|----------|
| `Consumers/SessionRevokedConsumer.cs` | MassTransit consumer. Слушает очередь `session-revoked-files`. При получении `SessionRevokedEvent` отзывает токен в `TokenRevocationCache` (XAuth). |

---

## Extensions, Helpers, Mapping, Configurations

| Файл | Описание |
|------|----------|
| `Extensions/ServiceCollectionExtensions.cs` | Extension-методы: `AddMinioS3` (регистрирует `S3BucketRegistry`, `S3Uploader`, `S3BucketInitializer`), `AddFileTypeDetection`. |
| `Extensions/FileExtensions.cs` | Вспомогательные extension-методы для работы с файлами. |
| `Helpers/FileUrlHelper.cs` | Статический хелпер. Формирует публичный base URL: через nginx (`ExternalEndpoint:Host/web`) или напрямую (`Host:Http1Port`). Генерирует URL upload/download. |
| `Mapping/UploadFileMapping.cs` | Маппинг `UploadFile` → `UploadFileInfo` (proto). |
| `Mapping/StickerPackMapping.cs` | Маппинг `StickerPack`/`Sticker` → proto-типы. |
| `Configurations/BucketS3Options.cs` | POCO-конфигурация одного S3-бакета (endpoint, credentials, bucket name). |

---

## Exceptions

| Файл | Описание |
|------|----------|
| `Exceptions/FileAlreadyUploadedException.cs` | Файл уже загружен (возвращается REST `400`). |
| `Exceptions/FileNotUploadedException.cs` | Файл не найден в S3 (возвращается REST `404`). |
| `Exceptions/StickerDimensionExceededException.cs` | Стикер превышает максимальные размеры (1024px). |
| `Exceptions/StickerTooLargeException.cs` | Файл стикера превышает 12 МБ. |

---

## Прочие файлы

| Файл | Описание |
|------|----------|
| `Dockerfile` / `Dockerfile.slim` | Docker-образы для production и slim-варианта. |
| `BarkFluff.Files.http` | HTTP-сниппеты для ручного тестирования REST-эндпоинтов. |
| `SECURITY_AUDIT.md` | Аудит безопасности сервиса. |
| `Properties/launchSettings.json` | Профили запуска для Visual Studio. |
| `Persistence/DataFixes/fix_profile_poster_type.sql` | SQL-скрипт для ручного исправления типа `UserProfilePoster` в существующих данных. |

---

## Proto-зависимости

| Proto | Роль |
|-------|------|
| `files_api.proto` | Определяет `FilesApi` (клиентский) и `FilesServerApi` (серверный) сервисы |
| `users_api.proto` | Клиент: `UsersServerApiClient` — валидация/получение данных пользователя при загрузке аватара |
| `shared.proto` | Общие типы (пагинация и др.) |
