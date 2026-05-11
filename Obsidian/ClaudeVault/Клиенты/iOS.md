# BarkFluff iOS

SwiftUI приложение для iOS. iOS-версия macOS-клиента — функциональный паритет с mac (за вычетом FastAuth и push-уведомлений, отложенных в этой итерации).

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

iOS-клиент использует тот же `Database`/`LocalChatRepository`/`LocalMessageRepository` что и macOS:
- При открытии чата — stale-while-revalidate: сначала показываются кешированные сообщения, флаг `isRefreshing`, затем фоновая загрузка с сервера и `replaceAll`/`upsertMessages`.
- Edited/deleted сообщения летят через `editedMessagesStream`/`deletedMessagesStream`, обновляя и UI, и БД.

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
- БД (GRDB), все in-memory кеши, файловый кеш картинок, recent stickers, ImagePipeline (Nuke).
- Локальные UserDefaults-настройки: `AppearanceSettings.reset()` (тема), `PersonalizationSettings.reset()` (cornerRadius/blur/dim/background fileID), `DeveloperSettings.reset()` (showUserIDs/showChatIDs).

**Что сохраняет:**
- `serverHost`/`serverPort` beacon-сервера. После wipe `AppCoordinator.performLocalWipe` вызывает `serverDiscoveryService.tryReconnect()` чтобы перезаполнить service endpoints в `ConnectionManager`, и уводит на `.authentication` (логин того же сервера), а не на `.serverSelection`.

## Раздел «Тестирование» в Профиле

Профиль → секция «Другое» → «Тестирование» (`SettingsCategory.testing` → `TestingSettingsView`). Содержит два локальных тогла из `DeveloperSettings` (UserDefaults): «Показывать ID пользователей» и «Показывать ID чатов». Эти флаги читает `ProfileInfoSection` в профиле собеседника — соответствующие строки появляются только при включённом тогле; тап по ID копирует значение в `UIPasteboard.general` + haptic. Тап по `@username` в `ProfileHeaderSection` — аналогично копирует.

## Контекстное меню сообщения (паритет с macOS)

`MessageBubbleView` показывает long-press меню: Изменить / Ответить / Переслать / Копировать текст / Скопировать изображение / Сохранить (изображения) / Сохранить (документы) / Удалить. Все действия проксируются в `ConversationViewModel` (edit/reply/delete/sendSticker), а медиа-действия — в `MediaActions` (UIPasteboard / PHPhotoLibrary / share sheet).

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

## Что вне scope текущей итерации

- **FastAuth (QR-авторизация)** — пропущено.
- **NotificationService / push-уведомления** — отложено.
- **Полноэкранные просмотрщики медиа** (отдельные `MediaViewerView`/`ImageViewerView`/`VideoPlayerView` как у macOS) — пока используется упрощённый viewer внутри ConversationView.
- **EmojiPickerView**, **GIFAttachmentView**, **MediaPreviewCard/FilePreviewCard/SendButton** — VMs готовы, UI пока стандартный.
