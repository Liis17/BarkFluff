# BarkFluff.Identity

Аутентификация, авторизация, 2FA, управление сессиями. Порт: **7000**.

Расположение: `Backend/BarkFluff.Identity/`

## Сборка

```bash
dotnet build BarkFluff.Identity.csproj
dotnet ef migrations add <MigrationName> --project BarkFluff.Identity.csproj
```

Миграции применяются автоматически при старте.

## Архитектура

### Слои

- `Domain/` — `RefreshToken`, `UserPassword`, `ConfirmationCode`, `AuthUserProperty`, `ResetPassword`
- `Features/` — CQRS-команды (MediatR)
- `Host/` — `IdentityApiService` (публичные методы) + `IdentityServerApiService` (service-to-service, `[Authorize(Policy = nameof(TokenType.Service))]`)
- `Infrastructure/` — `LocationClient` (ip-api.com геолокация), `NotificationQueueSender` (RabbitMQ)
- `Persistence/Contexts/` — `IdentityContext` (EF Core)
- `Persistence/Services/` — `RefreshTokensStorage`, `AuthPropertiesStorage`, `ConfirmationCodesStorage`, `PasswordsStorage`, `ResetPasswordsStorage`
- `Services/` — `JwtService`, `PasswordHasher` (SHA256), `CodeGenerator` (6 цифр), `RefreshTokenGenerator` (20 символов)
- `Settings/` — `JwtSettings` (SecretKey, Issuer, Audience, ExpiryMinutes)

### Ключевые потоки

**Аутентификация (`AuthCommandHandler`)**:
1. Проверка логина/пароля и обязательных заголовков (DeviceId, OS и т.д.)
2. Поиск пользователя в Users service
3. Проверка 2FA → `OtpCodeNeedException` (Email OTP) или `NotValidOtpCodeException` (TOTP)
4. SHA256-хеш пароля vs `UserPassword.PasswordHash`
5. Создание `RefreshToken` (старый для этого DeviceId удаляется)
6. JWT access token
7. Регистрация устройства в Users service
8. Email-уведомления (успех/неудача через RabbitMQ)

**Сброс пароля**:
1. `ResetPassword` → OTP-код, `ResetPassword` запись, отправка по email
2. `ConfirmResetPassword` → валидация кода, `IsApproved = true`, обнуление `PasswordHash`
3. `SetPassword` (без старого пароля) → запись нового хеша

**TOTP 2FA**:
1. `EnableOtpVerification` → генерация TOTP-секрета (Otp.NET), возврат клиенту для QR
2. `ConfirmOtpVerification` → валидация → `OtpEnabled = true`
3. `DisableOtpVerification` → требует валидного кода

## Внешние зависимости

- **Users service** — gRPC `UsersServerApi.UsersServerApiClient`: поиск пользователя, регистрация устройства
- **Notification service** — RabbitMQ (`EmailNotification`): коды подтверждения, алерты входа
- **ip-api.com** — HTTP геолокация по IP при каждом входе

## Обработка ошибок

Бросать исключения из `BarkFluff.Shared.Exceptions` — `ServerExceptionInterceptor` упакует в gRPC trailer `x-error-code`.

## Proto

- `identity_api.proto` — `GrpcServices="Server"`
- `users_api.proto` — `GrpcServices="Client"`
- `shared.proto` — `GrpcServices="None"`

## Связанные файлы

- [[Shared/Exceptions]] — коды ошибок аутентификации
- [[Backend/Users]] — поиск/регистрация пользователей
- [[Backend/Notification]] — email-уведомления
