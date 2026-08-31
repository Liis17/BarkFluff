# Docker deployment

В BarkFluff первичная настройка выполняется сервисом `BarkFluff.Setup`, а хранение
параметров — сервисом `BarkFluff.Settings`. Устаревший сервис Configuration больше
не входит в стек.

## Первичный запуск

Bootstrap-compose поднимает только `setup`, `settings` и PostgreSQL. Это позволяет
заполнить обязательные значения до запуска остальных сервисов:

```bash
docker network create barkfluff-network
mkdir -p secrets data/postgres
openssl rand -base64 32 > secrets/setup_token
chmod 600 secrets/setup_token

docker compose -f Docker/nightly/barkfluff/docker-compose.setup.yml up -d
```

Откройте `http://127.0.0.1:7032` (или адрес из `SETUP_PORT`) и введите содержимое
`secrets/setup_token`. Форма последовательно показывает группы Settings, сохраняет
их через внутренний gRPC API и после завершения блокируется. Для публичного доступа
используйте `Docker/setup/settings-setup.nginx.conf` с TLS.

После завершения настройки остановите bootstrap-compose и запустите основной стек:

```bash
docker compose -f Docker/nightly/barkfluff/docker-compose.setup.yml down
docker compose -f Docker/nightly/barkfluff/docker-compose.yml up -d
```

Основной compose содержит Settings, PostgreSQL и остальные backend-сервисы. Все
сервисы получают адрес Settings через `SETTINGS_SERVICE_URL`; значение
`CONFIGURATION_SERVICE_URL` принимается только как временный compatibility alias.

## PostgreSQL

Settings использует базу `settings` и учётные данные `POSTGRES_USER` /
`POSTGRES_PASSWORD`. При первом старте Settings создаёт собственную базу и применяет
миграции. Остальные базы сервисов создаются их собственными миграциями.

## Проверка

```bash
docker compose -f Docker/nightly/barkfluff/docker-compose.setup.yml ps
curl http://127.0.0.1:7032/health/live
docker compose -f Docker/nightly/barkfluff/docker-compose.yml ps
```

Полное описание полей, порядка заполнения и блокировки находится в
[`Docs/settings-setup.md`](../Docs/settings-setup.md). Список портов — в
[`Backend/PORTS_CONFIGURATION.md`](PORTS_CONFIGURATION.md).
