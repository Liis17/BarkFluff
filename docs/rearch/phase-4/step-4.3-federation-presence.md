# Этап 4.3 — Federation: агрегированный presence-мост (обе стороны)

## Цель

Статусы пересекают границу ноды: Federation(B) держит **один** S2S-стрим на ноду-партнёра и вливает полученные статусы в Onliner(B); Federation(A) отдаёт по такому стриму статусы своих пользователей — только тех, за кем у ноды-подписчика есть право следить, и только с учётом privacy. Обрыв стрима гасит статусы, реконнект их восстанавливает.

Критерии роадмапа 4.3: обрыв стрима → статусы гаснут → реконнект восстанавливает; privacy origin-стороны соблюдён.

## Контекст

- Схема «один агрегированный стрим на пару нод» и почему не «стрим на подписку» — [../07-presence-typing.md](../07-presence-typing.md), раздел «Федеративная схема».
- Обязательная проверка отношений (иначе инструмент слежки) — [../09-problems-open-questions.md](../09-problems-open-questions.md) №42, [../11-plan-review.md](../11-plan-review.md) С-2.
- Privacy фильтрует владелец данных — №27; реализация фильтра живёт в Onliner (`GetLocalPresence`, 4.2), Federation её не дублирует.
- Требуются выполненные 4.1 (`CheckFederatedPresenceAccess`) и 4.2 (`UpsertRemoteStatus`, `GetLocalPresence`, интерес-heartbeat).

Текущее состояние кода (проверено при планировании):

- `Host/FederationS2SApiService.cs` — реализованы `Ping`, `GetServerKeys`, `GetUserProfile`, `DeliverEvents`; `SubscribePresence` и `DeliverTyping` — `Unimplemented`.
- `Services/S2SChannelFactory.GetInvokerAsync(serverName, ct)` — единственный путь исходящих S2S (XFed-подпись + SPKI-пиннинг, кеш `CallInvoker` per-destination, инвалидация при смене endpoint/SPKI).
- `Services/ServerResolver.ResolveAsync(servername, ct)` — discovery + блоклист; `FederationSwitch.IsActive` гейтит все federation-пути.
- XFed-интерсептор покрывает server-streaming (запрос унарный — подпись без доработок).
- gRPC-клиенты Federation: Users, Messages, Navigator. Клиента **Onliner нет** — завести.
- nginx `federation.conf` (1.6): таймауты 3600s под долгоживущие стримы уже выставлены.

## Изменение 1 — proto: internal RPC регистрации интереса

`federation_internal_api.proto` (добавление RPC обратно-совместимо):

```protobuf
rpc SetPresenceInterest(SetPresenceInterestRequest) returns (SetPresenceInterestResponse);

message SetPresenceInterestRequest {
  string instance_id = 1;          // инстанс Onliner (наборы разных инстансов объединяются)
  repeated string user_uuids = 2;  // ПОЛНЫЙ текущий набор этого инстанса (не дельта)
}

message SetPresenceInterestResponse {
  int32 accepted_count = 1;        // сколько uuid принято (после лимита) — для диагностики
}
```

## Изменение 2 — принимающая сторона: реестр интереса и менеджер стримов

**`Services/PresenceInterestRegistry.cs`** (singleton, in-memory):

- `instance_id → (uuids, expiresAt)`; TTL записи — `Federation:PresenceInterestTtlSeconds` (конфиг, дефолт 60 ≈ 3 × интервала репортера из 4.2).
- `GetUnion()` — объединение живых наборов; протухшие записи выбрасываются лениво при чтении.
- Пустой набор от инстанса — валидное состояние (инстанс есть, подписчиков нет).

**Группировка по нодам:** uuid → `server_name` резолвится через Users (`GetUsersByUuid`, батч; в `RemoteUsers` есть `ServerName`). Результат кешировать (in-memory, TTL порядка часа): привязка uuid→нода стабильна by design — смена `ServerName` для известного uuid запрещена ([../09](../09-problems-open-questions.md) №4). Неизвестный uuid (нет в `RemoteUsers`) — пропускать с метрикой, не падать.

**`BackgroundServices/PresenceStreamManager.cs`** — по одному S2S-стриму на ноду:

1. Раз в `Federation:PresenceReconcileSeconds` (дефолт 10) сверяет «желаемое» (union интереса, сгруппированный по нодам, обрезанный лимитом) с «фактическим» (открытые стримы и их наборы).
2. Набор для ноды изменился → **переоткрыть** стрим с новым `SubscribePresenceRequest` (контракт передаёт набор в запросе; control-сообщений в v1 нет — переоткрытие и есть механизм обновления). Чтобы частые изменения не устраивали флаппинг, применять дебаунс: не переоткрывать чаще раза в `Federation:PresenceResubscribeMinSeconds` (дефолт 5).
3. Набор для ноды опустел → закрыть стрим.
4. Перед открытием: `ServerResolver.ResolveAsync` (неизвестна/заблокирована → не открывать), `FederationSwitch.IsActive`, проверка capability `presence` из последнего `Ping`-ответа (Изменение 5) — партнёр без capability не опрашивается, метрика.
5. Входящие `PresenceEvent` → `OnlinerServerApi.UpsertRemoteStatus(user_uuid, status, last_seen)` (service-токен).
6. **Обрыв/ошибка стрима:** все uuid этой ноды → `UpsertRemoteStatus(..., STATUS_TYPE_ID_UNKNOWN, now)` (статусы гаснут корректно, а не «залипают онлайн»), затем реконнект с экспоненциальным backoff (образец — backoff `OutboxDispatcher`/`PeerRefreshBackgroundService`; кап порядка минуты: presence — эфемерен, долго ждать бессмысленно).
7. Лимит per-нода: `Federation:MaxPresenceSubscriptionSize` (дефолт 500) — лишние uuid отбрасываются со счётчиком в метрике (не ошибка: это защита от разрастания, а не отказ).
8. Keepalive — на уровне HTTP/2 (`SocketsHttpHandler.KeepAlivePingDelay`/`KeepAlivePingTimeout` в `S2SChannelFactory`; сверься с актуальной документацией gRPC .NET), **не** прикладным «пустым событием»: в контракте нет keepalive-типа, и выдумывать его нельзя — это ломает сторонние реализации.

Гейт: при `Federation:Enabled = false` менеджер не стартует.

## Изменение 3 — origin-сторона: S2S `SubscribePresence`

Реализация в `FederationS2SApiService` (была `Unimplemented`). XFed уже проверил подпись запроса.

1. `FederationSwitch.IsActive` → иначе `FailedPrecondition` (`FederationNotConfigured`, как в остальных путях).
2. Origin в блоклисте → `PermissionDenied`.
3. Размер `user_uuids` > `Federation:MaxPresenceSubscriptionSize` → `ResourceExhausted` + метрика (не молча обрезать: на этой стороне это уже сигнал злоупотребления).
4. **Проверка отношений:** `Messages.CheckFederatedPresenceAccess(requesting_server = origin, user_uuids)` → работать только с `allowed_user_uuids`. Пустой ответ → стрим открывается и молчит (не `PermissionDenied`: не сообщаем, какие uuid существуют).
5. Резолв `uuid → long userId` через Users (батч; `GetUsersByUuid` — проверь, отдаёт ли он локальный `user_id`, и если нет, добавь поле в ответ, добавление обратно-совместимо). Неизвестные uuid просто выпадают.
6. **Начальный снимок:** `OnlinerServerApi.GetLocalPresence(user_ids)` → отправить `PresenceEvent` по каждому разрешённому uuid (в т.ч. `UNKNOWN` для скрытых privacy — иначе подписчик не поймёт, что данных не будет).
7. **Поток изменений:** подписаться на fan-out `OnlineStatusChangedEvent` (Onliner уже публикует его для межинстансной доставки) — отдельная per-instance очередь Federation (`AutoDelete = true`, `Durable = false`, образец — очереди Onliner). Событие про наблюдаемого пользователя → `GetLocalPresence([userId])` (privacy применит Onliner) → запись в стрим.
   - **Coalescing:** не чаще раза в `Federation:PresenceCoalesceSeconds` (дефолт 5) на пару (пользователь, стрим); в пределах окна отправляется только последнее состояние.
   - Периодический ресинк снимка (например, раз в `Federation:PresenceResyncSeconds`, дефолт 300) — дешёвая страховка от пропущенного fan-out-события; при большом наборе разносить по времени.
8. Стрим живёт до отмены клиентом или падения; при отмене — чистая уборка подписки и счётчиков.
9. Реестр активных входящих стримов (кто, сколько uuid, с какого времени) — нужен метрикам и `GetFederationStatus` (Фаза 6 покажет это в AdminPanel).

## Изменение 4 — инфраструктура

- gRPC-клиент `OnlinerServerApi` в Federation (образец — существующие клиенты Users/Messages) + ключи `OnlinerService:Host/Token` в бакете **`ServiceId.Federation = 15`** (миграция Configuration по образцу; помни правило «ключ в бакете потребителя»).
- Конфигурация Federation (дефолты в `ConfigurationDefaultsPopulator` + документация в [../04-federation-service.md](../04-federation-service.md)): `Federation:MaxPresenceSubscriptionSize`, `Federation:PresenceInterestTtlSeconds`, `Federation:PresenceReconcileSeconds`, `Federation:PresenceResubscribeMinSeconds`, `Federation:PresenceCoalesceSeconds`, `Federation:PresenceResyncSeconds`.

## Изменение 5 — capability `presence` в `Ping`

- В ответ `Ping` добавить `"presence"` в `capabilities` (поле уже есть в контракте 0.4), когда федерация активна.
- Кешировать capabilities пира при resolve/refresh (`KnownServers` — хранить как есть либо в памяти; персистентность не обязательна, но если добавляешь колонку — миграция по образцу).
- Партнёр без `presence` → стрим не открываем, метрика `presence_peer_unsupported`. Это ответ на риск «асимметрия ожиданий» из [../07](../07-presence-typing.md).

## Изменение 6 — метрики

`presence_streams_out` / `presence_streams_in` (gauges), `presence_events_out` / `presence_events_in`, `presence_subscribe_rejected{reason=blocked|limit|not_configured}`, `presence_access_denied_uuids` (сколько uuid отсеял `CheckFederatedPresenceAccess`), `presence_resubscribes`, `presence_stream_errors`, `presence_peer_unsupported`, `presence_interest_uuids` (gauge — размер union).

## Чего НЕ делать

- Не слать presence через outbox и не ретраить события (эфемерность — принцип фазы).
- Не хранить remote-статусы в БД Federation.
- Не реализовывать typing — 4.4 (хотя capability и rate limit там же, не забегать вперёд).
- Не дублировать privacy-правило в Federation (его владелец — Onliner, 4.2).
- Не вводить прикладной keepalive-тип в `PresenceEvent`.

## Критерии готовности

1. Юнит/интеграционные тесты (`Tests/BarkFluff.Federation.Tests`, образец инфраструктуры — `LoopbackS2SServer`, `TestServerCallContext`):
   - `SetPresenceInterest` от двух инстансов объединяется; протухший набор выпадает по TTL;
   - менеджер открывает один стрим на ноду; изменение набора → ровно один re-subscribe (с учётом дебаунса);
   - обрыв стрима → по всем uuid ноды ушёл `UNKNOWN` → после реконнекта снимок восстановил статусы;
   - `SubscribePresence`: заблокированный origin → `PermissionDenied`; превышение лимита → `ResourceExhausted`; uuid без общего чата отсеян `CheckFederatedPresenceAccess` (в стрим не попал);
   - privacy: пользователь, которому Onliner вернул `UNKNOWN`, отдаётся в стрим как `UNKNOWN` (и не «протекает» реальным статусом);
   - coalescing: N изменений статуса за окно → в стрим ушло одно последнее.
2. Сборка Federation, Onliner, Messages, Configuration — успех; существующие тесты Фаз 1–3 зелёные (регрессий в `DeliverEvents`/outbox нет).
3. **[делает разработчик]** Стенд (двух-нодовый, обеим нодам нужны onliner/users/messages): пользователь node1 и пользователь node2 в общем fed-чате → node2 видит статус пользователя node1 в реальном времени; `docker stop federation` на node1 → статус гаснет в `UNKNOWN` (не залипает «онлайн»); старт обратно → статус восстанавливается сам без действий пользователя; смена `OnlineVisibility` на `None` у пользователя node1 → node2 перестаёт видеть его статус.
4. Obsidian: `Backend/Federation.md` — presence-мост (обе стороны), реестр интереса, конфиги, метрики; `Backend/Onliner.md` — упоминание, кто вливает статусы. [../04-federation-service.md](../04-federation-service.md) дополнен ключами конфигурации.
5. Коммит: `feat(rearch-phase4): 4.3 — агрегированный presence-мост между нодами`.
