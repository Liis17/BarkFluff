# BarkFluff.Shared.Queue

Разделяемая библиотека (.NET 10.0) с типами событий для RabbitMQ (MassTransit). Только POCO-классы, без логики.

Расположение: `Shared/BarkFluff.Shared.Queue/`

## Namespace: `BarkFluff.Shared.Queue.Federation`

- `FederatedParticipant` — `{ Guid Uuid, string ServerName }` (этап 2.2). Используется расширенными полями message-events (см. ниже) для построения FederationOutbox-строк.
- `FederatedFileRefInfo` — снапшот метаданных вложения fed-сообщения (этап 3.1): `{ OriginServer, FileId, FileName?, SizeBytes, AttachmentType, PreviewFileId?, ImageWidth?, ImageHeight? }`. **Байты не реплицируются** — уезжают только метаданные, чтобы принимающая нода отрисовала сообщение без похода на origin. Заполняет [[Backend/Messages]] только для fed-чатов; консюмеры [[Backend/Federation]] маппят это в `FederatedFileRef` S2S-события.
- `FederatedChatRejectedEvent` — `{ Guid ChatId, string Reason }` (этап 2.5). Publisher — [[Backend/Federation]] (`OutboxDispatcher`, DeadLetter с `ErrorCode="FederatedDmRejected"`); consumer — [[Backend/Messages]] (`FederatedChatRejectedConsumer`, очередь `federated-chat-rejected-messages`) → `Chat.FederatedStatus = Rejected`.

## Расширенные федеративные поля message-событий (этап 2.2)

`NewMessageEvent`, `MessageEditedEvent`, `MessageDeletedEvent`, `MessageReadEvent` получили nullable-поля федеративного контекста. Старые сообщения очереди десериализуются без них (обратная совместимость сохраняется). Заполняет [[Backend/Messages]] начиная с этапа 2.3; в 2.2 поля пустые.

| Поле | Тип | Назначение |
|---|---|---|
| `IsFederated` | `bool` | true → Federation строит outbox-строки |
| `RemoteParticipants` | `List<FederatedParticipant>` | по строке outbox на каждый ServerName |
| `FederatedId` | `Guid?` | UUID сообщения на origin (идемпотентность) |
| `SenderUuid` | `Guid?` | UUID remote-автора (для рендера на приёмнике) |
| `LastChangeAt` | `DateTimeOffset?` | база для LWW (SentAt/EditedAt) |
| `IsFirstMessageInChat`, `InitiatorUuid`, `InviteeUuid` | — | только `NewMessageEvent`: построение `ChatCreated` без похода в Messages |
| `SenderFid` | `string?` | `NewMessageEvent`/`MessageEditedEvent`: FID отправителя (для `ChatCreated` и пушей [[Backend/CloudMessaging]]). `SenderDisplayName` (имя) заводится в 2.8 при потреблении |
| `FederatedAttachments` | `List<FederatedFileRefInfo>?` | `NewMessageEvent`/`MessageEditedEvent` (этап 3.1): снапшот метаданных вложений. Для правки список **всегда пересоздаётся целиком**; null/пусто = вложений нет |
| `ReaderUuid`, `UpToFederatedMessageId` | `Guid?` | только `MessageReadEvent` |

## Namespace: `BarkFluff.Shared.Queue.Messages`

События чатов (обычные, plaintext):
- `NewMessageEvent` — новое сообщение (ChatId, ChatMembers, Message как `byte[]`)
- `MessageReadEvent` — сообщение прочитано (`ChatId`, `MessageId`, `NewReadBy` — полный snapshot читателей для realtime-клиентов, `NewReaders` — delta новых читателей для отмены/dismiss push, `ChatMembers`). При чтении старых событий без `NewReaders` Updates использует `NewReadBy` как fallback.
- `MessageEditedEvent` — отредактированное сообщение (ChatId, ChatMembers, Message как `byte[]`)
- `MessageDeletedEvent` — удалённое сообщение (ChatId, ChatMembers, MessageId)
- `MessagePinnedEvent` / `MessageUnpinnedEvent` / `AllMessagesUnpinnedEvent` — закрепы
- `ReadReceiptEvent` — подтверждение прочтения (ChatId, MessageId, ReadBy, IsLastMessage, ReadAt)
- `PushNotificationEvent` — данные для push-уведомления (отправитель, чат, превью)
- `DismissPushEvent` — отзыв push (когда сообщение прочитано/удалено)
- `AdminBroadcastNotificationEvent` — админ-рассылка push (Title, Body, ImageUrl, TargetDeviceIds). Публикуется из [[Backend/AdminPanel|AdminPanel]] страницы «Уведомления», потребляется [[Backend/CloudMessaging|CloudMessaging]] (очередь `admin-broadcast-handler`).
- `IncomingCallPushEvent` — входящий звонок для high-priority FCM push (CallId, CallerUserId, RecipientUserIds, ChatId — null для личного, MediaType, StartedAt). Публикуется [[Backend/Calls]] при инициации звонка, потребляется [[Backend/CloudMessaging|CloudMessaging]].
- `CallDismissPushEvent` — погасить push входящего звонка на всех устройствах получателей (CallId, RecipientUserIds, Reason: accepted/rejected/ended/timeout/busy). Публикуется [[Backend/Calls]] при завершении ринга, потребляется [[Backend/CloudMessaging|CloudMessaging]].

События приватных чатов (E2E через passphrase, user-scope):
- `NewEncryptedMessageEvent` — новое шифрованное сообщение (ChatId, ChatMembers, Message как proto-bytes)
- `EncryptedMessageEditedEvent` — отредактированное шифрованное (ChatId, ChatMembers, Message)
- `EncryptedMessageDeletedEvent` — удалённое шифрованное (ChatId, ChatMembers, MessageId)
- `PrivateChatInviteEvent` — инвайт в приватный чат (ChatId, InviterUserId, InviteeUserId, KdfSalt, PassphraseVerifier, InvitedAt)
- `PrivateChatInviteResolutionEvent` — ответ на инвайт (ChatId, InviterUserId, InviteeUserId, Accepted)

События секретных чатов (Signal Double Ratchet, **device-scope** в Updates):
- `SecretChatInviteEvent` — инвайт секретного чата (InviteId, Sender User+Device, Recipient User+Device, InitialEnvelope, SentAt)
- `SecretChatInviteResolutionEvent` — ответ на инвайт (InviteId, Sender User+Device, Recipient User+Device, Accepted, ResponseEnvelope)
- `NewSecretMessageEvent` — opaque envelope (MessageId, Sender User+Device, Recipient User+Device, Envelope, SentAt)

## Namespace: `BarkFluff.Shared.Queue.Notifications`

- `Notification` (abstract) — базовый: TransportId, ServiceId, OwnerId, CreatedAt, Type, Payload
- `EmailNotification : Notification` — добавляет Title и Address
- `NotificationType` — enum: `Unknown=0`, `ConfirmationRegistration=1`, `ConfirmationOtpEmail=2`, `ConfirmationAuth=3`, `ResetPassword=4`, `FailedLogin=5`, `SuccessfulRegistration=6`, `SuccessfulLogin=7`, `PasswordChanged=8`, `TwoFactorMethodChanged=9`, **`PasswordChangedByAdmin=10`** (новый, см. [[Backend/Notification]] и `ForceSetPasswordServer` в [[Backend/Identity]])
- `TransportId` — транспорт доставки (Email=1)

## Namespace: `BarkFluff.Shared.Queue.Identity`

- `SessionRevokedEvent` — отзыв сессии: `UserId`, `DeviceId`, `AccessTokenExpiresAt`. Публикуется [[Backend/Identity]] при revoke сессии, потребляется всеми сервисами с XAuth ([[Backend/Onliner]], [[Backend/Messages]], ...) для invalidation `TokenRevocationCache`.

## Namespace: `BarkFluff.Shared.Queue.Users`

События профиля, потребляет [[Backend/Messages]] для Redis-кеша:
- `UserChangedAvatar` — URL аватара (основной + превью)
- `UserChangedName` — имя и фамилия
- `UserChangedUsername` — username
- `UserChangedBio` — bio
- `UserChangedPassword` — факт смены (только UserId)

## Паттерн использования

**Публикация:**
```csharp
await _publishEndpoint.Publish(new NewMessageEvent { ChatId = ..., ... });
```

**Потребление:**
```csharp
public class NewMessageConsumer : IConsumer<NewMessageEvent>
{
    public async Task Consume(ConsumeContext<NewMessageEvent> context) { ... }
}
// Регистрация: x.AddConsumer<NewMessageConsumer>();
```

## Добавление нового события

Добавить новый POCO-класс в соответствующий namespace. Нет базовых классов для Messages/Users событий — просто POCO.

## Федеративный контекст событий (этап 2.2/2.3)

Расширенные поля на существующих событиях — обратно совместимо (старые консюмеры игнорируют их, десериализация не ломается). Поля заполняются Messages **только для fed-чатов**.

- `NewMessageEvent`: `IsFederated`, `List<FederatedParticipant> RemoteParticipants`, `Guid? FederatedId`, `Guid? SenderUuid`, `DateTimeOffset? LastChangeAt`, `bool IsFirstMessageInChat`, `Guid? InitiatorUuid`, `Guid? InviteeUuid`, `string? SenderFid` (`@username:server`).
- `MessageEditedEvent` / `MessageDeletedEvent`: тот же набор (без `IsFirstMessageInChat/InitiatorUuid/InviteeUuid`).
- `MessageReadEvent`: `IsFederated`, `RemoteParticipants`, `Guid? ReaderUuid`, `Guid? UpToFederatedMessageId`, `DateTimeOffset? LastChangeAt`.

`FederatedParticipant { Guid Uuid; string ServerName }` — remote-участник fed-чата на чужой ноде. Список нужен консюмеру Federation для формирования `destinations` outbox-строк (по одной на ноду).

`MessageQueueSender` (`BarkFluff.Messages.Infrastructure`) держит 3 варианта публикации:
- `SendMessage(...)` — обычный путь, fed-поля default.
- `SendFederatedMessage(...)` — исходящий fed-путь (создание чата + сообщение); заполняет fed-поля, консюмер Federation кладёт в outbox.
- `SendImportedMessage(...)` — импортированное сообщение (от ноды-партнёра): `IsFederated=true`, но `RemoteParticipants=пусто` — консюмер Federation не входит в publish-ветку (сообщение пришло снаружи, наружу не пересылается).
