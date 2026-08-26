# BarkFluff.Identity — Карта проекта

> Полное описание сервиса: [[Backend/Identity]]

Расположение: `Backend/BarkFluff.Identity/`

---

## Корень

| Файл | Назначение |
|------|-----------|
| `Program.cs` | Точка входа. Регистрация сервисов: EF Core, MediatR, XAuth, gRPC, CORS, MassTransit, gRPC-клиент UsersServerApi, Redis `IConnectionMultiplexer`, `IdentitySecurityOptions` и `IdentityAbuseGuardBehavior`. Автоматическое применение миграций при старте. |
| `appsettings.json` | Базовая конфигурация (заглушка, реальные значения приходят от [[Backend/Configuration]]) |
| `appsettings.Development.json` | Конфигурация для локальной разработки |
| `Dockerfile.slim` | Docker-образ сервиса, используемый CI и production. |

---

## Domain/

Доменные модели — сущности EF Core, хранятся в PostgreSQL через `IdentityContext`.

| Файл | Назначение |
|------|-----------|
| `RefreshToken.cs` | Токен сессии пользователя. Поля: `Value`, `UserId`, `DeviceId`, `CreatedAt`, `ExpiresAt`. TTL: 9999 дней. |
| `AuthUserProperty.cs` | Настройки 2FA пользователя: `OtpEnabled` (TOTP), `EmailOtpEnabled`, `OtpSecret` (Base32), `LastEmailAuthCode`, `SelectedOtpType` (текущий активный тип OTP). |
| `ConfirmationCode.cs` | Код подтверждения (регистрация). Поля: `Value`, `Expires`, `OwnerId`, `Type` (`ConfirmationCodeType`). TTL: 6 часов. |
| `ConfirmationCodeType.cs` | Enum типа кода: `Unknown=0`, `Registration=1`. |
| `OtpType.cs` | Enum типа 2FA: `Unknown=0`, `Authenticator=1`, `Email=2`. |
| `ResetPassword.cs` | Запись на сброс пароля: `Id` (Guid), `UserId`, `CreatedAt`, `OtpType`, `OtpCode`, `IsApproved`. |
| `UserPassword.cs` | Хэш пароля пользователя. |

---

## Features/

CQRS-команды (MediatR). Каждая фича — папка с `*Command.cs` (параметры) + `*CommandHandler.cs` (логика).

| Папка | Команда | Описание |
|-------|---------|----------|
| `Auth/` | `AuthCommand` | Вход по username/email + пароль. Проверка 2FA, пароля, выдача токенов. Публикует `SuccessfulLogin` / `FailedLogin`. |
| `CreateAccount/` | `CreateAccountCommand` | Регистрация (шаг 1). Создаёт draft-пользователя в Users, генерирует `ConfirmationCode`, отправляет email. |
| `ConfirmAccount/` | `ConfirmAccountCommand` | Регистрация (шаг 2). Проверяет код, подтверждает пользователя в Users, выдаёт `RefreshToken`. |
| `CreateToken/` | `CreateTokenCommand` | Обмен `RefreshToken` → новый JWT access token. |
| `Logout/` | `LogoutCommand` | Разлогин: удаляет `RefreshToken`, публикует `SessionRevokedEvent`, удаляет устройство в Users. |
| `SetPassword/` | `SetPasswordCommand` | Установка/смена пароля. При смене требует `OldPassword`. Отправляет `PasswordChanged`. |
| `ResetPassword/` | `ResetPasswordCommand` | Запрос сброса пароля (шаг 1). Создаёт `ResetPassword`-запись, отправляет OTP-код. |
| `ConfirmResetPassword/` | `ConfirmResetPasswordCommand` | Подтверждение сброса (шаг 2). Валидирует OTP, обнуляет пароль, выдаёт токены. |
| `GetActiveSessions/` | `GetActiveSessionsCommand` | Список активных сессий текущего пользователя (`TokenType.User`). |
| `RemoveActiveSession/` | `RemoveActiveSessionCommand` | Удаление сессии по `DeviceId` (`TokenType.User`). |
| `EnableOtpVerification/` | `EnableOtpVerificationCommand` | Включение 2FA. TOTP: генерирует секрет + QR-код. Email: генерирует и высылает код. |
| `ConfirmOtpVerification/` | `ConfirmOtpVerificationCommand` | Подтверждение и активация 2FA. |
| `DisableOtpVerification/` | `DisableOtpVerificationCommand` | Отключение 2FA (`TokenType.User`). |
| `ListOtpVerification/` | `ListOtpVerificationCommand` | Список включённых методов 2FA пользователя. |
| `GetActiveSessionsServer/` | `GetActiveSessionsServerCommand` | Список сессий по `UserId` (service-to-service). |
| `RemoveActiveSessionServer/` | `RemoveActiveSessionServerCommand` | Удаление сессии по `UserId` + `DeviceId` (service-to-service). |
| `CreateSessionForUserServer/` | `CreateSessionForUserServerCommand` | Выпуск пары access+refresh токенов для пользователя из другого сервиса (используется [[Backend/FastAuth]]). |
| `DisableOtpVerificationServer/` | `DisableOtpVerificationServerCommand` | Принудительное отключение 2FA по `UserId` (service-to-service). |
| `ListOtpVerificationServer/` | `ListOtpVerificationServerCommand` | Список методов 2FA по `UserId` (service-to-service). |
| `ForceSetPasswordServer/` | `ForceSetPasswordServerCommand` | Принудительная смена пароля по `UserId` (admin, service-to-service). Запрещает установку пароля боту, отправляет email-уведомление. |
| `CreateBotTokenServer/` | `CreateBotTokenServerCommand` | Выпуск bot-JWT по `BotUserId` через `JwtService.GenerateBotToken`, генерирует `tokenId` (Guid). |

---

## Host/

gRPC-сервисы — точки входа в сервис.

| Файл | Назначение |
|------|-----------|
| `IdentityApiService.cs` | Публичный gRPC-сервис для клиентов. Включает gRPC-Web. Использует `MetricsCollector` для счётчика `auth_login_attempts`. Делегирует всю логику в MediatR-команды. |
| `IdentityServerApiService.cs` | Service-to-service gRPC-сервис. Требует `[Authorize(Policy = nameof(TokenType.Service))]`. Методы: `ListOtpVerificationServer`, `DisableOtpVerificationServer`, `GetActiveSessionsServer`, `RemoveActiveSessionServer`, `CreateSessionForUserServer`, `ForceSetPasswordServer`, `CreateBotTokenServer` (bot-JWT для [[Backend/Bots]]). |

---

## Infrastructure/

Внешние интеграции.

| Файл | Назначение |
|------|-----------|
| `LocationClient.cs` | HTTP-клиент к `ip-api.com`. Геолокация по IP (страна, город). Вызывается при каждом входе/регистрации. |
| `LocationClientExtensions.cs` | Extension-метод `GetLocationString(ipAddress)` — форматирует результат `LocationClient` в строку `"Country, RegionName, City"`. |
| `IpLocation.cs` | DTO для ответа от ip-api.com. |
| `NotificationQueueSender.cs` | Обёртка над MassTransit `IPublishEndpoint`. Публикует `EmailNotification`-события в RabbitMQ. |

---

## Persistence/

| Файл | Назначение |
|------|-----------|
| `Contexts/IdentityContext.cs` | EF Core DbContext. DbSet-ы: `RefreshTokens`, `ConfirmationCodes`, `AuthUserProperties`, `ResetPasswords`, `UserPasswords`. |
| `Contexts/IdentityContextFactory.cs` | Design-time factory (`IDesignTimeDbContextFactory`) для `dotnet ef migrations add`, когда сервис конфигурации недоступен. |
| `Services/RefreshTokensStorage.cs` | CRUD для `RefreshToken`. |
| `Services/ConfirmationCodesStorage.cs` | CRUD для `ConfirmationCode` (регистрация, TTL 6 ч). |
| `Services/AuthPropertiesStorage.cs` | CRUD для `AuthUserProperty` (настройки 2FA). |
| `Services/PasswordsStorage.cs` | CRUD для `UserPassword` (хэш пароля). |
| `Services/ResetPasswordsStorage.cs` | CRUD для `ResetPassword`. |
| `Exceptions/OtpNotCreatedException.cs` | Исключение: OTP-запись не создана. |
| `Exceptions/RefreshTokenNotFoundException.cs` | Исключение: refresh token не найден. |
| `Migrations/` | EF Core миграции (применяются автоматически при старте). |

## Security/

| Файл | Назначение |
|------|-----------|
| `IIdentityAbuseGuard.cs` | Контракт распределённых rate limit, failure counters, lockout и progressive delay |
| `RedisIdentityAbuseGuard.cs` | Атомарные Redis Lua-операции, TTL, разделение ключей по IP/login/user/code/OTP и fail-closed при ошибке Redis |
| `IdentityAbuseGuardBehavior.cs` | MediatR-защита восьми публичных high-risk RPC; Server API-команды не участвуют |
| `IdentityAbuseOperation.cs` / `IdentityFailureResult.cs` | Типы операций и результат счётчика попыток |

---

## Services/

Вспомогательные сервисы без EF-зависимостей.

| Файл | Назначение |
|------|-----------|
| `JwtService.cs` | Генерация JWT-токенов: `GenerateUserToken(userId, deviceId)`, `GenerateServerToken(ServiceId)`, `GenerateBotToken(botUserId, tokenId)`. Использует `JwtSettings`. |
| `PasswordHasher.cs` | Хэширование пароля: BCrypt (workFactor=12). Legacy SHA-256 → Base64 поддерживается только при верификации старых хешей. |
| `CodeGenerator.cs` | `GenerateDigitalCode(length)` — цифровой код произвольной длины (для email OTP / регистрации используется 6 знаков). |
| `RefreshTokenGenerator.cs` | Генерация refresh token — 32 случайных байта → Base64Url без padding = 43 символа. |

---

## Settings/

| Файл | Назначение |
|------|-----------|
| `JwtSettings.cs` | POCO для конфигурации JWT: `SecretKey`, `Issuer`, `Audience`, `ExpiryMinutes`. |
| `IdentitySecurityOptions.cs` | Лимиты, окна TTL, lockout и progressive delay защиты Identity. |

---

## Consumers/

| Файл | Назначение |
|------|-----------|
| `SessionRevokedConsumer.cs` | MassTransit consumer. Очередь `session-revoked-identity`. Получает `SessionRevokedEvent` → вызывает `TokenRevocationCache.Revoke(userId, deviceId, expiresAt)` для немедленной инвалидации access token. |

---

## Proto (Shared)

| Файл | Роль в проекте |
|------|---------------|
| `identity_api.proto` | Определяет `IdentityApi` + `IdentityServerApi` (`GrpcServices="Server"`) |
| `users_api.proto` | gRPC-клиент Users-сервиса (`GrpcServices="Client"`) |
| `shared.proto` | Общие типы (`GrpcServices="None"`) |
