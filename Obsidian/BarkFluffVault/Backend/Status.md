# Статус ноды

Parent: [[Architecture]]. Status — отдельный ASP.NET-сервис с одной публичной страницей: доступность публичных компонентов ноды за 90 суток. Он не входит в compose ноды, не использует Settings, Beacon, XAuth, RabbitMQ или gRPC; общий код берёт только из `Backend/BarkFluff.Settings/Catalog/NodeSubdomains.cs` (список хостов).

## Точки входа и карта

- `Program.cs`: конфигурация валидируется при старте; хранилище, зонд, монитор и отчёт регистрируются как singleton; статические файлы и `GET /api/status`.
- `Configuration/StatusOptions.cs`: `NODE_DOMAIN`, `FEDERATION_SPKI_PIN`, `Status:ServiceOrigins`, `Status:DisabledServices`, `Status:DatabasePath`.
- `Catalog/PublicServices.cs`: 13 публичных компонентов в порядке страницы; ключ совпадает с `NodeSubdomains`.
- `Probing/ReadinessProbe.cs`: HTTPS GET `/health/ready`, HTTP/2 через ALPN (`RequestVersionOrLower`), без редиректов, таймаут 5 с, тело до 4 КБ.
- `Probing/ReadinessClassifier.cs`: чистая классификация ответа в состояние проверки.
- `Probing/FederationPin.cs`: SPKI-пин для Federation по тому же контракту, что `S2SChannelFactory`.
- `Storage/StatusStore.cs`: SQLite (WAL), батч слотов, дневные агрегаты, последнее состояние, очистка.
- `Reporting/StatusReport.cs`: `StatusReportBuilder` собирает публичный отчёт из агрегатов.
- `Monitoring/StatusMonitorService.cs`: цикл опроса (сразу после старта, затем каждые 30 секунд), запись батча, пересборка отчёта.
- `wwwroot/`: одна страница без внешних ресурсов (CSP `'self'`), цвет всегда дублируется текстом и значком.

## Контракт API

`GET /api/status` — без входных параметров, `Cache-Control: no-cache`. Ответ: общее состояние (`operational`, `degraded`, `partial_outage`, `major_outage`, `unknown`), время формирования, окно (90 дней), интервал (30 секунд) и по каждому сервису: `key`, `name`, `state`, `lastCheckUtc`, `uptimePercent`, `coveragePercent`, `degradedSlots` и `days` (90 дат UTC, у каждой состояние и число наблюдённых/ожидаемых слотов). Адреса целей, трассировки, тексты ошибок и внутренние `checks` ноды в ответ не попадают. До первого отчёта сервис отвечает 503 `not_ready`.

## Классификация проверки

Контракт ответа ноды — `ReadinessSnapshot` (см. [[Backend/GrpcServer]]): 200 для `healthy`, `degraded`, `starting`, `unknown`; 503 для `down`. Читаются только `status` и `checkedAtUtc`.

| Ответ | Состояние | В доступность |
|---|---|---|
| 200 `healthy`, свежий снимок | `Up` | да, доступен |
| 200 `degraded`, свежий | `Degraded` | да, доступен, отмечается отдельно |
| 503 `down`, любой `checkedAtUtc` | `Down` | да, недоступен |
| 200 `starting` не старше 2 минут от `checkedAtUtc` | `Starting` | нет |
| `starting` старше 2 минут | `Down` | да, недоступен |
| 200 `unknown` свежий | `Unknown` | нет |
| 200 `healthy`/`degraded`/`unknown`, снимок старше 90 секунд или из будущего дальше 30 секунд | `Stale` | нет |
| Неверная пара статус/код (при любом `checkedAtUtc`), не JSON, нет `status`/`checkedAtUtc`, тело больше 4 КБ, редирект | `Down` | да, недоступен |
| DNS, TLS, соединение, таймаут | `Down` | да, недоступен |

Доступность = `(Up + Degraded) / (Up + Degraded + Down)`. Покрытие = наблюдённые слоты / ожидаемые слоты от первого слота мониторинга; ожидаемые слоты — все 30-секундные отметки UTC до текущей, пропуски влияют только на покрытие.

## Хранение

- `probe_results`: первичный ключ `(endpoint, service, slot_utc)`, `WITHOUT ROWID`. Повтор слота игнорируется, поэтому перезапуск внутри слота не удваивает данные.
- `daily_counts`: счётчики по дням UTC и состояниям, обновляются в той же транзакции и только если строка слота добавлена.
- `latest_results`: последний слот по каждому endpoint и сервису; обновляется только новее текущего.
- `endpoint` — origin (схема, хост, порт). Альтернативная нода хранится отдельно, пока не удалена очисткой.
- Очистка раз в час: слоты и дни раньше 90 календарных дней. Отчёт строится из агрегатов после каждого цикла, а не на каждый запрос.

## Конфигурация

| Источник | Значение |
|---|---|
| `NODE_DOMAIN` | Обязательна: DNS-имя без схемы и порта. Хосты `<сервис>.<NODE_DOMAIN>`. |
| `Status__ServiceOrigins__<сервис>` | Переопределение HTTPS origin одного сервиса (без пути, query и учётных данных). В compose задаётся в `.env`: контейнер получает весь `.env` через `env_file` (Compose ≥ 2.24). |
| `Status__DisabledServices` | Через запятую: намеренно выключенные компоненты, исключаются из общего состояния. |
| `FEDERATION_SPKI_PIN` | Base64(SHA256(SubjectPublicKeyInfo)), 32 байта. Заменяет проверку цепочки только для `federation`; несовпадение — недоступен. |
| `Status__DatabasePath` | Путь к SQLite, по умолчанию `data/status.db`; в образе `/var/lib/barkfluff-status/status.db`. |

## Развёртывание и CI

- Отдельный compose: `Docker/nightly/status/` (статус + TLS-фронт Nginx, сертификат кладёт оператор, том `status-data`; фронт резолвит `status` через Docker DNS `127.0.0.11` с `valid=10s`, поэтому пересоздание контейнера статуса не требует перезапуска nginx). Инструкция в `Docker/nightly/status/README.md`.
- Образ `barkfluff-status` публикуется через `ci_plan.py` (`SERVICES`) как остальные backend-образы, с `global.json` и `.dockerignore` в исходных путях; Lognara-слой не используется.
- PR в `nightly`: suite `status` выбирается по путям `Backend/BarkFluff.Status/**`, `Tests/BarkFluff.Status.Tests/**`, `Docker/nightly/status/**` и общему `NodeSubdomains.cs`; job `status-docker` собирает образ локально и запускает `Docker/nightly/status/smoke/smoke.sh`. Результат входит в `Tests passed`.
- Smoke: тестовый CA, mock-нода за TLS-фронтом (`grpc_pass` по HTTP/2 с явным `Host` и `proxy_pass` с `Host`, только точный маршрут `/health/ready`). Сначала поднимаются mock и фронт, и smoke ждёт реальные HTTPS-коды всех 13 фикстур (onliner 503 идёт через `grpc_pass`); только потом запускаются статус и nginx. Далее проверяется состояние каждого сервиса, отсутствие адресов в ответе и сохранность истории после `force-recreate` контейнера `status` (том `status-data` тот же, фронт переподключается через retry).

## Ограничения

- `SSL_CERT_FILE` в smoke должен подхватываться .NET на Linux; если нет, тесты CI покажут это, а не локальный запуск.
- Окно прогрева (`starting`) отсчитывается от `checkedAtUtc` снимка, не от времени первого наблюдения; после перезапуска статус сервиса не «запоминается» отдельно.
- Пин Federation заменяет проверку цепочки только для `federation` и только когда задан; остальные endpoint проверяются обычно.
- Очистка сканирует `probe_results` по `slot_utc` без отдельного индекса; раз в час это приемлемо для 13 сервисов, но при росте числа сервисов стоит добавить индекс.
