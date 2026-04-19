# BarkFluff.Navigator

Управляет реестром доступных серверов BarkFluff. Порт: **7010**.
Публичный эндпоинт: `navigator.barkfluff.com:64646` (plaintext HTTP/2).

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

**In-memory cache** (не PostgreSQL, несмотря на наличие пакета). Миграций нет, БД не используется.

- Ключ сервера: `"{Name}:{BeaconHost}:{BeaconPort}"`
- `ConcurrentDictionary` в `IMemoryCache` с приоритетом `NeverRemove`
- Сервер активен если `lastSeen` не старше `ServerRegistration:ActivePeriodMinutes` (default 10 мин)
- Throttling: повторная регистрация не чаще `ServerRegistration:ThrottleMinutes` (default 2 мин)

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
