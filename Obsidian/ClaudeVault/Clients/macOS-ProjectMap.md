# BarkFluff macOS — карта проекта

Расположение app target: Mac/Barkfluff/Barkfluff/
Общий Swift-код: Mac/Barkfluff/Packages/
Платформа: macOS 26.0, app Swift 6.0, Swift packages tools-version 6.2.

> Обзор и основные контракты: [[Clients/macOS]]

| Файл / область | Точка входа и назначение |
|---|---|
| Mac/Barkfluff/Barkfluff/App/BarkfluffApp.swift | @main; создаёт coordinator/container, регистрирует WindowGroup и Settings |
| Mac/Barkfluff/Barkfluff/App/DI/DependencyContainer.swift | Composition root; сетевые репозитории, сервисы, токены, DB и кеши |
| Mac/Barkfluff/Barkfluff/Navigation/AppCoordinator.swift | loading/authentication/main state, connection gate, logout |
| Mac/Barkfluff/Barkfluff/Navigation/RootView.swift | Роутинг состояния приложения, lifecycle Calls |
| Mac/Barkfluff/Barkfluff/Navigation/MainSplitView.swift | Sidebar и detail навигация |
| Mac/Barkfluff/Barkfluff/Features/Auth/ | Login/Register/ServerSelection; LoginView показывает QRPanelView |
| Mac/Barkfluff/Barkfluff/Features/FastAuth/ | FastAuthViewModel и FastAuthQRView — генератор QR для входа на macOS |
| Mac/Barkfluff/Barkfluff/Features/ChatList/ViewModels/ChatListViewModel.swift | Cached chats, folders, reconnect, Updates/OnlineStatus subscriptions |
| Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift | Messages, pagination, attachments, reply/edit/delete, optimistic updates |
| Mac/Barkfluff/Barkfluff/Features/Conversation/Views/ConversationView.swift | Composer, message list, profile/forward sheets, background |
| Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessageBubbleView.swift | Markdown, attachments, context menu |
| Mac/Barkfluff/Barkfluff/Features/Call/Views/CallOverlayView.swift | Плавающий звонковый оверлей |
| Mac/Barkfluff/Barkfluff/Features/Profile/ | ProfileEditViewModel и редактирование профиля |
| Mac/Barkfluff/Barkfluff/Features/Settings/ | SettingsCategoryView, token security, privacy, personalization, cache/cloud |
| Mac/Barkfluff/Barkfluff/App/Notifications/NotificationService.swift | Локальные системные уведомления из UpdatesService stream |
| Mac/Barkfluff/Barkfluff/DesignSystem/Components/ImageCropperView.swift | AppKit NSImage cropper для аватара и постера |
| Mac/Barkfluff/Barkfluff/Entitlements/Barkfluff.entitlements | Sandbox; network.client/network.server, камера, микрофон, Files/Downloads |

## Общие Swift-пакеты

| Путь | Основная ответственность |
|---|---|
| Mac/Barkfluff/Packages/BFProto/ | Proto-контракты и сгенерированные gRPC типы |
| Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Connection/ConnectionManager.swift | Bootstrap Beacon, service endpoints, TLS policy, gRPC clients |
| Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Auth/AuthInterceptor.swift | x-auth-token и proactive token refresh |
| Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Repositories/ | RPC маппинг: auth, messages, files, users, updates, fast auth, calls |
| Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Services/Implementations/ | Сервисы приложения и online tracking |
| Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Database/Migrations.swift | GRDB schema v1–v6 |
| Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Repositories/Local/ | Репозитории SQLite-кеша |
| Mac/Barkfluff/Packages/BFCore/Sources/BFCore/Markdown/ | Markdown AST, parser и sanitizer |
| Mac/Barkfluff/Packages/BFMarkdown/Sources/BFMarkdown/MarkdownMessageView.swift | Общий SwiftUI Markdown renderer |
| Mac/Barkfluff/Packages/BFCalls/Sources/BFCalls/CallController.swift | Общий звонковый state machine поверх LiveKit |

## Проектные настройки

- Mac/Barkfluff/Barkfluff.xcodeproj/project.pbxproj: macOS deployment target 26.0 на app target; project-level конфигурация 26.2 переопределяется target-настройкой.
- Mac/Barkfluff/Barkfluff/Resources/Localizable.xcstrings и BFCore Resources/Localizable.xcstrings: UI/доменная локализация.
- Mac/Barkfluff/Packages/*/Package.swift: реальные нижние границы Swift package dependencies.
