# BarkFluff.Files

Files хранит метаданные в PostgreSQL, объекты — в S3-совместимом хранилище. Сервис выдаёт upload reservations и временные capability-ссылки на скачивание. По умолчанию порты: 7005 (gRPC), 7006 (HTTP/1.1). Пути кода ниже указаны относительно `Backend/BarkFluff.Files/`; `Shared/` и `Tests/` — от корня репозитория.

## Загрузка и скачивание

- `FilesApi` (`TokenType.User`) в `Shared/BarkFluff.Proto/files_api.proto` выдаёт reservation через `GetUploadUrl`; клиент отправляет байты на `POST /upload/{uploadId}`. `client_operation_id` делает повторную выдачу reservation идемпотентной в пределах пользователя и типа файла.
- Reservation берётся с временной lease. Параллельные загрузки получают conflict/retry; повтор завершённой загрузки возвращает итоговый file ID. Состояние доступно через `GET /upload/{uploadId}/status`.
- Обработка определяет тип по содержимому, вычисляет hash для дедупликации, при необходимости сжимает изображения, создаёт preview и сохраняет объект с метаданными. При отмене/ошибке lease снимается, временные файлы удаляются.
- `GetTempDownloadUrl` выдаёт временную capability-ссылку. В текущем handler локальные file IDs проходят lookup без проверки членства/владения; отдельная проверка Messages применяется только к federated files. Нельзя считать выдачу локальных capabilities объектной авторизацией. Обычные вложения сообщений недоступны по публичному file ID; `/download/{fileId}` открыт для публичных типов вроде аватаров, картинок чата и постеров профиля.
- `MediaHost` — второй публичный media host, передаваемый через Beacon для прямой загрузки файлов в обход CDN. Files строит URL по `ExternalEndpoint`, сам не перенаправляет трафик между CDN и origin.

## Серверные и федеративные API

`FilesServerApi` (`TokenType.Service`) предоставляет метаданные файлов, сведения о хранилище, загрузку badge/avatar/poster/file, временные ссылки и управление стикерами. Bots использует серверную загрузку и временную ссылку.

Удалённое вложение также выдаётся по временной capability: Files проверяет право пользователя через Messages, затем получает файл через Federation и передаёт поток/диапазон байтов. `FetchFileStream` отдаёт разрешённые данные Federation; перед этим Messages проверяет доступ узла через `CheckFileFederationAccess`. Снимок метаданных федеративного вложения позволяет рендерить его без запроса в Files исходного узла.

## Хранение и обработка

- `UploadFile` содержит метаданные и связи с загрузившими пользователями; уникальный индекс SHA-256 используется для дедупликации.
- `UploadOperation` хранит ключ идемпотентности, зарезервированный и итоговый ID и состояние lease.
- `TempFile` содержит истекающие capability-ссылки для локальных и федеративных загрузок. `TempFileCleanupService` удаляет просроченные записи.
- `S3BucketRegistry` выбирает бакет по типу файла; инициализатор создаёт отсутствующие бакеты без публичной read-политики.
- `ImageCompressor`, `FileTypeDetector` и `VideoThumbnailExtractor` выполняют проверку/обработку; размеры графических файлов возвращаются в метаданных.

Источники: `Host/FilesApiService.cs`, `Host/FilesServerApiService.cs`, `Host/FilesController.cs`, `Features/GetUploadUrl/`, `Features/UploadFile/`, `Features/GetTempDownloadUrl/`, `Features/FetchFileStream/`, `Features/DownloadFile/FederatedDownloadService.cs`, `Infrastructure/S3Uploader.cs`, `Persistence/FilesContext.cs`.

Связанные сервисы: [[Backend/Users]], [[Backend/Messages]], [[Backend/Federation]], [[Backend/Beacon]]. Тесты: `Tests/BarkFluff.Files.Tests` (общий helper использует SQLite in-memory).
