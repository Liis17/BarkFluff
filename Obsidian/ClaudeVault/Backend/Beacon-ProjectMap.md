# BarkFluff.Beacon — Карта проекта

> Детальная карта файлов сервиса. Общее описание: [[Backend/Beacon]]

---

## Структура файлов

### `Program.cs`
Точка входа сервиса. Настраивает DI-контейнер и Kestrel.

- Читает порт из env-переменных `BEACON_PORT` / `RunSettings__Port`, иначе из `RunSettings:Port` (7002)
- Загружает конфигурацию через `LoadConfiguration(ServiceId.Beacon)` из Configuration service
- Регистрирует: MediatR, gRPC reflection, Serilog, метрики (`AddBarkFluffMetrics`)
- Регистрирует gRPC-клиенты: `NavigatorApiClient` (по ключу `NavigatorUrl`) и `ConfigurationApiClient` (по ключу `ConfigurationServiceAddr`)
- Запускает `ServerRegistrationService` как `HostedService`

---

### `Host/BeaconApiService.cs`
gRPC-сервис (реализует `BeaconApi.BeaconApiBase` из `beacon_api.proto`).

- Единственный метод: `GetServerInfo(GetServerInfoRequest)` → `GetServerInfoResponse`
- Инкрементирует метрику `server_info_requests` через `MetricsCollector`
- Делегирует логику в MediatR: отправляет `GetServerInfoCommand`

---

### `Features/GetServerInfo/GetServerInfoCommand.cs`
Пустой MediatR-запрос `IRequest<GetServerInfoResponse>`.  
Маркер команды, не несёт данных (все данные берутся из DI).

---

### `Features/GetServerInfo/GetServerInfoCommandHandler.cs`
Основная бизнес-логика сборки ответа клиенту.

- **Параллельно** (`Task.WhenAll`) запрашивает конфигурации 9 сервисов через `ConfigurationApiClient`:
  `Identity`, `Users`, `Files`, `Messages`, `Updates`, `Onliner`, `FastAuth`, `Calls`, `Bots`
- Ответ кешируется в `IMemoryCache` (`CacheKey`, `CacheTtl` = 5 минут); повторные вызовы отдают кеш
- Для каждого сервиса вызывает `ParseService()`:
  - Ищет ключ `ExternalEndpoint:Host` — внешний адрес через nginx (порт 443, TLS)
  - Если `ExternalEndpoint:Host` не задан → `ServiceStatus.Offline` (Host пустой, порт 0, `TlsEnabled = false`) + LogError; фолбэков на `RunSettings:Host`/`{service}.example.com` **нет**
  - Иначе → `ServiceStatus.Healthy`, `TlsEnabled = true`, порт `443`
- Дополнительно для `Calls` вычитывает публичный `LivekitUrl` из секции `LiveKit`/ключа `PublicUrl`; валидируется как абсолютный `wss://`-URI (иначе пусто + LogError)
- Собирает `GetServerInfoResponse` с полями: `Name`, `Description`, `Color`, `LivekitUrl`, и эндпоинты всех сервисов

---

### `Features/RegisterServer/ServerRegistrationService.cs`
`BackgroundService` — периодическая регистрация Beacon в Navigator.

- Интервал: **5 минут** (`TimeSpan.FromMinutes(5)`)
- Читает `ExternalEndpoint:Host` из конфигурации (фолбэк на `RunSettings:Host`)
- Формирует `ServerInfo` (Name, Description, PublicName, Location, BeaconUri, Color)
- Отправляет `RegisterServerAsync` в `NavigatorApiClient`
- Инкрементирует метрику `navigator_registrations`
- Ошибки логируются и не прерывают цикл

---

### `Configurations/ServerColorSettings.cs`
Settings-класс. Три hex-цвета сервера: `Lite`, `Main`, `Hard`.  
Биндится из секции `ServerColor` конфигурации.

---

### `Configurations/ServerPropsSettings.cs`
Settings-класс. Свойства сервера: `Name`, `Description`, `PublicName`, `Location`.  
Биндится из секции `ServerProps` конфигурации.

---

### `appsettings.json`
Базовая конфигурация:
| Ключ | Значение по умолчанию | Назначение |
|------|-----------------------|------------|
| `RunSettings:Port` | `7002` | Порт Kestrel |
| `NavigatorUrl` | `http://localhost:7010` | Адрес Navigator service |
| `ConfigurationServiceAddr` | `http://localhost:7003` | Адрес Configuration service |

---

### Proto-файлы (из `BarkFluff.Proto`)

| Файл | Роль |
|------|------|
| `beacon_api.proto` | **Server** — определяет `BeaconApi` сервис и `GetServerInfo` RPC |
| `navigator_api.proto` | **Client** — используется для `RegisterServerAsync` |

---

### Прочие файлы

| Файл | Назначение |
|------|-----------|
| `Dockerfile.slim` | Образ для CI и production |
| `SECURITY_AUDIT.md` | Аудит безопасности сервиса |
| `BarkFluff.Beacon.http` | HTTP-файл для ручного тестирования gRPC |
| `appsettings.Development.json` | Dev-конфигурация (переопределения) |
| `Properties/launchSettings.json` | Настройки запуска из Visual Studio |

---

## Поток данных

```
Клиент
  └─► BeaconApiService.GetServerInfo()
        └─► MediatR → GetServerInfoCommandHandler
              └─► ConfigurationApi (x9 сервисов, параллельно)
                    └─► собирает GetServerInfoResponse
                          └─► возвращает клиенту

ServerRegistrationService (каждые 5 мин)
  └─► NavigatorApi.RegisterServer()
```

---

## Актуальность на дату исследования

| Пункт из Beacon.md | Статус |
|--------------------|--------|
| Порт 7002 | ✅ Актуально |
| Нет БД | ✅ Актуально |
| GetServerInfo через CQRS/MediatR | ✅ Актуально |
| Регистрация в Navigator каждые 5 мин | ✅ Актуально |
| Зависимости: Configuration + Navigator | ✅ Актуально |
| 9 сервисов в GetServerInfoResponse (включая Calls, Bots) | ✅ Актуально |
| GetServerInfo: `ExternalEndpoint:Host` без фолбэка (пусто → `Offline`); ответ кешируется 5 мин | ✅ Актуально |
| MetricsCollector (`server_info_requests`, `navigator_registrations`) | ⚠️ Не упомянуто в Beacon.md |
| `ConfigurationServiceAddr` ключ конфига | ⚠️ Не упомянуто в Beacon.md |
