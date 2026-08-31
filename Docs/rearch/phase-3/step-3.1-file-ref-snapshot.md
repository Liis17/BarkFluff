# Этап 3.1 — Снапшот `FederatedFileRef` в MessageAttachments + вложения в fed-событиях

## Цель

Вложение из fed-сообщения рендерится на принимающей ноде (имя/размер/тип/preview_id/размеры изображения) **без какого-либо похода на origin** и без вызова локального Files: метаданные хранятся снапшотом в `MessageAttachments`. Исходящие fed-события (`NewMessage`/`MessageEdited`) несут полный `FederatedFileRef` каждого вложения. Это основа всех последующих этапов фазы: снапшот используется проверками доступа (3.2/3.3) и placeholder'ами (3.5).

## Контекст

- Модель «файлы не реплицируются, снапшот метаданных — да»: [../06-files.md](../06-files.md), раздел «Ссылка на файл в федеративном событии».
- Вложения при правке пересоздаются: [../05-chat-replication.md](../05-chat-replication.md), «Правка / удаление».
- Proto готово (0.4): `FederatedFileRef` (federation_api.proto, поля 1–8), `NewMessagePayload.attachments` / `MessageEditedPayload.attachments` (поле 5/4), `FederatedFileRefFlat` + `ImportFederatedMessageRequest.attachments` (messages_api.proto). Проверь, что `ApplyFederatedEditRequest` тоже несёт `attachments` — если нет, добавь (обратно-совместимо) по образцу import-запроса.
- Требуется выполненный 2.3 (импорт-путь, заполнение федеративных полей событий) и 2.4 (edit через федерацию). **Временное поведение 2.3** («вложения не рендерятся, сохранить факт наличия») этот этап заменяет на полный снапшот — временный код из 2.3 удали.

Текущее состояние кода (проверено при планировании):

- `Domain/MessageAttachment.cs` хранит только `Type, FileId, PreviewUrl, FileSize` + forwarded-поля. **Нет** filename, preview_file_id, width/height, OriginServer.
- Filename/PreviewFileId при рендере дотягиваются из Files: `ListMessagesCommandHandler` батчем зовёт `FilesServerApi.GetFilesData`, `Mapping/MessageContentMapping.cs` маппит. `ImageWidth/Height` из `UploadFileInfo` сейчас вообще теряются при маппинге — **не исправляй это заодно** (несвязанный дефект локального рендера; упомяни в коммите, не чини).
- `NewMessageEvent`/`MessageEditedEvent` (Shared/BarkFluff.Shared.Queue) несут федеративный блок 2.2, но не attachments.
- Консюмер `NewMessageFederationConsumer` (Federation) строит `NewMessagePayload` из полей события — сюда добавится маппинг вложений.

## Изменение 1 — миграция MessagesDb: снапшот-колонки `MessageAttachments`

- `OriginServer text NULL` — NULL = локальный файл (существующее поведение), NOT NULL = remote, байты живут на origin-ноде;
- `FileName text NULL` — снапшот имени (только для remote; у локальных filename по-прежнему из Files при рендере);
- `PreviewFileId text NULL`;
- `ImageWidth int NULL`, `ImageHeight int NULL`;
- индекс `IX_MessageAttachments_FileId` (не уникальный) — нужен проверкам доступа 3.2/3.3. Проверь, нет ли уже подходящего индекса.

Помни про баг `dotnet ef migrations add` (правило 5 README). Backfill не нужен: существующие строки — локальные, все новые колонки NULL.

## Изменение 2 — Queue: события несут fed-вложения

В `Shared/BarkFluff.Shared.Queue` (рядом с `FederatedParticipant` из 2.2):

```csharp
public class FederatedFileRefInfo {
    public string OriginServer { get; set; }
    public string FileId { get; set; }          // Guid на origin в строковой форме
    public string? FileName { get; set; }
    public long SizeBytes { get; set; }
    public int AttachmentType { get; set; }     // значения barkfluff.shared.MessageAttachmentType
    public string? PreviewFileId { get; set; }
    public int? ImageWidth { get; set; }
    public int? ImageHeight { get; set; }
}
```

- `NewMessageEvent.FederatedAttachments` и `MessageEditedEvent.FederatedAttachments` (`List<FederatedFileRefInfo>?`) — заполняются Messages **только для fed-чатов**; для edit — всегда полный пересозданный список (null/пусто = вложений нет).
- Только добавление свойств — десериализация старых сообщений очереди не ломается.

## Изменение 3 — исходящий путь (Messages)

- `SendMessageCommandHandler` fed-чата: метаданные вложений уже запрашиваются у Files (`GetFilesData`) — переиспользуй ответ для построения `FederatedFileRefInfo[]` (`origin_server` = свой `Federation:ServerName` из Configuration, ключ читается сервисами с 0.1 — проверь, как его получает Messages в 2.3, и не вводи второй источник). Заполни `FederatedAttachments` события.
- `EditMessageCommandHandler` fed-чата: то же для `MessageEditedEvent` (список пересоздаётся — [../05](../05-chat-replication.md)).
- Локальные `MessageAttachments` при отправке **не меняются**: снапшот-колонки для локальных вложений не заполняем (минимум изменений, рендер локальных — как раньше через Files).

## Изменение 4 — Federation: маппинг в payload

Консюмеры 2.2 (`NewMessageFederationConsumer`, edited-аналог): `FederatedAttachments` события → `repeated FederatedFileRef` в `NewMessagePayload.attachments` / `MessageEditedPayload.attachments` (proto-поля уже есть). Пустой список/NULL → поле не заполняется.

## Изменение 5 — импорт (Messages)

- `ImportFederatedMessage`: `attachments` (`FederatedFileRefFlat`) → строки `MessageAttachments { Type = (MessageAttachmentType)attachment_type, FileId = file_id, OriginServer = origin_server, FileName = filename, FileSize = size_bytes, PreviewFileId = preview_file_id, ImageWidth, ImageHeight, PreviewUrl = NULL }`. Заменяет временное «сохранить факт» из 2.3.
- `ApplyFederatedEdit`: список вложений сообщения **пересоздаётся** из payload (как локальный edit): старые строки удалить, новые вставить. LWW-правила 2.4 не меняются (список применяется только вместе с выигравшей правкой).
- Расширь общую валидацию импорта (хелпер 2.3): вложений ≤ локального лимита (в 05 зафиксировано ≤ 10 — сверь с фактической константой валидатора `SendMessage`); `file_id`/`preview_file_id` парсятся как Guid (пустой preview допустим); `size_bytes` ≥ 0 и ≤ локального лимита размера файла (лимит upload Files, 512 МБ — сверь константу); `attachment_type` ∈ известных значениям enum; `filename` ≤ 255 символов. Невалидное → `REJECTED` (permanent), не RETRY.

## Изменение 6 — выдача (Messages)

- `Mapping/MessageContentMapping.cs`: вложение с `OriginServer != null` → proto `MessageAttachment` собирается **из снапшота** (`file_id`, `file_name`, `attachment_size = FileSize`, `preview_file_id`, `image_width/height`, `type`); `preview_url` не заполняется (URL-контракт — 3.3, клиенты — Фаза 5).
- Батч `GetFilesData` в Files — только для локальных `FileId` (`OriginServer == null`); remote-вложения Files не дёргают вообще. Проверь все хендлеры выдачи, собирающие вложения (`ListMessages`, `ListChats`-превью, `ListChatAttachments`, pin-списки): везде фильтрация батча по `OriginServer == null`.
- `shared.proto`: `MessageAttachment.origin_server = 11` (string; пусто = локальный) — нужно клиентам (Фаза 5) и temp-выдаче (3.3). Только добавление поля, совместимо. Маппинг — туда же.
- Forwarded-вложения (`ForwardedAttachments`): не трогаем. Форвард fed-сообщения в другой чат копирует forwarded-структуру как есть — скачивание таких вложений поддерживается проверкой 3.3 (она ищет и по forwarded).

## Чего НЕ делать

- Скачивание/проксирование байтов, `FetchFile`, temp-ссылки — 3.2/3.3. Аватары — 3.4. Ошибки/placeholder-контракт — 3.5.
- Кеш превью — запрещён решением владельца (README фазы).
- Не менять рендер локальных вложений (в т.ч. не «чинить» потерю width/height локального маппинга — отдельный несвязанный дефект).
- Не трогать файл на origin при delete-событиях (решение фазы в README).

## Критерии готовности

1. Юнит-тесты: маппинг `FederatedFileRefFlat` → `MessageAttachment`; валидации импорта (превышение лимита количества, битый `file_id`, отрицательный `size_bytes`, неизвестный `attachment_type` → REJECTED); рендер fed-вложения из снапшота (mock `GetFilesData` **не вызывается** для remote FileId); edit пересоздаёт список — зелёные; существующие тесты Messages — без регрессий.
2. Стенд, критерий роадмапа: пользователь node1 отправляет fed-сообщение с картинкой (с превью) и документом → на node2 сообщение рендерится с именем/размером/типом/preview_id/размерами — данные из БД node2; ни одного сетевого обращения к node1 (в логах Federation node2 нет `FetchFile`, в логах Files node2 нет `GetFilesData` для этих id).
3. Исходящее событие: `NewMessagePayload`, доставленный на node2, содержит `attachments` с полным снапшотом (интеграционная проверка на стенде или юнит-тест консюмера).
4. Edit: правка fed-сообщения с заменой/удалением вложений отражается на node2 (список пересоздан, LWW 2.4 не сломан — тест 2.4 проходит).
5. Обратная совместимость: локальный чат с вложениями — отправка/выдача/скачивание без изменений поведения.
6. Obsidian: `Backend/Messages.md` (снапшот-колонки, импорт, рендер remote-вложений), `Shared/Queue.md` (`FederatedFileRefInfo`, новые поля событий).
7. Коммит: `feat(rearch-phase3): 3.1 — снапшот метаданных fed-вложений + FederatedFileRef в событиях`.
