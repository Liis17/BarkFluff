# BarkFluff.FastAuth

## Подтверждение Telegram

Новый статус `TelegramPending=6` сохраняет факт одобрения QR авторизованным устройством. `AcceptFastAuth` фиксирует этот переход атомарно и делает короткий запрос в [[Backend/Identity]]; ожидание ответа пользователя в боте не удерживает RPC. Подписчик раз в 2 секунды проверяет сохранённое состояние и завершает попытку; переподключение продолжает тот же QR с исходным `ExpiresAt`.

`FastAuthCompletion` передаёт в Identity ID попытки, неизменный DeviceId (ID QR) и срок. Identity хранит соответствующий challenge и выдаёт одну refresh-сессию после Telegram, если он включён для FastAuth. При выключенном подтверждении сессия выпускается сразу после QR. Старый серверный RPC без attempt ID для защищённого FastAuth отклоняется. Повторный Accept не отзывает сессию победителя; отказ/истечение при гонке компенсирует выпуск через отзыв устройства. Redis Lua учитывает статус 6 как незавершённый; TTL не продлевается. Контракт: [[Shared/Proto]].

QR-авторизация новых устройств (флоу как у WhatsApp Web). Порт **7008**.

Анонимный liveness endpoint: `GET /ping` → `pong`.

> 📂 Детальная карта файлов и классов → [[Backend/FastAuth-ProjectMap]]

Расположение: `Backend/BarkFluff.FastAuth/`

## Описание

Анонимный клиент (новое устройство) получает QR-код и подписывается на стрим статуса. Авторизованный мобильный клиент сканирует QR, получает метаданные нового устройства + одноразовый `confirmation_code` (GUID), затем подтверждает или отклоняет вход. На подтверждение сервис создаёт сессию через `Identity.IdentityServerApi.CreateSessionForUserServer` и пушит `access_token`+`refresh_token` в стрим нового устройства.

TTL QR-кода — **5 минут**. По истечении сервис закрывает стрим со статусом `EXPIRED`.

## Сборка

```bash
dotnet build Backend/BarkFluff.FastAuth/BarkFluff.FastAuth.csproj
```

## Tech Stack

- ASP.NET Core, gRPC server-streaming
- MediatR (Generate / Scan / Accept / Reject)
- QRCoder (PNG → base64)
- **Redis** (StackExchange.Redis) — общий стор QR-сессий + pub/sub wake-up стримов (масштабирование, см. `docs/scaling/fastauth.md`)
- Lua-скрипты для атомарных переходов состояний сессии

## Зависимости

- [[Backend/Settings]] — discovery + ключ `Redis`
- [[Backend/Identity]] — выпуск access/refresh через `CreateSessionForUserServer` (новый server-метод)
- Redis — стор сессий (`fastauth:session:{id}`), захват подписчика (`fastauth:subscriber:{id}`), канал событий `fastauth:events`

## Proto

`fast_auth_api.proto` (полностью переписан под актуальный флоу).

### FastAuthApi (клиентский)

| Метод | Auth | Назначение |
|------|------|-----------|
| `GenerateFastAuthToken` | без авторизации | Анонимный клиент создаёт QR-сессию. Метаданные устройства (UUID, имя, OS, app, версия, IP) — из gRPC headers. TTL 5 мин. |
| `SubscribeFastAuthResult` | без авторизации (stream) | Анонимный клиент подписывается на статус. На `ACCEPTED` стрим присылает `access_token`+`refresh_token` и закрывается. |
| `ScanFastAuth` | User token | Мобильный сканирует QR. В ответе — метаданные нового устройства + одноразовый `confirmation_code`. |
| `AcceptFastAuth` | User token | Мобильный подтверждает (`fast_auth_id` + `confirmation_code`). Сервис вызывает `Identity.CreateSessionForUserServer`; если политика аккаунта требует Telegram, QR остаётся в `TELEGRAM_PENDING`, а веб получает токены после одобрения в боте. |
| `RejectFastAuth` | User token | Мобильный отклоняет — стрим закрывается со статусом `REJECTED`. |

### FastAuthServerApi

| Метод | Auth | Назначение |
|------|------|-----------|
| `GetFastAuthInfo` | Service token | **Не реализован** в первой итерации, точка расширения. |

### Статусы (`FastAuthStatus`)

`PENDING → SCANNED → TELEGRAM_PENDING → ACCEPTED / REJECTED / EXPIRED` (Identity может завершить поток сразу после `SCANNED`, если Telegram-подтверждение выключено).

## Архитектура

Сервис **stateless-масштабируемый**: любое количество инстансов за балансировщиком — сессии в Redis, событие подтверждения доставляется в стрим через Redis pub/sub.

- `Domain/FastAuthSessionState.cs` — неизменяемый снимок сессии (record), включая `ClientDeviceId` из заголовка QR-сканера отдельно от `Id` QR-попытки, плюс `FastAuthSessionResult` и тайминги (`SessionTtl=5min`, `FinalRetention=30s`, `ExpirySlack=30s`). При выпуске Identity-сессии `DeviceId` равен UUID браузера, а `AttemptId` остаётся ID QR: так веб продолжает проходить проверку устройства и повторные Accept используют тот же challenge.
- `Domain/FastAuthSessionStore.cs` — контракты `IFastAuthSessionStore` / `IFastAuthEventBus`.
- `Infrastructure/RedisFastAuthSessionStore.cs` — стор сессий: ключ `fastauth:session:{id}`, TTL = 5 мин + 30 сек slack (после логического истечения значение читаемо — Expired отличим от NotFound); финализированная сессия живёт 30 сек (реконнект забирает токены). Переходы `TryScan/TryAccept/TryReject/TryExpire` — Lua-скрипты: атомарная проверка статуса/confirmation_code/userId/срока. Захват единственного подписчика — `SETNX fastauth:subscriber:{id}` с токеном владельца.
- `Infrastructure/FastAuthEventBus.cs` — hosted-сервис: подписан на канал `fastauth:events`; локальный реестр ожидающих (`Channel<FastAuthResult>`). Переход на инстансе B публикует событие → стрим на инстансе A просыпается. Гонка «переход до подписки» закрыта перечитыванием стора после Attach.
- `Infrastructure/QrCodeGenerator.cs` — обёртка над QRCoder.
- `Features/{GenerateFastAuthToken,ScanFastAuth,AcceptFastAuth,RejectFastAuth}` — MediatR handlers.
- `Features/SubscribeFastAuthResult` — прямой handler (без MediatR): финальная сессия → сразу отдаёт результат; иначе ждёт с **локальным дедлайном до ExpiresAt** → пишет EXPIRED (sweeper не нужен, TTL Redis чистит данные).
- `Host/{FastAuthApiService,FastAuthServerApiService}.cs` — gRPC overrides с `[AllowAnonymous]` / `[Authorize(User|Service)]`.
- `DependencyInjection.cs` — `AddFastAuthServices()`: стор, шина (singleton + hosted), QrCodeGenerator, MediatR.

Порядок Accept: pre-check → `Identity.CreateSessionForUserServer` → Lua-финализация → при проигрыше гонки компенсация `RemoveActiveSessionServer`.

## Защиты

- `confirmation_code` (GUID) обязателен для Accept/Reject — нельзя подтвердить без `Scan`.
- Только один подписчик стрима на сессию **глобально** (SETNX в Redis) — повторный `Subscribe` отклоняется на любом инстансе; после дисконнекта захват освобождается (реконнект возможен).
- Все state-переходы атомарны в Redis (Lua) — гонки параллельных Scan/Accept/Reject на разных инстансах решаются как раньше in-process lock.
- TTL принудительно закрывает стрим даже если клиент не отвалился: локальный дедлайн до ExpiresAt в подписчике + TTL ключа в Redis.
- `Accept` сверяет `userId` с тем, который зафиксирован при `Scan` — другой пользователь не может подтвердить.
- UUID клиента из `x-device-id` хранится отдельно от GUID QR-попытки. Identity связывает подтверждение с обоими значениями, но добавляет сессию к исходному браузеру; старые Redis-записи без UUID читаются с совместимым запасным значением `Id`.
- Финальный результат (с токенами) хранится в Redis только `FinalRetention=30 сек`.

### Security-аудит (S-серия)

- **S1/D2** — rate limit на анонимные эндпоинты (`GenerateFastAuthToken`, `SubscribeFastAuthResult`) в nginx, зона `fastauth_anon` (2 req/s) + `limit_conn_zone` на стримы. См. [[Backend/Nginx]].
- **S4** — во всех логах `session.Id` маскируется до первых 8 символов (`session.Id[..8]`) вместо полного GUID.
- **S5** — компенсация race condition при параллельном `AcceptFastAuth` на одну сессию.
- **S6** — gRPC reflection включён только при `Environment.IsDevelopment()`.

## Метрики

- `sessions_generated`, `sessions_scanned`, `sessions_accepted`, `sessions_rejected`, `sessions_expired`
- `active_subscriptions`, `active_subscriptions_closed`

> `sessions_removed` упразднён: финализированные сессии удаляет TTL Redis, sweeper'а больше нет.

## Конфиг

```json
{
  "RunSettings": { "Port": 7008 },
  "SettingsServiceAddr": "http://localhost:7003",
  "Redis": "redis:6379",
  "IdentityService": {
    "Host": "http://localhost:7000",
    "Token": "<Service JWT>"
  }
}
```

> Ключ `Redis` раздаёт Settings-сервис (ServiceId=7, миграция `20260814100000_AddRedisConfigurationForFastAuth`; значение по умолчанию подставляет каталог Settings). Без него сервис падает при старте.
