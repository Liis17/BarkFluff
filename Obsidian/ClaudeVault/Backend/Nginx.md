# Nginx — внешние маршруты

Parent: [[Architecture]]. Nginx терминирует TLS и передаёт gRPC/HTTP/WebSocket трафик сервисам. Основной deployment: `Docker/nightly/nginx/`; Navigator и Web Proxy имеют отдельные ingress-конфиги. Каталоги `Docker/dev/` и `Docker/master/` в текущем репозитории отсутствуют.

## Конфигурация и границы

`nginx.conf` включает `sites/*.conf`, задаёт общий body limit 100m. Compose публикует 80/443, монтирует конфиги/сертификаты read-only и подключается к внешней `barkfluff-network`. Большинство сайтов используют Docker DNS `127.0.0.11` и переменную upstream; `storage.conf` и `app.conf` используют именованные upstream.

- `00-default.conf` закрывает неизвестные hosts кодом 444; сервисные HTTP-блоки перенаправляют на HTTPS.
- `01-ssl-params.conf`: TLS 1.2/1.3 и сертификат/ключ `barkfluff.com`. В `files-media.conf` отдельная пара сертификатов `files2.barkfluff.com`, а не общий wildcard snippet. При установке сертификатов требуется полная цепочка; её фактическое содержимое не хранится в Git.
- `00-real-ip.conf` доверяет `CF-Connecting-IP` от **любого** IPv4/IPv6 источника (`0.0.0.0/0`, `::/0`), не ограничивает доверие Cloudflare CIDR. Корректность IP для rate limits зависит от внешней фильтрации доступа к origin; наличие безопасной фильтрации репозиторий не подтверждает.
- `00-rate-limits.conf`: Identity 10r/s, Beacon 5r/s, FastAuth 2r/s и максимум 10 соединений/IP, Federation S2S 30r/s. Зона well-known 5r/s объявлена, но location для неё в текущих Nginx-конфигах отсутствует.

## Маршруты основного ingress

Пути ниже — `Docker/nightly/nginx/sites/`. Числа — upstream этого deployment, не универсальные дефолты сервисов.

| Конфиг / host | Upstream и особенности |
|----------------|------------------------|
| `identity.conf`, `users.conf`, `beacon.conf` | gRPC Identity 7000, Users 7001, Beacon 7002 |
| `messages.conf`, `fast-auth.conf`, `onliner.conf`, `updates.conf` | gRPC Messages 7007, FastAuth 7008, Onliner 7009, Updates 7015 |
| `files.conf` — files | gRPC 7005; `/web/` → HTTP 7006 с удалением префикса |
| `files-media.conf` — files2 | Только HTTP `/web/` → Files 7006; CORS/preflight и `Retry-After`; отдельный media origin объявляется через Beacon |
| `web.conf` — web | Web 7016; upload 512m/600s, Updates/Onliner streams без buffering, timeout 86400s |
| `admin-panel.conf` — panel | AdminPanel 51888; `/api/remote/` поддерживает WebSocket Upgrade |
| `developers.conf` — developers | `/grpc/...IdentityApi/` → gRPC 7000; DevelopersApi/health → HTTP 7020; SPA → 7021; прочий `/grpc/` возвращает 404 |
| `calls.conf` — calls | gRPC 7025 с timeout 3600s; webhook 7026 через этот ingress не публикуется |
| `bots.conf` — bots | `/bot/` → HTTP 7028, прочее → gRPC 7027 |
| `federation.conf` — federation | gRPC 7030 с timeout 3600s |
| `livekit.conf` — livekit | HTTP/WebSocket 7880, timeout 3600s; WebRTC-медиа требует прямых портов LiveKit |
| `storage.conf` — storage | ClientStorage 7050; body 512m, timeout 1800s, upload без request buffering |
| `barkfluff.conf` — apex/wildcard | WebServer 64641 |
| `app.conf` — app | GitNotifier 28111 только `/webhook` и `/healthz`, прочее 404 |
| `fluffboard.conf` — fluffboard | HTTP 8080 |

`files2` предназначен для доступа к origin мимо Cloudflare согласно комментарию конфигурации; фактические DNS/proxy settings вне репозитория не проверены. Лимит Nginx 512m сохраняется. Settings, Notification и Setup не имеют публичных маршрутов в основном ingress.

## Отдельные ingress и ограничения

- `Docker/nightly/navigator/nginx/navigator.conf`: HTTP `/`, `/assets/`, `/api/`, `/ping`, `/admin/` → `navigator:64647`; `/admin` перенаправляется на `/admin/`; остальное gRPC → `navigator:64646`. Эти upstream-порты нужно согласовать с настройками Navigator, чей код по умолчанию использует gRPC 7010. См. [[Backend/Navigator]].
- `Docker/nightly/proxy/nginx/sites/proxy.conf` обслуживает отдельный Web Proxy; [[Backend/Web]] описывает выбранную ноду и allowlist media hosts. Медиа звонков идёт напрямую к LiveKit.
- Federation предоставляет HTTP `/.well-known/barkfluff` на отдельном listener 7031 (`Backend/BarkFluff.Federation/Program.cs`), но ни `barkfluff.conf`, ни другой текущий Nginx site не проксирует этот путь туда. Нельзя считать публичный discovery настроенным по одному наличию API. См. [[Backend/Federation]].

Контракты приложений: [[Backend/Files]], [[Backend/ClientStorage]], [[Backend/Identity]], [[Backend/Calls]].
