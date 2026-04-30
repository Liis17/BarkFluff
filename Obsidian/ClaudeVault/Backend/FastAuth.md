# BarkFluff.FastAuth

QR-авторизация новых устройств (флоу как у WhatsApp Web). Порт **7008**.

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
- In-memory `ConcurrentDictionary<string, FastAuthSession>` + `Channel<FastAuthResult>` per session
- BackgroundService для TTL и очистки финализированных сессий

## Зависимости

- [[Backend/Configuration]] — discovery
- [[Backend/Identity]] — выпуск access/refresh через `CreateSessionForUserServer` (новый server-метод)

## Proto

`fast_auth_api.proto` (полностью переписан под актуальный флоу).

### FastAuthApi (клиентский)

| Метод | Auth | Назначение |
|------|------|-----------|
| `GenerateFastAuthToken` | без авторизации | Анонимный клиент создаёт QR-сессию. Метаданные устройства (имя, OS, app, версия, IP) — из gRPC headers. TTL 5 мин. |
| `SubscribeFastAuthResult` | без авторизации (stream) | Анонимный клиент подписывается на статус. На `ACCEPTED` стрим присылает `access_token`+`refresh_token` и закрывается. |
| `ScanFastAuth` | User token | Мобильный сканирует QR. В ответе — метаданные нового устройства + одноразовый `confirmation_code`. |
| `AcceptFastAuth` | User token | Мобильный подтверждает (`fast_auth_id` + `confirmation_code`). Сервис вызывает `Identity.CreateSessionForUserServer`. |
| `RejectFastAuth` | User token | Мобильный отклоняет — стрим закрывается со статусом `REJECTED`. |

### FastAuthServerApi

| Метод | Auth | Назначение |
|------|------|-----------|
| `GetFastAuthInfo` | Service token | **Не реализован** в первой итерации, точка расширения. |

### Статусы (`FastAuthStatus`)

`PENDING → SCANNED → ACCEPTED / REJECTED / EXPIRED`

## Архитектура

- `Domain/FastAuthSession.cs` — модель сессии в памяти + `Channel<FastAuthResult>` для стрима событий + lock для атомарных переходов состояния (`TryScan`, `TryAccept`, `TryReject`, `TryExpire`).
- `Infrastructure/FastAuthSessionsManager.cs` — singleton, `ConcurrentDictionary<string, FastAuthSession>`. TTL `SessionTtl=5min`, `FinalRetention=30s`.
- `Infrastructure/FastAuthExpirationService.cs` — `BackgroundService`, тикает раз в 30 сек, помечает истёкшие как `EXPIRED` и удаляет финализированные сессии старше `FinalRetention`.
- `Infrastructure/QrCodeGenerator.cs` — обёртка над QRCoder.
- `Features/{GenerateFastAuthToken,ScanFastAuth,AcceptFastAuth,RejectFastAuth}` — MediatR handlers.
- `Features/SubscribeFastAuthResult` — прямой handler (без MediatR), читает `Channel` → пишет в `IServerStreamWriter`.
- `Host/{FastAuthApiService,FastAuthServerApiService}.cs` — gRPC overrides с `[AllowAnonymous]` / `[Authorize(User|Service)]`.

## Защиты

- `confirmation_code` (GUID) обязателен для Accept/Reject — нельзя подтвердить без `Scan`.
- Только один подписчик стрима на сессию — повторный `Subscribe` отклоняется.
- Все state-переходы идут через `lock` внутри `FastAuthSession`.
- TTL принудительно закрывает стрим даже если клиент не отвалился.
- `Accept` сверяет `userId` с тем, который зафиксирован при `Scan` — другой пользователь не может подтвердить.

## Метрики

- `sessions_generated`, `sessions_scanned`, `sessions_accepted`, `sessions_rejected`, `sessions_expired`, `sessions_removed`
- `active_subscriptions`, `active_subscriptions_closed`

## Конфиг

```json
{
  "RunSettings": { "Port": 7008 },
  "ConfigurationServiceAddr": "http://localhost:7003",
  "IdentityService": {
    "Host": "http://localhost:7000",
    "Token": "<Service JWT>"
  }
}
```
