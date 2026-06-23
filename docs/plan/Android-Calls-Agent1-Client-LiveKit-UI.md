# План для агента 1: Android V1 звонки, LiveKit UI и UX разговора

> Цель: довести клиентскую часть звонков Android V1 до production-уровня без изменений `Android/Barkfluff.ClientV2.Android`. Агент 1 работает только в `Android/Barkfluff.Client.Android/app`, при необходимости в `Android/core` для уже существующего call state/use-case слоя, и обновляет Obsidian `Клиенты/Android.md`.

---

## Границы ответственности

### Делать

- Полноценный экран разговора `CallActivity` в стиле Material You 3.
- Улучшение `LiveKitCallEngine`: участники, tracks, reconnect/disconnect, remote screen share.
- UI state-machine: синхронизация `CallEventsService` с `IncomingCallActivity`, `CallActivity`, foreground notification.
- Bottom sheets выбора камеры/экрана/качества/аудио-route, если это возможно надёжно.
- Android-only QA сценарии и фиксы V1.

### Не делать

- Не трогать `Android/Barkfluff.ClientV2.Android`.
- Не менять backend, shared proto и CloudMessaging контракты, кроме случаев, явно согласованных с агентом 2.
- Не реализовывать историю звонков без готового backend RPC. Для списка можно оставить заглушку/адаптер-контракт, но не парсить системные сообщения как основной источник.

---

## Текущее состояние

- Calls endpoint и LiveKit URL приходят из Beacon и сохраняются в `GlobalParam`.
- `CallRepository`, `CallEventsService`, `CallActivity`, `IncomingCallActivity`, `CallActionReceiver`, `CallForegroundService` уже есть.
- LiveKit SDK подключён в `:app-v1`.
- `CallActivity` умеет подключаться к LiveKit, публиковать mic/camera/screen share, показывать один remote renderer, self PiP, таймер и базовые controls.
- Вкладка `Звонки` есть, но реальные записи ждут работу агента 2.

---

## Этапы работ

### Этап 1. Аудит текущего клиента

1. Прочитать:
   - `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/calls/CallActivity.kt`
   - `Android/Barkfluff.Client.Android/app/src/main/java/com/barkfluff/client/calls/LiveKitCallEngine.kt`
   - `Android/core/src/main/java/com/barkfluff/client/calls/CallEventsService.kt`
   - `Android/Barkfluff.Client.Android/docs/material_you_3_guide.md`
   - `Obsidian/ClaudeVault/Клиенты/Android.md`
2. Зафиксировать реальные gaps перед правками: какие states/events уже есть, какие UI элементы уже работают.

Проверка: короткая заметка в PR/финальном сообщении, какие участки кода менялись и почему.

### Этап 2. Модель участников и tracks

1. Расширить `LiveKitCallEngine` так, чтобы он отдавал UI-модель участников:
   - participant id/name, local/remote;
   - camera track;
   - screen share track;
   - audio muted/video enabled;
   - speaking/connection quality, если SDK отдаёт эти сигналы.
2. Не хранить Android `View` как долгоживущую бизнес-модель. UI должен получать состояние и сам привязывать renderers.
3. Обработать join/leave/track published/unpublished/subscribed/unsubscribed.

Проверка: `:app-v1:assembleDebug`; ручной сценарий 1-на-1 с включением/выключением камеры.

### Этап 3. Полноценный экран разговора

1. Заменить single-remote layout на адаптивную область:
   - 1-на-1: основной remote video или waiting state;
   - группа: grid 2/3/4+ участников;
   - screen share: отдельная приоритетная плитка с aspect ratio;
   - self camera: PiP только при включённой камере.
2. Добавить tap-to-focus плитки и возврат к grid.
3. Убедиться, что controls не перекрывают видео и работают при font scale 200%.

Проверка: ручной QA portrait/landscape, малый экран и tablet/w600dp.

### Этап 4. Bottom sheets и controls

1. Довести bottom sheets:
   - выбор `Камера` / `Экран`;
   - выбор фронтальной/задней камеры или доступного устройства, если SDK позволяет;
   - предупреждение и запуск system `MediaProjection` для screen share;
   - качество голоса через `SetCallAudioQuality`;
   - локальное качество видео, если SDK позволяет перепубликовать track стабильно.
2. Добавить contentDescription и визуальные selected/disabled states для icon controls.
3. Не обещать ручной audio output route, если LiveKit/AudioManager не дают надёжного управления.

Проверка: mic/camera/screen/quality controls работают без крашей при повторных переключениях.

### Этап 5. State-machine и lifecycle

1. Синхронизировать `CallEventsService.currentCall` с `CallActivity`:
   - accepted/joined открывает или обновляет active UI;
   - rejected/ended закрывает или переводит экран в ended state;
   - late join показывает корректный connecting/active state.
2. Доработать reconnect/disconnect:
   - временная потеря сети не сбрасывает таймер;
   - terminal disconnect останавливает foreground service;
   - повторный accept/reject/end idempotent на UI уровне.
3. Улучшить имена/аватары входящего звонка, если данные есть в event/payload. Если данных нет, оставить понятный fallback.

Проверка: входящий звонок foreground, accept/reject/end с двух устройств, потеря сети.

### Этап 6. Android QA и полировка

1. Проверить сценарии:
   - Android V1 -> web audio/video;
   - web -> Android V1 audio/video;
   - Android V1 -> Android V1 video;
   - screen share Android -> web;
   - background/foreground transition во время звонка;
   - Android 13 notification permission;
   - Android 14 full-screen intent fallback.
2. Обновить `Obsidian/ClaudeVault/Клиенты/Android.md`.
3. Обновить общий план `docs/plan/Android-Calls-LiveKit-V1.md`, если пункты закрыты.

Проверка: `./gradlew.bat :app-v1:assembleDebug`.

---

## Ожидаемый результат

- `CallActivity` пригоден для 1-на-1 и групповых звонков.
- LiveKit tracks корректно отображаются, отключаются и восстанавливаются.
- Foreground service/notification и UI lifecycle не расходятся.
- V2 не изменён.

---

## Координация с агентом 2

- Агент 1 ждёт от агента 2 контракт истории звонков (`ListCallHistory`) и активных звонков (`GetActiveCalls` или аналог) перед полноценным списком звонков/баннером join.
- Агент 1 не меняет FCM payload-формат без согласования.
- Если в Android нужен новый proto или новое поле события, сначала зафиксировать контракт в плане агента 2.
