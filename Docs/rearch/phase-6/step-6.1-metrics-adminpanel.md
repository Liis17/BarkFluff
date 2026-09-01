# Этап 6.1 — Полный реестр метрик Federation + дашборд и операции в AdminPanel

## Цель

Оператор ноды видит состояние федерации и может на него влиять: глубина outbox и dead-letter, здоровье пиров, аномалии (квоты, отказы подписи), потоки presence/typing и скачиваний — плюс кнопки «переотправить», «синхронизировать чат», «заблокировать ноду». Критерий роадмапа: метрики в Seq и на странице «Федерация».

## Контекст

- Плановый набор метрик — [../04-federation-service.md](../04-federation-service.md), раздел «Метрики»; фактический — разбросан по Фазам 1–4 (см. `Obsidian/ClaudeVault/Backend/Federation.md`, раздел «Метрики»).
- Контур метрик платформы: `MetricsCollector` (counters сбрасываются в `SnapshotAndReset`, gauges — нет) → `MetricsReporterService` (лог `ServiceMetrics {@Metrics}` раз в 5 с) → Seq → AdminPanel (`Services/MetricsCollectorService.cs`, `SeqService`, кеш в LiteDB `HourlyServiceMetrics`). Подробный образец описания — `Backend/Onliner-Metrics.md`.
- Страница «Федерация» уже есть (1.7): `Pages/v2/federation.html` (~400 строк, MD3) + `Endpoints/FederationEndpoints.cs` (`/api/federation/status|peers|peers/{server}/block|keys/rotate`) + gRPC-клиент `FederationInternalApi`.
- Ручной retry/sync заявлен в [../04-federation-service.md](../04-federation-service.md) («состояние outbox: глубина, dead-letter, ручной retry/sync чата»); `TriggerChatSync` намечался этапом 2.6 — **проверь фактическое наличие** в `federation_internal_api.proto` и реализации, прежде чем что-то добавлять.

## Изменение 1 — аудит и добор метрик в коде Federation

1. Составить фактический список метрик: `grep` по `_metrics.` в `Backend/BarkFluff.Federation` (Increment/Add/Set) — что есть, где инкрементируется, counter это или gauge.
2. Сверить с планом ([../04](../04-federation-service.md)) и добрать недостающее, **не изобретая лишнего**. Ожидаемый минимум по областям:
   - XFed: `s2s_requests_in`, `s2s_requests_out`, `s2s_signature_failures`, `s2s_clock_skew_rejections`, `s2s_spki_pin_rejections`;
   - discovery: `discovery_lookups.{wellknown|navigator|manual|cache}`, `discovery_failures`, `crosscheck_mismatches`, `wellknown_signature_failures`, gauge `known_servers_active`;
   - доставка: `outbox_pending` (gauge), `outbox_delivered`, `outbox_retry`, `outbox_deadletter.*`, `outbox_enqueued_total`, `events_received.{type}`, `events_duplicate`, `events_rejected.{reason}`, `chatcreated_quota_exceeded.{origin}`;
   - файлы (Фаза 3): `fetchfile_requests{result}`, `fetchfile_bytes_out`, `remote_file_fetches{result}`, `remote_file_bytes_in`, `remote_file_circuit_open`;
   - presence/typing (Фаза 4): gauges `presence_streams_in`/`presence_streams_out`/`presence_interest_uuids`, `presence_events_in/out`, `typing_in{result}`, `typing_out{result}`, `typing_rate_limited.*`.
3. **Gauges требуют регулярного снятия.** Проверить, что каждое gauge-значение действительно обновляется (у `outbox_pending` это делает диспетчер, у `known_servers_active` — peer-refresh). Для тех, что «живут» в in-memory менеджерах (presence-стримы, размер интереса), завести `BackgroundServices/MetricsSnapshotService.cs` по образцу Onliner (интервал 2–5 с) — иначе значения будут нулями между циклами.
4. Ни одна метрика не должна содержать пользовательских данных; `origin`-суффиксы (имя ноды) допустимы — они уже используются в квоте.

## Изменение 2 — реестр метрик в vault

Создать `Obsidian/ClaudeVault/Backend/Federation-Metrics.md` строго по образцу `Backend/Onliner-Metrics.md`:

- как метрики работают (контур `MetricsCollector` → Seq → AdminPanel, разница counters/gauges);
- таблицы по областям (метрика | тип | где инкрементируется | смысл) — **только фактические** метрики, каждая со ссылкой на класс;
- раздел «производные показатели для оператора»: доля ретраев (`outbox_retry / outbox_delivered`), рост dead-letter, доля `events_rejected` к `events_received` (индикатор злонамеренного/сломанного пира), `s2s_signature_failures` (попытки подделки или рассинхрон ключей), `presence_streams_out` против числа пиров, отношение `typing_rate_limited` к `typing_in`;
- ссылка из `Backend/Federation.md` («Полный реестр метрик → …»), как это сделано у Onliner/Users/Beacon.

## Изменение 3 — операционные internal-RPC (то, чего не хватает для действий)

Проверить наличие и добавить в `federation_internal_api.proto` недостающее (добавление RPC обратно-совместимо):

```protobuf
rpc GetOutboxSummary(GetOutboxSummaryRequest) returns (GetOutboxSummaryResponse);
rpc ListDeadLetters(ListDeadLettersRequest) returns (ListDeadLettersResponse);
rpc RetryDeadLetters(RetryDeadLettersRequest) returns (RetryDeadLettersResponse);
rpc TriggerChatSync(TriggerChatSyncRequest) returns (TriggerChatSyncResponse); // если 2.6 его не завёл
```

- `GetOutboxSummary` — глубина Pending/DeadLetter в разрезе `destination`, возраст самой старой Pending-записи (главный индикатор «нода-партнёр лежит»).
- `ListDeadLetters` — постранично: `event_id`, `destination`, `chat_id`, тип события, `attempts`, `last_error`, время. Без `payload` (в нём содержимое переписки — админке оно не нужно).
- `RetryDeadLetters` — вернуть выбранные записи (по `event_id[]` или по `destination`) в `Pending` со сбросом `attempts`/`next_attempt_at`. Идемпотентно: уже доставленную запись не воскрешать.
- `TriggerChatSync(server_name, chat_id?)` — запустить catch-up (механика 2.6). Если RPC уже есть — переиспользовать, не дублировать.

Все — `TokenType.Service`, вызывает только AdminPanel.

## Изменение 4 — AdminPanel: дашборд и действия

`Endpoints/FederationEndpoints.cs` (+ `Pages/v2/federation.html`, только `v2/`, MD3):

1. **Блок «Доставка»**: глубина outbox по нодам, dead-letter, возраст старейшей записи; таблица dead-letter с фильтром по ноде и кнопкой «Повторить» (одну/все по ноде) с confirm.
2. **Блок «Синхронизация чата»**: поле `server_name` + опциональный `chat_id` → `TriggerChatSync`, результат — тост.
3. **Блок «Метрики федерации»**: показатели из существующего Seq-контура (`MetricsCollectorService`/`SeqService` уже собирают `ServiceMetrics` по всем сервисам — добавить `BarkFluff.Federation` в списки сервисов на страницах дашборда/сервисов, если его там нет) — ключевые счётчики за последние часы + графики в стиле `dashboard.html` (Chart.js уже подключён там).
4. **Блок «Аномалии»**: подсветка ненулевых `s2s_signature_failures`, `events_rejected.*`, `chatcreated_quota_exceeded.*` — и рядом кнопка «Заблокировать ноду» (существующий `SetServerBlocked`), как требует [../04](../04-federation-service.md) («однокнопочный блок ноды прямо из алерта»).
5. Мобильная адаптация и тема — по действующим правилам `Pages/v2` (адаптив только внутри `@media`, токены MD3 из `assets/md3.css`).

Никаких новых механизмов авторизации/ролей — страница уже под `TokenAuthMiddleware`.

## Чего НЕ делать

- Не выводить в админку содержимое сообщений/вложений (payload dead-letter, тексты) — это переписка пользователей.
- Не строить свой сборщик метрик и не менять контур `MetricsCollector`/Seq.
- Не трогать устаревшие страницы (`Pages/Redesigned/`, плоские `Pages/*.html`).
- Не добавлять автоблокировку ноды по порогам (решение владельца — блок ручной, 2.5).

## Критерии готовности

1. `Federation-Metrics.md` создан; каждая перечисленная метрика реально существует в коде (проверено grep'ом), каждая существующая — перечислена. Расхождений нет.
2. Добранные метрики и `MetricsSnapshotService` (если понадобился) покрыты юнит-тестами в объёме, принятом в `BarkFluff.Federation.Tests` (для снапшот-сервиса — что gauge выставляется из фактических источников).
3. Новые internal-RPC реализованы и покрыты тестами: `RetryDeadLetters` возвращает записи в Pending и идемпотентен; `ListDeadLetters` не отдаёт payload; `GetOutboxSummary` считает корректно на подготовленном наборе строк.
4. Сборка Federation и AdminPanel — успех; существующие тесты зелёные.
5. Страница «Федерация» содержит четыре блока (Изменение 4), действия работают против internal API (проверка кодом/ручным вызовом эндпоинтов; живой Seq — следующий пункт).
6. **[делает разработчик]** На живой ноде: метрики Federation видны в Seq (фильтр `Application = BarkFluff.Federation`) и на странице «Федерация»; dead-letter-запись реально переотправляется кнопкой; `TriggerChatSync` вызывает catch-up.
7. Obsidian: `Backend/Federation.md` (ссылка на реестр метрик, новые internal-RPC), `Backend/AdminPanel.md` + `AdminPanel-ProjectMap.md` (новые эндпоинты и блоки страницы).
8. Коммит: `feat(rearch-phase6): 6.1 — реестр метрик Federation и операционный дашборд`.
