# Этап 3.2 — S2S `FetchFile` + `CheckFileFederationAccess` + internal `FetchRemoteFile`

## Цель

Origin-нода отдаёт файл ноде-партнёру по S2S-стриму — только если file_id фигурирует во вложении федеративного чата, участником которого является запрашивающая нода. Принимающая нода получает внутренний канал `FetchRemoteFile` (Files → Federation → origin). Критерий роадмапа: **нода без общего чата с файлом получает отказ**. Клиентского скачивания здесь ещё нет (3.3) — этап строит транспорт и авторизацию уровня ноды.

## Контекст

- Поток скачивания и авторизация на уровне ноды: [../06-files.md](../06-files.md), «Скачивание remote-файла» и «Права доступа на стороне origin».
- Принцип «Guid недостаточно»: [../05-chat-replication.md](../05-chat-replication.md), «Валидация импортируемых событий» (тот же подход, что у `FetchChatHistory`).
- Защита от превышения размера: [../09-problems-open-questions.md](../09-problems-open-questions.md) №44; rate limit — №20/21.
- Proto готово (0.4): `FederationS2SApi.FetchFile(FetchFileRequest) → stream FetchFileChunk` (`range_from/to`, первый чанк несёт `total_size`/`content_type`), `FederationInternalApi.FetchRemoteFile` (стрим тех же чанков), `MessagesServerApi.CheckFileFederationAccess(file_id, requesting_server) → bool`.
- Требуется выполненный 3.1 (снапшот `OriginServer` в `MessageAttachments`, индекс по `FileId`).

Текущее состояние кода (проверено при планировании):

- `FetchFile`/`FetchRemoteFile` — `Unimplemented` (FederationS2SApiService / FederationInternalApiService).
- Federation имеет gRPC-клиенты только к Users и Navigator; клиенты Messages (появляется в 2.2/2.3) и Files (нет) — завести по образцу существующих (конфиг-ключи `MessagesService:Host/Token`, `FilesService:Host/Token` задекларированы в [../04](../04-federation-service.md), проверь фактическое наличие в Configuration из 0.1/1.1).
- XFed-интерсептор (1.3) уже обрабатывает `ServerStreamingServerMethod` — входящий `FetchFile` покрыт подписью без доработок.
- `IS3Uploader` имеет единственный метод `DownloadAsync(bucket, key)` — без offset/length.
- nginx `federation.conf` (1.6): таймауты 3600s под долгоживущие стримы уже выставлены.

## Изменение 1 — Messages: `CheckFileFederationAccess`

Реализация server-RPC (TokenType.Service, зовёт только Federation своей ноды):

1. Найти вложения с `FileId == request.file_id` **и `OriginServer == null`** — отдаём только свои локальные файлы (файл, пришедший с чужой ноды, мы не реэкспортируем: за ним — на его origin).
2. `allowed = true`, если хотя бы одно такое вложение принадлежит сообщению чата, где `IsFederated AND FederatedStatus = Active` И среди remote-участников (`ChatMembers.UserUuid` + нода из `RemoteUsers`/`ChatMembers.ServerName` — посмотри, где 2.3 хранит server_name remote-участника) есть `requesting_server`.
3. Иначе `allowed = false` (файл неизвестен, только в локальных чатах, чат Rejected/Merged без других Active-чатов с этим файлом, чужая нода — без различения, чтобы не светить существование файла).
4. Аватары здесь **не обслуживаются** (UserAvatar вообще не является MessageAttachment) — ветка аватаров в 3.4.

Юнит-тесты таблицей: файл в fed-чате с этой нодой → true; в fed-чате с другой нодой → false; только в локальном чате → false; несуществующий → false; remote-вложение (`OriginServer != null`) с тем же file_id → false.

## Изменение 2 — Files: `FetchFileStream` (server-streaming RPC) + Range в S3

- `files_api.proto`, сервис `FilesServerApi` (TokenType.Service) — добавление, обратно-совместимо:

```protobuf
rpc FetchFileStream(FetchFileStreamRequest) returns (stream FileStreamChunk);
message FetchFileStreamRequest { string file_id = 1; int64 range_from = 2; int64 range_to = 3; }
message FileStreamChunk { bytes data = 1; int64 total_size = 2; string content_type = 3; string file_name = 4; }
```

- Хендлер: резолв `UploadFile` по оригинальному Guid **без whitelist'а типов** (авторизация уже сделана вызывающим — Federation; service-токен). Файл не найден → `NotFound`. Бакет — по существующему `S3BucketRegistry`.
- Первый чанк: метаданные (`total_size` — фактический размер из S3/БД, `content_type`, `file_name`); далее данные чанками ~256 КБ (константа; держать gRPC-сообщение далеко ниже лимита 4 МБ).
- `IS3Uploader`: перегрузка `DownloadAsync(bucket, key, long? offset, long? length)` — AWS SDK `GetObjectRequest.ByteRange` (сверься с актуальной документацией AWS SDK через Context7; MinIO/Cloudflare R2 диапазоны поддерживают). `range_from` inclusive, `range_to` exclusive; 0/0 = весь файл.
- Стримить без буферизации всего файла в память; отмена по `CancellationToken` вызова.

## Изменение 3 — Federation (origin-сторона): S2S `FetchFile`

Реализация в `FederationS2SApiService` (была `Unimplemented`). XFed уже проверил подпись запроса (1.3). Порядок:

1. Origin не в блоклисте (как в остальных S2S-обработчиках 1.3/1.4) → иначе `PermissionDenied`.
2. Прикладной rate limit per-origin для `FetchFile` — отдельный бакет, строже общего (конфиг `Federation:FetchFileRateLimitPerOrigin`, разумный дефолт, например 30 запросов/мин; образец — квота `ChatCreated` per-origin из 2.5) → превышение: `ResourceExhausted` + метрика.
3. `Messages.CheckFileFederationAccess(file_id, origin)` → `false` → `PermissionDenied` **до начала стрима**.
4. `Files.FetchFileStream(file_id, range_from, range_to)` → маппинг чанков в `FetchFileChunk` → в ответный стрим. Проброс `CancellationToken` клиента; отмена/ошибка mid-stream → стрим рвётся (принимающая сторона обработает в 3.5).
5. Метрики: `fetchfile_requests{result}` (ok/denied/rate_limited/error), `fetchfile_bytes_out` (счётчик отданных байт).

Аватарная ветка этого RPC — **3.4**, здесь не делать (пока аватар → `CheckFileFederationAccess` вернёт false → denied; приемлемо до 3.4).

## Изменение 4 — Federation (принимающая сторона): internal `FetchRemoteFile`

Реализация в `FederationInternalApiService` (TokenType.Service, вызывает Files своей ноды — 3.3):

1. `ServerResolver` (1.4): server неизвестен/blocked → `PermissionDenied`; discovery — обычный путь.
2. S2S-вызов `FetchFile` через `S2SChannelFactory` (1.3) → перекладывать чанки в ответный стрим вызывающему.
3. **Deadline:** gRPC-deadline действует на весь вызов, поэтому для долгих стримов его **не ставить**; установление соединения контролируется connect-timeout'ом канала (проверь конфигурацию `SocketsHttpHandler` в `S2SChannelFactory`; если connect-timeout не задан — задай, конфиг `Federation:S2SConnectTimeout`, дефолт 10 с). Idle-надзор: linked `CancellationTokenSource`, перезаряжаемый при каждом полученном чанке (конфиг `Federation:RemoteFileIdleTimeout`, дефолт 60 с) → отмена стрима при молчании origin.
4. **Защита от превышения (№44, первый уровень):** первый чанк несёт `total_size` — запомнить; сумма фактически полученных байт превысила заявленный `total_size` → оборвать стрим ошибкой + метрика `remote_file_size_mismatch`. Точная сверка со снапшотом — в 3.3 (там есть данные).
5. Маппинг ошибок origin: `PermissionDenied`/`NotFound` → пробросить как есть; сеть/таймаут/`Unavailable` → `Unavailable` (3.5 мапит на HTTP). Метрики: `remote_file_fetches{server,result}`, `remote_file_bytes_in`.

## Изменение 5 — конфигурация

Ключи в Configuration для `ServiceId.Federation` (добавление, с дефолтами — по существующему паттерну): `Federation:FetchFileRateLimitPerOrigin`, `Federation:S2SConnectTimeout`, `Federation:RemoteFileIdleTimeout`. Задокументируй в [../04](../04-federation-service.md) (таблица конфигурации).

## Чего НЕ делать

- REST-маршрут, temp-ссылки, проверка на уровне пользователя — 3.3.
- Аватары (ветка `UserAvatar` в `FetchFile`) — 3.4.
- UX-ошибки клиенту, circuit breaker, HTTP-коды — 3.5.
- Кеш содержимого/превью — запрещён (README фазы).

## Критерии готовности

1. Юнит/интеграционные тесты: таблица `CheckFileFederationAccess` (Изменение 1); маппинг ошибок `FetchRemoteFile`; обрыв при превышении заявленного `total_size` — зелёные.
2. Стенд, критерий роадмапа: node2 запрашивает файл, вложенный в fed-чат между нодами → стрим приходит, байты совпадают с исходным в MinIO node1; node2 запрашивает файл из **локального** чата node1 (node2 не участвует) → `PermissionDenied` до стрима. Проверку делать вызовом `FederationInternalApi.FetchRemoteFile` на node2 (grpcurl с service-токеном) или тестовым хендлером — до появления клиентского пути в 3.3.
3. Range: `FetchFile` с `range_from/to` отдаёт ровно запрошенный диапазон (побайтовая сверка); `total_size` в первом чанке = полный размер объекта.
4. Rate limit per-origin: превышение → `ResourceExhausted`, метрика `fetchfile_requests{result=rate_limited}`.
5. Обратная совместимость: существующие S2S-RPC и внутренние API Federation без регрессий (тесты Фазы 1–2 зелёные).
6. Док 04 дополнен ключами конфигурации. Obsidian: `Backend/Federation.md` (`FetchFile`/`FetchRemoteFile`, метрики), `Backend/Files.md` (`FetchFileStream`, Range в S3), `Backend/Messages.md` (`CheckFileFederationAccess`).
7. Коммит: `feat(rearch-phase3): 3.2 — S2S FetchFile + CheckFileFederationAccess + FetchRemoteFile`.
