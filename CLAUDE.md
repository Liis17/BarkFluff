# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BarkFluff is a distributed real-time messaging platform built on a microservices architecture using .NET 9.0, gRPC, and RabbitMQ. The system supports private/group chats, file attachments, user profiles, 2FA authentication, and real-time updates.

Это крупный, распределенный проект .NET с микросервисной архитектурой. Система представляет собой платформу для общения в реальном времени с такими функциями, как обмен сообщениями, управление пользователями, обмен файлами и уведомления. Она также включает в себя функции на базе искусственного интеллекта, такие как перевод текста и модерация контента.

## Technology Stack

- **Framework**: .NET 9.0
- **API Protocol**: gRPC (HTTP/2)
- **Message Broker**: RabbitMQ (MassTransit)
- **Databases**: PostgreSQL with Entity Framework Core
- **Cache**: Redis
- **File Storage**: Minio (S3-compatible)
- **Containerization**: Docker
- **Desktop Client**: WPF (Windows Presentation Foundation)

## Build and Run Commands

### Running Backend Services (Docker)

```bash
# From Backend directory
cd Backend

# Create environment file (see sample.env for template)
cp sample.env .env
# Edit .env with your settings

# Start all services with infrastructure
docker-compose -f docker-compose-dev.yml up -d

# Check status
docker-compose -f docker-compose-dev.yml ps

# Stop services
docker-compose -f docker-compose-dev.yml down
```

### Building Individual Services

```bash
# Build specific microservice
dotnet build Backend/BarkFluff.Identity/BarkFluff.Identity.csproj

# Publish for Docker
dotnet publish Backend/BarkFluff.Identity/BarkFluff.Identity.csproj -c Release -o /app/publish
```

### Database Migrations

```bash
# Apply migrations (runs automatically on startup in Program.cs)
# Or manually using EF Core tools:
dotnet ef database update --project Backend/BarkFluff.Identity
```

## Microservices Architecture

### Core Infrastructure

| Service | Port | Description |
|---------|------|-------------|
| Configuration | 7003 | Centralized configuration and service discovery registry |
| Beacon | 7002 | Entry point providing service information to clients |

### Business Services

| Service | Port | Description |
|---------|------|-------------|
| Identity | 7000 | Auth, JWT tokens, 2FA, password reset, sessions |
| Users | 7001 | User profiles, relationships, badges |
| Messages | 7007 | Chat messages, group chats, read receipts |
| Files | 7005 | Minio integration, file upload/download, previews |
| Updates | 7015 | Real-time updates via gRPC streaming |
| Notification | 7004 | Email notifications via SMTP |
| FastAuth | 7008 | QR-based quick authentication |
| Navigator | 7010 | BarkFluff server registration/discovery |
| Onliner | 7009 | User online status tracking |

### Service Discovery Flow

1. Each service starts and queries `Configuration` service for its configuration
2. Services use gRPC to communicate directly with each other
3. Async events flow through RabbitMQ (e.g., Messages publishes events consumed by Updates)

## Microservice Structure

Each microservice follows a consistent pattern:

```
BarkFluff.{Service}/
├── Domain/           # Business entities and value objects
├── Features/         # CQRS-style command handlers (MediatR)
│   └── {Feature}/
│       ├── {Xxx}Command.cs       # IRequest/IRequest<TResponse>
│       └── {Xxx}CommandHandler.cs
├── Host/             # gRPC service implementation
├── Infrastructure/    # External service clients, integration logic
├── Persistence/      # EF Core DbContext, migrations, data services
├── Services/         # Domain services (JWT, hashing, etc.)
├── Settings/         # Configuration POCOs
├── Program.cs        # Startup and service registration
└── Dockerfile
```

## Shared Libraries

Located in `Shared/` directory:

- **BarkFluff.Proto**: All `.proto` files defining gRPC contracts
- **BarkFluff.Shared.Auth**: gRPC client interceptors (JWT, device, IP, OS metadata)
- **BarkFluff.Shared.Identity**: `ServiceId` enum, `TokenType` enum, `IdentityClaims` constants
- **BarkFluff.Shared.SecurityUtilities**: Security helper functions

## Authentication & Authorization (XAuth)

All services use the `XAuth` system from `BarkFluff.GrpcServer`:

- JWT tokens passed via `x-auth-token` header
- Required metadata headers: `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`
- Two authorization policies:
  - `TokenType.User` - Requires User or Service token
  - `TokenType.Service` - Requires Service token only

```csharp
// In Program.cs
builder.Services.AddXAuth(builder.Configuration);
app.UseXAuth();

// Protecting gRPC methods
[Authorize(Policy = nameof(TokenType.User))]
public override Task<XxxResponse> Xxx(XxxRequest request, ServerCallContext context)
```

## Configuration Loading

Services use centralized configuration via `LoadConfiguration()` extension:

```csharp
builder.LoadConfiguration(ServiceId.Identity);  // From Configuration service
builder.SetRunningAddress(builder.Configuration);
```

Configuration is fetched from the Configuration service gRPC API at startup (see `WebApplicationBuilderExtensions.cs`).

## Exception Handling

All exceptions inherit from `BaseGrpcException` in `BarkFluff.Shared.Exceptions`. The `ServerExceptionInterceptor` converts these to standardized gRPC errors with error codes.

## gRPC Client Communication

```csharp
// Add gRPC client with interceptors
builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
    {
        o.Address = new_uri(builder.Configuration["UsersService:Host"]);
    })
    .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
    .AddInterceptor(() => new ExceptionClientInterceptor());
```

## RabbitMQ Integration

Using MassTransit for event publishing:

```csharp
builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMQ:Host"], "/", h =>
        {
            h.Username(builder.Configuration["RabbitMQ:Username"]);
            h.Password(builder.Configuration["RabbitMQ:Password"]);
        });
    });
});
```

## Service IDs

When adding new services, update `ServiceId` enum in `Shared/BarkFluff.Shared.Identity/ServiceId.cs` and register in the Configuration service database.

## Proto Files

All proto definitions are in `Shared/BarkFluff.Proto/`. When modifying:
- Server services: Add `<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Server" />`
- Client services: Add `GrpcServices="Client"`
