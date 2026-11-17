# Nginx — внешний ingress

Parent: [[Architecture]]. Nginx terminates TLS and forwards gRPC, HTTP and WebSocket traffic. Основной deployment: `Docker/nightly/nginx/`; Navigator и Web Proxy имеют отдельные ingress-конфиги.

## Конфигурация и порядок загрузки

`Docker/nightly/nginx/nginx.conf` включает `sites/*.conf` внутри `http` и задаёт общий body limit 100m. Compose монтирует весь каталог `sites/` в `/etc/nginx/conf.d` и сертификаты в `/etc/nginx/certs`.

В `Docker/nightly/nginx/sites/` находятся `00-bft-proxy.conf`, `00-default.conf`, `00-rate-limits.conf`, `00-real-ip.conf`, `01-ssl-params.conf` и `10-subdomains.conf`. Последний содержит все сервисные `server`-блоки в алфавитном порядке, задаёт `server_names_hash_bucket_size 64` и объявляет HTTP-level `map` для LiveKit WebSocket и именованные upstream для App и Storage. Заголовочный сниппет `bft-proxy-headers.inc` не соответствует glob `*.conf` и подключается только из upstream `location`.

- `00-default.conf` закрывает неизвестные hosts кодом 444 и направляет access/error log в stdout/stderr.
- `00-real-ip.conf` доверяет `CF-Connecting-IP` только актуальным IPv4/IPv6 диапазонам Cloudflare; источник и дата проверки указаны в файле. Прямое подключение к origin не может переписать `$remote_addr` поддельным `CF-Connecting-IP`.
- `01-ssl-params.conf` задаёт TLS 1.2/1.3 и общую пару сертификатов `barkfluff.com` из `/etc/nginx/certs/`. `files2.barkfluff.com` использует этот же include. Содержимое сертификатов и ключей не хранится в Git.
- `00-rate-limits.conf` объявляет зоны Beacon 5r/s, FastAuth 2r/s и 10 соединений/IP, Federation S2S 30r/s, well-known 5r/s и Identity 10r/s. Well-known зона объявлена, но location для неё в текущем ingress отсутствует.
- `00-bft-proxy.conf` включает частные карты из `Docker/nightly/nginx/sites/bft-proxy-exit-ips/*.conf` и `Docker/nightly/nginx/sites/bft-proxy-secrets/*.conf`, проверяет real-ip-adjusted Exit IP, секрет и IP клиента. В каждой private map директории `.gitignore` сохраняет `00-empty.conf`, а значения для ручной настройки приведены в `bft-proxy-exit-ips.conf.example` и `bft-proxy-secrets.conf.example`. Невалидное значение `X-BFT-Client-IP`, которое `ngx_http_geo_module` преобразует в `255.255.255.255`, исключено явным правилом; такой адрес не используется как клиентский IP.
- BFT секрет в частной map-конфигурации — lowercase hexadecimal, не короче 64 символов; строковые ключи `map` сравниваются без учёта регистра. Создайте секрет командой `openssl rand -hex 32` или длиннее.
- `$bft_client_ip` подставляется в `X-Real-IP`; `$bft_forwarded_for` и `$bft_forwarded_for_add` сохраняют прежний стиль XFF каждой location при отключённом/недоверенном BFT и используют IP BFT-клиента при доверенном запросе. `$bft_rate_limit_key` применяет тот же IP в существующих `limit_req`/`limit_conn` зонах.
- `bft-proxy-headers.inc` удаляет входящие `X-BFT-Client-IP` и `X-BFT-Proxy-Secret`, а проверенный `X-BFT-Proxy-Id` передаёт сервису только для доверенного запроса. Секрет не включается в access-log.
- BFT выключен, пока в `bft-proxy-exit-ips/` и `bft-proxy-secrets/` нет дополнительных частных `*.conf`. `.gitignore` сохраняет только `00-empty.conf`; синтаксис показан в `bft-proxy-*.conf.example`. Реальные Exit IP и секреты не хранятся в репозитории.
- `RequestContextInterceptor` добавляет ProxyId только в unary gRPC-вызовах. Стримы не получают контекст запроса, как и до BFT-изменения.

## Субдомены в `10-subdomains.conf`

Порты ниже относятся к upstream этого deployment.

| Сервис / host | Маршруты и особенности |
|---|---|
| admin-panel — `panel.barkfluff.com` | AdminPanel 51888; `/api/remote/` поддерживает WebSocket Upgrade |
| app — `app.barkfluff.com` | GitNotifier 28111 только для `/webhook` и `/healthz`; остальное 404 |
| barkfluff — apex и wildcard | WebServer 64641 |
| beacon — `beacon.barkfluff.com` | gRPC 7002; limit 5r/s |
| bots — `bots.barkfluff.com` | `/bot/` → HTTP 7028, остальное → gRPC 7027 |
| calls — `calls.barkfluff.com` | gRPC 7025, timeout 3600s |
| developers — `developers.barkfluff.com` | Identity gRPC 7000; Developers HTTP/health 7020; SPA 7021; прочий `/grpc/` возвращает 404 |
| fast-auth — `fast-auth.barkfluff.com` | gRPC 7008; limit 2r/s и максимум 10 соединений/IP |
| federation — `federation.barkfluff.com` | gRPC 7030, timeout 3600s |
| files — `files.barkfluff.com` | gRPC 7005; `/web/` → HTTP 7006 с удалением префикса |
| files-media — `files2.barkfluff.com` | Только HTTP `/web/` → Files 7006; CORS/preflight и `Retry-After`; общий SSL include |
| fluffboard — `fluffboard.barkfluff.com` | HTTP 8080 |
| identity — `identity.barkfluff.com` | gRPC 7000; limit 10r/s |
| livekit — `livekit.barkfluff.com` | HTTP/WebSocket 7880, timeout 3600s; WebRTC-медиа требует прямых портов LiveKit |
| messages — `messages.barkfluff.com` | gRPC 7007 |
| onliner — `onliner.barkfluff.com` | gRPC 7009 |
| storage — `storage.barkfluff.com` | ClientStorage 7050; body 512m, timeout 1800s, upload без request buffering |
| updates — `updates.barkfluff.com` | gRPC 7015 |
| users — `users.barkfluff.com` | gRPC 7001 |
| web — `web.barkfluff.com` | Web 7016; upload 512m/600s; gRPC-Web streaming без buffering и с timeout 86400s |

`files2` предназначен для прямого доступа к origin без Cloudflare согласно комментариям его конфигурации; фактические DNS/proxy settings не проверены. Nginx принимает этот traffic, но не доверяет присланному клиентом `CF-Connecting-IP`.

## Отдельные ingress и генератор

- `Docker/nightly/navigator/nginx/navigator.conf` маршрутизирует Navigator; upstream-порты нужно согласовать с его настройками (`Backend/Navigator.md`).
- `Docker/nightly/proxy/nginx/sites/proxy.conf` обслуживает отдельный Web Proxy; [[Backend/Web]] описывает выбранную ноду и allowlist media hosts. Медиа звонков идёт напрямую к LiveKit.
- Federation публикует HTTP `/.well-known/barkfluff` на отдельном listener 7031 (`Backend/BarkFluff.Federation/Program.cs`), но основной ingress не проксирует этот путь туда. См. [[Backend/Federation]].
- `Tools/BarkFluff.Builder/NginxGenerator.cs` генерирует отдельный набор Nginx-конфигов и пока не генерирует BFT maps, очистку BFT-заголовков или клиентские ключи лимитов. Генератор и `Docker/setup/settings-setup.nginx.conf` не входят в основной ingress; их совместимость с этой раскладкой нужно обновить отдельно.

Контракты приложений: [[Backend/Files]], [[Backend/ClientStorage]], [[Backend/Identity]], [[Backend/Calls]].
