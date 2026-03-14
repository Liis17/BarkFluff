# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Описание

Микросервис `Beacon` (порт 7002) — точка входа для клиентов BarkFluff. Собирает адреса всех бизнес-сервисов из Configuration service и отдаёт клиентам единый `GetServerInfoResponse` с эндпоинтами, цветовой схемой и метаданными сервера. Также периодически (каждые 5 минут) регистрирует себя в Navigator.

## Сборка и запуск

```bash
dotnet build Backend/BarkFluff.Beacon/BarkFluff.Beacon.csproj
```

Порт задаётся через `BEACON_PORT` / `RunSettings__Port` env-переменные, либо из конфигурации Configuration service.

## Архитектура

Сервис не имеет собственной БД. Вся логика сводится к двум операциям:

1. **GetServerInfo** (gRPC endpoint) — запрашивает конфигурации Identity, Users, Files, Messages, Updates, Onliner из Configuration service и собирает ответ с внешними эндпоинтами (ExternalEndpoint:Host, фолбэк на RunSettings:Host).
2. **ServerRegistrationService** (BackgroundService) — каждые 5 минут отправляет `RegisterServerRequest` в Navigator с метаданными сервера.

CQRS через MediatR: `GetServerInfoCommand` → `GetServerInfoCommandHandler`.

## Зависимости (gRPC-клиенты)

- **Configuration** (`ConfigurationApi.ConfigurationApiClient`) — получение конфигураций сервисов по `ServiceId`
- **Navigator** (`NavigatorApi.NavigatorApiClient`) — регистрация сервера в глобальном реестре

## Конфигурация

Настройки загружаются из Configuration service (`builder.LoadConfiguration(ServiceId.Beacon)`):

- `ServerProps` → `ServerPropsSettings` (Name, Description, PublicName, Location)
- `ServerColor` → `ServerColorSettings` (Lite, Main, Hard — hex-цвета)
- `NavigatorUrl` — адрес Navigator service
- `ConfigurationServiceAddr` — адрес Configuration service
- `ExternalEndpoint:Host` — внешний адрес Beacon (через nginx), фолбэк на `RunSettings:Host`

## Proto-файлы

- `beacon_api.proto` — Server (реализация gRPC сервиса)
- `navigator_api.proto` — Client (клиент для регистрации)
