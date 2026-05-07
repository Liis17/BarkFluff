# BarkFluff.Identity

Аутентификация, авторизация, 2FA, управление сессиями. Порт: **7000**.

Расположение: `Backend/BarkFluff.Identity/`

## Сборка

```bash
dotnet build BarkFluff.Identity.csproj
dotnet ef migrations add <MigrationName> --project BarkFluff.Identity.csproj
```

Миграции применяются автоматически при старте.

## Конфигурация (от Configuration-сервиса)

| Ключ | Описание |
|------|----------|
| `IdentityDb` | Connection string PostgreSQL |
| `JwtSettings:SecretKey` | Секрет для JWT |
| `JwtSettings:Issuer` | JWT Issuer |
| `JwtSettings:Audience` | JWT Audience |
| `JwtSettings:ExpiryMinutes` | Время жизни access token (минуты) |
| `UsersService:Host` | gRPC-адрес Users-сервиса |
| `UsersService:Token` | Service token для Users |
| `RabbitMQ:Host` | Адрес брокера |
| `RabbitMQ:Username` / `RabbitMQ:Password` | Credentials MassTransit |

## Архитектура

### Слои

- `Domain/` — `RefreshToken`, `UserPassword`, `ConfirmationCode`, `AuthUserProperty`, `ResetPassword`
- `Features/` — CQRS-команды (MediatR)
- `Host/` — `IdentityApiService` (публичные методы, gRPC-Web) + `IdentityServerApiService` (service-to-service, `[Authorize(Policy = nameof(TokenType.Service))]`)
- `Infrastructure/` — `LocationClient` (ip-api.com, геолокация по IP), `NotificationQueueSender` (RabbitMQ/MassTransit)
- `Persistence/Contexts/` — `IdentityContext` (EF Core + Npgsql)
- `Persistence/Services/` — `RefreshTokensStorage`, `AuthPropertiesStorage`, `ConfirmationCodesStorage`, `PasswordsStorage`, `ResetPasswordsStorage`
- `Services/` — `JwtService`, `PasswordHasher` (BCrypt workFactor=12, с поддержкой legacy SHA-256 на verify), `CodeGenerator` (6-значный цифровой, CSPRNG), `RefreshTokenGenerator` (32 байта, URL-safe Base64, CSPRNG)
- `Settings/` — `JwtSettings` (SecretKey, Issuer, Audience, ExpiryMinutes)
- `Consumers/` — `SessionRevokedConsumer` (MassTransit, слушает `session-revoked-identity`)

### Domain-модели

**`RefreshToken`** — токен сессии пользователя:
- `Value`, `UserId`, `DeviceId`, `CreatedAt`, `ExpiresAt`
- TTL: 9999 дней

**`AuthUserProperty`** — настройки 2FA пользователя:
- `OtpEnabled` (TOTP), `EmailOtpEnabled`, `OtpSecret` (Base32), `LastEmailAuthCode`

**`ConfirmationCode`** — код подтверждения регистрации (TTL 6 часов)

**`ResetPassword`** — запись на сброс пароля:
- `OtpType`, `OtpCode`, `IsApproved`, `UserId`, `CreatedAt`, `ExpiresAt`
- TTL: 5 минут для Email OTP, 15 минут для Authenticator OTP

## gRPC-эндпоинты

### IdentityApiService (публичный, gRPC-Web)

| Метод | Proto-метод | Auth | Описание |
|-------|------------|------|----------|
| `Auth` | `AuthRequest → AuthResponse` | нет | Вход по username/email + пароль |
| `CreateToken` | `CreateTokenRequest → CreateTokenResponse` | нет | Обмен refresh → access token |
| `CreateAccount` | `CreateAccountRequest → CreateAccountResponse` | нет | Регистрация (шаг 1) |
| `ConfirmAccount` | `ConfirmAccountRequest → ConfirmAccountResponse` | нет | Подтверждение email (шаг 2) |
| `GetActiveSessions` | `GetActiveSessionsRequest → GetActiveSessionsResponse` | `TokenType.User` | Список активных сессий |
| `RemoveActiveSession` | `RemoveActiveSessionRequest → RemoveActiveSessionResponse` | `TokenType.User` | Удалить сессию по DeviceId |
| `EnableOtpVerification` | `EnableOtpVerificationRequest → EnableOtpVerificationResponse` | `TokenType.User` | Включить 2FA |
| `ConfirmOtpVerification` | `ConfirmOtpVerificationRequest → ConfirmOtpVerificationResponse` | `TokenType.User` | Подтвердить и активировать 2FA |
| `DisableOtpVerification` | `DisableOtpVerificationRequest → DisableOtpVerificationResponse` | `TokenType.User` | Отключить 2FA |
| `ListOtpVerification` | `ListOtpVerificationRequest → ListOtpVerificationResponse` | `TokenType.User` | Список методов 2FA пользователя |
| `ResetPassword` | `ResetPasswordRequest → ResetPasswordResponse` | нет | Запрос сброса пароля → `ResetId` |
| `ConfirmResetPassword` | `ConfirmResetPasswordRequest → ConfirmResetPasswordResponse` | нет | Подтверждение сброса → токены |
| `SetPassword` | `SetPasswordRequest → SetPasswordResponse` | `TokenType.User` | Установить/изменить пароль |
| `Logout` | `LogoutRequest → LogoutResponse` | `TokenType.User` | Разлогиниться с текущего устройства |

### IdentityServerApiService (service-to-service, только `TokenType.Service`)

| Метод | Параметры | Описание |
|-------|-----------|----------|
| `ListOtpVerificationServer` | `UserId` | Список методов 2FA по userId |
| `DisableOtpVerificationServer` | `UserId`, `OtpType` | Принудительно отключить 2FA |
| `GetActiveSessionsServer` | `UserId` | Список сессий по userId |
| `RemoveActiveSessionServer` | `UserId`, `DeviceId` | Удалить сессию по userId + deviceId |
| `CreateSessionForUserServer` | `UserId`, `DeviceId`, `DeviceName`, `OperationSystem`, `AppName`, `IpAddress` | Выпустить пару `access_token`+`refresh_token` для пользователя из другого сервиса (например [[Backend/FastAuth]] после Accept). Регистрирует устройство в Users + отправляет email-уведомление `SuccessfulLogin`. |

## Обязательные заголовки (XAuth) для большинства эндпоинтов

Для `Auth`, `CreateAccount`, `ConfirmAccount`, `EnableOtp`, `ResetPassword`, `ConfirmResetPassword`, `SetPassword`:
- `x-device-name` — обязателен
- `x-os-name` — обязателен  
- `x-app-name` + `x-app-version` — обязательны
- `x-device-id` — опционален (если не передан — генерируется UUID)

## Ключевые потоки

### Регистрация (2 шага)
1. `CreateAccount` → создаётся draft-пользователь в Users-сервисе (`AddDraftUser` / `OverrideDraftUser`), генерируется `ConfirmationCode` (6 цифр, TTL 6 ч.), код отправляется на email → возврат `CodeId`
2. `ConfirmAccount` → код проверяется, пользователь подтверждается в Users-сервисе, выдаётся `RefreshToken`

### Аутентификация (`Auth`)
1. Проверка username/email + обязательных заголовков
2. Поиск пользователя в Users-сервисе (`FindByLogin`)
3. Проверка 2FA:
   - Если включена но `OtpCode` не передан → Email OTP высылается, бросается `OtpCodeNeedException`
   - TOTP (Authenticator) → `Totp.VerifyTotp` (OtpNet, RFC window)
   - Email OTP → сравнение с `LastEmailAuthCode`
4. Проверка пароля через `PasswordHasher.VerifyPassword` (BCrypt; legacy SHA-256 поддерживается для старых хешей до смены пароля)
5. Удаление старого `RefreshToken` для данного DeviceId
6. Создание нового `RefreshToken` (TTL 9999 дней) + JWT access token
7. Регистрация/обновление устройства в Users-сервисе (`RegisterDevice`)
8. Email-уведомления через RabbitMQ: успех (`SuccessfulLogin`) или неудача (`FailedLogin`)

### Обновление токена (`CreateToken`)
1. Поиск `RefreshToken` по значению
2. Проверка срока действия и наличия `DeviceId`
3. Генерация нового JWT access token через `JwtService.GenerateUserToken`

### Сброс пароля (2 шага)
1. `ResetPassword` → поиск пользователя, создание `ResetPassword`-записи:
   - **Authenticator OTP**: запись сохраняется без кода (клиент использует TOTP-приложение), `ExpiresAt` = +15 мин
   - **Email OTP**: генерируется 6-значный код, высылается на email, код сохраняется в `ResetPassword.OtpCode`, `ExpiresAt` = +5 мин
   - Для несуществующего пользователя возвращается фейковый `ResetId` (защита от энумерации)
2. `ConfirmResetPassword` → проверка `ExpiresAt`, валидация OTP-кода по типу → `IsApproved = true`, обнуление `PasswordHash`, выдача новых токенов
   - Если `DeviceId` не передан — генерируется UUID

### Разлогин (`Logout`) — `[Authorize]`
1. `DeviceId` берётся из JWT-claim (`UserContext.DeviceId`) — аргументов нет
2. Удаляются все `RefreshToken` для этого `DeviceId` + `UserId` (safe, без исключения если уже нет)
3. Публикуется `SessionRevokedEvent` → `TokenRevocationCache` инвалидирует текущий access token немедленно
4. Устройство удаляется из Users-сервиса (`DeleteUserDevice`), ошибка — только warning в логах

### Установка/смена пароля (`SetPassword`) — `[Authorize]`
- Если пароль ранее установлен: требует `OldPassword` (`PasswordHasher.VerifyPassword`, поддерживает BCrypt и legacy SHA-256)
- Если первая установка (после сброса): `OldPassword` не нужен
- Новый пароль хешируется BCrypt-ом (workFactor=12)
- После смены отправляется уведомление `PasswordChanged`

### 2FA — включение (TOTP)
1. `EnableOtpVerification(OtpType.Authenticator)` → генерация TOTP-секрета (OtpNet, 20 байт), URI `otpauth://totp/...`, QR-код (Base64 PNG, QRCoder) → `OtpQr` + `OtpCode`
2. `ConfirmOtpVerification` → валидация первого кода → `OtpEnabled = true`

### 2FA — включение (Email)
1. `EnableOtpVerification(OtpType.Email)` → генерация кода, сохранение в `LastEmailAuthCode`, отправка на email
2. `ConfirmOtpVerification` → валидация кода → `EmailOtpEnabled = true`

## RabbitMQ

### Consumer (входящие события)
| Очередь | Событие | Действие |
|---------|---------|----------|
| `session-revoked-identity` | `SessionRevokedEvent` | Добавляет `(UserId, DeviceId)` в `TokenRevocationCache` для немедленной инвалидации access token |

### Producer (исходящие, через `NotificationQueueSender`)
| Тип | Когда |
|-----|-------|
| `ConfirmationRegistration` | Создан черновик аккаунта |
| `SuccessfulRegistration` | Email подтверждён |
| `ConfirmationAuth` | Вход с Email OTP |
| `FailedLogin` | Неверный пароль |
| `SuccessfulLogin` | Успешный вход |
| `ConfirmationOtpEmail` | Включение Email 2FA |
| `ResetPassword` | Запрос сброса пароля |
| `PasswordChanged` | Пароль изменён |

## Внешние зависимости

- **Users service** — gRPC `UsersServerApi` (service token): `FindByLogin`, `AddDraftUser`, `OverrideDraftUser`, `ConfirmUser`, `GetById`, `GetUserContacts`, `RegisterDevice`, `GetUserDevices`
- **Notification service** — RabbitMQ `EmailNotification` (через MassTransit)
- **ip-api.com** — HTTP геолокация по IP (`LocationClient`), вызывается при каждом входе/регистрации

## gRPC-Web

`IdentityApiService` включает gRPC-Web для поддержки браузерных клиентов:
- `app.UseGrpcWeb()` + `.EnableGrpcWeb()`
- CORS политика `IdentityCors` с exposed headers: `grpc-status`, `grpc-message`, `grpc-status-details-bin`, `x-error-code`

## Proto

- `identity_api.proto` — `GrpcServices="Server"`
- `users_api.proto` — `GrpcServices="Client"`
- `shared.proto` — `GrpcServices="None"`

## Метрики (MetricsCollector)

Регистрация: `builder.Services.AddBarkFluffMetrics("BarkFluff.Identity")` — каждые 5 сек публикует `ServiceMetrics {@Metrics}` в Seq → AdminPanel парсит. См. [[Архитектура]] про общую схему.

Ключевые группы метрик:
- **Auth**: `auth_login_attempts`, `auth_login_success`, `auth_login_failed[_user_not_found|_invalid_password]`, `auth_otp_required`
- **Tokens**: `tokens_refresh_attempts`, `tokens_refreshed`, `tokens_refresh_invalid`
- **Регистрация**: `account_creation_attempts`, `accounts_drafted`, `accounts_draft_overridden`, `accounts_confirmed`, `account_confirmation_failed[_not_found|_expired|_incorrect]`
- **OTP/2FA**: `otp_email_codes_sent`, `otp_email_verified`/`failed`, `otp_authenticator_verified`/`failed`, `otp_setup_email`/`_authenticator`, `otp_enabled_email`/`_authenticator`, `otp_disabled_email`/`_authenticator`, `otp_confirmation_failed`, `otp_disable_failed`
- **Сброс пароля**: `password_reset_requests`, `password_reset_user_not_found`, `password_reset_initiated_email`/`_authenticator`, `password_resets_confirmed`, `password_reset_confirmation_failed[_not_found|_already_used|_expired]`
- **Изменение пароля**: `password_changes`, `password_changes_initial`, `password_change_failed_invalid_old`
- **Сессии/logout**: `session_removal_attempts`, `sessions_removed`, `session_removal_failed_not_found`, `logouts`, `sessions_created`, `sessions_revoked`, `session_revocations_received`
- **Server API**: `server_session_creation_attempts`/`server_sessions_created`, `server_session_removal_attempts`/`server_sessions_removed`/`server_session_removal_failed_not_found`, `server_session_lookups`, `server_otp_lookups`, `server_otp_disable_attempts`
- **LocationClient**: `geolocation_requests`, `geolocation_success`, `geolocation_errors`
- **Gauge**: `service_started_unix` (для uptime)

Полный реестр и где они инкрементируются — см. файл памяти `project_identity_metrics.md`.

## Связанные файлы

- [[Backend/Identity-ProjectMap]] — подробная карта всех файлов и классов проекта
- [[Shared/Exceptions]] — коды ошибок аутентификации (`x-error-code` trailer)
- [[Backend/Users]] — поиск/регистрация пользователей
- [[Backend/Notification]] — email-уведомления
- [[Архитектура]] — XAuth, TokenType, JWT-flow
