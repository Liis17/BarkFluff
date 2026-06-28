# Аудит: BarkFluff.Shared.Identity

> **Область аудита:** `Shared/BarkFluff.Shared.Identity` и всей инфраструктуры идентификации — `JwtService`, `XAuthExtensions`, `TokenRevocationCache`, `UserContext`, `RefreshTokenGenerator`, `PasswordHasher`, `AuthCommandHandler`, `RefreshTokensStorage`.
> **Дата:** 2026-05-06
> **Последняя проверка:** 2026-05-18
> **Статус:** Активный

---

### SEC-04 — Email OTP-код хранится в открытом виде в БД и не имеет TTL

> ⚠️ **Статус (2026-05-18):** Актуальна. Поле LastEmailAuthCode не имеет TTL, после успешной проверки не сбрасывается (см. NEW-S-3).

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

### SEC-06 — `TokenRevocationCache` — in-memory revocation, не работает в multi-instance

> ✅ **Статус (2026-05-18):** Актуальна. TokenRevocationCache по-прежнему ConcurrentDictionary in-memory.

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
// ✅ РЕШЕНИЕ: 
 использовать IDistrubutedCache для Redis
```

---

## Оптимизация

---

### OPT-01 — `TokenRevocationCache.Cleanup()` итерирует весь словарь каждые 5 минут

> ✅ **Статус (2026-05-18):** Актуальна, но приемлема для текущих объёмов. TokenRevocationCleanupService вызывает Cleanup() каждые 5 минут в фоне.

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
   
    

### 

### 

## 

---

### 

### 



---

### 

---

### 

---

## Сводная таблица проблем
