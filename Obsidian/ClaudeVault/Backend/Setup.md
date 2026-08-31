# BarkFluff.Setup

Отдельная web-консоль для первичного заполнения `Settings` на новом сервере.
Сервис не хранит собственную базу: он использует только in-memory сессии и gRPC
`SettingsSetupApi` к [[Backend/Settings]].

## Контракт и поток

- HTTP-порт по умолчанию: `7032`; в production публикуется только через внешний
  Nginx, Compose привязывает его к `127.0.0.1:7032`.
- Вход: setup-токен из Docker secret (`SETUP_SECRET_FILE`), cookie-сессия с
  `HttpOnly`/`SameSite=Strict`, CSRF-токен в заголовке `X-CSRF-Token`.
- Пять неудачных попыток входа с одного адреса за пять минут блокируют новые
  попытки до окончания окна.
- Внутренний gRPC вызов передаёт токен в `x-settings-setup-token`; режим принимает
  вызовы только при `SETTINGS_SETUP_MODE=true`.
- После `CompleteSetup` запись `SetupState` блокирует изменения через setup API.
  Исправления после этого выполняются только через AdminPanel.

## Группы

UI последовательно показывает группы из каталога `SettingsSetupMetadata`:

1. Сведения о сервере — имя, описание, публичное имя, расположение и цвета Beacon.
2. Почтовая доставка — SMTP host/port, email и пароль отправителя.
3. Публичный адрес медиа — HTTPS-origin Files.
4. Федерация — переключатель и параметры S2S. При выключенной федерации домен,
   endpoint, SPKI и окна подписи не требуются.

Каждая запись имеет тип ввода, объяснение, placeholder и серверный validator.
Чувствительные значения не возвращаются клиенту; UI показывает только признак
`configured`. Все изменения записываются в `SettingsHistory` с `ChangeKind=Setup`.

## Bootstrap Compose

Файлы:

- `Docker/dev/barkfluff/docker-compose.setup.yml`
- `Docker/nightly/barkfluff/docker-compose.setup.yml`
- `Docker/master/barkfluff/docker-compose.setup.yml`

Каждый файл поднимает ровно `setup`, `settings` и `postgres`. Сеть и имена
`settings`/`postgres_barkfluff` совпадают с основным Compose, а bind-путь
`./data/postgres` сохраняет БД для последующего запуска основного стека. На
bootstrap initializer создаёт только БД `settings`; бизнес-сервисы создают свои
БД при собственном запуске. Импорт данных от удалённого источника конфигурации не предусмотрен:
поддерживается только новый чистый сервер.

Перед запуском нужно создать общую сеть и secret-файл:

```bash
docker network create barkfluff-network 2>/dev/null || true
mkdir -p secrets data/postgres
openssl rand -base64 32 > secrets/setup_token
docker compose -f docker-compose.setup.yml up -d
```

После заполнения и нажатия «Завершить настройку» сохранить каталог `data/postgres`,
остановить setup Compose и запустить основной Compose. Secret setup больше не нужен
основным сервисам и должен быть удалён/заменён по правилам эксплуатации.

## Внешний Nginx

Готовый отдельный шаблон для копирования на host-Nginx находится в
`Docker/setup/settings-setup.nginx.conf`. В нём нужно заменить
`setup.example.com` и пути сертификатов, затем выполнить `nginx -t` и reload.
Шаблон проксирует на `127.0.0.1:7032` и передаёт `X-Forwarded-*`, поэтому в
Compose рекомендуется задать `SETUP_PUBLIC_ORIGIN=https://<ваш-host>`.

## Проверка

```bash
dotnet build Backend/BarkFluff.Setup/BarkFluff.Setup.csproj
dotnet test Tests/BarkFluff.Setup.Tests/BarkFluff.Setup.Tests.csproj
dotnet test Tests/BarkFluff.Settings.Tests/BarkFluff.Settings.Tests.csproj
```
