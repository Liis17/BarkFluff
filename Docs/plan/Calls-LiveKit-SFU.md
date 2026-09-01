# План реализации звонков (LiveKit SFU)

> Аудио + видео, 1-на-1 и групповые звонки. Медиа-топология: **SFU на LiveKit**.
> Кода сервисов этот документ не содержит — это план + proto-контракт.

---

## Контекст и решение

«Звонки через сервер» = выбор медиа-топологии. Рассмотренные варианты:

- **A. P2P + coturn** — медиа напрямую между клиентами, сервер как TURN-relay только при NAT. Минимум инфраструктуры, но нет групп.
- **B. SFU на LiveKit** *(выбрано)* — потоки идут через медиа-сервер. Сразу 1-на-1 + группы + комнаты, зрелые SDK, минимум своего WebRTC-кода.
- **C. SFU на mediasoup** — максимум контроля, но много своего кода на Node.js.
- **D. Гибрид P2P+SFU** — лучшая латентность 1-на-1, но самая высокая сложность; целевая фаза 2.

**Выбран вариант B (LiveKit SFU):** закрывает 1-на-1, группы и будущие VoiceRooms (idea 09) единой инфраструктурой; LiveKit SDK берёт на себя SDP/ICE-переговоры, backend делает только call-control и выдачу токенов.

---

## 1. Целевая архитектура

```
                    ┌─────────────────────────────────────────┐
   Клиент A ───────▶│  BarkFluff.Calls (новый сервис, 7025)    │
   (caller)         │  • InitiateCall/Accept/Reject/End        │
                    │  • выдача LiveKit access-token (JWT)      │
   Клиент B ───────▶│  • lifecycle звонка, CallSession         │
   (callee)         │  • SubscribeCallEvents (device-scope)    │
                    └───────┬───────────────┬─────────────────┘
                            │ gRPC          │ RabbitMQ (события)
                            ▼               ▼
              CheckChatMembership      Updates / CloudMessaging
              (Messages)              (ring + VoIP push)

   Клиент A ◀══ media (WebRTC) ══▶ ┌──────────────┐ ◀══ media ══▶ Клиент B
                                   │ LiveKit SFU  │  (Docker)
                                   │ + встроенный │
                                   │   TURN       │
                                   └──────┬───────┘
                                          │ webhooks (join/leave/finished)
                                          ▼
                                   BarkFluff.Calls
```

Ключевая идея LiveKit: **SDP/ICE-переговоры берёт на себя LiveKit SDK на клиенте**. Backend НЕ ретранслирует offer/answer/ICE — только call-control (позвонить/принять/сбросить) и выдача подписанного токена на вход в комнату.

---

## 2. Новые / изменяемые компоненты

| Компонент | Тип | Что делает |
|-----------|-----|-----------|
| **BarkFluff.Calls** | новый микросервис (7025) | Lifecycle звонка, выдача LiveKit-токенов, server-stream событий, приём LiveKit-webhooks |
| **LiveKit server** | Docker-сервис | SFU + встроенный TURN. Конфиг api_key/secret, Redis для масштабирования |
| **calls_api.proto** | новый proto | Контракт сервиса (раздел 4) |
| **CloudMessaging** | изменение | High-priority/VoIP push «входящий звонок» (iOS PushKit/CallKit, Android ConnectionService) |
| **Beacon** | изменение | Отдавать клиенту `livekit_url` в `GetServerInfoResponse` |
| **Configuration** | изменение | Регистрация `ServiceId.Calls`, провижн ключей (LiveKit, Messages-client, RabbitMQ, Redis) |
| **Messages** | переиспользование | `CheckChatMembership` (уже есть, используется Onliner) — авторизация группового звонка; запись системного сообщения «звонок N мин / пропущенный» |
| **Identity** | переиспользование | XAuth, device-id из JWT-claim для device-scope ринга |
| Клиенты (4 шт.) | изменение | LiveKit SDK, экран звонка, incoming-overlay, мини-карточка |

**Почему отдельный сервис, а не расширение Updates:** у звонка есть состояние и lifecycle (ringing→active→ended), своя БД (CDR/история), приём webhooks от LiveKit и выдача токенов — это не stateless-relay. Но **доставку ринга** строим по существующему **device-scope паттерну** Updates (как `SubscribeSecretMessages`): звоним на все устройства пользователя и гасим на остальных при ответе.

---

## 3. Поток звонка

**Исходящий 1-на-1:**
1. A → `InitiateCall(callee_user_id, type=VIDEO)`.
2. Calls создаёт `CallSession` (status=RINGING), room `call:{guid}`, генерит LiveKit-токен для A → возвращает `{call_id, livekit_url, access_token}`. A коннектится в комнату.
3. Calls публикует `IncomingCallEvent` в RabbitMQ → доставка на **все устройства B** (device-scope `SubscribeCallEvents`) + VoIP push через CloudMessaging.
4. B на одном устройстве → `AcceptCall(call_id)` → токен, коннект. `CallAcceptedEvent`: остальным устройствам B — «гасим ринг», A — «принято».
5. Медиа течёт через LiveKit. `EndCall` любой стороной / webhook `room_finished` → `CallEndedEvent` + системное сообщение в Messages + запись в CDR.

**Групповой звонок:** room привязан к `chat_id`. `InitiateCall(chat_id)` → ринг участникам (членство через `CheckChatMembership`). Любой участник `JoinCall(chat_id)` присоединяется к идущему звонку. Тот же механизм покрывает будущие VoiceRooms (idea 09) — отличие в ролях/масштабе.

**Edge-cases:** таймаут «не ответили» (~45 c → CallMissed), занято, отклонение на одном устройстве, обрыв сети (LiveKit reconnect), параллельный звонок, отзыв сессии (SessionRevokedConsumer как в других сервисах).

---

## 4. Proto-контракт (`Shared/BarkFluff.Proto/calls_api.proto`)

```protobuf
syntax = "proto3";

option csharp_namespace = "BarkFluff.Proto.Calls";

import "google/protobuf/timestamp.proto";

package barkfluff.calls;

// ── Инициация ──────────────────────────────────────────────
message InitiateCallRequest {
  // Ровно одно из двух: 1-на-1 (callee_user_id) или групповой (chat_id).
  oneof target {
    int64  callee_user_id = 1; // Личный звонок конкретному пользователю
    string chat_id        = 2; // Групповой звонок в чат (Guid-строка)
  }
  CallMediaType media_type = 3; // AUDIO / VIDEO (старт с включённой камерой)
}

message InitiateCallResponse {
  string call_id      = 1; // ID звонка (Guid)
  string livekit_url  = 2; // WSS-адрес LiveKit (дублируется в Beacon)
  string access_token = 3; // LiveKit JWT для входа инициатора в комнату
}

// ── Присоединение к идущему звонку (group / второй девайс) ──
message JoinCallRequest { string call_id = 1; }
message JoinCallResponse {
  string livekit_url  = 1;
  string access_token = 2;
}

// ── Ответ / отклонение / завершение ────────────────────────
message AcceptCallRequest { string call_id = 1; }
message AcceptCallResponse {
  string livekit_url  = 1;
  string access_token = 2;
}

message RejectCallRequest { string call_id = 1; }
message RejectCallResponse { }

message EndCallRequest { string call_id = 1; }
message EndCallResponse { }

// ── Подписка на события звонка (device-scope) ──────────────
message SubscribeCallEventsRequest { }

message CallEvent {
  oneof event {
    IncomingCallEvent incoming = 1; // Входящий звонок (ring)
    CallAcceptedEvent accepted = 2; // Принят (гасим ring на др. устройствах / уведомляем caller)
    CallRejectedEvent rejected = 3; // Отклонён
    CallEndedEvent    ended    = 4; // Завершён / пропущен
    ParticipantEvent  member   = 5; // Кто-то вошёл/вышел (для группового UI)
  }
}

message IncomingCallEvent {
  string call_id          = 1;
  int64  caller_user_id   = 2;
  string chat_id          = 3; // непустой для группового звонка
  CallMediaType media_type = 4;
  google.protobuf.Timestamp started_at = 5;
}

message CallAcceptedEvent { string call_id = 1; int64 accepted_by_user_id = 2; }
message CallRejectedEvent { string call_id = 1; int64 rejected_by_user_id = 2; }

message CallEndedEvent {
  string call_id = 1;
  CallEndReason reason = 2;
  int64  duration_seconds = 3; // 0 для несостоявшихся
}

message ParticipantEvent {
  string call_id = 1;
  int64  user_id = 2;
  ParticipantAction action = 3; // JOINED / LEFT
}

enum CallMediaType {
  CALL_MEDIA_TYPE_UNKNOWN = 0;
  CALL_MEDIA_AUDIO = 1;
  CALL_MEDIA_VIDEO = 2;
}

enum CallEndReason {
  CALL_END_REASON_UNKNOWN = 0;
  CALL_END_HANGUP   = 1; // Завершён участником
  CALL_END_REJECTED = 2; // Отклонён
  CALL_END_MISSED   = 3; // Никто не ответил (таймаут)
  CALL_END_BUSY     = 4; // Получатель занят
  CALL_END_FAILED   = 5; // Сетевой/медиа-сбой
}

enum ParticipantAction {
  PARTICIPANT_ACTION_UNKNOWN = 0;
  PARTICIPANT_JOINED = 1;
  PARTICIPANT_LEFT   = 2;
}

service CallsApi {
  rpc InitiateCall(InitiateCallRequest) returns (InitiateCallResponse);
  rpc JoinCall(JoinCallRequest) returns (JoinCallResponse);
  rpc AcceptCall(AcceptCallRequest) returns (AcceptCallResponse);
  rpc RejectCall(RejectCallRequest) returns (RejectCallResponse);
  rpc EndCall(EndCallRequest) returns (EndCallResponse);

  // Device-scope: подписка живёт до отмены, как SubscribeSecretMessages в Updates
  rpc SubscribeCallEvents(SubscribeCallEventsRequest) returns (stream CallEvent);
}
```

Все методы — `[Authorize(Policy = nameof(TokenType.User))]`; `SubscribeCallEvents` дополнительно требует device-id в JWT-claim (как device-scope стримы Updates).

---

## 5. Пошаговый план (фазы с критериями проверки)

**Фаза 0 — Инфраструктура LiveKit**
1. `livekit/livekit-server` в `docker-compose-dev.yml` + конфиг (api_key/secret, Redis, TURN-порты UDP). → проверка: `livekit-cli` подключается, тестовая комната создаётся.
2. `ServiceId.Calls`, регистрация в Settings, провижн ключей. → проверка: сервис стартует и тянет конфиг.

**Фаза 1 — Backend, 1-на-1**
3. `calls_api.proto` (раздел 4) + подключение в `BarkFluff.Proto.csproj` (Server) и клиентских проектах. → проверка: генерится, solution билдится.
4. Скелет `BarkFluff.Calls` по структуре микросервиса (Domain/Features/Host/Persistence) + XAuth + Serilog + Metrics + EF Core (таблица `CallSessions` / CDR). → проверка: `dotnet build`, миграция применяется.
5. Выдача LiveKit-токена (подпись JWT секретом LiveKit, grants на room). → проверка: токеном реально входишь в комнату.
6. `InitiateCall/Accept/Reject/End` + device-scope `SubscribeCallEvents` + RabbitMQ-события (паттерн Updates). → проверка: тест «A звонит → B видит IncomingCall → Accept → оба в комнате → End → CallEnded».
7. LiveKit webhooks → обновление состояния, missed-таймаут, системное сообщение в Messages. → проверка: пропущенный и завершённый звонок дают корректный CDR + системное сообщение.

**Фаза 2 — Групповые звонки**
8. `InitiateCall(chat_id)` + `JoinCall` + авторизация через `CheckChatMembership`. → проверка: 3+ участника в одной комнате, late-join работает.

**Фаза 3 — Push и доставка ринга**
9. CloudMessaging: high-priority/VoIP push при `IncomingCall` (CallKit на iOS, ConnectionService на Android). → проверка: входящий поднимается при убитом приложении.
10. Beacon: `livekit_url` в `GetServerInfoResponse`. → проверка: клиент получает адрес без хардкода.

**Фаза 4 — Клиенты** (по платформам, параллельно)
11. LiveKit SDK + экран звонка + incoming-overlay + мини-карточка: Android (`io.livekit:livekit-android`), iOS/macOS (`LiveKitClient` Swift), WPF (нативный модуль/WebView2 — см. риск), Linux/Qt — позже. → проверка: реальный аудио+видео звонок между двумя платформами.

**Фаза 5 — Документация**
12. Обновить `Obsidian/ClaudeVault/`: новый `Backend/Calls.md`, ссылка в `Index.md`, отметка в `Идеи/01-Calls.md`, расширения в `Updates.md`/`CloudMessaging.md`/`Beacon.md`.

---

## 6. Риски

1. **WPF-клиент.** Официального .NET/WPF SDK у LiveKit нет. Варианты: (а) нативный WebRTC-модуль + ручной LiveKit-протокол, (б) WebView2 с livekit-client (JS), (в) отложить звонки на WPF. Самый большой неизвестный — решить отдельно.
2. **iOS VoIP push.** Без PushKit+CallKit входящий звонок на убитом приложении не поднять — обязательная часть Фазы 3.
3. **Сетевые порты/TURN.** LiveKit требует проброс UDP-диапазона и публичный адрес; согласовать с nginx/инфраструктурой.
4. **Версия LiveKit ↔ SDK** должны совпадать по всем платформам.
