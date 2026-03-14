# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Микросервис Navigator

Управляет реестром доступных серверов BarkFluff. Работает на порту `7010`. Публичный эндпоинт: `navigator.barkfluff.com:64646` (plaintext HTTP/2).

## Сборка и запуск

```bash
# Сборка
dotnet build BarkFluff.Navigator.csproj

# Запуск (порт из конфигурации)
dotnet run

# Запуск с переопределением порта
NAVIGATOR_PORT=7010 dotnet run
# или
RunSettings__Port=7010 dotnet run
```

## Архитектура

Сервис реализует два gRPC-метода (`navigator_api.proto`):

- `ListServers` — возвращает список активных серверов (без авторизации)
- `RegisterServer` — регистрирует сервер; `AddedBy` = UserId если есть JWT, иначе `"Anonymous"`

Поток: `NavigatorApiService` (Host) → MediatR → `ListServersQueryHandler` / `RegisterServerCommandHandler` → `ServersStorage`

### ServersStorage (Persistence/ServersStorage.cs)

Единственное хранилище — **in-memory cache** (не PostgreSQL, несмотря на наличие пакета в csproj). Миграций нет, база не используется.

Ключевые аспекты:
- Ключ сервера: `"{Name}:{BeaconHost}:{BeaconPort}"`
- Серверы хранятся в `ConcurrentDictionary` внутри `IMemoryCache` с приоритетом `NeverRemove`
- Сервер считается активным, если `lastSeen` не старше `ServerRegistration:ActivePeriodMinutes` (по умолчанию 10 мин)
- Throttling регистрации: повторная регистрация одного сервера не чаще `ServerRegistration:ThrottleMinutes` (по умолчанию 2 мин)

### Конфигурация

```json
{
  "ServerRegistration": {
    "ActivePeriodMinutes": 10,
    "ThrottleMinutes": 2
  }
}
```

### Proto-зависимости

- `navigator_api.proto` — **Server** (реализует NavigatorApi)
- `beacon_api.proto` — **Client** (используется для обнаружения Beacon-сервисов)

### Авторизация

`UseXAuth()` подключён, но методы не имеют атрибута `[Authorize]` — оба эндпоинта публичны. `UserContext` используется в `RegisterServer` для записи `AddedBy`.

## Добавление нового поля в ServerInfo

1. Добавить поле в `Domain/ServerInfo.cs`
2. Добавить поле в `navigator_api.proto` (ServerInfo message)
3. Обновить маппинг в `NavigatorApiService.cs` (request → domain)
4. Обновить маппинг в `ListServersQueryHandler.cs` (domain → response)
