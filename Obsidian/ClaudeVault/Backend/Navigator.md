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

> Этап 0.4 rearch: `ServerInfo` получил 5 полей федерации (`server_name`, `federation_endpoint`, `signing_keys`, `tls_spki_sha256`, `federation_protocol_versions`) + новый RPC `GetServerByName` (Фаза 1) — пока только контракт, `NavigatorApiService`/`ServersStorage` их не заполняют/не реализуют.

Поток: `NavigatorApiService` → MediatR → `ListServersQueryHandler` / `RegisterServerCommandHandler` → `ServersStorage`

## ServersStorage

**In-memory** (не PostgreSQL, несмотря на наличие пакета). Миграций нет, БД не используется.

- Ключ сервера: `"{Name}:{BeaconHost}:{BeaconPort}"`
- Хранение: два `ConcurrentDictionary` как поля класса (`_servers` + `_lastRegistrationTimes`)
- Сервер активен если `lastSeen` не старше `ServerRegistration:ActivePeriodMinutes` (default 10 мин)
- Throttling: повторная регистрация не чаще `ServerRegistration:ThrottleMinutes` (default 2 мин)
- Очистка throttle-записей: ленивая, при каждой `RegisterServer` удаляются записи старше throttle-периода
- `GetServers()` — синхронный метод (возвращает `List<ServerInfo>`)

## Domain/ServerInfo

Поля: `Id` (long, ключ), `CreatedAt` (DateTime), `AddedBy` (string — UserId или "Anonymous"), `Name`, `BeaconHost`, `BeaconPort`, `Description`, `ServerPublicName`, `Location`, `ColorLiteHex`, `ColorMainHex`, `ColorHardHex`. **`AccountsCount` в доменной модели нет** — это поле существует только в proto-ответе (см. ниже) и всегда возвращается как `0`.

В proto `ServerInfo` адрес маяка передаётся как `beacon_uri: ServiceEndpoint` (а не отдельные host/port), плюс поле `accounts_count` (жёстко `0` — не подсчитывается сервером, вычислять при необходимости на клиенте/из другого источника).

## Валидация при регистрации

`RegisterServerCommandHandler` проверяет:
- `BeaconHost` — не пустой, длина ≤ 2048, валидируется через `Uri.TryCreate` + `Uri.CheckHostName`
- `BeaconPort` — 1..65535
- `Name` — не пустой, длина ≤ 64
- `Description` — обязателен, длина ≤ 512
- `ServerPublicName` — обязателен, длина ≤ 64
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
