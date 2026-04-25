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

## Архитектура (Code-Behind + Service Layer)

**Без MVVM**. UI-логика в `.xaml.cs`. Reactive wrappers для XAML-биндинга:

```csharp
public ReactiveBool IsOpenChat { get; set; } = new ReactiveBool(false);
public ReactiveString ChatId { get; set; } = new ReactiveString(string.Empty);
```

## Глобальное состояние (App класс)

```csharp
App.ServerCommunication  // WebApi facade — все gRPC вызовы
App.GParam               // GlobalParam — user info, tokens, server addresses, colors
App.CacheManager         // MessageCacheManager (LiteDB)
App.FileCacheService     // Файловый кеш
App.MessengerWindow      // MainWindow
App.Messenger            // MessengerPage
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

- **RealtimeUpdateService** — `Updates.JustUpdate()` streaming, события `NewMessageReceived` / `ReadReceiptReceived`
- **OnlineStatusService** — `Onliner.SubscribeToOnlineStatus()`, `OnlineStatusChanged`; keepalive каждые 3 сек

## Конфигурация и шифрование

`GlobalParam` хранит состояние в `datas/GlobalParam.json`, зашифрованный AES-256 / PBKDF2 от PIN-кода.

## Single Instance и Deep Links

Mutex `BarkFluffMutex`. Второй экземпляр передаёт args через named pipe. Протокол: `bf://` и `bfdev://`.

## Темы

Light/Dark XAML resource dictionaries в `Resources/Styles/Themes/`. Toggle F8 в DEBUG. Hex-цвета в `GlobalParam.Colors`.
