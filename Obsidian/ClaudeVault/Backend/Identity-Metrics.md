# BarkFluff.Identity — реестр метрик

> ↩ Назад: [[Backend/Identity]] · [[Backend/GrpcServer]] (общий механизм) · [[Backend/Beacon-Metrics]] (пример общей схемы)

Общая схема сбора — та же, что у всех сервисов (см. [[Backend/Beacon-Metrics]]). Регистрация в `Program.cs`:
```csharp
builder.AddBarkFluffSerilog("BarkFluff.Identity");
builder.Services.AddBarkFluffMetrics("BarkFluff.Identity");
```

## Соглашения именования (общие для проекта)

- snake_case
- `_attempts` — попытки на уровне gRPC-контроллера (до валидации)
- `_failed` / `_failed_<reason>` — отказы с указанием причины
- `_total` — кумулятивная сумма (мс/байты)
- `_unix` — Unix-timestamp (gauge)
- `server_*` — метрика серверного API (`IdentityServerApiService`, требует `TokenType.Service`)

## Реестр метрик

### Защита Identity

| Метрика | Источник | Описание |
|---|---|---|
| `identity_rate_limited` | `RedisIdentityAbuseGuard` | Запрос отклонён из-за Redis rate limit |
| `identity_lockouts` | `RedisIdentityAbuseGuard` | Создана блокировка пользователя, кода или OTP-операции |
| `identity_code_invalidated` | `RedisIdentityAbuseGuard` | Регистрационный/reset-код инвалидирован после пятой ошибки |
| `identity_protection_unavailable` | `RedisIdentityAbuseGuard` | Redis недоступен; high-risk запрос отклонён fail-closed |

### Gauges (последнее значение, не сбрасывается)

| Метрика | Источник | Описание |
|---|---|---|
| `service_started_unix` | `Program.cs` | Unix-timestamp старта сервиса; используется для расчёта uptime |

### Универсальные counter'ы (общие с другими сервисами)

| Метрика | Источник | Когда инкрементируется |
|---|---|---|
| `grpc_requests_total` | `ServerExceptionInterceptor` | Каждый unary-вызов gRPC |
| `grpc_requests_failed` | `ServerExceptionInterceptor` | Бизнес-ошибка `BaseGrpcException` (FailedPrecondition + x-error-code) |
| `grpc_requests_errors` | `ServerExceptionInterceptor` | Необработанное исключение (Status.Unknown) |
| `rabbitmq_events_consumed` | `SessionRevokedConsumer` | Получено событие из очереди |

### Логин (`Auth`)

| Метрика | Источник | Описание |
|---|---|---|
| `auth_login_attempts` | `IdentityApiService.Auth` | Каждый вызов `Auth` (попытка) |
| `auth_login_success` | `AuthCommandHandler` | Полный успешный логин (после уведомления) |
| `auth_login_failed` | `AuthCommandHandler` | Любой провал логина (агрегат) |
| `auth_login_failed_user_not_found` | `AuthCommandHandler` | Пользователь не найден |
| `auth_login_failed_invalid_password` | `AuthCommandHandler` | Неверный пароль |
| `auth_otp_required` | `AuthCommandHandler` | Включена 2FA, OtpCode не передан → требуется ввод OTP |
| `sessions_created` | `AuthCommandHandler` (+ `ConfirmAccount`, `ConfirmResetPassword`, `CreateSessionForUserServer`) | Реально создан refresh token |

### Refresh токены (`CreateToken`)

| Метрика | Источник | Описание |
|---|---|---|
| `tokens_refresh_attempts` | `IdentityApiService.CreateToken` | Каждый вызов `CreateToken` |
| `tokens_refreshed` | `CreateTokenCommandHandler` | Успешный refresh access-токена |
| `tokens_refresh_invalid` | `CreateTokenCommandHandler` | Refresh-токен не найден / истёк / без DeviceId |

### Регистрация (`CreateAccount`, `ConfirmAccount`)

| Метрика | Источник | Описание |
|---|---|---|
| `account_creation_attempts` | `IdentityApiService.CreateAccount` | Каждый вызов `CreateAccount` |
| `accounts_drafted` | `CreateAccountCommandHandler` | Создан новый draft-пользователь в Users |
| `accounts_draft_overridden` | `CreateAccountCommandHandler` | Переопределён существующий draft (`UserIsDraftException`) |
| `accounts_confirmed` | `ConfirmAccountCommandHandler` | Email подтверждён, пользователь активирован |
| `account_confirmation_failed` | `ConfirmAccountCommandHandler` | Любой провал подтверждения (агрегат) |
| `account_confirmation_failed_not_found` | `ConfirmAccountCommandHandler` | CodeId не найден или неверный тип |
| `account_confirmation_failed_expired` | `ConfirmAccountCommandHandler` | Срок действия кода истёк |
| `account_confirmation_failed_incorrect` | `ConfirmAccountCommandHandler` | Введён неверный код |

### OTP / 2FA — общие

| Метрика | Источник | Описание |
|---|---|---|
| `otp_email_codes_sent` | `Auth`, `ResetPassword`, `EnableOtpVerification` | Email с OTP отправлен в очередь |
| `otp_email_verified` | `Auth`, `ConfirmOtpVerification`, `ConfirmResetPassword` | Email-OTP проверен успешно |
| `otp_email_failed` | те же handler'ы | Email-OTP неверный |
| `otp_authenticator_verified` | `Auth`, `ConfirmOtpVerification`, `ConfirmResetPassword`, `DisableOtpVerification` | TOTP проверен успешно |
| `otp_authenticator_failed` | те же handler'ы | TOTP неверный |

### OTP — настройка (`EnableOtpVerification`, `ConfirmOtpVerification`, `DisableOtpVerification`)

| Метрика | Источник | Описание |
|---|---|---|
| `otp_setup_email` | `EnableOtpVerificationCommandHandler` | Запущена настройка Email-2FA (отправлен код) |
| `otp_setup_authenticator` | `EnableOtpVerificationCommandHandler` | Сгенерирован TOTP secret + QR |
| `otp_enabled_email` | `ConfirmOtpVerificationCommandHandler` | Email-2FA активирован |
| `otp_enabled_authenticator` | `ConfirmOtpVerificationCommandHandler` | TOTP активирован |
| `otp_confirmation_failed` | `ConfirmOtpVerificationCommandHandler` | Подтверждение не прошло |
| `otp_disabled_email` | `DisableOtpVerificationCommandHandler` | Email-2FA отключён |
| `otp_disabled_authenticator` | `DisableOtpVerificationCommandHandler` | TOTP отключён |
| `otp_disable_failed` | `DisableOtpVerificationCommandHandler` | Отключение не прошло (неверный TOTP) |

### Сброс пароля (`ResetPassword`, `ConfirmResetPassword`)

| Метрика | Источник | Описание |
|---|---|---|
| `password_reset_requests` | `IdentityApiService.ResetPassword` | Каждый вызов `ResetPassword` |
| `password_reset_user_not_found` | `ResetPasswordCommandHandler` | Логин/email не найден (фейковый ResetId) |
| `password_reset_initiated_email` | `ResetPasswordCommandHandler` | Создан reset-запрос с Email-OTP |
| `password_reset_initiated_authenticator` | `ResetPasswordCommandHandler` | Создан reset-запрос с TOTP |
| `password_resets_confirmed` | `ConfirmResetPasswordCommandHandler` | Пароль успешно сброшен (хеш очищен) |
| `password_reset_confirmation_failed` | `ConfirmResetPasswordCommandHandler` | Любой провал подтверждения (агрегат) |
| `password_reset_confirmation_failed_not_found` | `ConfirmResetPasswordCommandHandler` | ResetId не найден |
| `password_reset_confirmation_failed_already_used` | `ConfirmResetPasswordCommandHandler` | ResetId уже использован |
| `password_reset_confirmation_failed_expired` | `ConfirmResetPasswordCommandHandler` | Срок ResetId истёк |

### Изменение пароля (`SetPassword`)

| Метрика | Источник | Описание |
|---|---|---|
| `password_changes` | `SetPasswordCommandHandler` | Пароль изменён |
| `password_changes_initial` | `SetPasswordCommandHandler` | Пароль установлен впервые (после reset, без OldPassword) |
| `password_change_failed_invalid_old` | `SetPasswordCommandHandler` | Не передан / неверный OldPassword |

### Сессии и logout

| Метрика | Источник | Описание |
|---|---|---|
| `session_removal_attempts` | `IdentityApiService.RemoveActiveSession` | Каждый вызов |
| `sessions_removed` | `RemoveActiveSessionCommandHandler` | Сессия удалена пользователем |
| `session_removal_failed_not_found` | `RemoveActiveSessionCommandHandler` | DeviceId не найден |
| `logouts` | `LogoutCommandHandler` | Пользователь разлогинился с текущего устройства |
| `sessions_revoked` | `LogoutCommandHandler`, `RemoveActiveSessionCommandHandler`, `RemoveActiveSessionServerCommandHandler` | Опубликован `SessionRevokedEvent` (инвалидация access-токена) |
| `session_revocations_received` | `SessionRevokedConsumer` | Получено `SessionRevokedEvent` от другого инстанса |

### Серверный API (`IdentityServerApiService`, `TokenType.Service`)

| Метрика | Источник | Описание |
|---|---|---|
| `server_session_creation_attempts` | `IdentityServerApiService` | Каждый вызов `CreateSessionForUserServer` |
| `server_sessions_created` | `CreateSessionForUserServerCommandHandler` | Сессия создана из другого сервиса (например FastAuth Accept) |
| `server_session_removal_attempts` | `IdentityServerApiService` | Каждый вызов `RemoveActiveSessionServer` |
| `server_sessions_removed` | `RemoveActiveSessionServerCommandHandler` | Сессия успешно удалена |
| `server_session_removal_failed_not_found` | `RemoveActiveSessionServerCommandHandler` | DeviceId не найден |
| `server_session_lookups` | `IdentityServerApiService` | `GetActiveSessionsServer` |
| `server_otp_lookups` | `IdentityServerApiService` | `ListOtpVerificationServer` |
| `server_otp_disable_attempts` | `IdentityServerApiService` | `DisableOtpVerificationServer` |
| `server_force_password_changes` | `IdentityServerApiService` | `ForceSetPasswordServer` |
| `server_bot_token_creations` | `IdentityServerApiService` | `CreateBotTokenServer` |

### Геолокация (`LocationClient` → ip-api.com)

| Метрика | Источник | Описание |
|---|---|---|
| `geolocation_requests` | `LocationClient.GetLocation` | Запрос к ip-api.com |
| `geolocation_success` | `LocationClient.GetLocation` | Успешно получена локация |
| `geolocation_errors` | `LocationClient.GetLocation` | HTTP-ошибка / non-2xx / исключение |

## Производные метрики (вычисляются в AdminPanel/UI)

- **Login success rate** = `auth_login_success / auth_login_attempts`
- **Login failure breakdown** = доли `auth_login_failed_user_not_found`, `auth_login_failed_invalid_password`, `auth_otp_required` от `auth_login_attempts`
- **Account funnel** = `accounts_confirmed / account_creation_attempts`
- **2FA adoption changes** = `otp_enabled_*` − `otp_disabled_*` за период
- **Geolocation health** = `geolocation_errors / geolocation_requests`
- **Uptime** = `now - service_started_unix`

## ⚠️ Важные нюансы

1. **AdminPanel парсит только последний 5-сек снапшот часа** — counters в нём показывают активность за последние ~5 секунд этого часа, а не сумму.
2. **Метрики `_attempts` живут в контроллере** до проверки авторизации/валидации — они шумят сильнее, чем `success`. Для бизнес-метрик берите `success`/`failed`.
3. **`sessions_created` инкрементируется в 4 местах**: `Auth`, `ConfirmAccount`, `ConfirmResetPassword`, `CreateSessionForUserServer` — намеренно, единая метрика «сколько новых refresh-токенов выдано».
4. **`sessions_revoked` ≠ `sessions_removed`**: `revoked` = опубликован event (включая logout); `removed` = пользователь явно удалил чужую сессию через `RemoveActiveSession`.

## Где менять метрики

- `Backend/BarkFluff.Identity/Host/IdentityApiService.cs` — публичный gRPC API (attempts)
- `Backend/BarkFluff.Identity/Host/IdentityServerApiService.cs` — server API (server_*)
- `Backend/BarkFluff.Identity/Features/**/Handler.cs` — бизнес-исходы
- `Backend/BarkFluff.Identity/Infrastructure/LocationClient.cs` — внешний HTTP
- `Backend/BarkFluff.Identity/Consumers/SessionRevokedConsumer.cs` — RabbitMQ
- `Backend/BarkFluff.Identity/Program.cs` — стартовые gauges
