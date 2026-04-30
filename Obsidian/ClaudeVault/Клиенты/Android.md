# BarkFluff.Client.Android

Kotlin + gRPC-OkHttp клиент. Activity-based архитектура.

Расположение: `Android/Barkfluff.Client.Android/`
Package: `com.barkfluff.client`

> Полная карта файлов и внутреннего строения: [[Android-ProjectMap]]
> Индекс всех файлов с кратким описанием роли каждого: [[Android-FileIndex]]
> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)

## Версии

- Kotlin 2.0.0, AGP 8.9.1
- gRPC-OkHttp 1.60.0 (NOT grpc-netty)
- ViewBinding, без Hilt/MVVM

## Архитектура

- Activity-based
- Локальное хранилище: SharedPreferences + EncryptedSharedPreferences для токенов
- Навигация: Welcome → SelectServer → Login → Chats

## UI — Экран списка чатов (MainActivity + ChatsFragment)

- `activity_main.xml`: `fragmentContainer` растянут на весь экран (`toBottomOf="parent"`), нижняя навигация (`bottomNavCard`) — плавающий элемент поверх, рисуется позже в XML → автоматически выше по z-order.
- `fragment_chats.xml`: `RecyclerView` (`chatRecyclerView`) занимает всё пространство фрагмента.
- `ChatAdapter` добавляет прозрачный **footer-спейсер** (126dp = 1.5 × высота элемента чата ≈ 84dp) в конец списка:
  - `VIEW_TYPE_FOOTER` / `FooterViewHolder` — не требует биндинга.
  - `submitList()` переопределён: `ensureFooter()` удаляет все footer-элементы из списка и добавляет один в конец перед каждой отправкой в DiffUtil.
  - Все методы изменения списка (`updateChatWithNewMessage`, `addNewChat`, `updateReadStatus`) фильтруют footer через `!it.isFooter`.
  - `ChatDiffCallback` корректно сравнивает footer-элементы.
  - Layout: `res/layout/item_chat_footer.xml` — прозрачная `View` высотой 126dp.

- grpc-okhttp 1.60.0 (coroutine stubs)
- `MetadataUtils.attachHeaders` не резолвится в grpc-okhttp 1.60.0 — использовать `ClientInterceptor` напрямую

## Хранение токенов

- EncryptedSharedPreferences (`barkfluff_secure_prefs`)
- При создании gRPC-каналов добавлять `x-auth-token` через interceptor

## Разлогин (LogoutHelper)

`utils/LogoutHelper.kt` — централизованный хелпер полного выхода из аккаунта:
1. Серверный `Logout` gRPC (удаляет refresh-токен в Identity)
2. `FirebaseMessaging.deleteToken()` — деактивирует push на устройстве
3. `globalParam.clearUserData()` + очистка `firebaseToken`
4. Очистка кешей: `AvatarLoader.clearAllCaches()`, `StickerCache.clear()`, `media_files/`
5. Переход на `LoginActivity` с `FLAG_ACTIVITY_NEW_TASK or FLAG_ACTIVITY_CLEAR_TASK`

Вызывается из:
- `ProfileFragment` → кнопка "Выйти"
- `DevicesActivity` → завершение сессии **текущего** устройства (если `deviceId == globalParam.deviceId`)

## Firebase FCM токен

`utils/FirebaseTokenHelper.kt`:
- `getTokenAndSendToServer()` — берёт существующий или запрашивает токен, отправляет на сервер. Используется в **SplashActivity** при старте (уже залогинен).
- `deleteAndRefreshTokenThenSend()` — удаляет старый токен, получает новый, отправляет. Используется в **LoginActivity** после успешного логина (свежий вход).
- `refreshTokenAndSendToServer()` — принудительное обновление токена (вызывается из `BarkFluffFirebaseMessagingService.onNewToken()`).



- `MessageAttachmentType`: Unknown, Image, Video, Gif, Document, Audio(4), Voice, Sticker; **AUDIO=5** (добавлен в shared.proto)

## Соотношение сторон изображений в облачках

`MessageAdapter.buildMediaGrid` вычисляет размер ячеек:
- **Одно изображение**: высота ячейки = `cellWidth * imageHeight / imageWidth` (из `MessageAttachment.image_width` / `image_height`), ограничена диапазоном `[cellWidth/3 .. cellWidth*2]`. Если размеры равны 0 — квадратная ячейка как fallback.
- **Несколько изображений**: всегда квадратные ячейки (стандартная сетка).

Это позволяет облачку принять правильный размер **до** загрузки картинки, исключая прыжок высоты после загрузки.

Поля `image_width` (field 8) и `image_height` (field 9) добавлены в `MessageAttachment` в `shared.proto`.
Backend заполняет эти поля при доставке сообщения с вложением-изображением.
## Система кеширования

Подробная документация: `Android/Barkfluff.Client.Android/docs/CACHING_SYSTEM.md`

Четыре слоя кеша:
1. **Runtime URL-кэш** — `AvatarLoader.urlCache` (`ConcurrentHashMap<fileId, URL>`, in-memory)
2. **Persistent URL-кэш** — `FileUrlCache` → SharedPreferences `"file_url_cache"` (SHA-256 keys)
3. **gRPC-запрос** — `getFileDownloadUrl(fileId)` к Files API
4. **Coil Image Cache** — memory (25% RAM) + disk `cacheDir/image_cache/` (10% storage), key=fileId

Бинарные файлы (аудио/видео/документы):
- `FileCache` (`utils/FileCache.kt`) — singleton disk cache, путь: `cacheDir/media_files/`
- `AudioPlayerHelper` (`utils/AudioPlayerHelper.kt`) — MediaPlayer singleton, один аудио за раз
- `ImageGridAdapter` (`adapter/ImageGridAdapter.kt`) — квадратная сетка с `SquareImageView`
- `SquareImageView` (`views/SquareImageView.kt`) — `onMeasure` устанавливает height=width
- `AspectRatioImageView` (`views/AspectRatioImageView.kt`) — `onMeasure` устанавливает height=width*3/2 (2:3, для превью фонов)
- `MediaViewerActivity` — ExoPlayer, не fullscreen, swipe-down dismiss
- `ImageViewerActivity` — без fullscreen, swipe-down dismiss
- `ChatRepository.downloadFile()` — HTTP download → FileCache

## ExoPlayer

```
androidx.media3:media3-exoplayer:1.3.1
androidx.media3:media3-ui:1.3.1
```

## UI — Экран чата (ChatActivity + activity_chat.xml)

- `activity_chat.xml`: ConstraintLayout, слои по z-order (снизу вверх):
  1. `chatBackgroundImage` — фоновое изображение (ImageView на весь экран)
  2. `chatDimOverlay` — оверлей затенения фона (View, `visibility=gone` при dim=0)
  3. `messagesRecyclerView` + панели кнопок (с elevation)
  4. `stickerPreviewOverlay` — поверх всего при предпросмотре стикера
- **Затенение фона (`chatBackgroundDim`)**: применяется в `ChatActivity.applyDimOverlay()` при старте. Цвет оверлея — `android.R.attr.colorBackground` (фон окна из темы), что автоматически адаптируется к светлой/тёмной теме. Alpha = `dim% / 100 * 255`.
- Аналогичная логика в превью `PersonalizationSettingsActivity.updatePreviewDim()`.

## FastAuth QR-сканер (DevicesActivity → QrScannerActivity → FastAuthConfirmActivity)

Флоу: авторизованный мобильный клиент сканирует QR нового устройства и подтверждает/отклоняет вход.

**Зависимости (app/build.gradle.kts):**
- CameraX 1.4.2: `camera-core`, `camera-camera2`, `camera-lifecycle`, `camera-view`
- ML Kit: `com.google.mlkit:barcode-scanning:17.3.0`

**Файлы:**
- `QrScannerActivity.kt` — CameraX + ML Kit (QR_CODE), при обнаружении QR вызывает `grpcManager.scanFastAuth()`, при успехе переходит в FastAuthConfirmActivity
- `FastAuthConfirmActivity.kt` — отображает метаданные нового устройства (имя, ОС, приложение, IP), кнопки «Подтвердить» / «Отклонить», вызывает `acceptFastAuth` / `rejectFastAuth`
- `views/ScannerOverlayView.kt` — кастомная View с полупрозрачным оверлеем и угловыми уголками (рисуется через Canvas, `LAYER_TYPE_SOFTWARE`)

**GrpcManager — новые поля и методы:**
- `fastAuthClient: FastAuthApiGrpcKt.FastAuthApiCoroutineStub?`
- `createFastAuthClient(address, context, includeDeviceInfo)` — с `AuthInterceptor` + `DeviceInfoInterceptor`
- `suspend fun scanFastAuth(fastAuthId: String): Result<ScanFastAuthResponse>`
- `suspend fun acceptFastAuth(fastAuthId, confirmationCode): Result<Unit>`
- `suspend fun rejectFastAuth(fastAuthId, confirmationCode): Result<Unit>`

**DevicesActivity:**
- `buttonConnectDevice` использует `ActivityResultLauncher<Intent>` → `QrScannerActivity`
- На RESULT_OK — вызывает `loadSessions()` для обновления списка устройств
- Проверяет `CAMERA` permission перед запуском; при отказе — подсказка о настройках

## gRPC Коды ошибок (из x-error-code trailer)

| Исключение | ErrorCode |
|-----------|-----------|
| OtpCodeNeedException | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| NotValidOtpCodeException | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| InvalidLoginOrPasswordException | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |

## Файловая структура

- `gradle/libs.versions.toml` — все версии зависимостей
- `app/src/main/proto/` — 11 proto файлов
- `app/src/main/java/com/barkfluff/client/` — все исходники
- `PROJECT_MAP.md` — полная карта Android-проекта

## Сборка

```bash
cd Android/Barkfluff.Client.Android
./gradlew assembleDebug
```
