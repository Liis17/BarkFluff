# BarkFluff.Shared.Queue

Разделяемая библиотека (.NET 9.0) с типами событий для RabbitMQ (MassTransit). Только POCO-классы, без логики.

Расположение: `Shared/BarkFluff.Shared.Queue/`

## Namespace: `BarkFluff.Shared.Queue.Messages`

События чатов (обычные, plaintext):
- `NewMessageEvent` — новое сообщение (ChatId, ChatMembers, Message как `byte[]`)
- `MessageReadEvent` — сообщение прочитано (ChatId, MessageId, NewReadBy, ChatMembers)
- `MessageEditedEvent` — отредактированное сообщение (ChatId, ChatMembers, Message как `byte[]`)
- `MessageDeletedEvent` — удалённое сообщение (ChatId, ChatMembers, MessageId)
- `MessagePinnedEvent` / `MessageUnpinnedEvent` / `AllMessagesUnpinnedEvent` — закрепы
- `ReadReceiptEvent` — подтверждение прочтения (ChatId, MessageId, ReadBy, IsLastMessage, ReadAt)
- `PushNotificationEvent` — данные для push-уведомления (отправитель, чат, превью)
- `DismissPushEvent` — отзыв push (когда сообщение прочитано/удалено)
- `AdminBroadcastNotificationEvent` — админ-рассылка push (Title, Body, ImageUrl, TargetDeviceIds). Публикуется из [[Backend/AdminPanel|AdminPanel]] страницы «Уведомления», потребляется [[Backend/CloudMessaging|CloudMessaging]] (очередь `admin-broadcast-handler`).

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
