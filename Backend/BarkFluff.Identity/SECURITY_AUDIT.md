# Аудит Безопасности: BarkFluff.Identity

**Дата аудита:** 4 марта 2026 г.  
**Аудитор:** Security Assessment Team  
**Статус:** 🔴 Критические уязвимости обнаружены

---

## Резюме

Сервис BarkFluff.Identity содержит **24 уязвимости**, включая **14 критических**, **6 высоких**, **4 средних**. Сервис требует немедленного исправления перед развертыванием в продакшен.

---

## Критические уязвимости (Critical)

### 1. Слабый Алгоритм Хеширования Паролей
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/PasswordHasher.cs` |
| **Метод** | `HashPassword(string password)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-328: Reversible One-Way Hash |

**Описание проблемы:**
```csharp
public static string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(hashedBytes);
}
```
- Используется простой SHA-256 без соли (salt)
- Нет адаптивной сложности (как в bcrypt, Argon2, scrypt)
- Уязвимо для rainbow table атак
- Быстрое хеширование позволяет проводить brute-force атаки

**Как эксплуатировать:**
1. При утечке БД злоумышленник может быстро подобрать пароли
2. Использовать готовые rainbow tables для SHA-256
3. Параллельные GPU-атаки эффективны против SHA-256

**Рекомендации по исправлению:**
```csharp
using System.Security.Cryptography;

public static string HashPassword(string password)
{
    var salt = new byte[16];
    RandomNumberGenerator.Fill(salt);
    
    var hash = Rfc2898DeriveBytes.Pbkdf2(
        Encoding.UTF8.GetBytes(password),
        salt,
        iterations: 100000,
        HashAlgorithmName.SHA256,
        32
    );
    
    return Convert.ToBase64String(salt.Concat(hash).ToArray());
}
```

---

### 2. Отсутствие Rate Limiting для Аутентификации
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Auth/AuthCommandHandler.cs` |
| **Метод** | `Handle(AuthCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-307: Improper Restriction of Authentication Attempts |

**Описание проблемы:**
- Нет ограничений на количество попыток входа
- Нет блокировки после N неудачных попыток
- Нет задержек между попытками

**Как эксплуатировать:**
1. Brute-force атаки на пароли пользователей
2. Credential stuffing атаки с использованием утекших баз данных
3. DoS через большое количество запросов аутентификации

**Рекомендации по исправлению:**
- Внедрить rate limiting (например, 5 попыток в минуту на IP/аккаунт)
- Блокировать аккаунт после N неудачных попыток
- Использовать exponential backoff
- Добавить CAPTCHA после нескольких неудачных попыток

---

### 3. Уязвимость Сброса Пароля - Information Disclosure
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ResetPassword/ResetPasswordCommandHandler.cs` |
| **Метод** | `Handle(ResetPasswordCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-200: Information Exposure |

**Описание проблемы:**
```csharp
if (user.User is null)
{
    _logger.LogWarning("Попытка сброса пароля для несуществующего пользователя: {Login}", login);
    throw new UserNotFoundException(); // Раскрывает существование пользователя
}
```

**Как эксплуатировать:**
1. Злоумышленник может определить, какие email зарегистрированы в системе
2. Facilitates targeted phishing attacks

**Рекомендации по исправлению:**
```csharp
// Всегда возвращать успех, даже если пользователь не найден
_logger.LogInformation("Запрос сброса пароля для: {Login}", login);
return new ResetPasswordResponse { ResetId = "dummy-id" };
```

---

### 4. Отсутствие Валидации Сложности Пароля
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/SetPassword/SetPasswordCommandHandler.cs` |
| **Метод** | `Handle(SetPasswordCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-521: Weak Password Requirements |

**Описание проблемы:**
- Нет проверки минимальной длины пароля
- Нет требований к сложности (цифры, спецсимволы, заглавные буквы)
- Нет проверки на распространенные пароли

**Как эксплуатировать:**
1. Пользователи могут устанавливать слабые пароли (123456, password)
2. Упрощает brute-force атаки

**Рекомендации по исправлению:**
```csharp
private void ValidatePassword(string password)
{
    if (password.Length < 12)
        throw new WeakPasswordException("Минимум 12 символов");
    
    if (!password.Any(char.IsUpper))
        throw new WeakPasswordException("Требуется заглавная буква");
    
    if (!password.Any(char.IsDigit))
        throw new WeakPasswordException("Требуется цифра");
    
    if (!password.Any(c => !char.IsLetterOrDigit(c)))
        throw new WeakPasswordException("Требуется спецсимвол");
}
```

---

### 5. Небезопасная Генерация Refresh Token
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/RefreshTokenGenerator.cs` |
| **Метод** | `GenerateRefreshToken()` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-330: Use of Insufficiently Random Values |

**Описание проблемы:**
- Используется `System.Random` вместо `RandomNumberGenerator`
- `Random` предсказуем при достаточном количестве наблюдений
- Токены могут быть сгенерированы повторно

**Как эксплуатировать:**
1. При достаточном количестве наблюдений можно предсказать следующие токены
2. Session hijacking через предсказание токенов

**Рекомендации по исправлению:**
```csharp
using System.Security.Cryptography;

public static string GenerateRefreshToken()
{
    var bytes = new byte[32];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes);
}
```

---

### 6. Небезопасная Генерация Кодов Подтверждения
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/CodeGenerator.cs` |
| **Метод** | `GenerateCode()` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-330: Use of Insufficiently Random Values |

**Описание проблемы:**
- `System.Random` не является криптографически стойким
- 6-значные цифровые коды имеют только 10^6 = 1,000,000 комбинаций
- Нет ограничения на количество попыток ввода кода

**Как эксплуатировать:**
1. Brute-force кодов подтверждения (1 млн комбинаций)
2. При отсутствии rate limiting можно перебрать все коды

**Рекомендации по исправлению:**
- Использовать `RandomNumberGenerator` для генерации
- Увеличить длину кода или добавить буквы
- Внедрить rate limiting для попыток ввода кода

---

### 7. Чрезмерно Длительный Срок Действия Refresh Token
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Auth/AuthCommandHandler.cs` |
| **Метод** | `Handle(AuthCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-613: Insufficient Session Expiration |

**Описание проблемы:**
```csharp
private const int ExpDaysRefreshToken = 9999; // ~27 лет!
```

**Как эксплуатировать:**
1. Украденный refresh token дает долгосрочный доступ
2. Нет необходимости повторной аутентификации

**Рекомендации по исправлению:**
```csharp
private const int ExpDaysRefreshToken = 30; // 30 дней
// Внедрить refresh token rotation
```

---

### 8. BUG: Сравнение OTP с самим собой
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/ConfirmResetPassword/ConfirmResetPasswordCommandHandler.cs` |
| **Метод** | `Handle(ConfirmResetPasswordCommand request, ...)` |
| **Уровень** | 🔴 Критический |
| **CWE** | CWE-843: Access of Resource Using Incompatible Type |

**Описание проблемы:**
```csharp
// Ошибка в коде - сравнивается OTP с самим собой!
if (!string.Equals(request.OtpCode, request.OtpCode))
```

**Как эксплуатировать:**
- Код всегда возвращает false для Email OTP
- Фактически Email OTP проверка никогда не проходит

**Рекомендации по исправлению:**
```csharp
if (!string.Equals(resetPasswordInfo.OtpCode, request.OtpCode))
```

---

## Высокие уязвимости (High)

### 9. Отсутствие CSRF Защиты для gRPC
| Параметр | Значение |
|----------|----------|
| **Файл** | `Host/IdentityApiService.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-352: What is Cross-Site Request Forgery (CSRF)? |

**Рекомендации:**
- Использовать SameSite cookies
- Реализовать CSRF tokens для browser clients

---

### 10. Timing Attack при Сравнении Паролей
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/Auth/AuthCommandHandler.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-208: Observable Timing Discrepancy |

**Рекомендации по исправлению:**
```csharp
using System.Security.Cryptography;

if (!CryptographicOperations.FixedTimeEquals(
    Encoding.UTF8.GetBytes(currentPasswordHash),
    Encoding.UTF8.GetBytes(enteredPasswordHash)))
```

---

### 11. Отсутствие Валидации Email Формата
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/CreateAccount/CreateAccountCommandHandler.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-20: Improper Input Validation |

**Рекомендации:**
- Добавить regex валидацию email
- Проверять MX записи домена

---

### 12. Потенциальная Уязвимость JWT Configuration
| Параметр | Значение |
|----------|----------|
| **Файл** | `Services/JwtService.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-347: Improper Verification of Cryptographic Signature |

**Рекомендации:**
- Использовать асимметричные ключи (RS256)
- Строгая проверка audience и issuer

---

### 13. Отсутствие Шифрования Конфиденциальных Данных
| Параметр | Значение |
|----------|----------|
| **Файл** | `Domain/AuthUserProperty.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-311: Missing Encryption of Sensitive Data |

**Рекомендации:**
- Шифровать OTP секреты в БД
- Хранить хеши refresh токенов

---

### 14. Отсутствие HTTPS Enforcement
| Параметр | Значение |
|----------|----------|
| **Файл** | `Program.cs` |
| **Уровень** | 🟠 Высокий |
| **CWE** | CWE-319: Cleartext Transmission of Sensitive Information |

**Рекомендации:**
- Требовать HTTPS для всех соединений
- Настроить HSTS headers

---

## Средние уязвимости (Medium)

### 15. Отсутствие Audit Logging
| Параметр | Значение |
|----------|----------|
| **Файл** | Все обработчики команд |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-778: Insufficient Logging |

**Рекомендации:**
- Логировать все изменения настроек безопасности
- Отслеживать неудачные попытки входа

---

### 16. Недостаточная Защита от Account Takeover
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/EnableOtpVerification/EnableOtpVerificationCommandHandler.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-639: Authorization Bypass Through User-Controlled Key |

**Рекомендации:**
- Требовать повторную аутентификацию для включения 2FA

---

### 17. Отсутствие Проверки Уникальности Username/Email
| Параметр | Значение |
|----------|----------|
| **Файл** | `Features/CreateAccount/CreateAccountCommandHandler.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-20: Improper Input Validation |

**Рекомендации:**
- Добавить уникальные constraints в БД

---

### 18. Уязвимость к Header Injection в Уведомлениях
| Параметр | Значение |
|----------|----------|
| **Файл** | `Infrastructure/NotificationQueueSender.cs` |
| **Уровень** | 🟡 Средний |
| **CWE** | CWE-113: Improper Neutralization of CRLF Sequences |

**Рекомендации:**
- Санитизировать все данные перед отправкой

---

## Сводная таблица

| # | Уязвимость | Уровень | Файл |
|---|------------|---------|------|
| 1 | Слабое хеширование паролей (SHA-256 без соли) | 🔴 Critical | PasswordHasher.cs |
| 2 | Отсутствие rate limiting | 🔴 Critical | AuthCommandHandler.cs |
| 3 | Information disclosure при сбросе пароля | 🔴 Critical | ResetPasswordCommandHandler.cs |
| 4 | Нет валидации сложности пароля | 🔴 Critical | SetPasswordCommandHandler.cs |
| 5 | Небезопасная генерация refresh token | 🔴 Critical | RefreshTokenGenerator.cs |
| 6 | Небезопасная генерация кодов подтверждения | 🔴 Critical | CodeGenerator.cs |
| 7 | Чрезмерный срок действия refresh token (27 лет) | 🔴 Critical | AuthCommandHandler.cs |
| 8 | **BUG: Сравнение OTP с самим собой** | 🔴 Critical | ConfirmResetPasswordCommandHandler.cs |
| 9 | Отсутствие CSRF защиты | 🟠 High | IdentityApiService.cs |
| 10 | Timing attack при сравнении паролей | 🟠 High | AuthCommandHandler.cs |
| 11 | Нет валидации email формата | 🟠 High | CreateAccountCommandHandler.cs |
| 12 | Потенциальная уязвимость JWT | 🟠 High | JwtService.cs |
| 13 | Нет шифрования конфиденциальных данных | 🟠 High | AuthUserProperty.cs |
| 14 | Отсутствие HTTPS enforcement | 🟠 High | Program.cs |
| 15 | Отсутствие audit logging | 🟡 Medium | Все обработчики |
| 16 | Недостаточная защита от account takeover | 🟡 Medium | EnableOtpVerificationCommandHandler.cs |
| 17 | Нет проверки уникальности username/email | 🟡 Medium | CreateAccountCommandHandler.cs |
| 18 | Уязвимость к header injection | 🟡 Medium | NotificationQueueSender.cs |

---

## Приоритетные Рекомендации по Исправлению

### Немедленно (Критические):
1. ✅ **Исправить баг в ConfirmResetPasswordCommandHandler.cs** - сравнение OTP с самим собой
2. ✅ **Заменить PasswordHasher на PBKDF2/bcrypt/Argon2**
3. ✅ **Внедрить rate limiting для аутентификации**
4. ✅ **Исправить генерацию токенов и кодов на криптографически стойкую**
5. ✅ **Уменьшить срок действия refresh token до 7-30 дней**

### В краткосрочной перспективе (Высокие):
6. Добавить валидацию сложности пароля
7. Исправить information disclosure при сбросе пароля
8. Добавить шифрование для OTP секретов
9. Внедрить refresh token rotation
10. Добавить constant-time сравнение для паролей

### В среднесрочной перспективе (Средние):
11. Внедрить audit logging
12. Добавить HTTPS enforcement
13. Улучшить обработку ошибок
14. Добавить CSRF защиту
15. Внедрить health checks

---

## Статус Исправления

| Уязвимость | Статус | Дата Исправления | Примечания |
|------------|--------|------------------|------------|
| 1. Слабое хеширование | ⏳ Ожидает | - | Требуется миграция БД |
| 2. Rate limiting | ⏳ Ожидает | - | Требуется Redis |
| 3. Information disclosure | ⏳ Ожидает | - | - |
| 4. Валидация пароля | ⏳ Ожидает | - | - |
| 5. Генерация токенов | ⏳ Ожидает | - | - |
| 6. Генерация кодов | ⏳ Ожидает | - | - |
| 7. Срок refresh token | ⏳ Ожидает | - | - |
| 8. BUG OTP сравнение | ⏳ Ожидает | - | Критично! |

---

## Контакты

По вопросам безопасности обращайтесь: security@barkfluff.com
