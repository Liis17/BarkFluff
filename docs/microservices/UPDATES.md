# Updates Microservice

## Назначение

Сервис Updates отвечает за **доставку real-time обновлений клиентам** в системе BarkFluff. Он управляет:

- 📡 Bi-directional gRPC streaming для постоянных соединений
- 💬 Push-уведомлениями о новых сообщениях
- 🔔 Real-time событиями изменения профилей пользователей
- 📊 Управлением активными подписками клиентов
- 🚀 Маршрутизацией событий от RabbitMQ к подключённым клиентам

**Порт**: 7015
**База данных**: Не используется (stateless)
**Зависимости**: RabbitMQ (consumer), Configuration service

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC Server Streaming**: Для push-уведомлений
- **RabbitMQ** (MassTransit): Потребление событий
- **MediatR**: Внутренняя шина событий
- **In-Memory Storage**: Управление активными подписками

## Архитектура

```
┌─────────────────────────────────────────────┐
│            Updates Service                   │
├─────────────────────────────────────────────┤
│  ┌──────────────┐      ┌─────────────────┐ │
│  │ gRPC Streams │←────►│ Subscriptions   │ │
│  │  (Clients)   │      │    Manager      │ │
│  └──────┬───────┘      └────────┬────────┘ │
│         │                       ↑          │
│         └───────┬───────────────┘          │
│                 ↓                          │
│        ┌─────────────────┐                 │
│        │  MediatR Bus    │                 │
│        └────────┬────────┘                 │
│                 ↑                          │
│        ┌────────┴────────┐                 │
│        │ RabbitMQ        │                 │
│        │ Consumers       │                 │
│        └─────────────────┘                 │
└───────────────┬─────────────────────────────┘
                │
                ↓
        ┌──────────────┐
        │   RabbitMQ   │
        │   (Events)   │
        └──────────────┘
```

## Основные компоненты

### StreamSubscriptionsManager

**Назначение**: Управление активными gRPC stream подписками клиентов.

**Интерфейс**:
```csharp
public interface IStreamSubscriptionsManager
{
    // Регистрация новой подписки
    Task RegisterSubscriptionAsync(
        long userId,
        IServerStreamWriter<UpdateEvent> stream
    );

    // Отмена подписки
    Task UnregisterSubscriptionAsync(long userId);

    // Отправка события конкретному пользователю
    Task SendToUserAsync(long userId, UpdateEvent updateEvent);

    // Отправка события списку пользователей
    Task SendToUsersAsync(
        IEnumerable<long> userIds,
        UpdateEvent updateEvent
    );

    // Получение всех активных подписчиков
    IReadOnlyDictionary<long, IServerStreamWriter<UpdateEvent>> GetActiveSubscriptions();
}
```

**Реализация** (Services/StreamSubscriptionsManager.cs):
```csharp
public class StreamSubscriptionsManager : IStreamSubscriptionsManager
{
    private readonly ConcurrentDictionary<long, IServerStreamWriter<UpdateEvent>>
        _subscriptions = new();

    private readonly ILogger<StreamSubscriptionsManager> _logger;

    public async Task RegisterSubscriptionAsync(
        long userId,
        IServerStreamWriter<UpdateEvent> stream)
    {
        if (_subscriptions.TryAdd(userId, stream))
        {
            _logger.LogInformation(
                "User {UserId} subscribed to updates",
                userId
            );
        }
        else
        {
            _logger.LogWarning(
                "User {UserId} already has an active subscription",
                userId
            );

            // Заменить старую подписку новой
            _subscriptions[userId] = stream;
        }
    }

    public async Task UnregisterSubscriptionAsync(long userId)
    {
        if (_subscriptions.TryRemove(userId, out _))
        {
            _logger.LogInformation(
                "User {UserId} unsubscribed from updates",
                userId
            );
        }
    }

    public async Task SendToUserAsync(long userId, UpdateEvent updateEvent)
    {
        if (_subscriptions.TryGetValue(userId, out var stream))
        {
            try
            {
                await stream.WriteAsync(updateEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send update to user {UserId}",
                    userId
                );

                // Удалить мёртвое соединение
                await UnregisterSubscriptionAsync(userId);
            }
        }
    }

    public async Task SendToUsersAsync(
        IEnumerable<long> userIds,
        UpdateEvent updateEvent)
    {
        var tasks = userIds.Select(userId =>
            SendToUserAsync(userId, updateEvent)
        );

        await Task.WhenAll(tasks);
    }
}
```

**Важно**:
- Один пользователь = одна активная подписка
- При новом подключении старая подписка заменяется
- Ошибки отправки автоматически удаляют мёртвые соединения

## Ключевые функции

### 1. Подписка на новые сообщения

**gRPC Method**: `SubscribeNewMessages` (Server Streaming)

**Request**:
```protobuf
message SubscribeNewMessagesRequest {
  // Пустой запрос
}
```

**Response Stream**:
```protobuf
message UpdateEvent {
  oneof event {
    NewMessageUpdate new_message = 1;
    UserProfileUpdate user_profile = 2;
  }
}

message NewMessageUpdate {
  string chat_id = 1;
  bytes message = 2;  // Serialized Message proto
}
```

**Реализация** (Features/SubscribeNewMessages/SubscribeNewMessagesQueryHandler.cs):
```csharp
public class SubscribeNewMessagesQueryHandler
    : IRequestHandler<SubscribeNewMessagesQuery, Empty>
{
    private readonly IStreamSubscriptionsManager _subscriptionsManager;
    private readonly IUserContext _userContext;

    public async Task<Empty> Handle(
        SubscribeNewMessagesQuery request,
        CancellationToken cancellationToken)
    {
        var userId = _userContext.UserId;
        var stream = request.ResponseStream;

        // Регистрация подписки
        await _subscriptionsManager.RegisterSubscriptionAsync(
            userId,
            stream
        );

        try
        {
            // Держать соединение открытым до отмены
            await Task.Delay(
                Timeout.Infinite,
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            // Клиент отключился
            _logger.LogInformation(
                "User {UserId} disconnected",
                userId
            );
        }
        finally
        {
            // Отменить подписку
            await _subscriptionsManager.UnregisterSubscriptionAsync(userId);
        }

        return new Empty();
    }
}
```

**Процесс**:
```
1. Client → SubscribeNewMessages()
2. Updates → Регистрация stream в StreamSubscriptionsManager
3. Updates → Task.Delay(Infinite) ─┐
4.                                  │ (соединение открыто)
5. ... события приходят ...        │
6. Updates → stream.WriteAsync()   ◄┘
7. Client ◄─ UpdateEvent
```

### 2. Потребление событий из RabbitMQ

#### NewMessageEvent Consumer

**Событие**: Публикуется Messages service при отправке сообщения.

**Payload**:
```csharp
public class NewMessageEvent
{
    public Guid ChatId { get; set; }
    public List<long> ChatMembers { get; set; }  // Кому доставить
    public byte[] Message { get; set; }           // Protobuf bytes
}
```

**Consumer** (Infrastructure/Consumers/NewMessageEventConsumer.cs):
```csharp
public class NewMessageEventConsumer : IConsumer<NewMessageEvent>
{
    private readonly IMediator _mediator;
    private readonly ILogger<NewMessageEventConsumer> _logger;

    public async Task Consume(ConsumeContext<NewMessageEvent> context)
    {
        var message = context.Message;

        _logger.LogInformation(
            "Received NewMessageEvent for chat {ChatId} with {Count} members",
            message.ChatId,
            message.ChatMembers.Count
        );

        // Публикация в MediatR для обработки
        await _mediator.Publish(new NewMessageNotification
        {
            ChatId = message.ChatId,
            ChatMembers = message.ChatMembers,
            MessageBytes = message.Message
        });
    }
}
```

**MediatR Notification Handler** (Features/NewMessage/NewMessageNotificationHandler.cs):
```csharp
public class NewMessageNotificationHandler
    : INotificationHandler<NewMessageNotification>
{
    private readonly IStreamSubscriptionsManager _subscriptionsManager;
    private readonly ILogger<NewMessageNotificationHandler> _logger;

    public async Task Handle(
        NewMessageNotification notification,
        CancellationToken cancellationToken)
    {
        var updateEvent = new UpdateEvent
        {
            NewMessage = new NewMessageUpdate
            {
                ChatId = notification.ChatId.ToString(),
                Message = ByteString.CopyFrom(notification.MessageBytes)
            }
        };

        _logger.LogInformation(
            "Sending NewMessageEvent to {Count} members",
            notification.ChatMembers.Count
        );

        // Отправка всем участникам чата
        await _subscriptionsManager.SendToUsersAsync(
            notification.ChatMembers,
            updateEvent
        );
    }
}
```

**Полный поток**:
```
Messages Service:
  │
  ├─→ RabbitMQ.Publish(NewMessageEvent)
  │
  ↓
Updates Service:
  │
  ├─→ NewMessageEventConsumer.Consume()
  │
  ├─→ MediatR.Publish(NewMessageNotification)
  │
  ├─→ NewMessageNotificationHandler.Handle()
  │
  ├─→ StreamSubscriptionsManager.SendToUsersAsync()
  │
  └─→ foreach member:
        if (member has active subscription):
          stream.WriteAsync(UpdateEvent)
            │
            ↓
          Client receives UpdateEvent
```

#### UserChangedName Consumer

**Событие**: Публикуется Users service при изменении имени пользователя.

**Payload**:
```csharp
public class UserChangedName
{
    public long UserId { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
}
```

**Consumer** (Infrastructure/Consumers/UserChangedNameConsumer.cs):
```csharp
public class UserChangedNameConsumer : IConsumer<UserChangedName>
{
    private readonly IMediator _mediator;

    public async Task Consume(ConsumeContext<UserChangedName> context)
    {
        await _mediator.Publish(new UserProfileChangedNotification
        {
            UserId = context.Message.UserId,
            UpdateType = ProfileUpdateType.Name,
            Data = new Dictionary<string, string>
            {
                ["firstName"] = context.Message.FirstName,
                ["lastName"] = context.Message.LastName
            }
        });
    }
}
```

#### Другие потребляемые события

| Событие | Источник | Описание |
|---------|----------|----------|
| **UserChangedUsername** | Users | Изменение username |
| **UserChangedAvatar** | Users | Изменение аватара |
| **UserChangedBio** | Users | Изменение биографии |

### 3. Поддержание соединения (Heartbeat)

**Проблема**: gRPC streams могут быть закрыты прокси-серверами при длительном отсутствии данных.

**Решение**: Периодическая отправка heartbeat сообщений.

**Реализация** (опциональная):
```csharp
public async Task SendHeartbeatAsync(
    long userId,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

        var heartbeat = new UpdateEvent
        {
            Heartbeat = new HeartbeatUpdate
            {
                Timestamp = DateTime.UtcNow.Ticks
            }
        };

        await _subscriptionsManager.SendToUserAsync(userId, heartbeat);
    }
}
```

## RabbitMQ Конфигурация

### Queues

| Queue | Consumer | Описание |
|-------|----------|----------|
| `new-messages-updates-handler` | NewMessageEventConsumer | Новые сообщения |
| `user-changed-name-updates` | UserChangedNameConsumer | Изменение имени |
| `user-changed-username-updates` | UserChangedUsernameConsumer | Изменение username |
| `user-changed-avatar-updates` | UserChangedAvatarConsumer | Изменение аватара |
| `user-changed-bio-updates` | UserChangedBioConsumer | Изменение биографии |

### Настройка MassTransit

**Program.cs**:
```csharp
builder.Services.AddMassTransit(x =>
{
    // Регистрация всех consumers
    x.AddConsumer<NewMessageEventConsumer>();
    x.AddConsumer<UserChangedNameConsumer>();
    x.AddConsumer<UserChangedUsernameConsumer>();
    x.AddConsumer<UserChangedAvatarConsumer>();
    x.AddConsumer<UserChangedBioConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitMqHost, h =>
        {
            h.Username(rabbitMqUsername);
            h.Password(rabbitMqPassword);
        });

        // Конфигурация endpoints
        cfg.ReceiveEndpoint("new-messages-updates-handler", e =>
        {
            e.ConfigureConsumer<NewMessageEventConsumer>(context);
        });

        cfg.ReceiveEndpoint("user-changed-name-updates", e =>
        {
            e.ConfigureConsumer<UserChangedNameConsumer>(context);
        });

        // ... остальные endpoints
    });
});
```

## API Reference

### gRPC Methods (UpdatesApi)

| Метод | Требует Auth | Тип | Описание |
|-------|--------------|-----|----------|
| `SubscribeNewMessages` | ✅ User | Server Streaming | Подписка на все real-time события |

**ВАЖНО**: Несмотря на название `SubscribeNewMessages`, этот метод доставляет ВСЕ типы обновлений (сообщения, профили и т.д.).

## Зависимости

### Configuration Service (gRPC)

**Методы**:
- `LoadConfiguration` - загрузка настроек при старте

**Настройки**:
```json
{
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  },
  "Server": {
    "Host": "0.0.0.0",
    "Port": 7015
  }
}
```

### RabbitMQ

**Направление**: Messages/Users → RabbitMQ → Updates → Clients

**Критичность**: Высокая. Без RabbitMQ real-time обновления не работают.

## Конфигурация

### appsettings.json

```json
{
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key",
    "Issuer": "BarkFluff.Identity",
    "Audience": "BarkFluff"
  }
}
```

### Переменные окружения

- `RabbitMQ:Host` - адрес RabbitMQ сервера
- `RabbitMQ:Username` - username для RabbitMQ
- `RabbitMQ:Password` - password для RabbitMQ

## Масштабирование

### Проблема: Multiple Instances

При горизонтальном масштабировании Updates service возникает проблема:

```
User 1 подключён к Instance A
User 2 подключён к Instance B

RabbitMQ → Instance A получает NewMessageEvent для User 2
          ❌ User 2 не подключён к Instance A
```

### Решение 1: Shared Redis для подписок

```csharp
public class RedisStreamSubscriptionsManager : IStreamSubscriptionsManager
{
    private readonly IConnectionMultiplexer _redis;

    public async Task RegisterSubscriptionAsync(long userId, ...)
    {
        // Сохранить в Redis: userId -> instanceId
        var db = _redis.GetDatabase();
        await db.StringSetAsync(
            $"user-subscription:{userId}",
            Environment.MachineName
        );
    }

    public async Task SendToUserAsync(long userId, UpdateEvent updateEvent)
    {
        var db = _redis.GetDatabase();
        var instanceId = await db.StringGetAsync($"user-subscription:{userId}");

        if (instanceId == Environment.MachineName)
        {
            // Пользователь подключён к этому instance
            await _localSubscriptions.SendToUserAsync(userId, updateEvent);
        }
        else
        {
            // Опубликовать в Redis Pub/Sub для другого instance
            var subscriber = _redis.GetSubscriber();
            await subscriber.PublishAsync(
                $"updates:{instanceId}",
                JsonSerializer.Serialize(updateEvent)
            );
        }
    }
}
```

### Решение 2: RabbitMQ Routing

Использовать RabbitMQ exchange с routing по userId для доставки событий конкретному instance.

**Текущая реализация**: Не поддерживает multiple instances. Работает только single instance.

## Производительность

### Метрики

- **Active Subscriptions**: Количество подключённых клиентов
- **Events Delivered/sec**: Скорость доставки событий
- **Failed Deliveries**: Количество ошибок отправки
- **Average Delivery Latency**: Задержка от RabbitMQ до клиента

### Оптимизации

1. **Batching**:
   ```csharp
   // Группировка событий для одного пользователя
   var batch = new List<UpdateEvent>();
   // ... накопление событий ...

   foreach (var event in batch)
   {
       await stream.WriteAsync(event);
   }
   ```

2. **Compression**:
   - Использование gRPC compression для больших payload
   ```csharp
   services.AddGrpc(options =>
   {
       options.CompressionProviders = new[]
       {
           new GzipCompressionProvider()
       };
   });
   ```

## Известные проблемы

### 🔴 Критичные

1. **Не поддерживается horizontal scaling**
   - Можно запустить только один instance
   - **Рекомендация**: Реализовать Redis-based subscription manager

### 🟡 Средние

2. **Нет retry механизма для failed deliveries**
   - Если stream.WriteAsync() падает, событие теряется
   - **Рекомендация**: Очередь для retry

3. **Отсутствие heartbeat**
   - Долгие периоды без событий могут привести к закрытию соединения
   - **Рекомендация**: Периодическая отправка heartbeat

### 🟢 Низкие

4. **Нет ограничения на количество подписок**
   - Один пользователь может создать множество соединений
   - **Рекомендация**: Rate limiting

## Troubleshooting

### Проблема: Клиент не получает обновления

**Диагностика**:
1. Проверить активную подписку:
   ```csharp
   var subscriptions = _subscriptionsManager.GetActiveSubscriptions();
   var hasSubscription = subscriptions.ContainsKey(userId);
   ```

2. Проверить RabbitMQ consumer status:
   ```bash
   curl http://rabbitmq:15672/api/queues
   ```

3. Проверить логи на ошибки stream.WriteAsync()

**Решение**: Переподключить клиента или перезапустить RabbitMQ consumer.

### Проблема: "Stream already closed"

**Причина**: Попытка записать в закрытый stream.

**Решение**: Automatic cleanup в StreamSubscriptionsManager удаляет мёртвые соединения.

### Проблема: Memory leak (рост памяти)

**Причина**: Мёртвые subscriptions не удаляются из ConcurrentDictionary.

**Решение**:
```csharp
// Periodic cleanup задача
public async Task CleanupDeadSubscriptionsAsync()
{
    foreach (var (userId, stream) in _subscriptions)
    {
        try
        {
            // Попытка записать пустое сообщение
            await stream.WriteAsync(new UpdateEvent());
        }
        catch
        {
            // Соединение мёртвое
            await UnregisterSubscriptionAsync(userId);
        }
    }
}
```

## Примеры использования

### Пример 1: Клиент подписывается на обновления

```csharp
var client = new UpdatesApiClient(channel);

var call = client.SubscribeNewMessages(new SubscribeNewMessagesRequest());

try
{
    await foreach (var update in call.ResponseStream.ReadAllAsync())
    {
        if (update.NewMessage != null)
        {
            var message = Message.Parser.ParseFrom(update.NewMessage.Message);
            Console.WriteLine($"New message: {message.Text}");
        }
        else if (update.UserProfile != null)
        {
            Console.WriteLine($"User profile updated: {update.UserProfile.UserId}");
        }
    }
}
catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
{
    Console.WriteLine("Subscription cancelled");
}
```

### Пример 2: Messages service отправляет событие

```csharp
// После сохранения сообщения в БД
await _messageBus.Publish(new NewMessageEvent
{
    ChatId = chat.Id,
    ChatMembers = chat.Members.Select(m => m.UserId).ToList(),
    Message = messageProto.ToByteArray()
});

// Updates service автоматически получает и доставляет клиентам
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Updates/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса и MassTransit
- `Host/UpdatesApiService.cs` - gRPC endpoints
- `Services/StreamSubscriptionsManager.cs` - управление подписками
- `Infrastructure/Consumers/` - RabbitMQ consumers
- `Features/NewMessage/` - обработчики событий новых сообщений
- `Features/SubscribeNewMessages/` - CQRS handler для подписки
