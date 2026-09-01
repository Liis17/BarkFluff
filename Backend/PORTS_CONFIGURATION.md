# Порты и настройки backend

Документ описывает актуальную схему портов BarkFluff. Первичная настройка выполняется
через `BarkFluff.Setup`, а централизованное хранение параметров — через `Settings`.

## Порты сервисов

| Сервис | ServiceId | HTTP/2 или gRPC | HTTP/1.1 | Переменная |
|--------|-----------|----------------|----------|------------|
| Settings | 0 | 7003 | — | `SETTINGS_PORT` |
| Setup UI | — | — | 7032 | `SETUP_PORT` |
| Identity | 1 | 7000 | — | `IDENTITY_PORT` |
| Users | 2 | 7001 | — | `USERS_PORT` |
| Beacon | 3 | 7002 | — | Settings: `RunSettings:Port` |
| Notification | 4 | 7004 | — | `NOTIFICATION_PORT` |
| Files | 5 | 7005 | 7006 | `FILES_PORT`, `FILES_HTTP1PORT` |
| Messages | 6 | 7007 | — | `MESSAGES_PORT` |
| FastAuth | 7 | 7008 | — | `FASTAUTH_PORT` |
| Onliner | 9 | 7009 | — | `ONLINER_PORT` |
| Navigator | — | 7010 | — | `NAVIGATOR_PORT` |
| Updates | 8 | 7015 | — | `UPDATES_PORT` |
| Developers | 12 | 7020 | 7021 | Settings: `RunSettings:Port`, `RunSettings:Http1Port` |

## Инфраструктура

| Сервис | Порт(ы) | Переменная |
|--------|---------|------------|
| PostgreSQL | 5432 | `POSTGRES_PORT` |
| RabbitMQ | 5672, 15672 | `RABBITMQ_1PORT`, `RABBITMQ_2PORT` |
| Redis | 6379 | `REDIS_PORT` |
| MinIO | 9000, 9001 | `MINIO_PORT`, `MINIO_WEBPORT` |

## Settings и Setup

В основном compose сервисы обращаются к Settings по внутреннему адресу:

```yaml
environment:
  SETTINGS_SERVICE_URL: http://settings:7003
```

Для локального запуска можно указать `SETTINGS_SERVICE_URL=http://localhost:7003`.
Settings получает подключение к PostgreSQL через `SETTINGS_HOST`,
`SETTINGS_DBPORT`, `SETTINGS_DATABASE`, `SETTINGS_USERNAME` и `SETTINGS_PASSWORD`.
`SETTINGS_ADMIN_DATABASE` используется для создания базы `settings` при первом старте.

Bootstrap-compose (`Docker/{dev,nightly,master}/barkfluff/docker-compose.setup.yml`)
включает только PostgreSQL, Settings в режиме настройки и Setup UI. Setup UI
слушает `SETUP_PORT` (по умолчанию 7032), принимает секрет из
`SETUP_SECRET_FILE`/`SETUP_TOKEN` и проксируется наружу через nginx.

После завершения первичной настройки Settings запускается с
`SETTINGS_SETUP_MODE=false`; остальные сервисы читают сохранённые значения при
старте. Подробный порядок и описание полей: [`Docs/settings-setup.md`](../Docs/settings-setup.md).

Переменная `CONFIGURATION_SERVICE_URL` временно поддерживается как compatibility
alias для старых образов и удаляется после их обновления. Новые deployment-файлы
должны использовать только `SETTINGS_SERVICE_URL`.

## Локальная проверка

```bash
dotnet run --project Backend/BarkFluff.Settings
curl http://localhost:7003/health/live
```

Сервис Settings применяет миграции и проверяет обязательные значения конфигурации.
Пустые ручные поля блокируют готовность и отображаются в Setup UI.
