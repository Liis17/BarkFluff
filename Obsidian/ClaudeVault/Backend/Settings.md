# BarkFluff.Settings

Централизованный каталог допустимых конфигурационных ключей для сервисов BarkFluff. Сохраняет wire-compatible имена из configuration_api.proto, package ConfigurationApi и номера полей; отдельный SettingsSetupApi обслуживает initial setup. Runtime port по умолчанию 7003. Код: Backend/BarkFluff.Settings/.

## Хранение и обновление

PostgreSQL SettingsContext маппит 16 конфигурационных таблиц (GlobalSettings и по таблице на каждый ServiceId), с Key как PK; дополнительно есть SettingsHistory, ReservedNames и SetupState. Поэтому в базе 19 mapped tables, не 16 всего.

SettingsCatalog разрешает только известные ServiceId/Section/Key и переводит legacy names в StorageKey. Неизвестный ключ отклоняется. Setup metadata скрывает значение sensitive-поля, но возвращает его признак и `Configured`; runtime ConfigurationApi отдаёт реальные значения потребляющим сервисам. Defaults добавляются только для отсутствующих строк. Изменения хранят ChangedBy/ChangedFrom и revision; rollback создаёт новую ревизию, а не удаляет историю.

Program.cs применяет EF migrations при старте. PostgreSQL database должен уже существовать (compose создаёт её отдельно). Настройки читаются клиентами из Settings при старте; автоматического push/live reload нет, поэтому изменение обычно требует перезапуска потребляющих процессов.

## Внутренняя готовность для загрузки конфигурации

`Host/ConfigurationReadinessEndpoints.cs` публикует анонимный `GET /internal/configuration/ready`
на внутреннем HTTP/2 listener Settings. Endpoint доступен после startup initializer, при каждом запросе
проверяет чтение всех таблиц настроек и актуальные правила `SettingsReadinessContributor`.
Успех — HTTP 200; недоступные таблицы, невалидные обязательные поля и ошибки проверки — HTTP 503
без значений настроек и подробностей исключения. Состояние Complete Setup не требуется;
условия SMTP, Telegram и федерации совпадают с существующим readiness contributor.

`?requireValidSettings=false` пропускает проверку обязательных ручных значений, сохраняя проверку
чтения таблиц. Этот режим использует [[Backend/AdminPanel]], чтобы исправлять настройки.
Общий `/health/ready` сохраняет свою семантику и кэш. Правила ожидания потребителей описаны
в [[Backend/GrpcServer]].

## Первичная настройка

SettingsSetupApi защищён x-settings-setup-token и доступен только при SETTINGS_SETUP_MODE=true. Секрет задаётся SETTINGS_SETUP_SECRET_FILE либо SETTINGS_SETUP_TOKEN. SaveSetupGroup валидирует поля и зависимости из SettingsCatalog; CompleteSetup проверяет обязательные значения и фиксирует SetupState/fingerprint. При завершении setup блокируется повторная первичная запись.

Отключение Email снимает обязательность SMTP-полей; включённый Telegram требует BotToken и NodeName. Хотя бы один канал должен быть доступен для регистрации. Эти правила используются snapshot, сохранением и completion. Setup UI: [[Backend/Setup]]; admin editor: [[Backend/AdminPanel]].

Сборка: dotnet build Backend/BarkFluff.Settings/BarkFluff.Settings.csproj. Текущая таблица портов и deployment config: [[Backend/Nginx]].

## Backend логирования

Для 15 сервисных scope Settings задаёт независимые root-level flags `ConsoleOutput` (default `true`),
`SeqEnable` (default `true`) и `LognaraEnable` (default `false`); это значения конкретного `ServiceId`,
без наследования logging flags из `GlobalSettings`. Для logging flags и параметров Lognara agent действует
приоритет CLI → environment → соответствующие Settings values → local configuration. Изменения читаются
при старте процесса. Settings инициализирует logger до готовности базы, поэтому startup events
управляются только локальными flags и не меняются задним числом сохранёнными Settings values.

Legacy `Logging:Backend` остаётся в `GlobalSettings` вместе с историей, но больше не выбирает sink.
`Lognara:AgentUrl` — отдельная глобальная настройка (default `http://127.0.0.1:7400/v1/logs`) с local
CLI/env override. `Lognara:AgentExecutable` не хранится в Settings и задаётся через конфигурацию
процесса (local configuration, environment или CLI). Состав sinks, запуск embedded agent и deployment описаны в [[Backend/GrpcServer]] и
`Docs/Logging.md`. Поиск Seq и rollups AdminPanel по-прежнему используют Seq.
