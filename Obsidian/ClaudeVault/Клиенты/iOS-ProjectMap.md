# BarkFluff iOS — Карта проекта

Расположение: `iOS/Barkfluff/`
Платформа: iOS 26, Swift (app-таргет `SWIFT_VERSION = 5.0`; локальные пакеты — `swift-tools-version: 6.2`), SwiftUI + gRPC-Swift 2.0

> iOS-клиент является адаптацией macOS-клиента. Архитектура и пакеты общие.
> Описание архитектуры и конвенций: [[Клиенты/iOS]]
> Референс macOS-клиента: [[Клиенты/macOS-ProjectMap]]

**Shared Packages** (расположены в `Mac/Barkfluff/Packages/`, используются напрямую через Xcode):
- `BFProto` — Proto-контракты и сгенерированный gRPC-код
- `BFNetworking` — gRPC-клиент, соединение, репозитории, интерсепторы
- `BFCore` — бизнес-сервисы, модели, кеши, mappers, локальный GRDB-кеш

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Barkfluff/BarkfluffApp.swift` | `@main` — точка входа, создаёт `DependencyContainer`, запускает `AppCoordinator` |

---

## App/

| Файл | Назначение |
|------|-----------|
| `App/DI/DependencyContainer.swift` | DI-контейнер: репозитории, сервисы, кеши; `Database` + `LocalChat/MessageRepository`, `MediaCacheManager`, `StickersService`, `RecentStickersStore`, `AppearanceSettings`, `PersonalizationSettings`. `reset()` чистит БД, токены и кеш. |
| `App/Models/TokenStorageSettings.swift` | Модель настроек хранилища токенов |
| `App/Settings/AppearanceSettings.swift` | Тема приложения (system/light/dark) — UserDefaults + `.preferredColorScheme()`. Метод `reset()` для logout. |
| `App/Settings/PersonalizationSettings.swift` | Радиус пузыря, размытие/затемнение фона, fileID фона чата. Метод `reset()` для logout. |
| `App/Settings/DeveloperSettings.swift` | Локальные отладочные флаги (`showUserIDs`, `showChatIDs`) — UserDefaults + `reset()` для logout. Использует `TestingSettingsView` и `ProfileInfoSection`. |

---

## Navigation/

| Файл | Назначение |
|------|-----------|
| `Navigation/AppCoordinator.swift` | `@Observable` координатор: состояния `loading → serverSelection → authentication → main`, табы chats/profile, NavigationPath (chat + profile, профильный включает категории настроек), `presentedSheet`, `logout/forceLogout` через container.reset() |
| `Navigation/RootView.swift` | Корневой SwiftUI-вид + `.preferredColorScheme(appearanceSettings.colorScheme)` |
| `Navigation/MainTabView.swift` | `TabView` (2 таба: Чаты / Профиль). У Profile-стека `navigationDestination(for: SettingsCategory.self)` → `SettingsCategoryView`. Sheet через `presentedSheet` для createGroupChat/userSearch/forwardMessage |
| `Navigation/SettingsCategory.swift` | Enum категорий настроек (порт из macOS) |

---

## DesignSystem/

| Файл | Назначение |
|------|-----------|
| `DesignSystem/Theme.swift` | Цвета, отступы, радиусы |
| `DesignSystem/Typography.swift` | Стили текста (порт) |
| `DesignSystem/Components/AvatarView.swift` | Аватар |
| `DesignSystem/Components/BadgeView.swift` + `BadgesRowView` | Баджи пользователя |
| `DesignSystem/Components/CachedImageView.swift` | Nuke + MediaCacheManager |
| `DesignSystem/Components/ErrorBannerView.swift` | Баннер ошибки |
| `DesignSystem/Components/FlowLayout.swift` | Flow layout |
| `DesignSystem/Components/LoadingView.swift` | Индикатор загрузки |
| `DesignSystem/Components/OnlineStatusText.swift` | Текстовый онлайн-статус |
| `DesignSystem/Components/RefreshingIndicatorView.swift` | Полоска "Обновление…" поверх списков |
| `DesignSystem/Components/ImageCropperView.swift` | Квадратный кропер картинки (pinch/pan через `UIScrollView`, выход 1024×1024). Используется в `AvatarStepView`. |
| `DesignSystem/Components/UnreadBadgeView.swift` | Бейдж непрочитанных |
| `DesignSystem/LiquidGlass/GlassButtonStyle.swift` | `GlassButtonStyle` + `GlassCapsuleButtonStyle` |
| `DesignSystem/LiquidGlass/GlassCardView.swift` | Glass карточка |
| `DesignSystem/LiquidGlass/GlassModifiers.swift` | `.glassEffect()`, `.glassCard()`, `.glassPanel()`, `.shimmer()` |

---

## Features/Auth/

(см. предыдущую карту — без структурных изменений; `RegisterViewModel` использует `Validators/*`, добавлены `passwordSaved`/`bioSaved`. `ServerSelectionViewModel` — `isManualSectionExpanded`)

---

## Features/ChatList/

| Файл | Назначение |
|------|-----------|
| `ChatList/ViewModels/ChatListViewModel.swift` | Список чатов: stale-while-revalidate через `LocalChatRepository`, `isRefreshing`, подписки на edited/deleted streams |
| `ChatList/Views/ChatListView.swift` | Список чатов с push к ConversationView; toolbar Menu: «Новый чат» (UserSearch) / «Новая группа»; pull-to-refresh; `RefreshingIndicatorView` |

---

## Features/Conversation/

### ViewModels

| Файл | Назначение |
|------|-----------|
| `Conversation/ViewModels/ConversationViewModel.swift` | Полный паритет с macOS: pendingReply, editingMessage, enterEditMode/cancelEdit/submitEdit, deleteMessage, sendSticker, edited/deletedStreams, optimizeImage (2500px, JPEG 0.9→0.5), stale-while-revalidate через LocalMessageRepository |
| `Conversation/ViewModels/ForwardChatPickerViewModel.swift` | Multi-select + комментарий, параллельная отправка через TaskGroup |
| `Conversation/ViewModels/StickerPickerViewModel.swift` | Загрузка паков, табы, фильтр по emoji, недавние |

### Views

| Файл | Назначение |
|------|-----------|
| `Conversation/Views/ConversationView.swift` | Корневой экран: ChatBackgroundView (фон), MessagesListView, EditPreview/ReplyPreview над инпутом, .confirmationDialog для удаления, `.fullScreenCover` для медиа |
| `Conversation/Views/MessageBubbleView.swift` | `.contextMenu` (Изменить/Ответить/Переслать/Копировать/Сохранить/Удалить); чтение `bubbleCornerRadius` из PersonalizationSettings; рендер `ForwardedMessageView` и `StickerMessageView` |
| `Conversation/Views/MessagesListView.swift` | Прокидывает все callback-и в MessageBubbleView |
| `Conversation/Views/EditPreviewView.swift` | Превью редактируемого сообщения над инпутом |
| `Conversation/Views/ReplyPreviewView.swift` | Превью ответа над инпутом, `makeSnippet()` |
| `Conversation/Views/ForwardedMessageView.swift` | Карточка пересланного сообщения внутри пузыря |
| `Conversation/Views/ForwardChatPickerView.swift` | Заглушка экрана пересылки (полная реализация: VM есть, UI ещё в разработке) |
| `Conversation/Views/Input/MessageInputView.swift` | Поле ввода: paperclip + кнопка-смайлик стикеров (между attach и текстовым полем) + TextField + send. Открытие пикера через `onStickerTap` callback |
| `Conversation/Views/Input/AttachmentPreviewStrip.swift` | Полоса предпросмотра |
| `Conversation/Views/Components/MessageTimeView.swift` | Время + галочки + иконка `pencil` для отредактированных |
| `Conversation/Views/Components/ChatBackgroundView.swift` | Фон чата по PersonalizationSettings |
| `Conversation/Views/Components/BubbleShape.swift` | Форма пузыря с хвостиком, `cornerRadius` из настроек |
| `Conversation/Views/Components/ConversationHeaderView.swift` | Заголовок |
| `Conversation/Views/Components/MediaUploadProgressView.swift` | Прогресс |
| `Conversation/Views/Components/MessageDateSeparatorView.swift` | Разделитель дат |
| `Conversation/Views/Components/ScrollToBottomButton.swift` | Кнопка вниз |
| `Conversation/Views/Stickers/StickerImageView.swift` | UIImage + WebP через MediaCacheManager |
| `Conversation/Views/Stickers/StickerPickerView.swift` | Bottom-sheet (`.presentationDetents([.medium, .large])`): поиск по emoji + сетка + табы паков. Sheet остаётся открытым после tap (Android-style) |
| `Conversation/Views/Stickers/StickersGridView.swift` | LazyVGrid 4 колонки + empty-state |
| `Conversation/Views/Stickers/StickerPackTabsView.swift` | Горизонтальная полоса табов: «Недавние» + по табу на пак с обложкой |
| `Conversation/Views/Stickers/StickerThumbView.swift` | Превью стикера в гриде: tap → отправка, long-press → overlay (без hover, в отличие от macOS) |
| `Conversation/Views/Attachments/StickerMessageView.swift` | Стикер в чате (180×180 / 140×140) |
| `Conversation/Views/Attachments/AttachmentGridView.swift` | Сетка вложений |
| `Conversation/Views/Attachments/{Image,Video,Audio,Document,FileIcon}AttachmentView.swift` | Существующие |

### Helpers

| Файл | Назначение |
|------|-----------|
| `Conversation/Helpers/MediaActions.swift` | `copyImageToPasteboard` (UIPasteboard), `saveImages` (PHPhotoLibrary), `saveDocuments` (UIActivityViewController), `presentShareSheet` |
| `Conversation/Helpers/FileDownloadHelper.swift` | `downloadAndShare` (вместо NSSavePanel — share sheet), `downloadToTemp` |
| `Conversation/Helpers/AttachmentNotifications.swift` | `attachmentTapped`, `documentDownloadRequested` |
| `Conversation/Helpers/RecentStickersStore.swift` | Недавние стикеры в UserDefaults |
| `Conversation/Helpers/AttachmentLayoutCalculator.swift` | Расчёт сетки |
| `Conversation/Helpers/MessageGrouper.swift` | Группировка по дате/автору |
| `Conversation/Helpers/ScrollPositionManager.swift` | Управление прокруткой |

---

## Features/Profile/

| Файл | Назначение |
|------|-----------|
| `Profile/Models/UsernameValidationStatus.swift` | Статус валидации username |
| `Profile/ViewModels/ProfileEditViewModel.swift` | PhotosPicker для аватара, валидация username (debounce 500ms), changeName/changeUsername/changeBio |
| `Profile/Views/ProfileView.swift` | Корневой таб «Профиль»: List с ProfileCardView (NavigationLink → ProfileEditView) + 4 секциями категорий настроек по образцу Android (Аккаунт и безопасность / Персонализация / Приложение / Другое). NavigationLink через `SettingsCategory` → `SettingsCategoryView` (registered в MainTabView) |
| `Profile/Views/ProfileCardView.swift` | Карточка пользователя (аватар + имя + username) |
| `Profile/Views/ProfileEditView.swift` | PhotosPicker, форма с валидацией. Кнопка «Выйти из аккаунта» через `coordinator.logout(...)` + force-logout fallback |
| `Profile/Views/BadgesView.swift` | Сетка баджей |

---

## Features/Settings/

### ViewModels

| Файл | Назначение |
|------|-----------|
| `Settings/ViewModels/SettingsViewModel.swift` | Сессии, серверная информация, миграция token storage |
| `Settings/ViewModels/CacheSettingsViewModel.swift` | Стат кеша + очистка (общий MediaCacheManager + LocalChatRepository + LocalMessageRepository) |
| `Settings/ViewModels/CloudSettingsViewModel.swift` | StorageInfo с сервера + разбиение по типам |
| `Settings/ViewModels/PrivacySettingsViewModel.swift` | Загрузка и автосохранение PrivacySettingsInfo с откатом при ошибке |
| `Settings/ViewModels/AboutServerSettingsViewModel.swift` | Эндпоинты микросервисов + пинг |

### Views

| Файл | Назначение |
|------|-----------|
| `Settings/Views/SettingsView.swift` | Контейнер: `SettingsCategoryRow` (используется ProfileView) + `SettingsCategoryView` (роутер 11 категорий → конкретные экраны). Корневой `SettingsView` struct удалён — таба настроек больше нет, всё внутри Профиля |
| `Settings/Views/GeneralSettingsView.swift` | Picker по `AppTheme` |
| `Settings/Views/SecuritySettingsView.swift` | 2FA toggle + переключение хранилища токенов |
| `Settings/Views/PrivacySettingsView.swift` | Видимость профиля + поиск + сообщения (через PrivacySettingsViewModel) |
| `Settings/Views/SessionsView.swift` | Список активных сессий + кнопка «Подключить устройство по QR» (NavigationLink на `FastAuthScannerView`), terminate per-session + "Завершить все остальные" |
| `Settings/Views/CacheSettingsView.swift` | Стат + очистка |
| `Settings/Views/CloudSettingsView.swift` | Облако: ProgressView + типы |
| `Settings/Views/AboutAppSettingsView.swift` | Версия приложения, OS, модель устройства |
| `Settings/Views/AboutServerSettingsView.swift` | Beacon, ping, список микросервисов |
| `Settings/Views/PersonalizationSettingsView.swift` | Слайдеры для PersonalizationSettings (cornerRadius, blur, dim) |
| `Settings/Views/TestingSettingsView.swift` | Раздел «Тестирование» — `DeveloperSettings` toggles (`showUserIDs`, `showChatIDs`). Виден в Профиль → «Другое» под «О приложении». |

> **Не переносится в эту итерацию:** `NotificationsSettingsView` (push-уведомления отложены), полноценный poster picker / background grid в Personalization (нужен отдельный PhotosPicker workflow + serverside upload).

---

## Features/UserProfile/ (новая фича)

| Файл | Назначение |
|------|-----------|
| `UserProfile/ViewModels/UserProfilePanelViewModel.swift` | Загрузка профиля, online tracking, members, shared media с пагинацией (порт из macOS 1:1) |
| `UserProfile/Views/UserProfilePanelView.swift` | Полноэкранный профиль (push в NavigationStack из ConversationView, открывается тапом по аватарке в toolbar) |
| `UserProfile/Views/ProfileHeaderSection.swift` | Постер 3:1 + аватар + имя + статус |
| `UserProfile/Views/ProfileInfoSection.swift` | Bio, награды, дата регистрации, ID |
| `UserProfile/Views/GroupMembersSection.swift` | Список участников группы |
| `UserProfile/Views/SharedMediaSection.swift` | Picker «Медиа/Документы» + грид |
| `UserProfile/Views/SharedMediaGridView.swift` | Адаптивный грид превью |
| `UserProfile/Views/SharedDocumentsListView.swift` | Список документов |

---

## Features/GroupChat/ (новая фича)

| Файл | Назначение |
|------|-----------|
| `GroupChat/ViewModels/GroupChatViewModel.swift` | Создание группы: title, multi-select участников, debounce поиск, createGroupChat() |
| `GroupChat/Views/CreateGroupChatView.swift` | `.sheet` форма с поиском и multi-select |
| `GroupChat/Views/GroupInfoView.swift` | Реюз `UserProfilePanelView` для группы |
| `GroupChat/Views/MembersListView.swift` | Полный список участников |

---

## Features/UserSearch/ (новая фича)

| Файл | Назначение |
|------|-----------|
| `UserSearch/ViewModels/UserSearchViewModel.swift` | Debounce 300ms, поиск через UserService.searchUsers |
| `UserSearch/Views/UserSearchView.swift` | `.sheet` с searchable; тап по найденному → `ChatListViewModel.openConversation()` |

---

## Info.plist (usage descriptions)

Добавлены в `INFOPLIST_KEY_*` build settings:
- `NSPhotoLibraryUsageDescription` — выбор фото
- `NSPhotoLibraryAddUsageDescription` — сохранение в Photos
- `NSCameraUsageDescription` — камера
- `NSMicrophoneUsageDescription` — голосовые сообщения

---

## Features/FastAuth/

iOS-сторона сканера для подключения нового устройства (паритет с Android-`QrScannerActivity`/`FastAuthConfirmActivity`).

### ViewModels

| Файл | Назначение |
|------|-----------|
| `FastAuth/ViewModels/FastAuthScannerViewModel.swift` | `@Observable @MainActor`, FSM `FastAuthScannerPhase`, запрос разрешения камеры (`AVCaptureDevice.requestAccess`), вызов `fastAuthService.scan(fastAuthID:)`, дебаунс ошибок |

### Views

| Файл | Назначение |
|------|-----------|
| `FastAuth/Views/QRCameraPreview.swift` | `UIViewControllerRepresentable` поверх `AVCaptureSession` + `AVCaptureMetadataOutput([.qr])`. Старт/стоп в фоновой `sessionQueue`, дебаунс одинаковых QR на 1.5 с |
| `FastAuth/Views/FastAuthScannerView.swift` | Превью камеры + полупрозрачный `ScannerOverlay` с рамкой по центру + хинт-капсула. Alert «Открыть настройки» для `.needsPermission`. `.navigationDestination(item:)` пушит экран подтверждения |
| `FastAuth/Views/FastAuthConfirmView.swift` | List с метаданными нового устройства (`deviceName`/`operationSystem`/`appName+appVersion`/`ipAddress`) + кнопки Accept (`fastAuthService.accept`) / Reject (`fastAuthService.reject`). После успеха — `dismiss()` |

**Permission**: `INFOPLIST_KEY_NSCameraUsageDescription` уже в build settings.

**Связанные изменения в общих пакетах** (`Mac/Barkfluff/Packages/`, влияют и на macOS):
- `BFCore/Models/ScanFastAuthInfo.swift` (новый) — доменная модель.
- `BFCore/Services/Protocols/FastAuthServiceProtocol.swift` — добавлены `scan`, `accept(fastAuthID:, confirmationCode:)`, `reject(...)`.
- `BFCore/Services/Implementations/FastAuthService.swift` — реализации трёх методов.
- `BFNetworking/DTOs.swift` — `ScanFastAuthInfo` сетевой DTO.
- `BFNetworking/Protocols/RepositoryProtocols.swift` — `FastAuthRepositoryProtocol` обновлён: `scanFastAuth`, `acceptFastAuth(fastAuthID:, confirmationCode:)`, `rejectFastAuth(...)`.
- `BFNetworking/Repositories/FastAuthRepository.swift` — реализация через `connectionManager.withAuthorizedClient(for: .fastauth)`.

---

## Что вне scope текущей итерации

- **NotificationService / push** — отложено.
- **Полноэкранные просмотрщики медиа** (`MediaViewerView`/`ImageViewerView`/`VideoPlayerView` как у macOS) — текущий iOS использует встроенный простой viewer в ConversationView.
- **EmojiPickerView**, **GIFAttachmentView**, **MediaPreviewCard / FilePreviewCard / SendButton** — VMs есть, UI ещё переносится.
- **Personalization poster/background picker UI** — VM-инфраструктура готова, само редактирование — следующий шаг.
- **iCloud sync настроек** — нет на macOS, нет и на iOS.
- **Голосовые сообщения** (запись микрофоном) — повторяет текущий уровень macOS.
