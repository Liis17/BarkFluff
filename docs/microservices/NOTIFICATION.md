# Notification Microservice

## Назначение

Сервис Notification отвечает за **отправку уведомлений пользователям** в системе BarkFluff. Он управляет:

- 📧 Отправкой email-уведомлений через SMTP
- 📝 Рендерингом HTML-шаблонов для писем
- 🔔 Обработкой событий из RabbitMQ от других сервисов
- ✉️ Подтверждением регистрации, логина, сброса пароля
- 🌍 Определением геолокации для безопасных уведомлений

**Порт**: 7004
**База данных**: Не используется (stateless)
**Зависимости**: RabbitMQ (consumer), SMTP сервер, Configuration service

## Технологический стек

- **.NET 9.0**: Framework
- **RabbitMQ** (MassTransit): Потребление событий
- **MailKit**: SMTP клиент для отправки email
- **Razor Engine**: Рендеринг HTML шаблонов
- **HttpClient**: IP Geolocation API

## Архитектура

```
┌─────────────────────────────────────────────┐
│         Notification Service                 │
├─────────────────────────────────────────────┤
│  ┌──────────────┐      ┌─────────────────┐ │
│  │  RabbitMQ    │─────►│  Email Sender   │ │
│  │  Consumers   │      │   (MailKit)     │ │
│  └──────────────┘      └────────┬────────┘ │
│         │                       │          │
│         │                       │          │
│         ↓                       ↓          │
│  ┌──────────────┐      ┌─────────────────┐ │
│  │  Template    │      │  SMTP Server    │ │
│  │  Renderer    │      │  (External)     │ │
│  └──────────────┘      └─────────────────┘ │
└─────────────────────────────────────────────┘
```

## Основные компоненты

### EmailSender Service

**Интерфейс**:
```csharp
public interface IEmailSender
{
    Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody
    );
}
```

**Реализация** (Services/EmailSender.cs):
```csharp
public class EmailSender : IEmailSender
{
    private readonly SmtpSettings _smtpSettings;
    private readonly ILogger<EmailSender> _logger;

    public async Task SendEmailAsync(
        string to,
        string subject,
        string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            _smtpSettings.FromName,
            _smtpSettings.FromEmail
        ));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        var bodyBuilder = new BodyBuilder
        {
            HtmlBody = htmlBody
        };

        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();

        try
        {
            await client.ConnectAsync(
                _smtpSettings.Host,
                _smtpSettings.Port,
                SecureSocketOptions.StartTls
            );

            await client.AuthenticateAsync(
                _smtpSettings.Username,
                _smtpSettings.Password
            );

            await client.SendAsync(message);

            _logger.LogInformation(
                "Email sent to {To} with subject '{Subject}'",
                to,
                subject
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send email to {To}",
                to
            );
            throw;
        }
        finally
        {
            await client.DisconnectAsync(true);
        }
    }
}
```

### Template Renderer

**Назначение**: Рендеринг Razor шаблонов с подстановкой данных.

**Пример шаблона** (Templates/ConfirmationRegistration.cshtml):
```html
<!DOCTYPE html>
<html>
<head>
    <style>
        body { font-family: Arial, sans-serif; }
        .code { font-size: 24px; font-weight: bold; color: #007bff; }
        .info { color: #6c757d; margin-top: 20px; }
    </style>
</head>
<body>
    <h1>Добро пожаловать в BarkFluff!</h1>

    <p>Здравствуйте, @Model.Username!</p>

    <p>Ваш код подтверждения:</p>
    <p class="code">@Model.ConfirmationCode</p>

    <div class="info">
        <p><strong>Информация о входе:</strong></p>
        <ul>
            <li>IP: @Model.Ip</li>
            <li>Устройство: @Model.DeviceName</li>
            <li>Операционная система: @Model.Os</li>
            <li>Местоположение: @Model.Location</li>
            <li>Дата: @Model.DateTime</li>
        </ul>
    </div>

    <p>Если это были не вы, проигнорируйте это письмо.</p>
</body>
</html>
```

## Ключевые функции

### 1. Обработка Email Notification Events

**Базовое событие**:
```csharp
public class EmailNotification
{
    public string Title { get; set; }          // Тема письма
    public string Address { get; set; }         // Email получателя
    public string Type { get; set; }            // Тип уведомления
    public Dictionary<string, string> Payload { get; set; }  // Данные для шаблона
}
```

**Типы уведомлений**:

| Type | Источник | Шаблон | Описание |
|------|----------|--------|----------|
| **ConfirmationRegistration** | Identity | ConfirmationRegistration.cshtml | Подтверждение регистрации |
| **ConfirmationAuth** | Identity | ConfirmationAuth.cshtml | Подтверждение входа (Email OTP) |
| **ConfirmationOtpEmail** | Identity | ConfirmationOtpEmail.cshtml | Включение Email 2FA |
| **ResetPassword** | Identity | ResetPassword.cshtml | Сброс пароля |

### 2. ConfirmationRegistration Consumer

**RabbitMQ Queue**: `notifications-email-handler`

**Payload**:
```json
{
  "Title": "Подтверждение регистрации",
  "Address": "user@example.com",
  "Type": "ConfirmationRegistration",
  "Payload": {
    "confirmation_code": "123456",
    "username": "john_doe",
    "ip": "192.168.1.100",
    "devicename": "iPhone 14",
    "os": "iOS 17",
    "location": "USA, California, San Francisco",
    "datetime": "Tuesday, November 23, 2025, 15:30"
  }
}
```

**Consumer** (Infrastructure/Consumers/EmailNotificationConsumer.cs):
```csharp
public class EmailNotificationConsumer : IConsumer<EmailNotification>
{
    private readonly IEmailSender _emailSender;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ILogger<EmailNotificationConsumer> _logger;

    public async Task Consume(ConsumeContext<EmailNotification> context)
    {
        var notification = context.Message;

        _logger.LogInformation(
            "Received email notification: Type={Type}, To={Address}",
            notification.Type,
            notification.Address
        );

        try
        {
            // Рендеринг HTML из шаблона
            var htmlBody = await _templateRenderer.RenderAsync(
                notification.Type,
                notification.Payload
            );

            // Отправка email
            await _emailSender.SendEmailAsync(
                notification.Address,
                notification.Title,
                htmlBody
            );

            _logger.LogInformation(
                "Email sent successfully: Type={Type}, To={Address}",
                notification.Type,
                notification.Address
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send email: Type={Type}, To={Address}",
                notification.Type,
                notification.Address
            );

            // MassTransit автоматически повторит попытку
            throw;
        }
    }
}
```

### 3. IP Geolocation

**Назначение**: Определение местоположения пользователя по IP для безопасных уведомлений.

**API**: `http://ip-api.com/json/{ip}`

**Реализация** (Services/IpGeolocationService.cs):
```csharp
public class IpGeolocationService
{
    private readonly HttpClient _httpClient;

    public async Task<string> GetLocationAsync(string ipAddress)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"http://ip-api.com/json/{ipAddress}"
            );

            if (!response.IsSuccessStatusCode)
                return "Unknown";

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<IpApiResponse>(json);

            if (data?.Status != "success")
                return "Unknown";

            return $"{data.Country}, {data.RegionName}, {data.City}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get geolocation for IP {Ip}", ipAddress);
            return "Unknown";
        }
    }
}

public class IpApiResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; }

    [JsonPropertyName("country")]
    public string Country { get; set; }

    [JsonPropertyName("regionName")]
    public string RegionName { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; }
}
```

**Использование**: Identity service получает геолокацию и включает в Payload перед публикацией события.

## Email Шаблоны

### Структура шаблонов

```
Templates/
├── ConfirmationRegistration.cshtml
├── ConfirmationAuth.cshtml
├── ConfirmationOtpEmail.cshtml
├── ResetPassword.cshtml
└── _Layout.cshtml  (общий layout)
```

### Пример: ConfirmationAuth.cshtml

```html
@{
    Layout = "_Layout";
}

<h1>Подтверждение входа</h1>

<p>Здравствуйте, @Model.Username!</p>

<p>Кто-то пытается войти в ваш аккаунт. Если это вы, введите код:</p>
<p class="code">@Model.ConfirmationCode</p>

<div class="security-info">
    <p><strong>⚠️ Детали входа:</strong></p>
    <ul>
        <li>IP адрес: @Model.Ip</li>
        <li>Устройство: @Model.DeviceName (@Model.Os)</li>
        <li>Местоположение: @Model.Location</li>
        <li>Время: @Model.DateTime</li>
    </ul>
</div>

<p class="warning">
    ❌ Если это были НЕ вы, немедленно измените пароль!
</p>
```

### Стилизация

Все шаблоны используют встроенные CSS стили для совместимости с email-клиентами:

```css
body {
    font-family: 'Segoe UI', Arial, sans-serif;
    max-width: 600px;
    margin: 0 auto;
    padding: 20px;
    background-color: #f5f5f5;
}

.code {
    font-size: 32px;
    font-weight: bold;
    color: #007bff;
    background-color: #e7f3ff;
    padding: 15px;
    border-radius: 8px;
    text-align: center;
    letter-spacing: 8px;
}

.security-info {
    background-color: #fff3cd;
    border-left: 4px solid #ffc107;
    padding: 15px;
    margin: 20px 0;
}

.warning {
    color: #dc3545;
    font-weight: bold;
}
```

## RabbitMQ Конфигурация

### Queues

| Queue | Consumer | Тип события |
|-------|----------|-------------|
| `notifications-email-handler` | EmailNotificationConsumer | EmailNotification |

### Настройка MassTransit

**Program.cs**:
```csharp
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<EmailNotificationConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitMqHost, h =>
        {
            h.Username(rabbitMqUsername);
            h.Password(rabbitMqPassword);
        });

        cfg.ReceiveEndpoint("notifications-email-handler", e =>
        {
            e.ConfigureConsumer<EmailNotificationConsumer>(context);

            // Retry политика
            e.UseMessageRetry(r => r.Intervals(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            ));
        });
    });
});
```

**Retry политика**: 3 попытки с интервалами 5s, 15s, 30s.

## Зависимости

### Configuration Service (gRPC)

**Методы**:
- `LoadConfiguration` - загрузка настроек при старте

**Настройки**:
```json
{
  "SmtpSettings": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "noreply@barkfluff.com",
    "Password": "app-password",
    "FromEmail": "noreply@barkfluff.com",
    "FromName": "BarkFluff"
  },
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  }
}
```

### SMTP Server

**Поддерживаемые провайдеры**:
- Gmail (smtp.gmail.com:587)
- Outlook (smtp.office365.com:587)
- SendGrid (smtp.sendgrid.net:587)
- Mailgun (smtp.mailgun.org:587)
- Custom SMTP

**Важно**: Для Gmail требуется App Password, а не обычный пароль.

### IP Geolocation API

**Провайдер**: http://ip-api.com

**Ограничения**:
- 45 запросов/минуту для бесплатного tier
- Не требует API key

## Конфигурация

### appsettings.json

```json
{
  "SmtpSettings": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "EnableSsl": true,
    "Username": "noreply@barkfluff.com",
    "Password": "your-app-password",
    "FromEmail": "noreply@barkfluff.com",
    "FromName": "BarkFluff Notifications"
  },
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  },
  "IpGeolocation": {
    "ApiUrl": "http://ip-api.com/json/{ip}",
    "Timeout": 5000
  }
}
```

### Переменные окружения

- `SmtpSettings:Host` - SMTP сервер
- `SmtpSettings:Port` - SMTP порт
- `SmtpSettings:Username` - SMTP username
- `SmtpSettings:Password` - SMTP password
- `RabbitMQ:Host` - адрес RabbitMQ

## API Reference

**ВАЖНО**: Notification service не имеет gRPC или HTTP API. Он работает исключительно как RabbitMQ consumer.

### Публикация событий (для других сервисов)

**Пример** (из Identity service):
```csharp
await _eventBus.Publish(new EmailNotification
{
    Title = "Подтверждение регистрации",
    Address = user.Email,
    Type = "ConfirmationRegistration",
    Payload = new Dictionary<string, string>
    {
        ["confirmation_code"] = code.Value,
        ["username"] = user.Username,
        ["ip"] = userContext.IpAddress,
        ["devicename"] = userContext.DeviceName,
        ["os"] = userContext.Os,
        ["location"] = await _geolocation.GetLocationAsync(userContext.IpAddress),
        ["datetime"] = DateTime.UtcNow.ToString("F")
    }
});
```

## Известные проблемы

### 🟡 Средние

1. **Отсутствие email delivery tracking**
   - Нет информации, было ли письмо доставлено
   - **Рекомендация**: Интеграция с SendGrid/Mailgun API для tracking

2. **Синхронная отправка email**
   - Блокирует RabbitMQ consumer
   - **Рекомендация**: Добавить очередь для отправки

3. **Нет rate limiting**
   - Возможна отправка spam
   - **Рекомендация**: Ограничение по email/пользователю

### 🟢 Низкие

4. **Жёстко заданные шаблоны**
   - Нельзя изменить без пересборки
   - **Рекомендация**: Хранить шаблоны в БД или файлах конфигурации

5. **IP Geolocation API без fallback**
   - При недоступности API location = "Unknown"
   - **Рекомендация**: Добавить альтернативные провайдеры

## Troubleshooting

### Проблема: Email не отправляются

**Диагностика**:
1. Проверить RabbitMQ consumer status:
   ```bash
   curl http://rabbitmq:15672/api/queues
   ```

2. Проверить SMTP настройки:
   ```bash
   telnet smtp.gmail.com 587
   ```

3. Проверить логи Notification service на ошибки SMTP

**Решение**: Убедиться, что SMTP credentials корректные и сервер доступен.

### Проблема: "Authentication failed" для Gmail

**Причина**: Используется обычный пароль вместо App Password.

**Решение**:
1. Включить 2FA в Google Account
2. Сгенерировать App Password: https://myaccount.google.com/apppasswords
3. Использовать App Password в `SmtpSettings:Password`

### Проблема: Письма попадают в Spam

**Причина**: Отсутствие SPF/DKIM/DMARC настроек.

**Решение**:
1. Настроить SPF record в DNS:
   ```
   v=spf1 include:_spf.google.com ~all
   ```

2. Настроить DKIM signing

3. Добавить DMARC policy

### Проблема: Шаблон не найден

**Причина**: Файл шаблона отсутствует или неправильное имя.

**Решение**:
```bash
# Проверить наличие шаблона
ls Templates/{Type}.cshtml

# Убедиться, что Type в событии совпадает с именем файла
```

## Метрики и мониторинг

### Ключевые метрики

- **Emails Sent/hour**: Количество отправленных писем
- **Failed Deliveries**: Процент ошибок отправки
- **Average Send Time**: Время отправки одного письма
- **SMTP Connection Errors**: Ошибки подключения к SMTP

### Логи

Все операции логируются:
- Получение событий из RabbitMQ
- Успешная отправка email
- Ошибки SMTP
- Ошибки рендеринга шаблонов
- Ошибки геолокации

**Пример лога**:
```
[2025-11-23 15:30:45] INFO: Received email notification: Type=ConfirmationRegistration, To=user@example.com
[2025-11-23 15:30:46] INFO: Rendered template: ConfirmationRegistration
[2025-11-23 15:30:48] INFO: Email sent successfully: Type=ConfirmationRegistration, To=user@example.com
```

## Примеры использования

### Пример 1: Identity отправляет код регистрации

```csharp
// Identity service
var confirmationCode = GenerateCode(); // 123456

await _eventBus.Publish(new EmailNotification
{
    Title = "Подтверждение регистрации в BarkFluff",
    Address = user.Email,
    Type = "ConfirmationRegistration",
    Payload = new Dictionary<string, string>
    {
        ["confirmation_code"] = confirmationCode,
        ["username"] = user.Username,
        ["ip"] = context.IpAddress,
        ["devicename"] = context.DeviceName,
        ["os"] = context.Os,
        ["location"] = "USA, California, San Francisco",
        ["datetime"] = "Tuesday, November 23, 2025, 15:30"
    }
});

// Notification service автоматически:
// 1. Получает событие из RabbitMQ
// 2. Рендерит шаблон ConfirmationRegistration.cshtml
// 3. Отправляет email через SMTP
```

### Пример 2: Сброс пароля

```csharp
// Identity service
await _eventBus.Publish(new EmailNotification
{
    Title = "Сброс пароля BarkFluff",
    Address = user.Email,
    Type = "ResetPassword",
    Payload = new Dictionary<string, string>
    {
        ["confirmation_code"] = resetCode,
        ["username"] = user.Username,
        ["ip"] = context.IpAddress,
        ["devicename"] = context.DeviceName,
        ["location"] = location,
        ["datetime"] = DateTime.UtcNow.ToString("F")
    }
});
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Notification/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса и MassTransit
- `Services/EmailSender.cs` - отправка email через SMTP
- `Services/TemplateRenderer.cs` - рендеринг Razor шаблонов
- `Services/IpGeolocationService.cs` - определение геолокации
- `Infrastructure/Consumers/EmailNotificationConsumer.cs` - RabbitMQ consumer
- `Templates/*.cshtml` - email шаблоны
