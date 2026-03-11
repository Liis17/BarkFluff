# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

BarkFluff — распределённая платформа обмена сообщениями в реальном времени на микросервисной архитектуре (.NET 9.0, gRPC, RabbitMQ). Поддерживает приватные/групповые чаты, вложения, профили пользователей, 2FA, AI-функции (перевод, модерация контента) и real-time обновления через gRPC streaming.

## Technology Stack

- **Backend**: .NET 9.0, gRPC (HTTP/2), MassTransit (RabbitMQ), PostgreSQL + EF Core, Redis, Minio (S3), Docker
- **Windows Client**: WPF (.NET 10), code-behind + reactive wrappers (ReactiveBool/String/Long)
- **Android Client**: Kotlin 2.0.0, AGP 8.9.1, gRPC-OkHttp 1.60.0, ViewBinding
- **Linux Client**: C++ с CMake, gRPC (ранняя стадия)
- **macOS/iOS Clients**: Swift (заглушки/ранняя стадия)

## Build and Run Commands

### Backend (Docker)

```bash
cd Backend
cp sample.env .env  # настроить по шаблону
docker-compose -f docker-compose-dev.yml up -d
docker-compose -f docker-compose-dev.yml ps
docker-compose -f docker-compose-dev.yml down
```

### Сборка отдельного микросервиса

```bash
dotnet build Backend/BarkFluff.Identity/BarkFluff.Identity.csproj
```

### Миграции БД

```bash
# Миграции применяются автоматически при старте сервиса (Program.cs)
# Ручной запуск:
dotnet ef database update --project Backend/BarkFluff.Identity
```

### Android Client

```bash
cd Android/Barkfluff.Client.Android
./gradlew assembleDebug
```

### WPF Client

```bash
dotnet build Windows/BarkFluff.Client.WPF/BarkFluff.Client.WPF.csproj
```

### Тесты

В проекте нет тестовых проектов (xunit/nunit/mstest).

## Microservices Architecture

### Core Infrastructure

| Service | Port | Description |
|---------|------|-------------|
| Configuration | 7003 | Централизованная конфигурация и реестр сервисов |
| Beacon | 7002 | Точка входа, предоставляет информацию о сервисах клиентам |

### Business Services

| Service | Port | Description |
|---------|------|-------------|
| Identity | 7000 | Auth, JWT, 2FA, сброс пароля, сессии |
| Users | 7001 | Профили, связи, бейджи |
| Messages | 7007 | Сообщения, групповые чаты, read receipts |
| Files | 7005 | Minio, загрузка/скачивание файлов, превью |
| Updates | 7015 | Real-time обновления через gRPC streaming |
| Notification | 7004 | Email-уведомления через SMTP |
| FastAuth | 7008 | QR-авторизация |
| Navigator | 7010 | Регистрация/обнаружение серверов BarkFluff |
| Onliner | 7009 | Трекинг онлайн-статусов |

### Additional Backend Services

| Service | Description |
|---------|-------------|
| AdminPanel | ASP.NET Minimal APIs (порт 51888), HTML+JS+Tailwind дашборд, LiteDB |
| WebServer | REST/HTTP, раздача файлов и статики |
| CloudMessaging | Background Worker, RabbitMQ consumer для push-уведомлений |

### Service Discovery Flow

1. Каждый сервис при старте запрашивает конфигурацию у `Configuration` через gRPC
2. Межсервисное взаимодействие — прямые gRPC-вызовы
3. Асинхронные события — через RabbitMQ (MassTransit)

## Microservice Structure

Каждый микросервис следует единой структуре:

```
BarkFluff.{Service}/
├── Domain/           # Бизнес-сущности
├── Features/         # CQRS-команды (MediatR)
│   └── {Feature}/
│       ├── {Xxx}Command.cs
│       └── {Xxx}CommandHandler.cs
├── Host/             # gRPC-сервис (реализация)
├── Infrastructure/   # Клиенты внешних сервисов
├── Persistence/      # EF Core DbContext, миграции
├── Services/         # Доменные сервисы (JWT, хеширование и т.д.)
├── Settings/         # POCO-конфигурации
├── Program.cs        # Startup
└── Dockerfile
```

## Shared Libraries

Located in `Shared/`:

- **BarkFluff.Proto**: Все `.proto` файлы (gRPC-контракты)
- **BarkFluff.Shared.Auth**: gRPC client interceptors (JWT, device, IP, OS metadata)
- **BarkFluff.Shared.Identity**: `ServiceId` enum, `TokenType` enum, `IdentityClaims` constants
- **BarkFluff.Shared.Exceptions**: `BaseGrpcException` и наследники, `ServerExceptionInterceptor`
- **BarkFluff.Shared.SecurityUtilities**: Security helpers
- **BarkFluff.Shared.Queue**: RabbitMQ-события (NewMessageEvent, MessageReadedEvent, ReadReceiptEvent, PushNotificationEvent, UserChanged*, EmailNotification)

Located in `Backend/`:

- **BarkFluff.GrpcServer**: XAuth, ServerExceptionInterceptor, WebApplicationBuilderExtensions, Serilog, Metrics

## Authentication & Authorization (XAuth)

Все сервисы используют систему `XAuth` из `BarkFluff.GrpcServer`:

- JWT через заголовок `x-auth-token`
- Обязательные metadata-заголовки: `x-device-id`, `x-device-name`, `x-ip`, `x-os`, `x-app-name`, `x-app-version`
- Политики авторизации:
  - `TokenType.User` — User или Service токен
  - `TokenType.Service` — только Service токен

```csharp
// Program.cs
builder.Services.AddXAuth(builder.Configuration);
app.UseXAuth();

// Защита gRPC-методов
[Authorize(Policy = nameof(TokenType.User))]
public override Task<XxxResponse> Xxx(XxxRequest request, ServerCallContext context)
```

## Configuration Loading

```csharp
builder.LoadConfiguration(ServiceId.Identity);  // Загрузка из Configuration service
builder.SetRunningAddress(builder.Configuration);
```

## gRPC Client Communication

```csharp
builder.Services.AddGrpcClient<UsersServerApi.UsersServerApiClient>(o =>
    {
        o.Address = new Uri(builder.Configuration["UsersService:Host"]);
    })
    .AddInterceptor(() => new JwtClientInterceptor(builder.Configuration["UsersService:Token"]))
    .AddInterceptor(() => new ExceptionClientInterceptor());
```

## RabbitMQ Integration

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

## Adding New Services

1. Добавить `ServiceId` в `Shared/BarkFluff.Shared.Identity/ServiceId.cs`
2. Зарегистрировать в БД Configuration service
3. Proto-файлы в `Shared/BarkFluff.Proto/`

## Proto Files

Все proto-определения в `Shared/BarkFluff.Proto/`. При подключении:
- Серверная сторона: `<Protobuf Include="..\..\Shared\BarkFluff.Proto\xxx_api.proto" GrpcServices="Server" />`
- Клиентская сторона: `GrpcServices="Client"`
- Только типы (без сервисов): `GrpcServices="None"`

## Client Platforms

### Windows (WPF) — `Windows/BarkFluff.Client.WPF/`
Основной десктопный клиент. Навигация: Welcome → SelectServer → Login → TwoFA → MainWindow. Включает кеширование сообщений, FFPlay для аудио, системные уведомления.

Дополнительные Windows-инструменты: `BarkFluff.DBEditor` (редактор БД), `Barkfluff.Updater.CLI` (обновление), `Barkfluff.Installer.CPP` (инсталлятор).

### Android — `Android/Barkfluff.Client.Android/`
Kotlin + gRPC-OkHttp. Activity-based, SharedPreferences + EncryptedSharedPreferences для токенов. Версии зависимостей в `gradle/libs.versions.toml`.

### Linux — `Linux/`
C++ клиент с CMake, gRPC. Ранняя стадия разработки.

### macOS/iOS — `Mac/Barkfluff/`, `iOS/Barkfluff/`
Swift. Ранняя стадия, в основном документация и заглушки.
