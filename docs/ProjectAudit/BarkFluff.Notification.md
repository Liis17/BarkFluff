# Аудит проекта: BarkFluff.Notification

> **Дата:** 2026  
> **Ветка:** `dev`  
> **Расположение проекта:** `Backend/BarkFluff.Notification/`  
> **Статус:** ✅ Исправлено

## 🟡 Оптимизация производительности

---

### PERF-03 — Повторная замена строк на каждом символе вместо одного прохода ✅ Исправлено

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

//проверить что регекс нормальный (важно)
```

---

## 🐛 Баги и недоработки

---

### BUG-04 — MediatR зарегистрирован, но обработчиков нет — лишняя зависимость ✅ Исправлено

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

## 🔵 Прочее / Качество кода

---

### 

### MISC-02 — Логирование email-адреса получателя в `Information` уровне ✅ Исправлено

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
