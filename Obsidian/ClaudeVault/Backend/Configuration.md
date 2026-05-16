# BarkFluff.Configuration

Централизованное хранилище конфигурации для всех микросервисов. Порт: **7003**.
**Не использует XAuth** — к нему обращаются сервисы при старте до получения токенов.

Расположение: `Backend/BarkFluff.Configuration/`

📄 **Детальная карта файлов:** [[Backend/Configuration-ProjectMap]]

## Сборка

```bash
dotnet build Backend/BarkFluff.Configuration/BarkFluff.Configuration.csproj
docker-compose -f docker-compose-dev.yml up -d configuration
```

Переменные БД: `CONFIGURATION_HOST`, `CONFIGURATION_DATABASE`, `CONFIGURATION_USERNAME`, `CONFIGURATION_PASSWORD`. Опционально: `CONFIGURATION_DBPORT`.

## Архитектура

CQRS через MediatR. gRPC API (`configuration_api.proto`):

**Конфигурация:**
- `GetConfiguration` — возвращает конфигурацию для `ServiceId`. Загружает записи с `ServiceId == запрошенный || ServiceId == Unknown`; при дублях по Section+Key приоритет у записи с конкретным ServiceId.
- `UpdateConfiguration` — upsert по Section+Key+ServiceId.

**Reserved Names (зарезервированные имена пользователей):**
- `GetReservedNames` / `AddReservedName` / `UpdateReservedName` / `DeleteReservedName` — CRUD. Хранится как одна строка в БД (`Section="ReservedNames"`, `Key="Usernames"`, Value — comma-separated). Имена нормализуются в lowercase.

## Ключевые компоненты

- `Domain/ConfigurationItem` — единственная сущность: Section, Key, Value, ServiceId, EditedAt/By/From
- `Infrastructure/ConfigurationStorage` — read/upsert конфигураций + CRUD reserved names
- `Infrastructure/ConfigurationDefaultsPopulator` — при старте заполняет пустые (`Value == ""`) конфигурации дефолтами (порты, JWT, RabbitMQ, Redis, Seq, S3, токены, строки подключения к БД, внешние эндпоинты). Поддерживает секции: `RunSettings` (с `Http1Port` для Files), `JwtSettings`, `RabbitMQ`, `Redis`, `Seq`, `S3Buckets:*`, `ExternalEndpoint`, `NavigatorUrl`, `TempFiles` (ExpiresAt 60 мин), `UsersService`, `FilesService`, `MessagesService`, `IdentityService`, `AdminPanel`, `CloudMessaging`, `Web`, `FastAuth`, базы данных (Identity, Users, Files, Messages, Onliner). Генерирует JWT SecretKey (64 символа) и Service-токены (TTL 10 лет) автоматически.
- `Infrastructure/ConfigurationContext` — EF Core DbContext, один DbSet: `Configurations`
- `Host/ConfigurationApiService` — gRPC-сервис, делегирует в MediatR-команды; инструментирован `MetricsCollector` (счётчики запросов, ошибок, длительность)

## Миграции

Применяются автоматически при старте (`ctx.Database.Migrate()`) с retry до 5 раз. После миграций запускается `ConfigurationDefaultsPopulator`.

Миграции-seed (например `SeedBeaconServerProps`) добавляют начальные записи через SQL в `Up()` — не через EF-модель.

```bash
dotnet ef migrations add MigrationName --project Backend/BarkFluff.Configuration
```

## Proto

`Shared/BarkFluff.Proto/configuration_api.proto` (`GrpcServices="Server"`).

## Зависимости

- [[Backend/GrpcServer]] — Serilog, Metrics, ServerExceptionInterceptor, SetRunningAddress
- [[Shared/Identity]] — `ServiceId` enum
