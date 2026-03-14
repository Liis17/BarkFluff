# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Назначение микросервиса

`BarkFluff.Users` — управление профилями пользователей, бейджами, устройствами и GDPR-экспортом данных. Порт: 7001.

## Сборка и запуск

```bash
# Сборка
dotnet build BarkFluff.Users.csproj

# Миграции применяются автоматически при старте (Program.cs)
# Ручное применение:
dotnet ef database update --project BarkFluff.Users.csproj
```

Тестов нет. Полный запуск — через Docker Compose в корне Backend.

## Архитектура

### Два gRPC-сервиса

- **`UsersApiService`** — клиентское API (`[Authorize(Policy = nameof(TokenType.User))]`)
- **`UsersServerApiService`** — межсервисное API (`[Authorize(Policy = nameof(TokenType.Service))]`)

### Слои

- `Domain/` — EF Core сущности (`User`, `UserContact`, `Badge`, `UserBadge`, `UserDevice`)
- `Features/` — MediatR команды/запросы (CQRS), организованы по фичам
- `Host/` — gRPC-сервисы, делегируют в MediatR
- `Persistence/Services/` — `UsersStorage` и `DevicesStorage` (Transient), инкапсулируют запросы к БД
- `Infrastructure/` — `UserInfoQueueSender` для публикации RabbitMQ-событий
- `Mapping/` — extension-методы для маппинга доменных объектов в protobuf

### Ключевые паттерны

**Draft-пользователи.** Регистрация двухфазная: `AddDraftUser` (IsDraft=true) → `ConfirmUser` (IsDraft=false). Identity-сервис управляет этим процессом.

**ID пользователя** генерируется как `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` при создании.

**Зарезервированные имена.** `ReservedUsernamesService` (Singleton) загружает список из конфига `ReservedNames:Usernames` (через запятую), проверяется при AddDraftUser и CheckExistUsername.

**Поиск пользователей.** `SearchUsers` использует два механизма PostgreSQL:
- Full-text search (русский язык, tsvector + plainto_tsquery) для точных совпадений
- Trigram fuzzy matching (`pg_trgm`, порог similarity 0.3) для опечаток
Запросы выполняются через `FromSqlRaw()` с `NpgsqlParameter`.

**Бейджи.** Уникальное ограничение (UserId, BadgeId). `Priority` — целое число, меньше = выше приоритет (default 1000).

**Устройства.** `RegisterDevice` — upsert по DeviceId (Guid). Хранит AppName, OS, Location, FirebaseDeviceToken.

### RabbitMQ-события (публикуются)

Все через `UserInfoQueueSender.PublishEndpoint`:

| Событие | Триггер |
|---------|---------|
| `UserChangedName` | ChangeName |
| `UserChangedUsername` | ChangeUsername |
| `UserChangedAvatar` | SetProfilePicture |
| `UserChangedPassword` | (вызывается Identity через серверное API) |
| `UserChangedBio` | ChangeBio |

### gRPC-клиенты

- `FilesServerApi.FilesServerApiClient` — валидация типа файла аватара, получение данных файла
- `MessagesServerApi.MessagesServerApiClient` — получение чатов и сообщений для GDPR-экспорта (`ExportData`)

## Ключевые файлы

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Startup, DI-регистрация |
| `Persistence/Contexts/UsersContext.cs` | DbContext, конфигурация связей |
| `Persistence/Services/UsersStorage.cs` | Основной слой данных для пользователей и бейджей |
| `Persistence/Services/DevicesStorage.cs` | Слой данных для устройств |
| `Host/UsersApiService.cs` | Клиентский gRPC-сервис |
| `Host/UsersServerApiService.cs` | Межсервисный gRPC-сервис |
| `Infrastructure/UserInfoQueueSender.cs` | Публикация событий в RabbitMQ |
| `../../Shared/BarkFluff.Proto/users_api.proto` | Proto-контракт |

## Конфигурация (ключи)

| Ключ | Описание |
|------|---------|
| `UsersDb` | PostgreSQL connection string |
| `RabbitMQ:Host`, `RabbitMQ:Username`, `RabbitMQ:Password` | RabbitMQ |
| `FilesService:Host`, `FilesService:Token` | gRPC-клиент Files |
| `MessagesService:Host`, `MessagesService:Token` | gRPC-клиент Messages |
| `ReservedNames:Usernames` | Зарезервированные имена через запятую |

Конфигурация загружается из сервиса Configuration при старте: `builder.LoadConfiguration(ServiceId.Users)`.
