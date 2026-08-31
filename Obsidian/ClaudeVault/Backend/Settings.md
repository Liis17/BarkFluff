# BarkFluff.Settings

Сервис настроек, который является единственным источником runtime-конфигурации. Для
совместимости сохраняет wire-имена `configuration_api.proto`/`ConfigurationApi`, но
рабочее хранилище — только `Settings`. Внутренний listener — `settings:7003`; setup
API защищён отдельным токеном.

Расположение: `Backend/BarkFluff.Settings/`.

## Совместимость

Settings использует без изменений `Shared/BarkFluff.Proto/configuration_api.proto`: package, `ConfigurationApi`, RPC, сообщения и номера полей сохранены. Клиенты, собранные со старым generated-контрактом, могут переключиться без перекомпиляции через:

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

Поля без безопасного default создаются пустыми. `SettingsReadinessContributor` возвращает `degraded` и отсортированный список незаполненных или невалидных полей. Основные setup-поля: Beacon `ServerProps`/`ServerColor`, SMTP `Email`, Files `ExternalEndpoint:MediaHost`, S3/MinIO credentials, LiveKit credentials и federation domain/TLS/window parameters. Каталог setup содержит 37 полей: 36 обязательных manual-полей и переключатель федерации; federation-поля становятся обязательными только при `Federation:Enabled=true`.

## Первичная настройка

[[Backend/Setup]] поднимается отдельным Compose только вместе с PostgreSQL и
`Settings`. `SettingsSetupApi` предоставляет `GetSetupState`, `SaveSetupGroup` и
`CompleteSetup`; значения валидируются на сервере и чувствительные поля маскируются.
После завершения в `SetupState` сохраняется fingerprint каталога и время операции.
Наличие записи `SetupState` означает необратимую блокировку setup API; fingerprint
остаётся для аудита и диагностики и не открывает setup повторно при добавлении новых
полей. Дальнейшие изменения выполняются AdminPanel. Bootstrap создаёт только БД `settings`, старые
данные Configuration не импортируются.

## Переменные окружения

- `SETTINGS_HOST`, `SETTINGS_DBPORT`, `SETTINGS_DATABASE`, `SETTINGS_USERNAME`, `SETTINGS_PASSWORD`
- `SETTINGS_ADMIN_DATABASE` (по умолчанию `postgres`), `SETTINGS_PORT` (по умолчанию `7003`)
- `CONFIGURATION_SERVICE_URL` поддерживается только как runtime-fallback для старых образов; новые deployment-конфигурации используют `SETTINGS_SERVICE_URL`
- `SETTINGS_SETUP_MODE`, `SETTINGS_SETUP_SECRET_FILE`/`SETTINGS_SETUP_TOKEN` — включение и секрет setup gRPC API
- `SETTINGS_SERVICE_URL` — адрес Settings, который используют потребители

## Deployment

Bootstrap-compose (`Docker/{dev,nightly,master}/barkfluff/docker-compose.setup.yml`)
поднимает только PostgreSQL, Settings и [[Backend/Setup]]. После заполнения формы
оператор останавливает bootstrap-compose и запускает основной compose. Основной стек
использует Settings напрямую; отдельной базы или контейнера Configuration больше нет.

## Проверка

```bash
dotnet build Backend/BarkFluff.Settings/BarkFluff.Settings.csproj
dotnet test Tests/BarkFluff.Settings.Tests/BarkFluff.Settings.Tests.csproj
```

InMemory-тесты покрывают каталог, idempotent seed, precedence, историю/rollback, reserved names, readiness, proto snapshot и вызов нового сервера старым generated gRPC client. Создание БД и физическую PostgreSQL-схему перед cutover обязательно проверить на staging.
