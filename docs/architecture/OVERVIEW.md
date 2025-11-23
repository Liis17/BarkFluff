# Архитектура BarkFluff Backend

## Общий обзор

BarkFluff построен по принципам **микросервисной архитектуры** с event-driven подходом. Система состоит из 10 независимых микросервисов, каждый из которых отвечает за свою область функциональности.

## Архитектурные принципы

### 1. Микросервисная архитектура

Каждый сервис:
- ✅ Имеет собственную базу данных (Database per Service pattern)
- ✅ Независимо разворачивается в Docker контейнере
- ✅ Общается через gRPC (синхронно) и RabbitMQ (асинхронно)
- ✅ Следует Clean Architecture / Domain-Driven Design
- ✅ Использует CQRS pattern с MediatR

### 2. API-First подход

- Все API определяются через Protocol Buffers (`.proto` файлы)
- Единый источник истины для контрактов между сервисами
- Строгая типизация и автоматическая кодогенерация

### 3. Event-Driven Architecture

Использование RabbitMQ для:
- Асинхронных уведомлений между сервисами
- Decoupling сервисов (слабая связность)
- Guaranteed delivery сообщений

### 4. Централизованная конфигурация

Configuration Service хранит все настройки:
- Эндпоинты сервисов
- Параметры подключения к БД
- JWT настройки
- Бизнес-настройки

## Слои архитектуры

### Presentation Layer (API Gateway)

```
Client → Beacon Service → Микросервисы
```

**Beacon** выступает как:
- Центральная точка входа
- Service registry (предоставляет информацию о всех сервисах)
- Health check aggregator

### Business Logic Layer (Микросервисы)

Каждый микросервис имеет стандартную структуру:

```
BarkFluff.ServiceName/
├── Domain/              # Бизнес-сущности
├── Features/            # CQRS команды/запросы
├── Host/                # gRPC endpoints
├── Persistence/         # EF Core + PostgreSQL
├── Infrastructure/      # Внешние интеграции
└── Program.cs           # Конфигурация сервиса
```

### Data Layer

- **PostgreSQL**: Основное хранилище данных
- **Redis**: Кеш для часто запрашиваемых данных
- **Minio**: S3-совместимое хранилище файлов

### Integration Layer

- **gRPC**: Синхронное взаимодействие
- **RabbitMQ**: Асинхронные события
- **SMTP**: Email нотификации

## Паттерны взаимодействия

### 1. Синхронный запрос-ответ (gRPC)

```
Messages Service ─[gRPC]→ Users Service
                          (GetUserById)
         ←[Response]─ User Details
```

Используется для:
- Получения данных от других сервисов
- Валидации перед операциями
- Критичных по времени операций

### 2. Асинхронные события (RabbitMQ)

```
Users Service ─[RabbitMQ]→ UserChangedName Event
                              │
         ┌────────────────────┼────────────┐
         ↓                    ↓            ↓
    Messages Service    Updates Service   Cache Invalidation
```

Используется для:
- Уведомлений о изменениях
- Decoupling сервисов
- Email нотификаций

### 3. Server-side streaming (gRPC)

```
Client ─[Subscribe]→ Updates Service
       ←─[Stream]─── Real-time events
```

Используется для:
- Real-time уведомлений о новых сообщениях
- Push-уведомлений
- Live updates

## Типы API

### 1. User-facing API

Требует User JWT token:
```csharp
[Authorize(Policy = nameof(TokenType.User))]
```

Примеры:
- `UsersApi.GetUser()`
- `MessagesApi.SendMessage()`
- `FilesApi.GetUploadUrl()`

### 2. Service-to-Service API

Требует Service JWT token:
```csharp
[Authorize(Policy = nameof(TokenType.Service))]
```

Примеры:
- `UsersServerApi.GetById()`
- `FilesServerApi.GetFileData()`

### 3. Public API

Без аутентификации:
```csharp
// No [Authorize] attribute
```

Примеры:
- `BeaconApi.GetServerInfo()`
- `NavigatorApi.ListServers()`

## Service Discovery

### Startup Discovery Flow

```
1. Service starts
2. LoadConfiguration(ServiceId.ServiceName)
3. Configuration Service returns:
   - JwtSettings (secret, issuer, audience)
   - RunSettings (host, port, TLS)
   - DatabaseSettings (connection strings)
   - Other service endpoints
4. Service configures itself
5. Service registers with Navigator (optional)
```

### Runtime Discovery

```
Service A needs Service B endpoint
   │
   ├─► Option 1: Query Configuration Service
   │   (GetConfiguration for ServiceB)
   │
   └─► Option 2: Query Beacon Service
       (GetServerInfo returns all endpoints)
```

## Security

### Authentication Flow

1. **User Login**:
   ```
   Client → Identity.Auth(username, password, otp)
          ← Access Token (short-lived) + Refresh Token (long-lived)
   ```

2. **API Requests**:
   ```
   Client → Service (x-auth-token: JWT)
          → XAuth middleware validates token
          → UserContext extracts userId
          → Request processed
   ```

3. **Token Refresh**:
   ```
   Client → Identity.CreateToken(refresh_token)
          ← New Access Token
   ```

### Authorization Policies

- **User Policy**: `IdentityClaims.TokenType == "User"`
- **Service Policy**: `IdentityClaims.TokenType == "Service"`
- **FastAuth Policy**: `IdentityClaims.TokenType == "FastAuth"`

### Service-to-Service Auth

```csharp
// Example: Messages calling Users service
builder.Services.AddGrpcClient<UsersServerApiClient>(o => {
    o.Address = new Uri(config["UsersService:Host"]);
})
.AddInterceptor(() => new JwtClientInterceptor(
    config["UsersService:Token"]  // Service token
));
```

## Observability

### Logging

Все сервисы используют стандартный `ILogger<T>`:
```csharp
_logger.LogInformation("User {UserId} sent message", userId);
_logger.LogError(ex, "Failed to process message");
```

### Error Handling

Все gRPC сервисы имеют `ServerExceptionInterceptor`:
- Перехватывает необработанные исключения
- Логирует ошибки
- Возвращает gRPC статус коды

### Health Checks

*(В разработке)* - планируется интеграция ASP.NET Core Health Checks

## Scalability

### Horizontal Scaling

Сервисы могут масштабироваться горизонтально:
- **Stateless design**: Состояние хранится в БД/Redis
- **Load balancing**: Через reverse proxy (Nginx/Envoy)
- **Message distribution**: RabbitMQ с multiple consumers

### Performance Optimizations

1. **Caching** (Redis):
   - Имена и аватары пользователей в Messages
   - Активные сессии в Updates

2. **Database Indexing**:
   - Composite indexes для часто запрашиваемых полей
   - Полнотекстовый поиск с pg_trgm

3. **Connection Pooling**:
   - EF Core DbContext pooling
   - gRPC channel reuse

## Deployment Architecture

```
Docker Compose Dev Environment:
┌────────────────────────────────────────┐
│ Infrastructure Services                 │
│ - postgres (5432)                       │
│ - rabbitmq (5672, 15672)                │
│ - redis (6379)                          │
│ - minio (9000, 9001)                    │
└────────────────────────────────────────┘
            │
            ▼
┌────────────────────────────────────────┐
│ BarkFluff Microservices                 │
│ - configuration:7003                    │
│ - beacon:7004                           │
│ - identity:7001                         │
│ - users:7002                            │
│ - messages:7006                         │
│ - files:7005                            │
│ - updates:7015                          │
│ - notification:7004                     │
│ - fastauth:7008                         │
│ - navigator:7010                        │
└────────────────────────────────────────┘
```

Production deployment использует Kubernetes для:
- Auto-scaling
- Service mesh (Istio/Linkerd)
- Distributed tracing
- Advanced load balancing

## Будущие улучшения

1. **API Gateway**: Внедрение Kong/Envoy для:
   - Rate limiting
   - Request routing
   - TLS termination

2. **Service Mesh**: Istio для:
   - Mutual TLS
   - Traffic management
   - Observability

3. **Event Sourcing**: Для критичных доменов:
   - Message history
   - Audit log

4. **CQRS with separate read models**:
   - Optimized query databases
   - ElasticSearch для поиска
