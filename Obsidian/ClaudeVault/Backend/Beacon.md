# BarkFluff.Beacon

> 📁 Подробная карта файлов: [[Backend/Beacon-ProjectMap]]

Точка входа для клиентов BarkFluff. Порт: **7002**.
Собирает адреса всех бизнес-сервисов из Settings service и отдаёт клиентам единый `GetServerInfoResponse`. Также периодически (каждые 5 минут) регистрирует себя в Navigator.

Анонимный liveness endpoint: `GET /ping` → `pong`.

Расположение: `Backend/BarkFluff.Beacon/`

## Сборка

```bash
dotnet build Backend/BarkFluff.Beacon/BarkFluff.Beacon.csproj
```

Порт задаётся через `BEACON_PORT` / `RunSettings__Port` env-переменные.

## Архитектура

Сервис **не имеет БД**. Вся логика — две операции:

1. **GetServerInfo** (gRPC endpoint) — **параллельно** (`Task.WhenAll`) запрашивает параметры Identity, Users, Files, Messages, Updates, Onliner, FastAuth, **Calls**, **Bots**, **Federation** (10 сервисов) из Settings service и собирает ответ с внешними эндпоинтами (`ExternalEndpoint:Host`; если host не задан — сервис помечается `Offline`, иначе `Healthy` + TLS + порт 443). Ответ кешируется в `IMemoryCache` на 5 минут. В `GetServerInfoResponse` отдаёт `public_name`, `location` и `livekit_url` (публичный wss://-адрес LiveKit SFU из секции `LiveKit`/`PublicUrl` параметров `Calls`; отдельно от внутреннего `LiveKit:Url`; пусто, если не задан или не является абсолютным `wss://`). Этап 0.4 rearch: также отдаёт `server_name`/`federation_enabled` (fields 16-17) — читает `Federation:ServerName`/`Federation:Enabled` из Settings; пустая строка/`false`, пока оператор ноды их не задал.

   Поле `files_media_endpoint` (field 18) — отдельный публичный origin файлового HTTP ноды (`ExternalEndpoint:MediaHost` конфигурации [[Backend/Files]], нормализуется до `https://host`). Нужен, чтобы загрузка и скачивание шли мимо CDN с его лимитом на размер файла ([[Backend/Nginx]], `files2.barkfluff.com`); клиент подменяет им **только хост** в ссылках, которые выдал Files. Пусто (значение по умолчанию) — клиент работает по старому адресу, поэтому старые клиенты и ноды без этого адреса не ломаются.
2. **ServerRegistrationService** (BackgroundService) — каждые 5 минут отправляет `RegisterServerRequest` в Navigator. Кроме `web_endpoint` передаёт `files_media_endpoint` (тот же `ExternalEndpoint:MediaHost` из Settings [[Backend/Files]]); недоступная конфигурация регистрацию не ломает — поле уходит пустым.

CQRS через MediatR: `GetServerInfoCommand` → `GetServerInfoCommandHandler`.

## Зависимости (gRPC-клиенты)

- **Settings** (`ConfigurationApi.ConfigurationApiClient`) — получение параметров по `ServiceId` через wire-compatible API
- **Navigator** (`NavigatorApi.NavigatorApiClient`) — регистрация сервера

## Конфигурация

Загружается из Settings service (`LoadConfiguration(ServiceId.Beacon)`):
- `ServerProps` → `ServerPropsSettings` (Name, Description, PublicName, Location)
- `ServerColor` → `ServerColorSettings` (Lite, Main, Hard — hex-цвета)
- `NavigatorUrl` — адрес Navigator service (default: `http://localhost:7010`)
- `SettingsServiceAddr` — адрес Settings service (default: `http://localhost:7003`)
- `ExternalEndpoint:Host` — внешний адрес через nginx (порт 443, TLS)

## Метрики

> 📊 Полный реестр метрик: [[Backend/Beacon-Metrics]]

Метрики собираются через `MetricsCollector` (из `BarkFluff.GrpcServer`) и каждые 5 секунд публикуются в Seq фоновым `MetricsReporterService` как структурированный лог `ServiceMetrics {@Metrics}`. AdminPanel читает эти логи и агрегирует их в LiteDB-кеш (`HourlyServiceMetrics`).

## Proto

- `beacon_api.proto` — Server
- `navigator_api.proto` — Client
