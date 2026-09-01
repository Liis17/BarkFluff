# Первичная настройка BarkFluff через Settings

`BarkFluff.Setup` — отдельная web-консоль для чистого нового сервера. Она запускается
с `BarkFluff.Settings` и PostgreSQL отдельным Compose, а после завершения основной
стек использует ту же папку `data/postgres`.

## 1. Подготовить сервер

Выберите окружение (`dev`, `nightly` или `master`) и перейдите в его каталог:

```bash
cd Docker/dev/barkfluff
docker network create barkfluff-network 2>/dev/null || true
mkdir -p secrets data/postgres
openssl rand -base64 32 > secrets/setup_token
chmod 600 secrets/setup_token
```

В `.env` задайте как минимум `POSTGRES_PASSWORD`,
`RABBITMQ_DEFAULT_USER` и `RABBITMQ_DEFAULT_PASS`. Для публичного доступа через
host-Nginx задайте `SETUP_PUBLIC_ORIGIN=https://setup.example.com`.

Заранее подготовьте непредсказуемые значения S3/MinIO и LiveKit. Их нужно будет
ввести в Setup UI и затем использовать в `MINIO_ROOT_USER`/
`MINIO_ROOT_PASSWORD` и `livekit/livekit.yaml` основного стека. Шаблон LiveKit
намеренно содержит пустые `keys`, поэтому без этой синхронизации звонки не стартуют.

## 2. Запустить bootstrap

```bash
docker compose -f docker-compose.setup.yml up -d
docker compose -f docker-compose.setup.yml logs -f settings
```

`postgres-bootstrap` создаёт только отсутствующие базы `settings`, `identity`, `users`,
`messages`, `files`, `onliner`, `calls`, `bots`, `federation` и `developers`. Операция
идемпотентна: существующие базы не удаляются и не пересоздаются. После этого
Initializer применяет миграции к `settings` и добавляет только отсутствующие ключи.

## 3. Подключить внешний Nginx

Скопируйте `Docker/setup/settings-setup.nginx.conf` на уже работающий Nginx,
замените hostname и пути сертификатов, проверьте конфигурацию и сделайте reload:

```bash
nginx -t && systemctl reload nginx
```

Откройте `https://setup.example.com`, введите содержимое `secrets/setup_token` и
заполняйте группы последовательно. Пароль SMTP и другие секреты после сохранения
не отображаются. При включении федерации становятся обязательными её домен,
HTTPS-endpoint, SPKI-отпечаток и окна подписи.

## 4. Завершить и переключить стек

После «Завершить настройку» сервис записывает `SetupState` и блокирует повторное
изменение через setup API. Остановите bootstrap-сервисы, сохранив bind-данные:

```bash
docker compose -f docker-compose.setup.yml down
docker compose -f docker-compose.yml up -d
```

Перед запуском основного Compose проверьте host-mounted data directory. Не удаляйте
его и не используйте `docker compose down -v`: bootstrap при каждом запуске создаёт
только отсутствующие базы, а существующие оставляет без изменений. Потребители уже
настроены на Settings, поэтому отдельный cutover-override не нужен.

После переключения удалите setup secret и не оставляйте порт `7032` доступным
напрямую из интернета. Дальнейшие исправления выполняются через AdminPanel.

Bootstrap рассчитан на чистую установку. Для существующего стека сначала сделайте
резервную копию и подготовьте отдельную миграцию: старые строки Configuration и
старую базу автоматически не импортируются и не удаляются.
