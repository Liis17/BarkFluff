# BarkFluff.Configuration

Централизованное хранилище конфигурации для всех микросервисов. Порт: **7003**.
**Не использует XAuth** — к нему обращаются сервисы при старте до получения токенов.

Расположение: `Backend/BarkFluff.Configuration/`

gRPC Reflection доступен только при `ASPNETCORE_ENVIRONMENT=Development`; в Production, Nightly и Master endpoint не публикуется.

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
- `GetAllConfigurations` — возвращает **все** строки таблицы без фильтров и дедупликации (для вкладки «Конфигурация» в [[Backend/AdminPanel|AdminPanel]]).
- `UpdateConfiguration` — upsert по Section+Key+ServiceId. `EditedAt` ставится сервером (`DateTime.UtcNow`), `EditedBy`/`EditedFrom` передаёт клиент; в той же транзакции создаётся `ConfigurationRevision` с переходом old → new.
- `GetConfigurationHistory` — последние 1–100 ревизий конкретного Section+Key+ServiceId, новые первыми.
- `RollbackConfiguration` — по id ревизии восстанавливает её `PreviousValue` и создаёт новую ревизию `ChangeKind=Rollback` со ссылкой `SourceRevisionId`. История содержит прошлые значения, включая секретные; AdminPanel маскирует их до ответа браузеру.

**Reserved Names (зарезервированные имена пользователей):**
- `GetReservedNames` / `AddReservedName` / `UpdateReservedName` / `DeleteReservedName` — CRUD. Хранится как одна строка в БД (`Section="ReservedNames"`, `Key="Usernames"`, Value — comma-separated). Имена нормализуются в lowercase.

## Ключевые компоненты

- `Domain/ConfigurationItem` — текущая строка: Section, Key, Value, ServiceId, EditedAt/By/From
- `Domain/ConfigurationRevision` — неизменяемая запись перехода PreviousValue → NewValue, автор/источник/время, вид изменения и optional source revision для rollback
- `Persistence/Services/ConfigurationStorage` — read/upsert/история/rollback конфигураций + CRUD reserved names
- `Infrastructure/ConfigurationDefaultsPopulator` — при старте заполняет пустые (`Value == ""`) конфигурации дефолтами (порты, JWT, RabbitMQ, Redis, Seq, S3, токены, строки подключения к БД, внешние эндпоинты). Поддерживает секции: `RunSettings` (с `Http1Port` для Files, Calls и [[Backend/Bots|Bots]]), `JwtSettings`, `RabbitMQ`, `Redis`, `IdentitySecurity` (для [[Backend/Identity]]), `Seq`, `S3Buckets:*`, `ExternalEndpoint`, `NavigatorUrl`, `TempFiles` (ExpiresAt 60 мин), `UsersService`, `FilesService`, `MessagesService`, `IdentityService`, `BotsService`, `FederationService`, `AdminPanel`, `CloudMessaging`, `Web`, `FastAuth`, `LiveKit` (+ `PublicUrl`), базы данных (Identity, Users, Files, Messages, Onliner, Bots, Calls, Federation). Генерирует JWT SecretKey (64 символа) и Service-токены (TTL 10 лет) автоматически.
  - `Federation` (ServiceId=15, задел под федерацию из [Фазы 0 rearch](../../../docs/rearch/phase-0/README.md)): `RunSettings:Port`=7030, `FederationDb`, `Federation:Enabled`=false (дефолт), `Federation:ServerName` и `Federation:ExternalEndpoint` **намеренно не заполняются** — остаются `""`, оператор ноды задаёт сам. `FederationService:Host`/`Token` — глобальные (ServiceId=0), по аналогии с `BotsService`. Сам сервис Federation ещё не существует (Фаза 1).
- `Persistence/Contexts/ConfigurationContext` — EF Core DbContext: `Configurations` и `ConfigurationRevisions`; ревизии удаляются каскадно вместе со строкой конфигурации
- `Host/ConfigurationApiService` — gRPC-сервис, делегирует в MediatR-команды; инструментирован `MetricsCollector` (счётчики запросов, ошибок, длительность)

## Миграции

Применяются автоматически при старте (`ctx.Database.Migrate()`) с retry до 5 раз. После миграций запускается `ConfigurationDefaultsPopulator`.

Миграции-seed (например `SeedBeaconServerProps`) добавляют начальные записи через SQL в `Up()` — не через EF-модель. Каждая migration-класс имеет `MigrationAttribute` (из сгенерированного `.Designer.cs` либо явно на классе), иначе EF Core не включит её в `Database.Migrate()`.

`AddIdentitySecurityConfiguration` добавляет для `ServiceId.Identity` строки `Redis` и `IdentitySecurity:*`; пустые значения заполняются production defaults через `ConfigurationDefaultsPopulator`.

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add MigrationName --project Backend/BarkFluff.Configuration
```

## Метрики

Инструментирован `MetricsCollector` (общая схема, см. [[Backend/Beacon-Metrics]]): счётчики запросов/успехов/ошибок/длительности на `GetConfiguration`, `UpdateConfiguration`, историю, rollback и Reserved Names, плюс gauges по БД (`configurations_total`, `db_healthy`) и миграциям при старте.

📄 Полный реестр метрик → [[Backend/Configuration-Metrics]]

## Proto

`Shared/BarkFluff.Proto/configuration_api.proto` (`GrpcServices="Server"`).

## Зависимости

- [[Backend/GrpcServer]] — Serilog, Metrics, ServerExceptionInterceptor, SetRunningAddress
- [[Shared/Identity]] — `ServiceId` enum
