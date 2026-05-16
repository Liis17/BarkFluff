# Backend / Nginx

Набор nginx-конфигураций для продакшн-деплоя всей платформы BarkFluff.
Nginx выступает **reverse proxy** перед всеми микросервисами — терминирует TLS, маршрутизирует трафик по субдоменам, поддерживает как gRPC (`grpc_pass`), так и HTTP (`proxy_pass`).

**Расположение:** `Backend/nginx/`

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
| `01-ssl-params.conf` | Общий SSL-сниппет (TLSv1.2/1.3, ciphers, session cache) | — | — |
| `barkfluff.single-server.conf` | Конфиг для деплоя на **одном сервере** без Docker (прямые IP:порт) | 64641, 7050, 7006 | HTTP → HTTPS 301 |
| `identity.conf` | `identity.barkfluff.com` → [[Identity]] | 7000 | gRPC |
| `users.conf` | `users.barkfluff.com` → [[Users]] | 7001 | gRPC |
| `beacon.conf` | `beacon.barkfluff.com` → [[Beacon]] | 7002 | gRPC |
| `files.conf` | `files.barkfluff.com` → [[Files]] | 7005 (gRPC) + 7006 (HTTP `/web/`) | gRPC + HTTP |
| `messages.conf` | `messages.barkfluff.com` → [[Messages]] | 7007 | gRPC |
| `fast-auth.conf` | `fast-auth.barkfluff.com` → [[FastAuth]] | 7008 | gRPC |
| `onliner.conf` | `onliner.barkfluff.com` → [[Onliner]] | 7009 | gRPC |
| `updates.conf` | `updates.barkfluff.com` → [[Updates]] | 7015 | gRPC |
| `web.conf` | `web.barkfluff.com` → [[Web]] | 7016 | HTTP (YARP proxy) |
| `admin-panel.conf` | `panel.barkfluff.com` → [[AdminPanel]] | 51888 | HTTP |
| `developers.conf` | `developers.barkfluff.com` → [[Developers]] | 7020 | HTTP (gRPC-Web) |

> ⚠️ Сервисы [[Configuration]] (порт 7003) и [[Notification]] (порт 7004) — **внутренние**, отдельных nginx-конфигов нет (наружу не публикуются). MinIO/S3 проксируется напрямую через провайдера (HostKey S3 в проде; в dev — Docker-сеть без nginx), `minio.conf` тоже нет.

---

## Детали по файлам

### `00-default.conf`
Catch-all server block. Слушает `:80` и `:443 ssl` с `default_server`. Для любого неизвестного `Host` возвращает `444` (nginx немедленно закрывает соединение без ответа). Защита от сканирования.

### `01-ssl-params.conf`
Общий сниппет SSL, подключается через `include /etc/nginx/conf.d/01-ssl-params.conf;` во всех сервисных конфигах.
- Сертификат: `/etc/nginx/certs/barkfluff.com-crt.pem`
- Ключ: `/etc/nginx/certs/barkfluff.com-key.pem`
- Протоколы: `TLSv1.2 TLSv1.3`
- Session cache: `shared:SSL:10m`, timeout: `10m`

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
- `developers.barkfluff.com` → `developers:7020`

---

## Субдомены — сводная таблица

| Субдомен | Сервис |
|----------|--------|
| `identity.barkfluff.com` | [[Identity]] |
| `users.barkfluff.com` | [[Users]] |
| `beacon.barkfluff.com` | [[Beacon]] |
| `files.barkfluff.com` | [[Files]] |
| `messages.barkfluff.com` | [[Messages]] |
| `fast-auth.barkfluff.com` | [[FastAuth]] |
| `onliner.barkfluff.com` | [[Onliner]] |
| `updates.barkfluff.com` | [[Updates]] |
| `web.barkfluff.com` | [[Web]] |
| `panel.barkfluff.com` | [[AdminPanel]] |
| `developers.barkfluff.com` | [[Developers]] |
| `storage.barkfluff.com` | [[ClientStorage]] |
| `barkfluff.com` / `api.barkfluff.com` | [[WebServer]] (single-server) |

---

## Актуальность

Проверено по коду `Backend/nginx/`. Все порты соответствуют портам сервисов из [[Архитектура]].
Конфиги актуальны на момент последнего исследования.
