# Этап 2.3 — Messages: схема fed-чатов, импорт, SendMessage с uuid-peer, выдача

## Цель

Messages умеет создавать федеративные DM (исходящие) и принимать их копии (входящие): миграция схемы, `ImportFederatedChat`/`ImportFederatedMessage`, `SendMessage`/`GetPersonChatId` с uuid-получателем, заполнение расширенных Queue-событий, выдача чатов/сообщений с remote-участниками. После этапа сообщение с node1 появляется у пользователя node2 в realtime.

## Контекст

- Жизненный цикл fed-DM, валидации импорта: [../05-chat-replication.md](../05-chat-replication.md) — главный документ, следуй разделу «Валидация импортируемых событий» дословно.
- Сводка изменений Messages: [../08-service-migration.md](../08-service-migration.md), секция Messages.
- Proto уже есть (0.4): `oneof` c `user_uuid` в `SendMessageRequest.source_id` и `GetPersonChatIdRequest`, поля `ChatMember.user_uuid/server_name`, `Message.federated_id/sender_uuid`, RPC `ImportFederatedChat/Message` в `MessagesServerApi`.
- Требуется выполненный 2.1 (`GetUsersByUuid`, `UpsertRemoteUsers`) и 2.2 (расширенные Queue-события, пайплайн доставки).

## Изменение 1 — миграция MessagesDb

Колонки 0.3 (`FederatedId`, `SenderUuid`, `LastChangeAt`, `ChatMembers.UserUuid`) уже есть. Добавить:

- `Chats.IsFederated bool NOT NULL DEFAULT false`;
- `Chats.FederatedStatus int NOT NULL DEFAULT 0` — enum `Active | Rejected | Merged` (Rejected — 2.5, Merged — 2.7; колонка сразу, чтобы не делать три миграции);
- `Chats.FederatedUuidLow uuid NULL`, `Chats.FederatedUuidHigh uuid NULL` — нормализованная пара UUID участников fed-DM (упорядочение — каноническое сравнение Guid, см. 2.7/README) + частичный уникальный индекс `WHERE IsFederated AND FederatedStatus = Active`. Образец нормализованной пары — `PrivateUserLowId/HighId` в `Domain/Chat.cs`;
- уникальный индекс `Messages (ChatId, FederatedId) WHERE FederatedId IS NOT NULL` — идемпотентность импорта;
- таблица `FederatedMessageEvents (ChatId uuid, FederatedId uuid, EventBytes bytea, ReceivedAt timestamptz, PK (ChatId, FederatedId))` — последний применённый state-event входящего сообщения (wire-байты `FederationEvent`); нужна catch-up'у (2.6), писать начинаем сразу здесь.

## Изменение 2 — общая валидация импорта

Хелпер (используют 2.3/2.4): 

- **clamp метки**: `origin_ts_ms > now + 5 мин` (окно подписи) → отказ `TimestampInFuture` (permanent);
- лимиты контента = локальным (текст ≤ 4096 и т.д. — возьми фактические константы из существующих валидаторов Messages);
- origin-нода — участник чата: `ServerName` remote-участника чата == origin события.

Ошибки — новые исключения с guid-кодами по существующему паттерну `x-error-code` (посмотри реестр исключений проекта). Federation мапит их на `REJECTED`/`RETRY`: валидационные — permanent (`REJECTED`), «чат неизвестен»/транзиентные — `RETRY`.

## Изменение 3 — `ImportFederatedChat` (обработчик ChatCreated)

Вызывается Federation (маршрутизация из 2.2, `TokenType.Service`). Логика:

1. `invitee.uuid` — локальный пользователь (`Users.GetUsersByUuid`); не найден/деактивирован → `REJECTED:UnknownInvitee`.
2. Перед созданием — upsert профиля инициатора: `Users.UpsertRemoteUsers([initiator])`; отказ upsert'а (коллизия uuid, пиннинг) → `REJECTED`.
3. Чат с этим `chat_id` уже есть → OK (идемпотентность). Активный fed-DM той же UUID-пары с **другим** `chat_id` → пока `REJECTED:DuplicateFederatedDm` (протокол слияния — 2.7, там этот ответ заменяется).
4. Создать копию: `Chat { Id = chat_id, IsFederated, члены: local(UserId+UserUuid), remote(UserUuid, UserId = NULL) }`, пара `FederatedUuidLow/High`.
5. Privacy-проверка `AllowFederatedDm` — **в 2.5**; здесь принимать всех (default и так «разрешено»).

## Изменение 4 — `ImportFederatedMessage`

1. Чат неизвестен → `RETRY:ChatUnknown` (catch-up дотянет — 2.6; из `NewMessagePayload` чат не воссоздать — нет данных второго участника).
2. Чат `Rejected/Merged` → см. 2.5/2.7; пока только `Active`.
3. `(ChatId, FederatedId)` уже есть → OK (идемпотентность).
4. Валидации (Изменение 2) + `sender.uuid` — remote-участник этого чата.
5. Вставка `Message { FederatedId, SenderUuid, SenderId = NULL, Text, SentAt = origin_ts, LastChangeAt = origin_ts }`. **Вложения не рендерятся** (Фаза 3): факт наличия сохранить (например пустой список + флаг/кол-во — реши минимально), текст доставляется.
6. Записать wire-байты события в `FederatedMessageEvents` (Изменение 1).
7. Опубликовать обычный `NewMessageEvent` (+ федеративные поля) → Updates/CloudMessaging ноды-получателя работают штатно — это главный выигрыш модели.

Проверь `Message.SenderId`: сейчас наверняка `NOT NULL` — миграция в NULL-able (или sentinel — **не надо**, только NULL) и аудит кода на `SenderId`-обращения без null-проверки в затрагиваемых путях выдачи.

## Изменение 5 — исходящий путь: `SendMessage`/`GetPersonChatId` с uuid

- `GetPersonChatIdRequest.user_uuid`: найти активный fed-DM по нормализованной паре (uuid отправителя — через `Users.GetUsersByUuid` по его `user_id`; закешируй в существующем Redis-кеше пользовательских данных Messages).
- `SendMessageRequest.source_id.user_uuid`: чата нет → создать (`Chat.Id = новый Guid`, члены, пара, `IsFederated`); uuid неизвестен в `RemoteUsers` → ошибка «сначала резолв» (клиент всегда резолвит перед отправкой). Сообщение: `FederatedId = новый uuid`, `SenderUuid`, `LastChangeAt = SentAt`.
- Публикация `NewMessageEvent` с заполненными федеративными полями (2.2): `IsFederated`, `RemoteParticipants`, `FederatedId`, `SenderUuid`, `LastChangeAt`, `IsFirstMessageInChat` (при создании чата), `SenderDisplayName/SenderFid` (для 2.8; имя — из профиля отправителя).
- **Publisher confirms** при публикации (№37): проверь конфигурацию MassTransit (актуальную опцию смотри в доках MassTransit через Context7); если включение глобально для Messages рискованно — включи для федеративных событий и зафиксируй в коммите.
- Отправка в чат со статусом `Rejected`/`Merged` → понятная ошибка (код заведи сейчас, UX-поток — 2.5/2.7).
- `AddUser` (группы) с uuid/FID → `FederatedGroupsNotSupported` (новое исключение + guid-код).

## Изменение 6 — выдача

`ListChats`/`ListMessages`/`GetChatInfo`/`ListChatMembers` (фактические имена RPC проверь в `messages_api.proto`):

- remote-участники: `ChatMember { user_uuid, server_name }`, `Message.sender_uuid`; имена — батч `Users.GetUsersByUuid` + существующий Redis-кеш имён (расширь ключи на uuid);
- не падать на `UserId = NULL` (аудит хендлеров выдачи затронутых путей);
- сортировка: уже по `SentAt` (`Persistence/Services/MessagesStorage.cs`, `ChatsStorage.cs` — проверено) — добавь tie-break `ThenBy(Id)` для стабильности при равных метках, в т.ч. в пагинации;
- Updates: проверить, что расширенный `NewMessageEvent` сериализуется в стрим-payload с `sender_uuid` (поле `Message.sender_uuid` в proto уже есть; Updates почти не меняется — [../08](../08-service-migration.md)).

## Изменение 7 — стенд

`Backend/dev-federation-testbed/` (1.3/1.6): дополнить стек node2 сервисами users, messages, updates (+ их БД) — иначе E2E-проверки фазы невозможны. Redis/RabbitMQ node2 — по образцу основного compose. Это разовая инфраструктурная работа этапа; последующие этапы стенд только используют.

## Чего НЕ делать

- Edit/Delete/Read — 2.4. Privacy-отказ — 2.5. Catch-up — 2.6. Слияние — 2.7 (здесь временный `REJECTED:DuplicateFederatedDm`).
- Вложения/файлы — Фаза 3. Клиентские UI — Фаза 5.
- Не менять поведение нефедеративных чатов: все новые ветки — под `IsFederated`/наличием uuid.

## Критерии готовности

1. Юнит-тесты: валидации импорта (таблица: чужой origin, метка из будущего, лимит текста, дубль FederatedId, неизвестный чат → RETRY), нормализация UUID-пары, `SendMessage` c uuid (создание чата, повторная отправка в существующий) — зелёные; существующие тесты Messages — без регрессий.
2. Стенд, критерий роадмапа: пользователь node1 отправляет `SendMessage(user_uuid=@user:node2)` → у пользователя node2 сообщение приходит в realtime через существующий стрим Updates; в БД обеих нод чат с одним `Chat.Id` и сообщение с одним `FederatedId`.
3. Отложенный E2E-критерий 2.2: остановить node2 → отправить N (например 5) сообщений → поднять node2 → все дошли ровно один раз, порядок в чате сохранён (outbox-упорядочивание + идемпотентность).
4. Обратная совместимость: обычный локальный чат (существующий клиент/тест) — отправка/выдача без изменений поведения.
5. Obsidian: `Backend/Messages.md` (схема, импорт, uuid-peer, выдача), `Backend/Federation.md` (маршрутизация импорта включена).
6. Коммит: `feat(rearch-phase2): 2.3 — Messages импорт/отправка федеративных DM`.
