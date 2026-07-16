# BarkFluff Android — Индекс файлов проекта

> Компактный справочник всех Kotlin-файлов Android-клиента с кратким описанием роли каждого.
> Исходники V1 (UI-слой): `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/`
> Детальное описание классов и связей: [[Android-ProjectMap]]
> Общее описание клиента: [[Android]] · Архитектура V2 и модуль `:core`: [[Android-V2]]
>
> ⚠️ **Пакеты `grpc/`, `data/`, `repository/` физически переехали в общий модуль `Android/core/src/main/java/com/barkfluff/client/` (`:core`)**, чтобы их могли переиспользовать оба приложения (V1 `app` и V2 `Barkfluff.ClientV2.Android`). Пакеты `calls/` и `crypto/` существуют **в обоих** модулях с разным назначением: в `:core` — сетевой/крипто-слой (репозитории, состояние сессий), в `app` — UI (Activities, Views, Telecom-интеграция, bootstrap). Ниже это указано в заголовке каждого раздела.

---

## Activities & Fragments (root)

| Файл | Роль |
|------|------|
| `BarkFluffApplication.kt` | Application-класс; инициализирует все синглтоны (GrpcManager, RealtimeService, кэши) |
| `SplashActivity.kt` | Точка входа; роутер — проверяет токены и направляет на WelcomeActivity, LoginActivity или MainActivity |
| `WelcomeActivity.kt` | Приветственный экран при первом запуске; запрашивает разрешения; ведёт на SelectServerActivity |
| `SelectServerActivity.kt` | Выбор/ввод адреса сервера; загружает список серверов через Navigator API; сохраняет endpoint'ы |
| `LoginActivity.kt` | Авторизация (логин/email, пароль, 2FA OTP); сохраняет токены в EncryptedSharedPreferences |
| `RegisterActivity.kt` | Регистрация в 9 шагов (имя, username, email, OTP, пароль, аватар, bio, 2FA, завершение) |
| `ResetPasswordActivity.kt` | Сброс пароля через email (запрос кода → подтверждение → новый пароль) |
| `MainActivity.kt` | Главный экран; BottomNavigation с 3 табами (Чаты, Звонки, Профиль); обрабатывает deep links и уведомления |
| `ChatsFragment.kt` | Список чатов; подписывается на RealtimeService; обновляется при новых сообщениях |
| `CallsFragment.kt` | Вкладка звонков — история (`CallHistoryAdapter`), инициация нового звонка |
| `ProfileFragment.kt` | Профиль пользователя + меню настроек + постер профиля; кнопка выхода (LogoutHelper) |
| `ChatActivity.kt` | Экран переписки; пагинация сообщений, вложения всех типов, стикер-панель, фон чата, блюр |
| `GroupInfoActivity.kt` | Информация о группе: название, аватар, участники; вход в `AddGroupMemberActivity`/редактирование |
| `AddGroupMemberActivity.kt` | Добавление участника в групповой чат (`MessagesApi.AddUser`) |
| `CreateChatBottomSheet.kt` | Нижний лист выбора: новый личный чат / новая группа |
| `CreateGroupChatActivity.kt` | Создание группового чата (выбор участников через `GroupMemberPickerAdapter`, название) |
| `PinnedMessagesActivity.kt` | Список закреплённых сообщений чата |
| `PrivateChatActivity.kt` | Экран приватного (E2E через passphrase) чата — шифрование Argon2id |
| `SecretChatActivity.kt` | Экран секретного чата — Signal Double Ratchet, сообщения не хранятся на сервере |
| `CreateEncryptedChatActivity.kt` | Создание приватного или секретного чата с собеседником |
| `FolderChatPickerActivity.kt` | Выбор чатов для добавления в папку |
| `FolderEditActivity.kt` | Создание/редактирование папки чатов (название, состав) |
| `ChatFoldersSettingsActivity.kt` | Управление папками чатов (список, порядок, удаление) |
| `UserProfileActivity.kt` | Полноэкранный профиль собеседника/группы; постер, аватар, онлайн-статус, медиафайлы чата |
| `ImageViewerActivity.kt` | Просмотр одиночного изображения с зумом и swipe-down dismiss |
| `MediaViewerActivity.kt` | Просмотр видео через ExoPlayer (media3); swipe-down dismiss |
| `PreviewImageActivity.kt` | Предпросмотр локального изображения перед отправкой |
| `PreviewVideoActivity.kt` | Предпросмотр локального видео перед отправкой через ExoPlayer |
| `SearchActivity.kt` | Поиск пользователей через Users API; открывает чат по результату |
| `DevicesActivity.kt` | Список авторизованных устройств/сессий; завершение сессии; QR-кнопка для FastAuth |
| `FastAuthConfirmActivity.kt` | Подтверждение/отклонение FastAuth QR-входа нового устройства |
| `QrScannerActivity.kt` | Сканер QR-кода через CameraX + ML Kit; инициирует FastAuth |
| `DeepLinkActivity.kt` | Перехватывает `bf://` / `bfdev://` deep links и перенаправляет в приложение |
| `AccountSettingsActivity.kt` | Редактирование профиля (имя, username, bio, email, аватар через uCrop) |
| `SecuritySettingsActivity.kt` | Смена пароля, управление двухфакторной аутентификацией |
| `PrivacySettingsActivity.kt` | Настройки приватности (видимость онлайна/аватара/bio: Все / Друзья / Никто) |
| `NotificationSettingsActivity.kt` | Переключатель уведомлений и настройки каналов Android |
| `StorageSettingsActivity.kt` | Просмотр и очистка кэша (FileCache, StickerCache, ImageCache) с разбивкой по категориям |
| `PersonalizationSettingsActivity.kt` | Персонализация: закругление пузырей, фон чата, блюр фона, затемнение фона |
| `UpdateActivity.kt` | Экран обновления приложения; changelog; скачивание APK |
| `AboutActivity.kt` | «О приложении»: версия, лицензии, ссылки |
| `LanguageSettingsActivity.kt` | Выбор языка приложения (локализация через `LocaleManager`) |
| `WidgetsSettingsActivity.kt` | Настройка виджетов главного экрана (закреплённые чаты) |
| `TestingSettingsActivity.kt` | Флаги для внутреннего тестирования (debug-меню) |

---

## Звонки (`calls/` — в двух модулях)

Аудио/видеозвонки 1-на-1 и групповые поверх LiveKit SFU (см. [[Backend/Calls]], `calls_api.proto`). Точки входа — `CallsFragment.kt` (история, root) и пакет `calls/`.

### `:core` → `calls/` (сетевой слой)

| Файл | Роль |
|------|------|
| `calls/CallRepository.kt` | Фасад `CallsApi`: InitiateCall/JoinCall/AcceptCall/RejectCall/EndCall/SetCallAudioQuality/ListCallHistory/GetActiveCalls |
| `calls/CallEventsService.kt` | Подписка на `SubscribeCallEvents` (device-scoped стрим), диспетчеризация `CallEvent` в UI-слой (`app` → `calls/LiveKitCallEngine.kt`) |

### `app` → `calls/` (UI-слой)

| Файл | Роль |
|------|------|
| `calls/LiveKitCallEngine.kt` | Обёртка над LiveKit `Room`: подключение, треки камеры/экрана/микрофона, пересборка списка `CallParticipant` из состояния комнаты |
| `calls/CallActivity.kt` | Экран активного звонка: сетка плиток участников (`CallTileView`), управление медиа, таймер, качество голоса |
| `calls/IncomingCallActivity.kt` | Полноэкранный входящий звонок (ring UI), поднимается поверх lock screen |
| `calls/CallTileView.kt` | Кастомный View — плитка участника звонка (аватар/видео-рендерер, имя, индикатор голоса) |
| `calls/WaveformView.kt` | Анимированный индикатор голоса (эквалайзер-полоски), активен пока говорит участник |
| `calls/CallParticipant.kt` | UI-модель участника: identity, имя, `isLocal`, видео-треки камеры/экрана |
| `calls/CallExtras.kt` | Константы Intent-actions/extras для звонков (`ACTION_ACCEPT_CALL`, `EXTRA_CALL_ID`, `EXTRA_LIVEKIT_URL` и т.п.) |
| `calls/CallForegroundService.kt` | Foreground `Service` активного звонка (типы microphone/camera/mediaProjection) — держит процесс живым при активном звонке |
| `calls/BarkFluffConnectionService.kt` | `ConnectionService` — self-managed `PhoneAccount` для интеграции с системным Telecom (входящие/исходящие как обычные звонки ОС) |
| `calls/CallTelecomManager.kt` | Обёртка над `TelecomManager`: регистрация исходящего/входящего звонка в системе (`addNewIncomingCall`) |
| `calls/CallTelecomRegistry.kt` | `ConcurrentHashMap` активных `Connection` по `callId` — мост между `BarkFluffConnectionService` и `LiveKitCallEngine` |
| `calls/CallActionReceiver.kt` | `BroadcastReceiver` для действий из уведомления/лок-скрина (принять/отклонить/завершить звонок) |
| `calls/CallBatteryOptimizationHelper.kt` | Хелпер запроса исключения из battery optimization (чтобы входящие звонки доходили в Doze) |

---

## gRPC слой (`:core` → `grpc/`)

| Файл | Роль |
|------|------|
| `grpc/GrpcManager.kt` | Центральный менеджер всех gRPC-клиентов и stub'ов; инициализация каналов, методы API (auth, chats, users, files, messages, online) |
| `grpc/RealtimeService.kt` | gRPC streaming: подписки на новые сообщения, read receipts, онлайн-статусы; exponential backoff; ping-loop |
| `grpc/RealtimeSideEffects.kt` | Интерфейс побочных эффектов realtime-событий (уведомления/аватары/виджеты), реализуется на стороне приложения — см. `notifications/RealtimeSideEffectsImpl.kt` (app) |
| `grpc/AuthInterceptor.kt` | ClientInterceptor; добавляет заголовок `x-auth-token` к каждому gRPC-запросу |
| `grpc/DeviceInfoInterceptor.kt` | ClientInterceptor; добавляет заголовки устройства (deviceId, OS, appName, IP) base64-encoded |

---

## Data (`:core` → `data/`)

| Файл | Роль |
|------|------|
| `data/GlobalParam.kt` | Хранилище состояния сессии (SharedPreferences + EncryptedSharedPreferences): токены, адреса серверов, данные юзера, настройки персонализации |
| `data/OpenChatManager.kt` | Синглтон; хранит ID текущего открытого чата для подавления дублирующих уведомлений |
| `data/ServerDataElement.kt` | Data class элемента списка серверов (ip, title, userCount, location, цвет) |
| `data/ClientColors.kt` | Data class цветовой темы сервера (liteHex, mainHex, hardHex) |

---

## Repository (`:core` → `repository/`)

| Файл | Роль |
|------|------|
| `repository/ChatRepository.kt` | Инкапсулирует Messages API: загрузка сообщений с пагинацией, отправка, скачивание файлов в FileCache |
| `repository/PrivateChatRepository.kt` | Фасад приватных (E2E через passphrase) чатов: CreatePrivateChat/Accept/Reject, отправка/чтение шифрованных сообщений |
| `repository/SecretChatRepository.kt` | Фасад секретных чатов (Signal Double Ratchet): инвайты, отправка/подтверждение сообщений устройству |

---

## Adapters (`adapter/`)

| Файл | Роль |
|------|------|
| `adapter/MessageAdapter.kt` | ListAdapter для сообщений; 4 типа ViewHolder (sent/received/date-sep/unread-sep); вложения всех типов; закругление пузырей |
| `adapter/ChatAdapter.kt` | ListAdapter списка чатов; аватар, последнее сообщение, счётчик непрочитанных; footer-спейсер |
| `adapter/ChatBackgroundAdapter.kt` | Сетка фоновых изображений чата (3 колонки); выбор, добавление, режим удаления |
| `adapter/UserAdapter.kt` | Список пользователей (контакты, результаты поиска) |
| `adapter/DeviceAdapter.kt` | Список устройств/сессий |
| `adapter/ServerAdapter.kt` | Список серверов в SelectServerActivity |
| `adapter/ImageGridAdapter.kt` | Квадратная сетка изображений-вложений в сообщении |
| `adapter/ImagePagerAdapter.kt` | ViewPager для просмотра нескольких изображений |
| `adapter/ImagePickerAdapter.kt` | Галерея медиафайлов устройства в ImagePickerBottomSheet |
| `adapter/AttachmentPreviewAdapter.kt` | Горизонтальная полоса предпросмотра вложений перед отправкой |
| `adapter/StickerPanelAdapter.kt` | Стикер-панель в ChatActivity (заголовок пака, стикер, loading, empty) |
| `adapter/StickerPanelItem.kt` | Data class элемента стикер-панели |
| `adapter/CallHistoryAdapter.kt` | Список истории звонков в `CallsFragment` |
| `adapter/ChatFoldersAdapter.kt` | Список папок чатов в `ChatFoldersSettingsActivity` |
| `adapter/GroupMemberAdapter.kt` | Список участников группового чата в `GroupInfoActivity` |
| `adapter/EncryptedMessageAdapter.kt` | `ListAdapter` сообщений приватного/секретного чата (`PrivateChatActivity`/`SecretChatActivity`) |
| `adapter/ForwardChatPickerAdapter.kt` | Список чатов для пересылки сообщения в `ForwardChatPickerBottomSheet` |
| `adapter/ReplySwipeCallback.kt` | `ItemTouchHelper.Callback` — свайп сообщения вправо инициирует ответ (reply) |
| `adapter/ChatSkeletonAdapter.kt` | Скелетон-заглушки списка чатов во время загрузки |
| `adapter/FolderTabsAdapter.kt` | Горизонтальные вкладки папок чатов над списком |
| `adapter/GroupMemberPickerAdapter.kt` | Выбор участников при создании группового чата (`CreateGroupChatActivity`) |

---

## Редактор медиа (`editor/`)

Редактирование изображений/видео перед отправкой (рисование, обрезка видео).

| Файл | Роль |
|------|------|
| `editor/MediaEditorActivity.kt` | Экран редактирования изображения: рисование, палитра цветов, толщина кисти |
| `editor/VideoEditorActivity.kt` | Экран редактирования видео: обрезка (trim) перед отправкой |
| `editor/MediaEditorPagerAdapter.kt` | ViewPager-адаптер для редактирования нескольких вложений подряд |
| `editor/DrawingOverlayView.kt` | View рисования поверх изображения (свободная кисть) |
| `editor/ColorPaletteView.kt` | Палитра выбора цвета кисти |
| `editor/BrushSizeSliderView.kt` | Слайдер толщины кисти |
| `editor/VideoTrimmerView.kt` | View обрезки видео — таймлайн с ручками начала/конца |
| `editor/EditedVideoSpec.kt` | Data class параметров перекодированного видео; `VideoEditCache` — временное хранилище результата |
| `editor/MediaEditCache.kt` | Singleton временного хранилища отредактированных изображений между экранами |

---

## Очередь отправки медиа (`send/`)

Foreground-загрузка вложений с прогрессом, включая перекодирование видео.

| Файл | Роль |
|------|------|
| `send/MediaSendService.kt` | Foreground `Service` очереди отправки: перекодирование видео при необходимости, загрузка в Files, отправка сообщения. `UploadState` (PREPARING/UPLOADING/SENDING/SENT/FAILED), `UploadEvent` |
| `send/SendJob.kt` | Data class задачи отправки (вложения, текст, chat/user назначение); `SendPayloadCache` — временное хранилище payload |
| `send/MediaSendNotification.kt` | Уведомление о прогрессе отправки (progress bar в шторке) |

---

## Системная интеграция Share (`share/`)

Приём контента, расшаренного из других приложений (`ACTION_SEND`).

| Файл | Роль |
|------|------|
| `share/ShareReceiverActivity.kt` | Точка входа `ACTION_SEND`/`ACTION_SEND_MULTIPLE` — принимает shared-контент из других приложений |
| `share/ShareConfirmBottomSheet.kt` | Выбор чата и подтверждение отправки расшаренного контента |
| `share/SharePayload.kt` | Data class нормализованного shared-контента (текст/изображения/файлы) |

---

## Виджеты главного экрана (`widget/`)

App Widget с закреплёнными чатами.

| Файл | Роль |
|------|------|
| `widget/PinnedChatsWidgetProvider.kt` | `AppWidgetProvider` — жизненный цикл виджета (обновление, удаление) |
| `widget/WidgetConfigureActivity.kt` | Экран настройки виджета при добавлении на главный экран |
| `widget/WidgetConfig.kt` | Data class конфигурации конкретного экземпляра виджета |
| `widget/WidgetRepository.kt` | Хранилище конфигураций виджетов (какие чаты закреплены) |
| `widget/WidgetRenderer.kt` | Рендер `RemoteViews` виджета из данных чатов |
| `widget/WidgetUpdater.kt` | Триггер обновления всех экземпляров виджета (после новых сообщений и т.п.) |
| `widget/WidgetRefreshWorker.kt` | `WorkManager`-воркер периодического фонового обновления виджета |

---

## E2E-криптография (`crypto/` — в двух модулях)

Дополняет [[Android]] раздел про шифрование (приватные чаты — Argon2id passphrase, секретные — Signal Double Ratchet).

### `:core` → `crypto/` (примитивы)

| Файл | Роль |
|------|------|
| `crypto/PrivateChatCrypto.kt` | Argon2id-вывод ключа из passphrase, AEAD-шифрование/расшифровка сообщений приватного чата |
| `crypto/BarkFluffSignalStore.kt` | Реализация хранилища identity/prekey/session для libsignal (секретные чаты) |
| `crypto/PrekeyManager.kt` | Генерация и ротация X3DH prekey bundle, пополнение one-time prekeys |

### `app` → `crypto/` (оркестрация)

| Файл | Роль |
|------|------|
| `crypto/E2EBootstrap.kt` | Инициализация prekey bundle устройства (X3DH) при первом использовании E2E-чатов (вызывает `PrekeyManager` из `:core`) |
| `crypto/EncryptedInviteHandler.kt` | Обработка входящих приглашений приватных/секретных чатов, вывод ключей |

---

## Диалоги (`dialog/`)

| Файл | Роль |
|------|------|
| `dialog/ForwardChatPickerBottomSheet.kt` | BottomSheet выбора чата для пересылки сообщения (`ForwardChatPickerAdapter`) |

---

## Утилиты (`utils/` — в двух модулях)

Часть утилит (файловые кэши, версия, сеть) вынесена в `:core` для переиспользования между V1 и V2; UI-специфичные (загрузка изображений, анимации, клавиатура) остались в `app`.

| Файл | Модуль | Роль |
|------|--------|------|
| `utils/AvatarLoader.kt` | app | Singleton; загрузка и кеширование аватаров через Coil; fallback с инициалами на круге |
| `utils/FileCache.kt` | `:core` | Singleton; дисковый кэш медиафайлов (`cacheDir/media_files/`) |
| `utils/FileUrlCache.kt` | `:core` | Персистентный кэш `fileId → downloadUrl` (SharedPreferences); восстанавливается между сессиями |
| `utils/FileSaveUtils.kt` | `:core` | Сохранение полученных файлов в публичное хранилище устройства (MediaStore/Downloads) |
| `utils/AudioPlayerHelper.kt` | `:core` | Singleton MediaPlayer; один аудиофайл за раз; коллбэки прогресса и состояния |
| `utils/StickerCache.kt` | app | Singleton; кэш стикер-паков и изображений |
| `utils/ImageCache.kt` | `:core` | In-memory LRU-кэш битмапов |
| `utils/ImageLoadHelper.kt` | app | Хелпер загрузки изображений-вложений (preview + full) для MessageAdapter |
| `utils/ImageCompressor.kt` | `:core` | Сжимает изображения перед отправкой (max разрешение → JPEG) |
| `utils/KeyboardHeightTracker.kt` | app | Отслеживает высоту клавиатуры через WindowInsetsCompat для анимации sticker panel |
| `utils/MessageItemAnimator.kt` | app | Кастомный RecyclerView.ItemAnimator — плавное появление новых сообщений |
| `utils/MessageTimeSpacingDecoration.kt` | app | RecyclerView.ItemDecoration; увеличенный отступ между группами сообщений с разрывом >10 мин |
| `utils/NetworkUtils.kt` | `:core` | Получает внешний IP устройства через HTTP |
| `utils/UpdateChecker.kt` | app | Проверяет наличие обновлений приложения |
| `utils/AppVersionUtil.kt` | `:core` | Читает `versionName` из PackageInfo |
| `utils/FirebaseTokenHelper.kt` | app | Получает FCM-токен и регистрирует на сервере; логика обновления токена |
| `utils/LogoutHelper.kt` | app | Централизованный выход: серверный Logout gRPC, удаление FCM-токена, очистка кэшей и прокси на LoginActivity |
| `utils/LocaleManager.kt` | app | Применение выбранного языка приложения (`LanguageSettingsActivity`) через `Configuration`/`AppCompatDelegate` |
| `utils/ServerInfoPrefs.kt` | app | Персистентное хранилище последнего `GetServerInfo` от Beacon (адреса сервисов, `livekitUrl`) |
| `utils/SpringPress.kt` | app | Пружинистый эффект нажатия для UI-элементов (scale-анимация) |
| `utils/AudioWaveformExtractor.kt` | app | Извлекает форму волны (waveform) из аудиофайла для голосовых сообщений |
| `utils/OnlineTimeFormatter.kt` | app | Форматирование времени последней активности пользователя («был(а) в сети …») |
| `utils/OtpCellsHelper.kt` | app | Хелпер для UI-ячеек ввода OTP-кода |

---

## Views (`views/`, `view/`)

| Файл | Роль |
|------|------|
| `view/AvatarView.kt` | Кастомный View: круглый аватар с индикатором онлайна |
| `views/SquareImageView.kt` | AppCompatImageView с height=width (квадрат); используется в ImageGridAdapter |
| `views/AspectRatioImageView.kt` | AppCompatImageView с height=width*3/2 (2:3); превью фонов чата |
| `views/ScannerOverlayView.kt` | Кастомный View: полупрозрачный оверлей с угловыми маркерами для QR-сканера |
| `view/VoiceWaveformView.kt` | Кастомный View: отрисовка формы волны голосового сообщения (интерактивный прогресс) |

---

## Уведомления (`notifications/`)

| Файл | Роль |
|------|------|
| `notifications/NotificationHelper.kt` | Создаёт каналы Android и показывает MessagingStyle-уведомления; дедупликация по messageId |
| `notifications/BarkFluffFirebaseMessagingService.kt` | FirebaseMessagingService; обрабатывает push когда приложение убито; обновляет FCM-токен |
| `notifications/MarkAsReadReceiver.kt` | BroadcastReceiver; обрабатывает action «Прочитано» из уведомления → RealtimeService.markAsRead() |
| `notifications/RealtimeSideEffectsImpl.kt` | Реализация интерфейса `RealtimeSideEffects` (объявлен в `grpc/`): уведомления (`NotificationHelper`), загрузка аватара/превью (Coil), обновление виджетов (`WidgetUpdater`) — отделяет `RealtimeService` от прямых UI-побочных эффектов |

---

## Deep Links (`deeplink/`)

| Файл | Роль |
|------|------|
| `deeplink/DeepLinkHandler.kt` | Парсит URI `bf://` / `bfdev://`; возвращает `DeepLinkCommand` (OpenUserChat / Unknown) |

---

## Picker (`picker/`)

| Файл | Роль |
|------|------|
| `picker/ImagePickerBottomSheet.kt` | BottomSheetDialogFragment; выбор медиа из галереи, камеры, файлов для отправки в ChatActivity |

---

## Proto-файлы (`core/src/main/proto/`) — 13 файлов

| Файл | Сервис |
|------|--------|
| `beacon_api.proto` | Информация о сервере и endpoint'ах (включая `livekit_url`, сервис `calls`) |
| `calls_api.proto` | Звонки: инициация, приём/отклонение/завершение, подписка на события, история, качество голоса |
| `configuration_api.proto` | Централизованная конфигурация |
| `developers_api.proto` | Портал документации для разработчиков |
| `fast_auth_api.proto` | QR-авторизация (FastAuth) |
| `files_api.proto` | Загрузка/скачивание файлов, URL, preview |
| `identity_api.proto` | Auth, JWT, 2FA, сессии, сброс пароля |
| `messages_api.proto` | Сообщения, чаты, read receipts |
| `navigator_api.proto` | Список публичных серверов |
| `onliner_api.proto` | Онлайн-статусы, ping, подписка |
| `shared.proto` | Общие типы: Message, MessageAttachmentType (AUDIO=5), Attachment |
| `updates_api.proto` | Streaming: NewMessageEvent, MessageReadEvent |
| `users_api.proto` | Профили, поиск, контакты, бейджи, приватность |

---

> Последнее обновление: 2026-07-04
