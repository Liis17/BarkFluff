# Аудит проекта: BarkFluff.Users

> **Дата:** 2025  
> **Ветка:** `dev`  
> **Расположение:** `Backend/BarkFluff.Users/`  
> **Порт:** 7001  
> **Автор аудита:** GitHub Copilot (BarkfluffAgent)

---

## Содержание

- [🔴 Безопасность](#-безопасность)
- [🟡 Оптимизация / Производительность](#-оптимизация--производительность)
- [🟠 Баги и недоработки](#-баги-и-недоработки)
- [🔵 Прочее / Качество кода](#-прочее--качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Утечка email пользователя через публичный логинг

**Проблема / Описание:**  
При создании черновика пользователя в `AddDraftUserCommandHandler` email и username пишутся в лог уровня `Information`. Если в production логи агрегируются в Loki/Elasticsearch — email становится виден всем, у кого есть доступ к логам. Email — PII-данные.

**Конкретно в чём проблема:**  
Email пользователя записывается в неструктурированный лог без маскирования.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/AddDraftUser/AddDraftUserCommandHandler.cs` : строки 29–35

```csharp
// ❌ ПРОБЛЕМА: Email пишется в лог в открытом виде
_logger.LogInformation(
    "Создание черновика пользователя. Username: {Username}, Email: {Email}, Имя: {FirstName} {LastName}",
    request.Username,
    request.Email,   // <- PII, не должен логироваться
    request.FirstName,
    request.LastName
);
```

**Варианты решения:**  
1. Маскировать email в логах (показывать только домен или первые 2 символа).  
2. Убрать email из лога совсем, оставить только UserId после создания.

```csharp
// ✅ РЕШЕНИЕ: маскируем email, убираем из логов информативную часть
private static string MaskEmail(string? email)
{
    if (string.IsNullOrEmpty(email)) return "***";
    var at = email.IndexOf('@');
    return at > 1 ? email[..2] + "***" + email[at..] : "***";
}

_logger.LogInformation(
    "Создание черновика пользователя. Username: {Username}, Email: {MaskedEmail}",
    request.Username,
    MaskEmail(request.Email) // <- только маскированный вид
);
```

---

### SEC-02 — Отсутствие валидации длины и формата Username / Email при создании

**Проблема / Описание:**  
`AddDraftUserCommandHandler` и `ChangeUsernameCommandHandler` не проверяют username на допустимые символы (буквы, цифры, подчёркивание), минимальную длину и максимальную длину. Любая строка (включая спецсимволы SQL/HTML, эмодзи, пустые строки из пробелов) пройдёт валидацию — `IsReserved` лишь проверяет зарезервированные слова.

**Конкретно в чём проблема:**  
Можно создать пользователя с username `" "` (пробелы), `"<script>"`, длиной 1 символ или 10 000 символов.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/AddDraftUser/AddDraftUserCommandHandler.cs` : строки 36–92  
**Путь к файлу:** `Backend/BarkFluff.Users/Features/ChangeUsername/ChangeUsernameCommandHandler.cs` : строки 31–60

```csharp
// ❌ ПРОБЛЕМА: нет валидации формата/длины — только проверка зарезервированных имён
if (_reservedUsernamesService.IsReserved(username))
    throw new UsernameReservedException();

// Сразу идёт создание/изменение без проверки формата
var user = await _usersStorage.CreateUser(username, firstName, lastName, email);
```

**Варианты решения:**  
1. Добавить `FluentValidation` или ручную проверку перед обращением к хранилищу.  
2. Использовать Regex для допустимых символов.

```csharp
// ✅ РЕШЕНИЕ: валидация перед сохранением
private static readonly Regex UsernameRegex = 
    new(@"^[a-zA-Z0-9_]{3,32}$", RegexOptions.Compiled);

if (string.IsNullOrWhiteSpace(username) || !UsernameRegex.IsMatch(username))
    throw new UsernameInvalidFormatException(); // новое исключение

if (_reservedUsernamesService.IsReserved(username))
    throw new UsernameReservedException();
```

---

### SEC-03 — Firebase Device Token хранится в открытом виде и логируется

**Проблема / Описание:**  
`SetFirebaseTokenCommandHandler` принимает FCM-токен и сохраняет его в базе данных без каких-либо дополнительных ограничений. Сам токен — секретный идентификатор устройства; при его утечке злоумышленник может использовать его для отправки push-уведомлений от имени сервиса.

**Конкретно в чём проблема:**  
Токен доступен любому кто имеет доступ к БД; нет TTL / ротации токена; нет ограничения на длину входящего значения.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/DevicesStorage.cs` : строки 83–95  
**Путь к файлу:** `Backend/BarkFluff.Users/Domain/UserDevice.cs` : строка 27

```csharp
// ❌ ПРОБЛЕМА: токен хранится как plain-text без ограничений
public async Task SetFirebaseToken(Guid deviceId, long userId, string token)
{
    // нет проверки длины token (FCM-токены ~160 символов, можно вставить 65535)
    device.FirebaseDeviceToken = token; // хранится открыто
    await context.SaveChangesAsync();
}
```

**Варианты решения:**  
1. Ограничить максимальную длину токена на уровне входа (255 символов достаточно).  
2. Добавить `[MaxLength(255)]` на поле в домене + проверку в handler.

```csharp
// ✅ РЕШЕНИЕ: валидация длины токена
public async Task SetFirebaseToken(Guid deviceId, long userId, string token)
{
    // FCM-токены не превышают 256 символов
    if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
        throw new ArgumentException("Недопустимый Firebase токен");

    var device = await context.UserDevices
        .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

    if (device == null)
        throw new InvalidOperationException("Устройство не найдено");

    device.FirebaseDeviceToken = token;
    await context.SaveChangesAsync();
}
```

---

### SEC-04 — `CheckExistEmail` / `CheckExistUsername` доступны анонимно — User Enumeration

**Проблема / Описание:**  
Оба метода в `UsersApiService` помечены `[AllowAnonymous]`. Это позволяет неограниченно проверять существование email и username без авторизации. Злоумышленник может перебором определить, какие email-адреса зарегистрированы на платформе.

**Конкретно в чём проблема:**  
Нет rate-limiting на уровне сервиса. Анонимный клиент может за секунды перебрать миллионы email.

**Путь к файлу:** `Backend/BarkFluff.Users/Host/UsersApiService.cs` : строки 63–76

```csharp
// ❌ ПРОБЛЕМА: анонимный доступ без rate-limit
[AllowAnonymous]
public override Task<CheckExistResponse> CheckExistEmail(CheckExistEmailRequest request, ServerCallContext context)
{
    var command = new CheckExistEmailQuery() { Email = request.Email?.Trim() };
    return _mediator.Send(command);  // нет никакого троттлинга
}
```

**Варианты решения:**  
1. Добавить rate-limiting middleware (например, через `AspNetCoreRateLimit` или кастомный gRPC interceptor).  
2. Рассмотреть полный запрет анонимного доступа — переносить эти проверки только в контекст авторизованной регистрации (через Identity сервис).

```csharp
// ✅ РЕШЕНИЕ: rate-limit interceptor или вынос проверки только в сервисный контекст
// Пример: добавить кастомный interceptor в pipeline
public class RateLimitInterceptor : Interceptor
{
    // Ограничение: не более 10 запросов в минуту с одного IP
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var ip = context.GetHttpContext().Connection.RemoteIpAddress?.ToString();
        if (!_rateLimiter.TryAcquire(ip))
            throw new RpcException(new Status(StatusCode.ResourceExhausted, "Too many requests"));
        return await continuation(request, context);
    }
}
```

---

### SEC-05 — `ExportData` не проверяет принадлежность данных вызывающему сервису

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
1. Передавать в команду идентификатор инициирующего сервиса и логировать его.  
2. Добавить аудит-лог экспорта (кто, когда, какой userId).

```csharp
// ✅ РЕШЕНИЕ: добавить аудит-лог с указанием инициатора
_logger.LogWarning(
    "[GDPR EXPORT] Запрос экспорта данных пользователя {TargetUserId}. " +
    "Инициатор: сервис {CallerService} в {Timestamp}",
    request.UserId,
    request.CallerServiceName, // добавить поле в команду
    DateTime.UtcNow);
```

---

## 🟡 Оптимизация / Производительность

---

### PERF-01 — Двойной SELECT в `SearchUsersByTrigram`: данные + COUNT

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

---

### PERF-02 — `MediatR` зарегистрирован дважды

**Проблема / Описание:**  
В `Program.cs` вызов `builder.Services.AddMediatR(...)` присутствует **два раза** подряд. Это двойная регистрация всех обработчиков в DI-контейнере. В зависимости от версии MediatR это может приводить к дублированию пайплайна или лишней работе при разрешении зависимостей.

**Конкретно в чём проблема:**  
Дублирование регистрации — потенциальная источник трудноотлавливаемых проблем при масштабировании/добавлении pipeline behaviors.

**Путь к файлу:** `Backend/BarkFluff.Users/Program.cs` : строки 36 и 51

```csharp
// ❌ ПРОБЛЕМА: AddMediatR вызывается дважды
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>()); // строка 36
// ... другие регистрации ...
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>()); // строка 51 — дубль
```

**Варианты решения:**  
Удалить один из вызовов — достаточно одного в нужном месте.

```csharp
// ✅ РЕШЕНИЕ: один вызов AddMediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
// Больше не повторять
```

---

### PERF-03 — N+1 запросов в `SearchUsersServerQueryHandler` при загрузке бейджей

**Проблема / Описание:**  
После получения списка пользователей в `SearchUsersServerQueryHandler` для каждого пользователя выполняется **отдельный запрос** в БД для загрузки его бейджей. При размере страницы 50 пользователей — это 51 запрос к базе данных.

**Конкретно в чём проблема:**  
Классическая N+1 проблема при пагинированном поиске.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/SearchUsersServer/SearchUsersServerQueryHandler.cs` : строки 60–94

```csharp
// ❌ ПРОБЛЕМА: для каждого пользователя — отдельный запрос в БД
foreach (var user in users)
{
    var grpcUser = user.ToGrpc();
    var badges = await _usersStorage.GetUserBadgesAsync(user.Id, limit: 3); // запрос в цикле!
    // ...
}
```

**Варианты решения:**  
1. Загружать бейджи одним запросом `WHERE UserId IN (...)` для всего набора пользователей.  
2. Добавить метод `GetBadgesForUsers(IEnumerable<long> userIds)` в `UsersStorage`.

```csharp
// ✅ РЕШЕНИЕ: один батч-запрос для всех пользователей
var userIds = users.Select(u => u.Id).ToList();

// Новый метод в UsersStorage:
public async Task<Dictionary<long, List<UserBadge>>> GetBadgesForUsersAsync(
    List<long> userIds, int limit = 3)
{
    var badges = await _usersContext.UserBadges
        .Include(ub => ub.Badge)
        .Where(ub => userIds.Contains(ub.UserId) && ub.Badge.IsActive)
        .OrderBy(ub => ub.UserId)
        .ThenBy(ub => ub.Priority)
        .ToListAsync();

    return badges
        .GroupBy(ub => ub.UserId)
        .ToDictionary(g => g.Key, g => g.Take(limit).ToList());
}

// Использование:
var badgesByUser = await _usersStorage.GetBadgesForUsersAsync(userIds);
foreach (var user in users)
{
    var grpcUser = user.ToGrpc();
    var badges = badgesByUser.GetValueOrDefault(user.Id, new());
    // ...
}
```

---

### PERF-04 — Повторный gRPC-вызов к MessagesService в `ExportData`

**Проблема / Описание:**  
В `ExportDataCommandHandler` метод `GetUserAllMessagesAsync` вызывается **дважды**: сначала для сериализации сообщений в `messages.json`, затем снова — для сбора `fileIds` вложений. Это два полноценных gRPC round-trip к Messages сервису, при этом данные идентичны.

**Конкретно в чём проблема:**  
Двойная сетевая нагрузка, двойное время ответа при экспорте.

**Путь к файлу:** `Backend/BarkFluff.Users/Features/ExportData/ExportDataCommandHandler.cs` : строки 69 и 138

```csharp
// ❌ ПРОБЛЕМА: два идентичных gRPC-вызова
// Вызов 1 (строка ~69):
var messagesResponse = await _messagesClient.GetUserAllMessagesAsync(
    new GetUserAllMessagesRequest { UserId = request.UserId }, ...);

// ... много кода ...

// Вызов 2 (строка ~138) — те же данные!
var messagesData = await _messagesClient.GetUserAllMessagesAsync(
    new GetUserAllMessagesRequest { UserId = request.UserId }, ...);
```

**Варианты решения:**  
Сохранить результат первого вызова в переменную и переиспользовать его.

```csharp
// ✅ РЕШЕНИЕ: один вызов, результат переиспользуется
GetUserAllMessagesResponse? messagesResponse = null;
try
{
    messagesResponse = await _messagesClient.GetUserAllMessagesAsync(
        new GetUserAllMessagesRequest { UserId = request.UserId },
        cancellationToken: cancellationToken);

    // Сериализация сообщений
    var messagesJson = SerializeMessages(messagesResponse);
    response.Files.Add(new JsonFile { Filename = "messages.json", Content = messagesJson });
}
catch (Exception ex) { /* ... */ }

// Сбор fileIds из уже загруженного messagesResponse (без повторного вызова)
var fileIds = new HashSet<string>();
if (messagesResponse != null)
{
    foreach (var msg in messagesResponse.Messages)
        foreach (var att in msg.Attachments)
        {
            if (!string.IsNullOrEmpty(att.FileId)) fileIds.Add(att.FileId);
            if (!string.IsNullOrEmpty(att.PreviewFileId)) fileIds.Add(att.PreviewFileId);
        }
}
```

---

### PERF-05 — `GetUserByUsername` в `UsersStorage` использует case-insensitive сравнение без индекса

**Проблема / Описание:**  
Метод `GetUserByUsername` применяет `x.Username.ToLower()` прямо в LINQ-запросе. EF Core транслирует это в `LOWER("Username") = LOWER(@username)` — PostgreSQL не может использовать стандартный B-tree индекс по полю `Username`. Аналогично для `GetUserByEmail`.

**Конкретно в чём проблема:**  
Каждый вызов `FindByLogin`, `CheckExistUsername`, `ChangeUsername` выполняет полное сканирование таблицы.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строки 20–33

```csharp
// ❌ ПРОБЛЕМА: LOWER() предотвращает использование индекса
var user = await _usersContext.Users.Include(u => u.Contact)
    .FirstOrDefaultAsync(x => string.Equals(x.Username.ToLower(), username.ToLower()));
//                                           ^^^^^^^^^^^ seq scan вместо index scan
```

**Варианты решения:**  
1. Создать `citext`-колонку в PostgreSQL (case-insensitive text) — используется как обычный индекс.  
2. Создать функциональный индекс `CREATE INDEX idx_users_username_lower ON "Users" (LOWER("Username"))` и использовать `EF.Functions.ILike`.

```csharp
// ✅ РЕШЕНИЕ: использовать ILike (работает с функциональным индексом)
var user = await _usersContext.Users
    .Include(u => u.Contact)
    .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Username, username));

// В миграции добавить:
// migrationBuilder.Sql(@"CREATE INDEX idx_users_username_ci ON ""Users"" (LOWER(""Username""))");
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — `ExecuteSqlRawAsync` возвращает affected rows, а не COUNT(*)

**Проблема / Описание:**  
В методе `SearchUsers` (полнотекстовый поиск) переменная `totalCount` получает значение из `ExecuteSqlRawAsync`, который по контракту возвращает **количество затронутых строк** (всегда 0 для SELECT). В итоге `TotalCount` всегда равен 0, несмотря на выполненный `COUNT(*)`.

**Конкретно в чём проблема:**  
Пагинация на основе `totalCount` из `SearchUsers` (не trigram-версии) будет всегда показывать 0 страниц — критический баг для клиента.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строки 100–107

```csharp
// ❌ БАГ: ExecuteSqlRawAsync возвращает affected rows (0 для SELECT), а не результат COUNT(*)
var totalCount = await _usersContext.Database.ExecuteSqlRawAsync(countSql,
    new NpgsqlParameter("@searchTerm", normalizedSearchTerm));
// totalCount всегда == 0 !
```

**Варианты решения:**  
Использовать `SqlQueryRaw<int>` или перенести COUNT в основной запрос через window function.

```csharp
// ✅ РЕШЕНИЕ: правильное получение COUNT через SqlQuery
var totalCount = await _usersContext.Database
    .SqlQueryRaw<int>(countSql,
        new NpgsqlParameter("@searchTerm", normalizedSearchTerm))
    .FirstOrDefaultAsync();

// Либо лучше — использовать window function (см. PERF-01)
```

---

### BUG-02 — Race condition при создании пользователя: проверка username/email не атомарна

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

### BUG-05 — `GetUserByUsername` в Storage игнорирует `IsDraft` при поиске

**Проблема / Описание:**  
Метод `GetUserByUsername` в `UsersStorage` возвращает пользователя независимо от флага `IsDraft`. Вызывающий код в `UsersServerApiService.GetUserByUsername` делает `if (user.IsDraft)` проверку, но `AddDraftUserCommandHandler` использует тот же метод для проверки занятости username и получает черновиков — это корректно. Однако `FindByLoginQueryHandler` (Identity) получает draft-пользователей, что может приводить к попытке авторизации незавершённой регистрации.

**Конкретно в чём проблема:**  
`GetUserByUsername` возвращает черновых пользователей везде, где используется — поведение неконсистентно и зависит от того, проверяет ли вызывающий код `IsDraft`.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строки 20–26

```csharp
// ❌ НЕДОРАБОТКА: метод не фильтрует по IsDraft — вся логика у вызывающего
public async Task<User?> GetUserByUsername(string username)
{
    var user = await _usersContext.Users.Include(u => u.Contact)
        .FirstOrDefaultAsync(x => string.Equals(x.Username.ToLower(), username.ToLower()));
    return user; // может вернуть черновика
}
```

**Варианты решения:**  
Разделить на два метода с явными названиями или добавить параметр `includeDraft`.

```csharp
// ✅ РЕШЕНИЕ: явное разделение
public Task<User?> GetConfirmedUserByUsername(string username) =>
    _usersContext.Users.Include(u => u.Contact)
        .FirstOrDefaultAsync(x =>
            EF.Functions.ILike(x.Username, username) && !x.IsDraft);

public Task<User?> GetAnyUserByUsername(string username) =>
    _usersContext.Users.Include(u => u.Contact)
        .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Username, username));
```

---

### BUG-06 — ID пользователя генерируется через `UnixTimeSeconds` — конфликт при быстрой регистрации

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

// ✅ РЕШЕНИЕ вариант 2 (лучший): PostgreSQL sequence в миграции
// ALTER TABLE "Users" ALTER COLUMN "Id" ADD GENERATED ALWAYS AS IDENTITY;
// В EF: modelBuilder.Entity<User>().Property(u => u.Id).UseIdentityAlwaysColumn();
```

---

## 🔵 Прочее / Качество кода

---

### CODE-01 — `RegistrationDate` хранится как `DateTime` без временной зоны

**Проблема / Описание:**  
Поле `RegistrationDate` в доменном классе `User` имеет тип `DateTime`. PostgreSQL хранит его как `timestamp without time zone`. При записи `DateTime.UtcNow` теряется информация о часовом поясе. EF Core + Npgsql в новых версиях выбрасывают предупреждение или ошибку при работе с `DateTime` без Kind=Utc.

**Конкретно в чём проблема:**  
Потенциальные проблемы при десериализации (Kind=Unspecified), несовместимость с будущими версиями Npgsql.

**Путь к файлу:** `Backend/BarkFluff.Users/Domain/User.cs` : строка 16  
**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs` : строка 191

```csharp
// ❌ ПРОБЛЕМА: DateTime без явного UTC
public DateTime RegistrationDate { get; set; } // тип без timezone
// ...
RegistrationDate = DateTime.UtcNow, // записывается UtcNow, но Kind теряется при чтении
```

**Варианты решения:**  
Использовать `DateTimeOffset` во всём проекте или настроить Npgsql на работу с UTC.

```csharp
// ✅ РЕШЕНИЕ: использовать DateTimeOffset
public DateTimeOffset RegistrationDate { get; set; }
// ...
RegistrationDate = DateTimeOffset.UtcNow,

// Либо в Program.cs добавить глобально для Npgsql:
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
// (временное решение для совместимости)
```

---

### CODE-02 — Два разных места хранения миграций

**Проблема / Описание:**  
В проекте существуют **две папки миграций**: `Persistence/Migrations/` и `Migrations/` в корне проекта. Это признак того, что в какой-то момент была изменена конфигурация `MigrationsAssembly` или `OutputDir`. Это усложняет понимание порядка применения и обслуживание.

**Конкретно в чём проблема:**  
Неясен хронологический порядок применения миграций из разных папок. Возможна путаница при `dotnet ef migrations add`.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Migrations/` и `Backend/BarkFluff.Users/Migrations/`

```
// ❌ ПРОБЛЕМА: две папки миграций
Backend/BarkFluff.Users/
├── Migrations/                    ← более новые (с 20250720...)
│   ├── 20250720122935_AddPreviewAvatar.cs
│   ├── 20250914111458_AddBadgesTables.cs
│   └── ...
└── Persistence/
    └── Migrations/                ← более старые (с 20250503...)
        ├── 20250503152850_AddUsers.cs
        └── ...
```

**Варианты решения:**  
Объединить все миграции в одну папку `Persistence/Migrations/` и настроить `UsersContextFactory` с явным `MigrationsAssembly`.

```csharp
// ✅ РЕШЕНИЕ: явно указать папку миграций в контексте
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
{
    optionsBuilder.UseNpgsql(connectionString,
        x => x.MigrationsAssembly("BarkFluff.Users")
               .MigrationsHistoryTable("__EFMigrationsHistory")); // одна история
}
```

---

### CODE-03 — `UserInfoQueueSender` зарегистрирован как `Scoped`, но вызывается из `Transient`-хранилища

**Проблема / Описание:**  
`UserInfoQueueSender` зарегистрирован как `Scoped`, а `UsersStorage` — как `Transient`. Однако `UserInfoQueueSender` внедряется напрямую в handlers (тоже Transient через MediatR). Это безопасно, но непоследовательно: часть сервисов Transient, часть Scoped без явного обоснования.

**Конкретно в чём проблема:**  
Непоследовательная регистрация lifetime создаёт риск ошибок при добавлении новых зависимостей.

**Путь к файлу:** `Backend/BarkFluff.Users/Program.cs` : строки 43–47

```csharp
// ❌ НЕПОСЛЕДОВАТЕЛЬНОСТЬ: смешанные lifetime без обоснования
builder.Services.AddTransient<UsersStorage>();       // Transient
builder.Services.AddTransient<DevicesStorage>();     // Transient
builder.Services.AddTransient<PrivacyStorage>();     // Transient
builder.Services.AddTransient<PersonalizationStorage>(); // Transient
builder.Services.AddScoped<UserInfoQueueSender>();   // Scoped — почему?
```

**Варианты решения:**  
Привести к единому lifetime. Для gRPC-сервисов рекомендуется `Scoped` (один scope = один запрос).

```csharp
// ✅ РЕШЕНИЕ: все storage — Scoped (один scope на gRPC-запрос)
builder.Services.AddScoped<UsersStorage>();
builder.Services.AddScoped<DevicesStorage>();
builder.Services.AddScoped<PrivacyStorage>();
builder.Services.AddScoped<PersonalizationStorage>();
builder.Services.AddScoped<UserInfoQueueSender>(); // уже Scoped — согласованно
```

---

### CODE-04 — `UsersServerApiService` принимает Storage напрямую в конструктор, нарушая CQRS

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
Вынести логику `GetUserByUsername` в отдельный `GetUserByUsernameQuery` + `GetUserByUsernameQueryHandler`.

```csharp
// ✅ РЕШЕНИЕ: вся логика — через MediatR
public override Task<GetUserByUsernameResponse> GetUserByUsername(
    GetUserByUsernameRequest request, ServerCallContext context)
{
    // Только делегирование — никакой логики в сервисе
    return _mediator.Send(new GetUserByUsernameQuery { Username = request.Username?.Trim() });
}

// Новый GetUserByUsernameQueryHandler инкапсулирует всю логику приватности
```

---

### CODE-05 — Отсутствует ограничение на количество устройств пользователя

**Проблема / Описание:**  
`RegisterDevice` (upsert по DeviceId) не ограничивает количество устройств одного пользователя. Теоретически один пользователь может зарегистрировать неограниченное количество устройств (каждый раз с новым Guid), засоряя таблицу.

**Конкретно в чём проблема:**  
Нет защиты от накопления мусорных устройств; потенциальная DoS-атака изнутри.

**Путь к файлу:** `Backend/BarkFluff.Users/Persistence/Services/DevicesStorage.cs` : строки 10–45

```csharp
// ❌ НЕДОРАБОТКА: нет лимита на количество устройств
public async Task<UserDevice> RegisterOrUpdateDevice(Guid deviceId, long userId, ...)
{
    var existing = await context.UserDevices
        .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

    if (existing != null) { /* update */ return existing; }

    // Нет проверки: сколько уже устройств у пользователя?
    var device = new UserDevice { ... };
    await context.UserDevices.AddAsync(device);
```

**Варианты решения:**  
Добавить проверку лимита (например, не более 20 устройств на пользователя).

```csharp
// ✅ РЕШЕНИЕ: проверка лимита перед созданием
private const int MaxDevicesPerUser = 20;

public async Task<UserDevice> RegisterOrUpdateDevice(Guid deviceId, long userId, ...)
{
    var existing = await context.UserDevices
        .FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

    if (existing != null) { /* update */ return existing; }

    // Проверяем лимит
    var deviceCount = await context.UserDevices.CountAsync(d => d.UserId == userId);
    if (deviceCount >= MaxDevicesPerUser)
        throw new InvalidOperationException($"Превышен лимит устройств ({MaxDevicesPerUser})");

    var device = new UserDevice { ... };
    await context.UserDevices.AddAsync(device);
    await context.SaveChangesAsync();
    return device;
}
```

---

## Сводная таблица

| ID | Категория | Приоритет | Название |
|----|-----------|-----------|----------|
| SEC-01 | 🔴 Безопасность | Высокий | Утечка email в логах |
| SEC-02 | 🔴 Безопасность | Высокий | Нет валидации формата Username/Email |
| SEC-03 | 🔴 Безопасность | Средний | Firebase Token без ограничений |
| SEC-04 | 🔴 Безопасность | Высокий | User Enumeration через анонимные эндпоинты |
| SEC-05 | 🔴 Безопасность | Средний | ExportData без аудит-лога |
| PERF-01 | 🟡 Оптимизация | Высокий | Двойной SELECT в trigram-поиске |
| PERF-02 | 🟡 Оптимизация | Низкий | AddMediatR вызван дважды |
| PERF-03 | 🟡 Оптимизация | Высокий | N+1 запрос при загрузке бейджей в поиске |
| PERF-04 | 🟡 Оптимизация | Средний | Двойной gRPC-вызов в ExportData |
| PERF-05 | 🟡 Оптимизация | Высокий | Seq scan при поиске по Username/Email |
| BUG-01 | 🟠 Баги | Критичный | ExecuteSqlRawAsync возвращает 0 вместо COUNT |
| BUG-02 | 🟠 Баги | Критичный | Race condition при создании пользователя |
| BUG-03 | 🟠 Баги | Критичный | ChangeUsername не проверяет занятость |
| BUG-04 | 🟠 Баги | Средний | ChangeBio принимает строку из пробелов |
| BUG-05 | 🟠 Баги | Средний | GetUserByUsername не фильтрует IsDraft |
| BUG-06 | 🟠 Баги | Высокий | ID через UnixTimeSeconds — конфликт ключей |
| CODE-01 | 🔵 Качество | Низкий | DateTime без timezone |
| CODE-02 | 🔵 Качество | Низкий | Две папки миграций |
| CODE-03 | 🔵 Качество | Низкий | Непоследовательные lifetime в DI |
| CODE-04 | 🔵 Качество | Средний | Нарушение CQRS в UsersServerApiService |
| CODE-05 | 🔵 Качество | Средний | Нет лимита на количество устройств |
