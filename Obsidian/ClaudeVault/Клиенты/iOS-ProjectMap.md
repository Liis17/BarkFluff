# BarkFluff iOS — Карта проекта

Расположение: `iOS/Barkfluff/`
Платформа: iOS 26, Swift 6.2, SwiftUI + gRPC-Swift 2.0

> iOS-клиент является адаптацией macOS-клиента. Архитектура и пакеты общие.
> Описание архитектуры и конвенций: [[Клиенты/iOS]]
> Референс macOS-клиента: [[Клиенты/macOS-ProjectMap]]

**Shared Packages** (расположены в `Mac/Barkfluff/Packages/`, используются напрямую через Xcode):
- `BFProto` — Proto-контракты и сгенерированный gRPC-код
- `BFNetworking` — gRPC-клиент, соединение, репозитории, интерсепторы
- `BFCore` — бизнес-сервисы, модели, кеши, mappers

---

## Точка входа

| Файл | Назначение |
|------|-----------|
| `Barkfluff/BarkfluffApp.swift` | `@main` — точка входа приложения, создаёт `DependencyContainer`, запускает `AppCoordinator` |

---

## App/

| Файл | Назначение |
|------|-----------|
| `App/DI/DependencyContainer.swift` | DI-контейнер — создаёт все репозитории, сервисы, кеши; включает `ImagePipeline` (Nuke с disk cache) вместо macOS Nuke pipeline |
| `App/Models/TokenStorageSettings.swift` | Модель настроек хранилища токенов |

---

## Navigation/

| Файл | Назначение |
|------|-----------|
| `Navigation/AppCoordinator.swift` | `@Observable` координатор — состояния: `loading → serverSelection → authentication → main` |
| `Navigation/RootView.swift` | Корневой SwiftUI-вид по состоянию координатора |
| `Navigation/MainTabView.swift` | `TabView` (tab bar) — основной layout iOS; вместо `NavigationSplitView` как в macOS |

---

## DesignSystem/

| Файл | Назначение |
|------|-----------|
| `DesignSystem/Theme.swift` | Цвета, отступы, радиусы |
| `DesignSystem/Components/AvatarView.swift` | Компонент аватара пользователя/чата |
| `DesignSystem/Components/ErrorBannerView.swift` | Баннер ошибки |
| `DesignSystem/Components/LoadingView.swift` | Индикатор загрузки |

> iOS DesignSystem значительно меньше macOS: отсутствуют `BadgeView`, `FlowLayout`, `OnlineStatusText`, `UnreadBadgeView`, LiquidGlass компоненты, `Typography` — они ещё не перенесены из macOS.

---

## Features/Auth/

| Файл | Назначение |
|------|-----------|
| `Auth/Models/RegistrationData.swift` | Модель данных многошаговой регистрации |
| `Auth/Models/RegistrationStep.swift` | Enum шагов регистрации |
| `Auth/ViewModels/LoginViewModel.swift` | VM входа |
| `Auth/ViewModels/RegisterViewModel.swift` | VM многошаговой регистрации |
| `Auth/ViewModels/ServerSelectionViewModel.swift` | VM выбора сервера |
| `Auth/ViewModels/Validators/EmailValidator.swift` | Валидатор email |
| `Auth/ViewModels/Validators/NameValidator.swift` | Валидатор имени |
| `Auth/ViewModels/Validators/OTPCodeValidator.swift` | Валидатор OTP-кода |
| `Auth/ViewModels/Validators/PasswordValidator.swift` | Валидатор пароля |
| `Auth/ViewModels/Validators/UsernameValidator.swift` | Валидатор username |
| `Auth/Views/LoginView.swift` | Экран входа |
| `Auth/Views/RegisterView.swift` | Экран регистрации (wizard) |
| `Auth/Views/ServerSelectionView.swift` | Экран выбора сервера |
| `Auth/Views/Steps/AvatarStepView.swift` | Шаг: загрузка аватара |
| `Auth/Views/Steps/BioStepView.swift` | Шаг: ввод bio |
| `Auth/Views/Steps/CompletionStepView.swift` | Шаг: завершение регистрации |
| `Auth/Views/Steps/ConfirmEmailStepView.swift` | Шаг: подтверждение email (OTP) |
| `Auth/Views/Steps/EmailStepView.swift` | Шаг: ввод email |
| `Auth/Views/Steps/PasswordStepView.swift` | Шаг: ввод пароля |
| `Auth/Views/Steps/PersonalInfoStepView.swift` | Шаг: имя и фамилия |
| `Auth/Views/Steps/TwoFAStepView.swift` | Шаг: 2FA |
| `Auth/Views/Steps/UsernameStepView.swift` | Шаг: выбор username |

---

## Features/ChatList/

| Файл | Назначение |
|------|-----------|
| `ChatList/ViewModels/ChatListViewModel.swift` | VM списка чатов: загрузка, обновления, онлайн-статусы |
| `ChatList/Views/ChatListView.swift` | Список чатов с `NavigationStack` |

---

## Features/Conversation/

| Файл | Назначение |
|------|-----------|
| `Conversation/ViewModels/ConversationViewModel.swift` | VM диалога: история, отправка, стриминг, вложения |
| `Conversation/Views/ConversationView.swift` | Экран диалога |
| `Conversation/Views/MessageBubbleView.swift` | Пузырь сообщения |
| `Conversation/Views/MessagesListView.swift` | Список сообщений |
| `Conversation/Views/Input/MessageInputView.swift` | Поле ввода + вложения |
| `Conversation/Views/Input/AttachmentPreviewStrip.swift` | Полоса предпросмотра вложений |
| `Conversation/Views/Components/BubbleShape.swift` | Форма пузыря |
| `Conversation/Views/Components/ConversationHeaderView.swift` | Заголовок диалога |
| `Conversation/Views/Components/MediaUploadProgressView.swift` | Прогресс загрузки |
| `Conversation/Views/Components/MessageDateSeparatorView.swift` | Разделитель дат |
| `Conversation/Views/Components/MessageTimeView.swift` | Время и статус сообщения |
| `Conversation/Views/Components/ScrollToBottomButton.swift` | Кнопка прокрутки вниз |
| `Conversation/Views/Attachments/AttachmentGridView.swift` | Сетка вложений |
| `Conversation/Views/Attachments/ImageAttachmentView.swift` | Изображение |
| `Conversation/Views/Attachments/VideoAttachmentView.swift` | Видео |
| `Conversation/Views/Attachments/AudioAttachmentView.swift` | Аудио |
| `Conversation/Views/Attachments/DocumentAttachmentView.swift` | Документ |
| `Conversation/Views/Attachments/FileIconView.swift` | Иконка типа файла |
| `Conversation/Helpers/AttachmentLayoutCalculator.swift` | Расчёт размеров сетки вложений |
| `Conversation/Helpers/MessageGrouper.swift` | Группировка сообщений по дате/автору |
| `Conversation/Helpers/ScrollPositionManager.swift` | Управление прокруткой |
| `Conversation/Models/SelectedAttachment.swift` | Модель выбранного вложения |
| `Conversation/Extensions/MessageAttachmentExtensions.swift` | Расширения модели вложения |

> В iOS отсутствуют (ещё не перенесены из macOS): `DragDropOverlay`, `EmojiPickerView`, `FilePreviewCard`, `MediaPreviewCard`, `SendButton`, `GIFAttachmentView`, полноэкранные просмотрщики (`FullScreenMediaWindow`, `MediaViewerView`, `ImageViewerView`, `VideoPlayerView`), `FileDownloadHelper`, `AttachmentNotifications`.

---

## Features/Profile/

| Файл | Назначение |
|------|-----------|
| `Profile/Views/ProfileView.swift` | Экран профиля текущего пользователя (базовый, без редактирования) |

> В iOS отсутствует: `ProfileEditView`, `ProfileEditViewModel`, `BadgesView`, `ProfileCardView`, `ProfileSidebarView` — из macOS-клиента.

---

## Features/Settings/

| Файл | Назначение |
|------|-----------|
| `Settings/ViewModels/SettingsViewModel.swift` | VM настроек |

> В iOS отсутствуют Views для Settings (нет `SettingsView`, `SecuritySettingsView`, `SessionsView`).

---

## Отсутствующие фичи (есть в macOS, нет в iOS)

| Фича | Статус |
|------|--------|
| FastAuth (QR-авторизация) | ❌ нет |
| GroupChat | ❌ нет |
| UserProfile (панель собеседника) | ❌ нет |
| UserSearch | ❌ нет |
| Полноэкранный просмотр медиа | ❌ нет |
| Emoji picker | ❌ нет |
| Drag & Drop | ❌ нет (iOS специфика — не нужен) |
| LiquidGlass компоненты | ❌ нет (ещё не реализованы) |
| SessionsView, SecuritySettingsView | ❌ нет |
| ProfileEditView | ❌ нет |
