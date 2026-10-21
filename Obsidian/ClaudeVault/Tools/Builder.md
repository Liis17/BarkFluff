# BarkFluff Builder

`Tools/BarkFluff.Builder/` — Windows WPF-приложение на `net10.0-windows` для генерации и повторного открытия конфигурации self-hosted ноды. `BuilderModel` хранит параметры; `BuilderModelValidator` проверяет их; `BuilderConfigLoader` загружает конфигурацию из папки Builder.

## Источники и защита от дрейфа

- `ComposeGenerator` генерирует Compose; guard-тесты сравнивают его сервисы, environment, mounts и dependencies с `Docker/nightly/barkfluff/docker-compose.yml` и `docker-compose.setup.yml`. Nginx guard-тесты сравнивают маршруты `NginxGenerator` с `Docker/nightly/nginx/`.
- Версия и контрольные суммы Lognara agent, встроенные в `ComposeGenerator`, проверяются относительно `Docker/Lognara/agent/install-agent.sh` и `Docker/Lognara/agent/SHA256SUMS`.
- `Backend/BarkFluff.Settings/Catalog/NodeSubdomains.cs` подключён в Builder как исходный файл и используется для доменов сайтов nginx. Источники публичных URL и заполнение defaults описаны в [[Backend/Settings]].
- `Tests/BarkFluff.Builder.Tests/NginxAndTemplateGuardTests.cs` проверяет дрейф compose и nginx; `BuilderCoreTests` — согласованность Lognara agent; `BuilderConfigLoaderTests` — round-trip конфигурации. Подробности CI — в [[Testing]] и `.github/workflows/build-builder.yml`.

## Выходные файлы и владение

`BuilderOutputWriter.Write` записывает файлы из своего списка `OwnedFiles` и при повторной генерации удаляет устаревшие файлы только из этого списка.

- Всегда создаются `.env`, `docker-compose.yml`, `docker-compose.setup.yml`, `secrets/setup_token`, `livekit/livekit.yaml` и `NEXT_STEPS.md`. В `.env` находятся параметры сервисов и секция `BUILDER_*` для повторного открытия конфигурации.
- `NginxGenerator.BuildFiles` добавляет `nginx/sites/*.conf`; `nginx/nginx.conf` создаётся для режима Compose, а `nginx/sites/00-real-ip.conf` — при включённом Cloudflare. В режиме Compose исходные TLS-файлы копируются как `certs/barkfluff-crt.pem` и `certs/barkfluff-key.pem`.
- Для включённых Lognara-агентов создаётся подмножество `lognara/*.Dockerfile`.
- Builder не владеет и не удаляет `data/`, `backups/` и `admindata/`; это каталоги постоянных данных, на которые ссылаются Compose-файлы.

Оба Compose-файла имеют проект `barkfluff` и имена контейнеров `barkfluff-<service>`. Они подключаются к заранее созданной внешней Docker-сети, имя которой задаётся через `.env`.

## Nginx

| `BuilderModel.NginxMode` | Сгенерированные файлы и upstream |
|---|---|
| `None` | Nginx-файлы не создаются; ingress настраивается отдельно. |
| `Compose` | Полная конфигурация и сайты в `nginx/`; upstream доступны через Docker DNS. Исходные сертификат и ключ копируются в `certs/`. |
| `ExternalContainer` | Сайты для `conf.d` внешнего контейнера; upstream через Docker DNS, пути сертификата и ключа задаются внутри этого контейнера. |
| `Host` | Сайты для nginx на хосте; upstream — `127.0.0.1`, порты backend привязываются к loopback, пути сертификата и ключа задаются на хосте. |

При `BehindCloudflare` `NginxGenerator` создаёт `nginx/sites/00-real-ip.conf` со статическим списком `CloudflareCidrs`. Список IPv4/IPv6 CIDR нужно обновлять при изменении диапазонов Cloudflare.

## Setup и основной стек

Сначала создайте указанную в `.env` Docker-сеть (подставьте её значение из `DOCKER_NETWORK_NAME`). Затем выполните bootstrap, завершите Setup с токеном из `secrets/setup_token`, остановите bootstrap Compose без `-v` и запустите основной стек:

```sh
docker network create "<DOCKER_NETWORK_NAME>"
docker compose -f docker-compose.setup.yml up -d
# Откройте адрес Setup из NEXT_STEPS.md и введите токен из secrets/setup_token.
docker compose -f docker-compose.setup.yml down
docker compose up -d --remove-orphans
```

Если включён Lognara, сгенерированный `NEXT_STEPS.md` добавляет `--build` к командам `up`. При MinIO начальные S3 credentials берутся из `.env`; без MinIO в Setup вводятся credentials внешнего S3.

## Lognara

Lognara Relay общий для ноды и использует именованный том `barkfluff-lognara-relay-spool`. Для каждого включённого агента получите relay token в Core и вручную внесите его в соответствующую переменную `*_LOGNARA_RELAY_TOKEN` файла `.env`. Адрес Core и credentials relay также задаются в `.env`.

## Ограничения интерфейса и каналов

- Не подключайте Builder-стек к Docker-сети старого nightly-стека: Compose DNS имена сервисов совпадут.
- При выборе каналов Dev или Release необходимые изменения Settings и AdminPanel должны быть продвинуты в соответствующие образы канала; детали defaults Settings и разрешения контейнеров AdminPanel описаны в [[Backend/Settings]] и [[Backend/AdminPanel]].
- Кнопка «Открыть» загружает конфигурацию только из выбранной папки Builder с `.env` и compose-файлом; произвольный файл не открывается. Отдельная ссылка запускает `NEXT_STEPS.md`.

## Проверки и публикация

`Tests/BarkFluff.Builder.Tests/` — кроссплатформенный проект `net10.0` с проверками модели, генераторов, валидации, записи файлов и round-trip. `BuilderSampleOutputTests` создаёт примеры конфигураций для CI. `.github/scripts/pr_tests.py` относит `Tools/` и `Tests/BarkFluff.Builder.Tests/` к backend-набору.

`.github/workflows/build-builder.yml` запускает тесты и публикует Windows x64 ZIP в self-contained и framework-dependent вариантах. На Ubuntu workflow проверяет `docker compose config`, `nginx -t` и bootstrap PostgreSQL на новой и повторно используемой БД. Только push в `master` создаёт release `builder-<run_number>-<run_attempt>` с `latest=false`.
