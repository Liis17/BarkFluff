# BarkFluff Android Client — Карта проекта

> Версия: 2026-04-11. Этот файл предназначен для агентов/AI-сессий — позволяет понять архитектуру без полного перечитывания исходников.

---

## Общая информация

| Параметр | Значение |
|---|---|
| Пакет | `com.barkfluff.client` |
| Язык | Kotlin 2.0.0 |
| AGP | 8.9.1 |
| Min SDK | 26 |
| Target SDK | 35 |
| gRPC | `grpc-okhttp` 1.60.0 (НЕ grpc-netty) |
| Image loading | Coil |
| Video | ExoPlayer (media3-exoplayer 1.3.1) |
| Архитектура | Activity-based, ViewBinding, без MVVM/Hilt |
| Хранилище токенов | `EncryptedSharedPreferences` (`barkfluff_secure_prefs`) |
| Обычное хранилище | `SharedPreferences` (`barkfluff_prefs`) |
| FCM | Firebase Cloud Messaging (push-уведомления) |
| Deep links | Схема `bf://` и `bfdev://` |

---

## Навигационный граф (Activity flow)

```
SplashActivity (точка входа)
    │
    ├─► WelcomeActivity ──► SelectServerActivity ──► LoginActivity
    │     (нет сервера)       (выбор/ввод адреса)      (auth)
    │                                                      │
    │                                                      ├─► RegisterActivity (9 шагов)
    │                                                      └─► ResetPasswordActivity
    │
    └─► MainActivity (есть токен)
          ├─ [tab 0] ContactsFragment
          ├─ [tab 1] ChatsFragment  ←── (default tab)
          └─ [tab 2] ProfileFragment
                          │
                          ├─► AccountSettingsActivity
                          ├─► SecuritySettingsActivity
                          ├─► NotificationSettingsActivity
                          ├─► StorageSettingsActivity
                          ├─► DevicesActivity
                          ├─► AboutActivity
                          └─► UpdateActivity

ChatsFragment / ContactsFragment
    └─► ChatActivity (отдельный экран чата)
              ├─► ImageViewerActivity (просмотр фото)
              └─► MediaViewerActivity (просмотр видео, ExoPlayer)

SearchActivity (из ChatsFragment по кнопке поиска)
DeepLinkActivity (перехват bf:// ссылок)
```

---

## Файлы — исходный код Kotlin

### `BarkFluffApplication.kt`
**Расположение:** `java/com/barkfluff/client/`
**Тип:** Application

Точка старта всего процесса. Инициализирует:
- `GrpcManager` (singleton на уровне app)
- `RealtimeService` (singleton, стримы gRPC)
- `AvatarLoader.initializeCache()` — URL-кэш аватаров
- `FileCache.init()` — дисковый кэш медиафайлов
- `StickerCache.init()` — кэш стикеров
- `NotificationHelper.createChannels()` — каналы уведомлений Android
- `DynamicColors` (Material You, Android 12+)

Подписывается на `ProcessLifecycleOwner` → при уходе в фон вызывает `realtimeService.pause()`, при возврате — `resume()`. Выставляет флаг `cameFromBackground = true`.

**Связи:** Все Activity/Fragment берут `GrpcManager` и `RealtimeService` через `applicationContext as BarkFluffApplication`.

---

### `SplashActivity.kt`
**Тип:** AppCompatActivity — экран-заставка / роутер

Логика при старте:
1. Нет `socketBeacon` / `socketIdentity` → `WelcomeActivity`
2. Нет `refreshToken` → `LoginActivity`
3. Есть `refreshToken`, но access-токен просрочен → пробует обновить через `GrpcManager.refreshAccessToken()`
   - Успех → `MainActivity`
   - Ошибка → `LoginActivity`
4. Все токены валидны → загружает данные текущего юзера и → `MainActivity`

Обрабатывает `pendingChatId` (если открыт из уведомления при убитом приложении).

**Связи:** `GlobalParam`, `GrpcManager`, `WelcomeActivity`, `LoginActivity`, `MainActivity`

---

### `WelcomeActivity.kt`
**Тип:** AppCompatActivity

Приветственный экран (первый запуск). Показывает логотип, кнопку "Начать" → `SelectServerActivity`. Запрашивает разрешения (хранилище, уведомления, медиа) для разных версий Android.

**Связи:** `SelectServerActivity`

---

### `SelectServerActivity.kt`
**Тип:** AppCompatActivity

Выбор сервера. Загружает список серверов через `GrpcManager.getServerList()` (Navigator API). При ручном вводе адреса — валидирует формат `host:port`. При выборе:
1. Создаёт Beacon-клиент → запрашивает `getServerInfo()`
2. Сохраняет все endpoint-адреса в `GlobalParam` (`socketIdentity`, `socketUsers`, `socketFiles`, `socketMessages`, `socketUpdates`, `socketOnliner`)
3. → `LoginActivity`

**Связи:** `GlobalParam`, `GrpcManager`, `ServerAdapter`, `LoginActivity`

---

### `LoginActivity.kt`
**Тип:** AppCompatActivity

Авторизация. Поддерживает логин по username ИЛИ email (поле `oneof` в proto). Поддерживает 2FA (OTP) — при ответе `OtpRequired` переключается на режим ввода 6-значного кода (6 отдельных EditText, автопереход и автоотправка). После успешного входа сохраняет токены в `GlobalParam`, загружает данные юзера → `MainActivity`.

Error codes (из gRPC trailer `x-error-code`):
- `OtpCodeNeedException` → показывает OTP-форму
- `NotValidOtpCodeException` → ошибка "неверный код"
- `InvalidLoginOrPasswordException` → ошибка "неверный логин/пароль"

**Связи:** `GlobalParam`, `GrpcManager`, `GrpcManager.AuthResult`, `MainActivity`, `RegisterActivity`, `ResetPasswordActivity`

---

### `RegisterActivity.kt`
**Тип:** AppCompatActivity

Регистрация в 9 шагов (ViewFlipper / ручное переключение layouts):
1. `step_register_01_name.xml` — имя и фамилия
2. `step_register_02_username.xml` — логин (проверка занятости через gRPC)
3. `step_register_03_email.xml` — email (создание аккаунта, отправка кода)
4. `step_register_04_verify.xml` — OTP-подтверждение email
5. `step_register_05_password.xml` — пароль
6. `step_register_06_avatar.xml` — аватар (выбор + кроп через uCrop)
7. `step_register_07_bio.xml` — описание профиля
8. `step_register_08_2fa.xml` — настройка 2FA
9. `step_register_09_complete.xml` — завершение

**Связи:** `GlobalParam`, `GrpcManager`, `DeviceInfoInterceptor`, `LoginActivity`

---

### `ResetPasswordActivity.kt`
**Тип:** AppCompatActivity

Сброс пароля через email (запрос кода, подтверждение, новый пароль).

**Связи:** `GrpcManager`, `LoginActivity`

---

### `MainActivity.kt`
**Тип:** AppCompatActivity

Главный экран с BottomNavigationView. 3 таба:
- `TAB_CONTACTS = 0` → `ContactsFragment`
- `TAB_CHATS = 1` → `ChatsFragment` (дефолтный)
- `TAB_PROFILE = 2` → `ProfileFragment`

Фрагменты кешируются в `Map<Int, Fragment>`, при переключении — show/hide (не replace), с анимацией slide.

Обрабатывает интент от уведомления (`EXTRA_CHAT_ID`) — открывает `ChatActivity`. Если gRPC не инициализирован — перенаправляет через `SplashActivity`.

Обрабатывает deep link из `BarkFluffApplication.pendingDeepLink` → ищет юзера → открывает чат.

Проверяет обновления (`UpdateChecker`) → показывает badge на вкладке Profile.

**Связи:** `ChatsFragment`, `ContactsFragment`, `ProfileFragment`, `ChatActivity`, `SplashActivity`, `DeepLinkHandler`, `OpenChatManager`, `UpdateChecker`

---

### `ChatsFragment.kt`
**Тип:** Fragment

Список чатов. Загружает чаты через `GrpcManager`. Подписывается на `RealtimeService.newMessages` → обновляет список при новом сообщении. Обрабатывает `cameFromBackground` — перезагружает список при возврате из фона. Регистрирует Firebase-токен для push-уведомлений. По клику на чат открывает `ChatActivity`.

**Связи:** `GrpcManager`, `RealtimeService`, `ChatAdapter`, `AvatarLoader`, `FirebaseTokenHelper`, `ChatActivity`, `SearchActivity`

---

### `ContactsFragment.kt`
**Тип:** Fragment

Список контактов (друзей/связей). Загружает через Users API. Открывает чат по клику.

**Связи:** `GrpcManager`, `UserAdapter`, `ChatActivity`

---

### `ProfileFragment.kt`
**Тип:** Fragment

Профиль текущего пользователя + меню настроек. Показывает аватар, имя, username. Пункты меню:
- Аккаунт → `AccountSettingsActivity`
- Безопасность → `SecuritySettingsActivity`
- Уведомления → `NotificationSettingsActivity`
- Хранилище → `StorageSettingsActivity`
- Устройства → `DevicesActivity`
- О приложении → `AboutActivity`
- Обновление → `UpdateActivity`
- Выход (logout → очищает `GlobalParam`, → `LoginActivity`)

Показывает update-badge если есть обновление.

**Связи:** `GlobalParam`, `GrpcManager`, `AvatarLoader`, `UpdateChecker`, все Settings-Activity

---

### `ChatActivity.kt`
**Тип:** AppCompatActivity (~1000 строк, основная логика)

Экран переписки. Основные возможности:
- Загрузка сообщений с **пагинацией** (подгрузка вверх/вниз по scroll)
- Поддержка типов вложений: изображения, видео, аудио, документы, стикеры
- **Inline sticker panel** (переключение keyboard ↔ стикер-панель с анимацией, KeyboardHeightTracker)
- Отправка файлов через `ImagePickerBottomSheet` (галерея, камера, файлы)
- Кроп изображений через uCrop
- Clipboard paste (вставка изображений из буфера)
- Показ онлайн-статуса собеседника (через `RealtimeService.onlineStatuses`)
- Read receipts (при входе в чат отмечает сообщения прочитанными)
- Профиль чата (нижний диалог `DialogChatProfileBinding`)
- Контекстное меню сообщения (copy, save, forward)

Хранит состояние:
- `chatId`, `chatTitle`, `chatAvatarFileId`, `isGroupChat`, `otherUserId`
- `firstVisibleMessageId` / `lastVisibleMessageId` для пагинации
- `firstUnreadMessageId` — разделитель непрочитанных
- Списки pending-вложений (`pendingPastedImages`, `pendingStickerUris`, `pendingDocumentUris`)

**Связи:** `GrpcManager`, `RealtimeService`, `ChatRepository`, `MessageAdapter`, `StickerPanelAdapter`, `ImagePickerBottomSheet`, `AvatarLoader`, `ImageCompressor`, `FileCache`, `StickerCache`, `KeyboardHeightTracker`, `MessageItemAnimator`, `OpenChatManager`, `ImageViewerActivity`, `MediaViewerActivity`

---

### `ImageViewerActivity.kt`
**Тип:** AppCompatActivity

Просмотр одного изображения с поддержкой зума (PhotoView или аналог). Swipe-down для закрытия. Кнопка сохранить.

**Связи:** `FileCache`

---

### `MediaViewerActivity.kt`
**Тип:** AppCompatActivity

Просмотр видео через ExoPlayer (media3). Non-fullscreen, swipe-down dismiss.

**Связи:** ExoPlayer (media3-exoplayer)

---

### `SearchActivity.kt`
**Тип:** AppCompatActivity

Поиск пользователей через Users API. По тапу на результат открывает чат.

**Связи:** `GrpcManager`, `UserAdapter`, `ChatActivity`

---

### `DevicesActivity.kt`
**Тип:** AppCompatActivity

Список авторизованных устройств (сессий). Кнопка "завершить сессию" для каждого. Нижний диалог с деталями (`bottom_sheet_device_details.xml`).

**Связи:** `GrpcManager`, `DeviceAdapter`

---

### `AccountSettingsActivity.kt`
**Тип:** AppCompatActivity

Редактирование профиля: имя, фамилия, username, bio, email, аватар (uCrop). Сохранение через Users API.

**Связи:** `GrpcManager`, `GlobalParam`, `AvatarLoader`

---

### `SecuritySettingsActivity.kt`
**Тип:** AppCompatActivity

Смена пароля, управление 2FA.

**Связи:** `GrpcManager`

---

### `NotificationSettingsActivity.kt`
**Тип:** AppCompatActivity

Toggle уведомлений, настройки каналов Android.

**Связи:** `GlobalParam`

---

### `StorageSettingsActivity.kt`
**Тип:** AppCompatActivity

Просмотр и очистка кэша (`FileCache`, `StickerCache`, `ImageCache`). Показывает использование по категориям (легенда с цветными точками `item_storage_legend.xml`).

**Связи:** `FileCache`, `StickerCache`

---

### `UpdateActivity.kt`
**Тип:** AppCompatActivity

Экран обновления приложения. Показывает текущую версию, changelog. Запускает скачивание APK.

**Связи:** `UpdateChecker`, `AppVersionUtil`

---

### `AboutActivity.kt`
**Тип:** AppCompatActivity

"О приложении": версия, лицензии, ссылки.

**Связи:** `AppVersionUtil`

---

### `DeepLinkActivity.kt`
**Тип:** AppCompatActivity

Перехватывает intent с deep link (`bf://` / `bfdev://`). Если приложение уже запущено — передаёт URI в `BarkFluffApplication.pendingDeepLink` и открывает `MainActivity`. Если нет — сохраняет URI и запускает `SplashActivity`.

**Связи:** `BarkFluffApplication`, `DeepLinkHandler`, `MainActivity`, `SplashActivity`

---

## gRPC слой

### `grpc/GrpcManager.kt`
Центральный менеджер всех gRPC-клиентов. **Аналог `WebApiClientManager` из WPF.**

Хранит:
- Каналы: `navigatorChannel`, `beaconChannel`, `identityChannel`, `usersChannel`, `filesChannel`, `messagesChannel`, `updatesChannel`, `onlinerChannel`
- Корутинные stubs: аналогичный набор `*Client`

Ключевые методы:
| Метод | Описание |
|---|---|
| `initAllClients(context, globalParam)` | Инициализирует все клиенты по адресам из GlobalParam (идемпотентно) |
| `recreateAllClients(context, globalParam)` | Принудительное пересоздание (после фона/ошибки) |
| `isInitialized()` | true если identity+users+files+messages != null |
| `createOnlyBeaconClient(address)` | Только Beacon (для SelectServer) |
| `createNavigatorClient()` | Navigator для списка серверов |
| `createIdentityClient(address, ctx?)` | Identity, опционально с interceptors |
| `createUsersClient(address, ctx)` | Users |
| `createFilesClient(address, ctx)` | Files |
| `createMessagesClient(address, ctx)` | Messages |
| `createUpdatesClient(address, ctx)` | Updates (стримы) |
| `createOnlinerClient(address, ctx)` | Onliner |
| `auth(email, username, password, otpCode, ctx)` | Авторизация → `AuthResult` |
| `refreshAccessToken(refreshToken, expiration)` | Обновление токена |
| `getCurrentUserData()` | Данные текущего юзера |
| `getUserData(userId)` | Данные юзера по ID |
| `getServerList()` | Список серверов из Navigator |
| `getServerInfo()` | Информация о сервере из Beacon |
| `getChats()` | Список чатов |
| `getPersonChatId(userId)` | ID личного чата с юзером |
| `searchUsers(query, size)` | Поиск юзеров |
| `markAsRead(messageIds)` | Прочитать сообщения |
| `getFileDownloadUrl(fileId)` | URL для скачивания файла |
| `ensureTokenValid(ctx)` | Проверить/обновить токен |

Interceptors применяются при создании канала:
- Всегда: `AuthInterceptor` (добавляет `x-auth-token`)
- Если `includeDeviceInfo=true`: `DeviceInfoInterceptor` (добавляет `x-device-id`, `x-device-name`, `x-os-name`, `x-app-name`, `x-app-version`, `x-ip-address`)

SSL: использует trust-all X509TrustManager (для серверов с самоподписанными сертификатами).

Error codes из `x-error-code` gRPC trailer:
```
ERROR_OTP_CODE_NEEDED      = "C1576884-12D8-4722-A7EE-9F9789AD1265"
ERROR_NOT_VALID_OTP_CODE   = "803B632C-4457-4B05-9435-9C3DD0F41E00"
ERROR_INVALID_LOGIN_OR_PASS = "21BFB9B5-C377-45D1-9B15-6B7F3432B397"
ERROR_INVALID_OLD_PASSWORD  = "A7E3F1B2-9C4D-4E8A-B5F6-2D1A3C7E9F04"
```

**Связи:** используется во всех Activities/Fragments; `AuthInterceptor`, `DeviceInfoInterceptor`

---

### `grpc/RealtimeService.kt`
Сервис реального времени — подписывается на gRPC streaming. **Аналог `RealtimeUpdateService` + `OnlineStatusService` из WPF.**

Хранится в `BarkFluffApplication`, управляется через `ProcessLifecycleOwner`.

Публичные SharedFlow:
- `newMessages: SharedFlow<NewMessageEvent>` — новые сообщения
- `messagesRead: SharedFlow<MessageReadEvent>` — прочитанные сообщения
- `onlineStatuses: SharedFlow<UserOnlineStatus>` — онлайн-статусы
- `connectionState: StateFlow<ConnectionState>` — DISCONNECTED/CONNECTING/CONNECTED

Методы:
- `resume()` — пересоздаёт scope, запускает стримы (вызывается при возврате из фона)
- `pause()` — отменяет scope (при уходе в фон)
- `shutdown()` — полная остановка (при завершении)
- `changeOnlineSubscription(userIds)` — обновить список юзеров для отслеживания онлайна
- `markAsRead(messageId)` — отметить прочитанным (из BroadcastReceiver)

Внутренние loop'ы:
- `streamWithReconnect` — wrapper с exponential backoff (2s→30s), после 3 ошибок форс-обновляет токен
- `onlinePingLoop` — каждые 3сек шлёт `setOnlineStatus`
- `notificationLoop` — подписывается на `newMessages`, показывает уведомления (кроме текущего открытого чата)

**Связи:** `GrpcManager`, `GlobalParam`, `OpenChatManager`, `NotificationHelper`, `AvatarLoader`

---

### `grpc/AuthInterceptor.kt`
`ClientInterceptor`. Берёт `accessToken` из `GlobalParam` и добавляет заголовок `x-auth-token` к каждому gRPC запросу.

---

### `grpc/DeviceInfoInterceptor.kt`
`ClientInterceptor`. Добавляет обязательные заголовки устройства (base64-encoded):
`x-device-id`, `x-device-name`, `x-os-name`, `x-app-name`, `x-app-version`, `x-ip-address`

---

## Data слой

### `data/GlobalParam.kt`
**Аналог `GlobalParam` из WPF.**

Оболочка над двумя SharedPreferences:
- `barkfluff_prefs` (обычные) — адреса серверов, данные пользователя, настройки
- `barkfluff_secure_prefs` (`EncryptedSharedPreferences`) — access/refresh токены и их сроки

Ключевые поля:
| Поле | Хранилище | Описание |
|---|---|---|
| `socketBeacon/Identity/Users/Files/Messages/Updates/Onliner` | обычное | Адреса gRPC endpoint'ов |
| `serverName/Description` | обычное | Имя/описание сервера |
| `deviceId` | обычное | UUID устройства (генерируется при первом обращении) |
| `ipAddress` | обычное | Внешний IP (загружается при старте) |
| `accessToken / refreshToken` | шифрованное | JWT токены |
| `accessTokenExpiration / refreshTokenExpiration` | шифрованное | Unix timestamp (мс) |
| `userId, userName, firstName, lastName, description` | обычное | Данные профиля |
| `pictureFileId, picturePreviewFileId` | обычное | ID файлов аватара |
| `pictureUrl, picturePreviewUrl, profilePictureUrl` | обычное | URL аватара |
| `notificationsEnabled` | обычное | Флаг уведомлений |
| `firebaseToken` | обычное | FCM токен |

Статические методы: `getDeviceName()`, `getOsVersion()`, `getAppName()`, `getAppVersion(ctx)`, `generateDeviceId()`, `loadIpAddress(prefs)`.

---

### `data/OpenChatManager.kt`
Синглтон-объект (in-memory). Хранит `currentOpenChatId`. Используется для подавления уведомлений если данный чат уже открыт.

Методы: `setOpenChat(id)`, `isOpen(id)`, `closeChat()`, `getCurrentChatId()`.

---

### `data/ServerDataElement.kt`
Data class для элемента списка серверов (ip, title, description, userCount, publicName, location, hexColor).

---

### `data/ClientColors.kt`
Data class: тема сервера (liteHex, mainHex, hardHex).

---

## Repository

### `repository/ChatRepository.kt`
Инкапсулирует Messages API. Получает `GrpcManager` через конструктор.

Методы:
- `loadMessages(chatId, fromMessageId, offsetBefore, offsetAfter, count)` — пагинация (max 50)
- `sendMessage(chatId, text, fileIds)` — отправка сообщения с вложениями
- `downloadFile(fileId, progressCallback)` — скачивание файла через HTTP (URL из Files API), сохранение в `FileCache`

---

## Adapters

### `adapter/MessageAdapter.kt`
`ListAdapter<MessageItem, RecyclerView.ViewHolder>`. 4 типа ViewHolder:
- `VIEW_TYPE_SENT` — отправленное (`item_message_sent.xml`)
- `VIEW_TYPE_RECEIVED` — полученное (`item_message_received.xml`)
- `VIEW_TYPE_DATE_SEPARATOR` — разделитель даты (`item_message_date_separator.xml`)
- `VIEW_TYPE_UNREAD_SEPARATOR` — разделитель непрочитанных

Поддерживает вложения: изображения (grid через `ImageGridAdapter`), видео, аудио (через `AudioPlayerHelper`), документы. Тап на изображение → `ImageViewerActivity`, тап на видео → `MediaViewerActivity`. Контекстное меню (copy, save, download).

Конструктор принимает: `getFileUrl: suspend (String) -> String?`, `downloadToCache: suspend (String) -> File?`, `scope: CoroutineScope`.

**Связи:** `ImageGridAdapter`, `AudioPlayerHelper`, `FileCache`, `AvatarLoader`, `ImageLoadHelper`, `ImageViewerActivity`, `MediaViewerActivity`

---

### `adapter/ChatAdapter.kt`
`ListAdapter<ChatDisplayItem, ChatViewHolder>`. Список чатов на главном экране. Показывает: название чата, последнее сообщение, время, счётчик непрочитанных. Аватар через `AvatarLoader`. Принимает `getFileUrl: suspend (String) -> String?`.

---

### `adapter/UserAdapter.kt`
Список пользователей (контакты, результаты поиска). Показывает аватар, имя, username.

---

### `adapter/DeviceAdapter.kt`
Список устройств/сессий. Показывает иконку типа устройства, имя, дату.

---

### `adapter/ServerAdapter.kt`
Список серверов в `SelectServerActivity`. Показывает название, описание, кол-во пользователей, локацию.

---

### `adapter/ImageGridAdapter.kt`
Сетка изображений в сообщении (квадратные ячейки). Использует `SquareImageView`. Тап → `ImageViewerActivity`.

---

### `adapter/ImagePagerAdapter.kt`
ViewPager для просмотра нескольких изображений в `ImageViewerActivity`.

---

### `adapter/ImagePickerAdapter.kt`
Адаптер для галереи в `ImagePickerBottomSheet`. Показывает миниатюры медиа из MediaStore.

---

### `adapter/AttachmentPreviewAdapter.kt`
Горизонтальная прокрутка предварительного просмотра выбранных вложений в `ChatActivity` перед отправкой.

---

### `adapter/StickerPanelAdapter.kt`
Адаптер для inline sticker panel в `ChatActivity`. Поддерживает типы: заголовок пака, стикер, состояния loading/empty.

---

### `adapter/StickerPanelItem.kt`
Data class: элемент стикер-панели (тип + данные).

---

## Утилиты (`utils/`)

### `utils/AvatarLoader.kt`
Singleton. Загружает и кешируют аватары через Coil. Показывает инициалы на цветном круге если аватар недоступен.

Ключевое:
- `urlCache: ConcurrentHashMap<String, String>` — кэш `fileId → URL` (используется в `RealtimeService` для уведомлений)
- `initializeCache(context)` — загружает `FileUrlCache` с диска
- `getImageLoader(context): ImageLoader` — Coil ImageLoader с trust-all SSL
- `load(context, fileId, getUrl, imageView, textView)` — загружает аватар или показывает инициали

**Связи:** `FileUrlCache`, `GrpcManager` (опосредованно), `RealtimeService`

---

### `utils/FileCache.kt`
Singleton. Дисковый кэш медиафайлов в `cacheDir/media_files/`.

Методы: `init(ctx)`, `hasFile(fileId)`, `getFile(fileId)`, `saveFile(fileId, bytes/stream)`, `deleteFile(fileId)`.

---

### `utils/FileUrlCache.kt`
Персистентный кэш `fileId → downloadUrl` на диске (JSON или SharedPreferences). Используется `AvatarLoader` для восстановления URL-кэша между сессиями.

---

### `utils/AudioPlayerHelper.kt`
Singleton `MediaPlayer`. Гарантирует воспроизведение только одного аудио. Интерфейс `AudioCallbacks` (onProgress, onStateChanged, onError, onComplete). Методы: `play(fileId, file, callbacks)`, `pause()`, `resume()`, `stop()`, `seekTo(ms)`.

---

### `utils/StickerCache.kt`
Singleton. Кэш стикеров (пакеты, изображения). Инициализируется в `BarkFluffApplication`.

---

### `utils/ImageCache.kt`
In-memory LRU кэш битмапов (используется как дополнение к Coil).

---

### `utils/ImageLoadHelper.kt`
Хелпер для загрузки изображений вложений (preview + full). Используется в `MessageAdapter`.

---

### `utils/ImageCompressor.kt`
Сжимает изображения перед отправкой (уменьшает до max разрешения, перекодирует в JPEG).

---

### `utils/KeyboardHeightTracker.kt`
Отслеживает высоту клавиатуры через `WindowInsetsCompat`. Используется в `ChatActivity` для корректной анимации sticker panel (подменяет высоту клавиатуры).

---

### `utils/MessageItemAnimator.kt`
Кастомный `RecyclerView.ItemAnimator` для сообщений — плавное появление новых сообщений.

---

### `utils/NetworkUtils.kt`
Получает внешний IP через HTTP-запрос (api.ipify.org или аналог).

---

### `utils/UpdateChecker.kt`
Проверяет наличие обновлений (запрос к серверу/GitHub). Возвращает `Boolean`. Используется в `MainActivity` и `ProfileFragment`.

---

### `utils/AppVersionUtil.kt`
Читает `versionName` из `PackageInfo`.

---

### `utils/FirebaseTokenHelper.kt`
Получает FCM-токен и регистрирует на сервере через Identity/Users API.

---

## Views

### `view/AvatarView.kt`
Кастомный View — круглый аватар с онлайн-индикатором. Использует `AvatarLoader`.

---

### `views/SquareImageView.kt`
`AppCompatImageView` с `onMeasure` → `height = width`. Используется в `ImageGridAdapter`.

---

## Notifications (`notifications/`)

### `notifications/NotificationHelper.kt`
Object. Создаёт каналы уведомлений Android и показывает уведомления.

Каналы:
- `chat_messages` (IMPORTANCE_HIGH) — новые сообщения
- `system` (IMPORTANCE_DEFAULT) — системные
- `other` (IMPORTANCE_LOW) — прочие

`showMessageNotification(...)` — строит `MessagingStyle` уведомление с аватаром отправителя (BitmapDrawable), Dynamic Shortcut для Android 11+ (чтобы показывалось в разделе "Диалоги"), action "Прочитано". Если у сообщения есть изображение-вложение — `BigPictureStyle`. Если аватар недоступен — генерирует placeholder с инициалами.

Дедупликация по `messageId` (set `recentlyShownMessages`).

**Связи:** `MarkAsReadReceiver`, `MainActivity`

---

### `notifications/BarkFluffFirebaseMessagingService.kt`
`FirebaseMessagingService`. Обрабатывает входящие push-уведомления (когда приложение убито). При новом `RemoteMessage` парсит `chatId`/`messageId` из data-payload, показывает уведомление через `NotificationHelper`. При обновлении FCM-токена → сохраняет в `GlobalParam`, регистрирует на сервере.

**Связи:** `NotificationHelper`, `GlobalParam`, `GrpcManager`

---

### `notifications/MarkAsReadReceiver.kt`
`BroadcastReceiver`. Обрабатывает action "Прочитано" из уведомления. Вызывает `RealtimeService.markAsRead(messageId)`.

**Связи:** `RealtimeService`

---

## Deep Links

### `deeplink/DeepLinkHandler.kt`
Парсит URI вида `bf://user-username=li_is` или `bfdev://user-username=li_is`.

Возвращает `DeepLinkCommand`:
- `OpenUserChat(username: String)` — открыть чат с юзером
- `Unknown` — неизвестная команда

---

## Picker

### `picker/ImagePickerBottomSheet.kt`
`BottomSheetDialogFragment`. Нижняя панель выбора медиа для `ChatActivity`. Показывает:
- Сетку фото/видео из MediaStore (`ImagePickerAdapter`)
- Кнопки: камера, файлы, документы
- Множественный выбор (с отображением счётчика)

Возвращает результат через `ImagePickerResult` (список URI).

**Связи:** `ImagePickerAdapter`, `ChatActivity`

---

## Proto файлы (`app/src/main/proto/`)

| Файл | Описание |
|---|---|
| `beacon_api.proto` | Информация о сервере (эндпоинты, название) |
| `configuration_api.proto` | Централизованная конфигурация |
| `fast_auth_api.proto` | QR-авторизация |
| `files_api.proto` | Загрузка/скачивание файлов, preview, URL |
| `identity_api.proto` | Auth, 2FA, сброс пароля, сессии, токены |
| `messages_api.proto` | Сообщения, чаты, read receipts |
| `navigator_api.proto` | Список публичных серверов |
| `onliner_api.proto` | Online-статусы, ping, подписка |
| `shared.proto` | Общие типы: `Message`, `MessageContent`, `MessageAttachmentType` (IMAGE=1,VIDEO=2,DOCUMENT=3,STICKER=4,AUDIO=5), `Attachment` |
| `updates_api.proto` | Стриминг: `NewMessageEvent`, `MessageReadEvent` |
| `users_api.proto` | Профили, поиск, связи, бейджи |

---

## Ресурсы (краткий обзор)

### Layouts
- `activity_*.xml` — экраны (1:1 к Activity)
- `fragment_chats.xml`, `fragment_contacts.xml`, `fragment_profile.xml` — фрагменты
- `item_message_sent.xml`, `item_message_received.xml` — пузыри сообщений
- `item_attachment_*.xml` — вложения (audio, video, document, image_cell, media_cell)
- `item_chat.xml`, `item_user.xml`, `item_server.xml`, `item_device.xml` — элементы списков
- `item_sticker*.xml` — стикер-панель (sticker, header, loading, empty)
- `step_register_0[1-9]_*.xml` — шаги регистрации
- `dialog_chat_profile.xml` — диалог профиля чата
- `bottom_sheet_image_picker.xml`, `bottom_sheet_device_details.xml`

### Animations (`res/anim/`)
`slide_in_from_left/right`, `slide_out_to_left/right`, `slide_in_left/right`, `slide_out_left/right`, `slide_left/right`, `slide_stay`, `fade_out`, `bottom_sheet_slide_in/out`

### Themes
- `values/themes.xml` — светлая тема (Material3)
- `values-night/themes.xml` — тёмная тема
- `values-v34/themes.xml` — Android 14+
- `values/colors.xml` + `values-night/colors.xml` — цвета

---

## Граф зависимостей (упрощённый)

```
BarkFluffApplication
    ├── GrpcManager  ←── используется везде
    └── RealtimeService ←── подписываются: ChatsFragment, ChatActivity

GlobalParam ←── используется везде

ChatActivity
    ├── ChatRepository
    │     └── GrpcManager.messagesClient
    ├── MessageAdapter
    │     ├── AudioPlayerHelper
    │     ├── FileCache
    │     ├── ImageGridAdapter
    │     └── AvatarLoader
    ├── StickerPanelAdapter
    │     └── StickerCache
    ├── RealtimeService (subscribe: newMessages, messagesRead, onlineStatuses)
    ├── ImagePickerBottomSheet
    └── OpenChatManager

RealtimeService
    ├── GrpcManager.updatesClient (stream newMessages, messagesRead)
    ├── GrpcManager.onlinerClient (stream onlineStatus, setOnlineStatus)
    ├── NotificationHelper
    └── AvatarLoader.urlCache

NotificationHelper
    └── MarkAsReadReceiver → RealtimeService.markAsRead()
```

---

## Известные особенности / ловушки

1. **grpc-okhttp, не grpc-netty** — `OkHttpChannelBuilder` вместо `NettyChannelBuilder`. Метод `MetadataUtils.attachHeaders` недоступен — использовать `ClientInterceptor`.

2. **Trust-all SSL** — оба `GrpcManager` и `AvatarLoader` отключают проверку SSL-сертификатов (для самоподписанных). Это намеренно для dev-серверов.

3. **Токены в `EncryptedSharedPreferences`** — файл `barkfluff_secure_prefs`, ключ `AES256_GCM_SPEC`.

4. **`AuthRequest.oneof login`** — поле login в proto — `oneof` (username ИЛИ email, не оба).

5. **`CreateTokenResponse.access_token`** — field_number=2 (не 1).

6. **Navigator endpoint** — `navigator.barkfluff.com:64646` (plaintext, порт 64646, не 443). `GrpcManager.DEFAULT_NAVIGATOR_URL` = `https://navigator.barkfluff.com:443` — для HTTPS.

7. **`cameFromBackground`** — флаг в `BarkFluffApplication`. После паузы `RealtimeService` не получает события, поэтому при возврате из фона `ChatsFragment` перезагружает список.

8. **`OpenChatManager`** — подавляет уведомления для текущего открытого чата. Нужно вызывать `setOpenChat(chatId)` при входе и `closeChat()` при выходе из `ChatActivity`.

9. **`pendingChatId`** в `MainActivity.Companion` — для cold-start из уведомления (приложение убито → FCM → `SplashActivity` → `MainActivity`).

10. **Пагинация сообщений** — `ChatRepository.loadMessages()` принимает `offsetBefore`/`offsetAfter` относительно `fromMessageId`. При первой загрузке `fromMessageId=0` (последние N сообщений).
