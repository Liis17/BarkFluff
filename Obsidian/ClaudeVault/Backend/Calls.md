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
- **Push для background/killed app** — параллельно с in-process рингом `InitiateCall` публикует `IncomingCallPushEvent` в RabbitMQ; [[Backend/CloudMessaging]] шлёт high-priority data-only FCM `type=incoming_call`. При завершении ринга (accept/reject/end/timeout/room_finished) публикуется `CallDismissPushEvent` → FCM `type=dismiss_call`, чтобы погасить нотификацию на остальных устройствах. Получатели push те же, что у ринга (`GetRingRecipientsAsync`). См. раздел «Push-события» ниже.
- **Токены** — `LiveKitTokenService` (NuGet `Livekit.Server.Sdk.Dotnet`): `AccessToken` с `VideoGrants` на комнату `call:{id}`, HS256-подпись секретом LiveKit.
- **Webhooks** — отдельный HTTP/1.1-листенер (`RunSettings:Http1Port=7026`), `WebhookReceiver` верифицирует подпись. `room_finished` → финализация CDR; `participant_joined/left` → `ParticipantEvent` в стрим.
- **CDR** — таблица `CallSessions` (Postgres/EF Core): caller/callee/chat, room, media, status (Ringing→Active→Ended), reason, тайминги, длительность.
- **Таймаут** — `CallTimeoutScheduler` (45с): не ответили → `CallEndReason.Missed`.
- **Системное сообщение** — при завершении звонок пишет в чат системное сообщение («Звонок · 5:23» / «Пропущенный звонок» / «Звонок отклонён») через `MessagesServerApi.PostCallSystemMessage` (best-effort). Для личного звонка — в существующий личный чат; если чата ещё нет, сообщение не пишется (чат не создаётся).

## gRPC API (`calls_api.proto`, `CallsApi`)

| RPC | Назначение |
|-----|-----------|
| `InitiateCall` | Старт звонка (oneof: `callee_user_id` / `chat_id`) → `{call_id, livekit_url, access_token}` |
| `JoinCall` | Присоединиться к идущему звонку (group late-join / второй девайс) |
| `AcceptCall` | Принять → токен; гасит ринг на остальных устройствах, уведомляет caller |
| `RejectCall` | Отклонить (1-на-1 завершает звонок; в группе — гасит ринг у отказавшегося) |
| `EndCall` | Завершить |
| `SetCallAudioQuality` | Сменить **общее** качество голоса звонка (AUTO/LOW/MEDIUM/HIGH); рассылает `CallAudioQualityChanged` всем участникам |
| `SubscribeCallEvents` | **Device-scope** стрим `CallEvent` (incoming/accepted/rejected/ended/member/**audio_quality**) — требует device-id в JWT |
| `ListCallHistory` | История звонков пользователя (фильтр `ALL`/`MISSED`, курсор `before_started_at` + `limit`≤50, `has_more`) → `CallHistoryItem[]` |
| `GetActiveCalls` | Активные звонки в указанных `chat_ids` (для join-баннера) → `ActiveCallItem[]` |

Все методы — `[Authorize(Policy = nameof(TokenType.User))]`.

> **История (v1):** `ListCallHistory` отдаёт личные звонки, где пользователь — участник, и **групповые, инициированные им** (`CallerUserId == me`). Полная групповая история (звонки всех чатов, где пользователь состоит) требует серверного lookup чатов пользователя в [[Backend/Messages]] — TODO. `GetActiveCalls` возвращает звонки со статусом `Active`; `participant_user_ids` в v1 пуст (клиент делает `JoinCall`).

> ⚠️ **Proto-синхронизация:** новые `ListCallHistory`/`GetActiveCalls`/`CallHistoryItem`/`ActiveCallItem` добавлены в `Shared/BarkFluff.Proto/calls_api.proto` и `Android/core/src/main/proto/calls_api.proto`. `Mac/Barkfluff/Protos/calls_api.proto` **не синхронизирован** (Swift-код уже ссылается на `ListCallHistory` — DTO согласовать при возврате к Mac/iOS).

### Push-события (RabbitMQ → [[Backend/CloudMessaging]])

| Событие (`BarkFluff.Shared.Queue.Messages`) | Когда | FCM payload |
|---|---|---|
| `IncomingCallPushEvent` | `InitiateCall` после ринга | `type=incoming_call`, `call_id`, `caller_user_id`, `chat_id`, `media_type`, `started_at`, `caller_name`, `avatar_url`, `chat_title` |
| `CallDismissPushEvent` | accept/reject/end/timeout/room_finished | `type=dismiss_call`, `call_id`, `reason` |

Событие несёт только ID; имя звонящего/аватар/заголовок чата резолвит consumer CloudMessaging через gRPC Users/Messages.

### Качество медиа

- **Голос — общий для звонка.** Любой участник вызывает `SetCallAudioQuality`; текущее значение хранит `CallQualityStore` (in-memory Singleton — состояние транзиентное, как подписки, поэтому колонки в CDR нет), сервер рассылает `CallAudioQualityChanged` всем (включая инициатора смены — единый источник истины). Текущее качество отдаётся в ответах `Initiate/Accept/Join` (`audio_quality`) — late-join получает актуальное. Применение пресета к публикации — на клиенте (LiveKit `audioPreset`).
- **Видео — локально у публикующего.** Качество своего видео-стрима (разрешение+битрейт) клиент меняет сам через LiveKit; на backend не ходит. См. [[Клиенты/Web]].

## Конфигурация (секция в [[Backend/Configuration]], ServiceId=13)

| Ключ | Назначение |
|------|-----------|
| `RunSettings:Port` = 7025 / `Http1Port` = 7026 | gRPC + webhooks |
| `CallsDb` | строка подключения CDR |
| `MessagesService:Host/Token` | авторизация группы + список участников |
| `LiveKit:Url` | WSS-адрес (дублируется в [[Backend/Beacon]].`livekit_url`) |
| `LiveKit:ApiKey` / `ApiSecret` | креды подписи токенов и верификации webhooks (совпадают с `keys` в `Backend/livekit/livekit.yaml`) |

## Внешний доступ ([[Backend/Nginx]])

Контейнеры наружу портов не публикуют — всё внешнее идёт через nginx :443 по субдоменам:

- `calls.barkfluff.com` → `grpc://calls:7025` (`calls.conf`, gRPC + долгий таймаут под `SubscribeCallEvents`).
- `livekit.barkfluff.com` → `http://livekit:7880` WSS-сигнализация (`livekit.conf`). В проде `LiveKit:Url = wss://livekit.barkfluff.com`.
- Webhook `calls:7026` — внутренний (LiveKit → Calls), наружу не выходит.
- **Медиа LiveKit** (UDP 50000-50200 + ICE/TCP 7881) nginx проксировать не может — публикуется напрямую на хосте; firewall открывает только 443 + эти медиа-порты. В проде `rtc.use_external_ip: true`.

## Зависимости

- **[[Backend/Messages]]** — `MessagesServerApi.CheckChatMembership` (авторизация группового звонка), `GetChatMemberIds` (ринг участникам) и `PostCallSystemMessage` (системное сообщение об итоге звонка при завершении).
- **[[Backend/Beacon]]** — отдаёт клиенту `livekit_url` из конфига Calls.
- **LiveKit server** — Docker-сервис `livekit` (`livekit/livekit-server`), конфиг `Backend/livekit/livekit.yaml`.
- **RabbitMQ** — `SessionRevokedConsumer` (отзыв токенов, паритет с другими сервисами); публикация `IncomingCallPushEvent` / `CallDismissPushEvent` для [[Backend/CloudMessaging]].

## Клиенты

- **[[Клиенты/Web]]** — первый клиент звонков (обкатка): gRPC-Web через YARP [[Backend/BarkFluff.Web]], медиа через `livekit-client` (WSS напрямую к LiveKit). Модули `js/app/calls.js` (сигнализация + `SubscribeCallEvents`) и `js/app/calls-ui.js` (ринг/экран + LiveKit Room). Поддержаны 1-на-1 и группы, аудио+видео.
  - ⚠️ Dev-нюанс: `LiveKit:Url` должен быть **browser-reachable** (`ws://localhost:7880`, не `ws://livekit:7880`); getUserMedia требует secure context (`localhost`/HTTPS).
- **[[Клиенты/macOS]] / [[Клиенты/iOS]]** — нативные клиенты звонков. Сигнализация — gRPC через общий пакет `BFNetworking` (`CallsRepository` + `CallEventsStreamManager`, эндпоинт Calls обнаруживается через [[Backend/Beacon]] `GetServerInfoResponse.calls`). Медиа — **LiveKit Swift SDK** в пакете `BFCalls` (`CallController` — state-машина + Room + медиа-контролы + плитки). UI общий (SwiftUI в `BFCalls`): ринг, экран звонка, контролы (mic/cam/screen/качество), self-PiP, таймер. 1-на-1 и группы, аудио+видео+демонстрация экрана.
  - **macOS** — плавающий немодальный оверлей поверх чата (не блокирует чат): сворачивается в компактную плашку (имя/таймер/mute/hangup), разворачивается со всеми контролами; перетаскивается.
  - **iOS** — полноэкранный оверлей; работает **только при открытом приложении** (нет аккаунта разработчика → нет VoIP-push/CallKit; в фоне звонок завершается по `scenePhase`). Демонстрация экрана — in-app (системный broadcast-extension вне объёма).
- Остальные клиенты (Android/Windows/Linux) — отдельно, по запросу.

## Не реализовано (следующие шаги)

- Системное сообщение для личного звонка с **новым контактом** (личного чата ещё нет) — сейчас не пишется, чтобы не тащить создание чата с кэшем имён/аватаров в путь звонка.
- VoIP/CallKit push (iOS) — **не сделано**. Для Android реализован FCM data-push (`incoming_call`/`dismiss_call`) — ловится при background/killed app. На вебе входящий по-прежнему ловится только при открытой вкладке (стрим живёт со страницей).
- Полная групповая история (`ListCallHistory`) — нужен серверный lookup чатов пользователя в Messages.
