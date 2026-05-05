# Аудит проекта: BarkFluff.Identity

> **Дата аудита:** 2025  
> **Проект:** `Backend/BarkFluff.Identity`  
> **Target Framework:** `net9.0`  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Производительность](#-производительность)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Качество кода](#-качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Небезопасное хеширование паролей (SHA-256 без соли)

**Проблема:**  
Пароли хешируются через `SHA-256` без соли. SHA-256 — это быстрый криптографический хеш, не предназначенный для хранения паролей. Атака по радужным таблицам или брутфорс GPU позволяют взломать такие хеши крайне быстро. Соли нет — два пользователя с одинаковым паролем будут иметь одинаковый хеш.

**Файл:** `Backend\BarkFluff.Identity\Services\PasswordHasher.cs` : строки 1–15

```csharp
// ❌ ПРОБЛЕМА: SHA-256 без соли — небезопасно для паролей
public static string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    // Нет соли, нет стретчинга, нет адаптивного алгоритма
    return Convert.ToBase64String(hashedBytes);
}
```

**Варианты решения:**  
Использовать `BCrypt`, `Argon2` или встроенный `PasswordHasher<T>` из `Microsoft.AspNetCore.Identity` — все они добавляют соль автоматически и медленны по дизайну.

```csharp
// ✅ РЕШЕНИЕ: Использовать BCrypt.Net-Next (NuGet)
// dotnet add package BCrypt.Net-Next

public static class PasswordHasher
{
    // BCrypt сам генерирует и встраивает соль в хеш
    public static string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public static bool VerifyPassword(string password, string hash)
        => BCrypt.Net.BCrypt.Verify(password, hash);
}

// Или вариант через встроенный ASP.NET Core PasswordHasher:
// services.AddSingleton<IPasswordHasher<object>, PasswordHasher<object>>();
```

---

### SEC-02 — Email OTP код хранится в открытом виде в БД

**Проблема:**  
Последний Email OTP-код для входа и для включения 2FA хранится как plain text в поле `LastEmailAuthCode` таблицы `AuthUserProperties`. При компрометации БД злоумышленник получает актуальный OTP-код и может войти в аккаунт пользователя.

**Файл:** `Backend\BarkFluff.Identity\Domain\AuthUserProperty.cs` : строка 20  
**Файл:** `Backend\BarkFluff.Identity\Persistence\Services\AuthPropertiesStorage.cs` : строки 122–145

```csharp
// ❌ ПРОБЛЕМА: OTP хранится открытым текстом
public class AuthUserProperty
{
    public string? LastEmailAuthCode { get; set; } // plain text OTP — небезопасно
}

// При проверке сравнивается строка напрямую:
if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode,
    StringComparison.InvariantCultureIgnoreCase)) // ...
```

**Варианты решения:**  
Хранить SHA-256 (или HMAC-SHA256 с секретным ключом) хеш OTP-кода. При проверке — хешировать введённый код и сравнивать хеши.

```csharp
// ✅ РЕШЕНИЕ: хранить хеш OTP
public static string HashOtpCode(string code)
{
    using var sha256 = SHA256.Create();
    var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(code));
    return Convert.ToHexString(hash);
}

// При сохранении:
await authPropertiesStorage.UpdateLastEmailAuthCode(userId, HashOtpCode(code));

// При проверке:
var inputHash = HashOtpCode(request.OtpCode);
if (!string.Equals(optOptions.LastEmailAuthCodeHash, inputHash, StringComparison.Ordinal))
    throw new NotValidOtpCodeException();
```

---

### SEC-03 — Нет ограничения на количество попыток ввода OTP / пароля (отсутствует Rate Limiting)

**Проблема:**  
В `AuthCommandHandler` нет никакой защиты от брутфорса — ни лимита попыток, ни временной блокировки. 6-значный Email OTP имеет только 1 000 000 вариантов, что при отсутствии ограничений позволяет перебрать его автоматизированно. Аналогично для пароля.

**Файл:** `Backend\BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs` : строки 29–337  
**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs` : строки 60–75

```csharp
// ❌ ПРОБЛЕМА: нет счётчика неудачных попыток
// Можно делать бесконечное количество запросов с разными OTP/паролями
if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode, ...))
{
    throw new NotValidOtpCodeException(); // просто ошибка, без блокировки
}
```

**Варианты решения:**  
Добавить Rate Limiting на уровне gRPC interceptor или middleware, либо хранить счётчик попыток в Redis/БД.

```csharp
// ✅ РЕШЕНИЕ (вариант 1): ASP.NET Core Rate Limiting (Program.cs)
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", config =>
    {
        config.PermitLimit = 5;
        config.Window = TimeSpan.FromMinutes(15);
        config.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        config.QueueLimit = 0;
    });
});

// ✅ РЕШЕНИЕ (вариант 2): счётчик в БД/Redis с блокировкой
// Добавить поля в AuthUserProperty:
public int FailedLoginAttempts { get; set; }
public DateTime? LockedUntil { get; set; }

// В AuthCommandHandler перед проверкой пароля:
if (props?.LockedUntil > DateTime.UtcNow)
    throw new AccountTemporarilyLockedException();

// После неверного пароля:
await authPropertiesStorage.IncrementFailedAttempts(userId);
if (props.FailedLoginAttempts >= 5)
    await authPropertiesStorage.LockAccount(userId, TimeSpan.FromMinutes(15));
```

---

### SEC-04 — Небезопасный генератор Refresh Token (System.Random)

**Проблема:**  
`RefreshTokenGenerator` использует `System.Random` — псевдослучайный генератор, непригодный для криптографических целей. Refresh токен с `ExpDaysRefreshToken = 9999` фактически является долгосрочным секретом — его предсказуемость критична.

**Файл:** `Backend\BarkFluff.Identity\Services\RefreshTokenGenerator.cs` : строки 1–26

```csharp
// ❌ ПРОБЛЕМА: System.Random — не криптостойкий ГПСЧ
public static string GenerateRefreshToken()
{
    var random = new Random(); // создаётся каждый раз — seed может быть предсказуем
    var stringChars = new char[20]; // только 20 символов — слабая энтропия
    for (var i = 0; i < stringChars.Length; i++)
    {
        var randomChar = (char)random.Next(48, 123); // System.Random, не CSPRNG
        // ...
    }
    return new string(stringChars);
}
```

**Варианты решения:**  
Использовать `RandomNumberGenerator` из `System.Security.Cryptography`.

```csharp
// ✅ РЕШЕНИЕ: криптографически стойкий токен через CSPRNG
using System.Security.Cryptography;

public static class RefreshTokenGenerator
{
    // 32 байта = 256 бит энтропии — достаточно для долгосрочного токена
    public static string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
               .Replace("+", "-")
               .Replace("/", "_")
               .TrimEnd('='); // URL-safe Base64
}
```

---

### SEC-05 — Server JWT токен с датой истечения 31.12.9999 — "бессмертный"

**Проблема:**  
`GenerateServerToken` создаёт JWT с `Expires = 9999-12-31`. Это означает, что скомпрометированный сервисный токен никогда не истечёт. Нет механизма его отзыва.

**Файл:** `Backend\BarkFluff.Identity\Services\JwtService.cs` : строки 33–46

```csharp
// ❌ ПРОБЛЕМА: токен действует вечно, отозвать невозможно
public string GenerateServerToken(ServiceId serviceId)
{
    var dateEnd = new DateTime(9999, 12, 31, 23, 59, 59); // "вечный" токен
    var token = CreateToken(claims, dateEnd);
    return token;
}
```

**Варианты решения:**  
Выдавать сервисные токены с разумным сроком (например, 24 часа или 30 дней) и реализовать автоматическое обновление между сервисами.

```csharp
// ✅ РЕШЕНИЕ: короткий срок жизни + автообновление
public string GenerateServerToken(ServiceId serviceId)
{
    var claims = new List<Claim>
    {
        new(IdentityClaims.ServiceId, serviceId.ToString()),
        new(IdentityClaims.TokenType, TokenType.Service.ToString()),
    };

    // Срок 24 часа — сервисы обновляют токен по расписанию
    var dateEnd = DateTime.UtcNow.AddHours(24);
    return CreateToken(claims, dateEnd);
}
```

---

### SEC-06 — OTP-секрет хранится в БД в открытом виде

**Проблема:**  
TOTP-секрет (`OtpSecret`) в `AuthUserProperty` хранится как plain text Base32-строка. При компрометации БД атакующий получает возможность генерировать валидные TOTP-коды навсегда.

**Файл:** `Backend\BarkFluff.Identity\Domain\AuthUserProperty.cs` : строка 16

```csharp
// ❌ ПРОБЛЕМА: TOTP секрет в открытом виде
public class AuthUserProperty
{
    public string? OtpSecret { get; set; } // plain text Base32 секрет
}
```

**Варианты решения:**  
Шифровать секрет через `IDataProtector` (ASP.NET Core Data Protection) перед записью в БД.

```csharp
// ✅ РЕШЕНИЕ: шифрование через Data Protection
// Program.cs:
builder.Services.AddDataProtection();

// AuthPropertiesStorage:
private readonly IDataProtector _protector;

public AuthPropertiesStorage(IdentityContext context, IDataProtectionProvider provider)
{
    _context = context;
    _protector = provider.CreateProtector("OtpSecret.v1");
}

// При сохранении:
props.OtpSecret = _protector.Protect(secretKey);

// При чтении:
return _protector.Unprotect(props.OtpSecret);
```

---

### SEC-07 — Утечка информации о существовании пользователя при сбросе пароля

**Проблема:**  
`ResetPasswordCommandHandler` бросает `UserNotFoundException` если пользователь не найден. Это позволяет атакующему энумерировать существующие логины/email через endpoint сброса пароля.

**Файл:** `Backend\BarkFluff.Identity\Features\ResetPassword\ResetPasswordCommandHandler.cs` : строки 88–98

```csharp
// ❌ ПРОБЛЕМА: раскрывает факт существования/несуществования пользователя
if (user.User is null)
{
    _logger.LogWarning("Попытка сброса пароля для несуществующего пользователя: {Login}", login);
    throw new UserNotFoundException(); // атакующий знает, что такого юзера нет
}
```

**Варианты решения:**  
Всегда возвращать один и тот же успешный ответ (сообщение «если такой аккаунт существует, код отправлен»).

```csharp
// ✅ РЕШЕНИЕ: одинаковый ответ независимо от наличия пользователя
if (user.User is null)
{
    // Логируем для внутреннего мониторинга, но клиенту отдаём "успех"
    _logger.LogWarning("Запрос сброса пароля для несуществующего пользователя: {Login}", login);
    // Имитируем задержку как у настоящего запроса (timing attack protection)
    await Task.Delay(Random.Shared.Next(100, 300), cancellationToken);
    return new ResetPasswordResponse { ResetId = Guid.NewGuid().ToString() }; // fake ID
}
```

---

### SEC-08 — Нет срока жизни у ResetPassword записи

**Проблема:**  
Запись `ResetPassword` не имеет поля `ExpiresAt`. Email OTP-код для сброса пароля действует вечно — пока не будет использован (`IsApproved = true`). Злоумышленник, перехвативший старый код, может использовать его через любое время.

**Файл:** `Backend\BarkFluff.Identity\Domain\ResetPassword.cs` : строки 1–18  
**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs` : строки 73–87

```csharp
// ❌ ПРОБЛЕМА: нет проверки срока действия ResetPassword
public class ResetPassword
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; } // есть, но никогда не проверяется!
    public bool IsApproved { get; set; }
    // ExpiresAt отсутствует
}

// В ConfirmResetPasswordCommandHandler нет проверки вроде:
// if (resetPasswordInfo.CreatedAt.AddMinutes(15) < DateTime.UtcNow) throw new ResetIdExpiredException();
```

**Варианты решения:**  
Добавить поле `ExpiresAt` и проверять его при подтверждении.

```csharp
// ✅ РЕШЕНИЕ: добавить ExpiresAt и проверку
public class ResetPassword
{
    [Key] public Guid Id { get; set; }
    public long UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; } // ← добавить
    public OtpType OtpType { get; set; }
    public string? OtpCode { get; set; }
    public bool IsApproved { get; set; }
}

// При создании (ResetPasswordCommandHandler):
var resetPassword = new Domain.ResetPassword
{
    CreatedAt = DateTime.UtcNow,
    ExpiresAt = DateTime.UtcNow.AddMinutes(15), // 15 минут
    // ...
};

// В ConfirmResetPasswordCommandHandler:
if (resetPasswordInfo.ExpiresAt < DateTime.UtcNow)
    throw new ResetIdExpiredException();
```

---

### SEC-09 — Нет удаления использованных ConfirmationCode из БД

**Проблема:**  
После успешного подтверждения аккаунта в `ConfirmAccountCommandHandler` запись `ConfirmationCode` не удаляется из БД. Коды накапливаются, и теоретически использованный код может быть применён повторно (нет флага `IsUsed`).

**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs` : строки 46–75  
**Файл:** `Backend\BarkFluff.Identity\Persistence\Services\ConfirmationCodesStorage.cs` : строки 1–30

```csharp
// ❌ ПРОБЛЕМА: код не удаляется и не помечается использованным
var equals = code.Value.Equals(request.Code, StringComparison.InvariantCultureIgnoreCase);
if (!equals) throw new ConfirmationCodeIncorrectException();

// Код верный — выполняем подтверждение...
await usersClient.ConfirmUserAsync(confirmRequest);
// Но код НЕ удаляется! Можно подтвердить ещё раз с тем же кодом
```

**Варианты решения:**  
Удалять код после успешного использования или добавить флаг `IsUsed`.

```csharp
// ✅ РЕШЕНИЕ: удалять код после использования
// В ConfirmationCodesStorage:
public async Task DeleteCode(Guid id)
{
    var code = await _context.ConfirmationCodes.FindAsync(id);
    if (code != null)
    {
        _context.ConfirmationCodes.Remove(code);
        await _context.SaveChangesAsync();
    }
}

// В ConfirmAccountCommandHandler после успешного подтверждения:
await usersClient.ConfirmUserAsync(confirmRequest);
await confirmationCodesStorage.DeleteCode(codeId); // ← удаляем использованный код
```

---

## 🟡 Производительность

---

### PERF-01 — Многократные вызовы `GetLocation` в одном запросе

**Проблема:**  
В `AuthCommandHandler` метод `locationClient.GetLocation()` вызывается до 3 раз в рамках одного запроса аутентификации: при Email OTP, при неверном пароле и при успешном входе. Каждый вызов — HTTP-запрос к внешнему API `ip-api.com`, что добавляет сотни миллисекунд задержки и нагружает внешний сервис.

**Файл:** `Backend\BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs` : строки 113–120, 208–215, 267–273

```csharp
// ❌ ПРОБЛЕМА: до 3 HTTP-вызовов к внешнему API за один запрос auth

// Вызов 1 — при отправке Email OTP
var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);

// Вызов 2 — при неверном пароле (вдруг тот же IP)
var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);

// Вызов 3 — при успешном входе
var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);
```

**Варианты решения:**  
Получать геолокацию один раз в начале метода и переиспользовать результат. Дополнительно — кешировать по IP через `IMemoryCache`.

```csharp
// ✅ РЕШЕНИЕ: одиночный вызов + кеширование
// Получить один раз в начале Handle():
string locationInfo = "-";
if (!string.IsNullOrEmpty(requestContext.IpAddress))
{
    var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);
    if (ipLocation != null)
        locationInfo = $"{ipLocation.Country}, {ipLocation.RegionName}, {ipLocation.City}";
}
// Далее переиспользовать locationInfo везде в методе

// Кеширование в LocationClient (IMemoryCache):
public async Task<IpLocation?> GetLocation(string ip)
{
    var cacheKey = $"ip_location_{ip}";
    if (_cache.TryGetValue(cacheKey, out IpLocation? cached))
        return cached;

    // ... HTTP запрос ...

    _cache.Set(cacheKey, location, TimeSpan.FromHours(24)); // IP меняет локацию редко
    return location;
}
```

---

### PERF-02 — N+1 вызовов `GetUserContactsAsync` при одном запросе

**Проблема:**  
В `AuthCommandHandler` при неверном пароле и при успешном входе делается отдельный gRPC-вызов `GetUserContactsAsync`. Данные пользователя уже были получены (`FindByLoginAsync`), но не содержат email — приходится делать дополнительный запрос. Итого 2 лишних gRPC-вызова при каждом входе.

**Файл:** `Backend\BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs` : строки 203–204, 257–258

```csharp
// ❌ ПРОБЛЕМА: отдельные gRPC-вызовы за контактами при каждом неверном/верном пароле
// Вызов при неверном пароле:
var userContactInfo = await usersClient.GetUserContactsAsync(
    new GetUserContactsRequest { UserId = user.User.Id });

// Ещё один вызов при успешном входе:
var successUserContactInfo = await usersClient.GetUserContactsAsync(
    new GetUserContactsRequest { UserId = user.User.Id });
```

**Варианты решения:**  
Загружать контакты один раз заранее или объединить `FindByLogin` + `GetContacts` в один gRPC-вызов на стороне Users-сервиса.

```csharp
// ✅ РЕШЕНИЕ: один вызов за контактами в начале, переиспользование
var user = await usersClient.FindByLoginAsync(usersRequest);
if (user.User is null) throw new InvalidLoginOrPasswordException();

// Загружаем контакты сразу, один раз
var userContactInfo = await usersClient.GetUserContactsAsync(
    new GetUserContactsRequest { UserId = user.User.Id });

// Далее используем userContactInfo.Contact.Email везде без повторных вызовов
```

---

### PERF-03 — Многократные запросы к `AuthUserProperties` в одном методе

**Проблема:**  
Несколько методов `AuthPropertiesStorage` в рамках одного Use Case делают отдельные `FirstOrDefaultAsync` к одной таблице. Например, в `EnableOtpVerificationCommandHandler` вызываются `GetUserAuthProperties`, `AddUserOtpSecretKey`, `UpdateOptType` — каждый делает свой SELECT.

**Файл:** `Backend\BarkFluff.Identity\Persistence\Services\AuthPropertiesStorage.cs` : строки 26–47, 95–101, 144–157  
**Файл:** `Backend\BarkFluff.Identity\Features\EnableOtpVerification\EnableOtpVerificationCommandHandler.cs` : строки 76–97

```csharp
// ❌ ПРОБЛЕМА: 3 отдельных SELECT к одной строке таблицы
var oldOptOptions = await _authPropertiesStorage.GetUserAuthProperties(userId);    // SELECT 1
await _authPropertiesStorage.AddUserOtpSecretKey(userId, base32Secret);           // SELECT 2
await _authPropertiesStorage.UpdateOptType(Domain.OtpType.Authenticator, userId); // SELECT 3
```

**Варианты решения:**  
Добавить атомарный метод `SetupAuthenticatorOtp(userId, secret)` который делает один SELECT и все изменения в одном `SaveChangesAsync`.

```csharp
// ✅ РЕШЕНИЕ: атомарный метод
public async Task SetupAuthenticatorOtp(long userId, string secret)
{
    var props = await _context.AuthUserProperties
        .FirstOrDefaultAsync(x => x.UserId == userId);

    if (props is null)
    {
        props = new AuthUserProperty { UserId = userId };
        _context.AuthUserProperties.Add(props);
    }

    props.OtpSecret = secret;
    props.SelectedOtpType = OtpType.Authenticator;

    await _context.SaveChangesAsync(); // один SaveChanges
}
```

---

### PERF-04 — Отсутствие индекса на `RefreshTokens.Value`

**Проблема:**  
`RefreshTokensStorage.FindRefreshToken` ищет токен по полю `Value` (строка) через `FirstOrDefaultAsync(x => x.Value == refreshToken)`. Без индекса на это поле — полный TABLE SCAN при каждом обновлении access token. Таблица будет расти быстро (по токену на каждое устройство, ExpiresAt = 9999).

**Файл:** `Backend\BarkFluff.Identity\Persistence\Services\RefreshTokensStorage.cs` : строки 12–17

```csharp
// ❌ ПРОБЛЕМА: поиск по неиндексированному строковому полю
var refreshTokenEntity = await context.RefreshTokens
    .AsNoTracking()
    .FirstOrDefaultAsync(x => x.Value == refreshToken); // TABLE SCAN без индекса
```

**Варианты решения:**  
Добавить уникальный индекс на `RefreshToken.Value` через Fluent API или атрибут `[Index]`.

```csharp
// ✅ РЕШЕНИЕ: индекс через Data Annotations (Domain/RefreshToken.cs)
using Microsoft.EntityFrameworkCore;

[Index(nameof(Value), IsUnique = true)]
public class RefreshToken
{
    [Key] public long Id { get; set; }
    public string Value { get; set; }
    public long UserId { get; set; }
    public string DeviceId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// Или через OnModelCreating в IdentityContext:
modelBuilder.Entity<RefreshToken>()
    .HasIndex(x => x.Value)
    .IsUnique();
```

---

### PERF-05 — Отсутствие индекса на `ConfirmationCodes.Id` тип поиска и накопление записей

**Проблема:**  
Истёкшие `ConfirmationCode` записи никогда не удаляются из БД (нет Cleanup Job). Таблица бесконечно растёт. Кроме того, каждый повторный запрос регистрации создаёт новую запись (через `OverrideDraftUser`, но код добавляется новый без удаления старых для того же `OwnerId`).

**Файл:** `Backend\BarkFluff.Identity\Persistence\Services\ConfirmationCodesStorage.cs` : строки 17–24  
**Файл:** `Backend\BarkFluff.Identity\Features\CreateAccount\CreateAccountCommandHandler.cs` : строки 75–84

```csharp
// ❌ ПРОБЛЕМА: при повторной регистрации старые коды не удаляются
var confirmationCode = new ConfirmationCode()
{
    Expires = DateTime.UtcNow.AddHours(6),
    OwnerId = responseUser.UserId,
    // ...
};
confirmationCode = await confirationCodesStorage.AddCode(confirmationCode); // старые коды остаются!
```

**Варианты решения:**  
Удалять старые коды перед добавлением нового + периодическая очистка истёкших записей (BackgroundService).

```csharp
// ✅ РЕШЕНИЕ: удалять старые коды для пользователя перед созданием нового
public async Task DeleteCodesByOwner(long ownerId, ConfirmationCodeType type)
{
    var old = _context.ConfirmationCodes
        .Where(x => x.OwnerId == ownerId && x.Type == type);
    _context.ConfirmationCodes.RemoveRange(old);
    await _context.SaveChangesAsync();
}

// В CreateAccountCommandHandler:
await confirationCodesStorage.DeleteCodesByOwner(responseUser.UserId, ConfirmationCodeType.Registration);
await confirationCodesStorage.AddCode(confirmationCode);

// ✅ Фоновая очистка (BackgroundService):
public class ExpiredCodesCleanupService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _storage.DeleteExpiredCodes();
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

---

### PERF-06 — Синхронная блокировка в CodeGenerator (lock + static Random)

**Проблема:**  
`CodeGenerator` использует `static Random` с `lock (_syncLock)` для потокобезопасности. В высоконагруженной среде это создаёт contention на одном объекте блокировки. `Random` также не является криптостойким (хотя для подтверждающих кодов важна предсказуемость).

**Файл:** `Backend\BarkFluff.Identity\Services\CodeGenerator.cs` : строки 7–23

```csharp
// ❌ ПРОБЛЕМА: глобальный lock конкурирует при высокой нагрузке
private static readonly Random _random = new();
private static readonly object _syncLock = new();

public static string GenerateDigitalCode(int length)
{
    lock (_syncLock) // блокировка всех потоков
    {
        for (int i = 0; i < length; i++)
            codeBuilder.Append(_random.Next(0, 10));
    }
}
```

**Варианты решения:**  
Использовать `Random.Shared` (.NET 6+) — потокобезопасен без блокировок. Или `RandomNumberGenerator` для криптостойкости.

```csharp
// ✅ РЕШЕНИЕ: Random.Shared — потокобезопасен, без lock
public static string GenerateDigitalCode(int length)
{
    if (length <= 0)
        throw new ArgumentException("Длина должна быть положительным числом.", nameof(length));

    // Используем криптостойкий генератор для OTP
    Span<byte> bytes = stackalloc byte[length];
    RandomNumberGenerator.Fill(bytes);

    var sb = new StringBuilder(length);
    foreach (var b in bytes)
        sb.Append(b % 10); // равномерное распределение 0-9

    return sb.ToString();
}
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — DeviceId может быть null при сбросе пароля → неверный токен устройства

**Проблема:**  
В `ConfirmResetPasswordCommandHandler` при создании Refresh Token используется `requestContext.DeviceId ?? requestContext.DeviceName`. Если `DeviceId` не передан, в качестве ID устройства используется его **имя** — это нарушает логику идентификации устройств и может создать дубликаты сессий для устройств с одинаковым именем.

**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs` : строка 129

```csharp
// ❌ БАГ: DeviceName используется как DeviceId — ненадёжно
await refreshTokensStorage.CreateNewRefreshToken(
    refreshTokenString,
    resetPasswordInfo.UserId,
    requestContext.DeviceId ?? requestContext.DeviceName, // DeviceName не уникален!
    ExpDaysRefreshToken
);
```

**Варианты решения:**  
Генерировать новый `DeviceId` если он не передан (как это делается в `AuthCommandHandler`), либо требовать его как обязательный заголовок.

```csharp
// ✅ РЕШЕНИЕ: явная генерация DeviceId если отсутствует
if (string.IsNullOrEmpty(requestContext.DeviceId))
{
    requestContext.DeviceId = Guid.NewGuid().ToString();
}

await refreshTokensStorage.CreateNewRefreshToken(
    refreshTokenString,
    resetPasswordInfo.UserId,
    requestContext.DeviceId, // теперь всегда не null
    ExpDaysRefreshToken
);
```

---

### BUG-02 — Константа `ExpDaysRefreshToken = 9999` дублируется в 3 местах

**Проблема:**  
Значение `9999` дней для Refresh Token захардкожено как константа в трёх разных классах: `AuthCommandHandler`, `ConfirmAccountCommandHandler`, `ConfirmResetPasswordCommandHandler`. При изменении политики токенов нужно менять в трёх местах — риск несоответствия.

**Файл:** `Backend\BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs` : строка 27  
**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs` : строка 25  
**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs` : строка 33

```csharp
// ❌ БАГ/ДУБЛИРОВАНИЕ: значение 9999 в трёх местах независимо
// AuthCommandHandler.cs:
private const int ExpDaysRefreshToken = 9999;

// ConfirmAccountCommandHandler.cs:
private const int ExpDaysRefreshToken = 9999;

// ConfirmResetPasswordCommandHandler.cs:
private const int ExpDaysRefreshToken = 9999;
```

**Варианты решения:**  
Вынести в `JwtSettings` или общий класс настроек, читаемый из конфига.

```csharp
// ✅ РЕШЕНИЕ: вынести в JwtSettings
public class JwtSettings
{
    public string SecretKey { get; set; }
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int ExpiryMinutes { get; set; }
    public int RefreshTokenExpiryDays { get; set; } = 30; // ← добавить (9999 — небезопасно!)
}

// В handlers:
private readonly JwtSettings _jwtSettings;
// Использование:
await refreshTokensStorage.CreateNewRefreshToken(..., _jwtSettings.RefreshTokenExpiryDays);
```

---

### BUG-03 — `ConfirmAccount` не проверяет, что код относится к типу `Registration`

**Проблема:**  
`ConfirmAccountCommandHandler` получает `ConfirmationCode` по `Id` и проверяет только значение и срок. Не проверяется поле `Type`. Теоретически можно передать `CodeId` от другого типа операции (например, от будущего кода смены email) и пройти проверку.

**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs` : строки 44–75

```csharp
// ❌ БАГ: тип кода не проверяется
var code = await confirmationCodesStorage.GetCode(codeId);

if (code is null) throw new ConfirmationCodeNotFoundException();
if (code.Expires < DateTime.UtcNow) throw new ConfirmationCodeExpiredException();

var equals = code.Value.Equals(request.Code, ...);
// Нет проверки: if (code.Type != ConfirmationCodeType.Registration) throw ...
```

**Варианты решения:**  
Добавить проверку типа кода.

```csharp
// ✅ РЕШЕНИЕ: проверка типа кода
var code = await confirmationCodesStorage.GetCode(codeId);

if (code is null) throw new ConfirmationCodeNotFoundException();

// Защита от использования кода другого типа
if (code.Type != ConfirmationCodeType.Registration)
    throw new ConfirmationCodeNotFoundException(); // не раскрываем детали

if (code.Expires < DateTime.UtcNow) throw new ConfirmationCodeExpiredException();
```

---

### BUG-04 — `DeleteRefreshToken` не проверяет принадлежность токена пользователю

**Проблема:**  
Метод `DeleteRefreshToken(long id, long userId)` принимает `userId`, но ищет токен только по `id` без фильтрации по `userId`. Пользователь A теоретически может удалить токен пользователя B, если знает его числовой `id`.

**Файл:** `Backend\BarkFluff.Identity\Persistence\Services\RefreshTokensStorage.cs` : строки 43–54

```csharp
// ❌ БАГ: userId игнорируется при поиске, можно удалить чужой токен
public async Task DeleteRefreshToken(long id, long userId)
{
    // userId передаётся, но НЕ используется в запросе!
    var refreshToken = await context.RefreshTokens.FirstOrDefaultAsync(x => x.Id == id);

    if (refreshToken is null) throw new RefreshTokenNotFoundException();

    context.RefreshTokens.Remove(refreshToken);
    await context.SaveChangesAsync();
}
```

**Варианты решения:**  
Включить `userId` в условие поиска.

```csharp
// ✅ РЕШЕНИЕ: проверять владельца токена
public async Task DeleteRefreshToken(long id, long userId)
{
    var refreshToken = await context.RefreshTokens
        .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId); // ← добавить userId

    if (refreshToken is null) throw new RefreshTokenNotFoundException();

    context.RefreshTokens.Remove(refreshToken);
    await context.SaveChangesAsync();
}
```

---

### BUG-05 — При включении 2FA (EnableOtpVerification) не отключается предыдущий метод

**Проблема:**  
При включении Authenticator OTP вызывается `AddUserOtpSecretKey` и `UpdateOptType`, но флаги `OtpEnabled`/`EmailOtpEnabled` не выставляются. Флаг `OtpEnabled = true` выставляется только в `ConfirmOtpVerification`. Однако при переключении с Email OTP на Authenticator — `EmailOtpEnabled` не сбрасывается в `false`. При включении Email OTP не сбрасывается `OtpEnabled`.

**Файл:** `Backend\BarkFluff.Identity\Features\EnableOtpVerification\EnableOtpVerificationCommandHandler.cs` : строки 87–112  
**Файл:** `Backend\BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs`

```csharp
// ❌ БАГ: при переключении методов 2FA старый флаг не сбрасывается
// Если был EmailOtpEnabled = true и пользователь переходит на Authenticator:
await _authPropertiesStorage.AddUserOtpSecretKey(userId, base32Secret);
await _authPropertiesStorage.UpdateOptType(OtpType.Authenticator, userId);
// EmailOtpEnabled остаётся true! При входе будет проверяться Email OTP вместо Authenticator
```

**Варианты решения:**  
При смене метода явно сбрасывать флаги предыдущего.

```csharp
// ✅ РЕШЕНИЕ: атомарный переход между методами 2FA
public async Task SwitchToAuthenticatorOtp(long userId, string secret)
{
    var props = await _context.AuthUserProperties
        .FirstOrDefaultAsync(x => x.UserId == userId);
    // ...
    props.OtpSecret = secret;
    props.SelectedOtpType = OtpType.Authenticator;
    props.EmailOtpEnabled = false; // ← явно сбрасываем Email OTP
    props.OtpEnabled = false;      // будет включён после подтверждения кода
    await _context.SaveChangesAsync();
}

public async Task SwitchToEmailOtp(long userId, string code)
{
    var props = await _context.AuthUserProperties
        .FirstOrDefaultAsync(x => x.UserId == userId);
    // ...
    props.LastEmailAuthCode = code;
    props.SelectedOtpType = OtpType.Email;
    props.OtpEnabled = false; // ← явно сбрасываем Authenticator флаг
    props.EmailOtpEnabled = false; // включится после подтверждения
    await _context.SaveChangesAsync();
}
```

---

### BUG-06 — `PasswordHasher` является статическим классом без интерфейса — невозможно тестировать и подменить

**Проблема:**  
`PasswordHasher.HashPassword` — статический метод. Его нельзя замокать в unit-тестах и нельзя заменить реализацию без изменения всех мест вызова. При переходе на BCrypt придётся менять код в нескольких местах.

**Файл:** `Backend\BarkFluff.Identity\Services\PasswordHasher.cs` : строки 1–15

```csharp
// ❌ ПРОБЛЕМА: статический класс — не тестируем, не заменяем
public class PasswordHasher  // фактически используется статически
{
    public static string HashPassword(string password) { ... }
}

// Вызов: PasswordHasher.HashPassword(request.Password) — напрямую, без DI
```

**Варианты решения:**  
Извлечь интерфейс и регистрировать через DI.

```csharp
// ✅ РЕШЕНИЕ: интерфейс + DI
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool Verify(string password, string hash)
        => BCrypt.Net.BCrypt.Verify(password, hash);
}

// Program.cs:
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

// В handlers через DI:
public class AuthCommandHandler(IPasswordHasher passwordHasher, ...) { ... }
```

---

## 🔵 Качество кода

---

### CODE-01 — Дублирование логики формирования `locationInfo` в 7+ местах

**Проблема:**  
Блок получения геолокации и формирования строки `locationInfo` скопирован дословно в `AuthCommandHandler`, `CreateAccountCommandHandler`, `ConfirmAccountCommandHandler`, `ResetPasswordCommandHandler`, `SetPasswordCommandHandler`, `EnableOtpVerificationCommandHandler`, `ConfirmResetPasswordCommandHandler` — минимум 7 копий одного и того же кода.

**Файл:** Множественные файлы в `Features/`

```csharp
// ❌ ДУБЛИРОВАНИЕ: один и тот же блок в 7 местах
string locationInfo = "-";
if (!string.IsNullOrEmpty(requestContext.IpAddress))
{
    var ipLocation = await locationClient.GetLocation(requestContext.IpAddress);
    if (ipLocation != null)
        locationInfo = $"{ipLocation.Country}, {ipLocation.RegionName}, {ipLocation.City}";
}
```

**Варианты решения:**  
Вынести в extension method или отдельный сервис.

```csharp
// ✅ РЕШЕНИЕ: extension method или сервис
public static class LocationClientExtensions
{
    public static async Task<string> GetLocationString(
        this LocationClient client, string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return "-";

        var location = await client.GetLocation(ipAddress);
        return location is null
            ? "-"
            : $"{location.Country}, {location.RegionName}, {location.City}";
    }
}

// Использование:
var locationInfo = await locationClient.GetLocationString(requestContext.IpAddress);
```

---

### CODE-02 — Непоследовательный стиль конструкторов (primary vs manual)

**Проблема:**  
Часть handlers использует primary constructors (`AuthCommandHandler`, `RefreshTokensStorage`), другие — ручное объявление полей и конструктора (`ResetPasswordCommandHandler`, `SetPasswordCommandHandler`). Код выглядит непоследовательно, нет единого стандарта.

**Файл:** `Backend\BarkFluff.Identity\Features\ResetPassword\ResetPasswordCommandHandler.cs` : строки 17–36  
**Файл:** `Backend\BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs` : строка 22

```csharp
// ❌ Непоследовательность: ручной конструктор
public class ResetPasswordCommandHandler : IRequestHandler<...>
{
    private readonly ResetPasswordsStorage _resetPasswordsStorage;
    private readonly AuthPropertiesStorage _authPropertiesStorage;
    // ... ещё 5 полей

    public ResetPasswordCommandHandler(ResetPasswordsStorage resetPasswordsStorage, ...)
    {
        _resetPasswordsStorage = resetPasswordsStorage;
        // ...
    }
}

// vs Primary constructor в других классах:
public class AuthCommandHandler(UsersServerApi.UsersServerApiClient usersClient, ...) { }
```

**Варианты решения:**  
Привести все handlers к единому стилю — primary constructors (C# 12 / .NET 8+).

```csharp
// ✅ РЕШЕНИЕ: primary constructor
public class ResetPasswordCommandHandler(
    ResetPasswordsStorage resetPasswordsStorage,
    AuthPropertiesStorage authPropertiesStorage,
    UsersServerApi.UsersServerApiClient usersClient,
    RequestContext requestContext,
    NotificationQueueSender notificationQueueSender,
    LocationClient locationClient,
    ILogger<ResetPasswordCommandHandler> logger)
    : IRequestHandler<ResetPasswordCommand, ResetPasswordResponse>
{
    public async Task<ResetPasswordResponse> Handle(...) { ... }
}
```

---

### CODE-03 — Отсутствует валидация входных данных на уровне команд

**Проблема:**  
Все валидации (`IsNullOrEmpty`, длина, формат) делаются внутри `Handle()` метода Handler'а с ручным бросанием исключений. Нет FluentValidation или DataAnnotations на уровне Command-объектов. При добавлении новых полей легко забыть добавить проверку.

**Файл:** `Backend\BarkFluff.Identity\Features\CreateAccount\CreateAccountCommand.cs`  
**Файл:** `Backend\BarkFluff.Identity\Features\CreateAccount\CreateAccountCommandHandler.cs` : строки 29–46

```csharp
// ❌ ПРОБЛЕМА: валидация размазана по Handler'ам
public async Task<CreateAccountResponse> Handle(CreateAccountCommand request, ...)
{
    if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Username))
        throw new UsernameOrEmailIsEmptyException();
    // Нет проверки формата email, длины username и т.д.
}
```

**Варианты решения:**  
Добавить FluentValidation через MediatR pipeline behavior.

```csharp
// ✅ РЕШЕНИЕ: FluentValidation + MediatR ValidationBehavior
public class CreateAccountCommandValidator : AbstractValidator<CreateAccountCommand>
{
    public CreateAccountCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.Username)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(50)
            .Matches("^[a-zA-Z0-9_]+$"); // только безопасные символы
    }
}

// Program.cs:
builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

---

### CODE-04 — `LocationClient` использует `http://` (не HTTPS) для внешнего API

**Проблема:**  
`BaseUrl = "http://ip-api.com/json/"` — запрос идёт по HTTP без шифрования. IP-адрес пользователя передаётся в открытом виде, ответ может быть подменён MITM-атакой, что приведёт к неверной геолокации в уведомлениях.

**Файл:** `Backend\BarkFluff.Identity\Infrastructure\LocationClient.cs` : строка 7

```csharp
// ❌ ПРОБЛЕМА: небезопасный HTTP
private const string BaseUrl = "http://ip-api.com/json/";
```

**Варианты решения:**  
Использовать `https://`. Бесплатный план ip-api.com поддерживает только HTTP — рассмотреть замену на `ipinfo.io` или `ip-api.com` Pro с HTTPS.

```csharp
// ✅ РЕШЕНИЕ: HTTPS + альтернативный провайдер
private const string BaseUrl = "https://ipinfo.io/"; // поддерживает HTTPS бесплатно

// Или — конфигурируемый URL через appsettings:
// "LocationApi": { "BaseUrl": "https://ipinfo.io/" }
```

---

*Итого найдено проблем:*  
| Категория | Количество |
|---|---|
| 🔴 Безопасность | 9 |
| 🟡 Производительность | 6 |
| 🟠 Баги и недоработки | 6 |
| 🔵 Качество кода | 4 |
| **Всего** | **25** |
