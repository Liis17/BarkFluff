# Аудит проекта: Barkfluff.Developers

> **Дата аудита:** 2025  
> **Ветка:** `dev`  
> **Путь к проекту:** `Backend/Barkfluff.Developers/`  
> **Стек:** .NET 9 · gRPC / Grpc.AspNetCore.Web · MediatR · EF Core 9 · PostgreSQL (Npgsql)

---

## Содержание

1. [🔴 Безопасность](#-безопасность)
2. [🟡 Производительность и оптимизация](#-производительность-и-оптимизация)
3. [🟠 Баги и недоработки](#-баги-и-недоработки)
4. [🔵 Архитектура и качество кода](#-архитектура-и-качество-кода)

---

## 🔴 Безопасность

---

### SEC-01 — Hardcoded credentials в Design-Time фабрике

**Описание:**  
`DevelopersContextFactory` содержит хардкод строки подключения с логином и паролем `postgres`. Файл попадает в git-историю, что является прямой утечкой учётных данных.

**Файл:** `Backend/Barkfluff.Developers/Persistence/DevelopersContextFactory.cs` : строки 10–14

```csharp
// ❌ Пароль захардкожен прямо в коде и попадает в репозиторий
public DevelopersContext CreateDbContext(string[] args)
{
    var options = new DbContextOptionsBuilder<DevelopersContext>()
        .UseNpgsql("Host=localhost;Database=developers;Username=postgres;Password=postgres")
        .Options;

    return new DevelopersContext(options);
}
```

**Варианты решения:**  
Читать строку подключения из переменной окружения или `user-secrets` при design-time:

```csharp
// ✅ Строка подключения берётся из переменной окружения или конфигурации
public DevelopersContext CreateDbContext(string[] args)
{
    var configuration = new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .AddUserSecrets<DevelopersContextFactory>(optional: true)
        .Build();

    var connectionString = configuration["DevelopersDb"]
        ?? throw new InvalidOperationException(
            "Connection string 'DevelopersDb' not found. " +
            "Set env variable or user-secrets.");

    var options = new DbContextOptionsBuilder<DevelopersContext>()
        .UseNpgsql(connectionString)
        .Options;

    return new DevelopersContext(options);
}
```

---

### SEC-02 — Отсутствие авторизации администратора для мутационных операций

**Описание:**  
Весь `DevelopersApiService` защищён политикой `TokenType.User`, то есть любой зарегистрированный пользователь теоретически может вызвать команды `CreateSection`, `UpdateSection`, `DeleteSection` (если они когда-либо будут подключены к API). Нет разграничения прав между обычным пользователем и администратором.

**Файл:** `Backend/Barkfluff.Developers/Host/DevelopersApiService.cs` : строки 17–18

```csharp
// ❌ Вся служба защищена только токеном пользователя — нет Admin-политики
[Authorize(Policy = nameof(TokenType.User))]
public class DevelopersApiService : DevelopersApi.DevelopersApiBase
```

**Варианты решения:**  
Вынести мутационные методы под отдельный атрибут `[Authorize(Policy = nameof(TokenType.Admin))]`:

```csharp
// ✅ Чтение доступно всем авторизованным, запись — только администраторам
[Authorize(Policy = nameof(TokenType.User))]
public class DevelopersApiService : DevelopersApi.DevelopersApiBase
{
    // GET-методы остаются без изменений...

    [Authorize(Policy = nameof(TokenType.Admin))] // только Admin
    public override async Task<DocumentationSection> CreateDocumentationSection(
        CreateSectionRequest request, ServerCallContext context)
    {
        return await _mediator.Send(
            new CreateSectionCommand { Key = request.Key, /* ... */ },
            context.CancellationToken);
    }
}
```

---

### SEC-03 — Полностью открытая CORS-политика

**Описание:**  
CORS настроен с `AllowAnyOrigin()` + `AllowAnyMethod()` + `AllowAnyHeader()`. Это допустимо только на этапе разработки. В production любой домен может делать кросс-доменные запросы к API.

**Файл:** `Backend/Barkfluff.Developers/Program.cs` : строки 47–53

```csharp
// ❌ AllowAnyOrigin в production — критическая уязвимость
builder.Services.AddCors(o => o.AddPolicy("DevelopersCors", p =>
{
    p.AllowAnyOrigin()
     .AllowAnyMethod()
     .AllowAnyHeader()
     .WithExposedHeaders("grpc-status", "grpc-message", "grpc-status-details-bin", "x-error-code");
}));
```

**Варианты решения:**  
Ограничить источники через конфигурацию:

```csharp
// ✅ Разрешённые origins берутся из конфигурации
builder.Services.AddCors(o => o.AddPolicy("DevelopersCors", p =>
{
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [];

    p.WithOrigins(allowedOrigins)
     .AllowAnyMethod()
     .AllowAnyHeader()
     .WithExposedHeaders("grpc-status", "grpc-message", "grpc-status-details-bin", "x-error-code");
}));
```

```json
// appsettings.json — добавить секцию:
{
  "Cors": {
    "AllowedOrigins": ["https://app.barkfluff.io", "https://developers.barkfluff.io"]
  }
}
```

---

### SEC-04 — Небезопасное создание экземпляров исключений через рефлексию без проверки

**Описание:**  
`ErrorCodeSeeder` использует `Activator.CreateInstance(type)!` с null-forgiving оператором `!`. Если какой-либо тип исключения требует параметры конструктора или вернёт `null` — произойдёт `NullReferenceException` при старте, а не graceful error. Дополнительно: рефлексия работает над всей сборкой — при добавлении нового типа с нестандартным конструктором это сломает запуск сервиса.

**Файл:** `Backend/Barkfluff.Developers/Infrastructure/ErrorCodeSeeder.cs` : строки 21–24

```csharp
// ❌ Null-forgiving + нет защиты от исключений при создании экземпляра
foreach (var type in exceptionTypes)
{
    var instance = (BaseGrpcException)Activator.CreateInstance(type)!;
```

**Варианты решения:**  
Добавить безопасное создание с пропуском проблемных типов:

```csharp
// ✅ Безопасное создание через try/catch, проблемные типы пропускаются с логом
foreach (var type in exceptionTypes)
{
    BaseGrpcException instance;
    try
    {
        instance = (BaseGrpcException)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException($"Activator returned null for {type.Name}"));
    }
    catch (Exception ex)
    {
        // логируем и пропускаем тип, у которого нет безпараметрного конструктора
        Console.Error.WriteLine($"[ErrorCodeSeeder] Skipped {type.Name}: {ex.Message}");
        continue;
    }

    // ... добавление entry
}
```

---

## 🟡 Производительность и оптимизация

---

### OPT-01 — GetAllSections загружает тяжёлое поле Content при листинге

**Описание:**  
`GetDocumentationSectionsQuery` запрашивает все поля, включая `Content` (тип `jsonb`), которое содержит большой JSON-блок с контентом секции. При отображении списка секций (навигация, оглавление) `Content` не нужен — это лишний трафик между БД и сервисом.

**Файл:** `Backend/Barkfluff.Developers/Persistence/Services/DocumentationStorage.cs` : строки 18–21

```csharp
// ❌ Загружается Content (большой JSONB) при каждом вызове GetAll
public async Task<List<DocumentationSection>> GetAllAsync()
{
    return await _context.DocumentationSections
        .OrderBy(s => s.Order)
        .ToListAsync();   // SELECT * — включая Content
}
```

**Варианты решения:**  
Создать отдельный DTO/проекцию без `Content` для листинга:

```csharp
// ✅ Проекция: Content не загружается, экономия сети и памяти
public async Task<List<DocumentationSectionSummary>> GetAllSummariesAsync()
{
    return await _context.DocumentationSections
        .OrderBy(s => s.Order)
        .Select(s => new DocumentationSectionSummary
        {
            Key     = s.Key,
            Title   = s.Title,
            Type    = s.Type,
            Order   = s.Order
        })
        .ToListAsync();
}

// Новый record-DTO (или отдельный класс):
public record DocumentationSectionSummary(
    string Key, string Title, string Type, int Order);
```

---

### OPT-02 — Отсутствие индекса по полю Order

**Описание:**  
`DocumentationSections` и `ProtoMetadata` сортируются по полю `Order` при каждом `GetAll`-запросе. В модели есть только уникальный индекс по `Key` / `FileName`, но не по `Order`. При росте данных сортировка без индекса деградирует в `SeqScan + Sort`.

**Файл:** `Backend/Barkfluff.Developers/Persistence/Contexts/DevelopersContext.cs` : строки 16–28, 30–42

```csharp
// ❌ Нет индекса по Order — сортировка без поддержки индекса
modelBuilder.Entity<DocumentationSection>(e =>
{
    // ...
    e.HasIndex(x => x.Key).IsUnique(); // только по Key
    // ORDER BY order — seq scan
});
```

**Варианты решения:**  
Добавить индекс по `Order` в `OnModelCreating`:

```csharp
// ✅ Индекс ускорит ORDER BY order при любом размере таблицы
modelBuilder.Entity<DocumentationSection>(e =>
{
    // ...
    e.HasIndex(x => x.Key).IsUnique();
    e.HasIndex(x => x.Order); // ← добавить
});

modelBuilder.Entity<ProtoMetadata>(e =>
{
    // ...
    e.HasIndex(x => x.FileName).IsUnique();
    e.HasIndex(x => x.Order); // ← добавить
});
```

Затем сгенерировать новую миграцию: `dotnet ef migrations add AddOrderIndexes`

---

### OPT-03 — Блокирующий вызов async-методов при старте (.GetAwaiter().GetResult())

**Описание:**  
Три операции seeding при запуске вызываются через `.GetAwaiter().GetResult()` — это синхронная блокировка пула потоков. При медленном PostgreSQL (cold start, Docker) это может вызвать deadlock в thread pool или значительно увеличить время старта.

**Файл:** `Backend/Barkfluff.Developers/Program.cs` : строки 61–70

```csharp
// ❌ .GetAwaiter().GetResult() блокирует поток, рискует deadlock
seeder.SeedIfNeeded(ctx).GetAwaiter().GetResult();
docStorage.SeedIfNeeded().GetAwaiter().GetResult();
protoStorage.SeedIfNeeded().GetAwaiter().GetResult();
```

**Варианты решения:**  
Использовать `IHostedService` или `app.Lifetime` для async-инициализации:

```csharp
// ✅ Вариант 1: через IHostedService (рекомендуется)
public class DatabaseInitializer(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var ctx     = scope.ServiceProvider.GetRequiredService<DevelopersContext>();
        var seeder  = scope.ServiceProvider.GetRequiredService<ErrorCodeSeeder>();
        var docSt   = scope.ServiceProvider.GetRequiredService<DocumentationStorage>();
        var protoSt = scope.ServiceProvider.GetRequiredService<ProtoMetadataStorage>();

        await ctx.Database.MigrateAsync(ct);
        await seeder.SeedIfNeeded(ctx);
        await docSt.SeedIfNeeded();
        await protoSt.SeedIfNeeded();
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

// В Program.cs:
builder.Services.AddHostedService<DatabaseInitializer>();
// Удалить блок using(scope) с GetAwaiter().GetResult()
```

---

### OPT-04 — GetErrorCodesQuery: нет сортировки при выдаче кодов ошибок

**Описание:**  
`GetErrorCodesQuery` возвращает коды ошибок без какого-либо `OrderBy`. Порядок зависит от реализации БД и может меняться между запросами — это затрудняет стабильное отображение документации.

**Файл:** `Backend/Barkfluff.Developers/Features/GetErrorCodes/GetErrorCodesQuery.cs` : строка 21

```csharp
// ❌ Нет ORDER BY — порядок не детерминирован
var codes = await _context.ErrorCodes.ToListAsync(cancellationToken);
```

**Варианты решения:**

```csharp
// ✅ Стабильный порядок: сначала по Domain, затем по Code
var codes = await _context.ErrorCodes
    .OrderBy(c => c.Domain)
    .ThenBy(c => c.Code)
    .ToListAsync(cancellationToken);
```

---

## 🟠 Баги и недоработки

---

### BUG-01 — CreateSection и UpdateSection: отсутствует валидация входных данных

**Описание:**  
`CreateSectionCommand` и `UpdateSectionCommand` не валидируют поля. Можно создать секцию с пустым `Key`, пустым `Title`, невалидным `Content` (битый JSON). Уникальный индекс по `Key` в БД выбросит `DbUpdateException` вместо понятной gRPC-ошибки `InvalidArgument`.

**Файл:** `Backend/Barkfluff.Developers/Features/CreateSection/CreateSectionCommand.cs` : строки 25–38

```csharp
// ❌ Нет проверки: Key может быть "", Content — невалидным JSON
public async Task<DocumentationSection> Handle(CreateSectionCommand request, CancellationToken cancellationToken)
{
    var section = new Domain.DocumentationSection
    {
        Id      = Guid.NewGuid(),
        Key     = request.Key,     // пустая строка?
        Title   = request.Title,   // пустая строка?
        Content = request.Content  // невалидный JSON?
    };
    var created = await _storage.CreateAsync(section); // ← DbUpdateException если Key уже есть
```

**Варианты решения:**  
Добавить валидацию в начало `Handle` или через MediatR Pipeline Behavior:

```csharp
// ✅ Явная валидация с понятными gRPC-ошибками
public async Task<DocumentationSection> Handle(CreateSectionCommand request, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(request.Key))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "Key cannot be empty"));

    if (string.IsNullOrWhiteSpace(request.Title))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "Title cannot be empty"));

    if (string.IsNullOrWhiteSpace(request.Content))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "Content cannot be empty"));

    // Проверка на дубликат ключа перед вставкой:
    var exists = await _storage.GetByKeyAsync(request.Key);
    if (exists != null)
        throw new RpcException(new Status(StatusCode.AlreadyExists, $"Section '{request.Key}' already exists"));

    var section = new Domain.DocumentationSection { /* ... */ };
    var created = await _storage.CreateAsync(section);
    return MapToProto(created);
}
```

---

### BUG-02 — CancellationToken не передаётся в Storage-методы

**Описание:**  
Все handler'ы получают `CancellationToken cancellationToken` от MediatR/gRPC, однако ни один метод `DocumentationStorage` и `ProtoMetadataStorage` не принимает `CancellationToken`. Токен отмены фактически игнорируется — при разрыве соединения клиентом запрос к БД продолжается до завершения.

**Файл:** `Backend/Barkfluff.Developers/Persistence/Services/DocumentationStorage.cs` : строки 18–22, 25–28

```csharp
// ❌ CancellationToken не принимается и не передаётся в ToListAsync / FirstOrDefaultAsync
public async Task<List<DocumentationSection>> GetAllAsync()
{
    return await _context.DocumentationSections
        .OrderBy(s => s.Order)
        .ToListAsync(); // ← нет cancellationToken
}

public async Task<DocumentationSection?> GetByKeyAsync(string key)
{
    return await _context.DocumentationSections
        .FirstOrDefaultAsync(s => s.Key == key); // ← нет cancellationToken
}
```

**Варианты решения:**  
Добавить `CancellationToken` во все публичные async-методы Storage:

```csharp
// ✅ CancellationToken пробрасывается до уровня EF Core
public async Task<List<DocumentationSection>> GetAllAsync(CancellationToken ct = default)
{
    return await _context.DocumentationSections
        .OrderBy(s => s.Order)
        .ToListAsync(ct); // ← передаём токен
}

public async Task<DocumentationSection?> GetByKeyAsync(string key, CancellationToken ct = default)
{
    return await _context.DocumentationSections
        .FirstOrDefaultAsync(s => s.Key == key, ct);
}

// В Handler'е:
var sections = await _storage.GetAllAsync(cancellationToken); // ← пробрасываем
```

---

### BUG-03 — Команды CreateSection/UpdateSection/DeleteSection не подключены к gRPC API

**Описание:**  
В проекте существуют три полноценных MediatR handler'а: `CreateSectionCommandHandler`, `UpdateSectionCommandHandler`, `DeleteSectionCommandHandler`. Однако в `DevelopersApiService` нет ни одного переопределённого gRPC-метода для мутаций. Функционал создания, редактирования и удаления документации **мёртв** — недостижим через API. Это либо незавершённая фича, либо команды удалены из proto-контракта но не из кода.

**Файл:** `Backend/Barkfluff.Developers/Host/DevelopersApiService.cs` : весь файл

```csharp
// ❌ Только 5 read-only методов — мутационные override'ы отсутствуют
public class DevelopersApiService : DevelopersApi.DevelopersApiBase
{
    public override async Task<GetDocumentationSectionsResponse> GetDocumentationSections(...) { }
    public override async Task<DocumentationSection> GetDocumentationSection(...) { }
    public override async Task<GetProtoFilesResponse> GetProtoFiles(...) { }
    public override async Task<GetProtoFileContentResponse> GetProtoFileContent(...) { }
    public override async Task<GetErrorCodesResponse> GetErrorCodes(...) { }
    // CreateDocumentationSection — НЕТ
    // UpdateDocumentationSection — НЕТ
    // DeleteDocumentationSection — НЕТ
}
```

**Варианты решения:**  
Либо добавить методы в сервис (если они есть в proto-контракте), либо удалить неиспользуемые Command-файлы:

```csharp
// ✅ Вариант A — подключить методы к API (если есть в proto):
[Authorize(Policy = nameof(TokenType.Admin))]
public override async Task<DocumentationSection> CreateDocumentationSection(
    CreateSectionRequest request, ServerCallContext context)
{
    return await _mediator.Send(new CreateSectionCommand
    {
        Key     = request.Key,
        Title   = request.Title,
        Type    = request.Type,
        Order   = request.Order,
        Content = request.Content
    }, context.CancellationToken);
}

// ✅ Вариант B — если функционал не нужен, удалить мёртвый код:
// Удалить файлы:
// Features/CreateSection/CreateSectionCommand.cs
// Features/UpdateSection/UpdateSectionCommand.cs
// Features/DeleteSection/DeleteSectionCommand.cs
```

---

### BUG-04 — Race condition при seeding при нескольких репликах

**Описание:**  
`SeedIfNeeded()` во всех трёх Storage/Seeder проверяет `AnyAsync()` и затем вставляет данные. При горизонтальном масштабировании (2+ replicas) оба экземпляра могут одновременно пройти проверку `AnyAsync() == false` и оба попытаются вставить данные — уникальный индекс выбросит исключение на одном из них.

**Файл:** `Backend/Barkfluff.Developers/Persistence/Services/DocumentationStorage.cs` : строки 63–68

```csharp
// ❌ TOCTOU: два инстанса могут одновременно пройти проверку
public async Task SeedIfNeeded()
{
    if (await _context.DocumentationSections.AnyAsync()) return; // ← оба видят false
    var sections = SeedData.GetSeedSections();
    _context.DocumentationSections.AddRange(sections); // ← оба пытаются вставить
    await _context.SaveChangesAsync(); // ← второй получит DbUpdateException
}
```

**Варианты решения:**  
Обернуть seeding в `try/catch` для `DbUpdateException`, или использовать `INSERT ... ON CONFLICT DO NOTHING` через `ExecuteSqlRaw`:

```csharp
// ✅ Обработка конкурентной вставки — второй инстанс тихо игнорирует конфликт
public async Task SeedIfNeeded()
{
    if (await _context.DocumentationSections.AnyAsync()) return;

    try
    {
        var sections = SeedData.GetSeedSections();
        _context.DocumentationSections.AddRange(sections);
        await _context.SaveChangesAsync();
    }
    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate key") == true)
    {
        // Другой инстанс успел первым — это нормально, игнорируем
    }
}
```

---

### BUG-05 — GetProtoFileContent не валидирует имя файла

**Описание:**  
`GetProtoFileContentQuery.FileName` передаётся напрямую в `_protoProvider.GetContent(request.FileName)` без какой-либо проверки. Хотя `ProtoFileProvider` работает с in-memory словарём (path traversal невозможен), пустое `FileName` или `FileName` с пробелами вернут `null` → `RpcException(NotFound)` вместо `InvalidArgument`. Это затрудняет отладку на стороне клиента.

**Файл:** `Backend/Barkfluff.Developers/Features/GetProtoFileContent/GetProtoFileContentQuery.cs` : строки 26–29

```csharp
// ❌ Пустое или некорректное FileName даёт NotFound вместо InvalidArgument
var content = _protoProvider.GetContent(request.FileName)
    ?? throw new RpcException(new Status(StatusCode.NotFound,
        $"Proto file '{request.FileName}' not found"));
```

**Варианты решения:**

```csharp
// ✅ Явная валидация формата имени файла
if (string.IsNullOrWhiteSpace(request.FileName))
    throw new RpcException(new Status(StatusCode.InvalidArgument, "FileName cannot be empty"));

// Разрешаем только имена файлов без пути (защита от path traversal на уровне протокола)
if (request.FileName.Contains('/') || request.FileName.Contains('\\'))
    throw new RpcException(new Status(StatusCode.InvalidArgument, "FileName must not contain path separators"));

var content = _protoProvider.GetContent(request.FileName)
    ?? throw new RpcException(new Status(StatusCode.NotFound,
        $"Proto file '{request.FileName}' not found"));
```

---

## 🔵 Архитектура и качество кода

---

### ARCH-01 — Нарушение слоистости: GetErrorCodesQuery обращается к DbContext напрямую

**Описание:**  
Все остальные Query/Command handler'ы работают через Storage-слой (`DocumentationStorage`, `ProtoMetadataStorage`), однако `GetErrorCodesQueryHandler` инжектирует `DevelopersContext` напрямую. Нарушается консистентность архитектуры: логика доступа к данным рассеяна между Storage-сервисами и обработчиками.

**Файл:** `Backend/Barkfluff.Developers/Features/GetErrorCodes/GetErrorCodesQuery.cs` : строки 12–13

```csharp
// ❌ Прямой доступ к DbContext в handler — нарушение слоистости
public class GetErrorCodesQueryHandler : IRequestHandler<GetErrorCodesQuery, GetErrorCodesResponse>
{
    private readonly DevelopersContext _context; // ← должен быть ErrorCodeStorage
```

**Варианты решения:**  
Создать `ErrorCodeStorage` по аналогии с другими Storage-сервисами:

```csharp
// ✅ ErrorCodeStorage — единое место доступа к данным об ошибках
public class ErrorCodeStorage(DevelopersContext context)
{
    public async Task<List<ErrorCodeEntry>> GetAllAsync(CancellationToken ct = default)
        => await context.ErrorCodes
            .OrderBy(c => c.Domain).ThenBy(c => c.Code)
            .ToListAsync(ct);
}

// Handler использует Storage, а не Context напрямую:
public class GetErrorCodesQueryHandler(ErrorCodeStorage storage)
    : IRequestHandler<GetErrorCodesQuery, GetErrorCodesResponse>
{
    public async Task<GetErrorCodesResponse> Handle(GetErrorCodesQuery request, CancellationToken ct)
    {
        var codes = await storage.GetAllAsync(ct);
        // ...
    }
}
```

---

### ARCH-02 — DocumentationStorage и ProtoMetadataStorage зарегистрированы как Transient

**Описание:**  
`AddTransient<DocumentationStorage>()` и `AddTransient<ProtoMetadataStorage>()` означают, что при каждом разрешении зависимости создаётся новый экземпляр. Поскольку `DevelopersContext` зарегистрирован как `Scoped` (стандарт для `AddDbContext`), а Storage — как `Transient`, несколько `Transient`-объектов в одном scope разделяют один Context — это корректно. Однако регистрация `Transient` без необходимости добавляет накладные расходы на аллокацию. Более подходящий lifetime — `Scoped`.

**Файл:** `Backend/Barkfluff.Developers/Program.cs` : строки 43–44

```csharp
// ⚠️ Transient избыточен — Storage не имеет состояния, достаточно Scoped
builder.Services.AddTransient<DocumentationStorage>();
builder.Services.AddTransient<ProtoMetadataStorage>();
```

**Варианты решения:**

```csharp
// ✅ Scoped совпадает с lifetime DbContext и семантически точнее
builder.Services.AddScoped<DocumentationStorage>();
builder.Services.AddScoped<ProtoMetadataStorage>();
builder.Services.AddScoped<ErrorCodeStorage>(); // ← новый storage из ARCH-01
```

---

### ARCH-03 — Свойства Domain-моделей имеют дефолтное значение DateTime.UtcNow в объявлении

**Описание:**  
В `DocumentationSection` и `ProtoMetadata` поля `CreatedAt` и `UpdatedAt` инициализируются через `= DateTime.UtcNow` прямо в теле класса. Это означает, что время фиксируется в момент создания C#-объекта, а не в момент записи в БД. При bulk-операциях или отложенной вставке время будет некорректным. Лучше управлять этим на уровне БД или EF Core interceptor'а.

**Файл:** `Backend/Barkfluff.Developers/Domain/DocumentationSection.cs` : строки 11–12

```csharp
// ⚠️ Время фиксируется при создании объекта, а не при коммите транзакции
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
```

**Варианты решения:**  
Использовать EF Core `SaveChangesInterceptor` для автоматической установки временных меток:

```csharp
// ✅ Interceptor управляет временными метками централизованно
public class TimestampInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyTimestamps(eventData.Context!);
        return base.SavingChanges(eventData, result);
    }

    private static void ApplyTimestamps(DbContext ctx)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ctx.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
                entry.Property("CreatedAt").CurrentValue = now;
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property("UpdatedAt").CurrentValue = now;
        }
    }
}

// В DevelopersContext:
protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    => optionsBuilder.AddInterceptors(new TimestampInterceptor());
```

---

## Сводная таблица

| ID | Категория | Приоритет | Название |
|---|---|---|---|
| SEC-01 | 🔴 Безопасность | Критический | Hardcoded credentials в DevelopersContextFactory |
| SEC-02 | 🔴 Безопасность | Высокий | Нет Admin-политики для мутационных операций |
| SEC-03 | 🔴 Безопасность | Высокий | Полностью открытая CORS-политика |
| SEC-04 | 🔴 Безопасность | Средний | Небезопасное создание исключений через рефлексию |
| OPT-01 | 🟡 Оптимизация | Высокий | GetAllSections загружает тяжёлое поле Content |
| OPT-02 | 🟡 Оптимизация | Средний | Отсутствие индекса по полю Order |
| OPT-03 | 🟡 Оптимизация | Средний | Блокирующий .GetAwaiter().GetResult() при старте |
| OPT-04 | 🟡 Оптимизация | Низкий | GetErrorCodes: нет детерминированной сортировки |
| BUG-01 | 🟠 Баги | Высокий | Нет валидации входных данных в Create/UpdateSection |
| BUG-02 | 🟠 Баги | Высокий | CancellationToken не передаётся в Storage-методы |
| BUG-03 | 🟠 Баги | Высокий | Create/Update/Delete команды не подключены к API |
| BUG-04 | 🟠 Баги | Средний | Race condition при seeding в multi-replica среде |
| BUG-05 | 🟠 Баги | Низкий | GetProtoFileContent не валидирует имя файла |
| ARCH-01 | 🔵 Архитектура | Средний | GetErrorCodesQuery обходит Storage-слой |
| ARCH-02 | 🔵 Архитектура | Низкий | Storage зарегистрированы как Transient вместо Scoped |
| ARCH-03 | 🔵 Архитектура | Низкий | Временные метки устанавливаются в C#-объекте, не в БД |
