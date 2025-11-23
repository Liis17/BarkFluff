# FastAuth Microservice

## Назначение

Сервис FastAuth отвечает за **быструю аутентификацию через QR-коды** в системе BarkFluff. Он управляет:

- 📱 Генерацией QR-кодов для быстрого входа
- 🔐 Созданием временных сессий аутентификации
- ✅ Подтверждением QR-сессий авторизованными пользователями
- 🎫 Генерацией FastAuth токенов для клиентов
- ⏱️ Автоматическим истечением неиспользуемых сессий

**Порт**: 7008
**База данных**: PostgreSQL (`fastauth_db`)
**Зависимости**: Configuration service, Identity service (опционально)

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Entity Framework Core**: ORM
- **PostgreSQL**: База данных сессий
- **JWT**: FastAuth токены

## Архитектура

```
┌─────────────────────────────────────────────┐
│           FastAuth Service                   │
├─────────────────────────────────────────────┤
│  ┌──────────────┐      ┌─────────────────┐ │
│  │  gRPC API    │─────►│  Storage        │ │
│  │              │      │ (Sessions)      │ │
│  └──────────────┘      └────────┬────────┘ │
│                                 │          │
│                                 ↓          │
│                        ┌─────────────────┐ │
│                        │  PostgreSQL     │ │
│                        │ (Sessions DB)   │ │
│                        └─────────────────┘ │
└─────────────────────────────────────────────┘

Flow:
1. Desktop App → CreateFastAuthSession() → QR Code
2. Mobile App → Scans QR → ConfirmFastAuthSession(sessionId, userId)
3. Desktop App → Polls GetFastAuthSession() → Gets userId
4. Desktop App → Identity.FastAuth(sessionId) → Access Token
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **FastAuthSessions** | Временные сессии для QR-аутентификации |

### Основные сущности

#### FastAuthSession

```csharp
public class FastAuthSession
{
    public Guid Id { get; set; }                 // Session ID (в QR-коде)
    public long? UserId { get; set; }            // Подтверждающий пользователь (null до подтверждения)
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }      // Обычно CreatedAt + 5 минут
    public bool IsConfirmed { get; set; }        // Подтверждена ли сессия
    public DateTime? ConfirmedAt { get; set; }
}
```

**Lifecycle**:
1. **Создание**: `IsConfirmed = false`, `UserId = null`
2. **Подтверждение**: `IsConfirmed = true`, `UserId = <confirming user>`
3. **Истечение**: `DateTime.UtcNow > ExpiresAt`

**Индексы**:
- Primary Key: `Id`
- Index на `ExpiresAt` для очистки expired сессий

## Ключевые функции

### 1. Создание FastAuth сессии

**gRPC Method**: `CreateFastAuthSession`

**Request**:
```protobuf
message CreateFastAuthSessionRequest {
  // Пустой запрос
}
```

**Response**:
```protobuf
message CreateFastAuthSessionResponse {
  string session_id = 1;        // UUID сессии
  string qr_code_data = 2;       // Данные для QR-кода (обычно URL)
  int64 expires_in = 3;          // Секунды до истечения
}
```

**Реализация** (Features/CreateFastAuthSession/CreateFastAuthSessionCommandHandler.cs):
```csharp
public class CreateFastAuthSessionCommandHandler
    : IRequestHandler<CreateFastAuthSessionCommand, CreateFastAuthSessionResponse>
{
    private readonly IFastAuthStorage _storage;
    private readonly IConfiguration _configuration;

    public async Task<CreateFastAuthSessionResponse> Handle(
        CreateFastAuthSessionCommand request,
        CancellationToken cancellationToken)
    {
        var session = new FastAuthSession
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(5),  // 5 минут
            IsConfirmed = false,
            UserId = null
        };

        await _storage.FastAuthSessions.AddAsync(session);
        await _storage.SaveChangesAsync();

        var serverUrl = _configuration["ServerUrl"] ?? "http://localhost:7008";
        var qrCodeData = $"{serverUrl}/fastauth/{session.Id}";

        return new CreateFastAuthSessionResponse
        {
            SessionId = session.Id.ToString(),
            QrCodeData = qrCodeData,
            ExpiresIn = (long)(session.ExpiresAt - DateTime.UtcNow).TotalSeconds
        };
    }
}
```

**QR Code Format**:
```
barkfluff://fastauth/{session-id}
или
http://server:7008/fastauth/{session-id}
```

**Использование на Desktop клиенте**:
```csharp
// 1. Создать сессию
var response = await fastAuthClient.CreateFastAuthSessionAsync(
    new CreateFastAuthSessionRequest()
);

// 2. Сгенерировать QR-код из qrCodeData
var qrGenerator = new QRCodeGenerator();
var qrCodeData = qrGenerator.CreateQrCode(
    response.QrCodeData,
    QRCodeGenerator.ECCLevel.Q
);

// 3. Отобразить QR-код пользователю
DisplayQrCode(qrCodeData);

// 4. Начать polling GetFastAuthSession
while (!sessionConfirmed && !expired)
{
    await Task.Delay(2000); // Опрос каждые 2 секунды
    var session = await GetFastAuthSessionAsync(response.SessionId);

    if (session.IsConfirmed)
    {
        // Получить userId и аутентифицироваться
        var userId = session.UserId;
        await AuthenticateWithFastAuthAsync(response.SessionId);
        break;
    }
}
```

### 2. Подтверждение FastAuth сессии

**gRPC Method**: `ConfirmFastAuthSession`

**Request**:
```protobuf
message ConfirmFastAuthSessionRequest {
  string session_id = 1;      // UUID сессии из QR-кода
}
```

**Response**:
```protobuf
message ConfirmFastAuthSessionResponse {
  bool success = 1;
}
```

**Требует**: User JWT token (пользователь должен быть авторизован)

**Реализация** (Features/ConfirmFastAuthSession/ConfirmFastAuthSessionCommandHandler.cs):
```csharp
public class ConfirmFastAuthSessionCommandHandler
    : IRequestHandler<ConfirmFastAuthSessionCommand, ConfirmFastAuthSessionResponse>
{
    private readonly IFastAuthStorage _storage;
    private readonly IUserContext _userContext;
    private readonly ILogger<ConfirmFastAuthSessionCommandHandler> _logger;

    public async Task<ConfirmFastAuthSessionResponse> Handle(
        ConfirmFastAuthSessionCommand request,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.Parse(request.SessionId);
        var session = await _storage.FastAuthSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                "Session not found"
            ));
        }

        if (session.ExpiresAt < DateTime.UtcNow)
        {
            throw new RpcException(new Status(
                StatusCode.DeadlineExceeded,
                "Session expired"
            ));
        }

        if (session.IsConfirmed)
        {
            throw new RpcException(new Status(
                StatusCode.AlreadyExists,
                "Session already confirmed"
            ));
        }

        // Подтверждение сессии
        session.UserId = _userContext.UserId;
        session.IsConfirmed = true;
        session.ConfirmedAt = DateTime.UtcNow;

        await _storage.SaveChangesAsync();

        _logger.LogInformation(
            "FastAuth session {SessionId} confirmed by user {UserId}",
            session.Id,
            session.UserId
        );

        return new ConfirmFastAuthSessionResponse
        {
            Success = true
        };
    }
}
```

**Использование на Mobile клиенте**:
```csharp
// 1. Сканировать QR-код
var qrCodeData = await ScanQrCodeAsync();

// Извлечь session ID из URL
var sessionId = ExtractSessionId(qrCodeData);

// 2. Подтвердить сессию (требуется авторизация)
await fastAuthClient.ConfirmFastAuthSessionAsync(
    new ConfirmFastAuthSessionRequest
    {
        SessionId = sessionId
    },
    headers: new Metadata
    {
        { "x-auth-token", userAccessToken }
    }
);

// 3. Показать успешное подтверждение
ShowMessage("Desktop app authenticated successfully!");
```

### 3. Получение статуса сессии

**gRPC Method**: `GetFastAuthSession`

**Request**:
```protobuf
message GetFastAuthSessionRequest {
  string session_id = 1;
}
```

**Response**:
```protobuf
message GetFastAuthSessionResponse {
  string session_id = 1;
  bool is_confirmed = 2;
  int64 user_id = 3;          // 0 если ещё не подтверждена
  int64 expires_in = 4;       // Секунды до истечения
}
```

**Реализация** (Features/GetFastAuthSession/GetFastAuthSessionQueryHandler.cs):
```csharp
public class GetFastAuthSessionQueryHandler
    : IRequestHandler<GetFastAuthSessionQuery, GetFastAuthSessionResponse>
{
    private readonly IFastAuthStorage _storage;

    public async Task<GetFastAuthSessionResponse> Handle(
        GetFastAuthSessionQuery request,
        CancellationToken cancellationToken)
    {
        var sessionId = Guid.Parse(request.SessionId);
        var session = await _storage.FastAuthSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session == null)
        {
            throw new RpcException(new Status(
                StatusCode.NotFound,
                "Session not found"
            ));
        }

        var expiresIn = (long)(session.ExpiresAt - DateTime.UtcNow).TotalSeconds;

        return new GetFastAuthSessionResponse
        {
            SessionId = session.Id.ToString(),
            IsConfirmed = session.IsConfirmed,
            UserId = session.UserId ?? 0,
            ExpiresIn = Math.Max(0, expiresIn)
        };
    }
}
```

### 4. Интеграция с Identity Service

**FastAuth flow через Identity**:

```csharp
// Identity Service: FastAuth method
public override async Task<AuthResponse> FastAuth(
    FastAuthRequest request,
    ServerCallContext context)
{
    // 1. Проверить сессию в FastAuth service
    var fastAuthSession = await _fastAuthClient.GetFastAuthSessionAsync(
        new GetFastAuthSessionRequest
        {
            SessionId = request.SessionId
        }
    );

    if (!fastAuthSession.IsConfirmed)
    {
        throw new RpcException(new Status(
            StatusCode.PermissionDenied,
            "Session not confirmed yet"
        ));
    }

    var userId = fastAuthSession.UserId;

    // 2. Генерация Access Token и Refresh Token
    var accessToken = _jwtService.GenerateToken(
        userId,
        TokenType.User
    );

    var refreshToken = await _refreshTokenService.CreateRefreshTokenAsync(
        userId,
        context.GetDeviceName()
    );

    return new AuthResponse
    {
        AccessToken = accessToken,
        RefreshToken = refreshToken.Value
    };
}
```

## Полный сценарий использования

### Сценарий: Вход на Desktop через Mobile App

```
Desktop App (не авторизован):
  │
  ├─1─→ FastAuth.CreateFastAuthSession()
  │       └─→ { sessionId, qrCodeData, expiresIn }
  │
  ├─2─→ Генерация и отображение QR-кода
  │
  └─3─→ Polling FastAuth.GetFastAuthSession() каждые 2 секунды
          │
          └─→ Ожидание подтверждения...

Mobile App (авторизован):
  │
  ├─4─→ Сканирование QR-кода
  │       └─→ sessionId из QR
  │
  ├─5─→ FastAuth.ConfirmFastAuthSession(sessionId)
  │       [с User JWT token в headers]
  │       └─→ Success
  │
  └─6─→ Показ "Desktop app authenticated!"

Desktop App (продолжение):
  │
  ├─7─→ Polling обнаружил IsConfirmed = true
  │
  ├─8─→ Identity.FastAuth(sessionId)
  │       └─→ { accessToken, refreshToken }
  │
  └─9─→ Успешный вход, перенаправление на главный экран
```

## Безопасность

### Время жизни сессий

**Срок**: 5 минут

**Обоснование**: Баланс между удобством и безопасностью.

**Рекомендация**: Настраиваемый параметр через Configuration service.

### Session ID

**Формат**: GUID (128-bit UUID v4)

**Entropy**: Достаточно высокая для предотвращения bruteforce.

### Rate Limiting

**Рекомендация**: Ограничение на количество создаваемых сессий:
```csharp
// Не более 10 сессий в минуту с одного IP
[RateLimit(Requests = 10, Period = "1m")]
public override Task<CreateFastAuthSessionResponse> CreateFastAuthSession(...)
```

### Очистка expired сессий

**Background Job** (Services/ExpiredSessionsCleanupService.cs):
```csharp
public class ExpiredSessionsCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExpiredSessionsCleanupService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            using var scope = _serviceProvider.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IFastAuthStorage>();

            var expiredSessions = await storage.FastAuthSessions
                .Where(s => s.ExpiresAt < DateTime.UtcNow)
                .ToListAsync();

            if (expiredSessions.Any())
            {
                storage.FastAuthSessions.RemoveRange(expiredSessions);
                await storage.SaveChangesAsync();

                _logger.LogInformation(
                    "Cleaned up {Count} expired FastAuth sessions",
                    expiredSessions.Count
                );
            }
        }
    }
}
```

## Зависимости

### Configuration Service (gRPC)

**Методы**:
- `LoadConfiguration` - загрузка настроек при старте

**Настройки**:
```json
{
  "FastAuthDb": "Host=postgres;Database=fastauth_db;...",
  "JwtSettings": { ... },
  "ServerUrl": "http://localhost:7008"
}
```

### Identity Service (опционально)

**Направление**: Identity → FastAuth

**Использование**: Identity вызывает FastAuth.GetFastAuthSession для проверки статуса.

## API Reference

### gRPC Methods (FastAuthApi)

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `CreateFastAuthSession` | ❌ Нет | Создание новой QR-сессии |
| `GetFastAuthSession` | ❌ Нет | Получение статуса сессии |
| `ConfirmFastAuthSession` | ✅ User | Подтверждение сессии авторизованным пользователем |

## Конфигурация

### appsettings.json

```json
{
  "FastAuthDb": "Host=postgres;Database=fastauth_db;Username=postgres;Password=postgres",
  "ServerUrl": "http://localhost:7008",
  "SessionExpiryMinutes": 5,
  "JwtSettings": {
    "SecretKey": "your-secret-key",
    "Issuer": "BarkFluff.Identity",
    "Audience": "BarkFluff"
  }
}
```

### Переменные окружения

- `FastAuthDb` - строка подключения PostgreSQL
- `ServerUrl` - публичный URL сервера для QR-кодов
- `SessionExpiryMinutes` - время жизни сессий

## Известные проблемы

### 🟡 Средние

1. **Отсутствие device binding**
   - Любой может подтвердить любую сессию
   - **Рекомендация**: Привязка к device fingerprint

2. **Нет notification о подтверждении**
   - Desktop app должен polling
   - **Рекомендация**: WebSocket или Server-Sent Events

3. **Session replay attack**
   - Можно повторно использовать sessionId
   - **Рекомендация**: One-time use sessions

### 🟢 Низкие

4. **Нет аналитики использования**
   - Неизвестно, как часто используется FastAuth
   - **Рекомендация**: Логирование метрик

## Troubleshooting

### Проблема: "Session expired"

**Причина**: Прошло более 5 минут с создания сессии.

**Решение**: Создать новую сессию и сгенерировать новый QR-код.

### Проблема: "Session not found"

**Причина**: Session ID некорректный или сессия была удалена.

**Решение**:
1. Проверить правильность sessionId
2. Убедиться, что сессия не истекла
3. Создать новую сессию

### Проблема: Polling не обнаруживает подтверждение

**Причина**: Mobile app не смог подтвердить или сетевая ошибка.

**Решение**:
1. Проверить логи FastAuth service
2. Убедиться, что Mobile app авторизован
3. Проверить сетевое соединение Mobile app

## Метрики и мониторинг

### Ключевые метрики

- **Sessions Created/hour**: Частота создания сессий
- **Sessions Confirmed/hour**: Успешные подтверждения
- **Confirmation Rate**: Процент подтверждённых сессий
- **Average Confirmation Time**: Время от создания до подтверждения
- **Expired Sessions/hour**: Количество истекших сессий

### Логи

**Примеры**:
```
[2025-11-23 15:30:45] INFO: FastAuth session created: {SessionId}
[2025-11-23 15:31:12] INFO: FastAuth session {SessionId} confirmed by user {UserId}
[2025-11-23 15:35:45] INFO: Cleaned up 15 expired FastAuth sessions
[2025-11-23 16:00:00] WARNING: High rate of expired sessions detected
```

## Примеры использования

### Пример 1: Desktop App - Создание QR-кода

```csharp
public async Task<string> ShowFastAuthQrCodeAsync()
{
    var response = await _fastAuthClient.CreateFastAuthSessionAsync(
        new CreateFastAuthSessionRequest()
    );

    // Генерация QR-кода
    var qrGenerator = new QRCodeGenerator();
    var qrCodeData = qrGenerator.CreateQrCode(
        response.QrCodeData,
        QRCodeGenerator.ECCLevel.Q
    );

    var qrCode = new QRCode(qrCodeData);
    var qrCodeImage = qrCode.GetGraphic(20);

    // Отображение
    QrCodeImage.Source = ConvertToImageSource(qrCodeImage);

    return response.SessionId;
}

public async Task<bool> WaitForConfirmationAsync(
    string sessionId,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await Task.Delay(2000, cancellationToken);

        var session = await _fastAuthClient.GetFastAuthSessionAsync(
            new GetFastAuthSessionRequest { SessionId = sessionId }
        );

        if (session.IsConfirmed)
        {
            _logger.LogInformation("Session confirmed by user {UserId}", session.UserId);
            return true;
        }

        if (session.ExpiresIn <= 0)
        {
            _logger.LogWarning("Session expired");
            return false;
        }
    }

    return false;
}
```

### Пример 2: Mobile App - Подтверждение сессии

```csharp
public async Task ConfirmQrCodeAsync(string qrCodeData)
{
    // Извлечение sessionId из QR
    var uri = new Uri(qrCodeData);
    var sessionId = uri.Segments.Last();

    try
    {
        await _fastAuthClient.ConfirmFastAuthSessionAsync(
            new ConfirmFastAuthSessionRequest
            {
                SessionId = sessionId
            },
            headers: CreateAuthHeaders()
        );

        ShowNotification("Desktop app authenticated successfully!");
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
    {
        ShowError("QR code expired. Please scan a new one.");
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
    {
        ShowError("This QR code has already been used.");
    }
}
```

## Расположение в коде

**Путь**: `/Backend/BarkFluff.FastAuth/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/FastAuthApiService.cs` - gRPC endpoints
- `Features/CreateFastAuthSession/` - создание сессии
- `Features/ConfirmFastAuthSession/` - подтверждение сессии
- `Features/GetFastAuthSession/` - получение статуса
- `Services/ExpiredSessionsCleanupService.cs` - очистка expired
- `Persistence/FastAuthDbContext.cs` - EF Core контекст
