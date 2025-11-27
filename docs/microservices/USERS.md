# Users Microservice

## Назначение

Сервис Users отвечает за **управление профилями пользователей и системой значков (badges)** в BarkFluff. Основные функции:

- 👤 Управление профилями пользователей (имя, юзернейм, био, аватар)
- 🔍 Поиск пользователей (полнотекстовый поиск с fuzzy matching)
- 🏅 Система значков (badges) с приоритетами
- ✅ Проверка уникальности username/email
- 📧 Управление контактными данными
- 📝 Управление черновиками пользователей (draft users)

**Порт**: 7002
**База данных**: PostgreSQL (`users_db`)
**Зависимости**: Files service (для аватаров)

## Технологический стек

- **.NET 9.0**: Framework
- **gRPC**: API протокол (два API: UsersApi и UsersServerApi)
- **Entity Framework Core 9.0.8**: ORM
- **PostgreSQL 16+** с расширениями:
  - `pg_trgm` - триграммный поиск
  - Полнотекстовый поиск (russian language)
- **RabbitMQ** (MassTransit): Публикация событий изменения профиля
- **MediatR**: CQRS pattern

## Архитектура

```
┌─────────────────────────────────────────────┐
│             Users Service                    │
├─────────────────────────────────────────────┤
│  ┌───────────┐  ┌──────────┐  ┌──────────┐ │
│  │ Features  │→ │ Storage  │→ │PostgreSQL│ │
│  │(21 шт.)   │  │          │  │          │ │
│  └───────────┘  └──────────┘  └──────────┘ │
│       │                                      │
│       ↓                                      │
│  ┌───────────┐                               │
│  │ RabbitMQ  │ → UserChanged* Events        │
│  │ Publisher │                               │
│  └───────────┘                               │
└─────────────────────────────────────────────┘
         │                        │
         ↓                        ↓
┌──────────────┐          ┌──────────────┐
│Files Service │          │Messages/     │
│(аватары)     │          │Updates       │
└──────────────┘          │(события)     │
                          └──────────────┘
```

## База данных

### Схема

| Таблица | Описание |
|---------|----------|
| **Users** | Основная информация о пользователях |
| **UserContacts** | Email адреса (one-to-one с Users) |
| **Badges** | Определения значков |
| **UserBadges** | Назначенные пользователям значки |

### Основные сущности

#### User
```csharp
public class User
{
    public long Id { get; set; }                    // Уникальный ID
    public string Username { get; set; }            // @username
    public string FirstName { get; set; }           // Имя
    public string LastName { get; set; }            // Фамилия
    public DateTime RegistrationDate { get; set; }  // Дата регистрации
    public UserContact Contact { get; set; }        // Email
    public bool IsDraft { get; set; }               // Черновик (незавершённая регистрация)
    public string? ProfilePicture { get; set; }     // URL аватара (full)
    public string? ProfilePicturePreviewUrl { get; set; }  // URL превью аватара
    public string? Bio { get; set; }                // Био (макс. 200 символов)
}
```

#### Badge
```csharp
public class Badge
{
    public int Id { get; set; }
    public string Name { get; set; }               // Название значка
    public string Description { get; set; }        // Описание
    public string ImageUrl { get; set; }           // URL изображения
    public DateTime CreatedDate { get; set; }      // Дата создания
    public bool IsActive { get; set; }             // Активен ли значок
}
```

#### UserBadge
```csharp
public class UserBadge
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public int BadgeId { get; set; }
    public int Priority { get; set; } = 1000;      // Меньше = выше приоритет
    public DateTime AssignedDate { get; set; }     // Дата назначения
}
```

**Уникальное ограничение**: `(UserId, BadgeId)` - один значок один раз

### Полнотекстовый поиск

**Миграция**: `20250629131458_AddFullTextSearch`

Создаёт:
1. **Триграммные индексы** (pg_trgm):
```sql
CREATE INDEX users_firstname_trgm_idx ON "Users" USING gin ("FirstName" gin_trgm_ops);
CREATE INDEX users_lastname_trgm_idx ON "Users" USING gin ("LastName" gin_trgm_ops);
CREATE INDEX users_username_trgm_idx ON "Users" USING gin ("Username" gin_trgm_ops);
```

2. **Full-text индекс** (Russian language):
```sql
CREATE INDEX users_fulltext_idx ON "Users" USING gin(
    to_tsvector('russian', "FirstName" || ' ' || "LastName" || ' ' || "Username")
);
```

## Ключевые функции

### 1. Управление профилем

#### Создание пользователя (Draft → Confirmed)

**3-этапный процесс**:

**Этап 1: Создание черновика** (вызывается Identity)
```
Identity → Users.AddDraftUser(username, firstName, lastName, email)
           ↓
Users → Создаёт User с IsDraft = true
      → Проверяет уникальность username/email
      → Возвращает userId
```

**Этап 2: Подтверждение**
```
Identity → Users.ConfirmUser(userId)
           ↓
Users → IsDraft = false
```

**Этап 3: Override (если регистрация повторяется)**
```
Identity → Users.OverrideDraftUser(...)
           ↓
Users → Обновляет данные черновика
      → Сбрасывает аватар
      → RegistrationDate = DateTime.UtcNow
```

#### Изменение профиля

**Методы**:
- `ChangeName(firstName, lastName)` → RabbitMQ: UserChangedName
- `ChangeUsername(username)` → RabbitMQ: UserChangedUsername
- `ChangeBio(bio)` → RabbitMQ: UserChangedBio
- `SetProfilePicture(fileId)` → RabbitMQ: UserChangedAvatar

**Процесс изменения аватара**:
```
1. Client → Files.GetUploadUrl(type=UserAvatar)
2. Client → Загружает файл
3. Client → Users.SetProfilePicture(fileId)
   ↓
4. Users → Files.GetFileData(fileId)
5. Users → Проверка: file.Type == UserAvatar
6. Users → Сохранение: ProfilePicture = file.FileUrl
                       ProfilePicturePreviewUrl = file.PreviewUrl
7. Users → RabbitMQ.Publish(UserChangedAvatar {
       UserId, ProfilePictureUrl, ProfilePictureUrlPreview
   })
```

### 2. Поиск пользователей

**Endpoint**: `SearchUsers(query, pagination)`

**Алгоритм**: Триграммный поиск (similarity-based)

```sql
SELECT u.* FROM "Users" u
WHERE (similarity(u."FirstName", @searchTerm) > 0.3
   OR similarity(u."LastName", @searchTerm) > 0.3
   OR similarity(u."Username", @searchTerm) > 0.3)
AND u."IsDraft" = false
ORDER BY GREATEST(
    similarity(u."FirstName", @searchTerm),
    similarity(u."LastName", @searchTerm),
    similarity(u."Username", @searchTerm)
) DESC
LIMIT @size OFFSET @offset;
```

**Особенности**:
- Fuzzy matching (находит "Jhon" при поиске "John")
- Работает на любом языке
- Threshold = 0.3 (30% совпадения)
- Макс. 50 результатов на страницу
- Исключает черновики

### 3. Система значков (Badges)

#### Назначение значка

```
Admin → Users.AssignUserBadge(userId, badgeId, priority=1000)
        ↓
Users → Проверка уникальности (userId, badgeId)
      → Создание UserBadge
      → AssignedDate = DateTime.UtcNow
```

#### Получение значков пользователя

```
Client → Users.GetUserBadges(userId, limit=3)
         ↓
Users → SELECT * FROM UserBadges
        WHERE UserId = @userId AND Badge.IsActive = true
        ORDER BY Priority ASC, AssignedDate ASC
        LIMIT @limit
```

**Сортировка**:
1. По приоритету (меньше = выше)
2. По дате назначения (старые первыми)

**Примеры limit**:
- `limit = 1` - для списка пользователей (показать главный значок)
- `limit = 3` - для профиля пользователя
- `limit = null` - все значки

#### Управление приоритетом

```
Admin → Users.UpdateUserBadgePriority(userId, badgeId, newPriority=10)
        ↓
Users → Обновление Priority в UserBadge
      → Возвращает обновлённый значок
```

### 4. Проверка уникальности

**Draft-aware валидация**:

```csharp
// CheckExistEmail
var userByEmail = await _usersStorage.GetUserByEmail(email);
if (userByEmail is null || userByEmail.IsDraft)
    return new CheckExistResponse { Exist = false };  // Черновики не считаются
return new CheckExistResponse { Exist = true };
```

**Логика**: Черновики (IsDraft = true) **не блокируют** повторную регистрацию с теми же данными.

## RabbitMQ События

Users **публикует** 5 типов событий:

### 1. UserChangedName
```json
{
  "UserId": 12345,
  "NewFirstName": "Иван",
  "NewLastName": "Петров"
}
```
**Когда**: Изменение имени через ChangeName
**Потребители**: Messages (обновление имён в чатах), Updates

### 2. UserChangedUsername
```json
{
  "UserId": 12345,
  "NewUsername": "ivan_petrov"
}
```
**Когда**: Изменение username через ChangeUsername
**Потребители**: Messages, Updates

### 3. UserChangedAvatar
```json
{
  "UserId": 12345,
  "ProfilePictureUrl": "https://cdn.../avatar.jpg",
  "ProfilePictureUrlPreview": "https://cdn.../avatar_preview.jpg"
}
```
**Когда**: Установка/изменение аватара через SetProfilePicture
**Потребители**: Messages (обновление аватаров в DM чатах)

### 4. UserChangedBio
```json
{
  "UserId": 12345,
  "NewBio": "Software developer"
}
```
**Когда**: Изменение био через ChangeBio
**Потребители**: Updates (для кеша профилей)

### 5. UserChangedPassword
```json
{
  "UserId": 12345
}
```
**Определён**, но **не используется** в текущей реализации

## Зависимости

### Files Service (gRPC)

**Метод**: `GetFileDataAsync(fileId)`

**Использование**: Валидация типа файла при установке аватара

```csharp
var fileInfo = await _filesServerApiClient.GetFileDataAsync(
    new GetFileDataRequest { FileId = request.FileId });

if (fileInfo.FileInfo.Type != UploadFileType.UserAvatar)
    throw new ProfilePictureHasNotValidType();

// Извлечение URLs
string fileUrl = fileInfo.FileInfo.FileUrl;
string previewUrl = fileInfo.FileInfo.PreviewUrl;
```

### Configuration Service

**Startup dependency**: Загрузка конфигурации

```csharp
builder.LoadConfiguration(ServiceId.Users);
```

## API Reference

### UsersApi (Client-facing)

Требует **User token**:

| Метод | Описание |
|-------|----------|
| `GetUser(userId)` | Получить профиль пользователя |
| `SetProfilePicture(fileId)` | Установить аватар |
| `ChangeName(firstName, lastName)` | Изменить имя |
| `ChangeUsername(username)` | Изменить username |
| `ChangeBio(bio)` | Изменить био |
| `SearchUsers(query, pagination)` | Поиск пользователей |
| `GetUserBadges(userId, limit)` | Получить значки пользователя |
| `CheckExistUsername(username)` | Проверить существование username |
| `CheckExistEmail(email)` | Проверить существование email |

### UsersServerApi (Service-to-Service)

Требует **Service token**:

| Метод | Описание |
|-------|----------|
| `FindByLogin(username/email)` | Поиск по логину (Identity) |
| `AddDraftUser(...)` | Создать черновик (Identity) |
| `OverrideDraftUser(...)` | Перезаписать черновик (Identity) |
| `ConfirmUser(userId)` | Подтвердить пользователя (Identity) |
| `GetById(userId)` | Получить пользователя по ID |
| `GetUserContacts(userId)` | Получить email пользователя |
| `ListByIds(userIds)` | Массовое получение пользователей (Messages) |
| `AssignUserBadge(userId, badgeId, priority)` | Назначить значок (Admin) |
| `RemoveUserBadge(userId, badgeId)` | Снять значок (Admin) |
| `UpdateUserBadgePriority(userId, badgeId, priority)` | Изменить приоритет значка (Admin) |
| `CreateBadge(name, description, imageUrl)` | Создать значок (Admin) |
| `GetAllBadges(includeInactive)` | Получить все значки |

## Конфигурация

### appsettings.json

```json
{
  "UsersDb": "Host=postgres;Database=users_db;Username=postgres;Password=***",
  "FilesService": {
    "Host": "http://files:7005",
    "Token": "service-token"
  },
  "RabbitMQ": {
    "Host": "rabbitmq://rabbitmq",
    "Username": "guest",
    "Password": "guest"
  }
}
```

### Миграции

**Auto-migrate**: При старте сервиса

```csharp
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<UsersContext>();
    ctx.Database.Migrate();  // Применяет pending migrations
}
```

## Особенности реализации

### 1. Draft Users Pattern

**Проблема**: Как резервировать username/email во время регистрации?

**Решение**: Два состояния пользователя
- `IsDraft = true` - незавершённая регистрация
- `IsDraft = false` - подтверждённый пользователь

**Преимущества**:
- Username/email резервируются сразу
- Можно перезаписать черновик (OverrideDraftUser)
- Черновики не видны в поиске
- Транзакционная безопасность

### 2. Badge Priority System

**Проблема**: Как контролировать порядок отображения значков?

**Решение**: Поле `Priority` с сортировкой
- Меньшее число = выше приоритет
- Default = 1000
- Можно изменить через UpdateUserBadgePriority

**Use cases**:
- Премиум значок: priority = 1
- Обычный значок: priority = 1000

### 3. Fuzzy Search с Trigrams

**Проблема**: Пользователи делают опечатки

**Решение**: PostgreSQL pg_trgm
- Разбивает строки на триграммы ("Jon" → ["__J", "_Jo", "Jon", "on_", "n__"])
- Вычисляет similarity коэффициент (0.0 - 1.0)
- Threshold = 0.3 (30% совпадения)

**Примеры**:
- "Jhon" находит "John" (similarity ~0.6)
- "Alexandr" находит "Alexander" (similarity ~0.8)
- "Pete" находит "Peter" (similarity ~0.5)

## Известные проблемы

### 🟡 Средние

1. **Нет валидации длины Bio**
   - Описано как "макс. 200 символов"
   - Не проверяется на уровне сервиса
   - **Рекомендация**: Добавить валидацию

2. **Нет удаления пользователей**
   - Отсутствует метод DeleteUser
   - **Рекомендация**: Soft delete с флагом IsDeleted

3. **AccountsCount не используется**
   - Возвращается всегда 0
   - **Рекомендация**: Вычислять реальное количество

## Troubleshooting

### Проблема: "UserIsDraftException" при регистрации

**Причина**: Попытка создать аккаунт с username/email существующего черновика

**Решение**:
```csharp
try {
    await usersClient.AddDraftUserAsync(...);
} catch (UserIsDraftException) {
    // Перезаписать черновик
    await usersClient.OverrideDraftUserAsync(...);
}
```

### Проблема: "Duplicate key violation (UserId, BadgeId)"

**Причина**: Попытка назначить один значок дважды

**Решение**: Проверить существующие значки перед назначением

### Проблема: Поиск не находит пользователя

**Причина 1**: Пользователь - черновик (IsDraft = true)
**Решение**: Подтвердить регистрацию

**Причина 2**: Similarity < 0.3
**Решение**: Увеличить точность запроса

## Метрики

### Ключевые метрики для мониторинга

- **Новые регистрации / день** (ConfirmUser calls)
- **Поисковые запросы / минуту**
- **Изменения профиля / день**
- **Средняя длительность SearchUsers**

## Расположение в коде

**Путь**: `/Backend/BarkFluff.Users/`

**Ключевые файлы**:
- `Program.cs` - конфигурация сервиса
- `Host/UsersApiService.cs` - Client API
- `Host/UsersServerApiService.cs` - Service API
- `Features/*/` - 21 CQRS handler
- `Persistence/Services/UsersStorage.cs` - Repository
- `Infrastructure/UserInfoQueueSender.cs` - RabbitMQ publisher
- `Migrations/20250629131458_AddFullTextSearch.cs` - Search indexes
- `Migrations/20250914111458_AddBadgesTables.cs` - Badge system
