# BarkFluff.Users

Управление профилями пользователей, бейджами, устройствами и GDPR-экспортом. Порт: **7001**.

Расположение: `Backend/BarkFluff.Users/`

→ [[Backend/Users-ProjectMap]] — карта всех файлов и классов проекта
→ [[Backend/Users-Metrics]] — реестр метрик сервиса (через `ServiceMetrics`-логи в Seq)
→ [[Backend/Users-ChatFolders-ClientGuide]] — гайд для клиентских агентов: какие RPC папок чатов когда вызывать, ошибки, edge cases

## Сборка

```bash
dotnet build BarkFluff.Users.csproj
dotnet ef database update --project BarkFluff.Users.csproj
```

Миграции применяются автоматически при старте.

> ⚠️ **Известный баг тулинга (актуально минимум на `dotnet ef`/EFCore 10.0.8):** `dotnet ef migrations add` в этом окружении падает `System.MissingMethodException: Method not found: 'System.String Microsoft.EntityFrameworkCore.Diagnostics.AbstractionsStrings.ArgumentIsEmpty(System.Object)'`. Затрагивает любой сервис, не только Users. Обход — писать миграцию вручную: 3 файла в `Persistence/Migrations/` (`{timestamp}_{Name}.cs` Up/Down, `{timestamp}_{Name}.Designer.cs` с `[Migration]`+`[DbContext]`+полным `BuildTargetModel`, и обновить `*ContextModelSnapshot.cs`). Образец в Users — `20260417120000_AddUserPrivacy.*`. Проверка drift без БД (эта команда РАБОТАЕТ, в отличие от `migrations add`): `dotnet ef migrations has-pending-model-changes --project ... --no-build`. **Важно:** если в snapshot руками прописан `.HasDefaultValueSql(...)`/`.ValueGeneratedOnAdd()` (как у `User.Uuid` выше), это же нужно продублировать Fluent-конфигом в `OnModelCreating` — иначе snapshot разойдётся с рантайм-моделью и `ctx.Database.Migrate()` при старте упадёт `PendingModelChangesWarning` (крэш-луп контейнера).

## Архитектура

### Два gRPC-сервиса

- `UsersApiService` — клиентское API (`[Authorize(Policy = nameof(TokenType.User))]`)
- `UsersServerApiService` — межсервисное API (`[Authorize(Policy = nameof(TokenType.Service))]`)

### Слои

- `Domain/` — `User` (включая `Uuid` — глобальный идентификатор для федерации, см. ниже), `UserContact`, `Badge`, `UserBadge`, `UserDevice`, `Privacy`, `UserPersonalization`, `ChatFolder`, `DevicePrekeyBundle`, `OneTimePrekey`
- `Features/` — MediatR команды/запросы (CQRS)
- `Host/` — gRPC-сервисы
- `Persistence/Services/` — `UsersStorage`, `DevicesStorage`, `PrivacyStorage`, `PersonalizationStorage`, `ChatFolderStorage`, `PrekeyStorage` (Transient)
- `Infrastructure/` — `UserInfoQueueSender` (RabbitMQ события, Scoped)
- `Services/` — `ReservedUsernamesService` (Singleton)
- `Mapping/` — extension-методы маппинга доменных объектов в protobuf

### Ключевые паттерны

**Draft-пользователи**: регистрация двухфазная — `AddDraftUser` (IsDraft=true) → `ConfirmUser` (IsDraft=false). Identity управляет процессом.

**ID пользователя**: `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` при создании — миллисекунды снижают шанс коллизии PK при быстрой регистрации и сохраняют монотонность по времени (важно для `OrderByDescending(u => u.Id)`). Уникальность username/email гарантируется регистронезависимыми UNIQUE-индексами `LOWER("Username")` / `LOWER("Email")` + обработкой `PostgresException 23505` в `UsersStorage.CreateUser` (страховка от гонки check-then-act).

**Uuid пользователя** (Фаза 0 rearch-федерации, [[Backend/Configuration#ServiceId=15]]): `Guid`, генерируется в коде (`Guid.NewGuid()`) при создании в обоих путях — `CreateUser` (draft) и `CreateBotUser`. Уникальный индекс `IX_Users_Uuid` (миграция `AddUserUuid`). У колонки есть страховочный `defaultValueSql: "gen_random_uuid()"` на уровне БД — заполнил все существующие строки при бэкфилле и подстрахует прямые INSERT в обход кода (например [[Backend/Users-Rust|Rust-порт]]). `long Id` остаётся внутренним PK, `Uuid` — будущий межсерверный идентификатор, пока без federation-логики поверх него. Отдаётся в proto `User.uuid` (поле 13, строка). Этап 0.4: `UsersApi.ResolveFederatedUser` (RPC-заглушка, реализация — Фаза 2) + `PrivacySettings.deny_federated_dm` (field 7, default false = федеративные DM разрешены) добавлены в `users_api.proto`, логики за ними пока нет.

**Remote-пользователи** (этап 2.1, docs/rearch/01-addressing-identity.md): кеш профилей пользователей чужих нод живёт в `Domain/RemoteUser` (миграция `AddRemoteUsers`):

| Поле | Тип | Описание |
|------|-----|---------|
| `Uuid` | `Guid` | UUID с origin-ноды (PK) |
| `Username` | `string` | текущий username (кеш, может протухнуть) |
| `ServerName` | `string` | punycode A-label lowercase (как `KnownServer.ServerName` в [[Backend/Federation]]) |
| `FirstName` / `LastName` / `Bio` | `string?` | профильные поля |
| `AvatarFileId` | `string?` | FileId на origin (рендер/проксирование — Фаза 3) |
| `IsDeactivated` | `bool` | флаг деактивации (нужен 2.9) |
| `LastSyncedAt` | `DateTime` | когда профиль обновлялся с origin |

`UNIQUE (Username, ServerName)` — при коллизии побеждает свежий резолв (старая запись по другому UUID удаляется, новая создаётся).

- **`Services/FidParser.cs`** — разбор `@username:servername` (допускается без `@`): username по regex `^[a-zA-Z0-9_]{3,32}$` (переиспользует `UsernameFormatValidator`), servername через `IdnMapping` к punycode A-label lowercase + проверки DNS/IP/localhost (тот же подход, что `ServernameValidator` в Federation). FID без servername (или с ownServerName из `Federation:ServerName`) → локальный пользователь. `LooksLikeFid` — для ветки резолва в `SearchUsers`.
- **`Persistence/Services/RemoteUsersStorage.cs`** — единая точка записи в кеш. Правила пиннинга:
  - UUID совпадает с локальной `Users.Uuid` → отказ `LocalUuidCollision` (защита от вредоносной ноды, заявляющей чужой UUID).
  - UUID уже известен с другим `ServerName` → отказ `ServerNameMismatch` (пиннинг UUID к ноде, docs/rearch/01 §"Спуфинг").
  - Конфликт `UNIQUE (Username, ServerName)` с другим UUID → побеждает свежий резолв (старая запись удаляется, создаётся новая).
- **Резолв FID** (`Features/ResolveFederatedUser`): локальный → поиск по username; remote — кеш в `RemoteUsers` (TTL 24ч), при отсутствии/протухании — gRPC к [[Backend/Federation]] `ResolveRemoteUser` и upsert кеша. `Federation:Enabled=false` (если ключ виден Users через конфиг) — `found=false` без похода в сеть. RPC-ошибки Federation не валят запрос — клиенту честный `found=false`.
- **Server-RPC** (этап 2.1, для Federation/Messages):
  - `UpsertRemoteUsers` — батч-upsert от Federation (валидация через `RemoteUsersStorage`).
  - `GetUsersByUuid` — батч-чтение профилей по UUID для Messages (локальные + remote вперемешку, признак `is_remote`).
  - `GetFederatedProfile` — профиль локального пользователя с privacy-фильтрацией (для S2S `Federation.GetUserProfile`); `found=false` если профиль скрыт целиком или пользователь draft.

**Зарезервированные имена**: `ReservedUsernamesService` (Singleton) загружает из конфига `ReservedNames:Usernames` (через запятую).

**Поиск (клиентский)**: `SearchUsersByTrigram` — trigram fuzzy matching (pg_trgm, порог 0.3), сортировка по `GREATEST(similarity)` DESC. Пустой запрос → пустой результат. Максимум 50 записей. Учитывает `SearchVisible` из Privacy. Данные и общий счёт берутся ОДНИМ запросом через оконную функцию `COUNT(*) OVER()` (`Database.SqlQueryRaw<TrigramSearchRow>` → ручной маппинг в `User`) — без второго trigram-скана. `IsBot`/`Uuid` выбираются и маппятся в результат, поэтому клиенты получают `User.is_bot`/`User.uuid`.

**Поиск (серверный)**: `SearchUsersServer` — через `GetAllUsersDescending` (если query пустой — все) или поиск по username/user_id. Без ограничения приватности, но только для не-ботов: это источник раздела «Юзеры» AdminPanel; боты управляются во вкладке «Боты».

**Бейджи**: уникальное ограничение (UserId, BadgeId). `Priority` — меньше = выше приоритет (default 1000). Только активные баджи включаются при `GetUserBadges`. Создание всегда устанавливает `IsActive=true`.

**Устройства**: `RegisterDevice` — upsert по DeviceId (Guid), обновляет все поля + `AuthorizedAt` (вызывается при логине). Хранит AppName, OS, Location, FirebaseDeviceToken. `UpdateDeviceAppInfo` — лёгкое обновление только `OriginalName` + `AppName` при refresh access-токена; пишет в БД только если значения изменились, `AuthorizedAt`/OS/Location не трогает (`DevicesStorage.UpdateDeviceAppInfoIfChanged`).

**Prekey-bundle (X3DH)**: один bundle на устройство (1:1 с `UserDevice`, PK = DeviceId). Содержит `IdentityPubkey` (Ed25519, постоянный), `SignedPrekey*` (X25519 + signature, ротируется), `RegistrationId` (libsignal), `SignedPrekeyRotatedAt`. Пул `OneTimePrekey` (Many:1, уникальный (DeviceId, PrekeyId)) — расходные ключи. `FetchPrekeyBundle` атомарно claim'ит одну prekey через PostgreSQL `DELETE ... RETURNING ... FOR UPDATE SKIP LOCKED` — параллельные запросы не получают одну и ту же. Сервер не валидирует подпись signed prekey — это делает клиент-получатель через identity_pubkey.

## UsersApi — клиентский (TokenType.User)

| Метод                                    | Описание                                      | Примечания                                                           |
| ---------------------------------------- | --------------------------------------------- | -------------------------------------------------------------------- |
| `GetUser(userId)`                        | Профиль пользователя                          | `userId=0` → текущий пользователь; содержит `profile_poster_file_id` |
| `SetProfilePicture(fileId)`              | Установить аватарку                           | `fileId` — GUID файла из Files; пустой → удалить аватар              |
| `CheckExistUsername(username)`           | Проверить существование username              | **`[AllowAnonymous]`**                                               |
| `CheckExistEmail(email)`                 | Проверить существование email                 | **`[AllowAnonymous]`**                                               |
| `ChangeName(firstName, lastName)`        | Изменить имя и фамилию                        | Trim, RabbitMQ `UserChangedName`                                     |
| `ChangeUsername(username)`               | Изменить username                             | Trim, RabbitMQ `UserChangedUsername`                                 |
| `ChangeBio(bio)`                         | Изменить описание профиля                     | RabbitMQ `UserChangedBio`                                            |
| `SearchUsers(query, pagination)`         | Поиск по имени/фамилии/username               | Trigram; макс 50; уважает `SearchVisible`                            |
| `GetUserBadges(userId, limit)`           | Получить баджи                                | Только активные; `limit=1` для списков, `limit=3` для профиля        |
| `GetDevices()`                           | Список устройств текущего пользователя        |                                                                      |
| `GetCurrentDevice()`                     | Текущее устройство                            | DeviceId — из JWT-claim (`UserContext.DeviceId`)                     |
| `RenameDevice(deviceId, customName)`     | Переименовать устройство                      |                                                                      |
| `SetFirebaseToken(firebaseToken)`        | Установить FCM-токен текущего устройства      | DeviceId — из JWT-claim (`UserContext.DeviceId`)                     |
| `SetNotificationsEnabled(enabled)`       | Включить/выключить push на текущем устройстве | DeviceId — из JWT-claim (`UserContext.DeviceId`)                     |
| `GetPrivacySettings()`                   | Настройки приватности текущего пользователя   |                                                                      |
| `UpdatePrivacySettings(settings)`        | Обновить настройки приватности                |                                                                      |
| `GetPersonalization()`                   | Персонализация текущего пользователя          |                                                                      |
| `UpdatePersonalization(personalization)` | Обновить персонализацию                       | Полная замена `ChatBackgroundFileIds`                                |
| `GetProfilePoster()`                     | Получить FileId постера профиля               | Быстрый аналог GetPersonalization только для постера                 |
| `SetProfilePoster(fileId)`               | Установить (или удалить) постер профиля       | Пустой `fileId` → удаление; не трогает `ChatBackgroundFileIds`       |
| `RegisterPrekeyBundle(...)`              | Зарегистрировать X3DH bundle текущего устройства | Идемпотентно (повторный вызов перезаписывает identity/signed prekey, дополняет one-time pool без дубликатов). DeviceId — из JWT |
| `FetchPrekeyBundle(userId, deviceId)`    | Получить bundle устройства собеседника        | Атомарно расходует одну one-time prekey через `DELETE...RETURNING ... FOR UPDATE SKIP LOCKED` (PostgreSQL). Возвращает `remaining_one_time_prekeys` для проактивного пополнения |
| `ListPeerDevices(userId)`                | Список устройств пользователя с флагом `has_bundle` | Без Privacy-фильтра на этой итерации; для UI выбора целевого устройства секретного чата |
| `ReplenishOneTimePrekeys(prekeys[])`     | Дополнить пул one-time prekeys                | Дубликаты по PrekeyId игнорируются; возвращает общее количество |
| `RotateSignedPrekey(signedPrekey)`       | Сменить signed prekey                         | Старый затирается; обновляется `SignedPrekeyRotatedAt` |
| `GetChatFolders()`                       | Список папок чатов текущего пользователя      | Сортировка по `SortOrder, Id`                                        |
| `CreateChatFolder(name, icon)`           | Создать новую папку чатов                     | Имя обязательное (≤64 символа); `FolderId` (Guid) генерируется на сервере; `SortOrder = max+1` |
| `UpdateChatFolder(folder_id, ...)`       | Частичное обновление папки                    | `optional` поля имени/иконки; `has_chat_list_update` — флаг замены списка чатов |
| `DeleteChatFolder(folder_id)`            | Удалить папку                                 | Чужие папки не находятся → `ChatFolderNotFoundException`             |
| `AddChatToFolder(folder_id, chat_id)`    | Добавить чат в папку                          | Идемпотентно (повтор не дублирует)                                   |
| `RemoveChatFromFolder(folder_id, chat_id)` | Убрать чат из папки                         | Идемпотентно (отсутствующий — no-op)                                 |
| `ReorderChatFolders(orders[])`           | Массовое изменение `SortOrder` папок          | Чужие `folder_id` молча игнорируются                                 |

## UsersServerApi — межсервисный (TokenType.Service)

| Метод | Описание | Примечания |
|-------|----------|-----------|
| `FindByLogin(username\|email)` | Найти пользователя по логину | `oneof login` |
| `CheckExistUsername(username)` | Проверить существование username | |
| `CheckExistEmail(email)` | Проверить существование email | |
| `AddDraftUser(...)` | Создать черновик пользователя | IsDraft=true |
| `OverrideDraftUser(...)` | Перезаписать данные черновика | |
| `ConfirmUser(userId)` | Подтвердить пользователя | IsDraft=false, создаёт Privacy |
| `GetById(userId)` | Получить пользователя по Id | |
| `GetUserContacts(userId)` | Получить email пользователя | |
| `ListByIds(ids[])` | Получить список пользователей по Id | |
| `AssignUserBadge(userId, badgeId, priority?)` | Назначить бадж пользователю | |
| `RemoveUserBadge(userId, badgeId)` | Снять бадж с пользователя | |
| `UpdateUserBadgePriority(userId, badgeId, newPriority)` | Обновить приоритет баджа | |
| `CreateBadge(name, description, imageUrl)` | Создать новый бадж | `IsActive=true` по умолчанию |
| `GetAllBadges(includeInactive)` | Получить все баджи | |
| `UpdateBadge(id, name, description, imageUrl, isActive)` | Обновить бадж | |
| `DeleteBadge(id)` | Удалить бадж | |
| `ExportData(userId)` | GDPR-экспорт данных пользователя | 3 файла: `profile.json`, `messages.json`, `files.json` |
| `RegisterDevice(...)` | Зарегистрировать устройство | Атомарный PostgreSQL upsert по DeviceId; повторная авторизация обновляет данные и переносит устройство текущему пользователю без конфликта PK. Вызывается Identity при авторизации |
| `UpdateDeviceAppInfo(deviceId, userId, originalName, appName)` | Обновить имя устройства + версию приложения | Вызывается Identity при refresh токена; пишет только при изменении, возвращает `updated: bool` |
| `GetUserDevices(userId)` | Список устройств пользователя | |
| `DeleteUserDevice(deviceId, userId)` | Удалить устройство | |
| `GetUserByUsername(username)` | Публичная информация для веб-сервера | Реализован как MediatR-фича `Features/GetUserByUsername` (Query+Handler). Применяет Privacy: `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility`. Возвращает `profile_poster_url` (field 7) — получается из PersonalizationStorage + FilesServerApi.GetFileData |
| `GetUserPrivacy(userId)` | Настройки приватности пользователя | Для других микросервисов (напр. Onliner) |
| `SearchUsersServer(query, offset, size)` | Поиск пользователей для AdminPanel | Пустой query → все пользователи убыванию ID; макс 50 |
| `UpdateStorageLimit(userId, storageLimit_gb)` | Обновить лимит хранилища | 1–250 ГБ |
| `SetProfilePictureServer(userId, url, previewUrl)` | Установить аватарку через URL | Для AdminPanel; не требует FileId |
| `GetDevicesWithFirebaseTokens(userIds[])` | Получить FCM-токены устройств | Для CloudMessaging/push-уведомлений |
| `GetDevicesWithFirebaseTokensByDeviceIds(deviceIds[])` | Получить FCM-токены по списку DeviceId | Для админ-рассылки (точечной) |
| `GetAllDevicesWithFirebaseTokens()` | Получить FCM-токены **всех** устройств с включёнными уведомлениями | Для админ-рассылки (broadcast) |
| `UpdateProfileServer(userId, first_name, last_name, bio, username)` | Обновить профиль пользователя (service-to-service) | |
| `SetProfilePosterServer(userId, poster_file_id)` | Установить (или удалить) постер профиля (service-to-service) | Пустой `poster_file_id` → удаление |
| `GetProfilePosterServer(userId)` | Получить FileId постера профиля (service-to-service) | |
| `CreateBotUser(username, first_name, bypass_username_rules)` | Создать бот-юзера для [[Backend/Bots]] | Сразу `IsDraft=false, IsBot=true`, без `UserContact` (nullable), Privacy по умолчанию. Идемпотентен: username занят ботом → его id + `already_existed` |
| `DeleteBotUser(user_id)` | Пометить бот-аккаунт удалённым | Username → `deleted_{id}` (освобождается), `IsDraft=true` (вне поиска). Чаты сохраняются |
| `GetFederatedProfile(username\|uuid)` | Privacy-фильтрованный профиль локального пользователя (для [[Backend/Federation]] S2S GetUserProfile) | Этап 2.1. Скрытое поле = пустое; профиль скрыт целиком/draft → `found=false` |
| `UpsertRemoteUsers(records[])` | Батч-upsert кешей remote-профилей от Federation | Этап 2.1. Per-запись: `ok` / `LocalUuidCollision` / `ServerNameMismatch` / `InvalidUuid` |
| `GetUsersByUuid(uuids[])` | Батч-чтение профилей для [[Backend/Messages]] (render remote-авторов) | Этап 2.1. Локальные + remote вперемешку, признак `is_remote`/`server_name` |

### Боты (интеграция с [[Backend/Bots]])

- `User.IsBot` (миграция `AddUserIsBot`), поле `is_bot` в proto `User` (12) и `GetUserByUsernameResponse` (8).
- `UserContact` стал **nullable** — у ботов нет email (null-guard в GetUserContacts/OverrideDraftUser).
- Суффикс **`bot`** в username зарезервирован: `UsernameFormatValidator.HasBotSuffix()` + отказ (`UsernameBotSuffixReservedException`) в AddDraftUser/OverrideDraftUser/ChangeUsername. Системные боты (напр. `botfather`) создаются с `bypass_username_rules`.

## Приватность

`Privacy` (1:1 с `User`). Поля: `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility`, `EmailVisibility`, `OnlineVisibility` (enum `All=0, Friends=1, None=2`), `SearchVisible`, `DenyFederatedDm` (bool, default false, этап 2.5).

Запись создаётся автоматически в `ConfirmUser`. Дефолты: профиль виден всем, email скрыт (`None`).

FRIENDS трактуется как NONE до появления сервиса отношений.

Применение:
- `GetUserByUsername` — если `!ProfileVisibleOnSite` → found=false; avatar/bio фильтруются по visibility; poster_url получается через FilesServerApi (если ошибка — пустая строка)
- `SearchUsers` — `SearchVisible=false` исключает из поиска, но пользователь видит сам себя
- [[Backend/Onliner]] — `OnlineVisibilityFilter` по `OnlineVisibility`

### DenyFederatedDm (этап 2.5, docs/rearch/05-chat-replication.md)

Запрет входящих федеративных DM. Действует **только на создание новых** fed-чатов — уже
существующие продолжают получать сообщения (намеренно, семантика «запретить писать мне первым»).

- `GetUsersByUuidQueryHandler` отдаёт флаг в `UserProfileByUuid.deny_federated_dm` для локальных
  пользователей (батч через `PrivacyStorage.GetByUserIds` — без похода в БД на каждого); для remote
  всегда `false` (не их privacy). [[Backend/Messages]] использует значение при резолве invitee в
  `ImportFederatedChat` — второй gRPC-поход в Users на каждый импорт не нужен.
- Proto `deny_federated_dm` в `PrivacySettings` (7) появился ещё в 0.4; маппинг `PrivacyMapping`
  (`ToGrpc`/`ToDomain`) и `PrivacyStorage.Update` дополнены этапом 2.5.

## Персонализация

`UserPersonalization` (1:1 с `User`). Поля:

| Поле | Тип | Описание |
|------|-----|---------|
| `ProfilePosterFileId` | `string?` | FileId файла-постера профиля (обложка страницы пользователя) |
| `ChatBackgroundFileIds` | `string[]` | Массив FileId фоновых изображений чатов, загруженных пользователем |

Запись создаётся по требованию при первом обращении (`GetOrCreate`). Хранится в таблице `UserPersonalizations` (PostgreSQL `text[]` для массива).

## Папки чатов

`ChatFolder` (1:Many с `User`). Поля:

| Поле | Тип | Описание |
|------|-----|---------|
| `Id` | `long` | Внутренний PK (identity) |
| `OwnerUserId` | `long` | FK на `User`, индексирован |
| `FolderId` | `Guid` | Публичный идентификатор, уникальный индекс — клиент работает только через него |
| `FolderName` | `string` | Название (≤64 символа после Trim) |
| `FolderIcon` | `string?` | Произвольная строка-идентификатор иконки (emoji / имя пресета / URL) |
| `ChatList` | `long[]` | PostgreSQL `bigint[]` — ID чатов из Messages-сервиса |
| `SortOrder` | `int` | Порядок отображения; меньше = выше |

**Приватность:** все Storage-методы фильтруют `OwnerUserId == _userContext.UserId` — чужие папки невидимы и неизменяемы. NotFound → `ChatFolderNotFoundException`.

**Денормализация:** `ChatList` хранит ID чатов плоским массивом без FK на Messages БД (это разные сервисы). Удалить чат на стороне Messages не вычистит его из ChatList — клиент сам отвечает за консистентность через `RemoveChatFromFolder`.

**Клиентская интеграция:** подробный гайд по выбору RPC под конкретные UX-сценарии, ошибки, идемпотентность, поведение при гонках устройств — [[Backend/Users-ChatFolders-ClientGuide]].

## GDPR-экспорт (ExportData)

Вызывается через `UsersServerApi.ExportData`. Возвращает массив `JsonFile`:

| Файл | Содержимое |
|------|-----------|
| `profile.json` | Профиль: id, username, firstName, lastName, registrationDate, profilePicture, bio, storageLimitGb |
| `messages.json` | Все сообщения + чаты (через MessagesServerApi.GetUserAllMessages) |
| `files.json` | Метаданные всех файлов вложений + аватарки (через FilesServerApi.GetFilesData) |

При ошибке получения данных добавляются `messages_error.json` / `files_error.json`.

## RabbitMQ-события

| Событие | Триггер |
|---------|---------|
| `UserChangedName` | ChangeName |
| `UserChangedUsername` | ChangeUsername |
| `UserChangedAvatar` | SetProfilePicture |
| `UserChangedPassword` | (через серверный API от Identity) |
| `UserChangedBio` | ChangeBio |

## Метрики (MetricsCollector)

| Ключ | Инкрементируется при |
|------|---------------------|
| `user_lookups` | GetUser, GetById, ListByIds |
| `profile_updates` | SetProfilePicture, ChangeName, ChangeUsername, ChangeBio |
| `user_searches` | SearchUsers |
| `firebase_token_updates` | SetFirebaseToken |
| `device_registrations` | RegisterDevice |
| `device_app_info_updates` | UpdateDeviceAppInfo |
| `chat_folder_lookups` | GetChatFolders |
| `chat_folder_creates` | CreateChatFolder |
| `chat_folder_updates` | UpdateChatFolder |
| `chat_folder_deletes` | DeleteChatFolder |
| `chat_folder_chat_adds` | AddChatToFolder |
| `chat_folder_chat_removes` | RemoveChatFromFolder |
| `chat_folder_reorders` | ReorderChatFolders |
| `chat_mute_lookups` | GetChatMute (per-chat mute) |
| `chat_mute_toggles` | SetChatMute (per-chat mute) |
| `bot_users_create_requests` | CreateBotUser (service-to-service) |
| `bot_users_delete_requests` | DeleteBotUser (service-to-service) |
| `profile_updates_server` | UpdateProfileServer (service-to-service) |

## Конфигурация

| Ключ | Описание |
|------|---------|
| `UsersDb` | PostgreSQL connection string |
| `RabbitMQ:*` | RabbitMQ |
| `FilesService:Host/Token` | gRPC-клиент Files |
| `MessagesService:Host/Token` | gRPC-клиент Messages |
| `ReservedNames:Usernames` | Зарезервированные имена (через запятую) |

## Per-chat mute (отключение уведомлений для чата)

Отключение push-уведомлений на уровне **отдельного чата** (в отличие от `UserDevice.NotificationsEnabled`, который глушит всё устройство).

- **Entity** `Domain/ChatMute.cs`: `Id`, `UserId` (FK→User Cascade), `ChatId` (Guid), `MutedUntil` (DateTime?, `null` = навсегда). Уникальный индекс `(UserId, ChatId)`. Таблица `ChatMutes` (миграция `AddChatMutes`).
- **Семантика muted:** строка есть И (`MutedUntil == null` ИЛИ `MutedUntil > UtcNow`). Снятие mute удаляет строку.
- **Storage** `Persistence/Services/ChatMuteStorage.cs`: `SetMuted/Unmute/GetActiveMutes/GetMutedChatIds`.
- **RPC `UsersApi`:** `SetChatMuted(chat_id, muted, muted_until)`, `GetMutedChats()`.
- **RPC `UsersServerApi`:** `GetMutedChatIds(user_id, chat_ids)` — батч для флага `muted` в [[Backend/Messages]].
- **Push-фильтр:** `DevicesStorage.GetDevicesWithFirebaseTokens(userIds, mutedChatFilter)` — при заданном `chat_id` исключает замьютивших. `GetDevicesWithFirebaseTokensRequest` получил поле `chat_id`; [[Backend/CloudMessaging]] пробрасывает туда `ChatId` события.

## Proto

- `users_api.proto` — Server
- `files_api.proto` — Client (валидация аватара, получение файлов для ExportData)
- `messages_api.proto` — Client (GDPR-экспорт)

## Связанные файлы

- [[Backend/Identity]] — Draft → Confirm flow, RegisterDevice при авторизации
- [[Backend/Onliner]] — проверка OnlineVisibility через GetUserPrivacy
- [[Backend/Files]] — валидация аватара
- [[Backend/AdminPanel]] — SearchUsersServer, UpdateStorageLimit, SetProfilePictureServer, бадж-управление
- [[Backend/CloudMessaging]] — GetDevicesWithFirebaseTokens для push-уведомлений
