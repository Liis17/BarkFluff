# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Описание

Микросервис `Configuration` (порт 7003) — централизованное хранилище конфигурации для всех микросервисов BarkFluff. Не использует XAuth — к нему обращаются сервисы при старте до получения токенов.

## Сборка и запуск

```bash
dotnet build Backend/BarkFluff.Configuration/BarkFluff.Configuration.csproj

# В составе Docker-композиции (из Backend/):
docker-compose -f docker-compose-dev.yml up -d configuration
```

Порт определяется через `CONFIGURATION_PORT` или `RunSettings__Port` env-переменную, либо через `appsettings.json`.

## Переменные БД

Обязательны при запуске: `CONFIGURATION_HOST`, `CONFIGURATION_DATABASE`, `CONFIGURATION_USERNAME`, `CONFIGURATION_PASSWORD`. Опционально: `CONFIGURATION_DBPORT`.

## Архитектура

CQRS через MediatR. gRPC API (`configuration_api.proto`) предоставляет методы:

**Конфигурация:**
- **GetConfiguration** — возвращает конфигурацию для `ServiceId`. Загружает записи с `ServiceId == запрошенный || ServiceId == Unknown`, при дублях по Section+Key приоритет у записи с конкретным ServiceId.
- **UpdateConfiguration** — обновляет или создаёт запись конфигурации (upsert по Section+Key+ServiceId).

**Reserved Names (зарезервированные имена пользователей):**
- **GetReservedNames** / **AddReservedName** / **UpdateReservedName** / **DeleteReservedName** — CRUD для списка зарезервированных username. Хранится как одна строка в БД (`Section="ReservedNames"`, `Key="Usernames"`, Value — comma-separated). Имена нормализуются в lowercase.

### Ключевые компоненты

- `Domain/ConfigurationItem` — единственная сущность: Section, Key, Value, ServiceId, EditedAt/By/From
- `Infrastructure/ConfigurationStorage` — доступ к БД (read/upsert конфигураций + CRUD reserved names)
- `Infrastructure/ConfigurationDefaultsPopulator` — при старте заполняет пустые (`Value == ""`) конфигурации дефолтами (порты, JWT, RabbitMQ, Redis, S3, межсервисные токены). Генерирует JWT SecretKey и Service-токены автоматически.
- `Infrastructure/ConfigurationContext` — EF Core DbContext, единственный DbSet: `Configurations`
- `Host/ConfigurationApiService` — gRPC-сервис, делегирует в MediatR-команды, собирает метрики через `MetricsCollector`

### Миграции

Применяются автоматически при старте (`ctx.Database.Migrate()`) с retry до 5 раз с экспоненциальным backoff. После миграций запускается `ConfigurationDefaultsPopulator`.

```bash
# Ручное создание миграции:
dotnet ef migrations add MigrationName --project Backend/BarkFluff.Configuration
```

Миграции-seed (например `SeedBeaconServerProps`, `SeedInitialConfigurationKeys`) добавляют начальные записи конфигурации напрямую через SQL в `Up()` — не через EF-модель.

## Proto

Серверная сторона: `Shared/BarkFluff.Proto/configuration_api.proto` (`GrpcServices="Server"`).

## Зависимости

- `BarkFluff.GrpcServer` — Serilog, Metrics (`MetricsCollector`), `ServerExceptionInterceptor`, `SetRunningAddress`
- `BarkFluff.Shared.Identity` — `ServiceId` enum
- PostgreSQL (Npgsql), MediatR, Grpc.Tools
