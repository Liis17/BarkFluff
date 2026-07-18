# BarkFluff.Navigator

Управляет реестром доступных серверов BarkFluff. Порт: **7010**.
Публичный эндпоинт: `navigator.barkfluff.com:443 (plaintext HTTP/2).

Расположение: `Backend/BarkFluff.Navigator/`

Роль в федерации — вторичный канал (источник 2 discovery, [[../../../docs/rearch/03-discovery|docs/rearch/03-discovery.md]]): каталог публичных нод + кросс-проверка ключей при первом контакте + фолбэк-резолв, когда `/.well-known` недоступен. Сеть обязана работать при лежащем Navigator — уже знакомые ноды (`KnownServers` в [[Backend/Federation]]) продолжают общаться напрямую.

**Вне платформенного шаблона** (публичная инфраструктура вне ноды): без `LoadConfiguration`/Serilog/метрик — осознанное решение фазы 1.

## Сборка

```bash
dotnet build BarkFluff.Navigator.csproj
NAVIGATOR_DB="Data Source=navigator.db" NAVIGATOR_PORT=7010 dotnet run
```

БД — локальная **SQLite** (`Microsoft.EntityFrameworkCore.Sqlite`). Connection string: env `NAVIGATOR_DB` → ключ `NavigatorDb` в appsettings → дефолт `Data Source=navigator.db`. Схема создаётся при старте через `Database.EnsureCreated()` (без миграций).

## Архитектура

Три gRPC-метода (`navigator_api.proto`):
- `ListServers` — список активных серверов (без авторизации); контракт **не менялся** этапом 1.5 (легаси-клиенты выбора сервера)
- `RegisterServer` — регистрация сервера; `AddedBy` = UserId если есть JWT, иначе `"Anonymous"`. С `server_name` — принимается только после проверки `/.well-known/barkfluff`
- `GetServerByName` — точечный резолв ноды по `server_name` (реализован этапом 1.5)

Поток: `NavigatorApiService` → MediatR → `ListServersQueryHandler` / `RegisterServerCommandHandler` / `GetServerByNameQueryHandler` → `ServersStorage`

## Персистентность (этап 1.5)

`Persistence/NavigatorContext.cs` — SQLite (было **in-memory**, `ConcurrentDictionary`, терялось при рестарте; этапом 1.5 был PostgreSQL, затем переведён на локальную SQLite ради экономии RAM на выделенном хосте). Таблица `Servers`: все прежние поля + `LastSeenAt` (заменяет in-memory `lastSeen`) + federation-поля (`ServerName` — уникальный частичный индекс `WHERE ServerName IS NOT NULL`, `FederationEndpoint`, `TlsSpkiSha256`, `FederationProtocolVersions`, `SigningKeys` — все три хранятся как `TEXT`/JSON: массивы через primitive-collections EF Core, `SigningKeys` через явный JSON-конвертер, список `{key_id, public_key_base64, expired_at}`, `NavigatorSigningKeyInfo`).

`ServersStorage` теперь **Scoped** (использует `NavigatorContext`), а не Singleton — троттлинг регистраций вынесен в отдельный синглтон `RegistrationThrottle` (тот же in-memory `ConcurrentDictionary`, потеря состояния при рестарте безвредна, как и раньше).

- `GetServersAsync()` — как раньше, только `LastSeenAt` в пределах `ServerRegistration:ActivePeriodMinutes` (дефолт 10 мин)
- `RegisterServerAsync()` — upsert. Ключ идентичности: `ServerName`, если задан; иначе легаси `Name+BeaconHost+BeaconPort`
- `GetByServerNameAsync()` — lowercase-сравнение, `found=false` если протухло (тот же TTL, что у `GetServers`)

**Деплой**: Navigator живёт в СВОИХ compose-файлах (`Backend/BarkFluff.Navigator/docker-compose-dev.yml` и `docker-compose-master.yml`, отдельные от основного стека ноды). SQLite-файл хранится в named volume `navigator_data:/app/db`, `NAVIGATOR_DB=Data Source=/app/db/navigator.db`; сервис запущен с `user: root`, чтобы писать в root-owned volume (образ `Dockerfile.slim` остаётся chiseled/non-root). Отдельного контейнера БД больше нет.

## Валидация федеративной регистрации (этап 1.5)

Если в `RegisterServerRequest.ServerInfo` заполнен `server_name` — регистрация принимается только после:

1. `Features/RegisterServer/FederationServernameGuard.cs` — минимальная анти-SSRF-проверка (punycode-нормализация к A-label, запрет IP-литералов/`localhost`, DNS-резолв + отклонение приватных диапазонов). **Осознанное дублирование** `BarkFluff.Federation.Services.ServernameValidator` — Navigator публичен, не имеет общего кода с Federation; упрощение — без anti-rebinding IP-пиннинга (не привилегированный канал, только проверка перед регистрацией в каталоге).
2. `Features/RegisterServer/FederationWellKnownValidator.cs` — `GET https://{server_name}/.well-known/barkfluff` по CA-валидному HTTPS (без trust-all), лимит 64 КБ/10с; сверка `server_name` + всех заявленных `signing_keys` по `key_id`+байтам (без проверки Ed25519-подписи документа — это не входит в скоуп Navigator). Dev-флаг `NAVIGATOR_INSECURE_WELLKNOWN=1` (только вне production) отключает CA-валидацию для стенда.
3. Фетч не удался / документ невалиден / ключи не совпали → `FederationRegistrationRejectedException` (`Shared/BarkFluff.Shared.Exceptions/Navigator/`). Синтаксически невалидный `server_name` → `InvalidServernameException` (переиспользован из `Shared/BarkFluff.Shared.Exceptions/Federation/` — общая библиотека).

Легаси-регистрация (без `server_name`) — без изменений, никакой валидации сверх существующей.

## Domain/ServerInfo

Поля: `Id` (long, ключ), `CreatedAt`, `AddedBy`, `Name`, `BeaconHost`, `BeaconPort`, `Description`, `ServerPublicName`, `Location`, `ColorLiteHex/MainHex/HardHex` + этап 1.5: `LastSeenAt`, `ServerName?`, `FederationEndpoint?`, `TlsSpkiSha256?`, `FederationProtocolVersions?`, `SigningKeys? : List<NavigatorSigningKeyInfo>`. **`AccountsCount` в доменной модели нет** — существует только в proto-ответе, всегда `0`.

В proto `ServerInfo` адрес маяка передаётся как `beacon_uri: ServiceEndpoint` (а не отдельные host/port).

## Валидация легаси-полей при регистрации

`RegisterServerCommandHandler` проверяет:
- `BeaconHost` — не пустой, длина ≤ 2048, валидируется через `Uri.TryCreate` + `Uri.CheckHostName`
- `BeaconPort` — 1..65535
- `Name` — не пустой, длина ≤ 64
- `Description` — обязателен, длина ≤ 512
- `ServerPublicName` — обязателен, длина ≤ 64
- `Location` — длина ≤ 128
- HEX-цвета (`ColorLiteHex`/`ColorMainHex`/`ColorHardHex`) — формат `^#?[0-9A-Fa-f]{6}$` если не пусто

Исключения: `BeaconHostEmptyException`, `InvalidBeaconHostException`, `NameEmptyException`, `InvalidHexColorException` — наследники `BaseGrpcException`. Длины — через `ArgumentException`.

## Авторизация

`UseXAuth()` подключён, но методы без `[Authorize]` — все три публичны. `UserContext` используется в `RegisterServer` для записи `AddedBy`.

## Proto

- `navigator_api.proto` — Server
- `beacon_api.proto` — Client

## Тесты

`Tests/BarkFluff.Navigator.Tests/` — 37 тестов (EF InMemory, без Docker/Postgres): легаси-валидация регистрации (не изменилась), `FederationServernameGuardTests` (IP/localhost/punycode), `RegisterServerCommandHandlerTests` — федеративная регистрация (well-known подтвердил/отклонил, невалидный `server_name`) через тестовый двойник `FederationWellKnownValidator` (виртуальный `ValidateAsync`, переопределён в тесте — без реальной сети), `GetServerByNameQueryHandlerTests` (найдено/не найдено/протухло).

## Добавление нового поля в ServerInfo

1. Добавить поле в `Domain/ServerInfo.cs` (+ миграцию, если персистентное)
2. Добавить поле в `navigator_api.proto`
3. Обновить маппинг в `NavigatorApiService.cs` (request → domain)
4. Обновить маппинг в `ListServersQueryHandler.cs`/`GetServerByNameQueryHandler.cs` (domain → response) — учти правило «не менять контракт `ListServers`» для существующих полей
