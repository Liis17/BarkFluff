# Этап 2.5 — Privacy `AllowFederatedDm`, отказ до отправителя, квота ChatCreated

## Цель

Пользователь может запретить входящие федеративные DM; отказ доезжает до отправителя понятной ошибкой; нода защищена от спам-волны создания чатов квотой per-origin.

## Контекст

- Семантика privacy (действует только на новые чаты) : [../05-chat-replication.md](../05-chat-replication.md), «Ограничения MVP»; поток отказа — там же, «Создание чата».
- Квота `ChatCreated`: [../04-federation-service.md](../04-federation-service.md), «Безопасность публичной поверхности»; риск №22 в [../09-problems-open-questions.md](../09-problems-open-questions.md).
- Proto-поле `deny_federated_dm` в настройках приватности уже есть (0.4, users_api; **инверсия**: proto3-default `false` = «разрешено», как сейчас).
- Требуются выполненные 2.2 (DeadLetter-classification, точка публикации) и 2.3 (`ImportFederatedChat`, `FederatedStatus`).

## Изменение 1 — Users: домен privacy

- Privacy-модель Users (найди существующую сущность настроек приватности): поле `DenyFederatedDm bool NOT NULL DEFAULT false` + миграция.
- `GetPrivacySettings`/`UpdatePrivacySettings`: маппинг proto-поля `deny_federated_dm` (существующий стиль хендлеров).
- В `UsersServerApi` — включи флаг в ответ `GetUsersByUuid` (2.1) либо отдельным легковесным RPC `GetFederatedDmPolicy(uuid)` — выбери то, что проще по фактическому коду, но без второго похода в БД на каждый импорт: Messages кеширует ответ как остальные данные пользователей.

## Изменение 2 — Messages: отказ при создании

`ImportFederatedChat` (2.3): после проверки invitee — запрос политики invitee; `DenyFederatedDm == true` → исключение `FederatedDmRejected` (новый guid-код по паттерну `x-error-code`). Federation мапит его в `REJECTED` с `error_code = "FederatedDmRejected"` (permanent).

Семантика **только новых чатов**: существующий активный fed-DM не трогается — входящие сообщения в него продолжают приниматься при любом значении флага (зафиксировано в [../05](../05-chat-replication.md); тест обязателен).

## Изменение 3 — доведение отказа до отправителя

Цепочка (решение уровня фазы, см. [README.md](README.md)):

1. `Shared/BarkFluff.Shared.Queue`: новое событие `FederatedChatRejectedEvent { Guid ChatId, string Reason }`.
2. Federation (диспетчер, 2.2): событие ушло в DeadLetter с `error_code == FederatedDmRejected` → publish `FederatedChatRejectedEvent` (заглушка из 2.2 реализуется).
3. Messages: консюмер события → `Chat.FederatedStatus = Rejected`.
4. UX: отправка в `Rejected`-чат → ошибка `FederatedDmRejected` (код из Изменения 2; ветка в `SendMessage` заведена в 2.3). Push-уведомление об отказе не делаем; realtime-событие в Updates — только если это дёшево по существующим механизмам (например, событие обновления чата) — иначе клиент узнаёт при следующей отправке/ListChats. Не изобретай новый стрим.

## Изменение 4 — квота ChatCreated per-origin

В Federation, пайплайн `DeliverEvents` (2.2), перед маршрутизацией `ChatCreatedPayload`:

- счётчик в Redis: ключ `fed:chatcreated:{origin}:{часовое окно}`, инкремент с TTL;
- лимит из конфигурации `Federation:ChatCreatedHourlyLimit` (default 100; добавь в каталог Settings по образцу ключей 0.1);
- превышение → ответ `RETRY` (троттлинг: временное состояние, не порча события) + метрика `chatcreated_quota_exceeded{origin}` + warning-лог. Блок ноды при злоупотреблении — вручную из AdminPanel (страница 1.7, кнопка блока уже есть); автоалерт-UI — Фаза 6.

Дополнительно (если ещё не сделано в 1.3/1.6): базовый прикладной rate-limit per-origin на `DeliverEvents` (батчей/мин) — проверь, что nginx-зоны 1.6 покрывают, прикладной слой добавляй только при пробеле.

## Чего НЕ делать

- Пользовательская блокировка конкретных отправителей — вне скоупа (реестр №22: сервиса блокировок нет и для локальных).
- Клиентский тумблер UI — Фаза 5 (проверка через grpcurl `UpdatePrivacySettings`).
- Автоматический блок ноды по квоте — только метрика + ручной блок.

## Критерии готовности

1. Юнит-тесты: маппинг `deny_federated_dm`, отказ импорта при флаге, приём сообщений в существующий чат при включённом флаге, квота (превышение → RETRY) — зелёные.
2. Стенд, критерий роадмапа: пользователь node2 включает запрет (`UpdatePrivacySettings`) → новый fed-чат с node1 отклоняется; на node1 чат помечен `Rejected`, повторная отправка возвращает понятную ошибку `FederatedDmRejected` (проверить `x-error-code`).
3. Существующий fed-DM (созданный до включения флага) продолжает получать сообщения.
4. Квота на укороченном лимите (например 2/час через конфиг): третий `ChatCreated` с node1 → `RETRY` + метрика; после сброса окна — доставляется.
5. Obsidian: `Backend/Users.md` (DenyFederatedDm), `Backend/Messages.md` (Rejected-статус), `Backend/Federation.md` (квота, FederatedChatRejectedEvent), `Shared/Queue.md`.
6. Коммит: `feat(rearch-phase2): 2.5 — AllowFederatedDm, отказ до отправителя, квота ChatCreated`.
