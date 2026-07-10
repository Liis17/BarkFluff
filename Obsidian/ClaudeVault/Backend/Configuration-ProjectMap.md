# BarkFluff.Configuration — Карта проекта

Детальный разбор всех файлов сервиса централизованной конфигурации.
Расположение: `Backend/BarkFluff.Configuration/`

← [[Backend/Configuration]]

---

## Точка входа

| Файл | Описание |
|------|----------|
| `Program.cs` | Точка входа. Настраивает Kestrel (HTTP/2), Serilog, Metrics, EF Core (PostgreSQL), MediatR, gRPC + `ServerExceptionInterceptor`. При старте запускает авто-миграции с retry (до 5 попыток, экспоненциальная задержка), затем `ConfigurationDefaultsPopulator`. Поддерживает динамический порт через `CONFIGURATION_PORT` / `RunSettings__Port`. |
| `appsettings.json` | Базовые настройки приложения. |
| `appsettings.Development.json` | Настройки для локальной разработки. |
| `Properties/launchSettings.json` | Профили запуска для Visual Studio / dotnet run. |
| `Dockerfile.slim` | Docker-образ сервиса, используемый CI и production. |
| `SECURITY_AUDIT.md` | Аудит безопасности сервиса. |

---

## Domain

| Файл | Описание |
|------|----------|
| `Domain/ConfigurationItem.cs` | Единственная доменная сущность. Поля: `Id`, `Section`, `Key`, `Value`, `ServiceId` (enum `ServiceId`), `EditedAt`, `EditedBy`, `EditedFrom`. Хранит одну запись конфигурации, привязанную к конкретному сервису или глобально (`ServiceId.Unknown`). |

---

## Host (gRPC)

| Файл | Описание |
|------|----------|
| `Host/ConfigurationApiService.cs` | gRPC-сервис, реализует `ConfigurationApiBase`. Принимает входящие RPC-вызовы, инкрементирует метрики через `MetricsCollector` и делегирует обработку в MediatR-команды. Методы: `GetConfiguration`, `GetAllConfigurations`, `UpdateConfiguration`, `GetReservedNames`, `AddReservedName`, `UpdateReservedName`, `DeleteReservedName`. |

---

## Infrastructure

| Файл | Описание |
|------|----------|
| `Infrastructure/ConfigurationContext.cs` | EF Core `DbContext`. Один `DbSet<ConfigurationItem> Configurations`. Минималистичный контекст без флюентной конфигурации. |
| `Infrastructure/ConfigurationStorage.cs` | Репозиторий для работы с БД. **Конфигурации:** `GetConfiguration(serviceId)` — загружает записи для конкретного ServiceId + Unknown, приоритет отдаётся конкретному сервису на уровне хендлера. `UpdateConfigurationAsync` — upsert по Section+Key+ServiceId. **Reserved Names:** хранит список зарезервированных юзернеймов как одну строку CSV (`Section="ReservedNames"`, `Key="Usernames"`). CRUD: `GetReservedNamesAsync`, `AddReservedNameAsync`, `UpdateReservedNameAsync`, `DeleteReservedNameAsync`. Имена нормализуются в lowercase. |
| `Infrastructure/ConfigurationDefaultsPopulator.cs` | Авто-заполнение пустых конфигураций при старте. Ищет записи с `Value == ""` и подставляет дефолты. Генерирует `JwtSettings:SecretKey` (64-символьный random). Генерирует JWT-сервисные токены (TTL 10 лет) для межсервисного взаимодействия. Поддерживаемые секции: `RunSettings` (Port, Http1Port), `JwtSettings`, `RabbitMQ`, `Redis`, `Seq`, `S3Buckets:*`, `ExternalEndpoint`, `NavigatorUrl`, `UsersService`, `FilesService`, `MessagesService`, `IdentityService`, базы данных (Identity, Users, Files, Messages, Onliner). Словари: `ContainerNames`, `DefaultPorts`, `SubdomainNames`, `DatabaseNames`. |

---

## Features (CQRS / MediatR)

### GetConfiguration
| Файл | Описание |
|------|----------|
| `Features/GetConfiguration/GetConfigurationCommand.cs` | Команда с полем `ServiceId`. |
| `Features/GetConfiguration/GetConfigurationCommandHandler.cs` | Загружает конфигурации через `ConfigurationStorage`, фильтрует дубли по Section+Key (приоритет — конкретный ServiceId над Unknown), возвращает `GetConfigurationResponse`. |

### GetAllConfigurations
| Файл | Описание |
|------|----------|
| `Features/GetAllConfigurations/GetAllConfigurationsCommand.cs` | Пустая команда. |
| `Features/GetAllConfigurations/GetAllConfigurationsCommandHandler.cs` | Возвращает все строки таблицы через `ConfigurationStorage.GetAllConfigurationsAsync()` без фильтров/дедупликации. Используется вкладкой «Конфигурация» AdminPanel. |

### UpdateConfiguration
| Файл | Описание |
|------|----------|
| `Features/UpdateConfiguration/UpdateConfigurationCommand.cs` | Команда: Section, Key, Value, ServiceId, EditedBy, EditedFrom. |
| `Features/UpdateConfiguration/UpdateConfigurationCommandHandler.cs` | Выполняет upsert через `ConfigurationStorage`, возвращает `UpdateConfigurationResponse { success, message }`. |

### GetReservedNames
| Файл | Описание |
|------|----------|
| `Features/GetReservedNames/GetReservedNamesCommand.cs` | Пустая команда. |
| `Features/GetReservedNames/GetReservedNamesCommandHandler.cs` | Возвращает список зарезервированных имён из CSV-строки в БД. |

### AddReservedName
| Файл | Описание |
|------|----------|
| `Features/AddReservedName/AddReservedNameCommand.cs` | Команда с полем `Name`. |
| `Features/AddReservedName/AddReservedNameCommandHandler.cs` | Добавляет имя в CSV-строку, если не существует. |

### UpdateReservedName
| Файл | Описание |
|------|----------|
| `Features/UpdateReservedName/UpdateReservedNameCommand.cs` | Команда с полями `OldName`, `NewName`. |
| `Features/UpdateReservedName/UpdateReservedNameCommandHandler.cs` | Заменяет старое имя новым в CSV-строке. |

### DeleteReservedName
| Файл | Описание |
|------|----------|
| `Features/DeleteReservedName/DeleteReservedNameCommand.cs` | Команда с полем `Name`. |
| `Features/DeleteReservedName/DeleteReservedNameCommandHandler.cs` | Удаляет имя из CSV-строки. |

---

## Proto

| Файл | Описание |
|------|----------|
| `Shared/BarkFluff.Proto/configuration_api.proto` | gRPC-контракт. Сервис `ConfigurationApi`: 2 метода для конфигураций + 4 метода для Reserved Names. Сообщение `ConfigurationItem` содержит Section, Key, Value, ServiceId, EditedAt (Timestamp), EditedBy, EditedFrom. |

---

## Persistence / Migrations

| Файл | Описание |
|------|----------|
| `20250508111334_AddConfiguration` | Начальная миграция — создаёт таблицу `Configurations`. |
| `20250509000000_SeedBeaconServerProps` | Seed начальных записей для Beacon через SQL в `Up()`. |
| `20250510000000_AddServerPropsLocation` | Добавляет поля/записи для Location. |
| `20251123000000_SeedInitialConfigurationKeys` | Seed начальных ключей конфигурации. |
| `20260129000000_AddPerBucketS3Configuration` | Добавляет конфигурацию S3-бакетов (отдельно на каждый бакет). |
| `20260130000000_AddOnlinerConfiguration` | Добавляет конфигурацию для сервиса Onliner. |
| `20260207000000_FixServiceIdsAndAddExternalEndpoints` | Исправляет ServiceId и добавляет записи ExternalEndpoint. |
| `20260221000000_AddBadgeImagesBucketConfiguration` | S3-бакет для изображений бейджей. |
| `20260222000000_AddAdminPanelServiceTokens` | Сервисные токены для AdminPanel. |
| `20260305000000_AddAudioBucketConfiguration` | S3-бакет для аудио. |
| `20260307000000_AddCloudMessagingConfiguration` | Конфигурация CloudMessaging-сервиса. |
| `20260307000001_AddCloudMessagingMessagesServiceConfiguration` | Конфигурация CloudMessaging для Messages-сервиса. |
| `20260308000000_AddConfigurationServiceTokenForAdminPanel` | Токен Configuration-сервиса для AdminPanel. |
| `20260313000000_AddReservedUsernamesConfiguration` | Начальная запись Reserved Names. |
| `20260318000000_AddWebServiceConfiguration` | Конфигурация BarkFluff.Web сервиса. |
| `20260429000000_AddFastAuthIdentityServiceConfiguration` | Конфигурация FastAuth → Identity. |
| `ConfigurationContextModelSnapshot.cs` | EF Core снимок модели. |
| `fix_migration_history.sql` | SQL-скрипт ручного исправления истории миграций. |
| `manual_add_location.sql` | SQL-скрипт ручного добавления колонки location. |
| `MIGRATION_FIX_README.md` | Инструкция по ручному исправлению миграций. |

---

## Зависимости NuGet

- `Microsoft.EntityFrameworkCore` + `Npgsql.EntityFrameworkCore.PostgreSQL` — ORM + PostgreSQL
- `MediatR` — CQRS
- `Microsoft.IdentityModel.Tokens` + `System.IdentityModel.Tokens.Jwt` — генерация JWT сервисных токенов
- [[Backend/GrpcServer]] — Serilog, Metrics, `ServerExceptionInterceptor`, `SetRunningAddress`
- [[Shared/Identity]] — `ServiceId` enum, `TokenType`, `IdentityClaims`
- [[Shared/Proto]] — `configuration_api.proto`
