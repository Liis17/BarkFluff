# Этап 4.4 — Typing-мост: `DeliverTypingOutbound` → S2S `DeliverTyping` → `InjectRemoteTyping`

## Цель

«Печатает…» доходит до собеседника на другой ноде: Onliner(A) при typing в федеративном чате отдаёт событие Federation(A), тот fire-and-forget шлёт его Federation(B), а Onliner(B) ретранслирует локальным подписчикам чата. Спам-частота режется на обеих сторонах.

Критерий роадмапа 4.4: «печатает…» видно через ноду; спам-частота режется.

## Контекст

- Схема и семантика (fire-and-forget, без ретраев, coalescing 2–3 с) — [../07-presence-typing.md](../07-presence-typing.md), раздел «Typing-индикаторы».
- Валидация на приёме («sender принадлежит origin и состоит в чате») — там же + принцип «нода говорит только за своих» ([../02-trust-and-certs.md](../02-trust-and-certs.md)).
- Rate limit per-origin как защита от дешёвого спама — [../04-federation-service.md](../04-federation-service.md), «Безопасность публичной поверхности».
- Требуются выполненные 4.1 (fed-контекст + `requester_uuid` + uuid-ветка `CheckChatMembership`), 4.2 (`InjectRemoteTyping`), 4.3 (клиент Onliner в Federation, capability-механика, конфиг-паттерн).

Текущее состояние кода (проверено при планировании):

- `Features/SetTypingStatus/SetTypingStatusCommandHandler.cs` (Onliner): проверяет членство через `ChatMembershipFilter` → публикует `TypingChangedEvent` в fan-out. Ни о какой федерации не знает.
- `FederationS2SApiService.DeliverTyping` — `Unimplemented`.
- Образец прикладного лимитера — `Federation/Services/ChatCreatedQuotaLimiter.cs` (Redis-счётчик с окном и TTL).

## Изменение 1 — proto: internal RPC исходящего typing

`federation_internal_api.proto` (добавление обратно-совместимо):

```protobuf
rpc DeliverTypingOutbound(DeliverTypingOutboundRequest) returns (DeliverTypingOutboundResponse);

message DeliverTypingOutboundRequest {
  string chat_id = 1;
  string sender_uuid = 2;                 // UUID печатающего (наш локальный пользователь)
  int32 action = 3;                       // значения barkfluff.onliner.TypingAction
  repeated string destination_servers = 4; // ноды-участники чата (из fed-контекста 4.1)
}

message DeliverTypingOutboundResponse { }
```

## Изменение 2 — Onliner(A): отправка typing в федерацию

В `SetTypingStatusCommandHandler` **после** существующей публикации локального `TypingChangedEvent`:

1. Взять из расширенного ответа `ChatMembershipFilter` (4.1) федеративный контекст чата. Контекста нет (чат локальный) → выйти, ничего не делая — это подавляющее большинство вызовов, лишних аллокаций быть не должно.
2. `requester_uuid` пуст → выйти (у отправителя нет uuid — федерировать нечего).
3. Вызвать `FederationInternalApi.DeliverTypingOutbound(chat_id, requester_uuid, action, destination_servers = уникальные ноды из peers)` — **fire-and-forget**: не ждать результата в критическом пути ответа клиенту, deadline порядка 2 секунд, ошибки → метрика + debug-лог (не warning: недоступность федерации не должна засорять логи на каждый heartbeat).
4. Гейт: если клиент Federation не сконфигурирован (`FederationService:Host` пуст) — ветка не активируется.

Локальное поведение typing (проверка членства, fan-out, «кроме отправителя») не меняется вовсе.

## Изменение 3 — Federation(A): coalescing и отправка

Реализация `DeliverTypingOutbound` в `FederationInternalApiService`:

1. `FederationSwitch.IsActive` → иначе тихий выход (`Ok` с метрикой: для вызывающего это не ошибка).
2. **Coalescing**: не чаще раза в `Federation:TypingCoalesceSeconds` (конфиг, дефолт 2) на ключ `(chat_id, sender_uuid, destination)`; in-memory-словарь с временем последней отправки и ленивой чисткой (persistent-хранилище тут — избыточность). Исключение: `action = CANCELLED` пропускать всегда — иначе индикатор гаснет только по клиентскому таймауту.
3. Для каждой ноды-назначения: `ServerResolver.ResolveAsync` (неизвестна/`Blocked` → пропуск + метрика), проверка capability `typing` (Изменение 6) → S2S `DeliverTyping` через `S2SChannelFactory` с коротким deadline (`Federation:TypingDeadlineMs`, дефолт 2000).
4. **Никаких ретраев и никакого outbox.** Ошибка → метрика `typing_out{result=error}` и всё.
5. Свою ноду в списке назначений игнорировать (симметрично `OutboxWriter`).

## Изменение 4 — Federation(B): приём, валидация, rate limit

Реализация `FederationS2SApi.DeliverTyping` (была `Unimplemented`). XFed уже проверил подпись:

1. `FederationSwitch.IsActive` → `FailedPrecondition`; origin в блоклисте → `PermissionDenied`.
2. **Rate limit per-origin**: Redis-счётчик по образцу `ChatCreatedQuotaLimiter`, окно — минута, ключ `fed:typing:{origin}:{yyyyMMddHHmm}`, лимит `Federation:TypingRateLimitPerOriginPerMinute` (конфиг, дефолт 600 — при coalescing 2 с это ~20 одновременно печатающих пар). Превышение → `ResourceExhausted` + метрика `typing_rate_limited.{origin}` (без алертов — typing дешёвый, всплеск не инцидент).
3. **Валидация авторства**: `sender_uuid` резолвится через Users (`GetUsersByUuid`) — его `server_name` обязан совпадать с origin, иначе `PermissionDenied` + метрика `typing_rejected{reason=author_not_origin}` (правило «нода говорит только за своих»).
4. **Валидация членства**: `Messages.CheckChatMembership(user_uuid = sender_uuid, chat_ids = [chat_id])` (uuid-ветка из 4.1) — чат не вернулся → `PermissionDenied` + метрика `typing_rejected{reason=not_member}`. Так чужая нода не может инжектить набор в чат, к которому не имеет отношения.
5. **Кеш проверок 3–4**: ключ `(origin, sender_uuid, chat_id)` → `allowed`, TTL `Federation:TypingValidationCacheSeconds` (дефолт 30). Typing-heartbeat приходит каждые 4–5 секунд — без кеша каждый порождал бы два внутренних gRPC-вызова. Отрицательный результат кешировать тоже (короче, например половина TTL), иначе спамящая нода бесплатно нагружает Users/Messages.
6. Успех → `OnlinerServerApi.InjectRemoteTyping(chat_id, sender_uuid, action)` → метрика `typing_in{result=ok}`.

## Изменение 5 — конфигурация

Ключи Federation (дефолты в `ConfigurationDefaultsPopulator`, документация — [../04-federation-service.md](../04-federation-service.md)): `Federation:TypingCoalesceSeconds`, `Federation:TypingDeadlineMs`, `Federation:TypingRateLimitPerOriginPerMinute`, `Federation:TypingValidationCacheSeconds`.

## Изменение 6 — capability `typing`

- Добавить `"typing"` в `capabilities` ответа `Ping` (рядом с `"presence"` из 4.3) при активной федерации.
- Отправка typing ноде без capability не производится (метрика `typing_peer_unsupported`) — не тратим вызовы на партнёра, который их всё равно отбросит.

## Чего НЕ делать

- Не ретраить typing, не класть его в outbox, не персистить.
- Не менять локальную typing-механику (relay-модель, гашение по клиентскому таймауту).
- Не рассылать typing в федеративные группы (их нет).
- Не поднимать отдельный стрим для typing — контракт unary by design.

## Критерии готовности

1. Юнит/интеграционные тесты:
   - Onliner: typing в локальном чате → вызова Federation нет; в fed-чате → `DeliverTypingOutbound` с корректными `sender_uuid`/`destination_servers`; недоступность Federation не ломает локальный typing (клиент получает обычный ответ);
   - Federation(A): coalescing душит частые события в пределах окна, `CANCELLED` проходит всегда; заблокированная нода пропускается;
   - Federation(B): валидная доставка → `InjectRemoteTyping`; чужой автор (`server_name != origin`) → `PermissionDenied`; не-участник чата → `PermissionDenied`; превышение лимита → `ResourceExhausted`; кеш валидации срабатывает (повторный heartbeat не вызывает Users/Messages второй раз в пределах TTL);
   - существующие тесты Onliner/Federation зелёные.
2. Сборка Onliner, Federation, Configuration — успех.
3. **[делает разработчик]** Стенд: пользователь node1 печатает в fed-чате → пользователь node2 видит «печатает…», индикатор гаснет по таймауту/`CANCELLED`; искусственный флуд typing с node1 → на node2 виден лимит (метрика `typing_rate_limited`), а обычная переписка при этом не страдает.
4. Obsidian: `Backend/Federation.md` (typing-мост, лимиты, конфиги, метрики), `Backend/Onliner.md` (исходящая ветка typing). [../04-federation-service.md](../04-federation-service.md) дополнен ключами.
5. **Гейт фазы 4 целиком** зафиксирован в отчёте: presence и typing remote-пользователей работают, privacy соблюдён, лимиты действуют; всё, что осталось на разработчика (E2E-пункты), перечислено явно.
6. Коммит: `feat(rearch-phase4): 4.4 — typing через федерацию (coalescing, rate limit, валидация)`.
