# nginx — конфигурация для web.barkfluff.com

## Файлы

| Файл | Назначение |
|------|------------|
| `web.conf` | Серверный блок nginx для `web.barkfluff.com` |

## Установка

```bash
# 1. Заменить текущий web.conf на сервере
sudo cp web.conf /etc/nginx/sites-available/web.conf

# 2. Создать симлинк (если ещё нет)
sudo ln -sf /etc/nginx/sites-available/web.conf /etc/nginx/sites-enabled/

# 3. Проверить синтаксис
sudo nginx -t

# 4. Перезагрузить nginx
sudo systemctl reload nginx
```

## TLS

Сертификаты и параметры SSL подключаются через общий файл:

```
include /etc/nginx/conf.d/01-ssl-params.conf;
```

Этот файл содержит `ssl_certificate`, `ssl_certificate_key`, протоколы и шифры для `*.barkfluff.com`.

## Ключевые настройки

### Server-streaming (Updates, Onliner)

gRPC-Web использует server-streaming для подписок на новые сообщения, статусы прочтения и онлайн-статус.
Эти соединения живут часами, поэтому:

- `proxy_read_timeout 86400s` (24 часа) — nginx не обрывает idle-потоки
- `proxy_buffering off` — чанки сразу отправляются браузеру
- Соответствует `ActivityTimeout = 24h` в YARP (Program.cs)

### Загрузка файлов

- `client_max_body_size 512m` — соответствует лимиту бэкенда
- `proxy_request_buffering off` — потоковая передача тела запроса без буферизации в tmpfs

### Порт бэкенда

BarkFluff.Web (Kestrel) слушает на **порту 7016** (HTTP/1.1 + HTTP/2).
Если порт изменён через `RunSettings:Port`, обновите `upstream barkfluff_web` в конфиге.
