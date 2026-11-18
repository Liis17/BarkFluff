# BFT Proxy Exit и Relay

Exit и Relay устанавливаются на разных серверах и запускаются отдельными compose-файлами. Они не входят в compose основной ноды. Примеры используют условные имена `*.barkfluff.com`; замените их значениями своей DNS-зоны. Для Exit и Relay создайте DNS-записи без проксирования Cloudflare (DNS only): BFT использует собственный TCP-транспорт.

## 1. Подготовьте Exit (Латвия)

Скопируйте каталог `Docker/nightly/bft-proxy/` на Exit-сервер и выполните команды из него:

```sh
cp exit.env.example exit.env
chmod 600 exit.env
sudo install -d -o 1654 -g 1654 -m 700 client-keys
sudo install -o 1654 -g 1654 -m 600 /dev/null client-keys/clients.txt
```

Перед pull войдите в используемый registry, если сервер ещё не авторизован: `docker login docker.barkfluff.com`. Вводите существующие credentials интерактивно; их не нужно добавлять в env-файлы.

Скачайте опубликованный образ и сгенерируйте статический ключ Exit:

```sh
docker compose -f docker-compose.exit.yml pull
docker compose -f docker-compose.exit.yml run --rm --no-deps exit genkey
```

Скопируйте значение `BFT_STATIC_PRIVATE_KEY=...` из вывода в `exit.env`. Не публикуйте этот приватный ключ. Сгенерируйте общий ключ Relay↔Exit один раз:

```sh
openssl rand -base64 32 | tr '+/' '-_' | tr -d '='
```

Скопируйте полученные 43 символа в `BFT_RELAY_KEY` обоих env-файлов. Сгенерируйте отдельный секрет для origin:

```sh
openssl rand -hex 32
```

Запишите его в `BFT_ORIGIN_SECRET` в `exit.env` и в частную Nginx map (шаг 4). Заполните `BFT_TRUSTED_RELAYS` публичным исходящим IP Relay-сервера. `BFT_NEXT_HOP` в `relay.env` должен указывать на DNS-имя Exit и порт `7443`.

`exit.env.example` показывает 12 необязательных HTTPS upstream: `BFT_UPSTREAM_BEACON`, `BFT_UPSTREAM_IDENTITY`, `BFT_UPSTREAM_USERS`, `BFT_UPSTREAM_MESSAGES`, `BFT_UPSTREAM_FILES`, `BFT_UPSTREAM_FILES_MEDIA`, `BFT_UPSTREAM_UPDATES`, `BFT_UPSTREAM_ONLINER`, `BFT_UPSTREAM_FASTAUTH`, `BFT_UPSTREAM_CALLS`, `BFT_UPSTREAM_BOTS`, `BFT_UPSTREAM_STORAGE`. Оставьте только используемые сервисы или замените примеры на доступные HTTPS origin-имена. URL не должен содержать credentials, query или fragment. Остальные Exit-переменные и значения по умолчанию приведены в этом файле: `BFT_MODE`, `BFT_LISTEN`, `BFT_STATIC_PRIVATE_KEY`, `BFT_CLIENT_KEYS_FILE`, `BFT_PROXY_ID`, `BFT_ORIGIN_SECRET`, `BFT_ACCESS_TOKENS`, `BFT_RELAY_LISTEN`, `BFT_TRUSTED_RELAYS`, `BFT_RELAY_KEY`, `BFT_MAX_TUNNELS_PER_KEY`, `BFT_MAX_TUNNELS`.

Заполните `exit.env` реальными значениями и поднимите Exit:

```sh
docker compose -f docker-compose.exit.yml up -d
docker compose -f docker-compose.exit.yml logs -f exit
```

Контейнер работает с UID `1654`; каталог и файл ключей должны оставаться доступными этому UID. Файл `clients.txt` смонтирован read-write: CLI добавляет и удаляет записи, а Exit перечитывает его без перезапуска.

## 2. Подготовьте Relay (Россия)

Скопируйте тот же каталог на отдельный Relay-сервер:

```sh
cp relay.env.example relay.env
chmod 600 relay.env
```

В `relay.env` укажите публичное имя Relay, DNS-имя и relay-порт Exit, общий `BFT_RELAY_KEY` и `BFT_RELAY_ID`. Переменные Relay: `BFT_MODE`, `BFT_LISTEN`, `BFT_NEXT_HOP`, `BFT_RELAY_ID`, `BFT_RELAY_KEY`, `BFT_RELAY_MAX_CONNECTIONS_PER_IP`, `BFT_RELAY_MAX_CONNECTIONS`, `BFT_RELAY_CONNECT_TIMEOUT_SECONDS`.

```sh
docker compose -f docker-compose.relay.yml pull
docker compose -f docker-compose.relay.yml up -d
docker compose -f docker-compose.relay.yml logs -f relay
```

## 3. Настройте DNS и firewall

- Exit: откройте TCP `443` для клиентов и TCP `7443` только для исходящего публичного IP Relay. Контейнер слушает `8443` (прямой порт) и `7443` (Relay), compose публикует их как `443` и `7443`.
- Relay: откройте TCP `443` для клиентов. Relay слушает `8443` внутри контейнера и подключается исходящим TCP к Exit `:7443`.
- Разрешите Exit исходящий TCP `443` к origin/Cloudflare. Дополнительный входящий порт на основном сервере не нужен.
- DNS имена Exit и Relay должны быть DNS only, не за Cloudflare HTTP proxy. На зоне origin проверьте, что включён gRPC.
- В Cloudflare добавьте исходящий IP Exit в исключение/Skip для bot- и rate-защиты. Сохраняйте обычную TLS-проверку и Nginx-проверки BFT IP/секрета: исключение Cloudflare не заменяет доверие на origin.

`BFT_TRUSTED_RELAYS` на Exit — IP, с которого Relay фактически подключается к Exit после NAT. Он должен совпадать с source IP, который видит Exit firewall.

## 4. Добавьте Exit в Nginx главного сервера

На origin Nginx создайте два приватных файла на главном сервере, не коммитьте их:

- `Docker/nightly/nginx/sites/bft-proxy-exit-ips/<имя>.conf`:

  ```nginx
  <исходящий-публичный-IP-Exit> 1;
  ```

- `Docker/nightly/nginx/sites/bft-proxy-secrets/<имя>.conf`:

  ```nginx
  "<значение-BFT_ORIGIN_SECRET>" 1;
  ```

Укажите исходящий публичный IP Exit, который origin получает после обработки `CF-Connecting-IP` в real-ip конфигурации; при NAT это внешний egress IP, не адрес интерфейса Exit и не адрес Cloudflare. Секрет должен совпадать с `BFT_ORIGIN_SECRET`, быть lowercase hex не короче 64 символов. После добавления проверьте и перечитайте Nginx:

```sh
docker exec nginx nginx -t
docker exec nginx nginx -s reload
```

Nginx принимает BFT заголовки только при совпадении Exit IP и секрета, использует `X-BFT-Client-IP` для исходного адреса, удаляет `X-BFT-Client-IP` и `X-BFT-Proxy-Secret` перед приложениями и передаёт доверенный `X-BFT-Proxy-Id`.

## 5. Выдайте ссылку и отзовите клиента

Добавляйте и удаляйте ключи через CLI Exit-контейнера: `docker compose run` получает тот же `exit.env` и тот же каталог `client-keys`, что и сервис.

Без Relay `--host` и `--port` — публичные адрес и порт прямого Exit. С Relay это публичные адрес и порт Relay, а Exit доступен Relay только по `BFT_NEXT_HOP`:

```sh
# Прямое подключение
docker compose -f docker-compose.exit.yml run --rm --no-deps exit add-client --label alice-phone --name "Alice phone" --host exit-lv.barkfluff.com --port 443

# Подключение через Relay
docker compose -f docker-compose.exit.yml run --rm --no-deps exit add-client --label alice-relay --name "Alice via Relay" --host relay-ru.barkfluff.com --port 443

# Отзыв по тому же label
docker compose -f docker-compose.exit.yml run --rm --no-deps exit remove-client --label alice-phone
```

Передайте выведенную `bft://` ссылку пользователю защищённым способом. Если задан `BFT_ACCESS_TOKENS`, выдайте клиенту соответствующий токен через `--token`. Exit перечитывает файл ключей примерно каждые 2 секунды; удалённый ключ и его активные туннели отзываются без перезапуска.

## 6. Проверьте соединение

После успешного клиентского соединения ожидайте в логах Exit строку `BFT tunnel accepted from ... with key ...`. Для подключения через Relay дополнительно ожидайте `BFT relay connection accepted from ... via relay ru-1` в логах Relay. Ошибки выбора upstream появляются как `BFT upstream forwarding failed ...` в логах Exit; проверьте `BFT_UPSTREAM_*`, DNS/TLS, доступность gRPC на Cloudflare и Nginx map. LiveKit WebSocket/WebRTC и медиа звонков идут напрямую, мимо BFT Proxy.

Compose публикует nightly-образ `docker.barkfluff.com/barkfluff-bftproxy-nightly:latest`. Сборка и push в registry не запускают эти compose-файлы и не выкатывают сервис на сервер.
