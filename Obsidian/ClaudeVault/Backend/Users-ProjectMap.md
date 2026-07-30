# BarkFluff.Users — Карта проекта

Расположение: `Backend/BarkFluff.Users/`
Порт: **7001** | .NET 10 | PostgreSQL (EF Core + Migrations) | MassTransit (RabbitMQ)

← [[Backend/Users]]

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Конфигурация DI, gRPC, EF Core, MassTransit, XAuth; запуск миграций при старте |
| `appsettings.json` | Базовая конфигурация |
| `appsettings.Development.json` | Конфигурация для локальной разработки |
| `Dockerfile.slim` | Образ для CI и production |
| `Properties/launchSettings.json` | Профили запуска VS |

---

## Domain — доменные модели

| Файл | Назначение |
|------|-----------|
| `Domain/User.cs` | Основная сущность пользователя: Id (unix timestamp), Username, FirstName, LastName, IsDraft, ProfilePicture, ProfilePicturePreviewUrl, Bio, StorageLimitGb |
| `Domain/UserContact.cs` | Email пользователя (1:1 с User) |
| `Domain/UserDevice.cs` | Авторизованное устройство: OriginalName, CustomName, AppName, OperationSystem, Location, FirebaseDeviceToken, NotificationsEnabled |
| `Domain/Privacy.cs` | Настройки приватности (1:1 с User): ProfileVisibleOnSite, AvatarVisibility, BioVisibility, EmailVisibility, OnlineVisibility, SearchVisible |
| `Domain/ProfileFieldVisibility.cs` | Enum: `All=0`, `Friends=1`, `None=2` |
| `Domain/Badge.cs` | Бадж: Name, Description, ImageUrl, IsActive |
| `Domain/UserBadge.cs` | Связь пользователь↔бадж: UserId, BadgeId, Priority, IsActive |
| `Domain/UserPersonalization.cs` | Персонализация (1:1 с User): ProfilePosterFileId, ChatBackgroundFileIds (массив) |
| `Domain/ChatFolder.cs` | Папка чатов (1:Many с User): Id, OwnerUserId, FolderId (Guid), FolderName, FolderIcon, ChatList (bigint[]), SortOrder |
| `Domain/DevicePrekeyBundle.cs` | X3DH bundle устройства (1:1 с UserDevice, PK=DeviceId): RegistrationId, IdentityPubkey, SignedPrekeyId/Public/Signature, SignedPrekeyRotatedAt, CreatedAt |
| `Domain/OneTimePrekey.cs` | Разовая prekey (Many:1 с UserDevice, уникальный (DeviceId, PrekeyId)): PrekeyId, PublicKey, CreatedAt — расходуется FetchPrekeyBundle |
| `Domain/ChatMute.cs` | Per-chat mute (уникальная пара UserId+ChatId): признак заглушённого чата пользователя |

---

## Host — gRPC-сервисы

| Файл | Назначение |
|------|-----------|
| `Host/UsersApiService.cs` | Клиентский gRPC-сервис `[Authorize(TokenType.User)]`. Обрабатывает запросы от клиентских приложений: профиль, поиск, баджи, устройства, приватность, персонализация |
| `Host/UsersServerApiService.cs` | Межсервисный gRPC `[Authorize(TokenType.Service)]`. Обрабатывает запросы от других микросервисов: регистрация, поиск, бадж-управление, GDPR-экспорт, FCM-токены |

---

## Features — CQRS (MediatR)

### Пользователи — основные операции

| Файл | Назначение |
|------|-----------|
| `Features/AddDraftUser/` | Создание черновика пользователя (IsDraft=true). Вызывается Identity при регистрации |
| `Features/OverrideDraftUser/` | Перезапись данных черновика пользователя |
| `Features/ConfirmUser/` | Подтверждение пользователя (IsDraft=false) + создание записи Privacy |
| `Features/GetUser/` | Получение профиля по userId (0 = текущий пользователь) |
| `Features/FindByLogin/` | Поиск пользователя по username или email (oneof login) |
| `Features/ListByIds/` | Получение списка пользователей по массиву Id |
| `Features/GetUserContacts/` | Получение email пользователя |
| `Features/CheckExistUsername/` | Проверка существования username |
| `Features/CheckExistEmail/` | Проверка существования email |
| `Features/SearchUsers/` | Trigram fuzzy-поиск клиентский (pg_trgm, порог 0.3, макс 50, учитывает SearchVisible) |
| `Features/SearchUsersServer/` | Поиск для AdminPanel: пустой query → все пользователи убывания ID; без ограничений приватности |
| `Features/SetProfilePicture/` | Установка аватарки (fileId из Files-сервиса; пустой → удалить) |
| `Features/SetProfilePictureServer/` | Установка аватарки через прямой URL (для AdminPanel) |
| `Features/ChangeName/` | Смена имени/фамилии + RabbitMQ `UserChangedName` |
| `Features/ChangeUsername/` | Смена username + RabbitMQ `UserChangedUsername` |
| `Features/ChangeBio/` | Смена описания профиля + RabbitMQ `UserChangedBio` |
| `Features/UpdateStorageLimit/` | Обновление лимита хранилища (1–250 ГБ) |
| `Features/ExportData/` | GDPR-экспорт: profile.json + messages.json (MessagesServerApi) + files.json (FilesServerApi) |
| `Features/GetUserByUsername/` | Поиск пользователя по username (без учёта регистра) |
| `Features/UpdateProfileServer/` | Обновление профиля (first_name/last_name/bio/username) от имени другого сервиса |
| `Features/CreateBotUser/` | Создание бот-юзера для [[Backend/Bots]] (service-to-service, идемпотентно) |
| `Features/DeleteBotUser/` | Пометка бот-аккаунта удалённым (username → `deleted_{id}`) |

### Устройства

| Файл | Назначение |
|------|-----------|
| `Features/Devices/RegisterDevice/` | Upsert устройства по DeviceId при авторизации. Вызывается Identity |
| `Features/Devices/GetDevices/` | Список устройств текущего пользователя |
| `Features/Devices/GetCurrentDevice/` | Текущее устройство (по DeviceId из токена) |
| `Features/Devices/GetUserDevices/` | Список устройств для межсервисного запроса |
| `Features/Devices/RenameDevice/` | Установка кастомного имени устройства |
| `Features/Devices/DeleteUserDevice/` | Удаление устройства |
| `Features/Devices/SetFirebaseToken/` | Сохранение FCM-токена текущего устройства |
| `Features/Devices/SetNotificationsEnabled/` | Включение/выключение push для текущего устройства |
| `Features/Devices/GetDevicesWithFirebaseTokens/` | Получение FCM-токенов по списку userId (для CloudMessaging) |

### Бейджи

| Файл | Назначение |
|------|-----------|
| `Features/Badges/Commands/CreateBadge/` | Создание нового баджа (IsActive=true по умолчанию) |
| `Features/Badges/UpdateBadge/` | Обновление данных баджа |
| `Features/Badges/DeleteBadge/` | Удаление баджа |
| `Features/Badges/Queries/GetAllBadges/` | Получение всех бейджей (с флагом includeInactive) |
| `Features/Badges/AssignUserBadge/` | Назначение баджа пользователю (уникальное ограничение UserId+BadgeId) |
| `Features/Badges/RemoveUserBadge/` | Снятие баджа с пользователя |
| `Features/Badges/UpdateUserBadgesPriority/` | Изменение приоритета баджа (меньше = выше) |
| `Features/Badges/GetUserBadges/` | Получение активных бейджей пользователя (limit=1 для списков, limit=3 для профиля) |

### Приватность

| Файл | Назначение |
|------|-----------|
| `Features/Privacy/GetPrivacySettings/` | Получение настроек приватности текущего пользователя |
| `Features/Privacy/UpdatePrivacySettings/` | Обновление настроек приватности |
| `Features/Privacy/GetUserPrivacyServer/` | Получение настроек приватности для других сервисов (Onliner) |

### Персонализация

| Файл | Назначение |
|------|-----------|
| `Features/Personalization/GetPersonalization/` | Получение персонализации (GetOrCreate) |
| `Features/Personalization/UpdatePersonalization/` | Полная замена ChatBackgroundFileIds |
| `Features/Personalization/GetProfilePoster/` | Быстрое получение только ProfilePosterFileId |
| `Features/Personalization/SetProfilePoster/` | Установка/удаление постера профиля (пустой fileId → удаление) |
| `Features/Personalization/GetProfilePosterServer/` | Получение ProfilePosterFileId (service-to-service) |
| `Features/Personalization/SetProfilePosterServer/` | Установка/удаление постера профиля (service-to-service) |

### Папки чатов

| Файл | Назначение |
|------|-----------|
| `Features/ChatFolders/GetChatFolders/` | Список папок текущего пользователя (сортировка SortOrder, Id) |
| `Features/ChatFolders/CreateChatFolder/` | Создать папку, авто-генерация FolderId (Guid) и SortOrder = max+1; имя ≤ 64 символов |
| `Features/ChatFolders/UpdateChatFolder/` | Частичное обновление (имя/иконка/полная замена ChatList) |
| `Features/ChatFolders/DeleteChatFolder/` | Удалить папку по Guid |
| `Features/ChatFolders/AddChatToFolder/` | Добавить chat_id в ChatList (идемпотентно) |
| `Features/ChatFolders/RemoveChatFromFolder/` | Удалить chat_id из ChatList (идемпотентно) |
| `Features/ChatFolders/ReorderChatFolders/` | Массовое изменение SortOrder (чужие FolderId игнорируются) |

### Заглушённые чаты (per-chat mute)

| Файл | Назначение |
|------|-----------|
| `Features/ChatMutes/GetMutedChatIds/` | Список ChatId заглушённых чатов текущего пользователя |
| `Features/ChatMutes/GetMutedChats/` | Полный список заглушённых чатов |
| `Features/ChatMutes/SetChatMuted/` | Заглушить/снять заглушение чата (upsert/delete) |

### Prekey-bundle (X3DH)

| Файл | Назначение |
|------|-----------|
| `Features/Prekeys/RegisterPrekeyBundle/` | Регистрация bundle текущего устройства (идемпотентно). DeviceId из JWT |
| `Features/Prekeys/FetchPrekeyBundle/` | Получение bundle устройства собеседника с атомарным расходом одной one-time prekey |
| `Features/Prekeys/ListPeerDevices/` | Устройства пользователя + флаг has_bundle |
| `Features/Prekeys/ReplenishOneTimePrekeys/` | Пополнение пула one-time prekeys (дубликаты по PrekeyId игнорируются) |
| `Features/Prekeys/RotateSignedPrekey/` | Смена signed prekey текущего устройства |

---

## Persistence — слой данных

| Файл | Назначение |
|------|-----------|
| `Persistence/Contexts/UsersContext.cs` | EF Core DbContext: Users, UserContacts, Badges, UserBadges, UserDevices, Privacies, UserPersonalizations, ChatFolders |
| `Persistence/Contexts/UsersContextExtensions.cs` | Extension-методы для контекста (применение миграций и пр.) |
| `Persistence/Contexts/UsersContextFactory.cs` | IDesignTimeDbContextFactory для EF CLI |
| `Persistence/Extensions/PostgresFullTextSearchExtensions.cs` | Extension для trigram-поиска (pg_trgm) |
| `Persistence/Services/UsersStorage.cs` | CRUD пользователей: поиск, создание, обновление, trigram-поиск |
| `Persistence/Services/DevicesStorage.cs` | CRUD устройств: upsert, получение, удаление |
| `Persistence/Services/PrivacyStorage.cs` | CRUD настроек приватности |
| `Persistence/Services/PersonalizationStorage.cs` | CRUD персонализации (GetOrCreate паттерн) |
| `Persistence/Services/ChatFolderStorage.cs` | CRUD папок чатов с фильтрацией по OwnerUserId; идемпотентные AddChat/RemoveChat; ReorderAsync |
| `Persistence/Services/ChatMuteStorage.cs` | CRUD per-chat mute: получение списка заглушённых чатов, upsert/delete по паре UserId+ChatId |
| `Persistence/Services/PrekeyStorage.cs` | CRUD prekey-bundle: RegisterBundleAsync (upsert), FetchBundleAsync (атомарно расходует one-time prekey через PostgreSQL `DELETE...RETURNING ... FOR UPDATE SKIP LOCKED`), ReplenishOneTimePrekeysAsync, RotateSignedPrekeyAsync, ListPeerDevicesAsync |
| `Persistence/Migrations/` | Миграции EF Core (история изменений схемы БД) |
| `Migrations/` | Дополнительные миграции (AddPreviewAvatar, AddBadgesTables, AddStorageLimitGb, AddUserDevicesTable, AddChatFolders, AddPrekeyBundles) |

---

## Infrastructure — внешние интеграции

| Файл | Назначение |
|------|-----------|
| `Infrastructure/UserInfoQueueSender.cs` | Публикация событий RabbitMQ через MassTransit: `UserChangedName`, `UserChangedUsername`, `UserChangedAvatar`, `UserChangedPassword`, `UserChangedBio` |

---

## Consumers — обработка событий из очереди

| Файл | Назначение |
|------|-----------|
| `Consumers/SessionRevokedConsumer.cs` | Подписка на `SessionRevokedEvent` (очередь `session-revoked-users`). Отзывает токен в `TokenRevocationCache` для немедленной инвалидации сессии |

---

## Mapping — маппинг proto ↔ domain

| Файл | Назначение |
|------|-----------|
| `Mapping/UserMapping.cs` | User → proto-ответ |
| `Mapping/BadgeMapping.cs` | Badge, UserBadge → proto-ответ |
| `Mapping/PrivacyMapping.cs` | Privacy → proto-ответ |
| `Mapping/PersonalizationMapping.cs` | UserPersonalization → proto-ответ |
| `Mapping/ChatFolderMapping.cs` | ChatFolder → proto-ответ (FolderId.ToString, FolderIcon ?? "") |
| `Mapping/PrekeyMapping.cs` | DevicePrekeyBundle / OneTimePrekey / UserDevice → proto SignedPreKey, OneTimePreKey, PrekeyBundle, PeerDeviceInfo |

---

## Services — прикладные сервисы

| Файл | Назначение |
|------|-----------|
| `Services/ReservedUsernamesService.cs` | Singleton. Загружает зарезервированные имена из конфига `ReservedNames:Usernames` (через запятую) и проверяет принадлежность username к списку |
| `Services/UsernameFormatValidator.cs` | Валидация формата username (`^[a-zA-Z0-9_]{3,32}$`) + проверка суффикса `bot` для бот-аккаунтов |

---

## Зависимости

```
BarkFluff.Users
  ├── BarkFluff.GrpcServer         (инфраструктура gRPC, XAuth, MetricsCollector)
  ├── BarkFluff.Shared.Auth        (JwtClientInterceptor, ExceptionClientInterceptor)
  ├── BarkFluff.Shared.Identity    (ServiceId, TokenType, IdentityClaims)
  ├── BarkFluff.Shared.Queue       (события RabbitMQ: UserChanged*, SessionRevokedEvent)
  ├── BarkFluff.Proto              (users_api.proto, files_api.proto, messages_api.proto, shared.proto)
  ├── MassTransit.RabbitMQ         (публикация и потребление событий)
  ├── MediatR                      (CQRS dispatcher)
  ├── Npgsql.EntityFrameworkCore   (PostgreSQL ORM)
  └── Serilog                      (логирование)
```

---

## Замечания по актуальности (сверка с кодом)

> Проверено: {{date:YYYY-MM-DD}}

- ✅ Документация в [[Backend/Users]] соответствует коду
- ✅ `SessionRevokedConsumer` в Obsidian **не упомянут** — добавлен в этот файл
- ✅ `UserInfoQueueSender` зарегистрирован как `Scoped` (не `Transient` как написано в Users.md) — в Program.cs: `AddScoped<UserInfoQueueSender>()`
- ✅ Все EF Core миграции находятся в `Persistence/Migrations/`.
