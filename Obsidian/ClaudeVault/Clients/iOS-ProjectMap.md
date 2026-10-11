# BarkFluff iOS — карта проекта

Расположение app target: iOS/Barkfluff/Barkfluff/
Shared packages: Mac/Barkfluff/Packages/ (подключены напрямую в Xcode).
Платформа: iOS 26.0, app Swift 5.0, Swift packages tools-version 6.2.

> Обзор и общие state/network контракты: [[Clients/iOS]] и [[Clients/macOS]]

| Файл / область | Точка входа и назначение |
|---|---|
| iOS/Barkfluff/Barkfluff/BarkfluffApp.swift | @main; создаёт AppCoordinator/DependencyContainer и injects locale |
| iOS/Barkfluff/Barkfluff/App/DI/DependencyContainer.swift | Composition root, shared services, GRDB/cache и token provider |
| iOS/Barkfluff/Barkfluff/Navigation/AppCoordinator.swift | Session state, connection gate, tabs, selectedChat и navigation paths |
| iOS/Barkfluff/Barkfluff/Navigation/RootView.swift | Root routing, SplashView, звонковый lifecycle по scenePhase |
| iOS/Barkfluff/Barkfluff/Navigation/MainTabView.swift | Chats/Profile TabView, adaptive NavigationSplitView, sheets |
| iOS/Barkfluff/Barkfluff/Features/ChatList/ | ChatListView/ChatListViewModel: selection, folders, cache, updates |
| iOS/Barkfluff/Barkfluff/Features/Conversation/ | ConversationView/ConversationViewModel и message UI |
| iOS/Barkfluff/Barkfluff/Features/Conversation/Views/Input/MessageInputView.swift | Composer с компактной ViewThatFits-раскладкой |
| iOS/Barkfluff/Barkfluff/Features/Conversation/Views/ForwardChatPickerView.swift | Текущий placeholder sheet пересылки |
| iOS/Barkfluff/Barkfluff/Features/Conversation/Helpers/MediaActions.swift | Pasteboard, Photos и share sheet |
| iOS/Barkfluff/Barkfluff/Features/Profile/Views/ProfileView.swift | Profile tab; через SettingsCategory открывает настройки |
| iOS/Barkfluff/Barkfluff/Features/Settings/ | Profile/security/privacy/personalization/folders/cache/cloud/language views |
| iOS/Barkfluff/Barkfluff/Features/Settings/ViewModels/PersonalizationSettingsViewModel.swift | PhotosPicker upload, poster/background sync |
| iOS/Barkfluff/Barkfluff/DesignSystem/Components/ImageCropperView.swift | UIImage cropper через UIScrollView для аватара и постера |
| iOS/Barkfluff/Barkfluff/Features/FastAuth/ | Authorized QR scanner, confirmationCode, accept/reject |
| iOS/Barkfluff/Barkfluff/Features/Call/Views/CallOverlayView.swift | Полноэкранный звонковый UI |
| iOS/Barkfluff/Barkfluff/DesignSystem/Components/ReadableContentContainer.swift | Максимальная ширина форм и профилей |

## Shared Swift-пакеты

Все пакеты находятся под Mac/Barkfluff/Packages/, не в каталоге iOS:

| Путь | Назначение |
|---|---|
| BFProto/ | Proto-контракты и generated gRPC |
| BFNetworking/ | ConnectionManager, auth interceptors, repositories |
| BFCore/ | Модели, сервисы, GRDB и общий Markdown AST/sanitizer |
| BFMarkdown/ | Общий SwiftUI Markdown renderer |
| BFCalls/ | Общий CallController и LiveKit views |

Основные настройки и локализация: iOS/Barkfluff/Barkfluff.xcodeproj/project.pbxproj и iOS/Barkfluff/Barkfluff/Localizable.xcstrings. Deployment target 26.0; SDK задаёт Xcode toolchain.
