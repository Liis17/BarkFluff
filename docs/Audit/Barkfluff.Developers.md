# Аудит: Barkfluff.Developers
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка
Barkfluff.Developers — gRPC-Web backend (порт 7020) для портала документации: отдаёт разделы документации, содержимое `.proto`-файлов и реестр кодов ошибок. Весь API закрыт политикой `TokenType.User`, так что документация доступна только аутентифицированным пользователям (не публично). Критичных уязвимостей не найдено; основные замечания — раскрытие внутренних `.proto` любому аутентифицированному пользователю, незащищённая gRPC-рефлексия и две ошибки конфигурации в Docker/nginx (несоответствие портов и протоколов). Также есть мёртвый код (Create/Update/Delete-обработчики не подключены к proto/сервису).

**SQL-инъекции (доп. проверка 2026-07-22):** не найдены. `DocumentationStorage`/`ProtoMetadataStorage` используют только EF Core LINQ, raw-SQL в сервисе отсутствует.

| Критичность | Кол-во |
| ----------- | ------ |
| Critical    | 0      |
| High        | 0      |
| Medium      | 4      |
| Low         | 4      |

## Безопасность

### S1. Раскрытие всех `.proto`-файлов, включая внутренние сервисы — Medium
**Файл:** `Backend/Barkfluff.Developers/Barkfluff.Developers.csproj:41`, `Backend/Barkfluff.Developers/Infrastructure/ProtoFileProvider.cs:12-19`, `Backend/Barkfluff.Developers/Features/GetProtoFileContent/GetProtoFileContentQuery.cs:27`
**Проблема:** В образ копируются ВСЕ `.proto` из `Shared/BarkFluff.Proto` (`Content Include="..\..\Shared\BarkFluff.Proto\*.proto"`), а `ProtoFileProvider` кэширует любой `*.proto` из каталога `Proto`. `GetProtoFileContent` отдаёт содержимое любого файла из кэша по имени, даже если для него нет метаданных. Таким образом любой аутентифицированный пользователь может скачать описания внутренних сервисов: `configuration_api.proto`, `beacon_api.proto`, `navigator_api.proto`, `fast_auth_api.proto` — не предназначенных для внешней публикации.
**Почему это проблема:** Раскрывает внутреннюю поверхность API (имена сервисов, RPC, поля), упрощая разведку для атак на внутренние сервисы (Configuration, Navigator, Beacon), которые не должны быть видны клиентам.
**Рекомендация:** Копировать в образ только публичные proto (явный whitelist), либо фильтровать список отдаваемых файлов по наличию записи в `ProtoMetadata` (которая контролирует, что публикуется).

### S2. gRPC-рефлексия включена и не защищена авторизацией — Medium
**Файл:** `Backend/Barkfluff.Developers/Program.cs:36, 78`
**Проблема:** `AddGrpcReflection()` + `MapGrpcReflectionService()` подключают сервис рефлексии без какой-либо политики авторизации (в отличие от `DevelopersApiService`, помеченного `[Authorize]`). Рефлексия позволяет неаутентифицированному клиенту перечислить определения gRPC-сервисов.
**Почему это проблема:** Рефлексия в проде — лишняя поверхность для разведки; полезна при разработке, но в продакшене даёт злоумышленнику схему сервиса бесплатно.
**Рекомендация:** Включать рефлексию только в Development (`if (app.Environment.IsDevelopment())`), либо закрыть её авторизацией.

### S3. CORS `AllowAnyOrigin` + `AllowAnyHeader` — Low
**Файл:** `Backend/Barkfluff.Developers/Program.cs:48-54`
**Проблема:** Политика CORS разрешает любой origin, любой метод и любой заголовок.
**Почему это проблема:** Сам по себе риск низкий — аутентификация идёт через заголовок `x-auth-token` (bearer-стиль), а не cookie, и `AllowCredentials` не выставлен, поэтому браузер не отдаст cookie кросс-доменно. Но политика шире необходимого.
**Рекомендация:** Ограничить список origin'ов доменом портала документации; заголовки — конкретным набором (как сделано в `BarkFluff.Web`).

### S4. Хардкод учётных данных БД в design-time фабрике — Low
**Файл:** `Backend/Barkfluff.Developers/Persistence/DevelopersContextFactory.cs:12`
**Проблема:** `UseNpgsql("Host=localhost;Database=developers;Username=postgres;Password=postgres")` — строка подключения с паролем зашита в код.
**Почему это проблема:** Используется только design-time (миграции EF), в рантайме строка берётся из `builder.Configuration["DevelopersDb"]` (Program.cs:41), поэтому в проде это не задействовано. Тем не менее это пример паттерна хранения секретов в коде.
**Рекомендация:** Оставить как есть (это паттерн проекта для миграций) либо читать строку из переменной окружения и в фабрике.

### Примечание (не находка): мёртвый код
Обработчики `CreateSectionCommand` (`Features/CreateSection/CreateSectionCommand.cs`), `UpdateSectionCommand` и `DeleteSectionCommand` реализованы, но НЕ подключены ни к proto (`developers_api.proto` содержит только 5 read-RPC), ни к `DevelopersApiService.cs`. То есть запись/изменение/удаление документации недостижимы извне — это хорошо с точки зрения безопасности (нет публичного эндпоинта изменения контента), но это мёртвый код. Удалять без отдельного запроса не нужно.

## Производительность

### P1. `GetErrorCodes` читает всю таблицу из БД на каждый вызов без кэша — Low
**Файл:** `Backend/Barkfluff.Developers/Features/GetErrorCodes/GetErrorCodesQuery.cs:21`
**Проблема:** `await _context.ErrorCodes.ToListAsync()` выполняется при каждом запросе. Данные сидируются один раз при старте (`ErrorCodeSeeder`) и далее неизменны.
**Почему это проблема:** Лишний round-trip к Postgres на статичные данные. Объём небольшой, нагрузка низкая, поэтому критичность Low.
**Рекомендация:** Закэшировать ответ в памяти (как уже сделано для `.proto` в `ProtoFileProvider`), сбрасывая кэш только при пересидировании.

### Позитив
`ProtoFileProvider` (`Infrastructure/ProtoFileProvider.cs`) читает все `.proto` в память один раз в конструкторе синглтона — корректное кэширование, файлы не читаются с диска на каждый запрос. Path traversal невозможен: запрос идёт по ключу словаря, файловая система в рантайме не трогается.

## Docker / nginx

### D1. Несоответствие проброшенного порта и порта прослушивания — Medium
**Файл:** `docker/backend/docker-compose-dev-backend.yml:169-170`
**Проблема:** Сервис публикует `"4425:4425"`, но Kestrel слушает порт 7020 (`Program.cs:26-29`, дефолт 7020; внутренний трафик идёт через nginx на `developers:7020`). На порту 4425 ничего не слушает.
**Почему это проблема:** Либо мёртвый маппинг (порт открыт на хосте впустую), либо опечатка. Если намерение было пробросить 7020 — это открыло бы незашифрованный gRPC-порт (h2c) напрямую на хост в обход TLS-терминации nginx.
**Рекомендация:** Убрать секцию `ports` (сервис доступен внутри Docker-сети для nginx), либо привести к реальному порту, осознавая последствия прямого проброса.

### D2. Возможное несоответствие протоколов nginx↔Kestrel для gRPC-Web — Medium
**Файл:** `Backend/Barkfluff.Developers/Program.cs:29-32`, `docker/nginx/developers.conf:15-24`
**Проблема:** Kestrel сконфигурирован только на HTTP/2 (`listenOptions.Protocols = HttpProtocols.Http2`), тогда как nginx делает `proxy_pass http://developers:7020` обычным `proxy_pass` (HTTP/1.1 к upstream, без `grpc_pass` и без `proxy_http_version 2`). gRPC-Web из браузера приходит по HTTP/1.1. Для сравнения, `BarkFluff.Web` намеренно слушает `Http1AndHttp2`.
**Почему это проблема:** HTTP/1.1-запрос от nginx к HTTP/2-only Kestrel не согласуется по протоколу — gRPC-Web через nginx, скорее всего, не работает (доступность). Замечание дано с оговоркой: подтвердить можно только запуском.
**Рекомендация:** Поставить `HttpProtocols.Http1AndHttp2` (как в Web), либо настроить nginx на `grpc_pass`/HTTP/2 к upstream.

### D3. Сервис `developers` опубликован напрямую на хост в dev-compose — Low
**Файл:** `docker/backend/docker-compose-dev-backend.yml:169-170`
**Проблема:** Единственный микросервис с секцией `ports` в dev-compose (остальные доступны только внутри сети). См. также D1.
**Рекомендация:** Убрать проброс, если прямой доступ к сервису с хоста не требуется.

### Позитив
Dockerfile корректен: multi-stage build, финальный образ `aspnet:10.0-noble-chiseled`, запуск под непривилегированным `$APP_UID`, `--chown` на скопированные файлы.
