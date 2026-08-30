# BarkFluff.Settings

Новый сервис настроек, который постепенно заменяет [[Backend/Configuration]]. Работает параллельно с legacy Configuration на внутреннем адресе `settings:7003`, не публикуется наружу и не использует XAuth.

Расположение: `Backend/BarkFluff.Settings/`.

## Совместимость

Settings использует без изменений `Shared/BarkFluff.Proto/configuration_api.proto`: package, `ConfigurationApi`, RPC, сообщения и номера полей сохранены. Старые сервисы продолжают вызывать `LoadConfiguration(ServiceId)` и переключаются без перекомпиляции через:

```env
CONFIGURATION_SERVICE_URL=http://settings:7003
```

`ServiceId` существует только на границе compatibility API и в статическом каталоге маршрутизации. В базе колонки `ServiceId` нет. `GetConfiguration` объединяет глобальную и сервисную таблицы; сервисное значение перекрывает глобальное с тем же IConfiguration-путём. Live reload не реализован: после изменения потребитель требуется перезапустить.

## Persistence

PostgreSQL БД по умолчанию — `settings`. Startup initializer делает до пяти попыток, через Npgsql создаёт отсутствующую БД, применяет EF migrations, дополняет каталог и проверяет инварианты. Ошибка bootstrap останавливает запуск.

16 таблиц (`GlobalSettings`, `IdentitySettings`, …, `FederationSettings`) используют один shared CLR-тип `SettingRow`. В каждой только `Key` (полный IConfiguration-путь, PK), `Value`, `EditedBy`, `EditedAt`. Section-only параметры хранятся одним ключом (`Redis`, `DevelopersDb`, `NavigatorUrl`), вложенные — полным путём (`S3Buckets:message-audio:SecretKey`).

`SettingsHistory` бессрочно хранит old/new value, автора, источник, вид изменения и optional self-reference `SourceRevisionId` для rollback. Индекс: `(SettingsTable, Key, ChangedAt DESC, Id DESC)`. `EditedFrom` в рабочих строках отсутствует и для compatibility-ответа берётся из последней revision. Seed не создаёт историю.

`ReservedNames` нормализована: одно lowercase-имя на строку, имя является primary key. Compatibility CRUD RPC сохранены.

## Строгий каталог и readiness

`SettingsCatalog` — единственный разрешённый список параметров и обратимое соответствие legacy `ServiceId + Section + Key` → `SettingsTable + storage Key`. Произвольные ключи отклоняются; новый параметр добавляется кодом и тестом. При старте вставляются только отсутствующие строки, существующие значения не перезаписываются. JWT secret и service-токены создаются один раз.

Поля без безопасного default создаются пустыми. `SettingsReadinessContributor` возвращает `degraded` и отсортированный список незаполненных полей. Основные manual-поля: Beacon `ServerProps`/`ServerColor`, SMTP `Email`, Files `ExternalEndpoint:MediaHost`, federation domain/TLS/window parameters.

## Переменные окружения

- `SETTINGS_HOST`, `SETTINGS_DBPORT`, `SETTINGS_DATABASE`, `SETTINGS_USERNAME`, `SETTINGS_PASSWORD`
- `SETTINGS_ADMIN_DATABASE` (по умолчанию `postgres`), `SETTINGS_PORT` (по умолчанию `7003`)
- старые `CONFIGURATION_*` поддерживаются как fallback; compose явно задаёт `SETTINGS_DATABASE=settings`, чтобы не подключиться к legacy БД

## Переключение

Во всех `docker/{dev,nightly,master}/barkfluff` Settings запускается параллельно с Configuration. Базовый compose оставляет потребителей на Configuration. После заполнения manual-полей и при необходимости переноса пользовательских reserved names используется override:

```bash
docker compose -f docker-compose.yml -f docker-compose.settings-cutover.yml up -d --force-recreate
```

Override меняет только URL и startup dependency. Старые контейнер и БД Configuration остаются доступны для длительной отладки и отката.

## Проверка

```bash
dotnet build Backend/BarkFluff.Settings/BarkFluff.Settings.csproj
dotnet test Tests/BarkFluff.Settings.Tests/BarkFluff.Settings.Tests.csproj
```

InMemory-тесты покрывают каталог, idempotent seed, precedence, историю/rollback, reserved names, readiness, proto snapshot и вызов нового сервера старым generated gRPC client. Создание БД и физическую PostgreSQL-схему перед cutover обязательно проверить на staging.
