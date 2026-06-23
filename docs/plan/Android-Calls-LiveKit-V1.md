# План реализации звонков в Android V1

> Цель: перенести в Android V1 функциональность звонков из web-клиента и добавить Android-специфичный входящий звонок через Firebase push. Целевая реализация — только `:app-v1` на XML/ViewBinding; общий сигналинг и use-case слой — в `:core`. `:app-v2` является тестовым проектом и не должен изменяться в рамках этой задачи.

---

## 0. Статус выполнения

### Сделано на текущем этапе

- V1-only ограничение зафиксировано: изменения идут в `Android/Barkfluff.Client.Android/app` и `Android/core`, `Android/Barkfluff.ClientV2.Android` не трогается.
- Android Beacon proto синхронизирован по Calls: добавлен `Service calls = 14` рядом с `livekit_url = 13`.
- В `GlobalParam` добавлены `socketCalls` и `livekitUrl`; `SelectServerActivity` сохраняет их из Beacon, `AboutActivity` показывает в диагностике.
- `GrpcManager` создаёт `CallsApi` client, хранит calls channel/client и пересоздаёт его вместе с остальными gRPC-клиентами.
- В `core` добавлен тонкий `CallRepository` для `InitiateCall`, `AcceptCall`, `RejectCall`, `JoinCall`, `EndCall`, `SetCallAudioQuality`, `SubscribeCallEvents`.
- В V1 `ChatActivity` добавлены кнопки аудио/видео звонка; они запускают signaling и открывают `CallActivity` с LiveKit-подключением.
- Добавлены базовые V1-компоненты входящего/активного звонка: `IncomingCallActivity`, `CallActivity`, `CallActionReceiver`.
- FCM V1 обрабатывает `incoming_call` и `dismiss_call`; `NotificationHelper` создаёт канал `calls` и показывает `NotificationCompat.CallStyle`.
- В V1 manifest добавлены call permissions и регистрация новых activity/receiver.
- В `:app-v1` подключён LiveKit Android SDK `2.26.0` + `livekit-android-camerax`.
- В `core` добавлен `CallEventsService`: lifecycle-подписка на `SubscribeCallEvents`, `StateFlow` текущего звонка/connection state, reconnect/backoff, forced token refresh после повторных ошибок и busy policy с auto-reject второго входящего звонка.
- `CallActivity` теперь подключается к LiveKit room через `LiveKitCallEngine`: запрашивает mic/camera permissions, публикует микрофон/камеру, показывает удалённый video track, базовый self PiP, запускает системный screen share intent и даёт bottom sheet качества голоса.
- Добавлены M3-style icon controls для микрофона, камеры, демонстрации, качества и завершения звонка.
- Добавлен `CallForegroundService` для ongoing notification активного звонка с foreground service types `microphone|camera|mediaProjection`; notification action умеет завершать активный звонок через `CallActionReceiver`.
- Проверка: `./gradlew :app-v1:assembleDebug` проходит успешно. В логе остаются D8/R8 warnings по Kotlin metadata и warning по strip `liblkjingle_peerconnection_so.so`, сборку они не блокируют.

### Осталось сделать

- Довести LiveKit-слой до production: расширить `LiveKitCallEngine`, добавить participant grid и обработку сложных disconnect/reconnect сценариев.
- Расширить `CallActivity` до полноценного экрана разговора: плитки участников, таймер, адаптивная сетка, отдельные bottom sheets камеры/экрана/качества.
- Довести state-machine звонков до UI-интеграции: открытие incoming UI из stream, завершение ring на других устройствах, синхронизация active/ended состояния с `CallActivity`.
- Доработать backend push для входящих звонков и dismiss-событий, если соответствующие events ещё не публикуются.
- Реализовать список звонков после появления backend `ListCallHistory`/источника истории.
- Добавить bottom navigation пункт `Звонки` и проверить constraints phone/tablet layouts.
- Добавить foreground service для активного звонка и screen-share, когда появится LiveKit media слой.
- Провести ручной QA Android V1 ↔ web/Android V1 для audio/video, background/killed incoming, Android 14 full-screen fallback.

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

### Android V1

- `:app-v1` — основной целевой Android-клиент для этой задачи.
- V1 уже имеет Firebase Messaging data-only payload для сообщений и локальные уведомления.
- V1 уже показывает системные сообщения внутри чата, поэтому итоги звонков могут появляться в переписке после backend-системного сообщения.
- V1 получил стартовый signaling/UI слой: `CallsApi` wiring, кнопки аудио/видео в чате, базовые incoming/active call activity и call notifications. Полноценный LiveKit media UI и экран списка звонков ещё не реализованы.
- Floating bottom navigation в `activity_main.xml` сейчас рассчитан на два пункта; добавление `Звонки` требует проверки phone и `layout-w600dp`.

### Android V2

- `:app-v2` не изменять: это тестовый Compose-проект.
- Не добавлять туда routes, screens, Firebase service, LiveKit dependency, permissions или Material You call UI.
- Можно использовать V2 только как справочный источник по теме/стилю, если это не приводит к изменениям файлов V2.

---

## 2. Допущения

1. Android должен повторить web-функции звонка, но список звонков — новая Android-фича, потому что web-истории звонков сейчас нет.
2. Реализация UI делается в `:app-v1` на XML/ViewBinding.
3. `:core` содержит только gRPC-сигналинг, модели состояния и use-cases. LiveKit Room и render видео остаются в `:app-v1`.
4. FCM wake-up требует backend-доработки: сейчас Calls не публикует push-события, а CloudMessaging не знает payload входящего звонка.
5. Экран/окно для демонстрации экрана на Android выбирается через системный `MediaProjection` consent flow; in-app bottom sheet может объяснить действие, но не заменяет системное разрешение.

---

## 3. Целевой UX для V1

### 3.1 Точки входа

- В `ChatActivity` добавить две иконки в toolbar/header: аудиозвонок и видеозвонок.
- Для личного чата отправлять `InitiateCall(callee_user_id, media_type)`.
- Для группового чата отправлять `InitiateCall(chat_id, media_type)`.
- Для активного группового звонка показывать компактный баннер в чате: аватарки/счётчик участников, текст `Идёт звонок`, кнопка `Присоединиться`.
- В `BottomNavigationView` добавить третий пункт `Звонки`.

### 3.2 Экран списка звонков

Визуально — близко к Telegram, но в стиле V1 приложения и с опорой на Material You 3 guide:

- экран `CallsFragment`;
- toolbar с заголовком `Звонки`;
- фильтры `Все` и `Пропущенные`;
- `RecyclerView` с группировкой по датам: `Сегодня`, `Вчера`, дата;
- строка звонка:
  - круглый аватар/группа;
  - имя собеседника или название группы;
  - иконка направления: входящий, исходящий, пропущенный;
  - тип: аудио/видео;
  - длительность или причина (`Пропущенный`, `Отклонён`);
  - время справа;
  - быстрые действия справа: аудио / видео;
- тап по строке открывает чат;
- долгий тап открывает bottom sheet действий: `Позвонить`, `Видеозвонок`, `Открыть чат`, `Очистить запись` если backend позволит;
- empty state: иконка телефона и короткая строка `Звонков пока нет`.

Технический нюанс: для полноценного списка нужен новый public RPC в Calls, например `ListCallHistory`. Временный вариант через системные сообщения слабее: он не даёт нормальную фильтрацию, направление, участников и активные звонки. Для личного звонка без существующего личного чата backend сейчас также может не написать системное сообщение в чат.

### 3.3 Входящий звонок

Foreground app:

- показывать `IncomingCallActivity` или полноэкранный dialog overlay;
- крупный аватар, имя, `Аудиозвонок`/`Видеозвонок`, ring state;
- две большие кнопки: accept и reject, minimum touch target 48dp;
- после accept переходить в `CallActivity`.

Background/killed app:

- CloudMessaging отправляет high-priority data-only `type=incoming_call`;
- V1 показывает отдельный `CHANNEL_CALLS` notification с `NotificationCompat.CallStyle`;
- notification actions: `Ответить`, `Отклонить`;
- content/full-screen intent открывает `IncomingCallActivity`;
- на Android 14+ проверять `NotificationManager.canUseFullScreenIntent()` и давать fallback на heads-up, если full-screen intent выключен.

### 3.4 Активный экран разговора

- `CallActivity` full-screen, без вложенных карточек;
- сверху: имя/название, статус (`Вызов...`, `Соединение...`, таймер), кнопка свернуть;
- центр:
  - ожидание собеседника: аватар + короткий статус;
  - 1-на-1: удалённое видео на весь доступный контейнер;
  - группа: адаптивная сетка плиток;
  - screen share — отдельная плитка с сохранением aspect ratio;
  - self camera — PiP в углу, только когда камера включена;
  - tap по плитке разворачивает её, повторный tap сворачивает;
  - speaking indicator — сначала использовать LiveKit active speakers, затем при необходимости локальный RMS-анализ как в web;
- снизу: иконки-кнопки микрофон, камера, демонстрация, качество, завершить;
- завершить — error color / красная кнопка, остальные состояния через Material color roles из темы.

### 3.5 Bottom sheets и модалки

- `CallMediaPickerBottomSheet`: выбрать `Камера` / `Экран`, если пользователь жмёт общую кнопку трансляции.
- `CameraPickerBottomSheet`:
  - фронтальная/задняя камера;
  - если SDK отдаёт список устройств — показать список;
  - быстрый toggle camera on/off.
- `ScreenShareBottomSheet`:
  - короткое предупреждение, что будет показано содержимое экрана;
  - кнопка `Начать демонстрацию`;
  - дальше запускать системный `MediaProjectionManager.createScreenCaptureIntent()`.
- `AudioDeviceBottomSheet`:
  - на первом этапе не обещать ручной выбор output route, потому что LiveKit Android по умолчанию отдаёт audio routing системе;
  - можно показать состояние `Телефон` / `Громкая связь` / `Bluetooth`, если SDK/AudioManager позволяют надёжно читать route.
- `QualityBottomSheet`:
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
- в `SelectServerActivity` сохранять `callsEndpoint/livekitUrl`;
- в `GrpcManager` добавить `callsChannel`, `callsClient`, `createCallsClient`, `initAllClients`, `recreateAllClients`, `shutdown`;
- не добавлять LiveKit dependency в core.

### 4.2 `:app-v1`

Добавить:

- `calls/CallActivity.kt` — активный звонок;
- `calls/IncomingCallActivity.kt` — входящий звонок из app/notification;
- `calls/CallsFragment.kt` — список звонков;
- `calls/CallsAdapter.kt` — элементы истории;
- `calls/CallViewModel.kt` — состояние экрана и команды;
- `calls/LiveKitCallEngine.kt` — Room lifecycle, tracks, camera/mic/screen toggles;
- `calls/CallNotificationHelper.kt` — channel, incoming/ongoing/dismiss notifications;
- `calls/CallActionReceiver.kt` — accept/reject/end из notification actions;
- `calls/CallForegroundService.kt` — ongoing call notification + foreground service types.

Изменить:

- `ChatActivity` — кнопки аудио/видео и баннер активного группового звонка;
- `MainActivity`/bottom navigation — третий пункт `Звонки`;
- `activity_main.xml` и `layout-w600dp` — проверить ширину/constraints bottom navigation под три пункта;
- `NotificationHelper` — добавить `CHANNEL_CALLS` или вынести call helper рядом;
- `BarkFluffFirebaseMessagingService` — обработать `incoming_call` и `dismiss_call`;
- `AndroidManifest.xml` — activity, service, receiver, permissions.

### 4.3 `:app-v2`

Не трогать:

- не добавлять зависимости;
- не менять manifest;
- не добавлять routes/screens/ViewModels;
- не переносить туда Firebase или LiveKit wiring.

---

## 5. Backend-доработки для Android V1

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

Проверка: Android V1 может построить `CallsFragment` без парсинга системных сообщений.

---

## 6. Разрешения и системная интеграция V1

V1 manifest:

- `POST_NOTIFICATIONS`;
- `RECORD_AUDIO`;
- `CAMERA`;
- `FOREGROUND_SERVICE`;
- `FOREGROUND_SERVICE_MICROPHONE`;
- `FOREGROUND_SERVICE_CAMERA`;
- `FOREGROUND_SERVICE_MEDIA_PROJECTION`;
- `USE_FULL_SCREEN_INTENT`;
- `BLUETOOTH_CONNECT` для Android 12+, если будет показываться/управляться Bluetooth route;
- опционально `FOREGROUND_SERVICE_PHONE_CALL` + `MANAGE_OWN_CALLS`, если отдельной фазой идти в Telecom/ConnectionService. Для обычного in-app LiveKit экрана это не базовое требование.

Runtime permissions:

- перед первым звонком: microphone;
- перед включением камеры: camera;
- перед notification UX: notifications;
- перед screen share: системный MediaProjection intent.

Foreground service:

- входящий ring может жить на high-priority notification;
- активный звонок должен иметь ongoing foreground notification;
- при включении screen share использовать media projection service type;
- если понадобится системный self-managed call через Telecom, это отдельная фаза: `phoneCall` foreground service type требует `MANAGE_OWN_CALLS` или роль default dialer.

---

## 7. Material You 3 правила для V1 UI

Опора: `Android/Barkfluff.Client.Android/docs/material_you_3_guide.md`.

- Использовать Material components и цветовые роли темы, без hardcoded цветов кроме семантического error для hangup.
- Touch target минимум 48dp.
- Кнопки управления звонком — icon buttons, не текстовые прямоугольники.
- Bottom sheets для выбора устройств/качества, dialogs только для опасных действий.
- Экран звонка — full-screen layout, не карточка в карточке.
- Font scale 200%: controls не должны перекрываться; таймер/имя должны ellipsize.
- Accessibility: contentDescription для всех icon-only actions, состояние mute/camera/screen share озвучивать через semantics.

---

## 8. Фазы реализации

### Фаза 0 — синхронизация контрактов — сделано

1. Обновить Android proto из `Shared/BarkFluff.Proto`: `beacon_api.proto`, `calls_api.proto`.
2. Добавить `socketCalls/livekitUrl` в `GlobalParam`.
3. Добавить Calls endpoint в `GrpcManager` и сохранение из `SelectServerActivity`.

Проверка: `./gradlew :core:assembleDebug :app-v1:assembleDebug`, `ServerInfo` содержит Calls и LiveKit.

### Фаза 1 — call signaling в core — базово сделано

1. `CallRepository`.
2. `CallEventsService` с reconnect/backoff.
3. State machine одного звонка: incoming, ringing, connecting, active, ended.
4. Unit tests на state transitions, если в Android-модуле уже есть тестовая инфраструктура; иначе минимальные JVM tests для pure state reducer — ещё нужно.

Проверка: `./gradlew :app-v1:assembleDebug` проходит; ручной multi-device QA stream/accept/dismiss ещё нужен.

### Фаза 2 — LiveKit engine в V1 — частично сделано

1. Добавить `io.livekit:livekit-android` и при необходимости `livekit-android-camerax` в `:app-v1` — сделано (`2.26.0`).
2. `LiveKitCallEngine`: connect/disconnect, publish mic/camera, screen share, track events — базово сделано.
3. Маппинг LiveKit participants/tracks в V1 UI model.
4. Audio quality через server event; video quality локально.

Проверка: Android V1 ↔ web аудио/видео звонок, mute/camera/screen share работают.

### Фаза 3 — активный экран звонка — начато

1. `CallActivity`.
2. Плитки участников, self PiP, waiting state, timer — self PiP и waiting state базово сделаны; сетка и таймер ещё нужны.
3. Controls: mic, camera, screen, quality, hangup — базово сделано.
4. Bottom sheets: camera/screen/quality — базово сделано для выбора camera/screen и качества голоса; отдельные device sheets ещё нужны.
5. Foreground service активного звонка и ongoing notification — базово сделано.

Проверка: ручной сценарий 1-на-1 и группа 3 участника; rotation/multi-window не ломают звонок.

### Фаза 4 — входящий звонок и FCM — частично сделано

1. Backend call push events + CloudMessaging consumer.
2. V1 Firebase handling для `incoming_call`/`dismiss_call`.
3. `CHANNEL_CALLS`, `CallStyle`, full-screen intent fallback.
4. Notification actions accept/reject.
5. Dismiss notification на accepted/rejected/ended/missed.

Проверка: входящий звонок приходит при foreground, background и killed app; Android 14+ без full-screen permission показывает heads-up fallback.

### Фаза 5 — список звонков

1. Backend `ListCallHistory`.
2. `CallsFragment` с фильтрами и быстрыми действиями.
3. `GetActiveCalls` или другой источник для баннера `Присоединиться`.

Проверка: завершённые, пропущенные, отклонённые и групповые звонки отображаются корректно.

---

## 9. QA-сценарии

- 1-на-1 audio Android V1 -> web.
- 1-на-1 video web -> Android V1.
- Android V1 -> Android V1 video.
- Группа 3 участника, late join.
- Screen share Android V1 -> web и web -> Android V1.
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

- Obsidian: `Backend/Calls.md`, `Backend/CloudMessaging.md`, `Клиенты/Web.md`, `Клиенты/Android.md`.
- Web implementation: `calls.js`, `calls-ui.js`, `main.js`, `messenger.html`.
- Android Material guide: `Android/Barkfluff.Client.Android/docs/material_you_3_guide.md`.
- LiveKit Android SDK docs: https://github.com/livekit/client-sdk-android
- Android CallStyle notifications: https://developer.android.com/develop/ui/compose/notifications/call-style
- Android time-sensitive notifications: https://developer.android.com/develop/ui/views/notifications/time-sensitive
- Android foreground service types: https://developer.android.com/develop/background-work/services/fgs/service-types
- Android 14 full-screen intent policy: https://developer.android.com/about/versions/14/behavior-changes-14#secure-full-screen-intent-notifications
- Firebase Android Messaging API notes checked via Context7: `/firebase/firebase-android-sdk`.
