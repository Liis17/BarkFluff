# BarkFluff.FastAuth — Карта файлов проекта

Детальный разбор каждого файла сервиса QR-авторизации.
Основная документация → [[Backend/FastAuth]]

---

## Точка входа

### `Program.cs`
Точка запуска сервиса. Настраивает WebApplication:
- Загружает конфигурацию через `LoadConfiguration(ServiceId.FastAuth)`
- Регистрирует Serilog, метрики, gRPC reflection только в Development
- Подключает `XAuth` (JWT/Service авторизация)
- Конфигурирует gRPC-клиент `IdentityServerApi` с `JwtClientInterceptor` и `ExceptionClientInterceptor`
- Маппит `FastAuthApiService` и `FastAuthServerApiService`

### `DependencyInjection.cs`
Расширение `IServiceCollection.AddFastAuthServices()`. Регистрирует:
- `FastAuthSessionsManager` — singleton
- `QrCodeGenerator` — singleton
- `FastAuthExpirationService` — BackgroundService
- `SubscribeFastAuthResultQueryHandler` — scoped
- MediatR handlers из сборки

---

## Domain

### `Domain/FastAuthSession.cs`
Доменная модель сессии QR-авторизации. Хранится в памяти.

**Поля:**
- `Id`, `CreatedAt`, `ExpiresAt` — идентификатор и время жизни
- `DeviceName`, `OperationSystem`, `AppName`, `AppVersion`, `IpAddress` — метаданные нового устройства
- `Status` — текущее состояние (`Pending → Scanned → Accepted/Rejected/Expired`)
- `ConfirmationCode` — одноразовый GUID, выдаётся при Scan
- `UserId` — id пользователя, зафиксированный при Scan
- `FinalizedAt` — момент финализации (для GC)

**Методы (все thread-safe через `lock`):**
- `TryAttachSubscriber()` — закрепляет единственного подписчика стрима
- `TryScan(userId)` → `ScanOutcome` — переводит в `Scanned`, выдаёт `ConfirmationCode`
- `TryAccept(code, userId, result)` — переводит в `Accepted`, пушит токены в Channel
- `TryReject(code, userId)` — переводит в `Rejected`
- `TryExpire()` — переводит в `Expired` (вызывается фоновым сервисом)

**Дополнительно:**
- `Events` — `ChannelReader<FastAuthResult>` для стрима событий подписчику
- `ScanOutcome` enum — `Ok / Expired / AlreadyHandled`

---

## Infrastructure

### `Infrastructure/FastAuthSessionsManager.cs`
Singleton-хранилище всех активных сессий.
- `ConcurrentDictionary<string, FastAuthSession>` — хранилище
- `SessionTtl = 5 минут` — TTL сессии
- `FinalRetention = 30 секунд` — задержка удаления финализированных сессий
- `Create(...)` — создаёт и регистрирует новую сессию
- `TryGet(id)` — получает сессию по ID
- `Remove(id)` — удаляет сессию
- `Snapshot()` — снимок коллекции для обхода в фоновом сервисе

### `Infrastructure/FastAuthExpirationService.cs`
`BackgroundService`, тикает каждые **30 секунд**.
- Перебирает снимок сессий
- Помечает истёкшие (не финальные + время вышло) → вызывает `TryExpire()`
- Удаляет финализированные сессии старше `FinalRetention`
- Пишет метрики: `sessions_expired`, `sessions_removed`

### `Infrastructure/QrCodeGenerator.cs`
Обёртка над библиотекой `QRCoder`.
- `GeneratePngBase64(payload)` — генерирует PNG QR-кода для строки (FastAuthId), возвращает base64

---

## Features (MediatR handlers)

### `Features/GenerateFastAuthToken/`
| Файл | Роль |
|------|------|
| `GenerateFastAuthTokenCommand.cs` | `IRequest<GenerateFastAuthTokenResponse>` — параметр `Format` (QR или Plain) |
| `GenerateFastAuthTokenCommandHandler.cs` | Валидирует метаданные устройства из `RequestContext` (headers), создаёт сессию через `FastAuthSessionsManager`, генерирует QR (или plain-id), инкрементирует `sessions_generated` |

### `Features/ScanFastAuth/`
| Файл | Роль |
|------|------|
| `ScanFastAuthCommand.cs` | `IRequest<ScanFastAuthResponse>` — параметр `FastAuthId` |
| `ScanFastAuthCommandHandler.cs` | Находит сессию, вызывает `TryScan(userId)` из `UserContext`, возвращает метаданные устройства + `ConfirmationCode`, метрика `sessions_scanned` |

### `Features/AcceptFastAuth/`
| Файл | Роль |
|------|------|
| `AcceptFastAuthCommand.cs` | `IRequest<AcceptFastAuthResponse>` — `FastAuthId` + `ConfirmationCode` |
| `AcceptFastAuthCommandHandler.cs` | Проверяет состояние, `UserId`, `ConfirmationCode`. Вызывает `IdentityServerApi.CreateSessionForUserServerAsync` → получает `access_token`+`refresh_token`. Вызывает `TryAccept(...)`, пушит токены в Channel подписчику. Метрика `sessions_accepted` |

### `Features/RejectFastAuth/`
| Файл | Роль |
|------|------|
| `RejectFastAuthCommand.cs` | `IRequest<RejectFastAuthResponse>` — `FastAuthId` + `ConfirmationCode` |
| `RejectFastAuthCommandHandler.cs` | Проверяет состояние, `UserId`, `ConfirmationCode`. Вызывает `TryReject(...)`, закрывает стрим. Метрика `sessions_rejected` |

### `Features/SubscribeFastAuthResult/`
| Файл | Роль |
|------|------|
| `SubscribeFastAuthResultQuery.cs` | Не через MediatR — DTO с `FastAuthId`, `ResponseStream`, `CancellationToken` |
| `SubscribeFastAuthResultQueryHandler.cs` | Находит сессию, вызывает `TryAttachSubscriber()` (только один подписчик). Читает `Channel` через `ReadAllAsync` и пишет события в `IServerStreamWriter`. При отмене логирует. Метрики: `active_subscriptions`, `active_subscriptions_closed` |

---

## Host (gRPC сервисы)

### `Host/FastAuthApiService.cs`
gRPC сервис для клиентов (`FastAuthApi.FastAuthApiBase`).
| Метод | Auth | Делегирует |
|-------|------|-----------|
| `GenerateFastAuthToken` | `[AllowAnonymous]` | MediatR `GenerateFastAuthTokenCommand` |
| `SubscribeFastAuthResult` | `[AllowAnonymous]` | `SubscribeFastAuthResultQueryHandler` напрямую |
| `ScanFastAuth` | `[Authorize(User)]` | MediatR `ScanFastAuthCommand` |
| `AcceptFastAuth` | `[Authorize(User)]` | MediatR `AcceptFastAuthCommand` |
| `RejectFastAuth` | `[Authorize(User)]` | MediatR `RejectFastAuthCommand` |

### `Host/FastAuthServerApiService.cs`
gRPC сервис для серверных клиентов (`FastAuthServerApi.FastAuthServerApiBase`).
- `[Authorize(Policy = Service)]`
- `GetFastAuthInfo` — **не реализован**, выбрасывает `Unimplemented` (точка расширения для админки/отладки)

---

## Конфигурация и инфраструктура

| Файл | Роль |
|------|------|
| `appsettings.json` | Базовые настройки: порт (`RunSettings:Port = 7008`), адрес Settings, IdentityService host/token |
| `appsettings.Development.json` | Переопределения для разработки |
| `Properties/launchSettings.json` | Профили запуска Visual Studio |
| `Dockerfile.slim` | Образ на основе `mcr.microsoft.com/dotnet/aspnet`, используемый CI и production. |
| `BarkFluff.FastAuth.http` | HTTP-файл для ручного тестирования gRPC эндпоинтов |
| `SECURITY_AUDIT.md` | Документ аудита безопасности сервиса |

---

## Proto-контракты (из `BarkFluff.Proto`)

| Файл | Роль |
|------|------|
| `fast_auth_api.proto` | Все методы FastAuth, сообщения, статусы, `TokenFormat` |
| `identity_api.proto` | `CreateSessionForUserServer` — выпуск токенов через Identity |
| `shared.proto` | Общие типы платформы |

---

## Статус актуальности

> Основная архитектура соответствует [[Backend/FastAuth]]. Security-аудит S-серии (S1/D2 rate limit, S4 маскирование session id, S5 компенсация гонки в Accept, S6 reflection только в Development) задокументирован в разделе «Защиты» основного файла.
