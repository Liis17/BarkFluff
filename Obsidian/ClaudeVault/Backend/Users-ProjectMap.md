# BarkFluff.Users — Карта проекта

Расположение: `Backend/BarkFluff.Users/`
Порт: **7001** | .NET 9 | PostgreSQL (EF Core + Migrations) | MassTransit (RabbitMQ)

← [[Backend/Users]]

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Конфигурация DI, gRPC, EF Core, MassTransit, XAuth; запуск миграций при старте |
| `appsettings.json` | Базовая конфигурация |
| `appsettings.Development.json` | Конфигурация для локальной разработки |
| `Dockerfile` | Образ для production |
| `Dockerfile.slim` | Облегчённый образ |
| `Properties/launchSettings.json` | Профили запуска VS |
| `SECURITY_AUDIT.md` | Аудит безопасности сервиса |

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

---

## Persistence — слой данных

| Файл | Назначение |
|------|-----------|
| `Persistence/Contexts/UsersContext.cs` | EF Core DbContext: Users, UserContacts, Badges, UserBadges, UserDevices, Privacies, UserPersonalizations |
| `Persistence/Contexts/UsersContextExtensions.cs` | Extension-методы для контекста (применение миграций и пр.) |
| `Persistence/Contexts/UsersContextFactory.cs` | IDesignTimeDbContextFactory для EF CLI |
| `Persistence/Extensions/PostgresFullTextSearchExtensions.cs` | Extension для trigram-поиска (pg_trgm) |
| `Persistence/Services/UsersStorage.cs` | CRUD пользователей: поиск, создание, обновление, trigram-поиск |
| `Persistence/Services/DevicesStorage.cs` | CRUD устройств: upsert, получение, удаление |
| `Persistence/Services/PrivacyStorage.cs` | CRUD настроек приватности |
| `Persistence/Services/PersonalizationStorage.cs` | CRUD персонализации (GetOrCreate паттерн) |
| `Persistence/Migrations/` | Миграции EF Core (история изменений схемы БД) |
| `Migrations/` | Дополнительные миграции (AddPreviewAvatar, AddBadgesTables, AddStorageLimitGb, AddUserDevicesTable) |

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

---

## Services — прикладные сервисы

| Файл | Назначение |
|------|-----------|
| `Services/ReservedUsernamesService.cs` | Singleton. Загружает зарезервированные имена из конфига `ReservedNames:Usernames` (через запятую) и проверяет принадлежность username к списку |

---

## Helpers

| Файл | Назначение |
|------|-----------|
| `Helpers/PasswordHasher.cs` | SHA-256 хэширование пароля (legacy-утилита, пароли хранятся в Identity) |

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
- ✅ `PasswordHasher.cs` в Helpers — legacy-утилита (пароль не хранится в Users), в Obsidian не упомянут
- ✅ `UserInfoQueueSender` зарегистрирован как `Scoped` (не `Transient` как написано в Users.md) — в Program.cs: `AddScoped<UserInfoQueueSender>()`
- ✅ Структура миграций разделена на два каталога: `Persistence/Migrations/` (старые) и `Migrations/` (новые — AddPreviewAvatar, Badges, StorageLimit, UserDevices, Firebase, NotificationsEnabled, UserPrivacy, UserPersonalization)
