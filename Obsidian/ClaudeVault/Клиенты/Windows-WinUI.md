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

`MainWindow` — контрол `TitleBar` (`ExtendsContentIntoTitleBar` + `SetTitleBar`), под ним `NavigationView` с `Frame` внутри, плюс `TaskbarIcon` из `H.NotifyIcon.WinUI`. WinUI-версия H.NotifyIcon не даёт событий клика, только команды, поэтому двойной клик по значку привязан к `DoubleClickCommand` из code-behind.

`PaneDisplayMode="LeftMinimal"` — единственный режим, дающий ровно кнопку-бургер сверху слева и панель поверх контента. Пункта два: «Профиль» и «Настройки». `IsPaneVisible` включается только когда текущая вьюмодель — `MessengerViewModel`: на онбординге и логине идти из панели некуда.

Профиль и настройки навигируются **через `Frame` напрямую**, не через `IOnboardingNavigationService`: сервис заменяет `CurrentViewModel`, а `ShowMessenger()` вызывает `LoadAsync()` — возврат перезагружал бы весь список чатов. Возврат — штатной кнопкой «назад» `NavigationView` (`IsBackEnabled` обновляется в `Frame.Navigated`). Стек онбординга чистится при каждой навигации из сервиса, иначе «назад» уводило бы на экран логина. `MessengerPage` объявлена `NavigationCacheMode="Required"`, чтобы возврат не пересоздавал ленту.

`ProfilePage` — единственная страница, на которую переходят из другой страницы (из заголовка чата, с идентификатором собеседника), поэтому вьюмодель она берёт из `App.Services`, а не из `e.Parameter`.

Закрытие обрабатывает `AppWindow.Closing`: в режиме `MinimizeToTray` отмена + `Hide()`, пункт «Выход» выставляет флаг и закрывает по-настоящему.

`App.OnLaunched` повторяет конвейер WPF-версии (стор → тема → локализация → `SettingsViewModel` → восстановление ноды/сессии → навигация → окно), включая `ShutdownHost` с теардауном вне UI-потока и таймаутом 2 с. Ошибка старта показывается отдельным окном: `ContentDialog` требует `XamlRoot`, которого до создания окна ещё нет.

Файлы, адресуемые как `ms-appx:///...`, обязаны быть объявлены `Content` в csproj — иначе они не попадают в MSIX. Так было с иконкой трея: `BitmapImage` грузит `UriSource` асинхронно, уже после конструктора окна, поэтому ошибка не ловилась try/catch в `OnLaunched` и всплывала как необработанное исключение. Проверять раскладку пакета через `dotnet build` бесполезно — она собирается таргетом упаковки; смотреть надо `dotnet msbuild -t:GetPackagingOutputs -getItem:PackagingOutputs`.

В solution проекту нужны строки `.Deploy.0` во всех конфигурациях: `dotnet sln add` их не пишет, а без них Visual Studio отказывается запускать отладку MSIX-проекта.

## Страницы

`Frame.Navigate(pageType, viewModel)` + `OnNavigatedTo` присваивают типизированное свойство `ViewModel` и вызывают `Bindings.Update()`. Это позволяет использовать `x:Bind` вместо `Binding` — опечатки в биндингах становятся ошибками сборки, что критично, раз UI не прогоняется автоматически.

Замены контролов WPF UI → WinUI: `ui:Card` → `Border` со стилем `OnboardingCard`; `ui:Button Appearance="Primary"` → `Style="{StaticResource AccentButtonStyle}"`; `ui:PasswordBox RevealButtonEnabled` → `PasswordRevealMode="Peek"`; `ui:ProgressRing` → `ProgressRing IsActive="True"` с `Visibility` по флагу (так поведение совпадает с WPF: скрытый индикатор не занимает места); fade-in `Storyboard` четырёх экранов → `EntranceNavigationTransitionInfo` на `Frame`.

`OtpInputBehavior` переписан на `BeforeTextChanging` (отменяемый фильтр цифр), `TextChanged` + `FocusManager.TryMoveFocus` и `TextBox.Paste`. Отличие от WPF: буфер обмена в WinRT читается только асинхронно, а `Handled` обязан выставляться синхронно, поэтому штатная вставка отменяется всегда и весь разбор текста делает `PasteOtpCodeCommand`.

## Мессенджер

Две колонки: список чатов (`ListView` + `PersonPicture`, точка онлайна в углу аватара) и область чата (заголовок-кнопка → профиль собеседника, лента, композер).

Лента — **`ScrollViewer` + `ItemsRepeater`**, не `ListView`: `ScrollViewer` остаётся нашим элементом (`ChangeView` доступен напрямую), нет ненужного выделения и контейнерных стилей, которые в WinUI нельзя переопределить через `BasedOn`. Плата — виртуализация, которой в WPF здесь не было: прокрутка к сообщению обязана материализовать цель через `GetOrCreateElement`, найти готовый контейнер уже нельзя.

Прижатие короткой переписки к нижней кромке: `ScrollViewer` меряет содержимое бесконечной высотой, поэтому у `VerticalAlignment="Bottom"` нет запаса. Обёртке ленты выставляется `MinHeight` по фактическому размеру вьюпорта из `SizeChanged`.

`MessageFeedBehavior` заменяет 196-строчный `MessageListBehavior`: `ScrollRequest` (прокрутка), `IsAtBottom` через `ViewChanged` и отметки прочтения через `FrameworkElement.EffectiveViewportChanged` на элементах ленты. `EffectiveViewport` уже задан в координатах элемента и учитывает обрезку предками, поэтому доля видимости считается пересечением — событийно, вместо обхода всех сообщений на каждый проход раскладки, и корректно при виртуализации. Обратная ссылка `ScrollViewer → ItemsRepeater` держится приватным attached-свойством: искать ленту по дереву на каждый `ViewChanged` нельзя.

Пузырь сообщения лежит прямо в `DataTemplate`, а не в `UserControl`: `x:Bind` внутри `UserControl` смотрит на сам контрол, и связь с элементом ленты пришлось бы тянуть через `DependencyProperty`.

12 `DataTrigger` заменены тремя конвертерами (`BoolToHorizontalAlignmentConverter`, `BoolToBrushConverter`, `BoolToThicknessConverter`) со значениями, заданными при объявлении ресурса. Кисти им подаются **синглтонами из `Brushes.xaml`**, а не `ThemeResource`: конвертер — не `DependencyObject`, тему он бы не отследил и заморозил цвета первой темы. Поэтому `MessageOwnBubble*`, `MessageOtherBubble*` и `PresenceOnline` добавлены и в палитры, и в `BrushKeys` сервиса тем.

`StringFormat` в WinUI нет — время отдают свойства вьюмодели `SentAtLabel` и `LastMessageAtLabel`. `UniformGrid` нет — медиасетка это `ItemsRepeater` + `UniformGridLayout`. `MediaElement` нет — `MediaPlayerElement`, скрытый до нажатия Play: держать плеер активным в каждой плитке виртуализованной ленты слишком дорого.

## Реалтайм

| Стрим | Обёртка `WebApi` | Потребитель |
| --- | --- | --- |
| Новые сообщения | `JustUpdate` | `RealtimeMessengerService.MessageReceived` |
| Отметки прочтения | `SubscribeToReadReceipts` | `MessageRead` |
| Прочтения в приватных чатах | `SubscribeToPrivateMessagesRead` | `PrivateMessageRead` |
| Онлайн-статусы | `SubscribeToOnlineStatus` | `OnlinePresenceService.PresenceChanged` |

`NewMessageEvent` несёт сырой `Proto.Shared.Message`, а вьюмодели работают с `MessageModel`. Маппер живёт приватным во внутреннем `WebApiMessageManager`, поэтому наружу выведен пробросом `WebApi.MapEventMessage` — дублировать разбор вложений в клиенте нельзя, он разъедется с разбором истории.

Три вещи, которые обязана учитывать обработка входящих:

- **сервер шлёт эхо собственного сообщения** и оно может обогнать ответ `SendMessage`, поэтому все добавления идут через `InsertMessageInOrder` — она и ставит по идентификатору, и отбрасывает уже показанный;
- **`MessageScrollRequest` — запись со сравнением по значению**, повторная установка того же запроса не поднимет `PropertyChanged`; все присваивания идут через `RequestScroll`, который сначала сбрасывает в `null`;
- **приватные чаты обслуживает отдельный шифрованный стрим**, их сообщения в `JustUpdate` не приходят и рисоваться из него не должны.

Прокрутка к новому сообщению происходит, только если лента уже внизу: иначе чтение истории сбрасывалось бы в конец при каждом чужом сообщении. Положение сообщает сама лента командой `FeedPositionChangedCommand`.

Присутствие вынесено в отдельный `OnlinePresenceService`, потому что у него свой жизненный цикл: набор наблюдаемых пользователей меняется вместе со списком чатов (`ChangeUsersInSubscription` без пересоздания стрима) и есть keepalive `SetOnlineStatus` каждые 3 с — сервер помечает офлайном через 5 с без пинга. Таск keepalive входит в тот же ограниченный по времени набор ожидания, что и стримы, иначе он блокировал бы выход из приложения. Для локальных пользователей стрим отдаёт **только изменения**, без начального снимка, поэтому после подписки состояние дозапрашивается унарным `GetOnlineStatus` — иначе шапка чата пустует до первого переключения статуса.

## Профиль

Одна страница на два случая: из панели навигации открывается свой профиль (`userId = 0`, так же трактует запрос сервер), из заголовка чата — профиль собеседника. Только просмотр: редактирования нет ни в одном Windows-клиенте. Присутствие собеседника берётся из кэша `IOnlinePresenceService.TryGet` — заводить ради экрана вторую подписку не нужно, все собеседники в ней уже есть. Почту сервер отдаёт только для собственного профиля, пустая строка прячет блок целиком.

## Настройки

`SettingsPage` — master-detail оболочка с четырнадцатью разделами. Разделы «Безопасность», «Аккаунт», «Приватность», «Устройства» и «Уведомления» уже подключены к [[Клиенты/Windows-WebApiCore]]; «Данные и кеш» отображает серверную квоту и разбивку по типам файлов. Локальные кеши по-прежнему недоступны и явно помечены как Android-only. Остальные разделы пока содержат каркас интерфейса и явно помечают недоступные возможности. Приватность сохраняется оптимистично и при ошибке повторно загружает серверное состояние.

«Аккаунт» получает и изменяет имя, username, bio и аватар через `IAccountSettingsService`. Выход — единый порядок: остановка real-time стримов, отзыв refresh-токена, очистка защищённой сессии и ключей приватных чатов, `MessengerViewModel.Reset()`, затем переход на логин. Это не оставляет данные предыдущей сессии в кэшированной странице мессенджера.

## Состояние порта

| Этап | Содержимое | Статус |
| --- | --- | --- |
| 1 | Каркас, Core-сборка, инфраструктура, shell, трей, настройки, тесты | Готово |
| 2 | Онбординг и авторизация (6 страниц, FastAuth QR, OTP) | Готово |
| 3 | Мессенджер (список чатов, лента, композер), бургер-навигация, профиль, реалтайм | Готово |
| 4 | Действия над сообщениями (ответ, пересылка, закрепы, удаление) | Готово |

Порт завершён.

## Действия над сообщениями

`ContextFlyout` на пузыре наследует `DataContext` шаблона, поэтому WPF-хак с чтением контекста из `PlacementTarget` не понадобился. Аналога `ContextMenuService.IsEnabled` в WinUI нет: сообщение без доступных действий (системное или приватное) гасит сам запрос меню через `ContextRequested`, иначе открывалось бы меню со всеми скрытыми пунктами.

Пересылка, подтверждение удаления и разблокировка приватного чата остались **оверлеями**, а не `ContentDialog`: вьюмодель уже управляет ими флагами (`IsForwardVisible`, `IsDeleteConfirmVisible`, `IsPrivateUnlockVisible`) и командами отмены, а перевод на диалоги потребовал бы абстракции диалогов в Core ради того же поведения. `IDialogService` при этом остаётся зарегистрированным — он нужен для сообщений об ошибках.

`ForwardTargetViewModel` получил `Owner` по образцу `MessageItemViewModel`: в WinUI нет `RelativeSource AncestorType`, и шаблон иначе не дотянется до команды владельца.

## Известные отличия от WPF-версии

- `LoginViewModel.FastAuthQrCode` отдаёт base64-строку, а не `ImageSource`; картинку собирает `Base64ToImageSourceConverter`.
- `MessengerViewModel` использует `IUiDispatcher` вместо `Application.Current.Dispatcher`; буфер обмена — `DataPackage` + `Clipboard.SetContent`, изображение копируется как `RandomAccessStreamReference` по URL.
- `MainWindowViewModel` больше не хранит `IsSettingsVisible`: настройки — отдельная страница в панели навигации.
- Поиск чатов в WPF-версии не работал: `SearchText` объявлен, но нигде не читался. В WinUI добавлена `VisibleChats` — фильтр по заголовку, всегда сохраняющий выбранный чат (иначе список сбросил бы `SelectedItem` и закрыл переписку).
- Новый чат, о котором пришло сообщение, дозагружается точечно и вставляется первым: `Clear()` на привязанной коллекции обнулил бы `SelectedChat`.
- `MVVMTK0045` подавлен в Core: `[ObservableProperty]` на полях несовместим с AOT в WinRT-сценариях, но AOT и trimming выключены; переход на partial-свойства требует выноса инициализаторов в конструкторы и вынесен в отдельную задачу.
