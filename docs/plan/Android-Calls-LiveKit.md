# План реализации звонков в Android-клиенте

> Цель: перенести в Android функциональность звонков из web-клиента и добавить Android-специфичный входящий звонок через Firebase push. Основная целевая реализация — `:app-v2` на Jetpack Compose + Material You 3; общий сигналинг и use-case слой — в `:core`. V1 на XML/ViewBinding описан как совместимая фаза после core.

---

## 1. Что уже есть

### Backend и протокол

- `BarkFluff.Calls` уже реализует call-control поверх LiveKit SFU: `InitiateCall`, `AcceptCall`, `RejectCall`, `JoinCall`, `EndCall`, `SetCallAudioQuality`, `SubscribeCallEvents`.
- Медиа не идут через backend: клиент получает `livekit_url` + `access_token` и подключается к LiveKit room `call:{id}`.
- `SubscribeCallEvents` — device-scope stream: входящий звонок приходит на все устройства, а `AcceptCall/JoinCall` гасит ring на остальных устройствах пользователя.
- `CallSessions` уже хранит CDR в Calls DB, а завершение звонка пишет системное сообщение в Messages: `Пропущенный звонок`, `Звонок отклонён`, `Звонок · m:ss`.
- В `Shared/BarkFluff.Proto/beacon_api.proto` есть `livekit_url = 13` и `Service calls = 14`, но Android-копия `Android/core/src/main/proto/beacon_api.proto` отстаёт: там нет `calls`.

### Web-клиент

Файлы:

- `Backend/BarkFluff.Web/wwwroot/js/app/calls.js` — сигналинг, reconnect/backoff, state-machine одного звонка.
- `Backend/BarkFluff.Web/wwwroot/js/app/calls-ui.js` — LiveKit Room, ring overlay, активный экран звонка, плитки участников, self PiP, screen share, device picker, качество.
- `Backend/BarkFluff.Web/wwwroot/js/app/main.js` — кнопки аудио/видео в шапке чата и запуск `BF.calls.start()`.

Возможности web:

- исходящий аудио- и видеозвонок из личного или группового чата;
- входящий ring overlay с accept/reject и рингтоном;
- полноэкранный активный звонок;
- микрофон, камера, демонстрация экрана, сброс;
- отдельные плитки камеры и screen share, self PiP;
- разворот плитки на весь экран;
- выбор микрофона/камеры;
- общее качество голоса через `SetCallAudioQuality`;
- локальное качество своего видео;
- групповые звонки на уровне room/events;
- `JoinCall` есть в JS API, но отдельного UI для late join не найдено;
- отдельного списка/истории звонков в web нет.

### Android-клиент

- `:core` уже содержит `calls_api.proto`, но `GrpcManager` не создаёт `CallsApi` client.
- В `GlobalParam` нет `socketCalls` и `livekitUrl`.
- `GrpcManager.ServerInfo` не содержит `callsEndpoint/livekitUrl`.
- V1 (`:app-v1`) уже умеет Firebase Messaging data-only payload для сообщений и локальные уведомления.
- V2 (`:app-v2`) — Compose + Material 3, но без FCM, notification side-effects и системных разрешений кроме `INTERNET`.

---

## 2. Допущения

1. Android должен повторить web-функции звонка, но список звонков — новая Android-фича, потому что web-истории звонков сейчас нет.
2. Для Android сначала делаем нативный Compose UI в `:app-v2`, а не WebView.
3. `:core` остаётся без LiveKit UI/media SDK: там только gRPC-сигналинг, модели состояния и use-cases. LiveKit Room и render видео — app-layer.
4. FCM wake-up требует небольшой backend-доработки: сейчас Calls не публикует push-события, а CloudMessaging не знает payload входящего звонка.
5. Экран/окно для демонстрации экрана на Android выбирается через системный `MediaProjection` consent flow; in-app bottom sheet может объяснить действие, но не должен заменять системное разрешение.

---

## 3. Целевой UX

### 3.1 Точки входа

- В `ChatScreen` добавить две иконки в `TopAppBar`: аудиозвонок и видеозвонок.
- Для личного чата отправлять `InitiateCall(callee_user_id, media_type)`.
- Для группового чата отправлять `InitiateCall(chat_id, media_type)`.
- Для активного группового звонка показывать компактный баннер в чате: аватарки/счётчик участников, текст `Идёт звонок`, кнопка `Присоединиться`.
- В `HomeScreen` добавить третий tab `Звонки` между `Чаты` и `Профиль`.

### 3.2 Экран списка звонков

Визуально — близко к Telegram, но на Material You 3:

- `LargeTopAppBar` с заголовком `Звонки`.
- Filter chips: `Все`, `Пропущенные`.
- `LazyColumn` с группировкой по датам: `Сегодня`, `Вчера`, дата.
- Строка звонка:
  - круглый аватар/группа;
  - имя собеседника или название группы;
  - иконка направления: входящий, исходящий, пропущенный;
  - тип: аудио/видео;
  - длительность или причина (`Пропущенный`, `Отклонён`);
  - время справа;
  - быстрые действия справа: аудио / видео.
- Тап по строке открывает чат, долгий тап открывает bottom sheet действий: `Позвонить`, `Видеозвонок`, `Открыть чат`, `Очистить запись` если backend позволит.
- Empty state без маркетингового текста: иконка телефона, короткая строка `Звонков пока нет`.

Технический нюанс: для полноценного списка нужен новый public RPC в Calls, например `ListCallHistory`. Временный вариант через системные сообщения слабее: он не даёт нормальную фильтрацию, направление, участников и активные звонки.

### 3.3 Входящий звонок

Foreground app:

- показывать full-screen Compose overlay/route `IncomingCallScreen`;
- крупный аватар, имя, `Аудиозвонок`/`Видеозвонок`, пульсирующий ring state;
- две большие кнопки: accept и reject, minimum touch target 48dp;
- после accept сразу перейти в `CallScreen`.

Background/killed app:

- CloudMessaging отправляет high-priority data-only `type=incoming_call`;
- Android показывает отдельное `CHANNEL_CALLS` notification с `NotificationCompat.CallStyle`;
- notification actions: `Ответить`, `Отклонить`;
- content/full-screen intent открывает `IncomingCallActivity` или route `incoming_call/{callId}`;
- на Android 14+ проверять `NotificationManager.canUseFullScreenIntent()` и давать fallback на heads-up, если full-screen intent выключен.

### 3.4 Активный экран разговора

Экран должен быть первым делом функциональным, не декоративным:

- полноэкранный `CallScreen`, без вложенных карточек;
- сверху: имя/название, статус (`Вызов...`, `Соединение...`, таймер), кнопка свернуть;
- центр:
  - ожидание собеседника: аватар + короткий статус;
  - 1-на-1: удалённое видео на весь доступный контейнер;
  - группа: адаптивная сетка плиток;
  - screen share — отдельная плитка, `object-fit` аналог — сохранять aspect ratio;
  - self camera — PiP в углу, только когда камера включена;
  - tap по плитке разворачивает её, повторный tap сворачивает;
  - speaking indicator — сначала использовать LiveKit active speakers, затем при необходимости локальный RMS-анализ как в web.
- снизу: иконки-кнопки микрофон, камера, демонстрация, качество, завершить.
- завершить — всегда error color / красная кнопка, остальные состояния через `primary`, `secondaryContainer`, `surfaceContainer`.

### 3.5 Bottom sheets и модалки

- `CallMediaPickerSheet`: перед началом или во время звонка выбрать `Камера` / `Экран`, если пользователь жмёт общую кнопку трансляции.
- `CameraPickerSheet`:
  - фронтальная/задняя камера;
  - если SDK отдаёт список устройств — показать список;
  - быстрый toggle camera on/off.
- `ScreenShareSheet`:
  - короткое предупреждение, что будет показано содержимое экрана;
  - кнопка `Начать демонстрацию`;
  - дальше запускать системный `MediaProjectionManager.createScreenCaptureIntent()`.
- `AudioDeviceSheet`:
  - на первом этапе не обещать ручной выбор output route, потому что LiveKit Android по умолчанию отдаёт audio routing системе;
  - можно показать состояние `Телефон` / `Громкая связь` / `Bluetooth`, если SDK/AudioManager позволяют надёжно читать route.
- `QualitySheet`:
  - `Голос · для всех`: Auto/Low/Medium/High, вызывает `SetCallAudioQuality`;
  - `Видео · ваш стрим`: Auto/360p/540p/720p, локально перепубликует camera track.

---

## 4. Android-архитектура

### 4.1 `:core`

Добавить пакет `com.barkfluff.client.calls`:

- `CallRepository`
  - `initiateDirect(calleeUserId, mediaType)`
  - `initiateGroup(chatId, mediaType)`
  - `accept(callId)`
  - `reject(callId)`
  - `join(callId)`
  - `end(callId)`
  - `setAudioQuality(callId, quality)`
  - `subscribeCallEvents()`
- `CallEventsService`
  - аналог `RealtimeService`, но только для `SubscribeCallEvents`;
  - `SharedFlow<CallEventModel>`;
  - reconnect/backoff, forced refresh после auth errors, token refresh before stream;
  - `currentCall: StateFlow<CallState?>`;
  - busy policy: если уже active call, новый incoming автоматически reject/end reason busy.
- `CallState`
  - `callId`, `role`, `phase`, `target`, `mediaType`, `livekitUrl`, `accessToken`, `audioQuality`, timestamps.

Изменить инфраструктуру:

- синхронизировать `Android/core/src/main/proto/beacon_api.proto` с `Shared/BarkFluff.Proto/beacon_api.proto`;
- добавить `socketCalls` и `livekitUrl` в `GlobalParam`;
- добавить `callsEndpoint/livekitUrl` в `GrpcManager.ServerInfo`;
- в `SelectServerActivity` и V2 `SelectServerViewModel` сохранять `callsEndpoint/livekitUrl`;
- в `GrpcManager` добавить `callsChannel`, `callsClient`, `createCallsClient`, `initAllClients`, `recreateAllClients`, `shutdown`;
- не добавлять LiveKit dependency в core.

### 4.2 `:app-v2`

Добавить:

- `di/AppContainer`: `callRepository`, `callEventsService`, `callController`.
- `ui/screens/calls/CallsScreen.kt` — список звонков.
- `ui/screens/call/IncomingCallScreen.kt`.
- `ui/screens/call/CallScreen.kt`.
- `ui/screens/call/CallViewModel.kt`.
- `calls/LiveKitCallEngine.kt` — Room lifecycle, tracks, camera/mic/screen toggles.
- `calls/CallNotificationHelper.kt` — channel, incoming/ongoing/dismiss notifications.
- `calls/CallActionReceiver.kt` — accept/reject/end из notification actions.
- `calls/CallForegroundService.kt` — ongoing call notification + foreground service types.

Навигация:

- `Routes.CALLS = "calls"`;
- `Routes.CALL = "call/{callId}"`;
- `Routes.INCOMING_CALL = "incoming_call/{callId}"`;
- в `HomeScreen` добавить третий `NavigationBarItem`.

### 4.3 `:app-v1`

После готовности core:

- новый пакет `com.barkfluff.client.calls`;
- `CallActivity` для активного звонка;
- `IncomingCallActivity` или dialog overlay;
- `CallsFragment` и третий пункт `BottomNavigationView`;
- кнопки аудио/видео в `ChatActivity`;
- переиспользовать V1 `NotificationHelper`/`BarkFluffFirebaseMessagingService` для call payload.

---

## 5. Backend-доработки для Android

### 5.1 Push входящего звонка

Добавить RabbitMQ event, например `IncomingCallPushEvent`:

- `call_id`;
- `caller_user_id`;
- `recipient_user_ids`;
- `chat_id`;
- `media_type`;
- `started_at`;
- опционально `caller_name/avatar_url`, `chat_title/avatar_url` или получать их в CloudMessaging как для сообщений.

В `BarkFluff.Calls`:

- публиковать событие из `RingAsync`;
- при `Accepted/Rejected/Ended/Timeout` публиковать dismiss event, чтобы убрать notification с устройств без активного stream.

В `Barkfluff.CloudMessaging`:

- consumer входящего звонка;
- data-only FCM payload:
  - `type=incoming_call`;
  - `call_id`;
  - `caller_user_id`;
  - `chat_id`;
  - `media_type`;
  - `started_at`;
  - `caller_name`;
  - `avatar_url`;
  - `chat_title`;
- dismiss payload:
  - `type=dismiss_call`;
  - `call_id`;
  - `reason`.

### 5.2 История звонков

Добавить в `calls_api.proto`:

- `ListCallHistory(page, filter)` -> `CallHistoryItem[]`;
- опционально `GetActiveCalls(chat_ids)` для баннера `Присоединиться`.

`CallHistoryItem`:

- `call_id`;
- `chat_id`;
- `peer_user_id`;
- `is_group`;
- `media_type`;
- `direction`;
- `end_reason`;
- `started_at`;
- `answered_at`;
- `ended_at`;
- `duration_seconds`;
- `participant_user_ids`.

Проверка: Android может построить `CallsScreen` без парсинга системных сообщений.

---

## 6. Разрешения и системная интеграция

V2 manifest:

- `POST_NOTIFICATIONS`;
- `RECORD_AUDIO`;
- `CAMERA`;
- `FOREGROUND_SERVICE`;
- `FOREGROUND_SERVICE_MICROPHONE`;
- `FOREGROUND_SERVICE_CAMERA`;
- `FOREGROUND_SERVICE_MEDIA_PROJECTION`;
- `USE_FULL_SCREEN_INTENT`;
- `BLUETOOTH_CONNECT` для Android 12+, если будет показываться/управляться Bluetooth route.

Runtime permissions:

- перед первым звонком: microphone;
- перед включением камеры: camera;
- перед notification UX: notifications;
- перед screen share: системный MediaProjection intent.

Foreground service:

- входящий ring может жить на high-priority notification;
- активный звонок должен иметь ongoing foreground notification;
- при включении screen share использовать media projection service type.

---

## 7. Material You 3 правила для UI

Опора: `Android/Barkfluff.Client.Android/docs/material_you_3_guide.md`.

- Только `MaterialTheme.colorScheme.*`, без hardcoded цветов кроме семантического error для hangup.
- Dynamic Color включён в V2 theme; fallback уже есть через `BarkFluffTheme`.
- Compact: bottom navigation; Medium/Expanded: NavigationRail или NavigationSuiteScaffold.
- Touch target минимум 48dp.
- Кнопки управления звонком — `IconButton`/`FilledIconButton`, не текстовые прямоугольники.
- Bottom sheets для выбора устройств/качества, dialogs только для опасных действий.
- Типографика: `titleLarge/titleMedium` в call headers, без display-scale внутри рабочих панелей.
- Экран звонка — full-screen layout, не карточка в карточке.
- Font scale 200%: controls не должны перекрываться; таймер/имя должны ellipsize.
- Accessibility: contentDescription для всех icon-only actions, состояние mute/camera/screen share озвучивать через semantics.

---

## 8. Фазы реализации

### Фаза 0 — синхронизация контрактов

1. Обновить Android proto из `Shared/BarkFluff.Proto`: `beacon_api.proto`, `calls_api.proto`.
2. Добавить `socketCalls/livekitUrl` в `GlobalParam`.
3. Добавить Calls endpoint в `GrpcManager` и сохранение из SelectServer.

Проверка: `./gradlew :core:assembleDebug :app-v2:assembleDebug`, `ServerInfo` содержит Calls и LiveKit.

### Фаза 1 — call signaling в core

1. `CallRepository`.
2. `CallEventsService` с reconnect/backoff.
3. State machine одного звонка: incoming, ringing, connecting, active, ended.
4. Unit tests на state transitions, если в Android-модуле уже есть тестовая инфраструктура; иначе минимальные JVM tests для pure state reducer.

Проверка: два Android-клиента получают incoming через stream, accept на одном устройстве гасит ring на другом.

### Фаза 2 — LiveKit engine

1. Добавить `io.livekit:livekit-android` и при необходимости `livekit-android-camerax`.
2. `LiveKitCallEngine`: connect/disconnect, publish mic/camera, screen share, track events.
3. Маппинг LiveKit participants/tracks в UI model.
4. Audio quality через server event; video quality локально.

Проверка: Android ↔ web аудио/видео звонок, mute/camera/screen share работают.

### Фаза 3 — активный экран звонка

1. `CallScreen`.
2. Плитки участников, self PiP, waiting state, timer.
3. Controls: mic, camera, screen, quality, hangup.
4. Bottom sheets: camera/screen/quality.

Проверка: ручной сценарий 1-на-1 и группа 3 участника; rotation/multi-window не ломают звонок.

### Фаза 4 — входящий звонок и FCM

1. Backend call push events + CloudMessaging consumer.
2. V2 Firebase dependency/plugin и `FirebaseMessagingService`.
3. `CHANNEL_CALLS`, `CallStyle`, full-screen intent fallback.
4. Notification actions accept/reject.
5. Dismiss notification на accepted/rejected/ended/missed.

Проверка: входящий звонок приходит при foreground, background и killed app; Android 14+ без full-screen permission показывает heads-up fallback.

### Фаза 5 — список звонков

1. Backend `ListCallHistory`.
2. `CallsScreen` с фильтрами и быстрыми действиями.
3. `GetActiveCalls` или другой источник для баннера `Присоединиться`.

Проверка: завершённые, пропущенные, отклонённые и групповые звонки отображаются корректно.

### Фаза 6 — V1 parity

1. Добавить V1 UI поверх готового core.
2. Подключить call notifications к существующему `NotificationHelper`.
3. Добавить третий bottom nav item.

Проверка: V1 собирается и умеет принять/совершить звонок с V2/web.

---

## 9. QA-сценарии

- 1-на-1 audio Android -> web.
- 1-на-1 video web -> Android.
- Android -> Android video.
- Группа 3 участника, late join.
- Screen share Android -> web и web -> Android.
- Выключение/включение mic/camera во время звонка.
- Смена общего audio quality, событие доходит всем.
- Локальная смена video quality не ходит на backend.
- Звонок отклонён на одном устройстве, ring гаснет на остальных.
- Входящий при foreground/background/killed app.
- Пропущенный по timeout.
- Потеря сети и reconnect.
- Отзыв сессии во время stream.
- Android 13 без `POST_NOTIFICATIONS`.
- Android 14+ без full-screen intent permission.
- Font scale 200%, TalkBack, landscape, tablet width.

---

## 10. Источники и проверенная документация

- Obsidian: `Backend/Calls.md`, `Backend/CloudMessaging.md`, `Клиенты/Web.md`, `Клиенты/Android.md`, `Клиенты/Android-V2.md`.
- Web implementation: `calls.js`, `calls-ui.js`, `main.js`, `messenger.html`.
- Android Material guide: `Android/Barkfluff.Client.Android/docs/material_you_3_guide.md`.
- LiveKit Android SDK docs: https://github.com/livekit/client-sdk-android
- Android CallStyle notifications: https://developer.android.com/develop/ui/compose/notifications/call-style
- Android time-sensitive notifications: https://developer.android.com/develop/ui/views/notifications/time-sensitive
- Android 14 full-screen intent policy: https://developer.android.com/about/versions/14/behavior-changes-14#secure-full-screen-intent-notifications
- Firebase Android Messaging API notes checked via Context7: `/firebase/firebase-android-sdk`.
