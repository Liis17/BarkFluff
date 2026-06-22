# Web

Веб-клиент мессенджера BarkFluff — **vanilla-JS SPA** (без фреймворка и бандлера приложения), раздаётся хостом [[Backend/BarkFluff.Web]].

Расположение: `Backend/BarkFluff.Web/wwwroot/` (разметка `messenger.html`, модули `js/app/*.js`).

> ⚠️ Раньше существовало React-переписывание (`Frontend/Web/`), но оно было **намеренно откатано** (коммиты «возвращаемся на старую вебверсию» / «Восстановить веб-версию мессенджера (vanilla, до React)»). Актуальный и развиваемый клиент — этот vanilla-вариант. React-доку считать неактуальной.

## Tech Stack

| Технология | Версия | Назначение |
|------------|--------|-----------|
| grpc-web | 1.5.0 | gRPC-Web клиенты (`protoc-gen-grpc-web`, callback-style) |
| google-protobuf | 3.21.2 | runtime сообщений |
| esbuild | 0.24.0 | сборка proto-бандла и LiveKit-бандла в IIFE-глобал |
| livekit-client | 2.19.2 | WebRTC SDK для звонков ([[Backend/Calls]]) |

Приложение — обычные `<script>`-модули, каждый оборачивает себя в IIFE и вешает API на глобал `window.BF.*`.

## Сборка

```bash
cd Backend/BarkFluff.Web
# proto-бандл (window.barkfluff + window.proto.barkfluff.*)
pwsh scripts/generate-proto.ps1      # Windows
bash scripts/generate-proto.sh       # Linux/macOS (нужен protoc-gen-grpc-web в PATH)
# LiveKit JS SDK (window.LivekitClient)
pwsh scripts/vendor-livekit.ps1      # либо bash scripts/vendor-livekit.sh
```

Бандлы коммитятся в `wwwroot/js/proto/` и `wwwroot/js/vendor/`. В Docker-сборке proto-бандл пересобирается заново из `scripts/proto-bundle-index.js` и **перезаписывает** закоммиченный — поэтому список proto держим синхронным в трёх местах: `generate-proto.*`, `proto-bundle-index.js` и `protoc`-списках в `Dockerfile`/`Dockerfile.slim`.

## Архитектура

### Транспорт и авторизация
- gRPC-Web клиенты создаются в `js/app/clients.js`: `new window.barkfluff.<Service>ApiClient(origin)`, складываются в `BF.clients` (`identity/users/messages/files/updates/onliner/fastAuth/calls`).
- `BF.clients.authCall(method, req)` — унарный вызов с авто-рефрешем токена и ретраем при `UNAUTHENTICATED` (код 16).
- `js/app/metadata.js` (`BF.metadata.build(token)`) формирует метаданные: `x-auth-token` (plain) + base64 `x-device-id`/`x-device-name`/`x-os-name`/`x-app-name`/`x-app-version`. Device-id — из `js/app/device.js` (localStorage `barkfluff_device_id`).
- `js/app/tokens.js` (`BF.tokens`) — хранение/refresh токенов.

### Real-time
- `js/app/realtime.js` (`BF.realtime`) — server-streaming подписки [[Backend/Updates|UpdatesApi]] (new/read/edited/deleted/pinned/...) и [[Backend/Onliner|OnlinerApi]]. Реконнект с backoff 2→30с, превентивный age-timer (180с), watchdog по молчанию (90с), реакция на `visibilitychange`, forced refresh при коде 16, событие `resync` для дозагрузки пропущенного.
- События раздаются через `BF.realtime.on(event, cb)`; UI слушает их в `js/app/main.js`.

### Звонки (см. [[Backend/Calls]])
- `js/app/calls.js` (`BF.calls`) — сигнализация: device-scope стрим `SubscribeCallEvents` (паттерн `realtime.js`), call-control `InitiateCall/Accept/Reject/Join/End`, машина состояний одного звонка, события `incoming/connect/peer_accepted/peer_rejected/ring_dismiss/ended/member`. Запускается в `main.js` рядом с `BF.realtime.startAll()`.
- `js/app/calls-ui.js` (`BF.callsUI`) — UI: ринг-оверлей входящего, полноэкранный экран активного звонка (сетка плиток + контролы микро/камера/демонстрация/сброс), рингтон (WebAudio), медиа через `window.LivekitClient` (Room: connect, публикация треков, привязка `TrackSubscribed`/`ParticipantConnected` к плиткам). Имя/аватар участника резолвится через `BF.api.getUser` (identity LiveKit-токена = userId).
- Кнопки звонка — в шапке чата (`#chatHeader`), обработчики в `main.js` (1-на-1 → `callee_user_id`, группа → `chat_id`).
- LiveKit-бандл: `wwwroot/js/vendor/livekit-client.bundle.js` (esbuild IIFE, `window.LivekitClient`).

### Хост и маршрутизация
- [[Backend/BarkFluff.Web]] (`Program.cs`) — YARP: на каждый gRPC-сервис маршрут `/{package}.{Service}/{**catchall}` → cluster (`http://<service>:<port>`), CORS под gRPC-Web, раздача статики, fallback `/messenger`. Долгоживущие стримы (`updates/onliner/fast-auth/calls`) — с `ActivityTimeout 24ч`.

### Темы
- 3 темы (light/dark/midnight) на CSS-переменных (`--primary`, `--text-main`, `--dialog-bg`, ...) во встроенном `<style>` `messenger.html`. Иконки — Unicode/эмодзи.

## Функции
Логин + 2FA, регистрация, fast-auth (QR), список чатов и сообщения (отправка/редактирование/удаление/закреп/прочитано, вложения, пересылка), папки, настройки (профиль/сессии/2FA/хранилище), персонализация, **звонки (аудио/видео, 1-на-1 и группы)**.

## Связи
- [[Backend/BarkFluff.Web]] — хост (YARP gRPC-Web↔gRPC + статика).
- [[Backend/Calls]] — бэкенд звонков (LiveKit SFU), первый клиент которого — этот веб.
- [[Backend/Updates]], [[Backend/Onliner]] — источники real-time стримов.
- [[Архитектура]] — общий tech stack, XAuth.
