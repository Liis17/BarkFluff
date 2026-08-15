# Web

Веб-клиент мессенджера BarkFluff — **vanilla-JS SPA** без фреймворка, раздаётся хостом [[Backend/BarkFluff.Web]]. Исходные IIFE-модули мессенджера собираются esbuild в один fingerprinted asset.

Расположение: `Backend/BarkFluff.Web/wwwroot/` (`index.html` — авторизация и регистрация, `messenger.html` — мессенджер, модули `js/app/*.js`).

> ⚠️ Раньше существовало React-переписывание (`Frontend/Web/`), но оно было **намеренно откатано** (коммиты «возвращаемся на старую вебверсию» / «Восстановить веб-версию мессенджера (vanilla, до React)»). Актуальный и развиваемый клиент — этот vanilla-вариант. React-доку считать неактуальной.

## Tech Stack

| Технология | Версия | Назначение |
|------------|--------|-----------|
| grpc-web | 1.5.0 | gRPC-Web клиенты (`protoc-gen-grpc-web`, callback-style) |
| google-protobuf | 3.21.2 | runtime сообщений |
| esbuild | 0.24.0 | сборка proto-, LiveKit- и app-бандлов |
| livekit-client | 2.19.2 | WebRTC SDK для звонков ([[Backend/Calls]]) |

Исходные модули `js/app/*.js` оборачивают API в IIFE на `window.BF.*`. `messenger.html` оставляет отдельно ранний `i18n.js`, runtime-конфигурацию и vendor-бандлы, а `app-loader.js` читает `app-manifest.json` и подгружает один `app-<hash>.js` в заданной `scripts/app-bundle-entry.js` последовательности.

## Сборка

```bash
cd Backend/BarkFluff.Web
# proto-бандл (window.barkfluff + window.proto.barkfluff.*)
pwsh scripts/generate-proto.ps1      # Windows
bash scripts/generate-proto.sh       # Linux/macOS (нужен protoc-gen-grpc-web в PATH)
# LiveKit JS SDK (window.LivekitClient)
pwsh scripts/vendor-livekit.ps1      # либо bash scripts/vendor-livekit.sh
# Messenger app bundle (wwwroot/js/app/app-<hash>.js + manifest)
npm --prefix scripts run build:app # вручную — только если нужен bundle без dotnet build/publish
```

Proto-бандл собирается с `--minify` (1.7 МБ вместо 2.9 МБ; property mangling у esbuild выключен, поэтому reflection-код `google-protobuf`/`grpc-web` не страдает). Бандлы proto и vendor коммитятся в `wwwroot/js/proto/` и `wwwroot/js/vendor/`. App-бандл и manifest генерируются и игнорируются Git; `BuildMessengerApp` в `.csproj` пересобирает их перед обычными `dotnet build`/`dotnet publish` (и при необходимости сначала устанавливает инструменты из `scripts/package.json`), Docker — из `scripts/app-bundle-entry.js`. В production `app-<hash>.js` получает `Cache-Control: public, max-age=31536000, immutable`, manifest — `no-store`. CI устанавливает Node-зависимости и запускает ESLint и проверку Prettier для новых app- и build-файлов.

## Архитектура

### Выбор ноды (мультинодовость)

Клиент **не привязан к ноде, которая его отдала**. Статику раздаёт глобальный
`web.barkfluff.com` ([[Backend/Web]] в режиме `Web:Mode=Shell`), а gRPC-Web и загрузка
файлов идут **напрямую в выбранную ноду**, cross-origin. Нода при этом остаётся
самодостаточной: её `BarkFluff.Web` раздаёт ту же статику и фиксирует себя как ноду.

- `js/app/node.js` (`BF.node`) — единственный источник адреса ноды. Импортируется entry-point'ом **до**
  `device.js`/`clients.js`: клиенты gRPC-Web создаются синхронно при загрузке скрипта и
  захватывают origin, поэтому адрес обязан резолвиться из localStorage, без сети.
  - `origin()` — `bf_node_origin` из localStorage; при `pinned` всегда `window.location.origin`.
  - `key(name)` — ключ хранилища с суффиксом `@{origin}`; `set/clear/meta/list/normalize`.
  - `beaconClient()` / `navigatorClient()` — Beacon смотрит на выбранную ноду, Navigator
    всегда same-origin (каталог проксирует тот хост, что отдал страницу).
- **Режим хоста** — `/node-config.js` (эндпоинт [[Backend/Web]], `no-store`):
  `{pinned: true}` у ноды, `{pinned: false, navigatorProxy: true}` у шелла. Переключатель
  сервера показывается только при `pinned: false`.
- `js/app/nodepicker.js` (`BF.nodePicker`) — экран выбора в `index.html` (`#nodeSection`):
  каталог `NavigatorApi.ListServers`, история ранее использованных нод и ручной ввод.
  Выбор подтверждается `BeaconApi.GetServerInfo` через шлюз — иначе опечатка в адресе
  всплыла бы только на экране логина; оттуда же берутся имя, цвета, `livekit_url`.
  Ноды с пустым `web_endpoint` показываются заблокированными.
- Пока нода не выбрана, `body.node-picking` скрывает форму входа и QR, а `login-page.js`
  не проверяет сессию. После выбора `resumeOrShowLogin()` уводит в мессенджер, если под
  неймспейсом этой ноды уже лежит валидный токен.
- Смена сервера — `settings.js`, раздел «Сервер»: `BF.node.clear()` + переход на `/`.
  Сессия покидаемой ноды сохраняется.

⚠️ **Локальный стенд без TLS.** Клиент держит на origin ноды **11** долгоживущих
server-streaming подписок: 8 Updates (`realtime.js`: new/read/edited/deleted/pinned/
unpinned/allUnpinned/privateMessages), 2 Onliner (online-статусы + typing) и 1 Calls
(`calls.js`, device-scope). Typing подписывается на открытый чат, поэтому в простое их
10, с открытым чатом — 11. По HTTP/1.1 браузер даёт **6 соединений на origin**, то есть
лимит превышен уже без открытого чата: на стенде без TLS (Kestrel напрямую, HTTP/2 без
TLS не согласуется) unary-вызовы к ноде встают в очередь и висят. За nginx с TLS это
HTTP/2 без такого лимита. Симптом на стенде: `GetUploadUrl` и подобные не возвращаются,
пока не остановить стримы.

**Неймспейс хранилища.** Привязаны к ноде: `barkfluff_auth`, `barkfluff_temp`,
`bf_private_chat_keys`, `bf_web_push_enabled`, `bf_chat_drafts_{userId}` — все с суффиксом
`@{origin}`. Общие (без суффикса): `barkfluff_device_id` (одно устройство на все ноды, как
в [[Клиенты/Android]]), `bf_theme`, `bf_lang`, `bf_pers_*`, `bf_legal_accepted`.
`migrateLegacy()` в `node.js` один раз переносит домультинодовые ключи под неймспейс
origin'а страницы — обновление ноды на своём домене не разлогинивает.
⚠️ Смену роли домена (был нодой, стал шеллом) это не покрывает: нода переезжает на другой
origin, сопоставить ключ нечем — вход потребуется заново.

### Транспорт и авторизация
- gRPC-Web клиенты создаются в `js/app/clients.js`: `new window.barkfluff.<Service>ApiClient(BF.node.origin())`, складываются в `BF.clients` (`identity/users/messages/files/updates/onliner/fastAuth/calls`). Без выбранной ноды модуль редиректит на `/` до создания клиентов.
- `auth.js`, `fast-auth.js` и `register.js` держат **собственные** клиенты и строят их лениво: на шелле ноду выбирают на той же странице, поэтому origin известен только к моменту первого вызова.
- `BF.clients.authCall(method, req)` — унарный вызов с авто-рефрешем токена и ретраем при `UNAUTHENTICATED` (код 16). Временная ошибка refresh (сеть, таймаут, 5xx) сохраняет локальную сессию; [[Backend/Identity|Identity]] очищает её только через `x-error-code` невалидного refresh token (`7E6A31C5-3C4D-412E-87BC-0A387617A5D3`).
- `js/app/metadata.js` (`BF.metadata.build(token)`) формирует метаданные: `x-auth-token` (plain) + base64 `x-device-id`/`x-device-name`/`x-os-name`/`x-app-name`/`x-app-version`. Device-id — из `js/app/device.js` (localStorage `barkfluff_device_id`); его методы `getBrowserName()` и `getOsName()` также выводятся в «О BarkFluff».
- `js/app/health.js` (`BF.health`) — параллельная проверка `/ping` шлюза Web и `/ping/{service}` для пользовательских микросервисов; успешен только ответ `200` с телом `pong`, таймаут — 8 секунд, результат содержит время каждого запроса.
- `js/app/tokens.js` (`BF.tokens`) — хранение/refresh токенов.

### Real-time
- `js/app/realtime.js` (`BF.realtime`) — server-streaming подписки [[Backend/Updates|UpdatesApi]] (new/read/edited/deleted/pinned/...) и [[Backend/Onliner|OnlinerApi]]. Реконнект с backoff 2→30с, превентивный age-timer (180с), watchdog по молчанию (90с), реакция на `visibilitychange`, forced refresh при коде 16, событие `resync` для дозагрузки пропущенного. Если refresh временно недоступен, сессия сохраняется, а реконнект повторяется с начальным backoff; редирект на вход происходит только после очистки невалидного refresh token. `connection_status` сохраняет `connected` и передаёт транспортное `state`: `offline` (только browser offline), `reconnecting` или `connected`; ручной `reconnect()` сбрасывает backoff и инвалидирует отложенные попытки предыдущего цикла.
- События раздаются через `BF.realtime.on(event, cb)`; UI слушает их в `js/app/main.js`.
- `#connectionBanner` — глобальная доступная плашка проблемы соединения с «Нет сети» / «Переподключаемся…» и ручным «Повторить», видимая также в мобильном списке чатов. После восстановления `main.js` ждёт catch-up списка и открытого чата; только успешный проход кратко (3 с) показывает в чате «Синхронизировано».

### Папки чатов

- `js/app/folders.js` (`BF.folders`) рендерит горизонтальные вкладки `#folderTabs` и показывает на каждой папке сумму `countUnread` её чатов; при значении больше 99 отображается `99+`.
- `main.js::renderChatList()` передаёт актуальный список чатов в рендер вкладок, поэтому realtime-сообщение и событие прочтения сразу обновляют бейдж папки.

### Файлы и медиа
- `js/app/files.js` (`BF.files`) — загрузка (`uploadFile` → REST `/api/files/upload/{fileId}`) и кэш presigned-ссылок (`getFileUrls`/`getCachedFileUrl`, `Map` `fileId → {url, previewUrl}`) через gRPC `GetTempDownloadUrl` ([[Backend/Files]]).
- `js/app/messages.js` рендерит вложения: `renderImageGrid` (сетка превью), `renderVideos`, `renderAudios`, `renderDocs`. Клик по картинке/видео зовёт `onMediaClick(type, url, fileId)`.
- Видеосообщение без текстовой подписи выводится без полей облачка: время и статус накладываются на превью. При подписи между превью и текстом остаётся только нижний отступ.
- **Optimistic upload.** При отправке картинок и файлов `main.js` сразу добавляет локальное исходящее сообщение со статусом часов. Картинки используют локальные `ObjectURL`, сохраняют финальную раскладку сетки, размываются и показывают круговой прогресс для каждого файла; документы показывают линейный прогресс под именем. `files.js/uploadFile` использует `XMLHttpRequest.upload.progress`, поэтому UI отражает реально переданные браузером байты. После `SendMessage` или собственного realtime-эха локальный bubble заменяется серверным: корреляция идёт по уже зарезервированному набору `fileId`, что поддерживает оба порядка прихода и не создаёт дубликат. Активные pending-сообщения хранятся по чатам и возвращаются в список после переключения между чатами или полного realtime-resync. Набор вложений отправляется целиком: при сбое любого upload сообщение снимается и показывается ошибка, а не отправляется неполная версия. `ObjectURL` освобождаются после замены или ошибки; галочки появляются только у серверного сообщения.
- ⚠️ **Презайнед-ссылки протухают**, если чат долго открыт. Inline-превью переживают это (картинка уже загружена браузером), но full-версия грузится только по клику → 404. Поэтому `showMediaOverlay(type, url, fileId)` в `js/app/media-viewer.js` (`BF.mediaViewer`) при открытии lightbox принудительно перезапрашивает свежую ссылку через `BF.files.refreshFileUrl(fileId)` (обход кэша) и подменяет `src`; защита от гонки — `overlayFileToken` (сбрасывается при закрытии оверлея).
- `js/app/imageeditor.js` (`BF.imageEditor`) — редактор изображения перед отправкой (crop/rotate/flip/кисть/пикселизация/ластик). Инициализируется в `main.js` (`BF.imageEditor.init()`), открывается из `attach.js` (`BF.imageEditor.open(...)`).

### Markdown в облачках

Текст сообщения клиент интерпретирует как markdown (бэкенд хранит сырой текст). Парсер — `BF.utils.renderMarkdown(text)` в `js/app/utils.js` (рядом с `escapeHtml`): возвращает безопасную HTML-строку. Применяется в `messages.js` для основного текста (`.msg-text.md`) и текста пересланного блока (`.fwd-text.md`) через `innerHTML`. Reply-цитата, системные плашки и превью списка чатов остаются plain (`textContent`).

- **Набор:** `**жирный**`, `*` / `_курсив_`, `~~зачёркнутый~~`, `` `код` ``, ` ```блоки``` `, `[текст](url)`, списки (`- ` / `* ` / `1. `), цитаты (`> `), заголовки (`#`..`######`), автоссылки на голый `http(s)://…`. HTML allowlist для README-фрагментов: `p`/`h1…h6` с `align=left|center|right`, `strong`, `sub`, `a[href]`, `img[src,alt,width,height]`.
- **Безопасность (XSS):** текст экранируется ПЕРВЫМ (`escapeHtml`), затем добавляются только свои теги (сырых `<>&` не остаётся); HTML-теги пересобираются по allowlist без пользовательских атрибутов; `href` принимает только `http`/`https`/`mailto`, `src` — только HTTP(S). Относительные/fragment URL не имеют безопасной базы внутри сообщения: ссылка становится обычным текстом, а картинка показывает `alt`; `javascript:`/`data:` не становятся активными URL; у `<a>` — `target=_blank rel="noopener noreferrer"`. Разбор сегментный: защищённые куски (код/ссылки) не проходят emphasis.
- CSS `.md` (`strong/em/del/sub/code/pre/ul/ol/blockquote/h1..h6/img`) — в `wwwroot/css/messenger.css` рядом с `.msg-text`; для `.md`-контента `white-space: normal` (разметку держат блоки + `<br>`), внутри `<pre>` — `pre-wrap`.
- Композер/редактирование не меняются — в `messageInput` попадает сырой markdown (`setPendingEdit`).
- Приватные E2E-чаты получают markdown автоматически (расшифрованные сообщения рендерятся тем же `buildMessageElement`).

### Звонки (см. [[Backend/Calls]])
- `js/app/calls.js` (`BF.calls`) — сигнализация: device-scope стрим `SubscribeCallEvents` (паттерн `realtime.js`), call-control `InitiateCall/Accept/Reject/Join/End` + `SetCallAudioQuality`, машина состояний одного звонка, события `incoming/connect/peer_accepted/peer_rejected/ring_dismiss/ended/member/audio_quality_changed`. `peer_rejected` — терминальное событие для исходящего личного звонка: очищает локальное состояние и закрывает экран без ожидания `ended` (backend отправляет только `rejected`). Запускается в `main.js` рядом с `BF.realtime.startAll()`.
- `js/app/calls-ui.js` (`BF.callsUI`) — UI: ринг-оверлей входящего, полноэкранный экран активного звонка (сетка плиток + контролы микро/камера/демонстрация/сброс), рингтон (WebAudio), медиа через `window.LivekitClient` (Room: connect, публикация треков, привязка `TrackSubscribed`/`ParticipantConnected` к плиткам). Перед `InitiateCall` и `AcceptCall` `ensureMediaPermissions(mediaType)` проверяет разрешения браузера: микрофон нужен всегда, камера — только для видео. При отсутствии доступа модалка объясняет нужные разрешения и по действию пользователя запрашивает их через `getUserMedia`; пока доступ не получен, сигнализация звонка не начинается. Имя/аватар участника резолвится через `BF.api.getUser` (identity LiveKit-токена = userId). Кнопки — inline-SVG (не эмодзи), перечёркивание во всех off-иконках в одну сторону (↘). Микрофон: выключенный = красный перечёркнутый (mute). Камера/демонстрация: обычная иконка пока не транслируешь, красная перечёркнутая — кнопка остановки активной трансляции. Завершение — сплошная «трубка отбоя» (`phoneEnd`), всегда красная. Говорящий участник обводится через overlay `::after` поверх видео (видно и в режиме демонстрации). Детекция речи — **client-side WebAudio** (`AnalyserNode` + RMS time-domain по `track.mediaStreamTrack`, порог `SPEAK_THRESHOLD`, удержание `SPEAK_HOLD_MS`, общий rAF-цикл), а не серверный `ActiveSpeakersChanged` — это даёт низкую задержку и реакцию на любой звук. Тайлы сетки — компактные (`flex-wrap`, `clamp` ширина, `aspect-ratio:3/2`), по центру, помещаются в окно, с именем участника внизу блока. **Камера и демонстрация экрана — отдельные равноправные блоки** одного участника (ключ плитки: камера `identity`, экран `identity#screen`; экран — `object-fit:contain`), показываются одновременно для всех. Любой блок **разворачивается на весь экран по клику** (класс `.expanded`, `position:fixed`, анимация `tileExpand`); контролы остаются слоем выше (`z-index` контролов > `z` развёрнутого блока), есть кнопка «свернуть». Своя камера — отдельный PiP в углу (`#callSelfPip`), показывается только при включённой камере (выключил камеру → блок исчезает, кадр сбрасывается `srcObject=null`); в сетке только удалённые участники + своя демонстрация экрана. Пока собеседник не подключился — компактный экран ожидания (аватар + pulse-анимация). **Выбор устройства** — карет (▾) на кнопке микрофона/камеры открывает in-app меню `enumerateDevices`, переключение на лету через `room.switchActiveDevice`. ⚠️ Окно/экран для демонстрации выбирается **только нативным пикером браузера** (`getDisplayMedia`) — заменить его in-app UI нельзя (ограничение безопасности браузера). **Качество** (поповер-ползунки): «Голос · для всех» → `BF.calls.setAudioQuality` (общее, через сервер, применяется по событию `audio_quality_changed` переопубликацией микрофона с `audioPreset`); «Видео · ваш стрим» (видно только при включённой камере) → локально меняет разрешение+битрейт своей камеры (republish, на сервер не ходит). Смена качества идёт с задержкой (republish ~секунды) — инициатору показывается спиннер (`.busy`/`.pending`).
- Кнопки звонка — в шапке чата (`#chatHeader`), обработчики в `main.js` (1-на-1 → `callee_user_id`, группа → `chat_id`).
- LiveKit-бандл: `wwwroot/js/vendor/livekit-client.bundle.js` (esbuild IIFE, `window.LivekitClient`).

### Приватные чаты (E2E через passphrase, см. [[Backend/Messages]])
- Портирует Android `PrivateChatCrypto.kt`/`PrivateChatRepository.kt`: Argon2id (t=3, m=64MiB, p=4, salt 32 байта) → ключ 32 байта → AES-256-GCM (nonce 12 байт, tag 128 бит), AAD `barkfluff:private:{chatId}`, verifier `HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER")`.
- `js/app/privatechat.js` (`BF.privateChat`) — deriveKey (Argon2id через **hash-wasm**, `js/vendor/hash-wasm.umd.min.js`, глобал `hashwasm`), computeVerifier/validateVerifier + encryptText/decryptMessage (WebCrypto AES-GCM). Ключи (не passphrase): in-memory `Map` + localStorage `bf_private_chat_keys` (base64, если отмечен чекбокс «запомнить»).
- `js/app/api.js` — `mapChat` расширен (`chatType/kdfSalt/passphraseVerifier/lastActivityAt/privateInviteState/privateInviterUserId`), `mapEncryptedMessage`, методы `acceptPrivateChat/rejectPrivateChat/listPrivateMessages/sendPrivateMessage/markPrivateMessagesAsRead`.
- `js/app/realtime.js` — стрим `SubscribePrivateMessages` → событие `private_message` (тот же паттерн backoff/watchdog/age-timer/resync).
- `js/app/private-chat-ui.js` (`BF.privateChatUI`): `open` (обходит `getChatInfo`/`listMessages`, данные чата из ListChats); карточки состояний (`showCard`) — инвайт «Принять/Отклонить» (PENDING у приглашённого), «Ожидание собеседника» (PENDING у инициатора), «Чат заблокирован» (ACCEPTED без ключа); passphrase-модал `#privatePassOverlay` с проверкой verifier'а до `AcceptPrivateChat`; расшифрованные сообщения маппятся в обычный формат и рендерятся `BF.messages.buildMessageElement`.
- Ограничения: только текст (attach/стикеры/контекст-меню/звонки скрыты классом `.private-chat` и guard'ами `currentChatType === 1`), read-чек всегда серый (у `EncryptedMessage` нет `read_by`), превью в списке — «Сообщения зашифрованы», сортировка по `last_activity_at`. Стримы invite/resolution/edit/delete приватных не подключены — инвайт появляется при перечитке списка чатов.

### Черновики обычных чатов
- `js/app/drafts.js` (`BF.drafts`) держит локальный durable outbox в `localStorage` на пользователя и синхронизирует текст/reply с [[Backend/Messages]] через 2 секунды бездействия, при смене чата и после `online`.
- При внезапном закрытии сохраняется локальная запись; после перезапуска она синхронизируется с сервером. Список чатов показывает «Черновик» через `Chat.has_draft` или несинхронизированную локальную запись.
- Вложения, диалог прикрепления и незавершённые загрузки не восстанавливаются. Private/Secret-чаты исключены, чтобы не нарушать E2E-модель.

### Создание чатов
- `js/app/newchat.js` (`BF.newchat`) — FAB (карандаш) в сайдбаре над навигацией → меню из трёх пунктов → оверлей `#newChatOverlay` с поиском пользователей (`SearchUsers`, исключается сам пользователь):
  - **Новое сообщение** — клик по пользователю → `GetPersonChatId` → `openChat` (создания RPC нет, ЛС материализуется лениво).
  - **Новая группа** — название + мультивыбор участников (чипы) → `CreateGroupChat(user_ids, title)`.
  - **Приватный чат** — один собеседник (боты исключаются) + пароль ≥6 символов + чекбокс «запомнить» → `generateSalt(32)` → Argon2id → verifier → `CreatePrivateChat`; ключ сохраняется только если `created=true` (у существующего чата свой salt/пароль — поведение Android `PrivateChatRepository.createPrivateChat`). Создатель видит «Ожидание собеседника» (PENDING).
- `main.js` передаёт в `BF.newchat.init` колбэки `openChat`/`upsertChat` (вставка чата в список + открытие) и `getMyUserId`; на мобильном layout вызывается `window.__mobileShowChat`.

### Хост и маршрутизация
- [[Backend/BarkFluff.Web]] (`Program.cs`) — YARP: на каждый gRPC-сервис маршрут `/{package}.{Service}/{**catchall}` → cluster (`http://<service>:<port>`), CORS под gRPC-Web, раздача статики, fallback `/messenger`. Долгоживущие стримы (`updates/onliner/fast-auth/calls`) — с `ActivityTimeout 24ч`.
- Два режима: **Node** (по умолчанию) — все кластеры ноды + Beacon + Navigator + `/api/files/upload`; **Shell** — только статика и прокси Navigator.
- CORS открытый (`AllowAnyOrigin` + `AllowAnyHeader`, без `AllowCredentials`): origin страницы заранее неизвестен, токен идёт заголовком `x-auth-token`, куки в API не участвуют.

### Темы
- 3 темы (light/dark/midnight) на CSS-переменных (`--primary`, `--text-main`, `--dialog-bg`, ...) в `wwwroot/css/messenger.css`.
- Цвет времени и статуса сообщения задаётся отдельными переменными темы (`--msg-meta-color` и `--msg-out-meta-color`), чтобы они сохраняли контраст на тёмных входящих и исходящих облачках.
- Открытый чат использует общую контентную ширину `--chat-content-width` для колонки сообщений, шапки и composer: верхняя панель и нижний ввод оформлены как Material You-поверхности с большими скруглениями, blur/elevation и иконками действий из общего пакета.
- Вкладка без открытого чата называется «Мессенджер». Для личного чата — «Чат • Имя Фамилия»; favicon заменяется на круглую, центрированно обрезанную аватарку собеседника.

### Общий пакет иконок
Веб-клиент использует общий каталог `icons/`, чтобы визуальные идентификаторы совпадали с остальными клиентами:

- MSBuild подключает `../../icons/**/*.svg` в `wwwroot/icons/`, поэтому и нода, и web-shell раздают файлы по `/icons/{category}/{name}.svg`.
- `wwwroot/js/app/icons.js` предоставляет `BF.icons` (`html`, `element`, `url`), а `wwwroot/css/icons.css` окрашивает монохромные SVG через CSS mask и `currentColor`.
- Общий пакет уже используется в навигации, composer, настройках, меню действий сообщения, превью вложений, папках чатов и кнопках звонка. Динамические элементы создаются через `BF.icons`, статические — через `data-bf-icon`.
- Новые папки сохраняют ключи иконок в `folderIcon` (например, `favorites`, `work`); старые значения-эмодзи продолжают отображаться до редактирования папки.
- Service worker включает helper и его CSS в shell-cache, а запрошенные файлы `/icons/` кэширует во время работы.
- CI workflow веб-сервиса также отслеживает `icons/**`, поэтому обновление общего пакета запускает новую веб-сборку.

### Скользящее окно ленты

`main.js` держит в памяти и в DOM не больше `MAX_MESSAGES = 200` сообщений открытого чата.

- Подгрузка старых (скролл вверх, страница 30) вставляется **инкрементально** — `prependMessages()` строит только новые узлы и кладёт их перед лентой, чинит разделитель даты на стыке и пересобирает бывшее первое сообщение, если изменилась группировка. Полный `renderMessages()` (с `innerHTML = ''`) остаётся только для смены чата, resync и jump-to-message.
- `trimMessages('tail'|'head')` отрезает противоположный край. Обрезка хвоста поднимает `hasNewerGap` (окно не доходит до конца чата), обрезка головы сбрасывает `noMoreOlder`, иначе отрезанную историю нельзя было бы догрузить обратно.
- При `hasNewerGap` живые сообщения **не рисуются**: единственный guard стоит в `appendMessageToView`, он же убирает сообщение из массива, чтобы буфер остался непрерывным. Все пять мест отправки/realtime и `private-chat-ui.js` проходят через него.
- Скролл вниз догружает по 30 вперёд (`loadNewerMessages`, `offsetAfter`); действительно новых пришло меньше 30 → `hasNewerGap = false`, окно снова на живом хвосте. Считать именно новые, а не размер ответа: `api.js` подменяет `offsetBefore = 0` на 30, поэтому в ответе всегда есть уже загруженные сообщения.
- `scrollToBottom()` при `hasNewerGap` вместо прокрутки вызывает `jumpToLiveTail()`: перезагрузка последних 30 + `mergePendingUploadsIntoMessages` (иначе оптимистичные загрузки исчезнут прямо во время upload) + полный рендер. Это же покрывает кнопку «вниз» и отправку своего сообщения из середины истории.
- Общий `loadMessagesPage(chatId, fromId, before, after)` скрывает разницу обычного и приватного чата (во втором батч ещё расшифровывается).

### Группировка сообщений
- Последовательные сообщения одного отправителя объединяются визуально, если между ними не больше пяти минут и нет разделителя даты: вертикальный зазор уменьшается на 2px.
- В групповых чатах у входящих сообщений резервируется одинаковая левая колонка для аватара; сам аватар показывается только у последнего сообщения последовательности. В личных чатах аватара и колонки нет. Имя находится в tooltip при наведении на аватар.

### Motion UI
- `index.html` и `messenger.html` используют общий ease-out-токен `--ease-out: cubic-bezier(0.23, 1, 0.32, 1)` для коротких UI-переходов.
- На странице авторизации fast-auth QR плавно заменяет спиннер только после загрузки изображения; QR настройки 2FA при регистрации получает отдельный state-reveal. Скрытые QR исключаются из accessibility tree через `aria-hidden`.
- В мессенджере анимируются только редкие и пространственно значимые состояния: меню FAB создания чата, popover выбора устройств/качества звонка, file-drop feedback, вход/выход media lightbox и success-toast. Списки чатов/сообщений и клавиатурная навигация lightbox остаются мгновенными.
- Lightbox инвалидирует `overlayFileToken` сразу при закрытии, но очищает media `src` после 120 мс exit-перехода; быстрое повторное открытие отменяет отложенную очистку.
- `prefers-reduced-motion: reduce` убирает scale/translate из новых переходов и сохраняет только короткий opacity-feedback.

### Устойчивая загрузка медиа (рефреш протухших ссылок + плейсхолдер)

Проблема: при долгой сессии временные ссылки на файлы/картинки/стикеры (Minio presigned из [[Backend/Files]] `GetTempDownloadUrl`) протухают → браузер грузит 404 → сломанные контролы. Решение в `js/app/files.js` (`BF.files`):

- **`refreshFileUrl(fileId)`** — удаляет закешированный (протухший) URL из `urlCache`, перезапрашивает свежий через `BF.api.getTempDownloadUrl`, обновляет кеш (другие элементы по тому же `fileId` забирают свежую запись), возвращает `{fileId,url,previewUrl}|null`.
- **`bindResilientMedia(el, fileId, preferPreview)`** — для `<img>`/`<video>`/`<audio>`: сохраняет `data-bf-file-id` на элементе, по событию `error` (404/протухание) рефрешит ссылку и перезагружает (`el.src = fresh`); при повторной неудаче — плейсхолдер. `fileId` читается из data-атрибута на момент ошибки (актуально для переиспользуемых элементов — лайтбокс `#overlayImage`/`#overlayVideo`).
- **`bindResilientLink(el, fileId)`** — для документов (`<a>`): URL не загружается до клика, поэтому рефрешится лениво — префетч при `mouseenter`/`focus`, чтобы к моменту клика ссылка была свежей. При неудаче — заглушка.
- **`loadResilientBackground(el, fileId, preferPreview)`** — для CSS-фонов: предварительно грузит URL через `Image`, поэтому может обработать ошибку, недоступную самому `background-image`; один раз обновляет URL по `fileId`, затем показывает SVG-заглушку. Используется для постера профиля.
- **`applyPlaceholder(el)`** — заменяет элемент на векторную заглушку (inline-SVG data-URI, нейтральный серый) + класс `.bf-load-failed`: img → SVG в `src`, video → SVG в `poster` + сброс `src`, audio → сброс `src`, a → удаление `href`.
- **Состояние** в data-атрибутах: `data-bf-file-id`, `data-bf-prefer-preview`, `data-bf-refreshed`, `data-bf-refreshing`, `data-bf-failed`. Защита от гонок: пока рефреш в полёте (`data-bf-refreshing`), повторные ошибки не показывают плейсхолдер; при сбросе элемента (закрытие лайтбокса) `data-bf-file-id` снимается — случайные ошибки игнорируются.

Точки применения: рендер вложений в `messages.js` (`renderImageGrid`/`renderVideos`/`renderAudios`/`renderDocs`), стикерпанель и галерея медиа профиля в `main.js` (`renderStickerPackTabs`/`loadStickerPackContent`/`loadProfileMedia`), лайтбокс `showMediaOverlay(type, url, fileId)` в `media-viewer.js` (обработчики `error` навешены один раз на `#overlayImage`/`#overlayVideo`) и постер профиля (`openProfile`). CSS `.bf-load-failed` — в `wwwroot/css/messenger.css`.

> Аватары (`chat.picture`/`user.profilePicture`) приходят с сервера готовой ссылкой (не через `urlCache`) и этим механизмом не покрываются — их рефреш требует перезапроса чата/юзера.

### Локализация (i18n)

Пять языков — те же, что в [[Клиенты/Android]], [[Клиенты/iOS]] и [[Клиенты/macOS]]: **ru, en, es, de, zh-Hans**. Переводы, совпадающие по формулировке с мобильными клиентами, взяты из `strings.xml` и `Localizable.xcstrings`, поэтому термины не расходятся между платформами.

- **Движок** — `js/app/i18n.js` (`BF.i18n`), подключается **первым скриптом в `<head>`** обеих страниц (`index.html`, `messenger.html`) и в `offline.html`, до всех остальных модулей.
- **Выбор языка:** cookie `bf_lang` → `navigator.languages` → `en` для неподдерживаемых локалей. `zh-CN`/`zh-SG` нормализуются в `zh-Hans`, традиционный китайский (`zh-TW`/`zh-HK`) не поддерживается и уходит в запасной `en`.
- **Словари** — `wwwroot/js/i18n/{ru,en,es,de,zh-Hans}.json`, плоские `{ключ: текст}`. Грузится только выбранный язык плюс `ru` как fallback для ещё не переведённых ключей (базовый язык разметки). Все пять лежат в precache service worker'а, поэтому язык работает и офлайн.
- **API:** `t(key, params)` с подстановкой `{name}`; `tp(key, count, params)` — множественное число через `Intl.PluralRules` (формы `key.one/few/many/other`), им переведены «был(а) в сети N минут назад», счётчики участников/файлов/чатов. `current()`, `langs()`, `setLang(code)`, `apply(root)`, `onChange(cb)`.
- **Разметка:** `data-i18n` (textContent), `data-i18n-html` (innerHTML, для строк с `<br>`), `data-i18n-placeholder` / `-title` / `-aria-label` / `-alt` / `-value` / `-content`. Русский текст остаётся в разметке запасным вариантом на случай сбоя загрузки словаря.
- **Мелькание** исключено: `i18n.js` вешает на `<html>` класс `i18n-pending` (CSS `html.i18n-pending body { visibility: hidden }`), снимает после применения словаря; страховочный таймаут 3 с не даёт странице остаться скрытой при сбое.
- **Готовность:** `BF.i18n.ready` — промис. `main.js` откладывает до него первый рендер списка чатов, `login-page.js` — старт страницы входа. Форматирование дат/времени/размеров в `utils.js` идёт по `BF.i18n.current()`.
- **Смена языка** — экран «Язык интерфейса» в настройках (`settings.js`, вью `language`). Применяется мгновенно: `setLang` пишет cookie, перечитывает DOM через `apply()` и рассылает событие `bf:langchange` + колбэки `onChange`. На него подписаны настройки (перерисовка открытого экрана) и `main.js` (список чатов, вкладки папок, открытый чат, заголовок вкладки).
- **Проверка доступности** — в «О приложении» (`settings.js`, вью `about`) кнопка запускает `BF.health.check()`. Для каждого liveness listener показываются имя, `Доступен/Недоступен` и фактическое время HTTP-запроса; `/ping` проверяет Web, а `/ping/{service}` — сервисы через YARP-шлюз выбранной ноды.
- ⚠️ Тема-пикер в `messenger.html` определяет главный экран настроек по `sdBody.dataset.view === 'main'`, а **не по тексту заголовка** — заголовок локализуется.
- **Вне охвата:** тексты, приходящие с сервера (системные сообщения в чате, ошибки API, push от бэкенда) и сами правовые документы (`/legal/*.ru.md`) остаются на русском.

## Функции
Согласие с документами (гейт на все три входа), логин + 2FA, регистрация, fast-auth (QR), список чатов и сообщения (отправка/редактирование/удаление/закреп/прочитано, вложения, пересылка), папки, настройки (профиль/сессии/2FA/хранилище/**язык**), персонализация, **звонки (аудио/видео, 1-на-1 и группы)**, локализация на 5 языков.

Личные чаты с ботами определяются по `User.is_bot`: в списке и профиле показывается SVG-бейдж с подсказкой «Бот», а статус онлайна и действия аудио-/видеозвонка скрыты.

### Согласие с документами и cookie (страница входа)
Чекбокс «Я принимаю Пользовательское соглашение и Политику конфиденциальности» (`legal.js`) блокирует форму входа, регистрацию и QR fast-auth, пока не отмечен. Тексты открываются модалкой прямо на странице — markdown копируется из [[Backend/WebServer]] MSBuild-таргетом `CopyLegalDocs`, рендер через `BF.utils.renderMarkdown`. Хранится редакция документа (как `acceptedLegalRevision` в [[Клиенты/Android]]), после входа она уходит в профиль через `UsersApi.AcceptLegalConsent`. Там же плашка об использовании cookie. Подробности — [[Backend/Web]].

### Command palette (Ctrl+K / Cmd+K)

`js/app/cmdpalette.js` (`BF.cmdPalette`) — палитра быстрых команд, только в мессенджере (не на странице входа). `#cmdPaletteOverlay` в `messenger.html` (`.confirm-overlay`-паттерн, как `#newChatOverlay`).

- Триггер: глобальный `document.keydown`, `(ctrlKey||metaKey)+K` (без Alt), `preventDefault()`. Игнорируется, если уже открыт другой модальный overlay (`.confirm-overlay.visible`/`.settings-overlay.visible`/`.profile-overlay.visible`/`.call-permission-overlay.visible`/`.image-overlay.visible`/`.newchat-menu.visible`/`.msg-context-menu.visible`) — палитры не стекуются.
- Esc закрывает, ArrowUp/Down двигают выделение (clamp, без цикла), Enter/клик выполняет пункт и закрывает палитру.
- Фильтр — простой case-insensitive substring по лейблу (без fuzzy-либы, тексты уже короткие).
- **Статичные действия** (лейблы переиспользуют существующие i18n-ключи, отдельных переводов не заводили): `BF.newchat.open('message'|'group'|'private')` (потребовал добавить `open: openOverlay` в экспорт `newchat.js`, раньше был только `init`), `BF.settings.open(view)` для профиля/сессий/2FA/приватности/персонализации/языка/о приложении, `window.__setTheme('light'|'dark'|'midnight')` (уже существовавший глобальный хук темы, ничего менять не пришлось).
  - `BF.settings.open()` расширен: теперь принимает опциональный `view` (по умолчанию `'main'`); если передан не-`'main'` view, `viewStack` инициализируется как `['main']`, чтобы кнопка «назад» вела на главный экран настроек.
- **Динамика:** поиск по уже загрученным в память чатам (`getChats()` — колбэк из `main.js`, читает closure-переменную `chats`, без похода в API) по подстроке в `title`, до 6 совпадений, выбор → `openChat(chatId)` (+ `window.__mobileShowChat()` на мобильном layout, как в `newchat.js`).
- Команда выхода из аккаунта сознательно не включена (деструктивное действие, не должно срабатывать по Enter в палитре).
- Init вызывается в `main.js` рядом с `BF.newchat.init`/`BF.realtime.startAll()`.

### Управление групповыми чатами (паритет с Android)
Клик по шапке группового чата открывает инфо-панель `#groupOverlay` (`messenger.html`, переиспользует CSS `.profile-*`; логика в `js/app/group-info.js` (`BF.groupInfo.open`)). Возможности:
- смена названия (карандаш → `BF.api.updateGroupChat(chatId, title)`) и аватара (`BF.files.uploadFile(file, 6 /*CHAT_PICTURE*/)` → `updateGroupChat(chatId, null, fileId)`);
- список участников (`BF.api.listChatMembers`, аватары резолвятся через `getUser`), добавление через инлайн-поиск (`searchUsers` → `addUser`) и удаление (`kickUser`);
- вкладки Медиа/Файлы (общая `renderChatMedia(type, container)`, бывшая `loadProfileMedia`).
Аватарки чужих сообщений в ленте групповых чатов (`messages.js` → `.msg-sender-avatar`). API-обёртки `listChatMembers/addUser/kickUser/updateGroupChat` — в `api.js`. Бэкенд-методы готовы в [[Backend/Messages]], proto-bundle уже сгенерирован. Live-обновление шапки/состава у других участников не делается — приходит системное сообщение (паритет с Android).

## Ответы и пересылка

Reply и forward разделены на бэкенде (см. [[Backend/Messages]]).

- `api.js` маппит `Message.reply_to` в `msg.replyTo` и шлёт `replyToMessageId` /
  `forwardedMessageIds` вместо прежнего одиночного `forwardedMessageId`.
- `messages.js` рисует цитату по `msg.replyTo`, а пересылки — по вложениям `FORWARDED_MESSAGE`,
  **по блоку на каждое** (пачку нельзя схлопнуть в один блок). Reply и forward не исключают друг друга.
- Эвристика `knownMessageIds` (ответ = «оригинал среди загруженных») удалена вместе со всей
  обвязкой в `main.js` и экспортом `resetKnownMessageIds`: из-за неё ответ превращался в
  пересылку после прокрутки истории.
- Удалённый оригинал рисуется как «Сообщение удалено» (класс `.reply-quote.deleted`), без текста
  и без перехода.
- `attachmentSummary(type, fileName)` теперь принимает имя типа — reply-превью переиспользует его
  вместе с i18n-ключами вместо своей копии строк.

## Связи
- [[Backend/BarkFluff.Web]] — хост (YARP gRPC-Web↔gRPC + статика).
- [[Backend/Calls]] — бэкенд звонков (LiveKit SFU), первый клиент которого — этот веб.
- [[Backend/Updates]], [[Backend/Onliner]] — источники real-time стримов.
- [[Архитектура]] — общий tech stack, XAuth.

## PWA и web-push

⚠️ **На глобальном шелле PWA пока не работает.** `push.js` регистрирует service worker
только если `/pwa-config.js` вернул непустой Firebase-конфиг, а стек `docker/webshell`
переменные `Web__Push__*` не передаёт. Значит на `web.barkfluff.com` не будет ни push,
ни **precache/offline-страницы, ни предложения установки** — сегодня этот домен обслуживает
нода и SW там регистрируется, после переключения перестанет. Это известное следствие того,
что мультинодовый push вынесен в отдельную фазу: подсунуть шеллу конфиг ноды нельзя, пока
SW не научится получать его после выбора сервера — иначе токен уйдёт в один Firebase-проект,
а слать пуш будет нода с другим.

`Backend/BarkFluff.Web` устанавливается как PWA: `manifest.webmanifest`, иконки 192/512, `service-worker.js` и `offline.html` составляют только offline-оболочку. Service worker precache'ит HTML/локальные JS, CSS, `app-loader.js`, manifest и текущий fingerprinted app-бандл; manifest загружается network-first и при отсутствии сети отдаётся из shell-кэша. Для навигации используется network-first с fallback на offline-страницу. gRPC-Web, API, presigned URL, чаты, сообщения и вложения не кэшируются.

**Версионирование статики.** Главный бандл мессенджера fingerprinted esbuild'ом (`scripts/build-app.js`, `entryNames: 'app-[hash]'`) и отдаётся как `public, max-age=31536000, immutable`; `app-manifest.json` (указатель на текущий хеш) — `no-store` (`Program.cs:OnPrepareResponse`). Версия SW-кэша `CACHE_NAME` в `service-worker.js` бампится **автоматически** MSBuild-таргетом `BuildShellVersion`: `scripts/build-shell-version.js` парсит `APP_SHELL`, считает SHA-1 от содержимого файлов шелла и подставляет 8 hex в `barkfluff-shell-<hash>`. Браузер проверяет главный SW-скрипт побайтово при навигации, поэтому версия обязана жить в самом `service-worker.js` (не в `importScripts`). Запросы `/js/` и `/css/` переведены на stale-while-revalidate: кэш отдаётся мгновенно, в фоне тянется свежая копия — ручной бамп больше не нужен, а immutable `app-<hash>.js` здесь остаётся no-op.

`BF.push` регистрирует worker после авторизации, но не запрашивает разрешение сам: пользователь включает уведомления отдельным переключателем в настройках. После явного согласия модуль получает FCM Web token с VAPID key и передаёт его в `UsersApi.SetFirebaseToken(..., WEB)`; при выключении или logout вызывает `ClearFirebaseToken` и удаляет локальный Firebase token. Если браузер запрещает уведомления либо не поддерживает API, UI показывает соответствующее состояние. Install CTA показывается только после `beforeinstallprompt` (Safari/iOS не обещает background-push в этой версии). Открытый чат сохраняется в `?chat=` URL: service worker не показывает web-push `new_message`, если этот чат видим в окне браузера; для других чатов уведомление остаётся.

Публичный Firebase-конфиг и VAPID key отдаёт эндпоинт `/pwa-config.js` (`self.BF_PWA_CONFIG`); если хоть одно поле пустое, он возвращает `null` и переключатель уведомлений в настройках становится недоступным. Значения задаются **только** переменными окружения `Web__Push__*` (см. `docker/backend/sample-backend.env`). Класть их плейсхолдерами в `appsettings.json` нельзя: `LoadConfiguration` повторно добавляет `appsettings.json` последним источником, поэтому пустая строка из файла перекрывает env.

Видимая вкладка оставляет уведомление текущему realtime-клиенту; скрытая или закрытая — service worker через FCM. Click фокусирует существующий клиент либо открывает `/messenger?chat=...`; `main.js` открывает чат после загрузки списка. Worker группирует уведомления tag'ом чата/звонка и закрывает их по dismiss-событию. Секретные сообщения, E2E-инвайты, звонки и админ-рассылки не раскрывают содержимое: Web получает только технические ID, имя/аватар отправителя там, где они нужны.
