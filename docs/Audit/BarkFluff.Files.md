# Аудит: BarkFluff.Files
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Сервис в целом аккуратно стримит загрузки/скачивания из S3 и проверяет JWT-политики на всех gRPC-методах (`FilesApiService` — `TokenType.User`, `FilesServerApiService` — `TokenType.Service`). Однако обнаружены серьёзные проблемы контроля доступа: при инициализации **всем S3-бакетам выставляется политика публичного чтения** (включая приватные документы и вложения сообщений), а выдача временных ссылок (`GetTempDownloadUrl`) и метод `CheckFileHash` **не проверяют владельца файла** — это классический IDOR/oracle на чужой контент. Дополнительно: отсутствует allowlist типов файлов и защита от inline-рендеринга SVG/HTML (stored XSS через прямой S3-URL). По производительности — загрузки до сотен МБ буферизуются целиком в `MemoryStream`, а ключевые запросы скачивания и подсчёта квоты идут по таблице без индексов.

| Критичность | Кол-во |
| ----------- | ------ |
| Critical    | 1      |
| High        | 3      |
| Medium      | 6      |
| Low         | 6      |
| **Итого**   | **16** |

Распределение по категориям: Безопасность — 9, Производительность — 6, Docker/nginx — 1.

## Безопасность

### S1. Публичное чтение для всех S3-бакетов (включая приватные вложения) — Critical
**Статус: ✅ Исправлено** (коммит `03a8dd5e`) — публичная bucket policy снята.

**Файл:** `Backend/BarkFluff.Files/Infrastructure/S3BucketInitializer.cs:87-114` (вызов на `:75`)
**Проблема:** При создании каждого бакета (в т.ч. `message-documents`, `message-images`, `message-videos`, `message-audio`, `chat-pictures`) ставится bucket policy с `"Principal": {"AWS": "*"}` и `"Action": "s3:GetObject"` на `arn:aws:s3:::{bucketName}/*`. То есть любой объект читается напрямую из S3 без какой-либо авторизации — нужно лишь знать ключ объекта (GUID файла).
**Почему это проблема:** Это полностью обходит модель доступа сервиса: проверку типа в `DownloadFileCommandHandler`, временные истекающие ссылки (`TempFile`), любые будущие ACL. Один раз раскрытый прямой S3-URL даёт постоянный доступ к приватному файлу, даже после истечения temp-ссылки. В проде используется внешний HostKey S3 (см. память проекта) — если его endpoint доступен из интернета, это прямое раскрытие приватной переписки/документов. GUID-ключи не перечислимы (политика не даёт `ListBucket`), но и не секретны: они утекают в логи, URL, превью, заголовки.
**Рекомендация:** Не делать бакеты публичными. Отдавать файлы только через сервис (как сейчас для документов) или через S3 pre-signed URL с коротким TTL. Если публичность нужна для аватаров/картинок чата — выделить отдельный публичный бакет только под них, а приватные бакеты оставить закрытыми.

### S2. IDOR: временные ссылки и скачивание не проверяют владельца файла — High
**Статус: ❌ Неактуально** — проверка по `Uploaders` сломает пересылку сообщений (пересланный файл не добавляет получателя в `Uploaders`, см. `SendMessageCommandHandler.cs:270-289`). Правильный фикс потребовал бы membership-check через Messages (новый эндпоинт + доп. round-trip) — решено не делать сейчас.

**Файл:** `Backend/BarkFluff.Files/Features/GetTempDownloadUrl/GetTempDownloadUrlCommandHandler.cs:35-66`; смежно `Backend/BarkFluff.Files/Host/FilesApiService.cs:44-55` и `Backend/BarkFluff.Files/Features/DownloadFile/DownloadFileCommandHandler.cs:40-48`
**Проблема:** `GetTempDownloadUrl` принимает произвольный список `FileIds` от любого аутентифицированного пользователя, поднимает файлы `GetFiles(ids)` и для каждого создаёт работающую temp-ссылку — без проверки, что вызывающий есть в `Uploaders` файла или имеет к нему доступ. `DownloadFileCommandHandler` ограничивает прямой доступ по исходному GUID только типами `UserAvatar/ChatPicture/UserProfilePoster` (`:40`), но через temp-ссылку скачивается файл **любого** типа, включая `message-documents`/`message-images`/`message-videos` чужого чата.
**Почему это проблема:** Нарушение object-level авторизации. Пользователь, узнавший GUID вложения (например, из пересланного сообщения, через oracle S3 или из CheckFileHash — см. S3), минтит temp-ссылку и скачивает приватное вложение чата, в котором не состоит. GUID не угадывается, но контроль доступа должен опираться на принадлежность, а не на «знание идентификатора».
**Рекомендация:** В `GetTempDownloadUrl` (и/или при скачивании) проверять, что `UserContext.UserId` входит в `Uploaders` файла либо имеет доступ к ресурсу, к которому файл прикреплён (через сервис Messages). Для приватных типов не выдавать ссылку посторонним.

### S4. Stored XSS: inline-отдача SVG/HTML с управляемым Content-Type — High
**Статус: ⏳ Отложено** — после закрытия S1 (публичный S3-доступ снят) прямой вектор через S3-URL закрыт; HTTP-эндпоинт сервиса и так ставит `Content-Disposition: attachment`. Риск ниже, но не нулевой (inline-превью в будущем). Вернуться позже.

**Файл:** `Backend/BarkFluff.Files/Extensions/FileExtensions.cs:24,35,37` (используется в `DownloadFileCommandHandler.cs:126` и при заливке в S3 `UploadFileCommandHandler.cs:105,312`)
**Проблема:** `GetContentType` отображает `.svg → image/svg+xml`, `.html/.htm → text/html`, `.js → text/javascript`. Этот Content-Type сохраняется в метаданных объекта S3 и возвращается при скачивании. Для типа `MessageAttachmentDocument` детекция содержимого намеренно пропускается (см. S5), поэтому злоумышленник заливает `evil.svg`/`evil.html` со скриптом.
**Почему это проблема:** При публичном чтении из S3 (S1) объект отдаётся inline со стором-Content-Type и без `Content-Disposition: attachment` и без `X-Content-Type-Options: nosniff` — браузер исполнит скрипт в SVG/HTML на домене S3/MinIO (stored XSS, кража токенов/фишинг). Примечание: HTTP-эндпоинт самого сервиса (`FilesController.DownloadFile`, `File(stream, contentType, fileName)`) выставляет `Content-Disposition: attachment` (т.к. задано имя файла) и потому inline-исполнение через сервис смягчено — основной вектор именно прямой S3-доступ и любая будущая inline-отдача.
**Рекомендация:** Закрыть S1. Дополнительно: принудительно ставить `Content-Disposition: attachment` и `X-Content-Type-Options: nosniff` для скачиваемых файлов; для SVG отдавать `text/plain`/`application/octet-stream` либо санитизировать; запретить заливку активных типов (`.svg`, `.html`, `.js`) или нормализовать их Content-Type.

### S3. CheckFileHash: oracle существования контента + захват чужого файла в Uploaders — Medium
**Статус: ❌ Неактуально** — дедупликация by design, риск принят.

**Файл:** `Backend/BarkFluff.Files/Features/CheckFileHash/CheckFileHashCommandHandler.cs:51-63`
**Проблема:** Метод (политика `TokenType.User`) принимает SHA256-хеш от любого пользователя и, если файл с таким хешем существует, возвращает его `FileId` (`:62`) и добавляет вызывающего в список `Uploaders` файла (`:58`, `AddUploaderToFile`).
**Почему это проблема:** Это oracle: зная (или подобрав) содержимое файла, атакующий подтверждает его наличие на платформе и получает `FileId` — а далее скачивает через temp-ссылку (см. S2). Кроме того, тихое добавление вызывающего в `Uploaders` чужого файла искажает учёт владения/квоты и может влиять на логику дедупликации/удаления. Для контента с низкой энтропией (типовые картинки, документы-шаблоны) хеш предсказуем.
**Рекомендация:** Не возвращать `FileId` и не привязывать пользователя к чужому файлу на основании одного знания хеша. Дедупликацию делать серверной (по факту реальной загрузки этим пользователем) либо подтверждать владение иным способом.

### S5. Отсутствует allowlist типов/расширений при загрузке (произвольные и исполняемые файлы) — Medium
**Статус: ❌ Неактуально** — документ должен приниматься как есть, без изменения типа by design.

**Файл:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs:42-50` и `Backend/BarkFluff.Files/Host/FilesController.cs:23-25`
**Проблема:** Детекция по сигнатуре выполняется только для медиа-типов (`TypesRequiringDetection`), а для `MessageAttachmentDocument` намеренно не выполняется — документ принимается с любым содержимым и любым расширением. Серверного allowlist разрешённых расширений/MIME нет; единственное ограничение — размер 512 МБ (`RequestSizeLimit(536_870_912)`).
**Почему это проблема:** Платформа становится хостингом произвольных файлов, включая исполняемые/скриптовые (`.exe`, `.svg`, `.html`, `.js`, `.bat`). В связке с S1/S4 это даёт раздачу вредоносного контента с доверенного домена.
**Рекомендация:** Ввести серверный allowlist расширений и MIME для документов, ограничить размеры по типам, отклонять активные форматы или хранить их с безопасным Content-Type и принудительным attachment.

### S6. Раскрытие внутренних сообщений об ошибках при скачивании — Low
**Статус: ✅ Исправлено** — генерик-сообщение клиенту, детали в лог (`ILogger<FilesController>`).

**Файл:** `Backend/BarkFluff.Files/Host/FilesController.cs:72-75`
**Проблема:** Общий `catch (Exception ex)` возвращает клиенту `NotFound($"Ошибка при скачивании файла: {ex.Message}")`.
**Почему это проблема:** В тело ответа могут попасть детали инфраструктуры (ошибки AWS SDK, имена бакетов, сетевые сообщения), что облегчает разведку.
**Рекомендация:** Возвращать обобщённый текст, детали писать только в лог.

### S7. Анонимный HTTP-эндпоинт загрузки — слот может заполнить кто угодно — Low
**Файл:** `Backend/BarkFluff.Files/Host/FilesController.cs:23-26` (нет `[Authorize]`, в `Program.cs:104` контроллеры мапятся без политики)
**Проблема:** `POST /upload/{uploadId}` анонимен: авторизация держится на том, что GUID-слот создаётся только аутентифицированным `GetUploadUrl`. Но сам аплоад не проверяет личность загружающего — любой, кто узнал `uploadId`, может залить содержимое в чужой слот, пока он пуст (`Etag` не задан).
**Почему это проблема:** Capability-URL модель: утечка upload-URL (логи, прокси, история) позволяет подменить содержимое чужого ожидающего файла.
**Рекомендация:** Признать осознанным выбором или привязать аплоад к идентичности (короткоживущий подписанный токен на конкретный `uploadId`+пользователя).
**Статус: ❌ Неактуально** — HTTP-клиенты не шлют auth-заголовок на upload-эндпоинт, идентичность физически негде проверить без правки протокола/клиентов. Проверены Android, Windows, Mac/iOS (общий пакет `BFNetworking`) — все потребляют upload-URL как opaque capability-токен. Если атакующий перехватывает upload-URL (MITM/логи), он перехватывает и остальной трафик (в т.ч. refresh-токен) — этот вектор не даёт дополнительной поверхности атаки. Риск принят.

### S8. Capability-URL загрузки логируются в Seq — Low
**Файл:** `Backend/BarkFluff.Files/Features/GetUploadUrl/GetUploadUrlCommandHandler.cs:53-58` (Information); смежно `GetTempDownloadUrl/GetTempDownloadUrlCommandHandler.cs:66-79` (Debug)
**Проблема:** Полный upload-URL (содержит секретный GUID-слот) пишется на уровне Information; temp-download URL — на Debug.
**Почему это проблема:** URL — это capability-токен (см. S7). Его попадание в централизованные логи расширяет поверхность утечки.
**Рекомендация:** Логировать только `FileId`/`TempFileId`, не полный URL; либо понизить уровень и маскировать токен.
**Статус: ✅ Исправлено** — добавлен `FileUrlHelper.MaskCapabilityToken(Guid)` (первые 8 hex-символов + `-****`), применён в `GetUploadUrlCommandHandler` (Information) и `GetTempDownloadUrlCommandHandler` (Debug) вместо полного URL/TempFileId.

### S9. ~~Незагруженные upload-слоты не истекают и не очищаются~~ — ~~Medium~~ **Исправлено (2026-07-15)**

**Файлы:** `Domain/UploadFile.cs` (поле `ExpiresAt`); `Features/GetUploadUrl/GetUploadUrlCommandHandler.cs` (TTL 2 часа при создании слота); `Persistence/UploadedFilesStorage.cs` (`DeleteExpiredPendingAsync`); `Services/TempFileCleanupService.cs` (чистит также просроченные pending-слоты, не только `TempFile`); миграция `20260715165101_AddUploadFileExpiresAt`.
**Решение:** У `UploadFile` появилось поле `ExpiresAt` (для уже существующих строк — дефолт `DateTime.MinValue`, что означает мгновенное истечение старых pending-слотов при накатке миграции). Фоновый `TempFileCleanupService` раз в час дополнительно удаляет `UploadedAt == null && ExpiresAt < now`. Rate-limiting на `GetUploadUrl` не добавлен — не требовался пользователем.

## Производительность

### P1. ~~Загрузки до сотен МБ буферизуются целиком в MemoryStream~~ — ~~High~~ **Исправлено (2026-07-15)**

**Файл:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs`
**Решение:** Убран особый случай для изображений/GIF в условии буферизации — порог 100 МБ (диск через `FileStream`) теперь применяется единообразно ко всем типам файлов, а не только к не-графическим. Дальнейший код (SHA256, детекция типа, ImageSharp) работает с `Stream` абстрактно и не зависит от того, диск это или память.

### P3. Многократные полные проходы по содержимому (hash + детекция + повторные декодирования) — Medium
**Файл:** `Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs:144-149, 158, 190, 259`
**Проблема:** Для одной загрузки выполняется: отдельный проход SHA256 (`:144-149`, отмечено TODO), затем детекция типа (`:158`), затем для стикеров — `Image.IdentifyAsync` (`:190`), затем `ProcessImageAllInOneAsync` с полным декодированием (`:259`). То есть изображение декодируется минимум дважды, плюс полный проход хеша и чтение для детекции.
**Почему это проблема:** Лишний CPU/время на горячем пути загрузки; для стикеров — два полных декодирования. `ProcessImageAllInOneAsync` уже объединил часть проходов, но хеш и identify стикеров остались отдельными.
**Рекомендация:** Считать SHA256 во время первичного копирования стрима (как предлагает TODO); размеры стикера брать из единого `ProcessImageAllInOneAsync`, убрав отдельный `IdentifyAsync`.

### P4. Нет индекса под запросы квоты по массиву Uploaders (seq scan) — Medium
**Файл:** `Backend/BarkFluff.Files/Persistence/UploadedFilesStorage.cs:83-89, 94-102`; модель — `Persistence/Migrations/FilesContextModelSnapshot.cs:182-184` (`Uploaders` → `bigint[]`, индекса нет)
**Проблема:** `GetUserStorageUsed`/`GetUserStorageByType` фильтруют `x.Uploaders.Contains(userId)` (PostgreSQL `userId = ANY(uploaders)`) с агрегацией `Sum`. Колонка `Uploaders` — массив `bigint[]` без GIN-индекса; таблица `UploadedFiles` индексирована только по PK `Id`.
**Почему это проблема:** Подсчёт занятого места выполняется последовательным сканированием всей таблицы файлов на каждый вызов `GetUserStorageInfo` — деградирует линейно с ростом числа файлов.
**Рекомендация:** Добавить GIN-индекс на `Uploaders` (`CREATE INDEX ... USING gin (\"Uploaders\")`) или денормализовать суммарную квоту пользователя в отдельную таблицу/счётчик.

### P5. Нет индекса на PreviewId — seq scan на горячем пути скачивания — Medium
**Файл:** `Backend/BarkFluff.Files/Persistence/UploadedFilesStorage.cs:75-78` (`GetFileByPreviewId`), вызывается из `Features/DownloadFile/DownloadFileCommandHandler.cs:103`; модель — `FilesContextModelSnapshot.cs:149-189` (индекса на `PreviewId` нет)
**Проблема:** Скачивание превью идёт через `FirstOrDefaultAsync(x => x.PreviewId == previewId)` по неиндексированной колонке `UploadedFiles.PreviewId`.
**Почему это проблема:** Каждое скачивание, дошедшее до ветки превью, делает полный скан таблицы файлов — это самый горячий путь сервиса (загрузка картинок/аватаров в ленте).
**Рекомендация:** Добавить индекс на `PreviewId` (можно частичный, `WHERE PreviewId IS NOT NULL`).

### P2. Несколько полных копий в памяти в серверных image-хендлерах — Low
**Файл:** `Backend/BarkFluff.Files/Features/UploadAvatarServer/UploadAvatarServerCommandHandler.cs:54-67` (аналогично `UploadPosterServer/...:52-56`, `UploadStickerImage/...:53-57`)
**Проблема:** `request.ImageData.ToByteArray()` → `MemoryStream(rawStream)` → `ProcessAvatarAsync` → `processedBytes` → `MemoryStream(mainStream)` → плюс ещё `MemoryStream(processedBytes)` для превью. Несколько полных копий одного изображения в памяти.
**Почему это проблема:** Лишние аллокации/копирования. Ограничено лимитом gRPC-сообщения 20 МБ, поэтому риск умеренный.
**Рекомендация:** Переиспользовать буферы/потоки, не создавать новый `MemoryStream` под каждый шаг там, где это не нужно.

### P6. Неуникальный индекс на Hash — гонка дедупликации может вставить дубликаты — Low
**Файл:** `Backend/BarkFluff.Files/Persistence/FileHashesStorage.cs:19-23` (`AddHash`); `Persistence/FilesContext.cs:30-31` (индекс без `IsUnique`); проверка-перед-вставкой — `Features/UploadFile/UploadFileCommandHandler.cs:200`
**Проблема:** Дедупликация делает read (`GetFileIdByHash`) затем insert (`AddHash`) без уникального ограничения на `Hash`. Две параллельные загрузки одинакового контента обе пройдут проверку «не найдено» и обе вставят строку с одним хешем.
**Почему это проблема:** Появляются дубликаты в `FileHashes`, дедупликация теряет детерминизм (какой `FileId` вернётся при следующем `GetFileIdByHash` — недетерминировано), растёт таблица.
**Рекомендация:** Сделать индекс на `Hash` уникальным и обрабатывать конфликт вставки (upsert / `ON CONFLICT`), либо вести дедуп через единый ключ.

## Docker / nginx

### D1. master-compose публикует порты Files на хост в обход nginx/TLS — Low
**Файл:** `Backend/docker-compose-master.yml:40-42`
**Проблема:** Сервис `files` мапит `"${FILES_PORT}:${FILES_PORT}"` и `"${FILES_HTTP1PORT}:${FILES_HTTP1PORT}"` на хост. В отличие от `docker-compose-dev.yml` (только внутренняя сеть, без публикации портов), здесь gRPC (7005) и анонимный HTTP-эндпоинт скачивания/загрузки (7006) доступны напрямую, минуя nginx (TLS, таймауты, `client_max_body_size`).
**Почему это проблема:** Прямой доступ к анонимному upload/download без TLS и без ограничений nginx; в связке с S7/P1 — путь для DoS и подмены содержимого слота. Если хост-фаервол не закрывает эти порты — они доступны извне в открытом виде.
**Рекомендация:** Не публиковать порты Files на хост (оставить только внутреннюю сеть, как в dev), доступ — только через nginx. Если публикация нужна для single-server — биндить на `127.0.0.1` и закрывать фаерволом.

### Положительные наблюдения
- Все gRPC-методы покрыты политиками XAuth: `FilesApiService` — `TokenType.User`, `FilesServerApiService` — `TokenType.Service` (`Host/FilesApiService.cs:21`, `Host/FilesServerApiService.cs:28`).
- Идентификаторы файлов и temp-ссылок — случайные `Guid.NewGuid()` (v4), не предсказуемы.
- Ключи объектов S3 — это GUID, не пользовательский ввод; path traversal в ключах/именах нет.
- Скачивание из S3 корректно стримится без буферизации в памяти (`Infrastructure/S3Uploader.cs:39-52` + `S3ObjectStream`), контроллер отдаёт поток (`FilesController.cs:66`).
- Секреты S3 не захардкожены в коде/appsettings/compose (конфиг `S3Buckets` приходит из сервиса Configuration); `SecretKey` не попадает в строковый ключ кэша клиентов (хешируется — `S3BucketRegistry.cs:97-99`).
- Контейнер запускается под非-root (`USER $APP_UID`) на chiseled-образе (`Dockerfile:20-23`).
- `ListStickerPacks` использует пагинацию и одиночный сгруппированный запрос счётчиков (`GetStickerCountsAsync`) — N+1 нет; есть фоновая очистка `TempFile` (`Services/TempFileCleanupService.cs`).
