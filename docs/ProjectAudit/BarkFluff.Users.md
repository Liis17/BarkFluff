# Аудит проекта: BarkFluff.Users

> **Дата создания:** 2025  
> **Последняя проверка актуальности:** 2026-05-18  
> **Ветка:** `dev`  
> **Расположение:** `Backend/BarkFluff.Users/`  
> **Порт:** 7001  
> **Автор аудита:** GitHub Copilot (BarkfluffAgent)

---

## 🔴 Безопасность

---

### 

### SEC-05 — `ExportData` не проверяет принадлежность данных вызывающему сервису

> 🔧 **РЕШЕНИЕ — на уровне nginx (2026-05-27):** блокировать `*ServerApi`-пути снаружи кластера. Код `ExportDataCommandHandler` не меняется — это сервис-к-сервису вызов, осмысленной проверки принадлежности в коде нет.

**Проблема / Описание:**  
`ExportData` принимает произвольный `UserId` из запроса и возвращает все данные этого пользователя (профиль, сообщения, файлы). Метод защищён `TokenType.Service`, но любой авторизованный сервис может запросить данные любого пользователя без дополнительной проверки.

**Конкретно в чём проблема:**  
Нет проверки того, что запрос на экспорт исходит именно от самого пользователя или из доверенного административного контекста.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/ExportData/ExportDataCommandHandler.cs` : строки 35–45

```csharp
// ❌ ПРОБЛЕМА: любой сервис может запросить данные любого userId
var user = await _usersStorage.GetById(request.UserId); // userId из запроса, без проверки
```

**Варианты решения:**  

1. на уровне nginx блокировать запросы к серверным эндпоинтам по grpc путям типа 
   
   /MyService/Method
   
   /UsersApiService/Method
   /UsersServerApiService/Method (там что то такое) (p.s у серверных grpc называется UsersServerApi а у клиентских UsersApi и нужно блокировать по имени *ServerApi)

## 🟡 Оптимизация / Производительность

---

### PERF-01 — Двойной SELECT в `SearchUsersByTrigram`: данные + COUNT

> ✅ **ИСПРАВЛЕНО (2026-05-28):** `SearchUsersByTrigram` переписан на ОДИН запрос с оконной функцией `COUNT(*) OVER()` через `Database.SqlQueryRaw<TrigramSearchRow>` (EF Core 8+, немаппированный DTO) + ручной маппинг DTO→User. Убран второй тяжёлый trigram-скан. Попутно исправлен **BUG-01**: прежний `ExecuteSqlRawAsync` возвращал число затронутых строк (`-1` для SELECT), а не `COUNT(*)`, из-за чего тотал поиска всегда схлопывался в 0. Аналогичный баг в `SearchUsers` (full-text) НЕ трогал — вне рамок PERF-01.

**Проблема / Описание:**  
В `UsersStorage.SearchUsersByTrigram` выполняются **два отдельных тяжёлых запроса** к PostgreSQL с `similarity()`: один за данными, второй за `COUNT(*)`. Оба запроса полностью сканируют таблицу с вычислением trigram-сходства. При большой таблице это удваивает нагрузку.

**Конкретно в чём проблема:**  
`COUNT(*)` с trigramm similarity — полносканный запрос. Он дублирует всю работу первого запроса.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строки 161–178

```csharp
// ❌ ПРОБЛЕМА: два тяжёлых запроса вместо одного
var users = await _usersContext.Users
    .FromSqlRaw(sql, ...) // запрос 1 — данные
    .Include(u => u.Contact)
    .ToListAsync();

var totalCount = await _usersContext.Database.ExecuteSqlRawAsync(countSql, ...); // запрос 2 — COUNT
// ExecuteSqlRawAsync возвращает affected rows, а не COUNT(*) — это ещё и БАГ (см. BUG-01)
```

**Варианты решения:**  

1. Использовать `COUNT(*) OVER()` (window function) в основном запросе — получить и данные, и total за один SELECT.  
2. Кэшировать результаты поиска в Redis на короткое время.

```csharp
// ✅ РЕШЕНИЕ: window function — один запрос
var sql = @"
    SELECT u.""Id"", ..., COUNT(*) OVER() AS ""TotalCount""
    FROM ""Users"" u
    LEFT JOIN ""UserContacts"" uc ON u.""Id"" = uc.""UserId""
    LEFT JOIN ""Privacies"" p ON u.""Id"" = p.""UserId""
    WHERE (similarity(u.""FirstName"", @searchTerm) > @threshold ...)
    AND u.""IsDraft"" = false" + privacyFilter + @"
    ORDER BY GREATEST(...) DESC
    LIMIT @pageSize OFFSET @skip";

// Затем читаем TotalCount из первой строки результата
```

p.s перепроведить правильность такого запроса

---

### 

### BUG-02 — Race condition при создании пользователя: проверка username/email не атомарна

> ✅ **ИСПРАВЛЕНО (2026-05-28):** функциональные индексы из PERF-05 подняты до УНИКАЛЬНЫХ (`CREATE UNIQUE INDEX` на `LOWER("Username")` и `LOWER("Email")`) — регистронезависимая уникальность на уровне БД. В `UsersStorage.CreateUser` добавлена обработка `PostgresException` SqlState `23505`: маппинг на `EmailExistException`/`UsernameExistException` по имени индекса, прочие нарушения (например PK по Id) пробрасываются как есть. ⚠️ Миграция упадёт, если в проде уже есть регистронезависимые дубликаты username/email — нужно вычистить заранее.

**Проблема / Описание:**  
В `AddDraftUserCommandHandler` проверка существования username и email производится отдельными SELECT-запросами, после чего — INSERT. Между проверкой и созданием другой поток может успеть создать пользователя с таким же username/email. Нет транзакции и нет `SELECT FOR UPDATE`.

**Конкретно в чём проблема:**  
При параллельных запросах возможно создание двух пользователей с одинаковым email или username. Это нарушает целостность данных.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/AddDraftUser/AddDraftUserCommandHandler.cs` : строки 42–92  
**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строки 179–200

```csharp
// ❌ БАГ: check-then-act без транзакции — race condition
var userByEmail = await _usersStorage.GetUserByEmail(email);    // SELECT
if (userByEmail != null) throw new EmailExistException();

var userByUsername = await _usersStorage.GetUserByUsername(username); // SELECT
if (userByUsername != null) throw new UsernameExistException();

var user = await _usersStorage.CreateUser(...); // INSERT — слишком поздно
```

**Варианты решения:**  

1. Добавить уникальные индексы на `Username` и `UserContact.Email` на уровне БД — PostgreSQL сам выбросит исключение при дубликате.  
2. Обрабатывать `DbUpdateException` / `PostgresException` с кодом `23505` (unique violation).

```csharp
// ✅ РЕШЕНИЕ: уникальные индексы в OnModelCreating + обработка исключения
// В UsersContext.OnModelCreating:
modelBuilder.Entity<User>()
    .HasIndex(u => u.Username)
    .IsUnique();

modelBuilder.Entity<UserContact>()
    .HasIndex(c => c.Email)
    .IsUnique();

// В UsersStorage.CreateUser — обработка нарушения уникальности:
try
{
    await _usersContext.Users.AddAsync(user);
    await _usersContext.SaveChangesAsync();
}
catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
{
    // Определяем по pg.ConstraintName — email или username
    if (pg.ConstraintName?.Contains("Email") == true)
        throw new EmailExistException();
    throw new UsernameExistException();
}
```

---

### BUG-03 — `ChangeUsername` не проверяет уникальность нового username

> ✅ **ИСПРАВЛЕНО (2026-05-28):** в `ChangeUsernameCommandHandler` перед обновлением добавлена проверка `GetUserByUsername` — если username занят другим пользователем (`Id != текущего`), бросается `UsernameExistException`. Регистронезависимый уникальный индекс из BUG-02 служит страховкой от гонки.

**Проблема / Описание:**  
`ChangeUsernameCommandHandler` проверяет только зарезервированность нового username, но **не проверяет, свободен ли он**. Пользователь может занять username другого пользователя. Защита только на уровне `CreateUser` (и то с race condition — см. BUG-02).

**Конкретно в чём проблема:**  
Метод изменения username не делает `SELECT` на существование — только проверяет зарезервированные имена.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/ChangeUsername/ChangeUsernameCommandHandler.cs` : строки 38–47

```csharp
// ❌ БАГ: нет проверки что username уже занят другим пользователем
if (_reservedUsernamesService.IsReserved(username))
    throw new UsernameReservedException();

// Сразу обновляем — можно украсть username!
await _usersStorage.ChangeUsername(_userContext.UserId, username);
```

**Варианты решения:**  
Добавить проверку существования username перед обновлением (+ уникальный индекс из BUG-02 как страховку).

```csharp
// ✅ РЕШЕНИЕ: проверить что username свободен
if (_reservedUsernamesService.IsReserved(username))
    throw new UsernameReservedException();

var existingUser = await _usersStorage.GetUserByUsername(username);
if (existingUser != null && existingUser.Id != _userContext.UserId)
    throw new UsernameExistException(); // существующее или новое исключение

await _usersStorage.ChangeUsername(_userContext.UserId, username);
```

---

### BUG-04 — `ChangeBio` не делает `Trim()` — Bio может содержать только пробелы

> ✅ **ИСПРАВЛЕНО (2026-05-28):** в `UsersApiService.ChangeBio` значение нормализуется — `string.IsNullOrWhiteSpace(request.Bio) ? string.Empty : request.Bio.Trim()`. Bio из одних пробелов → пустая строка (очистка), пробелы по краям срезаются. Проверка длины в хендлере работает по обрезанному значению. ⚠️ В аудите предлагался `null`, но Bio в проекте всегда был непустой строкой (`ToGrpc` делает `?? string.Empty`), поэтому нормализуем в `string.Empty` — фикс без рискованного null-каскада в RabbitMQ-событие и контракт.

**Проблема / Описание:**  
В `ChangeBioCommandHandler` значение `request.Bio` передаётся в хранилище **без `Trim()`**. Пользователь может установить bio из 200 пробелов — валидация длины пройдёт, но значение семантически пустое. Также в `UsersApiService` при вызове `ChangeBio` не делается trim в отличие от `ChangeName` и `ChangeUsername`.

**Конкретно в чём проблема:**  
Bio `"   "` (пробелы) пройдёт проверку длины и сохранится как не-null.

**Путь к файлу:** `Backend/BarkFluff.Users/Host/UsersApiService.cs` : строка 113  
**Путь к файлу:** `Backend/BarkFluff.Users/Features/ChangeBio/ChangeBioCommandHandler.cs` : строка 44

```csharp
// ❌ БАГ: нет Trim() в отличие от ChangeName/ChangeUsername
var command = new ChangeBioCommand() { Bio = request.Bio }; // без .Trim()
// ...
await _usersStorage.ChangeBio(_userContext.UserId, request.Bio); // пробелы сохраняются
```

**Варианты решения:**  
Добавить `Trim()` и проверку на whitespace.

```csharp
// ✅ РЕШЕНИЕ: trim + проверка на пустую строку
var bio = string.IsNullOrWhiteSpace(request.Bio) ? null : request.Bio.Trim();
var command = new ChangeBioCommand() { Bio = bio };

// В handler:
if (request.Bio != null && request.Bio.Trim().Length > 200)
    throw new BioTooLongException();
await _usersStorage.ChangeBio(_userContext.UserId, request.Bio?.Trim());
```

---

### 

### BUG-06 — ID пользователя генерируется через `UnixTimeSeconds` — конфликт при быстрой регистрации

> ✅ **ИСПРАВЛЕНО (2026-05-28):** `UsersStorage.CreateUser` использует `ToUnixTimeMilliseconds()` вместо `ToUnixTimeSeconds()` — ×1000 ниже шанс коллизии PK, сохраняется свойство «Id ≈ время регистрации» (важно для `OrderByDescending(u => u.Id)`). PK-коллизия, если всё же случится, ловится как `23505` в `CreateUser` и пробрасывается (не маскируется под Email/Username conflict).

**Проблема / Описание:**  
ID пользователя задаётся как `DateTimeOffset.UtcNow.ToUnixTimeSeconds()` — целое число секунд. При одновременной регистрации двух пользователей в одну секунду произойдёт `DbUpdateException` из-за конфликта Primary Key.

**Конкретно в чём проблема:**  
Не уникальная генерация ID при нагрузке > 1 регистрации/сек.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строки 183–186

```csharp
// ❌ БАГ: Id не уникален при параллельных регистрациях
var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // точность: 1 секунда
var user = new User
{
    Id = unixTimestamp, // два пользователя в одну секунду → PK constraint violation
```

**Варианты решения:**  

1. Использовать `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` (миллисекунды) для снижения вероятности.  
2. Использовать `SEQUENCE` в PostgreSQL с `GENERATED ALWAYS AS IDENTITY` — надёжный вариант.  
3. Использовать Snowflake ID / ULIDGen для распределённо-уникальных числовых ID.

```csharp
// ✅ РЕШЕНИЕ вариант 1: миллисекунды + retry при коллизии
var id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
var user = new User { Id = id, ... };
---
```

## 🔵 Прочее / Качество кода

---

### CODE-04 — `UsersServerApiService` принимает Storage напрямую в конструктор, нарушая CQRS

> ✅ **ИСПРАВЛЕНО (2026-05-28):** `GetUserByUsername` вынесен в Feature `Features/GetUserByUsername/` (Query + Handler). Из конструктора `UsersServerApiService` убраны все 4 прямые зависимости (`UsersStorage`, `PrivacyStorage`, `PersonalizationStorage`, `FilesServerApiClient`) — остались только `IMediator` и `MetricsCollector`. Метрика `public_profile_views` осталась на входе сервиса, остальные (`public_profile_not_found/hidden`, `files_fetch_*`) перенесены в хендлер.

**Проблема / Описание:**  
`UsersServerApiService` имеет прямые зависимости на `UsersStorage`, `PrivacyStorage`, `PersonalizationStorage` и `FilesServerApiClient`, используя их **напрямую** для реализации `GetUserByUsername`. Остальные методы идут через MediatR. Это нарушает единообразие архитектуры CQRS: часть логики в handler'ах, часть — в gRPC-сервисе.

**Конкретно в чём проблема:**  
Бизнес-логика (применение правил приватности, получение постера) размазана между сервисным слоем и обработчиками запросов.

**Путь к файлу:** `Backend/BarkFluff.Users/Host/UsersServerApiService.cs` : строки 43–60, 280–345

```csharp
// ❌ ПРОБЛЕМА: прямые зависимости на Storage в gRPC-сервисе
public UsersServerApiService(
    IMediator mediator,
    UsersStorage _usersStorage,       // ← Storage напрямую
    PrivacyStorage _privacyStorage,   // ← Storage напрямую
    PersonalizationStorage _personalizationStorage, // ← Storage напрямую
    FilesServerApi.FilesServerApiClient _filesClient, // ← gRPC клиент напрямую
    MetricsCollector metrics) { ... }
```

**Варианты решения:**
---

метод GetUserByUsername в UsersServerApiService переделать в Feature как и все остальные фичи уже сделаны

# 
