# BarkFluff.Beacon

> 📁 Подробная карта файлов: [[Backend/Beacon-ProjectMap]]

Точка входа для клиентов BarkFluff. Порт: **7002**.
Собирает адреса всех бизнес-сервисов из Configuration service и отдаёт клиентам единый `GetServerInfoResponse`. Также периодически (каждые 5 минут) регистрирует себя в Navigator.

Расположение: `Backend/BarkFluff.Beacon/`

## Сборка

```bash
dotnet build Backend/BarkFluff.Beacon/BarkFluff.Beacon.csproj
```

Порт задаётся через `BEACON_PORT` / `RunSettings__Port` env-переменные.

## Архитектура

Сервис **не имеет БД**. Вся логика — две операции:

1. **GetServerInfo** (gRPC endpoint) — запрашивает конфигурации Identity, Users, Files, Messages, Updates, Onliner, FastAuth из Configuration service и собирает ответ с внешними эндпоинтами (`ExternalEndpoint:Host`, фолбэк на `RunSettings:Host`).
2. **ServerRegistrationService** (BackgroundService) — каждые 5 минут отправляет `RegisterServerRequest` в Navigator.

CQRS через MediatR: `GetServerInfoCommand` → `GetServerInfoCommandHandler`.

## Зависимости (gRPC-клиенты)

- **Configuration** (`ConfigurationApi.ConfigurationApiClient`) — получение конфигураций по `ServiceId`
- **Navigator** (`NavigatorApi.NavigatorApiClient`) — регистрация сервера

## Конфигурация

Загружается из Configuration service (`LoadConfiguration(ServiceId.Beacon)`):
- `ServerProps` → `ServerPropsSettings` (Name, Description, PublicName, Location)
- `ServerColor` → `ServerColorSettings` (Lite, Main, Hard — hex-цвета)
- `NavigatorUrl` — адрес Navigator service (default: `http://localhost:7010`)
- `ConfigurationServiceAddr` — адрес Configuration service (default: `http://localhost:7003`)
- `ExternalEndpoint:Host` — внешний адрес через nginx (порт 443, TLS)

## Метрики

> 📊 Полный реестр метрик: [[Backend/Beacon-Metrics]]

Метрики собираются через `MetricsCollector` (из `BarkFluff.GrpcServer`) и каждые 5 секунд публикуются в Seq фоновым `MetricsReporterService` как структурированный лог `ServiceMetrics {@Metrics}`. AdminPanel читает эти логи и агрегирует их в LiteDB-кеш (`HourlyServiceMetrics`).

## Proto

- `beacon_api.proto` — Server
- `navigator_api.proto` — Client
