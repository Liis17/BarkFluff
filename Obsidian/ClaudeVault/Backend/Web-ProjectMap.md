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
| `files-http-upload` | `POST /api/files/upload/{uploadId}` | files-http |

**`BuildClusters()`** — YARP кластеры:

| ClusterId | Конфиг-ключ | Дефолт | Протокол | ActivityTimeout |
|-----------|-------------|--------|----------|-----------------|
| identity | `IdentityService:Host` | http://identity:7000 | HTTP/2 | 100 с |
| users | `UsersService:Host` | http://users:7001 | HTTP/2 | 100 с |
| messages | `MessagesService:Host` | http://messages:7007 | HTTP/2 | 100 с |
| files | `FilesService:Host` | http://files:7005 | HTTP/2 | 100 с |
| updates | `UpdatesService:Host` | http://updates:7015 | HTTP/2 | **24 ч** (streaming) |
| onliner | `OnlinerService:Host` | http://onliner:7009 | HTTP/2 | **24 ч** (streaming) |
| files-http | `FilesService:HttpHost` | http://files:7006 | HTTP/1.1 | 100 с |

**`GrpcWebResponseStream`** (вложенный класс) — потоковый враппер `Stream`:
- Кодирует данные в base64 на лету (режим `grpc-web-text`)
- Буферизует неполные триплеты между вызовами `WriteAsync`
- `FlushFinalAsync()` — записывает остаток с padding перед trailer-frame
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
Страница входа. Подключает: `device.js`, `tokens.js`, `metadata.js`, `auth.js`, `login-page.js`, proto-bundle.

### `wwwroot/messenger.html`
Главный мессенджер. Подключает все JS-модули из `js/app/`.

### `wwwroot/mobile.html`
Мобильная версия страницы (отдельный HTML-шаблон для мобильных браузеров).

### `wwwroot/favicon.ico`
Иконка приложения.

---

## Frontend — JS модули (`wwwroot/js/app/`)

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
- Редирект в `/messenger` после успешного входа

### `clients.js` → `BF.clients`
gRPC-Web клиенты для всех сервисов (IdentityApi, UsersApi, MessagesApi, FilesApi, UpdatesApi, OnlinerApi).
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

### `main.js`
Bootstrap мессенджера: инициализирует все BF-модули, загружает список чатов, запускает real-time подписки.

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

### `Dockerfile`
Стандартная сборка образа сервиса (multi-stage: build → runtime).

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
| `Grpc.AspNetCore.Web` | Подключено, но gRPC-Web конвертация реализована вручную в middleware |
| `Yarp.ReverseProxy` | Проксирование запросов к бэкенд-сервисам |
| [[Backend/GrpcServer]] | `LoadConfiguration`, `AddBarkFluffSerilog`, `AddBarkFluffMetrics` |

---

*Последнее обновление: исследование кодовой базы — актуально для текущего состояния ветки `dev`.*
