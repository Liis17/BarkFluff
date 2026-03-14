# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

```bash
dotnet build Windows/BarkFluff.Client.WPF/BarkFluff.Client.WPF.csproj
```

Target framework: `net10.0-windows10.0.26100.0`, x64 only. No test projects.

## Project Structure

```
BarkFluff.Client.WPF/
├── Pages/
│   ├── SetupPages/       # Login, registration, password reset
│   ├── PinCode/          # PIN lock/unlock screens
│   └── MessengerPage.xaml(.cs)  # Main messenger UI (large code-behind)
├── MessengerWindows/     # MainWindow container
├── Services/
│   ├── App/
│   │   ├── Caching/      # MessageCacheManager (LiteDB) + FileCacheService
│   │   ├── Update/       # GitHub release update checker
│   │   └── Converter/    # Audio/video converters
│   ├── Erida/            # In-app toast notification system
│   ├── Notification/     # Windows toast (WinRT) notifications
│   └── QR/               # QR code scanning/generation
├── UserControls/
│   ├── MessageContent/   # Audio, video, image, document renderers
│   └── SettingsPages/    # Settings UI components
├── Reactive/             # ReactiveString, ReactiveBool, ReactiveLong
├── Validators/           # Email, username, password, OTP validators
└── Resources/            # Styles, themes (Light/Dark), icons, fonts
```

The companion project `Windows/BarkFluff.WebApi.Core/` contains all gRPC client code.

## Architecture: Code-Behind + Service Layer

The project does **not** use MVVM. UI logic lives in `.xaml.cs` code-behind files. Reactive wrappers (`ReactiveString`, `ReactiveBool`, `ReactiveLong`) implement `INotifyPropertyChanged` for XAML binding:

```csharp
public ReactiveBool IsOpenChat { get; set; } = new ReactiveBool(false);
public ReactiveString ChatId { get; set; } = new ReactiveString(string.Empty);
```

## Global App State

Static properties on `App` class hold the primary app state:

```csharp
App.ServerCommunication  // WebApi facade — all gRPC calls go through here
App.GParam               // GlobalParam — user info, tokens, server addresses, colors
App.CacheManager         // MessageCacheManager (LiteDB message cache)
App.FileCacheService     // File/image/video/audio disk cache
App.MessengerWindow      // MainWindow reference
App.Messenger            // MessengerPage reference
```

## Navigation Flow

```
OnStartup
  └─ First run? → WelcomePage → SelectServer → CreateAccount / Login
     Returning?  → PinCodePage
                    ├─ No refresh token → SelectServer or LoginPage
                    └─ All tokens OK   → MessengerPage
```

## gRPC Communication (WebApi.Core)

`WebApi.cs` is the central facade. It holds gRPC client instances created via manager classes in `WebApi.Core/Managers/`:

- `WebApiClientManager` — creates gRPC channels + injects interceptors (device ID, OS, app version, JWT)
- `WebApiTokenManager` — token refresh; fires `TokenInvalidated` when refresh token expires
- `WebApiAuthManager` — login, OTP
- `WebApiMessageManager` — chats and messages
- `WebApiFileManager` — upload/download with progress
- `WebApiOnlinerManager` — online status gRPC stream
- Plus: Server, Registration, Password, Search, Update managers

Token expiry during any call is handled via `SafeCallAsync()` which transparently refreshes the access token. When the refresh token itself is invalid, `TokenInvalidated` fires and the app calls `DeadToken()` — this clears tokens and redirects to login while preserving server selection.

## Configuration & Encryption

`GlobalParam` stores all persistent state (server sockets, user info, tokens, device ID, theme colors) in `datas/GlobalParam.json` encrypted with AES-256 / PBKDF2, keyed from the user's PIN code.

## Real-Time Services (Singletons)

Both services connect via gRPC streaming and auto-reconnect with exponential backoff:

- **RealtimeUpdateService** — subscribes to `Updates.JustUpdate()`, fires `NewMessageReceived` / `ReadReceiptReceived`
- **OnlineStatusService** — subscribes to `Onliner.SubscribeToOnlineStatus()`, fires `OnlineStatusChanged`; sends a keepalive ping every 3 seconds

## Caching

| Store | Location | Contents |
|-------|----------|----------|
| Message cache | `datas/cache.db` (LiteDB) | Chat message history |
| File metadata | `datas/file_cache.db` (LiteDB) | File records |
| File data | `datas/cache/{type}/` | Avatars, images, video, audio, docs |

Max 5 concurrent file downloads (SemaphoreSlim). Placeholder images shown per media type while downloading.

## Notifications

Three layers:
1. **Erida** — in-app toast via `DropMessage` (StackPanel overlay, auto-dismiss)
2. **NotificationManager** (WinRT) — Windows toast with configurable `NotificationDisplayMode` (Disabled / HiddenContent / SenderOnly / FullTextNoPreview / FullWithPreview)
3. **SystemNotificationHelper** — deep Windows shell integration

## Single Instance & Deep Links

App uses a named Mutex (`BarkFluffMutex`). A second instance pipes its args to the first via named pipe. Registers `bf://` (and `bfdev://`) URI protocol handlers for deep linking to chats.

## Theming

Light and Dark XAML resource dictionaries under `Resources/Styles/Themes/`. Toggle with F8 in DEBUG builds. Active theme colors (hex) are stored in `GlobalParam.Colors`.
