# BarkFluff.Files — карта проекта

Сервис выдачи upload-capability, приёма файлов по HTTP и хранения метаданных в PostgreSQL с объектами в S3. Основные контракты — `Shared/BarkFluff.Proto/files_api.proto` и HTTP endpoints из `Host/FilesController.cs`.
Пути `Program.cs`, `Host/`, `Features/`, `Persistence/` и другие пути сервиса ниже отсчитываются от `Backend/BarkFluff.Files/`; `Shared/` и `Tests/` — от корня репозитория.

## Границы и точки входа

- `Program.cs` настраивает gRPC, XAuth, EF Core, MassTransit, S3 и `FilesController`.
- `Host/FilesApiService.cs` — пользовательские RPC (`TokenType.User`). `Host/FilesServerApiService.cs` — межсервисные и административные RPC (`TokenType.Service`).
- `Host/FilesController.cs` — `POST /upload/{uploadId}`, upload status и `GET /download/{fileId}`. Federated temp-capability downloads завершаются здесь через `Features/DownloadFile/FederatedDownloadService.cs`.

## Основные сценарии

- `Features/GetUploadUrl` резервирует upload ID и возвращает capability URL. `client_operation_id` даёт идемпотентное резервирование для повторов.
- `Features/UploadFile` принимает reservation, определяет тип по magic bytes, вычисляет SHA-256, обрабатывает изображения/видео, дедуплицирует и сохраняет объект вместе с метаданными.
- `Features/GetTempDownloadUrl` выдаёт временные ссылки по найденным локальным file IDs, без проверки доступа пользователя к локальному объекту. Для federated attachments проверяет членство через Messages и использует сохранённый metadata snapshot; скачивание проксируется через Federation.
- `Features/FetchFileStream` отдаёт файл chunks-only ноде-партнёру после отдельной проверки доступа Messages.
- `Features/UploadAvatarServer`, `UploadPosterServer`, `UploadBadgeImage`, `UploadFileServer` обслуживают межсервисные загрузки. Sticker pack/sticker features предоставляют CRUD и чтение стикеров.

## Хранение и обработка

- `Persistence/FilesContext.cs` и `Persistence/UploadedFilesStorage.cs` хранят метаданные, хеши, upload reservations, временные файлы, badges и stickers.
- `Infrastructure/S3BucketRegistry.cs`, `Infrastructure/S3BucketInitializer.cs`, `Infrastructure/S3Uploader.cs` управляют бакетами и операциями S3. Публичная выдача `/download/{id}` ограничена разрешёнными типами; обычные вложения сообщений скачиваются по временной capability-ссылке.
- `Services/ImageCompressor.cs`, `Services/FileTypeDetector.cs`, `Services/VideoThumbnailExtractor.cs` выполняют проверку и генерацию превью. Upload pipeline передаёт cancellation и снимает lease при ошибке.
- `Services/TempFileCleanupService.cs` удаляет истёкшие reservations и временные файлы; `Consumers/SessionRevokedConsumer.cs` обслуживает отзыв токенов.

## Конфигурация и проверка

Сервис зависит от PostgreSQL, S3-compatible storage, Redis/Settings и Users; federated download также использует Federation и Messages. Сервисные тесты находятся в `Tests/BarkFluff.Files.Tests`; common test helper использует SQLite in-memory.

← [[Backend/Files]]
