# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Микросервис Identity

Отвечает за аутентификацию, авторизацию, 2FA и управление сессиями. Порт: `7000`.

## Сборка и запуск

```bash
# Сборка
dotnet build BarkFluff.Identity.csproj

# Запуск (требует инфраструктуру через docker-compose в корне Backend/)
dotnet run --project BarkFluff.Identity.csproj

# Миграции применяются автоматически при старте через Program.cs
# Ручное создание миграции:
dotnet ef migrations add <MigrationName> --project BarkFluff.Identity.csproj

# Ручное применение миграций:
dotnet ef database update --project BarkFluff.Identity.csproj
```

## Архитектура

### Слои

- **Domain/** — бизнес-сущности: `RefreshToken`, `UserPassword`, `ConfirmationCode`, `AuthUserProperty`, `ResetPassword`
- **Features/** — CQRS-команды (MediatR). Каждый фолдер: `{Feature}Command.cs` + `{Feature}CommandHandler.cs`
- **Host/** — gRPC-реализация: `IdentityApiService` (публичные методы) + `IdentityServerApiService` (service-to-service)
- **Infrastructure/** — `LocationClient` (ip-api.com геолокация), `NotificationQueueSender` (RabbitMQ)
- **Persistence/Contexts/** — `IdentityContext` (EF Core)
- **Persistence/Services/** — Storage-классы (репозитории): `RefreshTokensStorage`, `AuthPropertiesStorage`, `ConfirmationCodesStorage`, `PasswordsStorage`, `ResetPasswordsStorage`
- **Services/** — `JwtService`, `PasswordHasher` (SHA256), `CodeGenerator` (6-цифр), `RefreshTokenGenerator` (20-символов)
- **Settings/** — `JwtSettings` (SecretKey, Issuer, Audience, ExpiryMinutes)

### gRPC-хосты

- `IdentityApiService` — пользовательские операции (регистрация, вход, 2FA, сессии)
- `IdentityServerApiService` — межсервисные операции, класс помечен `[Authorize(Policy = nameof(TokenType.Service))]`

### Внешние зависимости

- **Users service** — gRPC клиент (`UsersServerApi.UsersServerApiClient`): поиск пользователя, регистрация устройства, создание черновика
- **Notification service** — RabbitMQ (`EmailNotification` через MassTransit): коды подтверждения, алерты входа
- **ip-api.com** — HTTP геолокация по IP (при каждом входе/регистрации, без кеширования)

## Ключевые потоки

### Аутентификация (`AuthCommandHandler`)
1. Проверка логина/пароля и обязательных заголовков (DeviceId, OS и т.д.)
2. Поиск пользователя в Users service
3. Проверка 2FA → может выбросить `OtpCodeNeedException` (Email) или `NotValidOtpCodeException` (TOTP)
4. SHA256-хеш пароля vs. `UserPassword.PasswordHash`
5. Создание `RefreshToken` (старый для этого DeviceId удаляется)
6. `CreateToken` → JWT access token
7. Регистрация устройства в Users service
8. Публикация email-уведомлений (успех/неудача)

### Сброс пароля
1. `ResetPassword` → генерация OTP-кода, создание `ResetPassword` записи, отправка по email
2. `ConfirmResetPassword` → валидация кода, `IsApproved = true`, обнуление `PasswordHash`
3. `SetPassword` (без старого пароля, т.к. хеш null) → запись нового хеша

### Настройка 2FA (TOTP)
1. `EnableOtpVerification` → генерация TOTP-секрета (Otp.NET), возврат клиенту для QR
2. `ConfirmOtpVerification` → валидация кода с секретом → `OtpEnabled = true`
3. `DisableOtpVerification` → требует валидного кода перед отключением

## Добавление новой фичи

1. Создать `Features/{Feature}/{Feature}Command.cs` и `{Feature}CommandHandler.cs`
2. MediatR автоматически регистрирует хендлеры из сборки — регистрация в DI не нужна
3. Добавить метод в `Host/IdentityApiService.cs` или `Host/IdentityServerApiService.cs`
4. Storage-классы зарегистрированы как `Transient` — для нового хранилища добавить аналогично в `Program.cs`
5. Для новых полей в БД — создать миграцию через `dotnet ef migrations add`

## Паттерны кода

### Команда + хендлер
```csharp
public class XxxCommand : IRequest<XxxResponse>
{
    // свойства из gRPC запроса
}

public class XxxCommandHandler : IRequestHandler<XxxCommand, XxxResponse>
{
    // зависимости через конструктор
    public async Task<XxxResponse> Handle(XxxCommand request, CancellationToken ct) { }
}
```

### gRPC-метод
```csharp
[Authorize(Policy = nameof(TokenType.User))]
public override async Task<XxxResponse> Xxx(XxxRequest request, ServerCallContext context)
{
    return await _mediator.Send(new XxxCommand { /* map */ });
}
```

### Метрики
Хост-методы логируют метрики через `MetricsCollector` (из `BarkFluff.GrpcServer`). При добавлении новых значимых операций добавлять счётчики аналогично существующим (`auth_login_attempts`, `tokens_refreshed` и т.д.).

## Обработка ошибок

Исключения из `BarkFluff.Shared.Exceptions` конвертируются в gRPC-статусы через `ServerExceptionInterceptor`. Клиент определяет тип ошибки по trailer-заголовку `x-error-code`. Бросать именно эти типизированные исключения, не `RpcException` напрямую.

## Proto-файлы

Расположены в `Shared/BarkFluff.Proto/`. В `.csproj`:
- `identity_api.proto` — `GrpcServices="Server"`
- `users_api.proto` — `GrpcServices="Client"`
- `shared.proto` — `GrpcServices="None"`
