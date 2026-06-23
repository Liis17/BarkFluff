# План для агента 2: backend/proto для Android V1 звонков, push и история

> Цель: закрыть недостающие backend-контракты для Android V1 звонков: входящие push-события, dismiss-события, история звонков и активные звонки для join-баннера. Агент 2 работает в backend/shared proto/CloudMessaging и только минимально трогает Android V1 для подключения новых контрактов. `Android/Barkfluff.ClientV2.Android` не изменять.

---

## Границы ответственности

### Делать

- Backend events из `BarkFluff.Calls` для входящего звонка и dismiss.
- CloudMessaging consumer и FCM payload `incoming_call` / `dismiss_call`.
- Public RPC истории звонков (`ListCallHistory`) и, если нужно, активных звонков (`GetActiveCalls` или аналог).
- Shared proto + Android core proto sync.
- Минимальный Android V1 wiring: repository methods, models, `CallsFragment` adapter после готового RPC.

### Не делать

- Не менять `Android/Barkfluff.ClientV2.Android`.
- Не заниматься полноценным LiveKit UI, participant grid и screen-share UX. Это зона агента 1.
- Не менять визуальный дизайн `CallActivity`, кроме минимальных интеграционных данных, нужных для новых backend контрактов.

---

## Текущее состояние

- `BarkFluff.Calls` уже реализует сигналинг и LiveKit token flow.
- `SubscribeCallEvents` уже используется Android V1 для foreground stream.
- Android V1 уже умеет принимать FCM `type=incoming_call` и `type=dismiss_call`, но backend должен гарантированно отправлять эти payload для background/killed app.
- `CallsFragment` в Android V1 пока каркас: реальные строки ждут backend `ListCallHistory`.
- Beacon уже отдаёт Calls endpoint и LiveKit URL.

---

## Статус реализации (2026-06-24)

- ✅ **Этап 2** — `IncomingCallPushEvent` / `CallDismissPushEvent` (`Shared/BarkFluff.Shared.Queue/Messages/`), публикация из `CallsService` (initiate + accept/reject/end/timeout/room_finished).
- ✅ **Этап 3** — `IncomingCallPushConsumer` / `CallDismissPushConsumer` + `FirebaseService.SendIncomingCallBatchAsync`/`SendCallDismissBatchAsync`, регистрация в `Program.cs` (`incoming-call-push-handler`, `call-dismiss-push-handler`).
- ✅ **Этап 4** — `ListCallHistory` (фильтр ALL/MISSED, курсор `before_started_at`+limit, has_more) и `GetActiveCalls(chat_ids)` в `calls_api.proto` (shared + Android core) + хендлеры в `CallsService`/`CallsApiService`. ⚠️ Групповая история v1 ограничена звонками, инициированными пользователем (TODO: lookup чатов пользователя).
- ✅ **Этап 5** — `GetActiveCalls` реализован (контракт для join-баннера готов; participant_user_ids пуст в v1).
- ✅ **Этап 6** — `CallRepository.listCallHistory/getActiveCalls`, `CallsFragment` + `CallHistoryAdapter` (`item_call_history.xml`): список истории, фильтр, tap→чат, quick-call. `:app-v1:assembleDebug` зелёный.
- ✅ **Этап 7** — Obsidian обновлён ([[Calls]], [[CloudMessaging]], [[Android]]). Интеграционный QA на устройстве — за пользователем.
- ⚠️ Mac/iOS proto не синхронизированы (вне scope Агента 2).

## Этапы работ

### Этап 1. Аудит backend Calls и CloudMessaging

1. Прочитать:
   - `Obsidian/ClaudeVault/Backend/Calls.md`
   - `Obsidian/ClaudeVault/Backend/CloudMessaging.md`
   - `Backend/BarkFluff.Calls/**`
   - `Backend/Barkfluff.CloudMessaging/**`
   - `Shared/BarkFluff.Proto/calls_api.proto`
   - `Android/core/src/main/proto/calls_api.proto`
2. Найти, где сейчас создаются ring/accepted/rejected/ended/timeout события и где пишется CDR.
3. Проверить существующие RabbitMQ event patterns и naming.

Проверка: коротко описать найденные точки публикации/consuming перед изменениями.

### Этап 2. Push events для входящего звонка

1. Добавить backend event для входящего звонка, например `IncomingCallPushEvent`:
   - `call_id`;
   - `caller_user_id`;
   - `recipient_user_ids`;
   - `chat_id`;
   - `media_type`;
   - `started_at`;
   - optional display fields: `caller_name`, `avatar_url`, `chat_title`.
2. Публиковать event из Calls ring flow после успешного создания/обновления call session.
3. Не отправлять push на устройство, которое само инициировало звонок, если backend уже умеет различать device/session.
4. Добавить dismiss event для accepted/rejected/ended/timeout/busy:
   - `call_id`;
   - `recipient_user_ids`;
   - `reason`;
   - `ended_at`.

Проверка: backend build/test для Calls; лог/тест публикации event.

### Этап 3. CloudMessaging consumer

1. Добавить consumer входящего call event.
2. Сформировать high-priority data-only FCM payload:
   - `type=incoming_call`;
   - `call_id`;
   - `caller_user_id`;
   - `chat_id`;
   - `media_type`;
   - `started_at`;
   - `caller_name`;
   - `avatar_url`;
   - `chat_title`.
3. Добавить consumer dismiss event:
   - `type=dismiss_call`;
   - `call_id`;
   - `reason`.
4. Соблюдать текущие правила CloudMessaging по Firebase token/device filtering.

Проверка: Android V1 получает notification в background/killed app; dismiss убирает notification на других устройствах.

### Этап 4. RPC истории звонков

1. Расширить `calls_api.proto`:
   - `ListCallHistoryRequest`;
   - `ListCallHistoryResponse`;
   - `CallHistoryItem`;
   - enum/filter для `ALL` / `MISSED`.
2. Минимальный состав `CallHistoryItem`:
   - `call_id`;
   - `chat_id`;
   - `peer_user_id`;
   - `is_group`;
   - `media_type`;
   - `direction`;
   - `end_reason`;
   - `started_at`;
   - `answered_at`;
   - `ended_at`;
   - `duration_seconds`;
   - `participant_user_ids`.
3. Реализовать handler в Calls на основе существующих CDR/CallSessions.
4. Добавить пагинацию. Если в проекте есть cursor pattern, использовать его; иначе page/limit по существующему стилю Calls.
5. Обновить `Shared/BarkFluff.Proto`, Android `core/src/main/proto`, Mac/iOS generated flow только если это принято в проекте.

Проверка: backend tests на личный/групповой, missed/rejected/ended, pagination.

### Этап 5. Активные звонки для join-баннера

1. Выбрать контракт:
   - `GetActiveCalls(chat_ids)` в Calls API;
   - или включение active-call info в существующий chat/list endpoint, если так архитектурно лучше.
2. Вернуть минимум:
   - `call_id`;
   - `chat_id`;
   - `media_type`;
   - `started_at`;
   - `participant_user_ids`;
   - `livekit_url` или гарантию, что `JoinCall` вернёт все нужные данные.
3. Android V1 должен иметь возможность показать баннер `Идёт звонок` и вызвать `JoinCall`.

Проверка: два клиента видят активный групповой звонок и late join проходит.

### Этап 6. Android V1 wiring истории

1. В `Android/core` добавить методы `CallRepository.listCallHistory(...)` и, если есть контракт, `getActiveCalls(...)`.
2. В `CallsFragment` заменить empty-only каркас на реальные данные:
   - adapter строк;
   - группировка по датам;
   - фильтр `Все` / `Пропущенные`;
   - быстрые actions audio/video;
   - tap открывает чат.
3. UI-детали держать минимальными и согласовать с агентом 1, если меняется shared component style.

Проверка: `./gradlew.bat :app-v1:assembleDebug`; список показывает завершённые/пропущенные звонки.

### Этап 7. Документация и QA

1. Обновить:
   - `Obsidian/ClaudeVault/Backend/Calls.md`;
   - `Obsidian/ClaudeVault/Backend/CloudMessaging.md`;
   - `Obsidian/ClaudeVault/Shared/Proto.md` или релевантный proto-док;
   - `Obsidian/ClaudeVault/Клиенты/Android.md`, если менялся Android wiring;
   - `docs/plan/Android-Calls-LiveKit-V1.md`.
2. Провести интеграционный QA:
   - foreground stream incoming;
   - background/killed FCM incoming;
   - dismiss after accept/reject/end;
   - call history after missed/rejected/ended;
   - group call late join.

Проверка: backend build/tests + `./gradlew.bat :app-v1:assembleDebug`, если Android touched.

---

## Ожидаемый результат

- Android V1 получает входящие звонки через FCM при background/killed app.
- Dismiss payload гасит лишние уведомления на остальных устройствах.
- `CallsFragment` может строить список звонков из backend RPC, а не из системных сообщений.
- Есть контракт активных групповых звонков для join-баннера.
- V2 не изменён.

---

## Координация с агентом 1

- До изменения payload согласовать поля с Android V1 обработчиком агента 1.
- До реализации списка звонков согласовать DTO `CallHistoryItem`, чтобы Android adapter не делал догадки.
- Агент 2 не занимается grid/LiveKit renderers; если для UI нужны новые event fields, добавить их как отдельный контрактный пункт.
