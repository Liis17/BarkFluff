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
- Все методы защищены `[Authorize(Policy = nameof(TokenType.User))]`
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
- `Infrastructure/` — `ErrorCodeSeeder`, `ProtoFileProvider`, `SeedData`

### Ключевые паттерны

**Auto-seed при старте**: `SeedData` заполняет БД из текущего контента документации (overview, quickstart, implementation, auth-headers, connection-flow, error-codes + 10 proto metadata).

**ErrorCodeSeeder**: читает все наследники `BaseGrpcException` из `BarkFluff.Shared.Exceptions` через reflection (`Activator.CreateInstance`), достаёт `ErrorCode` и `ErrorMessage`.

**ProtoFileProvider**: читает `.proto` файлы из `output/Proto/` (копируются при сборке из `Shared/BarkFluff.Proto/`), всегда актуальные.

### База данных (PostgreSQL)

| Таблица | Описание |
|---------|----------|
| `documentation_sections` | Секции документации (title, slug, content, order) |
| `proto_metadata` | Метаданные proto-файлов (name, package, services, messages, path) |
| `error_codes` | Коды ошибок (code, message, description) |

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

## Proto

- `developers_api.proto` — Server
- `identity_api.proto` — Client (JWT-валидация)

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
