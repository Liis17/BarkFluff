# BarkFluff Android — Индекс файлов проекта

> Компактный справочник всех Kotlin-файлов Android-клиента с кратким описанием роли каждого.
> Исходники: `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/`
> Детальное описание классов и связей: [[Android-ProjectMap]]
> Общее описание клиента: [[Android]]

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
| `MainActivity.kt` | Главный экран; BottomNavigation с 3 табами (Контакты, Чаты, Профиль); обрабатывает deep links и уведомления |
| `ChatsFragment.kt` | Список чатов; подписывается на RealtimeService; обновляется при новых сообщениях |
| `ContactsFragment.kt` | Список контактов (друзей); открывает чат по клику |
| `ProfileFragment.kt` | Профиль пользователя + меню настроек + постер профиля; кнопка выхода (LogoutHelper) |
| `ChatActivity.kt` | Экран переписки; пагинация сообщений, вложения всех типов, стикер-панель, фон чата, блюр |
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
| `WelcomeActivity.kt` | Приветственный экран; запрашивает разрешения |

---

## gRPC слой (`grpc/`)

| Файл | Роль |
|------|------|
| `grpc/GrpcManager.kt` | Центральный менеджер всех gRPC-клиентов и stub'ов; инициализация каналов, методы API (auth, chats, users, files, messages, online) |
| `grpc/RealtimeService.kt` | gRPC streaming: подписки на новые сообщения, read receipts, онлайн-статусы; exponential backoff; ping-loop |
| `grpc/AuthInterceptor.kt` | ClientInterceptor; добавляет заголовок `x-auth-token` к каждому gRPC-запросу |
| `grpc/DeviceInfoInterceptor.kt` | ClientInterceptor; добавляет заголовки устройства (deviceId, OS, appName, IP) base64-encoded |

---

## Data (`data/`)

| Файл | Роль |
|------|------|
| `data/GlobalParam.kt` | Хранилище состояния сессии (SharedPreferences + EncryptedSharedPreferences): токены, адреса серверов, данные юзера, настройки персонализации |
| `data/OpenChatManager.kt` | Синглтон; хранит ID текущего открытого чата для подавления дублирующих уведомлений |
| `data/ServerDataElement.kt` | Data class элемента списка серверов (ip, title, userCount, location, цвет) |
| `data/ClientColors.kt` | Data class цветовой темы сервера (liteHex, mainHex, hardHex) |

---

## Repository (`repository/`)

| Файл | Роль |
|------|------|
| `repository/ChatRepository.kt` | Инкапсулирует Messages API: загрузка сообщений с пагинацией, отправка, скачивание файлов в FileCache |

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

---

## Утилиты (`utils/`)

| Файл | Роль |
|------|------|
| `utils/AvatarLoader.kt` | Singleton; загрузка и кеширование аватаров через Coil; fallback с инициалами на круге |
| `utils/FileCache.kt` | Singleton; дисковый кэш медиафайлов (`cacheDir/media_files/`) |
| `utils/FileUrlCache.kt` | Персистентный кэш `fileId → downloadUrl` (SharedPreferences); восстанавливается между сессиями |
| `utils/AudioPlayerHelper.kt` | Singleton MediaPlayer; один аудиофайл за раз; коллбэки прогресса и состояния |
| `utils/StickerCache.kt` | Singleton; кэш стикер-паков и изображений |
| `utils/ImageCache.kt` | In-memory LRU-кэш битмапов |
| `utils/ImageLoadHelper.kt` | Хелпер загрузки изображений-вложений (preview + full) для MessageAdapter |
| `utils/ImageCompressor.kt` | Сжимает изображения перед отправкой (max разрешение → JPEG) |
| `utils/KeyboardHeightTracker.kt` | Отслеживает высоту клавиатуры через WindowInsetsCompat для анимации sticker panel |
| `utils/MessageItemAnimator.kt` | Кастомный RecyclerView.ItemAnimator — плавное появление новых сообщений |
| `utils/MessageTimeSpacingDecoration.kt` | RecyclerView.ItemDecoration; увеличенный отступ между группами сообщений с разрывом >10 мин |
| `utils/NetworkUtils.kt` | Получает внешний IP устройства через HTTP |
| `utils/UpdateChecker.kt` | Проверяет наличие обновлений приложения |
| `utils/AppVersionUtil.kt` | Читает `versionName` из PackageInfo |
| `utils/FirebaseTokenHelper.kt` | Получает FCM-токен и регистрирует на сервере; логика обновления токена |
| `utils/LogoutHelper.kt` | Централизованный выход: серверный Logout gRPC, удаление FCM-токена, очистка кэшей и прокси на LoginActivity |

---

## Views (`views/`, `view/`)

| Файл | Роль |
|------|------|
| `view/AvatarView.kt` | Кастомный View: круглый аватар с индикатором онлайна |
| `views/SquareImageView.kt` | AppCompatImageView с height=width (квадрат); используется в ImageGridAdapter |
| `views/AspectRatioImageView.kt` | AppCompatImageView с height=width*3/2 (2:3); превью фонов чата |
| `views/ScannerOverlayView.kt` | Кастомный View: полупрозрачный оверлей с угловыми маркерами для QR-сканера |

---

## Уведомления (`notifications/`)

| Файл | Роль |
|------|------|
| `notifications/NotificationHelper.kt` | Создаёт каналы Android и показывает MessagingStyle-уведомления; дедупликация по messageId |
| `notifications/BarkFluffFirebaseMessagingService.kt` | FirebaseMessagingService; обрабатывает push когда приложение убито; обновляет FCM-токен |
| `notifications/MarkAsReadReceiver.kt` | BroadcastReceiver; обрабатывает action «Прочитано» из уведомления → RealtimeService.markAsRead() |

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

## Proto-файлы (`app/src/main/proto/`)

| Файл | Сервис |
|------|--------|
| `beacon_api.proto` | Информация о сервере и endpoint'ах |
| `configuration_api.proto` | Централизованная конфигурация |
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

> Последнее обновление: 2026-05-30
