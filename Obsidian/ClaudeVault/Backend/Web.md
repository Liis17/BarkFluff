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
2. **gRPC-Web прокси** — кастомный middleware (`GrpcWebResponseStream` в `Program.cs`) конвертирует `application/grpc-web-text` (HTTP/1.1) → HTTP/2 gRPC; YARP проксирует к бэкенд-сервисам. Используется собственная реализация, так как `Grpc.AspNetCore.Web` не работает с YARP.
3. **HTTP upload прокси** — `POST /api/files/upload/{uploadId}` → Files-сервис (HTTP-порт **7006**)

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

**Мессенджер** (messenger.html):
- `clients.js` — gRPC-Web клиенты, authCall с auto-refresh
- `utils.js` — форматирование, escapeHtml, parseJwt
- `api.js` — высокоуровневые обёртки (listChats, sendMessage и др.)
- `files.js` — кэш URL файлов, upload
- `messages.js` — рендеринг пузырей, вложений, аудиоплеер. Маркер «изм.» в `.msg-meta` для `msg.isEdited`
- `realtime.js` — server-streaming подписки (new_message, message_read, message_edited, message_deleted, message_pinned, message_unpinned, all_messages_unpinned, online_status)
- `folders.js` — `BF.folders`: папки чатов (горизонтальные вкладки `#folderTabs` над списком чатов, drag-and-drop реордер, контекстное меню чата `#chatContextMenu` с «Добавить/Удалить из папки», модалка `#folderEditOverlay` с emoji-сеткой 7×3). `init()` обязательно ДО `loadChats` — гайд требует «сперва папки, потом чаты».
- `pinned.js` — `BF.pinned`: закреплённые сообщения. Плашка `#pinnedBar` под `.chat-header` Telegram-style (`1/N` слева, превью текущего, клик → переключение по кругу + scroll к оригиналу). Большая модалка `#pinnedListOverlay` со всеми пинами (рендер через `BF.messages.buildMessageElement`) + кнопка «Открепить все» с подтверждением.
- `attach.js` — диалог прикрепления файлов: сегментированный переключатель images/docs, превью (сетка/список с иконкой расширения и размером), поле подписи (`#attachCaption`, prefill из `#messageInput`, Enter=отправка, Shift+Enter=перенос, Escape=закрыть). Подпись передаётся третьим аргументом callback'а (`outFiles, asDocuments, caption`) и используется как текст сообщения. **Клик по превью-картинке** открывает редактор `BF.imageEditor.open(file, cb)`; callback заменяет `item.file` на отредактированный, отзывает старый `previewUrl` и перерендеривает сетку.
- `imageeditor.js` — `BF.imageEditor`: модальный canvas-редактор изображения (`#imgEditorOverlay`, z-index 260, открывается из `attach.js`). См. раздел [[#Редактор изображений (imageeditor.js)]].
- `settings.js` — многоэкранная панель настроек. Разделы: профиль (имя, юзернейм, био, аватар), пароль, 2FA (TOTP + Email), активные сессии (с переименованием устройства через `RenameDevice` и кнопкой «Завершить все остальные»), **приватность** (`GetPrivacySettings`/`UpdatePrivacySettings` — toggles `profileVisibleOnSite`/`searchVisible` + сегментированные radio для `avatarVisibility`/`bioVisibility`/`emailVisibility`/`onlineVisibility` через `ProfileFieldVisibility` enum), **персонализация** (по образцу Android/Mac: сверху превью профиля «постер 3:1 + аватар overlay + имя/@username», кнопка смены постера → модальный кроп `#posterCropOverlay` с draggable рамкой 3:1 и canvas-экспортом JPEG 90% max 2400×800, далее `BF.files.uploadFile(blob, USER_PROFILE_POSTER=10)` + `SetProfilePoster`; ниже превью чата с 5 моковыми сообщениями для демонстрации настроек; локальные слайдеры закругления пузырей/радиуса размытия/затенения + toggle размытия — все хранятся в `localStorage` и применяются к реальному чату через CSS-переменные на `:root`; сетка фонов с карточкой «Без фона», выбранной обводкой и кнопкой «+» добавления через `UpdatePersonalization` + `MESSAGE_ATTACHMENT_IMAGE=2`), **о приложении** (версия, deviceId, браузер/ОС, origin). Цветовая схема (light/dark/midnight) инжектится отдельным inline-скриптом в `messenger.html` через `MutationObserver` за `#sdBody`. UI-хелперы: `makeField`/`makeInput`/`makeHint`/`makeSaveBtn`/`makeToggleRow`/`makeSegmented` + локальные `buildSlider`/`openPosterCrop`/`bindCropDrag`.
- `personalization.js` — `BF.personalization`: локальные косметические настройки чата (по образцу Android `GlobalParam` / macOS `@AppStorage`). Хранит в `localStorage` ключи `bf_pers_bubble_radius`, `bf_pers_bg_blur_enabled`, `bf_pers_bg_blur_radius`, `bf_pers_bg_dim`, `bf_pers_bg_file_id`. Применяет к `:root` CSS-переменные `--msg-bubble-radius`, `--chat-bg-image`, `--chat-bg-blur`, `--chat-bg-dim-alpha`, которые читаются `.msg-bubble.incoming/.outgoing` и слоями `.messages-bg-layer`/`.messages-bg-dim` внутри `.messages-area`. `init()` вызывается из `main.js` сразу после `BF.realtime.startAll()`, сверяет выбранный bg-fileId с серверной коллекцией (`GetPersonalization`) и сбрасывает, если файл удалён.
- `main.js` — bootstrap мессенджера. `openProfile(userId)` рендерит постер собеседника через `user.profilePosterFileId` → `BF.files.getFileUrls()` в `#profilePoster` поверх `.profile-header` (аватар получает `margin-top: -56px` через CSS). Поле `profilePosterFileId` маппится в `api.js:mapUser`.

**Proto bundle** (`wwwroot/js/proto/barkfluff.bundle.js`):
Генерируется через `scripts/generate-proto.ps1` (или `.sh`). Требует: protoc, protoc-gen-grpc-web, Node.js (esbuild).

> `wwwroot/` содержит только `index.html`, `messenger.html`, `favicon.ico` и каталог `js/`. Отдельной мобильной страницы (`mobile.html`) нет — мобильный режим реализуется адаптивной вёрсткой основных страниц.

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
| onlineStream | OnlinerApi | SubscribeToOnlineStatus | Онлайн/оффлайн |

## Редактирование и удаление сообщений (`main.js`)

- Контекстное меню сообщения (`#msgContextMenu`): пункты **Изменить** / **Удалить** видны только для своих не-системных сообщений (фильтрация в `openContextMenu`).
- **Edit**: `setPendingEdit(msg)` кладёт текст в `#messageInput`, показывает блок `#editPreviewBar`. `sendMessage()` вместо `SendMessage` вызывает `BF.api.editMessage(messageId, text, keepFileIds)`. `keepFileIds` — все не-forward вложения исходного сообщения (forward-снапшоты не редактируются на бэке).
- **Delete**: `requestDelete(messageId)` показывает `#deleteMsgConfirmOverlay`, по подтверждению — `BF.api.deleteMessage(messageId)`.
- `applyMessageEdit/applyMessageDelete` обновляют локальный `messages[]`, перерендеривают bubble (через `BF.messages.buildMessageElement`) или удаляют DOM-узел; обновляют `chat.lastMessage` в чат-листе.
- Realtime: подписки `BF.realtime.on('message_edited' | 'message_deleted', ...)` зеркалируют изменения с других устройств.
- Во время `pendingEdit` attach-flow (`sendMessageWithFiles`) заблокирован, чтобы drop/paste не отправил новое сообщение поверх правки.
- В обработчике клика по `#msgContextMenu` `isOutgoing` снимается из `contextMenuTarget` **до** `closeContextMenu()` (иначе `contextMenuTarget = null` обнуляется и условия для edit/delete всегда падают). Сопоставление сообщения — `Number(m.id) === Number(msgId)`, чтобы пережить возможное расхождение типов int64.

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

- `Yarp.ReverseProxy` 2.2.0 — единственный NuGet-пакет
- [[Backend/GrpcServer]] — Serilog, MetricsCollector, LoadConfiguration
- Кастомный `GrpcWebResponseStream` в `Program.cs` — base64-обёртка для server-streaming через gRPC-Web (без `Grpc.AspNetCore.Web`, который несовместим с YARP)
