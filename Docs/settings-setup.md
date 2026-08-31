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

В `.env` задайте как минимум `POSTGRES_PASSWORD`. Для публичного доступа через
host-Nginx задайте `SETUP_PUBLIC_ORIGIN=https://setup.example.com`.

## 2. Запустить bootstrap

```bash
docker compose -f docker-compose.setup.yml up -d
docker compose -f docker-compose.setup.yml logs -f settings
```

Initializer создаёт только базу `settings`, применяет миграции и заполняет безопасные
значения. Базы Identity/Users/Files и прочих бизнес-сервисов на этом шаге не создаются.

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

Основной Compose создаст бизнес-базы при старте соответствующих сервисов: потребители
уже настроены на Settings, поэтому отдельный cutover-override не нужен.

После переключения удалите setup secret и не оставляйте порт `7032` доступным
напрямую из интернета. Дальнейшие исправления выполняются через AdminPanel.
