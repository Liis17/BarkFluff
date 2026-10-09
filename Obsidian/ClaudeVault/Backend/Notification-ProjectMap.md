# BarkFluff.Notification — карта проекта

Основное поведение: [[Backend/Notification]]. Исходники: Backend/BarkFluff.Notification/.

- Program.cs загружает Settings, настраивает HTTP listener и shared readiness, затем RabbitMQ consumer.
- Consumers/EmailQueueConsumer.cs — обрабатывает сообщения из notifications-email-handler.
- Senders/EmailSender.cs и Parsers/HtmlEmailTemplateParser.cs — SMTP и разбор HTML шаблонов.
- Configurations/EmailConfiguration.cs, appsettings.json и appsettings.Development.json — параметры.
- Templates/ — HTML письма для текущих типов событий.

Сервис не хранит собственную БД и не публикует gRPC API. Он запускает HTTP health endpoints вместе с worker.
