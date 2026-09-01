# Этап 4.2 — Onliner: uuid-ветка статусов, приём remote-presence и remote-typing

## Цель

Onliner начинает работать с remote-пользователями, у которых нет `long userId`: хранит их статусы (полученные от Federation), отдаёт локальным подписчикам через **существующие** стримы, ретранслирует их typing и сообщает Federation, за кем сейчас реально следят. Критерий роадмапа 4.2: статус remote-пользователя виден локальному подписчику; remote-статусы исключены из персиста.

Сетевого федеративного кода здесь нет — Onliner общается только со своей нодой; S2S-мост — 4.3/4.4.

## Контекст

- Модель — [../07-presence-typing.md](../07-presence-typing.md) (учитывая поправку README фазы: presence живёт в Redis, а не в памяти инстанса).
- Эвикция uuid без подписчиков обязательна ([../11-plan-review.md](../11-plan-review.md), И-7) — здесь она решена TTL-ключами, а не фоновой чисткой.
- Требуется выполненный 4.1 (расширенный `ChatMembershipFilter` уже протягивает федеративный контекст).

Текущее состояние кода (проверено при планировании):

- `Services/RedisPresenceStore.cs` — sorted set `onliner:presence` (member = `long userId`, score = unix ms). `MarkOnlineAsync`/`SetOfflineAsync`/`GetOnlineAsync`/`GetStaleUsersAsync`/`GetOnlineSnapshotAsync`/`GetOnlineCountAsync`.
- `Services/OnlineStatusSubscriptionsManager.cs` — прямой индекс `subscriberId → (connectionId → SubscriptionData)`, обратный `trackedUserId → (connectionId → Stream)`; `RegisterSubscription(long, List<long>, stream)`, `GetStreamsTrackingUser(long)`, `UpdateAllSubscriptions(long, List<long>)`.
- `Services/TypingSubscriptionsManager.cs` + `TypingNotifier` — ключ `chatId` (строка), рассылка всем кроме отправителя.
- Межинстансная доставка: `Messages/OnlineStatusChangedEvent.cs { long UserId; int Status; DateTime LastSeen; }` и `Messages/TypingChangedEvent.cs { string ChatId; long UserId; int Action; }`, публикуются хендлерами, потребляются `Consumers/OnlineStatusChangedConsumer.cs` / `TypingChangedConsumer.cs` (очереди per-instance, `AutoDelete = true`, `Durable = false`).
- `BackgroundServices/OfflineDetectionService.cs` (single-runner, гасит записи старше 5 с), `DatabasePersistenceService.cs` (раз в 10 мин пишет снимок в PostgreSQL), `MetricsSnapshotService.cs`.
- `Host/OnlinerApiService.cs` — 7 клиентских RPC (`TokenType.User`). Сервис `OnlinerServerApi` из proto **не реализован** (контракт с 0.4).
- gRPC-клиенты Onliner: Users (`UsersService:Host/Token`), Messages (`MessagesService:Host/Token`). Клиента Federation нет.

## Изменение 1 — хранилище remote-статусов (Redis, отдельно от локального)

Новый сервис (`Services/IRemotePresenceStore.cs` + `RedisRemotePresenceStore.cs`), по стилю `RedisPresenceStore`:

- Ключ на пользователя: `onliner:presence:remote:{uuid}`, значение — статус + `last_seen` (простая строка или JSON, на твой вкус, но фиксированный формат).
- `UpsertAsync(Guid uuid, StatusTypeId status, DateTime lastSeen)` — `SET` с TTL `Onliner:RemotePresenceTtlSeconds` (конфиг, дефолт 900). Каждое обновление продлевает TTL.
- `GetAsync(Guid uuid)`, `GetManyAsync(IReadOnlyCollection<Guid> uuids)` — батчем (`MGET`/pipeline), отсутствие ключа = `STATUS_TYPE_ID_UNKNOWN` («нода-партнёр статус не отдаёт»), **не** Offline.
- `RemoveAsync(Guid uuid)` — для явного гашения (4.3 при обрыве S2S-стрима шлёт `UNKNOWN`; метод нужен как явный путь очистки).

**Почему отдельный ключ, а не `onliner:presence`:** sorted set обслуживает `OfflineDetectionService`, гасящий записи через 5 секунд без heartbeat. У remote-пользователей heartbeат'ов нет — они попали бы в offline мгновенно. TTL-ключи заодно дают эвикцию (И-7) без фонового сервиса.

## Изменение 2 — подписки по uuid

`OnlineStatusSubscriptionsManager`:

- Второй обратный индекс `trackedUuid → (connectionId → Stream)` рядом с существующим по `userId` (структура и потокобезопасность — копия имеющейся).
- `RegisterSubscription` и `UpdateAllSubscriptions` дополнительно принимают список uuid; `SubscriptionData` хранит оба набора.
- Новый `GetStreamsTrackingUuid(Guid uuid)` — симметрично `GetStreamsTrackingUser`.
- Новый `GetTrackedUuids()` — снимок всех отслеживаемых uuid этого инстанса (нужен Изменению 6).
- Снятие подписки чистит оба индекса.

## Изменение 3 — клиентские RPC: приём uuid

- `SubscribeToOnlineStatus` / `ChangeUsersInSubscription` — поля `user_uuids` уже есть в proto (0.4). Хендлеры: парсинг uuid (невалидный — игнорировать конкретный элемент, не валить весь вызов), регистрация в uuid-индексе, **начальный снимок** статусов из `IRemotePresenceStore.GetManyAsync` отправляется в стрим сразу после подписки (как для локальных).
- `GetOnlineStatus` — добавить в proto `repeated string user_uuids = 2;` в `GetOnlineStatusRequest` (в 0.4 расширили только Subscribe/Change; без этого клиент не может запросить статус remote-собеседника разово). Ответ уже умеет `user_uuid` в `UserOnlineStatus`.
- **Privacy к remote не применяется**: `OnlineVisibilityFilter` работает только по локальным `user_ids`. Origin-нода уже отфильтровала своё ([../09-problems-open-questions.md](../09-problems-open-questions.md) №27) — повторная фильтрация невозможна (у нас нет их настроек) и не нужна.
- Лимит числа uuid в подписке — по образцу существующих лимитов на `user_ids` (если их нет, ввести константу, например 500, и вернуть `InvalidArgument` при превышении: подписка — вектор ресурсного злоупотребления).

## Изменение 4 — `OnlinerServerApi` (новый gRPC-сервис, `TokenType.Service`)

Реализовать оба RPC из контракта 0.4 (`Host/OnlinerServerApiService.cs`, регистрация в `Program.cs` рядом с `OnlinerApiService`):

- **`UpsertRemoteStatus(user_uuid, status, last_seen)`** → `IRemotePresenceStore.UpsertAsync` → публикация `OnlineStatusChangedEvent` (fan-out) → консюмер на каждом инстансе → `OnlineStatusNotifier` рассылает `UserOnlineStatus` в стримы, отслеживающие этот uuid.
- **`InjectRemoteTyping(chat_id, user_uuid, action)`** → публикация `TypingChangedEvent` (fan-out) → `TypingNotifier` ретранслирует `TypingEvent` подписчикам чата. Исключение «не слать автору» здесь неприменимо (автор — на чужой ноде): слать всем локальным подписчикам чата.
- **`GetLocalPresence(user_ids[]) → repeated UserOnlineStatus`** — новый RPC (добавление в `OnlinerServerApi`, обратно-совместимо). Нужен origin-стороне presence-моста (4.3): Federation спрашивает статусы **своих** пользователей, за которыми следит нода-партнёр. Ключевое: **privacy применяется здесь**, в Onliner (переиспользовать `OnlineVisibilityFilter.IsVisibleToCaller` — правило «`OnlineVisibility != All` ⇒ скрыт», `Friends` === `None` до появления сервиса отношений); скрытому пользователю отдаётся `STATUS_TYPE_ID_UNKNOWN`, а не его настоящий статус. Так инвариант «данные фильтрует владелец» остаётся в одном месте, и Federation не заводит копию privacy-логики.
- Валидацию «отправитель имеет право» здесь **не** делать: вызывающий — Federation своей ноды с service-токеном, а проверки origin/членства выполняются в 4.4 до вызова.

## Изменение 5 — расширение внутренних событий и нотифаеров

- `Messages/OnlineStatusChangedEvent` → добавить `Guid? UserUuid`; `Messages/TypingChangedEvent` → добавить `Guid? UserUuid`. Заполнены только для remote; локальные пути не меняются (поле `null`).
- `Consumers/OnlineStatusChangedConsumer` — при заполненном `UserUuid` рассылает через `GetStreamsTrackingUuid` и кладёт uuid в `UserOnlineStatus.user_uuid` (`user_id` = 0).
- `Consumers/TypingChangedConsumer` / `TypingNotifier` — при заполненном `UserUuid` формирует `TypingEvent` с `user_uuid` (`user_id` = 0) и **не применяет** фильтр «кроме отправителя» (см. выше).
- Совместимость очередей: события сериализуются MassTransit'ом; добавление nullable-поля безопасно, но проверь, что оба консюмера переживают события **без** нового поля (инстансы обновляются не одновременно).

## Изменение 6 — интерес к remote-presence (heartbeat в Federation)

Новый `BackgroundServices/PresenceInterestReporter.cs`:

- Раз в `Onliner:PresenceInterestIntervalSeconds` (конфиг, дефолт 20) берёт `GetTrackedUuids()` и вызывает `FederationInternalApi.SetPresenceInterest(instance_id, user_uuids[])` (RPC добавляется в 4.3; до него метод отвечает `Unimplemented` — логировать debug и не считать ошибкой).
- `instance_id` — тот же идентификатор инстанса, что используется для имён fan-out очередей (`InstanceId.Current`).
- **Полный набор, а не дельты** — обоснование в README фазы (несколько инстансов, стримы живут на разных).
- Пустой набор тоже отправляется (это сигнал «за нами больше никто не следит» → Federation закрывает S2S-подписку).
- Ошибки/недоступность Federation: warning-лог + метрика, **без ретраев** — следующий тик через N секунд.
- Гейт: если конфиг `FederationService:Host` не задан — сервис не стартует вовсе (нода без федерации не должна лить ошибки).

Инфраструктура: gRPC-клиент `FederationInternalApi` в Onliner (образец — существующие клиенты Users/Messages, `JwtClientInterceptor`, service-токен) + ключи `FederationService:Host/Token` в **бакете `ServiceId.Onliner = 9`** (миграция Configuration по образцу `AddFederationMessagesServiceConfiguration`; помни найденный в 2.5 баг — ключ хранится в бакете **потребителя**, не вызываемого сервиса).

## Изменение 7 — изоляция от персиста и offline-детекции

- Убедиться (и закрепить тестом), что `OfflineDetectionService` и `DatabasePersistenceService` работают только с `onliner:presence` и никогда не видят `onliner:presence:remote:*` — иначе remote-статусы попадут в PostgreSQL и будут гаснуть по 5-секундному порогу.
- `UsersOnlineStatuses` (PK `long UserId`) не расширяется — remote-статусы не персистятся by design.

## Изменение 8 — метрики и документация

- Метрики (по образцу `Backend/Onliner-Metrics.md`): `remote_status_upserts`, `remote_typing_injections`, `presence_interest_reports` / `presence_interest_errors`, gauge `remote_tracked_uuids` (снимается `MetricsSnapshotService`), gauge `remote_presence_keys` — опционально, если дёшево.
- Правка [../07-presence-typing.md](../07-presence-typing.md): заменить описание «параллельная ветка `ConcurrentDictionary<Guid,…>` в памяти» на фактическую модель (Redis-ключи с TTL, fan-out доставка, отсутствие персиста), сохранив остальные решения дока.

## Чего НЕ делать

- Никаких S2S-вызовов и стримов к другим нодам — 4.3/4.4.
- Не персистить remote-статусы и не заводить для них фоновый offline-детектор (истина — на чужой ноде).
- Не применять `OnlineVisibility` к remote-пользователям.
- Не переписывать существующие структуры подписок на «универсальный ключ» — только параллельный индекс (минимальный диф в hot-path).

## Критерии готовности

1. Юнит-тесты (`Tests/BarkFluff.Onliner.Tests`, если проекта нет — создать по образцу `BarkFluff.Federation.Tests`, EF/Redis-двойники в стиле существующих):
   - `UpsertRemoteStatus` → статус читается `GetManyAsync`, публикуется событие с `UserUuid`;
   - подписка с `user_uuids` получает начальный снимок и последующие изменения; отписка чистит uuid-индекс (нет утечки в обратном индексе);
   - неизвестный uuid → `UNKNOWN`, не `OFFLINE`;
   - `InjectRemoteTyping` → `TypingEvent` с `user_uuid` уходит всем подписчикам чата;
   - `GetLocalPresence`: пользователь с `OnlineVisibility = All` отдаётся с реальным статусом; с `Friends`/`None` — `UNKNOWN`; сбой Users → `UNKNOWN` (fail-closed, как в существующем фильтре);
   - `OfflineDetectionService` не гасит remote-ключи (тест на изоляцию хранилищ);
   - `PresenceInterestReporter` шлёт полный набор и переживает `Unimplemented`/недоступность Federation.
2. Совместимость: существующие тесты Onliner зелёные; клиент, подписывающийся только по `user_ids`, ведёт себя идентично прежнему (проверить тестом).
3. `dotnet build` Onliner + Configuration (миграция ключей) — успех; миграция Configuration применяется локально.
4. **[делает разработчик]** На стенде: `UpsertRemoteStatus` через grpcurl с service-токеном → клиент, подписанный на этот uuid, видит статус в реальном времени; спустя TTL без обновлений статус становится `UNKNOWN`.
5. Obsidian: `Backend/Onliner.md` (uuid-ветка, `OnlinerServerApi`, remote-хранилище, интерес-репортер), `Backend/Onliner-Metrics.md` (новые метрики). Правка [../07-presence-typing.md](../07-presence-typing.md) (Изменение 8) выполнена.
6. Коммит: `feat(rearch-phase4): 4.2 — uuid-ветка Onliner, OnlinerServerApi, интерес к remote-presence`.
