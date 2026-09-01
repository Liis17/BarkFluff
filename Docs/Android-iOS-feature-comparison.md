# Сравнение функционала Android и iOS клиентов

Дата анализа: 2026-07-05.

Основано на Obsidian-документации клиентов и текущем коде:
- `Android/Barkfluff.Client.Android/app`
- `iOS/Barkfluff`
- общие Swift-пакеты `Mac/Barkfluff/Packages`

Важно: iOS уже имеет звонки через `BFCalls` и `CallOverlayView`, но только при активном приложении, без VoIP-push/CallKit. Android-звонки функционально шире.

## Сводная таблица

| Функция | Android клиент | iOS клиент | Сводка |
|---|---|---|---|
| Архитектура | Activity/Fragment, ViewBinding, SharedPreferences/EncryptedSharedPreferences | SwiftUI, MVVM/DI, GRDB local cache, shared packages `BFCore/BFNetworking/BFCalls` | iOS архитектурно чище и сильнее по локальному кешу |
| Выбор сервера / Beacon | Да | Да | Паритет |
| Логин | Да, username/email + 2FA | Да, login + 2FA | Паритет |
| Регистрация | Да, многошаговая, avatar/bio/2FA | Да, многошаговая, avatar/bio/2FA | Паритет |
| Сброс пароля | Да, `ResetPasswordActivity` | Не найден UI-флоу | Android-only |
| Список чатов | Да, realtime + папки | Да, stale-while-revalidate + offline banner | iOS сильнее по offline UX |
| Папки чатов | Да, вкладки, создание/редактирование, compact/exclude | Да, вкладки, создание/редактирование, compact/exclude | Паритет |
| Поиск пользователей / новый чат | Да, `SearchActivity` | Да, `UserSearchView` | Паритет |
| Группы | Да: создание/инфо/участники/аватар/добавление/удаление | Да: создание, group info, members | Почти паритет |
| Сообщения: текст | Да | Да | Паритет |
| Вложения фото/видео/документы | Да, upload queue, previews, progress | Да, PhotosPicker/fileImporter, previews, progress | Паритет, Android богаче по обработке медиа |
| Голосовые сообщения | Да, запись OGG/Opus из инпута | Не найдена запись voice message в UI | Android-only |
| Стикеры | Да | Да | Паритет |
| Reply | Да, меню + свайп | Да | Паритет |
| Forward | Да, полноценный picker и отправка | Частично: VM есть, `ForwardChatPickerView` пока заглушка | Android сильнее |
| Edit/Delete сообщений | Да | Да | Паритет |
| Закреплённые сообщения | Да, `PinnedMessagesActivity`, pinned bar | Не найден полноценный UI | Android-only |
| Shared media/documents в профиле | Есть частично в профиле пользователя | Да, отдельные секции shared media/documents | iOS сильнее |
| Профиль пользователя | Да | Да | iOS цельнее как push-экран внутри чата |
| Персонализация | Да: фон, пузыри, папки, постер/аватар по докам | Да: постер, фон, blur/dim, bubble radius, backgrounds grid | Паритет, iOS цельнее по UI |
| Темы / Material | Android Material You dynamic colors | SwiftUI appearance + Liquid Glass | Разные нативные реализации |
| Локализация | Есть `strings.xml` на нескольких языках + `LocaleManager` | Полная `Localizable.xcstrings`, RU/EN, live switch | iOS сильнее по текущей реализации |
| Push-уведомления | Да, FCM, каналы, mark-as-read, call notifications | Нет: notifications screen заглушка | Android-only |
| Звонки | Да: Calls tab/history, incoming screen, LiveKit, Telecom, foreground service, ringtone, screen share | Да: `BFCalls`, LiveKit, incoming/outgoing overlay, mic/camera/screen share; только active app, без VoIP/CallKit | Android заметно сильнее |
| FastAuth QR | Да, scanner + confirm/reject | Да, scanner + confirm/reject | Паритет |
| Активные сессии | Да | Да | Паритет |
| 2FA настройки | Да | Да | Паритет |
| Privacy settings | Да | Да | Паритет |
| Cache/storage/cloud settings | Да | Да | Паритет |
| Виджеты | Да, pinned chats widget | Нет | Android-only |
| Share-in из системы | Да, `ACTION_SEND/SEND_MULTIPLE` receiver | Не найден share extension | Android-only |
| Медиа-редактор | Да: crop/draw/video trim/editor | Нет сопоставимого редактора | Android-only |
| Секретные/E2E чаты | Да: private passphrase + secret Signal/device flow | Не найдено | Android-only |
| Deep links | Да, `bf://`, `bfdev://` | Не найден аналог | Android-only |
| Автообновление клиента | Да, `UpdateActivity/UpdateChecker` | Не найдено | Android-only |

## Итог

Ядро мессенджера у Android и iOS близко к паритету: сервер, auth, чаты, папки, группы, базовые вложения, стикеры, reply/edit/delete, профили и настройки.

Android сейчас функционально шире за счёт платформенных и тяжёлых сценариев: push, звонки, voice messages, виджеты, system share-in, медиа-редактор, pinned messages, E2E/secret chats, deep links и updater.

iOS сильнее в архитектуре, локальном GRDB-кеше, offline-first UX, локализации и цельности профиля/настроек.

## Основные точки проверки

- `Obsidian/ClaudeVault/Клиенты/Android.md`
- `Obsidian/ClaudeVault/Клиенты/iOS.md`
- `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/ChatActivity.kt`
- `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/MainActivity.kt`
- `iOS/Barkfluff/Barkfluff/Navigation/RootView.swift`
- `iOS/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift`
