# BarkFluff.Navigator

Управляет реестром доступных серверов BarkFluff. Порты по умолчанию: **7010** (внутренний gRPC/HTTP2) и **7011** (внутренний HTTP/1.1 для админки и публичной страницы).
Публичный эндпоинт: `navigator.barkfluff.com:443` (TLS завершается в nginx; конфиг — `docker/navigator/nginx/navigator.conf`, источник истины для выделенного хоста: `/`, `/assets/`, `/api/`, `/ping`, `/admin/` → HTTP-порт, остальное — gRPC).

Анонимный liveness endpoint: `GET /ping` → `pong`.

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

### Веб-админка

React-сборка находится в `Backend/BarkFluff.Navigator/Admin/`; её production-файлы в `wwwroot/admin/` включаются в publish проекта. Navigator сам раздаёт их и HTTP API на отдельном HTTP/1.1-порту, а nginx публикует их как `https://navigator.barkfluff.com/admin/`. API `/admin/api/session`, `/admin/api/servers` и `/admin/api/logout` требуют cookie-сессию; `/admin/api/login` создаёт её после проверки логина и пароля.

Учётные данные **обязательны** и не имеют дефолтных значений: `NavigatorAdmin__Username` и `NavigatorAdmin__Password` внутри контейнера. Compose передаёт их из `.env` переменных `NAVIGATOR_ADMIN_USERNAME` и `NAVIGATOR_ADMIN_PASSWORD`. Cookie защищена (`Secure`, `HttpOnly`, `SameSite=Strict`) и действует 8 часов.

GitHub Actions workflow `build-backend-navigator.yml` перед `dotnet publish` запускает `npm ci` и `npm run build` в `Admin/`, поэтому runner всегда пересобирает React-ассеты из исходников.

**Дизайн админки — MD3** в стиле [[Backend/AdminPanel]] (светлая терракотовая тема, копия `md3.css` в `Admin/src/md3.css`): login-карточка с expressive-blobs, `.md-field-outlined`/`.md-btn`/`.msr`-иконки.

### Публичная главная страница `/`

`wwwroot/index.html` (+ `wwwroot/assets/md3.css`) — статический каталог серверов без авторизации, отдаётся через `UseDefaultFiles` на `/`. Тот же MD3-стиль админ-панели: карточки серверов (color-dot, публичное имя, описание, локация), чип «Закреплён» у ручных записей, кнопка «Открыть веб-клиент» при наличии `web_endpoint`, состояния empty/error/loading, автообновление каждые 30 с. Данные берёт из анонимного `GET /api/servers`. В шапке — ссылка на `/admin/`.

⚠️ md3.css существует в **трёх копиях** (AdminPanel `Pages/v2/assets/`, Navigator `wwwroot/assets/` и `Admin/src/`) — при смене темы синхронизировать вручную.

### Ручные («закреплённые») серверы

`IsManual` в `Domain/ServerInfo` — запись, добавленная админом через админку. Всегда видна в каталоге (gRPC `ListServers`, `GetServersAsync`, админка, публичная страница) вне TTL активной регистрации — контракт `ListServers` при этом не меняется (полей больше не становится, ручные серверы просто попадают в выдачу). Добавление — `POST /admin/api/servers` (полный набор полей: имя, публичное имя, описание, локация, цвет, Beacon host/port, web/files endpoint; валидация та же, что у gRPC-регистрации, ошибки — 400 с текстом), удаление — `DELETE /admin/api/servers/{id}` (только `IsManual`-строки, иначе 404). `AddedBy` = логин из сессии. `ServerName` у ручных пуст — well-known валидация не применяется. Если реальная нода позже зарегистрируется с тем же легаси-ключом `Name+BeaconHost+BeaconPort` — upsert обновит ту же строку, `IsManual` сохранится. Анонимный `GET /api/servers` отдаёт подмножество полей для публичной страницы (без beacon/файлового адреса).

## Персистентность (этап 1.5)

`Persistence/NavigatorContext.cs` — SQLite (было **in-memory**, `ConcurrentDictionary`, терялось при рестарте; этапом 1.5 был PostgreSQL, затем переведён на локальную SQLite ради экономии RAM на выделенном хосте). Таблица `Servers`: все прежние поля + `LastSeenAt` (заменяет in-memory `lastSeen`) + federation-поля (`ServerName` — уникальный частичный индекс `WHERE ServerName IS NOT NULL`, `FederationEndpoint`, `TlsSpkiSha256`, `FederationProtocolVersions`, `SigningKeys` — все три хранятся как `TEXT`/JSON: массивы через primitive-collections EF Core, `SigningKeys` через явный JSON-конвертер, список `{key_id, public_key_base64, expired_at}`, `NavigatorSigningKeyInfo`).

`ServersStorage` теперь **Scoped** (использует `NavigatorContext`), а не Singleton — троттлинг регистраций вынесен в отдельный синглтон `RegistrationThrottle` (тот же in-memory `ConcurrentDictionary`, потеря состояния при рестарте безвредна, как и раньше).

- `GetServersAsync()` — `IsManual || LastSeenAt в пределах ServerRegistration:ActivePeriodMinutes` (дефолт 10 мин); ручные записи закреплены навсегда
- `RegisterServerAsync()` — upsert. Ключ идентичности: `ServerName`, если задан; иначе легаси `Name+BeaconHost+BeaconPort`. `IsManual` существующей строки сохраняется
- `AddManualServerAsync()` / `DeleteManualServerAsync(id)` — добавление/удаление ручных записей (guard: только `IsManual`)
- `GetByServerNameAsync()` — lowercase-сравнение, `found=false` если протухло (тот же TTL, что у `GetServers`)

**Деплой**: Navigator живёт в отдельном compose-файле `docker/navigator/docker-compose-dev.yml`, отдельно от основного стека ноды. SQLite-файл лежит в bind-mount `./db:/app/db` (папка `db/` рядом с compose-файлом на хосте), `NAVIGATOR_DB=Data Source=/app/db/navigator.db`; сервис запущен с `user: root`, чтобы писать в bind-каталог (образ `Dockerfile.slim` остаётся chiseled/non-root). Отдельного контейнера БД больше нет. `NAVIGATOR_PORT` задаёт gRPC-порт, `NAVIGATOR_HTTP_PORT` — внутренний HTTP-порт админки; наружу к нему обращается только [[Backend/Nginx]].

## Валидация федеративной регистрации (этап 1.5)

Если в `RegisterServerRequest.ServerInfo` заполнен `server_name` — регистрация принимается только после:

1. `Features/RegisterServer/FederationServernameGuard.cs` — минимальная анти-SSRF-проверка (punycode-нормализация к A-label, запрет IP-литералов/`localhost`, DNS-резолв + отклонение приватных диапазонов). **Осознанное дублирование** `BarkFluff.Federation.Services.ServernameValidator` — Navigator публичен, не имеет общего кода с Federation; упрощение — без anti-rebinding IP-пиннинга (не привилегированный канал, только проверка перед регистрацией в каталоге).
2. `Features/RegisterServer/FederationWellKnownValidator.cs` — `GET https://{server_name}/.well-known/barkfluff` по CA-валидному HTTPS (без trust-all), лимит 64 КБ/10с; сверка `server_name` + всех заявленных `signing_keys` по `key_id`+байтам (без проверки Ed25519-подписи документа — это не входит в скоуп Navigator). Dev-флаг `NAVIGATOR_INSECURE_WELLKNOWN=1` (только вне production) отключает CA-валидацию для стенда.
3. Фетч не удался / документ невалиден / ключи не совпали → `FederationRegistrationRejectedException` (`Shared/BarkFluff.Shared.Exceptions/Navigator/`). Синтаксически невалидный `server_name` → `InvalidServernameException` (переиспользован из `Shared/BarkFluff.Shared.Exceptions/Federation/` — общая библиотека).

Легаси-регистрация (без `server_name`) — без изменений, никакой валидации сверх существующей.

## Domain/ServerInfo

Поля: `Id` (long, ключ), `CreatedAt`, `AddedBy`, `Name`, `BeaconHost`, `BeaconPort`, `Description`, `ServerPublicName`, `Location`, `ColorLiteHex/MainHex/HardHex` + этап 1.5: `LastSeenAt`, `ServerName?`, `FederationEndpoint?`, `TlsSpkiSha256?`, `FederationProtocolVersions?`, `SigningKeys? : List<NavigatorSigningKeyInfo>` + `WebEndpoint?` + `FilesMediaEndpoint?` + `IsManual` (ручная запись админа). **`AccountsCount` в доменной модели нет** — существует только в proto-ответе, всегда `0`.

### `web_endpoint` — адрес веб-клиента ноды

`ServerInfo.web_endpoint` (proto-поле 13) — абсолютный origin gRPC-Web шлюза ноды
(`https://gw.node.example`). Нужен для бутстрапа [[Клиенты/Web]]: браузер достаёт Beacon
**только через этот шлюз**, поэтому его адрес обязан приходить из каталога — иначе
дискавери замыкается сам на себя. Пустое значение = нода не предлагается веб-клиенту
(карточка в списке заблокирована).

- Заполняет [[Backend/Beacon]] (`ServerRegistrationService`) из `ExternalEndpoint:Host`
  сервиса Web. Недоступная конфигурация не ломает регистрацию — поле просто уходит пустым.
- Валидация в `RegisterServerCommandHandler`: длина ≤ 2048, абсолютный http/https-URI с
  разбираемым хостом, иначе `InvalidWebEndpointException`. Поле необязательное.
- Отдаётся в `ListServers` и `GetServerByName`, показывается в админке.
- ⚠️ Схему создаёт `EnsureCreated()`, миграций в проекте нет — новая колонка на
  существующей БД сама не появляется. Поэтому `Program.cs` после `EnsureCreated()` делает
  идемпотентный `ALTER TABLE "Servers" ADD COLUMN` (`EnsureServersColumn`, проверка через
  `pragma_table_info`). Следующие поля добавлять так же, иначе запрос упадёт с
  «no such column» у всех клиентов.

### `files_media_endpoint` — отдельный файловый адрес ноды

`ServerInfo.files_media_endpoint` (proto-поле 14) — абсолютный origin файлового HTTP ноды
(`https://files2.node.example`), направленный мимо CDN с его лимитом на размер файла
([[Backend/Nginx]]). Пустое значение = файлы качаются по адресу [[Backend/Files]], как раньше.

- Заполняет [[Backend/Beacon]] (`ServerRegistrationService`) из `ExternalEndpoint:MediaHost`
  сервиса Files; недоступная конфигурация оставляет поле пустым.
- Валидация в `RegisterServerCommandHandler` — та же, что у `web_endpoint` (`IsValidPublicEndpoint`,
  длина ≤ 2048), иначе `InvalidFilesMediaEndpointException`. Поле необязательное.
- Отдаётся в `ListServers` и `GetServerByName`, показывается в админке.
- Колонка добавляется тем же идемпотентным `EnsureServersColumn` в `Program.cs` (миграций нет).

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

`Tests/BarkFluff.Navigator.Tests/` — 60 тестов (EF InMemory, без Docker/Postgres): легаси-валидация регистрации (не изменилась), `FederationServernameGuardTests` (IP/localhost/punycode), `RegisterServerCommandHandlerTests` — федеративная регистрация (well-known подтвердил/отклонил, невалидный `server_name`) через тестовый двойник `FederationWellKnownValidator` (виртуальный `ValidateAsync`, переопределён в тесте — без реальной сети), `GetServerByNameQueryHandlerTests` (найдено/не найдено/протухло), `ServersStorageManualTests` — ручные записи (видимость после TTL, upsert сохраняет `IsManual`, delete-guard).

## Добавление нового поля в ServerInfo

1. Добавить поле в `Domain/ServerInfo.cs` (+ миграцию, если персистентное)
2. Добавить поле в `navigator_api.proto`
3. Обновить маппинг в `NavigatorApiService.cs` (request → domain)
4. Обновить маппинг в `ListServersQueryHandler.cs`/`GetServerByNameQueryHandler.cs` (domain → response) — учти правило «не менять контракт `ListServers`» для существующих полей
# Метрики

Navigator экспортирует в Seq через [[Backend/GrpcServer]] число успешных регистраций и запросов списка/поиска серверов. Поскольку сервис не использует [[Backend/Configuration]], адрес Seq берётся из локальной конфигурации или стандартного `http://seq:5341`.
