# BarkFluff iOS

SwiftUI приложение для iOS. iOS-версия macOS-клиента — функциональный паритет с mac (за вычетом push-уведомлений, отложенных в этой итерации).

Расположение: `iOS/Barkfluff/`

## Важно: Референс — macOS клиент и дизайн-документ

**При разработке iOS-версии ВСЕГДА смотреть на [[Клиенты/macOS]] как на референс.**
**UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)**
**Карта всех файлов проекта: [[Клиенты/iOS-ProjectMap]]**

Использовать те же:
- Архитектурные паттерны (MVVM, DI, Coordinator)
- Имена сервисов, репозиториев, моделей
- Структуру Features
- Взаимодействие с бэкендом

## Shared Packages

Те же пакеты, что и macOS-клиент (`../../Mac/Barkfluff/Packages/`):
- `BFProto/` — Proto definitions
- `BFNetworking/` — gRPC клиент, репозитории
- `BFCore/` — Бизнес-логика: сервисы, модели, кеш, локальный GRDB-кеш

## Локальный кеш (GRDB)

iOS-клиент использует тот же `Database`/`LocalChatRepository`/`LocalMessageRepository`/`LocalChatFolderRepository`/**`LocalCurrentUserRepository`** что и macOS. Миграции: v1…v5 + **v6_current_user** (таблица `cached_current_user`, single-row кеш текущего пользователя через `CachedCurrentUserRecord`).

- При открытии чата — stale-while-revalidate: сначала показываются кешированные сообщения, флаг `isRefreshing`, затем фоновая загрузка с сервера и `replaceAll`/`upsertMessages`.
- Edited/deleted сообщения летят через `editedMessagesStream`/`deletedMessagesStream`, обновляя и UI, и БД.
- **Текущий пользователь** (`container.currentUser`) — тоже stale-while-revalidate: `DependencyContainer.loadCurrentUser()` мгновенно поднимает из `cached_current_user` и запускает `Task { await revalidateCurrentUser() }` в фоне (fire-and-forget — не блокирует `ChatListView.task`). Write-through в SQLite — после `setProfilePicture`/`changeName`/`setProfilePoster` через `revalidateCurrentUser()` (вызывается из `ProfileEditViewModel.saveChanges`/`uploadCropped` и `PersonalizationSettingsViewModel.uploadPoster`).

## iOS-specific

- Навигация: `NavigationStack` + `TabView` (2 таба: Чаты / Профиль) вместо `NavigationSplitView`. Категории настроек живут внутри таба Профиль (по образцу Android).
- Tab bar вместо Sidebar для основного интерфейса.
- Профиль собеседника / инфо группы — push в стек чата (`ConversationDestination.userProfile(chat)`), не боковая панель.
- Адаптивный UI для разных размеров экрана.
- Touch-ориентированные жесты, `.contextMenu` через long-press.

### iOS-замены macOS API

| macOS | iOS |
|---|---|
| `NSPasteboard` | `UIPasteboard.general` |
| `NSWorkspace.activateFileViewerSelecting` | `UIActivityViewController` (share sheet) |
| `NSSavePanel` | `UIActivityViewController` (через `MediaActions.presentShareSheet`) |
| `NSImage` | `UIImage` |
| `NSWindow`-полноэкранный просмотр | `.fullScreenCover` |
| `.fileImporter` для аватара (sandbox) | `PhotosPicker` (`PhotosPickerItem.loadTransferable(type: Data.self)`) |
| `NSApplication.shared.appearance` | `.preferredColorScheme()` через `AppearanceSettings.colorScheme` |
| Sandbox entitlements | `INFOPLIST_KEY_NS*UsageDescription` build settings (Photo Library, Camera, Microphone) |

## Стартовый флоу (stale-first + connection gate)

`AppCoordinator.onAppLaunch(serverDiscovery:, authService:, tokenProvider:)`:

- Нет `savedServerHost` → `.serverSelection`.
- Нет `hasRefreshToken` (юзер не залогинен) → синхронный `tryReconnect` (нужны endpoints для login) → `.authentication` или `.serverSelection`.
- **Есть `hasRefreshToken`** → **сразу `.main`**, без ожидания сети. UI показывает `MainTabView` с пустым `ChatListView` (плейсхолдеры/крутилка). В фоне `Task`: `tryReconnect` → `tryRestoreSession`. Исходы: OK — `coordinator.isConnectionReady = true`; refresh-токен невалиден — `handleSessionExpired()`; нет сети — `isConnectionReady` остаётся `false`, ChatListView показывает крутилку до 30 сек таймаута.

**Connection gate**. `AppCoordinator.isConnectionReady: Bool` — инвариант «endpoints в `ConnectionManager` есть И валидный access-токен». Без этого `listChats`/`onlineStatusService.track`/`getCurrentUser` падают с «Messages не настроено» и пользователи показываются онлайн-по-дефолту. `coordinator.waitForConnectionReady(timeout:)` — polling каждые 100 мс с таймаутом 30 сек. Выставляется в `true`: онлайн-фон после `tryRestoreSession`, `LoginViewModel.login`, `RegisterViewModel.completeRegistration`. Сбрасывается в `false`: `handleSessionExpired`, `performLocalWipe`.

`ChatListView.task` (порядок):
1. `vm.isLoading = true` — чтобы UI показывал `ChatRowPlaceholderView`.
2. `await coordinator.waitForConnectionReady()` — ждём сетевую готовность.
3. Параллельно `vm.loadFolders()` / `vm.loadChats()` / `container.loadCurrentUser()`. `loadCurrentUser` теперь stale-first: возвращается мгновенно после чтения SQLite, ревалидация уходит в фон через `Task { await revalidateCurrentUser() }`.
4. `coordinator.isInitialChatsLoaded = true`.
5. `vm.startListeningForUpdates()` — подписки UpdatesStream.

`ChatListViewModel.loadChats()` — неблокирующий: показывает кеш и стартует фоновую ревалидацию через `revalidateTask` (метод `performRevalidate` грузит все страницы). Публичный `revalidateChats()` (await-able) — для pull-to-refresh, `.reconnected`, нового чата, удаления. `loadFolders()` тоже разнесён: кеш мгновенно, refresh в фоне.

Флаг `ChatListViewModel.isOffline` — ставится в `performRevalidate()` если ревалидация упала, но кеш есть. Рисует `ErrorBannerView` в `.safeAreaInset(.top)` с кнопкой dismiss.

`RootView`: `SplashView` теперь только на `currentState == .loading` (короткий cold-start момент). На `.main` сразу `MainTabView` — крутилка живёт в `ChatListView`. `loadCurrentUser` убран из `RootView.onChange` и перенесён в `ChatListView.task` (после gate). На Mac аналогично, плюс `notificationService.start` тоже после gate.

## Иконка приложения и сплеш-скрин

**Иконка** (`Assets.xcassets/AppIcon.appiconset/`): один файл `AppIcon.png` (1024×1024, заимствован у macOS — `Mac/.../AppIcon.appiconset/icon-ios-1024x1024.png`). Формат iOS 17+ single-size — iOS сама рендерит все промежуточные размеры. В `Contents.json` три слота (universal / dark / tinted) указывают на тот же PNG, отдельных variants нет.

**Сплеш-скрин** (`DesignSystem/Components/SplashView.swift`): `Image("BrandLogo")` (отдельный imageset на той же 1024×1024) + название «BarkFluff» + `ProgressView`. Показывается:
- На `AppCoordinator.currentState == .loading` (вместо прежнего `LoadingView`).
- На `.main`, пока `coordinator.isInitialChatsLoaded == false` — флаг ставится в `true` в `ChatListView.task` после `await (userLoad, foldersLoad, chatsLoad)` (даже при ошибке, чтобы splash не повис).

Реализация: `RootView` оборачивает switch по `currentState` в `ZStack`, поверх лежит `SplashView` с `.transition(.opacity)` и `.animation(.easeInOut(duration: 0.25), value: showSplash)`. Контент рендерится «под» сплешем, чтобы `ChatListView.task` стартовал параллельно с показом сплеша.

Флаг `isInitialChatsLoaded` живёт в `AppCoordinator` и **не сбрасывается** при logout/`handleSessionExpired` — splash появляется только на cold start. При повторном входе пользователь видит `MainTabView` сразу с `ChatRowPlaceholderView` плашками, пока чаты грузятся.

Нативный iOS LaunchScreen — авто-генерируемый пустой (`INFOPLIST_KEY_UILaunchScreen_Generation = YES`), наш SwiftUI splash перекрывает его мгновенно.

## Онлайн-статусы

Реализация идентична [[Клиенты/macOS]]: per-row подписка через `.task(id: otherUserID)` с парой `track`/`untrack` и `withTaskCancellationHandler`.

- `ChatListViewModel` — только warmup кеша через `onlineStatusService.start(initialUserIDs:)`. Не хранит словари статусов и не управляет отдельными tracking-тасками.
- `ChatRowView` — сам подписывается на статус собеседника по `otherUserID`; при reuse cell под другой чат `.task(id:)` отменяет предыдущую таску, `onCancel` вызывает `untrack`.
- `ConversationViewModel.startListeningForOnlineStatus()` — `track` + snapshot + stream; парный `untrack` в `stopListeningForUpdates()`.
- Текстовый статус — компонент `OnlineStatusText` (общий с Mac), использует `OnlineStatus.displayText` из BFCore.

## Персонализация (паритет с macOS / Android)

`Features/Settings/Views/PersonalizationSettingsView.swift` + `Features/Settings/Views/Personalization/Components/` (`PosterPreviewCard.swift`, `BubblePreviewView.swift`, `BackgroundsGrid.swift`) + `Features/Settings/ViewModels/PersonalizationSettingsViewModel.swift`.

Один Form-экран с четырьмя секциями:
1. **Превью профиля** — постер 3:1, аватар поверх (88pt), имя/`@username`, кнопка «Установить новый постер» через `PhotosPicker`. После аплоада — `userService.setProfilePoster` + `container.loadCurrentUser()` (шапки в боковых панелях подхватывают новый постер).
2. **Внешний вид сообщений** — 5 пузырей (`MessageBubbleShape`) на реальном `ChatBackgroundView` + Slider закругления (0-30 pt).
3. **Фон чата** — Toggle размытия, Slider радиуса размытия (1-25), Slider затемнения (0-100%).
4. **Изображения фона** — `LazyVGrid` 1:2-ячейки, первая ячейка — `PhotosPicker` для добавления. Long-press → delete-mode → красные крестики и «Готово» в шапке секции. «Убрать фон чата» сбрасывает выбор.

Серверная синхронизация — через `UserService.getPersonalization` / `setProfilePoster` / `updatePersonalization` (BFCore, общие для iOS/macOS). Локальные настройки (radius/blur/dim/currentBackgroundFileID) — `PersonalizationSettings` (UserDefaults) из DI.

PhotosPicker-паттерн: VM хранит `var selectedPosterItem: PhotosPickerItem?` / `var selectedBackgroundItem: PhotosPickerItem?` с `didSet`. Постер: `didSet` → `loadTransferable(Data.self)` → `UIImage(data:)` → `pendingPosterImage` + `showPosterCropper = true` → после `onCrop` — `uploadPoster(_ image:)` → `jpegData(0.85)` → `fileService.uploadFile(.userProfilePoster)`. Фоны кропом не идут — сразу `uploadFile(.messageAttachmentImage)`.

## Кропер изображений — `ImageCropperView`

`DesignSystem/Components/ImageCropperView.swift` — общий кропер для аватара (1:1, output 1024×1024) и постера (3:1, output 1500×500). Pinch-zoom + pan через `UIScrollView` в `UIViewControllerRepresentable`, поверх — затемнение even-odd с белой рамкой по окну кропа. Минимальный зум подобран так, что картинка всегда покрывает окно (выделение не может выехать за пределы), max — `minScale * 4`. Двойной тап — toggle zoom. На «Готово» возвращает `UIImage` нужного output-size через `UIGraphicsImageRenderer`, уважая `imageOrientation`.

Параметры: `image: UIImage`, `aspectRatio: CGFloat` (1 / 3), `outputWidth: CGFloat` (1024 / 1500), `onCancel`, `onCrop`. Презентация — `.fullScreenCover`.

Точки вызова:
- `Features/Auth/Views/Steps/AvatarStepView.swift` — регистрация, 1:1.
- `Features/Profile/Views/ProfileEditView.swift` — настройки профиля, 1:1, биндится к `ProfileEditViewModel.showCropper`.
- `Features/Settings/Views/Personalization/Components/PosterPreviewCard.swift` — персонализация, 3:1, биндится к `PersonalizationSettingsViewModel.showPosterCropper`.

Общий flow: PhotosPicker → `loadTransferable(Data)` → `UIImage` → cropper → `jpegData(0.85)` → `fileService.uploadFile` → `setProfilePicture` / `setProfilePoster`.

## Logout (паритет с macOS)

Кнопка «Выйти из аккаунта» в `ProfileEditView` → `coordinator.logout(container:)` → `authService.logout()` (gRPC `Identity.Logout`) → `container.reset()`. При ошибке серверного шага — alert «Не удалось разлогиниться» с тремя действиями: «Повторить» / «Выйти всё равно» (через `coordinator.forceLogout(container:)`) / «Отмена».

**Что стирает logout:**
- Токены (access/refresh) + `device_id` — через `tokenProvider.purgeForLogout()` (общий пакет `BFNetworking`, `UserDefaultsTokenProvider` / `KeychainTokenProvider`).
- БД (GRDB), все таблицы кеша — `cached_message`, `cached_chat`, `cached_chat_folder`, `cached_file`, `cached_sticker`, `cached_sticker_pack`, **`cached_current_user`** (`Database.truncateAll()`). In-memory кеши, файловый кеш картинок, recent stickers, ImagePipeline (Nuke).
- Локальные UserDefaults-настройки: `AppearanceSettings.reset()` (тема), `PersonalizationSettings.reset()` (cornerRadius/blur/dim/background fileID), `DeveloperSettings.reset()` (showUserIDs/showChatIDs).

**Что сохраняет:**
- `serverHost`/`serverPort` beacon-сервера. После wipe `AppCoordinator.performLocalWipe` вызывает `serverDiscoveryService.tryReconnect()` чтобы перезаполнить service endpoints в `ConnectionManager`, и уводит на `.authentication` (логин того же сервера), а не на `.serverSelection`.

## Раздел «Тестирование» в Профиле

Профиль → секция «Другое» → «Тестирование» (`SettingsCategory.testing` → `TestingSettingsView`). Содержит два локальных тогла из `DeveloperSettings` (UserDefaults): «Показывать ID пользователей» и «Показывать ID чатов». Эти флаги читает `ProfileInfoSection` в профиле собеседника — соответствующие строки появляются только при включённом тогле; тап по ID копирует значение в `UIPasteboard.general` + haptic. Тап по `@username` в `ProfileHeaderSection` — аналогично копирует.

## Контекстное меню сообщения (паритет с macOS)

`MessageBubbleView` показывает long-press меню: Изменить / Ответить / Переслать / Копировать текст / Скопировать изображение / Сохранить (изображения) / Сохранить (документы) / Удалить. Все действия проксируются в `ConversationViewModel` (edit/reply/delete/sendSticker), а медиа-действия — в `MediaActions` (UIPasteboard / PHPhotoLibrary / share sheet).

## Стикеры

`Features/Conversation/Views/Stickers/` — полноценный стикер-пикер:
- `StickerPickerView` + `StickerPickerViewModel` — `@Observable`, грузит паки через `FileService.listStickerPacks` / `getStickerPack`, локально кеширует.
- `StickerPackTabsView` — горизонтальные табы паков + вкладка «Недавние» (`RecentStickersStore` хранит последние использованные).
- `StickersGridView` — `LazyVGrid` со стикерами; тап шлёт `MessageAttachmentType.sticker`.
Пикер открывается из `MessageInputView` (кнопка эмодзи/стикеров).

## Структура

```
Barkfluff/
├── App/
│   ├── BarkfluffApp.swift
│   ├── DI/DependencyContainer.swift   # Database, LocalRepos, MediaCacheManager, Stickers, Personalization, Appearance
│   ├── Models/TokenStorageSettings.swift
│   └── Settings/{AppearanceSettings, PersonalizationSettings, DeveloperSettings}.swift
├── Navigation/
│   ├── AppCoordinator.swift           # loading → serverSelection → authentication → main; 2 таба + sheets
│   ├── RootView.swift                 # .preferredColorScheme(appearanceSettings.colorScheme)
│   ├── MainTabView.swift              # Чаты / Профиль (с категориями настроек внутри) + .sheet
│   └── SettingsCategory.swift
├── DesignSystem/
│   ├── Theme.swift, Typography.swift
│   ├── Components/                    # Avatar, Badge, CachedImage, FlowLayout, Refreshing, UnreadBadge, OnlineStatusText, ErrorBanner, Loading
│   └── LiquidGlass/                   # GlassButtonStyle, GlassCardView, GlassModifiers
├── Features/
│   ├── Auth/
│   ├── ChatList/
│   ├── Conversation/                  # VM полный паритет, ChatBackgroundView, ContextMenu, EditPreview/ReplyPreview
│   ├── Profile/                       # ProfileView (карточка + 4 группы категорий настроек, Android-pattern), ProfileEditView с PhotosPicker
│   ├── Settings/                      # 5 VM + 9 экранов; SettingsCategoryRow + SettingsCategoryView (root SettingsView удалён)
│   ├── UserProfile/                   # Push-навигация из ConversationView
│   ├── UserSearch/                    # Sheet через coordinator.presentedSheet = .userSearch
│   └── GroupChat/                     # Sheet через coordinator.presentedSheet = .createGroupChat
```

## Liquid Glass (iOS 26)

API совпадает с macOS 26. Перенесены `GlassButtonStyle`, `GlassCardView`, `GlassModifiers` (`.glassEffect`, `.glassCard`, `.glassPanel`, `.shimmer`).

## Code Conventions

- Комментарии на русском
- Services с `Protocol` суффиксом
- ViewModels — `@Observable`
- Async/await
- @MainActor у VM, которые работают с UI-state; `@Bindable var vm = vm` в Views для биндингов

## Сборка

```bash
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -destination 'platform=iOS Simulator,name=iPhone 17' build
open Barkfluff.xcodeproj
```

## Папки чатов

Полный паритет с [[Android]]-клиентом (контракт описан в [[Backend/Users-ChatFolders-ClientGuide]]). UI — горизонтальный ряд «чипов» над списком чатов: первый таб «Все чаты», далее пользовательские папки с бейджем непрочитанных. Раздел `Settings → Папки чатов` управляет порядком (drag-drop), созданием/редактированием/удалением.

Shared-слой (общий с [[macOS]]):
- `BFCore.ChatFolder` — доменная модель (`id`, `name`, `icon`, `chatIDs`, `sortOrder`).
- `BFNetworking.ChatFoldersRepository` — gRPC-репозиторий с 7 методами (`getChatFolders`/`create`/`update`/`delete`/`addChatToFolder`/`removeChatFromFolder`/`reorder`).
- `BFCore.ChatFolderService` — stale-while-revalidate поверх `ChatFoldersRepository` + `LocalChatFolderRepository` (GRDB, миграция `v5_chat_folders`, таблица `cached_chat_folder`).

iOS-специфика:
- `Features/Settings/Views/ChatFoldersListView.swift` + `EditChatFolderView.swift` + `FolderChatPickerView.swift` (+ соответствующие ViewModel-ы).
- Категория `.chatFolders` в [[Клиенты/iOS]] `Navigation/SettingsCategory.swift`, в `ProfileView` — отдельная секция «Чаты».
- `Features/ChatList/Views/FolderTabChip.swift` + `ChatFolderTabsBar` — горизонтальный ряд табов над `ChatListView`.
- `ChatListViewModel.folders/selectedFolderID/excludeFolderChatsFromAll` + `loadFolders()`, `selectFolder(_:)`, `unreadCount(for:)`. Метод `applyFilter()` учитывает выбранную папку и флаг «исключать чаты папок из «Все чаты»».
- Персонализация: два `@AppStorage` ключа `folders.compact` (компактные табы — только иконки) и `folders.excludeFromAll`. Тогглы в `PersonalizationSettingsView`.

## FastAuth — сканер QR (паритет с Android)

iOS-клиент выступает в роли **сканера**: авторизованный пользователь сканирует QR-код с экрана логина нового устройства (web/macOS/WPF), получает его метаданные и подтверждает/отклоняет вход. Генерация QR на iOS не реализована — для входа в новый аккаунт нужна другая платформа.

**Точка входа**: `Настройки → Активные сессии → «Подключить устройство по QR»` (NavigationLink в `Features/Settings/Views/SessionsView.swift`).

**Файлы фичи** (`Features/FastAuth/`):
- `ViewModels/FastAuthScannerViewModel.swift` — `@Observable @MainActor`, FSM `FastAuthScannerPhase` (`.requestingPermission`/`.scanning`/`.processing`/`.needsPermission`/`.failed`), `requestPermissionAndStart()`, `handleQRDetected(_:)` (вызывает `fastAuthService.scan(fastAuthID:)`), `resetToScanning()`.
- `Views/QRCameraPreview.swift` — `UIViewControllerRepresentable` поверх `AVCaptureSession` + `AVCaptureMetadataOutput([.qr])`. `Coordinator` дебаунсит повторные срабатывания (1.5 с), `session.startRunning()` — только в фоновой `sessionQueue`. Старт/стоп управляется параметром `isActive`.
- `Views/FastAuthScannerView.swift` — `ZStack` (превью камеры + полупрозрачный `ScannerOverlay` с RoundedRectangle-окном + хинт-капсула). `.alert` с «Открыть настройки» (`UIApplication.openSettingsURLString`) при `.needsPermission`. `.navigationDestination(item: $vm.scannedInfo)` пушит экран подтверждения.
- `Views/FastAuthConfirmView.swift` — List с метаданными (`deviceName`, `operationSystem`, `appName`/`appVersion`, `ipAddress`) и кнопками Accept/Reject; после успеха вызывает `dismiss()` (возврат в SessionsView, pull-to-refresh обновит список сессий).

**Permission**: `INFOPLIST_KEY_NSCameraUsageDescription` в build settings iOS-таргета.

**Контракт (через общие пакеты)**:
- `BFCore.ScanFastAuthInfo` — доменная модель в `Packages/BFCore/Sources/BFCore/Models/ScanFastAuthInfo.swift`.
- `BFCore.FastAuthServiceProtocol` — `func scan(fastAuthID:) async throws -> ScanFastAuthInfo`, `func accept(fastAuthID:, confirmationCode:)`, `func reject(fastAuthID:, confirmationCode:)`.
- `BFNetworking.FastAuthRepository` — реализация через `connectionManager.withAuthorizedClient(for: .fastauth)` (RPC требуют User token), маппинг ошибок через `GRPCErrorMapper.map`.

**Пайплайн** (полный аналог Android из [[Backend/FastAuth]]):
1. Новое устройство анонимно дёргает `GenerateFastAuthToken` → получает QR + `fast_auth_id`, подписывается на `SubscribeFastAuthResult`.
2. iOS-сканер распознаёт QR → `scanFastAuth(fastAuthID)` → получает device_name/OS/app/IP/**confirmation_code**.
3. Пользователь видит `FastAuthConfirmView` и жмёт Accept/Reject → `AcceptFastAuth(fastAuthID, confirmationCode)` или `RejectFastAuth(...)`.
4. Стрим на новом устройстве отдаёт `ACCEPTED` (с access/refresh токенами) или `REJECTED`.

## Локализация (RU / EN)

Полное двуязычное приложение, мгновенное переключение без перезапуска. Источник истины — `Barkfluff/Localizable.xcstrings` (sourceLanguage = `ru`, локализации `ru` + `en`). На первом запуске берётся системная локаль, если не `ru` — fallback на `en`.

**Инфраструктура:**
- `App/Settings/LocalizationSettings.swift` — `@Observable @MainActor`, перечисление `AppLanguage { system, ru, en }`, computed `appliedLocale: Locale` (fallback `en`), сохранение в UserDefaults (`localization.language`).
- `App/DI/DependencyContainer.swift` — `let localizationSettings: LocalizationSettings`, сбрасывается в `reset()`.
- `App/BarkfluffApp.swift` — `.environment(\.locale, container.localizationSettings.appliedLocale)` на корневом `WindowGroup`. Чтение `container.localizationSettings.appliedLocale` внутри `body` делает корень реактивным: при смене языка `@Observable` уведомляет SwiftUI → весь tree пере-резолвит `LocalizedStringKey`.
- `Navigation/SettingsCategory.swift` — `case language` + `title: LocalizedStringKey` (НЕ `String`, иначе значение замораживается). Категория показывается в Профиле, экран `Features/Settings/Views/LanguageSettingsView.swift` — `Picker` по `AppLanguage.allCases`.
- `Barkfluff.xcodeproj/project.pbxproj` — в `knownRegions` добавлены `ru` и `en`.

**Конвенция ключей:** `<area>.<screen>.<element>` snake_case через точку. Примеры: `auth.login.submit`, `settings.general.theme.label`, `enum.theme.system`. Plurals — нативно в xcstrings (`%lld` placeholder + `one/few/many/other` для русского, `one/other` для английского).

**Типы строк в коде:**
- `Text("key")`, `Button("key")`, `TextField("key", text:)`, `.navigationTitle("key")`, `Label("key", systemImage:)` — параметр имеет тип `LocalizedStringKey`. Используется напрямую через строковый литерал.
- `LocalizedStringResource?` — для error/state-полей во ViewModel и валидаторах (`error: LocalizedStringResource?`, `errorMessage: LocalizedStringResource?`). В SwiftUI `Text(resource)` рендерится через текущую environment-локаль.
- `enum.titleKey: LocalizedStringKey` — для перечислений типа `AppTheme`, `ProfileFieldVisibility`. `String(localized: "key")` из computed property **запрещён** — фиксирует значение в момент вычисления, не реактивен.
- `String(localized: "key")` — только когда нужна именно `String` (передача в не-SwiftUI API).

**BFCore (общий с macOS):**
- Каталог: `Packages/BFCore/Sources/BFCore/Resources/Localizable.xcstrings`, `resources: [.process("Resources")]` в `Package.swift`. Внутри пакета — `String(localized: "key", bundle: .module, locale: …)`.
- Каждый enum/struct с локализованным `displayName: String` отдаёт системную локаль по умолчанию, плюс имеет **locale-aware overload** `displayName(in locale: Locale) -> String`. Тот же паттерн для `OnlineStatus.displayText(in:)`, `AttachmentType.previewText(fileName:in:)`, `BFError.errorDescription(in:)`/`recoverySuggestion(in:)`, `MediaCacheError.errorDescription(in:)`.
- `DateFormatterHelper.formatForChatList/formatForMessage/formatLastActive` принимают `locale: Locale = .current` — внутри собирают локаль-специфичный `DateFormatter`.
- `ErrorLocalizer.localize(_:locale:)` / `recoverySuggestion(for:locale:)` — locale-aware.
- Plurals для last-seen (`bfcore.online.last_seen_minutes/_hours/_days %lld`) — native xcstrings plural variants для русского (one/few/many) и английского (one/other).

**Реактивность во вьюхах:** компонент, отображающий BFCore-данные, инжектит `@Environment(\.locale) private var locale` и передаёт его в API: `status.displayText(in: locale)`, `role.displayName(in: locale)`, `DateFormatterHelper.formatForChatList(date, locale: locale)`, `ReplyPreviewView.makeSnippet(message, locale: locale)`. Без `in: locale` API остаётся бэкап-вариантом на `.current` (для логов, нотификаций, edge-кейсов).

**Валидаторы** (`Features/Auth/ViewModels/Validators/`): `error: LocalizedStringResource?` вместо `error: String?`. `PasswordStrength.label: LocalizedStringResource`. Это позволяет валидации мгновенно перерисовываться при смене языка без re-validate.

**Hardcoded `Locale(identifier: "ru_RU")` запрещены** — заменены на `@Environment(\.locale)` (`ProfileInfoSection.swift`, `ConversationView.swift`, `MessageGrouper.swift`).

## Что вне scope текущей итерации

- **NotificationService / push-уведомления** — отложено.
- **Полноэкранные просмотрщики медиа** (отдельные `MediaViewerView`/`ImageViewerView`/`VideoPlayerView` как у macOS) — пока используется упрощённый viewer внутри ConversationView.
- **EmojiPickerView**, **GIFAttachmentView**, **MediaPreviewCard/FilePreviewCard/SendButton** — VMs готовы, UI пока стандартный.
