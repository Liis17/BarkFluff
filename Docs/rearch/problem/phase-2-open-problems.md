# Открытые проблемы после реализации Phase 2 (этапы 2.1–2.2)

Зафиксировано: 2026-07-19. Источник:

- Standards/Spec review диапазона `af5b373e...HEAD` — два коммита:
  - `2522916d` feat(rearch-phase2): 2.1 — Users RemoteUsers + резолв FID + S2S-профиль;
  - `cb866333` feat(rearch-phase2): 2.2 — outbox, ProcessedEvents, консюмеры, DeliverEvents.
- Спецификации: `docs/rearch/phase-2/step-2.1-users-remoteusers.md`, `docs/rearch/phase-2/step-2.2-outbox.md`.

Явно задекларированные автором отклонения (не считаются скрытыми дефектами, перечислены для полноты):

- E2E на двух-нодовом testbed не прогнан (Docker вне скоупа сессии) — этапы 2.1 и 2.2;
- юнит-тесты диспетчера/DeliverEvents не добавлены (см. P2-06);
- извлечение текста из `byte[] Message` в `NewMessagePayload` отложено до 2.3;
- `GetFederationStatus` переделан на реальный подсчёт Pending/DeadLetter и добавлены доп. метрики (`outbox_deliver_duration_ms_total`, `outbox_enqueued_total`, `outbox_retry`, `federation_consumer_new_message`, `users_by_uuid_lookups`) — небольшой scope creep сверх §Изменение 7, функционально безвреден, отдельной проблемой не оформляется.

## Сводка

| ID | Приоритет | Проблема | Статус |
|---|---|---|---|
| P2-01 | P1 | NewMessage `event_id` = id сообщения, а не новый uuid; в null-ветке двойная генерация guid | **Исправлена** (f0865a07) |
| P2-02 | P1 | «Нода говорит только за своих» не проверяется для edited/deleted/read | **Решение зафиксировано** — проверка на импорт-слое Messages (step-2.4 §Изм.3/4, step-2.2 §Изм.6); код в 2.4 |
| P2-03 | P2 | Нет warning-лога при отказе UUID-пиннинга (`ServerNameMismatch`) | **Исправлена** (f0865a07) |
| P2-04 | P2 | Деактивированный/забаненный пользователь не даёт `found=false` (нет флага в домене) | **Отклонение согласовано** (step-2.1 §Изм.5) |
| P2-05 | P2 | `FederatedChatRejectedEvent`-заглушка не оставлена | **Исправлена** (f0865a07) |
| P2-06 | P2 | Нет юнит-тестов диспетчера/DeliverEvents (ordering/backoff/dedup) | Открыта (Батч 3) |
| P2-07 | P2 | Дублированный код в четырёх federation-консюмерах (Shotgun Surgery) | Отложена (Батч 4; 2.4 всё равно тронет консюмеры) |
| P2-08 | P2 | Спекулятивное поле `SenderDisplayName` + неиспользуемый `_logger` в консюмерах | **Исправлена** (f0865a07) |
| P2-09 | P2 | Дублированный парсинг FID | Отложена (Батч 4) |
| P2-10 | P2 | Повторяющийся `switch` по `FederationEvent.PayloadCase` в трёх местах | Отложена (Батч 4) |
| P2-11 | P2 | `OutboxDispatcher.GetMaxAttempts` создаёт scope ради singleton `IConfiguration` | **Исправлена** (f0865a07) |

## P2-01 — NewMessage `event_id` не является новым uuid

Spec 2.2 §Изменение 4 (`step-2.2-outbox.md`): «обернуть в `FederationEvent` (`event_id = новый uuid` …)». В `NewMessageFederationConsumer.cs:75` в `BuildEvent` передаётся `msg.FederatedId ?? Guid.NewGuid()` как `event_id`, тогда как `ChatCreated` (строка 56) и консюмеры Edited/Read используют `Guid.NewGuid()`.

Два аспекта:

- Инкриминирующий дефект: в null-ветке `event_id` (строка 75) и `federated_message_id` (строка 81) вычисляются двумя независимыми вызовами `msg.FederatedId ?? Guid.NewGuid()` и становятся разными случайными guid. Один и тот же логический факт получает два разных идентификатора.
- Расхождение со spec/консистентностью: использование `FederatedId` в роли `event_id` может быть намеренным (дедуп в `ProcessedEvents` ключуется по `event_id`), но тогда это должно быть зафиксировано в спецификации, а поведение — единообразно между всеми консюмерами.

Что сделать: вычислять `event_id` один раз; определиться, `event_id` = новый uuid (как в spec и остальных консюмерах) или детерминированный от `FederatedId` (тогда описать в spec и применить во всех консюмерах); `federated_message_id` заполнять из того же источника, что и остальные, без второй генерации.

Критерий закрытия: `event_id` детерминирован в рамках одного сообщения, согласован со спецификацией и единообразен по всем консюмерам; в null-ветке не возникает двух разных guid.

## P2-02 — «Нода говорит только за своих» не проверяется для edited/deleted/read

Spec 2.2 §Изменение 6 шаг 4 (`step-2.2-outbox.md`): «uuid/server_name автора внутри payload принадлежит origin — нет → `REJECTED`». `PayloadAuthorBelongsToOrigin` (`FederationS2SApiService.cs:227`) возвращает `author` только для `ChatCreated`/`NewMessage`/`ProfileChanged`; для `MessageEdited`/`MessageDeleted`/`MessagesRead` возвращает `null` и тем самым `return true` (проверка не применяется). proto-payload'ы редактирования/удаления не несут author `server_name`; `MessagesRead` имеет `reader_uuid`, но он не проверяется.

Сейчас дыра инертна: в 2.2 все чатовые payload'ы маршрутизируются в `RETRY` (`RouteToInternal`, `FederationS2SApiService.cs:212`) до реализации импорта в Messages. Проблему нужно закрыть до 2.3, когда импорт будет подключён и события начнут применяться.

Что сделать: обеспечить, чтобы proto-payload'ы edited/deleted/read несли достаточные атрибуты автора/читателя, и проверять их принадлежность origin до применения; либо явно зафиксировать проверку на этапе импорта 2.3 как обязательное предусловие.

Критерий закрытия: событие редактирования/удаления/прочтения от ноды, где автор/читатель принадлежит не этой ноде, отклоняется до применения; сценарий покрыт тестом.

## P2-03 — Нет warning-лога при отказе UUID-пиннинга

Spec 2.1 §Изменение 2 (`step-2.1-users-remoteusers.md`): «uuid уже известен с другим `ServerName` → отказ … + warning-лог + метрика». `RemoteUsersStorage.UpsertAsync` возвращает `UpsertStatus.RejectedServerNameMismatch` (`RemoteUsersStorage.cs:92`), хендлер инкрементирует метрику, но `RemoteUsersStorage` не имеет `ILogger` и warning нигде не пишется. Метрика есть, лог отсутствует.

Что сделать: логировать warning при `RejectedServerNameMismatch` (и симметрично при `LocalUuidCollision`, если требуется наблюдаемость), с указанием `uuid`, ожидаемого и полученного `ServerName`.

Критерий закрытия: отказ по несовпадению `ServerName` фиксируется warning-логом с диагностическими полями.

## P2-04 — Деактивированный/забаненный не даёт `found=false`

Spec 2.1 §Изменение 5 (`step-2.1-users-remoteusers.md`): «Деактивированный/забаненный → `found=false`». `GetFederatedProfileQueryHandler` фильтрует только `IsDraft` и `ProfileVisibleOnSite`. Домен `User` (`Domain/User.cs`) имеет лишь `IsDraft` и `IsBot`; флага деактивации/бана нет, поэтому требование неисполнимо в текущей модели, а не проигнорировано злонамеренно. Тем не менее требование spec фактически не закрыто.

Что сделать: определить, существует ли (или нужен) в домене признак деактивации/бана; при появлении — включить в фильтр `GetFederatedProfile`; при осознанном отсутствии — согласовать и зафиксировать отклонение в spec 2.1.

Критерий закрытия: профиль деактивированного/забаненного пользователя не отдаётся по федерации, либо отсутствие такого состояния явно задокументировано как согласованное отклонение.

## P2-05 — Не оставлена заглушка `FederatedChatRejectedEvent`

Spec 2.2 §Изменение 5 (`step-2.2-outbox.md`): «DeadLetter по privacy-отказу дополнительно публикует `FederatedChatRejectedEvent` — заглушку оставь». В `OutboxDispatcher.cs:201–203` есть комментарий `// Privacy-отказ → FederatedChatRejectedEvent (этап 2.5).` и инкремент метрики `outbox_deadletter.federated_dm_rejected`, но нет публикующего placeholder-метода/точки под 2.5.

Что сделать: оставить явную заглушку публикации (метод/no-op-точку с TODO 2.5), чтобы на 2.5 подключалась публикация, а не искалось место.

Критерий закрытия: в коде существует именованная точка публикации `FederatedChatRejectedEvent`, вызываемая на privacy-DeadLetter, с TODO на этап 2.5.

## P2-06 — Нет юнит-тестов диспетчера/DeliverEvents

Spec 2.2 §Критерий 1 (`step-2.2-outbox.md`) требует покрытие. Автор задекларировал отклонение в коммите: добавлены только `EventSignerTests`; упорядочивание per-(Destination, ChatId), backoff, дедуп `ProcessedEvents`, классификация статусов `DeliverEvents` не покрыты. Отклонение принято как известное, но требование остаётся открытым.

Что сделать: добавить тесты на порядок доставки в рамках (Destination, ChatId), backoff-переходы и `MaxAttempts → DeadLetter`, дедуп по `event_id`, а также классификацию `origin_mismatch`/`ALREADY_PROCESSED`/подпись в `DeliverEvents`.

Критерий закрытия: перечисленные сценарии автоматизированы в `BarkFluff.Federation.Tests` и проходят в CI.

## P2-07 — Дублированный код в federation-консюмерах

`NewMessageFederationConsumer.cs`, `MessageDeletedFederationConsumer.cs`, `MessageEditedFederationConsumer.cs`, `MessageReadFederationConsumer.cs` дословно повторяют: конструктор (writer/config/metrics/logger), `IsFederationEnabled()`, гард `if (!IsFederationEnabled() || !msg.IsFederated || msg.RemoteParticipants.Count == 0) return;`, а также скелет `destinations/origin/ts` и `new FederationEvent { EventId, OriginServer, OriginTsMs }`. Добавление поля в этот скелет требует правки в четырёх файлах (Shotgun Surgery). Нарушает CLAUDE.md §Duplicated-Code (judgement call).

Что сделать: вынести общую часть в базовый класс/хелпер консюмера (гард, построение конверта `FederationEvent`, разбор destinations).

Критерий закрытия: изменение общего скелета конверта затрагивает один файл; консюмеры содержат только payload-специфичную логику.

## P2-08 — Спекулятивное поле и неиспользуемый логгер

Нарушение CLAUDE.md §2 «ничего спекулятивного» (judgement call):

- `Shared/BarkFluff.Shared.Queue/Messages/NewMessageEvent.cs:40` — поле `SenderDisplayName` с комментарием `// Для пушей (этап 2.8) — завести поля сразу`, нигде в диапазоне не читается; заведено под будущую фазу.
- Поле `_logger` присвоено во всех четырёх message-консюмерах (напр. `MessageDeletedFederationConsumer.cs`), но `.Log*` в них не вызывается (реально логирует только `SessionRevokedConsumer`) — мёртвая зависимость.

Что сделать: удалить `SenderDisplayName` до этапа, где оно реально используется (2.8 заведёт его вместе с потреблением); либо использовать `_logger` в консюмерах, либо убрать его из ctor.

Критерий закрытия: в диапазоне нет полей/зависимостей, добавленных «на будущее» без текущего потребления.

## P2-09 — Дублированный парсинг FID

`Backend/BarkFluff.Users/Services/FidParser.cs`: методы `TryParse` и `LooksLikeFid` повторяют логику trim / снятия `@` / `IndexOf(':')`. Отдельная копия — `FederationInternalApiService.cs` (`TryParseFid`, проверка `colon <= 0 || colon == trimmed.Length - 1`). Кросс-сервисное переиспользование ограничено (разные проекты), но внутрифайловый повтор в `FidParser` схлопывается (judgement call, CLAUDE.md §Duplicated-Code).

Что сделать: свести повтор внутри `FidParser` к одной точке разбора; рассмотреть общий формат разбора FID для Users и Federation, если это оправдано.

Критерий закрытия: логика разбора FID внутри `FidParser` не дублируется; поведение подтверждено существующими `FidParserTests`.

## P2-10 — Повторяющийся switch по `PayloadCase`

Один и тот же `switch` по `FederationEvent.PayloadCase` присутствует минимум в трёх местах двух файлов: извлечение `chatId` (`FederationInternalApiService.cs`), `RouteToInternal` и `PayloadAuthorBelongsToOrigin` (`FederationS2SApiService.cs`). Каждый новый payload требует правки во всех трёх (Repeated Switches, judgement call). Concern'ы разные, поэтому не безусловно (маршрутизация vs извлечение автора vs извлечение chatId).

Что сделать: при добавлении новых payload'ов рассмотреть единый реестр/маппинг payload → (chatId, author, routing), чтобы новый case добавлялся в одном месте.

Критерий закрытия: добавление нового payload-типа не требует синхронной правки трёх разрозненных switch'ей.

## P2-11 — Scope ради singleton в `GetMaxAttempts`

`OutboxDispatcher.GetMaxAttempts` (`OutboxDispatcher.cs:260`) выполняет `_scopeFactory.CreateScope().ServiceProvider.GetRequiredService<IConfiguration>()["Federation:OutboxMaxAttempts"]` — создаёт недиспозимый scope ради чтения singleton `IConfiguration`, тогда как `DispatchOnceAsync` уже держит `using var scope` (строка 74). Нарушает CLAUDE.md §2 «минимум кода» и оставляет неосвобождённый scope на каждый вызов.

Что сделать: инжектировать `IConfiguration` в конструктор `OutboxDispatcher` (как это делают `OutboxWriter` и консюмеры) и читать значение напрямую.

Критерий закрытия: `GetMaxAttempts` не создаёт `IServiceScope`; конфигурация читается из инжектированной зависимости.
