# BarkFluff.Bots

Платформа ботов по образцу Telegram Bot API. Боты — пользователи с `IsBot=true` и username с суффиксом `bot`; их свойства (владелец, токен, роль) — в отдельной БД `bots`. Порты: **7027** (gRPC) + **7028** (HTTP/1.1, Bot REST API).

Расположение: `Backend/BarkFluff.Bots/`. План v1: `docs/plan/Bot-API.md`. Рефакторинг на общие JWT (`TokenType.Bot`) + эталонные паттерны **выполнен** (2026-07-15, план: `docs/plan/Bots-JWT-Refactor.md`); старый формат токена `{botId}:{secret}` выпилен без обратной совместимости.

## Сборка

```bash
dotnet build Backend/BarkFluff.Bots/BarkFluff.Bots.csproj
dotnet test Tests/BarkFluff.Bots.Tests/BarkFluff.Bots.Tests.csproj
```

## Структура проекта (эталон Identity/Users/Files)

- `Features/` — CQRS (MediatR): admin (`CreateSystemBot`, `ListBots`, `DeleteBot`, `RegenerateToken`) + внешний API (`GetMe`, `SendBotMessage`, `SendBotFile`, `EditBotMessage`, `DeleteBotMessage`, `GetBotFile`, `GetBotUserInfo`, `GetBotUpdates`, `SetMyCommands`, `GetMyCommands`)
- `Host/` — `BotsServerApiService`, `BotsExternalApiService` + `BotAuthInterceptor`; `Http/` — `BotApiEndpoints`, `BotAuthEndpointFilter`, `BotApiResponse`
- `Services/` — `BotRegistryCache`, `BotAccessValidator`, `BotCallerContext`, `IBotRateLimiter`/`RedisBotRateLimiter`, `IBotPollingGuard`/`RedisBotPollingGuard`, `BotUpdateNotifier`, `UpdateJsonMapper`, `BotFather/`; hosted: `SystemBotsSeeder`, `BotsCleanupService`
- `Infrastructure/` — только `BotTokenIssuer` (обёртка gRPC-клиента [[Backend/Identity]])
- `Mapping/` — `BotMapping`, `BotMessageMapping`, `BotUpdateMapping` (Domain/proto → gRPC/HTTP-ответы)
- `Persistence/` — `BotsContext`, storages, миграции; `Consumers/` — `NewMessageConsumer`, `LoginNotificationConsumer`
- Тесты: `Tests/BarkFluff.Bots.Tests` (BotAccessValidator, SendBotMessage/GetBotUpdates хендлеры, consumers)

## Архитектура

```
Внешняя программа ──x-auth-token──▶ HTTP /bot/{method} (7028) ─┐
Внешняя программа ──x-auth-token──▶ gRPC BotsExternalApi (7027)┤
                                                               ▼
Пользователь ──сообщение боту──▶ Messages ──NewMessageEvent──▶ Bots (BotUpdates + notifier)
Бот ──sendMessage──▶ Bots ──SendMessageServer──▶ Messages (членство/запрет инициации)
```

- **Токен бота** — общесистемный долгоживущий JWT (`TokenType.Bot`, exp 9999): claims `x-user-id` (= botId), `x-token-type=Bot`, `x-bot-token-id` (uuid выпуска). Выпускает [[Backend/Identity]] (`IdentityServerApi.CreateBotTokenServer`) через `Infrastructure/BotTokenIssuer`; Bots хранит только `TokenId` (plaintext-JWT показывается один раз: BotFather / AdminPanel / RegenerateToken).
- **Авторизация внешнего API**: штатный XAuth (заголовок `x-auth-token`) + политика `[Authorize(Policy = nameof(TokenType.Bot))]`. После JWT-валидации `BotAccessValidator` (общий синглтон) сверяет claim `x-bot-token-id` с `TokenId` в кэше (мгновенный отзыв: `RegenerateToken`/`DeleteBot` убивают старый JWT, переживает рестарты — источник истины Postgres), проверяет `SystemRole == None` и rate-limit. Обёртки: `Host/BotAuthInterceptor` (gRPC, через `AddServiceOptions`) и `Host/Http/BotAuthEndpointFilter` (401/429 в формате Bot API). Бот текущего запроса — scoped `Services/BotCallerContext` (из `UserContext.UserId` + кэш). Политика `User` bot-JWT не принимает — на другие API платформы токен не проходит.
- **`BotRegistryCache`** — локальный кэш всех ботов. Грузится сидером, обновляется в местах записи; авторитативен для сверки `TokenId`. При масштабировании изменения (Set/Remove) рассылаются fan-out событием `BotRegistryChangedEvent`, консьюмер на каждом инстансе перечитывает бота из БД (иначе XAuth на другом инстансе видел бы старый `TokenId` после регенерации).
- **CQRS**: внешний API идёт через Features (`GetMe`, `SendBotMessage` — общий для gRPC/HTTP, `SendBotFile` — квота 1 ГБ + upload, `GetBotUserInfo`, `GetBotUpdates` — long-poll в хендлере). Host тонкий; `SubscribeUpdates` остаётся в Host (server-streaming в MediatR не ложится). Маппинг Domain/proto → ответы — в `Mapping/` (`BotMapping`, `BotMessageMapping`, `BotUpdateMapping`). В `Infrastructure/` — только `BotTokenIssuer`; hosted-сервисы (`BotsCleanupService`, `SystemBotsSeeder`) — в `Services/`.
- **Приём входящих**: второй consumer `NewMessageEvent` (очередь `new-messages-bots-handler`, competing — сохраняет update один раз, [[Backend/Updates]] не затронут). Пересечение `ChatMembers` с реестром ботов (исключая отправителя) → `BotUpdates` (jsonb payload, Telegram-like) + публикация fan-out `BotUpdateSignalEvent`; консьюмер на каждом инстансе будит свои локальные waiter'ы `BotUpdateNotifier` (TaskCompletionSource per bot) — сигнал доходит до poller'а на любом инстансе (update шарится через БД).
- **`getUpdates(offset)`** подтверждает и удаляет строки `< offset`. Ретеншн: `BotsCleanupService` раз в час (BotUpdates >24ч, BotFatherSessions >30 мин).
- **Лимиты**: `IBotRateLimiter`/`RedisBotRateLimiter` — 30 req/s на бота, общий счётчик на все инстансы через Redis `INCR` (иначе 30×N); `IBotPollingGuard`/`RedisBotPollingGuard` — один активный поток getUpdates/SubscribeUpdates на бота глобально через распределённый лок `SET pollguard:{botId} NX EX` (TTL 90с переживает падение инстанса, долгие стримы продлевают его). Квота хранилища вложений бота — 1 ГБ (проверка перед `UploadFileServer`).
- **Правка и удаление своих сообщений**: `EditMessage`/`DeleteMessage` идут в [[Backend/Messages]] через `EditMessageServer`/`DeleteMessageServer` (тот же паттерн `sender_user_id`, что у `SendMessageServer`). Авторство проверяет Messages — по чужому `message_id` бот получит `NoPermission`, поэтому `chat_id` в запросе не нужен. Метод назван `editMessage`, а **не** `editMessageText`: Messages заменяет вложения переданным списком, так что правка без `file_ids` их снимает (forwarded-вложения сохраняются).
- **`getFile`**: вложения не отдаются по прямому `file_id` — `DownloadFile` в [[Backend/Files]] пропускает только аватарки, картинки чата и постеры. Поэтому бот получает временную ссылку через `GetTempDownloadUrlServer` (серверный аналог `GetTempDownloadUrl`, добавлен для Bots), а имя и размер — из `GetFileData`. Проверки «файл приходил именно этому боту» нет — доступ ровно тот же, что у обычного пользователя платформы.
- **Команды бота**: `SetMyCommands` заменяет список целиком (пустой очищает) и хранит его в jsonb-колонке `Bots.Commands`, `GetMyCommands` читает из `BotRegistryCache`. Запись идёт через `BotRegistryCache.Set` — он же рассылает fan-out инвалидацию, иначе другие инстансы отдавали бы старый список. Валидация Telegram-совместимая: имя `^[a-z0-9_]{1,32}$`, описание 1–256, ≤100 команд, без дублей.
- **Бот не пишет первым**: чат бот↔пользователь создаётся только когда пользователь напишет боту; авторизацию отправки делает `SendMessageServer` в [[Backend/Messages]] (членство при `chat_id`, запрет авто-DM при `user_id`). Исключение — системные боты (`allow_chat_creation`, login-notifier).

## Системные боты (in-process, не под rate-limit, без внешнего токена)

| Бот                  | Роль      | Что делает                                                                                                                                                                                                                               |
| -------------------- | --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `@botfather`         | BotFather | State machine создания/управления ботами: `/newbot`, `/mybots`, `/token`, `/setname`, `/setdescription`, `/setuserpic`, `/deletebot`, `/cancel`. Сессии — `BotFatherSessions` (TTL 30 мин). Username `botfather` создан с bypass правил. |
| `@barkfluffnotifier` | Barkfluff | Consumer `EmailNotification` (очередь `email-notifications-bots-handler`), фильтр `SuccessfulLogin` → DM о входе (устройство/ОС/IP/локация). Может создать чат первым (`allow_chat_creation`).                                           |

Consumer дополнительно пропускает события, где `OwnerId` принадлежит боту: уведомления о входе остаются у людей и не создают DM ботам.

Сидятся `SystemBotsSeeder` при старте (после Migrate), идемпотентно через `UsersServerApi.CreateBotUser`.

## Схема БД (`bots`, BotsContext)

- `Bots`: Id (= Users.Id), OwnerUserId (NULL = системный), Username, Name, TokenId (uuid выпуска bot-JWT, для отзыва), SystemRole (unique partial index ≠0), LastConfirmedUpdateId, Commands (jsonb-массив `{command, description}`, NULL = команд нет), CreatedAt.
- `BotUpdates`: Id (IDENTITY = update_id), BotId (FK CASCADE), Payload jsonb, CreatedAt; index (BotId, Id).
- `BotFatherSessions`: UserId PK, State, ContextBotId, PendingName, UpdatedAt.

## gRPC API (`bots_api.proto`)

**BotsServerApi** (`TokenType.Service`, для [[Backend/AdminPanel]]):

| RPC | Назначение |
|-----|-----------|
| `CreateSystemBot(username, name)` | Создать системного бота → `{bot_id, token}` (токен один раз) |
| `ListBots` | Все боты |
| `DeleteBot(bot_id)` | Удалить: строка Bots + BotUpdates каскадом; в Users username освобождается (`deleted_{id}`), аккаунт из поиска (`IsDraft=true`). Чаты сохраняются |
| `RegenerateToken(bot_id)` | Новый токен (старый отозван) |

**BotsExternalApi** (`[Authorize(Policy = nameof(TokenType.Bot))]` + `BotAuthInterceptor` — сверка token-id и rate-limit, вешается через `AddServiceOptions`; bot-JWT в `x-auth-token`):

| RPC | Назначение |
|-----|-----------|
| `GetMe` | Информация о боте |
| `SendMessage(oneof chat_id/user_id, text, file_ids)` | Отправка (только в чаты, где бот состоит) |
| `EditMessage(message_id, text, file_ids)` | Правка своего сообщения (авторство проверяет Messages) |
| `DeleteMessage(message_id)` | Удаление своего сообщения |
| `GetFile(file_id)` | Временная ссылка на оригинал вложения + имя и размер |
| `SetMyCommands(commands)` / `GetMyCommands` | Список команд бота (замена целиком) |
| `GetUserInfo(oneof user_id/username)` | Публичный профиль + `is_bot` (privacy применяет Users) |
| `SubscribeUpdates(offset) → stream BotUpdate` | Подтверждение по offset + backlog + live |

## HTTP REST API (порт 7028, `/bot/{method}`)

Bot-JWT — в заголовке **`x-auth-token`** (НЕ в URL — не течёт в логи прокси). Ответы `{"ok":true,"result":…}` / `{"ok":false,"error_code":N,"description":"…"}`. Группа `/bot`: `RequireAuthorization(Bot)` + фильтр `BotAuthEndpointFilter` (сверка token-id → 401, rate-limit → 429; квота → 413, конфликт getUpdates → 409).

| Endpoint | Вход | result |
|---|---|---|
| `GET /bot/getMe` | — | `{id, is_bot, first_name, username}` |
| `POST /bot/sendMessage` (JSON) | `chat_id`/`user_id`, `text` ≤4096 | message-объект |
| `POST /bot/sendPhoto`, `/bot/sendDocument` (multipart) | `file` + `chat_id`/`user_id` + `caption?` | message + вложение |
| `POST /bot/editMessage` (JSON) | `message_id`, `text`, `file_ids?` | `{message_id, text, edited_at}` |
| `POST /bot/deleteMessage` (JSON) | `message_id` | `true` |
| `GET /bot/getFile` | `file_id=` | `{file_id, file_name, file_size, file_url}` |
| `POST /bot/setMyCommands` (JSON) | `commands: [{command, description}]` | `true` |
| `GET /bot/getMyCommands` | — | массив `{command, description}` |
| `GET /bot/getUpdates` | `offset?`, `limit?=100`, `timeout?≤50` (long-poll) | массив update'ов |
| `GET /bot/getUserInfo` | `user_id=` / `username=` | публичный профиль |

## Конфигурация (секция в [[Backend/Configuration]], ServiceId=14)

| Ключ | Назначение |
|------|-----------|
| `RunSettings:Port` = 7027 / `Http1Port` = 7028 | gRPC + Bot REST API |
| `BotsDb` | строка подключения БД `bots` |
| `Redis` | общий rate-limit и распределённый polling-guard (без ключа сервис не стартует) |
| `UsersService:Host/Token` | CreateBotUser/DeleteBotUser, профили, getUserInfo |
| `MessagesService:Host/Token` | SendMessageServer |
| `FilesService:Host/Token` | UploadFileServer (вложения), UploadAvatarServer (аватарки) |
| `IdentityService:Host/Token` | CreateBotTokenServer (выпуск bot-JWT) |
| `ExternalEndpoint:Host` | субдомен bots |

Для AdminPanel — секция `BotsService:Host/Token` (ServiceId=0).

## Внешний доступ ([[Backend/Nginx]])

`bots.barkfluff.com` (`bots.conf`): `~ ^/bot/` → `http://bots:7028` (client_max_body_size 50m, proxy_read_timeout 90s под long-poll); остальное `/` → `grpc://bots:7027`.

## Зависимости

- **[[Backend/Users]]** — `CreateBotUser` (идемпотентен, IsBot=true, без UserContact), `DeleteBotUser`, `GetById`/`GetUserByUsername`, `UpdateProfileServer`, `SetProfilePictureServer`.
- **[[Backend/Messages]]** — `SendMessageServer` (sender = бот; авторизация отправки внутри), `EditMessageServer`/`DeleteMessageServer` (авторство внутри), `NewMessageEvent` (второй consumer).
- **[[Backend/Files]]** — `UploadFileServer` (полный пайплайн), `UploadAvatarServer`, `GetFileData`, `GetTempDownloadUrlServer` (getFile), `GetUserStorageInfoServer` (квота 1 ГБ).
- **[[Backend/Identity]]** — `CreateBotTokenServer` (выпуск bot-JWT); публикует `EmailNotification` (SuccessfulLogin) для login-notifier.
- **[[Backend/Beacon]]** — отдаёт `Service bots = 15` в `GetServerInfoResponse`.

## Не реализовано (v1)

- Webhook-доставка входящих (только long-poll + gRPC-стрим).
- Клиентский бейдж «bot» (поле `is_bot` в proto уже есть — клиентская задача вне плана).
- Автоподсказка команд в клиентах: `setMyCommands`/`getMyCommands` уже хранят список, но UI его не показывает (клиентская задача).
- Типы update'ов кроме `message`: нет `chat_member` (бот не узнаёт о добавлении в группу) и `edited_message`.
