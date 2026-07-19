# Этап 2.1 — Users: RemoteUsers, резолв FID, S2S-профиль

## Цель

Нода умеет представлять пользователей чужих нод: таблица `RemoteUsers` (кеш профилей), клиентский резолв `@username:servername`, S2S-отдача профиля своих пользователей с privacy-фильтрацией, server-RPC для Federation и Messages.

## Контекст

- Три идентификатора, формат FID, схема `RemoteUsers`, happy-path резолва: [../01-addressing-identity.md](../01-addressing-identity.md) — главный документ этапа.
- `ResolveRemoteUser` (internal API Federation): [../04-federation-service.md](../04-federation-service.md), поверхность 2.
- Сводка изменений Users: [../08-service-migration.md](../08-service-migration.md), секция Users.
- Риски: №3 (протухание FID), №4 (пиннинг UUID к ServerName), С-3.4 из [../11-plan-review.md](../11-plan-review.md) (коллизия remote-uuid с локальным) — все закрываются здесь.
- Proto уже есть (0.4): `UsersApi.ResolveFederatedUser`, `FederationInternalApi.ResolveRemoteUser`, `FederationS2SApi.GetUserProfile`.

## Изменение 1 — миграция UsersDb: таблица `RemoteUsers`

Схема из [../01](../01-addressing-identity.md) + флаг деактивации (нужен 2.9):

```
RemoteUsers
  Uuid           uuid PK
  Username       text NOT NULL
  ServerName     text NOT NULL      -- punycode A-label lowercase
  FirstName      text NULL
  LastName       text NULL
  Bio            text NULL
  AvatarFileId   text NULL          -- file_id на origin (рендер — Фаза 3)
  IsDeactivated  bool NOT NULL DEFAULT false
  LastSyncedAt   timestamptz NOT NULL
  UNIQUE (Username, ServerName)
```

Помни про баг `dotnet ef migrations add` (правило 5 README).

## Изменение 2 — FID-парсер и валидация upsert'а

Общий хелпер (например `Services/FidParser.cs`): разбор `@username:servername` (допускается без `@`), username по существующему формату `^[a-zA-Z0-9_]{3,32}$`, servername — DNS-hostname, punycode-нормализация к A-label lowercase через `IdnMapping` (тот же подход, что в `ServernameValidator` Federation из 1.4 — сравни, не дублируй логику бездумно). FID без `:servername` или с servername своей ноды (`Federation:ServerName` из Configuration) = локальный пользователь.

Правила upsert'а `RemoteUsers` (единая точка записи, используется резолвом и 2.9):

- **uuid существует в локальной `Users.Uuid` → отказ** (вредоносная нода заявляет uuid нашего пользователя — «remote-двойник»);
- **uuid уже известен с другим `ServerName` → отказ** (пиннинг UUID к ноде, №4) + warning-лог + метрика;
- конфликт `UNIQUE (Username, ServerName)` — username освободился/занялся на чужой ноде: побеждает свежий резолв, старая запись переименовывается по данным нового (см. [../01](../01-addressing-identity.md), «Смена username»).

Юнит-тесты: таблица кейсов парсера (валидные/невалидные/punycode-хомограф/без @/локальный servername) + все три правила upsert'а.

## Изменение 3 — proto: server-RPC Users (добавление, обратно-совместимо)

В `users_api.proto`, сервис `UsersServerApi` (строка ~127; проверь свободные номера):

```protobuf
  // Federation: батч-upsert кешей remote-профилей (валидация — правила пиннинга)
  rpc UpsertRemoteUsers(UpsertRemoteUsersRequest) returns (UpsertRemoteUsersResponse);

  // Messages: батч-чтение профилей по uuid (локальные и remote вперемешку)
  rpc GetUsersByUuid(GetUsersByUuidRequest) returns (GetUsersByUuidResponse);
```

`UpsertRemoteUsersRequest` — repeated профилей (uuid, username, server_name, имена, bio, avatar_file_id); ответ — per-запись результат (ok/rejected + причина). `GetUsersByUuidResponse` — repeated профилей с признаком `is_remote` и `server_name`. Состав полей выведи из `RemoteUsers` и существующих ответов Users; стиль — соседние RPC файла.

## Изменение 4 — клиентский резолв `ResolveFederatedUser`

Реализация `UsersApi.ResolveFederatedUser` (была `Unimplemented`):

1. Парсинг FID (Изменение 2). Локальный → существующий путь поиска по username.
2. Remote: если запись в `RemoteUsers` свежая (`LastSyncedAt` < TTL, например 24 ч) — отдать из кеша.
3. Иначе — gRPC `FederationInternalApi.ResolveRemoteUser(fid)` (клиент Federation: конфиг `FederationService:Host/Token` — дефолты заведены в 0.1; образец клиента — любой существующий межсервисный клиент Users).
4. Ответ → upsert `RemoteUsers` → отдать клиенту (`found=false` — честно, без ошибки).
5. `Federation:Enabled = false` → сразу `found=false` (или существующий код ошибки «федерация выключена» — посмотри, что заведено в Фазе 1, не выдумывай новый).

`SearchUsers`: строка, матчащаяся на FID-паттерн (есть `:` + валидный разбор) → ветка резолва (единичный результат), не trigram-поиск.

## Изменение 5 — S2S-отдача профиля своих пользователей

Две части:

- **Users**: server-RPC (добавь в `UsersServerApi`: `GetFederatedProfile(username | uuid)`) — профиль локального пользователя с privacy-фильтрацией. Переиспользуй логику существующего публичного `GetUserByUsername` (поля `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility` — прочитай актуальные имена в Privacy-домене Users): скрытое поле = пустое в ответе; профиль скрыт целиком → `found=false`. Также фильтровать `IsDraft`.
  > **Согласованное отклонение (P2-04):** «деактивированный/забаненный → `found=false`» **не реализуется** — в домене `User` нет состояния деактивации/бана (есть только `IsDraft`/`IsBot`), концепта бана нет даже локально. Вводить lifecycle-состояние ради федерации — вне скоупа Фазы 2. Когда/если такой флаг появится (напр. вместе с сервисом блокировок из [../09](../09-problems-open-questions.md)) — добавить его в этот фильтр. `RemoteUsers.IsDeactivated` на приёмной стороне уже есть под будущее.
- **Federation**: реализация `FederationS2SApi.GetUserProfile` (была `Unimplemented`): XFed уже проверил подпись (1.3) → вызов `Users.GetFederatedProfile` → маппинг в `GetUserProfileResponse`. Блоклист-проверка origin — как в остальных S2S-обработчиках 1.3/1.4.

## Изменение 6 — `FederationInternalApi.ResolveRemoteUser`

Реализация в Federation (была `Unimplemented`): парсинг fid/uuid+server_name → `ServerResolver` из 1.4 (discovery, анти-SSRF, блоклист) → S2S `GetUserProfile` на ноду-владельца (подписанный клиент из 1.3) → ответ Users. Не пишет в `RemoteUsers` сам — это делает Users (Изменение 4, п. 4); Federation остаётся без пользовательского состояния.

## Изменение 7 — метрики

Users: `federated_resolves{result}` (cache/resolved/not_found/error), `remote_users_upsert_rejected{reason}`. Federation: `s2s_profile_requests` — если счётчики S2S-запросов из 1.3 уже покрывают per-RPC, не дублируй.

## Чего НЕ делать

- Privacy `AllowFederatedDm` — этап 2.5 (домен/БД; proto-поле уже есть из 0.4).
- События профиля (`UserChanged*` → федерация, входящие `profile_changed`) — этап 2.9.
- Рендер/проксирование аватаров — Фаза 3.
- Клиентские UI-изменения — Фаза 5 (проверка резолва — grpcurl/тесты).

## Критерии готовности

1. Юнит-тесты парсера FID и правил upsert'а (Изменение 2) — зелёные; существующие тесты Users — без регрессий.
2. Стенд: с node1 `ResolveFederatedUser("@<user>:node2")` (grpcurl с пользовательским токеном) возвращает профиль; в `RemoteUsers` node1 появилась запись; повторный вызов идёт из кеша (видно по логам/метрике).
3. Privacy: у пользователя node2 скрыт bio → в ответе node1 bio пустой; профиль скрыт целиком → `found=false`.
4. Негатив: резолв с uuid, совпадающим с локальным `Users.Uuid`, отклонён; резолв несуществующего username → `found=false`; `Federation:Enabled=false` → резолв не ходит в сеть.
5. Obsidian: `Backend/Users.md` дополнен (RemoteUsers, резолв, server-RPC), `Backend/Federation.md` — GetUserProfile/ResolveRemoteUser.
6. Коммит: `feat(rearch-phase2): 2.1 — Users RemoteUsers + резолв FID + S2S-профиль`.
