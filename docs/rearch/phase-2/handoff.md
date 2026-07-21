# Handoff — Фаза 2 (федеративные DM), состояние на 2026-07-21

## Общая картина

Фаза 2 реализует переписку 1-на-1 между нодами (федеративные DM). План — `docs/rearch/phase-2/README.md`, roadmap — `docs/rearch/10-roadmap.md`.

### Выполнено и закоммичено

| Этап | Коммит | Суть |
|------|--------|------|
| 2.1 | `2522916d` | Users: `RemoteUsers`, резолв FID, S2S-профиль с privacy |
| 2.2 | `cb866333` | Outbox, `ProcessedEvents`, консюмеры, `DeliverEvents` |
| fixes | `f0865a07` | P2-01/03/05/08/11 — фиксы shipped-кода 2.1/2.2 |

### Выполнено, **не закоммичено** (69 изменённых файлов)

**2.3 полностью реализован в staging:**

- **Domain**: `Chat.IsFederated/FederatedStatus/FederatedUuidLow+High`; `ChatMember.UserId` nullable + `ServerName`; `Message.SenderId` nullable; `FederatedStatus` enum; `FederatedMessageEvent` entity; `ChatMemberExtensions.LocalUserIds()`.
- **EF Core**: `MessagesContext` + Configurations (`FederatedStatus` default, unique index UUID-пары, `FederatedMessageEvent`); миграция `20260721010000_AddFederatedChatSchema` (ручная, три файла); snapshot обновлён.
- **Config migration**: `20260721020000_AddFederationMessagesServiceConfiguration`.
- **Proto**: `messages_api.proto` → `invitee_uuid` в `ImportFederatedChatRequest`, `raw_event` в `ImportFederatedMessageRequest`; `users_api.proto` → `user_id` в `UserProfileByUuid`.
- **Exceptions** (9 новых): `ChatUnknownException`, `DuplicateFederatedDmException`, `FederatedGroupsNotSupported`, `FederatedMessageUnknownException`, `RemoteProfileRejectedException`, `RemoteUserNotResolvedException`, `TimestampInFutureException`, `UnknownInviteeException`, `MessageTextTooLongException`, `TooManyAttachmentsException`.
- **Helpers**: `FederationImportValidator` (clamp меток, лимиты, origin-проверка), `FederatedUuidPair`.
- **ImportFederatedChatCommandHandler**: валидация → upsert initiator → анти-дубль UUID-пары → создание чата.
- **ImportFederatedMessageCommandHandler**: валидация → идемпотентность → вставка `Message` → `FederatedMessageEvents` → `NewMessageEvent`.
- **SendMessageCommandHandler**: ветка `request.UserUuid` (резолв через `Users.GetUsersByUuid`, создание/reuse fed-чата, публикация с fed-полями).
- **GetPersonChatIdCommandHandler**: ветка `UserUuid` (только find, без авто-создания).
- **MessageQueueSender**: `SendImportedMessage` (без remote-публикации), `SendFederatedMessage` (исходящий fed-путь).
- **MessagesApiService**: `source_id OneofCase.UserUuid` → ветка с uuid-peer.
- **MessagesServerApiService**: `ImportFederatedChat`, `ImportFederatedMessage` (остальные Unimplemented).
- **Mapping**: `MessageMapping` → `federated_id`/`sender_uuid`; `ChatMemberMapping` → `user_uuid`/`server_name`.
- **Federation.csproj**: добавлен `messages_api.proto` (GrpcServices=Client).
- **FederationS2SApiService**: `MessagesServerApiClient` зарегистрирован; `RouteToInternalAsync` отправляет `ChatCreated→ImportFederatedChat`, `NewMessage→ImportFederatedMessage`, остальное → RETRY.
- **NewMessageFederationConsumer**: парсит `byte[] Message` (proto `barkfluff.shared.Message`) для текста; кладёт `Text` и `Sender.Username` (парсинг SenderFid).
- **MessageMapping**: добавлено отображение `FederatedId` и `SenderUuid` в proto.
- **ChatMemberMapping**: добавлено отображение `UserUuid` и `ServerName` в proto.
- **Tests**: Federation.Tests **198/198 passed**; новые тесты `FederationImportValidatorTests` (10) + `FederatedUuidPairTests` (1) — pass. Починены: `FederationS2SApiService` конструктор, `NewMessageFederationConsumerTests.Username`, `DeliverEventsTests` (маршрутизация в Messages).
- **Obsidian** дополнен: `Backend/Messages.md` (секция федеративных DM), `Shared/Queue.md` (федеративный контекст событий).

### Pre-existing баги (не от 2.3)

8 падающих тестов в `Messages.Tests` (не связаны с федерацией):
- `GetChatInfoCommandHandlerTests`, `CreatePrivateChatCommandHandlerTests`, `RejectPrivateChatCommandHandlerTests`, `SendPrivateMessageCommandHandlerTests` — NRE от `GetMutedChatIdsAsync`/`PrivateChatInviteNotFoundException`.
- Были красными ДО 2.3 (проверено: изменения 2.3 не трогают `PrivateInviteState` default или `GetMutedChatIdsAsync`).

### Билд

- `dotnet build` для всех backend-проектов (Messages, Federation, Users, Configuration) — **OK**.
- WPF (Windows) не билдится на macOS — ожидаемо.
- Временно изменён `global.json`: `rollForward: "latestFeature"` (снят обратно на `"disable"`). **Не коммитить эту правку.**

## Следующие этапы (читать каждый план перед реализацией)

### 2.4 — Edit/Delete/Read через федерацию + LWW
**Файл плана**: `step-2.4-edit-delete-read-lww.md`

Что делать:
- Миграция `FederatedReadStates` (ChatId, UserUuid, LastReadFederatedMessageId, ReadAt)
- LWW-хелпер: `event.origin_ts_ms > local.LastChangeAt` → применить; tie-break `(origin_ts_ms, origin_server, event_id)`; удаление терминально (правка после удаления → игнорировать)
- `ApplyFederatedEdit`/`ApplyFederatedDelete` RPC: проверка `homeserver(SenderUuid) == x-bf-origin`; иначе REJECTED (закрывает P2-02)
- `ApplyFederatedRead` RPC: upsert `FederatedReadStates`; отдача объединения локальных + федеративных прочтений
- Исходящий путь: локальные edit/delete/read в fed-чате → расширенные Queue-события → консюмеры Federation → outbox
- RETRY:ChatUnknown / RETRY:MessageUnknown при неизвестном чате/сообщении (триггеры catch-up 2.6)

### 2.5 — Privacy AllowFederatedDm, отказ до отправителя, квота
**Файл плана**: `step-2.5-privacy-antispam.md`

Что делать:
- Users: `DenyFederatedDm` поле в privacy-модели + миграция + маппинг `deny_federated_dm` в proto
- Messages: `ImportFederatedChat` проверяет `DenyFederatedDm` → `FederatedDmRejected` (permanent REJECTED)
- `FederatedChatRejectedEvent` Queue-событие → консюмер → `Chat.FederatedStatus = Rejected`
- Квота `ChatCreated` per-origin: Redis-счётчик `fed:chatcreated:{origin}:{hour}`, лимит `Federation:ChatCreatedHourlyLimit` (default 100)
- Повторная отправка в Rejected-чат → понятная ошибка

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
- Federation.Tests — **198/198 passed** (после изменений 2.3)
- Messages.Tests — 8 pre-existing failures (не от 2.3)
- Новые тесты: `FederationImportValidatorTests` (10), `FederatedUuidPairTests` (1)

### Уже реализованные infra-зависимости (не менять)
- `ChatsStorage` → методы `GetFederatedChatAsync`, `FindActiveFederatedChatByUuidPairAsync`, `CreateFederatedChatAsync`
- `MessageQueueSender` → `SendImportedMessage`, `SendFederatedMessage`
- Маппинг Federation для `RpcException.StatusCode → EventStatus`: FailedPrecondition/InvalidArgument/PermissionDenied/AlreadyExists → REJECTED; NotFound/Unavailable/DeadlineExceeded/Aborted/Cancelled → RETRY
- `Message.SenderId` nullable, `ChatMember.UserId` nullable — все хендлеры выдачи используют `ChatMemberExtensions.LocalUserIds()` для фильтрации remote-участников
