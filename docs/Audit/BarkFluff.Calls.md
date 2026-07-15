# Аудит: BarkFluff.Calls

> Дата: 2026-07-10. Область: код сервиса звонков, LiveKit webhooks/JWT, Dockerfile, nginx и docker-compose.

## Сводка

Сервис корректно защищает gRPC API политикой `TokenType.User`, берёт userId только из `UserContext`, проверяет членство в чате перед выдачей токена для `AcceptCall`/`JoinCall`, а webhook LiveKit проверяет подпись через `WebhookReceiver`. Новые проблемы находятся в двух путях: `GetActiveCalls` не проверяет право вызывающего на каждый запрошенный чат и потому выдаёт метаданные чужих групповых звонков; уже выданный LiveKit JWT живёт два часа и не отзывается ни при отзыве сессии, ни при исключении пользователя из чата.

| Критичность | Количество |
| ----------- | ---------- |
| Critical    | 0 |
| High        | 1 |
| Medium      | 1 |
| Low         | 0 |
| **Итого**   | **2** |

## Безопасность

### S1. ~~IDOR в `GetActiveCalls`: раскрытие активных звонков чужих чатов~~ — ~~High~~ **Исправлено (2026-07-15)**

**Файл:** `Backend/BarkFluff.Calls/Services/CallsService.cs` (`GetActiveCallsAsync`).
**Решение:** Перед чтением `CallSessions` вызывающий проверяется батчем через `CheckChatMembershipAsync` (все `chat_ids` разом), запрос строится только по подтверждённому подмножеству `MemberChatIds`. Число входных `chat_ids` ограничено 100 (`MaxActiveCallsChatIds`).

### S2. ~~LiveKit-токен сохраняет доступ после отзыва сессии или исключения из чата~~ — ~~Medium~~ **Исправлено (2026-07-15)**

**Файлы:** `Backend/BarkFluff.Calls/Consumers/SessionRevokedConsumer.cs`, `Backend/BarkFluff.Calls/Consumers/ChatMemberKickedConsumer.cs`, `Backend/BarkFluff.Calls/Program.cs`; `Backend/BarkFluff.Messages/Features/KickUser/KickUserCommandHandler.cs`; `Shared/BarkFluff.Shared.Queue/Messages/ChatMemberKickedEvent.cs`.
**Решение:**
- **Отзыв сессии:** `SessionRevokedConsumer` best-effort кикает пользователя (`RoomServiceClient.RemoveParticipant`, identity = `userId` — LiveKit identity не привязан к устройству) из всех его активных direct-звонков и из всех активных групповых комнат (участники группового звонка не трекаются в БД, поэтому пробуем удалить из всех активных; «не найден» не считается ошибкой).
- **Исключение из чата:** `KickUserCommandHandler` теперь публикует `ChatMemberKickedEvent {ChatId, UserId}` через MassTransit; новый `ChatMemberKickedConsumer` в Calls по `ChatId` находит активную комнату звонка этого чата и кикает исключённого участника.
- В обоих консьюмерах ошибки LiveKit не валят обработку сообщения (try/catch + debug-лог, идемпотентно). `RoomServiceClient` создаётся на **внутреннем** `LiveKit:Url` (ws→http), не на публичном (см. Beacon S5).
**Не сделано:** TTL токена (2 часа) не уменьшен, повторная проверка доступа при выдаче нового токена не добавлена — сами по себе не являются дырой при наличии кика, оставлены как есть.

## Проверенные области без новых уникальных находок

- Все RPC `CallsApiService` защищены `[Authorize(Policy = nameof(TokenType.User))]`; userId берётся из claims (`Host/CallsApiService.cs:13-65`).
- `InitiateCall` проверяет членство инициатора для группового звонка, а `AcceptCall`/`JoinCall`/`EndCall`/`SetCallAudioQuality` — участие или членство (`Services/CallsService.cs:79-98,152-220,268-319,616-649`).
- Webhook проверяет подпись `WebhookReceiver`; неподписанные запросы получают `401` (`Program.cs:102-141`). SSRF не найден.
- Dockerfile использует chiseled runtime и непривилегированного пользователя (`Dockerfile:1-22`); порт webhook не опубликован Calls в `docker-compose-dev.yml:178-187`; `calls.conf` проксирует gRPC через TLS.
- Dev-ключ LiveKit уже отражён в `docs/Audit/BarkFluff.Configuration.md`; общие замечания о rate/connection limiting и gRPC reflection уже есть в других аудитах и не дублируются.
