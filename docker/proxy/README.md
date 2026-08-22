# docker/proxy — Web Proxy (зеркало веб-клиента для РФ)

Автономный стек: `nginx` + `web` (образ `barkfluff-web` в режиме `Web:Mode=Proxy`).
Основной стек ноды стоит за Cloudflare и недоступен из РФ; этот стек поднимается
на доступном хосте и релеит весь трафик веб-клиента на Web-шлюз ноды. Подробности —
Obsidian `Backend/Web.md` (режим Proxy).

## Что делает Proxy-режим

| Трафик | Путь через прокси |
|---|---|
| Статика (html/js/css) | раздаётся локально из того же образа — версии клиента на ноде и прокси идентичны |
| gRPC-Web (все сервисы), `/api/files/upload`, `/ping/{service}`, `/pwa-config.js` | pass-through на `WEB_PROXY_TARGET` |
| Медиа (`files.barkfluff.com`, `files2...`) | `/media/{host}/{path}?подпись` → relay с allowlist `WEB_PROXY_MEDIA_HOSTS` |

Звонки: сигналинг проходит через прокси, но WebRTC-медиа (LiveKit) идёт напрямую
на `livekit_url` ноды — из РФ может не работать (известное ограничение).

## Установка

```bash
cd docker/proxy
cp sample.env .env        # проверить WEB_IMAGE и заполнить target/media hosts
mkdir certs               # положить TLS-сертификат для своего домена
docker compose up -d
```

`WEB_IMAGE` должен быть собран из той же ветки, что и Web-шлюз ноды:
`barkfluff-web-nightly:latest` для nightly или `barkfluff-web:latest` для stable.
Compose намеренно не имеет fallback на stable и всегда подтягивает указанный образ,
чтобы прокси не запускался на старом бинарнике без режима `Proxy`.

DNS домена (по умолчанию в конфиге `proxy.barkfluff.com`) направить на этот хост
**мимо Cloudflare** (серое облако в CF), иначе теряется смысл прокси. Имя домена
правится в `nginx/sites/proxy.conf`.

## Файлы

| Файл | Назначение |
|------|------------|
| `docker-compose.yml` | web (Proxy-режим) + nginx, параметры из `.env` |
| `sample.env` | шаблон env: образ, target ноды, allowlist медиа-хостов |
| `nginx/nginx.conf` | базовый конфиг nginx |
| `nginx/sites/proxy.conf` | site: TLS, upload 512m, streaming 86400s, media без буферизации |
| `nginx/sites/01-ssl-params.conf` | пути сертификатов и SSL-параметры |
| `certs/` | (не в git) TLS-сертификаты |

Обновление веб-клиента — тем же образом, что и на ноде: `docker compose pull web && docker compose up -d`.
