# BarkFluff.Files

Управление файлами. Метаданные в PostgreSQL, файлы в Minio (S3). Порт: **7005**.
Поддерживает дедупликацию по SHA256, сжатие изображений, генерацию превью, определение типа по magic bytes.

Анонимный liveness endpoint: `GET /ping` → `pong`.

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
- `GetTempDownloadUrl` валидирует `file_ids` через `Guid.TryParse` и на невалидном значении бросает `NotValidFileIdException` (логируя само значение). Раньше `Guid.Parse` ронял вызов `FormatException` — в логах это выглядело как «КРИТИЧЕСКАЯ ОШИБКА», хотя виноват клиент, приславший вместо идентификатора, например, готовый URL картинки

**FilesServerApiService** (`TokenType.Service`) — серверный API (включает админ-операции для [[Backend/AdminPanel]]):
- `GetFileData` / `GetFilesData` — метаданные файлов
- `UploadBadgeImage` — загрузка PNG бейджей (байты напрямую, без сжатия)
- `UploadAvatarServer` — аватар от имени пользователя
- `UploadPosterServer` — постер профиля (UserProfilePoster) от имени пользователя для админ-панели
- `UploadFileServer(data, filename, file_type, owner_user_id)` — загрузка файла от имени пользователя (для [[Backend/Bots]]); переиспользует полный пайплайн `UploadFileCommand`: детекция типа, компрессия, превью, дедупликация → `{file_id, preview_url, file_size}`
- `GetTempDownloadUrlServer(file_ids)` — временные ссылки на скачивание (для [[Backend/Bots]], метод `getFile`); обёртка над тем же `GetTempDownloadUrlCommand`, что и клиентский `GetTempDownloadUrl`. Нужен потому, что вложения сообщений по прямому `file_id` через `/download/{fileId}` **не отдаются** — там пропускаются только `UserAvatar`, `ChatPicture`, `UserProfilePoster`
- `GetUserStorageInfoServer` — хранилище по userId
- Стикеры (управление): `CreateStickerPack`, `UpdateStickerPack`, `DeleteStickerPack`, `ListStickerPacks`, `GetStickerPack`, `GetStickers`, `AddSticker`, `UpdateSticker`, `RemoveSticker`

### REST-контроллер

- `POST /upload/{uploadId}` — загрузка файла (multipart, лимит 512 MB)
- `GET /download/{fileId}` — скачивание. `DownloadFileCommandHandler` отдаёт `FileName = file.Filename` (оригинальное имя), ASP.NET `File(stream, contentType, fileName)` ставит `Content-Disposition: attachment; filename*=UTF-8''…` (кириллица ок). Раньше отдавался `{file.Id}{extension}` — браузер сохранял файл с именем-GUID.

Поток: клиент получает uploadId через gRPC `GetUploadUrl`, затем загружает по HTTP.

### URL-маршрутизация (FileUrlHelper)

- Через nginx: `ExternalEndpoint:Host` + `/web`
- Локально: `RunSettings.Host:Http1Port`

> **Отдельный файловый адрес (мимо CDN).** Ссылки Files всегда указывают на `ExternalEndpoint:Host`,
> который в проде стоит за Cloudflare с лимитом 100 МБ на файл. Ключ `ExternalEndpoint:MediaHost`
> (миграция `20260816120000_AddFilesMediaExternalEndpoint`, по умолчанию пустой) задаёт второй
> публичный адрес того же HTTP-порта, направленный на origin напрямую
> (`files2.barkfluff.com`, [[Backend/Nginx]]). **Сам сервис его не использует** — адрес уходит
> клиентам через [[Backend/Beacon]] (`files_media_endpoint`), и хост подменяет клиент, сохраняя
> путь `/web/...`. Так старые клиенты продолжают работать через прежний адрес.

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

- **ImageCompressor** — SixLabors.ImageSharp. Превью: ресайз до 1024px, JPEG 75%. Оригинал: макс. 2500px, макс. 2 МБ, JPEG 90%. Для `MessageAttachmentGif` также создаётся JPEG-preview первого кадра: веб-профиль показывает его статично, не запуская GIF-анимацию.
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
# Метрики

[[Backend/AdminPanel]] показывает успешные и ошибочные upload/download, байты по направлениям и суммарный файловый трафик. Локальный download проходит через `CountingReadStream`: учитываются реально прочитанные HTTP-ответом байты, поэтому non-seekable S3-поток не даёт нулевой трафик. Завершённые federated downloads добавляются в те же общие Files-счётчики.

Для диагностики долгих загрузок сервис публикует gauges последнего успешного upload (в миллисекундах): `files_last_upload_total_ms`, `files_last_upload_buffering_ms`, `files_last_upload_hashing_ms`, `files_last_upload_processing_ms`, `files_last_upload_s3_ms`. Это измерения копирования запроса в буфер, SHA-256, детекции/валидации/обработки превью, исходных S3-загрузок и полного пайплайна соответственно. Они сохраняются только в памяти и экспортируются обычным фоновым batched-репортёром, без отдельного лога на файл; все пять значений обновляются атомарно, поэтому относятся к одной загрузке.

## `FetchFileStream` — отдача файла ноде-партнёру (этап 3.2, docs/rearch/phase-3/step-3.2-fetchfile-access.md)

Server-streaming RPC в `FilesServerApi`. Зовёт только [[Backend/Federation]] своей ноды с service-токеном, **уже выполнив авторизацию на уровне ноды** (`Messages.CheckFileFederationAccess`).

- **Whitelist типов намеренно не применяется.** Публичный `/download/{id}` отдаёт по оригинальному Guid только `UserAvatar`/`ChatPicture`/`UserProfilePoster`; здесь то же ограничение сделало бы отдачу вложений невозможной, а авторизация уже произошена уровнем выше.
- Первый чанк — метаданные (`total_size`, `content_type`, `file_name`), далее данные по 256 КБ (`FetchFileStreamQueryHandler.ChunkSize`): заметно ниже лимита gRPC-сообщения в 4 МБ с запасом на накладные расходы.
- `total_size` — размер файла **целиком**, а не длина выданного куска: по нему принимающая нода контролирует объём и рвёт стрим при превышении (риск №44).
- «Файла нет» и «файл не загружен» наружу неразличимы — оба дают `NotFound`.
- Стриминг без буферизации файла в памяти; отмена по `CancellationToken` вызова.
- Не через MediatR: результат — поток, а не сообщение.

### Range в S3 (`IS3Uploader.DownloadRangeAsync`)

Нужен перемотке видео на принимающей ноде — без Range клиент тянул бы файл целиком ради середины.

- Контракт: `rangeFrom` inclusive, `rangeTo` **exclusive**; `AWS SDK ByteRange` inclusive с обеих сторон, конверсия внутри.
- Полный размер объекта берётся из заголовка `Content-Range` (`bytes a-b/TOTAL`): при range-запросе `ContentLength` — это длина куска, а не файла. Без range — `ContentLength`.
- Возвращает `S3ObjectRange(Content, TotalSize, ContentType)`; поток по-прежнему владеет `GetObjectResponse` (возврат HTTP-соединения в пул).

## Скачивание federated-вложений через свою ноду (этап 3.3, docs/rearch/phase-3/step-3.3-fed-download.md)

Клиент ноды B качает вложение с ноды A **через свою ноду** — прямого доступа к чужой ноде у него нет и не должно быть.

### Модель — temp-capability, в паритет локальным вложениям

Изначальный план фазы предполагал прямой маршрут `/download/fed/{server}/{fileId}`. Фактически существующий `/download` **не имеет auth вообще**: локальные вложения качаются по временным capability-ссылкам. Fed-вложения идут тем же путём, иначе в системе появились бы две разные модели доступа к одному и тому же типу данных.

```
GetTempDownloadUrl(fed_files=[{origin_server, file_id}])   ← user-токен
  → Messages.CheckFedFileUserAccess: пользователь — участник чата с этим вложением?
  → TempFile { OriginServer, FileName, SizeBytes, AttachmentType }   ← снапшот 3.1
  → обычная ссылка /download/{tempId}

GET /download/{tempId}                                     ← без auth, capability
  → temp-запись федеративная → fed-ветка
  → Federation.FetchRemoteFile → origin → S3
```

Прямой публичный маршрут остаётся только для **аватаров** (этап 3.4) — они и локально публичны по Guid.

**Недоступное вложение просто не попадает в ответ** `GetTempDownloadUrl` — ровно та же семантика, что у ненайденного локального `file_id`. Отдельной ошибки нет намеренно: иначе перебором `(origin, file_id)` можно было бы выяснять, что существует на чужих нодах.

### Fed-ветка скачивания (`FederatedDownloadService`)

Хелпер `File(...)` не подходит: поток приходит чанками с чужой ноды и **не seekable**, а Range нужен (перемотка видео). Поэтому заголовки и тело пишутся в `Response` вручную.

- **Range** (`ByteRangeHeader`): один диапазон — `bytes=a-b`, `bytes=a-`, `bytes=-N`. Корректный → `206` + `Content-Range` + `Accept-Ranges`; вне файла → `416`; **некорректный синтаксис → отдаём целиком** (RFC 9110 разрешает игнорировать битый Range — это лучше, чем 416 на каждую опечатку). Множественные диапазоны не поддерживаются.
- **Отсечение по объёму (риск №44, второй уровень):** считаем отданные байты и рвём соединение при превышении снапшота. Federation уже режет по заявленному origin'ом `total_size` (3.2) — здесь строже, по тому, что мы сами записали при импорте сообщения. Заголовки к этому моменту уже ушли, корректного кода ошибки не осталось — поэтому именно `Abort()`.
- **`Content-Type`** — из первого чанка (origin знает реальный тип из S3), fallback по имени из снапшота.
- **`Content-Disposition`** — имя пришло с чужой ноды, поэтому санитизируется: убирается путь (traversal) и всё, что может разорвать заголовок (CR/LF, кавычки, управляющие символы); `.`/`..` после `GetFileName` отбрасываются.
- **Без буферизации**: чанк пришёл — сразу ушёл в `Response.Body` с flush; `HttpContext.RequestAborted` пробрасывается в gRPC-вызов.
- **Кеша содержимого и превью нет** — решение владельца: каждое обращение тянет байты с origin заново.

### Конфигурация Files

`MessagesService:Host/Token` (проверка доступа) и `FederationService:Host/Token` (проксирование байтов) — бакет `ServiceId.Files = 5`, миграция `20260728050000_AddFilesFederationConfiguration`.

### Метрики

`fed_downloads`, `fed_download_bytes_total`, `fed_download_size_exceeded`.

## Аватары remote-пользователей (этап 3.4, docs/rearch/phase-3/step-3.4-remote-avatars.md)

У аватара **своя ветка доступа** — по приватности владельца, а не по членству в чате: аватар не является вложением сообщения, поэтому проверка «есть общий fed-чат» (3.2) к нему неприменима.

### `CheckFedAvatarAccess(file_id)` — origin

Файл существует, `Type == UserAvatar`, загружен → владелец (`Uploaders.First()`) → `Users.IsAvatarVisibleToFederation`. «Не аватар» и «нет файла» снаружи неразличимы. Сбой Users → отказ (fail-closed): лучше не показать аватар, чем показать вопреки настройке.

### `/download/fed/{server}/{fileId}` — принимающая сторона

Публичный маршрут **без auth** — как и локальный `/download` для аватаров: они и локально публичны по оригинальному Guid. Это единственный прямой fed-маршрут; вложения чатов идут temp-моделью (3.3).

1. `server` == своё `Federation:ServerName` → 404 (свои аватары качаются обычным `/download`).
2. `Users.CheckRemoteAvatarRef` → пара (нода, file_id) должна быть в `RemoteUsers`, иначе 404.
3. `Federation.FetchRemoteFile` → стрим клиенту (тот же путь, что у fed-вложений).
4. **Кап вместо снапшота**: у аватара нет записи размера, объём ограничивает `Files:FedAvatarMaxBytes` (дефолт 20 МБ). Второй уровень — обрыв по заявленному `total_size` в Federation (3.2).

Любая неудача — **404**: существование файлов и нод не светим. Классификация ошибок недоступного origin — этап 3.5.

**Ограничение MVP:** превью аватара remote-пользователя не федерируется — `CheckRemoteAvatarRef` сверяет только `AvatarFileId`, а `preview_file_id` в `RemoteUsers` не хранится. Клиент тянет полный аватар.

### Метрики

`fed_avatars_served`, `fed_avatar_rejected`, `fed_avatar_errors`.

## Карта ошибок fed-скачивания (этап 3.5, docs/rearch/phase-3/step-3.5-origin-down-ux.md)

Едина для обеих fed-веток — вложений (3.3) и аватаров (3.4).

| gRPC от Federation | HTTP | Смысл |
|---|---|---|
| `Unavailable` (origin лежит / circuit open / замолчал) | **503** + `Retry-After` (`Files:FedRetryAfterSeconds`, дефолт 30) | временно, ретрай уместен |
| `PermissionDenied` (нет общего чата, приватный аватар) | **404** | окончательно |
| `NotFound` (файла нет на origin) | **404** | окончательно |
| обрыв mid-stream | тело обрывается | клиент ретраит с `Range` (докачка) |

`403` и `404` намеренно сливаются: capability-модель не светит причину отказа.

**Заголовки пишутся только после первого чанка.** Пока ответ не начат, ошибку ещё можно отдать честным кодом; после — остаётся только оборвать соединение. Поэтому `WriteHeaders` вызывается из цикла чтения, а не до него.

Логи: отказ origin и недоступность — `warning` с `server_name`/`file_id`, без содержимого. Частоту при лежащей ноде ограничивает сам circuit breaker.

Метрики: `fed_download_total.{ok|origin_unavailable|denied|aborted}`.
