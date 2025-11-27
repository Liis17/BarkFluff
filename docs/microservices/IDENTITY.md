# Identity Microservice

## Назначение

Сервис Identity отвечает за **аутентификацию и авторизацию пользователей** в системе BarkFluff. Он управляет:

- 🔐 Аутентификацией пользователей (вход/выход)
- 🎫 Генерацией и обновлением JWT токенов
- 👤 Регистрацией новых аккаунтов
- 🔒 Двухфакторной аутентификацией (2FA) через Google Authenticator и Email
- 🔑 Сбросом пароля
- 📱 Управлением активными сессиями (устройствами)

**Порт**: 7001
**База данных**: PostgreSQL (`identity_db`)
**Зависимости**: Users service, Notification service (через RabbitMQ)

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол
- **Entity Framework Core**: ORM
- **PostgreSQL**: База данных
- **RabbitMQ** (MassTransit): Message bus
- **JWT**: Токены доступа
- **OTP.NET**: TOTP для Google Authenticator
- **QRCoder**: Генерация QR кодов для 2FA

## Архитектура

```
┌─────────────────────────────────────────────┐
│             Identity Service                 │
├─────────────────────────────────────────────┤
│  ┌───────────┐  ┌──────────┐  ┌──────────┐ │
│  │ Features  │→ │ Services │→ │ Storage  │ │
│  └───────────┘  └──────────┘  └──────────┘ │
│       │              │              │       │
│       └──────────────┴──────────────┘       │
│                      ↓                      │
│             ┌─────────────────┐             │
│             │  Domain Models  │             │
│             └─────────────────┘             │
└─────────────────────────────────────────────┘
         │                        │
         ↓                        ↓
┌──────────────┐          ┌──────────────┐
│  PostgreSQL  │          │  RabbitMQ    │
│  (5 tables)  │          │ (Events)     │
└──────────────┘          └──────────────┘
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **RefreshTokens** | Сессии пользователей (refresh tokens) |
| **UserPasswords** | Хеши паролей (SHA256) |
| **AuthUserProperties** | Настройки 2FA (TOTP секреты, email OTP) |
| **ConfirmationCodes** | Коды подтверждения регистрации |
| **ResetPasswords** | Запросы на сброс пароля |

### Основные сущности

#### RefreshToken
```csharp
public class RefreshToken
{
    public long Id { get; set; }
    public string Value { get; set; }              // 20-символьный токен
    public long UserId { get; set; }
    public string DeviceName { get; set; }          // Имя устройства
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }         // +9999 дней (~27 лет)
}
```

#### AuthUserProperty
```csharp
public class AuthUserProperty
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public bool OtpEnabled { get; set; }            // Google Authenticator
    public bool EmailOtpEnabled { get; set; }       // Email OTP
    public string OtpSecret { get; set; }           // Base32 TOTP секрет
    public string LastEmailAuthCode { get; set; }   // Последний email код
}
```

## Ключевые функции

### 1. Регистрация пользователя

**Endpoints**:
- `CreateAccount` - создание черновика аккаунта
- `ConfirmAccount` - подтверждение email

**Процесс**:
```
1. Client → CreateAccount(email, username, firstName, lastName)
2. Identity → Users.AddDraftUser()
3. Identity → Генерация 6-значного кода
4. Identity → RabbitMQ: EmailNotification (ConfirmationRegistration)
5. Notification → Email с кодом подтверждения
6. Client → ConfirmAccount(codeId, codeValue)
7. Identity → Users.ConfirmUser()
8. Identity ← Refresh Token
```

### 2. Аутентификация

**Endpoint**: `Auth`

**Процесс без 2FA**:
```
1. Client → Auth(username/email, password)
2. Identity → Users.FindByLogin()
3. Identity → Проверка пароля (SHA256)
4. Identity → Генерация Access Token (JWT) + Refresh Token
5. Client ← { accessToken, refreshToken }
```

**Процесс с 2FA**:
```
1. Client → Auth(username, password, null)
2. Identity → Проверка пароля
3. Identity → Обнаружена включённая 2FA
4. Identity → Email OTP: отправка кода
5. Identity ← OtpCodeNeedException
6. Client → Auth(username, password, otpCode)
7. Identity → Проверка TOTP/Email кода
8. Identity → Генерация токенов
9. Client ← { accessToken, refreshToken }
```

### 3. Двухфакторная аутентификация (2FA)

#### Включение Google Authenticator

```
1. Client → EnableOtpVerification(type=Authenticator)
2. Identity → Генерация Base32 секрета
3. Identity → Создание TOTP URI: otpauth://totp/BarkFluff:{username}?secret={secret}
4. Identity → QRCoder: генерация QR кода
5. Client ← { qrCodeBase64, secret }
6. User → Сканирует QR в Google Authenticator
7. Client → ConfirmOtpVerification(otpCode)
8. Identity → Проверка TOTP кода
9. Identity → Сохранение OtpEnabled = true
```

#### Включение Email OTP

```
1. Client → EnableOtpVerification(type=Email)
2. Identity → Генерация 6-значного кода
3. Identity → RabbitMQ: EmailNotification (ConfirmationOtpEmail)
4. Client ← Success
5. User → Получает email с кодом
6. Client → ConfirmOtpVerification(otpCode)
7. Identity → Проверка кода
8. Identity → EmailOtpEnabled = true
```

### 4. Сброс пароля

**Endpoints**:
- `ResetPassword` - запрос сброса
- `ConfirmResetPassword` - подтверждение с OTP
- `SetPassword` - установка нового пароля

**Процесс**:
```
1. Client → ResetPassword(username/email, otpType=Email)
2. Identity → Users.FindByLogin()
3. Identity → Создание ResetPassword записи
4. Identity → Генерация 6-значного кода
5. Identity → RabbitMQ: EmailNotification (ResetPassword)
6. Client ← { resetId }
7. User → Получает email с кодом
8. Client → ConfirmResetPassword(resetId, otpCode)
9. Identity → Проверка кода
10. Client ← { accessToken, refreshToken }
11. Client → SetPassword(newPassword)
12. Identity → Обновление хеша пароля
```

### 5. Управление сессиями

**Endpoints**:
- `GetActiveSessions` - список активных устройств
- `RemoveActiveSession` - удаление сессии (logout с устройства)

**Информация о сессии**:
- Имя устройства (из заголовка `X-Device-Name`)
- Дата создания
- Дата истечения
- Уникальный ID сессии

## JWT Токены

### Access Token

**Срок жизни**: Настраивается в `JwtSettings:ExpiryMinutes` (обычно 60 минут)

**Claims**:
```csharp
{
  "userId": "12345",
  "tokenType": "User"
}
```

**Генерация** (JwtService.cs:32):
```csharp
var claims = new List<Claim>
{
    new(IdentityClaims.UserId, userId.ToString()),
    new(IdentityClaims.TokenType, TokenType.User.ToString()),
};

var securityKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(jwtSettings.SecretKey));
var credentials = new SigningCredentials(
    securityKey, SecurityAlgorithms.HmacSha256);

var token = new JwtSecurityToken(
    issuer: jwtSettings.Issuer,
    audience: jwtSettings.Audience,
    claims: claims,
    expires: DateTime.UtcNow.AddMinutes(jwtSettings.ExpiryMinutes),
    signingCredentials: credentials
);
```

### Refresh Token

**Срок жизни**: 9999 дней (~27 лет)

**Формат**: 20-символьная alphanumeric строка (RefreshTokenGenerator.cs:9)

**Использование**:
```
Client → CreateToken(refreshToken)
Identity → Проверка существования и срока действия
Identity ← Новый Access Token
```

## События RabbitMQ

Identity **публикует** (не потребляет) следующие события:

| Событие | Тип | Когда отправляется |
|---------|-----|-------------------|
| **ConfirmationRegistration** | EmailNotification | При регистрации аккаунта |
| **ConfirmationAuth** | EmailNotification | При логине с Email OTP |
| **ConfirmationOtpEmail** | EmailNotification | При включении Email OTP |
| **ResetPassword** | EmailNotification | При запросе сброса пароля |

**Payload**:
```json
{
  "Title": "Код подтверждения",
  "Address": "user@example.com",
  "Type": "ConfirmationRegistration",
  "Payload": {
    "confirmation_code": "123456",
    "username": "john_doe",
    "ip": "192.168.1.1",
    "devicename": "iPhone 14",
    "os": "iOS 17",
    "location": "USA, California, San Francisco",
    "datetime": "Tuesday, November 23, 2025"
  }
}
```

## Зависимости

### Users Service (gRPC)

**Методы**:
- `FindByLoginAsync` - поиск пользователя по username/email
- `AddDraftUserAsync` - создание черновика
- `OverrideDraftUserAsync` - перезапись черновика
- `ConfirmUserAsync` - подтверждение регистрации
- `GetByIdAsync` - получение пользователя по ID
- `GetUserContactsAsync` - получение email пользователя

**Аутентификация**: Service token через `JwtClientInterceptor`

### Notification Service (RabbitMQ)

**Направление**: Identity → RabbitMQ → Notification

**События**: Email notifications для всех критичных операций

## Конфигурация

### appsettings.json

```json
{
  "JwtSettings": {
    "SecretKey": "your-secret-key-here",
    "Issuer": "BarkFluff.Identity",
    "Audience": "BarkFluff",
    "ExpiryMinutes": 60
  },
  "UsersService": {
    "Host": "http://users:7002",
    "Token": "service-token"
  },
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  },
  "IdentityDb": "Host=postgres;Database=identity_db;..."
}
```

### Переменные окружения

- `IdentityDb` - строка подключения PostgreSQL
- `UsersService:Host` - эндпоинт Users service
- `UsersService:Token` - service token для Users

## API Reference

### gRPC Methods

| Метод | Требует Auth | Описание |
|-------|--------------|----------|
| `Auth` | ❌ | Аутентификация пользователя |
| `FastAuth` | ❌ | Быстрый вход через QR |
| `CreateToken` | ❌ | Обновление access token |
| `CreateAccount` | ❌ | Создание аккаунта |
| `ConfirmAccount` | ❌ | Подтверждение регистрации |
| `ResetPassword` | ❌ | Запрос сброса пароля |
| `ConfirmResetPassword` | ❌ | Подтверждение сброса |
| `SetPassword` | ✅ User | Установка пароля |
| `GetActiveSessions` | ✅ User | Список сессий |
| `RemoveActiveSession` | ✅ User | Удаление сессии |
| `EnableOtpVerification` | ✅ User | Включение 2FA |
| `ConfirmOtpVerification` | ✅ User | Подтверждение 2FA |
| `DisableOtpVerification` | ✅ User | Отключение 2FA |
| `ListOtpVerification` | ✅ User | Статус 2FA |
| `GenerateTestToken` | ❌ | Тестовый токен (dev only) |

## Известные проблемы

### 🔴 Критичные

1. **Небезопасное хеширование паролей**
   - Используется SHA256 без соли
   - **Рекомендация**: Переход на bcrypt/Argon2

2. **Слишком долгий срок жизни Refresh Token**
   - 9999 дней = ~27 лет
   - **Рекомендация**: Сократить до 30-90 дней

### 🟡 Средние

3. **Отключена валидация SSL сертификатов**
   - Для IP геолокации через http://ip-api.com
   - **Рекомендация**: Включить валидацию

4. **Нет автоматической очистки старых сессий**
   - RefreshTokens накапливаются в БД
   - **Рекомендация**: Background job для очистки

## Troubleshooting

### Проблема: "Invalid JWT token"

**Решение**: Проверить синхронизацию `JwtSettings`:
```bash
# Все сервисы должны иметь одинаковые настройки
JwtSettings:SecretKey
JwtSettings:Issuer
JwtSettings:Audience
```

### Проблема: "OTP code needed exception"

**Причина**: Включена 2FA, но код не передан

**Решение**:
1. Первый запрос без кода → получение OtpCodeNeedException
2. Второй запрос с кодом из email/authenticator

### Проблема: "User is draft exception"

**Причина**: Попытка создать аккаунт с уже существующим username/email черновика

**Решение**: Вызвать `OverrideDraftUser` вместо `AddDraftUser`

## Метрики и мониторинг

### Ключевые метрики

- **Успешные логины / минуту**
- **Неудачные логины / минуту** (брутфорс детекция)
- **2FA активации / день**
- **Сбросы пароля / день**
- **Средняя длительность запроса Auth**

### Логи

Все критичные операции логируются:
- Успешные/неуспешные логины с IP и device info
- Включение/отключение 2FA
- Запросы сброса пароля
- Удаление сессий

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Identity/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/IdentityApiService.cs` - gRPC endpoints
- `Features/*/` - CQRS handlers
- `Services/JwtService.cs` - генерация JWT
- `Services/PasswordHasher.cs` - хеширование паролей
- `Infrastructure/NotificationQueueSender.cs` - отправка в RabbitMQ
- `Persistence/` - EF Core контексты и storage
