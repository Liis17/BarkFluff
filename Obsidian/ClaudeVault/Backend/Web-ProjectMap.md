# BarkFluff.Web — Карта проекта

Детальная карта всех файлов сервиса `Backend/BarkFluff.Web/`.
Связанная заметка: [[Backend/Web]]

---

## Серверная часть (.NET)

### `Program.cs`
Единственный C#-файл сервиса. Содержит весь pipeline ASP.NET Core.

**Ответственности:**
- **Kestrel**: слушает порт из конфига (`RunSettings:Port`, дефолт 7016), `Http1AndHttp2`
- **CORS**: разрешённые origins из `Web:AllowedOrigins`; пробрасывает gRPC-Web-заголовки и трейлеры
- **ForwardedHeaders**: доверяет X-Forwarded-For от Docker/nginx-сетей (172.16/12, 10/8, 192.168/16)
- **MetricsCollector**: счётчики `http_requests_total` / `http_requests_errors`
- **Security-заголовки**: `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`, `X-Frame-Options: DENY`
- **gRPC-Web middleware** (ручная реализация): декодирует `application/grpc-web-text` (base64, HTTP/1.1) → `application/grpc` (binary, HTTP/2) для запроса; для ответа перехватывает тело через `GrpcWebResponseStream` и кодирует каждый data-frame обратно в base64; добавляет trailer-frame при завершении потока; защита от больших тел — 4 МБ
- **Static files**: раздаёт `wwwroot/`
- **`/health`**: HealthChecks
- **`/messenger`**: возвращает `messenger.html`
- **YARP `MapReverseProxy()`**: форвардит gRPC и HTTP-запросы в бэкенд-сервисы
- **Режим `Web:Mode=Proxy`**: пропуск `LoadConfiguration`, gRPC-Web конвертация не выполняется (pass-through `grpc-web-text` на кластер `remote`), fail-fast валидация `Web:Proxy:Target`, эндпоинт `/media/{host}/{**path}` (media-relay c allowlist `Web:Proxy:MediaHosts`, Range/If-Range, стриминг, named HttpClient `media-relay` без таймаута)
- **SPA Fallback**: все URL кроме `/api/*` и `/health` → `index.html`

**`BuildRoutes()`** — YARP маршруты:

| RouteId | Path | ClusterId |
|---------|------|-----------|
| `grpc-identity` | `/barkfluff.identity.IdentityApi/{**catchall}` | identity |
| `grpc-users` | `/barkfluff.users.UsersApi/{**catchall}` | users |
| `grpc-messages` | `/barkfluff.messages.MessagesApi/{**catchall}` | messages |
| `grpc-files` | `/barkfluff.files.FilesApi/{**catchall}` | files |
| `grpc-updates` | `/barkfluff.updates.UpdatesApi/{**catchall}` | updates |
| `grpc-onliner` | `/barkfluff.onliner.OnlinerApi/{**catchall}` | onliner |
| `grpc-fast-auth` | `/barkfluff.fast.auth.FastAuthApi/{**catchall}` | fast-auth |
| `grpc-calls` | `/barkfluff.calls.CallsApi/{**catchall}` | calls |
| `files-http-upload` | `POST /api/files/upload/{uploadId}` | files-http |
| `remote-catchall` (только Proxy) | `/{**catchall}` | remote |

**`BuildClusters()`** — YARP кластеры:

| ClusterId | Конфиг-ключ | Дефолт | Протокол | ActivityTimeout |
|-----------|-------------|--------|----------|-----------------|
| identity | `IdentityService:Host` | http://identity:7000 | HTTP/2 | 100 с |
| users | `UsersService:Host` | http://users:7001 | HTTP/2 | 100 с |
| messages | `MessagesService:Host` | http://messages:7007 | HTTP/2 | 100 с |
| files | `FilesService:Host` | http://files:7005 | HTTP/2 | 100 с |
| updates | `UpdatesService:Host` | http://updates:7015 | HTTP/2 | **24 ч** (streaming) |
| onliner | `OnlinerService:Host` | http://onliner:7009 | HTTP/2 | **24 ч** (streaming) |
| fast-auth | `FastAuthService:Host` | http://fast-auth:7008 | HTTP/2 | **24 ч** (streaming) |
| calls | `CallsService:Host` | http://calls:7025 | HTTP/2 | **24 ч** (streaming) |
| files-http | `FilesService:HttpHost` | http://files:7006 | HTTP/1.1 | 100 с |
| remote (только Proxy) | `Web:Proxy:Target` | — | HTTP/1.1 | **24 ч** |

**`GrpcWebResponseStream`** (вложенный класс) — потоковый враппер `Stream`:
- Кодирует данные в base64 на лету (режим `grpc-web-text`) — каждый `WriteAsync` кодирует свой чанк независимо (без буферизации неполных триплетов между вызовами)
- В режиме `grpc-web` (бинарный) данные проходят насквозь

---

## Конфигурация

### `appsettings.json`
```json
{
  "Web": {
    "AllowedOrigins": ["http://localhost:7016", "https://barkfluff.com"]
  }
}
```

### `appsettings.Development.json`
Переопределение для среды разработки.

### `Properties/launchSettings.json`
Профили запуска (VS / dotnet run).

---

## Frontend — страницы

### `wwwroot/index.html`
Страница входа. Подключает: `/node-config.js`, `node.js`, `device.js`, `tokens.js`, `metadata.js`, `utils.js`, `auth.js`, `legal.js`, `nodepicker.js`, `fast-auth.js`, `login-page.js`, `register.js`, proto-bundle. Содержит секцию выбора сервера `#nodeSection` (стили `.node-*`) и плашку текущей ноды `#nodeBar`. Содержит разметку модалки регистрации `#registerOverlay` со стилями `.reg-*`, читалку документов `#legalOverlay` (`.legal-*`, переиспользует `.reg-dialog`), строку согласия `#legalConsentRow` и плашку `#cookieNotice`.

### `wwwroot/legal/`
Генерируется при сборке MSBuild-таргетом `CopyLegalDocs` из `Backend/Barkfluff.WebServer/html/legal/*.md`. В git не хранится (`.gitignore`).

### `wwwroot/messenger.html`
Главный мессенджер. Подключает все JS-модули из `js/app/`.

### `wwwroot/css/icons.css`
Стили общего SVG-пакета `/icons/`: отображают монохромные файлы как CSS mask, наследующую
`currentColor` текущей темы.

### `wwwroot/icons/`
Каталог SVG из корневого `icons/` (плоская структура), подключаемый ссылками MSBuild.
Пути сохраняют контракт `/{name}.svg`, общий для клиентов.

### `wwwroot/favicon.ico`
Иконка приложения.

---

## Frontend — JS модули (`wwwroot/js/app/`)

### `icons.js` → `BF.icons`
Единый helper для общего пакета иконок: строит безопасные URL, создаёт DOM-элементы и HTML
с `data-bf-icon`, а также гидратирует статическую разметку `messenger.html`.

### `node.js` → `BF.node`
Выбранная нода: `origin()` (синхронно из localStorage `bf_node_origin`), `id()`, `key(name)` —
неймспейс `@{origin}` для ключей хранилища, `pinned()`, `normalize()`, `set/clear/forget`,
`meta/setMeta`, `list()`, `beaconClient()`, `navigatorClient()`. Подключается **первым**
из app-модулей: клиенты gRPC-Web создаются синхронно и захватывают origin.
Разовая миграция домультинодовых ключей. Подробности — [[Клиенты/Web#Выбор ноды (мультинодовость)]].

### `nodepicker.js` → `BF.nodePicker`
Экран выбора ноды на `index.html` (`#nodeSection`): каталог `NavigatorApi.ListServers`,
история и ручной ввод; выбор подтверждается `BeaconApi.GetServerInfo`. `init/open/close/connect`.

### `device.js` → `BF.device`
Определяет и хранит `deviceId` (UUID, генерируется при первом визите, хранится в localStorage).
Определяет `browserName`, `osName` из `navigator.userAgent`.

### `tokens.js` → `BF.tokens`
TokenStore: хранит access/refresh токены в localStorage или sessionStorage.
Методы: `get`, `set`, `clear`, `getAccessToken`, `getRefreshToken`.

### `metadata.js` → `BF.metadata`
Собирает gRPC metadata: `x-auth-token` (JWT plain), остальные заголовки (deviceId, deviceName, IP, OS, appName, appVersion) — base64-encoded.

### `auth.js` → `BF.auth`
- `login(username, password, otp?)` — вызов IdentityApi
- `refreshToken()` — обновление JWT
- `getValidAccessToken()` — проверяет срок действия, при необходимости рефрешит

### `login-page.js` → `BF.loginPage`
Логика страницы `index.html`:
- Форма входа (username/password)
- OTP-форма (если 2FA включён)
- Проверка существующей сессии при загрузке страницы
- Гейт согласия: `applyGate()` блокирует `#signInBtn` / `#toRegisterBtn`, `startFastAuth()` — единственная точка запуска QR-сессии
- Редирект в `/messenger` после успешного входа (после `BF.legal.flushConsent()`)

### `register.js` → `BF.register`
Модальный мастер регистрации из 9 шагов на `index.html` (открывается по `#toRegisterBtn`). Самодостаточный (собственные gRPC-клиенты + ручная metadata). Шаги: имя → username → email → OTP → пароль → аватар (интерактивный кроп) → био → 2FA → готово. Подробности — [[Backend/Web#Регистрация по шагам (register.js)]].

### `legal.js` → `BF.legal`
Согласие с Пользовательским соглашением и Политикой конфиденциальности + cookie-уведомление на `index.html`. `init()` / `isAccepted()` / `accept()` / `flushConsent()` / `open(doc)`. Редакция парсится из шапки `TERMS_OF_SERVICE.ru.md`, хранится в cookie `bf_legal_accepted` и уходит в профиль через `UsersApi.AcceptLegalConsent`. Подробности — [[Backend/Web#Согласие с документами (legal.js)]].

### `clients.js` → `BF.clients`
gRPC-Web клиенты для всех сервисов (IdentityApi, UsersApi, MessagesApi, FilesApi, UpdatesApi, OnlinerApi).
Origin — `BF.node.origin()`; без выбранной ноды модуль редиректит на `/` до создания клиентов.
`authCall(fn)` — обёртка с auto-refresh токена.

### `utils.js` → `BF.utils`
Утилиты: форматирование дат/времени, `escapeHtml`, `parseJwt`, форматирование размера файла, прочие хелперы.

### `api.js` → `BF.api`
Высокоуровневые обёртки над gRPC-клиентами:
- Чаты: `listChats`, `getChat`, `createChat`
- Сообщения: `sendMessage`, `getMessages`, `markAsRead`
- Пользователи: `getUser`, `searchUsers`, `checkExistUsername`, `changeName`, `changeUsername`, `changeBio`, `setProfilePicture`
- Аккаунт: `setPassword`, `listOtpVerification`, `enableOtpVerification`, `disableOtpVerification`, `confirmOtpVerification`
- Сессии: `getActiveSessions`, `removeActiveSession`

### `files.js` → `BF.files`
- Кэш URL файлов (lazy-load из FilesApi)
- `uploadFile(file, fileType)` — multipart POST на `/api/files/upload/{uploadId}`
- Получение URL превью/вложений

### `messages.js` → `BF.messages`
Рендеринг пузырей сообщений в чате:
- Текстовые сообщения, вложения (изображения, документы), аудиоплеер
- Группировка по отправителю/дате
- Индикаторы прочтения

### `realtime.js` → `BF.realtime`
Server-streaming подписки:

| Stream | Сервис | RPC | Назначение |
|--------|--------|-----|------------|
| updatesStream | UpdatesApi | SubscribeNewMessages | Новые сообщения |
| readStream | UpdatesApi | SubscribeMessagesRead | Статусы прочтения |
| editedStream | UpdatesApi | SubscribeMessagesEdited | Редактирование сообщений |
| deletedStream | UpdatesApi | SubscribeMessagesDeleted | Удаление сообщений |
| pinnedStream | UpdatesApi | SubscribeMessagesPinned | Закреп сообщения |
| unpinnedStream | UpdatesApi | SubscribeMessagesUnpinned | Открепление сообщения |
| allUnpinnedStream | UpdatesApi | SubscribeAllMessagesUnpinned | Открепление всех |
| privateStream | UpdatesApi | SubscribePrivateMessages | Новые приватные (E2E) сообщения |
| onlineStream | OnlinerApi | SubscribeToOnlineStatus | Онлайн/оффлайн |

Механизмы надёжности: exponential backoff (2с → 30с), page-visibility reconnection, keep-alive ping каждые 3 с, `stopAll()` при выходе.

### `attach.js` → `BF.attach`
Диалог прикрепления файлов перед отправкой сообщения:
- Открывается при выборе файлов: `open(files, onSendCallback, prefillText?)`
- Два режима: **images** (сетка превью) / **docs** (список с иконкой расширения и человекочитаемым размером); сегментированный переключатель в шапке скрывается, если изображений нет
- Автоопределение типа по MIME / расширению
- Превью изображений через `URL.createObjectURL`, очистка через `URL.revokeObjectURL` при удалении / закрытии
- **Поле подписи** (`#attachCaption`): автоматически растёт до 140px, prefill из текста чата (`#messageInput`), Enter — отправка, Shift+Enter — перенос, Escape — закрыть
- Callback вызывается как `onSend(outFiles, asDocuments, caption)`; в `main.js` `sendMessageWithFiles` использует `caption` как текст сообщения и обнуляет `#messageInput`, чтобы текст не отправился отдельным сообщением
- В шапке диалога — счётчик файлов с правильным склонением (1 файл / 2 файла / 5 файлов)
- **Клик по превью-картинке** → `BF.imageEditor.open(item.file, cb)`; в callback заменяется `item.file`, отзывается старый `previewUrl`, перерендеривается сетка

### `imageeditor.js` → `BF.imageEditor`
Модальный canvas-редактор изображения перед отправкой (`#imgEditorOverlay`), открывается из `attach.js`:
- `open(file, onResult)` — `onResult(newFile|null)`, результат — `File` `image/jpeg` q=0.92
- 3 canvas-буфера в натуральных координатах: `baseCanvas` (фото после поворота/отражения), `drawCanvas` (штрихи), `#ieCanvas` (композит для показа)
- Инструменты: свободная обрезка, поворот ±90°, отражение H/V, кисть (цвет+размер), пикселизация области, ластик (`destination-out`)
- Поворот/отражение запекаются (`rebakeBase` + `applyDeltaToDraw`); экспорт через crop→композит→`toBlob`
- Pointer Events (mouse+touch), `MAX_DIM=4096`, EXIF через `createImageBitmap`. Полное описание — [[Backend/Web#Редактор изображений (imageeditor.js)]]

### `settings.js` → `BF.settings`
Многоэкранная панель настроек профиля (sliding view-stack):

| View | Функция |
|------|---------|
| `main` | Меню: профиль, аккаунт, безопасность, устройства, кнопка выхода |
| `profile` | Загрузка/смена фото профиля (upload → FilesApi) |
| `name` | Изменение имени, фамилии, юзернейма (debounce-проверка занятости) |
| `bio` | Редактирование биографии |
| `password` | Смена пароля (старый + новый + повтор) |
| `twofa` | Управление 2FA: TOTP Authenticator (QR-код) + Email OTP |
| `sessions` | Список активных сессий; завершение чужих сессий |

Зависит от: `BF.api`, `BF.files`, `BF.tokens`, `BF.realtime`, `BF.device`, `BF.utils`.

### `calls.js` / `calls-ui.js` → `BF.calls`
Звонки через LiveKit (vendor `wwwroot/js/vendor/livekit-client.bundle.js`): установка соединения, UI звонка.

### `newchat.js` → `BF.newchat`
Создание новых чатов (поиск пользователя, старт диалога/группы).

### `privatechat.js` → `BF.privateChat`
Приватные E2E-чаты: инвайт/пароль/расшифровка. Argon2id (t=3, m=64MiB, p=4) через vendor `hash-wasm.umd.min.js`, ключи в localStorage.

### `sound.js` → `BF.sound`
Звуки уведомлений.

### `main.js`
Bootstrap мессенджера: инициализирует все BF-модули (включая `BF.imageEditor.init()`), загружает список чатов, запускает real-time подписки.

---

## Proto bundle

### `wwwroot/js/proto/barkfluff.bundle.js`
Скомпилированный JS-bundle всех protobuf-определений для gRPC-Web.
Генерируется скриптами из `scripts/`.

---

## Scripts

### `scripts/generate-proto.ps1` / `generate-proto.sh`
Генерация proto-bundle: вызывает `protoc` + `protoc-gen-grpc-web`, затем `esbuild` для bundling.

### `scripts/proto-bundle-index.js`
Точка входа для esbuild — импортирует все сгенерированные proto JS-файлы.

### `scripts/package.json` / `package-lock.json`
Node.js зависимости для генерации proto-bundle (esbuild и др.).

### `scripts/.gitignore`
Исключает `node_modules` и генерируемые артефакты.

---

## Docker / nginx

### `Dockerfile.slim`
Сборка образа сервиса для CI и production.

### `Dockerfile.slim`
Облегчённый образ (меньший base image / без dev-зависимостей).

### `nginx/web.conf`
nginx-конфиг для production: проксирование к сервису, SSL termination, кэширование статики.

### `nginx/README.md`
Инструкция по настройке nginx для BarkFluff.Web.

---

## Зависимости (.csproj)

| Пакет | Назначение |
|-------|-----------|
| `Yarp.ReverseProxy` (2.3.0) | Проксирование запросов к бэкенд-сервисам |
| `AWSSDK.Core` (4.0.7.4) | Транзитивная зависимость AWS SDK |
| [[Backend/GrpcServer]] | `LoadConfiguration`, `AddBarkFluffSerilog`, `AddBarkFluffMetrics` |

---

*Последнее обновление: исследование кодовой базы — актуально для текущего состояния ветки `dev`.*
