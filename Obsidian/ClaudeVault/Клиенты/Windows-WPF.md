# BarkFluff.Client.WPF

Основной Windows-клиент. WPF (.NET 10), x64, Code-behind + Reactive wrappers.

Расположение: `Windows/BarkFluff.Client.WPF/`
Target framework: `net10.0-windows10.0.26100.0`

## Сборка

```bash
dotnet build Windows/BarkFluff.Client.WPF/BarkFluff.Client.WPF.csproj
```

## Структура проекта

```
BarkFluff.Client.WPF/
├── Pages/
│   ├── SetupPages/       # Login, registration, password reset
│   ├── PinCode/          # PIN lock/unlock
│   ├── Messenger/
│   │   ├── Controllers/  # AttachmentController, ChatHistoryController, ChatListController,
│   │   │                 # ChatOpeningController, MessageReadController, MessageSendController,
│   │   │                 # NotificationController, OnlineStatusController, RealtimeMessagesController
│   │   ├── MessengerPage.DragDrop.cs
│   │   ├── MessengerPage.Input.cs
│   │   ├── MessengerPage.Panels.cs
│   │   ├── MessengerPage.Search.cs
│   │   ├── MessengerPage.Viewers.cs
│   │   ├── MessageTypeMapper.cs
│   │   └── ScrollAnimationBehavior.cs
│   └── MessengerPage.xaml(.cs)
├── MessengerWindows/     # MainWindow
├── Services/
│   ├── App/Caching/      # MessageCacheManager (LiteDB) + FileCacheService
│   ├── App/Update/       # GitHub release update checker
│   ├── App/Converter/    # Audio/video converters
│   ├── Erida/            # In-app toast notifications
│   ├── Notification/     # Windows toast (WinRT)
│   └── QR/               # QR scanning/generation
├── UserControls/
│   ├── MessageContent/   # Audio, video, image, document renderers
│   └── SettingsPages/
├── Reactive/             # ReactiveString, ReactiveBool, ReactiveLong
├── Validators/           # Email, username, password, OTP
└── Resources/            # Styles, themes (Light/Dark), icons, fonts
```

gRPC-клиентский код — в [[Клиенты/Windows-WebApiCore]].
UI/UX спецификация всех экранов и сценариев — [[Клиенты/DesignDocument]] (источник: `dd.md`).

**Карта всех файлов и классов** — [[Клиенты/Windows-WPF-ProjectMap]]

## Архитектура (Code-Behind + Service Layer)

**Без MVVM**. UI-логика в `.xaml.cs`. Reactive wrappers для XAML-биндинга:

```csharp
public ReactiveBool IsOpenChat { get; set; } = new ReactiveBool(false);
public ReactiveString ChatId { get; set; } = new ReactiveString(string.Empty);
```

## Глобальное состояние (App класс)

```csharp
App.ServerCommunication    // WebApi facade — все gRPC вызовы
App.GParam                 // GlobalParam — user info, tokens, server addresses, colors
App.CacheManager           // MessageCacheManager (LiteDB)
App.FileCacheService       // Файловый кеш
App.MessengerWindow        // MainWindow
App.Messenger              // MessengerPage
App.WindowStateService     // Сохранение/восстановление размера и позиции окна
App.NotificationManager    // Singleton WinRT toast-уведомлений
App.OnlineStatusService    // Singleton gRPC-стриминга онлайн-статусов
App.DeadTokenMode          // true — токен невалиден, ждём повторной авторизации
```

## Навигация

```
OnStartup
  └─ First run? → WelcomePage → SelectServer → CreateAccount / Login
     Returning?  → PinCodePage
                    ├─ No refresh token → SelectServer или LoginPage
                    └─ All tokens OK   → MessengerPage
```

## Кеш

| Хранилище | Путь | Содержимое |
|-----------|------|------------|
| Message cache | `datas/cache.db` (LiteDB) | История чатов |
| File metadata | `datas/file_cache.db` (LiteDB) | Записи файлов |
| File data | `datas/cache/{type}/` | Аватары, изображения, видео, аудио, документы |

Макс. 5 параллельных загрузок (SemaphoreSlim).

## Уведомления (три слоя)

1. **Erida** — in-app toast через `DropMessage` (StackPanel overlay, auto-dismiss)
2. **NotificationManager** (WinRT) — Windows toast с настройками (Disabled/HiddenContent/SenderOnly/FullWithPreview)
3. **SystemNotificationHelper** — Windows shell

## Real-Time Services (Singleton)

- **RealtimeUpdateService** — `Updates.JustUpdate()` streaming. Автопереподключение (exp. backoff 2с→30с). Дедупликация по `MessageId` (HashSet, макс. 1000). События: `NewMessageReceived`, `ReadReceiptReceived`, `ConnectionStatusChanged`
- **OnlineStatusService** — `Onliner.SubscribeToOnlineStatus()`. Keepalive (ping) каждые 3 сек. Дебаунс обновлений 500 мс. Кеш статусов в памяти. События: `OnlineStatusChanged`, `ConnectionStatusChanged`

## Конфигурация и шифрование

`GlobalParam` хранит состояние в `datas/GlobalParam.json`, зашифрованный AES-256 / PBKDF2 от PIN-кода. Класс — `Windows/BarkFluff.WebApi.Core/MessengerData/GlobalParam.cs` (общий с другими WPF-проектами).

Помимо токенов, профиля и адресов микросервисов, в `GlobalParam` лежат **локальные пользовательские настройки**:

| Поле | Тип | Default | Назначение |
|------|-----|---------|------------|
| `AppTheme` | string | `"system"` | Тема: `light` / `dark` / `system` (зеркало `ThemeRegistryHelper`) |
| `NotificationMode` | enum | `FullWithPreview` | 5 режимов отображения уведомлений |
| `NotificationSoundEnabled` | bool | `true` | Звук уведомлений |
| `MessageBubbleCornerRadius` | int | `12` | Скругление пузырей сообщений (0–20) |
| `BackgroundBlurEnabled` | bool | `false` | Размытие фона чата |
| `BackgroundBlurRadius` | int | `12` | Радиус Gaussian blur (1–25) |
| `BackgroundDimPercent` | int | `0` | Затемнение фона (0–100 %) |
| `CurrentBackgroundFileId` | string | `""` | FileId выбранного фона из `ChatBackgroundFileIds` |

### Сохранение

В `App.xaml.cs` есть статические helper-методы — это **единственный** правильный способ записи на диск:

```csharp
App.SaveGlobalParam();              // синхронный, тихо ничего не делает если PIN/AppPath пусты
App.SaveGlobalParamDebounced();     // DispatcherTimer 700 мс — для слайдеров
App.FlushPendingSave();             // финальный flush (вызывается в OnExit)
```

Прямой вызов `GlobalParam.Save(...)` запрещён — обходит проверки и провоцирует дубликаты пути.

## Settings (12 разделов)

`UserControls/Settings.xaml` — UserControl 700×600 с sidebar-навигацией и `ContentControl`-областью. Sidebar собран по образцу macOS-клиента ([[Клиенты/macOS]]), плюс сохранён существующий раздел Language.

| Sidebar Tag | Заголовок | Файл (`UserControls/SettingsPages/`) |
|-------------|-----------|--------------------------------------|
| `Profile` | Профиль | `ProfileSettingsPage` |
| `General` | Общие | `GeneralSettingsPage` (тема) |
| `Notifications` | Уведомления и звук | `NotificationsSettingsPage` |
| `Language` | Язык | `LanguageSettingsPage` (плейсхолдер) |
| `Security` | Безопасность | `SecuritySettingsPage` (пароль / 2FA TOTP / PIN) |
| `Privacy` | Приватность | `PrivacySettingsPage` |
| `Personalization` | Персонализация | `PersonalizationSettingsPage` (постер, скругление, размытие, затемнение, фоны) |
| `Cloud` | Облако | `CloudSettingsPage` |
| `Cache` | Кеш | `CacheSettingsPage` |
| `Sessions` | Активные сессии | `SessionsSettingsPage` |
| `AboutApp` | О приложении | `AboutAppSettingsPage` (версия, .NET, ОС, CPU, RAM) |
| `AboutServer` | О сервере | `AboutServerSettingsPage` (имя/адрес сервера, авто-пинг, микросервисы) |

Навигация — `Dictionary<string, Func<BaseSettingsPage>>` factory + кеш страниц (`_pageCache`) с fade-анимацией. Базовый класс `BaseSettingsPage` имеет `Title` и `OnNavigatedTo()` — последний вызывается на каждый переход (там грузим серверные данные).

### Personalization — взаимодействие с сервером

- **Постер** (`UserProfilePoster`) — `WebApi.UserManager.GetProfilePoster(gp)` / `SetProfilePoster(fileId, gp)` — атомарные методы; пустая строка = удалить.
- **Список фонов** (`ChatBackgroundFileIds`) — `GetPersonalization` / `UpdatePersonalization`. Поскольку `UpdatePersonalization` **полностью** перезаписывает `UserPersonalizationData`, при апдейте фонов мы передаём актуальный `ProfilePosterFileId` чтобы не затереть постер.
- **Локальные параметры** (размытие/затемнение/скругление/выбранный фон) — только в `App.GParam`, без серверной синхронизации.

## Фон окна чата (ChatBackgroundLayer)

`MessengerPage.xaml` содержит слой `ChatBackgroundLayer` (`ZIndex = -3`, `IsHitTestVisible="False"`):
```xml
<Grid x:Name="ChatBackgroundLayer">
    <Image x:Name="BackgroundChatImage" Stretch="UniformToFill" />
    <Border x:Name="ChatBackgroundDim" Background="Black" Opacity="0" />
</Grid>
```

Метод `MessengerPage.ApplyChatBackgroundSettings()` читает `App.GParam` и применяет:
- **Картинка** — `App.FileCacheService.GetCachedImageAsync(CurrentBackgroundFileId, FileType.Image, url)` где `url` получен через `WebApi.GetFile(...)`.
- **Размытие** — `BlurEffect { Radius=BackgroundBlurRadius, KernelType=Gaussian }` на `BackgroundChatImage` (или `null`).
- **Затемнение** — `Opacity = BackgroundDimPercent / 100.0` на `ChatBackgroundDim`.

Вызывается:
1. Один раз в `MessengerPage_Loaded` (после получения server info).
2. Из `PersonalizationSettingsPage` после каждого изменения соответствующих настроек: `App.Messenger?.ApplyChatBackgroundSettings()`.

## Single Instance и Deep Links

Mutex `BarkFluffMutex`. Второй экземпляр передаёт args через named pipe. Протокол: `bf://` и `bfdev://`.

## Темы

Light/Dark XAML resource dictionaries в `Resources/Styles/Themes/`. Toggle F8 в DEBUG. Hex-цвета в `GlobalParam.Colors`.

## MessageBubble — отображение сообщений

`UserControls/MessageBubble.xaml.cs` — пузырь сообщения. Поддерживает типы: Text, Image, Gif, Video, Audio, Voice, Document, Sticker, а также множественные вложения (MultiImageGrid, MultiVideoGrid, MultiDocumentList, AudioPanel).

### Изображения с placeholder'ом правильного размера

`ImageMessageContent` использует поля `ImageWidth`/`ImageHeight` из `AttachmentsModel` для задания размеров до загрузки картинки:
- При создании контрола вычисляется масштаб (`maxWidth=400, maxHeight=500`) с сохранением соотношения сторон
- `CachedImage.Width`/`Height` задаются сразу → облачко принимает финальный размер без прыжков
- Если размеры неизвестны (0) — fallback 300×200

`AttachmentsModel` содержит поля `ImageWidth` и `ImageHeight`, которые маппируются из proto `MessageAttachment.image_width` / `image_height`.
Маппинг присутствует в: `WebApiMessageManager` (SendMessage, GetMessages, GetMessagesWithOffset), `RealtimeUpdateService`.



## Система обновлений

**`UpdateService`** (`Services/App/Update/UpdateService.cs`) — запускается из `App.MainPageLoaded()`:
1. Каждые 2 часа запрашивает `GET /get/barkfluffwindows/{channel}/version`
2. Сравнивает версии; если `remoteVer > currentVer` → огонь `UpdateAvailable`
3. Канал определяется из `AppVersion.VersionType`: `"Release"` → `release`, иначе `beta`
4. `LatestDownloadUrl` = `https://storage.barkfluff.com/get/barkfluffwindows/{channel}/` (streaming URL)

**`LaunchUpdater`** (`Pages/Messenger/MessengerPage.Viewers.cs`) — вызывается по иконке обновления:
1. Генерирует PS1-скрипт через `GenerateUpdateScript(downloadUrl, installPath, isInstalledInAppData)`
2. Записывает в `%TEMP%\BarkFluff_update.ps1` и запускает через `powershell.exe -ExecutionPolicy Bypass`
3. Приложение сразу завершается (`Application.Current.Shutdown()`)

**Сгенерированный скрипт** (логика загрузки):
1. Завершает процессы `Barkfluff`, ждёт 2 сек
2. `GET {channel_url}/bitsurl` → получает presigned S3 URL + checksum + версию
3. BITS → `Start-BitsTransfer -Source $BitsInfo.url` (прямо к S3, минуя сервер)
4. Fallback: WebClient → `{channel_url}` (streaming endpoint)
5. Проверка SHA-256 из `BitsInfo.checksum`
6. `Expand-Archive` в `InstallPath`
7. Ярлык + протокол `bf://` (если `IsInstalled = true`)
8. Запуск с аргументом `--successfulupdate` → показывает `PostUpdateMessage`

## Локализация (RU / EN)

Полная локализация Settings, мгновенное переключение без перезапуска. На первом запуске берётся `CultureInfo.CurrentUICulture`, если не `ru` — fallback на `en`. Подход — swap `ResourceDictionary` (по образцу уже существующей смены темы) + событие для C# код-бихайнда.

**Хранилище языка:**
- `Services/App/LanguageRegistryHelper.cs` — `HKCU\Software\BarkFluff\AppLanguage`. Значения: `"system"` / `"ru"` / `"en"`. Дефолт `"system"`.
- `BarkFluff.WebApi.Core/MessengerData/GlobalParam.cs` — поле `public string AppLanguage { get; set; } = "system";`. При загрузке `GlobalParam.json` старые сохранения без поля получают `"system"`.

**Словари строк:**
- `Resources/Localization/Strings.ru.xaml`, `Strings.en.xaml` — `ResourceDictionary` с `system:String` ключами. Конвенция: `L_<Area>_<Screen>_<Element>` PascalCase (`L_Settings_Theme_Title`, `L_Settings_Language_Header`).
- В `App.xaml` стартовый плейсхолдер `<ResourceDictionary Source="Resources/Localization/Strings.ru.xaml"/>` — будет заменён до открытия первого окна.
- Множество ключей в `ru.xaml` и `en.xaml` обязано совпадать (нет ключа = `DynamicResource` вернёт `{DynamicResource ...}`-литерал).

**App.xaml.cs:**
- `LanguageLoader()` — читает реестр, резолвит `"system"` → `CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"`, вызывает `ApplyLanguage`.
- `ApplyLanguage(string lang)` — удаляет из `Application.Current.Resources.MergedDictionaries` любой словарь, чей `Source.OriginalString.Contains("/Localization/")`, добавляет `Strings.{lang}.xaml`, ставит `Thread.CurrentThread.CurrentUICulture` и `CurrentCulture` в `new CultureInfo(lang)` (нужно для `DateTime.ToString("dddd")`), поднимает событие `LanguageChanged`.
- В `OnStartup` `LanguageLoader()` вызывается **до** `ThemeLoader()`.

**LanguageManager** (`Services/App/LanguageManager.cs`):
- Singleton: `CurrentLanguage { get; }`, `event EventHandler? LanguageChanged`, метод `Apply(string requested)` — резолвит `"system"`, зовёт `BarkFluff.Client.WPF.App.ApplyLanguage(...)`, поднимает событие.
- Событие — единственный путь обновить C# код-бихайнд после смены языка.

**XAML — только `DynamicResource`:**
- `Text="{DynamicResource L_Settings_Theme_Title}"` — `StaticResource` для локализованного ключа **запрещён**, он замораживает значение и swap не сработает.
- Распространяется на `Text`, `Content`, `Header`, `ToolTip`, `Title` (включая `Window.Title`), `PlaceholderText`.
- Проверка после миграции: `grep -rn 'StaticResource L_'` должен быть пуст.

**C# код-бихайнд:**
- `(string)Application.Current.FindResource("L_Greeting")` — для строк, формируемых в коде. Завёрнуто в хелпер.
- Долгоживущие UI-элементы (`DropMessage`, `BaseSettingsPage.Title`, `ChatItem` subtitle) — подписаны на `LanguageManager.LanguageChanged` (Loaded/Unloaded — `+=`/`-=`, иначе утечка).

**BaseSettingsPage.Title — переход на ключи:**
- `virtual string TitleKey { get; }` (возвращает ключ ресурса) + `string Title => (string)Application.Current.FindResource(TitleKey)`. Реализует `INotifyPropertyChanged`, при `LanguageChanged` поднимает `PropertyChanged(nameof(Title))`. Сайдбар Settings автоматически перерисовывается.
- Все 12 наследников переведены на `TitleKey => "L_Settings_..."`.

**LanguageSettingsPage** (`UserControls/SettingsPages/LanguageSettingsPage.xaml(.cs)`):
- 3 `Border` с `MouseLeftButtonUp`, `Tag` = `"system"` / `"ru"` / `"en"`, Ellipse-радиокнопка, заголовок `L_Settings_Language_Header`.
- На клике: `LanguageRegistryHelper.SetLanguage(tag)` → `App.GParam.AppLanguage = tag` → `App.SaveGlobalParam()` → `LanguageManager.Instance.Apply(tag)` → `UpdateRadioVisuals(tag)`.
