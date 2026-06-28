# Аудит: nginx-конфигурация
> Дата: 2026-06-12. Область: Backend/nginx/*.conf, BarkFluff.Web/nginx, Frontend/Developers/nginx.conf.

## Сводка

Проверены все 16 nginx-конфигов проекта и оба docker-compose (dev/master). Базовая TLS-гигиена в порядке: общий сниппет `01-ssl-params.conf` задаёт TLSv1.2/1.3, есть catch-all `00-default.conf` с `return 444`, gRPC-сервисы корректно проксируются через `grpc_pass`. Главные проблемы: **ни на одном auth-критичном хосте нет rate limiting**, admin-panel (с доступом к docker.sock и SSH-кредам) опубликована в интернет без сетевых ограничений, стриминговые сервисы (updates, onliner) обрываются через 300 секунд простоя, а для `web.barkfluff.com` и `developers.barkfluff.com` в репозитории лежат по два конкурирующих конфига с разным поведением. HSTS отсутствует везде, security-заголовков нет почти нигде.

**Статус конфигов (важно):** ни `docker-compose-dev.yml`, ни `docker-compose-master.yml` не содержат сервиса nginx. Единственный compose с nginx — `Backend/nginx/docker-compose-msk.yml` — монтирует один файл `./nginx/default.conf` (отсутствует в репозитории) и работает в `network_mode: host`, где Docker-DNS `127.0.0.11` и имена вида `identity:7000` из per-service конфигов **не работают**. То есть все 13 файлов `Backend/nginx/*.conf` деплоятся вручную, вне репозитория, и подтвердить по коду, какие из них боевые, невозможно. Аудит исходит из того, что per-service конфиги боевые (это подтверждает Obsidian-документация), но фактический рабочий конфиг на серверах может отличаться.

| Критичность | Кол-во |
|-------------|--------|
| Critical    | 0      |
| High        | 5      |
| Medium      | 7      |
| Low         | 8      |
| **Всего**   | **20** |

---

## Безопасность

### S1. Admin-panel доступна из интернета без сетевых ограничений — High
**Файл:** `Backend/nginx/admin-panel.conf:15-24`
**Проблема:** `location /` для `panel.barkfluff.com` проксирует на `admin-panel:51888` без каких-либо `allow`/`deny`, mTLS, basic auth или иных ограничений на уровне nginx. Защита — только аутентификация самого приложения.
**Почему это проблема:** Admin-panel — самый привилегированный компонент платформы: у контейнера смонтирован `/var/run/docker.sock` (= root на хосте), в env лежат SSH-креды боевых серверов, токены всех сервисов и почтовые пароли (`docker-compose-master.yml:142-181`). Любая уязвимость в аутентификации панели (а это самописный flow через Telegram-бота) означает полный захват инфраструктуры. Единственный слой защиты — плохая практика для такого актива.
**Рекомендация:** Добавить на уровне nginx второй фактор: IP-allowlist (`allow <admin-ip>; deny all;`), client-cert (mTLS) или хотя бы `auth_basic` поверх app-аутентификации. Идеально — убрать панель с публичного интернета за VPN/WireGuard.

### S2. Нет rate limiting на auth-критичных хостах — High
**Файл:** `Backend/nginx/identity.conf:15-24`, `Backend/nginx/fast-auth.conf:15-24`, `Backend/nginx/admin-panel.conf:15-24`
**Проблема:** Ни в одном конфиге проекта нет `limit_req`/`limit_conn` (проверено grep по всем файлам). Эндпоинты логина (`identity` — пароли и OTP-коды, `fast-auth`, форма входа панели) принимают неограниченный поток запросов.
**Почему это проблема:** Перебор паролей и особенно OTP-кодов (6 цифр = 10^6 вариантов) без троттлинга на edge — реалистичная атака. Даже если в приложении есть лимиты, нагрузка от брутфорса целиком доходит до бэкенда и БД (заодно DoS-вектор). nginx — правильное место для первой линии.
**Рекомендация:** Объявить зону `limit_req_zone $binary_remote_addr zone=auth:10m rate=10r/s;` (http-уровень, например в `01-ssl-params.conf` или отдельном сниппете) и добавить `limit_req zone=auth burst=20 nodelay;` в `location /` identity, fast-auth и admin-panel. Для gRPC это работает так же, как для HTTP.

### S3. Plaintext gRPC через публичную сеть и открытый порт 4425 — High
**Файл:** `Frontend/Developers/nginx.conf:33`, `Backend/docker-compose-dev.yml:169-170`
**Проблема:** Конфиг SPA-портала проксирует `grpc_pass grpc://api.barkfluff.com:4425` — нешифрованный gRPC на внешнее DNS-имя. Параллельно `docker-compose-dev.yml` публикует порт `4425:4425` сервиса `developers` наружу.
**Почему это проблема:** Если nginx с этим конфигом и сервис `developers` стоят на разных машинах, весь трафик DevelopersApi — включая заголовок `x-auth-token` (JWT) — идёт по интернету открытым текстом. Плюс порт 4425 доступен любому напрямую, в обход nginx (без TLS, без будущего rate limiting).
**Рекомендация:** Проксировать через `grpcs://developers.barkfluff.com:443` (как сделано для identity строкой выше) либо держать nginx и сервис на одном хосте/в одной Docker-сети и ходить по внутреннему имени. Публикацию `4425:4425` из compose убрать или ограничить (`127.0.0.1:4425:4425`).

### S4. HSTS отсутствует во всех конфигах — Medium
**Файл:** `Backend/nginx/01-ssl-params.conf:1-11` (общий сниппет — естественное место), а также все server-блоки 443
**Проблема:** Ни один конфиг не отдаёт `Strict-Transport-Security` (grep по `add_header`/`Strict-Transport` — ноль совпадений в `Backend/nginx`; в `Backend/BarkFluff.Web/nginx/web.conf:112-114` есть другие заголовки, но HSTS тоже нет).
**Почему это проблема:** HTTP→HTTPS редиректы (301) есть, но без HSTS первый запрос браузера уходит по HTTP и уязвим к SSL-stripping/MITM в недоверенных сетях. Для мессенджера с web-клиентом и admin-панелью это значимо.
**Рекомендация:** Добавить в HTML-отдающие хосты (web, panel, developers): `add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;`. С `includeSubDomains` осторожно — он покроет и gRPC-субдомены (для нативных gRPC-клиентов это безвредно).

### S5. Отсутствуют security-заголовки на HTML-отдающих хостах — Medium
**Файл:** `Backend/nginx/admin-panel.conf:8-25`, `Backend/nginx/web.conf:8-40`, `Backend/nginx/developers.conf:8-25`, `Frontend/Developers/nginx.conf:1-45`, `Backend/nginx/barkfluff.single-server.conf:52-101` (location `/web/`)
**Проблема:** Хосты, отдающие HTML (admin-panel, web-клиент, developers-портал), не выставляют `X-Frame-Options`, `X-Content-Type-Options`, `Content-Security-Policy`, `Referrer-Policy`. Исключение — `Backend/BarkFluff.Web/nginx/web.conf:112-114`, где есть nosniff/SAMEORIGIN/Referrer-Policy, но нет CSP.
**Почему это проблема:** Без X-Frame-Options админ-панель кликджекается; без nosniff возможен MIME-sniffing загруженного контента; без CSP любая XSS в web-клиенте (мессенджер рендерит пользовательский контент) получает полную свободу. Для admin-panel это усиливает S1.
**Рекомендация:** Минимум на admin-panel, web и developers: `X-Frame-Options "DENY"` (для панели) / `"SAMEORIGIN"`, `X-Content-Type-Options "nosniff"`, `Referrer-Policy "strict-origin-when-cross-origin"`, и подобрать CSP (хотя бы `frame-ancestors 'none'` для панели). Если заголовки ставит приложение — проверить и задокументировать; в конфигах их нет.

### S6. Upstream-TLS без верификации сертификата — Medium
**Файл:** `Frontend/Developers/nginx.conf:21-23`
**Проблема:** `grpc_pass grpcs://identity.barkfluff.com:443` без `grpc_ssl_verify on` и `grpc_ssl_trusted_certificate`. По умолчанию в nginx `grpc_ssl_verify off` — сертификат апстрима не проверяется.
**Почему это проблема:** Прокси-хоп до identity идёт по публичному DNS-имени; без верификации MITM на этом пути (подмена DNS/BGP, компрометация сети) может перехватить логины/пароли/OTP, и nginx этого не заметит. TLS без проверки сертификата защищает только от пассивного прослушивания.
**Рекомендация:** Добавить `grpc_ssl_verify on; grpc_ssl_trusted_certificate /etc/ssl/certs/ca-certificates.crt; grpc_ssl_name identity.barkfluff.com;`.

### S7. server_tokens не отключён — Low
**Файл:** все конфиги (grep по `server_tokens` — ноль совпадений; главный nginx.conf в репозитории отсутствует)
**Проблема:** Дефолт `server_tokens on` — версия nginx раскрывается в заголовке `Server` и на страницах ошибок (включая 502 при падении апстрима).
**Почему это проблема:** Точная версия упрощает подбор эксплойтов под известные CVE. Малый, но бесплатно устранимый риск.
**Рекомендация:** `server_tokens off;` на http-уровне (можно добавить в `01-ssl-params.conf`, раз он грузится из conf.d и на http-уровне).

### S8. Слабая cipher-политика `HIGH:!aNULL:!MD5` + prefer_server_ciphers — Low
**Файл:** `Backend/nginx/01-ssl-params.conf:8,11`, `Backend/nginx/barkfluff.single-server.conf:28-29,62-63`, `Frontend/Developers/nginx.conf:11-12`
**Проблема:** `HIGH:!aNULL:!MD5` — устаревший шаблон: разрешает TLS1.2-сюиты без PFS (статический RSA key exchange) и CBC-режимы; AEAD не приоритизирован. `ssl_prefer_server_ciphers on` при таком списке закрепляет неоптимальный порядок.
**Почему это проблема:** Без PFS компрометация приватного ключа сервера ретроспективно раскрывает записанный трафик. TLSv1.3-сюиты не затронуты, поэтому риск ограничен клиентами на TLSv1.2.
**Рекомендация:** Mozilla intermediate: `ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-CHACHA20-POLY1305:ECDHE-RSA-CHACHA20-POLY1305;` и `ssl_prefer_server_ciphers off;`.

### S9. ssl_protocols не задан в standalone-конфигах — Low
**Файл:** `Backend/nginx/barkfluff.single-server.conf:18-29,52-63`, `Frontend/Developers/nginx.conf:1-12`
**Проблема:** В отличие от `01-ssl-params.conf:7` (TLSv1.2/1.3), эти два конфига не задают `ssl_protocols` и полагаются на дефолт сборки nginx.
**Почему это проблема:** На nginx < 1.23.4 дефолт включает TLSv1.0/1.1. `docker-compose-msk.yml` использует `nginx:latest` (дефолт TLSv1.2/1.3 — фактически безопасно), но поведение неявное и сломается при деплое на старый системный nginx.
**Рекомендация:** Явно добавить `ssl_protocols TLSv1.2 TLSv1.3;` в оба файла.

### S10. docker-compose-msk: конфиги и приватный ключ смонтированы :rw, образ не зафиксирован — Low
**Файл:** `Backend/nginx/docker-compose-msk.yml:5,10-13`
**Проблема:** `./nginx/default.conf`, `./certs` (включая приватный ключ TLS), `./site` и `./auth` смонтированы с правами записи (`:rw`); образ — `nginx:latest` без пина версии.
**Почему это проблема:** При компрометации контейнера атакующий может подменить конфиг/ключ/статику на хосте. `latest` делает деплой невоспроизводимым и может молча притащить breaking-изменения.
**Рекомендация:** Сменить монтирования на `:ro`, зафиксировать версию образа (например `nginx:1.27-alpine`).

### S11. В single-server конфиге нет default_server / catch-all — Low
**Файл:** `Backend/nginx/barkfluff.single-server.conf:18-21,52-55`
**Проблема:** Нет блока `default_server` с `return 444` (аналога `00-default.conf`). Запросы с произвольным/пустым Host на 443 попадут в первый server-блок (`storage.barkfluff.com`), а `*.barkfluff.com` целиком уходит в gateway.
**Почему это проблема:** Сервер отвечает по прямому IP без Host — облегчает обнаружение сканерами (Shodan и т.п.) и обход host-based фильтров. Любой несуществующий поддомен прозрачно проксируется в gateway.
**Рекомендация:** Добавить catch-all блок `listen 443 ssl default_server; server_name _; return 444;` по образцу `00-default.conf`.

---

## Производительность / корректность

### P1. grpc_read_timeout 300s рвёт долгоживущие стримы updates/onliner — High
**Файл:** `Backend/nginx/updates.conf:22-23`, `Backend/nginx/onliner.conf:22-23`
**Проблема:** Updates и Onliner — server-streaming сервисы (подписки на новые сообщения, прочтения, онлайн-статусы), соединения живут часами. `grpc_read_timeout 300s` означает: если апстрим 5 минут ничего не шлёт в стрим (тихий чат, никто не меняет статус), nginx разрывает соединение.
**Почему это проблема:** Каждые ≤5 минут простоя все нативные клиенты (Android, WPF и др.), подключённые через эти субдомены, теряют подписку и вынуждены переподключаться — лишние реконнекты, пропуски событий в окне переподключения, шторм запросов. Проект сам знает о требовании: в `Backend/BarkFluff.Web/nginx/web.conf:55` для тех же сервисов стоит 86400s (24 ч) с комментарием «соединение может жить часами».
**Рекомендация:** В `updates.conf` и `onliner.conf` поднять `grpc_read_timeout`/`grpc_send_timeout` до `86400s` (или вынести streaming-методы в отдельный `location` с большим таймаутом, оставив 300s для unary).

### P2. Backend/nginx/web.conf устарел: буферизация и 300s ломают gRPC-Web стримы; дубль с BarkFluff.Web/nginx/web.conf — High
**Файл:** `Backend/nginx/web.conf:30-39` (проблемный location), ср. `Backend/BarkFluff.Web/nginx/web.conf:52-72` (исправленный вариант)
**Проблема:** В репозитории два конфига для `web.barkfluff.com`. Старый (`Backend/nginx/web.conf`, последнее изменение 2026-04-15, коммит 452d0bb1) проксирует весь трафик — включая gRPC-Web server-streaming Updates/Onliner — через `location /` без `proxy_buffering off`, без `proxy_http_version 1.1` (апстрим получает HTTP/1.0 без keepalive) и с `proxy_read_timeout 300s`. Новый (`Backend/BarkFluff.Web/nginx/web.conf`, 2026-06-06, коммит 067188bf) всё это исправляет: отдельный location для стримов, buffering off, 24h, keepalive, security-заголовки. Также в старом `client_max_body_size 512m` задан только для `/api/files/upload/` (строка 23) — gRPC-Web вызовы с телом >1 МБ через `location /` получат 413.
**Почему это проблема:** Если на сервере развёрнут старый вариант, реалтайм в web-клиенте деградирует: с дефолтной `proxy_buffering on` чанки стриминговых ответов задерживаются в буферах nginx, а через 300s простоя стрим рвётся. Наличие двух разных конфигов под один домен — гарантированный источник «на сервере не то, что в репо».
**Рекомендация:** Удалить `Backend/nginx/web.conf` или заменить его содержимым из `Backend/BarkFluff.Web/nginx/web.conf` (адаптировав upstream `127.0.0.1:7016` → `web:7016`, если nginx в Docker-сети). Один домен — один конфиг-источник истины.

### P3. client_max_body_size не задан для gRPC-локаций (дефолт 1 МБ) — Medium
**Файл:** `Backend/nginx/files.conf:16-25` (главное), а также gRPC-локации `identity.conf:15`, `users.conf:15`, `messages.conf:15`, `beacon.conf:15`, `fast-auth.conf:15`, `updates.conf:15`, `onliner.conf:15`
**Проблема:** `client_max_body_size` влияет и на gRPC-запросы. В `files.conf` лимит 512m задан только в `location /web/` (строка 38), а основная gRPC-локация `/` (FilesApi на 7005) живёт с дефолтным 1m — стриминговая загрузка файла >1 МБ через gRPC API получит `413 Request Entity Too Large` от nginx, до бэкенда запрос не дойдёт.
**Почему это проблема:** Это классическая ловушка nginx+gRPC: лимит срабатывает на накопленном размере тела даже без Content-Length. Если клиенты грузят файлы через gRPC FilesApi (а не только через HTTP `/web/`), загрузка молча ломается на 1 МБ. Для остальных gRPC-сервисов 1m, вероятно, достаточно (вложения ходят через files), но лимит нигде не задан осознанно.
**Рекомендация:** В gRPC-локации `files.conf` добавить `client_max_body_size 512m;`. Для остальных gRPC-хостов — явно зафиксировать осмысленный лимит (например 4m), чтобы поведение было задокументированным, а не дефолтным.

### P4. Два конкурирующих конфига для developers.barkfluff.com — Medium
**Файл:** `Backend/nginx/developers.conf:8-25` и `Frontend/Developers/nginx.conf:1-45`
**Проблема:** Один и тот же `server_name developers.barkfluff.com` определён дважды с принципиально разным поведением: первый проксирует всё на `developers:7020` (gRPC-Web бэкенд), второй отдаёт SPA из `/usr/share/nginx/html` и проксирует `/grpc/...` на identity/api. Если оба файла попадут в conf.d одного nginx — победит загруженный первым (алфавитно `developers.conf`), второй будет молча проигнорирован (nginx выдаст только warning о conflicting server name).
**Почему это проблема:** Непонятно, какой конфиг боевой; деплой «обоих» приводит к недетерминированному результату, а SPA-портал и прямой бэкенд дают разный API-контракт для клиента.
**Рекомендация:** Зафиксировать схему (видимо, SPA + `/grpc`-прокси — актуальная, судя по Frontend/Developers) и удалить/переименовать неактуальный конфиг, либо явно задокументировать, что они для разных серверов.

### P5. Конфиги не привязаны ни к одному деплой-артефакту; msk-compose несовместим с ними — Medium
**Файл:** `Backend/nginx/docker-compose-msk.yml:8-13`; `Backend/docker-compose-dev.yml`, `Backend/docker-compose-master.yml` (сервиса nginx нет)
**Проблема:** Per-service конфиги рассчитаны на nginx-контейнер внутри `barkfluff-network` (resolver `127.0.0.11`, апстримы `identity:7000` и т.д.), но ни один compose их не монтирует. Единственный nginx-compose (`docker-compose-msk.yml`) монтирует один файл `./nginx/default.conf`, которого нет в репозитории, и работает в `network_mode: host`, где Docker-DNS `127.0.0.11` недоступен — per-service конфиги там физически не заработают.
**Почему это проблема:** Боевой nginx-конфиг живёт вне системы контроля версий: невозможно проверить, что на серверах развёрнуто то, что аудируется; правки в репозитории (например фиксы из этого аудита) не попадут на сервер автоматически. Это же объясняет существование дублей (P2, P4).
**Рекомендация:** Добавить сервис nginx в `docker-compose-master.yml` с монтированием `./nginx:/etc/nginx/conf.d:ro` и `certs`, либо завести деплой-скрипт/CI-шаг, копирующий конфиги на серверы. Файл `default.conf` для msk положить в репозиторий.

### P6. Нет keepalive к апстримам из-за динамического proxy_pass/grpc_pass — Low
**Файл:** все per-service конфиги, например `Backend/nginx/identity.conf:16-17` (`set $grpc_backend ...; grpc_pass $grpc_backend;`)
**Проблема:** Паттерн `set $var` + `grpc_pass $var` (нужен для ленивого DNS-резолва) исключает использование `upstream`-блока с `keepalive` — каждый RPC открывает новое TCP-соединение к сервису. Пул keepalive есть только в `Backend/BarkFluff.Web/nginx/web.conf:13-16`.
**Почему это проблема:** Под нагрузкой — лишний connection churn (TCP handshake + HTTP/2 preface на каждый unary-вызов), рост латентности и сокетов в TIME_WAIT.
**Рекомендация:** Для стабильных Docker-имен можно перейти на `upstream identity_up { server identity:7000; keepalive 16; }` + `grpc_pass grpc://identity_up;` (с `resolve`-параметром в nginx Plus или периодическим reload в OSS). Не критично при текущих нагрузках — оценить по метрикам.

### P7. SPA-статика без gzip и кэш-заголовков; нет HTTP→HTTPS редиректа — Low
**Файл:** `Frontend/Developers/nginx.conf:16-17,42-44`
**Проблема:** SPA отдаётся без `gzip on` (дефолт nginx сжимает только text/html на уровне http — JS/CSS-бандлы Vite уходят несжатыми, если в главном nginx.conf не настроено иначе), без `expires`/`Cache-Control` для хэшированных ассетов, и в конфиге нет server-блока на :80 с редиректом.
**Почему это проблема:** Мегабайтные JS-бандлы без сжатия и кэширования — медленная загрузка портала; без редиректа `http://developers.barkfluff.com` либо не отвечает, либо обслуживается чужим default-блоком.
**Рекомендация:** Добавить `gzip on; gzip_types text/css application/javascript application/json;`, `location /assets/ { expires 1y; add_header Cache-Control "public, immutable"; }` и редирект-блок на :80.

### P8. single-server: 512 МБ с буферизацией запроса на диск; устаревший синтаксис http2 — Low
**Файл:** `Backend/nginx/barkfluff.single-server.conf:65,87-100`; синтаксис `listen 443 ssl http2` — все per-service конфиги (например `identity.conf:9`)
**Проблема:** В single-server конфиге `client_max_body_size 512m` действует на весь server-блок, при этом в `location /` (gateway) `proxy_request_buffering` не отключён — тела до 512 МБ буферизуются nginx на диск перед отправкой апстриму. Отдельно: per-service конфиги используют `listen 443 ssl http2` — синтаксис устарел с nginx 1.25.1 (warning), новый — `http2 on;` (как уже сделано в `barkfluff.single-server.conf:20`).
**Почему это проблема:** Параллельные крупные загрузки через gateway заполняют диск временными файлами nginx (DoS-вектор); депрекация в перспективе сломает конфиг на новых версиях.
**Рекомендация:** Сузить 512m до конкретных upload-локаций (как в `web.conf`) либо добавить `proxy_request_buffering off` в `location /`; при ближайшей правке конфигов перейти на `http2 on;`.
