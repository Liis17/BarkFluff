# BarkFluff macOS — Карта проекта

Расположение: `Mac/Barkfluff/`
Платформа: macOS 26, Swift 6.2, SwiftUI + gRPC-Swift 2.0

> Описание архитектуры и конвенций: [[Клиенты/macOS]]

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Barkfluff/BarkfluffApp.swift` | `@main` — точка входа приложения, создаёт `DependencyContainer`, запускает `AppCoordinator` |

---

## App/

| Файл | Назначение |
|------|-----------|
| `App/DI/DependencyContainer.swift` | Главный DI-контейнер — создаёт и связывает все репозитории, кеши, интерсепторы и сервисы; хранит `currentUser` |
| `App/Models/TokenStorageSettings.swift` | Модель настроек хранилища токенов (Keychain vs UserDefaults) |

---

## Navigation/

| Файл | Назначение |
|------|-----------|
| `Navigation/AppCoordinator.swift` | `@Observable` координатор — управляет состояниями приложения: `loading → serverSelection → authentication → main` |
| `Navigation/RootView.swift` | Корневой SwiftUI-вид, переключает экраны по состоянию `AppCoordinator` |
| `Navigation/MainSplitView.swift` | `NavigationSplitView` — основной layout с sidebar и content для macOS |
| `Navigation/SidebarView.swift` | Боковая панель: список чатов + навигация по разделам |
| `Navigation/SidebarContainerView.swift` | Обёртка sidebar с поддержкой Glass-эффектов |
| `Navigation/SettingsCategory.swift` | Enum категорий настроек для навигации |
| `Navigation/Components/SidebarTabBar.swift` | Кастомный tab bar в sidebar (иконки разделов) |
| `Navigation/Components/SidebarTabButton.swift` | Кнопка одного раздела в sidebar с glass-стилем |
| `Navigation/Components/ProfileTabIcon.swift` | Иконка профиля с аватаром пользователя в sidebar |

---

## DesignSystem/

| Файл | Назначение |
|------|-----------|
| `DesignSystem/Theme.swift` | Цвета, отступы, радиусы — единая тема приложения |
| `DesignSystem/Typography.swift` | Стили шрифтов |
| `DesignSystem/Components/AvatarView.swift` | Переиспользуемый компонент аватара пользователя/чата (Nuke) |
| `DesignSystem/Components/BadgeView.swift` | Компонент бейджа пользователя |
| `DesignSystem/Components/ErrorBannerView.swift` | Баннер ошибки (отображается поверх UI) |
| `DesignSystem/Components/FlowLayout.swift` | Flow layout для тегов/бейджей |
| `DesignSystem/Components/LoadingView.swift` | Индикатор загрузки |
| `DesignSystem/Components/OnlineStatusText.swift` | Текстовый индикатор онлайн-статуса |
| `DesignSystem/Components/UnreadBadgeView.swift` | Бейдж непрочитанных сообщений |
| `DesignSystem/LiquidGlass/GlassButtonStyle.swift` | `.buttonStyle(.glass)` / `.buttonStyle(.glassProminent)` — macOS 26 glass-кнопки |
| `DesignSystem/LiquidGlass/GlassCardView.swift` | Карточка с glass-эффектом |
| `DesignSystem/LiquidGlass/GlassModifiers.swift` | ViewModifier-расширения для glass-эффектов |
| `DesignSystem/LiquidGlass/LiquidGlassGuide.swift` | Inline-документация правил применения Liquid Glass |

---

## Features/Auth/

| Файл | Назначение |
|------|-----------|
| `Auth/Models/RegistrationData.swift` | Модель данных многошаговой регистрации |
| `Auth/Models/RegistrationStep.swift` | Enum шагов регистрации |
| `Auth/ViewModels/LoginViewModel.swift` | VM входа: email/пароль, 2FA, обработка ошибок |
| `Auth/ViewModels/RegisterViewModel.swift` | VM регистрации: управление шагами, валидация, загрузка аватара |
| `Auth/ViewModels/ServerSelectionViewModel.swift` | VM выбора сервера: запрос к Navigator, список доступных серверов |
| `Auth/ViewModels/Validators/*.swift` | Валидаторы: Email, Name, OTPCode, Password, Username |
| `Auth/Views/LoginView.swift` | Экран входа |
| `Auth/Views/RegisterView.swift` | Экран регистрации (многошаговый wizard) |
| `Auth/Views/ServerSelectionView.swift` | Экран выбора BarkFluff-сервера |
| `Auth/Views/Steps/*.swift` | Отдельные шаги регистрации: Email, Password, Username, PersonalInfo, ConfirmEmail, Avatar, Bio, Completion, TwoFA |
| `Auth/Components/OTPInputView.swift` | Компонент ввода OTP-кода |
| `Auth/Components/PasswordStrengthView.swift` | Индикатор надёжности пароля |
| `Auth/Components/StepProgressIndicator.swift` | Прогресс-бар шагов регистрации |

---

## Features/ChatList/

| Файл | Назначение |
|------|-----------|
| `ChatList/ViewModels/ChatListViewModel.swift` | VM списка чатов: загрузка, подписка на обновления, онлайн-статусы |
| `ChatList/Views/ChatListView.swift` | Отображение списка чатов с превью последнего сообщения и бейджами |

---

## Features/Conversation/

| Файл | Назначение |
|------|-----------|
| `Conversation/ViewModels/ConversationViewModel.swift` | VM диалога: загрузка сообщений, отправка, стриминг, вложения, пагинация |
| `Conversation/Views/ConversationView.swift` | Основной экран диалога |
| `Conversation/Views/MessageBubbleView.swift` | Пузырь сообщения с текстом и вложениями |
| `Conversation/Views/MessagesListView.swift` | Список сообщений с группировкой по дате |
| `Conversation/Views/MessageAttachmentView.swift` | Отображение одного вложения в пузыре |
| `Conversation/Views/MessageInputView.swift` | Поле ввода сообщения с кнопками вложений |
| `Conversation/Views/MessageDateSeparatorView.swift` | Разделитель дат между сообщениями |
| `Conversation/Views/ConversationHeaderView.swift` | Заголовок диалога с именем и онлайн-статусом |
| `Conversation/Views/Input/AttachmentPreviewStrip.swift` | Полоса предпросмотра прикреплённых файлов перед отправкой |
| `Conversation/Views/Input/DragDropOverlay.swift` | Overlay для drag-and-drop файлов в окно чата |
| `Conversation/Views/Input/EmojiPickerView.swift` / `EmojiData.swift` | Пикер эмодзи |
| `Conversation/Views/Input/FilePreviewCard.swift` | Превью документа/файла перед отправкой |
| `Conversation/Views/Input/MediaPreviewCard.swift` | Превью медиа (фото/видео) перед отправкой |
| `Conversation/Views/Input/SendButton.swift` | Кнопка отправки с glass-стилем |
| `Conversation/Views/Attachments/AttachmentGridView.swift` | Сетка вложений в пузыре сообщения |
| `Conversation/Views/Attachments/ImageAttachmentView.swift` | Вложение-изображение |
| `Conversation/Views/Attachments/VideoAttachmentView.swift` | Вложение-видео |
| `Conversation/Views/Attachments/AudioAttachmentView.swift` | Вложение-аудио |
| `Conversation/Views/Attachments/DocumentAttachmentView.swift` | Вложение-документ |
| `Conversation/Views/Attachments/FileIconView.swift` | Иконка типа файла |
| `Conversation/Views/Attachments/GIFAttachmentView.swift` | Анимированный GIF |
| `Conversation/Views/Viewers/ImageViewerView.swift` | Полноэкранный просмотр изображения |
| `Conversation/Views/Viewers/VideoPlayerView.swift` / `CustomVideoPlayerView.swift` | Встроенный видеоплеер |
| `Conversation/Views/Viewers/MediaViewerView.swift` | Универсальный просмотрщик медиа |
| `Conversation/Views/Viewers/FullScreenMediaWindow.swift` | Отдельное окно macOS для полноэкранного медиа |
| `Conversation/Views/Components/BubbleShape.swift` | Форма пузыря сообщения |
| `Conversation/Views/Components/MediaUploadProgressView.swift` | Прогресс загрузки файла |
| `Conversation/Views/Components/MessageTimeView.swift` | Время и статус сообщения |
| `Conversation/Views/Components/ScrollToBottomButton.swift` | Кнопка прокрутки вниз |
| `Conversation/Helpers/AttachmentLayoutCalculator.swift` | Расчёт размеров сетки вложений |
| `Conversation/Helpers/AttachmentNotifications.swift` | NotificationCenter-уведомления о вложениях |
| `Conversation/Helpers/FileDownloadHelper.swift` | Скачивание файла и сохранение на диск (macOS NSSavePanel) |
| `Conversation/Helpers/MessageGrouper.swift` | Группировка сообщений по дате и автору |
| `Conversation/Helpers/ScrollPositionManager.swift` | Управление позицией прокрутки, автоскролл |
| `Conversation/Models/SelectedAttachment.swift` | Модель выбранного для отправки вложения |
| `Conversation/Extensions/MessageAttachmentExtensions.swift` | Расширения модели вложения |

---

## Features/FastAuth/

| Файл | Назначение |
|------|-----------|
| `FastAuth/ViewModels/FastAuthViewModel.swift` | VM QR-авторизации: генерация QR, polling статуса |
| `FastAuth/Views/FastAuthQRView.swift` | Экран отображения QR-кода |
| `FastAuth/Views/FastAuthApprovalView.swift` | Экран подтверждения авторизации на другом устройстве |

---

## Features/GroupChat/

| Файл | Назначение |
|------|-----------|
| `GroupChat/ViewModels/GroupChatViewModel.swift` | VM группового чата: создание, управление участниками |
| `GroupChat/Views/CreateGroupChatView.swift` | Экран создания группового чата |
| `GroupChat/Views/GroupInfoView.swift` | Информация о группе |
| `GroupChat/Views/MembersListView.swift` | Список участников группы |

---

## Features/Main/

| Файл | Назначение |
|------|-----------|
| `Main/Views/AuthSuccessView.swift` | Экран успешной авторизации (splash после входа) |

---

## Features/Profile/

| Файл | Назначение |
|------|-----------|
| `Profile/ViewModels/ProfileEditViewModel.swift` | VM редактирования профиля: смена имени, username, аватара, bio |
| `Profile/Models/UsernameValidationStatus.swift` | Enum статуса валидации username |
| `Profile/Views/ProfileCardView.swift` | Карточка профиля текущего пользователя |
| `Profile/Views/ProfileSidebarView.swift` | Профиль в боковой панели |
| `Profile/Views/ProfileEditView.swift` | Экран редактирования профиля |
| `Profile/Views/BadgesView.swift` | Просмотр бейджей пользователя |

---

## Features/Settings/

| Файл | Назначение |
|------|-----------|
| `Settings/ViewModels/SettingsViewModel.swift` | VM настроек: управление сессиями, безопасностью, выход |
| `Settings/Views/SettingsView.swift` | Основной экран настроек |
| `Settings/Views/SettingsPlaceholderView.swift` | Placeholder для пустых разделов настроек |
| `Settings/Views/SecuritySettingsView.swift` | Настройки безопасности (2FA, смена пароля) |
| `Settings/Views/SessionsView.swift` | Список активных сессий и их завершение |

---

## Features/UserProfile/

| Файл | Назначение |
|------|-----------|
| `UserProfile/ViewModels/UserProfilePanelViewModel.swift` | VM панели профиля другого пользователя |
| `UserProfile/Views/UserProfilePanelView.swift` | Боковая панель с профилем собеседника |
| `UserProfile/Views/ProfileHeaderSection.swift` | Шапка с аватаром и именем |
| `UserProfile/Views/ProfileInfoSection.swift` | Секция с информацией (bio, username) |
| `UserProfile/Views/ProfileActionsSection.swift` | Действия (написать, заблокировать) |
| `UserProfile/Views/SharedMediaSection.swift` | Секция общих медиафайлов |
| `UserProfile/Views/SharedMediaGridView.swift` | Сетка общих медиа |
| `UserProfile/Views/SharedDocumentsListView.swift` | Список общих документов |
| `UserProfile/Views/GroupMembersSection.swift` | Секция участников группы в профиле |

---

## Features/UserSearch/

| Файл | Назначение |
|------|-----------|
| `UserSearch/ViewModels/UserSearchViewModel.swift` | VM поиска пользователей по username |
| `UserSearch/Views/UserSearchView.swift` | Экран поиска с результатами |

---

## Пакет BFProto

Расположение: `Packages/BFProto/`

| Файл | Назначение |
|------|-----------|
| `Sources/BFProto/Generated/beacon_api.*` | Proto-код Beacon (точка входа клиентов) |
| `Sources/BFProto/Generated/identity_api.*` | Proto-код Identity (авторизация, JWT, 2FA) |
| `Sources/BFProto/Generated/users_api.*` | Proto-код Users (профили) |
| `Sources/BFProto/Generated/messages_api.*` | Proto-код Messages (чаты, сообщения) |
| `Sources/BFProto/Generated/files_api.*` | Proto-код Files (загрузка файлов, S3) |
| `Sources/BFProto/Generated/updates_api.*` | Proto-код Updates (real-time стриминг событий) |
| `Sources/BFProto/Generated/onliner_api.*` | Proto-код Onliner (онлайн-статусы) |
| `Sources/BFProto/Generated/navigator_api.*` | Proto-код Navigator (реестр серверов) |
| `Sources/BFProto/Generated/fast_auth_api.*` | Proto-код FastAuth (QR-авторизация) |
| `Sources/BFProto/Generated/configuration_api.*` | Proto-код Configuration |
| `Sources/BFProto/Generated/developers_api.*` | Proto-код Developers |
| `Sources/BFProto/Generated/shared.*` | Общие proto-типы |
| `Protos/*.proto` | Исходные .proto файлы (копии из `Shared/BarkFluff.Proto`) |

---

## Пакет BFNetworking

Расположение: `Packages/BFNetworking/`

| Файл | Назначение |
|------|-----------|
| `Auth/AuthInterceptor.swift` | gRPC-интерсептор: добавляет JWT Bearer в каждый запрос, триггерит рефреш |
| `Auth/KeychainTokenProvider.swift` | Хранение токенов в Keychain (production) |
| `Auth/UserDefaultsTokenProvider.swift` | Хранение токенов в UserDefaults (отладка/preview) |
| `Auth/TokenRefreshCoordinator.swift` | Actor, координирует рефреш токена без гонок |
| `Auth/TokenExportData.swift` | DTO для экспорта токенов (FastAuth handoff) |
| `Connection/ConnectionManager.swift` | Actor, управляет gRPC-соединением: подключение, переподключение, shutdown |
| `Connection/ConnectionState.swift` | Enum состояний соединения |
| `Connection/ServerInfoDTO.swift` | DTO информации о сервере от Navigator |
| `DTOs.swift` | Все DTO-типы сетевого слоя |
| `Errors/BFNetworkingError.swift` | Ошибки сетевого слоя |
| `Errors/ConnectionError.swift` | Ошибки подключения |
| `Metadata/DeviceMetadataInterceptor.swift` | Интерсептор: добавляет метаданные устройства (device ID, platform, version) |
| `Metadata/DeviceMetadataProvider.swift` | Провайдер метаданных устройства |
| `Protocols/RepositoryProtocols.swift` | Протоколы всех репозиториев |
| `Protocols/TokenProvider.swift` | Протокол провайдера токенов |
| `Repositories/IdentityRepository.swift` | gRPC-клиент Identity: login, register, refresh, 2FA |
| `Repositories/UsersRepository.swift` | gRPC-клиент Users: профили, устройства |
| `Repositories/MessagesRepository.swift` | gRPC-клиент Messages: чаты, сообщения, история |
| `Repositories/FilesRepository.swift` | gRPC-клиент Files: загрузка/скачивание файлов |
| `Repositories/UpdatesRepository.swift` | gRPC-клиент Updates: стриминг событий (ServerStreamingCall) |
| `Repositories/OnlinerRepository.swift` | gRPC-клиент Onliner: онлайн-статусы |
| `Repositories/OnlinerStreamManager.swift` | Actor, управляет стримом онлайн-статусов |
| `Repositories/BeaconRepository.swift` | gRPC-клиент Beacon: получение информации о сервере |
| `Repositories/NavigatorRepository.swift` | gRPC-клиент Navigator (TLS): список серверов |
| `Repositories/FastAuthRepository.swift` | gRPC-клиент FastAuth: QR-токены |
| `Utilities/GRPCErrorMapper.swift` | Маппинг gRPC-статусов в `BFNetworkingError` |
| `Utilities/RetryPolicy.swift` | Политика повтора запросов |

---

## Пакет BFCore

Расположение: `Packages/BFCore/`

| Файл | Назначение |
|------|-----------|
| `Models/Chat.swift` | Модель чата |
| `Models/Message.swift` | Модель сообщения с вложениями |
| `Models/User.swift` | Модель пользователя |
| `Models/Token.swift` | Модель токена (access + refresh) |
| `Models/Session.swift` | Модель сессии устройства |
| `Models/OnlineStatus.swift` / `OnlineStatusEvent.swift` | Модели онлайн-статуса |
| `Models/Events.swift` | Модели real-time событий (NewMessage, MessageRead, etc.) |
| `Models/FastAuthToken.swift` | Модель QR-токена FastAuth |
| `Models/ServerInfo.swift` | Модель информации о сервере |
| `Models/SharedMediaFilter.swift` / `SharedMediaItem.swift` | Модели фильтрации и элементов общих медиа |
| `Mappers/ChatMapper.swift` | Proto → Chat |
| `Mappers/MessageMapper.swift` | Proto → Message |
| `Mappers/UserMapper.swift` | Proto → User |
| `Mappers/FileMapper.swift` | Proto → FileInfo |
| `Cache/CacheProtocol.swift` | Протокол универсального кеша |
| `Cache/InMemoryCache.swift` | Базовый in-memory кеш (actor) |
| `Cache/FileCacheService.swift` | Кеш скачанных файлов на диске |
| `Cache/FileURLCache.swift` | Кеш URL файлов (presigned S3) |
| `Cache/OnlineStatusCache.swift` | Кеш онлайн-статусов |
| `Services/Protocols/*Protocol.swift` | Протоколы всех бизнес-сервисов |
| `Services/Implementations/AuthService.swift` | Авторизация, регистрация, выход, рефреш |
| `Services/Implementations/ChatService.swift` | Получение, создание чатов |
| `Services/Implementations/MessageService.swift` | Отправка, загрузка истории, стриминг сообщений |
| `Services/Implementations/UserService.swift` | Получение/обновление профилей с кешированием |
| `Services/Implementations/FileService.swift` | Загрузка/скачивание файлов через S3 |
| `Services/Implementations/UpdatesService.swift` | Подписка и обработка real-time событий |
| `Services/Implementations/OnlineStatusService.swift` | Получение и отслеживание онлайн-статусов |
| `Services/Implementations/FastAuthService.swift` | QR-авторизация |
| `Services/Implementations/ServerDiscoveryService.swift` | Обнаружение и подключение к BarkFluff-серверу |
| `Services/Implementations/SharedMediaService.swift` | Загрузка общих медиафайлов диалога |
| `Utilities/DateFormatters.swift` | Форматтеры дат для UI |
| `Utilities/Errors/BFError.swift` | Доменные ошибки приложения |
| `Utilities/Errors/ErrorLocalizer.swift` | Локализация ошибок для отображения пользователю |
| `Utilities/PaginationHelper.swift` | Утилита курсорной пагинации |

---

## Технические файлы

| Файл | Назначение |
|------|-----------|
| `Protos/*.proto` | Исходные proto-файлы (зеркало `Shared/BarkFluff.Proto`) |
| `ExportOptions.plist` | Параметры экспорта архива для App Store |
| `LiquidGlassGuide.md` | Руководство применения Liquid Glass в проекте |
| `Mac/TZ_0_FilesRepository.md` … `TZ_4_Registration_macOS.md` | Технические задания по основным фичам |
