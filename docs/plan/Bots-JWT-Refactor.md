# Рефакторинг BarkFluff.Bots — общие JWT (TokenType.Bot) + эталонные паттерны

> Перевод аутентификации ботов с кастомного токена `{botId}:{secret}` на общесистемные JWT
> (новое значение `TokenType.Bot`) и приведение сервиса к паттернам эталонных сервисов
> (Identity/Users/Files). Кода этот документ не содержит — это план.

---

## Контекст

Сервис [[Backend/Bots]] (см. `docs/plan/Bot-API.md`) написан с отклонениями от общих паттернов платформы:

1. **Собственная система аутентификации мимо XAuth.** Токен `{botId}:{secret}` (secret — 32 байта base64url, в БД SHA-256 хеш в `Bots.TokenHash`), заголовок `x-bot-token` / `X-Bot-Token`, кастомный стек: `Services/BotTokenService.cs`, `Services/BotTokenAuthenticator.cs`, `Host/BotTokenInterceptor.cs` (gRPC), `Host/Http/BotTokenEndpointFilter.cs` (HTTP). Внешний API — под `[AllowAnonymous]`. Все остальные сервисы используют JWT через заголовок `x-auth-token` + политики `[Authorize(Policy = nameof(TokenType.*))]` + `UserContext` (см. [[Архитектура]], `Backend/BarkFluff.GrpcServer/XAuth/`).
2. **Бизнес-логика в Host-слое.** `Host/BotsExternalApiService.cs` (GetMe/SendMessage/GetUserInfo/SubscribeUpdates) и `Host/Http/BotApiEndpoints.cs` (~300 строк: sendMessage/sendPhoto/sendDocument с проверкой квоты, getUpdates long-poll, getUserInfo) вызывают storages и gRPC-клиентов напрямую, минуя MediatR. Через Features/CQRS идут только 4 admin-метода `BotsServerApi` (CreateSystemBot/ListBots/DeleteBot/RegenerateToken).
3. **Мелочи.** Hosted-сервисы (`BotsCleanupService`, `SystemBotsSeeder`) лежат в `Infrastructure/`, хотя в эталонах `Infrastructure/` — клиенты внешних систем (Identity: `LocationClient`; Files: `S3Uploader`), а hosted-сервисы живут в `Services/` (Files: `TempFileCleanupService`). Нет папки `Mapping/` (у Users/Files — extension-методы Domain → proto).

Согласованные решения:

- **`TokenType.Bot = 3`** добавляется к существующему enum (`Unknown=0, User=1, Service=2`);
- bot-JWT выпускает **Identity** — новый server-RPC в `IdentityServerApi` (Identity остаётся единственным эмитентом пользовательских/ботовых токенов; Bots вызывает его по gRPC);
- **без обратной совместимости**: формат `{botId}:{secret}` и весь кастомный стек выпиливаются, существующие боты перевыпускают токены (`/token` у BotFather, `RegenerateToken` в AdminPanel);
- заголовок внешнего Bot API — стандартный **`x-auth-token`** (gRPC 7027 и HTTP 7028), авторизация через штатный XAuth.

---

## 1. Механика токенов

### Claims и срок жизни bot-JWT

| Claim | Значение |
|-------|----------|
| `x-user-id` | botId (бот — это пользователь, `Users.Id`; `UserContext.UserId` работает без правок) |
| `x-token-type` | `Bot` |
| `x-bot-token-id` | uuid выпуска токена (новая константа `IdentityClaims.BotTokenId`) |

Без `x-device-id` и `x-service-id`. Срок — до 9999 года, симметрично `GenerateServerToken` (`Backend/BarkFluff.Identity/Services/JwtService.cs`): контракт Bot API телеграм-образный — статический токен без refresh-флоу, безопасность обеспечивает мгновенный отзыв по token-id (ниже), exp ничего не добавляет. `ValidateLifetime=true` в XAuth продолжает работать.

### Отзыв long-lived токена

- В таблице `Bots` колонка `TokenHash` заменяется на `TokenId` (text). Plaintext-JWT по-прежнему нигде не хранится и показывается один раз.
- Identity при выпуске генерирует `token_id` (Guid), кладёт его в claim и возвращает вместе с токеном; Bots сохраняет `TokenId` в БД и `BotRegistryCache`.
- На каждом запросе внешнего API — **после** штатной JWT-валидации XAuth — сверка claim `x-bot-token-id` с `BotRegistryCache.Get(botId).TokenId`. Кэш авторитативен: грузится из БД при старте (`SystemBotsSeeder`), Bots — единственный писатель, все точки записи его обновляют. Отзыв мгновенный и переживает рестарты (источник истины — Postgres).
- `RegenerateToken` перезаписывает `TokenId` → старый JWT сразу мёртв. `DeleteBot` убирает бота из БД/кэша → токен мёртв.
- `TokenRevocationCache` из GrpcServer не подходит: in-memory (не переживает рестарт), ключ userId+deviceId, рассчитан на короткоживущие access-токены.

### Политика авторизации

В `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs` добавляется:

```csharp
options.AddPolicy(nameof(TokenType.Bot),
    p => p.RequireClaim(IdentityClaims.TokenType, "Bot"));
```

- **Только `"Bot"`, без `"Service"`**: хендлеры внешнего API берут botId из `UserContext.UserId`, у Service-токена `x-user-id` нет → `UserId == 0`, и token-id для revocation-сверки у него тоже нет. Админ-операции уже идут через `BotsServerApi` (policy `Service`).
- Политика `User` (`"User", "Service"`) **не меняется** → bot-JWT автоматически не проходит ни на один другой API платформы.
- `OnTokenValidated` в XAuth не трогаем (revocation ботов — забота Bots, знание о ботах не утаскиваем в общий GrpcServer).

### Сверка token-id, rate-limit

Общий синглтон `Services/BotAccessValidator.cs` (проверяет: `TokenType == Bot`, бот существует, `SystemRole == None`, claim token-id совпадает с кэшем, `BotRateLimiter.TryAcquire`). Две тонкие обёртки:

- `Host/BotAuthInterceptor.cs` — gRPC (Unary + ServerStreaming), вешается через существующий `AddServiceOptions<BotsExternalApiService>`; claims — из `context.GetHttpContext().User`;
- `Host/Http/BotAuthEndpointFilter.cs` — на группе `/bot`, ответы через `BotApiResponse.Error` (401/429).

`BotPollingGuard` — не auth, а concurrency-механика; остаётся у мест использования (getUpdates/SubscribeUpdates). `BotRateLimiter` сохраняется как есть.

### Новый RPC в Identity

`Shared/BarkFluff.Proto/identity_api.proto`, сервис `IdentityServerApi`:

```protobuf
rpc CreateBotTokenServer(CreateBotTokenServerRequest) returns(CreateBotTokenServerResponse);

message CreateBotTokenServerRequest  { int64 bot_user_id = 1; }
message CreateBotTokenServerResponse { string token = 1; string token_id = 2; }
```

`token_id` генерирует Identity (`Guid.NewGuid()`), Bots только хранит. В Bots — `Infrastructure/BotTokenIssuer.cs` (обёртка над `IdentityServerApiClient`, метод `IssueAsync(botId) → (Token, TokenId)`) — ровно семантика папки Infrastructure у эталонов. Регистрация клиента — по образцу `Backend/BarkFluff.FastAuth/Program.cs` (секция `IdentityService:Host/Token`, `JwtClientInterceptor` + `ExceptionClientInterceptor`).

### Конфигурация

`ConfigurationDefaultsPopulator.cs` уже умеет секцию `IdentityService` generically (Host=`http://identity:7000`, Token=сервисный JWT) — правка популятора **не нужна**. Нужна только идемпотентная миграция Configuration, добавляющая ключи `IdentityService:Host` / `IdentityService:Token` для ServiceId=14 — точный образец `Backend/BarkFluff.Configuration/Persistence/Migrations/20260429000000_AddFastAuthIdentityServiceConfiguration.cs` (там ровно эти два ключа для FastAuth, ServiceId=7).

---

## 2. Фазы (каждая = коммит; работа в текущей ветке, без push, если явно не сказано)

### Фаза 1 — Shared.Identity + GrpcServer + Identity

1. `Shared/BarkFluff.Shared.Identity/TokenType.cs` — `Bot = 3`.
2. `Shared/BarkFluff.Shared.Identity/IdentityClaims.cs` — `BotTokenId = "x-bot-token-id"`.
3. `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs` — политика `nameof(TokenType.Bot)` (см. выше). `OnTokenValidated` не трогать.
4. `Shared/BarkFluff.Proto/identity_api.proto` — RPC + messages (см. выше).
5. `Backend/BarkFluff.Identity/Services/JwtService.cs` — `GenerateBotToken(long botUserId, string tokenId)` (claims из раздела 1, exp 9999 как у `GenerateServerToken`).
6. Новые `Backend/BarkFluff.Identity/Features/CreateBotTokenServer/CreateBotTokenServerCommand.cs` + `CreateBotTokenServerCommandHandler.cs` (валидация `BotUserId > 0` исключением из Shared.Exceptions).
7. `Backend/BarkFluff.Identity/Host/IdentityServerApiService.cs` — override метода + метрика `server_bot_token_creations` (класс уже под `[Authorize(Policy = nameof(TokenType.Service))]`).
8. Тесты: дополнить `Tests/BarkFluff.Identity.Tests/Services/JwtServiceTests.cs` (claims, exp), новый тест хендлера.

**Проверка:** build; `dotnet test Tests/BarkFluff.Identity.Tests`; grpcurl `CreateBotTokenServer` с Service-токеном → декодировать JWT: `x-user-id`, `x-token-type=Bot`, `x-bot-token-id`, exp≈9999.

### Фаза 2 — Bots: миграция БД + переход auth на XAuth + выпил кастомного стека

1. `Backend/BarkFluff.Bots/Domain/Bot.cs` — `TokenHash` → `TokenId`.
2. Новая миграция `Persistence/Migrations/` (+Designer, правка `BotsContextModelSnapshot.cs`): Drop `TokenHash`, Add `TokenId text NOT NULL DEFAULT ''`. Существующие боты перевыпускают токены. Если `dotnet ef migrations add` падает (известный баг, см. `docs/plan/Bot-API.md`, фаза 1) — писать вручную по образцу InitialCreate.
3. Новый `Infrastructure/BotTokenIssuer.cs`.
4. `Program.cs`: `AddGrpcClient<IdentityServerApi.IdentityServerApiClient>` (`IdentityService:Host/Token`); в `AddServiceOptions<BotsExternalApiService>` — `BotAuthInterceptor` вместо `BotTokenInterceptor`; удалить регистрации `BotTokenService`/`BotTokenAuthenticator`; добавить `BotAccessValidator` (singleton), `BotCallerContext` (scoped, см. фазу 3), `BotTokenIssuer`.
5. Новые `Services/BotAccessValidator.cs`, `Host/BotAuthInterceptor.cs`, `Host/Http/BotAuthEndpointFilter.cs`.
6. `Host/BotsExternalApiService.cs` — `[AllowAnonymous]` → `[Authorize(Policy = nameof(TokenType.Bot))]`.
7. `Host/Http/BotApiEndpoints.cs` — группа `/bot`: `.RequireAuthorization(nameof(TokenType.Bot)).AddEndpointFilter<BotAuthEndpointFilter>()` вместо `.AddEndpointFilter<BotTokenEndpointFilter>().AllowAnonymous()`. Заголовок — стандартный `x-auth-token` (JwtBearer `OnMessageReceived` уже читает его; HTTP-порт 7028 проходит `UseXAuth`).
8. Выпуск токенов через `BotTokenIssuer.IssueAsync` + `bot.TokenId = tokenId`: `Features/CreateSystemBot/CreateSystemBotCommandHandler.cs`, `Features/RegenerateToken/RegenerateTokenCommandHandler.cs`, `Services/BotFather/BotFatherService.cs` (`/token`, создание в `/newbot`).
9. Удалить: `Services/BotTokenService.cs`, `Services/BotTokenAuthenticator.cs`, `Host/BotTokenInterceptor.cs`, `Host/Http/BotTokenEndpointFilter.cs` (вместе с extension-методами `GetBot`).
10. Миграция Configuration: ключи `IdentityService:Host/Token` для ServiceId=14 (образец — FastAuth-миграция, см. раздел 1).
11. `docker/nginx/bots.conf` — только комментарии (`X-Bot-Token` → `x-auth-token`); маршрутизация не меняется.

**Проверка:** build; dev-компоуз; Configuration отдаёт `IdentityService` для ServiceId=14; grpcurl `CreateSystemBot` → в ответе JWT; grpcurl `GetMe` и `curl /bot/getMe` с `x-auth-token: <bot-jwt>` работают; после `RegenerateToken` старый JWT → 401 сразу и после рестарта Bots; Service-токен на `BotsExternalApi` → PermissionDenied; bot-JWT на `BotsServerApi` → PermissionDenied.

### Фаза 3 — Bots: Host → Features (CQRS) + Mapping + папки

1. Новые Features (валидация inline исключениями, метрики в хендлерах — эталон Identity/Users/Files):
   - `Features/GetMe/GetMeQuery.cs` + Handler → `GetMeResponse`;
   - `Features/SendBotMessage/SendBotMessageCommand.cs` + Handler (BotId, ChatId?/UserId?, Text, FileIds; сюда переезжает `SendViaMessages` из `BotApiEndpoints.cs` и метрика `bot_api_messages_sent`) — **общий** для gRPC `SendMessage` и HTTP `sendMessage`;
   - `Features/SendBotFile/SendBotFileCommand.cs` + Handler (квота 1 ГБ через `GetUserStorageInfoServer`, `UploadFileServer`, затем отправка) — для `sendPhoto`/`sendDocument`;
   - `Features/GetBotUserInfo/GetBotUserInfoQuery.cs` + Handler → `GetUserInfoResponse`;
   - `Features/GetBotUpdates/GetBotUpdatesQuery.cs` + Handler (long-poll внутри хендлера: Confirm/GetBacklog/`BotUpdateNotifier`/`BotPollingGuard`).
2. Тонкий Host: `Host/BotsExternalApiService.cs` — унарные методы = маппинг + `mediator.Send` (образец `Host/IdentityServerApiService.cs`); **`SubscribeUpdates` остаётся в Host** — server-streaming в MediatR не ложится, прецедент: `Backend/BarkFluff.Updates/Host/UpdatesApiService.cs` держит все стримы в Host. `Host/Http/BotApiEndpoints.cs` — endpoints зовут mediator; multipart-парсинг остаётся в endpoint'е.
3. Новый scoped `Services/BotCallerContext.cs` (свойство `Bot Bot` из `UserContext.UserId` + `BotRegistryCache`) — вместо `context.GetBot()` / `HttpContext.Items["bot"]`.
4. Новая `Mapping/` по образцу Users/Files: `BotMapping.cs` (Bot → `GetMeResponse`/http-объекты), `BotMessageMapping.cs` (Message → `SendMessageResponse`/`ToMessageResult`), `BotUpdateMapping.cs`.
5. Перенос: `Infrastructure/BotsCleanupService.cs` → `Services/`, `Infrastructure/SystemBotsSeeder.cs` → `Services/` (в `Infrastructure/` остаётся только `BotTokenIssuer`).
6. Тесты `Tests/BarkFluff.Bots.Tests/` (сейчас только Consumers): `Services/BotAccessValidatorTests.cs`, `Features/SendBotMessageCommandHandlerTests.cs`, `Features/GetBotUpdatesQueryHandlerTests.cs`.

**Проверка:** build + `dotnet test Tests/BarkFluff.Bots.Tests`; повторить smoke фазы 2; параллельный getUpdates → 409.

### Фаза 4 — Документация + AdminPanel

- **AdminPanel — без правок кода**: контракт `BotsServerApi` сохранён (`CreateSystemBot`/`RegenerateToken` возвращают `token`-строку), токен показывается один раз в модалке `Pages/v2/bots.html` — визуально проверить, что длинный JWT влезает в `.token-box`.
- Obsidian: `Backend/Bots.md` (архитектура, формат токена, схема БД, авторизация внешнего API), `Backend/Identity.md` (новый server-RPC), `Shared/Identity.md` (TokenType.Bot, новый claim), `Shared/Proto.md` (identity_api), при необходимости `Архитектура.md` (политика Bot в XAuth).
- Финальный grep `x-bot-token|TokenHash|BotTokenService` — должны остаться только исторические упоминания в `docs/plan/Bot-API.md`.

---

## 3. Риски

- **Порядок выката**: фаза 1 (Identity + GrpcServer + Shared) должна деплоиться раньше или вместе с фазой 2 — иначе Bots не сможет выпускать токены, а политика `Bot` не будет зарегистрирована.
- **Все действующие боты теряют токены** (принятое решение «без совместимости») — нужна коммуникация владельцам: перевыпустить через `/token` у BotFather или AdminPanel.
- **Миграции Bots**, возможно, придётся писать вручную — `dotnet ef migrations add` падает с MissingMethodException (прецедент Users, задокументирован в `docs/plan/Bot-API.md`).
- `BotRegistryCache` остаётся in-memory (Bots — единственный писатель); при горизонтальном масштабировании сверка token-id переезжает в Redis вместе с кэшем — вне рамок этого плана (тот же trade-off, что в Bot-API v1).
