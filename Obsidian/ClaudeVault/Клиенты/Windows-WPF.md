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

`GlobalParam` хранит состояние в `datas/GlobalParam.json`, зашифрованный AES-256 / PBKDF2 от PIN-кода.

## Single Instance и Deep Links

Mutex `BarkFluffMutex`. Второй экземпляр передаёт args через named pipe. Протокол: `bf://` и `bfdev://`.

## Темы

Light/Dark XAML resource dictionaries в `Resources/Styles/Themes/`. Toggle F8 в DEBUG. Hex-цвета в `GlobalParam.Colors`.

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
