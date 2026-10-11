# BarkFluff.Settings

Централизованный каталог допустимых конфигурационных ключей для сервисов BarkFluff. Сохраняет wire-compatible имена из configuration_api.proto, package ConfigurationApi и номера полей; отдельный SettingsSetupApi обслуживает initial setup. Runtime port по умолчанию 7003. Код: Backend/BarkFluff.Settings/.

## Хранение и обновление

PostgreSQL SettingsContext маппит 16 конфигурационных таблиц (GlobalSettings и по таблице на каждый ServiceId), с Key как PK; дополнительно есть SettingsHistory, ReservedNames и SetupState. Поэтому в базе 19 mapped tables, не 16 всего.

SettingsCatalog разрешает только известные ServiceId/Section/Key и переводит legacy names в StorageKey. Неизвестный ключ отклоняется. Setup metadata скрывает значение sensitive-поля, но возвращает его признак и `Configured`; runtime ConfigurationApi отдаёт реальные значения потребляющим сервисам. Defaults добавляются только для отсутствующих строк. Изменения хранят ChangedBy/ChangedFrom и revision; rollback создаёт новую ревизию, а не удаляет историю.

Program.cs применяет EF migrations при старте. PostgreSQL database должен уже существовать (compose создаёт её отдельно). Настройки читаются клиентами из Settings при старте; автоматического push/live reload нет, поэтому изменение обычно требует перезапуска потребляющих процессов.

## Первичная настройка

SettingsSetupApi защищён x-settings-setup-token и доступен только при SETTINGS_SETUP_MODE=true. Секрет задаётся SETTINGS_SETUP_SECRET_FILE либо SETTINGS_SETUP_TOKEN. SaveSetupGroup валидирует поля и зависимости из SettingsCatalog; CompleteSetup проверяет обязательные значения и фиксирует SetupState/fingerprint. При завершении setup блокируется повторная первичная запись.

Отключение Email снимает обязательность SMTP-полей; включённый Telegram требует BotToken и NodeName. Хотя бы один канал должен быть доступен для регистрации. Эти правила используются snapshot, сохранением и completion. Setup UI: [[Backend/Setup]]; admin editor: [[Backend/AdminPanel]].

Сборка: dotnet build Backend/BarkFluff.Settings/BarkFluff.Settings.csproj. Текущая таблица портов и deployment config: [[Backend/Nginx]].
