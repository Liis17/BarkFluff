# Аудит: BarkFluff.Web
> Дата: 2026-06-12. Область: код сервиса (Program.cs + wwwroot JS), Dockerfile/Dockerfile.slim, nginx, docker-compose.

## Сводка
BarkFluff.Web — веб-версия мессенджера (порт 7016): один `.cs`-файл (`Program.cs`), который вручную конвертирует gRPC-Web ↔ gRPC и проксирует через YARP в backend-сервисы, плюс vanilla-JS фронтенд в `wwwroot`. Архитектурно сделано аккуратно: CORS с явным whitelist origin'ов (не `AllowAnyOrigin`), базовые security-заголовки в middleware, корректное отключение буферизации для server-streaming, YARP-клиенты и gRPC-канал создаются один раз. Основные замечания — защита в глубину против XSS: токены (включая refresh) в `localStorage`, функция `escapeHtml` не экранирует кавычки, но используется в контексте HTML-атрибута, и полностью отсутствует CSP. Критичных дыр не обнаружено.

| Критичность | Кол-во |
| ----------- | ------ |
| Critical    | 0      |
| High        | 0      |
| Medium      | 3      |
| Low         | 5      |

## Безопасность

### S1. `escapeHtml` не экранирует кавычки, но применяется внутри HTML-атрибута — Medium
**Файл:** `Backend/BarkFluff.Web/wwwroot/js/app/utils.js:38-43`; точки использования: `wwwroot/js/app/main.js:152, 228, 1012`
**Проблема:** `escapeHtml` реализована как `div.textContent = str; return div.innerHTML;` — это экранирует `<`, `>`, `&`, но НЕ экранирует `"` и `'`. При этом результат подставляется в значение атрибута, обёрнутого в двойные кавычки, например:
`'<img src="' + u.escapeHtml(info.picture) + '" alt="">'` (main.js:228), аналогично `chat.picture` (main.js:152) и `user.profilePicturePreview` (main.js:1012).
Если URL картинки/аватара содержит `"`, происходит выход из атрибута: значение вида `" onerror="alert(1)` даёт `<img src="" onerror="alert(1)">` → XSS.
**Почему это проблема:** URL картинок профиля/чата приходят с сервера и в общем случае производны от пользовательских данных (профиль). Если пользователь сможет задать в качестве picture строку с кавычкой, это исполнимый XSS. Даже если сейчас URL формирует сервер — это хрупкий паттерн (экранирование, недостаточное для атрибутного контекста).
**Рекомендация:** Либо устанавливать `img.src` через свойство DOM (`img.src = info.picture`) вместо склейки HTML, либо дополнить `escapeHtml` экранированием `"`/`'` (`&quot;`/`&#39;`) — но безопаснее присваивать атрибуты через DOM API (`setAttribute`/свойство), как уже делается в `messages.js`.

### S2. Access- и refresh-токены в localStorage/sessionStorage — Medium
**Файл:** `Backend/BarkFluff.Web/wwwroot/js/app/tokens.js:13-31`
**Проблема:** Токены (access + refresh) сохраняются в `localStorage` (или `sessionStorage` во временном режиме) в открытом виде. Refresh-токен — долгоживущий.
**Почему это проблема:** Любой XSS (см. S1, отсутствие CSP в S3) даёт полный доступ к токенам и, через refresh-токен, к захвату аккаунта. `localStorage` доступен любому JS на странице.
**Рекомендация:** Идеально — хранить refresh-токен в `HttpOnly Secure SameSite`-cookie (недоступной JS), access-токен держать в памяти. Если cookie невозможен из-за gRPC-Web (auth через заголовок), то минимизировать риск строгой CSP (S3) и коротким временем жизни refresh-токена. Как минимум — задокументировать осознанный компромисс.

### S3. Отсутствует Content-Security-Policy — Medium
**Файл:** `Backend/BarkFluff.Web/Program.cs:113-125`; `Backend/BarkFluff.Web/wwwroot/index.html`, `messenger.html` (нет `<meta http-equiv="Content-Security-Policy">`)
**Проблема:** Middleware выставляет `X-Content-Type-Options: nosniff`, `Referrer-Policy: same-origin`, `X-Frame-Options: DENY`, но не выставляет `Content-Security-Policy`. В HTML-файлах CSP тоже нет.
**Почему это проблема:** CSP — главный механизм защиты в глубину против XSS (S1) и эксфильтрации токенов (S2). Без него любой инжект скрипта исполняется и может слать данные куда угодно. Дополнительно `index.html` подключает внешние ресурсы с `fonts.googleapis.com`/`fonts.gstatic.com`, что нужно учесть в политике.
**Рекомендация:** Добавить `Content-Security-Policy` в middleware: ограничить `default-src 'self'`, `connect-src 'self'`, `img-src 'self' data: https:` (под нужды аватаров), `style-src`/`font-src` для Google Fonts, `script-src 'self'`. Проверить inline-стили в HTML (есть `<style>`-блоки — потребуется `'unsafe-inline'` для стилей или их вынос).

### S4. CORS `AllowCredentials` с origin другого поддомена — Low
**Файл:** `Backend/BarkFluff.Web/Program.cs:39-58`, `Backend/BarkFluff.Web/appsettings.json:9-14`
**Проблема:** Политика CORS использует `WithOrigins(allowedOrigins).AllowCredentials()`, где `allowedOrigins` включает `https://barkfluff.com` — origin, отличный от домена приложения `web.barkfluff.com`. То есть страница на `barkfluff.com` может слать credentialed-запросы в Web.
**Почему это проблема:** Риск ограничен: приложение аутентифицируется заголовком `x-auth-token` (localStorage), а не cookie, поэтому `AllowCredentials` (касается cookie) почти не играет. Корректно, что НЕ используется `AllowAnyOrigin` (это и был главный антипаттерн из чек-листа — он здесь отсутствует). Тем не менее список origin'ов шире необходимого.
**Рекомендация:** Оставить в whitelist только реально нужные origin'ы (домен веб-клиента). Если кросс-доменные запросы с `barkfluff.com` не требуются — убрать его.

### S5. Клиентский заголовок `x-ip-address` задаётся самим клиентом — Low
**Файл:** `Backend/BarkFluff.Web/wwwroot/js/app/metadata.js:27`
**Проблема:** В метаданные gRPC кладётся `x-ip-address: '0.0.0.0'` — значение целиком контролируется клиентом и легко подменяется.
**Почему это проблема:** Если backend доверяет этому заголовку для геолокации/аудита, его можно подделать. Реальный IP должен браться из `X-Forwarded-For` (что в Web настроено корректно через `ForwardedHeaders`, Program.cs:60-72).
**Рекомендация:** Убедиться, что backend-сервисы игнорируют клиентский `x-ip-address` и используют IP соединения. (Замечание про клиента; исправление — на стороне backend.)

### Позитив
- CORS не использует `AllowAnyOrigin` — origin'ы заданы явно (Program.cs:34-40).
- `ForwardedHeaders` сконфигурированы с явными доверенными сетями (Program.cs:60-72) — подделать реальный IP через `X-Forwarded-For` нельзя.
- Базовые security-заголовки выставляются для всех ответов (Program.cs:114-125).
- Большинство вставок в `innerHTML` пользовательских данных (названия чатов, имена, превью, имена папок) проходят через `escapeHtml` в текстовом контексте — там экранирования достаточно. Текст сообщений рендерится через `textContent` (messages.js) — безопасно.
- Лимит размера gRPC-Web-тела 4 МБ (Program.cs:139, 153-157, 164-168) защищает от гигантских запросов.

## Производительность

### P1. Sync-over-async в `GrpcWebResponseStream.Write` — Low
**Файл:** `Backend/BarkFluff.Web/Program.cs:439-442`
**Проблема:** Синхронный `Write(...)` вызывает `WriteAsync(...).AsTask().GetAwaiter().GetResult()` — блокирующее ожидание асинхронной операции.
**Почему это проблема:** Блокировка потока пула при синхронной записи. На практике Kestrel/YARP пишут в поток через `WriteAsync`, поэтому путь срабатывает редко, отсюда Low. Но при синхронном вызове возможен deadlock/исчерпание пула под нагрузкой.
**Рекомендация:** Бросать `NotSupportedException` в синхронном `Write` (заставив всех использовать `WriteAsync`), либо оставить, но осознавать риск.

### P2. Полная буферизация grpc-web-text запроса в память — Low
**Файл:** `Backend/BarkFluff.Web/Program.cs:162-174`
**Проблема:** Тело grpc-web-text целиком копируется в `MemoryStream`, затем `ToArray()`, затем base64-декод в новый массив.
**Почему это проблема:** Двойная аллокация на запрос. Ограничено 4 МБ, для unary-вызовов приемлемо, поэтому Low. Client-streaming в gRPC-Web не используется, так что больших тел тут нет.
**Рекомендация:** Оставить (ограничение 4 МБ делает это безопасным); при желании — потоковый base64-декодер.

### Позитив
- Для server-streaming (updates/onliner/fast-auth) буферизация ответа отключена корректно: `X-Accel-Buffering: no`, `Cache-Control: no-cache`, `DisableBuffering()`, потоковая base64-кодировка по мере поступления фреймов (Program.cs:198-218, 449-513). Это именно то, что требует чек-лист про «буферизацию стримов» — сделано правильно.
- YARP-кластеры/маршруты строятся один раз через `LoadFromMemory` (Program.cs:77-78) — клиенты не создаются на запрос.
- Для streaming-кластеров `ActivityTimeout` поднят до 24 ч (Program.cs:364-366) — долгоживущие соединения не рвутся.
- Статика отдаётся через `UseStaticFiles` (Program.cs:253) — встроенный ETag/кэширование.

## Docker / nginx

### D1. Два расходящихся `web.conf`; развёрнутый — без security-заголовков и без CSP — Low
**Файл:** `Backend/nginx/web.conf` (монтируется в Docker — заголовков безопасности НЕТ) vs `Backend/BarkFluff.Web/nginx/web.conf:111-114` (есть `X-Content-Type-Options`, `X-Frame-Options: SAMEORIGIN`, `Referrer-Policy`)
**Проблема:** В репозитории два конфига для `web.barkfluff.com`. Тот, что реально используется в docker-compose-стеке (`Backend/nginx/web.conf`), не добавляет ни одного security-заголовка; эталонный standalone-конфиг (`Backend/BarkFluff.Web/nginx/web.conf`, upstream 127.0.0.1) их добавляет, но `X-Frame-Options: SAMEORIGIN` конфликтует с `DENY`, который выставляет приложение. CSP отсутствует в обоих.
**Почему это проблема:** Расхождение конфигов запутывает; в проде заголовки держатся только на middleware приложения (Program.cs), а nginx-слой их не дублирует/не усиливает. Нет CSP ни там, ни там (см. S3).
**Рекомендация:** Свести к одному источнику истины; добавить CSP. Решить, где задаются security-заголовки (приложение или nginx), и убрать конфликт по `X-Frame-Options`.

### D2. Загрузка protoc / protoc-gen-grpc-web без проверки контрольных сумм — Low
**Файл:** `Backend/BarkFluff.Web/Dockerfile:11-20`, `Backend/BarkFluff.Web/Dockerfile.slim:13-22`
**Проблема:** В stage `proto-build` бинарники `protoc` (v3.20.3) и `protoc-gen-grpc-web` (v1.5.0) скачиваются с GitHub по HTTPS без сверки SHA-256.
**Почему это проблема:** Supply-chain риск: при компрометации источника/MITM в сборку попадёт подменённый бинарь, генерирующий вредоносный JS-бандл, который уедет всем веб-клиентам.
**Рекомендация:** Закрепить и проверять контрольные суммы скачанных бинарников (`echo "<sha256>  file" | sha256sum -c`).

### Позитив
- gRPC-Web `location` (`^/barkfluff\.`) корректно отключает `proxy_buffering off` (`Backend/nginx/web.conf:60, 82`) и поднимает таймауты до 24 ч для стримов — согласовано с настройками приложения.
- Финальный образ под непривилегированным `$APP_UID`, есть healthcheck (Dockerfile:70-78). Alpine выбран осознанно (нужен `wget` для healthcheck).
- В `appsettings.json` секретов нет; хосты backend-сервисов берутся из окружения (docker-compose-dev.yml:147-153).
- Сервис `web` в docker-compose НЕ публикует порты наружу — доступен только через nginx (docker-compose-dev.yml:143-163).
