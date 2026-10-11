# BarkFluff.Notification

RabbitMQ worker email notifications. Не публикует gRPC API; в Program.cs есть ASP.NET HTTP listener с /ping, /health/live и /health/ready. `SetRunningAddress` явно настраивает Kestrel из `RunSettings:Port` (appsettings default 7004); launchSettings объявляет 5017/7034, но не задаёт фактические listeners. Карта: [[Backend/Notification-ProjectMap]].

Consumer notifications-email-handler принимает EmailNotification. Если Email:Enabled=false или Address пуст, письмо пропускается; Disabled почта допускает пустой SMTP port. Email:Enabled default true. SMTP-поля поступают из [[Backend/Settings]], письма собираются из Templates/ и отправляются через EmailSender.

Identity отправляет SuccessfulLogin/FailedLogin через этот сервис только если выбран Email канал; при Telegram настройке эти login notices идут напрямую из Identity node bot. Другие email события также используют очередь.

Важное текущее ограничение: EmailSender задаёт ServicePointManager.ServerCertificateValidationCallback, принимающий любой сертификат. В коде причина и ограничение на self-signed certificates не заданы, поэтому не считайте SMTP TLS проверенным.

Метрики: rabbitmq_events_consumed, emails_skipped, emails_sent, emails_failed. Ошибка SMTP пробрасывается обратно MassTransit для retry.

Сборка: dotnet build Backend/BarkFluff.Notification/BarkFluff.Notification.csproj.
