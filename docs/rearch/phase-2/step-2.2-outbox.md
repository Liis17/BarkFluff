# Этап 2.2 — Federation: outbox, ProcessedEvents, консюмеры, пайплайн DeliverEvents

## Цель

Надёжная доставка федеративных событий: таблица outbox с диспетчером (at-least-once, упорядочивание per-(destination, chat), backoff, dead-letter, janitor), идемпотентность входящих (`ProcessedEvents`), консюмеры RabbitMQ внутренних событий → outbox, серверный пайплайн `DeliverEvents` с per-event классификацией ответов.

## Контекст

- Outbox-схема, поток входящего события, классификация retryable/permanent: [../04-federation-service.md](../04-federation-service.md) — главный документ этапа.
- Упорядочивание и LWW-основа: [../05-chat-replication.md](../05-chat-replication.md).
- Риски: №15 (порядок), №37 (потеря на стыке publish), №38 (head-of-line blocking) в [../09-problems-open-questions.md](../09-problems-open-questions.md).
- Конверт `FederationEvent` и `EventStatus` — уже в `federation_api.proto` (0.4).

## Изменение 1 — миграция FederationDb

```
FederationOutbox
  Id             bigserial PK
  Destination    text NOT NULL          -- server_name
  ChatId         uuid NULL              -- для упорядочивания; NULL = вне-чатовое (профильные, 2.9)
  EventId        uuid NOT NULL
  EventType      text NOT NULL
  PayloadBytes   bytea NOT NULL         -- сериализованный FederationEvent (уже подписанный)
  CreatedAt      timestamptz NOT NULL
  Attempts       int NOT NULL DEFAULT 0
  NextAttemptAt  timestamptz NOT NULL
  Status         int NOT NULL           -- enum Pending | Delivered | DeadLetter
  LastError      text NULL

ProcessedEvents
  EventId        uuid PK
  OriginServer   text NOT NULL
  ReceivedAt     timestamptz NOT NULL
```

Индексы: `(Status, NextAttemptAt)`, `(Destination, ChatId, Id)`. Колонка `ChatId` отсутствовала в схеме [../04](../04-federation-service.md) — она нужна для упорядочивания; допиши её в док 04 (одной строкой).

## Изменение 2 — расширение `Shared/BarkFluff.Shared.Queue`

События `NewMessageEvent`, `MessageEditedEvent`, `MessageDeletedEvent`, `MessageReadEvent` (файлы в `Shared/BarkFluff.Shared.Queue/Messages/`; обрати внимание: файл `MessageReadedEvent.cs` содержит класс `MessageReadEvent`) — добавить nullable-поля федеративного контекста:

- `bool IsFederated`
- `List<FederatedParticipant> RemoteParticipants` — `{ Guid Uuid, string ServerName }`
- `Guid? FederatedId`, `Guid? SenderUuid`, `DateTimeOffset? LastChangeAt`
- для `NewMessageEvent` дополнительно: `bool IsFirstMessageInChat` + uuid'ы инициатора/приглашённого — чтобы консюмер мог построить `ChatCreated` без похода в Messages
- отображаемое имя отправителя (`SenderDisplayName`, `SenderFid`) — понадобится 2.8; заведи поля сразу

Только добавление свойств (десериализация старых сообщений очереди не ломается). Заполняет их Messages в 2.3 — в этом этапе поля пустые.

## Изменение 3 — подпись события (`origin_signature`)

Хелпер `Services/EventSigner.cs`: подписываются **wire-байты `FederationEvent`, сериализованного с пустыми `origin_signature`/`origin_key_id`**; затем поля проставляются. Проверка: получатель копирует событие, очищает оба поля, пере-сериализует, проверяет подпись ключом `origin_key_id` (ключи origin — из KnownServerKeys, 1.4). Подпись/проверка — Ed25519-инфраструктура из 1.2/1.3.

Допиши в [../02-trust-and-certs.md](../02-trust-and-certs.md) (раздел про подпись событий) абзац: схема канонизации = сериализация с очищенными полями подписи, поля в порядке возрастания номеров; C#-реализация protobuf это гарантирует, требование войдёт в спецификацию протокола (Фаза 6).

Юнит-тесты: подпись→проверка, отказ при изменённом payload, отказ при чужом ключе.

## Изменение 4 — консюмеры RabbitMQ → outbox

MassTransit-консюмеры в Federation (очереди из [../04](../04-federation-service.md), поверхность 3): `new-messages-federation-handler`, `messages-edited-federation-handler`, `messages-deleted-federation-handler`, `read-receipts-federation-handler`. Образец подключения консюмеров — существующие сервисы (Updates/CloudMessaging).

Логика каждого: `IsFederated == false` или `Federation:Enabled == false` → игнор. Иначе: построить payload (`NewMessagePayload` и т.д. из полей события), обернуть в `FederationEvent` (`event_id = новый uuid`, `origin_server = Federation:ServerName`, `origin_ts_ms = LastChangeAt` события), подписать (Изменение 3), вставить строку outbox **для каждой ноды** из `RemoteParticipants` (для DM — одна). Для `NewMessageEvent` с `IsFirstMessageInChat` — две строки: `ChatCreated`, затем `NewMessage` (порядок по возрастанию Id обеспечит доставку в этом порядке).

Консюмер `session-revoked` для Federation — проверь: стандартная инвалидация XAuth должна была появиться в каркасе 1.1; если нет — добавь по образцу других сервисов.

## Изменение 5 — диспетчер outbox

`BackgroundService` (например `Services/OutboxDispatcher.cs`):

- цикл: выбрать Pending с `NextAttemptAt <= now`, сгруппировать по `Destination`;
- **упорядочивание**: в батч попадает событие чата только если у чата нет более раннего (меньший Id) недоставленного события; события с `ChatId = NULL` — без ограничений; между чатами — независимо;
- батч-лимиты: ≤ 100 событий и ≤ 1 МБ на вызов `DeliverEvents` (константы/конфиг);
- вызов S2S `DeliverEvents` подписанным клиентом (1.3) через `ServerResolver` (1.4);
- обработка ответа per-event: `OK`/`ALREADY_PROCESSED` → Delivered; `REJECTED` → **DeadLetter немедленно** (permanent, очередь чата продолжает ехать; `LastError = error_code`); `RETRY` → backoff; транспортная ошибка (нода недоступна) → backoff **всем** событиям этого Destination;
- backoff: 30s → 2m → 10m → 1h → 6h (далее кап 6h); `MaxAttempts`/окно из конфига (дефолт — эквивалент 7 суток) → DeadLetter + метрика;
- DeadLetter по privacy-отказу дополнительно публикует `FederatedChatRejectedEvent` — заглушку оставь, реализация в 2.5.

Janitor (второй `BackgroundService` или таймер в том же): Delivered старше 7 дней — удалять; `ProcessedEvents` старше 14 дней — удалять (окно > максимального окна ретраев).

## Изменение 6 — серверный пайплайн `DeliverEvents`

Реализация `FederationS2SApi.DeliverEvents` (была `Unimplemented`). Для каждого события батча:

1. `origin_server` события == `x-bf-origin` подписи (XFed уже проверил подпись запроса) — нет → `REJECTED`.
2. `ProcessedEvents` содержит `event_id` → `ALREADY_PROCESSED`.
3. Проверка `origin_signature` события (Изменение 3) ключами origin — невалидна → `REJECTED`.
4. «Нода говорит только за своих»: uuid/server_name автора внутри payload принадлежит origin — нет → `REJECTED`.
5. Маршрутизация по типу payload → внутренний вызов (Messages/Users). **В этом этапе** обработчики чатовых payload'ов возвращают `RETRY` с ошибкой `NotImplementedYet` (импорт — 2.3); каркас маршрутизации и коды готовы.
6. Успех внутреннего вызова → запись `ProcessedEvents` + `OK`. Внутренний вызов упал транзиентно → `RETRY` (без записи в ProcessedEvents!).

Реализуй также `FederationInternalApi.EnqueueOutbound` (прямая постановка в outbox) — понадобится 2.9 и ручным тестам.

## Изменение 7 — метрики

`outbox_pending` (gauge), `outbox_delivered`, `outbox_deadletter{reason}`, `outbox_dispatch_errors`, `events_received{type}`, `events_duplicate`, `events_rejected{reason}`. Gauge — по образцу решения Onliner (`MetricsSnapshotService`), не псевдо-gauge.

## Чего НЕ делать

- Импорт-RPC Messages и заполнение расширенных полей событий — 2.3.
- `FederatedChatRejectedEvent`-логика — 2.5 (здесь только заглушка-точка).
- Catch-up/`SyncChatStates` — 2.6.
- Профильные консюмеры (`UserChanged*`) — 2.9.

## Критерии готовности

1. Юнит-тесты: подпись/проверка события; диспетчер — упорядочивание per-chat (событие N+1 не уходит при недоставленном N; независимые чаты едут параллельно), классификация OK/REJECTED/RETRY/транспорт, backoff-прогрессия, DeadLetter по MaxAttempts; дедуп по `ProcessedEvents` — зелёные.
2. Стенд: вручную опубликованный в RabbitMQ node1 расширенный `NewMessageEvent` (`IsFederated=true`, RemoteParticipants=[node2]) → строка в outbox → диспетчер доставил в node2 → node2 ответил `RETRY:NotImplementedYet` → событие осталось Pending, `Attempts` растёт по backoff. (Полный E2E-критерий роадмапа — «остановить node2 → N сообщений → поднять → дошли один раз, порядок сохранён» — прогоняется в 2.3, когда появится импорт.)
3. Невалидная `origin_signature` события (подмена байта в тесте) → `REJECTED`, метрика `events_rejected`.
4. Janitor чистит Delivered/ProcessedEvents по TTL (проверка на укороченном конфиге).
5. Док 04 дополнен колонкой `ChatId`; док 02 — схемой канонизации подписи события.
6. Obsidian: `Backend/Federation.md` (outbox, консюмеры, пайплайн), `Shared/Queue.md` (новые поля).
7. Коммит: `feat(rearch-phase2): 2.2 — outbox, ProcessedEvents, консюмеры, DeliverEvents`.
