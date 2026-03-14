# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Service Does

CloudMessaging — фоновый сервис (Background Worker) для отправки push-уведомлений через Firebase Cloud Messaging. Не обслуживает gRPC-запросы напрямую — потребляет события `PushNotificationEvent` из RabbitMQ (очередь `push-notifications-handler`) и отправляет data-only FCM-сообщения на устройства получателей.

## Build

```bash
dotnet build Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj
```

Docker (из корня репозитория):
```bash
docker-compose -f Backend/docker-compose-dev.yml up -d cloudmessaging
```

## Architecture

Сервис состоит из трёх ключевых компонентов:

1. **Program.cs** — startup: загрузка конфигурации из Configuration service (`ServiceId.CloudMessaging`), регистрация gRPC-клиентов (Users, Messages) и MassTransit consumer.
2. **PushNotificationConsumer** — MassTransit consumer для `PushNotificationEvent`. При получении события параллельно запрашивает данные отправителя (Users) и чата (Messages), затем рассылает push на все устройства получателей через FirebaseService.
3. **FirebaseService** — singleton-обёртка над Firebase Admin SDK. Инициализируется из конфигурации (`Firebase:ProjectId`, `Firebase:PrivateKey`, `Firebase:ClientEmail`). Отправляет **data-only** сообщения (без `Notification` блока), чтобы Android-клиент мог сам строить уведомления.

## Dependencies

- **gRPC-клиенты**: `UsersServerApi` (получение профилей, FCM-токенов устройств), `MessagesApi` (информация о чате)
- **RabbitMQ**: MassTransit, очередь `push-notifications-handler`
- **Firebase Admin SDK**: `FirebaseAdmin` 3.2.0
- **Proto-файлы**: `users_api.proto` (Client), `messages_api.proto` (Client), `shared.proto` (None)

## Configuration Keys

Загружаются из Configuration service при старте:
- `Firebase:ProjectId`, `Firebase:PrivateKeyId`, `Firebase:PrivateKey`, `Firebase:ClientEmail`, `Firebase:ClientId`
- `UsersService:Host`, `UsersService:Token`
- `MessagesService:Host`, `MessagesService:Token`
- `RabbitMQ:Host`, `RabbitMQ:Username`, `RabbitMQ:Password`

## Event Flow

```
Messages service → publishes PushNotificationEvent → RabbitMQ
    → push-notifications-handler queue
    → PushNotificationConsumer
    → parallel: Users.GetById + Messages.GetChatInfo
    → Users.GetDevicesWithFirebaseTokens
    → FirebaseService.SendNotificationAsync (per device)
```
