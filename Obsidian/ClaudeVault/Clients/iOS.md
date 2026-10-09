# BarkFluff iOS

SwiftUI-клиент для iPhone и iPad. Исходники приложения — iOS/Barkfluff/Barkfluff/, общие Swift-пакеты физически находятся в Mac/Barkfluff/Packages/ и подключаются проектом iOS напрямую.

> Карта точек входа: [[Clients/iOS-ProjectMap]]
> Общие state, network и message-контракты: [[Clients/macOS]]
> UI/UX-сценарии: [[Clients/DesignDocument]]

## Платформа и общие сервисы

- iOS deployment target — 26.0; app target использует Swift 5.0. Swift packages требуют tools-version 6.2 и объявляют iOS 26. SDK задаётся установленным Xcode, в project.pbxproj конкретный SDK не зафиксирован.
- Xcode-проект подключает BFProto, BFNetworking, BFCore, BFMarkdown и BFCalls по локальным путям из Mac/Barkfluff/Packages/.
- Сервисы, gRPC, GRDB-кеш, авторизация и state-поведение общие с macOS; их основные контракты описаны в [[Clients/macOS]]. App-specific state находится в iOS/Barkfluff/Barkfluff/Navigation/AppCoordinator.swift и App/DI/DependencyContainer.swift.
- ChatListView тоже ждёт connection gate перед loadChats/loadFolders/loadCurrentUser. При timeout chat cache не читается, потому что загрузчики не запущены; приложение снимает splash и показывает пустой/текущий список без явной ошибки подключения.

## Навигация и адаптивная раскладка

- MainTabView содержит вкладки Chats и Profile. Вкладка чатов использует NavigationSplitView: на широком экране видны список и detail, в compact-контейнере система сворачивает split-навигацию.
- AppCoordinator.selectedChat — источник выбранного чата; chatNavigationPath предназначен для дополнительных detail-маршрутов, например UserProfilePanelView. Профиль и настройки идут по собственному profileNavigationPath.
- Код принимает решения по доступной ширине текущего view, а не по модели устройства или ориентации. NavigationSplitView выбирает структуру; ReadableContentContainer ограничивает ширину auth около 560 pt, форм и профиля — около 720 pt.
- MessageBubbleView рассчитывает максимум по detail-ширине от 300 до 560 pt; media-grid масштабируется в доступной ширине. MessageInputView использует ViewThatFits для однострочной и компактной двухстрочной компоновки.
- Проект не запрещает полноэкранный режим и объявляет четыре ориентации для iPhone и iPad.

## Фото и персонализация

- Для изображений iOS использует PhotosPicker и UIImage; системный fileImporter заменён PhotosPicker.
- ImageCropperView работает через UIScrollView/UIViewControllerRepresentable, поддерживает pinch/pan и уважает imageOrientation. Профильное фото: 1:1/1024×1024; постер: 3:1/1500×500. Результат кодируется в JPEG перед загрузкой.
- В Settings → Personalization доступны постер профиля и список фонов. Фоны загружаются как messageAttachmentImage, постер — как userProfilePoster. UpdatePersonalization принимает posterFileID и весь backgroundFileIDs вместе; при изменении фонов передавайте текущий постер.
- Локальный radius/blur/dim/background selection хранится в UserDefaults. Профиль, папки чатов, privacy settings и остальные серверные модели используют общие BFCore/BFNetworking services.

## Отличия функций от macOS

- FastAuth: iOS выступает авторизованным сканером QR, не генератором. Сканер получает confirmationCode и данные нового устройства; Accept/Reject RPC отправляются с авторизованным клиентом. Генерация QR и получение результата входа выполняются на новой desktop/web-сессии.
- Звонки используют общий BFCalls/LiveKit, но CallOverlayView на iOS занимает экран. RootView останавливает звонок при переходе приложения в background и поднимает stream при возврате в active; VoIP push и CallKit здесь не подключены.
- На iOS нет NotificationService / remote push для входящих сообщений. Уведомления macOS строятся отдельно из локального Updates stream.
- ForwardChatPickerView пока placeholder: sheet показывает описание, хотя iOS приложение монтирует его при действии пересылки. ViewModel пересылки не подключён к этому экрану.
- Стикер-пикер поддерживает выбор и отправку стикера. Медиадействия адаптированы к UIPasteboard, Photos и share sheet.
- SecuritySettingsView показывает переключатель 2FA, но `enable2FA`/`disable2FA` пока TODO и серверную защиту не включают. Смена token storage переносит токены и требует перезапуска. PIN/биометрической блокировки приложения в коде нет. E2E-шифрование в Apple-клиенте также не реализовано; см. ограничения в [[Clients/macOS]].

## Локализация

Каталог iOS/Barkfluff/Barkfluff/Localizable.xcstrings содержит переводы ru/en. LocalizationSettings предлагает system, ru, en, es, zh-Hans и de; для es/zh/de каталога нет, поэтому текстовые ключи откатываются на английский source. SwiftUI получает locale из корневого WindowGroup в BarkfluffApp.swift. Logout сбрасывает выбор на system.

## Сборка

Из корня репозитория: cd iOS/Barkfluff && xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -destination 'generic/platform=iOS Simulator' build
