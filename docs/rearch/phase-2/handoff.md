# Handoff — Фаза 2 (федеративные DM), состояние на 2026-07-21 (обновлено после 2.5)

## Общая картина

Фаза 2 реализует переписку 1-на-1 между нодами (федеративные DM). План — `docs/rearch/phase-2/README.md`, roadmap — `docs/rearch/10-roadmap.md`.

### Выполнено и закоммичено

| Этап | Коммит | Суть |
|------|--------|------|
| 2.1 | `2522916d` | Users: `RemoteUsers`, резолв FID, S2S-профиль с privacy |
| 2.2 | `cb866333` | Outbox, `ProcessedEvents`, консюмеры, `DeliverEvents` |
| fixes | `f0865a07` | P2-01/03/05/08/11 — фиксы shipped-кода 2.1/2.2 |
| 2.3 | `aafe8797` | Импорт федеративных чатов и сообщений (`ImportFederatedChat/Message`) |
| 2.4 | `bb3e8157` | Edit/Delete/Read федеративных DM + LWW |
| 2.5 | *(следующий коммит)* | Privacy `DenyFederatedDm`, отказ до отправителя, квота `ChatCreated` — детали ниже |

### Этап 2.4 — реализован

- **LWW**: `Features.Federation.LwwResolver` — `ShouldApplyMessageChange` (новее/старше/tie-break `(origin_server, event_id)`/удаление терминально) и `ShouldApplyRead` (монотонное "не откатывает более новое"). `FederatedMessageEvents` дополнен колонками `OriginServer`/`EventId` — хранят метку последнего применённого события для tie-break.
- **`FederatedReadStates(ChatId, UserUuid, LastReadFederatedMessageId, ReadAt)`** — миграция `20260721030000_AddFederatedReadStates` (та же миграция добавляет колонки `OriginServer`/`EventId` в `FederatedMessageEvents`).
- **`ApplyFederatedEdit`/`ApplyFederatedDelete`/`ApplyFederatedRead`** handlers (`Features.ApplyFederated{Edit,Delete,Read}`) — P2-02 origin-проверка через `FederationImportValidator.ResolveHomeServer` (резолв домашней ноды автора/читателя **по `ChatMember` того же чата**, без обращения к Users — в 1-на-1 fed-DM оба участника уже в `ChatMembers`). Проверка REJECTED через новое исключение `FederatedOriginMismatchException`.
- **Proto**: `origin_server`/`event_id`/`raw_event` добавлены в `ApplyFederatedEditRequest`/`ApplyFederatedDeleteRequest`; `origin_server` в `ApplyFederatedReadRequest`; `event_id` в `ImportFederatedMessageRequest` (для tie-break с первого события). `shared.proto Message.federated_read_by` (repeated string uuid).
- **Federation routing**: `FederationS2SApiService.RouteToInternalAsync` — `MessageEdited/MessageDeleted/MessagesRead` теперь маршрутизируются в `ApplyFederated*` (были `RETRY` заглушкой). `MessageEditedFederationConsumer` перестал быть заглушкой — извлекает `NewText` из `byte[] Message`.
- **Исходящий путь — важный пререк, не только "новый функционал"**: `SendMessageCommandHandler` **чинит реальный пробел 2.3** — путь отправки по уже известному `chat_id` (не только по `user_uuid` первого сообщения) не помечал сообщение как федеративное вовсе. Без этого фикса федерировалось бы только самое первое сообщение любой переписки. Признак теперь — наличие remote-участника (`ChatMember.ServerName` не пусто) среди участников чата, не требует доп. вызова Users.
- `EditMessageCommandHandler`/`DeleteMessageCommandHandler`/`MarkAsReadCommandHandler` дозаполняют fed-поля исходящих событий при `Message.FederatedId.HasValue`. `MarkAsRead` агрегирует несколько прочитанных сообщений одного fed-чата в одно "up to" событие (по максимальному `Id`), не шлёт N дублей.
- `ListMessagesCommandHandler` отдаёт `federated_read_by` — объединение `FederatedReadStates` с текущей страницей (приближение: `message.SentAt <= state.ReadAt`, без резолва anchor-сообщения отдельным запросом). Рендер на клиентах — Фаза 5, здесь только данные.
- **Тесты**: Federation.Tests 204/204 (было 198, +6: consumer text extraction + 5 routing/RETRY/REJECTED). Messages.Tests 282/290 passed (было 248, +34 новых теста, те же 8 pre-existing failures не связаны с федерацией). Новые файлы: `LwwResolverTests` (10), `ApplyFederatedEditCommandHandlerTests` (7), `ApplyFederatedDeleteCommandHandlerTests` (6), `ApplyFederatedReadCommandHandlerTests` (6) + точечные добавления в существующие тесты Edit/Delete/MarkAsRead/SendMessage/ListMessages.
- **Obsidian**: `Backend/Messages.md` дополнен разделом «Edit/Delete/Read федеративных DM + LWW (этап 2.4)».
- **Не сделано в 2.4** (осознанно, по плану): `ExportChatEvents`/`FetchChatHistory`/catch-up после RETRY — этап 2.6. Вложения при правке — семантика в Фазе 3. Клиентский рендер `federated_read_by` — Фаза 5.

### Этап 2.5 — реализован

- **Users**: `Privacy.DenyFederatedDm` (bool, default false) + миграция `20260721040000_AddDenyFederatedDm`; `PrivacyMapping`/`PrivacyStorage.Update` дополнены. `GetUsersByUuidQueryHandler` отдаёт флаг в `UserProfileByUuid.deny_federated_dm` для локальных (батч через новый `PrivacyStorage.GetByUserIds` — без похода в БД на каждого); remote всегда `false`. Proto-поле `deny_federated_dm` в `PrivacySettings`(7) уже существовало с 0.4.
- **`FederatedDmRejectedException`** (`Shared.Exceptions/Messages/`) — единственное исключение в проекте с `ErrorCode` = литеральная строка `"FederatedDmRejected"` (не GUID): так `OutboxDispatcher` сравнивает `x-error-code` напрямую.
- **`ImportFederatedChatCommandHandler`**: после идемпотентности (существующий чат — privacy не проверяется, флаг влияет только на новые чаты) проверяет `invitee.DenyFederatedDm` → бросает исключение выше.
- **`FederatedChatRejectedEvent`** (`Shared.Queue.Federation`, `{ Guid ChatId, string Reason }`). Publisher — `OutboxDispatcher.DispatchDestinationAsync`: при `Rejected` с `ErrorCode == "FederatedDmRejected"` и `row.ChatId.HasValue` — публикует через `IPublishEndpoint` (раньше был TODO-стаб `PublishChatRejected`, оставленный этапом 2.2). Consumer — новый `Messages.Consumers.FederatedChatRejectedConsumer` (очередь `federated-chat-rejected-messages`) → `ChatsStorage.MarkFederatedChatRejectedAsync` ставит `Chat.FederatedStatus = Rejected` (idempotent).
- **`SendMessageCommandHandler`**: для существующего чата дополнительно проверяет `ChatsStorage.GetFederatedStatusAsync` — `Rejected` → `FederatedDmRejectedException` сразу на этой ноде (не уходит в бесконечный RETRY через Federation).
- **Квота `ChatCreated` per-origin**: `Federation.Services.ChatCreatedQuotaLimiter` (`IChatCreatedQuotaLimiter`) — Redis-счётчик `fed:chatcreated:{origin}:{yyyyMMddHH}`, лимит `Federation:ChatCreatedHourlyLimit` (default 100). Проверяется в `RouteToInternalAsync`, `case ChatCreated`, до вызова `ImportFederatedChat` — превышение → `RETRY` + метрика `chatcreated_quota_exceeded.{origin}` + warning-лог (добавлен `ILogger<FederationS2SApiService>`, которого раньше не было). Federation получил первую зависимость от Redis (`Microsoft.Extensions.Caching.StackExchangeRedis` пакет + `IConnectionMultiplexer` в DI).
- **Configuration**: новая миграция `20260721050000_AddFederationChatCreatedQuotaConfiguration` (`Federation:ChatCreatedHourlyLimit` + `Redis` под ServiceId=15) + default `"100"` в `ConfigurationDefaultsPopulator`.
- **Найден и исправлен баг из 2.3**: миграция `20260721020000_AddFederationMessagesServiceConfiguration` сеяла `MessagesService:Host/Token` под `ServiceId=6` (Messages) вместо `ServiceId=15` (Federation) — по образцу `AddBotsConfiguration`, где тот же `MessagesService` заведён под ServiceId Bots=14, каждый потребитель хранит ключ в СВОЁМ бакете. Итог бага: `builder.LoadConfiguration(ServiceId.Federation)` никогда не получил бы эти значения — Federation упал бы при старте (`new Uri(null!)` в `AddGrpcClient<MessagesServerApi...>`). Исправлено прямым редактированием миграции (не fix-forward: миграция создана в этой же цепочке работы, ещё не применялась ни к одной реальной БД).
- **Tests**: Federation.Tests 208/208 (было 204, +4: 2 OutboxDispatcher FederatedChatRejectedEvent + 2 DeliverEvents quota routing/RETRY). Messages.Tests 290/298 passed (было 282, +8: ImportFederatedChat×4, SendMessage Rejected×1, FederatedChatRejectedConsumer×3), те же 8 pre-existing failures. Users.Tests 291/293 passed (+6: PrivacyMapping DenyFederatedDm roundtrip обновлён, GetUsersByUuidQueryHandlerTests новый файл×3) — **1 pre-existing непричастный к 2.5 сбой**: `DevicesStorageConcurrencyTests.RegisterOrUpdateDevice_ConcurrentCalls_KeepSingleDevice` (реальная гонка двух SQLite-соединений, падает детерминированно на этой машине независимо от моих изменений — не чинил, вне скоупа 2.5).
- **Obsidian**: `Backend/Users.md` (DenyFederatedDm), `Backend/Messages.md` (Rejected-статус, консюмер), `Backend/Federation.md` (квота + FederatedChatRejectedEvent, попутно актуализирован раздел DeliverEvents/консюмеров, отстававший с 2.2), `Shared/Queue.md` (FederatedChatRejectedEvent).
- **Не сделано в 2.5** (осознанно, по плану): пользовательская блокировка конкретных отправителей, клиентский UI-тумблер (Фаза 5), автоблок ноды по квоте (только метрика + ручной блок).

### Pre-existing баги (не от федерации)

8 падающих тестов в `Messages.Tests` (не связаны с федерацией):
- `GetChatInfoCommandHandlerTests`, `CreatePrivateChatCommandHandlerTests`, `RejectPrivateChatCommandHandlerTests`, `SendPrivateMessageCommandHandlerTests` — NRE от `GetMutedChatIdsAsync`/`PrivateChatInviteNotFoundException`.
- Были красными ДО 2.3 (проверено: изменения 2.3 не трогают `PrivateInviteState` default или `GetMutedChatIdsAsync`).

### Билд

- `dotnet build` для всех backend-проектов (Messages, Federation, Users, Configuration) — **OK**.
- WPF (Windows) не билдится на macOS — ожидаемо.
- Временно изменён `global.json`: `rollForward: "latestFeature"` (снят обратно на `"disable"`). **Не коммитить эту правку.**

## Следующие этапы (читать каждый план перед реализацией)

### 2.4 — Edit/Delete/Read через федерацию + LWW — ГОТОВО
См. секцию «Этап 2.4 — реализован» выше.

### 2.5 — Privacy DenyFederatedDm, отказ до отправителя, квота — ГОТОВО
См. секцию «Этап 2.5 — реализован» выше. Файл плана `step-2.5-privacy-antispam.md` выполнен полностью.

### 2.6 — Catch-up: ExportChatEvents, FetchChatHistory, SyncChatStates
**Файл плана**: `step-2.6-catchup.md`

Что делать:
- `MessagesServerApi.ExportChatEvents(chat_id, since_ts, limit, requesting_server)`: проверка участия + выборка сообщений + сборка событий из `FederatedMessageEvents` (чужие) или свежие (свои)
- Federation: S2S `FetchChatHistory` + internal `FetchRemoteChatHistory` (через тот же in-пайплайн)
- proto + сервер `SyncChatStates`: пары `(chat_id, last_event_ts)` для обнаружения тихих дыр
- BackgroundService в Federation: плановая сверка (раз в час) + при reconnect
- Триггеры: RETRY:ChatUnknown/MessageUnknown → постановка задачи catch-up
- Ручной триггер: `TriggerChatSync(server_name, chat_id?)` internal-RPC

### 2.7 — Слияние одновременно созданных DM
**Файл плана**: `step-2.7-dm-merge.md`

Что делать:
- Детерминированный протокол: победитель = чат с меньшим ChatId (`string.CompareOrdinal` формата `"D"` lowercase)
- `ImportFederatedChat` заменяет временный `REJECTED:DuplicateFederatedDm` на merge-логику
- Перенос сообщений из L → W (UPDATE ChatId), `MergedIntoChatId` колонка
- Перенаправление входящих событий для Merged-чатов в чат-победитель

### 2.8 — Пуши: имя remote-отправителя
**Файл плана**: `step-2.8-pushes.md`

Что делать:
- `SenderDisplayName` поле в `NewMessageEvent` (заполняется при импорте из `RemoteUsers`)
- CloudMessaging: ветка для `SenderUuid` (remote) → имя = `SenderDisplayName`, fallback = `SenderFid`

### 2.9 — Профильные события + Beacon
**Файл плана**: `step-2.9-profile-events-beacon.md`

Что делать:
- `MessagesServerApi.GetFederatedPeersForUser(user_uuid)` → ноды remote-участников
- Консюмеры `UserChangedName/Username/Avatar/Bio` → `UserProfileChangedPayload` → outbox на ноды-партнёры
- Входящие: `UserProfileChangedPayload` → `UpsertRemoteUsers`; `UserDeactivatedPayload` → `IsDeactivated = true`
- Beacon: расширенная регистрация с `server_name`/`federation_endpoint`

### Фаза 3 — Файлы (после Фазы 2)
План: `docs/rearch/phase-3/README.md` (создан, этапы 3.1–3.5).

## Ключевые архитектурные решения (Phase 2)

1. **Каждый сервер хранит свою копию чата** — история доступна при offline второй ноды.
2. **LWW по `LastChangeAt`** — актуальна версия с более новой UTC-меткой; tie-break `(origin_ts_ms, origin_server, event_id)`.
3. **Удаление терминально** — правка после удаления игнорируется (отступление от чистого LWW).
4. **Канонический порядок Guid** для merge — `string.CompareOrdinal` lowercase `"D"`-format.
5. **Хранение wire-байтов** в `FederatedMessageEvents` для catch-up (подпись origin сохранена).
6. **`FederatedUuidLow/High`** — нормализованная пара UUID (low < high) с уникальным индексом для Active-чатов (анти-дубль).
7. **`invitee_uuid`** в `ImportFederatedChatRequest` вместо `invitee_user_id` (proto backward-compatible).
8. **`raw_event`** в `ImportFederatedMessageRequest` — wire-байты FederationEvent.
9. **Нода говорит только за своих** — проверка `homeserver(SenderUuid) == origin` для edit/delete/read (P2-02).
10. **Publisher confirms** для федеративных событий (MassTransit) — проверить включение.

## Технические заметки

### Разработка окружения
- macOS, .NET SDK 10.0.203 (ожидает 10.0.110) — `global.json` временно `rollForward: "latestFeature"`, восстановить на `"disable"` перед коммитом.
- WPF-проекты не собираются на macOS — игнорировать.
- `dotnet ef migrations add` может падать с `MissingMethodException` — писать миграции вручную (3 файла).

### Ветка и коммиты
- Ветка: `dev` (не создавать новых)
- Пуш не делать. Коммит после каждого этапа. Формат: `feat(rearch-phase2): <этап> — <суть>`

### Obsidian
После каждого этапа дополнять:
- 2.3 → `Backend/Messages.md`, `Backend/Federation.md`, `Shared/Queue.md`
- 2.4 → `Backend/Messages.md`
- 2.5 → `Backend/Users.md`, `Backend/Messages.md`, `Backend/Federation.md`, `Shared/Queue.md`
- 2.6 → `Backend/Messages.md`, `Backend/Federation.md`
- 2.7 → `Backend/Messages.md`
- 2.8 → `Backend/CloudMessaging.md`
- 2.9 → `Backend/Users.md`, `Backend/Federation.md`, `Backend/Beacon.md`, `Backend/Navigator.md`

### Стенд
`Backend/dev-federation-testbed/` — двух-нодовый стенд. node2 пока не имеет сервисов Users/Messages/Updates (будет добавлено в конце 2.3 или отдельно). До этого E2E-проверки только юнит-тестами.

### Тесты
- Federation.Tests — **208/208 passed** (после изменений 2.5; было 204 после 2.4, 198 после 2.3)
- Messages.Tests — **290/298 passed**, 8 pre-existing failures (не от федерации — см. раздел выше)
- Users.Tests — **291/293 passed**, 1 pre-existing failure не от 2.5 (SQLite-гонка в `DevicesStorageConcurrencyTests`, см. раздел «Этап 2.5»), 1 skipped
- Новые тесты 2.3: `FederationImportValidatorTests` (10), `FederatedUuidPairTests` (1)
- Новые тесты 2.4: `LwwResolverTests` (10), `ApplyFederatedEditCommandHandlerTests` (7), `ApplyFederatedDeleteCommandHandlerTests` (6), `ApplyFederatedReadCommandHandlerTests` (6), точечные добавления в EditMessage/DeleteMessage/MarkAsRead/SendMessage/ListMessages тесты + Federation `DeliverEventsTests`/`MessageEditedFederationConsumerTests`
- Новые тесты 2.5: `ImportFederatedChatCommandHandlerTests` (4), `FederatedChatRejectedConsumerTests` (3), `GetUsersByUuidQueryHandlerTests` (3, Users.Tests), точечные добавления в `SendMessageCommandHandlerTests`, `PrivacyMappingTests`, Federation `OutboxDispatcherTests`/`DeliverEventsTests`

### Уже реализованные infra-зависимости (не менять)
- `ChatsStorage` → методы `GetFederatedChatAsync`, `FindActiveFederatedChatByUuidPairAsync`, `CreateFederatedChatAsync`, `GetFederatedStatusAsync`, `MarkFederatedChatRejectedAsync` (2.5)
- `MessageQueueSender` → `SendImportedMessage`, `SendFederatedMessage`, `SendEdited`/`SendDeleted` (fed-поля — опциональные параметры, этап 2.4)
- `ReadByQueueSender.SendEvent` — fed-поля опциональными параметрами (этап 2.4)
- `FederatedReadStatesStorage` → `UpsertAsync` (идемпотентно, монотонно через `LwwResolver.ShouldApplyRead`), `GetForChatAsync`
- `Features.Federation.LwwResolver` → `ShouldApplyMessageChange`, `ShouldApplyRead` — переиспользовать, не дублировать логику LWW в новом коде
- `Features.Federation.FederationImportValidator.ResolveHomeServer(chat, uuid, ownServer)` — резолв домашней ноды участника fed-чата по `ChatMember`, без обращения к Users
- `Users.PrivacyStorage.GetByUserIds(userIds)` — батч-чтение Privacy для `GetUsersByUuid` (2.5)
- `Federation.Services.IChatCreatedQuotaLimiter` / `ChatCreatedQuotaLimiter` — квота ChatCreated per-origin (2.5); тестовый двойник `FakeChatCreatedQuotaLimiter` в `Federation.Tests.Infrastructure`
- `FederatedDmRejectedException.ErrorCode` — литеральная строка `"FederatedDmRejected"` (единственное исключение проекта вне GUID-паттерна, см. «Этап 2.5»)
- Маппинг Federation для `RpcException.StatusCode → EventStatus`: FailedPrecondition/InvalidArgument/PermissionDenied/AlreadyExists → REJECTED; NotFound/Unavailable/DeadlineExceeded/Aborted/Cancelled → RETRY
- `Message.SenderId` nullable, `ChatMember.UserId` nullable — все хендлеры выдачи используют `ChatMemberExtensions.LocalUserIds()` для фильтрации remote-участников
- **ServiceId для Configuration-миграций сервисов**: `BarkFluff.Shared.Identity.ServiceId` (Federation=15, Messages=6, Bots=14, ...) — каждый потребитель inter-service ключа (`XxxService:Host/Token`) хранит его в СВОЁМ ServiceId-бакете, не в бакете вызываемого сервиса (см. найденный баг 2.3 в разделе «Этап 2.5»)
