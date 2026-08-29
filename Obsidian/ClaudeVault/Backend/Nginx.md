# Backend / Nginx

Набор nginx-конфигураций для продакшн-деплоя всей платформы BarkFluff.
Nginx выступает **reverse proxy** перед всеми микросервисами — терминирует TLS, маршрутизирует трафик по субдоменам, поддерживает как gRPC (`grpc_pass`), так и HTTP (`proxy_pass`).

**Расположение:** `docker/{dev,nightly,master}/nginx/`

---

## Архитектурная роль

```
Клиент (gRPC / HTTPS)
        │
        ▼
   [ Nginx :443 ]   ← TLS-терминация
        │
   по субдомену
        │
   ┌────┴──────────────────────────────────┐
   │  grpc_pass → gRPC-сервис             │
   │  proxy_pass → HTTP-сервис            │
   └───────────────────────────────────────┘
```

- Все HTTP → HTTPS 301-редиректы
- Все gRPC-сервисы — через `grpc_pass` с заголовками `grpc_set_header`
- HTTP-сервисы — через `proxy_pass` с `proxy_set_header`
- Динамический upstream через `set $backend` + `resolver 127.0.0.11` (Docker DNS)
- SSL параметры вынесены в общий сниппет `01-ssl-params.conf`

---

## Файлы проекта

| Файл | Субдомен / Назначение | Порт сервиса | Протокол |
|------|-----------------------|--------------|----------|
| `00-default.conf` | Catch-all для неизвестных хостов | — | Возвращает `444` |
| `00-rate-limits.conf` | Общие rate-limit зоны (`limit_req_zone`/`limit_conn_zone`) для анонимных эндпоинтов | — | — |
| `01-ssl-params.conf` | Общий SSL-сниппет (TLSv1.2/1.3, ciphers, session cache) | — | — |
| `barkfluff.single-server.conf` | Конфиг для деплоя на **одном сервере** без Docker (прямые IP:порт) | 64641, 7050, 7006 | HTTP → HTTPS 301 |
| `identity.conf` | `identity.barkfluff.com` → [[Identity]] | 7000 | gRPC |
| `users.conf` | `users.barkfluff.com` → [[Users]] | 7001 | gRPC |
| `beacon.conf` | `beacon.barkfluff.com` → [[Beacon]] | 7002 | gRPC |
| `navigator.conf` | `navigator.barkfluff.com` → [[Navigator]] | 64646 (gRPC) + 64647 (HTTP `/`, `/admin/`) | gRPC + HTTP |
| `files.conf` | `files.barkfluff.com` → [[Files]] | 7005 (gRPC) + 7006 (HTTP `/web/`) | gRPC + HTTP |
| `files-media.conf` | `files2.barkfluff.com` → [[Files]] HTTP **в обход Cloudflare** | 7006 (HTTP `/web/`) | HTTP |
| `messages.conf` | `messages.barkfluff.com` → [[Messages]] | 7007 | gRPC |
| `fast-auth.conf` | `fast-auth.barkfluff.com` → [[FastAuth]] | 7008 | gRPC |
| `onliner.conf` | `onliner.barkfluff.com` → [[Onliner]] | 7009 | gRPC |
| `updates.conf` | `updates.barkfluff.com` → [[Updates]] | 7015 | gRPC |
| `web.conf` | `web.barkfluff.com` → [[Web]] | 7016 | HTTP (YARP proxy) |
| `admin-panel.conf` | `panel.barkfluff.com` → [[AdminPanel]] | 51888 | HTTP + WSS (WebSocket) |
| `developers.conf` | `developers.barkfluff.com` → [[Developers]] | 7020 (API) + 7021 (SPA) | HTTP + gRPC-Web |
| `calls.conf` | `calls.barkfluff.com` → [[Calls]] | 7025 (gRPC) | gRPC |
| `bots.conf` | `bots.barkfluff.com` → [[Bots]] | 7027 (gRPC) + 7028 (HTTP REST) | gRPC + HTTP |
| `federation.conf` | `federation.barkfluff.com` → [[Federation]] | 7030 (gRPC) + 7031 (well-known, только apex) | gRPC |
| `livekit.conf` | `livekit.barkfluff.com` → LiveKit SFU (сигнализация) | 7880 | WSS (WebSocket) |

> ⚠️ Сервисы [[Configuration]] (порт 7003) и [[Notification]] (порт 7004) — **внутренние**, отдельных nginx-конфигов нет (наружу не публикуются).
> ⚠️ Webhook-порт [[Calls]] (7026) — внутренний (LiveKit → Calls), через nginx не выходит.
> ⚠️ **LiveKit-медиа** (UDP 50000-50200 + ICE/TCP 7881) nginx проксировать НЕ может — это WebRTC-транспорт, обязан публиковаться напрямую на хосте. Через nginx идёт только сигнализация/API (WSS на 7880). MinIO/S3 проксируется напрямую через провайдера (HostKey S3 в проде; в dev — Docker-сеть без nginx), `minio.conf` тоже нет.

---

## Детали по файлам

### `00-default.conf`
Catch-all server block. Слушает `:80` и `:443 ssl` с `default_server`. Для любого неизвестного `Host` возвращает `444` (nginx немедленно закрывает соединение без ответа). Защита от сканирования.

### `00-rate-limits.conf`
Security-аудит (S1/D2): rate limit на анонимные (без JWT) эндпоинты, где нет другой защиты от перебора/спама.
- `limit_req_zone $binary_remote_addr zone=beacon_anon:10m rate=5r/s;` — используется в `beacon.conf` (`burst=10 nodelay`)
- `limit_req_zone $binary_remote_addr zone=fastauth_anon:10m rate=2r/s;` — используется в `fast-auth.conf` (`burst=5 nodelay`), защищает `GenerateFastAuthToken`
- `limit_conn_zone $binary_remote_addr zone=fastauth_streams:10m;` — используется в `fast-auth.conf` (`limit_conn fastauth_streams 10`), ограничивает число одновременных стримов `SubscribeFastAuthResult` с одного IP
- `limit_req_zone $binary_remote_addr zone=federation_s2s:10m rate=30r/s;` — `federation.conf` (`burst=20 nodelay`), S2S-трафик между нодами
- `limit_req_zone $binary_remote_addr zone=federation_wellknown:10m rate=5r/s;` — apex-location `/.well-known/barkfluff` (`burst=5 nodelay`)

### `01-ssl-params.conf`
Общий сниппет SSL, подключается через `include /etc/nginx/conf.d/01-ssl-params.conf;` во всех сервисных конфигах.
- Сертификат: `/etc/nginx/certs/barkfluff.com-crt.pem`
- Ключ: `/etc/nginx/certs/barkfluff.com-key.pem`
- Протоколы: `TLSv1.2 TLSv1.3`
- Session cache: `shared:SSL:10m`, timeout: `10m`

> ⚠️ **Известный инцидент:** серт `barkfluff.com` настоящий (не самоподписанный), но если в `barkfluff.com-crt.pem` лежит только листовой сертификат без intermediate — клиенты, которые сами достраивают цепочку до корня (Android/OkHttp, а не только браузеры с их встроенными intermediate-кешами), не проходят TLS-валидацию на нестандартных WSS-эндпоинтах (например LiveKit-сигнализация, см. [[Backend/Calls]]). Диагностика: `grep -c "BEGIN CERTIFICATE" barkfluff.com-crt.pem` должно быть **2+** (лист + intermediate). Фикс — заменить файл на fullchain (обычно `fullchain.pem` от certbot) и `docker exec nginx_proxy nginx -s reload`.

### `barkfluff.single-server.conf`
Конфигурация для **single-server деплоя** (не Docker-Compose). Упстримы указывают на `127.0.0.1:<port>`.
- `storage.barkfluff.com` → [[ClientStorage]] `:7050`
- `barkfluff.com` / `api.barkfluff.com` / `*.barkfluff.com` → [[WebServer]] `:64641` (основной gateway), с отдельным `location /web/` → [[Files]] HTTP `:7006`
- HTTP → HTTPS 301 для `barkfluff.com` и `*.barkfluff.com`
- `client_max_body_size 512m`

### `files.conf`
Два location в одном server block:
- `/` → gRPC `:7005` (основной API)
- `/web/` → HTTP `:7006` с rewrite и `client_max_body_size 512m`, таймауты 600s для больших файлов

### `files-media.conf`
Отдельный субдомен `files2.barkfluff.com` только под файловый HTTP (`:7006`), **направленный на
origin напрямую, минуя Cloudflare** с его жёстким лимитом 100 МБ на файл. gRPC туда не публикуется —
остаётся на `files.barkfluff.com`.

- Location ровно один — `/web/` (тот же rewrite и таймауты 600s, `client_max_body_size 512m`), путь
  намеренно совпадает с `files.conf`: [[Files]] по-прежнему выдаёт ссылки
  `https://files.barkfluff.com/web/download/{id}`, а клиент **подменяет в них только хост**.
- `add_header Access-Control-Allow-Origin '*'` — веб-клиент работает с другого origin
  (`web.barkfluff.com`), а Cloudflare, который раньше стоял перед файловым хостом, здесь
  отсутствует. Ссылки и так capability-based (кто знает URL — тот и качает).
- `OPTIONS` для `/web/` обрабатывается самим nginx и возвращает `204` с
  `Access-Control-Allow-Methods: POST, OPTIONS` и разрешёнными заголовками, поэтому
  браузерный upload с progress-событиями проходит CORS preflight до backend.
- Адрес объявляется нодой через `ExternalEndpoint:MediaHost` конфигурации [[Files]] →
  [[Beacon]] `files_media_endpoint`. Пустое значение = клиенты работают по старому адресу,
  поэтому уже установленные (старые) клиенты продолжают ходить через Cloudflare.
- DNS `files2` должен указывать на origin **без** проксирования Cloudflare, иначе смысла нет;
  сертификат — общий wildcard из `01-ssl-params.conf`.

### `navigator.conf`
`navigator.barkfluff.com` совмещает публичный gRPC-реестр, публичную главную страницу и React-админку [[Backend/Navigator]]. HTTP-часть (порт `64647`): `/` (публичный каталог серверов), `/assets/`, `/api/` (анонимный список серверов), `/ping`, `/admin/`; все прочие пути идут через `grpc_pass` на `64646`. Раньше `/` делал 301 на `/admin/` — убрано. Источник истины конфига — `docker/navigator/nginx/navigator.conf` в репозитории (upstream по умолчанию `127.0.0.1:64646/64647` — хостовые порты выделенного хоста; при размещении nginx в общей docker-сети заменить на `navigator:7010/7011` и добавить resolver, в dev-варианте сервис называется `navigator-dev`).

### `web.conf`
Два location:
- `/api/files/upload/` — увеличенный `client_max_body_size 512m`, таймауты 600s (YARP пробрасывает загрузку файлов)
- `/` — стандартный proxy_pass на [[Web]] `:7016`

### Остальные сервисы (identity, users, beacon, messages, fast-auth, onliner, updates)
Единообразная структура:
- HTTP → HTTPS 301
- `listen 443 ssl http2`, `include 01-ssl-params.conf`
- `resolver 127.0.0.11 valid=30s ipv6=off` — Docker embedded DNS
- `set $grpc_backend grpc://<service>:<port>` + `grpc_pass`
- Таймауты: `grpc_read_timeout 300s`, `grpc_send_timeout 300s`

### `admin-panel.conf` и `developers.conf`
HTTP-сервисы (не gRPC), используют `proxy_pass`:
- `panel.barkfluff.com` → `admin-panel:51888`
- `/api/remote/` на `panel.barkfluff.com` проксируется как HTTP/1.1 WebSocket Upgrade; для этого server block не включает HTTP/2, а консольный location отключает proxy buffering и держит таймаут 3600s.
- `developers.barkfluff.com`: `/grpc/barkfluff.identity.IdentityApi/` → `identity:7000`,
  `/grpc/barkfluff.developers.DevelopersApi/` → `developers:7020`, health endpoints → `developers:7020`,
  остальные пути → `developers:7021` (SPA)
- HTTPS server block Developers добавляет security headers `X-Content-Type-Options: nosniff`,
  `X-Frame-Options: DENY` и `Referrer-Policy: strict-origin-when-cross-origin`.

### `calls.conf`
gRPC по образцу `updates.conf` (`grpc://calls:7025`), но `grpc_read/send_timeout 3600s` —
`SubscribeCallEvents` долгоживущий и должен переживать простои (доставка входящих звонков).
Webhook-порт 7026 наружу не выходит (LiveKit достукивается до `calls:7026` по docker-сети).

### `federation.conf`
gRPC по образцу `users.conf` (`grpc://federation:7030`), но `grpc_read/send_timeout 3600s` (как у
`calls.conf`) — `SubscribePresence` (Фаза 4) и `FetchFile` (Фаза 3) долгоживущие/тяжёлые. TLS-серт
ноды может быть self-signed (S2S проверяет SPKI-пин, не CA — [[Federation]]). Rate-limit
`federation_s2s` (30r/s, `00-rate-limits.conf`) — S2S-батчи легитимно часты, зона шире самой
нагруженной анонимной (`beacon_anon`); точный тюнинг — Фаза 6.2.

`/.well-known/barkfluff` отдаётся НЕ этим конфигом, а apex-сервером (`barkfluff.single-server.conf`
и любой другой server-блок, обслуживающий сам `barkfluff.com`) — location `= /.well-known/barkfluff`
проксирует на `federation:7031` (HTTP/1-листенер well-known, отдельный от gRPC-порта 7030). На apex
для публичных нод обязателен CA-валидный серт (Let's Encrypt) — это bootstrap-канал discovery;
своя rate-limit-зона `federation_wellknown` (5r/s, жёстче — редкие запросы).

### `livekit.conf`
WSS-сигнализация LiveKit SFU: `listen 443 ssl` (**без** `http2` — WSS это HTTP/1.1 Upgrade),
`proxy_pass http://livekit:7880` с `Upgrade/Connection` (через `map $http_upgrade $connection_upgrade`),
таймауты 3600s. Клиент получает `wss://livekit.barkfluff.com` (Configuration `LiveKit:Url` → [[Beacon]]).
**Медиа через nginx не идёт** — только сигнализация/серверный API LiveKit.

---

## Субдомены — сводная таблица

| Субдомен | Сервис |
|----------|--------|
| `identity.barkfluff.com` | [[Identity]] |
| `users.barkfluff.com` | [[Users]] |
| `beacon.barkfluff.com` | [[Beacon]] |
| `navigator.barkfluff.com` | [[Navigator]] (gRPC + `/` + `/admin/`) |
| `files.barkfluff.com` | [[Files]] (gRPC + `/web/` через Cloudflare) |
| `files2.barkfluff.com` | [[Files]] HTTP (`/web/`, мимо Cloudflare — без лимита 100 МБ) |
| `messages.barkfluff.com` | [[Messages]] |
| `fast-auth.barkfluff.com` | [[FastAuth]] |
| `onliner.barkfluff.com` | [[Onliner]] |
| `updates.barkfluff.com` | [[Updates]] |
| `web.barkfluff.com` | [[Web]] |
| `panel.barkfluff.com` | [[AdminPanel]] |
| `developers.barkfluff.com` | [[Developers]] |
| `calls.barkfluff.com` | [[Calls]] (gRPC) |
| `bots.barkfluff.com` | [[Bots]] (gRPC + HTTP REST) |
| `federation.barkfluff.com` | [[Federation]] (S2S gRPC, self-signed + SPKI-пин) |
| `livekit.barkfluff.com` | LiveKit SFU (WSS-сигнализация; медиа — напрямую) |
| `storage.barkfluff.com` | [[ClientStorage]] |
| `barkfluff.com` / `api.barkfluff.com` | [[WebServer]] (single-server) |

---

## Актуальность

Проверено по коду `docker/dev/nginx/`; конфигурации `nightly` и `master` содержат тот же набор маршрутов. Все порты соответствуют портам сервисов из [[Архитектура]].
Конфиги актуальны на момент последнего исследования.
