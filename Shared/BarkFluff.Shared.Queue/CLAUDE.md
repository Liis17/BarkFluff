# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Purpose

`BarkFluff.Shared.Queue` — разделяемая библиотека (.NET 9.0) с типами событий для RabbitMQ (MassTransit). Используется всеми микросервисами, которым нужно публиковать или потреблять асинхронные сообщения.

## Build

```bash
dotnet build BarkFluff.Shared.Queue.csproj
```

## Architecture

Библиотека содержит только POCO-классы событий, без логики. Разделена на три пространства имён:

### `BarkFluff.Shared.Queue.Messages`
События, связанные с сообщениями в чатах:
- `NewMessageEvent` — новое сообщение (ChatId, ChatMembers, сериализованный Message как `byte[]`)
- `MessageReadEvent` — сообщение прочитано (ChatId, MessageId, NewReadBy, ChatMembers)
- `ReadReceiptEvent` — подтверждение прочтения (ChatId, MessageId, ReadBy, ChatMembers, IsLastMessage, ReadAt)
- `PushNotificationEvent` — данные для push-уведомления (отправитель, чат, превью контента)

### `BarkFluff.Shared.Queue.Notifications`
Абстракция для email-уведомлений:
- `Notification` (abstract) — базовый класс: TransportId, ServiceId, OwnerId, CreatedAt, Type, Payload
- `EmailNotification : Notification` — добавляет Title и Address
- `NotificationType` — перечисление типов (ConfirmationRegistration, ResetPassword, FailedLogin и др.)
- `TransportId` — транспорт доставки (сейчас только Email=1)

### `BarkFluff.Shared.Queue.Users`
События изменения профиля пользователя, потребляемые сервисом Updates:
- `UserChangedAvatar` — новые URL аватара (основной + превью)
- `UserChangedName` — новые имя и фамилия
- `UserChangedUsername` — новый username
- `UserChangedBio` — новое bio
- `UserChangedPassword` — факт смены пароля (только UserId)

## Dependencies

- `BarkFluff.Shared.Identity` — для `ServiceId` enum в `Notification.ServiceId`

## Usage Pattern

**Публикация (MassTransit):**
```csharp
await _publishEndpoint.Publish(new NewMessageEvent { ChatId = ..., ChatMembers = ..., Message = ... });
```

**Потребление:**
```csharp
public class NewMessageConsumer : IConsumer<NewMessageEvent>
{
    public async Task Consume(ConsumeContext<NewMessageEvent> context) { ... }
}
// Регистрация:
x.AddConsumer<NewMessageConsumer>();
```

## Adding New Events

Добавить новый класс в соответствующий namespace (`Messages`, `Notifications`, или `Users`). Нет базовых классов или интерфейсов для событий Messages/Users — просто POCO.
