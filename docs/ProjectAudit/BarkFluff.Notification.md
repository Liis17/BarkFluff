# Аудит проекта: BarkFluff.Notification

> **Дата:** 2026  
> **Ветка:** `dev`  
> **Расположение проекта:** `Backend/BarkFluff.Notification/`  
> **Статус:** 🟠 Требует исправлений

---

## Содержание

- [🔴 Безопасность](#безопасность)
- [🟡 Оптимизация производительности](#оптимизация-производительности)
- [🐛 Баги и недоработки](#баги-и-недоработки)
- [🔵 Прочее / Качество кода](#прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Глобальное отключение проверки TLS-сертификатов

**Проблема / Описание:**  
В `EmailSender.SendEmail()` перед отправкой письма устанавливается глобальный callback `ServicePointManager.ServerCertificateValidationCallback`, который безусловно возвращает `true`. Это означает, что **весь процесс** перестаёт проверять TLS-сертификаты для **любого** исходящего HTTPS/SMTP-соединения (не только для SMTP). Атакующий в позиции MITM может перехватить SMTP-сессию, получить учётные данные SMTP и содержимое писем.

**Конкретно в чём проблема:**
- `ServicePointManager` — глобальный синглтон для всего процесса.
- Callback устанавливается заново при **каждом** вызове `SendEmail`, что создаёт гонку данных при параллельном потреблении.
- CWE-295: Improper Certificate Validation.

**Путь к файлу:** `Backend/BarkFluff.Notification/Senders/EmailSender.cs` : строки 36–37

```csharp
// ❌ ПРОБЛЕМА: глобальное отключение проверки сертификата для всего процесса
// Действует не только на SMTP — на все HttpClient/WebRequest в процессе
// При параллельных вызовах — гонка данных
ServicePointManager.ServerCertificateValidationCallback =
    (sender, certificate, chain, errors) => true;
```

**Варианты решения:**

**Вариант A (рекомендуемый) — использовать `MailKit` с явным управлением сертификатом:**
```csharp
// ✅ Установить пакет: MailKit
// Управление проверкой только для конкретного соединения

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

public async Task SendEmail(EmailNotification notification)
{
    var body = await _templateParser.Parse(notification.Type, notification.Payload);

    var message = new MimeMessage();
    message.From.Add(MailboxAddress.Parse(_emailConfiguration.SenderEmail));
    message.To.Add(MailboxAddress.Parse(notification.Address));
    message.Subject = notification.Title;
    message.Body = new TextPart("html") { Text = body };

    using var client = new SmtpClient();

    // Для продакшена: SecureSocketOptions.StartTls или SslOnConnect
    // Для self-signed (dev): передать кастомный валидатор только этому клиенту
    client.ServerCertificateValidationCallback = (s, c, h, e) =>
    {
        // Можно проверять по thumbprint конкретного сертификата
        return _emailConfiguration.AllowSelfSigned || e == SslPolicyErrors.None;
    };

    await client.ConnectAsync(_emailConfiguration.Host, _emailConfiguration.Port, SecureSocketOptions.StartTlsWhenAvailable);
    await client.AuthenticateAsync(_emailConfiguration.SenderEmail, _emailConfiguration.SenderPassword);
    await client.SendAsync(message);
    await client.DisconnectAsync(true);
}
```

**Вариант B — минимальная правка без смены библиотеки (не рекомендуется для продакшена):**
```csharp
// ⚠️ Временное решение: хотя бы не трогать глобальный callback,
// убрать строку — по умолчанию .NET проверяет сертификаты.
// Для self-signed SMTP добавить сертификат в доверенные в ОС/контейнере.

// Удалить эти две строки:
// ServicePointManager.ServerCertificateValidationCallback =
//     (sender, certificate, chain, errors) => true;
```

---

### SEC-02 — Отсутствие валидации email-адреса получателя (Email Header Injection)

**Проблема / Описание:**  
Поле `notification.Address` принимается из RabbitMQ без какой-либо валидации. Если злоумышленник контролирует содержимое очереди (компрометация продюсера или отсутствие авторизации на RabbitMQ), он может передать адрес вида `victim@example.com\r\nBcc: attacker@evil.com`, что при использовании `System.Net.Mail` может привести к инъекции заголовков письма. Также возможна отправка писем на произвольные адреса.

**Конкретно в чём проблема:**
- Нет проверки формата email до передачи в `MailAddress`.
- `System.Net.Mail.MailAddress` не всегда корректно обрабатывает спецсимволы в адресе.
- CWE-113: Improper Neutralization of CRLF Sequences.

**Путь к файлу:** `Backend/BarkFluff.Notification/Senders/EmailSender.cs` : строки 54–56

```csharp
using var mailMessage = new MailMessage();
mailMessage.From = new MailAddress(_emailConfiguration.SenderEmail);
// ❌ ПРОБЛЕМА: address берётся из очереди без валидации
// Возможна Header Injection если строка содержит \r\n
mailMessage.To.Add(new MailAddress(notification.Address));
```

**Варианты решения:**

```csharp
// ✅ Вариант: валидация через регулярное выражение + нормализация перед использованием

private static readonly Regex EmailRegex = new(
    @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
    RegexOptions.Compiled | RegexOptions.IgnoreCase);

public async Task SendEmail(EmailNotification notification)
{
    // Валидируем адрес ДО любой обработки
    if (string.IsNullOrWhiteSpace(notification.Address)
        || !EmailRegex.IsMatch(notification.Address)
        || notification.Address.Contains('\r')
        || notification.Address.Contains('\n'))
    {
        _logger.LogWarning("Невалидный email-адрес получателя: {Address}", notification.Address);
        // Не бросаем исключение — чтобы не триггерить retry для заведомо плохих сообщений
        return;
    }

    // ... остальная логика
}
```

---

### SEC-03 — XSS-контент в HTML-шаблонах (payload без HTML-кодирования)

**Проблема / Описание:**  
Значения из `payload` вставляются в HTML-шаблон через прямую строковую замену без HTML-кодирования. Если атакующий передаст в поле `name` значение `<script>...</script>`, оно будет вставлено в письмо «как есть». Email-клиенты по-разному обрабатывают HTML — некоторые выполняют скрипты. Более реально: CSS-инъекция, фишинговые ссылки, искажение вёрстки письма.

**Конкретно в чём проблема:**
- `String.Replace` не знает, что подставляет в HTML-контекст.
- CWE-79: Improper Neutralization of Input During Web Page Generation (XSS).

**Путь к файлу:** `Backend/BarkFluff.Notification/Parsers/HtmlEmailTemplateParser.cs` : строки 28–31

```csharp
// ❌ ПРОБЛЕМА: payload вставляется без экранирования HTML-спецсимволов
// payload["name"] = "<b>взломан</b><img src=x onerror=alert(1)>"
// → будет вставлено в HTML без изменений
foreach (var payloadItem in payload)
{
    fileContent = fileContent.Replace($"ꟿꟿꟿ{payloadItem.Key}ꟿꟿꟿ", payloadItem.Value);
}
```

**Варианты решения:**

```csharp
// ✅ HTML-кодирование каждого значения перед вставкой
using System.Net;

foreach (var payloadItem in payload)
{
    // HtmlEncode заменяет < > & " ' → &lt; &gt; &amp; &quot; &#39;
    var safeValue = WebUtility.HtmlEncode(payloadItem.Value);
    fileContent = fileContent.Replace($"ꟿꟿꟿ{payloadItem.Key}ꟿꟿꟿ", safeValue);
}

// Плейсхолдер currentyear — безопасен (число), без изменений
fileContent = fileContent.Replace("ꟿꟿꟿcurrentyearꟿꟿꟿ", DateTime.UtcNow.Year.ToString());
```

---

### SEC-04 — SMTP-пароль хранится в plaintext-конфигурации

**Проблема / Описание:**  
`EmailConfiguration.SenderPassword` загружается напрямую из `appsettings` / переменных среды без какого-либо шифрования или интеграции с хранилищем секретов. При утечке конфигурационного файла или переменных среды контейнера пароль SMTP будет скомпрометирован.

**Конкретно в чём проблема:**
- Поле `SenderPassword` — обычная `string`, нет маскировки в логах.
- CWE-311: Missing Encryption of Sensitive Data.

**Путь к файлу:** `Backend/BarkFluff.Notification/Configurations/EmailConfiguration.cs` : строки 1–12

```csharp
public class EmailConfiguration
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string SenderEmail { get; set; }
    // ❌ ПРОБЛЕМА: пароль в открытом виде — попадёт в логи при сериализации,
    // виден в переменных среды контейнера при docker inspect
    public string SenderPassword { get; set; }
}
```

**Варианты решения:**

```csharp
// ✅ Вариант A: пометить как [JsonIgnore] + не логировать конфигурацию целиком

public class EmailConfiguration
{
    public string Host { get; set; }
    public int Port { get; set; }
    public string SenderEmail { get; set; }

    // Защищаем от случайной сериализации в логи
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public string SenderPassword { get; set; }

    // Опционально: хранить как SecureString (для in-memory защиты)
    // public SecureString SenderPasswordSecure { get; set; }
}

// ✅ Вариант B (рекомендуется для продакшена):
// Использовать Docker Secrets / Kubernetes Secrets / HashiCorp Vault
// и загружать значение через IConfiguration с провайдером секретов,
// а не через appsettings.json напрямую
```

---

## 🟡 Оптимизация производительности

---

### PERF-01 — Чтение HTML-шаблона с диска при каждой отправке письма

**Проблема / Описание:**  
`HtmlEmailTemplateParser.Parse()` читает файл шаблона с диска при каждом вызове через `File.ReadAllTextAsync`. Шаблоны статичны и не меняются в рантайме. При высокой нагрузке (много писем подряд) это создаёт излишние I/O операции, а также увеличивает латентность обработки каждого сообщения из очереди.

**Конкретно в чём проблема:**
- `File.ReadAllTextAsync` — операция ввода-вывода при каждом письме.
- Шаблонов 9 штук, они неизменны после запуска.
- `HtmlEmailTemplateParser` зарегистрирован как `Transient` — нет возможности хранить кэш в самом парсере между вызовами.

**Путь к файлу:** `Backend/BarkFluff.Notification/Parsers/HtmlEmailTemplateParser.cs` : строки 22–27

```csharp
public async Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
{
    var templateName = _templatesMap[type];
    // ❌ ПРОБЛЕМА: чтение с диска при каждом вызове
    // При 1000 писем/мин — 1000 File I/O операций на шаблоны
    var fileName = Path.Combine(Environment.CurrentDirectory, "Templates", templateName);
    var fileContent = await File.ReadAllTextAsync(fileName);
    // ...
}
```

**Варианты решения:**

```csharp
// ✅ Предзагрузить все шаблоны в память при старте сервиса

// Изменить регистрацию в Program.cs: AddSingleton вместо AddTransient
builder.Services.AddSingleton<HtmlEmailTemplateParser>();

// В самом парсере — lazy-load или eager-load в конструкторе:
public class HtmlEmailTemplateParser
{
    private readonly Dictionary<NotificationType, string> _templatesMap = new() { /* ... */ };
    // Кэш загруженных шаблонов
    private readonly Dictionary<NotificationType, string> _templateCache = new();

    public HtmlEmailTemplateParser()
    {
        // Предзагрузка всех шаблонов при создании синглтона
        foreach (var (type, fileName) in _templatesMap)
        {
            var fullPath = Path.Combine(Environment.CurrentDirectory, "Templates", fileName);
            if (File.Exists(fullPath))
                _templateCache[type] = File.ReadAllText(fullPath);
        }
    }

    public Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
    {
        if (!_templateCache.TryGetValue(type, out var template))
            throw new InvalidOperationException($"Шаблон для типа {type} не найден");

        // Работаем с копией — оригинал в кэше не трогаем
        var fileContent = template;

        foreach (var payloadItem in payload)
        {
            var safeValue = System.Net.WebUtility.HtmlEncode(payloadItem.Value);
            fileContent = fileContent.Replace($"ꟿꟿꟿ{payloadItem.Key}ꟿꟿꟿ", safeValue);
        }

        fileContent = fileContent.Replace("ꟿꟿꟿcurrentyearꟿꟿꟿ", DateTime.UtcNow.Year.ToString());

        return Task.FromResult(fileContent);
    }
}
```

---

### PERF-02 — `SmtpClient` создаётся заново при каждой отправке (нет connection reuse)

**Проблема / Описание:**  
В `EmailSender.SendEmail()` создаётся новый `SmtpClient` с `using` при каждом вызове. Каждое создание — новое TCP-соединение + TLS-handshake к SMTP-серверу. При пакетной обработке очереди это значительно увеличивает время на установку соединений. Кроме того, `EmailSender` зарегистрирован как `Transient`, что дополнительно исключает возможность переиспользования.

**Конкретно в чём проблема:**
- Новый `SmtpClient` = новый TCP-коннект + TLS handshake (~100–300 мс в зависимости от сети).
- Устаревший `System.Net.Mail.SmtpClient` не поддерживает connection pooling.
- CWE (performance): Allocation Without Reuse.

**Путь к файлу:** `Backend/BarkFluff.Notification/Senders/EmailSender.cs` : строки 38–44

```csharp
// ❌ ПРОБЛЕМА: новый SmtpClient (= новое TCP+TLS соединение) на каждое письмо
using var smtpClient = new SmtpClient(_emailConfiguration.Host, _emailConfiguration.Port)
{
    Credentials = new NetworkCredential(_emailConfiguration.SenderEmail, _emailConfiguration.SenderPassword),
    EnableSsl = true,
    DeliveryMethod = SmtpDeliveryMethod.Network
};
```

**Варианты решения:**

```csharp
// ✅ Вариант: MailKit с переиспользованием подключения (Singleton EmailSender)
// MailKit.Net.Smtp.SmtpClient поддерживает открытое соединение между отправками

public class EmailSender : IAsyncDisposable
{
    private readonly MailKit.Net.Smtp.SmtpClient _smtpClient = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    // ...

    public async Task SendEmail(EmailNotification notification)
    {
        await _lock.WaitAsync();
        try
        {
            if (!_smtpClient.IsConnected)
                await _smtpClient.ConnectAsync(_cfg.Host, _cfg.Port, SecureSocketOptions.StartTls);
            if (!_smtpClient.IsAuthenticated)
                await _smtpClient.AuthenticateAsync(_cfg.SenderEmail, _cfg.SenderPassword);

            await _smtpClient.SendAsync(message);
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync() => await _smtpClient.DisconnectAsync(true);
}

// В Program.cs — регистрировать как Singleton:
builder.Services.AddSingleton<EmailSender>();
```

---

### PERF-03 — Повторная замена строк на каждом символе вместо одного прохода

**Проблема / Описание:**  
В `HtmlEmailTemplateParser.Parse()` для каждого элемента `payload` вызывается `String.Replace`, который создаёт новую строку. При большом HTML-шаблоне (~10 КБ) и нескольких плейсхолдерах это означает N промежуточных аллокаций строк. Незначительно для единичных писем, но суммируется при высокой нагрузке.

**Конкретно в чём проблема:**
- `string.Replace` — иммутабельный тип, каждый вызов = новая аллокация.
- При 10 ключах в payload = 10 промежуточных строк на одно письмо.

**Путь к файлу:** `Backend/BarkFluff.Notification/Parsers/HtmlEmailTemplateParser.cs` : строки 28–33

```csharp
// ❌ ПРОБЛЕМА: N аллокаций строк (по одной на каждый ключ payload)
foreach (var payloadItem in payload)
{
    fileContent = fileContent.Replace($"ꟿꟿꟿ{payloadItem.Key}ꟿꟿꟿ", payloadItem.Value);
}
fileContent = fileContent.Replace("ꟿꟿꟿcurrentyearꟿꟿꟿ", DateTime.UtcNow.Year.ToString());
```

**Варианты решения:**

```csharp
// ✅ Использовать StringBuilder или Regex.Replace для одного прохода

public Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
{
    var template = _templateCache[type];

    // Один проход по строке с заменой через Regex
    var allPayload = new Dictionary<string, string>(payload)
    {
        ["currentyear"] = DateTime.UtcNow.Year.ToString()
    };

    var result = Regex.Replace(template, @"ꟿꟿꟿ(\w+)ꟿꟿꟿ", match =>
    {
        var key = match.Groups[1].Value;
        return allPayload.TryGetValue(key, out var value)
            ? WebUtility.HtmlEncode(value)
            : match.Value; // оставить нетронутым если ключ не найден
    });

    return Task.FromResult(result);
}
```

---

## 🐛 Баги и недоработки

---

### BUG-01 — `KeyNotFoundException` при неизвестном `NotificationType`

**Проблема / Описание:**  
В `HtmlEmailTemplateParser.Parse()` обращение к `_templatesMap[type]` выбросит `KeyNotFoundException`, если в очередь придёт сообщение с `NotificationType.Unknown` (значение `0`) или с новым типом, для которого ещё не добавлен шаблон. Это исключение будет поймано MassTransit, сообщение уйдёт в retry, затем в dead-letter очередь. Ошибка не диагностируется явно.

**Конкретно в чём проблема:**
- `_templatesMap[type]` — индексатор без проверки наличия ключа.
- `NotificationType.Unknown = 0` не имеет маппинга, но может прийти при десериализации некорректного сообщения.
- Нет проверки существования файла шаблона на диске.

**Путь к файлу:** `Backend/BarkFluff.Notification/Parsers/HtmlEmailTemplateParser.cs` : строка 22–24

```csharp
public async Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
{
    // ❌ ПРОБЛЕМА: KeyNotFoundException если type не в словаре
    // Например: NotificationType.Unknown = 0 — нет маппинга
    var templateName = _templatesMap[type];
    // ❌ ПРОБЛЕМА: нет проверки File.Exists — FileNotFoundException если файл удалён
    var fileName = Path.Combine(Environment.CurrentDirectory, "Templates", templateName);
    var fileContent = await File.ReadAllTextAsync(fileName);
```

**Варианты решения:**

```csharp
// ✅ Явная проверка + информативное исключение

public async Task<string> Parse(NotificationType type, Dictionary<string, string> payload)
{
    if (!_templatesMap.TryGetValue(type, out var templateName))
        throw new NotSupportedException(
            $"Тип уведомления '{type}' не поддерживается: шаблон не зарегистрирован");

    var fileName = Path.Combine(Environment.CurrentDirectory, "Templates", templateName);

    if (!File.Exists(fileName))
        throw new FileNotFoundException(
            $"Файл шаблона не найден: {fileName}", fileName);

    var fileContent = await File.ReadAllTextAsync(fileName);
    // ...
}
```

---

### BUG-02 — `null` payload не обрабатывается — `NullReferenceException` в парсере

**Проблема / Описание:**  
Поле `Payload` в базовом классе `Notification` объявлено как `Dictionary<string, string>` без инициализации по умолчанию. Если продюсер отправит сообщение с `Payload = null`, то в `HtmlEmailTemplateParser.Parse()` при `foreach (var payloadItem in payload)` будет выброшен `NullReferenceException`. Аналогично — при десериализации сообщения из RabbitMQ если поле отсутствует в JSON.

**Конкретно в чём проблема:**
- `Notification.Payload` — ссылочный тип без `= new()` инициализации.
- Нет null-проверки перед `foreach` в парсере.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Notifications/Notification.cs` : строка 16  
**Путь к файлу:** `Backend/BarkFluff.Notification/Parsers/HtmlEmailTemplateParser.cs` : строка 29

```csharp
// Notification.cs
// ❌ ПРОБЛЕМА: Payload может быть null после десериализации
public Dictionary<string, string> Payload { get; set; }

// HtmlEmailTemplateParser.cs
// ❌ ПРОБЛЕМА: NullReferenceException если Payload == null
foreach (var payloadItem in payload)
{
    fileContent = fileContent.Replace(...);
}
```

**Варианты решения:**

```csharp
// ✅ Вариант A: инициализация по умолчанию в модели (предпочтительно)
// Notification.cs
public Dictionary<string, string> Payload { get; set; } = new();

// ✅ Вариант B: защитная проверка в парсере
// HtmlEmailTemplateParser.cs
var safePayload = payload ?? new Dictionary<string, string>();
foreach (var payloadItem in safePayload)
{
    // ...
}
```

---

### BUG-03 — `NotificationType.Unknown` может быть принят и обработан без ошибки на уровне консьюмера

**Проблема / Описание:**  
`EmailQueueConsumer` не валидирует `notification.Type` перед передачей в `EmailSender`. Если тип `Unknown` (0) — сообщение упадёт с `KeyNotFoundException` глубоко в парсере. Это приведёт к retry-циклу в MassTransit, что создаст лишнюю нагрузку на очередь и SMTP-сервер (если retry сработает до исключения в парсере).

**Конкретно в чём проблема:**
- Нет ранней валидации входящего сообщения.
- Плохие сообщения (Unknown type, пустой адрес) должны отвергаться **без retry** — через `context.NegativeAcknowledge(false)` или специальное исключение.

**Путь к файлу:** `Backend/BarkFluff.Notification/Consumers/EmailQueueConsumer.cs` : строки 22–26

```csharp
public async Task Consume(ConsumeContext<EmailNotification> context)
{
    _metrics.Increment("rabbitmq_events_consumed");
    var notification = context.Message;
    // ❌ ПРОБЛЕМА: нет ранней валидации — тип Unknown и пустой адрес
    // попадут глубже и вызовут retry
    // ...
    await _emailSender.SendEmail(notification);
```

**Варианты решения:**

```csharp
// ✅ Ранняя валидация — отказ без retry для заведомо плохих сообщений

public async Task Consume(ConsumeContext<EmailNotification> context)
{
    _metrics.Increment("rabbitmq_events_consumed");
    var notification = context.Message;

    // Валидация входных данных
    if (notification.Type == NotificationType.Unknown)
    {
        _logger.LogWarning("Получено сообщение с неизвестным типом NotificationType.Unknown, пропускаем");
        // Не бросаем исключение — NACKаем без requeue
        return;
    }

    if (string.IsNullOrWhiteSpace(notification.Address))
    {
        _logger.LogWarning("Получено сообщение с пустым адресом получателя, пропускаем");
        return;
    }

    // ... остальная логика
}
```

---

### BUG-04 — MediatR зарегистрирован, но обработчиков нет — лишняя зависимость

**Проблема / Описание:**  
В `Program.cs` зарегистрирован MediatR (`AddMediatR`), однако в проекте нет ни одного `IRequestHandler`, `INotificationHandler` или команды. По Obsidian-документации это «задел на будущее». Лишняя зависимость увеличивает время старта (MediatR сканирует сборку через reflection), добавляет пакет без реального использования и может ввести в заблуждение разработчиков.

**Конкретно в чём проблема:**
- Рефлексивное сканирование сборки без пользы.
- Зависимость MediatR весит ~500 КБ и тянет `Microsoft.Extensions.DependencyInjection.Abstractions`.

**Путь к файлу:** `Backend/BarkFluff.Notification/Program.cs` : строка 25

```csharp
// ❌ ПРОБЛЕМА: MediatR зарегистрирован, но не используется нигде в проекте
// Лишний overhead при старте: reflection-сканирование сборки
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
```

**Варианты решения:**

```csharp
// ✅ Вариант A: удалить до тех пор, пока не появятся реальные обработчики
// Закомментировать или убрать строку:
// builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());

// ✅ Вариант B: оставить с комментарием-объяснением (если это осознанный задел)
// Зарегистрировать MediatR когда появятся первые IRequestHandler
```

---

### BUG-05 — Отсутствие retry-политики и dead-letter конфигурации в MassTransit

**Проблема / Описание:**  
`ReceiveEndpoint` настроен минимально — без явной политики retry и без dead-letter очереди. При ошибке MassTransit использует стандартные настройки (обычно 3 retry с короткой задержкой), после чего сообщение попадает в автоматически созданную очередь `*_error`. Это означает, что при SMTP-недоступности сервис быстро исчерпает retry и потеряет сообщения — они осядут в error-очереди без алертинга.

**Конкретно в чём проблема:**
- Нет явного `UseMessageRetry` с exponential backoff для SMTP-ошибок.
- Нет `UseDelayedRedelivery` для долгих SMTP outage.
- Нет интеграции с метриками/алертами при попадании в dead-letter.

**Путь к файлу:** `Backend/BarkFluff.Notification/Program.cs` : строки 38–43

```csharp
cfg.ReceiveEndpoint("notifications-email-handler", e =>
{
    // ❌ ПРОБЛЕМА: нет политики retry — используются дефолты MassTransit
    // При SMTP outage сообщения быстро уходят в _error очередь
    e.ConfigureConsumer<EmailQueueConsumer>(context);
});
```

**Варианты решения:**

```csharp
// ✅ Явная политика retry с exponential backoff

cfg.ReceiveEndpoint("notifications-email-handler", e =>
{
    // Retry с нарастающей задержкой — даёт SMTP-серверу время восстановиться
    e.UseMessageRetry(r => r.Exponential(
        retryLimit: 5,
        minInterval: TimeSpan.FromSeconds(5),
        maxInterval: TimeSpan.FromMinutes(10),
        intervalDelta: TimeSpan.FromSeconds(10)
    ));

    // Отложенная повторная доставка при длительном outage (требует delayed exchange)
    e.UseDelayedRedelivery(r => r.Intervals(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1)
    ));

    e.ConfigureConsumer<EmailQueueConsumer>(context);
});
```

---

## 🔵 Прочее / Качество кода

---

### MISC-01 — `EmailConfiguration` не имеет Data Annotations / валидации при старте

**Проблема / Описание:**  
Поля `EmailConfiguration` — обычные `string` и `int` без атрибутов `[Required]` и без вызова `ValidateDataAnnotations()`. Если конфигурация не загрузилась (нет ключа в secrets, опечатка в имени), сервис стартует успешно, а падение произойдёт лишь при первой попытке отправить письмо.

**Конкретно в чём проблема:**
- Fail-fast при старте не работает.
- Ошибка конфигурации обнаруживается только в рантайме под нагрузкой.

**Путь к файлу:** `Backend/BarkFluff.Notification/Configurations/EmailConfiguration.cs` : строки 1–12

```csharp
public class EmailConfiguration
{
    // ❌ Нет [Required], нет проверки при старте
    public string Host { get; set; }
    public int Port { get; set; }
    public string SenderEmail { get; set; }
    public string SenderPassword { get; set; }
}
```

**Варианты решения:**

```csharp
// ✅ Data Annotations + ValidateDataAnnotations в Program.cs

using System.ComponentModel.DataAnnotations;

public class EmailConfiguration
{
    [Required(ErrorMessage = "Email:Host обязателен")]
    public string Host { get; set; }

    [Range(1, 65535, ErrorMessage = "Email:Port должен быть в диапазоне 1–65535")]
    public int Port { get; set; }

    [Required, EmailAddress(ErrorMessage = "Email:SenderEmail должен быть валидным email")]
    public string SenderEmail { get; set; }

    [Required(ErrorMessage = "Email:SenderPassword обязателен")]
    [JsonIgnore]
    public string SenderPassword { get; set; }
}

// Program.cs — добавить валидацию при старте:
builder.Services.AddSettings<EmailConfiguration>(builder.Configuration, "Email")
    .ValidateDataAnnotations()
    .ValidateOnStart(); // падает сразу при запуске если конфиг неверный
```

---

### MISC-02 — Логирование email-адреса получателя в `Information` уровне

**Проблема / Описание:**  
В `EmailQueueConsumer` и `EmailSender` email-адрес получателя логируется на уровне `Information`. В зависимости от конфигурации Serilog и используемого sink (Elasticsearch, Loki, и т.д.) эти логи могут содержать PII (Personal Identifiable Information) и сохраняться в системах, не предназначенных для хранения персональных данных. Это может нарушать требования GDPR / 152-ФЗ.

**Конкретно в чём проблема:**
- Email-адрес — персональные данные (PII).
- Логируется открыто в structured logging — индексируется в поисковых системах.

**Путь к файлу:** `Backend/BarkFluff.Notification/Consumers/EmailQueueConsumer.cs` : строки 27–32

```csharp
_logger.LogInformation(
    "Получено уведомление для отправки email. Адрес: {Email}, Тип: {Type}, Заголовок: '{Title}'",
    // ❌ ПРОБЛЕМА: Email — PII, не должен попадать в общие логи в открытом виде
    notification.Address,
    notification.Type,
    notification.Title
);
```

**Варианты решения:**

```csharp
// ✅ Вариант A: маскировать email в логах (показывать только домен)
private static string MaskEmail(string email)
{
    var at = email.IndexOf('@');
    return at > 0 ? $"***@{email[(at + 1)..]}" : "***";
}

_logger.LogInformation(
    "Получено уведомление. Адрес: {Email}, Тип: {Type}",
    MaskEmail(notification.Address), // ***@gmail.com
    notification.Type
);

// ✅ Вариант B: логировать только хеш адреса (для корреляции без раскрытия)
var emailHash = Convert.ToHexString(
    System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(notification.Address)))[..8];

_logger.LogInformation("Уведомление получено. EmailHash: {EmailHash}, Тип: {Type}",
    emailHash, notification.Type);
```

---

### MISC-03 — `AddTransient` для `EmailSender` и `HtmlEmailTemplateParser` семантически неверно

**Проблема / Описание:**  
Оба сервиса зарегистрированы как `Transient` в `Program.cs`. `HtmlEmailTemplateParser` — stateless и инициализирует `_templatesMap` как readonly — идеальный кандидат для `Singleton`. `EmailSender` содержит логику подключения к SMTP — при `Transient` каждый раз создаётся новый экземпляр (и новое соединение). Правильная регистрация напрямую влияет на возможность кэширования (PERF-01) и переиспользования соединений (PERF-02).

**Конкретно в чём проблема:**
- `Transient` = новый объект каждый раз, кэширование невозможно.
- Противоречит паттернам оптимизации PERF-01 и PERF-02.

**Путь к файлу:** `Backend/BarkFluff.Notification/Program.cs` : строки 46–47

```csharp
// ❌ Transient для stateless сервисов без побочных эффектов — семантически неверно
builder.Services.AddTransient<EmailSender>();
builder.Services.AddTransient<HtmlEmailTemplateParser>();
```

**Варианты решения:**

```csharp
// ✅ Правильные lifetime'ы

// Singleton: stateless, _templatesMap инициализируется один раз
// При реализации PERF-01 — обязательно Singleton
builder.Services.AddSingleton<HtmlEmailTemplateParser>();

// Singleton: при использовании постоянного SMTP-соединения (PERF-02)
// Если остаётся System.Net.Mail.SmtpClient — можно Scoped, но Transient избыточен
builder.Services.AddSingleton<EmailSender>();
```

---

## Сводная таблица проблем

| ID | Категория | Название | Критичность | Статус |
|----|-----------|----------|-------------|--------|
| SEC-01 | 🔴 Безопасность | Глобальное отключение TLS-проверки | 🔴 Критическая | ⏳ Открыта |
| SEC-02 | 🔴 Безопасность | Email Header Injection (нет валидации адреса) | 🟠 Высокая | ⏳ Открыта |
| SEC-03 | 🔴 Безопасность | XSS в HTML-шаблонах (payload без HtmlEncode) | 🟠 Высокая | ⏳ Открыта |
| SEC-04 | 🔴 Безопасность | SMTP-пароль в plaintext | 🟡 Средняя | ⏳ Открыта |
| PERF-01 | 🟡 Оптимизация | Чтение шаблона с диска при каждой отправке | 🟡 Средняя | ⏳ Открыта |
| PERF-02 | 🟡 Оптимизация | SmtpClient создаётся заново при каждой отправке | 🟡 Средняя | ⏳ Открыта |
| PERF-03 | 🟡 Оптимизация | N аллокаций строк при замене плейсхолдеров | 🟢 Низкая | ⏳ Открыта |
| BUG-01 | 🐛 Баг | KeyNotFoundException при неизвестном NotificationType | 🟠 Высокая | ⏳ Открыта |
| BUG-02 | 🐛 Баг | NullReferenceException при Payload = null | 🟠 Высокая | ⏳ Открыта |
| BUG-03 | 🐛 Баг | Unknown тип уходит в retry-цикл без ранней валидации | 🟡 Средняя | ⏳ Открыта |
| BUG-04 | 🐛 Баг | MediatR зарегистрирован без обработчиков | 🟢 Низкая | ⏳ Открыта |
| BUG-05 | 🐛 Баг | Нет retry-политики и dead-letter конфигурации | 🟠 Высокая | ⏳ Открыта |
| MISC-01 | 🔵 Качество | EmailConfiguration без валидации при старте | 🟡 Средняя | ⏳ Открыта |
| MISC-02 | 🔵 Качество | PII (email) логируется открыто | 🟡 Средняя | ⏳ Открыта |
| MISC-03 | 🔵 Качество | Неверные DI lifetime для EmailSender и Parser | 🟡 Средняя | ⏳ Открыта |

---

*Документ сгенерирован на основе аудита кода проекта `BarkFluff.Notification`. Проверенные файлы: `Program.cs`, `EmailSender.cs`, `HtmlEmailTemplateParser.cs`, `EmailQueueConsumer.cs`, `EmailConfiguration.cs`, `appsettings.json`, `Dockerfile`, `Notification.cs` (Shared), `NotificationType.cs` (Shared).*
