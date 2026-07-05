# Bot API для BarkFluff — план реализации

## Контекст

Платформа ботов по образцу Telegram Bot API: пользователи создают ботов через диалог с системным ботом @botfather, получают токен и управляют ботом из внешних программ по gRPC и HTTP (отправка/получение сообщений, картинок, файлов, публичная информация о пользователях). Системные боты (BotFather, login-notifier — уведомление о входе в аккаунт) создаются только через AdminPanel и автоматически сидятся при старте сервера. Боты — это пользователи с флагом `IsBot` и username с суффиксом `bot`; их свойства (владелец, токен, роль) хранятся в отдельной таблице.

Согласованные решения:
- новый микросервис **BarkFluff.Bots**;
- HTTP — REST, токен бота в заголовке `X-Bot-Token` (НЕ в URL, чтобы не течь в логи прокси); методы под префиксом `/bot/{method}`;
- доставка входящих — long-polling `getUpdates` + gRPC server-stream (webhook — не в v1);
- создание ботов обычными пользователями — только через BotFather-чат (без клиентского UI/RPC);
- бот не может писать первым: чат бот↔пользователь создаётся только когда пользователь сам напишет боту; бот отвечает только в уже существующие чаты, где он состоит;
- лимиты: один активный поток запросов на бота (concurrent `getUpdates` запрещён, как в Telegram); rate-limit на внешний API per-bot; квота хранилища бота — 1 ГБ (расширение — будущие обновления);
- системные боты (BotFather, login-notifier) работают внутри самого микросервиса (in-process, админские права на создание ботов и уведомления), не через внешний токен/HTTP и не под rate-limit.

## Архитектура

Новый сервис `Backend/BarkFluff.Bots` (шаблон — `Backend/BarkFluff.Calls`, самый свежий сервис):

- `ServiceId.Bots = 14`, gRPC-порт **7027**, HTTP/1.1-порт **7028** (`RunSettings:Http1Port`, как Files 7005/7006; `SetRunningAddress` в `Backend/BarkFluff.GrpcServer/WebApplicationBuilderExtensions.cs` уже поддерживает второй листенер).
- Владеет: БД `bots` (таблицы Bots/BotUpdates/BotFatherSessions), токенами ботов, HTTP API, gRPC `BotsExternalApi` + `BotsServerApi`, логикой BotFather и login-notifier, сидингом системных ботов.
- Redis не нужен в v1: сервис — единственный писатель по ботам (in-memory `BotRegistryCache`), long-poll сигналится in-process, стейт BotFather — в Postgres. При горизонтальном масштабировании кэш/сигналы переезжают в Redis.

**Токен бота**: `{botId}:{secret}`, secret = 32 случайных байта base64url (`RandomNumberGenerator`). В БД — только SHA-256 хеш (constant-time compare). Plaintext показывается один раз (BotFather / AdminPanel / RegenerateToken).

### Схема БД (BotsContext, база `bots`)

```sql
"Bots":            Id bigint PK (= Users.Id), OwnerUserId bigint NULL (NULL = системный),
                   Username text, Name text (кэш из Users), TokenHash text,
                   SystemRole int (0=None,1=BotFather,2=LoginNotifier),
                   LastConfirmedUpdateId bigint DEFAULT 0, CreatedAt timestamptz
                   + UNIQUE partial index (SystemRole) WHERE SystemRole<>0; INDEX (OwnerUserId)

"BotUpdates":      Id bigint IDENTITY PK (= update_id), BotId bigint FK CASCADE,
                   Payload jsonb (готовый Telegram-like update без update_id), CreatedAt
                   + INDEX (BotId, Id)

"BotFatherSessions": UserId bigint PK, State int, ContextBotId bigint NULL,
                   PendingName text NULL, UpdatedAt (TTL 30 мин по UpdatedAt)
```

### Proto

Новый `Shared/BarkFluff.Proto/bots_api.proto` (`csharp_namespace BarkFluff.Proto.Bots`):

- **BotsServerApi** (policy `TokenType.Service`, для AdminPanel): `CreateSystemBot(username,name) → {bot_id, token}`, `ListBots`, `DeleteBot`, `RegenerateToken → {token}`.
- **BotsExternalApi** (`[AllowAnonymous]` + собственный интерцептор `x-bot-token`): `GetMe`, `SendMessage(oneof chat_id|user_id, text, file_ids[])`, `GetUserInfo(oneof user_id|username)`, `SubscribeUpdates(offset) → stream BotUpdate` (backlog с offset + live). Типы `BotUpdate{update_id, BotIncomingMessage}`, `BotIncomingMessage{message_id, chat_id, from_*, date, text, attachments[]}`.

Изменения существующих proto:

- `users_api.proto`: `bool is_bot = 12` в `User`, `is_bot = 8` в `GetUserByUsernameResponse`; RPC `CreateBotUser(username, first_name, bypass_username_rules) → {user_id, already_existed}` в UsersServerApi.
- `messages_api.proto`: RPC `SendMessageServer(sender_user_id, oneof chat_id|user_id, OutgoingMessage)` в MessagesServerApi.
- `files_api.proto`: RPC `UploadFileServer(data, filename, UploadFileType, owner_user_id) → {file_id, preview_url, file_size}` в FilesServerApi (для вложений ботов; аватары — существующий `UploadAvatarServer`).
- `beacon_api.proto`: `Service bots = 15` в `GetServerInfoResponse`.

### HTTP API (порт 7028, REST + токен в заголовке)

Ответы `{"ok":true,"result":...}` / `{"ok":false,"error_code":N,"description":"..."}`. Маршрут `/bot/{method}` (токена в URL нет), токен — в заголовке `X-Bot-Token` (тот же формат, что gRPC-интерцептор). Endpoint-фильтр `BotTokenEndpointFilter` читает заголовок, валидирует токен, применяет rate-limit per-bot и кладёт Bot в `HttpContext.Items`.

| Endpoint | Вход | result |
|---|---|---|
| `GET /bot/getMe` | — | `{id, is_bot:true, first_name, username}` |
| `POST /bot/sendMessage` (JSON) | `chat_id`(guid) или `user_id`, `text` ≤4096 | message-объект |
| `POST /bot/sendPhoto` / `POST /bot/sendDocument` (multipart) | файл + `chat_id`/`user_id` + `caption?` | message + вложение |
| `GET /bot/getUpdates` | `offset?`, `limit?=100`, `timeout?≤50` сек | массив update'ов |
| `GET /bot/getUserInfo` | `user_id=` или `username=` | публичный профиль + `is_bot` |

Отправка (`sendMessage`/`sendPhoto`/`sendDocument`) разрешена только в чаты, где бот уже состоит (см. «Отправка от бота»); в чужой `chat_id` — ошибка.

### Ключевые механики

- **Отправка от бота**: `MessagesServerApi.SendMessageServer` → в `SendMessageCommand` добавить `long? SenderId`, в хендлере `var senderId = request.SenderId ?? _userContext.UserId` (переиспользуется вся логика: авто-DM, вложения, лимиты, `NewMessageEvent`). Клиентский путь не меняется.
  - **Авторизация отправки**: бот не инициирует чат — он лишь отвечает. При `chat_id` проверять членство бота в `ChatMembers`; при `user_id` — слать можно только если чат бот↔user уже существует (пользователь написал первым), иначе ошибка. Так `SendMessageServer` не даёт спамить в чужие чаты по перебору guid. Авто-DM в этом пути НЕ создаётся для ботов-отправителей (в отличие от клиентского) — бота нельзя заставить написать первым.
- **Приём**: Bots-сервис — второй consumer `NewMessageEvent` на собственной очереди `new-messages-bots-handler` (MassTransit fanout, Updates не затрагивается). Пересечение `ChatMembers` с кэшем bot-id, исключая отправителя: BotFather → `BotFatherService`, LoginNotifier → игнор, остальные → insert `BotUpdates` + сигнал `BotUpdateNotifier` (TaskCompletionSource per bot) для long-poll и стримов. `getUpdates(offset)` подтверждает и удаляет строки `< offset`; ретеншн-cleanup (HostedService, раз в час: BotUpdates — подтверждённые либо старше 24ч; BotFatherSessions — старше 30 мин по `UpdatedAt`).
- **Создание бот-юзера**: НЕ через AddDraftUser (у ботов нет email; `UsersStorage.CreateUser` жёстко создаёт `UserContact`). Новый `CreateBotUser`: сразу `IsDraft=false, IsBot=true`, `Contact` → nullable, Privacy по умолчанию (как ConfirmUser). Идемпотентен (username занят ботом → вернуть его id).
- **Суффикс «bot»**: username бота обязан заканчиваться на `bot` (bypass-флаг для системных, напр. `botfather`); обычным регистрациям суффикс запретить — проверка в AddDraftUser/OverrideDraftUser/ChangeUsername + новое `UsernameBotSuffixReservedException` в Shared.Exceptions.
- **BotFather**: state machine per userId в `BotFatherSessions`. Команды: `/start`, `/help`, `/newbot` (имя → username → токен), `/mybots`, `/token`, `/setname`, `/setdescription` (→ `UsersServerApi.UpdateProfileServer`), `/setuserpic` (картинка → `GetTempDownloadUrl` → `UploadAvatarServer` → `SetProfilePictureServer`), `/deletebot` + подтверждение, `/cancel`. Ответы через `SendMessageServer`. Владение — по `Bots.OwnerUserId`.
- **Login notifier**: без изменений в Identity — оно уже публикует `EmailNotification` с `Type=SuccessfulLogin` (payload: ip, device, os, location, app, время; `NotificationQueueSender`). Bots заводит вторую очередь `email-notifications-bots-handler`, consumer фильтрует SuccessfulLogin и шлёт DM от login-notifier-бота.
- **getUserInfo**: прокси на `UsersServerApi.GetUserByUsername` (Privacy применяет Users) / `GetById` — только публичные поля + `is_bot`.
- **Удаление бота** (`DeleteBot` из AdminPanel/BotFather `/deletebot`): удалить строку `Bots` + токен; в Users — освободить username и исключить из поиска (пометить аккаунт удалённым, переиспользуя существующий путь удаления аккаунта Users, если он есть). Чаты бот↔user НЕ удаляются — показываются как «чат с удалённым аккаунтом» (клиентская отрисовка — по флагу удалённого юзера). BotUpdates бота — каскадом.
- **Rate-limit и один поток**: per-bot rate-limit на весь внешний API (HTTP endpoint-фильтр + gRPC-интерцептор, общий счётчик in-memory на `botId`). Concurrent `getUpdates`/`SubscribeUpdates` для одного бота запрещён — короткий per-bot lock, второй параллельный вызов отклоняется (как Telegram). Это же снимает гонку по `LastConfirmedUpdateId`. Квота хранилища вложений бота — 1 ГБ (проверять перед `UploadFileServer`).

## Фазы (каждая = коммит; работа в текущей ветке, без push)

**Фаза 1 — Users**: `is_bot` + `CreateBotUser` в `users_api.proto`; `User.IsBot`, `UserContact?` nullable (`Domain/User.cs`); **миграцию писать вручную** (migration + Designer + snapshot — `dotnet ef migrations add` в Users падает с MissingMethodException); `Features/CreateBotUser/`; `UsernameFormatValidator.HasBotSuffix()`; проверка суффикса в AddDraftUser/OverrideDraftUser/ChangeUsername; исключение в Shared.Exceptions; маппинги User→proto; null-guard на `user.Contact` (GetUserContacts, экспорт).
Проверка: build; grpcurl `CreateBotUser` → юзер с `is_bot=true`; регистрация `somethingbot` отклоняется.

**Фаза 2 — Messages + Files**: `SendMessageServer` (`Features/SendMessage/SendMessageCommand.cs` +SenderId, хендлер, `Host/MessagesServerApiService.cs`); `UploadFileServer` в Files (переиспользовать пайплайн загрузки: компрессия+превью). Files уже принимает 20 МБ gRPC-сообщения.
Проверка: grpcurl SendMessageServer (sender=бот из ф.1) — сообщение доходит юзеру; UploadFileServer с PNG → file_id.

**Фаза 3 — инфраструктура конфигурации**: `ServiceId.Bots = 14` (`Shared/BarkFluff.Shared.Identity/ServiceId.cs`); миграция Configuration `AddBotsConfiguration` (образец `AddCallsConfiguration`): `RunSettings:Port/Http1Port`, `BotsDb`, `ExternalEndpoint:Host`, `UsersService/MessagesService/FilesService:Host+Token`; `ConfigurationDefaultsPopulator.cs`: порты 7027/7028, контейнер `bots`, сабдомен, БД, секция `BotsService` для AdminPanel.
Проверка: Configuration заполняет дефолты, `GetConfiguration(14)` отдаёт значения.

**Фаза 4 — каркас BarkFluff.Bots**: проект по образцу Calls (`Backend/BarkFluff.Calls/Program.cs`); Domain (Bot, BotUpdate, BotFatherSession, SystemBotRole); BotsContext + Initial-миграция; `BotTokenService`, `BotRegistryCache`; Features + `Host/BotsServerApiService.cs` (`DeleteBot`: удаляет `Bots`+токен+BotUpdates каскадом, вызывает Users для освобождения username и пометки аккаунта удалённым — чат сохраняется); `SystemBotsSeeder` (после Migrate: BotFather `botfather` bypass, LoginNotifier `login_notifier_bot`; идемпотентен через CreateBotUser); Dockerfile'ы, appsettings, launchSettings, в .sln.
Проверка: сервис стартует, сидит двух ботов (в Users появляются `is_bot=true`), grpcurl ListBots.

**Фаза 5a — приём и хранение updates**: `Consumers/NewMessageConsumer` (`new-messages-bots-handler`, пересечение `ChatMembers` с кэшем bot-id, исключая отправителя); `BotUpdatesStorage` (insert + чтение backlog по offset, подтверждение/удаление `< offset`); `BotUpdateNotifier` (TaskCompletionSource per bot); cleanup-HostedService (BotUpdates 24ч/подтверждённые + BotFatherSessions 30 мин); `UpdateJsonMapper` (proto Message → Telegram-like JSON/BotUpdate).
Проверка: юзер шлёт боту (из ф.2 SendMessageServer в обратную сторону — обычный клиент) → строка в BotUpdates; ручной вызов storage отдаёт backlog по offset и удаляет подтверждённые.

**Фаза 5b — внешний gRPC + токен + лимиты**: `Host/BotsExternalApiService` (`[AllowAnonymous]`, `GetMe`/`SendMessage`/`GetUserInfo`/`SubscribeUpdates`) + `BotTokenInterceptor` только на этот сервис (`AddServiceOptions<BotsExternalApiService>`); per-bot rate-limit + one-active-stream lock (общий компонент, используется и HTTP); авторизация отправки (членство бота в чате, запрет инициации). `SubscribeUpdates` = backlog по offset + live через `BotUpdateNotifier`.
Проверка: CreateSystemBot → токен; grpcurl `GetMe`/`SubscribeUpdates` с `x-bot-token`; `SendMessage` в существующий чат доходит, в чужой `chat_id` — отказ; второй параллельный `SubscribeUpdates` тем же ботом отклоняется.

**Фаза 5c — HTTP REST API**: HTTP minimal API `Host/Http/BotApiEndpoints.cs` (маршруты `/bot/{method}`) + `BotTokenEndpointFilter` (заголовок `X-Bot-Token`, rate-limit, Bot в `HttpContext.Items`) + модели ответов `{ok,result}`/`{ok,error_code,description}`; multipart для `sendPhoto`/`sendDocument` (квота 1 ГБ перед `UploadFileServer`).
Проверка: `curl -H "X-Bot-Token: …"` getMe / sendMessage / getUpdates (long-poll + offset-подтверждение); sendPhoto через `curl -F`; concurrent getUpdates отклоняется.

**Фаза 6 — BotFather + login notifier**: `Services/BotFather/BotFatherService` + `BotFatherSessionsStorage`; `Consumers/LoginNotificationConsumer` (`email-notifications-bots-handler`, фильтр SuccessfulLogin).
Проверка: из клиента: @botfather `/newbot` до токена, getMe этим токеном; `/setname`, `/setdescription`, `/setuserpic`, `/token`, `/deletebot`; повторный логин → DM от login-notifier.

**Фаза 7 — AdminPanel**: `Endpoints/BotsEndpoints.cs` (GET/POST `/api/bots`, regenerate-token, DELETE), регистрация + gRPC-клиент BotsServerApi (`BotsService:Host/Token`), `Pages/bots.html` (старый UI, образец `badges.html`, НЕ Redesigned) + пункт навигации.
Проверка: создать системного бота из админки, увидеть токен один раз, перегенерировать, удалить.

**Фаза 8 — Beacon, nginx, docker, CI**: `beacon_api.proto` `bots=15` + фетч в `GetServerInfoCommandHandler`; `Backend/nginx/bots.conf` (образец `files.conf`: `~ ^/bot/` → http 7028, остальное `/` → grpc 7027, `client_max_body_size 50m`, `proxy_read_timeout 90s` под long-poll; токен в заголовке `X-Bot-Token`, в URL/логи не попадает); docker-compose-master/dev + sample.env; workflow `build-backend-bots.yml` (копия calls).
Проверка: docker compose dev поднимается; Beacon отдаёт bots; через nginx `curl -H "X-Bot-Token: …" https://bots.<domain>/bot/getMe`.

**Фаза 9 — Obsidian**: `Obsidian/ClaudeVault/Backend/Bots.md` + ссылка в `Index.md`; обновить `Backend/Users.md` (IsBot, CreateBotUser, суффикс), `Backend/Messages.md` (SendMessageServer), `Backend/Files.md` (UploadFileServer), `Shared/Proto.md`, `Backend/AdminPanel.md`, `Архитектура.md` (порт 7027).

## Риски

- `UserContact` → nullable: пройтись по всем чтениям `user.Contact.Email` в Users (GetUserContacts, GDPR-экспорт), null-guard для ботов.
- `SendMessageServer` доверяет сервисному токену (Messages не знает о ботах) — валидировать `sender_user_id > 0`, зафиксировать доверие комментарием. Авторизацию отправки (членство бота в чате, запрет инициации) делает Bots ДО вызова Messages — иначе перебор `chat_id` = спам в чужие чаты.
- Удаление бота: не оставить сироту в Users — освободить username + исключить из поиска + пометить аккаунт удалённым, но сохранить чаты (отрисовка «удалённый аккаунт»). Нужна проверка, что путь удаления аккаунта Users переиспользуем для ботов.
- Миграции Users — только вручную (известный баг `dotnet ef` в этом проекте).
- Клиенты (Android/WPF/…) пока не показывают бейдж «bot» — поле `is_bot` уже в proto, клиентская часть — отдельная задача вне этого плана.

## Ключевые файлы

- `Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs` — параметризация sender
- `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` — CreateUser/CreateBotUser
- `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs` — дефолты нового сервиса
- `Backend/BarkFluff.Calls/` — шаблон нового сервиса (Program.cs, csproj, Dockerfile)
- `Shared/BarkFluff.Proto/users_api.proto`, `messages_api.proto`, `files_api.proto`, `beacon_api.proto`, новый `bots_api.proto`
- `Backend/Barkfluff.AdminPanel/Endpoints/`, `Pages/badges.html` — образцы для страницы ботов
