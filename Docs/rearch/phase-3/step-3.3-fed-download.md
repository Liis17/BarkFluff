# Этап 3.3 — Files: temp-выдача fed-ссылок + скачивание со стримингом и Range

## Цель

Клиент ноды B скачивает fed-вложение **через свою ноду** по той же модели, что локальные вложения: авторизованный `GetTempDownloadUrl` выдаёт временную capability-ссылку (с проверкой «пользователь — участник fed-чата с этим вложением»), скачивание по `/download/{tempId}` идёт без auth, со стримингом и поддержкой Range (перемотка видео). Критерий роадмапа: клиент ноды 2 скачивает файл/видео (с перемоткой) с ноды 1 через свою ноду.

## Контекст

- Решение фазы (README, «Решения»): **temp-capability в паритет локальным** — уточняет [../06-files.md](../06-files.md), где для всех файлов был описан прямой маршрут `/download/fed/{server}/{fileId}`. Прямой маршрут остаётся только для аватаров (3.4). Правка 06 — часть этого этапа.
- Транспорт: `FetchRemoteFile`/`FetchFile`/Range — этап 3.2. Снапшот метаданных — 3.1.
- Существующая модель (проверено при планировании): `FilesController.GET /download/{fileId}` → `DownloadFileCommand`; резолв fileId: прямой (только whitelist типов UserAvatar/ChatPicture/UserProfilePoster) → `TempFilesStorage` → бейджи → preview; проверок прав нет — capability-модель; `FilesApi.GetTempDownloadUrl` (TokenType.User) создаёт `TempFile { Id, OriginalFileId, ExpiresAt }`. Range сейчас не поддержан нигде (`File(stream, contentType, fileName)` без `enableRangeProcessing`).
- У Files нет gRPC-клиентов к Messages и Federation — завести по образцу существующего клиента `UsersServerApi` (Program.cs). Конфиг-ключи `MessagesService:Host/Token`, `FederationService:Host/Token` — по паттерну существующих.
- Клиентский контракт (для Фазы 5, здесь только бэкенд): вложение с `origin_server` (поле добавлено в 3.1) → клиент передаёт `{origin_server, file_id}` в `GetTempDownloadUrl` → обычная temp-ссылка. Старые клиенты передадут remote `file_id` как локальный → не найдется → текущее поведение «файл не найден» (читаемая деградация, контракт — 3.5).

## Изменение 1 — Messages: `CheckFedFileUserAccess` (B-сторона, user-level)

Новый server-RPC в `MessagesServerApi` (proto-добавление, совместимо):

```protobuf
rpc CheckFedFileUserAccess(CheckFedFileUserAccessRequest) returns (CheckFedFileUserAccessResponse);
message CheckFedFileUserAccessRequest { int64 user_id = 1; string origin_server = 2; string file_id = 3; }
message CheckFedFileUserAccessResponse {
  bool allowed = 1; string file_name = 2; int64 size_bytes = 3; int32 attachment_type = 4;
}
```

Логика: вложение с `OriginServer == origin_server AND FileId == file_id` в сообщении чата, где `user_id` — участник (`ChatMembers`, активное членство по существующим правилам) → `allowed` + снапшот из колонок 3.1. Искать и в `ForwardedAttachments` (форвард fed-сообщения в локальный чат тоже даёт право: форварднувший пользователь легитимно видел вложение). `ForwardedAttachments` — jsonb: запрос по содержимому, предварительно сузив до сообщений чатов пользователя (индекса на file_id внутри jsonb нет — seq scan по суженному набору приемлем; если по факту выйдет дорого — опиши отклонение в коммите).

## Изменение 2 — Files: `GetTempDownloadUrl` принимает fed-refs

- `files_api.proto`: в запрос `GetTempDownloadUrlRequest` добавить `repeated FedFileRef fed_files` (новое поле + message `FedFileRef { string origin_server = 1; string file_id = 2; }`). Ответ не меняется — temp-ссылки тем же списком/мапой, что сейчас (сохрани семантику соответствия запрос↔ответ).
- Хендлер (TokenType.User): для каждого fed-ref → `Messages.CheckFedFileUserAccess(user, ...)` → `allowed` → создать `TempFile` со ссылкой на remote-файл (Изменение 3) → URL обычный `/download/{tempId}` (через существующий `FileUrlHelper`). `allowed = false` → обработай как существующий код обрабатывает ненайденные локальные file_id (пропуск/ошибка — следуй текущей семантике, не выдумывай новую).
- Миграция `TempFiles`: `OriginServer text NULL` (NULL = локальный temp, как раньше), `FileName text NULL`, `SizeBytes bigint NULL`, `AttachmentType int NULL`. Снапшот нужен на скачивании: отсечение по размеру и `Content-Disposition` без второго похода в Messages.

## Изменение 3 — download: fed-ветка temp-файла

`DownloadFileCommandHandler`: `TempFile` резолвится → `OriginServer != null` → fed-ветка (новый хендлер/ветка, локальный путь не трогаем):

1. `Federation.FetchRemoteFile(origin_server, file_id, range_from, range_to)` (3.2) → стрим чанков.
2. **Range вручную** (`File()` с `enableRangeProcessing` не подходит — стрим не seekable): парсинг заголовка `Range: bytes=a-b` (одиночный диапазон; суффикс `bytes=-N` — от конца, размер известен из `TempFile.SizeBytes`). Нет Range → 200; корректный → 206 + `Content-Range` + `Accept-Ranges: bytes`; неудовлетворимый → 416. Конвертация в `range_from` inclusive / `range_to` exclusive для 3.2.
3. **Отсечение по снапшоту (№44, второй уровень):** суммировать отданные байты; превышение `min(TempFile.SizeBytes, declared total_size)` → оборвать соединение + метрика (Federation уже режет по declared — здесь строже, по снапшоту).
4. `Content-Type` — из первого чанка (origin отдаёт из S3); fallback по `AttachmentType` (маппинг enum→MIME — посмотри, как upload/рендер определяет, иначе `application/octet-stream`). `Content-Disposition` — `TempFile.FileName`, санитизация (CRLF, path traversal — по существующим соглашениям контроллера).
5. Стриминг клиенту **без буферизации на диск/в память**: gRPC chunk-итератор → `Stream`-адаптер или `System.IO.Pipelines` (паттерн на усмотрение исполнителя; требование — bounded buffer и проброс `HttpContext.RequestAborted` в gRPC-вызов). Контроллеру понадобится ручная запись в `Response.Body` для fed-ветки (не хелпер `File()`), чтобы управлять кодом/заголовками.
6. Клиент Federation для Files (новый) + конфиг.

## Изменение 4 — стенд

`Backend/dev-federation-testbed/`: node2 дополнить сервисом **files** (+ его БД) и собственным **minio2** (изоляция S3 нод — как в проде; образец — minio в основном `docker-compose-dev.yml`). Users/messages/updates node2 — уже добавлены в 2.3; если нет — стенд не готов, вернись к 2.3. `seed-peers.sql`/конфиги — по образцу существующих сервисов node2.

## Изменение 5 — правки документов

- [../06-files.md](../06-files.md): раздел «Скачивание remote-файла» — заменить flow прямого маршрута на temp-модель (выдача ссылки с проверкой членства → `/download/{tempId}` → fed-ветка → `FetchRemoteFile`); прямой `/download/fed/{server}/{fileId}` пометить как аватарный (3.4); в таблице проблем закрыть «открытый вопрос кеша превью» — решено: без кеша (решение владельца).
- [../09-problems-open-questions.md](../09-problems-open-questions.md): №19 → **решено** (превью всегда с origin, без кеша; placeholder — 3.5).

## Чего НЕ делать

- Аватары и прямой маршрут `/download/fed/...` — 3.4.
- Карта HTTP-ошибок недоступного origin, circuit breaker, placeholder-контракт — 3.5 (здесь только тривиальный проброс: gRPC-ошибка → 502/503, без классификации).
- Кеш превью/содержимого — запрещён (README фазы). Множественные Range-диапазоны (`multipart/byteranges`) — нет.
- Клиентские правки — Фаза 5 (проверки здесь — curl/grpcurl).

## Критерии готовности

1. Юнит-тесты: `CheckFedFileUserAccess` (участник fed-чата → allowed+снапшот; не участник → denied; локальное вложение с тем же file_id не матчится по `OriginServer`; forwarded-вложение → allowed); temp-выдача (allowed → `TempFile` со снапшотом; denied → семантика ненайденного); парсинг Range (a-b, -N, a-, битый → 416/игнор по выбранному правилу) — зелёные.
2. Стенд, критерий роадмапа: fed-чат node1↔node2 с картинкой и видео → на node2 `GetTempDownloadUrl(fed_files=[...])` (service-вызовом с user-токеном пользователя node2) → temp-ссылки → `curl` скачивает файл целиком (байты = оригиналу в MinIO node1) и диапазоном `Range: bytes=...` → 206, байты диапазона совпадают (перемотка). В логах: Federation обеих нод видит `FetchFile`, S3 node2 не трогается.
3. Негатив: пользователь node2 **вне** fed-чата → `allowed=false`, ссылка не выдаётся; подделанный/просроченный tempId → 404; temp-ссылка пользователя А не даёт доступа сверх capability-модели (как локальные).
4. Отсечение по размеру: подменённый origin-стрим, шлющий больше снапшота → обрыв, метрика (тест с mock Federation).
5. Обратная совместимость: локальные вложения — выдача/скачивание как раньше; `GetTempDownloadUrl` только с `file_ids` (старый клиент) работает без изменений; существующие тесты Files зелёные.
6. Доки 06/09 обновлены (Изменение 5). Obsidian: `Backend/Files.md` (fed-ветка download, temp-модель, Range), `Backend/Messages.md` (`CheckFedFileUserAccess`), `Backend/Federation.md` (проброс стрима используется Files).
7. Коммит: `feat(rearch-phase3): 3.3 — temp-выдача fed-ссылок + скачивание со стримингом и Range`.
