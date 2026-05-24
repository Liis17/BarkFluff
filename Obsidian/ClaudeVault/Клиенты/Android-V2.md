# Android V2 — Jetpack Compose + Material 3 Expressive

Второй вариант Android-клиента: UI переписан с нуля на **Jetpack Compose** строго по [Material 3 Expressive](../../../docs/material-3-expressive-guidelines.md). Вся не-UI логика (gRPC, репозитории, крипто, proto, хранилище) переиспользуется из общего модуля `:core` без изменений. Старый клиент — [[Android]] (Views/XML).

## Топология сборки (единый Gradle-рут)

Оба Android-клиента теперь в ОДНОЙ Gradle-сборке с корнем `Android/`:

```
Android/
  settings.gradle.kts        # include(:core, :app-v1, :app-v2)
  build.gradle.kts           # плагины apply false (включая kotlin-compose)
  gradle/libs.versions.toml  # единый каталог версий
  core/                      # общий не-UI слой + proto
  Barkfluff.Client.Android/app   → :app-v1  (V1, Views/XML)
  Barkfluff.ClientV2.Android/app → :app-v2  (V2, Compose)
```

- Старые `settings.gradle.kts` обоих приложений переименованы в `.bak`.
- **Тулчейн:** AGP 8.9.1, Gradle 9.2.1, **Kotlin 2.2.20** (поднят с 2.0.0 ради современного Compose-compiler), Java 17.
- **Сборка только с JDK 17+:** `JAVA_HOME` = JBR Android Studio (JDK 21). Из `Android/`:
  `./gradlew :core:assembleDebug :app-v1:assembleDebug :app-v2:assembleDebug`

## Модуль `:core`

`com.android.library`, namespace `com.barkfluff.client.core`, minSdk 31. Пакеты сохранили имена `com.barkfluff.client.*` (V1-код не правит импорты). Содержит: `grpc/` (GrpcManager, AuthInterceptor, DeviceInfoInterceptor, RealtimeService), `data/` (GlobalParam, ClientColors, ServerDataElement, OpenChatManager), `repository/` (Chat/Private/Secret), `crypto/` (BarkFluffSignalStore, PrekeyManager, PrivateChatCrypto), чистые `utils/` (FileCache, ImageCompressor, FileUrlCache, ImageCache, NetworkUtils, AudioPlayerHelper, FileSaveUtils, AppVersionUtil), `proto/` (11 .proto + protobuf-плагин, режим lite). `api(libsignal-android)`, `consumer-rules.pro` с keep-правилами.

**Развязка границы:** `RealtimeService` больше не зависит от UI/Notification/Widget — введён интерфейс `RealtimeSideEffects` (onChatChanged / dismissChatNotifications / showMessageNotification). Реализация `RealtimeSideEffectsImpl` живёт в app-слое каждого клиента (в V1 — пакет `notifications/`, грузит уведомления через NotificationHelper + AvatarLoader/Coil). V2 на MVP передаёт `null`.

**Осталось в app (НЕ в core):** `EncryptedInviteHandler`, `E2EBootstrap`, `StickerCache`, View-coupled utils (AvatarLoader, LocaleManager, SpringPress, ImageLoadHelper, FirebaseTokenHelper, LogoutHelper, UpdateChecker) — зависят от Activity/Application.

## Архитектура V2 (`:app-v2`)

- Пакет `com.barkfluff.clientv2`, minSdk 35, Java 17, зависит от `:core`.
- **Стек:** Compose BOM 2025.10.01, material3, navigation-compose, lifecycle-viewmodel/runtime-compose, activity-compose, coil-compose, material-icons-core.
- **DI:** ручной (`di/AppContainer` создаётся в `BarkFluffV2Application`, прокидывается через `LocalAppContainer`; хелпер `appViewModel { }`). Без Hilt.
- **Паттерн:** MVVM + `StateFlow` (экран читает через `collectAsStateWithLifecycle`), ViewModel вызывает репозитории `:core` и подписывается на `RealtimeService` SharedFlow.
- **Навигация:** navigation-compose — `Splash → Welcome → SelectServer → Login → Home(Chats/Profile) → Chat`, плюс `EditProfile` и `Settings` как отдельные маршруты.

## Тема M3 Expressive (`ui/theme/`)

Стандартный `MaterialTheme` с брендовыми expressive-токенами (seed **#FF6B35**):
- `Color.kt` — light/dark схемы из палитры V1 + `BfExtendedColors` (success/warning) через CompositionLocal.
- `Type.kt` — Typography с emphasized-весами на display/headline/title.
- `Shape.kt` — расширенные скругления (medium 16dp, large 24dp, extraLarge 32dp).
- `Motion.kt` — `BfMotion` spring-спеки (spatial/effect/bouncy).
- `Theme.kt` — `BarkFluffTheme(darkTheme, dynamicColor)`, dynamic color (Material You) всегда доступен на minSdk 35. Параметры темы берутся из `SettingsStore` (см. ниже): `MainActivity` collect'ит `themeMode` (SYSTEM/LIGHT/DARK) и `dynamicColor` → мгновенная перекраска при смене в настройках.

> ⚠️ `MaterialExpressiveTheme`/`expressiveLightColorScheme` в material3 1.4.x (BOM 2025.10.01) помечены `internal` — недоступны. Поэтому expressive реализован принципами (цвет/форма/размер/spring-движение) поверх обычного `MaterialTheme`. При переходе на material3, где эти API публичны, можно заменить.

## MVP-экраны (`ui/screens/`)

| Экран | Логика / `:core` |
|-------|------------------|
| `splash` (StartupViewModel) | Роутинг как V1 SplashActivity: нет сервера→Welcome, нет токенов→Login, иначе refresh+getCurrentUserData→Home |
| `welcome` | Hero + кнопка «Начать» |
| `server` (SelectServer) | Navigator `getServerList`, Beacon `getServerInfo` → запись socket-адресов в GlobalParam |
| `login` (+OTP) | `GrpcManager.auth` → AuthResult.{Success, OtpRequired, Error}; коды ошибок OTP/INVALID_LOGIN |
| `home` | Bottom NavigationBar: Чаты (`getChats` + realtime) и Профиль (данные + выход) |
| `chat` | `ChatRepository`: loadMessages/sendMessage/markAsRead, отправка фото (PickVisualMedia → uploadFile), realtime newMessages |
| `search` | Поиск пользователей (`searchUsers`, debounce ≥3 симв.) → `getPersonChatId` → переход в чат; вход — иконка поиска в TopAppBar списка чатов |
| `ui/components/BfAvatar` | Круг с инициалами (цвет по имени) |

## Статус

Все три модуля собираются (`assembleDebug`), `app-v2-debug.apk` генерируется. На устройстве (Pixel 8 Pro) проверены: выбор сервера, авторизация, список чатов. UI V1 не тронут (точечно: фикс smart-cast в `ChatAdapter`, `BarkFluffApplication` передаёт `RealtimeSideEffectsImpl`).

**Реализовано сверх MVP:**
- **Поиск пользователей + старт чата** (`ui/screens/search`): иконка поиска в списке чатов → `searchUsers` (debounce ≥3) → `getPersonChatId` → чат.
- **Действия с сообщениями** (long-press → ModalBottomSheet): копировать, редактировать/удалить (своё), **ответить** (reply через `forwardedMessageId` в тот же чат), **переслать** (forward в другой чат — выбор из списка чатов). Рендер цитаты из `FORWARDED_MESSAGE`-вложения, пометка «· ред.», realtime `messageEdited`/`messageDeleted`.

> В протоколе нет отдельного reply — и ответ, и пересылка идут через `OutgoingMessage.forwarded_message_id`; reply = пересылка в тот же чат, forward = в другой.

- **Просмотрщик изображений** (`ui/components/ImageViewer`): тап по фото в чате → полноэкранный `Dialog` с `HorizontalPager` между фото сообщения + pinch-to-zoom; полный файл резолвится через `getFileDownloadUrl`, до загрузки — `previewUrl`.
- **Видео** (`ui/components/VideoPlayer`): видео-вложения в чате рендерятся как превью + ▶; тап → полноэкранный плеер на ExoPlayer/media3 (`getFileDownloadUrl` → `MediaItem`, `PlayerView` во `AndroidView`). Добавлены зависимости media3-exoplayer/ui в `:app-v2`.
- **Аудио** (`ui/components/AudioAttachment`): вложения AUDIO/VOICE — круглая кнопка play/pause + перематываемый `Slider` + таймер. Файл качается через `ChatRepository.downloadFile` (кэш в `FileCache`), играет синглтон `AudioPlayerHelper` из `:core` (один файл одновременно); перепривязка callbacks через `swapCallbacks` при возврате на экран.
- **Документы** (`ui/components/DocumentAttachment`): вложения DOCUMENT — плитка с расширением файла + имя/размер; тап качает (кэш) и открывает системным выбором через `FileProvider`. Добавлены `<provider>` в манифест `:app-v2` + `res/xml/file_paths.xml` (`media_files/`). `FileCache.init` вызывается в `AppContainer`.
- **Профиль + настройки** (`ui/screens/profile`, `ui/screens/settings`):
  - `ProfileScreen` (вкладка Профиль): `getCurrentUserData` → аватар (`BfAvatar` теперь принимает `imageUrl`, Coil), имя/@username/bio/дата регистрации; синк в `GlobalParam`; rows «Редактировать профиль»/«Настройки»/«Выйти». Обновляется при каждом входе (`LaunchedEffect`).
  - `EditProfileScreen`: правка имени/фамилии/username/bio + смена аватара (PickVisualMedia → `ImageCompressor` → `uploadUserAvatar` → `setProfilePicture`). Username — debounce 500мс + `checkUsername` (свободно/занято/невалидно). Сохранение через `changeName`/`changeUsername`/`changeBio`, апдейт `GlobalParam`.
  - `SettingsScreen`: режим темы (Системная/Светлая/Тёмная) + переключатель «Динамические цвета», версия приложения. Хранение — `di/SettingsStore` (собственные SharedPreferences, `StateFlow`), читается в `MainActivity`.

**Дальше:** медиа (камера, GIF), настройки приватности/безопасности/языка (`getPrivacySettings`/2FA/смена пароля — RPC в `:core`), стикеры, папки, pinned-сообщения, регистрация/восстановление, E2E (приватные/секретные чаты), системная интеграция (уведомления/FCM, виджеты, share, deeplink, локализации). E2E-проверка логина/отправки — на устройстве arm64 (libsignal).
