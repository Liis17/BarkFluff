# BarkFluff.ClientV2.WPF

Parent: [[Index]]

## Назначение

Новый Windows-клиент BarkFluff. Реализуется независимо от замороженного [[Клиенты/Windows-WPF]]: WPF .NET 10, MVVM, DI и WPF UI.

Расположение: `Windows/BarkFluff.ClientV2.WPF/`  
Target framework: `net10.0-windows10.0.26100.0`

## Сборка и тесты

```bash
dotnet build Windows/BarkFluff.ClientV2.WPF/BarkFluff.ClientV2.WPF.csproj
dotnet test Tests/BarkFluff.ClientV2.WPF.Tests/BarkFluff.ClientV2.WPF.Tests.csproj
```

Требуется SDK из корневого `global.json`.

## Архитектура

- `Views` — XAML-представления без UI-логики в code-behind.
- `ViewModels` — состояние и команды через CommunityToolkit.Mvvm.
- `Services` — навигация, подключение к ноде и сессия; `OnboardingNavigationService` хранит последнее ViewModel, поэтому стартовый маршрут не зависит от порядка создания окна. [[Клиенты/Windows-WebApiCore]] используется только через `INodeConnectionService`.
- `Infrastructure/Storage` — SQLite `data/barkfluff.db` рядом с exe: настройки онбординга/языка и выбранная нода.
- `Infrastructure/Localization` — смена `ResourceDictionary`; `Resources/Localization/Strings.ru.xaml` и `Strings.en.xaml` имеют одинаковые ключи, в XAML используются `DynamicResource`.
- `Resources/Styles/Controls.xaml` — единая точка общих стилей интерфейса поверх WPF UI: Fluent-контролы, поверхности, карточки и типографика.

## Визуальный стиль

- Интерфейс следует Windows 11 Fluent: `ApplicationThemeManager` включает светлую тему, Mica и системный accent; `MainWindow` содержит `TitleBar` WPF UI.
- Для интерактивных элементов использовать только `ui:*`-контролы WPF UI, не нативные WPF `Button`, `TextBox`, `ListBox` или `ProgressBar`.
- Экраны онбординга используют общую типографику `Segoe UI Variable Text`, карточки, мягкие поверхности и короткую декларативную анимацию появления.

## Темы и вход

- `Resources/Colors/Light.xaml`, `Dark.xaml` и `BarkFluffDark.xaml` содержат семантические палитры. `ApplicationThemeService` применяет режим из SQLite: `System`, `Light`, `Dark` или фирменный `BarkFluffDark` с accent `#81341E`; первые три используют системный Windows accent, а `SystemThemeWatcher` отслеживает изменения Windows в запущенном приложении.
- После `GetServerInfo` `NodeServiceConfiguration` сохраняет endpoints Beacon, Identity, Users, Files, Messages, Updates, Onliner, FastAuth и Calls без токенов. На следующем запуске конфигурация восстанавливает `IClientSession` и открывает вход.
- `LoginViewModel` общается только с `IAuthenticationService`: обычный вход поддерживает login/email, пароль и 2FA. Код OTP — шесть полей, полная вставка в первом поле заполняет их все и запускает проверку. FastAuth создаёт анонимную QR-сессию, слушает server-stream статусов; `SCANNED` показывает ожидание, `ACCEPTED` применяет токены, а `REJECTED`/`EXPIRED` автоматически выпускают новый QR. При уходе с экрана стрим отменяется.
- `RegistrationViewModel` реализует минимальный серверный флоу `CreateAccount → ConfirmAccount → SetPassword`; `PasswordRecoveryViewModel` — `ResetPassword → ConfirmResetPassword → SetPassword`. Все строки экранов находятся в синхронизированных RU/EN resource dictionaries.
- Access/refresh токены живут только в памяти текущей сессии. Их постоянное хранение требует отдельной DPAPI-миграции.

Подробные правила написания кода: `Windows/BarkFluff.ClientV2.WPF/docs/Architecture.md`.

Полная карта классов и ресурсов: [[Клиенты/Windows-WPF-V2-ProjectMap]].

## Первый реализованный маршрут

```
First run: Welcome → Select node → Connected node
Next runs: Select node → Connected node
```

`SelectNodeViewModel` получает публичные ноды из Navigator и позволяет вручную подключиться к Beacon. Адреса `http(s)://host[:port]` и `host[:port]` поддерживаются; домен без порта использует HTTPS/443, IP требует явно указанный порт. `GetServerInfo` проверяет ноду, а `NodeConnectionMapper` переносит endpoint’ы сервисов в параметры сессии.

## Важные ограничения

- Кеш сообщений и локальные таблицы для него пока не реализованы.
- При добавлении токенов использовать отдельную миграцию SQLite и DPAPI; открытое хранение запрещено.
- Каталог рядом с exe должен быть доступен для записи; установка в `Program Files` требует отдельного решения прав доступа.
