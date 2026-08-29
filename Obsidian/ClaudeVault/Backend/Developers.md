# Barkfluff.Developers

Портал документации для разработчиков-клиентов BarkFluff. Содержит секции документации, метаданные proto-файлов и коды ошибок.
API/gRPC-Web слушает **7020**, статика SPA — **7021**.

Расположение: `Backend/Barkfluff.Developers/`

gRPC Reflection доступен только при `ASPNETCORE_ENVIRONMENT=Development`; в Production, Nightly и Master endpoint не публикуется.

## Сборка

```bash
dotnet build Barkfluff.Developers.csproj
```

Миграции применяются автоматически при старте.

## Особенности

- Принимает **gRPC-Web** напрямую (не через [[Backend/Web]] YARP-прокси)
- Kestrel: API-порт `HttpProtocols.Http1AndHttp2`, static-порт `HttpProtocols.Http1`
- Статика раздаётся самим сервисом с `7021`; API на `7020` доступен через `developers.conf`
- Все методы защищены отдельной policy `DevelopersReader`, которая принимает только User JWT;
  Service-токены к пользовательскому read API не допускаются
- Фронтенд: React + Vite + TypeScript (`Frontend/Developers/`) — см. [[Клиенты/Developers-Web]]

## Архитектура

### gRPC-сервис

- `DevelopersApiService` — единое клиентское API, все методы требуют User JWT

### Слои

- `Domain/` — `DocumentationSection`, `ProtoMetadata`, `ErrorCodeEntry`
- `Features/` — MediatR команды/запросы (CQRS). Каждый request и его `*Handler`
  находятся в отдельных файлах, как в [[Backend/Identity]]:
  - **Exposed via gRPC** (5 методов): `GetSections`, `GetSectionByKey`, `GetProtoFiles`, `GetProtoFileContent`, `GetErrorCodes`
  - **Внутренние** (используются только `SeedData`/админ-флоу, не в proto): `CreateSection`, `UpdateSection`, `DeleteSection`
- `Host/` — `DevelopersApiService` (gRPC)
- `Persistence/` — `DevelopersContext` (PostgreSQL), `DocumentationStorage`, `ProtoMetadataStorage`
- `Infrastructure/` — `DevelopersStartupInitializer`, `DevelopersReader` policy,
  `PublishedProtoCatalog`, `ErrorCodeSeeder`, `ProtoFileProvider`, `SeedData`

### Ключевые паттерны

**Асинхронная инициализация при старте**: `DevelopersStartupInitializer` выполняет
`MigrateAsync`, затем в одной транзакции добавляет отсутствующие defaults из `SeedData` и
проверяет инварианты. Seed аддитивный и идемпотентный: ключ документации — `Key`, proto —
`FileName`, ошибка — `Code`; существующие значения не обновляются и не удаляются. В PostgreSQL
вставки используют `ON CONFLICT DO NOTHING`, поэтому параллельный старт реплик не создаёт
дубликаты. При нарушении инварианта (битый JSON, duplicate error code, отсутствие
parameterless-конструктора exception или физического опубликованного proto) сервис не стартует.

**ErrorCodeSeeder**: читает все наследники `BaseGrpcException` из `BarkFluff.Shared.Exceptions` через reflection (`Activator.CreateInstance`), достаёт `ErrorCode` и `ErrorMessage`.

**PublishedProtoCatalog**: единственный read seam для proto. Allowlist содержит ровно:
`shared.proto`, `beacon_api.proto`, `identity_api.proto`, `users_api.proto`,
`messages_api.proto`, `files_api.proto`, `updates_api.proto`, `onliner_api.proto`,
`fast_auth_api.proto`, `navigator_api.proto`. `configuration_api.proto`,
`federation_internal_api.proto` и неизвестные имена не выдаются даже при прямом запросе.
`ProtoFileProvider` читает только эти файлы из `output/Proto/`; csproj также копирует только их.

### База данных (PostgreSQL)

| Таблица | Описание |
|---------|----------|
| `DocumentationSections` | Секции документации (`key`, `title`, `type`, `order`, `content`) |
| `ProtoMetadata` | Метаданные опубликованных proto (`file_name`, `display_name`, `slug`, `order`, `rpc_descriptions`) |
| `ErrorCodes` | Коды ошибок (`code`, `exception_name`, `description`, `domain`) |

## gRPC-методы

| Метод | Описание |
|-------|----------|
| `GetDocumentationSections` | Список всех секций |
| `GetDocumentationSection` | Одна секция по slug/ключу |
| `GetProtoFiles` | Список proto-файлов с метаданными |
| `GetProtoFileContent` | Содержимое .proto файла по имени |
| `GetErrorCodes` | Все коды ошибок |

## Конфигурация

| Ключ | Описание |
|------|---------|
| `DevelopersDb` | PostgreSQL connection string |
| `IdentityService:Host` | gRPC-клиент Identity (для валидации JWT) |
| `RunSettings:Port` | API/gRPC-порт, по умолчанию `7020` |
| `RunSettings:Http1Port` | HTTP-порт статики, по умолчанию `7021` |
| `Developers:AllowedOrigins` | Разрешённые CORS origins; production — `https://developers.barkfluff.com`, в Development дополнительно `http://localhost:5173` |
| `ExternalEndpoint:Host` | внешний адрес портала, по умолчанию `https://developers.example.com` |

Для чистого deployment миграция `AddDevelopersConfiguration` идемпотентно создаёт
строки `RunSettings`, `DevelopersDb` и `ExternalEndpoint` для `ServiceId=12`.
ConfigurationDefaultsPopulator заполняет пустые значения: БД `developers`, API `7020`
и SPA `7021`; заранее заданные оператором значения сохраняются.

## Proto

- `developers_api.proto` — Server
- `identity_api.proto` — Client (JWT-валидация)
- Канонический источник опубликованных файлов — `Shared/BarkFluff.Proto/`; backend output
  собирается явным списком из 10 файлов. Изменения в proto и `Shared.Exceptions` запускают
  Developers CI вместе с backend-тестами и frontend generation/drift-check.

## Docker

```bash
docker build -t barkfluff-developers .
```

CI собирает сервис из `Dockerfile.slim`, как и [[Backend/Users]]. Dockerfile собирает
`Frontend/Developers` через Node/Vite и копирует `dist/` в `/app/wwwroot` образа.
Переменные `DEVELOPERS_PORT` и `DEVELOPERS_HTTP1PORT` позволяют переопределить API- и
static-порты.

## Связанные файлы

- [[Клиенты/Developers-Web]] — React-фронтенд портала
- [[Shared/Proto]] — `developers_api.proto`
- [[Shared/Exceptions]] — коды ошибок через reflection
- [[Shared/Identity]] — `ServiceId.Developers = 12`
# Метрики

Сервис подключён к [[Backend/GrpcServer]] metrics reporter. В [[Backend/AdminPanel]] доступны агрегированные gRPC-запросы и ошибки портала; детализация отдельных document/proto RPC намеренно не выводится.
