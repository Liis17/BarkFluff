# BarkFluff.Notification — Карта проекта

Фоновый воркер-потребитель RabbitMQ, отправляющий email-уведомления пользователям.
**Нет gRPC API**, нет БД — только обработка очереди.

Связанный файл: [[Backend/Notification]]

---

## Файлы проекта

### Точка входа

| Файл | Описание |
|------|----------|
| `Program.cs` | Точка входа. Регистрирует конфигурацию (`EmailConfiguration`), Serilog, метрики, MediatR, MassTransit с RabbitMQ-consumer (`EmailQueueConsumer`), `EmailSender`, `HtmlEmailTemplateParser`. Запускает ASP.NET Web-хост (без маршрутов — только воркер). |

### Consumers

| Файл | Описание |
|------|----------|
| `Consumers/EmailQueueConsumer.cs` | Единственный entrypoint приёма сообщений из очереди `notifications-email-handler`. Реализует `IConsumer<EmailNotification>` (MassTransit). Инкрементирует метрики `rabbitmq_events_consumed`, `emails_sent`, `emails_failed`. При ошибке пробрасывает исключение — MassTransit обрабатывает retry. |

### Senders

| Файл | Описание |
|------|----------|
| `Senders/EmailSender.cs` | Отправляет email через `System.Net.Mail.SmtpClient` (SSL). Получает HTML-тело письма через `HtmlEmailTemplateParser`. Намеренно отключает проверку TLS-сертификата через `ServicePointManager.ServerCertificateValidationCallback` для поддержки self-signed сертификатов. |

### Parsers

| Файл | Описание |
|------|----------|
| `Parsers/HtmlEmailTemplateParser.cs` | Загружает HTML-шаблон из папки `Templates/` по типу `NotificationType`. Заменяет плейсхолдеры вида `ꟿꟿꟿvariableNameꟿꟿꟿ` значениями из `Payload`. Спецплейсхолдер `ꟿꟿꟿcurrentyearꟿꟿꟿ` подставляется автоматически. |

### Configurations

| Файл | Описание |
|------|----------|
| `Configurations/EmailConfiguration.cs` | POCO-конфигурация SMTP: `Host`, `Port`, `SenderEmail`, `SenderPassword`. Биндится из секции `Email` конфигурации. |

### Шаблоны (Templates/)

| Файл | Тип уведомления |
|------|----------------|
| `Templates/confirmation_account.html` | `ConfirmationRegistration` — подтверждение регистрации |
| `Templates/confirmation_otp_email.html` | `ConfirmationOtpEmail` — OTP-код на email |
| `Templates/confirmation_auth.html` | `ConfirmationAuth` — подтверждение входа |
| `Templates/reset_password.html` | `ResetPassword` — сброс пароля |
| `Templates/failed_login.html` | `FailedLogin` — неудачная попытка входа |
| `Templates/successful_registration.html` | `SuccessfulRegistration` — успешная регистрация |
| `Templates/successful_login.html` | `SuccessfulLogin` — успешный вход |
| `Templates/password_changed.html` | `PasswordChanged` — смена пароля |
| `Templates/password_changed_by_admin.html` | `PasswordChangedByAdmin` — принудительная смена пароля администратором (см. `ForceSetPasswordServer` в [[Backend/Identity]]) |
| `Templates/two_factor_method_changed.html` | `TwoFactorMethodChanged` — смена метода 2FA |

### Конфигурационные файлы

| Файл | Описание |
|------|----------|
| `appsettings.json` | Базовая конфигурация: порт `7004`, адрес ConfigurationService `http://localhost:7003`. |
| `appsettings.Development.json` | Конфигурация для dev-окружения (SMTP, RabbitMQ). |
| `Properties/launchSettings.json` | Профили запуска для VS/dotnet run. |

### Docker

| Файл | Описание |
|------|----------|
| `Dockerfile.slim` | Образ для CI и production. |

### Прочее

| Файл | Описание |
|------|----------|
| `BarkFluff.Notification.csproj` | `net10.0`, зависимости: `MassTransit.RabbitMQ 8.5.2`, `Microsoft.AspNetCore.OpenApi 10.0.1`, проекты `BarkFluff.Shared.Queue`, `BarkFluff.GrpcServer`. |

---

## Поток обработки

```
RabbitMQ: notifications-email-handler
  → EmailQueueConsumer       (IConsumer<EmailNotification>)
      → HtmlEmailTemplateParser  (Templates/*.html, заменяет плейсхолдеры)
      → EmailSender              (SmtpClient, SSL)
```

---

## Зависимости

| Зависимость | Роль |
|-------------|------|
| [[Shared/Queue]] | `EmailNotification`, `NotificationType` |
| [[Backend/GrpcServer]] | `LoadConfiguration`, `AddBarkFluffSerilog`, `MetricsCollector`, `SetRunningAddress`, `AddBarkFluffMetrics` |
