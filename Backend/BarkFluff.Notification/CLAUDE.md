# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Описание

Микросервис `Notification` — фоновый потребитель RabbitMQ, отправляющий email-уведомления пользователям BarkFluff. Не имеет gRPC API — только обработка очереди.

## Сборка и запуск

```bash
# Из корня репозитория — запустить все сервисы
cd Backend && docker-compose -f docker-compose-dev.yml up -d

# Собрать только этот сервис
dotnet build Backend/BarkFluff.Notification/BarkFluff.Notification.csproj

# Локальный запуск (требует конфигурации SMTP и RabbitMQ)
dotnet run --project Backend/BarkFluff.Notification/BarkFluff.Notification.csproj
```

## Архитектура

Сервис является **stateless фоновым воркером** без БД и без gRPC-сервера. Поток обработки:

```
RabbitMQ queue: notifications-email-handler
    → EmailQueueConsumer (IConsumer<EmailNotification>)
        → HtmlEmailTemplateParser  (загружает Templates/*.html, заменяет переменные)
        → EmailSender              (отправляет через System.Net.Mail.SmtpClient)
```

## Ключевые компоненты

**`Consumers/EmailQueueConsumer.cs`** — единственный entrypoint. Получает `EmailNotification` из RabbitMQ, при ошибке перебрасывает исключение (MassTransit отвечает за retry).

**`Senders/EmailSender.cs`** — SMTP-клиент с SSL. Отключает проверку TLS-сертификата через `ServicePointManager.ServerCertificateValidationCallback` (намеренно, для self-signed).

**`Parsers/HtmlEmailTemplateParser.cs`** — загружает HTML-шаблон из `Templates/` по `NotificationType`, заменяет плейсхолдеры вида `ꟿꟿꟿvariableNameꟿꟿꟿ` значениями из `EmailNotification.Payload`. Специальный плейсхолдер `ꟿꟿꟿcurrentyearꟿꟿꟿ` заменяется автоматически.

**`Configurations/EmailConfiguration.cs`** — POCO конфигурации SMTP (`Host`, `Port`, `SenderEmail`, `SenderPassword`). Загружается из Configuration-сервиса под ключом `Email`.

## Шаблоны и типы уведомлений

Каждый `NotificationType` из `BarkFluff.Shared.Queue` связан с HTML-файлом:

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

1. Добавить значение в `NotificationType` enum в `Shared/BarkFluff.Shared.Queue`
2. Создать HTML-шаблон в `Templates/` с плейсхолдерами `ꟿꟿꟿnameꟿꟿꟿ`
3. Добавить маппинг в `_templatesMap` в `HtmlEmailTemplateParser.cs`
4. Добавить `<None Include="Templates\new_template.html"><CopyToOutputDirectory>Always</CopyToOutputDirectory></None>` в `.csproj`

## Метрики

Сервис инкрементирует счётчики через `MetricsCollector`:
- `rabbitmq_events_consumed` — при получении сообщения
- `emails_sent` — при успешной отправке
- `emails_failed` — при ошибке

## Конфигурация

| Ключ | Описание |
|------|----------|
| `Email:Host` | SMTP-сервер |
| `Email:Port` | SMTP-порт |
| `Email:SenderEmail` | Адрес отправителя |
| `Email:SenderPassword` | Пароль SMTP |
| `RabbitMQ:Host` | Хост RabbitMQ |
| `RabbitMQ:Username` / `RabbitMQ:Password` | Учётные данные RabbitMQ |

Сервис слушает на порту **7004**, конфигурация загружается с `http://localhost:7003` (Configuration service).
