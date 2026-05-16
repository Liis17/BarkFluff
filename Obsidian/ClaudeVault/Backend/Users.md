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

## Архитектура

### Два gRPC-сервиса

- `UsersApiService` — клиентское API (`[Authorize(Policy = nameof(TokenType.User))]`)
- `UsersServerApiService` — межсервисное API (`[Authorize(Policy = nameof(TokenType.Service))]`)

### Слои

- `Domain/` — `User`, `UserContact`, `Badge`, `UserBadge`, `UserDevice`, `Privacy`, `UserPersonalization`, `ChatFolder`, `DevicePrekeyBundle`, `OneTimePrekey`
- `Features/` — MediatR команды/запросы (CQRS)
- `Host/` — gRPC-сервисы
- `Persistence/Services/` — `UsersStorage`, `DevicesStorage`, `PrivacyStorage`, `PersonalizationStorage`, `ChatFolderStorage`, `PrekeyStorage` (Transient)
- `Infrastructure/` — `UserInfoQueueSender` (RabbitMQ события, Scoped)
- `Services/` — `ReservedUsernamesService` (Singleton)
- `Mapping/` — extension-методы маппинга доменных объектов в protobuf

### Ключевые паттерны

**Draft-пользователи**: регистрация двухфазная — `AddDraftUser` (IsDraft=true) → `ConfirmUser` (IsDraft=false). Identity управляет процессом.

**ID пользователя**: `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` при создании.

**Зарезервированные имена**: `ReservedUsernamesService` (Singleton) загружает из конфига `ReservedNames:Usernames` (через запятую).

**Поиск (клиентский)**: `SearchUsersByTrigram` — trigram fuzzy matching (pg_trgm, порог 0.3), сортировка по `GREATEST(similarity)` DESC. Пустой запрос → пустой результат. Максимум 50 записей. Учитывает `SearchVisible` из Privacy.

**Поиск (серверный)**: `SearchUsersServer` — через `GetAllUsersDescending` (если query пустой — все) или поиск по username/user_id. Без ограничения приватности.

**Бейджи**: уникальное ограничение (UserId, BadgeId). `Priority` — меньше = выше приоритет (default 1000). Только активные баджи включаются при `GetUserBadges`. Создание всегда устанавливает `IsActive=true`.

**Устройства**: `RegisterDevice` — upsert по DeviceId (Guid). Хранит AppName, OS, Location, FirebaseDeviceToken.

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
| `RegisterDevice(...)` | Зарегистрировать устройство | Upsert по DeviceId; вызывается Identity при авторизации |
| `GetUserDevices(userId)` | Список устройств пользователя | |
| `DeleteUserDevice(deviceId, userId)` | Удалить устройство | |
| `GetUserByUsername(username)` | Публичная информация для веб-сервера | Применяет Privacy: `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility`. Возвращает `profile_poster_url` (field 7) — получается из PersonalizationStorage + FilesServerApi.GetFileData |
| `GetUserPrivacy(userId)` | Настройки приватности пользователя | Для других микросервисов (напр. Onliner) |
| `SearchUsersServer(query, offset, size)` | Поиск пользователей для AdminPanel | Пустой query → все пользователи убыванию ID; макс 50 |
| `UpdateStorageLimit(userId, storageLimit_gb)` | Обновить лимит хранилища | 1–250 ГБ |
| `SetProfilePictureServer(userId, url, previewUrl)` | Установить аватарку через URL | Для AdminPanel; не требует FileId |
| `GetDevicesWithFirebaseTokens(userIds[])` | Получить FCM-токены устройств | Для CloudMessaging/push-уведомлений |
| `GetDevicesWithFirebaseTokensByDeviceIds(deviceIds[])` | Получить FCM-токены по списку DeviceId | Для админ-рассылки (точечной) |
| `GetAllDevicesWithFirebaseTokens()` | Получить FCM-токены **всех** устройств с включёнными уведомлениями | Для админ-рассылки (broadcast) |

## Приватность

`Privacy` (1:1 с `User`). Поля: `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility`, `EmailVisibility`, `OnlineVisibility` (enum `All=0, Friends=1, None=2`), `SearchVisible`.

Запись создаётся автоматически в `ConfirmUser`. Дефолты: профиль виден всем, email скрыт (`None`).

FRIENDS трактуется как NONE до появления сервиса отношений.

Применение:
- `GetUserByUsername` — если `!ProfileVisibleOnSite` → found=false; avatar/bio фильтруются по visibility; poster_url получается через FilesServerApi (если ошибка — пустая строка)
- `SearchUsers` — `SearchVisible=false` исключает из поиска, но пользователь видит сам себя
- [[Backend/Onliner]] — `OnlineVisibilityFilter` по `OnlineVisibility`

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
| `chat_folder_lookups` | GetChatFolders |
| `chat_folder_creates` | CreateChatFolder |
| `chat_folder_updates` | UpdateChatFolder |
| `chat_folder_deletes` | DeleteChatFolder |
| `chat_folder_chat_adds` | AddChatToFolder |
| `chat_folder_chat_removes` | RemoveChatFromFolder |
| `chat_folder_reorders` | ReorderChatFolders |

## Конфигурация

| Ключ | Описание |
|------|---------|
| `UsersDb` | PostgreSQL connection string |
| `RabbitMQ:*` | RabbitMQ |
| `FilesService:Host/Token` | gRPC-клиент Files |
| `MessagesService:Host/Token` | gRPC-клиент Messages |
| `ReservedNames:Usernames` | Зарезервированные имена (через запятую) |

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
