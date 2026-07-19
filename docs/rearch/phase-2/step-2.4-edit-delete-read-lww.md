# Этап 2.4 — Edit/Delete/Read через федерацию + LWW

## Цель

Правки, удаления и прочтения распространяются между копиями чата и разрешаются детерминированно: LWW по `LastChangeAt` с tie-break, терминальность удаления, `FederatedReadStates` для прочтений remote-стороны.

## Контекст

- LWW-правила, tie-break, терминальность удаления, read receipts: [../05-chat-replication.md](../05-chat-replication.md), разделы «Метка последнего изменения», «Правка / удаление», «Read receipts» — следуй дословно.
- Пробелы валидации (автор == отправитель, clamp метки): там же, «Валидация импортируемых событий» (изначально С-3 из [../11-plan-review.md](../11-plan-review.md)).
- Требуется выполненный 2.3 (импорт, общая валидация, `FederatedMessageEvents`).

## Изменение 1 — миграция: `FederatedReadStates`

```
FederatedReadStates
  ChatId                       uuid
  UserUuid                     uuid
  LastReadFederatedMessageId   uuid NULL
  ReadAt                       timestamptz NOT NULL
  PK (ChatId, UserUuid)
```

## Изменение 2 — LWW-хелпер

Единая функция применения входящего изменения к сообщению:

- `event.origin_ts_ms > local.LastChangeAt` → применить;
- метка меньше → игнорировать, **ответ OK** (устаревшее событие — не ошибка);
- равенство → tie-break лексикографически по `(origin_ts_ms, origin_server, event_id)` — обе ноды выбирают одного победителя;
- **удаление терминально**: сообщение удалено → любые правки игнорируются (OK), независимо от меток;
- clamp метки из будущего — общий хелпер 2.3.

Юнит-тесты — таблица: новее/старше/равно+tie-break обеими сторонами/будущее/правка после удаления/удаление после правки.

## Изменение 3 — `ApplyFederatedEdit` / `ApplyFederatedDelete`

Обработчики (зовёт Federation, маршрутизация уже есть в 2.2):

1. Чат неизвестен → `RETRY:ChatUnknown`; сообщение по `FederatedId` неизвестно → `RETRY:MessageUnknown` (оба — триггеры catch-up в 2.6).
2. **Origin владеет автором правимого сообщения** (закрывает P2-02). Payload `MessageEdited`/`MessageDeleted` **не несёт** идентичность автора — и не должен: любое поле автора в payload атакер-контролируемо (чужая нода солжёт `server_name`). Проверка — локальная у получателя: по `(ChatId, FederatedId)` найти сообщение `M`, взять `M.SenderUuid`, резолвнуть его **домашнюю ноду** (`Users` локально → своя нода; иначе `RemoteUsers[SenderUuid].ServerName`, запиннен при знакомстве) и сверить `homeserver(M.SenderUuid) == x-bf-origin` события. Иначе `REJECTED`. Отклоняет два вектора: (а) чужая нода правит/удаляет сообщение **локального** автора в нашей копии (`homeserver = своя ≠ origin`); (б) партнёрская нода правит сообщение **не своего** пользователя. Проверки `sender_uuid == Message.SenderUuid` **недостаточно** (не ловит вектор «а») и такого поля в payload нет — не полагаться на него.
3. LWW (Изменение 2). Применение: правка — текст + `EditedAt`/`LastChangeAt = origin_ts` (семантика вложений — Фаза 3); удаление — существующий soft-delete + `LastChangeAt`.
4. Обновить `FederatedMessageEvents` (событие-победитель заменяет предыдущее; для удалённого — хранится delete-событие).
5. Опубликовать обычные внутренние события (`MessageEditedEvent`/`MessageDeletedEvent`) → Updates разносит своим клиентам штатно.

## Изменение 4 — `ApplyFederatedRead`

1. Чат неизвестен → `RETRY:ChatUnknown`; `reader_uuid` — remote-участник чата **и** его домашняя нода == origin события (`homeserver(reader_uuid) == x-bf-origin`), иначе `REJECTED` (P2-02: `MessagesReadPayload.reader_uuid` есть, но `server_name` читателя нет — принадлежность резолвится локально, как для edit/delete).
2. Upsert `FederatedReadStates` по `up_to_federated_message_id` (идемпотентно; «прочитано до» — более старое событие не откатывает более новое: сравнивай по `origin_ts` события либо по `SentAt` сообщения).
3. Внутреннее `MessageReadEvent` → Updates.

Выдача прочтений клиентам: найди, где сейчас отдаётся `ReadBy` (массив long), и добавь объединение с `FederatedReadStates` (remote-читатель = uuid). Клиентский рендер — Фаза 5; здесь только данные в proto-ответе.

## Изменение 5 — исходящий путь

Локальные правка/удаление/прочтение в fed-чате → существующие события `MessageEditedEvent`/`MessageDeletedEvent`/`MessageReadEvent` публикуются с заполненными федеративными полями (контракт 2.2, по образцу заполнения в 2.3): `FederatedId`, `LastChangeAt` (= метка правки/удаления), `RemoteParticipants`. Консюмеры Federation (2.2) уже превращают их в outbox-события — проверь маппинг payload'ов `MessageEditedPayload`/`MessageDeletedPayload`/`MessagesReadPayload` и дозаполни, если в 2.2 остались заглушки.

Прочтение: определи существующую точку «пользователь прочитал сообщения» (RPC/хендлер read) — там для fed-чата публикуется расширенное `MessageReadEvent` с `up_to`-семантикой (маппинг в `MessagesReadPayload.up_to_federated_message_id`).

## Чего НЕ делать

- Catch-up при `RETRY:MessageUnknown` — 2.6 (здесь только корректный код ответа).
- Прочтения/правки в нефедеративных чатах — поведение не меняется.
- Историю правок не хранить — только актуальное состояние (модель дока 05).

## Критерии готовности

1. Юнит-тесты LWW-таблицы (Изменение 2) + проверки «автор == отправитель» — зелёные.
2. Стенд, критерий роадмапа: правка сообщения на node1 → текст обновился на node2; удаление на node2 → удалено на node1; прочтение на node2 → на node1 отображается как прочитанное (в данных выдачи).
3. Тест с подменой метки: событие правки со старой меткой (вручную построенное, `EnqueueOutbound` или юнит на импорт-хендлере) — игнорируется, ответ OK, текст не изменился.
4. Правка удалённого сообщения (метка новее удаления) — игнорируется: удаление терминально.
5. Edit от имени чужого автора (sender_uuid ≠ автор) → `REJECTED`, метрика `events_rejected`.
6. Obsidian: `Backend/Messages.md` дополнен (LWW, FederatedReadStates, apply-RPC).
7. Коммит: `feat(rearch-phase2): 2.4 — федеративные edit/delete/read + LWW`.
