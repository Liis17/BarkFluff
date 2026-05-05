# Аудит: BarkFluff.Shared.Identity

> **Область аудита:** `Shared/BarkFluff.Shared.Identity` и всей инфраструктуры идентификации — `JwtService`, `XAuthExtensions`, `TokenRevocationCache`, `UserContext`, `RefreshTokenGenerator`, `PasswordHasher`, `AuthCommandHandler`, `RefreshTokensStorage`.
> **Дата:** 2026-05-06
> **Статус:** Активный

---

## Содержание

1. [🔴 Безопасность](#безопасность)
   - [SEC-01 — SHA-256 без соли для хэширования паролей](#sec-01--sha-256-без-соли-для-хэширования-паролей)
   - [SEC-02 — `new Random()` для генерации Refresh-токенов](#sec-02--new-random-для-генерации-refresh-токенов)
   - [SEC-03 — Refresh-токен не имеет индекса в БД и хранится в открытом виде](#sec-03--refresh-токен-не-имеет-индекса-в-бд-и-хранится-в-открытом-виде)
   - [SEC-04 — Email OTP-код хранится в открытом виде в БД и не имеет TTL](#sec-04--email-otp-код-хранится-в-открытом-виде-в-бд-и-не-имеет-ttl)
   - [SEC-05 — Отсутствие Rate Limiting при аутентификации (Brute-force)](#sec-05--отсутствие-rate-limiting-при-аутентификации-brute-force)
   - [SEC-06 — `TokenRevocationCache` — in-memory revocation, не работает в multi-instance](#sec-06--tokenrevocationcache--in-memory-revocation-не-работает-в-multi-instance)
   - [SEC-07 — Срок действия Service-токена — вечность (9999 год)](#sec-07--срок-действия-service-токена--вечность-9999-год)
   - [SEC-08 — `JwtSettings.SecretKey` без валидации минимальной длины](#sec-08--jwtsettingssecretkey-без-валидации-минимальной-длины)
   - [SEC-09 — `x-auth-token` в заголовке — токен не protected от логирования](#sec-09--x-auth-token-в-заголовке--токен-не-protected-от-логирования)
   - [SEC-10 — Email OTP без защиты от timing attack](#sec-10--email-otp-без-защиты-от-timing-attack)
2. [🟡 Оптимизация](#оптимизация)
   - [OPT-01 — `TokenRevocationCache.Cleanup()` итерирует весь словарь каждые 5 минут](#opt-01--tokenrevocationcachecleanu-итерирует-весь-словарь-каждые-5-минут)
   - [OPT-02 — `RefreshTokensStorage.FindRefreshToken` — поиск по неиндексированной строке](#opt-02--refreshtokensstoragefindrefreshtoken--поиск-по-неиндексированной-строке)
   - [OPT-03 — `UpdateActivity` в `TokenService` делает двойной FindById](#opt-03--updateactivity-в-tokenservice-делает-двойной-findbyadid)
   - [OPT-04 — `AuthCommandHandler` вызывает `GetUserContactsAsync` до трёх раз за один запрос](#opt-04--authcommandhandler-вызывает-getusercontactsasync-до-трёх-раз-за-один-запрос)
3. [🟠 Баги и недоработки](#баги-и-недоработки)
   - [BUG-01 — `UserContext.IsAuthenticated` для Service-токенов всегда `false`](#bug-01--usercontextisauthenticated-для-service-токенов-всегда-false)
   - [BUG-02 — `DeleteRefreshToken` не проверяет принадлежность userId](#bug-02--deleterefreshtoken-не-проверяет-принадлежность-userid)
   - [BUG-03 — `IdentityClaims` — строковые константы и enum `TokenType` сравниваются через `.ToString()` без проверки регистра](#bug-03--identityclaims--строковые-константы-и-enum-tokentype-сравниваются-через-tostring-без-проверки-регистра)
   - [BUG-04 — `ServiceId.Unknown = 0` — может быть случайно использован как валидный сервис](#bug-04--serviceidunknown--0--может-быть-случайно-использован-как-валидный-сервис)
   - [BUG-05 — `RefreshToken.ExpiresAt` не проверяется при использовании токена](#bug-05--refreshtokenexpiresat-не-проверяется-при-использовании-токена)
4. [🔵 Прочее / Code Quality](#прочее--code-quality)
   - [MISC-01 — `JwtSettings` — properties без `required` или `init` (nullable warnings)](#misc-01--jwtsettings--properties-без-required-или-init-nullable-warnings)
   - [MISC-02 — Дублирование `PasswordHasher` в двух проектах](#misc-02--дублирование-passwordhasher-в-двух-проектах)
   - [MISC-03 — `TokenType` и `ServiceId` — trailing comma в последнем члене enum](#misc-03--tokentype-и-serviceid--trailing-comma-в-последнем-члене-enum)

---

## Безопасность

---

### SEC-01 — SHA-256 без соли для хэширования паролей

**Проблема / Описание**
Пароли хэшируются через SHA-256 без соли. SHA-256 — криптографически стойкая хэш-функция общего назначения, но **не предназначена для хэширования паролей**: она чрезвычайно быстра (миллиарды итераций/сек на GPU), что делает брутфорс и атаки по словарю тривиальными. Отсутствие соли позволяет использовать радужные таблицы.

**Путь к файлу:** `Backend/BarkFluff.Identity/Services/PasswordHasher.cs : 8–14`

```csharp
// ❌ ПРОБЛЕМА: SHA-256 без соли — не подходит для паролей
public static string HashPassword(string password)
{
    using var sha256 = SHA256.Create();
    var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
    // Нет соли, нет итераций, нет work-factor — уязвимо к rainbow tables и GPU brute-force
    return Convert.ToBase64String(hashedBytes);
}
```

**Варианты решения**
1. Использовать `Microsoft.AspNetCore.Identity.PasswordHasher<T>` (PBKDF2-SHA512, 100k итераций).
2. Использовать `BCrypt.Net-Next` (BCrypt, адаптивный work-factor).

```csharp
// ✅ РЕШЕНИЕ: PBKDF2 через стандартный ASP.NET Core PasswordHasher
using Microsoft.AspNetCore.Identity;

public static class PasswordHasher
{
    // Stateless hasher — T может быть любым классом-заглушкой
    private static readonly PasswordHasher<object> _hasher = new();

    public static string HashPassword(string password)
        => _hasher.HashPassword(null!, password);

    public static bool VerifyPassword(string hashedPassword, string providedPassword)
        => _hasher.VerifyHashedPassword(null!, hashedPassword, providedPassword)
           != PasswordVerificationResult.Failed;
}
```

> ⚠️ **Важно:** при смене алгоритма необходима миграция существующих хэшей (принудительная смена паролей или ленивая миграция при следующем входе).

---

### SEC-02 — `new Random()` для генерации Refresh-токенов

**Проблема / Описание**
`System.Random` — псевдослучайный генератор, **не криптографически стойкий**. Злоумышленник, зная алгоритм и seed (который может быть предсказуем), может предсказать сгенерированные токены. Длина токена — 20 символов (алфавит ≈ 62 символа) — это ~119 бит энтропии при честном RNG, но на практике с `Random` — намного меньше.

**Путь к файлу:** `Backend/BarkFluff.Identity/Services/RefreshTokenGenerator.cs : 6–25`

```csharp
// ❌ ПРОБЛЕМА: System.Random — не CSPRNG, предсказуем
public static string GenerateRefreshToken()
{
    var random = new Random(); // seed может быть предсказан
    var stringChars = new char[20]; // 20 символов — мало для refresh token
    for (var i = 0; i < stringChars.Length; i++)
    {
        var randomChar = (char)random.Next(48, 123); // не криптографически стойко
        // ...
    }
    return new string(stringChars);
}
```

**Варианты решения**
1. Использовать `RandomNumberGenerator.GetBytes()` (BCL CSPRNG).
2. Использовать `Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))` — 256 бит энтропии, URL-safe.

```csharp
// ✅ РЕШЕНИЕ: криптографически стойкий генератор
using System.Security.Cryptography;

public static class RefreshTokenGenerator
{
    public static string GenerateRefreshToken()
    {
        // 32 байта = 256 бит энтропии — стандарт для refresh token
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('='); // URL-safe Base64
    }
}
```

---

### SEC-03 — Refresh-токен не имеет индекса в БД и хранится в открытом виде

**Проблема / Описание**
`RefreshToken.Value` — строка без индекса в сущности EF. Поиск `FirstOrDefaultAsync(x => x.Value == refreshToken)` — полный table scan. Кроме того, токен хранится в открытом виде: компрометация БД = компрометация всех сессий.

**Путь к файлу:** `Backend/BarkFluff.Identity/Domain/RefreshToken.cs : 10` и `Persistence/Services/RefreshTokensStorage.cs : 12–17`

```csharp
// ❌ ПРОБЛЕМА 1: нет индекса на Value — table scan при каждом обновлении токена
public string Value { get; set; } // нет [Index] или FluentAPI HasIndex

// ❌ ПРОБЛЕМА 2: хранится в открытом виде — утечка БД = утечка всех токенов
var refreshTokenEntity = await context.RefreshTokens
    .AsNoTracking()
    .FirstOrDefaultAsync(x => x.Value == refreshToken); // full scan
```

**Варианты решения**
1. Добавить индекс на `Value` через FluentAPI или атрибут.
2. Хранить SHA-256 хэш токена, сравнивать с хэшем входящего значения.

```csharp
// ✅ РЕШЕНИЕ: индекс + хэширование при хранении

// Domain/RefreshToken.cs
public class RefreshToken
{
    [Key]
    public long Id { get; set; }

    // Хранится SHA-256 хэш токена
    public string ValueHash { get; set; } = null!;

    public long UserId { get; set; }
    public string DeviceId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

// В OnModelCreating:
modelBuilder.Entity<RefreshToken>()
    .HasIndex(x => x.ValueHash)
    .IsUnique();

// RefreshTokensStorage: поиск по хэшу
public async Task<RefreshToken?> FindRefreshToken(string refreshToken)
{
    var hash = ComputeHash(refreshToken);
    return await context.RefreshTokens
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.ValueHash == hash);
}

private static string ComputeHash(string value)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
    return Convert.ToHexString(bytes);
}
```

---

### SEC-04 — Email OTP-код хранится в открытом виде в БД и не имеет TTL

**Проблема / Описание**
`LastEmailAuthCode` записывается в `authPropertiesStorage` как plaintext-строка и никогда не инвалидируется после использования (нет поля `LastEmailAuthCodeExpiresAt`, нет сброса после успешного использования). Любой доступ к БД раскрывает текущий OTP. Повторное использование кода не защищено.

**Путь к файлу:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs : 115, 182–191`

```csharp
// ❌ ПРОБЛЕМА 1: код записывается в открытом виде, без TTL
await authPropertiesStorage.UpdateLastEmailAuthCode(userContactInfo.User.Id, code);

// ❌ ПРОБЛЕМА 2: код не сбрасывается после успешной проверки — можно использовать повторно
if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode,
        StringComparison.InvariantCultureIgnoreCase))
{
    throw new NotValidOtpCodeException();
}
// После этого if — код всё ещё живёт в БД
```

**Варианты решения**
1. Хранить SHA-256 хэш кода и timestamp истечения (например, +10 минут).
2. После успешной проверки — немедленно обнулять код в БД.

```csharp
// ✅ РЕШЕНИЕ: хэш кода + TTL + очистка после использования

// При генерации:
var code = CodeGenerator.GenerateDigitalCode(6);
var codeHash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
var expiresAt = DateTime.UtcNow.AddMinutes(10);
await authPropertiesStorage.UpdateLastEmailAuthCode(
    userId, Convert.ToHexString(codeHash), expiresAt);

// При проверке:
var inputHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.OtpCode)));
if (optOptions.LastEmailAuthCodeExpiresAt < DateTime.UtcNow)
    throw new NotValidOtpCodeException(); // код истёк

if (!string.Equals(optOptions.LastEmailAuthCodeHash, inputHash, StringComparison.Ordinal))
    throw new NotValidOtpCodeException();

// Сбросить код после успешной проверки
await authPropertiesStorage.ClearEmailAuthCode(user.User.Id);
```

---

### SEC-05 — Отсутствие Rate Limiting при аутентификации (Brute-force)

**Проблема / Описание**
Endpoint авторизации (`AuthCommandHandler`) не имеет никакой защиты от перебора. Злоумышленник может отправлять неограниченное количество запросов с разными паролями. При слабом хэше пароля (SEC-01) атака становится ещё опаснее.

**Путь к файлу:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs : 29–337` (нет ни одной проверки на количество попыток)

```csharp
// ❌ ПРОБЛЕМА: нет ограничения на количество попыток входа
public async Task<AuthResponse> Handle(AuthCommand request, CancellationToken cancellationToken)
{
    // Просто проверяем пароль — без счётчика неудачных попыток,
    // без блокировки IP, без временной задержки
    if (!string.Equals(currentPasswordHash, enteredPasswordHash))
    {
        throw new InvalidLoginOrPasswordException(); // и снова можно пробовать
    }
}
```

**Варианты решения**
1. Использовать `AspNetCoreRateLimit` или встроенный `Microsoft.AspNetCore.RateLimiting` (.NET 7+).
2. Хранить счётчик неудачных попыток в Redis с TTL и блокировать после N попыток.

```csharp
// ✅ РЕШЕНИЕ: встроенный rate limiter в Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("auth", cfg =>
    {
        cfg.Window = TimeSpan.FromMinutes(15);
        cfg.PermitLimit = 5; // максимум 5 попыток за 15 минут с одного IP
        cfg.QueueLimit = 0;
        cfg.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

// На gRPC-эндпоинте или в middleware — применять по IP + username
// Дополнительно: блокировка аккаунта после X неудачных попыток
private async Task IncrementFailedAttempts(long userId)
{
    var key = $"failed_auth:{userId}";
    var count = await _redis.StringIncrementAsync(key);
    await _redis.KeyExpireAsync(key, TimeSpan.FromMinutes(15));

    if (count >= 10)
        throw new AccountTemporarilyLockedException();
}
```

---

### SEC-06 — `TokenRevocationCache` — in-memory revocation, не работает в multi-instance

**Проблема / Описание**
Отзыв токенов хранится исключительно в памяти одного инстанса (`ConcurrentDictionary`). В multi-instance сценарии (несколько реплик сервиса, что типично в Docker/k8s) отзыв токена на одном инстансе не распространяется на другие. Токен будет считаться действительным на остальных репликах.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs : 1–34`

```csharp
// ❌ ПРОБЛЕМА: локальный in-memory словарь — не работает при горизонтальном масштабировании
public class TokenRevocationCache
{
    // Это состояние есть только в одном процессе
    private readonly ConcurrentDictionary<string, DateTime> _revokedSessions = new();

    public void Revoke(long userId, string deviceId, DateTime accessTokenExpiresAt)
        => _revokedSessions[BuildKey(userId, deviceId)] = accessTokenExpiresAt;

    public bool IsRevoked(long userId, string deviceId)
        => _revokedSessions.ContainsKey(BuildKey(userId, deviceId)); // на другом поде — false
}
```

**Варианты решения**
1. Перенести хранилище отзывов в **Redis** с TTL = время истечения access token.
2. Использовать Redis Pub/Sub для трансляции отзывов на все инстансы.

```csharp
// ✅ РЕШЕНИЕ: Redis-backed revocation cache
public class TokenRevocationCache(IConnectionMultiplexer redis)
{
    private readonly IDatabase _db = redis.GetDatabase();
    private const string Prefix = "revoked:";

    public async Task RevokeAsync(long userId, string deviceId, DateTime accessTokenExpiresAt)
    {
        var key = $"{Prefix}{userId}:{deviceId}";
        var ttl = accessTokenExpiresAt - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            await _db.StringSetAsync(key, "1", ttl);
    }

    public async Task<bool> IsRevokedAsync(long userId, string deviceId)
    {
        var key = $"{Prefix}{userId}:{deviceId}";
        return await _db.KeyExistsAsync(key);
    }
}
```

---

### SEC-07 — Срок действия Service-токена — вечность (9999 год)

**Проблема / Описание**
Service-токены генерируются с датой истечения `9999-12-31`. Если такой токен утечёт (например, через логи, утечку конфигурации), он будет действителен практически вечно. Стандарт безопасности требует минимально необходимого срока жизни токенов.

**Путь к файлу:** `Backend/BarkFluff.Identity/Services/JwtService.cs : 41`

```csharp
// ❌ ПРОБЛЕМА: токен живёт 7900+ лет — нарушение принципа минимальных привилегий
var dateEnd = new DateTime(9999, 12, 31, 23, 59, 59);
var token = CreateToken(claims, dateEnd);
```

**Варианты решения**
1. Выдавать Service-токены с разумным сроком (например, 1 год) и реализовать механизм ротации.
2. Использовать короткоживущие токены (1–24 часа) с автоматическим обновлением через клиентский interceptor.

```csharp
// ✅ РЕШЕНИЕ: ограниченный срок жизни с конфигурируемым значением
public string GenerateServerToken(ServiceId serviceId)
{
    var claims = new List<Claim>
    {
        new(IdentityClaims.ServiceId, serviceId.ToString()),
        new(IdentityClaims.TokenType, TokenType.Service.ToString()),
    };

    // Срок берём из конфигурации, например 365 дней
    var dateEnd = DateTime.UtcNow.AddDays(jwtSettings.ServiceTokenExpiryDays);
    return CreateToken(claims, dateEnd);
}
```

---

### SEC-08 — `JwtSettings.SecretKey` без валидации минимальной длины

**Проблема / Описание**
Секретный ключ берётся из конфигурации без проверки длины. HMAC-SHA256 требует минимум 256 бит (32 байта). Слабый ключ делает JWT легко подделываемыми.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs : 26`, `Backend/BarkFluff.Identity/Services/JwtService.cs : 50`

```csharp
// ❌ ПРОБЛЕМА: ключ может быть любой длины, даже "abc"
IssuerSigningKey = new SymmetricSecurityKey(
    Encoding.ASCII.GetBytes(configuration["JwtSettings:SecretKey"]!))
// Нет проверки: configuration["JwtSettings:SecretKey"]?.Length >= 32
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: валидация при старте приложения
var secretKey = configuration["JwtSettings:SecretKey"]
    ?? throw new InvalidOperationException("JwtSettings:SecretKey is not configured");

if (Encoding.UTF8.GetByteCount(secretKey) < 32)
    throw new InvalidOperationException(
        "JwtSettings:SecretKey must be at least 32 bytes (256 bits) for HMAC-SHA256");

IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
// Используем UTF8, а не ASCII — ASCII теряет символы > 127
```

---

### SEC-09 — `x-auth-token` в заголовке — токен не protected от логирования

**Проблема / Описание**
Токен передаётся в HTTP-заголовке `x-auth-token`. Стандартный логгер ASP.NET Core, nginx-логи, gRPC-трассировка и APM-инструменты по умолчанию могут логировать заголовки запросов. Access token в логах = долгосрочный секрет в plaintext.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs : 39–41`

```csharp
// ❌ РИСК: нестандартный заголовок может попасть в логи без sanitization
if (context.Request.Headers.TryGetValue("x-auth-token", out var token))
{
    context.Token = token; // токен может быть в access-логах nginx/ASP.NET
}
```

**Варианты решения**
1. Добавить middleware для маскирования заголовка `x-auth-token` в логах.
2. Обеспечить на уровне конфигурации логгера исключение чувствительных заголовков.

```csharp
// ✅ РЕШЕНИЕ: исключить заголовок из логирования
// В Program.cs при настройке логирования gRPC:
builder.Services.AddGrpc(options =>
{
    options.EnableDetailedErrors = false; // не в Production
});

// Middleware для sanitization:
app.Use(async (context, next) =>
{
    // Убираем из трассировки перед логированием
    context.Request.Headers.Remove("x-auth-token");
    await next();
});
// Или настроить фильтрацию в Serilog/NLog DestructuringPolicy
```

---

### SEC-10 — Email OTP без защиты от timing attack

**Проблема / Описание**
Сравнение Email OTP кода выполняется через `string.Equals` — обычное строковое сравнение. Атака по времени (timing attack) позволяет определить правильный префикс кода, замеряя время ответа.

**Путь к файлу:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs : 182–183`

```csharp
// ❌ ПРОБЛЕМА: не constant-time сравнение
if (!string.Equals(optOptions.LastEmailAuthCode, request.OtpCode,
        StringComparison.InvariantCultureIgnoreCase))
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: constant-time сравнение через CryptographicOperations
using System.Security.Cryptography;

// Сравниваем байтовые представления — constant-time
var expected = Encoding.UTF8.GetBytes(optOptions.LastEmailAuthCode ?? "");
var actual   = Encoding.UTF8.GetBytes(request.OtpCode ?? "");

if (!CryptographicOperations.FixedTimeEquals(expected, actual))
    throw new NotValidOtpCodeException();
```

---

## Оптимизация

---

### OPT-01 — `TokenRevocationCache.Cleanup()` итерирует весь словарь каждые 5 минут

**Проблема / Описание**
`CleanupService` итерирует **весь** `ConcurrentDictionary` каждые 5 минут. При большом количестве отозванных сессий это блокирует потоки. Оптимальнее — хранить ключи с их TTL и использовать структуру с автоматическим устареванием.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs : 21–30`

```csharp
// ❌ ПРОБЛЕМА: O(N) итерация всего словаря на каждый цикл очистки
public void Cleanup()
{
    var now = DateTime.UtcNow;
    foreach (var kvp in _revokedSessions) // перебираем все записи
    {
        if (kvp.Value < now)
            _revokedSessions.TryRemove(kvp.Key, out _);
    }
}
```

**Варианты решения**
1. При переходе на Redis (SEC-06) — автоматический TTL решает проблему.
2. Использовать `MemoryCache` с встроенным TTL вместо `ConcurrentDictionary`.

```csharp
// ✅ РЕШЕНИЕ: MemoryCache с автоматическим вытеснением по TTL
public class TokenRevocationCache(IMemoryCache cache)
{
    public void Revoke(long userId, string deviceId, DateTime accessTokenExpiresAt)
    {
        var key = BuildKey(userId, deviceId);
        var ttl = accessTokenExpiresAt - DateTime.UtcNow;
        if (ttl > TimeSpan.Zero)
            cache.Set(key, true, ttl); // автоматически удалится по истечении
    }

    public bool IsRevoked(long userId, string deviceId)
        => cache.TryGetValue(BuildKey(userId, deviceId), out _);

    // Cleanup() больше не нужен — MemoryCache управляет жизненным циклом сам
    private static string BuildKey(long userId, string deviceId) => $"revoked:{userId}:{deviceId}";
}
```

---

### OPT-02 — `RefreshTokensStorage.FindRefreshToken` — поиск по неиндексированной строке

**Проблема / Описание**
Метод `FindRefreshToken` выполняет поиск по полю `Value` без индекса — это full table scan при каждом обновлении access token. При большом количестве сессий это узкое место производительности.

**Путь к файлу:** `Backend/BarkFluff.Identity/Persistence/Services/RefreshTokensStorage.cs : 12–17`

```csharp
// ❌ ПРОБЛЕМА: нет индекса на Value — полное сканирование таблицы
var refreshTokenEntity = await context.RefreshTokens
    .AsNoTracking()
    .FirstOrDefaultAsync(x => x.Value == refreshToken); // seq scan в PostgreSQL
```

**Варианты решения**
Добавить уникальный индекс в конфигурации EF (связано с SEC-03):

```csharp
// ✅ РЕШЕНИЕ: уникальный индекс через Fluent API в OnModelCreating
modelBuilder.Entity<RefreshToken>(entity =>
{
    entity.HasIndex(x => x.Value)
          .IsUnique()
          .HasDatabaseName("IX_RefreshTokens_Value");
});
// После добавления: CREATE UNIQUE INDEX IX_RefreshTokens_Value ON "RefreshTokens" ("Value");
// Поиск становится O(log N) вместо O(N)
```

---

### OPT-03 — `UpdateActivity` в `TokenService` делает двойной `FindById`

**Проблема / Описание**
`ValidateToken` вызывает `FindById`, затем вызывает `UpdateActivity`, которая снова делает `FindById` — итого 2 запроса к БД вместо 1 при каждом запросе через AdminPanel.

**Путь к файлу:** `Backend/Barkfluff.AdminPanel/Services/TokenService.cs : 50–73`

```csharp
// ❌ ПРОБЛЕМА: двойной FindById — 2 запроса к БД
public AuthToken? ValidateToken(Guid tokenId)
{
    var token = _db.Tokens.FindById(tokenId); // запрос #1
    if (token == null) return null;
    if (token.IsExpired(...)) { _db.Tokens.Delete(tokenId); return null; }

    UpdateActivity(tokenId); // здесь снова FindById — запрос #2
    return token;
}

public void UpdateActivity(Guid tokenId)
{
    var token = _db.Tokens.FindById(tokenId); // избыточный запрос #2
    if (token != null) { token.LastActivity = DateTime.UtcNow; _db.Tokens.Update(token); }
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: переиспользовать уже загруженный объект
public AuthToken? ValidateToken(Guid tokenId)
{
    var token = _db.Tokens.FindById(tokenId); // единственный FindById
    if (token == null) return null;

    if (token.IsExpired(_settings.Value.TokenExpirationDays))
    {
        _db.Tokens.Delete(tokenId);
        return null;
    }

    // Обновляем прямо здесь, без повторного запроса
    token.LastActivity = DateTime.UtcNow;
    _db.Tokens.Update(token);
    return token;
}
// UpdateActivity можно оставить публичным для внешних вызовов,
// но ValidateToken должен работать через единственный запрос
```

---

### OPT-04 — `AuthCommandHandler` вызывает `GetUserContactsAsync` до трёх раз за один запрос

**Проблема / Описание**
За один успешный `Handle` вызов в зависимости от пути выполнения `GetUserContactsAsync` вызывается 2–3 раза: при отправке Email OTP, при неудачном пароле и при успешном входе. Каждый вызов — gRPC round-trip к Users сервису.

**Путь к файлу:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs : 112, 211, 296`

```csharp
// ❌ ПРОБЛЕМА: до 3 gRPC вызовов GetUserContactsAsync за один Handle
// Строка 112: при отправке OTP на email
var userContactInfo = await usersClient.GetUserContactsAsync(...);

// Строка 211: при неудачном входе (уведомление)
var userContactInfo = await usersClient.GetUserContactsAsync(...);

// Строка 296: при успешном входе (уведомление)
var successUserContactInfo = await usersClient.GetUserContactsAsync(...);
```

**Варианты решения**
Закэшировать результат в локальную переменную при первом вызове:

```csharp
// ✅ РЕШЕНИЕ: единственный вызов, результат переиспользуется
GetUserContactsResponse? contactInfo = null;

// Ленивая инициализация — загрузить один раз когда нужно
async Task<GetUserContactsResponse> GetContactInfo() =>
    contactInfo ??= await usersClient.GetUserContactsAsync(
        new GetUserContactsRequest { UserId = user.User.Id });

// Далее везде вместо прямых вызовов:
var info = await GetContactInfo(); // gRPC вызов только при первом обращении
```

---

## Баги и недоработки

---

### BUG-01 — `UserContext.IsAuthenticated` для Service-токенов всегда `false`

**Проблема / Описание**
`IsAuthenticated` проверяет `UserId != 0`, но Service-токены не содержат клейм `x-user-id` — `UserId` всегда будет `0`. Любой код, использующий `userContext.IsAuthenticated` для проверки авторизации сервисных вызовов, получит `false` даже при валидном Service-токене.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/UserContext.cs : 15`

```csharp
// ❌ БАГ: для Service-токена UserId = 0 → IsAuthenticated = false, хотя токен валиден
public bool IsAuthenticated => UserId != 0 && TokenType != TokenType.Unknown;
// Service-токен: UserId=0, TokenType=Service → 0 != 0 → false ← НЕВЕРНО
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: учитывать тип токена при определении IsAuthenticated
public bool IsAuthenticated => TokenType switch
{
    TokenType.User    => UserId != 0,  // User-токен должен иметь UserId
    TokenType.Service => true,          // Service-токен валиден без UserId
    _                 => false
};
```

---

### BUG-02 — `DeleteRefreshToken` не проверяет принадлежность `userId`

**Проблема / Описание**
Метод `DeleteRefreshToken(long id, long userId)` принимает `userId`, но ищет токен **только по `id`** — `userId` не используется в запросе. Злоумышленник, зная чужой `id` токена, может удалить сессию другого пользователя.

**Путь к файлу:** `Backend/BarkFluff.Identity/Persistence/Services/RefreshTokensStorage.cs : 43–55`

```csharp
// ❌ БАГ: userId передаётся, но не используется в запросе!
public async Task DeleteRefreshToken(long id, long userId)
{
    var refreshToken = await context.RefreshTokens
        .FirstOrDefaultAsync(x => x.Id == id); // userId не фильтруется!

    if (refreshToken is null) throw new RefreshTokenNotFoundException();

    context.RefreshTokens.Remove(refreshToken);
    await context.SaveChangesAsync();
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: фильтровать по обоим полям — id И userId
public async Task DeleteRefreshToken(long id, long userId)
{
    var refreshToken = await context.RefreshTokens
        .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId); // проверяем принадлежность

    if (refreshToken is null)
        throw new RefreshTokenNotFoundException(); // не найден ИЛИ чужой токен

    context.RefreshTokens.Remove(refreshToken);
    await context.SaveChangesAsync();
}
```

---

### BUG-03 — `IdentityClaims` и enum `TokenType` сравниваются через `.ToString()` без проверки регистра

**Проблема / Описание**
В `XAuthExtensions` политики авторизации сравнивают значение клейма с `"Service"` / `"User"` (PascalCase). Если когда-либо будет изменён `TokenType.ToString()` или добавлен `[EnumMember]`, сравнение сломается молча.

**Путь к файлу:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs : 51, 76–80`

```csharp
// ❌ ХРУПКО: магические строки, завязанные на ToString() enum
if (tokenType == TokenType.User.ToString())  // "User" — хардкод

options.AddPolicy(nameof(TokenType.Service),
    p => p.RequireClaim(IdentityClaims.TokenType, "Service")); // ещё один хардкод
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: использовать nameof или константы из самого enum
// В IdentityClaims добавить строковые значения:
public static class IdentityClaimValues
{
    public static readonly string UserToken    = TokenType.User.ToString();
    public static readonly string ServiceToken = TokenType.Service.ToString();
}

// В XAuthExtensions:
if (tokenType == IdentityClaimValues.UserToken) // единственный источник истины

options.AddPolicy(nameof(TokenType.Service),
    p => p.RequireClaim(IdentityClaims.TokenType, IdentityClaimValues.ServiceToken));
```

---

### BUG-04 — `ServiceId.Unknown = 0` — может быть случайно использован как валидный сервис

**Проблема / Описание**
Первый член enum `ServiceId` имеет значение `0`, что является значением по умолчанию в C# для `enum`-полей. Любое неинициализированное поле типа `ServiceId` будет иметь значение `Unknown`. Если где-то сервисный токен будет создан с дефолтным значением — он пройдёт валидацию как токен с `ServiceId = "Unknown"`.

**Путь к файлу:** `Shared/BarkFluff.Shared.Identity/ServiceId.cs : 5`

```csharp
// ⚠️ РИСК: ServiceId serviceId = default; → serviceId == ServiceId.Unknown = 0
// GenerateServerToken(default) создаст токен с ServiceId="Unknown"
public enum ServiceId
{
    Unknown = 0, // это значение по умолчанию для всех неинициализированных переменных
    Identity = 1,
    // ...
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: валидация в GenerateServerToken
public string GenerateServerToken(ServiceId serviceId)
{
    if (serviceId == ServiceId.Unknown)
        throw new ArgumentException("Cannot generate token for Unknown service", nameof(serviceId));

    // ...
}
```

---

### BUG-05 — `RefreshToken.ExpiresAt` не проверяется при использовании токена

**Проблема / Описание**
`RefreshToken` содержит поле `ExpiresAt`, но при поиске токена через `FindRefreshToken` нет проверки, что токен не истёк. Устаревший refresh-токен считается валидным до тех пор, пока не будет удалён вручную.

**Путь к файлу:** `Backend/BarkFluff.Identity/Persistence/Services/RefreshTokensStorage.cs : 11–18` и `Domain/RefreshToken.cs : 18`

```csharp
// ❌ БАГ: ExpiresAt есть в модели, но не проверяется при поиске
public async Task<RefreshToken?> FindRefreshToken(string refreshToken)
{
    return await context.RefreshTokens
        .AsNoTracking()
        .FirstOrDefaultAsync(x => x.Value == refreshToken);
        // Нет: && x.ExpiresAt > DateTime.UtcNow
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: фильтровать истёкшие токены на уровне запроса
public async Task<RefreshToken?> FindRefreshToken(string refreshToken)
{
    return await context.RefreshTokens
        .AsNoTracking()
        .FirstOrDefaultAsync(x =>
            x.Value == refreshToken &&
            x.ExpiresAt > DateTime.UtcNow); // токен должен быть актуальным
}
```

---

## Прочее / Code Quality

---

### MISC-01 — `JwtSettings` — properties без `required` или `init` (nullable warnings)

**Проблема / Описание**
Все свойства `JwtSettings` объявлены как `string` без `required` или `= null!`. При включённом `<Nullable>enable</Nullable>` — предупреждения компилятора. При отсутствии конфигурации — `NullReferenceException` в runtime вместо информативного сообщения при старте.

**Путь к файлу:** `Backend/BarkFluff.Identity/Settings/JwtSettings.cs : 5–11`

```csharp
// ❌ ПРОБЛЕМА: non-nullable свойства без инициализатора
public class JwtSettings
{
    public string SecretKey { get; set; }   // CS8618 — может быть null
    public string Issuer { get; set; }
    public string Audience { get; set; }
    public int ExpiryMinutes { get; set; }
}
```

**Варианты решения**

```csharp
// ✅ РЕШЕНИЕ: required свойства + валидация через IOptions
public class JwtSettings
{
    [Required]
    public required string SecretKey { get; init; }

    [Required]
    public required string Issuer { get; init; }

    [Required]
    public required string Audience { get; init; }

    [Range(1, 10080)] // от 1 минуты до 7 дней
    public int ExpiryMinutes { get; init; } = 60;

    public int ServiceTokenExpiryDays { get; init; } = 365;
}

// В Program.cs:
builder.Services.AddOptions<JwtSettings>()
    .BindConfiguration("JwtSettings")
    .ValidateDataAnnotations()
    .ValidateOnStart(); // упадёт при старте, а не в runtime
```

---

### MISC-02 — Дублирование `PasswordHasher` в двух проектах

**Проблема / Описание**
Идентичный по назначению класс `PasswordHasher` существует в двух местах: `BarkFluff.Identity/Services/PasswordHasher.cs` и `BarkFluff.Users/Helpers/PasswordHasher.cs`. Если алгоритм хэширования изменится (что необходимо — см. SEC-01), его нужно менять в двух местах.

**Путь к файлам:**
- `Backend/BarkFluff.Identity/Services/PasswordHasher.cs`
- `Backend/BarkFluff.Users/Helpers/PasswordHasher.cs`

```csharp
// ❌ ДУБЛИРОВАНИЕ: одинаковая логика в двух разных проектах
// Identity: PasswordHasher.HashPassword(password)
// Users:    PasswordHasher.HashPassword(password) — та же реализация
```

**Варианты решения**
Вынести в `BarkFluff.Shared.Identity` или отдельную shared-библиотеку `BarkFluff.Shared.Security`:

```csharp
// ✅ РЕШЕНИЕ: единая реализация в Shared-проекте
// Shared/BarkFluff.Shared.Identity/PasswordHasher.cs
namespace BarkFluff.Shared.Identity;

public static class PasswordHasher
{
    private static readonly PasswordHasher<object> _hasher = new();

    public static string HashPassword(string password)
        => _hasher.HashPassword(null!, password);

    public static bool VerifyPassword(string hash, string password)
        => _hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
```

---

### MISC-03 — `TokenType` и `ServiceId` — trailing comma в последнем члене enum

**Проблема / Описание**
`ServiceId.cs` содержит лишнюю запятую после последнего члена `Developers = 12,`. Хотя это допустимо синтаксически в C#, это нарушает единообразие с `TokenType`, где последний член не имеет запятой. Незначительная проблема стиля.

**Путь к файлу:** `Shared/BarkFluff.Shared.Identity/ServiceId.cs : 29`

```csharp
// ⚠️ СТИЛЬ: trailing comma после последнего члена enum
public enum ServiceId
{
    // ...
    Developers = 12, // ← лишняя запятая
}
```

**Варианты решения**

```csharp
// ✅ Убрать trailing comma или принять как стандарт и применить везде единообразно
    Developers = 12
}
```

---

## Сводная таблица проблем

| ID | Категория | Серьёзность | Файл | Краткое описание |
|----|-----------|-------------|------|-----------------|
| SEC-01 | Безопасность | 🔴 Критическая | `PasswordHasher.cs` | SHA-256 без соли для паролей |
| SEC-02 | Безопасность | 🔴 Критическая | `RefreshTokenGenerator.cs` | `new Random()` — не CSPRNG |
| SEC-03 | Безопасность | 🟠 Высокая | `RefreshToken.cs` | Нет индекса, токен в открытом виде |
| SEC-04 | Безопасность | 🟠 Высокая | `AuthCommandHandler.cs` | Email OTP без TTL и очистки |
| SEC-05 | Безопасность | 🟠 Высокая | `AuthCommandHandler.cs` | Нет Rate Limiting (brute-force) |
| SEC-06 | Безопасность | 🟡 Средняя | `TokenRevocationCache.cs` | In-memory revocation, не масштабируется |
| SEC-07 | Безопасность | 🟡 Средняя | `JwtService.cs` | Service-токен живёт до 9999 года |
| SEC-08 | Безопасность | 🟡 Средняя | `XAuthExtensions.cs` | Нет валидации длины SecretKey |
| SEC-09 | Безопасность | 🟡 Средняя | `XAuthExtensions.cs` | Токен в заголовке — риск логирования |
| SEC-10 | Безопасность | 🟡 Средняя | `AuthCommandHandler.cs` | Timing attack на Email OTP |
| OPT-01 | Оптимизация | 🟡 Средняя | `TokenRevocationCache.cs` | O(N) итерация при очистке |
| OPT-02 | Оптимизация | 🟡 Средняя | `RefreshTokensStorage.cs` | Нет индекса на RefreshToken.Value |
| OPT-03 | Оптимизация | 🟢 Низкая | `TokenService.cs` | Двойной FindById в ValidateToken |
| OPT-04 | Оптимизация | 🟡 Средняя | `AuthCommandHandler.cs` | До 3 gRPC вызовов GetUserContacts |
| BUG-01 | Баг | 🟠 Высокая | `UserContext.cs` | IsAuthenticated=false для Service-токенов |
| BUG-02 | Баг | 🟠 Высокая | `RefreshTokensStorage.cs` | DeleteRefreshToken не проверяет userId |
| BUG-03 | Баг | 🟡 Средняя | `XAuthExtensions.cs` | Магические строки для TokenType |
| BUG-04 | Баг | 🟡 Средняя | `ServiceId.cs` | Unknown=0 как значение по умолчанию |
| BUG-05 | Баг | 🟠 Высокая | `RefreshTokensStorage.cs` | ExpiresAt не проверяется при поиске |
| MISC-01 | Code Quality | 🟢 Низкая | `JwtSettings.cs` | Нет required/валидации |
| MISC-02 | Code Quality | 🟢 Низкая | `PasswordHasher.cs` × 2 | Дублирование класса |
| MISC-03 | Code Quality | ⚪ Минимальная | `ServiceId.cs` | Trailing comma в enum |
