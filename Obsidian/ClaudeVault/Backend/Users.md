# BarkFluff.Users

Управление профилями пользователей, бейджами, устройствами и GDPR-экспортом. Порт: **7001**.

Расположение: `Backend/BarkFluff.Users/`

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

- `Domain/` — `User`, `UserContact`, `Badge`, `UserBadge`, `UserDevice`, `Privacy`
- `Features/` — MediatR команды/запросы (CQRS)
- `Host/` — gRPC-сервисы
- `Persistence/Services/` — `UsersStorage`, `DevicesStorage`, `PrivacyStorage` (Transient)
- `Infrastructure/` — `UserInfoQueueSender` (RabbitMQ события)
- `Mapping/` — extension-методы маппинга доменных объектов в protobuf

### Ключевые паттерны

**Draft-пользователи**: регистрация двухфазная — `AddDraftUser` (IsDraft=true) → `ConfirmUser` (IsDraft=false). Identity управляет процессом.

**ID пользователя**: `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` при создании.

**Зарезервированные имена**: `ReservedUsernamesService` (Singleton) загружает из конфига `ReservedNames:Usernames` (через запятую).

**Поиск**: Full-text search (русский, tsvector + plainto_tsquery) + trigram fuzzy matching (pg_trgm, порог 0.3). Через `FromSqlRaw()`.

**Бейджи**: уникальное ограничение (UserId, BadgeId). `Priority` — меньше = выше приоритет (default 1000).

**Устройства**: `RegisterDevice` — upsert по DeviceId (Guid). Хранит AppName, OS, Location, FirebaseDeviceToken.

### Приватность

`Privacy` (1:1 с `User`). Поля: `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility`, `EmailVisibility`, `OnlineVisibility` (enum `All=0, Friends=1, None=2`), `SearchVisible`.

Запись создаётся автоматически в `ConfirmUser`. Дефолты: профиль виден всем, email скрыт (`None`).

FRIENDS трактуется как NONE до появления сервиса отношений.

Применение:
- `GetUserByUsername` — если `!ProfileVisibleOnSite` → found=false; видимость avatar/bio фильтруется
- `SearchUsers` — `SearchVisible=false` исключает из поиска
- [[Backend/Onliner]] — `OnlineVisibilityFilter` по `OnlineVisibility`

### RabbitMQ-события

| Событие | Триггер |
|---------|---------|
| `UserChangedName` | ChangeName |
| `UserChangedUsername` | ChangeUsername |
| `UserChangedAvatar` | SetProfilePicture |
| `UserChangedPassword` | (через серверный API от Identity) |
| `UserChangedBio` | ChangeBio |

## Конфигурация

| Ключ | Описание |
|------|---------|
| `UsersDb` | PostgreSQL connection string |
| `RabbitMQ:*` | RabbitMQ |
| `FilesService:Host/Token` | gRPC-клиент Files |
| `MessagesService:Host/Token` | gRPC-клиент Messages |
| `ReservedNames:Usernames` | Зарезервированные имена |

## Proto

- `users_api.proto` — Server
- `files_api.proto` — Client
- `messages_api.proto` — Client (для GDPR-экспорта)

## Связанные файлы

- [[Backend/Identity]] — Draft → Confirm flow
- [[Backend/Onliner]] — проверка OnlineVisibility
- [[Backend/Files]] — валидация аватара
