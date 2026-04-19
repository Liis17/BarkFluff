# BarkFluff.Shared.Queue

Разделяемая библиотека (.NET 9.0) с типами событий для RabbitMQ (MassTransit). Только POCO-классы, без логики.

Расположение: `Shared/BarkFluff.Shared.Queue/`

## Namespace: `BarkFluff.Shared.Queue.Messages`

События чатов:
- `NewMessageEvent` — новое сообщение (ChatId, ChatMembers, Message как `byte[]`)
- `MessageReadEvent` — сообщение прочитано (ChatId, MessageId, NewReadBy, ChatMembers)
- `ReadReceiptEvent` — подтверждение прочтения (ChatId, MessageId, ReadBy, IsLastMessage, ReadAt)
- `PushNotificationEvent` — данные для push-уведомления (отправитель, чат, превью)

## Namespace: `BarkFluff.Shared.Queue.Notifications`

- `Notification` (abstract) — базовый: TransportId, ServiceId, OwnerId, CreatedAt, Type, Payload
- `EmailNotification : Notification` — добавляет Title и Address
- `NotificationType` — enum типов (ConfirmationRegistration, ResetPassword, FailedLogin, ...)
- `TransportId` — транспорт доставки (Email=1)

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
