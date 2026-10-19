# Barkfluff.CloudMessaging

Worker для RabbitMQ → Firebase Cloud Messaging. Program.cs регистрирует восемь consumers и их очереди: новые push, dismiss чата/звонка, admin broadcast, incoming call, private-chat invite, encrypted message и secret message. Список: [[Backend/CloudMessaging-ProjectMap]].

Public gRPC API не маппится. ASP.NET listener настраивается через SetRunningAddress, но Program.cs не регистрирует HTTP routes/health endpoints; обработка бизнес-событий идёт через RabbitMQ.

## Payload и границы приватности

Обычный new_message Android payload включает metadata и preview текста до 100 символов. Для Web формируется отдельный data-only payload без message body, image preview и attachment content. Dismiss, call и invite события также получают platform-specific payload.

Encrypted и secret message consumers отбирают не-Web токены и отправляют только идентификаторы/метаданные события — ciphertext/envelope в push не включается. Admin broadcast Android получает notification title/body; Web payload содержит только type=admin_broadcast.

FCM принимает не более 500 токенов на multicast-запрос. На 500 токенов явно разбивают encrypted push и admin broadcast; остальные методы передают список одним вызовом, поэтому их вызывающая сторона должна соблюдать лимит. Успех означает принятие запроса Firebase, не подтверждённую доставку устройству. Незарегистрированные токены логируются для очистки, но автоматическое удаление отмечено TODO. Метрики: push_jobs_received, push_target_devices, fcm_pushes_sent/failed.

Firebase credentials, RabbitMQ и service tokens поступают из Settings; сервис вызывает Users и Messages APIs. Сборка: dotnet build Backend/Barkfluff.CloudMessaging/Barkfluff.CloudMessaging.csproj.
