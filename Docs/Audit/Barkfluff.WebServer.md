# Аудит: Barkfluff.WebServer
> Дата: 2026-06-12. Дополнение: 2026-07-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка
Barkfluff.WebServer — REST/HTTP-сервис (порт 64641): отдаёт лендинг, юридические страницы, страницы профилей пользователей, файлы установщиков и проксирует профиль пользователя из Users-сервиса по gRPC. Главная проблема — **reflected XSS** на странице профиля: имя пользователя из URL вставляется в HTML простым `String.Replace` без какого-либо экранирования, причём страница отдаётся вообще без security-заголовков и CSP. Раздача файлов реализована безопасно (whitelist имён, фиксированные пути — path traversal нет, directory listing отсутствует). По производительности — чтение файлов целиком в память и отсутствие кэширования/ETag.

| Критичность | Кол-во |
| ----------- | ------ |
| Critical    | 1      |
| High        | 0      |
| Medium      | 3      |
| Low         | 5      |

## Безопасность

### S1. Reflected XSS через имя пользователя в URL — Critical
**Файл:** `Backend/Barkfluff.WebServer/Controllers/FallbackController.cs:19-53` → `Backend/Barkfluff.WebServer/Services/UserPageService.cs:26` (и `:15`), шаблон `Backend/Barkfluff.WebServer/html/userpage.html:6, 326, 365, 381`
**Проблема:** Catch-all маршрут `[HttpGet("/{**catchAll}")]` передаёт URL-декодированный путь в `UserPageService.ProcessUserPage(path)`, который делает `File.ReadAllText(htmlPath).Replace("%%username%%", path)`. Значение `%%username%%` подставляется в HTML без экранирования сразу в нескольких местах: в `<title>` (строка 6), в `.pc-username` (строка 326), в текст 404-блока (строка 365) и — критично — в JavaScript-строку `const USERNAME = "%%username%%";` (строка 381). Запрос вида `GET /%22%3B%3C/script%3E%3Cscript%3Ealert(document.cookie)%3C/script%3E` (или просто `/<img src=x onerror=alert(1)>`) приводит к выполнению произвольного JS в браузере жертвы.
**Почему это проблема:** Классический reflected XSS на основном домене. Позволяет красть токены/cookie, выполнять действия от имени жертвы, фишинг. Усиливается отсутствием CSP (S2).
**Рекомендация:** HTML-экранировать `path` перед подстановкой (`System.Net.WebUtility.HtmlEncode`), а для JS-контекста (строка 381) — отдельно JS-экранировать или передавать значение через `data-`атрибут/`textContent`. Дополнительно валидировать username по строгому шаблону (`^[a-zA-Z0-9_]{3,32}$`, как принято в проекте) и отдавать 404 при несоответствии.

### S2. Отсутствие security-заголовков и CSP на отдаваемых HTML-страницах — Medium
**Файл:** `Backend/Barkfluff.WebServer/Program.cs:56-65` (нет middleware заголовков), `docker/nginx/barkfluff.single-server.conf:87-100` (location `/` на gateway без `add_header`)
**Проблема:** Сервис отдаёт HTML (`HomeController`, `FallbackController` — профили, legal, selfhosted) без заголовков `Content-Security-Policy`, `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`. nginx-апстрим этого сервиса (`barkfluff_gateway`, порт 64641) в `barkfluff.single-server.conf` тоже не добавляет заголовки безопасности.
**Почему это проблема:** Нет CSP — нет защиты в глубину против XSS из S1; нет `X-Frame-Options` — возможен clickjacking; нет `nosniff`.
**Рекомендация:** Добавить middleware с базовыми заголовками (минимум `Content-Security-Policy`, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`) — как сделано в `BarkFluff.Web/Program.cs`. Либо выставлять их в nginx для gateway.

### S3. In-memory хранилище чатов поддержки без TTL и лимитов — Low
**Файл:** `Backend/Barkfluff.WebServer/Services/SupportChatService.cs:7, 10-22`, `Backend/Barkfluff.WebServer/Controllers/SupportChatController.cs:20-44`
**Проблема:** Сообщения чата поддержки складываются в `ConcurrentDictionary<string, ChatSession>` по ключу — GUID, который генерирует клиент. Эндпоинт `POST /api/support/send` и `GET /api/support/messages/{chatId}` неаутентифицированы (by design — это форма поддержки для анонимов). Записи никогда не удаляются и не имеют срока жизни.
**Почему это проблема:** (1) Неограниченный рост памяти — любой может создавать новые чаты (есть лимит длины сообщения 4000 и формат GUID, но нет лимита частоты/количества). (2) Чтение чужого чата возможно при знании chatId, но GUID практически неугадываем, поэтому риск раскрытия низкий.
**Рекомендация:** Добавить TTL/вытеснение старых сессий (например, `IMemoryCache` со скользящим окном) и rate-limit на создание сообщений.

### S4. Хардкод Telegram admin ID — Low
**Файл:** `Backend/Barkfluff.WebServer/Services/TelegramService.cs:13`
**Проблема:** `private readonly long _adminId = 495716470;` зашит в код.
**Почему это проблема:** Не секрет (это Telegram chat id), но конфигурационное значение в коде; при смене администратора требуется пересборка.
**Рекомендация:** Вынести в конфигурацию (`Telegram:AdminId`).

### Позитив (проверено — уязвимостей нет)
- **Path traversal отсутствует.** Несмотря на то, что задача указывала на конкатенацию путей из URL, все контроллеры раздачи файлов используют фиксированные имена либо whitelist: `AssetsController.cs:8-19` (словарь разрешённых файлов), `InstallController.cs:9-22` (4 фиксированных имени), `DownloadController.cs:13`, `FaviconController.cs:12`, `HomeController.cs:12` (константные пути). `LegalPageService.cs:18-25` сопоставляет имя страницы через `switch` (whitelist). `UserPageService` использует `path` только для подстановки в шаблон, а не для построения пути к файлу.
- **Directory listing отсутствует** — `UseStaticFiles`/`UseDirectoryBrowser` не подключены.
- **Service-токен к Users** передаётся через заголовок и берётся из конфигурации (`UserProfileService.cs:42-46`), не захардкожен.

## Производительность

### P1. Загрузка установщика целиком в память — Medium
**Файл:** `Backend/Barkfluff.WebServer/Controllers/DownloadController.cs:20-21`
**Проблема:** `var fileBytes = System.IO.File.ReadAllBytes(installerPath); return File(fileBytes, ...)` читает весь EXE-установщик в память на каждый запрос.
**Почему это проблема:** Установщик может весить десятки МБ; при нескольких параллельных скачиваниях — всплески потребления памяти и нагрузка на GC (LOH). Нет потоковой передачи, нет поддержки Range/докачки.
**Рекомендация:** Использовать `PhysicalFile(installerPath, "application/octet-stream", "Barkfluff.Updater.CLI.exe")` — он стримит файл (sendfile), поддерживает Range и ETag. В этом же сервисе `InstallController.cs:30` уже корректно использует `PhysicalFile`.

### P2. Чтение файлов и страниц с диска на каждый запрос без кэша/ETag — Low
**Файл:** `Backend/Barkfluff.WebServer/Controllers/HomeController.cs:19`, `FaviconController.cs:19`, `AssetsController.cs:27`, `Services/LegalPageService.cs:39, 51`, `Services/UserPageService.cs:16, 26`
**Проблема:** `File.ReadAllText`/`ReadAllBytes` выполняются при каждом запросе; ответы отдаются через `Content(...)`/`File(bytes,...)` без `ETag`/`Last-Modified`/`Cache-Control`, поэтому браузеры и CDN не кэшируют статику.
**Почему это проблема:** Лишний дисковый I/O и трафик на статичный контент (лендинг, favicon, картинки, legal-страницы).
**Рекомендация:** Для статичных файлов (favicon, assets, installer) — `PhysicalFile` (даёт ETag + sendfile) или `UseStaticFiles` с заголовками кэширования. Для HTML-шаблонов — кэшировать содержимое в памяти при старте.

### P3. Неограниченный рост коллекции сообщений поддержки — Low
**Файл:** `Backend/Barkfluff.WebServer/Services/SupportChatService.cs:7-8`
**Проблема:** `_chats` и `_telegramMessageMap` растут без очистки (см. также S3).
**Рекомендация:** TTL/вытеснение старых сессий.

## Docker / nginx

### D1. Нет security-заголовков на nginx-gateway для WebServer — Low
**Файл:** `docker/nginx/barkfluff.single-server.conf:87-100`
**Проблема:** WebServer (порт 64641) проксируется через `barkfluff_gateway`; в этом `location /` нет `add_header` для CSP/X-Frame-Options/nosniff (см. S2).
**Рекомендация:** Добавить заголовки на уровне nginx либо в приложении.

### D2. Host network открывает plaintext WebServer в обход nginx/TLS — Medium
**Файл:** `docker/msk/docker-compose-msk.yml:2,5`.
**Проблема:** Оба compose-файла используют `network_mode: host`, а Kestrel настроен как `ASPNETCORE_URLS=http://+:${WEBSERVER_PORT}`. Поэтому процесс слушает plaintext HTTP на всех интерфейсах хоста без явного Docker port-binding.
**Почему это проблема:** Если порт 64641 не закрыт внешним firewall, клиент может обратиться к Kestrel напрямую, обойти TLS и любые ограничения/заголовки nginx; трафик можно перехватить или подменить в сети. Общий Docker S3 перечисляет опубликованные gRPC-порты основного compose, но отдельный host-network compose WebServer там не покрыт.
**Рекомендация:** Убрать host network и подключить сервис к bridge-сети nginx. Если nginx работает на хосте и host network необходим, слушать только loopback (`http://127.0.0.1:${WEBSERVER_PORT}`).

### Позитив
Dockerfile (`Backend/Barkfluff.WebServer/Dockerfile`) корректен: multi-stage, финальный образ `aspnet:10.0-noble-chiseled`, запуск под `$APP_UID`, `--chown` на скопированные артефакты. Секретов в `appsettings.json` нет (Telegram BotToken и UsersService:Token пустые, заполняются из окружения).
