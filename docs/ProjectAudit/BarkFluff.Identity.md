# Аудит проекта: BarkFluff.Identity

> **Дата аудита:** 2025  
> **Проект:** `Backend/BarkFluff.Identity`  
> **Target Framework:** `net9.0`  
> **Аудитор:** GitHub Copilot (BarkfluffAgent)

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

### 

### 

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
//заменить на нормальный генератор
}
```

---

### 

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
    ExpiresAt = DateTime.UtcNow.AddMinutes(5), // 5 минут
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
// ✅ РЕШЕНИЕ: добавить индекс , сделай миграцию (важно)

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
