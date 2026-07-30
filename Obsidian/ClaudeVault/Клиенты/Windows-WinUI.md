# BarkFluff.Client.WinUI

Parent: [[Index]]

## Назначение

Целевой Windows-клиент BarkFluff. Порт [[Клиенты/Windows-WPF-V2]] на WinUI 3 (Windows App SDK) с переходом на нативные идиомы платформы. WPF-версия остаётся предшественником и источником поведения.

Расположение: `Windows/BarkFluff.Client.WinUI/` + `Windows/BarkFluff.Client.Core/`
Target framework: `net10.0-windows10.0.26100.0`, Windows App SDK `2.3.1`, упаковка **MSIX**.

## Сборка и тесты

```bash
dotnet build Windows/BarkFluff.Client.WinUI/BarkFluff.Client.WinUI.csproj -p:Platform=x64
dotnet test Tests/BarkFluff.Client.WinUI.Tests/BarkFluff.Client.WinUI.Tests.csproj
bash Windows/BarkFluff.Client.WinUI/tools/check-localization.sh
```

Платформы `x86;x64;ARM64`, конфигурации `Any CPU` в solution отображены на `x64`. `PublishTrimmed` и `PublishAot` выключены во всех конфигурациях: WinUI активирует XAML-типы через рантайм-метаданные, gRPC/Protobuf строят дескрипторы рефлексией, а `StoredSession` сериализуется `System.Text.Json` без source-gen — под trimming это ломается в рантайме, а не на сборке.

## Разделение на две сборки

`BarkFluff.Client.Core` — слой без UI-фреймворка: `Models`, `Services`, `Infrastructure/Storage`, `ViewModels`, интерфейсы `ILocalizationService` и `IUiDispatcher`. Ссылается на [[Клиенты/Windows-WebApiCore]].

`BarkFluff.Client.WinUI` — только представление: `App`, `MainWindow`, `Views`, `Infrastructure/{Appearance,Converters,Dialogs}` и WinUI-реализации `LocalizationService` / `DispatcherQueueUiDispatcher`.

Разделение не косметическое: обращение к любому типу из WinUI-сборки запускает её module initializer (Windows App SDK `DeploymentManager`), который вне пакетной идентичности падает с `REGDB_E_CLASSNOTREG`. Без отдельной Core-сборки юнит-тесты незапускаемы в принципе.

## Ограничения WinUI, определившие архитектуру

- **`DynamicResource` не существует.** `StaticResource` не перевычисляется, `ThemeResource` — только при смене `ElementTheme`.
- **Неявных `DataTemplate` по `DataType` нет** — навигация построена на `Frame` + `Page` и таблице `Views/ViewLocator.cs`.
- **Нет** `Style.Triggers`/`DataTrigger`, `StringFormat`, `InputBindings`, `RelativeSource AncestorType`, `UniformGrid`, `MediaElement`, `BooleanToVisibilityConverter`.
- **`ThemeDictionaries` знает только Light/Dark/HighContrast** — четвёртая тема `BarkFluffDark` туда не помещается.
- WinUI **не публикует** `DefaultListViewStyle`, поэтому `Style TargetType="ListView"` без `BasedOn` затёр бы `ControlTemplate`; свойства списка задаются прямо на контроле.

## Локализация

`Resources/Localization/Strings.{ru,en}.xaml` — по 136 ключей `x:String`, в XAML используется `{StaticResource}`. `LocalizationService.Apply` домерживает нужный словарь в `Application.Resources` **до создания первого окна**; `GetString` читает `Application.Current.Resources.TryGetValue`.

Смена языка на лету не поддерживается (её нет и в UI). Отсутствующий ключ в WinUI — исключение в рантайме, а не пустая строка, поэтому `tools/check-localization.sh` проверяет паритет ru/en и наличие каждого ключа, использованного в XAML.

## Темы

`Resources/Colors/{Light,Dark,BarkFluffDark}.xaml` содержат `Color`, а не кисти. Семантические кисти объявлены синглтонами в `Resources/Colors/Brushes.xaml` и подключены к `App.xaml`; `ApplicationThemeService` переписывает им `SolidColorBrush.Color` — это обновляет всех потребителей и обходит отсутствие `DynamicResource`.

- `PrepareAccent` для `BarkFluffDark` пишет `SystemAccentColor*` от `#81341E` **до** создания контента окна: встроенные `AccentFillColor*Brush` резолвятся из них при парсинге.
- `RequestedTheme` ставится на корневой элемент окна, а не на `Application`: сеттер приложения валиден только до создания первого окна, а тема приезжает из SQLite позже.
- Замена `SystemThemeWatcher` — подписка на `FrameworkElement.ActualThemeChanged`. Замена Mica — `Window.SystemBackdrop = MicaBackdrop`.
- Соответствие ключей WPF UI → WinUI: `ApplicationBackgroundBrush` → `ApplicationPageBackgroundThemeBrush`, `SystemAccentColorPrimaryBrush` → `AccentFillColorDefaultBrush`; остальные совпадают.

## Хранилище

`AppDataPaths.CreateDefault()` выбирает `ApplicationData.Current.LocalFolder` при пакетной идентичности и `%LOCALAPPDATA%\BarkFluff` без неё — каталог установки MSIX доступен только на чтение. `LegacyDatabaseImporter` при первом старте копирует `data/barkfluff.db` (+ `-wal`/`-shm`) из раскладки «рядом с exe», если целевой базы ещё нет.

DPAPI под MSIX работает без ограничений (`rescap:runFullTrust`). Строки entropy в `DpapiSecureSessionStore` и `DpapiPrivateChatKeyStore` намеренно сохранили значения WPF-клиента (`BarkFluff.ClientV2.WPF.*`): они входят в блоб, и переименование клиента сделало бы импортированную сессию нерасшифровываемой.

## Shell

`MainWindow` — контрол `TitleBar` (`ExtendsContentIntoTitleBar` + `SetTitleBar`) с кнопкой настроек в `RightHeader`, `Frame` для контента и `TaskbarIcon` из `H.NotifyIcon.WinUI`. WinUI-версия H.NotifyIcon не даёт событий клика, только команды, поэтому двойной клик по значку привязан к `DoubleClickCommand` из code-behind.

Закрытие обрабатывает `AppWindow.Closing`: в режиме `MinimizeToTray` отмена + `Hide()`, пункт «Выход» выставляет флаг и закрывает по-настоящему. Настройки открываются `ContentDialog` через `IDialogService` вместо самодельного оверлея-скрима.

`App.OnLaunched` повторяет конвейер WPF-версии (стор → тема → локализация → `SettingsViewModel` → восстановление ноды/сессии → навигация → окно), включая `ShutdownHost` с теардауном вне UI-потока и таймаутом 2 с. Ошибка старта показывается отдельным окном: `ContentDialog` требует `XamlRoot`, которого до создания окна ещё нет.

Файлы, адресуемые как `ms-appx:///...`, обязаны быть объявлены `Content` в csproj — иначе они не попадают в MSIX. Так было с иконкой трея: `BitmapImage` грузит `UriSource` асинхронно, уже после конструктора окна, поэтому ошибка не ловилась try/catch в `OnLaunched` и всплывала как необработанное исключение. Проверять раскладку пакета через `dotnet build` бесполезно — она собирается таргетом упаковки; смотреть надо `dotnet msbuild -t:GetPackagingOutputs -getItem:PackagingOutputs`.

В solution проекту нужны строки `.Deploy.0` во всех конфигурациях: `dotnet sln add` их не пишет, а без них Visual Studio отказывается запускать отладку MSIX-проекта.

## Страницы

`Frame.Navigate(pageType, viewModel)` + `OnNavigatedTo` присваивают типизированное свойство `ViewModel` и вызывают `Bindings.Update()`. Это позволяет использовать `x:Bind` вместо `Binding` — опечатки в биндингах становятся ошибками сборки, что критично, раз UI не прогоняется автоматически.

Замены контролов WPF UI → WinUI: `ui:Card` → `Border` со стилем `OnboardingCard`; `ui:Button Appearance="Primary"` → `Style="{StaticResource AccentButtonStyle}"`; `ui:PasswordBox RevealButtonEnabled` → `PasswordRevealMode="Peek"`; `ui:ProgressRing` → `ProgressRing IsActive="True"` с `Visibility` по флагу (так поведение совпадает с WPF: скрытый индикатор не занимает места); fade-in `Storyboard` четырёх экранов → `EntranceNavigationTransitionInfo` на `Frame`.

`OtpInputBehavior` переписан на `BeforeTextChanging` (отменяемый фильтр цифр), `TextChanged` + `FocusManager.TryMoveFocus` и `TextBox.Paste`. Отличие от WPF: буфер обмена в WinRT читается только асинхронно, а `Handled` обязан выставляться синхронно, поэтому штатная вставка отменяется всегда и весь разбор текста делает `PasteOtpCodeCommand`.

## Состояние порта

| Этап | Содержимое | Статус |
| --- | --- | --- |
| 1 | Каркас, Core-сборка, инфраструктура, shell, трей, настройки, тесты | Готово |
| 2 | Онбординг и авторизация (6 страниц, FastAuth QR, OTP) | Готово |
| 3 | Мессенджер (список чатов, лента, composer) | Не начат |
| 4 | Действия над сообщениями (ответ, пересылка, закрепы, удаление) | Не начат |

ViewModel'и всех экранов уже перенесены в Core и покрыты частью тестов; на этапах 2–4 добавляется разметка страниц и оставшиеся тесты.

## Известные отличия от WPF-версии

- `LoginViewModel.FastAuthQrCode` отдаёт base64-строку, а не `ImageSource`; картинку собирает `Base64ToImageSourceConverter`.
- `MessengerViewModel` использует `IUiDispatcher` вместо `Application.Current.Dispatcher`; буфер обмена — `DataPackage` + `Clipboard.SetContent`, изображение копируется как `RandomAccessStreamReference` по URL.
- `MainWindowViewModel` больше не хранит `IsSettingsVisible`: настройки — `ContentDialog`.
- `MVVMTK0045` подавлен в Core: `[ObservableProperty]` на полях несовместим с AOT в WinRT-сценариях, но AOT и trimming выключены; переход на partial-свойства требует выноса инициализаторов в конструкторы и вынесен в отдельную задачу.
