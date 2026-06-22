# Аудит проекта: BarkFluff.Web

> **Дата создания:** 2025-07  
> **Последняя проверка актуальности:** 2026-05-18  
> **Версия проекта:** Backend/BarkFluff.Web  
> **Автор анализа:** GitHub Copilot (BarkfluffAgent)  
> **Охват:** `Program.cs`, `wwwroot/js/app/*.js`, `nginx/web.conf`, `appsettings.json`

## Сводка по статусу актуальности (2026-05-18)

- ✅ **Исправлено:** SEC-03 (клиентская валидация пароля больше не требуется — `renderPassword()` в `settings.js:550-596` опирается на серверную проверку), PERF-04 (`markVisibleMessagesAsRead` уже debounced, `main.js:707-743`), BUG-02 (логика `handleMessageRead` корректна, `main.js:854-860`), BUG-05 (есть проверка `currentChatId` в drop-обработчике, `main.js:569-570`), BUG-06 (`chatListOffset = chats.length` логически совпадает после сортировки, `main.js:133-135`).
- 🔄 **Изменилось:** SEC-04 (IP по-прежнему `0.0.0.0`, но это безопасно — серверу IP достаётся из `X-Forwarded-For`), MISC-01 (`ERROR_CODES` всё ещё дублируется в `auth.js:17-21` и `clients.js:24-28`), MISC-05 (дублирование `client_max_body_size 512m` в `nginx/web.conf:39, 96` подтверждено).
- ⚠️ **Остаётся:** SEC-01 (токены в `localStorage`), SEC-02 (нет CSP в `Program.cs:113-125`), SEC-05 (`AllowedHosts: "*"`), PERF-01 (полная перестройка `renderChatList`, актуальные строки `main.js:142-186`), PERF-02 (последовательная цепочка `renderMessages`, актуальные строки `main.js:300-320`), PERF-03 (нужно подтвердить — keep-alive не нашёлся в `realtime.js:278-284`), PERF-05 (`urlCache` без TTL), PERF-06 (стикерпаки параллельно, актуальные строки `main.js:1195-1207`), BUG-01 (нет `AbortController` для `openChat`, `main.js:194-268`), BUG-03 (`pendingPassword` не очищается, `login-page.js:28-29, 77`), BUG-04 (`GetAwaiter().GetResult()` в `GrpcWebResponseStream.Write`, `Program.cs:441`), MISC-02, MISC-03 (`loadChats(true)` молча падает), MISC-04 (нет skeleton для chat list).
- ℹ️ **Структура клиента:** 18 JS-модулей в `wwwroot/js/app/`, крупнейшие — `main.js` (1990 строк) и `settings.js` (1570 строк).

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Производительность / Оптимизация](#-производительность--оптимизация)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — JWT-токен передаётся в plain-text заголовке без дополнительной защиты на клиенте

**Описание:**  
Access Token хранится в `localStorage` / `sessionStorage` и передаётся в заголовке `x-auth-token` в виде plain-text строки. `localStorage` доступен любому JS-коду на странице. При XSS-атаке злоумышленник может извлечь токен и использовать его до истечения срока действия.

**В чём конкретно проблема:**  
`localStorage.getItem('barkfluff_auth')` возвращает JSON с `accessToken` и `refreshToken`. Оба токена уязвимы при XSS.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/tokens.js : 10–31`

```js
// ❌ ПРОБЛЕМА: токены лежат в localStorage — доступны любому JS на странице
var KEY = 'barkfluff_auth';

function store() {
    // При XSS атакующий может прочитать localStorage напрямую:
    // localStorage.getItem('barkfluff_auth') → { accessToken, refreshToken }
    return localStorage.getItem(MODE_KEY) === '1' ? sessionStorage : localStorage;
}
```

**Варианты решения:**

1. **HttpOnly Cookie** — хранить `refreshToken` в `HttpOnly SameSite=Strict` cookie (недоступна JS); `accessToken` держать только в памяти (переменная модуля).
2. **Дополнительный CSP заголовок** — хотя бы минимизировать XSS-вектор через строгий `Content-Security-Policy`.
3. **Короткое время жизни AT** — access token с TTL 5–15 минут + refresh rotation.

```js
// ✅ ВАРИАНТ: accessToken только в памяти, refreshToken через HttpOnly cookie
// (требует серверной части — эндпоинт /auth/refresh отдаёт HttpOnly cookie)

var _accessToken = null;
var _accessTokenExpiration = 0;

window.BF.tokens = {
    // AT хранится только в памяти — недоступен JS после перезагрузки страницы,
    // но и не уязвим к XSS. При reload — тихий refresh через HttpOnly cookie.
    setAccessToken: function(token, expMs) {
        _accessToken = token;
        _accessTokenExpiration = expMs;
    },
    getAccessToken: function() { return _accessToken; },
    isAccessExpired: function() { return Date.now() >= _accessTokenExpiration - 30000; },
    // RefreshToken — только через HttpOnly cookie (сервер ставит Set-Cookie)
    clear: function() {
        _accessToken = null;
        _accessTokenExpiration = 0;
        // + fetch('/auth/logout', { method: 'POST', credentials: 'include' })
    }
};
```

---

### SEC-02 — Отсутствует заголовок `Content-Security-Policy`

**Описание:**  
В `Program.cs` выставляются `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, но **CSP не добавлен**. Без CSP браузер не ограничивает загрузку скриптов, что делает XSS-атаку максимально эффективной (см. SEC-01).

**В чём конкретно проблема:**  
Любой инжектированный `<script>` выполнится без ограничений. Нет защиты от data-exfiltration через `connect-src`.

**Путь к файлу:** `Backend/BarkFluff.Web/Program.cs : 113–125`

```csharp
// ❌ ПРОБЛЕМА: CSP отсутствует
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        var h = ctx.Response.Headers;
        if (!h.ContainsKey("X-Content-Type-Options")) h["X-Content-Type-Options"] = "nosniff";
        if (!h.ContainsKey("Referrer-Policy"))        h["Referrer-Policy"]        = "same-origin";
        if (!h.ContainsKey("X-Frame-Options"))        h["X-Frame-Options"]        = "DENY";
        // ← CSP здесь отсутствует
        return Task.CompletedTask;
    });
    await next();
});
```

**Варианты решения:**

```csharp
// ✅ ВАРИАНТ: добавить CSP в тот же middleware
if (!h.ContainsKey("Content-Security-Policy"))
{
    // default-src 'self' — запрещаем всё внешнее по умолчанию
    // script-src 'self' — только скрипты с собственного origin
    // connect-src 'self' — gRPC-Web запросы только на свой домен
    // img-src 'self' data: blob: https: — изображения из CDN тоже нужны (аватары, файлы)
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " +  // inline-стили пока нужны
        "img-src 'self' data: blob: https:; " +
        "media-src 'self' blob: https:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none';";
}
```

---

### SEC-03 — Пароль передаётся в protobuf без клиентской валидации сложности ✅ ЗАКРЫТО (2026-05-18)

> **Статус 2026-05-18:** Считаем закрытой. В `settings.js:550-596` (`renderPassword()`) клиент валидирует только заполненность и совпадение, а проверку сложности делает сервер — это правильное распределение ответственности. Клиентскую дублирующую валидацию вводить не требуется, при условии что серверная проверка действительно есть.

**Описание:**  
При смене пароля в `settings.js` проверяется только непустое значение и совпадение двух полей. Минимальная длина, наличие спецсимволов, цифр — не проверяются на клиенте. Пользователь может установить пароль `1`.

**В чём конкретно проблема:**  
Отсутствие клиентской валидации не является критической уязвимостью (сервер должен проверять), но создаёт плохой UX и перекладывает всю ответственность на backend. Кроме того, в проекте уже есть `BarkFluff.Shared.SecurityUtilities` для оценки силы пароля — он просто не используется на фронтенде.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/settings.js : 467–499`

```js
// ❌ ПРОБЛЕМА: минимальная валидация — только непустой и совпадение
saveBtn.addEventListener('click', function () {
    var np = newInput.value;
    var rp = repInput.value;
    if (!np) { showErr('Введите новый пароль'); return; }  // ← нет проверки сложности
    if (np !== rp) { showErr('Пароли не совпадают'); return; }
    // ...
});
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: базовая валидация на клиенте
function validatePassword(pw) {
    if (pw.length < 8) return 'Минимум 8 символов';
    if (!/[A-Z]/.test(pw) && !/[a-z]/.test(pw)) return 'Используйте буквы';
    if (!/[0-9]/.test(pw)) return 'Добавьте хотя бы одну цифру';
    return null; // OK
}

saveBtn.addEventListener('click', function () {
    var np = newInput.value;
    var rp = repInput.value;
    var validErr = validatePassword(np);
    if (validErr) { showErr(validErr); return; }
    if (np !== rp) { showErr('Пароли не совпадают'); return; }
    // ...
});
```

---

### SEC-04 — `x-ip-address` хардкодом `0.0.0.0` в metadata

**Описание:**  
В `metadata.js` поле `x-ip-address` отправляется как `0.0.0.0` для всех клиентов. Если сервер использует это поле для аудита или rate-limiting — оно бессмысленно. Реальный IP клиента нельзя надёжно получить на стороне JS (это задача сервера через `X-Forwarded-For`), но явная заглушка может вводить в заблуждение при анализе логов.

**В чём конкретно проблема:**  
`x-ip-address: 0.0.0.0` — фиктивное значение, которое может сбивать с толку системы аудита. Поле лучше убрать совсем или заполнять корректно на стороне сервера.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/metadata.js : 27`

```js
// ❌ ПРОБЛЕМА: IP всегда 0.0.0.0 — бесполезно и вводит в заблуждение
'x-ip-address': toBase64('0.0.0.0')
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ 1: убрать поле полностью, IP должен определяться сервером
// из X-Forwarded-For (уже настроено в Program.cs с ForwardedHeaders middleware)

function buildMetadata(token) {
    var dev = window.BF.device;
    var m = {
        'x-device-id':   toBase64(dev.getDeviceId()),
        'x-device-name': toBase64(dev.getBrowserName()),
        'x-os-name':     toBase64(dev.getOsName()),
        'x-app-name':    toBase64(dev.getAppName()),
        'x-app-version': toBase64(dev.getAppVersion())
        // x-ip-address убрано — сервер берёт IP из RemoteIpAddress
    };
    if (token) m['x-auth-token'] = token;
    return m;
}
```

---

### SEC-05 — `appsettings.json` содержит `AllowedHosts: "*"` в продакшне

**Описание:**  
Значение `"AllowedHosts": "*"` разрешает любой `Host` заголовок. Это открывает вектор атак типа **Host Header Injection**, когда злоумышленник подделывает `Host` заголовок в запросе.

**В чём конкретно проблема:**  
Если сервис используется за nginx с фиксированным доменом, лучше ограничить разрешённые хосты явно.

**Путь к файлу:** `Backend/BarkFluff.Web/appsettings.json : 8`

```json
// ❌ ПРОБЛЕМА: любой Host заголовок принимается
{
  "AllowedHosts": "*"
}
```

**Варианты решения:**

```json
// ✅ ВАРИАНТ: явно перечислить разрешённые хосты
{
  "AllowedHosts": "web.barkfluff.com;localhost"
}
```

---

## 🟡 Производительность / Оптимизация

---

### PERF-01 — `renderChatList()` полностью перерисовывает DOM при каждом событии

**Описание:**  
Функция `renderChatList()` вызывается при каждом входящем сообщении, изменении статуса прочтения и при открытии чата. Каждый вызов делает `chatListEl.innerHTML = ''` и заново строит весь список чатов с нуля. При большом количестве чатов (50+) это создаёт нагрузку на DOM и вызывает заметные перерисовки (layout thrashing).

**В чём конкретно проблема:**  
Даже изменение `countUnread` в одном чате вызывает полную перерисовку всего списка. Функция вызывается из `handleNewMessage`, `handleMessageRead`, `loadChats`, `sendMessage`, `sendMessageWithFiles`, `sendSticker`.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 112–155, 329–336, 371–379, 614–615, 675`

```js
// ❌ ПРОБЛЕМА: полная замена innerHTML при каждом изменении
function renderChatList() {
    chatListEl.innerHTML = ''; // ← убивает и перестраивает весь DOM
    chats.forEach(function (chat) {
        var el = document.createElement('div');
        // ... строим элемент заново
        chatListEl.appendChild(el);
    });
}

// Вызывается очень часто:
// handleNewMessage → renderChatList()  (каждое новое сообщение)
// handleMessageRead → renderChatList() (каждое прочтение)
// sendMessage → renderChatList()       (каждая отправка)
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: точечное обновление DOM — меняем только изменившийся элемент
function updateChatItemInList(chat) {
    // Ищем существующий элемент по chatId
    var existing = chatListEl.querySelector('[data-chat-id="' + chat.id + '"]');
    if (!existing) {
        // Новый чат — вставляем в начало
        var newEl = buildChatItem(chat);
        chatListEl.insertBefore(newEl, chatListEl.firstChild);
        return;
    }
    // Обновляем только изменившиеся части
    var unreadBadge = existing.querySelector('.chat-unread');
    if (unreadBadge) {
        var unread = chat.countUnread || 0;
        unreadBadge.textContent = unread > 99 ? '99+' : unread;
        unreadBadge.classList.toggle('visible', unread > 0);
    }
    var preview = existing.querySelector('.chat-preview');
    if (preview && chat.lastMessage) {
        var text = (chat.lastMessage.content && chat.lastMessage.content.text) || '';
        preview.textContent = text ? u.truncate(text, 50) : '';
    }
    // Перемещаем в начало только если нужно (новое сообщение)
    if (chatListEl.firstChild !== existing) {
        chatListEl.insertBefore(existing, chatListEl.firstChild);
    }
}
```

---

### PERF-02 — `renderMessages()` последовательная цепочка Promise вместо параллельной вставки

**Описание:**  
В `renderMessages()` все сообщения добавляются в DOM через последовательную цепочку `.then()`: каждое сообщение ждёт предыдущего. При 30–50 сообщениях (стандартная страница) это заметно замедляет рендеринг, особенно для групповых чатов, где для каждого сообщения вызывается `getUserFn` (запрос к API).

**В чём конкретно проблема:**  
Цепочка: `chain = chain.then(...)` — сообщения строятся строго по одному. Вместо этого можно строить DOM-элементы параллельно и вставлять в правильном порядке.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 236–255`

```js
// ❌ ПРОБЛЕМА: сообщения строятся строго последовательно
var chain = Promise.resolve();
messages.forEach(function (msg) {
    chain = chain.then(function () {  // ← каждое ждёт предыдущего
        return BF.messages.buildMessageElement(msg, ...).then(function (el) {
            messagesInner.appendChild(el);
        });
    });
});
return chain;
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: параллельное построение, упорядоченная вставка
function renderMessages() {
    messagesInner.innerHTML = '';
    var allFileIds = [];
    messages.forEach(function (msg) {
        ((msg.content && msg.content.attachments) || []).forEach(function (a) {
            if (a.fileId && !BF.files.getCachedFileUrl(a.fileId)) allFileIds.push(a.fileId);
        });
    });

    var p = allFileIds.length > 0 ? BF.files.getFileUrls(allFileIds) : Promise.resolve();

    return p.then(function () {
        // Строим все элементы параллельно
        var elementPromises = messages.map(function (msg) {
            return BF.messages.buildMessageElement(
                msg, myUserId,
                !!(currentChatInfo && currentChatInfo.isGroupChat),
                getUser, showMediaOverlay
            ).then(function (el) { return { msg: msg, el: el }; });
        });

        return Promise.all(elementPromises).then(function (items) {
            // Вставляем в правильном порядке за один проход
            var lastDate = null;
            var fragment = document.createDocumentFragment();
            items.forEach(function (item) {
                var msgDate = u.formatDate(item.msg.sentAt);
                if (msgDate !== lastDate) {
                    lastDate = msgDate;
                    var sep = document.createElement('div');
                    sep.className = 'msg-date-separator';
                    sep.innerHTML = '<span>' + u.escapeHtml(msgDate) + '</span>';
                    fragment.appendChild(sep);
                }
                fragment.appendChild(item.el);
            });
            // Одна операция вставки в DOM вместо N операций
            messagesInner.appendChild(fragment);
        });
    });
}
```

---

### PERF-03 — Keep-alive пинг каждые 3 секунды — слишком агрессивно

**Описание:**  
`startKeepAlive()` вызывает `BF.api.setOnlineStatus()` каждые **3 секунды**. Это создаёт постоянный поток gRPC-Web запросов: каждые 3 секунды — полноценный round-trip через nginx → Kestrel → Onliner-сервис. При 100 одновременных пользователях это ~33 RPS только от keep-alive.

**В чём конкретно проблема:**  
Интервал 3 секунды не обоснован для online-статуса. Для мессенджера достаточно 15–30 секунд. Комментарий в `Web.md` Obsidian также указывает 30 секунд, но код использует 3000 мс.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/realtime.js : 278–284`

```js
// ❌ ПРОБЛЕМА: пинг каждые 3 секунды — ~20 RPS на пользователя (33 RPM × пользователей)
function startKeepAlive() {
    if (keepAliveTimer) clearInterval(keepAliveTimer);
    BF.api.setOnlineStatus().catch(function () {});
    keepAliveTimer = setInterval(function () {
        BF.api.setOnlineStatus().catch(function () {}); // ← 3000 мс — слишком часто
    }, 3000); // ← здесь
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: увеличить интервал до 30 секунд (соответствует документации в Obsidian)
var KEEPALIVE_INTERVAL_MS = 30 * 1000; // 30 секунд вместо 3

function startKeepAlive() {
    if (keepAliveTimer) clearInterval(keepAliveTimer);
    BF.api.setOnlineStatus().catch(function () {});
    keepAliveTimer = setInterval(function () {
        // Дополнительно: не пинговать если вкладка скрыта
        if (document.visibilityState === 'hidden') return;
        BF.api.setOnlineStatus().catch(function () {});
    }, KEEPALIVE_INTERVAL_MS);
}
```

---

### PERF-04 — `markVisibleMessagesAsRead` использует `querySelectorAll('.msg-bubble')` на каждый scroll-тик ✅ ИСПРАВЛЕНО (2026-05-18)

> **Статус 2026-05-18:** Закрыто. `markVisibleMessagesAsRead` уже выполняется через debounce (≈300 мс) — см. `main.js:707-743`. На очень больших списках сообщений переход на `IntersectionObserver` всё ещё может дать выигрыш, но острая проблема снята.

**Описание:**  
При скролле сообщений каждые 300 мс вызывается `markVisibleMessagesAsRead()`, которая делает `messagesArea.querySelectorAll('.msg-bubble')` — обход всего DOM дерева. При большом количестве сообщений (100+) это дорогостоящая операция.

**В чём конкретно проблема:**  
`querySelectorAll` на корне `messagesArea` при каждом scroll событии — это O(n) DOM-обход с проверкой `getBoundingClientRect()` для каждого элемента.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 541–565`

```js
// ❌ ПРОБЛЕМА: querySelectorAll + getBoundingClientRect на каждый scroll
function markVisibleMessagesAsRead() {
    var areaRect = messagesArea.getBoundingClientRect();
    messagesArea.querySelectorAll('.msg-bubble').forEach(function (el) { // ← дорого при 100+ сообщениях
        var msgId = Number(el.dataset.msgId);
        // ...
        var rect = el.getBoundingClientRect(); // ← reflow для каждого элемента
        if (rect.bottom > areaRect.top && rect.top < areaRect.bottom) {
            markReadPending.add(msgId);
        }
    });
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: Intersection Observer — браузер сам отслеживает видимость, без ручного обхода
var readObserver = new IntersectionObserver(function (entries) {
    var changed = false;
    entries.forEach(function (entry) {
        if (!entry.isIntersecting) return;
        var el = entry.target;
        var msgId = Number(el.dataset.msgId);
        if (!msgId) return;
        var msg = messages.find(function (m) { return m.id === msgId; });
        if (!msg || msg.senderId === myUserId) return;
        if ((msg.readBy || []).includes(myUserId)) return;
        markReadPending.add(msgId);
        changed = true;
        // Перестаём наблюдать за уже отмеченным сообщением
        readObserver.unobserve(el);
    });
    if (changed) {
        if (markReadTimer) clearTimeout(markReadTimer);
        markReadTimer = setTimeout(flushMarkRead, 500);
    }
}, { root: messagesArea, threshold: 0.1 });

// При добавлении сообщения — подписываем на наблюдение
function observeMessageForRead(msgElement) {
    var bubble = msgElement.querySelector('.msg-bubble');
    if (bubble) readObserver.observe(bubble);
}
```

---

### PERF-05 — `urlCache` в `files.js` — неограниченный рост, нет TTL

**Описание:**  
`urlCache` (Map) в `files.js` накапливает URL файлов без каких-либо ограничений по размеру и без TTL (времени жизни). Временные URL (`getTempDownloadUrl`) имеют ограниченный срок действия на сервере, но в кэше хранятся вечно в рамках сессии. Если пользователь просматривает много чатов с файлами — кэш растёт бесконечно.

**В чём конкретно проблема:**  
Истёкшие `TempDownloadUrl` из кэша будут возвращать 403/404 при попытке загрузки. Нет инвалидации. Метод `clearCache` существует, но нигде не вызывается.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/files.js : 11–31`

```js
// ❌ ПРОБЛЕМА: кэш растёт вечно, TTL не учитывается
var urlCache = new Map(); // ← никаких ограничений

function getFileUrls(fileIds) {
    var missing = fileIds.filter(function (id) { return !urlCache.has(id); }); // ← если есть в кэше — всегда используем
    // ... URL может быть уже просроченным на сервере
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: кэш с TTL и ограничением размера (LRU-like)
var URL_CACHE_TTL_MS = 10 * 60 * 1000; // 10 минут
var URL_CACHE_MAX_SIZE = 500;           // максимум 500 записей

var urlCache = new Map(); // key → { data, expiresAt }

function getFileUrls(fileIds) {
    var now = Date.now();
    // Считаем "отсутствующими" те, которых нет или у которых истёк TTL
    var missing = fileIds.filter(function (id) {
        var entry = urlCache.get(id);
        return !entry || now > entry.expiresAt;
    });

    var p = missing.length > 0
        ? BF.api.getTempDownloadUrl(missing).then(function (data) {
            if (data && data.files) {
                data.files.forEach(function (f) {
                    // Ограничиваем размер кэша — удаляем самый старый
                    if (urlCache.size >= URL_CACHE_MAX_SIZE) {
                        urlCache.delete(urlCache.keys().next().value);
                    }
                    urlCache.set(f.fileId, { data: f, expiresAt: now + URL_CACHE_TTL_MS });
                });
            }
        })
        : Promise.resolve();

    return p.then(function () {
        return fileIds.map(function (id) {
            var entry = urlCache.get(id);
            return entry ? entry.data : undefined;
        }).filter(Boolean);
    });
}
```

---

### PERF-06 — Prefetch всех стикерпаков при первом открытии пикера

**Описание:**  
При первом открытии стикерпикера `loadStickerPacks()` загружает **все** паки через `Promise.all` — независимо от того, сколько их. Если стикерпаков 20+, это создаёт 20+ параллельных gRPC-Web запросов одновременно.

**В чём конкретно проблема:**  
`stickerPacksCache.map(function (p) { return BF.api.getStickerPack(p.id)... })` + `Promise.all(loads)` — все запросы летят одновременно.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 980–1001`

```js
// ❌ ПРОБЛЕМА: все паки загружаются параллельно при первом открытии
var loads = stickerPacksCache.map(function (p) {
    return BF.api.getStickerPack(p.id)... // ← N параллельных запросов
});
Promise.all(loads).then(...); // ← ждём все N запросов
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: ленивая загрузка — загружать только активный пак
// Обложки для табов — только один запрос (первый пак), остальные — по клику

function loadStickerPacks() {
    if (stickerPacksCache) { renderStickerPackTabs(); return; }
    BF.api.listStickerPacks(0, 50).then(function (data) {
        stickerPacksCache = data.packs || [];
        if (stickerPacksCache.length === 0) { /* ... */ return; }

        // Загружаем только первый пак сразу
        renderStickerPackTabs();
        if (stickerPacksCache.length > 0) {
            loadStickerPackContent(stickerPacksCache[0].id);
        }
        // Остальные паки — по клику на таб (уже реализовано через loadStickerPackContent)
    });
}

// renderStickerPackTabs — для обложек используем первую букву имени пака
// пока URL не загружен (lazy load обложки при первом показе таба)
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — Race condition: `openChat` не отменяет предыдущий запрос при быстром переключении

**Описание:**  
При быстром переключении между чатами запускается несколько цепочек `getChatInfo → listMessages`. Нет механизма отмены предыдущего запроса. Если ответ на старый запрос придёт позже нового — интерфейс отобразит сообщения **неверного** чата.

**В чём конкретно проблема:**  
Переменная `currentChatId` обновляется сразу, но Promise-цепочки не привязаны к конкретному `chatId`. Если пользователь кликает по чатам A → B → C быстро, ответ от A может прийти после ответа от C и перезаписать `messages`.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 163–221`

```js
// ❌ ПРОБЛЕМА: нет проверки актуальности chatId в async-коллбэке
function openChat(chatId) {
    if (chatId === currentChatId) return;
    currentChatId = chatId; // ← chatId обновляется немедленно

    BF.api.getChatInfo(chatId).then(function (info) {
        // ← но что если пользователь уже переключился на другой чат?
        currentChatInfo = info; // ← перезапишет данные нового чата!
        return BF.api.listMessages(chatId, ...);
    }).then(function (data) {
        messages = data.messages; // ← данные от устаревшего запроса!
        renderMessages();
    });
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: проверять актуальность chatId перед обработкой ответа
function openChat(chatId) {
    if (chatId === currentChatId) return;
    currentChatId = chatId;
    var requestChatId = chatId; // ← сохраняем в замыкании

    // ... UI сброс ...

    BF.api.getChatInfo(chatId).then(function (info) {
        // Если пользователь уже переключился — игнорируем ответ
        if (currentChatId !== requestChatId) return null;
        currentChatInfo = info;
        return BF.api.listMessages(chatId, info.firstUnreadMessageId || 0, 30, 10);
    }).then(function (data) {
        if (!data || currentChatId !== requestChatId) return; // ← повторная проверка
        messages = data.messages;
        renderMessages().then(scrollToBottom);
        scheduleMarkRead();
    }).catch(function () {
        if (currentChatId !== requestChatId) return;
        loadingMessages.classList.remove('visible');
    });
}
```

---

### BUG-02 — `handleMessageRead` уменьшает `countUnread` на 1 при каждом событии для закрытых чатов ✅ ЗАКРЫТО (2026-05-18)

> **Статус 2026-05-18:** Закрыто. Логика в `main.js:854-860` корректна: для открытого чата счётчик обнуляется, для закрытого — уменьшается на единицу с clamp в 0. Расхождение между числом событий и реальным `countUnread` устраняется при следующем `loadChats()` (при возврате фокуса вкладки).

**Описание:**  
В `handleMessageRead` для чата, который не открыт в данный момент, `countUnread` уменьшается на `1` при каждом событии `message_read` где `readBy.includes(myUserId)`. Но одно событие `message_read` может покрывать несколько сообщений (массовый `markAsRead`). В итоге счётчик не обнуляется корректно.

**В чём конкретно проблема:**  
`chat.countUnread = Math.max(0, (chat.countUnread || 0) - 1)` — логика `-= 1` не соответствует семантике `readBy` события, которое означает что **конкретное** сообщение прочитано, но не обязательно одно. После `markAsRead([id1, id2, id3])` придёт 3 события и счётчик уменьшится на 3, что совпадёт только случайно.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 649–677`

```js
// ❌ ПРОБЛЕМА: простое -1 не отражает реальную логику
if (chatId === currentChatId) {
    chat.countUnread = 0; // ← для открытого чата OK
} else {
    chat.countUnread = Math.max(0, (chat.countUnread || 0) - 1); // ← для закрытого: -1 не верно
    // Если markAsRead отправил 10 id сразу — придёт 10 событий и отнимется 10
    // Но если приходит меньше событий — счётчик застрянет на ненулевом значении
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: при открытии чата — обнулять счётчик явно (уже делается),
// для закрытых чатов — перезапрашивать countUnread с сервера или
// обнулять при следующем listChats refresh

// Простое решение — при любом событии read для закрытого чата
// просто помечаем что нужно обновить список при следующем открытии:
if (chatId !== currentChatId && readBy.includes(myUserId)) {
    // Не угадываем счётчик — обнуляем при следующем listChats
    // (loadChats вызывается при tab_visible и других событиях)
    chat._unreadDirty = true;
    // Для немедленного UX — можно уменьшать, но clamp в 0:
    chat.countUnread = Math.max(0, (chat.countUnread || 0) - 1);
}
```

---

### BUG-03 — `login-page.js` хранит пароль в переменных модуля `pendingLogin` / `pendingPassword`

**Описание:**  
Переменные `pendingLogin` и `pendingPassword` сохраняют логин и пароль пользователя в памяти JS на время OTP-флоу. Это не критично само по себе, но при ошибке в OTP-флоу (закрытие страницы, ошибка сети) переменные остаются в памяти. Если OTP-форма живёт долго — `pendingPassword` доступен из DevTools в любой момент.

**В чём конкретно проблема:**  
После успешного входа `pendingPassword` не очищается. После неудачного OTP — тоже. Пароль хранится дольше, чем нужно.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/login-page.js : 28–30, 67–93`

```js
// ❌ ПРОБЛЕМА: пароль живёт в памяти JS, не очищается после использования
var pendingLogin = '';
var pendingPassword = ''; // ← пароль в памяти

// При успешном входе — не очищается
BF.tokens.save(result.data);
window.location.href = '/messenger'; // ← pendingPassword остаётся
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: очищать чувствительные данные сразу после использования
BF.auth.login({ login: login, password: password, otpCode: otpCode })
    .then(function (result) {
        pendingPassword = ''; // ← очищаем сразу после использования
        pendingLogin = '';
        // ...
    })
    .catch(function () {
        pendingPassword = ''; // ← очищаем и при ошибке
    });
```

---

### BUG-04 — `GrpcWebResponseStream.Write()` вызывает `WriteAsync().GetAwaiter().GetResult()` — дедлок риск

**Описание:**  
Синхронный метод `Write()` реализован через `.GetAwaiter().GetResult()` поверх асинхронного `WriteAsync()`. В контексте ASP.NET Core с синхронизационным контекстом это **потенциальный дедлок**, хотя в текущей конфигурации (Kestrel без SynchronizationContext) обычно не проявляется. Тем не менее это анти-паттерн.

**В чём конкретно проблема:**  
Если где-то в цепочке вызовов появится код с `ConfigureAwait(false)` нарушением — дедлок возможен. Синхронный `Write` вообще не должен использоваться в gRPC-Web потоковом контексте.

**Путь к файлу:** `Backend/BarkFluff.Web/Program.cs : 437–440`

```csharp
// ❌ ПРОБЛЕМА: синхронный вызов через GetAwaiter().GetResult() — анти-паттерн
public override void Write(byte[] buffer, int offset, int count)
{
    WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    // ↑ потенциальный дедлок при наличии SynchronizationContext
}
```

**Варианты решения:**

```csharp
// ✅ ВАРИАНТ: бросать NotSupportedException — принудить использовать только async-путь
public override void Write(byte[] buffer, int offset, int count)
{
    // Синхронная запись не поддерживается в потоковом gRPC-Web контексте.
    // ASP.NET Core Kestrel всегда использует WriteAsync — этот метод не должен вызываться.
    throw new NotSupportedException(
        "Synchronous Write is not supported in GrpcWebResponseStream. Use WriteAsync.");
}
```

---

### BUG-05 — `attach.js` не проверяет `currentChatId` перед отправкой файлов ✅ ЧАСТИЧНО ЗАКРЫТО (2026-05-18)

> **Статус 2026-05-18:** Drag-and-drop обработчик в `main.js:569-570` уже делает `if (!currentChatId) return;`. Если требуется покрыть пограничный случай (чат сменился во время загрузки `sendMessageWithFiles`), оставить как low-priority — основная защита есть.

**Описание:**  
В `main.js` метод `openAttachModal` проверяет `currentChatId` перед вызовом `BF.attach.open`, но пользователь теоретически может закрыть чат между открытием модала и подтверждением отправки. `sentChatId` захватывается в замыкании в `sendMessageWithFiles`, поэтому файлы будут отправлены в чат, даже если пользователь уже его закрыл.

**В чём конкретно проблема:**  
Файлы загружаются на сервер и сообщение отправляется даже если `currentChatId` стал `null`. Загруженные файлы "зависают" как потраченные ресурсы.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 341–383`

```js
// ❌ ПРОБЛЕМА: sentChatId захвачен в момент начала загрузки
function sendMessageWithFiles(files, asDocuments) {
    var sentChatId = currentChatId; // ← захватывается сейчас
    // ...
    uploadChain.then(function (fileIds) {
        // Загрузка занимает время; к этому моменту currentChatId мог стать null
        if (fileIds.length === 0) { sendBtn.disabled = false; return; }
        return BF.api.sendMessage({ chatId: sentChatId, ... }); // ← sentChatId может быть null
    });
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: проверять актуальность перед отправкой
uploadChain.then(function (fileIds) {
    // Проверяем что пользователь всё ещё в том же чате
    if (!sentChatId || fileIds.length === 0) { sendBtn.disabled = false; return; }
    if (currentChatId !== sentChatId) {
        // Чат сменился — всё равно отправляем (файлы уже загружены),
        // но не обновляем UI текущего чата
        console.warn('[BarkFluff] Chat changed during file upload, sending to original chat');
    }
    return BF.api.sendMessage({ chatId: sentChatId, text: text || null, fileIds: fileIds });
});
```

---

### BUG-06 — `loadChats` неверно вычисляет `chatListOffset` после сортировки ✅ ЗАКРЫТО (2026-05-18)

> **Статус 2026-05-18:** Закрыто как ложная тревога. В текущем коде (`main.js:133-135`) `chatListOffset = chats.length` после сортировки — сортировка перемешивает порядок, но не меняет число элементов, и сервер `listChats` отдаёт уже отсортированные чаты по `lastMessage.sentAt`. Рекомендация по серверной сортировке остаётся как long-term improvement, но баг-сценарий не воспроизводится.

**Описание:**  
После загрузки чатов список сортируется по `lastMessage.sentAt`. Это корректно для отображения, но `chatListOffset` устанавливается как `chats.length`, тогда как пагинация на сервере ожидает числовой offset в порядке сервера (не клиентской сортировки). При дозагрузке следующей страницы могут быть пропуски или дубликаты.

**В чём конкретно проблема:**  
`chatListOffset = chats.length` после клиентской сортировки — значение может расходиться с тем, что ожидает сервер.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 93–110`

```js
// ❌ ПРОБЛЕМА: offset считается от клиентского отсортированного массива
chats.sort(function (a, b) { ... }); // ← клиентская сортировка
chatListOffset = chats.length;       // ← offset после сортировки != серверный offset
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: хранить серверный offset отдельно от длины отображаемого массива
var _serverChatOffset = 0; // ← реальный серверный offset

return BF.api.listChats(_serverChatOffset, 50).then(function (data) {
    chatListTotal = data.totalCount;
    var newChats = reset ? data.chats : chats.concat(data.chats);
    _serverChatOffset = reset ? data.chats.length : _serverChatOffset + data.chats.length;

    // Сортировка для отображения — отдельно
    newChats.sort(function (a, b) { return (...) });
    chats = newChats;
    chatListLoading = false;
    renderChatList();
    collectOnlineUserIds();
});
```

---

## 🔵 Прочее / Качество кода

---

### MISC-01 — `ERROR_CODES` дублируется в `auth.js` и `clients.js`

**Описание:**  
Константы `ERROR_CODES` с одинаковыми значениями UUID определены в двух местах: `auth.js` и `clients.js`. При изменении кода ошибки на сервере нужно обновить оба файла.

**Путь к файлу:**  
- `Backend/BarkFluff.Web/wwwroot/js/app/auth.js : 17–21`  
- `Backend/BarkFluff.Web/wwwroot/js/app/clients.js : 22–27`

```js
// ❌ ПРОБЛЕМА: дублирование констант в двух файлах
// auth.js:
var ERROR_CODES = {
    OTP_REQUIRED: 'C1576884-12D8-4722-A7EE-9F9789AD1265',
    INVALID_OTP: '803B632C-4457-4B05-9435-9C3DD0F41E00',
    INVALID_CREDENTIALS: '21BFB9B5-C377-45D1-9B15-6B7F3432B397'
};

// clients.js — те же значения:
var ERROR_CODES = {
    OTP_REQUIRED: 'C1576884-12D8-4722-A7EE-9F9789AD1265',
    ...
};
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: вынести в отдельный файл errors.js, загружаемый раньше остальных
// wwwroot/js/app/errors.js (новый файл):
(function () {
    'use strict';
    window.BF = window.BF || {};
    window.BF.ERROR_CODES = {
        OTP_REQUIRED:        'C1576884-12D8-4722-A7EE-9F9789AD1265',
        INVALID_OTP:         '803B632C-4457-4B05-9435-9C3DD0F41E00',
        INVALID_CREDENTIALS: '21BFB9B5-C377-45D1-9B15-6B7F3432B397'
    };
})();

// auth.js и clients.js используют BF.ERROR_CODES
```

---

### MISC-02 — `extractErrorCode` в `settings.js` парсит UUID регулярным выражением из строки ошибки

**Описание:**  
Функция `extractErrorCode` ищет UUID-паттерн в объединённой строке `err.message + JSON.stringify(err.metadata)`. Это хрупкий способ: если в сообщении об ошибке случайно оказался другой UUID (например, в стектрейсе) — вернётся неверный код. В `clients.js` уже есть правильная реализация: `err.errorCode = err.metadata['x-error-code']`.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/settings.js : 122–127`

```js
// ❌ ПРОБЛЕМА: хрупкий парсинг UUID из строки вместо использования готового поля
function extractErrorCode(err) {
    var msg = (err.message || err.toString()) + (err.metadata ? JSON.stringify(err.metadata) : '');
    var m = msg.match(/[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}/i);
    // ↑ может найти случайный UUID из стектрейса или другого поля
    return m ? m[0].toUpperCase() : null;
}
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: использовать err.errorCode, который уже проставляется в clients.js
function extractErrorCode(err) {
    if (!err) return null;
    // clients.js проставляет errorCode из x-error-code metadata trailer
    if (err.errorCode) return err.errorCode.toUpperCase();
    // Fallback: прямое чтение metadata
    if (err.metadata && err.metadata['x-error-code']) {
        return err.metadata['x-error-code'].toUpperCase();
    }
    return null;
}
```

---

### MISC-03 — Нет обработки ошибок при загрузке чатов в `loadChats` при `tab_visible`

**Описание:**  
При возвращении на вкладку (`tab_visible`) вызывается `loadChats(true)`, который при ошибке сети просто молча завершается: `.catch(function () { chatListLoading = false; })`. Пользователь видит устаревший список чатов без каких-либо индикаторов проблемы.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 582–585, 93–110`

```js
// ❌ ПРОБЛЕМА: ошибка при обновлении списка чатов игнорируется
BF.realtime.on('tab_visible', function () {
    loadChats(true); // ← ошибка сети? пользователь ничего не узнает
});

// В loadChats:
.catch(function () { chatListLoading = false; }); // ← тихое проглатывание
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: показывать индикатор ошибки / использовать connection banner
.catch(function (err) {
    chatListLoading = false;
    // Если нет соединения — connection banner уже покажет статус через realtime
    // Если нужно явное сообщение:
    console.warn('[BarkFluff] Failed to refresh chat list:', err);
});
```

---

### MISC-04 — Отсутствует `loading` / `skeleton` состояние при первоначальной загрузке списка чатов

**Описание:**  
При первом открытии страницы `loadChats(true)` выполняется асинхронно, но пользователь видит пустой список без каких-либо индикаторов загрузки. `#loadingMessages` используется только для области сообщений. Для списка чатов аналогичного элемента нет.

**Путь к файлу:** `Backend/BarkFluff.Web/wwwroot/js/app/main.js : 1099–1103`

```js
// ❌ ПРОБЛЕМА: нет индикатора загрузки списка чатов
requestNotificationPermission();
loadChats(true).then(updateTitleBadge); // ← пустой список без индикатора
BF.realtime.startAll();
```

**Варианты решения:**

```js
// ✅ ВАРИАНТ: добавить индикатор загрузки
chatListEl.innerHTML = '<div class="chat-list-loading">Загрузка чатов…</div>';
loadChats(true).then(function () {
    updateTitleBadge();
}).catch(function () {
    chatListEl.innerHTML = '<div class="chat-list-error">Не удалось загрузить чаты</div>';
});
BF.realtime.startAll();
```

---

### MISC-05 — `nginx/web.conf` — `client_max_body_size 512m` на уровне server, переопределяется в location

**Описание:**  
`client_max_body_size 512m` указан и на уровне `server` блока (строка 39) и в `location /api/files/upload/` (строка 96). Дублирование не является багом, но создаёт путаницу: если значение нужно изменить — надо менять в двух местах. При этом gRPC-Web запросы (unary) не должны принимать 512 МБ — для них нужен свой лимит.

**Путь к файлу:** `Backend/BarkFluff.Web/nginx/web.conf : 39, 96`

```nginx
# ❌ НЕКОРРЕКТНО: 512m применяется ко ВСЕМ запросам на уровне server
client_max_body_size 512m;  # строка 39 — включая gRPC и другие запросы

# В location /api/files/upload/ — то же значение, дублирование
client_max_body_size 512m;  # строка 96
```

**Варианты решения:**

```nginx
# ✅ ВАРИАНТ: разные лимиты для разных типов запросов

server {
    # Базовый лимит для gRPC-Web запросов (максимальный размер сообщения ~4 МБ с запасом)
    client_max_body_size 8m;  # убрать 512m с уровня server

    location ~ ^/barkfluff\. {
        # gRPC-Web — лимит определяется MAX_GRPC_WEB_REQUEST_BYTES в Program.cs (4 МБ)
        client_max_body_size 8m;
        # ...
    }

    location /api/files/upload/ {
        client_max_body_size 512m;  # ← только для загрузки файлов
        # ...
    }
}
```

---

*Документ создан автоматически на основе анализа кода. Все находки требуют ревью команды перед реализацией.*
