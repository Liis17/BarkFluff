# Web

Веб-клиент мессенджера BarkFluff — **vanilla-JS SPA** (без фреймворка и бандлера приложения), раздаётся хостом [[Backend/BarkFluff.Web]].

Расположение: `Backend/BarkFluff.Web/wwwroot/` (`index.html` — авторизация и регистрация, `messenger.html` — мессенджер, модули `js/app/*.js`).

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

Бандлы коммитятся в `wwwroot/js/proto/` и `wwwroot/js/vendor/`. В Docker-сборке proto-бандл пересобирается заново из `scripts/proto-bundle-index.js` и **перезаписывает** закоммиченный — поэтому список proto держим синхронным в трёх местах: `generate-proto.*`, `proto-bundle-index.js` и `protoc`-списке в `Dockerfile.slim`.

## Архитектура

### Транспорт и авторизация
- gRPC-Web клиенты создаются в `js/app/clients.js`: `new window.barkfluff.<Service>ApiClient(origin)`, складываются в `BF.clients` (`identity/users/messages/files/updates/onliner/fastAuth/calls`).
- `BF.clients.authCall(method, req)` — унарный вызов с авто-рефрешем токена и ретраем при `UNAUTHENTICATED` (код 16).
- `js/app/metadata.js` (`BF.metadata.build(token)`) формирует метаданные: `x-auth-token` (plain) + base64 `x-device-id`/`x-device-name`/`x-os-name`/`x-app-name`/`x-app-version`. Device-id — из `js/app/device.js` (localStorage `barkfluff_device_id`); его методы `getBrowserName()` и `getOsName()` также выводятся в «О BarkFluff».
- `js/app/tokens.js` (`BF.tokens`) — хранение/refresh токенов.

### Real-time
- `js/app/realtime.js` (`BF.realtime`) — server-streaming подписки [[Backend/Updates|UpdatesApi]] (new/read/edited/deleted/pinned/...) и [[Backend/Onliner|OnlinerApi]]. Реконнект с backoff 2→30с, превентивный age-timer (180с), watchdog по молчанию (90с), реакция на `visibilitychange`, forced refresh при коде 16, событие `resync` для дозагрузки пропущенного.
- События раздаются через `BF.realtime.on(event, cb)`; UI слушает их в `js/app/main.js`.

### Файлы и медиа
- `js/app/files.js` (`BF.files`) — загрузка (`uploadFile` → REST `/api/files/upload/{fileId}`) и кэш presigned-ссылок (`getFileUrls`/`getCachedFileUrl`, `Map` `fileId → {url, previewUrl}`) через gRPC `GetTempDownloadUrl` ([[Backend/Files]]).
- `js/app/messages.js` рендерит вложения: `renderImageGrid` (сетка превью), `renderVideos`, `renderAudios`, `renderDocs`. Клик по картинке/видео зовёт `onMediaClick(type, url, fileId)`.
- Видеосообщение без текстовой подписи выводится без полей облачка: время и статус накладываются на превью. При подписи между превью и текстом остаётся только нижний отступ.
- **Optimistic upload.** При отправке картинок и файлов `main.js` сразу добавляет локальное исходящее сообщение со статусом часов. Картинки используют локальные `ObjectURL`, сохраняют финальную раскладку сетки, размываются и показывают круговой прогресс для каждого файла; документы показывают линейный прогресс под именем. `files.js/uploadFile` использует `XMLHttpRequest.upload.progress`, поэтому UI отражает реально переданные браузером байты. После `SendMessage` или собственного realtime-эха локальный bubble заменяется серверным: корреляция идёт по уже зарезервированному набору `fileId`, что поддерживает оба порядка прихода и не создаёт дубликат. Активные pending-сообщения хранятся по чатам и возвращаются в список после переключения между чатами или полного realtime-resync. Набор вложений отправляется целиком: при сбое любого upload сообщение снимается и показывается ошибка, а не отправляется неполная версия. `ObjectURL` освобождаются после замены или ошибки; галочки появляются только у серверного сообщения.
- ⚠️ **Презайнед-ссылки протухают**, если чат долго открыт. Inline-превью переживают это (картинка уже загружена браузером), но full-версия грузится только по клику → 404. Поэтому `showMediaOverlay(type, url, fileId)` в `main.js` при открытии lightbox принудительно перезапрашивает свежую ссылку через `BF.files.refreshFileUrl(fileId)` (обход кэша) и подменяет `src`; защита от гонки — `overlayFileToken` (сбрасывается при закрытии оверлея).
- `js/app/imageeditor.js` (`BF.imageEditor`) — редактор изображения перед отправкой (crop/rotate/flip/кисть/пикселизация/ластик). Инициализируется в `main.js` (`BF.imageEditor.init()`), открывается из `attach.js` (`BF.imageEditor.open(...)`).

### Markdown в облачках

Текст сообщения клиент интерпретирует как markdown (бэкенд хранит сырой текст). Парсер — `BF.utils.renderMarkdown(text)` в `js/app/utils.js` (рядом с `escapeHtml`): возвращает безопасную HTML-строку. Применяется в `messages.js` для основного текста (`.msg-text.md`) и текста пересланного блока (`.fwd-text.md`) через `innerHTML`. Reply-цитата, системные плашки и превью списка чатов остаются plain (`textContent`).

- **Набор:** `**жирный**`, `*` / `_курсив_`, `~~зачёркнутый~~`, `` `код` ``, ` ```блоки``` `, `[текст](url)`, списки (`- ` / `* ` / `1. `), цитаты (`> `), заголовки (`#`..`######`), автоссылки на голый `http(s)://…`. HTML allowlist для README-фрагментов: `p`/`h1…h6` с `align=left|center|right`, `strong`, `sub`, `a[href]`, `img[src,alt,width,height]`.
- **Безопасность (XSS):** текст экранируется ПЕРВЫМ (`escapeHtml`), затем добавляются только свои теги (сырых `<>&` не остаётся); HTML-теги пересобираются по allowlist без пользовательских атрибутов; `href` принимает только `http`/`https`/`mailto`, `src` — только HTTP(S). Относительные/fragment URL не имеют безопасной базы внутри сообщения: ссылка становится обычным текстом, а картинка показывает `alt`; `javascript:`/`data:` не становятся активными URL; у `<a>` — `target=_blank rel="noopener noreferrer"`. Разбор сегментный: защищённые куски (код/ссылки) не проходят emphasis.
- CSS `.md` (`strong/em/del/sub/code/pre/ul/ol/blockquote/h1..h6/img`) — во встроенном `<style>` `messenger.html` рядом с `.msg-text`; для `.md`-контента `white-space: normal` (разметку держат блоки + `<br>`), внутри `<pre>` — `pre-wrap`.
- Композер/редактирование не меняются — в `messageInput` попадает сырой markdown (`setPendingEdit`).
- Приватные E2E-чаты получают markdown автоматически (расшифрованные сообщения рендерятся тем же `buildMessageElement`).

### Звонки (см. [[Backend/Calls]])
- `js/app/calls.js` (`BF.calls`) — сигнализация: device-scope стрим `SubscribeCallEvents` (паттерн `realtime.js`), call-control `InitiateCall/Accept/Reject/Join/End` + `SetCallAudioQuality`, машина состояний одного звонка, события `incoming/connect/peer_accepted/peer_rejected/ring_dismiss/ended/member/audio_quality_changed`. Запускается в `main.js` рядом с `BF.realtime.startAll()`.
- `js/app/calls-ui.js` (`BF.callsUI`) — UI: ринг-оверлей входящего, полноэкранный экран активного звонка (сетка плиток + контролы микро/камера/демонстрация/сброс), рингтон (WebAudio), медиа через `window.LivekitClient` (Room: connect, публикация треков, привязка `TrackSubscribed`/`ParticipantConnected` к плиткам). Имя/аватар участника резолвится через `BF.api.getUser` (identity LiveKit-токена = userId). Кнопки — inline-SVG (не эмодзи), перечёркивание во всех off-иконках в одну сторону (↘). Микрофон: выключенный = красный перечёркнутый (mute). Камера/демонстрация: обычная иконка пока не транслируешь, красная перечёркнутая — кнопка остановки активной трансляции. Завершение — сплошная «трубка отбоя» (`phoneEnd`), всегда красная. Говорящий участник обводится через overlay `::after` поверх видео (видно и в режиме демонстрации). Детекция речи — **client-side WebAudio** (`AnalyserNode` + RMS time-domain по `track.mediaStreamTrack`, порог `SPEAK_THRESHOLD`, удержание `SPEAK_HOLD_MS`, общий rAF-цикл), а не серверный `ActiveSpeakersChanged` — это даёт низкую задержку и реакцию на любой звук. Тайлы сетки — компактные (`flex-wrap`, `clamp` ширина, `aspect-ratio:3/2`), по центру, помещаются в окно, с именем участника внизу блока. **Камера и демонстрация экрана — отдельные равноправные блоки** одного участника (ключ плитки: камера `identity`, экран `identity#screen`; экран — `object-fit:contain`), показываются одновременно для всех. Любой блок **разворачивается на весь экран по клику** (класс `.expanded`, `position:fixed`, анимация `tileExpand`); контролы остаются слоем выше (`z-index` контролов > `z` развёрнутого блока), есть кнопка «свернуть». Своя камера — отдельный PiP в углу (`#callSelfPip`), показывается только при включённой камере (выключил камеру → блок исчезает, кадр сбрасывается `srcObject=null`); в сетке только удалённые участники + своя демонстрация экрана. Пока собеседник не подключился — компактный экран ожидания (аватар + pulse-анимация). **Выбор устройства** — карет (▾) на кнопке микрофона/камеры открывает in-app меню `enumerateDevices`, переключение на лету через `room.switchActiveDevice`. ⚠️ Окно/экран для демонстрации выбирается **только нативным пикером браузера** (`getDisplayMedia`) — заменить его in-app UI нельзя (ограничение безопасности браузера). **Качество** (поповер-ползунки): «Голос · для всех» → `BF.calls.setAudioQuality` (общее, через сервер, применяется по событию `audio_quality_changed` переопубликацией микрофона с `audioPreset`); «Видео · ваш стрим» (видно только при включённой камере) → локально меняет разрешение+битрейт своей камеры (republish, на сервер не ходит). Смена качества идёт с задержкой (republish ~секунды) — инициатору показывается спиннер (`.busy`/`.pending`).
- Кнопки звонка — в шапке чата (`#chatHeader`), обработчики в `main.js` (1-на-1 → `callee_user_id`, группа → `chat_id`).
- LiveKit-бандл: `wwwroot/js/vendor/livekit-client.bundle.js` (esbuild IIFE, `window.LivekitClient`).

### Приватные чаты (E2E через passphrase, см. [[Backend/Messages]])
- Портирует Android `PrivateChatCrypto.kt`/`PrivateChatRepository.kt`: Argon2id (t=3, m=64MiB, p=4, salt 32 байта) → ключ 32 байта → AES-256-GCM (nonce 12 байт, tag 128 бит), AAD `barkfluff:private:{chatId}`, verifier `HMAC-SHA256(key, "BARKFLUFF_PRIVATE_CHAT_VERIFIER")`.
- `js/app/privatechat.js` (`BF.privateChat`) — deriveKey (Argon2id через **hash-wasm**, `js/vendor/hash-wasm.umd.min.js`, глобал `hashwasm`), computeVerifier/validateVerifier + encryptText/decryptMessage (WebCrypto AES-GCM). Ключи (не passphrase): in-memory `Map` + localStorage `bf_private_chat_keys` (base64, если отмечен чекбокс «запомнить»).
- `js/app/api.js` — `mapChat` расширен (`chatType/kdfSalt/passphraseVerifier/lastActivityAt/privateInviteState/privateInviterUserId`), `mapEncryptedMessage`, методы `acceptPrivateChat/rejectPrivateChat/listPrivateMessages/sendPrivateMessage/markPrivateMessagesAsRead`.
- `js/app/realtime.js` — стрим `SubscribePrivateMessages` → событие `private_message` (тот же паттерн backoff/watchdog/age-timer/resync).
- `js/app/main.js`, секция `PRIVATE CHATS`: `openPrivateChat` (обходит `getChatInfo`/`listMessages`, данные чата из ListChats); карточки состояний (`showPrivateCard`) — инвайт «Принять/Отклонить» (PENDING у приглашённого), «Ожидание собеседника» (PENDING у инициатора), «Чат заблокирован» (ACCEPTED без ключа); passphrase-модал `#privatePassOverlay` с проверкой verifier'а до `AcceptPrivateChat`; расшифрованные сообщения маппятся в обычный формат и рендерятся `BF.messages.buildMessageElement`.
- Ограничения: только текст (attach/стикеры/контекст-меню/звонки скрыты классом `.private-chat` и guard'ами `currentChatType === 1`), read-чек всегда серый (у `EncryptedMessage` нет `read_by`), превью в списке — «Сообщения зашифрованы», сортировка по `last_activity_at`. Стримы invite/resolution/edit/delete приватных не подключены — инвайт появляется при перечитке списка чатов.

### Создание чатов
- `js/app/newchat.js` (`BF.newchat`) — FAB (карандаш) в сайдбаре над навигацией → меню из трёх пунктов → оверлей `#newChatOverlay` с поиском пользователей (`SearchUsers`, исключается сам пользователь):
  - **Новое сообщение** — клик по пользователю → `GetPersonChatId` → `openChat` (создания RPC нет, ЛС материализуется лениво).
  - **Новая группа** — название + мультивыбор участников (чипы) → `CreateGroupChat(user_ids, title)`.
  - **Приватный чат** — один собеседник (боты исключаются) + пароль ≥6 символов + чекбокс «запомнить» → `generateSalt(32)` → Argon2id → verifier → `CreatePrivateChat`; ключ сохраняется только если `created=true` (у существующего чата свой salt/пароль — поведение Android `PrivateChatRepository.createPrivateChat`). Создатель видит «Ожидание собеседника» (PENDING).
- `main.js` передаёт в `BF.newchat.init` колбэки `openChat`/`upsertChat` (вставка чата в список + открытие) и `getMyUserId`; на мобильном layout вызывается `window.__mobileShowChat`.

### Хост и маршрутизация
- [[Backend/BarkFluff.Web]] (`Program.cs`) — YARP: на каждый gRPC-сервис маршрут `/{package}.{Service}/{**catchall}` → cluster (`http://<service>:<port>`), CORS под gRPC-Web, раздача статики, fallback `/messenger`. Долгоживущие стримы (`updates/onliner/fast-auth/calls`) — с `ActivityTimeout 24ч`.

### Темы
- 3 темы (light/dark/midnight) на CSS-переменных (`--primary`, `--text-main`, `--dialog-bg`, ...) во встроенном `<style>` `messenger.html`.
- Цвет времени и статуса сообщения задаётся отдельными переменными темы (`--msg-meta-color` и `--msg-out-meta-color`), чтобы они сохраняли контраст на тёмных входящих и исходящих облачках.
- Открытый чат использует общую контентную ширину `--chat-content-width` для колонки сообщений, шапки и composer: верхняя панель и нижний ввод оформлены как Material You-поверхности с большими скруглениями, blur/elevation и inline-SVG иконками действий.
- Вкладка без открытого чата называется «Мессенджер». Для личного чата — «Чат • Имя Фамилия»; favicon заменяется на круглую, центрированно обрезанную аватарку собеседника.

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
- **`applyPlaceholder(el)`** — заменяет элемент на векторную заглушку (inline-SVG data-URI, нейтральный серый) + класс `.bf-load-failed`: img → SVG в `src`, video → SVG в `poster` + сброс `src`, audio → сброс `src`, a → удаление `href`.
- **Состояние** в data-атрибутах: `data-bf-file-id`, `data-bf-prefer-preview`, `data-bf-refreshed`, `data-bf-refreshing`, `data-bf-failed`. Защита от гонок: пока рефреш в полёте (`data-bf-refreshing`), повторные ошибки не показывают плейсхолдер; при сбросе элемента (закрытие лайтбокса) `data-bf-file-id` снимается — случайные ошибки игнорируются.

Точки применения: рендер вложений в `messages.js` (`renderImageGrid`/`renderVideos`/`renderAudios`/`renderDocs`), стикерпанель и галерея медиа профиля в `main.js` (`renderStickerPackTabs`/`loadStickerPackContent`/`loadProfileMedia`), лайтбокс `showMediaOverlay(type, url, fileId)` (обработчики `error` навешены один раз на `#overlayImage`/`#overlayVideo`). CSS `.bf-load-failed` — во встроенном `<style>` `messenger.html`.

> Аватары (`chat.picture`/`user.profilePicture`) приходят с сервера готовой ссылкой (не через `urlCache`) и этим механизмом не покрываются — их рефреш требует перезапроса чата/юзера.

## Функции
Согласие с документами (гейт на все три входа), логин + 2FA, регистрация, fast-auth (QR), список чатов и сообщения (отправка/редактирование/удаление/закреп/прочитано, вложения, пересылка), папки, настройки (профиль/сессии/2FA/хранилище), персонализация, **звонки (аудио/видео, 1-на-1 и группы)**.

Личные чаты с ботами определяются по `User.is_bot`: в списке и профиле показывается SVG-бейдж с подсказкой «Бот», а статус онлайна и действия аудио-/видеозвонка скрыты.

### Согласие с документами и cookie (страница входа)
Чекбокс «Я принимаю Пользовательское соглашение и Политику конфиденциальности» (`legal.js`) блокирует форму входа, регистрацию и QR fast-auth, пока не отмечен. Тексты открываются модалкой прямо на странице — markdown копируется из [[Backend/WebServer]] MSBuild-таргетом `CopyLegalDocs`, рендер через `BF.utils.renderMarkdown`. Хранится редакция документа (как `acceptedLegalRevision` в [[Клиенты/Android]]), после входа она уходит в профиль через `UsersApi.AcceptLegalConsent`. Там же плашка об использовании cookie. Подробности — [[Backend/Web]].

### Управление групповыми чатами (паритет с Android)
Клик по шапке группового чата открывает инфо-панель `#groupOverlay` (`messenger.html`, переиспользует CSS `.profile-*`; логика в `main.js` — `openGroupInfo`). Возможности:
- смена названия (карандаш → `BF.api.updateGroupChat(chatId, title)`) и аватара (`BF.files.uploadFile(file, 6 /*CHAT_PICTURE*/)` → `updateGroupChat(chatId, null, fileId)`);
- список участников (`BF.api.listChatMembers`, аватары резолвятся через `getUser`), добавление через инлайн-поиск (`searchUsers` → `addUser`) и удаление (`kickUser`);
- вкладки Медиа/Файлы (общая `renderChatMedia(type, container)`, бывшая `loadProfileMedia`).
Аватарки чужих сообщений в ленте групповых чатов (`messages.js` → `.msg-sender-avatar`). API-обёртки `listChatMembers/addUser/kickUser/updateGroupChat` — в `api.js`. Бэкенд-методы готовы в [[Backend/Messages]], proto-bundle уже сгенерирован. Live-обновление шапки/состава у других участников не делается — приходит системное сообщение (паритет с Android).

## Связи
- [[Backend/BarkFluff.Web]] — хост (YARP gRPC-Web↔gRPC + статика).
- [[Backend/Calls]] — бэкенд звонков (LiveKit SFU), первый клиент которого — этот веб.
- [[Backend/Updates]], [[Backend/Onliner]] — источники real-time стримов.
- [[Архитектура]] — общий tech stack, XAuth.
