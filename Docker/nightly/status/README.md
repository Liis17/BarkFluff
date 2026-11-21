# Статус ноды (BarkFluff.Status)

Отдельное развёртывание страницы статуса публичной ноды. Оно не связано с compose основной ноды: нет общих сетей, томов и nginx. Сервис опрашивает готовность публичных компонентов по HTTPS (`/health/ready`) и показывает доступность за 90 суток.

## Что нужно

- Docker с Compose v2.
- Доменное имя для страницы статуса (по умолчанию `status.example.com`), запись DNS на этот сервер.
- TLS-сертификат и ключ для этого имени: `fullchain.pem` и `privkey.pem` в каталоге `STATUS_CERTS_DIR`. Сертификат кладёт оператор; в репозитории их нет.
- Публичное DNS-имя ноды (`NODE_DOMAIN`). Статус обращается к `https://<сервис>.<NODE_DOMAIN>/health/ready` для 13 публичных компонентов: beacon, bots, calls, developers, fast-auth, federation, files, identity, messages, onliner, updates, users, web.

## Развёртывание

```sh
cd Docker/nightly/status
cp .env.example .env          # заполните NODE_DOMAIN, STATUS_SERVER_NAME и при необходимости остальное
mkdir -p certs                # положите fullchain.pem и privkey.pem
docker compose config -q      # проверка compose-файла
docker compose pull
docker compose up -d
docker compose logs -f status
```

Проверка: `https://<STATUS_SERVER_NAME>/` открывает страницу, `https://<STATUS_SERVER_NAME>/api/status` возвращает JSON со списком сервисов.

Обновление образа: `docker compose pull && docker compose up -d`. Данные остаются в томе `status-data`.

### Без Compose

Образ можно запустить отдельно. Переменные берутся из того же `.env`, контейнер слушает HTTP на 8080 без TLS, поэтому перед ним нужен свой TLS-фронт:

```sh
docker run -d --name barkfluff-status \
  --env-file .env \
  -v status-data:/var/lib/barkfluff-status \
  -p 127.0.0.1:8080:8080 \
  docker.barkfluff.com/barkfluff-status-nightly:latest
```

## Настройка

| Переменная | Назначение |
|---|---|
| `NODE_DOMAIN` | Обязательна. Хосты сервисов: `<сервис>.<NODE_DOMAIN>` (имена из `Backend/BarkFluff.Settings/Catalog/NodeSubdomains.cs`). |
| `FEDERATION_SPKI_PIN` | Необязательна. Base64(SHA256(SubjectPublicKeyInfo)) ключа Federation. Если задана, для `federation` цепочка сертификата не проверяется, принимается только совпадающий ключ; несовпадение — сервис недоступен. Для остальных сервисов проверка обычная. |
| `STATUS_DISABLED_SERVICES` | Необязательна. Компоненты, которые намеренно выключены. Они не проверяются и не входят в общий статус. |
| `Status__ServiceOrigins__<сервис>` | Необязательна. HTTPS origin вместо `<сервис>.<NODE_DOMAIN>`, только схема, хост и порт; путь, query и учётные данные запрещены. Пишется в `.env` рядом с остальными переменными, например `Status__ServiceOrigins__messages=https://msg.example.org`; compose передаёт весь `.env` в контейнер через `env_file` (нужен Compose ≥ 2.24). Имя с дефисом (`fast-auth`) пишется как есть. |
| `STATUS_SERVER_NAME`, `STATUS_HTTPS_PORT`, `STATUS_CERTS_DIR` | Имя страницы, порт на хосте и каталог сертификата. |

Публичные запросы ничего не настраивают. В API и на странице не выводятся адреса целей, трассировки и тексты ошибок проверок; подробности пишутся только в журнал контейнера, без секретов.

## Как считается статус

- Проверка всех сервисов сразу после запуска и затем каждые 30 секунд; циклы не перекрываются, проверки идут параллельно (до 4).
- Таймаут 5 секунд на проверку. Редиректы не выполняются, тело ответа ограничено 4 КБ.
- `healthy` → доступен; `degraded` → доступен, отмечается отдельно; `down` (503), недоступность DNS, TLS, соединения, таймаут или непарсящийся ответ → недоступен.
- `starting` — прогрев, не больше 2 минут от `checkedAtUtc`; дольше — недоступен.
- Снимок старше 90 секунд у ответа 200 со статусом `healthy`, `degraded` или `unknown` — «устарел». 503 `down` и неверная пара статус/код (например, `healthy` с 503) — недоступен при любом `checkedAtUtc`. `unknown` — «неизвестно». Устарел и неизвестно не входят ни в числитель, ни в знаменатель доступности.
- Доступность = (доступен) / (доступен + недоступен). Покрытие = наблюдённые слоты / ожидаемые слоты от начала мониторинга; пропуски влияют только на покрытие.
- История хранится 90 календарных дней по UTC в SQLite (`/var/lib/barkfluff-status/status.db`, том `status-data`). Старше 90 дней удаляется. История разных endpoint (например, при смене `Status__ServiceOrigins`) хранится раздельно.

## Эксплуатация

- Перезапуск: `docker compose restart status`. История сохраняется, повторный запуск в том же слоте не удваивает счётчики.
- Резервная копия: `docker run --rm -v barkfluff-status_status-data:/data -v "$PWD":/backup alpine tar czf /backup/status-data.tgz -C /data .`
- Удалять том `status-data` без необходимости не стоит: это потеря истории за 90 дней.
- Журнал: `docker compose logs status` — переходы состояний сервисов и ошибки запуска (например, неверный `NODE_DOMAIN` или `FEDERATION_SPKI_PIN`).

## Что проверяет CI

Для PR, затрагивающих `Backend/BarkFluff.Status`, `Tests/BarkFluff.Status.Tests` и `Docker/nightly/status`: модульные и интеграционные тесты, сборка образа, проверка compose-файла и HTTPS-smoke в `smoke/`. Smoke поднимает тестовую ноду из mock-сервисов с тестовым CA за TLS-фронтом (gRPC-путь через `grpc_pass` с явным `Host`, HTTP-путь через `proxy_pass`, только маршрут `/health/ready`, ответы 503, редирект, пустой ответ, прогрев, устаревший снимок). Сначала поднимаются mock и фронт, и smoke ждёт реальные HTTPS-коды всех 13 фикстур; только потом запускаются статус и nginx. Затем проверяются страница, состояния, сохранность истории после перезапуска и отсутствие адресов целей в ответе.
