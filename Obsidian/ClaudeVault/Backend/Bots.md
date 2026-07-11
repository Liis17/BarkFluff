# BarkFluff.Bots

Платформа ботов по образцу Telegram Bot API. Боты — пользователи с `IsBot=true` и username с суффиксом `bot`; их свойства (владелец, токен, роль) — в отдельной БД `bots`. Порты: **7027** (gRPC) + **7028** (HTTP/1.1, Bot REST API).

Расположение: `Backend/BarkFluff.Bots/`. План: `docs/plan/Bot-API.md`.

## Сборка

```bash
dotnet build Backend/BarkFluff.Bots/BarkFluff.Bots.csproj
```

## Архитектура

```
Внешняя программа ──X-Bot-Token──▶ HTTP /bot/{method} (7028) ─┐
Внешняя программа ──x-bot-token──▶ gRPC BotsExternalApi (7027)┤
                                                              ▼
Пользователь ──сообщение боту──▶ Messages ──NewMessageEvent──▶ Bots (BotUpdates + notifier)
Бот ──sendMessage──▶ Bots ──SendMessageServer──▶ Messages (членство/запрет инициации)
```

- **Токен бота**: `{botId}:{secret}`, secret = 32 случайных байта base64url. В БД — только SHA-256 хеш (`BotTokenService`, constant-time compare). Plaintext показывается один раз (BotFather / AdminPanel / RegenerateToken).
- **`BotRegistryCache`** — in-memory реестр всех ботов (Bots — единственный писатель; Redis не нужен в v1). Грузится сидером, обновляется в местах записи.
- **Приём входящих**: второй consumer `NewMessageEvent` (очередь `new-messages-bots-handler`, fanout, [[Backend/Updates]] не затронут). Пересечение `ChatMembers` с реестром ботов (исключая отправителя) → `BotUpdates` (jsonb payload, Telegram-like) + сигнал `BotUpdateNotifier` (TaskCompletionSource per bot).
- **`getUpdates(offset)`** подтверждает и удаляет строки `< offset`. Ретеншн: `BotsCleanupService` раз в час (BotUpdates >24ч, BotFatherSessions >30 мин).
- **Лимиты**: `BotRateLimiter` 30 req/s на бота (общий для gRPC и HTTP); `BotPollingGuard` — один активный поток getUpdates/SubscribeUpdates на бота (как Telegram; снимает гонку по `LastConfirmedUpdateId`). Квота хранилища вложений бота — 1 ГБ (проверка перед `UploadFileServer`).
- **Бот не пишет первым**: чат бот↔пользователь создаётся только когда пользователь напишет боту; авторизацию отправки делает `SendMessageServer` в [[Backend/Messages]] (членство при `chat_id`, запрет авто-DM при `user_id`). Исключение — системные боты (`allow_chat_creation`, login-notifier).

## Системные боты (in-process, не под rate-limit, без внешнего токена)

| Бот                  | Роль      | Что делает                                                                                                                                                                                                                               |
| -------------------- | --------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `@botfather`         | BotFather | State machine создания/управления ботами: `/newbot`, `/mybots`, `/token`, `/setname`, `/setdescription`, `/setuserpic`, `/deletebot`, `/cancel`. Сессии — `BotFatherSessions` (TTL 30 мин). Username `botfather` создан с bypass правил. |
| `@barkfluffnotifier` | Barkfluff | Consumer `EmailNotification` (очередь `email-notifications-bots-handler`), фильтр `SuccessfulLogin` → DM о входе (устройство/ОС/IP/локация). Может создать чат первым (`allow_chat_creation`).                                           |

Consumer дополнительно пропускает события, где `OwnerId` принадлежит боту: уведомления о входе остаются у людей и не создают DM ботам.

Сидятся `SystemBotsSeeder` при старте (после Migrate), идемпотентно через `UsersServerApi.CreateBotUser`.

## Схема БД (`bots`, BotsContext)

- `Bots`: Id (= Users.Id), OwnerUserId (NULL = системный), Username, Name, TokenHash, SystemRole (unique partial index ≠0), LastConfirmedUpdateId, CreatedAt.
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

**BotsExternalApi** (`[AllowAnonymous]` + `BotTokenInterceptor` по метадате `x-bot-token`, вешается через `AddServiceOptions`):

| RPC | Назначение |
|-----|-----------|
| `GetMe` | Информация о боте |
| `SendMessage(oneof chat_id/user_id, text, file_ids)` | Отправка (только в чаты, где бот состоит) |
| `GetUserInfo(oneof user_id/username)` | Публичный профиль + `is_bot` (privacy применяет Users) |
| `SubscribeUpdates(offset) → stream BotUpdate` | Подтверждение по offset + backlog + live |

## HTTP REST API (порт 7028, `/bot/{method}`)

Токен — в заголовке **`X-Bot-Token`** (НЕ в URL — не течёт в логи прокси). Ответы `{"ok":true,"result":…}` / `{"ok":false,"error_code":N,"description":"…"}`. Фильтр `BotTokenEndpointFilter`.

| Endpoint | Вход | result |
|---|---|---|
| `GET /bot/getMe` | — | `{id, is_bot, first_name, username}` |
| `POST /bot/sendMessage` (JSON) | `chat_id`/`user_id`, `text` ≤4096 | message-объект |
| `POST /bot/sendPhoto`, `/bot/sendDocument` (multipart) | `file` + `chat_id`/`user_id` + `caption?` | message + вложение |
| `GET /bot/getUpdates` | `offset?`, `limit?=100`, `timeout?≤50` (long-poll) | массив update'ов |
| `GET /bot/getUserInfo` | `user_id=` / `username=` | публичный профиль |

## Конфигурация (секция в [[Backend/Configuration]], ServiceId=14)

| Ключ | Назначение |
|------|-----------|
| `RunSettings:Port` = 7027 / `Http1Port` = 7028 | gRPC + Bot REST API |
| `BotsDb` | строка подключения БД `bots` |
| `UsersService:Host/Token` | CreateBotUser/DeleteBotUser, профили, getUserInfo |
| `MessagesService:Host/Token` | SendMessageServer |
| `FilesService:Host/Token` | UploadFileServer (вложения), UploadAvatarServer (аватарки) |
| `ExternalEndpoint:Host` | субдомен bots |

Для AdminPanel — секция `BotsService:Host/Token` (ServiceId=0).

## Внешний доступ ([[Backend/Nginx]])

`bots.barkfluff.com` (`bots.conf`): `~ ^/bot/` → `http://bots:7028` (client_max_body_size 50m, proxy_read_timeout 90s под long-poll); остальное `/` → `grpc://bots:7027`.

## Зависимости

- **[[Backend/Users]]** — `CreateBotUser` (идемпотентен, IsBot=true, без UserContact), `DeleteBotUser`, `GetById`/`GetUserByUsername`, `UpdateProfileServer`, `SetProfilePictureServer`.
- **[[Backend/Messages]]** — `SendMessageServer` (sender = бот; авторизация отправки внутри), `NewMessageEvent` (второй consumer).
- **[[Backend/Files]]** — `UploadFileServer` (полный пайплайн), `UploadAvatarServer`, `GetFileData`, `GetUserStorageInfoServer` (квота 1 ГБ).
- **[[Backend/Identity]]** — публикует `EmailNotification` (SuccessfulLogin) для login-notifier (без изменений в Identity).
- **[[Backend/Beacon]]** — отдаёт `Service bots = 15` в `GetServerInfoResponse`.

## Не реализовано (v1)

- Webhook-доставка входящих (только long-poll + gRPC-стрим).
- Горизонтальное масштабирование (BotRegistryCache/notifier — in-memory; переезд в Redis при необходимости).
- Клиентский бейдж «bot» (поле `is_bot` в proto уже есть — клиентская задача вне плана).
