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

## Сегмент папок над списком чатов

`fragment_chats.xml` содержит горизонтальный `RecyclerView` `foldersRecyclerView` поверх `chatRecyclerView`, скрытый при отсутствии папок. Раньше был `ChipGroup` с одинарными `Chip`-ами; заменён на кастомный адаптер ради иконки + имени + бейджа непрочитанных и поддержки компактного режима.

- `adapter/FolderTabsAdapter.kt` — `RecyclerView.Adapter` с моделью `Item(id, icon, name, unreadCount)`. Метод `submit(items, compact, selected)` обновляет список целиком; `updateSelection(id)` — только подсветку. Иконка по умолчанию для папки «Все чаты» (folderId=null) — `📋`. Бейдж непрочитанных рисуется при `unreadCount > 0`, формат `99+` при переполнении.
- `layout/item_folder_tab.xml` — корневой `LinearLayout` с `bg_folder_tab` selector (state_selected → `bg_folder_tab_selected` с `colorSecondaryContainer`, иначе прозрачный). Иконка (TextView 18sp под эмодзи) + имя (`?attr/textAppearanceLabelLarge`, ellipsize=end) + бейдж (`bg_folder_unread_badge` — rounded rect `colorPrimary` r=10dp, 20dp высота, minWidth=20dp, текст bold 11sp на `colorOnPrimary`).
- `ChatsFragment`:
  - `setupFolderTabs()` инициализирует горизонтальный `LinearLayoutManager` + адаптер.
  - `renderFolderTabs()` собирает `Item`-ы из `folders`, добавляя первой «Все чаты»; передаёт `compact = globalParam.compactFolders`.
  - `computeFolderUnread(chatIds)` суммирует `chat.countUnread` из `allChats`, ограничиваясь `chatIds` папки. Для «Все чаты» — `computeAllChatsUnread()` (см. ниже).
  - Реалтайм-события зеркалятся в `allChats` через `mirrorNewMessageInAllChats` / `mirrorReadInAllChats`, после чего `refreshFolderTabs()` пересчитывает бейджи без перезагрузки чатов.
  - На клик по табу — `selectedFolderId` обновляется, `foldersAdapter.updateSelection` + `applyFolderFilter()`.
  - `onResume()` ререндерит сегмент с актуальной настройкой компактности (чтобы изменение в персонализации применялось при возврате).

### Настройка «Убирать чаты из «Все чаты»» (`excludeFolderChatsFromAll`)

Если включена — чаты, входящие хотя бы в одну пользовательскую папку, исключаются из вкладки «Все чаты» **и** не учитываются в её бейдже непрочитанных. Логика в `applyFolderFilter()` (фильтрация списка) и `computeAllChatsUnread()` (счётчик). Реализовано чисто на клиенте.

### Настройка «Компактные папки» (`compactFolders`)

Скрывает `folderName` (`View.GONE`) в каждом табе, оставляя иконку + бейдж. Передаётся в `FolderTabsAdapter.submit(compact = ...)`.

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


## Звонки (V1)

Стартовая интеграция звонков живёт только в V1 (`Android/Barkfluff.Client.Android/app`) и общем `Android/core`; V2 не менялся.

- `Android/core/src/main/proto/beacon_api.proto` синхронизирован с `Shared/BarkFluff.Proto/beacon_api.proto`: добавлен `Service calls = 14` рядом с `livekit_url = 13`.
- `GlobalParam` хранит `socketCalls` и `livekitUrl`; `SelectServerActivity` сохраняет их из Beacon, `AboutActivity` показывает в диагностике.
- При применении ответа Beacon V1 не превращает пустой/offline Calls endpoint в URL, по умолчанию добавляет `https://` к адресам без схемы и пишет в Logcat raw-диагностику `Beacon calls: has/host/port/tls/livekit`.
- `utils/ServerInfoPrefs.kt` централизует сохранение Beacon `GetServerInfo` в `GlobalParam`; `SplashActivity` при запуске требует только сохранённый `socketBeacon`, обновляет остальные endpoint'ы из Beacon и продолжает вход только если после refresh есть Identity, а `AboutActivity` при открытии refresh'ит Beacon и перерисовывает список сервисов.
- `GrpcManager` умеет создавать `CallsApi` client (`createCallsClient`) и пересоздавать его через `initAllClients`/`recreateAllClients`.
- `core/calls/CallRepository.kt` — тонкая обёртка над `InitiateCall`, `AcceptCall`, `RejectCall`, `JoinCall`, `EndCall`, `SetCallAudioQuality`, `SubscribeCallEvents`, `ListCallHistory`, `GetActiveCalls`.
- `core/calls/CallEventsService.kt` подключается в `BarkFluffApplication` вместе с `RealtimeService`: держит lifecycle-подписку на `SubscribeCallEvents`, публикует raw events через `SharedFlow`, текущее состояние звонка через `StateFlow`, делает reconnect/backoff и auto-reject второго входящего звонка при уже активном звонке.
- В `BarkFluffApplication` есть foreground bridge для `CallEventsService.events`: incoming открывает `IncomingCallActivity` и показывает call notification, accepted/rejected/ended закрывают входящий экран через package-local broadcast и убирают notification. Background/killed сценарий остаётся за FCM payload `incoming_call`/`dismiss_call`.
- `CallsFragment` (вкладка `Звонки`) показывает реальную историю из `ListCallHistory` через `CallHistoryAdapter` (`item_call_history.xml`): direction/missed-иконка, имя/чат, относительное время + длительность. Фильтр `Все`/`Пропущенные` перезагружает список; tap по строке открывает чат (личный — через `getPersonChatId`), кнопка action — повторный звонок (audio/video) через `CallActivity`. Имена резолвятся: личные — `getUserData`, групповые — из списка чатов. Пагинация v1: одна страница (limit 50), `has_more` пока не используется. Бэкенд `GetActiveCalls` доступен в репозитории, join-баннер ещё не нарисован.
- В V1 `ChatActivity` в верхней панели кнопка **только аудиозвонка** (видео-кнопка убрана; видео включается уже внутри `CallActivity`). Запускает сигналинг и открывает `CallActivity` с `livekitUrl/accessToken`.
- **Групповые чаты V1**: клик по шапке группового чата открывает `GroupInfoActivity` (для ЛС — `UserProfileActivity`). `GroupInfoActivity`: смена названия (`updateGroupChat(title)`), смена аватара (UCrop → `uploadFile(CHAT_PICTURE)` → `updateGroupChat(pictureFileId)`), список участников (`listChatMembers` + `getUserData` для аватарок, `GroupMemberAdapter`; fallback preview/full URL → preview/full fileId), тап по участнику открывает `UserProfileActivity`, удаление (`kickUser`), добавление через `AddGroupMemberActivity` (переиспользует search-layout + `UserAdapter`, `addUser`). Аватарки/имена чужих сообщений в группе резолвятся через кэш `groupMemberInfoCache` с тем же URL-first fallback в `ChatActivity` + `senderInfoProvider` в `MessageAdapter`. Сортировка списка чатов: `ChatsFragment.mirrorNewMessageInAllChats`/`applyFolderFilter` пересортировывают `allChats` по `lastMessage.sentAt`. Контекстное меню сообщения (`PopupWindow isFocusable=false`) больше не закрывает клавиатуру.
- FCM service обрабатывает `type=incoming_call` и `type=dismiss_call`. `NotificationHelper` создаёт канал `calls` и показывает `NotificationCompat.CallStyle` для входящего звонка.
- Telecom-интеграция звонков: V1 регистрирует self-managed PhoneAccount через MANAGE_OWN_CALLS и BarkFluffConnectionService, а realtime/FCM incoming_call сначала вызывает TelecomManager.addNewIncomingCall, затем показывает собственный CallStyle/full-screen UI и ringtone. Telecom не проигрывает ringtone сам для self-managed VoIP, но учитывает звонок как системный call для маршрутизации, Bluetooth и конкуренции с другими звонками.
- Для входящего звонка `NotificationHelper.showIncomingCallNotification` дополнительно запускает системный ringtone через `RingtoneManager.TYPE_RINGTONE` + `AudioAttributes.USAGE_NOTIFICATION_RINGTONE` в loop-режиме. Входящее call-уведомление показывается в отдельном канале `incoming_calls_v2` с `IMPORTANCE_HIGH`, vibration/default vibration и без `setSilent(true)`, чтобы Android мог показать heads-up баннер поверх экрана/lockscreen; ongoing-уведомление активного звонка остаётся в `calls` и `setSilent(true)`, чтобы не было короткого notification-звука поверх ringtone. После accept используется `NotificationHelper.clearIncomingCallAlert`, чтобы остановить ringtone и убрать входящий notification без разрыва активного Telecom `Connection`; полный `NotificationHelper.dismissCall` завершает Telecom connection для realtime/FCM `dismiss_call`, reject/end.
- Добавлены IncomingCallActivity, CallActivity, CallActionReceiver и permissions для микрофона/camera/screen-share/full-screen intent.
- `IncomingCallActivity` показывает аватар звонящего с локальными retry-попытками загрузки (3 попытки с короткой паузой; после провала cached URL запрашивается заново через `ChatRepository.getFileDownloadUrl`) и анимированными ring-pulse кольцами вокруг аватара.
- В `:app-v1` подключён LiveKit Android SDK `2.26.0` + `livekit-android-camerax`. `LiveKitCallEngine` управляет room lifecycle, mic/camera/screen-share и **отдаёт UI-модель участников** `StateFlow<List<CallParticipant>>` (камера+экран track, mic/camera enabled, speaking, connection quality) вместо хранения renderer'ов. Движок пересобирает список по событиям Room (`ParticipantConnected/Disconnected`, `TrackPublished/Subscribed/Muted`, `ActiveSpeakersChanged`, `ConnectionQualityChanged`), различает `Track.Source.CAMERA` и `SCREEN_SHARE`. Дополнительно: `flipCamera()` (`LocalVideoTrack.switchCamera`), `selectAudioDevice()` через `AudioSwitchHandler` (динамик/наушник/проводная/Bluetooth), `setRemoteVideoQuality()` (`RemoteTrackPublication.setVideoQuality`).
- **UI-дизайн** (`activity_call.xml` + программная отрисовка плиток). Тёмный иммерсивный экран (`bg_call_root`), edge-to-edge с обработкой `WindowInsets` (верхняя панель не залезает под статус-бар, панель управления — над навигацией). Стиль повторяет веб-референс. Режимы раскладки в `CallActivity.renderTiles`:
  - **single** (1-на-1, нет демонстрации) — `CallTileView.setHero(true)`: крупный аватар (112dp) + имя + waveform по центру, локальный участник — в мини-окне `selfMiniContainer` (100×140, скруглённое) вверху справа;
  - **grid** — сетка плиток до 2 колонок (ячейки через weight, не растягиваются вертикально);
  - **stage** — демонстрация экрана/focus: крупная плитка сверху + полоса камер снизу. Tap по плитке — focus/возврат.
- `CallTileView` (плитка/hero): аватар (картинка через Coil CircleCrop, иначе цветной круг с инициалами через `AvatarLoader`), имя, чип статуса микрофона (зелёный/красный/нейтральный), зелёная обводка говорящего, при включённой камере — `SurfaceViewRenderer` на весь размер. Имена/аватары участников резолвятся в `CallActivity.infoFor` по userId (livekit identity) через `getUserData` с кешем `infoCache` (раньше показывался сырой id без аватара).
- **Палочки голоса** — кастомный `WaveformView` (анимированный эквалайзер с независимыми случайными высотами палочек), активен пока участник говорит (для hero под именем, для плитки вместо «молчит»).
- **MD3 dynamic (адаптация под системную тему)**: весь хром берёт цвета из ролей темы (`Theme.Material3.DayNight` + DynamicColors) — фон-градиент `colorSurface`→`colorSurfaceContainerLow`, плитки `colorSurfaceContainerHigh`, кнопки `colorSurfaceContainerHighest`/`colorOnSurfaceVariant`, текст `colorOnSurface`/`colorOnSurfaceVariant`, панель/бейджи + `colorOutlineVariant`. Активные кнопки — `colorPrimary`/`colorOnPrimary` (`applyButtonState`). Иконки статус-бара переключаются по яркости `colorSurface` (`ColorUtils.calculateLuminance`). Семантические акценты фиксированы: зелёный «говорит» (waveform/обводка/чип), красный mute (`colorError`), красная «Завершить». Аватары — палитра `AvatarLoader.colorForUser`. Так экран следует за светлой/тёмной системной динамикой, а не всегда тёмный.
- Контролы — стеклянная «таблетка» (`bg_call_control_pill`) с круглыми кнопками с подписями: `Микро`, `Камера`, большая красная `Завершить` (`bg_call_btn_end`), `Экран`, `Ещё`. Переворот камеры перенесён в лист `Ещё` (виден при включённой камере) вместе с `Маршрут звука` / `Качество голоса` / `Качество видео собеседника`. Бейдж количества участников в шапке для групповых (>2). Состояния кнопок и foreground service синхронизированы с моделью участников; foreground обновляется только при смене camera/screen.
- Таймер длительности устойчив к reconnect (anchor не сбрасывается). `CallActivity` слушает `CallEventsService.events` по своему `callId`: `ENDED`/`REJECTED` (в т.ч. от собеседника или со второго устройства) закрывают экран, останавливают foreground и гасят notification — работает и для звонящего, где `currentCall` не инициализируется. Завершение идемпотентно (флаг `callEnded`); accept/reject в `IncomingCallActivity` защищены флагом `actionTaken`.
- `CallForegroundService` держит ongoing notification активного звонка с foreground service types `microphone|camera|mediaProjection`; action уведомления завершает активный звонок через `CallActionReceiver`.
- Foreground активного звонка стартует до подключения к LiveKit, поэтому аудиозвонок тоже сразу получает ongoing foreground notification. Runtime type включает `microphone|phoneCall`, при включении камеры добавляется `camera`, при демонстрации — `mediaProjection`.
- `CallActivity` при `RoomEvent.Disconnected` / `FailedToConnect` не завершает звонок сразу: запускает backoff-реконнект (2s, 4s, 8s, 15s, далее 30s, максимум 8 попыток), обновляет LiveKit credentials через `JoinCall(call_id)`, пересоздаёт room и восстанавливает состояние микрофона/камеры. Демонстрация экрана после полного reconnect не восстанавливается автоматически, т.к. `MediaProjection` требует свежего системного consent.
- Пока локально есть активный звонок (`CallTelecomRegistry.hasActiveCall()`), `BarkFluffApplication.onStop()` не ставит `CallEventsService` на паузу — remote `ENDED`/`REJECTED` продолжает доходить в фоне.
- `CallBatteryOptimizationHelper` один раз во время успешного звонка просит Android исключить BarkFluff из battery optimization (`ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS`), если система ещё не дала исключение. Это дополняет foreground service для агрессивных OEM/Doze-сценариев.

Связанный backend-контекст: [[Backend/Calls]], [[Backend/CloudMessaging]].
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

### Оптимистичный UI и прогресс отправки медиа

При отправке фото/видео `ChatActivity.handleMediaSend` сразу добавляет оптимистичное сообщение (`MessageItem` с `localId`, `uploadProgress`, `localPreviewUris`) и кидает `SendJob` в `MediaSendService`. Сообщение видно мгновенно — с локальным превью медиа и оверлеем прогресса поверх.

- **Локальное превью.** `MessageItem.localPreviewUris: List<Uri>` — URI исходных медиа (RawImage→uri, EditedImage→originalUri, Video→spec.uri; документы/стикеры превью не имеют). `MessageAdapter.buildLocalMediaGrid` рендерит ту же сетку, что и серверные вложения (`determineLayout` + `item_attachment_media_cell`, загрузка через Coil `.load(uri)`). Поэтому количество загружаемых файлов видно сразу как N миниатюр.
- **Прогресс.** `MediaSendService.aggregateProgress(idx, pct)`: при `sendSeparately` прогресс пофайловый (у каждого файла своё сообщение/localId), иначе — агрегированный по всем N файлам одного сообщения `(idx*100+pct)/total`, чтобы бар не сбрасывался в 0 на каждом следующем файле. События идут через `MediaSendService.uploadEvents` (`UploadEvent`: PREPARING/UPLOADING/SENDING/SENT/FAILED + `progress` + `serverMessageId`).
- **Реальная скорость.** `ChatRepository.uploadFile` ставит `connection.setFixedLengthStreamingMode(...)` и флашит каждый чанк — без этого `HttpURLConnection` буферизует тело в память и `onProgress` прыгал бы в 100% ещё до сетевой отправки.
- **Реконсиляция (защита от дубликата/пустого сообщения).** `ChatActivity.addNewMessage` ищет оптимистичный плейсхолдер ДО проверки дубликата, в обоих порядках прихода: realtime-эхо раньше ответа `sendMessage` (матч по контенту + `uploadProgress != null`/`localPreviewUris`, т.к. вложения плейсхолдера ещё пустые) ИЛИ `SENT` раньше эха (матч по уже проставленному `messageId` + `localId`). Без этого эхо с фото добавлялось бы вторым item'ом, а `clearOptimisticUploadProgress` оставлял первый с пустыми вложениями → два item с одним `messageId` → коллизия `DiffUtil.areItemsTheSame` (сравнение по `messageId`) → пустой bubble до переоткрытия чата.

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

- **Action menu**: клик по корневому `FrameLayout` строки сообщения (вне bubble — там, где padding 80dp слева/справа) открывает `PopupWindow` (`popup_message_actions.xml`). Базовые пункты: Ответить / Изменить / Удалить / Переслать / Закрепить. Закрепить — заглушка `Toast "Скоро будет"`. Изменить/Удалить — реализованы, скрываются через `View.GONE` для чужих сообщений.
- **Подсветка выбранного сообщения**: `ChatActivity.showMessageActionMenu` находит `R.id.messageCard` внутри anchor, сохраняет `originalForeground` и устанавливает полупрозрачный `ColorDrawable(?attr/colorPrimary)` (~24% alpha) как `foreground`. На `popup.setOnDismissListener` — восстановление. Анимация не нужна: `popup.isFocusable=true` блокирует прокрутку RecyclerView, поэтому ViewHolder не перепривязывается.
- **Контекстные пункты (по содержимому сообщения)**:
  - **Копировать текст** (`actionCopyText`) — если `item.text.isNotBlank()`. `ClipData.newPlainText`.
  - **Скопировать изображение** (`actionCopyImage`) — если ровно одна картинка (IMAGE/GIF). Скачивание в `FileCache`, `FileProvider.getUriForFile(...)`, `ClipData.newUri`.
  - **Сохранить изображение / Сохранить изображения** (`actionSaveImages`) — если ≥1 картинка. Текст меняется по количеству. Сохраняет в `Pictures/BarkFluff/` через `FileSaveUtils.saveImageToGallery` (видно в Галерее).
  - **Сохранить в загрузки** (`actionSaveDocs`) — если ≥1 документ (DOCUMENT). Сохраняет в `Downloads/BarkFluff/` через `FileSaveUtils.saveToDownloads`.
- **Места сохранения файлов** (`utils/FileSaveUtils.kt`): картинки → `Pictures/BarkFluff` (MediaStore.Images), видео и документы → `Downloads/BarkFluff` (MediaStore.Downloads). На API < Q используется `Environment.getExternalStoragePublicDirectory(...)` + `uniqueFile(...)` для дедуплицирования имён.
- **Вьюверы**: `ImageViewerActivity.saveToGallery` использует `FileSaveUtils.saveBitmapToGallery` (Pictures/BarkFluff). `MediaViewerActivity.saveToGallery` использует `FileSaveUtils.saveToDownloads` (Downloads/BarkFluff).
- **Long-press на вложениях**: меню картинок удалено целиком (`menu_image_attachment.xml` удалён) — теперь сохранение картинок только через основное меню. У документов осталось только «Удалить из кеша» (показывается лишь если файл закеширован). У аудио — без изменений.
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

## E2E чаты (приватные + секретные)

Stage 6 плана `messages-crystalline-axolotl.md` — на Android реализован клиентский слой E2E.

### Зависимости (libs.versions.toml + build.gradle.kts)

- `org.signal:libsignal-android:0.86.16` + `org.signal:libsignal-client:0.86.16` — Signal Double Ratchet для секретных чатов. Maven repo: `https://build-artifacts.signal.org/libraries/maven/` добавлен в `settings.gradle.kts`.
- `com.lambdapioneer.argon2kt:argon2kt:1.6.0` — Argon2id для приватных чатов.
- `coreLibraryDesugaring("com.android.tools:desugar_jdk_libs:2.1.4")` + `sourceCompatibility = VERSION_17` — требование libsignal (Java records).
- `packaging.resources.excludes` — `libsignal_jni*.dylib`, `signal_jni*.dll`, `**/libsignal_jni_testing.so` чтобы не тащить native-либы других платформ в APK.

### Crypto-модуль `com/barkfluff/client/crypto/`

- `PrivateChatCrypto.kt` — Argon2id (t=3, m=64MiB, p=4) → 32-байтный ключ → AES-256-GCM (nonce 12 байт, GCM tag 128 бит). HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER") — passphrase verifier для проверки на стороне приглашённого. AAD = `barkfluff:private:{chatId}`.
- `BarkFluffSignalStore.kt` — реализация `org.signal.libsignal.protocol.state.SignalProtocolStore` поверх `EncryptedSharedPreferences("barkfluff_signal_store")`. Все записи (identity-key, sessions, prekeys, signed prekeys, kyber prekeys, sender keys) сериализуются через `.serialize() → Base64`. `saveIdentity` возвращает `IdentityKeyStore.IdentityChange`, `markKyberPreKeyUsed(int, int, ECPublicKey)` — no-op (Kyber не используется в текущем proto).
- `PrekeyManager.kt` — генерация identity-key (`IdentityKeyPair.generate()`), registration_id (`KeyHelper.generateRegistrationId(false)`), signed prekey (ручной: `ECKeyPair.generate()` + `identityPriv.calculateSignature(pubBytes)`), 100 one-time prekeys (`PreKeyRecord(id, ECKeyPair.generate())`). Метаданные регистрации в `barkfluff_prekey_state` (plain prefs, не sensitive).
- `E2EBootstrap.kt` — `ensurePrekeyBundleRegistered(context)`: при первом логине вызывается из `MainActivity.onCreate` → генерит bundle → `UsersApi.RegisterPrekeyBundle` → `PrekeyManager.persistBundle()`. Идемпотентно. Также `replenishIfNeeded(remaining)`.
- `EncryptedInviteHandler.kt` — слушает 4 стрима (`privateChatInvites`, `privateChatInviteResolutions`, `secretChatInvites`, `secretChatResolutions`) из `RealtimeService` и показывает MaterialAlertDialog. При accept приватного — запрашивает passphrase, валидирует verifier, вызывает `PrivateChatRepository.acceptPrivateChatInvite`. Регистрируется в `MainActivity.onCreate`.

### Repositories `com/barkfluff/client/repository/`

- `PrivateChatRepository.kt` — фасад приватных чатов:
  - `createPrivateChat(peerId, passphrase)`: генерит salt+key+verifier, gRPC, кеширует key в `EncryptedSharedPreferences("barkfluff_private_chat_keys")` ключ=chatId.
  - `acceptPrivateChatInvite(chatId, passphrase, salt, verifier)`: derive → validate → если ОК, gRPC accept + кеширует key.
  - `unlockExistingChat(chat, passphrase)`: для повторного открытия после разлогина или с другого устройства.
  - `sendText/editText/deleteMessage/listMessages/decryptIncoming` — encrypt/decrypt поверх gRPC. Если ключ забыт — `KeyNotAvailableException`.
- `SecretChatRepository.kt` — фасад секретных чатов:
  - `createSecretChat(peerUserId, peerDeviceId, initialPlaintext)`: fetch peer bundle → SessionBuilder.process → SessionCipher.encrypt (PreKeySignalMessage) → SendSecretChatInvite. Метаданные локального чата (id, peer, inviteId, role) в `EncryptedSharedPreferences("barkfluff_secret_chats")`.
  - `acceptIncomingInvite(inviteId, sender, envelope)`: decrypt PreKeySignalMessage → создать локальную SecretChat запись → AcceptSecretChatInvite gRPC.
  - `sendMessage/decryptIncoming/ack` — runtime-операции через SessionCipher.
  - **Лимитация**: libsignal 0.86+ требует Kyber prekey в `PreKeyBundle` (PQXDH), а текущий proto `barkfluff.users.PrekeyBundle` хранит только X25519. Метод `toLibsignal()` бросает `UnsupportedOperationException` с пояснением — требуется расширить proto Kyber-полями + backend Users (в плане как future work). Приватные чаты от этого не зависят и работают полностью.

### gRPC методы (в `GrpcManager.kt`)

Добавлены 16 новых методов:
- Приватные: `createPrivateChat`, `acceptPrivateChat`, `rejectPrivateChat`, `sendPrivateMessage`, `listPrivateMessages`, `editPrivateMessage`, `deletePrivateMessage`, `getChat`.
- Секретные: `sendSecretChatInvite`, `acceptSecretChatInvite`, `rejectSecretChatInvite`, `sendSecretMessage`, `ackSecretMessage`.
- Prekey-bundle: `registerPrekeyBundle`, `fetchPrekeyBundle`, `listPeerDevices`, `replenishOneTimePrekeys`, `rotateSignedPrekey`.

### RealtimeService — 8 новых SharedFlow + collectors

`privateMessages`, `privateMessageEdits`, `privateMessageDeletes`, `privateChatInvites`, `privateChatInviteResolutions` (user-scope) + `secretChatInvites`, `secretChatResolutions`, `secretMessages` (device-scope; маршрутизация на `RecipientDeviceId` из JWT). Подписки запускаются в `RealtimeService.resume()` через `streamWithReconnect("…")` с тем же exponential backoff, что у существующих стримов.

### UI

- `CreateEncryptedChatActivity` (+ `activity_create_encrypted_chat.xml`) — единый экран создания: выбор типа (Private/Secret через MaterialButtonToggleGroup), ввод peer userId, passphrase ИЛИ peer device + initial message. Spinner устройств заполняется через `ListPeerDevices`.
- `PrivateChatActivity` (+ `activity_private_chat.xml`) — минимальная история приватного чата: загрузка через `listPrivateMessages` (расшифровка inline), отправка `sendText`, реалтайм через `realtimeService.privateMessages`. При отсутствии локального ключа — диалог запроса passphrase + `unlockExistingChat`.
- `SecretChatActivity` (+ `activity_secret_chat.xml`) — секретный чат, без истории (сервер не хранит). Только runtime-сообщения через `realtimeService.secretMessages` + `decryptIncoming` + `ack`.
- `EncryptedMessageAdapter` + `item_encrypted_message.xml` — простой list-адаптер для расшифрованных сообщений (sender label / text / time).
- `ChatsFragment` — кнопка `encryptedChatButton` в toolbar открывает `CreateEncryptedChatActivity`.
- AndroidManifest — три новых Activity зарегистрированы.

### Storage prefs

| Имя | Содержимое |
|-----|-----------|
| `barkfluff_signal_store` | EncryptedSharedPreferences. Identity-key, sessions, prekeys (libsignal сериализация). |
| `barkfluff_prekey_state` | Plain prefs. Флаги `prekey_registered`, `prekey_next_one_time_id`, `prekey_next_signed_id`. |
| `barkfluff_private_chat_keys` | EncryptedSharedPreferences. chatId → Base64(AES-256 key). |
| `barkfluff_secret_chats` | EncryptedSharedPreferences. secretChatId → `peerUserId\|peerDeviceId\|inviteId\|role\|accepted`. |

## Раздел «Тестирование» (dev/QA-флаги)

В настройках профиля под пунктом «О приложении» есть пункт **Тестирование** (`itemTesting`, иконка `ic_science`), открывающий `TestingSettingsActivity`. Раздел содержит локальные `MaterialSwitch`-флаги, читаемые/записываемые через `data/GlobalParam.kt`. Все ключи — в `barkfluff_prefs` (plain SharedPreferences).

| Свойство `GlobalParam` | Ключ prefs | Что включает |
|---|---|---|
| `showIdsInProfile` | `testing_show_ids_in_profile` | ID-строки в `UserProfileActivity` и ChatId-карточка в `GroupInfoActivity`. В профиле пользователя показывает `UserId: <otherUserId>` и `ChatId: <chatId>`, в профиле группы — `ChatId: <chatId>`. Тап по строке копирует значение в `ClipboardManager` + Toast. |
| `secretChatsEnabled` | `testing_secret_chats_enabled` | `encryptedChatButton` в шапке `ChatsFragment` (иконка `ic_hood`, открывает `CreateEncryptedChatActivity`). По умолчанию кнопка `View.GONE`; видимость переоценивается в `onViewCreated` и `onResume`, чтобы переключение в TestingSettings подхватывалось при возврате. |

Оба флага по умолчанию `false` — обычная сборка не показывает ни блок ID, ни кнопку скрытых чатов.

## Раздел «Персонализация» — папки

`PersonalizationSettingsActivity` (между блоками «Настройки отображения» и «Фон чатов») содержит блок «Папки» с двумя `MaterialSwitch`:

| Свойство `GlobalParam` | Ключ prefs | Что включает |
|---|---|---|
| `compactFolders` | `folders_compact` | Компактные папки: в сегменте `ChatsFragment.foldersRecyclerView` скрывает текст имени папки, оставляя только иконку + бейдж непрочитанных. |
| `excludeFolderChatsFromAll` | `folders_exclude_from_all` | Чаты, входящие хотя бы в одну пользовательскую папку, не показываются во вкладке «Все чаты» и не учитываются в её бейдже. |

Оба флага по умолчанию `false`. Применяются в `ChatsFragment.renderFolderTabs()` / `applyFolderFilter()` / `computeAllChatsUnread()`. При возврате на список чатов из персонализации `onResume()` фрагмента ререндерит сегмент.

## Настройки → Аккаунт — поле «О себе»

`AccountSettingsActivity` в карточке полей профиля содержит `itemBio` под `itemUsername` (разделитель `MaterialDivider`). Текущее значение читается из `globalParam.description` и отображается в `textBio` (placeholder «Не указано» при пустой строке). По клику — `showEditDialog("О себе", …, allowEmpty = true)` → `grpcManager.changeBio(newValue)` (`GrpcManager.kt:1674`). При успехе значение сохраняется в `globalParam.description` (тот же бэкенд-поле, что наполняется из `getCurrentUserData().bio` в `SplashActivity`/`LoginActivity`/`RegisterActivity`).

## App Widget «Закреплённые чаты»

Нативный App Widget для рабочего стола Android: до 3 строк с аватаром, именем чата, текстом последнего сообщения и бейджем непрочитанных. Тап по строке открывает `ChatActivity` соответствующего чата, тап по заголовку — `MainActivity`. Кнопка refresh в углу триггерит немедленное обновление.

Пользователь может создавать **несколько** виджетов одновременно (каждый со своим именем и своей подборкой 1-3 чатов). Управление — через пункт **«Виджеты»** в `ProfileFragment` (`itemWidgets`, иконка `ic_widgets`, между блоками Персонализация и Папки чатов) → `WidgetsSettingsActivity`. Создание нового виджета — стандартный Android-флоу: long-press на рабочем столе → BarkFluff → «Закреплённые чаты», автоматически открывается `WidgetConfigureActivity`.

### Компоненты `com/barkfluff/client/widget/`

- `WidgetConfig.kt` — data class `{ name: String, chatIds: List<String> }`, константа `MAX_CHATS = 3`.
- `WidgetRepository.kt` — singleton поверх `SharedPreferences("barkfluff_widgets")`. Ключ `widget_<appWidgetId>` → JSON. Методы: `getConfig`, `saveConfig`, `deleteConfig`, `listAllConfigs`, `findAppWidgetIdsForChat(chatId)`, `placedAppWidgetIds(context)`.
- `WidgetRenderer.kt` — строит `RemoteViews` по конфигу + снимку `List<ChatData>`. Аватары грузит синхронно через `AvatarLoader.getImageLoader(context).execute()` → `Bitmap` → круглая маска через `BitmapShader` → `setImageViewBitmap`. При отсутствии fileId или ошибке — placeholder-Bitmap с цветным кругом и инициалами (палитра из `AvatarLoader.PLACEHOLDER_COLORS`). **Важно**: include одного и того же layout трижды внутри RemoteViews не работает (RemoteViews адресует view по id и находит только первое вхождение), поэтому строки собираются через `views.removeAllViews(R.id.widgetRowsContainer)` + `views.addView(..., rowViews)` с отдельным `RemoteViews(R.layout.widget_chat_row)` на каждую строку.
- `WidgetUpdater.kt` — единая точка обновления:
  - `refreshWidget(context, appWidgetId)` — synchronous suspend под mutex.
  - `refreshAllWidgets(context)` — для всех размещённых.
  - `scheduleRefreshForChat(context, chatId)` — дебаунсит 500мс через `ConcurrentHashMap<Int, Job>`, ранний return если ни один виджет не содержит `chatId`. Используется из realtime-стримов.
  - In-memory кеш `getChats()` на 10 секунд (`CACHE_TTL_MS`) — чтобы шторм real-time событий не дёргал gRPC по разу на каждый виджет.
  - Если `messagesClient == null` (виджет работает в фоне без активного приложения) — переинициализирует через `grpcManager.createMessagesClient(globalParam.socketMessages, …)`.
  - Если `accessToken` пуст — виджет рендерится в режиме «Войдите в приложение».
- `PinnedChatsWidgetProvider.kt` — `AppWidgetProvider`. `onUpdate` → корутинами вызывает `refreshWidget` для каждого id. `onDeleted` → `WidgetRepository.deleteConfig` для каждого id. `onReceive` ловит кастомный `ACTION_REFRESH = "com.barkfluff.client.widget.ACTION_REFRESH"` (от кнопки refresh в виджете), использует `goAsync()` чтобы не убить процесс пока корутина грузит данные.
- `WidgetRefreshWorker.kt` — `CoroutineWorker`. Periodic WorkManager job `widget-refresh` раз в 30 минут с `NetworkType.CONNECTED`. Регистрируется в `BarkFluffApplication.onCreate()` через `enqueueUniquePeriodicWork(... KEEP ...)`. Fallback на случай killed-app (когда `RealtimeService` не работает).
- `WidgetConfigureActivity.kt` — Activity с `ACTION_APPWIDGET_CONFIGURE` в манifest'е. Поля: `nameInput` (max 48 символов, дефолт «Закреплённые чаты»), `pickChatsButton` (переиспользует `FolderChatPickerActivity` с `EXTRA_INITIAL_SELECTED`, обрезает результат до 3 + Toast при переборе), `selectedChatsRecyclerView` (показывает выбранные + крестик «удалить»). По умолчанию `setResult(RESULT_CANCELED)` — Android удаляет widget если пользователь не дошёл до «Сохранить». При сохранении: `WidgetRepository.saveConfig` → `WidgetUpdater.refreshWidget` → `setResult(RESULT_OK, intentWithAppWidgetId)` + `finish()`. В edit-mode (флаг `EXTRA_EDIT_MODE=true` от `WidgetsSettingsActivity`) `setResult` пропускается.

### `WidgetsSettingsActivity.kt`

Экран «Виджеты» в настройках. `RecyclerView` со списком конфигов из `WidgetRepository.listAllConfigs()`. Тап по строке открывает `WidgetConfigureActivity` с `EXTRA_APPWIDGET_ID` + `EXTRA_EDIT_MODE=true`. На `onResume` чистит «висячие» конфиги — id, которых нет в `placedAppWidgetIds()` (на случай если `onDeleted` не успел сработать). Под списком — карточка-подсказка как добавить виджет с рабочего стола.

### Интеграция в `RealtimeService`

`WidgetUpdater.scheduleRefreshForChat(context, event.chatId)` вызывается из 4 collector'ов: `collectNewMessages`, `collectMessagesRead` (только когда читал сам пользователь), `collectMessagesEdited`, `collectMessagesDeleted`. Дебаунс 500мс внутри `WidgetUpdater` гасит штормы; если виджетов с этим chatId нет — мгновенный return без gRPC.

### Resources

- Layouts: `widget_pinned_chats.xml` (root), `widget_chat_row.xml` (одна строка). `activity_widget_configure.xml`, `activity_widgets_settings.xml`, `item_widget_config.xml`, `item_widget_selected_chat.xml`.
- Drawables: `widget_background.xml` (rounded rect r=24dp), `widget_unread_badge.xml` (rounded rect r=12dp), `widget_row_ripple.xml`, `ic_widgets.xml`, `ic_refresh.xml`.
- Widget metadata: `res/xml/pinned_chats_widget_info.xml` — `minWidth=250dp`, `minHeight=180dp`, `configure=...WidgetConfigureActivity`, `updatePeriodMillis="0"` (обновляемся через WorkManager + RealtimeService, без системного таймера).
- Цвета: `widget_background_color`, `widget_text_primary`, `widget_text_secondary`, `widget_accent_color`, `widget_accent_text` — все через Material You system tokens (`@android:color/system_neutral1_*`, `system_accent1_*`), вариант для dark в `values-night/colors.xml`. `?attr/colorPrimary` в RemoteViews не резолвится корректно — поэтому в widget layouts всегда `@color/widget_*`.
- Manifest: `<receiver .widget.PinnedChatsWidgetProvider>` с двумя action — `APPWIDGET_UPDATE` и `com.barkfluff.client.widget.ACTION_REFRESH`. `<activity .widget.WidgetConfigureActivity>` exported=true с `APPWIDGET_CONFIGURE`. `<activity .WidgetsSettingsActivity>` exported=false.
- Зависимость: `androidx.work:work-runtime-ktx:2.9.1` (WorkManager) добавлена в `app/build.gradle.kts`.

### Передача chatId в ChatActivity

`WidgetRenderer.openChatPendingIntent` использует `Intent(context, ChatActivity::class.java).putExtra("chat_id", chatId).putExtra("chat_title", title)` с `FLAG_ACTIVITY_NEW_TASK or FLAG_ACTIVITY_CLEAR_TOP`. requestCode = `appWidgetId * 10 + rowIndex + 1` — уникальный для каждой PendingIntent, иначе несколько строк / виджетов схлопывались бы в один Intent. Расширения ChatActivity: `EXTRA_CHAT_ID = "chat_id"` и `EXTRA_CHAT_TITLE = "chat_title"`.

## gRPC Коды ошибок (из x-error-code trailer)

| Исключение | ErrorCode |
|-----------|-----------|
| OtpCodeNeedException | `C1576884-12D8-4722-A7EE-9F9789AD1265` |
| NotValidOtpCodeException | `803B632C-4457-4B05-9435-9C3DD0F41E00` |
| InvalidLoginOrPasswordException | `21BFB9B5-C377-45D1-9B15-6B7F3432B397` |

## Системное «Поделиться» (Share Intent)

Клиент зарегистрирован как получатель `ACTION_SEND` / `ACTION_SEND_MULTIPLE`. В системном Share Sheet пользователь может выбрать Barkfluff и переслать в любой чат текст/ссылку, одно или несколько изображений/видео/аудио/файлов.

- **Activity**: `share/ShareReceiverActivity.kt` — экспортированная (`android:exported=true`) с `launchMode=singleTask`, `excludeFromRecents=true`, `taskAffinity=""`. Intent-filters в `AndroidManifest.xml` покрывают `text/*`, `image/*`, `video/*`, `audio/*`, `application/*`, `*/*`.
- **Авторизация**: при отсутствии `refreshToken` / `socketUsers` / `socketMessages` — Toast `share_not_authorized` и `finish()`. Pending-share-payload **не** сохраняется (по решению UX).
- **Парсинг**: `parseSend` / `parseSendMultiple` достают `EXTRA_STREAM` (один Uri или ArrayList) и `EXTRA_TEXT` / `EXTRA_SUBJECT`; для каждого Uri резолвится MIME через `contentResolver.getType`, делается `takePersistableUriPermission` под try/catch. Результат — `SharePayload` (`Text` / `SingleFile` / `MultipleFiles`).
- **UI выбора чата**: `activity_share_receiver.xml` — `CoordinatorLayout` (`fitsSystemWindows=true`, фон `colorSurfaceContainerLowest`) + `AppBarLayout` с `liftOnScroll` + `MaterialToolbar` (иконка `ic_close`, заголовок «Куда отправить?», подзаголовок «Через Barkfluff») + `RecyclerView` с переиспользованным `ChatAdapter`. Чаты грузятся через `grpcManager.getChats()` после `ensureTokenValid` + `initAllClients`. Display-title для ЛС резолвится через `getUserData` (как в `ChatsFragment.resolveDisplayItem`). Window insets применяются вручную через `ViewCompat.setOnApplyWindowInsetsListener`: top → padding AppBar, bottom → padding RecyclerView (под gesture-nav). Пустое состояние — круглая M3-карта с `ic_chat_bubble` (как в `fragment_chats.xml`). Индикатор — `CircularProgressIndicator`.
- **Подтверждение**: клик по чату открывает `share/ShareConfirmBottomSheet.kt` (`BottomSheetDialogFragment`) — M3-bottom-sheet с `BottomSheetDragHandleView`, заголовком `headlineSmall`, label-подзаголовком, превью контента, `TextInputLayout` OutlinedBox для подписи и pill-кнопкой `MaterialButton` (56dp, corner 28dp) с иконкой `ic_send`. Bottom-padding динамически учитывает IME и navigation-bar инсеты (`max(ime, nav)`) — кнопка поднимается над клавиатурой.
  - `SharePayload.Text` → текст в EditText (редактируется), без превью изображения, отправляется как `SendJob(text=..., attachments=[])`.
  - `SharePayload.SingleFile`: image → Coil `imageView.load(uri)` в `ShapeableImageView` (corner Large); video → `contentResolver.loadThumbnail(uri, Size(512,512))` (API 29+); прочее → filled `MaterialCardView` (`colorSurfaceContainerHigh`, corner 16dp) с filled-tonal плашкой иконки (`colorPrimaryContainer`, corner 14dp) + имя/размер (`OpenableColumns.DISPLAY_NAME` / `SIZE`).
  - `SharePayload.MultipleFiles` → горизонтальный `RecyclerView` миниатюр (`item_share_preview_thumb.xml`, 96×96dp, corner Medium).
- **Постановка в очередь**: MIME → `AttachmentSpec`: `image/*` → `RawImage(uri)`, `video/*` → `Video(EditedVideoSpec(uri))`, остальное → `Document(uri)`. Дальше — обычный `MediaSendService.enqueue(ctx, SendJob(...))`, и SendJob проходит существующий конвейер (compress/upload/sendMessage) без изменений.
- **После отправки**: Toast `share_sent_toast`, `dismissAllowingStateLoss()`, `activity.finish()` — задача live в foreground-сервисе и без открытого UI.
- **Payload через Activity**: `SharePayload` содержит `Uri`-списки и не парселится — bottom-sheet читает его через `(activity as ShareReceiverActivity).payload`.

Связанные файлы:
- `share/SharePayload.kt`
- `share/ShareReceiverActivity.kt`
- `share/ShareConfirmBottomSheet.kt`
- `res/layout/activity_share_receiver.xml`
- `res/layout/sheet_share_confirm.xml`
- `res/layout/item_share_preview_thumb.xml`
- `res/values/strings.xml` — ключи `share_*`

## Локализация

Per-app locales через `AppCompatDelegate.setApplicationLocales` (без `attachBaseContext`/`BaseActivity`).

- Поддержанные языки: **ru**, **en**, **de**, **es**, **zh-CN** + «Системный» (сбрасывает override).
- Ресурсы: `res/values/strings.xml` (ru, default) + `values-en/`, `values-de/`, `values-es/`, `values-zh-rCN/`.
- `res/xml/locales_config.xml` перечисляет все 5 локалей; `AndroidManifest.xml` ссылается через `android:localeConfig="@xml/locales_config"`.
- `utils/LocaleManager.kt` — `apply(language)` маппит `GlobalParam.LANGUAGE_*` константы в `LocaleListCompat`; `"system"` → `getEmptyLocaleList()`.
- Хранение: `GlobalParam.appLanguage` (обычный `SharedPreferences`, ключ `app_language`, сохраняется при `clearUserData()` — это настройка устройства, не аккаунта).
- Применяется при старте: `BarkFluffApplication.onCreate()` вызывает `LocaleManager.apply(GlobalParam(this).appLanguage)`.
- UI: `LanguageSettingsActivity` (`activity_language_settings.xml`) — карточка с `RadioGroup` из 6 пунктов. Каждая строка содержит **флаг-эмодзи + нативное имя языка** (🌐 Системный, 🇷🇺 Русский, 🇬🇧 English, 🇩🇪 Deutsch, 🇪🇸 Español, 🇨🇳 中文). Открывается из `ProfileFragment` → пункт «Язык».
- При смене языка AppCompat сам пересоздаёт Activity-стек через `recreate()`.

## Файловая структура

- `gradle/libs.versions.toml` — все версии зависимостей
- `app/src/main/proto/` — 11 proto файлов
- `app/src/main/java/com/barkfluff/client/` — все исходники
- `docs/` — `CACHING_SYSTEM.md`, `MATERIAL3_REPORT.md`, `material_you_3_guide.md`
- Полная карта проекта — в Obsidian: [[Android-ProjectMap]] + [[Android-FileIndex]] (отдельного `PROJECT_MAP.md` в репозитории нет)

## Per-chat mute (отключение уведомлений чата)

- `ChatActivity` — пункт меню «три точки» (`btnMore` → `showChatMenu`) переключает mute через `GrpcManager.setChatMuted(chatId, muted, until?)`. Состояние читается из `GetChatInfo.muted`.
- `GrpcManager`: `setChatMuted()`, `getMutedChats()` (Set<chatId>).
- `ChatRepository.ChatInfo.muted` — маппится из proto `GetChatInfoResponse.muted`.
- `GlobalParam.mutedChatIds` (StringSet) + `setChatMutedLocal()` — локальный кэш; `BarkFluffFirebaseMessagingService.onMessageReceived` пропускает уведомление, если `chatId` в кэше (guard от гонок кэша токенов; сервер и так подавляет push).
- Строки: `chat_menu_mute/unmute`, `chat_muted/unmuted`, `chat_mute_error` (все 5 локалей). Серверная часть — [[Backend/Users]] → Per-chat mute.

## Сборка

```bash
cd Android/Barkfluff.Client.Android
./gradlew assembleDebug
```

### Тестовая сборка на macOS

На macOS клиент V1 собирается из корневого Android Gradle-проекта. Использовать JBR из Android Studio и локальный Android SDK:

```bash
cd /Users/liis/Projects/BarkFluff/Android
JAVA_HOME="/Applications/Android Studio.app/Contents/jbr/Contents/Home" \
ANDROID_HOME="/Users/liis/Library/Android/sdk" \
sh ./gradlew :app-v1:assembleDebug
```
