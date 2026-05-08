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

## Обработка FCM data-only payload

`BarkFluffFirebaseMessagingService.onMessageReceived` диспатчит payload по полю `type`:

- `type = "new_message"` (по умолчанию, если поле не задано) — строит локальную нотификацию через `NotificationHelper.showMessageNotification` с аватаром и BigPictureStyle при наличии превью.
- `type = "dismiss_chat_notifications"` — вызывает `NotificationHelper.dismissForChat(context, chat_id)`, удаляя нотификацию чата из шторки. Шлётся бекендом ([[Backend/CloudMessaging]] / [[Backend/Updates]]) после прочтения сообщения, чтобы скрыть уведомление на остальных устройствах пользователя. На читавшем устройстве — no-op (нотификации уже нет).



- `MessageAttachmentType`: Unknown, Image, Video, Gif, Document, Audio(4), Voice, Sticker; **AUDIO=5** (добавлен в shared.proto)

## Соотношение сторон изображений в облачках

`MessageAdapter.buildMediaGrid` вычисляет размер ячеек:
- **Одно изображение**: высота ячейки = `cellWidth * imageHeight / imageWidth` (из `MessageAttachment.image_width` / `image_height`), ограничена диапазоном `[cellWidth/3 .. cellWidth*2]`. Если размеры равны 0 — квадратная ячейка как fallback.
- **Несколько изображений**: всегда квадратные ячейки (стандартная сетка).

Это позволяет облачку принять правильный размер **до** загрузки картинки, исключая прыжок высоты после загрузки.

Поля `image_width` (field 8) и `image_height` (field 9) добавлены в `MessageAttachment` в `shared.proto`.
Backend заполняет эти поля при доставке сообщения с вложением-изображением.

## Отправка файлов и pre-upload дедупликация (SHA-256)

Перед заливкой файла на S3 клиент проверяет, не существует ли уже такой файл на сервере по SHA-256-хешу — это экономит мобильный трафик при повторных отправках.

**Цепочка отправки** (`MediaSendService.processJob` → `ChatRepository.uploadFile`):

1. `MediaSendService.prepareAttachment` готовит байты (`PreparedAttachment.bytes`):
   - Документы — читаются как есть (`AttachmentSpec.Document`).
   - Картинки — сжимаются через `ImageCompressor.compressImage` (JPEG q=90, max 2500px по длинной стороне).
   - Видео — обрезка/перекодировка через `Transformer` в MP4.
2. `ChatRepository.uploadFile(jpegImageBytes, fileType, ...)` (`repository/ChatRepository.kt`) ДО получения upload URL:
   - Считает SHA-256 от итоговых байт через приватный extension `ByteArray.sha256Hex()` → lowercase hex, 64 символа.
   - Вызывает `grpcManager.checkFileHash(hash)` → `FilesApi.CheckFileHash` (gRPC, см. [[Backend/Files]] и [[Shared/Proto]]).
   - Если сервер вернул непустой `fileId` — `onProgress(100)` и сразу `Result.success(existingFileId)`, никакого HTTP POST. В Logcat: `File already exists on server (hash=..., reusing fileId: ...)`.
   - Если пустой ответ или ошибка вызова — fallback к обычному multipart-upload через `getUploadUrl` + S3 POST. Серверная пост-дедупликация (`UploadFileCommandHandler`) всё равно вернёт существующий `fileId` в JSON-ответе, если контент совпал.
3. `GrpcManager.checkFileHash(fileHash: String): Result<String>` — обёртка над `filesClient.checkFileHash(CheckFileHashRequest)`. На любом исключении (нет filesClient, gRPC error) возвращает `Result.failure` — вызывающая сторона (`uploadFile`) использует `getOrNull()` и тихо переходит к обычной загрузке.

**Что хешируется:** именно те байты, которые были бы залиты на сервер. Для картинки — уже сжатый JPEG, не оригинал из галереи. Это совпадает с тем, что хеширует backend при загрузке (`Backend/BarkFluff.Files/Features/UploadFile/UploadFileCommandHandler.cs`), поэтому дедупликация работает кросс-клиентно: файл, залитый с macOS-клиента, дедуплицируется при отправке с Android и наоборот.

**Не покрывается этим check'ом** (заливают мимо `ChatRepository.uploadFile`): загрузка аватара через `GrpcManager.uploadAvatar`/`uploadProfilePoster` — там свой путь, дедупликация только на сервере.

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

## Пересылка и ответы (Forward / Reply)

Технически на бэкенде reply и forward — **одно и то же**: оба используют `OutgoingMessage.forwarded_message_id` (см. `messages_api.proto`). Различие чисто визуальное на клиенте.

**Эвристика выбора UI** (в `MessageAdapter.bindMessageQuote`): если `ForwardedMessageAttachment.original_message_id` найден в текущей загруженной истории чата (`hasMessageInCurrentList`), отображается компактный **reply-блок** (вертикальная полоска `?attr/colorPrimary` + автор + 1 строка превью). Иначе — полный **forward-блок** в `MaterialCardView` (с заголовком автора, текстом и медиа-сеткой через `setupAttachmentsContainer`).

Layout цитаты: `view_message_quote.xml` (включается в `item_message_sent.xml` и `item_message_received.xml` через `<include android:id="@+id/messageQuote">`). Универсальный — переключается между `replyView` / `forwardView` по visibility.

При рендере основного бабла FORWARDED_MESSAGE-вложения **исключаются** из `setupAttachmentsContainer` (фильтр `displayedAttachments`), чтобы не задвоить.

### Ответ (reply) в открытом чате

- **Action menu**: клик по корневому `FrameLayout` строки сообщения (вне bubble — там, где padding 80dp слева/справа) открывает `PopupWindow` (`popup_message_actions.xml`) с пунктами Ответить / Изменить / Удалить / Переслать / Закрепить. Закрепить — заглушка `Toast "Скоро будет"`. Изменить/Удалить — реализованы (см. ниже), скрываются через `View.GONE` для чужих сообщений.
- **Свайп влево**: `ReplySwipeCallback : ItemTouchHelper.SimpleCallback(0, ItemTouchHelper.LEFT)`. При сдвиге `>= 64dp` — haptic + триггер. После отпускания bubble возвращается на место (`onSwiped` пуст, `clearView` сбрасывает `translationX`). Иконка стрелки рисуется справа в `onChildDraw` с alpha по прогрессу.
- **Reply preview bar**: `replyPreviewBar` в `activity_chat.xml` — `MaterialCardView` над `attachmentPreviewBar`, показывается при `pendingReplyMessageId != 0L`. Содержит автора, превью текста (или "📷 N фото" / "📎 N файлов"), кнопку отмены `clearReplyButton`.
- **Отправка**: `ChatRepository.sendMessage(..., forwardedMessageId = pendingReplyMessageId)`. После успеха — `clearPendingReply()`.

### Редактирование и удаление сообщений

Реализовано через `EditMessage` / `DeleteMessage` gRPC из [[Backend/Messages]] и стримы `SubscribeMessagesEdited` / `SubscribeMessagesDeleted` из [[Backend/Updates]].

- **Repository** (`ChatRepository.editMessage(messageId, text, fileIds)` / `deleteMessage(messageId)`): обёртки над gRPC-вызовами. Возвращают `Result<Shared.Message>` / `Result<Unit>`.
- **RealtimeService** (`grpc/RealtimeService.kt`): два дополнительных `MutableSharedFlow` — `messageEdited`, `messageDeleted`. Стартует две корутины `streamWithReconnect("MessagesEdited"/"MessagesDeleted")` в `resume()`.
- **ChatActivity состояние**: `pendingEditMessageId: Long`, `pendingEditFileIds: List<String>`. Если != 0 → `sendMessage()` вызывает `sendEdit()` вместо `chatRepository.sendMessage()`.
- **Edit preview bar**: `editPreviewBar` в `activity_chat.xml` — `MaterialCardView` с заголовком «Редактирование сообщения» и превью текста, привязан к `attachmentPreviewBar` (как `replyPreviewBar`). Кнопка отмены `clearEditButton`.
- **Edit-режим**: `setPendingEdit(item)` подставляет текст в `messageEditText`, фокусирует поле, открывает клавиатуру. Сохраняет существующие `file_id` (без FORWARDED_MESSAGE) в `pendingEditFileIds` — backend сохраняет их при правке без изменений. Edit и reply — взаимоисключающие, при входе в edit активный reply сбрасывается.
- **Delete-режим**: `confirmAndDelete(item)` показывает `MaterialAlertDialogBuilder` («Удалить сообщение?» / «Удалить» / «Отмена»). При подтверждении — `chatRepository.deleteMessage()`, при успехе сразу `removeMessageById(messageId)` (UI-обновление до прихода стрима).
- **Применение событий**: `applyEditedMessage(msg)` и `removeMessageById(id)` модифицируют `messageAdapter.currentList` через `submitList`. Вызываются и из обработчика ответа на gRPC (для своих изменений), и из подписок на стримы (для изменений других участников).
- **Метка «изменено»**: `MessageItem.isEdited: Boolean` пробрасывается в три места создания (`messagesWithDateSeparators`, `appendMessages`, `addNewMessage`). В layouts `item_message_sent.xml` / `item_message_received.xml` рядом со временем — `editedLabelTextView` (italic, alpha 0.6, `?attr/colorOnPrimaryContainer` или `?attr/colorOnSurfaceVariant`). Привязка в обоих ViewHolder: `editedLabelTextView.visibility = if (item.isEdited) VISIBLE else GONE`.
- **Proto**: добавлены `EditMessage` / `DeleteMessage` rpc в `messages_api.proto`, `SubscribeMessagesEdited` / `SubscribeMessagesDeleted` в `updates_api.proto`, `is_edited` (field 7) и `edited_at` (field 8) в `shared.proto:Message`.

### Пересылка в другие чаты (forward)

`ForwardChatPickerBottomSheet` (`dialog/ForwardChatPickerBottomSheet.kt`):
- `BottomSheetDialogFragment` с `STATE_EXPANDED` + `skipCollapsed=true`.
- Загружает чаты через `grpcManager.getChats()` (та же сортировка по `lastMessage.sentAt`, что в `ChatsFragment`).
- `ForwardChatPickerAdapter` — multi-select через `selectedIds: LinkedHashSet<String>`, click тоглит CheckBox и вызывает `notifyItemChanged`.
- Кнопка "Переслать (N)" активируется при `count > 0`. При нажатии — параллельный `async/awaitAll` вызов `chatRepository.sendMessage` для каждого выбранного чата с тем же `forwardedMessageId`, опциональным комментарием.
- Layouts: `bottom_sheet_forward_chats.xml`, `item_chat_forward_picker.xml`.

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
