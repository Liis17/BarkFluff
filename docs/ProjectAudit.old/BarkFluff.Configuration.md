# Аудит проекта: BarkFluff.Configuration

> **Первичный аудит:** 2026-07-01
> **Ревизия:** 2026-05-18
> **Ветка:** `dev`
> **Аудитор:** GitHub Copilot (BarkfluffAgent) + Claude Opus 4.7 (ревизия)
> **Статус:** 🟠 Прежние замечания закрыты, но обнаружены новые критичные проблемы

---

---

## Статус ранее найденных проблем

| ID     | Проблема                                             | Статус         | Где исправлено                               |
| ------ | ---------------------------------------------------- | -------------- | -------------------------------------------- |
| SEC-05 | Пароли RabbitMQ `guest/guest` записывались в БД      | ✅ Исправлено\* | `Program.cs:105-106` — теперь через env vars |
| BUG-03 | `UpdateConfigurationAsync` не валидирует `ServiceId` | ✅ Исправлено   | `UpdateConfigurationCommandHandler.cs:22-33` |
| OPT-XX | `.Any()` вместо `.Count == 0` для `List<T>`          | ✅ Исправлено   | `ConfigurationDefaultsPopulator.cs:125`      |

> \* SEC-05 закрыт частично: env-vars читаются, но при их отсутствии работает fallback на `guest`/`guest`. См. [NEW-SEC-01](#new-sec-01--rabbitmq-fallback-на-guestguest-cwe-798).

---

## 🔴 Безопасность (новые)

---

### NEW-SEC-01 — RabbitMQ fallback на `guest/guest` (CWE-798)

**Описание:**
Конфигурация RabbitMQ читается из переменных окружения, но при их отсутствии используется fallback на дефолтные `guest/guest`. Если DevOps забудет установить `RABBITMQ_DEFAULT_USER` / `RABBITMQ_DEFAULT_PASS`, сервис стартует с известными всем учётными данными.

**CWE:** CWE-798 (Use of Hard-coded Credentials)
**Severity:** 🟠 Высокая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Program.cs` : 105–106

```csharp
// ❌ ПРОБЛЕМА: fallback на дефолтные учётные данные
var rabbitUsername = builder.Configuration["RABBITMQ_DEFAULT_USER"] ?? "guest";
var rabbitPassword = builder.Configuration["RABBITMQ_DEFAULT_PASS"] ?? "guest";
```

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ: fail-fast при отсутствии секретов
var rabbitUsername = builder.Configuration["RABBITMQ_DEFAULT_USER"]
    ?? throw new InvalidOperationException("RABBITMQ_DEFAULT_USER не задан");
var rabbitPassword = builder.Configuration["RABBITMQ_DEFAULT_PASS"]
    ?? throw new InvalidOperationException("RABBITMQ_DEFAULT_PASS не задан");
```

---

### NEW-SEC-02 — Дефолтные креды MinIO (`minioadmin/minioadmin`)

**Описание:**
В `ConfigurationDefaultsPopulator` для S3 (MinIO) записываются жёстко прошитые `minioadmin/minioadmin`. Эти креды публично известны как стандартные для MinIO и не меняются автоматически.

**CWE:** CWE-798 (Use of Hard-coded Credentials)
**Severity:** 🟠 Высокая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationDefaultsPopulator.cs` : 344–345

```csharp
// ❌ ПРОБЛЕМА: стандартные креды MinIO в дефолтах
"AccessKey" => "minioadmin",
"SecretKey" => "minioadmin",
```

**Вариант решения:**

Генерировать случайные ключи при первом запуске (как уже делается для JWT-ключей):

```csharp
// ✅ РЕШЕНИЕ: генерировать криптостойкие ключи при первом старте
"AccessKey" => GenerateRandomKey(20),
"SecretKey" => GenerateRandomKey(40),
```

---

### NEW-SEC-03 — gRPC API без авторизации caller'а

**Описание:**
Методы `GetConfiguration` / `UpdateConfiguration` в `ConfigurationApiService` доступны любому, кто может достучаться до gRPC-порта. Нет проверки JWT-токена, нет policy-based авторизации, нет фильтрации по `ServiceId` источника.

**В чём проблема:**
Любой контейнер кластера может прочитать (или, что страшнее, перезаписать) конфигурацию любого другого сервиса. Утечка одного контейнера → компрометация всех конфигов кластера.

**CWE:** CWE-306 (Missing Authentication for Critical Function)
**Severity:** 🔴 Критическая

**Путь к файлу:** `Backend\BarkFluff.Configuration\Host\ConfigurationApiService.cs` : 30–91

```csharp
// ❌ ПРОБЛЕМА: нет проверки авторизации
public override async Task<GetConfigurationResponse> GetConfiguration(
    GetConfigurationRequest request, ServerCallContext context)
{
    // ← context.GetHttpContext().User не проверяется
    var response = await _mediator.Send(...);
    return response;
}
```

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ: внутренняя авторизация по сервисному токену
[Authorize(Policy = "InternalService")]
public override async Task<GetConfigurationResponse> GetConfiguration(...)
{
    // policy проверит подпись JWT и роль InternalService
}
```

Или взаимный TLS между сервисами кластера (mTLS).

---

## 🟠 Баги и недоработки (новые)

---

### NEW-BUG-01 — Race condition при upsert конфигураций

**Описание:**
В `ConfigurationStorage.UpdateConfigurationAsync` нет синхронизации между `FirstOrDefaultAsync` и `AddAsync`. При параллельных запросах с одинаковыми `(Section, Key, ServiceId)` обе транзакции прочитают `null`, затем обе попытаются вставить запись — нарушение unique-ограничения (если оно есть) либо появление дубликатов (если ограничения нет — см. [NEW-BUG-02](#new-bug-02--отсутствие-unique-constraint-в-бд)).

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 30–57

```csharp
// ❌ ПРОБЛЕМА: read-then-write без транзакции/блокировки
var existing = await _context.Configurations
    .FirstOrDefaultAsync(c => c.Section == section && c.Key == key && c.ServiceId == serviceId);

if (existing == null)
    await _context.Configurations.AddAsync(new ConfigurationItem { ... });
else
    existing.Value = value;

await _context.SaveChangesAsync();
```

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ A: транзакция с уровнем SERIALIZABLE
using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
// ... read-then-write
await tx.CommitAsync();

// ✅ РЕШЕНИЕ B: PostgreSQL ON CONFLICT DO UPDATE (raw SQL или EF.UpsertRange из EFCore.Upsert)
```

---

### NEW-BUG-02 — Отсутствие unique constraint в БД

**Описание:**
В миграции `20250508111334_AddConfiguration` не создан уникальный индекс на `(ServiceId, Section, Key)`. Это позволяет создавать дубли, что нарушает контракт `GetConfiguration` (там делается `GroupBy + OrderByDescending(EditedAt).First()` — см. [NEW-BUG-04](#new-bug-04--groupby-материализуется-в-памяти)).

**Путь к файлу:** `Backend\BarkFluff.Configuration\Persistence\Migrations\20250508111334_AddConfiguration.cs`

**Вариант решения:**

Новая миграция:

```csharp
migrationBuilder.CreateIndex(
    name: "IX_Configurations_ServiceId_Section_Key",
    table: "Configurations",
    columns: new[] { "ServiceId", "Section", "Key" },
    unique: true);
```

---

### NEW-BUG-03 — Нет валидации входных строковых параметров proto

**Описание:**
`UpdateConfiguration` принимает `Section`, `Key`, `Value`, `EditedBy`, `EditedFrom` без проверок на null/пустоту/длину/спецсимволы. Можно вставить `Value` длиной в 10 МБ или строку с управляющими символами — это влияет и на хранение, и на логирование, и на UI AdminPanel.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Host\ConfigurationApiService.cs` : 57–91

```csharp
// ❌ ПРОБЛЕМА: значения проходят в команду без валидации
var response = await _mediator.Send(new UpdateConfigurationCommand
{
    Section = request.Section,   // может быть null/пустой/огромный
    Key = request.Key,
    Value = request.Value,
});
```

**Вариант решения:**

Добавить MediatR `ValidationBehavior` + FluentValidation, либо ручные проверки в handler:

```csharp
// ✅ РЕШЕНИЕ
if (string.IsNullOrWhiteSpace(request.Section)) return Fail("Section is required");
if (request.Section.Length > 64) return Fail("Section too long");
if (request.Value?.Length > 64_000) return Fail("Value too large");
```

---

### NEW-BUG-04 — GroupBy материализуется в памяти

**Описание:**
В `GetConfigurationCommandHandler` дедупликация записей по `(Section, Key)` выполняется уже после загрузки в память:

```csharp
var filteredConfigurations = configurations
    .GroupBy(c => new { c.Section, c.Key })
    .Select(group => group.OrderByDescending(...).First())
    .ToList();
```

При 100k+ строк это пик памяти. Корень проблемы — отсутствие unique constraint (см. [NEW-BUG-02](#new-bug-02--отсутствие-unique-constraint-в-бд)): дубли существуют именно потому, что не запрещены БД.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Features\GetConfiguration\GetConfigurationCommandHandler.cs` : 27–32

**Вариант решения:**

1. Добавить unique-индекс (см. NEW-BUG-02) → дубли невозможны → GroupBy не нужен.
2. Если нужна история — перенести `GroupBy` в SQL через `DISTINCT ON (Section, Key) ORDER BY EditedAt DESC` (PostgreSQL).

---

## 🔵 Производительность (новые)

---

### NEW-PERF-01 — Нет индекса на `(Section, Key)` для частых запросов

**Описание:**
`GetConfiguration` фильтрует по `Section` и `Key`, но в миграции нет соответствующего индекса. Полный скан таблицы при росте числа записей.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Persistence\Migrations\20250508111334_AddConfiguration.cs`

**Вариант решения:**

```csharp
migrationBuilder.CreateIndex(
    name: "IX_Configurations_Section_Key",
    table: "Configurations",
    columns: new[] { "Section", "Key" });
```

---

### NEW-PERF-02 — Нет публикации `ConfigurationChanged` в RabbitMQ

**Описание:**
`UpdateConfigurationAsync` сохраняет изменения в БД, но не публикует событие в RabbitMQ. Сервисы-потребители конфигов узнают об изменениях только при перезапуске или ручном опросе.

**В чём проблема:**
Hot-reload конфигов не работает. Изменение пароля/хоста/флага в AdminPanel не доходит до сервисов до следующего рестарта.

**Путь к файлу:** `Backend\BarkFluff.Configuration\Infrastructure\ConfigurationStorage.cs` : 30–64

**Вариант решения:**

```csharp
// ✅ РЕШЕНИЕ: публиковать событие после SaveChangesAsync
await _context.SaveChangesAsync();
await _publishEndpoint.Publish(new ConfigurationChangedEvent
{
    ServiceId = serviceId,
    Section = section,
    Key = key
});
```

---

## Перечень дефолтных кредов

> Источник: `ConfigurationDefaultsPopulator.cs`, `Program.cs`

| Сервис     | Ключ                | Дефолтное значение      | Где                                     | Риск       |
| ---------- | ------------------- | ----------------------- | --------------------------------------- | ---------- |
| RabbitMQ   | Username            | `guest` (fallback)      | `Program.cs:105`                        | 🔴 Высокий |
| RabbitMQ   | Password            | `guest` (fallback)      | `Program.cs:106`                        | 🔴 Высокий |
| RabbitMQ   | Host                | `rabbitmq`              | `ConfigurationDefaultsPopulator.cs:246` | 🟢 Низкий  |
| RabbitMQ   | VirtualHost         | `/`                     | `ConfigurationDefaultsPopulator.cs:249` | 🟢 Низкий  |
| MinIO S3   | AccessKey           | `minioadmin`            | `ConfigurationDefaultsPopulator.cs:344` | 🔴 Высокий |
| MinIO S3   | SecretKey           | `minioadmin`            | `ConfigurationDefaultsPopulator.cs:345` | 🔴 Высокий |
| MinIO S3   | ServiceUrl          | `http://minio:9000`     | `ConfigurationDefaultsPopulator.cs:343` | 🟢 Низкий  |
| PostgreSQL | Username / Password | из env                  | `ConfigurationDefaultsPopulator.cs:271` | 🟢 Низкий  |
| Redis      | Host                | `redis:6379` (без auth) | `ConfigurationDefaultsPopulator.cs:257` | 🟡 Средний |
| Seq        | ServerUrl           | `http://seq:5341`       | `ConfigurationDefaultsPopulator.cs:263` | 🟢 Низкий  |

---

## Сводная таблица

| ID          | Категория       | Серьёзность  | Файл                                            | Краткое описание                                     |
| ----------- | --------------- | ------------ | ----------------------------------------------- | ---------------------------------------------------- |
| NEW-SEC-01  | 🔴 Безопасность | **Высокая**  | `Program.cs:105`                                | Fallback на `guest/guest` для RabbitMQ               |
| NEW-SEC-02  | 🔴 Безопасность | **Высокая**  | `ConfigurationDefaultsPopulator.cs:344`         | Дефолтные креды MinIO `minioadmin`                   |
| NEW-SEC-03  | 🔴 Безопасность | **Критично** | `ConfigurationApiService.cs:30`                 | gRPC API без авторизации caller'а                    |
| NEW-BUG-01  | 🟠 Баг          | **Средняя**  | `ConfigurationStorage.cs:30`                    | Race condition при upsert                            |
| NEW-BUG-02  | 🟠 Баг          | **Средняя**  | `Persistence/Migrations/...AddConfiguration.cs` | Нет unique constraint на `(ServiceId, Section, Key)` |
| NEW-BUG-03  | 🟠 Баг          | **Средняя**  | `ConfigurationApiService.cs:57`                 | Нет валидации входных параметров proto               |
| NEW-BUG-04  | 🟠 Баг          | **Низкая**   | `GetConfigurationCommandHandler.cs:27`          | GroupBy материализуется в памяти                     |
| NEW-PERF-01 | 🔵 Перф         | **Средняя**  | `Persistence/Migrations/...AddConfiguration.cs` | Нет индекса `(Section, Key)`                         |
| NEW-PERF-02 | 🔵 Перф         | **Средняя**  | `ConfigurationStorage.cs:30`                    | Нет публикации `ConfigurationChanged` в RabbitMQ     |

---

*Ревизия 2026-05-18: 3 проблемы исходного аудита закрыты, найдено 9 новых. Начать с NEW-SEC-03 (авторизация) и NEW-SEC-01/02 (дефолтные креды).*
