# Этап 4.1 — Messages: федеративный контекст членства + доступ к presence

## Цель

Messages начинает отдавать сервисам ноды то, чего им не хватает для маршрутизации presence/typing через федерацию:

1. `CheckChatMembership` умеет проверять **remote-участника по uuid** и возвращает per-chat федеративный контекст (чат федеративный? какие ноды и uuid в нём участвуют?) — Onliner узнаёт, куда слать typing, одним вызовом, который он и так делает на каждый heartbeat.
2. Новый `CheckFederatedPresenceAccess` — origin-сторона проверки «у ноды-подписчика есть основание видеть статус этого нашего пользователя» (риск №42: без неё любая нода сети массово мониторит presence).

Критерий роадмапа 4.1: ответ содержит данные для маршрутизации typing. Клиентского и сетевого кода здесь нет — только контракт и его наполнение.

## Контекст

- Механика presence/typing и роль membership-ответа — [../07-presence-typing.md](../07-presence-typing.md), разделы «Федеративная схема» и «Как Onliner(A) узнаёт, что чат федеративный».
- Требование проверки отношений — [../11-plan-review.md](../11-plan-review.md) С-2 и [../09-problems-open-questions.md](../09-problems-open-questions.md) №42.
- Принцип «Guid недостаточно» (знание chat_id не даёт прав) — [../05-chat-replication.md](../05-chat-replication.md), «Валидация импортируемых событий».

Текущее состояние кода (проверено при планировании):

- `messages_api.proto`: `CheckChatMembershipRequest { int64 user_id = 1; repeated string chat_ids = 2; }`, `CheckChatMembershipResponse { repeated string member_chat_ids = 1; }`.
- Реализация: `Features/CheckChatMembership/CheckChatMembershipQueryHandler.cs` → `ChatsStorage.GetMemberChatIds(userId, chatIds)` (простой `Where(m => m.UserId == userId && chatIds.Contains(m.ChatId))`).
- Единственный потребитель сегодня — Onliner (`Services/ChatMembershipFilter.cs`, fail-closed при ошибке).
- Федеративные поля уже есть: `Chat.IsFederated`, `Chat.FederatedStatus` (`Active|Rejected|Merged`), `ChatMember.UserId` (nullable), `ChatMember.UserUuid`, `ChatMember.ServerName`; хелперы — `ChatMemberExtensions.LocalUserIds()` / `RemoteParticipants()`.
- `GetFederatedPeersForUser` (заявлен в 2.9) на момент планирования **не реализован** — на него не опираться.

## Изменение 1 — proto (`messages_api.proto`, только добавления)

```protobuf
message CheckChatMembershipRequest {
  int64 user_id = 1;            // проверяемый локальный пользователь
  repeated string chat_ids = 2; // идентификаторы чатов (Guid-строки)
  string user_uuid = 3;         // проверяемый участник по UUID (remote или локальный);
                                // заполнять ЛИБО user_id, ЛИБО user_uuid
}

message CheckChatMembershipResponse {
  repeated string member_chat_ids = 1;                  // как раньше
  repeated FederatedChatContext federated_chats = 2;    // только для чатов из member_chat_ids
  string requester_uuid = 3;                            // UUID проверяемого пользователя
                                                        // (пусто, если у него его нет)
}

message FederatedChatContext {
  string chat_id = 1;
  repeated FederatedChatPeer peers = 2;  // remote-участники чата
}

message FederatedChatPeer {
  string user_uuid = 1;
  string server_name = 2;
}
```

Обратная совместимость: существующий вызов с `user_id` + чтением `member_chat_ids` работает без изменений; новые поля игнорируются старыми клиентами сервиса.

`requester_uuid` нужен typing-мосту (4.4): Onliner знает только `long userId` печатающего, а через границу ноды уходит uuid — иначе понадобился бы отдельный вызов в Users на каждый heartbeat.

## Изменение 2 — хендлер `CheckChatMembership`

`Features/CheckChatMembership/` — расширить команду и хендлер:

1. **Ветка идентификатора.** Заполнен `user_uuid` → членство ищется по `ChatMembers.UserUuid == uuid`; иначе — как сейчас по `UserId`. Оба поля пустые/нулевые → `InvalidArgument` (ошибка вызывающего, не «пустой ответ»).
2. **Федеративный контекст.** Для чатов, попавших в `member_chat_ids`, вернуть `FederatedChatContext` **только** если `Chat.IsFederated && Chat.FederatedStatus == Active`. `peers` — remote-участники этого чата (`ChatMemberExtensions.RemoteParticipants()`); нефедеративные чаты в `federated_chats` не попадают вовсе (пустой список = «все чаты локальные», рабочий случай для 99% вызовов).
3. **`requester_uuid`.** Для `user_id`-ветки — uuid этого участника из `ChatMembers.UserUuid` (у локального участника федеративного чата он заполнен с 2.3); если ни в одном из запрошенных чатов uuid не найден — вернуть пусто (не ходить в Users: это hot-path каждого typing-heartbeat). Для `user_uuid`-ветки — эхо запрошенного uuid.
4. **Один проход по БД.** Сейчас `GetMemberChatIds` выбирает только `ChatId`. Нужен метод в `ChatsStorage`, возвращающий за один запрос членство + федеративный контекст (например, выборка `ChatMembers` по `chatIds` с `Include`/join на `Chats` и проекцией в DTO). Не превращать это в N+1 по чатам — метод вызывается на каждый typing-heartbeat.
5. Лимит `chat_ids` в запросе — как сейчас (если ограничения нет, не вводить: вызывающий свой, сервисный).

## Изменение 3 — новый RPC `CheckFederatedPresenceAccess`

`MessagesServerApi` (`TokenType.Service`, зовёт только Federation своей ноды):

```protobuf
rpc CheckFederatedPresenceAccess(CheckFederatedPresenceAccessRequest)
    returns (CheckFederatedPresenceAccessResponse);

message CheckFederatedPresenceAccessRequest {
  string requesting_server = 1;      // нода-подписчик (канонический lowercase/punycode)
  repeated string user_uuids = 2;    // UUID НАШИХ пользователей, за которыми хотят следить
}

message CheckFederatedPresenceAccessResponse {
  repeated string allowed_user_uuids = 1;  // подмножество, по которым отдавать статус можно
}
```

Правило: `uuid` попадает в ответ, если существует чат, у которого `IsFederated && FederatedStatus == Active`, среди участников есть `ChatMember.UserUuid == uuid` **и** участник с `ServerName == requesting_server`. Иначе — молча не включать (не различать «нет чата» и «нет пользователя»: не светим существование аккаунтов).

- Батч-запрос: один SQL на весь список, без цикла по uuid.
- Жёсткий лимит размера входа (конфиг Messages не заводить — константа, например 500): превышение → `InvalidArgument`. Основной лимит подписки живёт в Federation (4.3), это вторая линия.
- `requesting_server` сравнивать в канонической форме (lowercase; `ChatMember.ServerName` хранится уже канонизированным — сверься с 2.3).

## Изменение 4 — Onliner: адаптация вызывающего кода (минимальная)

`Services/ChatMembershipFilter.cs` сейчас читает только `member_chat_ids`. В этом этапе:

- Расширить возвращаемый тип фильтра так, чтобы федеративный контекст и `requester_uuid` доходили до вызывающих хендлеров (например, возвращать небольшой record вместо `HashSet<string>`), **не меняя поведения**: фильтрация чатов по членству и fail-closed при ошибке остаются как есть.
- Потребители контекста появляются в 4.4 — здесь только «протянуть данные», без новой логики маршрутизации.

Это единственная правка вне Messages в данном этапе; делать её здесь, а не в 4.4, чтобы контракт и его потребитель менялись одним коммитом.

## Чего НЕ делать

- Никакой отправки typing/presence через федерацию — 4.3/4.4.
- Не трогать `GetChatMemberIds` (используется Calls) и остальные server-RPC.
- Не заводить кеш членства/контекста (Redis) — вызов и так индексированный; кеш без инвалидации на смене состава чата даст утечку доступа.
- Не реализовывать `GetFederatedPeersForUser` из 2.9 — это другой этап и другая задача (профильные события).

## Критерии готовности

1. Юнит-тесты (`Tests/BarkFluff.Messages.Tests`), таблицей:
   - `CheckChatMembership` по `user_id` для локального чата — ответ как раньше, `federated_chats` пуст;
   - по `user_id` для fed-чата — чат в `member_chat_ids`, в `federated_chats` ровно один peer с корректными `user_uuid`/`server_name`, `requester_uuid` заполнен;
   - по `user_uuid` для remote-участника fed-чата — членство подтверждается; для чужого uuid — нет;
   - fed-чат в статусе `Rejected`/`Merged` — членство отдаётся (чат существует), но `federated_chats` для него пуст;
   - оба идентификатора пусты → `InvalidArgument`.
2. Юнит-тесты `CheckFederatedPresenceAccess`: есть активный fed-чат с этой нодой → uuid разрешён; чат с другой нодой → нет; чат `Rejected` → нет; неизвестный uuid → нет; батч из N uuid обрабатывается одним запросом (проверить, что метод storage вызывается один раз); превышение лимита → `InvalidArgument`.
3. Существующие тесты Messages и Onliner зелёные; `dotnet build` для Messages и Onliner — успех.
4. Регрессия контракта: старый вызов `CheckChatMembership` (только `user_id` + `chat_ids`) даёт тот же результат, что до этапа — проверить тестом, а не на глаз.
5. Obsidian: `Backend/Messages.md` — раздел про федеративный контекст членства и `CheckFederatedPresenceAccess`; `Backend/Onliner.md` — упоминание расширенного ответа фильтра.
6. Коммит: `feat(rearch-phase4): 4.1 — федеративный контекст CheckChatMembership + CheckFederatedPresenceAccess`.
