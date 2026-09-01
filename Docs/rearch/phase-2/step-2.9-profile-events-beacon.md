# Этап 2.9 — Профильные события через федерацию + регистрация Beacon в Navigator

## Цель

Кешированные профили remote-пользователей не протухают: смена имени/username/аватара/bio и деактивация распространяются push-событиями на ноды-партнёры. Beacon регистрирует ноду в Navigator с federation-полями — discovery-источник «Navigator» работает для прод-нод.

Этап добавлен в роадмап по итогам перепроверки фазы (см. [README.md](README.md), «Решения фазы»): обе задачи объявлены в [../01-addressing-identity.md](../01-addressing-identity.md)/[../08-service-migration.md](../08-service-migration.md), но отсутствовали в исходной таблице Фазы 2.

## Контекст

- Профильные события: [../01](../01-addressing-identity.md), «Проблемы этого слоя»; консюмер-таблица в [../04-federation-service.md](../04-federation-service.md), поверхность 3.
- Регистрация: [../08](../08-service-migration.md), секция Beacon; валидация регистрации на стороне Navigator сделана в 1.5.
- Требуются 2.1 (upsert RemoteUsers) и 2.2 (outbox, `EnqueueOutbound`).

## Изменение 1 — Messages: ноды-партнёры пользователя

Новый server-RPC `MessagesServerApi.GetFederatedPeersForUser(user_uuid) → repeated server_name` (proto-добавление, совместимо): ноды remote-участников активных fed-чатов пользователя. Данные членства — у Messages; Federation своей копии не ведёт.

## Изменение 2 — Federation: исходящие профильные события

Консюмеры существующих Queue-событий профиля (`UserChangedName/Username/Avatar/Bio` — фактические имена классов посмотри в `Shared/BarkFluff.Shared.Queue`; убедись, что события несут `Uuid` пользователя — если нет, расширь их nullable-полем и заполни в Users):

- `Federation:Enabled` и у события есть uuid → `GetFederatedPeersForUser` → `UserProfileChangedPayload` (актуальный профиль запросить у Users, privacy-фильтрация как в S2S `GetUserProfile` — отдаём только видимое) → outbox для каждой ноды (`ChatId = NULL`, порядок не важен — LWW по `origin_ts_ms` на приёме).
- Деактивация/удаление аккаунта: найди существующее событие Users (или добавь `UserDeactivatedEvent` в Queue + публикацию в Users) → `UserDeactivatedPayload` → те же ноды.
- Коалессация: несколько изменений подряд → отправляется актуальный профиль; слить дубликаты в outbox не обязательно (LWW на приёме), но не плодить события чаще раза в N секунд на пользователя (in-memory дебаунс).

## Изменение 3 — входящие `profile_changed` / `user_deactivated`

Маршрутизация в пайплайне 2.2 (заглушки → реализация):

- `UserProfileChangedPayload`: `user.server_name == origin` (уже проверено правилом «за своих») → `Users.UpsertRemoteUsers` (правила пиннинга из 2.1 действуют; LWW — по `origin_ts_ms` против `LastSyncedAt`, старое событие игнорируется с OK).
- `UserDeactivatedPayload`: пометить `RemoteUsers.IsDeactivated = true`. Копии чатов живут ([../01](../01-addressing-identity.md)); выдача показывает профиль деактивированным.
- Смена username: конфликт `UNIQUE (Username, ServerName)` разрешается правилом 2.1 (свежий резолв побеждает).

## Изменение 4 — Beacon: расширенная регистрация в Navigator

Beacon уже регистрирует ноду в Navigator (существующий механизм — найди его). Расширить запрос federation-полями:

- `server_name` = `Federation:ServerName`, `federation_endpoint` = `Federation:ExternalEndpoint` (из Configuration; Beacon отдаёт `server_name` в `GetServerInfoResponse` с 0.4);
- `signing_keys`/`tls_spki_sha256` **не отправлять**: Navigator при валидации регистрации сам фетчит `/.well-known/barkfluff` (1.5) — он источник ключей; Beacon не ходит в Federation. Если контракт 0.4 (`navigator_api.proto`, `ServerInfo`) требует ключи в запросе регистрации — оставь поля пустыми, Navigator заполняет из well-known; зафиксируй это поведение в [../03-discovery.md](../03-discovery.md) одной строкой.
- Пустой `Federation:ServerName`/`Enabled=false` → регистрация как раньше, без federation-полей.

## Чего НЕ делать

- Аватар как файл (рендер/прокси) — Фаза 3; здесь только `avatar_file_id`-ссылка в кеше.
- GDPR-очистка данных на чужих нодах — отдельная проработка (№23).
- Пересинхронизация всех профилей скопом — только событийная + существующий TTL-lazy из 2.1.

## Критерии готовности

1. Стенд: смена имени пользователя node1 → `RemoteUsers` node2 обновилась (без ручного резолва); смена username → FID обновился, старый username освободился; деактивация → профиль на node2 помечен.
2. Событие со старой меткой (ручная подача через `EnqueueOutbound`) не откатывает свежий профиль.
3. Privacy: скрытый bio не появляется на ноде-партнёре после события профиля.
4. Beacon: рестарт ноды → `Navigator.GetServerByName(server_name)` возвращает federation-поля (endpoint, ключи из well-known); нода с выключенной федерацией регистрируется по-старому.
5. Юнит-тесты: маппинг событий → payload, LWW-приём профиля, деактивация — зелёные.
6. Obsidian: `Backend/Users.md`, `Backend/Federation.md`, `Backend/Beacon.md`, `Backend/Navigator.md` дополнены.
7. Коммит: `feat(rearch-phase2): 2.9 — профильные события + регистрация Beacon в Navigator`.
