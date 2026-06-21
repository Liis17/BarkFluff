# BarkFluff.Calls

Сервис звонков: аудио/видео, **1-на-1 и групповые**. Медиа-топология — **SFU на LiveKit**. Backend делает только call-control и выдачу LiveKit-токенов; SDP/ICE и медиа идут мимо backend. Порты: **7025** (gRPC) + **7026** (HTTP/1.1, приём LiveKit-webhooks).

Расположение: `Backend/BarkFluff.Calls/`. План: `docs/plan/Calls-LiveKit-SFU.md`.

## Сборка

```bash
dotnet build Backend/BarkFluff.Calls/BarkFluff.Calls.csproj
docker-compose -f docker-compose-dev.yml up -d livekit calls
```

## Архитектура

```
Клиент A ──InitiateCall──▶ BarkFluff.Calls ──ring (device-scope)──▶ все устройства B
                                │                                    SubscribeCallEvents
                                │ gRPC CheckChatMembership/GetChatMemberIds (Messages)
A ◀══ media (WebRTC) ══▶ LiveKit SFU ◀══ media ══▶ B
                                │ webhooks (room_finished / participant_*)
                                ▼
                          BarkFluff.Calls (финализация CDR)
```

- **Ринг — in-process** через `CallEventSubscriptionsManager` (device-scope, как `SubscribeSecretMessages` в [[Backend/Updates]]): событие рассылается на все устройства получателя; при ответе с одного устройства ринг гасится на остальных (`SendToUserExceptDevice`). Масштабирование на несколько инстансов потребует RabbitMQ-фанаута (пока один инстанс).
- **Токены** — `LiveKitTokenService` (NuGet `Livekit.Server.Sdk.Dotnet`): `AccessToken` с `VideoGrants` на комнату `call:{id}`, HS256-подпись секретом LiveKit.
- **Webhooks** — отдельный HTTP/1.1-листенер (`RunSettings:Http1Port=7026`), `WebhookReceiver` верифицирует подпись. `room_finished` → финализация CDR; `participant_joined/left` → `ParticipantEvent` в стрим.
- **CDR** — таблица `CallSessions` (Postgres/EF Core): caller/callee/chat, room, media, status (Ringing→Active→Ended), reason, тайминги, длительность.
- **Таймаут** — `CallTimeoutScheduler` (45с): не ответили → `CallEndReason.Missed`.

## gRPC API (`calls_api.proto`, `CallsApi`)

| RPC | Назначение |
|-----|-----------|
| `InitiateCall` | Старт звонка (oneof: `callee_user_id` / `chat_id`) → `{call_id, livekit_url, access_token}` |
| `JoinCall` | Присоединиться к идущему звонку (group late-join / второй девайс) |
| `AcceptCall` | Принять → токен; гасит ринг на остальных устройствах, уведомляет caller |
| `RejectCall` | Отклонить (1-на-1 завершает звонок; в группе — гасит ринг у отказавшегося) |
| `EndCall` | Завершить |
| `SubscribeCallEvents` | **Device-scope** стрим `CallEvent` (incoming/accepted/rejected/ended/member) — требует device-id в JWT |

Все методы — `[Authorize(Policy = nameof(TokenType.User))]`.

## Конфигурация (секция в [[Backend/Configuration]], ServiceId=13)

| Ключ | Назначение |
|------|-----------|
| `RunSettings:Port` = 7025 / `Http1Port` = 7026 | gRPC + webhooks |
| `CallsDb` | строка подключения CDR |
| `MessagesService:Host/Token` | авторизация группы + список участников |
| `LiveKit:Url` | WSS-адрес (дублируется в [[Backend/Beacon]].`livekit_url`) |
| `LiveKit:ApiKey` / `ApiSecret` | креды подписи токенов и верификации webhooks (совпадают с `keys` в `Backend/livekit/livekit.yaml`) |

## Зависимости

- **[[Backend/Messages]]** — `MessagesServerApi.CheckChatMembership` (авторизация группового звонка) + `GetChatMemberIds` (ринг участникам).
- **[[Backend/Beacon]]** — отдаёт клиенту `livekit_url` из конфига Calls.
- **LiveKit server** — Docker-сервис `livekit` (`livekit/livekit-server`), конфиг `Backend/livekit/livekit.yaml`.
- **RabbitMQ** — `SessionRevokedConsumer` (отзыв токенов, паритет с другими сервисами).

## Не реализовано (следующие шаги)

- Системное сообщение «звонок N мин / пропущенный» в [[Backend/Messages]] (нужен резолв person-chat-id + фан-аут участникам).
- VoIP/CallKit push при входящем звонке через [[Backend/CloudMessaging]] (Фаза 3 плана).
- Клиенты (LiveKit SDK, экран звонка) — Фаза 4.
