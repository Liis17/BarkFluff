# Аудит: Barkfluff.AdminPanel
> Дата: 2026-06-12. Область: код сервиса (C#), HTML/JS (Pages/ и Pages/Redesigned/ и Pages/v2/), Dockerfile/Dockerfile.slim, nginx (admin-panel.conf, 01-ssl-params.conf), docker-compose-dev.yml / docker-compose-master.yml.

## Сводка

AdminPanel — самая чувствительная поверхность проекта: процесс имеет доступ к `docker.sock` и работает под `root`, то есть фактически обладает правами root на хосте. Аутентификация построена на approve/reject через Telegram-бота и сессионных токенах в LiteDB. Архитектура middleware закрывает все `/api/*` маршруты проверкой токена (кроме публичных `auth/request` и `auth/status`), поэтому endpoint'ов «без авторизации» по сути нет — каждый маршрут защищён и middleware'ом, и явной проверкой `context.Items["AuthToken"]`. Command injection в локальный docker CLI отсутствует (используется `ArgumentList`, без shell), а удалённый SSH-слой экранирует аргументы и валидирует имена по allowlist'у. Однако обнаружены критичные проблемы: захардкоженный (закоммиченный) токен Telegram-бота — основного канала авторизации; запуск контейнера под root при смонтированном `docker.sock`; выдача в браузер plaintext-секретов (SSH root-пароли удалённых серверов, S3 secret keys); сессионная cookie без `HttpOnly/Secure/SameSite`; отсутствие rate-limit и заголовков безопасности.

| Критичность | Безопасность | Производительность | Docker/nginx | Итого |
|-------------|--------------|--------------------|--------------|-------|
| Critical    | 2            | 0                  | 0            | 2     |
| High        | 3            | 0                  | 1            | 4     |
| Medium      | 4            | 4                  | 2            | 10    |
| Low         | 3            | 1                  | 2            | 6     |
| **Всего**   | **12**       | **5**              | **5**        | **22**|

---

## Безопасность

### S1. Захардкоженный токен Telegram-бота в репозитории — Critical
**Файл:** `Backend/Barkfluff.AdminPanel/appsettings.json:10` (и `appsettings.Development.json:9`)
**Проблема:** В обоих файлах закоммичен боевой токен бота: `"BotToken": "8539569051:AAHMs6TwTKOpYqcA8XkWTB7p6w8CO1RWQwQ"`, а также реальный admin id (`"Admins": "495716470:admin_nick"`).
**Почему это проблема:** Telegram-бот — единственный канал подтверждения входа в панель. Кто угодно с доступом к репозиторию (или к истории git) получает полный контроль над ботом: может перехватывать/подделывать сообщения, рассылать от его имени, в ряде сценариев манипулировать процессом одобрения входа. Даже несмотря на то, что в проде значение переопределяется через `Telegram__BotToken` (см. compose), сам токен уже скомпрометирован самим фактом коммита и остаётся валидным до отзыва.
**Рекомендация:** Немедленно отозвать токен через @BotFather и выпустить новый. Убрать секреты из `appsettings*.json` (оставить пустые строки/placeholder), хранить только в `.env`/секрет-менеджере. Почистить git-историю (BFG/`git filter-repo`). Тот же подход — для `Admins`.

### S2. Контейнер работает под root со смонтированным docker.sock (root на хосте) — Critical
**Файл:** `Backend/docker-compose-dev.yml:184` и `:228` (`user: root` + `- /var/run/docker.sock:/var/run/docker.sock`); аналогично `Backend/docker-compose-master.yml:146` и `:178`. Контр-пример: `Backend/Barkfluff.AdminPanel/Dockerfile:23` (`USER $APP_UID`).
**Проблема:** Compose принудительно запускает контейнер как `user: root` и монтирует `docker.sock`. Доступ к `docker.sock` эквивалентен root на хосте (можно запустить контейнер с `-v /:/host`). Панель при этом опубликована наружу через nginx (`panel.barkfluff.com`).
**Почему это проблема:** Любая уязвимость (обход аутентификации, RCE, SSRF, передача чужого имени контейнера) превращается в полную компрометацию хоста. Dockerfile аккуратно создаёт непривилегированного пользователя `app`, добавляет его в группу `docker` и сбрасывает привилегии (`USER $APP_UID`) — но `user: root` в compose это полностью нивелирует, и hardening Dockerfile становится «мёртвым».
**Рекомендация:** Убрать `user: root`, оставить privilege-drop из Dockerfile. Рассмотреть docker-socket-proxy (tecnativa/docker-socket-proxy) с whitelisting только нужных API (containers list/start/stop/restart) вместо прямого монтирования `docker.sock`. Минимизировать поверхность: не давать `image prune`, `compose up` напрямую.

### S3. Endpoint отдаёт plaintext SSH root-пароли удалённых серверов в браузер; SSH без проверки host key — High
**Файл:** `Backend/Barkfluff.AdminPanel/Services/RemoteDockerService.cs:284-288` (`GetServerInfo` возвращает `config.Password`), endpoint `Backend/Barkfluff.AdminPanel/Endpoints/RemoteDockerEndpoints.cs:28-40` (`GET /api/remote/{server}/config`); подключение — `RemoteDockerService.cs:199-227`.
**Проблема:** `GetServerInfo` кладёт в DTO пароль (`new RemoteServerInfoDto(Host, Port, Username, Password)`), и endpoint возвращает его клиенту как есть. Это root-пароли (`Username = "root"`) серверов navigator/msk. Отдельно: `RunSshCommandAsync` создаёт `SshClient` без подписки на `HostKeyReceived`, то есть host key не проверяется (MITM на SSH-канал).
**Почему это проблема:** Любой авторизованный администратор (а при XSS — и сторона, укравшая токен) получает по HTTP root-пароли от внешних серверов; они оседают в истории браузера, кэше, логах, devtools. Отсутствие проверки host key позволяет перехватить SSH-сессию и пароль.
**Рекомендация:** Не передавать пароль в браузер — `RemoteServerInfoDto` не должен содержать пароль (для UI достаточно host/port/username). Перейти на SSH-ключи вместо паролей. Включить проверку host key (`HostKeyReceived` со сверкой фингерпринта).

### S4. S3 secret keys возвращаются в браузер открытым текстом — High
**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/ConfigurationEndpoints.cs:46` (`secretKey = g.FirstOrDefault(c => c.Key == "SecretKey")?.Value ?? ""`), endpoint `GET /api/configuration/s3-configuration` (строки 21-57).
**Почему это проблема:** Секретные ключи всех S3-бакетов (прод — HostKey S3) отдаются клиенту в открытом виде и отображаются/хранятся в UI. Это раскрытие учётных данных к объектному хранилищу всем, кто имеет сессию (и любому, кто украдёт токен через XSS). Те же ключи доступны через `S3BrowserService`, поэтому показывать их в API смысла нет.
**Рекомендация:** Не возвращать `secretKey` в GET-ответе (маскировать: `"********"` или признак «задан/не задан»). При обновлении (`POST /s3/update`) принимать новый ключ, но никогда не отдавать существующий обратно.

### S5. Cookie сессии без HttpOnly/Secure/SameSite (устанавливается из JS) — High
**Файл:** `Backend/Barkfluff.AdminPanel/Pages/Login.html:625,648` (`document.cookie = ... '; path=/'`, `setCookie('auth_token', data.token, 7)`); то же в `Pages/v2/Login.html:316,336` и `Pages/Redesigned/screen-login.js:192`. Сервер cookie только удаляет (`Middleware/TokenAuthMiddleware.cs:89`, `Endpoints/AuthEndpoints.cs:79`), но не выставляет с флагами.
**Проблема:** Токен (`data.token`) приходит из `GET /api/auth/status` и записывается клиентским JS в `document.cookie` без атрибутов `HttpOnly`, `Secure`, `SameSite`.
**Почему это проблема:** Раз cookie ставится из JS, она по определению не `HttpOnly` — любой XSS (которых на дашборде много потенциальных точек, см. S12) крадёт сессию. Нет `Secure` — cookie может уйти по HTTP. Нет явного `SameSite` — защита от CSRF держится только на дефолте браузера (`Lax`), что хрупко (см. S6).
**Рекомендация:** Выдавать токен и ставить cookie на сервере (`context.Response.Cookies.Append("auth_token", token, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = ... })`), а не возвращать его в теле `auth/status`. Клиентский JS не должен иметь доступ к токену.

### S6. Нет CSRF-защиты на мутирующих endpoint'ах; местами `DisableAntiforgery()` — Medium
**Файл:** мутирующие маршруты `Endpoints/DockerEndpoints.cs` (start/stop/restart/pull/update-all, строки 46-159), `Endpoints/UsersEndpoints.cs:329,430` (`.DisableAntiforgery()`), `Endpoints/MailEndpoints.cs:227` (`.DisableAntiforgery()`), `Endpoints/NotificationsEndpoints.cs` (broadcast).
**Проблема:** Аутентификация — по cookie, которая отправляется браузером автоматически. Анти-CSRF токенов нет, кастомный заголовок не требуется, часть endpoint'ов явно отключает antiforgery.
**Почему это проблема:** При cookie-аутентификации без `SameSite`/CSRF-токена защита держится исключительно на браузерном дефолте `SameSite=Lax`. Это «защита по умолчанию», а не «по дизайну»: достаточно где-то выставить `SameSite=None`, использовать старый клиент или GET-мутацию — и CSRF на «перезапусти все контейнеры»/«разошли push всем» становится реальным.
**Рекомендация:** Выставлять cookie с `SameSite=Strict` (см. S5) и/или требовать кастомный заголовок (`X-Requested-With`) либо antiforgery-токен на всех мутирующих запросах. Не отключать antiforgery без компенсации.

### S7. Нет rate-limit и защиты от перебора/флуда на публичных auth-endpoint'ах; enumeration админов — Medium
**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/AuthEndpoints.cs:16-56` (`POST /api/auth/request`, `GET /api/auth/status/{requestId}`), логика — `Services/AuthService.cs:51-90`.
**Проблема:** `/api/auth/request` публичен (middleware пропускает его без токена, `TokenAuthMiddleware.cs:36-41`). На каждый запрос с известным username-админа бот шлёт админу сообщение и делает `SendChatAction` (`AuthService.CreateAuthRequestAsync` → `CheckBotReachabilityAsync`). Rate-limit отсутствует. Ответы различают `unknown_user` («Пользователь не найден») и успешную постановку — это enumeration валидных админ-username'ов.
**Почему это проблема:** Атакующий может флудить админа Telegram-сообщениями (annoyance/DoS, риск «усталости от подтверждений» → ошибочный approve), а также перебором установить валидные admin-username'ы. `/api/auth/status` тоже без ограничения частоты.
**Рекомендация:** Добавить rate-limiting (`AddRateLimiter`, фиксированное окно по IP) на `auth/request` и `auth/status`. Единый ответ без раскрытия существования пользователя. Ограничить число активных pending-запросов на админа.

### S8. Нет allowlist/валидации имени контейнера — управление любым контейнером хоста; риск argument-injection — Medium
**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/DockerEndpoints.cs:31-103` (`{name}` напрямую из маршрута), `Services/DockerService.cs:84-166,171-208` (`StartContainerAsync`/`Stop`/`Restart`/`Pull...` → `RunDockerCommandAsync("start", containerName)`), маппинг — `DockerService.cs:213-238` (`GetValueOrDefault(containerName, containerName)` — неизвестное имя проходит как есть).
**Проблема:** `name` не валидируется и не сверяется с allowlist'ом сервисов BarkFluff. Command injection нет (используется `ProcessStartInfo.ArgumentList`, без shell — это правильно), но: (1) можно управлять ЛЮБЫМ контейнером на хосте, не только сервисами BarkFluff; (2) имя, начинающееся с `-`, передаётся docker CLI как отдельный аргумент-флаг (нет разделителя `--`), что в зависимости от подкоманды может изменить поведение.
**Почему это проблема:** Авторизованный пользователь (или укравший токен) может остановить/пересоздать произвольные контейнеры хоста, включая инфраструктурные. В связке с root+docker.sock (S2) это расширяет радиус поражения.
**Рекомендация:** Проверять `name` по белому списку известных сервисов (он уже есть — `BarkFluffServicesOrder`/`ServiceToContainerMap`) до вызова docker. Передавать `--` перед позиционным именем контейнера в docker-командах.

### S9. Сообщение об одобрении входа не содержит IP запрашивающего — «слепой» approve — Low
**Файл:** `Backend/Barkfluff.AdminPanel/Services/TelegramBotService.cs:224-248` (`SendAuthRequestToAdmin` — в тексте только nickname/browser/os/time, без IP).
**Почему это проблема:** Хотя IP вычисляется и сохраняется (`AuthEndpoints.GetIpAddress`, `TokenService.CreateToken`), админ при подтверждении не видит источник запроса. «Администратор: {nickname}» — это собственный username админа, поэтому атакующий, инициировавший вход, показывает админу его же имя; админ может одобрить, приняв чужой вход за свой.
**Рекомендация:** Добавить в сообщение IP-адрес и (опц.) геолокацию запрашивающего, чтобы решение принималось осознанно.

### S10. Сессионный токен — Guid.NewGuid() (не гарантирует криптостойкость) — Low
**Файл:** `Backend/Barkfluff.AdminPanel/Models/AuthToken.cs:33` (`Id = Guid.NewGuid()`), используется как значение cookie `auth_token` и проверяется в `Services/TokenService.cs:50-63`.
**Почему это проблема:** `Guid.NewGuid()` (UUID v4) даёт ~122 бита случайности, но контрактно не является криптографически стойким ГСЧ (документация .NET этого не гарантирует). Для значения сессии лучше использовать явный CSPRNG. Практический риск перебора низкий, но для security-токена это отклонение от best practice.
**Рекомендация:** Генерировать токен через `RandomNumberGenerator.GetBytes(32)` → Base64Url, хранить отдельно от `Id`-записи. Минимум — задокументировать осознанный выбор.

### S11. Безусловное доверие X-Forwarded-For / X-Real-IP — Low
**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/AuthEndpoints.cs:204-217` (`GetIpAddress`).
**Почему это проблема:** Сервер слушает `0.0.0.0:51888` (`Program.cs:27`) внутри docker-сети. Если контейнер достижим в обход nginx, эти заголовки подделываются, и в логи/одобрение попадает фальшивый IP. Влияние ограничено (IP — для отображения/аудита), но снижает доверие к данным.
**Рекомендация:** Использовать `ForwardedHeadersMiddleware` с `KnownProxies`/`KnownNetworks` (только адрес nginx), не парсить заголовки вручную.

### S12. (Информационно/Low) XSS-поверхность дашборда и рендер писем
**Файлы:** рендер тела письма — `Pages/mail.html:375-376` и `Pages/v2/mail.html:390-391` (`<iframe sandbox="allow-same-origin allow-popups allow-popups-to-escape-sandbox" srcdoc="${escapeHtml(d.htmlBody)}">`); рендер user-контента — `Pages/users.html`/`Pages/v2/users.html` (через `escapeHtml`), логи — `Pages/logs.html:646-714` (через `escapeHtml`).
**Оценка:** Это в основном сделано корректно. HTML письма (контент, контролируемый внешним отправителем на support@/help@ и т.п.) рендерится в sandbox-iframe БЕЗ `allow-scripts`, а `srcdoc` экранируется — то есть JS из письма не исполняется, XSS практически закрыт. Пользовательские поля (firstName/bio/username) и сообщения логов экранируются `escapeHtml`. Остаточные мелочи: (а) `Pages/users.html:353` — `<img src="${ub.badge.imageUrl}">` подставляется без экранирования (URL формируется сервером/Files, риск низкий, но это атрибутная инъекция при «грязном» URL); (б) комбинация `allow-same-origin` + `allow-popups-to-escape-sandbox` в mail-iframe — минимальный риск редиректов/popup'ов.
**Рекомендация:** Экранировать `imageUrl` и валидировать схему URL (`http(s):`). Для mail-iframe убрать `allow-same-origin` (при отсутствии скриптов он не нужен) и пересмотреть `allow-popups-to-escape-sandbox`.

---

## Производительность

### P1. docker CLI вызывается на запрос без таймаута/отмены — Medium
**Файл:** `Backend/Barkfluff.AdminPanel/Services/DockerService.cs:280` (`await process.WaitForExitAsync();` без timeout и без `CancellationToken`); вызывается из `GetContainersAsync` (`:45-58`) на каждый `GET /api/docker/containers` и из `Endpoints/SeqEndpoints.cs:546` (`/api/seq/services/status`).
**Почему это проблема:** Если docker-демон подвис, `WaitForExitAsync` ждёт бесконечно, держа запрос/поток/процесс. При поллинге дашборда это копится и может исчерпать ресурсы. Нет `CreateNoWindow`-таймаута, нет привязки к `RequestAborted`.
**Рекомендация:** Передавать `CancellationToken` (из `HttpContext.RequestAborted`) и таймаут (`WaitForExitAsync(cts.Token)` с `CancelAfter`), при превышении — kill процесса.

### P2. Нет кэширования статусов контейнеров — `docker ps` на каждый поллинг каждого клиента — Medium
**Файл:** `Backend/Barkfluff.AdminPanel/Services/DockerService.cs:45-58` (`RunDockerCommandAsync("ps", "--all", ...)`), потребители — `Endpoints/DockerEndpoints.cs:17-28` и `Endpoints/SeqEndpoints.cs:505-588`.
**Почему это проблема:** Каждый запрос порождает отдельный процесс `docker ps`. Дашборд опрашивает статусы периодически; при нескольких открытых вкладках/админах это десятки fork'ов процессов в минуту, плюс парсинг JSON. `GetContainerStatusAsync` к тому же тянет ВЕСЬ список и фильтрует в памяти (`:63-79`).
**Рекомендация:** Кэшировать результат `docker ps` на 2-5 секунд (`IMemoryCache`/`Lazy` с TTL), общий для всех клиентов. Для статуса одного контейнера — фильтровать на стороне docker (`--filter name=`), а не тянуть всё.

### P3. Fallback дашборда грузит до 50 000 событий из Seq синхронно на запрос — Medium
**Файл:** `Backend/Barkfluff.AdminPanel/Endpoints/SeqEndpoints.cs:143` и `:213` (`GetAllEventsListAsync(null, fromDateUtc, 50000)`); пагинация — `Services/SeqService.cs:98-134` (страницы по 500 → до 100 последовательных HTTP-запросов).
**Почему это проблема:** При пустом кэше (после рестарта/нового интервала) endpoint'ы `dashboard/kpis` и `dashboard/traffic` тянут до 50k событий 100 последовательными round-trip'ами к Seq, собирают всё в `List<JsonElement>` и агрегируют в памяти на потоке запроса. Это медленно, нагружает память и Seq.
**Рекомендация:** Считать агрегаты на стороне Seq (`/api/sqlquery` — `count(*) group by`), как уже сделано в `CountEventsAsync`, вместо вытягивания «сырых» событий. Ограничить fallback меньшим окном и отдавать частичный результат.

### P4. RemoteDockerService открывает новое SSH-соединение на каждый контейнер на каждый запрос — Medium
**Файл:** `Backend/Barkfluff.AdminPanel/Services/RemoteDockerService.cs:56-110` (`GetContainersStatusAsync` в цикле по контейнерам вызывает `RunSshCommandAsync`), сам connect — `:199-227` (`await Task.Run(() => client.Connect())`, затем `Disconnect` в `finally`).
**Почему это проблема:** Для msk (3 контейнера) — 3 последовательных SSH connect+auth+disconnect на каждый опрос статусов, до 6 при fallback'е. SSH-handshake дорогой; соединение не переиспользуется; блокирующий `Connect()` оффлоадится в пул потоков. При поллинге это заметная задержка и нагрузка.
**Рекомендация:** Один SSH-вызов на сервер: получать статусы всех сервисов одной командой (`docker ps` с фильтром по нескольким labels / `... --format`). Кэшировать соединение или результат на несколько секунд.

### P5. Fire-and-forget `Task.Run`/`Task.Delay` на каждое изменение статуса заявки — Low
**Файл:** `Backend/Barkfluff.AdminPanel/Services/PendingAuthService.cs:76-80` (`_ = Task.Run(async () => { await Task.Delay(5min); _requests.TryRemove(...) })`).
**Почему это проблема:** На каждый approve/reject создаётся отдельная задача с 5-минутной задержкой. Объёмы малы (защита rate-limit'ом всё равно нужна по S7), но это неуправляемые fire-and-forget таски без обработки исключений; при флуде заявок их число растёт. Чистка и так выполняется таймером (`CleanupExpiredRequests`).
**Рекомендация:** Полагаться на единый периодический таймер очистки (он уже есть), убрав per-request `Task.Run`.

---

## Docker / nginx

### D1. docker.sock + user root (см. S2) — Critical → отражено в Безопасности
Дублирует S2; в инфраструктурном разрезе: `docker-compose-dev.yml:184,228`, `docker-compose-master.yml:146,178`. Главный системный риск сервиса.

### D2. Полное отсутствие заголовков безопасности на nginx — High
**Файл:** `Backend/nginx/admin-panel.conf:8-25` (в `server`-блоке нет `add_header`), `Backend/nginx/01-ssl-params.conf` (есть только `ssl_protocols TLSv1.2 TLSv1.3`, никаких security-заголовков).
**Проблема:** Для `panel.barkfluff.com` не выставлены `Strict-Transport-Security`, `Content-Security-Policy`, `X-Frame-Options`/`frame-ancestors`, `X-Content-Type-Options`, `Referrer-Policy`.
**Почему это проблема:** Нет HSTS — риск SSL-strip. Нет `X-Frame-Options`/CSP `frame-ancestors` — панель можно встроить в iframe (clickjacking; особенно опасно для UX-одобрений и кнопок «перезапустить всё»). Нет CSP — слабее защита от XSS (а cookie не `HttpOnly`, см. S5). Нет `X-Content-Type-Options: nosniff`.
**Рекомендация:** Добавить в `admin-panel.conf` (или общий include): `add_header Strict-Transport-Security "max-age=31536000" always;`, `add_header X-Frame-Options "DENY" always;`, `add_header X-Content-Type-Options "nosniff" always;`, `add_header Referrer-Policy "no-referrer" always;` и строгую CSP.

### D3. Все секреты проекта (`.env`) и `docker-compose.yml` смонтированы внутрь контейнера панели — Medium
**Файл:** `Backend/docker-compose-dev.yml:230-231` и `docker-compose-master.yml:180-181` (`- ./docker-compose.yml:/docker-compose.yml:ro`, `- ./.env:/.env:ro`).
**Почему это проблема:** `.env` содержит токены всех сервисов, пароли RabbitMQ/SSH/почты, S3-ключи. Любое чтение файла/RCE/path-traversal в панели (работающей под root) раскрывает ВСЕ секреты платформы сразу. Монтирование нужно лишь для self-update через helper-контейнер (`DockerService.UpdateAdminPanelAsync`), но цена — концентрация секретов.
**Рекомендация:** Не монтировать полный `.env` в долгоживущий контейнер. Передавать helper-контейнеру только то, что нужно для `compose pull/up admin-panel`, через отдельный минимальный env-файл; либо генерировать его на лету в момент обновления и удалять.

### D4. Hardening Dockerfile нивелируется `user: root` в compose — Low
**Файл:** `Backend/Barkfluff.AdminPanel/Dockerfile:14-23` и `Dockerfile.slim:2-9` (создают `app`, добавляют в группу `docker`, `USER $APP_UID`) против `docker-compose-*.yml` (`user: root`).
**Почему это проблема:** Образ спроектирован для запуска под непривилегированным пользователем с групповым доступом к docker, но runtime запускает root — несогласованность, и труд по privilege-drop пропадает. Также `addgroup docker`/`adduser app docker` в образе не используется.
**Рекомендация:** Привести в соответствие: убрать `user: root`, проверить, что GID группы `docker` в образе совпадает с GID сокета на хосте (или задавать через build-arg).

### D5. `AllowedHosts: "*"` и UseHttpsRedirection за TLS-терминирующим nginx — Low
**Файл:** `Backend/Barkfluff.AdminPanel/appsettings.json:8` (`"AllowedHosts": "*"`); `Backend/Barkfluff.AdminPanel/Program.cs:179` (`app.UseHttpsRedirection()`), при этом nginx ходит в контейнер по HTTP (`admin-panel.conf:16`, `proxy_pass http://...`).
**Почему это проблема:** `AllowedHosts: "*"` отключает host-filtering. `UseHttpsRedirection` внутри контейнера, куда трафик приходит по HTTP от nginx, в лучшем случае no-op, в худшем — лишние редиректы/путаница (особенно без корректного `X-Forwarded-Proto` handling, который не сконфигурирован). Эксплуатируемость низкая, но это запах конфигурации.
**Рекомендация:** Ограничить `AllowedHosts` доменом панели. Либо убрать `UseHttpsRedirection` (TLS и так на nginx), либо настроить `ForwardedHeaders` (`X-Forwarded-Proto`) корректно.

---

## Положительные наблюдения
- Command injection в локальном docker CLI отсутствует: `DockerService` использует `ProcessStartInfo.ArgumentList` (аргументы передаются ОС буквально, без shell). (`Services/DockerService.cs:244-340`)
- Удалённый SSH-слой экранирует пользовательский ввод (`EscapeShell`, одинарные кавычки) и валидирует имя сервиса по allowlist'у (`FindContainer`), поэтому SSH-command-injection не воспроизводится. (`Services/RemoteDockerService.cs:270-304`)
- Все `/api/*` маршруты закрыты и middleware'ом, и явной проверкой токена; публичны только `auth/request` и `auth/status` — это by design. (`Middleware/TokenAuthMiddleware.cs:26-52`)
- Seq-фильтры строятся с экранированием `'` → `''`, значения помещаются в строковые литералы — инъекция в Seq filter language не воспроизводится. (`Endpoints/SeqEndpoints.cs:80-98`, `:444`)
- HTML писем рендерится в sandbox-iframe без `allow-scripts` с экранированным `srcdoc` — исполнение скриптов из письма заблокировано.
- Раздача статики через `PhysicalFileProvider`/`UseStaticFiles` и `ServeHtmlFile` с захардкоженными именами файлов — path traversal не воспроизводится (имена страниц не берутся из пользовательского ввода).
