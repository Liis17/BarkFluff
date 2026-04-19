# BarkFluff.Notification

Фоновый потребитель RabbitMQ, отправляющий email-уведомления. Порт: **7004**.
**Нет gRPC API** — только обработка очереди.

Расположение: `Backend/BarkFluff.Notification/`

## Сборка

```bash
dotnet build Backend/BarkFluff.Notification/BarkFluff.Notification.csproj
```

## Архитектура

Stateless фоновый воркер без БД. Поток обработки:

```
RabbitMQ: notifications-email-handler
  → EmailQueueConsumer (IConsumer<EmailNotification>)
      → HtmlEmailTemplateParser  (Templates/*.html, заменяет переменные)
      → EmailSender              (System.Net.Mail.SmtpClient)
```

## Ключевые компоненты

- **`EmailQueueConsumer`** — единственный entrypoint. При ошибке перебрасывает исключение (MassTransit retry).
- **`EmailSender`** — SMTP с SSL. Отключает проверку TLS-сертификата через `ServicePointManager.ServerCertificateValidationCallback` (намеренно, для self-signed).
- **`HtmlEmailTemplateParser`** — загружает HTML из `Templates/` по `NotificationType`, заменяет плейсхолдеры `ꟿꟿꟿvariableNameꟿꟿꟿ`. Спецплейсхолдер `ꟿꟿꟿcurrentyearꟿꟿꟿ` — автоматически.

## Шаблоны и типы уведомлений

| NotificationType | Файл шаблона |
|-----------------|--------------|
| ConfirmationRegistration | `confirmation_account.html` |
| ConfirmationOtpEmail | `confirmation_otp_email.html` |
| ConfirmationAuth | `confirmation_auth.html` |
| ResetPassword | `reset_password.html` |
| FailedLogin | `failed_login.html` |
| SuccessfulRegistration | `successful_registration.html` |
| SuccessfulLogin | `successful_login.html` |
| PasswordChanged | `password_changed.html` |
| TwoFactorMethodChanged | `two_factor_method_changed.html` |

## Добавление нового типа уведомления

1. Добавить значение в `NotificationType` в [[Shared/Queue]]
2. Создать HTML-шаблон в `Templates/` с плейсхолдерами `ꟿꟿꟿnameꟿꟿꟿ`
3. Добавить маппинг в `_templatesMap` в `HtmlEmailTemplateParser.cs`
4. Добавить `<None Include="Templates\new_template.html"><CopyToOutputDirectory>Always</CopyToOutputDirectory></None>` в `.csproj`

## Метрики

- `rabbitmq_events_consumed` — получение сообщения
- `emails_sent` — успешная отправка
- `emails_failed` — ошибка

## Конфигурация

| Ключ | Описание |
|------|----------|
| `Email:Host/Port/SenderEmail/SenderPassword` | SMTP |
| `RabbitMQ:Host/Username/Password` | RabbitMQ |
