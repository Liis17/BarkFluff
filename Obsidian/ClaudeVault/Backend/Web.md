# BarkFluff.Web

gRPC-Web reverse proxy + static file server для веб-клиента. Порт: **7016**.

Расположение: `Backend/BarkFluff.Web/`

📁 **Детальная карта файлов и классов:** [[Backend/Web-ProjectMap]]

## Сборка

```bash
dotnet build BarkFluff.Web.csproj
docker-compose -f docker-compose-dev.yml up web
```

## Архитектура

Три функции:
1. **Статика** — раздаёт `wwwroot/` (index.html, messenger.html, JS-модули)
2. **gRPC-Web прокси** — кастомный middleware (`GrpcWebResponseStream` в `Program.cs`) конвертирует `application/grpc-web-text` (HTTP/1.1) → HTTP/2 gRPC; YARP проксирует к бэкенд-сервисам. Используется собственная реализация, так как `Grpc.AspNetCore.Web` не работает с YARP. **Base64-фрейминг:** каждый Write кодируется независимым padded base64-чанком и сразу флашится (`=` в середине потока — законный разделитель по спеке gRPC-Web, декодер клиента обрабатывает 4-символьные группы независимо). Раньше остаток 1-2 байта буферизовался до следующего Write — из-за этого server-streaming сообщения приходили с отставанием на одно.
3. **HTTP upload прокси** — `POST /api/files/upload/{uploadId}` → Files-сервис (HTTP-порт **7006**). До чтения тела Web поднимает Kestrel-лимит только для этого маршрута до **512 МБ**; иначе дефолт Kestrel (~28,6 МБ) вернёт `413` до передачи в [[Backend/Files]].

## gRPC-Web трейлеры (grpc-status) — критично

Браузерный gRPC-Web (connect-es) ждёт `grpc-status` **в trailer-frame тела** (последний фрейм с флагом `0x80`), а не в HTTP-трейлерах. Middleware (`Program.cs:178-248`) собирает этот фрейм из **двух** источников:

1. **promotedTrailers** — `grpc-status`/`grpc-message`/`grpc-status-details-bin`/`x-error-code`, продвинутые YARP в *заголовки* ответа; ловятся в `OnStarting` и удаляются из заголовков. Покрывает **trailers-only** случай: бизнес-ошибки (`OtpCodeNeedException` и пр.) бэкенд отдаёт через `RpcException` → статус летит в HTTP/2-**заголовках**.
2. **IHttpResponseTrailersFeature.Trailers** — читаются после `await next()` и мёржатся поверх promotedTrailers (для успешных ответов, где Kestrel отдаёт реальные HTTP-трейлеры).

Оба словаря объединяются в `trailerHeaders`, сериализуются в `key: value\r\n` и пишутся trailer-frame'ом (флаг `0x80`, для grpc-web-text — base64).

## YARP Routes

| Route | Backend | Protocol |
|-------|---------|----------|
| `/barkfluff.identity.IdentityApi/{**catch-all}` | Identity (7000) | gRPC/HTTP2 |
| `/barkfluff.messages.MessagesApi/{**catch-all}` | Messages (7007) | gRPC/HTTP2 |
| `/barkfluff.users.UsersApi/{**catch-all}` | Users (7001) | gRPC/HTTP2 |
| `/barkfluff.files.FilesApi/{**catch-all}` | Files (7005) | gRPC/HTTP2 |
| `/barkfluff.updates.UpdatesApi/{**catch-all}` | Updates (7015) | gRPC/HTTP2 |
| `/barkfluff.onliner.OnlinerApi/{**catch-all}` | Onliner (7009) | gRPC/HTTP2 |
| `/barkfluff.fast.auth.FastAuthApi/{**catch-all}` | FastAuth (7008) | gRPC/HTTP2 (server-streaming) |
| `/barkfluff.calls.CallsApi/{**catch-all}` | Calls (7025) | gRPC/HTTP2 (server-streaming, входит в `streamingServices`, 24h timeout) |
| `/api/files/upload/{uploadId}` | Files (7006) | HTTP/1.1 |

## Frontend JS Modules (`wwwroot/js/app/`)

**Инфраструктура:**
- `device.js` — `BF.device`: deviceId, browserName, osName
- `tokens.js` — `BF.tokens`: TokenStore (localStorage/sessionStorage)
- `metadata.js` — `BF.metadata`: сборка gRPC metadata с base64-заголовками

**Страница логина** (index.html):
- `auth.js` — login, refreshToken, getValidAccessToken
- `login-page.js` — форма логина, OTP, проверка сессии, запуск/остановка QR-сессии при смене секции
- `fast-auth.js` — `BF.fastAuth`: QR fast-auth логин (анонимный `GenerateFastAuthToken` + server-streaming `SubscribeFastAuthResult`), автоперезапуск при EXPIRED/REJECTED, отсчёт TTL 5 минут
- `register.js` — `BF.register`: модальный мастер регистрации из 9 шагов (кнопка `#toRegisterBtn`). См. раздел [[#Регистрация по шагам (register.js)]].
- `legal.js` — `BF.legal`: согласие с документами и cookie-уведомление. См. раздел [[#Согласие с документами (legal.js)]].

**Мессенджер** (messenger.html):
- `clients.js` — gRPC-Web клиенты, authCall с auto-refresh
- `utils.js` — форматирование, escapeHtml, parseJwt и безопасный Markdown-рендер (`renderMarkdown`) для текста сообщений и forward-блоков: заголовки, цитаты, списки, code blocks, ссылки, акцентирование и GFM-таблицы. Таблица определяется строкой заголовков и валидной строкой-разделителем (`-` / `:---:` / `---:`); в ячейках сохраняется inline Markdown. На узком экране контейнер `.md-table-wrap` прокручивается по горизонтали.
- `api.js` — высокоуровневые обёртки (listChats, sendMessage и др.)
- `files.js` — кэш URL файлов, upload
- `messages.js` — рендеринг пузырей, вложений, аудиоплеер. Маркер «изм.» в `.msg-meta` для `msg.isEdited`. **Компоновка вложений** (`buildMessageElement`): флаги `hasImages`/`imageOnly`/`docsOnly` (по типам вложений, независимо от направления) → классы пузыря `has-images`/`image-only`/`docs-only`. `image-only` (только картинки без текста/forward) — медиа на всю площадь пузыря (`padding:0`), время+галочки полупрозрачным бейджем поверх картинки (`.msg-meta.msg-img-overlay-meta`); с текстом — сетка сверху без полей, время снизу. `docs-only` — компактный padding без двойной рамки. CSS — в `messenger.html` рядом с `.msg-bubble`.
- `realtime.js` — server-streaming подписки (new_message, message_read, message_edited, message_deleted, message_pinned, message_unpinned, all_messages_unpinned, online_status, private_messages, typing)
- `calls.js` / `calls-ui.js` — `BF.calls`: звонки через LiveKit (vendor `livekit-client.bundle.js`)
- `newchat.js` — `BF.newchat`: создание чатов
- `privatechat.js` — `BF.privateChat`: приватные E2E-чаты (Argon2id через `hash-wasm.umd.min.js`, ключи в localStorage)
- `sound.js` — `BF.sound`: звуки уведомлений
- `imageeditor.js` — `BF.imageEditor`: редактор изображений перед отправкой
- `folders.js` — `BF.folders`: папки чатов (горизонтальные вкладки `#folderTabs` над списком чатов, drag-and-drop реордер, контекстное меню чата `#chatContextMenu` с «Добавить/Удалить из папки», модалка `#folderEditOverlay` с emoji-сеткой 7×3). `init()` обязательно ДО `loadChats` — гайд требует «сперва папки, потом чаты».
- `pinned.js` — `BF.pinned`: закреплённые сообщения. Плашка `#pinnedBar` под `.chat-header` Telegram-style (`1/N` слева, превью текущего, клик → переключение по кругу + scroll к оригиналу). Большая модалка `#pinnedListOverlay` со всеми пинами (рендер через `BF.messages.buildMessageElement`) + кнопка «Открепить все» с подтверждением.
- `attach.js` — диалог прикрепления файлов: сегментированный переключатель images/docs, превью (сетка/список с иконкой расширения и размером), поле подписи (`#attachCaption`, prefill из `#messageInput`, Enter=отправка, Shift+Enter=перенос, Escape=закрыть). Подпись передаётся третьим аргументом callback'а (`outFiles, asDocuments, caption`) и используется как текст сообщения. **Клик по превью-картинке** открывает редактор `BF.imageEditor.open(file, cb)`; callback заменяет `item.file` на отредактированный, отзывает старый `previewUrl` и перерендеривает сетку.
- `imageeditor.js` — `BF.imageEditor`: модальный canvas-редактор изображения (`#imgEditorOverlay`, z-index 260, открывается из `attach.js`). См. раздел [[#Редактор изображений (imageeditor.js)]].
- `settings.js` — многоэкранная панель настроек. Разделы: профиль (имя, юзернейм, био, аватар), пароль, 2FA (TOTP + Email), активные сессии (с переименованием устройства через `RenameDevice` и кнопкой «Завершить все остальные»), **приватность** (`GetPrivacySettings`/`UpdatePrivacySettings` — toggles `profileVisibleOnSite`/`searchVisible` + сегментированные radio для `avatarVisibility`/`bioVisibility`/`emailVisibility`/`onlineVisibility` через `ProfileFieldVisibility` enum), **персонализация** (по образцу Android/Mac: сверху превью профиля «постер 3:1 + аватар overlay + имя/@username», кнопка смены постера → модальный кроп `#posterCropOverlay` с draggable рамкой 3:1 и canvas-экспортом JPEG 90% max 2400×800, далее `BF.files.uploadFile(blob, USER_PROFILE_POSTER=10)` + `SetProfilePoster`; ниже превью чата с 5 моковыми сообщениями для демонстрации настроек; локальные слайдеры закругления пузырей/радиуса размытия/затенения + toggle размытия — все хранятся в `localStorage` и применяются к реальному чату через CSS-переменные на `:root`; сетка фонов с карточкой «Без фона», выбранной обводкой и кнопкой «+» добавления через `UpdatePersonalization` + `MESSAGE_ATTACHMENT_IMAGE=2`), **о приложении** (версия, deviceId, браузер/ОС, origin). Сохранение имени/username выполняется последовательно (`ChangeName` → `ChangeUsername`), после чего `GetUser` перечитывает серверный профиль; сообщение «Сохранено» показывается только если сервер вернул новый username. Цветовая схема (light/dark/midnight) инжектится отдельным inline-скриптом в `messenger.html` через `MutationObserver` за `#sdBody`. UI-хелперы: `makeField`/`makeInput`/`makeHint`/`makeSaveBtn`/`makeToggleRow`/`makeSegmented` + локальные `buildSlider`/`openPosterCrop`/`bindCropDrag`.
- `personalization.js` — `BF.personalization`: локальные косметические настройки чата (по образцу Android `GlobalParam` / macOS `@AppStorage`). Хранит в `localStorage` ключи `bf_pers_bubble_radius`, `bf_pers_bg_blur_enabled`, `bf_pers_bg_blur_radius`, `bf_pers_bg_dim`, `bf_pers_bg_file_id`. Применяет к `:root` CSS-переменные `--msg-bubble-radius`, `--chat-bg-image`, `--chat-bg-blur`, `--chat-bg-dim-alpha`, которые читаются `.msg-bubble.incoming/.outgoing` и слоями `.messages-bg-layer`/`.messages-bg-dim` внутри `.messages-area`. `init()` вызывается из `main.js` сразу после `BF.realtime.startAll()`, сверяет выбранный bg-fileId с серверной коллекцией (`GetPersonalization`) и сбрасывает, если файл удалён.
- `main.js` — bootstrap мессенджера. `openProfile(userId)` рендерит постер собеседника через `user.profilePosterFileId` → `BF.files.getFileUrls()` в `#profilePoster` поверх `.profile-header` (аватар получает `margin-top: -56px` через CSS). Поле `profilePosterFileId` маппится в `api.js:mapUser`. **Deep-link из cookie**: `maybeOpenChatFromCookie()` (вызывается в INIT после `loadChats`) читает cookie `bf_open_chat` (ставится кнопкой «Написать в браузере» на странице пользователя [[Backend/WebServer]]), сразу удаляет её (одноразово), затем `SearchUsers(username)` → точное совпадение по username (case-insensitive) → `GetPersonChatId(userId)` → `openChat(chatId)`. Auth-gate в начале `main.js` гарантирует выполнение только на странице мессенджера (иначе редирект на логин, cookie переживает вход). Логика повторяет Android `DeepLinkActivity.resolveAndOpenChat`.
- В модалках профиля и информации о группе вкладки вложений имеют независимые DOM-панели и токены запросов: запоздавшая загрузка другой вкладки или другого чата не может подменить активный список. «Файлы» ищет документы по имени через `ListChatAttachments.file_name_query` с debounce 300 мс. Видео в «Медиа» помечено `▶`; GIF здесь показываются статичным server-preview, а у старых GIF без preview браузер фиксирует первый кадр. В ленте чата GIF остаются анимированными.

**Proto bundle** (`wwwroot/js/proto/barkfluff.bundle.js`):
Генерируется через `scripts/generate-proto.ps1` (или `.sh`). Требует: protoc, protoc-gen-grpc-web, Node.js (esbuild).

> `wwwroot/` содержит только `index.html`, `messenger.html`, `favicon.ico`, каталог `js/` и генерируемый при сборке `legal/` (см. [[#Согласие с документами (legal.js)]]). Отдельной мобильной страницы (`mobile.html`) нет — мобильный режим реализуется адаптивной вёрсткой основных страниц.

## Аутентификация (gRPC-Web)

- `x-auth-token` — JWT (plain text)
- Остальные заголовки — base64-encoded
- Server-streaming (Updates, Onliner) — `grpcwebtext` режим

## Real-time Подписки (`realtime.js`)

| Stream | Service | RPC | Назначение |
|--------|---------|-----|------------|
| updatesStream | UpdatesApi | SubscribeNewMessages | Новые сообщения |
| readStream | UpdatesApi | SubscribeMessagesRead | Статусы прочтения |
| editedStream | UpdatesApi | SubscribeMessagesEdited | Редактирование сообщений (синхронизация между устройствами) |
| deletedStream | UpdatesApi | SubscribeMessagesDeleted | Удаление сообщений (синхронизация между устройствами) |
| pinnedStream | UpdatesApi | SubscribeMessagesPinned | Закреп сообщения (синхронизация плашки `#pinnedBar`) |
| unpinnedStream | UpdatesApi | SubscribeMessagesUnpinned | Открепление одного сообщения |
| allUnpinnedStream | UpdatesApi | SubscribeAllMessagesUnpinned | Открепление всех сообщений в чате |
| privateStream | UpdatesApi | SubscribePrivateMessages | Новые приватные (E2E) сообщения |
| onlineStream | OnlinerApi | SubscribeToOnlineStatus | Онлайн/оффлайн |
| typingStream | OnlinerApi | SubscribeToTyping | Индикатор «печатает…» (только открытый чат) |

### Устойчивость стримов и catch-up

Сервер (`BarkFluff.Updates`) держит server-streaming подписку на `await Task.Delay(Timeout.Infinite, ct)` и пишет в поток **только** при реальном событии: ни heartbeat, ни replay пропущенного нет (`SubscribeNewMessagesRequest` пустой — отдаются лишь live-события с момента подписки). Поэтому весь учёт «пропущенного за время разрыва» лежит на клиенте.

Механизмы клиента (`realtime.js`):
- **exponential backoff** (2с → 30с) на error/end каждого стрима;
- **age-timer** — превентивный реконнект каждые `STREAM_MAX_AGE` (180с);
- **watchdog** — реконнект «молчащего» стрима после `STREAM_INACTIVITY_THRESHOLD` (90с, проверка раз в 30с): ловит «чёрные дыры», когда прокси/NAT молча дропнул сокет;
- **page-visibility** — при возврате на вкладку (`tab_visible`) восстанавливаются упавшие стримы (`reconnectDeadStreams`);
- **window `online`** — при возврате сети реконнект упавших стримов сразу, без ожидания backoff.

**Catch-up (важно):** при ЛЮБОМ ПЕРЕоткрытии потоков `new_messages/read/edited/deleted` (не первый запуск) `realtime.js` эмитит событие **`resync`**. Все catch-up-пути (`resync` с дебаунсом 1200мс, `connection_status` restore, `tab_visible`) идут через один тихий дифф-механизм:
- `resyncCurrentChatTail` → `diffFetchedTail`: сверяет хвост открытого чата и применяет **точечный дифф** теми же аппликаторами, что live-события — новые → `appendMessageToView`, правки → `applyMessageEdit` (сравнение по `editedAt`+тексту), удаления → `applyMessageDelete`, прочтения → `applyReadByUpdate` (только галочки, без мутации счётчиков). Без звуков/нотификаций. Fallback на полный `renderMessages` только если новые сообщения не в хвосте (окно прыгало через `scrollToMessage` или своё сообщение ушло раньше дебаунса) или окно пусто. Различий нет → DOM не трогается.
- `refreshChatListQuiet`: тянет первую страницу `listChats`, сравнивает сигнатуры чатов (`id|title|picture|countUnread|privateInviteState|lastActivityAt|lastMessage`) — `renderChatList` только при реальном различии.
- Приватные чаты по-прежнему перезагружаются целиком (`reloadCurrentPrivateChat`) — инкремент требует decrypt-aware диффа. Прежний `reloadCurrentChatMessages` (безусловный полный ререндер) удалён.

## Typing-индикатор («печатает…»)

Клиентская интеграция готового API [[Backend/Onliner]] (relay-модель). Индикатор — только в шапке открытого чата (`#chatHeaderStatus`), список чатов не затронут.

- **Модель подписки:** отдельный стрим на открытый чат — `subscribeTyping(chatId)` открывает `SubscribeToTyping([chatId])`, при смене чата стрим переоткрывается; `ChangeChatsInTypingSubscription` не используется. `unsubscribeTyping()` при уходе в приватный чат.
- **realtime.js:** `subscribeTyping`/`unsubscribeTyping` — зеркало `subscribeOnline` (backoff, age-timer 180с, watchdog 90с, UNAUTHENTICATED→refresh, `reconnectDeadStreams`, `stopAll`); emit `'typing'` `{chatId, userId, action}`. Известное отклонение: `reconnect()` (проактивный token-refresh) typing не переоткрывает — самолечится собственным backoff/watchdog.
- **api.js:** `setTypingStatus(chatId, typing)` — unary `SetTypingStatus`, action 1=TYPING / 2=CANCELLED, ошибки глотаются.
- **main.js — отправка:** listener `input` на `#messageInput` (guard `currentChatType !== 0`): первый непустой ввод → TYPING + interval 4с; idle ≥5с → стоп без CANCELLED; пустое поле → CANCELLED. Программный `value=''` НЕ триггерит `input` → явный `stopTypingSend(true)` в начале `sendMessage()`/`sendMessageWithFiles()`. В `openChat()` `stopTypingSend(true)` вызывается ДО перезаписи `currentChatId` (CANCELLED уходит в старый чат).
- **main.js — приём:** `BF.realtime.on('typing', ...)`: фильтр по chatId (case-insensitive) и `myUserId`; `typingUsers: Map<userId, timeout>` — гашение 6с. `renderTypingIndicator()`: 1:1 — «печатает…», восстановление через `updateChatHeaderOnline(peerId)` (в ней guard `typingUsers.size > 0`); группа — имена из кэша `getUser` («Иван, Мария печатают…», макс 3), восстановление «N участников». `clearTypingReceiveState()` при смене чата.
- Приватные чаты: heartbeat не шлётся, стрим не открывается.

## Редактирование и удаление сообщений (`main.js`)

- Контекстное меню сообщения (`#msgContextMenu`): пункты **Изменить** / **Удалить** видны только для своих не-системных сообщений (фильтрация в `openContextMenu`).
- **Копировать текст** (`data-act="copy-text"`) — виден если у сообщения есть текст (`navigator.clipboard.writeText`). **Копировать изображение** (`data-act="copy-image"`) — виден только если ровно одно изображение и оно единственное медиа; `copyImageToClipboard(fileId)` берёт превью из кэша → `fetch` → конвертация в `image/png` через canvas → `ClipboardItem`. `imageFileId` снимается из `contextMenuTarget` до `closeContextMenu()`.
- **Edit**: `setPendingEdit(msg)` кладёт текст в `#messageInput`, показывает блок `#editPreviewBar`. `sendMessage()` вместо `SendMessage` вызывает `BF.api.editMessage(messageId, text, keepFileIds)`. `keepFileIds` — все не-forward вложения исходного сообщения (forward-снапшоты не редактируются на бэке).
- **Delete**: `requestDelete(messageId)` показывает `#deleteMsgConfirmOverlay`, по подтверждению — `BF.api.deleteMessage(messageId)`.
- `applyMessageEdit/applyMessageDelete` обновляют локальный `messages[]`, перерендеривают bubble (через `BF.messages.buildMessageElement`) или удаляют DOM-узел; обновляют `chat.lastMessage` в чат-листе.
- Realtime: подписки `BF.realtime.on('message_edited' | 'message_deleted', ...)` зеркалируют изменения с других устройств.
- Во время `pendingEdit` attach-flow (`sendMessageWithFiles`) заблокирован, чтобы drop/paste не отправил новое сообщение поверх правки.
- В обработчике клика по `#msgContextMenu` `isOutgoing` снимается из `contextMenuTarget` **до** `closeContextMenu()` (иначе `contextMenuTarget = null` обнуляется и условия для edit/delete всегда падают). Сопоставление сообщения — `Number(m.id) === Number(msgId)`, чтобы пережить возможное расхождение типов int64.

## Полноэкранный просмотрщик медиа (`main.js`)

Оверлей `#imageOverlay` (img/video) с листанием по всем медиа чата. Кнопки `#overlayPrev`/`#overlayNext` (`.overlay-nav`) + стрелки клавиатуры (`ArrowLeft`/`ArrowRight`, активны пока оверлей виден).

- `viewerState = {chatId, items, index, offset, exhausted, totalCount, loading}` — кэш списка на текущий чат; сбрасывается при смене `currentChatId`.
- Источник — `BF.api.listChatAttachments(chatId, type, offset, size)` (`api.js`): отдельно картинки (`type=1`) и видео (`type=2`), `VIEWER_PAGE=30`, склейка без дублей по `fileId`, сортировка по `sentAt DESC`. **Не** дёргает `ListMessages`.
- `showMediaOverlay` после показа кадра вызывает `viewerInit(fileId)` — ищет индекс кликнутого медиа, при ненахождении догружает страницы (`viewerLoadMore`) до нахождения/`exhausted`.
- `viewerNav(dir)`/`viewerShow(index)` — переключение с пре-загрузкой соседних страниц у конца списка; `refreshFileUrl` против протухания presigned. Видео листается наравне с фото.

## Контекст вкладки браузера (`main.js`)

`baseTitle` и `<link id="favicon">` динамически меняются при открытии чата:
- **Приватный чат** — после `getChatInfo` вызывается `getUser(peerId)` → `setChatTabContext('Чат с @' + username, peer.profilePicturePreview || peer.profilePicture)`. Это меняет `document.title` (через `updateTitleBadge`, который сохраняет префикс `(N)` для непрочитанных) и favicon.
- **Групповой чат / нет собеседника** — `resetChatTabContext()` возвращает дефолтный заголовок «BarkFluff — Мессенджер» и `/favicon.ico`.
- Гонка между быстрым переключением чатов решается проверкой `chatId !== currentChatId` в callback'е `getUser`.

Механизмы: exponential backoff (2с → 30с), page-visibility reconnection, keep-alive ping каждые 3с, tab title badge `(N)`, Browser Notification API, scroll-based mark-as-read.

## Папки чатов (`folders.js`)

Все RPC — `UsersApi` (см. [[Backend/Users-ChatFolders-ClientGuide]]). `chat_id` в API папок — **string (Guid)**, тот же формат что `Chat.id` из `messages_api`. Локально `chatToFolders: Map<string, Set<string>>`.

- **Bootstrap**: `BF.folders.init()` (`getChatFolders` → заполняет `foldersById/sortedFolderIds/chatToFolders` → `renderTabs`) → затем `loadChats(true)`. Порядок жёсткий — иначе нечего фильтровать.
- **Вкладки** `#folderTabs`: первая всегда «Все чаты» (`activeFolderId === 'all'`), остальные — `<button class="folder-tab" draggable>{icon} {name}</button>`. ПКМ на вкладке → `openEditModal(folderId)` (редактирование/удаление). Клик → `setActiveFolderId` → перерендер chat list через колбэк `setOnChange(renderChatList)`.
- **Фильтр чатов**: `renderChatList()` применяет `BF.folders.filterChats(chats)` перед итерацией. Для `'all'` — без изменений; для папки — `chats.filter(c => folder.chatList.includes(c.id))`.
- **DnD реордер**: `dragstart`/`dragover`/`drop` на `.folder-tab` (кроме «Все чаты»). При drop пересчитывает `sortOrder` по новому порядку и шлёт `reorderChatFolders([{folderId, sortOrder}])`. На ошибке — full refetch.
- **Контекстное меню чата** `#chatContextMenu` (новое, отдельное от `#msgContextMenu`): ПКМ на `.chat-item` → секции «Добавить в папку» (папки без чата) / «Удалить из папки» (папки с чатом) + всегда пункт «Создать папку».
- **Модалка** `#folderEditOverlay`: input имени (1-64 после Trim) + emoji-сетка 7×3 (20 пресетов + «без иконки»). Save → `createFolder` или `updateFolder({folderName, folderIcon})`. В режиме редактирования есть кнопка «Удалить» с `confirm()`.

## Закреплённые сообщения (`pinned.js`)

RPC — `MessagesApi.PinMessage/UnpinMessage/ListPinnedMessages/UnpinAll`, стримы `Subscribe*Pinned*` (см. [[Backend/Messages-PinnedMessages-ClientGuide]]). `MessageId` — `int64`.

- **Init**: `BF.pinned.init({getMyUserId, getCurrentChatInfo, getUser, showMediaOverlay, scrollToMessage})` — main.js передаёт зависимости один раз.
- **На каждый openChat**: `BF.pinned.openForChat(chatId)` → `listPinnedMessages(chatId, 0, 50)` → state (`byMessageId`, `sorted` по `pinnedAt DESC`, `barIndex=0`, `totalCount`) → `renderBar`.
- **Плашка** `#pinnedBar`: `1/N` (скрыто если `N=1`), превью `sorted[barIndex]` (имя автора + текст/emoji вложения), кнопка справа `#pinnedBarOpen`. Клик по `.pinned-bar-preview` → `scrollToMessage(currentPinId)` + `barIndex = (barIndex+1) % N`.
- **Контекстное меню сообщения** (`main.js::openContextMenu`): добавлена кнопка `data-act="pin"` с динамическим текстом «Закрепить»/«Открепить» (`BF.pinned.isPinned(msgId)`). Скрывается для системных сообщений.
- **Realtime**: `applyPinnedEvent` (для current chat — refetch listPinnedMessages, иначе игнор), `applyUnpinnedEvent` (drop из map), `applyAllUnpinnedEvent` (clear). `applyMessageDeleted` вызывается из `applyMessageDelete` в main.js — превентивно убирает из плашки до прихода `message_unpinned`.
- **Большая модалка** `#pinnedListOverlay`: список облачков через `BF.messages.buildMessageElement` + подпись «Закрепил {имя} · {pinnedAt}». Кнопка «Открепить все» → `confirm()` → `unpinAll`. Лимит 100 — на 101 пин backend бросает `TooManyPinnedMessagesException` (`F7E1A4B8-2C9D-4F3A-B6E7-8D5C1A0F9B23`), клиент показывает alert.

## Редактор изображений (imageeditor.js)

Модальный canvas-редактор для картинок перед отправкой. Открывается из `attach.js` по клику на превью; не зависит от бэкенда (всё локально в браузере). Стили `.ie-*` и overlay `#imgEditorOverlay` — в `messenger.html` (по образцу `.sd-crop-*` poster-crop). Подключён `<script>` после `attach.js`, инициализируется `BF.imageEditor.init()` в `main.js` рядом с `BF.attach.init()`.

**API:** `BF.imageEditor.open(file, onResult)` — `onResult(newFile|null)`; `null` = отмена. Результат — `new File([...], '<имя>.jpg', {type:'image/jpeg'})`, качество **0.92**.

**Архитектура — 3 canvas-буфера в натуральных координатах изображения:**
- `baseCanvas` — фото после применённых поворота/отражения («запечённый» буфер).
- `drawCanvas` — слой штрихов (кисть/мозаика/ластик), прозрачный. Ластик = `globalCompositeOperation='destination-out'` (стирает только штрихи, фото не трогает).
- `#ieCanvas` (displayCanvas) — видимый, каждый кадр композит `base`+`draw` с масштабом под `.ie-stage`. Маппинг экран→натуральные через `getBoundingClientRect()`.

**Инструменты** (тулбар `.ie-toolbar`): обрезка (свободная рамка `#ieCropFrame` с 4 ручками, без aspect-lock), поворот ±90° (`rotateLeft/rotateRight`), отражение H/V, кисть (цвет `#ieColor` + размер `#ieSize`), пикселизация (downscale→upscale через `ensurePixelated`, рисуется сквозь clip-окружность мазка), ластик. Указатель — Pointer Events (mouse+touch), `touch-action:none`.

**Трансформации — rebake:** поворот/отражение сразу запекаются (`rebakeBase` строит base из оригинала по абсолютному состоянию `rotate/flipH/flipV`; `applyDeltaToDraw` поворачивает/отражает слой штрихов дельтой). Повороты кратны 90° — lossless. После трансформации crop сбрасывается, `pixelatedCanvas` инвалидируется.

**Экспорт** (`exportFile`): crop переводится из дисплейных px в натуральные через `view.scale`; на выходной canvas заливается белый фон (JPEG без альфы) → `base` → `draw` → `toBlob('image/jpeg', 0.92)`.

**Ограничения:** рабочее разрешение `MAX_DIM=4096` (память); EXIF-ориентация фото с камеры применяется через `createImageBitmap(file,{imageOrientation:'from-image'})` с фолбэком на `<img>`; undo не реализован.

## Регистрация по шагам (register.js)

Модальный мастер из 9 шагов, открывается по `#toRegisterBtn` на странице логина. Полностью повторяет flow мобильных/desktop клиентов ([[Клиенты/Android]], [[Клиенты/macOS]], [[Клиенты/iOS]]). Самодостаточный модуль (как `auth.js`): собственные gRPC-Web клиенты (`IdentityApiClient`/`UsersApiClient`/`FilesApiClient`), metadata строится вручную через `BF.metadata.build([token])`. Разметка модалки `#registerOverlay` и CSS `.reg-*` — в `index.html`.

Шаги 1–4 анонимны (без токена); после `ConfirmAccount`+`CreateToken` access/refresh токены сохраняются в `BF.tokens` (тот же формат, что у логина), и шаги 5–9 идут авторизованными.

| Шаг | Поля | RPC | Auth |
|-----|------|-----|------|
| 1. Имя/фамилия | firstName (3–40), lastName (≤40, опц.) | — (локально) | — |
| 2. Username | lowercase, `^[a-z0-9_-]+$`, 3–30, не с цифры | `UsersApi.CheckExistUsername` (debounce 500мс) | анонимно |
| 3. Email | RFC-формат | `UsersApi.CheckExistEmail` (debounce) → при «Далее» `IdentityApi.CreateAccount` → `code_id` | анонимно |
| 4. OTP-код | 6 цифр (авто-submit) | `IdentityApi.ConfirmAccount` → `refresh_token`, затем `IdentityApi.CreateToken` → `access_token` + `BF.tokens.save` | анонимно |
| 5. Пароль | password+confirm (min 8, индикатор силы по 5 критериям) | `IdentityApi.SetPassword(password, old_password="")` | токен |
| 6. Аватар (skip) | фото → интерактивный квадратный кроп (canvas, drag+zoom, экспорт 512×512 JPEG 0.85) | `FilesApi.GetUploadUrl(USER_AVATAR=1)` → `POST /api/files/upload/{fileId}` → `UsersApi.SetProfilePicture` | токен |
| 7. Био (skip) | bio (≤200) | `UsersApi.ChangeBio` (только если непусто) | токен |
| 8. 2FA (skip) | 6 цифр | `IdentityApi.EnableOtpVerification(Authenticator)` → QR+secret; при «Подтвердить» `IdentityApi.ConfirmOtpVerification` | токен |
| 9. Готово | — | редирект на `/messenger` | — |

- **Навигация**: кнопка «Назад» только на шагах 2–3 (после создания аккаунта возврат запрещён); «Пропустить» — на 6/7/8; прогресс-бар `#regProgressBar`.
- **Graceful-фолбэк**: ошибки сети при `CheckExist*` и опциональных шагах (аватар/био) не блокируют регистрацию — сервер валидирует повторно при `CreateAccount`.
- **Resend OTP**: кулдаун 60с, повторно вызывает `CreateAccount`.

## Согласие с документами (`legal.js`)

Блок на странице входа: чекбокс «Я принимаю Пользовательское соглашение и Политику конфиденциальности» + информационная плашка про cookie. Повторяет схему Android (`LegalConsentBottomSheet` + `GlobalParam.acceptedLegalRevision`, см. [[Клиенты/Android]]): один чекбокс на **оба** документа, и хранится **редакция**, а не флаг.

**Редакция** — строка `**Последнее обновление:** …` из шапки `TERMS_OF_SERVICE.ru.md` (сейчас `29 июля 2026 г.`). Парсер повторяет `LegalDocsRepository.revision`: последняя строка вида `**Метка:** значение` в первых 8 строках, всегда из русского оригинала. Обновилась дата в документе — cookie перестаёт совпадать, согласие спрашивается заново. Хардкодить дату нельзя.

**Что блокируется до согласия** (`login-page.js`):

| Путь входа | Как заблокирован |
|---|---|
| Форма входа | `#signInBtn.disabled`, submit-хендлер выходит раньше и подсвечивает строку (`.legal-consent.nudge`) |
| Регистрация | `#toRegisterBtn.disabled` — мастер не открывается |
| QR fast-auth | `BF.fastAuth.start()` не вызывается, `.qr-card` получает класс `gated`, статус — «Примите документы, чтобы войти по QR» |

QR блокируется **обязательно**: это полноценный вход, и без этого согласие обходится в один клик. `startFastAuth()` — единственная точка запуска, `showSection('login')` и bootstrap идут через неё.

**Хранение — два уровня:**
- cookie `bf_legal_accepted=<редакция>` (`path=/`, 1 год, `SameSite=Lax`) — это и есть гейт до входа;
- `UsersApi.AcceptLegalConsent(revision)` — запись в профиль (`User.AcceptedLegalRevision` / `AcceptedLegalAt`, см. [[Backend/Users]]). Вызывается **после** появления токена: до входа RPC вызвать нечем. Точки вызова — `login-page.js` перед редиректом на `/messenger` и `register.js` сразу после `BF.tokens.save` на шаге 4. `flushConsent()` возвращает промис, который резолвится не дольше `FLUSH_TIMEOUT` (1500 мс), чтобы медленная сеть не задерживала вход.

**Модалка чтения** `#legalOverlay` — переиспользует `.reg-overlay` / `.reg-dialog` / `.reg-header`, свои классы только `.legal-tabs` / `.legal-tab` / `.legal-body` (`.legal-dialog` расширяет ширину до 640px). Вкладки «Соглашение» / «Политика», открывается кликом по названию документа в строке согласия. Рендер — существующий `BF.utils.renderMarkdown`, из-за чего `utils.js` подключён и в `index.html` (раньше только в `messenger.html`).

**Откуда берётся текст.** `web.barkfluff.com` и `barkfluff.com` — разные origin, `fetch` за документом упёрся бы в CORS. Поэтому MSBuild-таргет `CopyLegalDocs` (`BarkFluff.Web.csproj`, `BeforeTargets="AssignTargetPaths"`) копирует `..\Barkfluff.WebServer\html\legal\{TERMS_OF_SERVICE,PRIVACY_POLICY}.*.md` в `wwwroot/legal/`. Зеркало gradle-таска `copyLegalDocs` у Android; источник по-прежнему один — [[Backend/WebServer]]. Копии сгенерированные, поэтому `wwwroot/legal/` в `.gitignore`, а каталог исключён из дефолтных Content-глобов SDK (`<Content Remove="wwwroot\legal\**" />`), иначе на второй сборке файлы попали бы в Content дважды.

**Устойчивость.** Если документ не загрузился, `revision` остаётся пустой и `isAccepted()` считает достаточным сам факт cookie — недоступный файл не должен запирать вход. Гейт применяется дважды: сразу по cookie (вернувшийся пользователь не видит мигания заблокированной формы) и повторно после `init()`, когда редакция прочитана.

**Cookie-уведомление** — плашка `#cookieNotice` с одной кнопкой «Понятно». Имя и значение cookie (`bf_cookie_notice=1`) совпадают с баннером сайта, который ставит её на `.barkfluff.com`, — принявшему на `barkfluff.com` здесь ничего не покажется.

**Не сделано намеренно:** Android / WinUI / macOS / iOS `AcceptLegalConsent` не отправляют — у них согласие остаётся локальным. Серверного гейта (отказ в `Auth` без принятой редакции) нет: это поменяло бы поведение сразу всех клиентов.

## QR Fast-Auth (`fast-auth.js`)

QR-вход на странице логина — анонимный поток (без токена), повторяет шаблон [[Клиенты/Windows]] / [[Клиенты/MacOS]].

| Шаг | RPC | Тип | Auth |
|-----|-----|-----|------|
| 1. Получить QR-PNG | `FastAuthApi.GenerateFastAuthToken({format: QR})` | unary | анонимно |
| 2. Подписка на статус | `FastAuthApi.SubscribeFastAuthResult({fast_auth_id})` | server-streaming | анонимно |

Метаданные устройства передаются в gRPC headers (`x-device-name`, `x-os-name`, `x-app-name`, `x-app-version`, `x-device-id`, `x-ip-address`, base64).

Финальные статусы: `ACCEPTED` (получаем `access_token` + `refresh_token` → `BF.tokens.save` → редирект на `/messenger`), `REJECTED` (toast и автоперезапуск через 1с), `EXPIRED` (молчаливый перезапуск). На сетевой разрыв — exponential backoff (2с → 30с).

YARP-маршрут `fast-auth` входит в `streamingServices` set — `ActivityTimeout: 24h`.

## Зависимости

- `Yarp.ReverseProxy` 2.3.0 + `AWSSDK.Core` 4.0.7.4
- [[Backend/GrpcServer]] — Serilog, MetricsCollector, LoadConfiguration
- Кастомный `GrpcWebResponseStream` в `Program.cs` — base64-обёртка для server-streaming через gRPC-Web (без `Grpc.AspNetCore.Web`, который несовместим с YARP)
