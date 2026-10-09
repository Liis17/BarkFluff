# BarkFluff.Shared.Queue

Контракты событий RabbitMQ для MassTransit: `Shared/BarkFluff.Shared.Queue/`. Библиотека хранит POCO-типы; публикацию и обработку реализуют backend-сервисы. Поля добавляйте совместимо с уже опубликованными сообщениями.

## Messages

Обычные события содержат chat/user IDs и сериализованный `barkfluff.shared.Message` там, где нужен полный снимок. `NewMessageEvent.EventId` — стабильный ID события для корреляции at-least-once доставки. `MessageReadEvent.NewReadBy` — полный снимок прочитавших, `NewReaders` — новые читатели. `ChatHiddenEvent.ContentWiped` сообщает клиентам, нужно ли очистить локальную историю; `ChatMemberKickedEvent` адресует исключённого участника.

Push-контракты:
- `PushNotificationEvent` несёт preview и данные чата; payload privacy policy задаётся consumers [[Backend/CloudMessaging]].
- В `DismissPushEvent` положительный `MessageId` задаёт прочитанную границу, а 0 сохраняет legacy-отзыв push для всего чата.
- Пустой `AdminBroadcastNotificationEvent.TargetDeviceIds` означает рассылку на все устройства с FCM-токеном; непустой список ограничивает аудиторию.
- `IncomingCallPushEvent` и `CallDismissPushEvent` запускают звонковый push и его отмену.

Private-chat события (`NewEncryptedMessageEvent`, edit/delete, invite/resolution, `PrivateMessagesReadEvent`) доставляются пользователям; message bytes — сериализованный `EncryptedMessage`. Secret-chat invite/resolution/message события содержат opaque envelope и адресованы конкретным device IDs. Серверный стрим secret-событий device-scoped; envelope не интерпретируется Updates/CloudMessaging, а push не включает ciphertext. См. [[Backend/Updates]] и [[Backend/CloudMessaging]].

## Federation и присутствие

Для `NewMessageEvent`, `MessageEditedEvent`, `MessageDeletedEvent` и `MessageReadEvent` доступны федеративные поля: `IsFederated`, `RemoteParticipants`, `FederatedId`, `LastChangeAt`; набор дополнительных полей зависит от типа события. `NewMessageEvent` также несёт `ReplyToFederatedMessageId`. Поля с default-значениями совместимы со старыми сообщениями.

`FederatedParticipant` — UUID удалённого участника и `ServerName`. `FederatedFileRefInfo` — снимок имени/размера/типа/preview и размеров изображения; байты файла через это событие не реплицируются. `FederatedChatRejectedEvent` возвращает постоянный отказ создания чата в origin-ноду. `SigningKeyRotatedEvent` сигнализирует всем инстансам перечитать текущий signing key.

`OnlineStatusChangedEvent` расположен в этом проекте, но намеренно сохраняет namespace `BarkFluff.Onliner.Messages`: namespace входит в MassTransit URN, его смена нарушит совместимость сообщений между версиями. В событии: UserId, Status, LastSeen, опциональный UserUuid для remote-пользователя.

## Остальные контракты

- `SessionRevokedEvent`: UserId, DeviceId и срок действия access token; Identity публикует его для очистки XAuth revocation cache на сервисах.
- User profile events `UserChangedAvatar/Name/Username/Bio/Password` сообщают изменения подписчикам, в том числе Messages.
- `Notification` задаёт TransportId, ServiceId, OwnerId, CreatedAt, Type и string Payload; `EmailNotification` добавляет Title/Address. `TransportId.Email=1`; `NotificationType` значения 1–10 зарезервированы, включая `PasswordChangedByAdmin=10`.

Контракты событий и фактическую маршрутизацию см. в `Shared/BarkFluff.Shared.Queue/Messages/`, `Notifications/`, `Identity/`, `Users/`, `Federation/`, `Onliner/` и в соответствующих consumers.
