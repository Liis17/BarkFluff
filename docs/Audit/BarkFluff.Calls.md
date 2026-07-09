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

### S1. IDOR в `GetActiveCalls`: раскрытие активных звонков чужих чатов — High

**Файл:** `Backend/BarkFluff.Calls/Services/CallsService.cs:366-395`
**Проблема:** Метод принимает произвольный список `chat_ids`, парсит идентификаторы и запрашивает `CallSessions` только по `Status == Active` и `ChatId IN (...)`. В отличие от `InitiateAsync`, `AcceptAsync` и `JoinAsync`, перед запросом нет вызова `CheckChatMembership`/`EnsureChatMemberAsync`. Поэтому любой аутентифицированный пользователь, знающий UUID чата (в том числе бывший участник), получает `call_id`, тип медиа и время начала текущего группового звонка.
**Почему это проблема:** Это нарушение object-level authorization и утечка метаданных коммуникации: факт и время созвона, а также тип звонка являются чувствительными данными группы. `call_id` дополнительно раскрывает идентификатор объекта; `JoinCall` его корректно блокирует, но утечка уже произошла.
**Рекомендация:** До чтения `CallSessions` проверить членство вызывающего для каждого валидного `chat_id` через Messages и строить запрос только по подтверждённому подмножеству. Ограничить число входных идентификаторов.

### S2. LiveKit-токен сохраняет доступ после отзыва сессии или исключения из чата — Medium

**Файлы:** `Backend/BarkFluff.Calls/Services/LiveKitTokenService.cs:27-41`; `Backend/BarkFluff.Calls/Services/CallsService.cs:195-220,628-649`; `Backend/BarkFluff.Calls/Consumers/SessionRevokedConsumer.cs:18-27`; `Backend/BarkFluff.Calls/Program.cs:67-84`
**Проблема:** `JoinCall` и `AcceptCall` проверяют членство только перед выдачей JWT, а `CreateRoomToken` подписывает независимый LiveKit access token с `CanPublish`/`CanSubscribe` и TTL два часа. После выдачи сервис не хранит токен и не вызывает LiveKit API для удаления участника. Consumer отзыва сессии помещает данные только в `TokenRevocationCache`; он не завершает LiveKit-подключение и не инвалидирует LiveKit JWT. Поэтому исключённый из чата пользователь или пользователь с отозванной сессией продолжает публиковать и получать медиа в уже доступной комнате до истечения токена.
**Почему это проблема:** Отзыв доступа к аккаунту или группе не прекращает доступ к содержимому активного звонка. Клиенту достаточно получить токен до исключения — далее Calls в медиасессии не участвует.
**Рекомендация:** При `SessionRevokedEvent` и изменении членства чата удалять участника из соответствующих комнат через LiveKit RoomService API. Уменьшить TTL токена, выдавать новый токен лишь после повторной проверки доступа и передавать изменения членства из Messages в Calls.

## Проверенные области без новых уникальных находок

- Все RPC `CallsApiService` защищены `[Authorize(Policy = nameof(TokenType.User))]`; userId берётся из claims (`Host/CallsApiService.cs:13-65`).
- `InitiateCall` проверяет членство инициатора для группового звонка, а `AcceptCall`/`JoinCall`/`EndCall`/`SetCallAudioQuality` — участие или членство (`Services/CallsService.cs:79-98,152-220,268-319,616-649`).
- Webhook проверяет подпись `WebhookReceiver`; неподписанные запросы получают `401` (`Program.cs:102-141`). SSRF не найден.
- Dockerfile использует chiseled runtime и непривилегированного пользователя (`Dockerfile:1-22`); порт webhook не опубликован Calls в `docker-compose-dev.yml:178-187`; `calls.conf` проксирует gRPC через TLS.
- Dev-ключ LiveKit уже отражён в `docs/Audit/BarkFluff.Configuration.md`; общие замечания о rate/connection limiting и gRPC reflection уже есть в других аудитах и не дублируются.
