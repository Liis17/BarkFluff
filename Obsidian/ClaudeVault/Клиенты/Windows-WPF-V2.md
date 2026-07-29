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
- Access/refresh токены сохраняются в SQLite только как DPAPI (`CurrentUser`) blob. При следующем запуске клиент восстанавливает выбранную ноду, обновляет access token через refresh token и, если проверка профиля успешна, сразу открывает главный экран; повреждённая или недействительная сессия удаляется.

## Мессенджер

- `MessengerViewModel` — основной маршрут после авторизации: двухпанельный экран со списком чатов, областью выбранного чата, загрузкой истории и composer. Обычные сообщения отправляются кнопкой или `Enter`; `Shift+Enter` оставляет перенос строки.
- Список и история берутся через [[Клиенты/Windows-WebApiCore]] (`GetChats`, `GetMessagesWithOffset`, `SendMessage`), а пользователь текущей сессии загружается после входа для корректного выравнивания исходящих сообщений.
- `DpapiSecureSessionStore` изолирует чувствительные токены от открытой SQLite-базы. Кеш сообщений, файлов и realtime/presence/private-chat flows остаются следующими подэтапами реализации.

Подробные правила написания кода: `Windows/BarkFluff.ClientV2.WPF/docs/Architecture.md`.

Матрица функций всех клиентов и бэклог V2 по этапам: `docs/clients-feature-matrix.md`.

Полная карта классов и ресурсов: [[Клиенты/Windows-WPF-V2-ProjectMap]].

## Первый реализованный маршрут

```
First run: Welcome → Select node → Connected node
Next runs: Select node → Connected node
```

`SelectNodeViewModel` получает публичные ноды из Navigator и позволяет вручную подключиться к Beacon. Адреса `http(s)://host[:port]` и `host[:port]` поддерживаются; домен без порта использует HTTPS/443, IP требует явно указанный порт. Перед каждым `GetServerInfo` [[Клиенты/Windows-WPF-V2-ProjectMap|NodeConnectionService]] пересоздаёт Beacon-клиент для выбранного адреса, поэтому возврат со входа и смена ноды не используют старый канал. `NodeConnectionMapper` переносит endpoint’ы сервисов в параметры сессии.

## Важные ограничения

- Кеш сообщений и локальные таблицы для него пока не реализованы.
- При добавлении токенов использовать отдельную миграцию SQLite и DPAPI; открытое хранение запрещено.
- Каталог рядом с exe должен быть доступен для записи; установка в `Program Files` требует отдельного решения прав доступа.
