# BarkFluff.Navigator

Управляет реестром доступных серверов BarkFluff. Порт: **7010**.
Публичный эндпоинт: `navigator.barkfluff.com:443 (plaintext HTTP/2).

Расположение: `Backend/BarkFluff.Navigator/`

## Сборка

```bash
dotnet build BarkFluff.Navigator.csproj
NAVIGATOR_PORT=7010 dotnet run
```

## Архитектура

Два gRPC-метода (`navigator_api.proto`):
- `ListServers` — список активных серверов (без авторизации)
- `RegisterServer` — регистрация сервера; `AddedBy` = UserId если есть JWT, иначе `"Anonymous"`

Поток: `NavigatorApiService` → MediatR → `ListServersQueryHandler` / `RegisterServerCommandHandler` → `ServersStorage`

## ServersStorage

**In-memory** (не PostgreSQL, несмотря на наличие пакета). Миграций нет, БД не используется.

- Ключ сервера: `"{Name}:{BeaconHost}:{BeaconPort}"`
- Хранение: два `ConcurrentDictionary` как поля класса (`_servers` + `_lastRegistrationTimes`)
- Сервер активен если `lastSeen` не старше `ServerRegistration:ActivePeriodMinutes` (default 10 мин)
- Throttling: повторная регистрация не чаще `ServerRegistration:ThrottleMinutes` (default 2 мин)
- Очистка throttle-записей: ленивая, при каждой `RegisterServer` удаляются записи старше throttle-периода
- `GetServers()` — синхронный метод (возвращает `List<ServerInfo>`)

## Валидация при регистрации

`RegisterServerCommandHandler` проверяет:
- `BeaconHost` — не пустой, длина ≤ 253, формат hostname (RFC 1123) или IP
- `BeaconPort` — 1..65535
- `Name` — не пустой, длина ≤ 64
- `Description` — не пустой, длина ≤ 512
- `ServerPublicName` — не пустой, длина ≤ 64
- `Location` — длина ≤ 128
- HEX-цвета (`ColorLiteHex`/`ColorMainHex`/`ColorHardHex`) — формат `^#?[0-9A-Fa-f]{6}$` если не пусто

Исключения: `BeaconHostEmptyException`, `InvalidBeaconHostException`, `NameEmptyException`, `InvalidHexColorException` — наследники `BaseGrpcException`. Длины — через `ArgumentException`.

## Авторизация

`UseXAuth()` подключён, но методы без `[Authorize]` — оба публичны. `UserContext` используется в `RegisterServer` для записи `AddedBy`.

## Proto

- `navigator_api.proto` — Server
- `beacon_api.proto` — Client

## Добавление нового поля в ServerInfo

1. Добавить поле в `Domain/ServerInfo.cs`
2. Добавить поле в `navigator_api.proto`
3. Обновить маппинг в `NavigatorApiService.cs` (request → domain)
4. Обновить маппинг в `ListServersQueryHandler.cs` (domain → response)
