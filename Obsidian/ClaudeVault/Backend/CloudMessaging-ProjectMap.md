# Barkfluff.CloudMessaging — карта проекта

Основное описание, push payload и privacy ограничения: [[Backend/CloudMessaging]]. Исходники: Backend/Barkfluff.CloudMessaging/.

Program.cs регистрирует восемь RabbitMQ consumers:
PushNotificationConsumer, DismissPushConsumer, AdminBroadcastConsumer, IncomingCallPushConsumer, CallDismissPushConsumer, PrivateChatInvitePushConsumer, NewEncryptedMessagePushConsumer и NewSecretMessagePushConsumer. Очереди задаются там же; сервис не маппит публичный gRPC API.

Services/FirebaseService.cs строит FCM payload, пакетирует токены до 500 и отправляет отдельные payloads web-клиентам. Consumers получают device tokens через UsersServerApi и, для call/chat данных, читают MessagesServerApi. Контракты событий лежат в Shared/BarkFluff.Shared.Queue/Messages/.

Секретные и encrypted message события передают push только с метаданными, без ciphertext. Детали payload важнее списка файлов; прочие файлы — appsettings, Dockerfile и launchSettings.
