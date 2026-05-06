# Аудит проекта: Barkfluff.CloudMessaging

> **Дата:** 2025  
> **Ветка:** `dev`  
> **Проект:** `Backend/Barkfluff.CloudMessaging`  
> **Описание:** Микросервис отправки push-уведомлений через Firebase Cloud Messaging (FCM). Подписывается на RabbitMQ-очередь `push-notifications-handler`, получает данные о новом сообщении, запрашивает информацию у сервисов `Users` и `Messages` через gRPC, и отправляет FCM-уведомления на устройства получателей.

---

## 🟠 Производительность

---

### PERF-01 — Push-уведомления отправляются последовательно вместо батча

**Проблема / Описание**  
При наличии нескольких получателей цикл `foreach` отправляет уведомления **последовательно** — каждый следующий вызов Firebase ждёт завершения предыдущего. Firebase Admin SDK поддерживает `SendEachAsync` для батчевой отправки (до 500 сообщений за раз).

**Конкретно в чём проблема**  
При 10 получателях — 10 последовательных HTTP-запросов к FCM. Каждый ~100–300ms → суммарная задержка до 3 секунд на одно событие.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Consumers/PushNotificationConsumer.cs` : строки 91–107

```csharp
// ❌ ПРОБЛЕМА: последовательная отправка — O(n) HTTP-запросов
foreach (var token in tokensResponse.Tokens)
{
    await _firebaseService.SendNotificationAsync(
        token.FirebaseToken,
        // ...много параметров...
    );
    // Каждый вызов ждёт предыдущего: 10 токенов = 10 последовательных запросов к FCM
}
```

**Варианты решения**

```csharp
// ✅ Использовать SendEachAsync — один HTTP-запрос для всех токенов (до 500)
// В FirebaseService добавить батч-метод:

public async Task SendNotificationBatchAsync(
    IEnumerable<string> fcmTokens,
    string senderName,
    string messagePreview,
    string chatId,
    long senderId,
    long messageId,
    // ... остальные параметры
    CancellationToken cancellationToken = default)
{
    if (_messaging == null) return;

    var messages = fcmTokens.Select(token => new Message
    {
        Token = token,
        Data = new Dictionary<string, string>
        {
            ["chat_id"] = chatId,
            ["sender_id"] = senderId.ToString(),
            // ... остальные поля
            ["message_text"] = TruncateMessage(messagePreview, 100),
        },
        Android = new AndroidConfig { Priority = Priority.High }
    }).ToList();

    // Один запрос к FCM вместо N последовательных
    var response = await _messaging.SendEachAsync(messages, cancellationToken);

    // Обрабатываем результаты — находим невалидные токены
    var failedTokens = messages
        .Zip(response.Responses, (msg, resp) => (msg.Token, resp))
        .Where(x => !x.resp.IsSuccess &&
                    x.resp.Exception?.MessagingErrorCode == MessagingErrorCode.Unregistered)
        .Select(x => x.Token)
        .ToList();

    if (failedTokens.Count > 0)
    {
        _logger.LogWarning("Невалидные FCM-токены ({Count}): требуется очистка в БД", failedTokens.Count);
        // TODO: отправить событие на удаление токенов из БД
    }
}

// В PushNotificationConsumer:
// ✅ Вместо foreach — один вызов:
await _firebaseService.SendNotificationBatchAsync(
    tokensResponse.Tokens.Select(t => t.FirebaseToken),
    senderName, message.MessageText ?? string.Empty,
    // ...
    context.CancellationToken);
```

---

### PERF-02 — Избыточные gRPC-вызовы: данные уже есть в event, но не используются

**Проблема / Описание**  
`PushNotificationEvent` уже содержит поля `SenderAvatarUrl`, `ChatTitle`, `ChatAvatarUrl`, `IsGroupChat` — они заполняются публикатором. Тем не менее, Consumer делает **2 параллельных gRPC-вызова** (`GetByIdAsync` и `GetChatInfoAsync`), чтобы получить те же самые данные заново. Это лишние сетевые roundtrip'ы при каждом сообщении.

**Конкретно в чём проблема**  
Consumer игнорирует данные в event и делает 3 gRPC-вызова вместо 1 (только `GetDevicesWithFirebaseTokens`).

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Consumers/PushNotificationConsumer.cs` : строки 51–75

```csharp
// ❌ ПРОБЛЕМА: 2 лишних gRPC-вызова — данные уже есть в message!
var senderCall = _usersClient.GetByIdAsync(
    new GetByIdRequest { UserId = message.SenderId });       // ← получаем имя и аватар

var chatInfoCall = _messagesClient.GetChatInfoAsync(
    new GetChatInfoRequest { ChatId = message.ChatId.ToString() }); // ← получаем title и isGroup

await System.Threading.Tasks.Task.WhenAll(senderCall.ResponseAsync, chatInfoCall.ResponseAsync);

// А вот что уже есть в PushNotificationEvent:
// message.SenderAvatarUrl  — URL аватара отправителя
// message.ChatTitle        — название чата
// message.ChatAvatarUrl    — аватар чата
// message.IsGroupChat      — флаг группового чата
// ↑↑↑ ВСЁ ЭТО ИГНОРИРУЕТСЯ и запрашивается заново по сети
```

**Варианты решения**

```csharp
// ✅ Использовать данные из event напрямую — убрать 2 gRPC-вызова

public async Task Consume(ConsumeContext<PushNotificationEvent> context)
{
    var message = context.Message;

    if (message.RecipientUserIds.Count == 0)
    {
        _logger.LogWarning("Нет получателей для push-уведомления");
        return;
    }

    // Данные уже есть в event — gRPC не нужен для sender/chat info
    var senderName = message.SenderName ?? "Unknown";   // добавить SenderName в event
    var senderAvatarUrl = message.SenderAvatarUrl ?? string.Empty;
    var chatTitle = message.ChatTitle ?? string.Empty;
    var chatAvatarUrl = message.ChatAvatarUrl ?? string.Empty;
    var isGroupChat = message.IsGroupChat;

    // Остаётся только ОДИН gRPC-вызов — получить FCM-токены устройств
    var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
        new GetDevicesWithFirebaseTokensRequest
        {
            UserIds = { message.RecipientUserIds }
        },
        cancellationToken: context.CancellationToken);  // ← передаём CancellationToken

    // ... отправка уведомлений
}

// Также добавить SenderName в PushNotificationEvent:
// public string? SenderName { get; set; }
// Публикатор уже знает имя отправителя при публикации события
```

---

## 🐛 Баги и недоработки

---

### BUG-04 — `throw` в Consumer без настройки Dead Letter Queue — бесконечные retry

**Проблема / Описание**  
При возникновении исключения Consumer делает `throw`, что заставляет MassTransit повторять обработку сообщения. По умолчанию MassTransit выполняет несколько попыток, после чего сообщение попадает в `_error` очередь. Нет явной настройки политики retry и DLQ для этого endpoint'а.

**Конкретно в чём проблема**  
Нет контроля над числом попыток и поведением при исчерпании retry; ошибочные сообщения могут накапливаться в `push-notifications-handler_error` без алертинга.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Consumers/PushNotificationConsumer.cs` : строки 115–123  
`Backend/Barkfluff.CloudMessaging/Program.cs` : строки 53–56

```csharp
// В Consumer:
catch (Exception ex)
{
    _logger.LogError(ex, "Ошибка при обработке push-уведомления...");
    throw; // ← MassTransit получит исключение и начнёт retry по умолчанию
}

// В Program.cs — нет явной retry-политики:
cfg.ReceiveEndpoint("push-notifications-handler", e =>
{
    e.ConfigureConsumer<PushNotificationConsumer>(context);
    // ← нет e.UseMessageRetry(...) и Dead Letter настроек
});
```

**Варианты решения**

```csharp
// при ошибке просто залогировать для того чтробы оно считалось обработанным
```
