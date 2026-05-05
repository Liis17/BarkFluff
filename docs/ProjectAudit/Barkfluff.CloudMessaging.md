# Аудит проекта: Barkfluff.CloudMessaging

> **Дата:** 2025  
> **Ветка:** `dev`  
> **Проект:** `Backend/Barkfluff.CloudMessaging`  
> **Описание:** Микросервис отправки push-уведомлений через Firebase Cloud Messaging (FCM). Подписывается на RabbitMQ-очередь `push-notifications-handler`, получает данные о новом сообщении, запрашивает информацию у сервисов `Users` и `Messages` через gRPC, и отправляет FCM-уведомления на устройства получателей.

---

## Содержание

- [🔴 Безопасность](#-безопасность)
  - [SEC-01 — Секреты Firebase хранятся в конфигурации без управления секретами](#sec-01--секреты-firebase-хранятся-в-конфигурации-без-управления-секретами)
  - [SEC-02 — Нет TTL/expiration на очереди push-уведомлений](#sec-02--нет-ttlexpiration-на-очереди-push-уведомлений)
  - [SEC-03 — Текст сообщения передаётся в event и логируется косвенно](#sec-03--текст-сообщения-передаётся-в-event-и-логируется-косвенно)
- [🟠 Производительность](#-производительность)
  - [PERF-01 — Push-уведомления отправляются последовательно вместо батча](#perf-01--push-уведомления-отправляются-последовательно-вместо-батча)
  - [PERF-02 — Избыточные gRPC-вызовы: данные уже есть в event, но не используются](#perf-02--избыточные-grpc-вызовы-данные-уже-есть-в-event-но-не-используются)
  - [PERF-03 — Нет Retry/CircuitBreaker на gRPC-клиентах](#perf-03--нет-retrycircuitbreaker-на-grpc-клиентах)
- [🐛 Баги и недоработки](#-баги-и-недоработки)
  - [BUG-01 — Невалидный FCM-токен не удаляется из БД](#bug-01--невалидный-fcm-токен-не-удаляется-из-бд)
  - [BUG-02 — Race condition при инициализации FirebaseApp в singleton](#bug-02--race-condition-при-инициализации-firebaseapp-в-singleton)
  - [BUG-03 — .Result после Task.WhenAll — анти-паттерн и потенциальный дедлок](#bug-03--result-после-taskwhenall--анти-паттерн-и-потенциальный-дедлок)
  - [BUG-04 — throw в Consumer без Dead Letter Queue — бесконечные retry](#bug-04--throw-в-consumer-без-dead-letter-queue--бесконечные-retry)
  - [BUG-05 — CancellationToken не передаётся в gRPC-вызовы](#bug-05--cancellationtoken-не-передаётся-в-grpc-вызовы)
- [📋 Прочее / Качество кода](#-прочее--качество-кода)
  - [MISC-01 — Dockerfile не использует .slim образ как final](#misc-01--dockerfile-не-использует-slim-образ-как-final)
  - [MISC-02 — Нет Health Check эндпоинта](#misc-02--нет-health-check-эндпоинта)

---

## 🔴 Безопасность

---

### SEC-01 — Секреты Firebase хранятся в конфигурации без управления секретами

**Проблема / Описание**  
Firebase Admin SDK требует приватный ключ (`private_key`) и другие чувствительные данные сервисного аккаунта. Эти данные читаются напрямую из `IConfiguration` (переменные окружения / appsettings), что означает: они могут оказаться в логах CI/CD, в образе Docker, в истории git, если кто-то случайно закоммитит секреты.

**Конкретно в чём проблема**  
Приватный ключ RSA сервисного аккаунта Google сериализуется в JSON "на лету" и передаётся в SDK. Нет интеграции с хранилищем секретов (Azure Key Vault, HashiCorp Vault, Docker Secrets).

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs` : строки 24–48

```csharp
// ❌ ПРОБЛЕМА: секреты читаются напрямую из IConfiguration
var privateKey = configuration["Firebase:PrivateKey"];   // RSA private key в plaintext!
var clientEmail = configuration["Firebase:ClientEmail"];

// Весь serviceAccount-JSON собирается в памяти из config-значений —
// нет никакой защиты от случайной утечки в логах или трейсах
var serviceAccountJson = JsonSerializer.Serialize(new
{
    type = "service_account",
    private_key = privateKey,   // ← RSA ключ в анонимном объекте
    client_email = clientEmail,
    // ...
});
```

**Варианты решения**

1. Использовать **Google Application Default Credentials** — примонтировать файл `service-account.json` как Docker Secret и указать путь через `GOOGLE_APPLICATION_CREDENTIALS`.
2. Использовать **Azure Key Vault** / **HashiCorp Vault** для получения секретов в runtime.

```csharp
// ✅ ВАРИАНТ 1: GOOGLE_APPLICATION_CREDENTIALS через Docker Secret
// В docker-compose:
// environment:
//   GOOGLE_APPLICATION_CREDENTIALS: /run/secrets/firebase_credentials
// secrets:
//   - firebase_credentials

// В коде — Firebase SDK сам подтягивает ADC, ручная сборка JSON не нужна:
if (FirebaseApp.DefaultInstance == null)
{
    FirebaseApp.Create(new AppOptions
    {
        // GoogleCredential.GetApplicationDefault() использует GOOGLE_APPLICATION_CREDENTIALS
        Credential = GoogleCredential.GetApplicationDefault()
    });
}

// ✅ ВАРИАНТ 2: Файл учётных данных через конфигурацию пути (не содержимого)
var credentialsPath = configuration["Firebase:CredentialsFilePath"]
    ?? throw new InvalidOperationException("Firebase:CredentialsFilePath not configured");

var credential = GoogleCredential.FromFile(credentialsPath);
```

---

### SEC-02 — Нет TTL/expiration на очереди push-уведомлений

**Проблема / Описание**  
RabbitMQ-очередь `push-notifications-handler` не имеет настройки `MessageTtl` (time-to-live). Если сервис лежит несколько часов — в очереди накапливаются устаревшие push-уведомления. При старте сервиса они будут отправлены пользователям спустя часы после события — это плохой UX и потенциально позволяет "replay" старых уведомлений.

**Конкретно в чём проблема**  
Нет ограничения на время жизни сообщения в очереди. Push-уведомления актуальны только несколько минут.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Program.cs` : строки 53–56

```csharp
// ❌ ПРОБЛЕМА: нет TTL — устаревшие уведомления будут отправлены после простоя
cfg.ReceiveEndpoint("push-notifications-handler", e =>
{
    e.ConfigureConsumer<PushNotificationConsumer>(context);
    // ← нет e.SetQueueArgument("x-message-ttl", ...) 
});
```

**Варианты решения**

```csharp
// ✅ Установить TTL 10 минут для сообщений в очереди
cfg.ReceiveEndpoint("push-notifications-handler", e =>
{
    // Сообщения старше 10 минут будут удалены из очереди автоматически
    e.SetQueueArgument("x-message-ttl", (int)TimeSpan.FromMinutes(10).TotalMilliseconds);

    // Также настроить Dead Letter Exchange для истёкших сообщений
    e.SetQueueArgument("x-dead-letter-exchange", "push-notifications-dlx");

    e.ConfigureConsumer<PushNotificationConsumer>(context);
});
```

---

### SEC-03 — Текст сообщения передаётся в event и логируется косвенно

**Проблема / Описание**  
`PushNotificationEvent.MessageText` содержит текст личного сообщения пользователя. Это поле передаётся через RabbitMQ (потенциально без шифрования at-rest) и используется в FCM Data payload. При включённом дебаг-логировании MassTransit может залогировать весь payload события, включая текст переписки.

**Конкретно в чём проблема**  
Персональные данные (содержимое переписки) оседают в брокере сообщений и потенциально в логах.

**Путь к файлу:** `Shared/BarkFluff.Shared.Queue/Messages/PushNotificationEvent.cs` : строка 11  
`Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs` : строка 115

```csharp
// В PushNotificationEvent:
public string? MessageText { get; set; }  // ← текст личного сообщения в очереди

// В FirebaseService — текст попадает в FCM payload (до 100 символов):
["message_text"] = TruncateMessage(messagePreview, 100),
// ← если FCM сервис скомпрометирован — содержимое переписки утечёт
```

**Варианты решения**

```csharp
// ✅ Не передавать текст сообщения в FCM Data payload.
// Клиент сам получит текст по gRPC после получения уведомления.
// В FCM отправлять только идентификаторы:

var message = new Message
{
    Token = fcmToken,
    Data = new Dictionary<string, string>
    {
        ["chat_id"] = chatId,
        ["message_id"] = messageId.ToString(),
        ["type"] = "new_message",
        // ← убрать "message_text" из payload
        // Клиент откроет чат по chat_id и загрузит сообщения сам
    }
};

// Если предпросмотр всё же нужен — хранить его зашифрованным
// и расшифровывать только на стороне клиента
```

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

### PERF-03 — Нет Retry/CircuitBreaker на gRPC-клиентах

**Проблема / Описание**  
gRPC-клиенты к `UsersService` и `MessagesService` не имеют политик повторных попыток и circuit breaker. При кратковременной недоступности сервисов сообщения будут падать с исключениями и уходить на retry всей очереди через MassTransit, вместо того чтобы повторить gRPC-вызов на уровне HTTP/2.

**Конкретно в чём проблема**  
Transient-сбои gRPC → весь consumer падает → сообщение возвращается в очередь → через N секунд повторная попытка с нуля (включая уже выполненные вызовы).

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Program.cs` : строки 25–38

```csharp
// ❌ ПРОБЛЕМА: нет retry и circuit breaker на gRPC-клиентах
builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
    {
        o.Address = new Uri(builder.Configuration["UsersService:Host"] ?? ...);
    })
    .AddInterceptor(() => new JwtClientInterceptor(...))
    .AddInterceptor(() => new ExceptionClientInterceptor());
    // ← нет .AddPolicyHandler() для retry
```

**Варианты решения**

```csharp
// ✅ Добавить Polly retry и circuit breaker через Microsoft.Extensions.Http.Resilience

// Установить пакет: Microsoft.Extensions.Http.Resilience

builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
    {
        o.Address = new Uri(builder.Configuration["UsersService:Host"] ?? ...);
    })
    .AddInterceptor(() => new JwtClientInterceptor(...))
    .AddInterceptor(() => new ExceptionClientInterceptor())
    .AddStandardResilienceHandler(options =>
    {
        // Retry до 3 раз с экспоненциальной задержкой
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
        options.Retry.BackoffType = DelayBackoffType.Exponential;

        // Circuit breaker — открывается при 50% ошибок за 30 сек
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.FailureRatio = 0.5;
    });

// То же самое для MessagesApiClient
```

---

## 🐛 Баги и недоработки

---

### BUG-01 — Невалидный FCM-токен не удаляется из БД

**Проблема / Описание**  
Когда Firebase возвращает `MessagingErrorCode.Unregistered` (токен устарел или устройство удалило приложение) — сервис только логирует предупреждение. Токен остаётся в базе данных и будет использоваться при каждом следующем сообщении, вызывая лишние запросы к Firebase.

**Конкретно в чём проблема**  
Накопление невалидных токенов → бесполезная нагрузка на FCM и сервис Users; утечка данных (токен может быть переназначен другому устройству).

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs` : строки 131–138

```csharp
// ❌ ПРОБЛЕМА: невалидный токен только логируется, но не удаляется из БД
catch (FirebaseMessagingException ex)
{
    if (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
    {
        _logger.LogWarning(
            "FCM токен невалиден или истёк: {TokenPrefix}...",
            fcmToken[..Math.Min(10, fcmToken.Length)]);
        // ← токен остался в БД, следующий раз снова попробуем и снова получим ошибку
    }
}
```

**Варианты решения**

```csharp
// ✅ ВАРИАНТ 1: Вернуть признак невалидного токена из метода
// и опубликовать событие на очистку через RabbitMQ / gRPC

// В FirebaseService — возвращаем результат:
public async Task<FirebaseSendResult> SendNotificationAsync(string fcmToken, ...)
{
    try
    {
        await _messaging!.SendAsync(message);
        return FirebaseSendResult.Success;
    }
    catch (FirebaseMessagingException ex)
        when (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
    {
        _logger.LogWarning("FCM-токен невалиден: {TokenPrefix}...", fcmToken[..10]);
        return FirebaseSendResult.TokenInvalid; // ← сигнализируем вызывающему
    }
}

// В PushNotificationConsumer — убираем невалидные токены:
var invalidTokens = new List<string>();

foreach (var token in tokensResponse.Tokens)
{
    var result = await _firebaseService.SendNotificationAsync(token.FirebaseToken, ...);
    if (result == FirebaseSendResult.TokenInvalid)
        invalidTokens.Add(token.FirebaseToken);
}

// Публикуем событие на удаление невалидных токенов
if (invalidTokens.Count > 0)
{
    await context.Publish(new InvalidFirebaseTokensEvent
    {
        Tokens = invalidTokens
    });
}
```

---

### BUG-02 — Race condition при инициализации FirebaseApp в singleton

**Проблема / Описание**  
`FirebaseService` зарегистрирован как `Singleton`. Инициализация `FirebaseApp.DefaultInstance` защищена проверкой `if (FirebaseApp.DefaultInstance == null)`, но эта проверка **не потокобезопасна**. При параллельном старте (маловероятно, но возможно при warm-up) два потока могут одновременно пройти проверку и попытаться создать два `FirebaseApp`, что бросит исключение.

**Конкретно в чём проблема**  
TOCTOU (Time-of-check/time-of-use) в конструкторе singleton-сервиса.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs` : строки 52–58

```csharp
// ❌ ПРОБЛЕМА: не потокобезопасная проверка (TOCTOU)
if (FirebaseApp.DefaultInstance == null)   // ← поток A и поток B оба видят null
{
    FirebaseApp.Create(new AppOptions       // ← оба пытаются создать → исключение
    {
        Credential = credential
    });
}
```

**Варианты решения**

```csharp
// ✅ Использовать lock или Lazy<T> для потокобезопасной инициализации

private static readonly Lock _firebaseLock = new();

// В конструкторе:
lock (_firebaseLock)
{
    if (FirebaseApp.DefaultInstance == null)
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = credential
        });
    }
}

_messaging = FirebaseMessaging.DefaultInstance;

// ✅ АЛЬТЕРНАТИВА: явно получить или создать именованный экземпляр
// чтобы не зависеть от DefaultInstance:
var appName = "BarkFluffCloudMessaging";
var existingApp = FirebaseApp.GetInstance(appName);  // возвращает null если нет
var app = existingApp ?? FirebaseApp.Create(new AppOptions { Credential = credential }, appName);
_messaging = FirebaseMessaging.GetMessaging(app);
```

---

### BUG-03 — `.Result` после `Task.WhenAll` — анти-паттерн

**Проблема / Описание**  
После `await Task.WhenAll(...)` результаты извлекаются через `.Result` на уже завершённых задачах. Это работает без дедлока только потому, что задачи уже завершены, но является явным анти-паттерном: скрывает `AggregateException` вместо `RpcException`, ухудшает читаемость и может стать источником бага при рефакторинге.

**Конкретно в чём проблема**  
`.Result` на `AsyncUnaryCall<T>.ResponseAsync` маскирует тип исключения и не передаёт `CancellationToken`.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Consumers/PushNotificationConsumer.cs` : строки 58–61

```csharp
// ❌ ПРОБЛЕМА: .Result после WhenAll — анти-паттерн, скрывает AggregateException
await System.Threading.Tasks.Task.WhenAll(senderCall.ResponseAsync, chatInfoCall.ResponseAsync);

var senderResponse = senderCall.ResponseAsync.Result;    // ← .Result на уже завершённой задаче
var chatInfoResponse = chatInfoCall.ResponseAsync.Result; // ← то же самое
```

**Варианты решения**

```csharp
// ✅ Awaiting задач напрямую через переменные после WhenAll:
var senderTask = _usersClient.GetByIdAsync(
    new GetByIdRequest { UserId = message.SenderId },
    cancellationToken: context.CancellationToken).ResponseAsync;

var chatInfoTask = _messagesClient.GetChatInfoAsync(
    new GetChatInfoRequest { ChatId = message.ChatId.ToString() },
    cancellationToken: context.CancellationToken).ResponseAsync;

// Параллельное ожидание с корректным await
await Task.WhenAll(senderTask, chatInfoTask);

// Теперь await на уже завершённых задачах — корректный паттерн
var senderResponse = await senderTask;
var chatInfoResponse = await chatInfoTask;
```

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
// ✅ Явно настроить retry политику и Dead Letter Exchange в Program.cs:
cfg.ReceiveEndpoint("push-notifications-handler", e =>
{
    // 3 попытки с интервалом 1s, 5s, 15s (экспоненциальный backoff)
    e.UseMessageRetry(r =>
    {
        r.Exponential(3,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(15),
            intervalDelta: TimeSpan.FromSeconds(2));

        // Не ретраить при невалидных данных — сразу в DLQ
        r.Ignore<InvalidOperationException>();
    });

    // Настроить Dead Letter Queue
    e.SetQueueArgument("x-dead-letter-exchange", "push-notifications-dlx");
    e.SetQueueArgument("x-message-ttl", (int)TimeSpan.FromMinutes(10).TotalMilliseconds);

    e.ConfigureConsumer<PushNotificationConsumer>(context);
});
```

---

### BUG-05 — `CancellationToken` не передаётся в gRPC-вызовы

**Проблема / Описание**  
MassTransit передаёт `CancellationToken` через `ConsumeContext.CancellationToken`. При graceful shutdown или таймауте обработки этот токен будет отменён, но gRPC-вызовы этого не увидят — они продолжат выполняться, удерживая соединения и ресурсы.

**Конкретно в чём проблема**  
gRPC-вызовы не реагируют на отмену операции → утечка ресурсов при shutdown, зависшие запросы.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Consumers/PushNotificationConsumer.cs` : строки 52–82

```csharp
// ❌ ПРОБЛЕМА: CancellationToken не передаётся ни в один gRPC-вызов
var senderCall = _usersClient.GetByIdAsync(
    new GetByIdRequest { UserId = message.SenderId });
    // ← нет cancellationToken: context.CancellationToken

var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
    new GetDevicesWithFirebaseTokensRequest { UserIds = { message.RecipientUserIds } });
    // ← нет cancellationToken: context.CancellationToken
```

**Варианты решения**

```csharp
// ✅ Передавать CancellationToken во все async-вызовы:
var ct = context.CancellationToken;

var senderTask = _usersClient.GetByIdAsync(
    new GetByIdRequest { UserId = message.SenderId },
    cancellationToken: ct).ResponseAsync;

var chatInfoTask = _messagesClient.GetChatInfoAsync(
    new GetChatInfoRequest { ChatId = message.ChatId.ToString() },
    cancellationToken: ct).ResponseAsync;

await Task.WhenAll(senderTask, chatInfoTask);

var tokensResponse = await _usersClient.GetDevicesWithFirebaseTokensAsync(
    new GetDevicesWithFirebaseTokensRequest { UserIds = { message.RecipientUserIds } },
    cancellationToken: ct);
```

---

## 📋 Прочее / Качество кода

---

### MISC-01 — Dockerfile не использует `.slim` образ как final

**Проблема / Описание**  
В репозитории есть `Dockerfile.slim`, но основной `Dockerfile` использует `aspnet:9.0-noble-chiseled` как runtime образ. `noble-chiseled` — минималистичный образ Ubuntu, что хорошо. Однако `Dockerfile.slim` не используется в продакшне, что может вызвать путаницу: непонятно, какой Dockerfile является каноническим.

**Конкретно в чём проблема**  
Два Dockerfile без явного указания назначения каждого; риск случайно собрать неправильный образ.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Dockerfile` : строка 19  
`Backend/Barkfluff.CloudMessaging/Dockerfile.slim`

```dockerfile
# Основной Dockerfile использует noble-chiseled (уже хороший выбор):
FROM mcr.microsoft.com/dotnet/aspnet:9.0-noble-chiseled AS final

# Но существует ещё Dockerfile.slim — без комментария о назначении
# Неясно: slim для CI? для arm? для edge-окружений?
```

**Варианты решения**

```dockerfile
# ✅ Добавить комментарий-заголовок в каждый Dockerfile:

# Dockerfile — продакшн образ (Ubuntu Noble Chiseled, rootless)
# Используется в docker-compose-prod.yml и CI/CD pipeline

# Dockerfile.slim — минимальный образ для edge/IoT развёртывания
# Использует alpine runtime, меньший размер, но без glibc

# ✅ Либо удалить Dockerfile.slim если он не используется,
# чтобы не создавать путаницу в команде
```

---

### MISC-02 — Нет Health Check эндпоинта

**Проблема / Описание**  
Сервис не регистрирует ни один health check эндпоинт (`/health`, `/ready`). Docker и Kubernetes не могут определить готовность сервиса к работе. При падении Firebase SDK или потере соединения с RabbitMQ — оркестратор не узнает об этом и не перезапустит контейнер.

**Конкретно в чём проблема**  
Нет liveness и readiness проб → невидимые сбои в production; нет индикатора готовности к приёму сообщений.

**Путь к файлу:** `Backend/Barkfluff.CloudMessaging/Program.cs` : строки 60–63

```csharp
// ❌ ПРОБЛЕМА: нет health check — Docker/K8s не могут проверить состояние сервиса
var app = builder.Build();

app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
app.Run(); // ← ни одного app.MapHealthChecks(...)
```

**Варианты решения**

```csharp
// ✅ Зарегистрировать health checks в Program.cs:

// В builder section:
builder.Services.AddHealthChecks()
    .AddRabbitMQ(
        rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMQ:Username"]}:{builder.Configuration["RabbitMQ:Password"]}@{builder.Configuration["RabbitMQ:Host"]}",
        name: "rabbitmq",
        tags: ["ready"])
    .AddCheck<FirebaseHealthCheck>("firebase", tags: ["ready"]);

// После builder.Build():
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    // Liveness — сервис жив (не завис)
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    // Readiness — сервис готов принимать сообщения
    Predicate = check => check.Tags.Contains("ready")
});

// Реализация FirebaseHealthCheck:
public class FirebaseHealthCheck : IHealthCheck
{
    private readonly FirebaseService _firebaseService;

    public FirebaseHealthCheck(FirebaseService firebaseService)
        => _firebaseService = firebaseService;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // FirebaseService.IsInitialized — добавить публичное свойство
        return Task.FromResult(_firebaseService.IsInitialized
            ? HealthCheckResult.Healthy("Firebase SDK инициализирован")
            : HealthCheckResult.Unhealthy("Firebase SDK не инициализирован"));
    }
}
```

---

## Сводная таблица

| ID | Категория | Приоритет | Название |
|---|---|---|---|
| SEC-01 | 🔴 Безопасность | Высокий | Секреты Firebase в конфигурации без secrets management |
| SEC-02 | 🔴 Безопасность | Средний | Нет TTL на очереди push-уведомлений |
| SEC-03 | 🔴 Безопасность | Средний | Текст сообщения в FCM payload и очереди |
| PERF-01 | 🟠 Производительность | Высокий | Последовательная отправка вместо SendEachAsync |
| PERF-02 | 🟠 Производительность | Высокий | 2 лишних gRPC-вызова — данные уже в event |
| PERF-03 | 🟠 Производительность | Средний | Нет Retry/CircuitBreaker на gRPC-клиентах |
| BUG-01 | 🐛 Баг | Высокий | Невалидный FCM-токен не удаляется из БД |
| BUG-02 | 🐛 Баг | Средний | Race condition при инициализации FirebaseApp |
| BUG-03 | 🐛 Баг | Низкий | `.Result` после `Task.WhenAll` — анти-паттерн |
| BUG-04 | 🐛 Баг | Средний | `throw` без DLQ и явной retry-политики |
| BUG-05 | 🐛 Баг | Средний | CancellationToken не передаётся в gRPC-вызовы |
| MISC-01 | 📋 Прочее | Низкий | Два Dockerfile без явного назначения |
| MISC-02 | 📋 Прочее | Средний | Нет Health Check эндпоинта |
