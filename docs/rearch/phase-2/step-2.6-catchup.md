# Этап 2.6 — Catch-up: ExportChatEvents, FetchChatHistory, SyncChatStates

## Цель

Нода, пропустившая события (даунтайм дольше окна ретраев, потеря на стыке publish, dead-letter у отправителя), дотягивает историю чата и узнаёт о «тихих дырах» сверкой.

## Контекст

- Catch-up и сверка: [../05-chat-replication.md](../05-chat-replication.md), «Catch-up после даунтайма» — главный раздел.
- Проверка участия ноды (принцип «Guid недостаточно»): [../04-federation-service.md](../04-federation-service.md), FetchChatHistory; риски №14, №37, №39 в [../09](../09-problems-open-questions.md).
- Хранение исходных событий (`FederatedMessageEvents`) заведено в 2.3; решение о подписях catch-up — [README.md](README.md), «Решения фазы».
- Proto: `FetchChatHistory` и `FetchRemoteChatHistory` есть (0.4); `SyncChatStates` добавляется здесь.

## Изменение 1 — Messages: `ExportChatEvents`

Реализация RPC (0.4, `MessagesServerApi`; зовёт только Federation, `TokenType.Service`). Вход: `chat_id`, `since_ts_ms`, `limit`, `requesting_server` (если параметра нет в 0.4-контракте — добавь поле, совместимо).

1. **Проверка участия**: `requesting_server` — нода remote-участника чата; иначе отказ (знание ChatId не даёт переписку).
2. Выборка сообщений чата с `LastChangeAt > since_ts_ms`, сортировка по `LastChangeAt`, лимит + `has_more`-курсор.
3. Каждое сообщение → событие: **чужие** (импортированные, `SenderUuid` remote) — исходные wire-байты из `FederatedMessageEvents` (подпись origin сохранена); **свои** — свежесобранное событие актуального состояния (new/edited/deleted по состоянию сообщения) — подписывает Federation при отдаче (см. Изменение 2). `since_ts_ms = 0` → первым отдаётся `ChatCreated`-эквивалент (данные чата/участников) — иначе приёмник не сможет создать копию.
4. Ответ — актуальное состояние, не журнал промежуточных правок ([../05](../05-chat-replication.md)).

## Изменение 2 — Federation: S2S `FetchChatHistory` + internal `FetchRemoteChatHistory`

- **S2S `FetchChatHistory`** (сервер): XFed/блоклист → `Messages.ExportChatEvents(requesting_server = origin)` → для «своих» несобранных событий проставить подпись (`EventSigner`, 2.2) → ответ. Чужие события отдавать байт-в-байт (подпись origin валидна только над исходными байтами).
- **Internal `FetchRemoteChatHistory`** (клиент-сторона): резолв ноды (1.4) → S2S `FetchChatHistory` → полученные события прогнать через **тот же входящий пайплайн**, что и `DeliverEvents` (2.2: подпись события, «нода говорит только за своих» — с поправкой: для событий чужого origin в истории подпись проверяется ключами их origin, а не ноды-канала; ProcessedEvents-дедуп, маршрутизация в Import/Apply-RPC). LWW и идемпотентность делают импорт истории безопасным при любых повторах.
- Пагинация по `has_more` до конца.

## Изменение 3 — proto + сверка `SyncChatStates`

Новый S2S-RPC в `federation_api.proto` (добавление, совместимо):

```protobuf
  // Сверка состояния общих чатов: пары (chat_id, last_event_ts) для обнаружения дыр
  rpc SyncChatStates(SyncChatStatesRequest) returns (SyncChatStatesResponse);

message SyncChatStatesRequest {
  string cursor = 1;   // пагинация; пусто = сначала
  int32 limit = 2;
}

message SyncChatStatesResponse {
  message ChatState {
    string chat_id = 1;
    int64 last_event_ts_ms = 2;  // max(LastChangeAt) сообщений чата
  }
  repeated ChatState chats = 1;  // только чаты, общие с нодой-запросчиком
  string next_cursor = 2;
}
```

Сервер: чаты, где есть участник ноды-запросчика (запрос в Messages — новый server-RPC `GetFederatedChatStates(server, cursor, limit)`), + XFed/блоклист. Клиент-сторона (`BackgroundService` в Federation): по расписанию (конфиг, дефолт раз в час) и при переходе пира из `Unreachable` в `Active` (рефреш 1.4) — запросить, сравнить с локальными `(chat_id, max(LastChangeAt))`; удалённая метка новее → `FetchRemoteChatHistory(chat, since = локальная метка)`. Неизвестный локально chat_id → catch-up с нуля.

## Изменение 4 — триггеры дыр в реальном времени

- `RETRY:ChatUnknown` (2.3) и `RETRY:MessageUnknown` (2.4): Federation при таком ответе внутреннего вызова — поставить задачу catch-up чата (in-memory очередь + дедуп по chat_id; не чаще раза в N минут на чат), событие остаётся Pending у отправителя и доедет после починки.
- Внимание на петлю: catch-up сам может вернуть `MessageUnknown`-подобные состояния — дедуп и rate-limit обязательны.
- Ручной триггер: internal-RPC `TriggerChatSync(server_name, chat_id?)` (добавь в `federation_internal_api.proto`) — для кнопки «Синхронизировать» в AdminPanel (UI — Фаза 6; сейчас grpcurl).

## Изменение 5 — метрики

`catchup_runs{trigger}` (schedule/reconnect/gap/manual), `catchup_events_imported`, `sync_mismatches`, `export_history_requests`.

## Чего НЕ делать

- Файлы в истории — Фаза 3 (события с вложениями импортируются как в 2.3 — текст).
- UI dead-letter/синхронизации — Фаза 6.
- Не изобретать журнал событий: экспортируется актуальное состояние.

## Критерии готовности

1. Юнит-тесты: проверка участия в `ExportChatEvents` (чужая нода → отказ), пагинация, сборка событий для своих/чужих сообщений; дедуп catch-up-триггеров — зелёные.
2. Стенд, критерий роадмапа: node2 остановлена дольше окна ретраев (укороти `MaxAttempts` конфигом → события в DeadLetter) → поднять node2 → сверка/ручной `TriggerChatSync` → история чата на node2 полная, включая правки/удаления; повторный запуск catch-up ничего не дублирует (LWW + идемпотентность).
3. Тест «тихой дыры»: удалить строку `ProcessedEvents` + сообщение на node2 (или доставить при выключенном Messages node2 до DeadLetter) → плановая сверка находит расхождение → дыра закрыта.
4. `RETRY:MessageUnknown`: правка с node1 сообщения, которого нет на node2 (смоделировать удалением строки) → node2 дотягивает сообщение, затем применяет правку (событие отправителя доезжает ретраем).
5. Obsidian: `Backend/Messages.md` (Export/ChatStates), `Backend/Federation.md` (catch-up, сверка).
6. Коммит: `feat(rearch-phase2): 2.6 — catch-up истории и сверка SyncChatStates`.
