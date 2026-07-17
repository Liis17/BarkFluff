# Этап 0.2 — UUID у пользователей (Users)

## Цель

У каждого пользователя (включая ботов) появляется глобально уникальный `Uuid` (Guid): колонка в БД с backfill'ом существующих, отдача в gRPC. Это фундамент федеративной идентичности ([../01-addressing-identity.md](../01-addressing-identity.md)): `long Id` останется внутренним, через границу серверов в будущем ходит только UUID.

Никакой логики поверх UUID на этом этапе нет — только поле, индекс, отдача клиентам.

## Изменение 1 — доменная модель

**Файл:** `Backend/BarkFluff.Users/Domain/User.cs`

Добавить свойство (после `Id`, стиль файла сохранить):

```csharp
    public Guid Uuid { get; set; }
```

## Изменение 2 — генерация при создании

**Файл:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs`

Найди метод создания пользователя (`CreateUser` — используется и draft-регистрацией, и `CreateBotUser`). Перед сохранением проставлять:

```csharp
user.Uuid = Guid.NewGuid();
```

(если объект собирается в хендлерах `AddDraftUser`/`CreateBotUser` — проставь в одном общем месте, в storage, чтобы покрыть все пути создания). Убедись, что `OverrideDraftUser` **не** перезаписывает Uuid существующего черновика.

## Изменение 3 — EF Core: контекст и миграция

**Файл:** `Backend/BarkFluff.Users/Persistence/Contexts/UsersContext.cs`

В `OnModelCreating` добавить уникальный индекс (по стилю существующих индексов файла):

```csharp
modelBuilder.Entity<User>()
    .HasIndex(u => u.Uuid)
    .IsUnique();
```

**Миграция** `AddUserUuid`. Сначала попробуй `dotnet ef` (см. README фазы, п.4); при падении — вручную по образцу `20260705130000_AddUserIsBot.*`. Содержимое `Up`:

```csharp
// 1. Колонка с backfill на уровне БД: gen_random_uuid() встроен в PostgreSQL 13+
migrationBuilder.AddColumn<Guid>(
    name: "Uuid",
    table: "Users",
    type: "uuid",
    nullable: false,
    defaultValueSql: "gen_random_uuid()");

// 2. Уникальный индекс
migrationBuilder.CreateIndex(
    name: "IX_Users_Uuid",
    table: "Users",
    column: "Uuid",
    unique: true);
```

`Down`: DropIndex + DropColumn.

Пояснения:
- `defaultValueSql: "gen_random_uuid()"` автоматически заполняет **все существующие строки** уникальными значениями при `AddColumn` — отдельный UPDATE не нужен.
- Дефолт в БД оставляем и после миграции — как страховка; основной путь генерации всё равно код (Изменение 2).
- Если пишешь миграцию вручную: обязателен `.Designer.cs` с `[Migration("<timestamp>_AddUserUuid")]` и `[DbContext(typeof(UsersContext))]`, плюс синхронное обновление `UsersContextModelSnapshot.cs` (добавить свойство `Uuid` с `HasDefaultValueSql` и индекс в модель `User`) — иначе следующая миграция сгенерирует дифф заново.

## Изменение 4 — proto

**Файл:** `Shared/BarkFluff.Proto/users_api.proto`

Message `User` (сейчас поля 1–12, последнее `bool is_bot = 12`). Добавить:

```protobuf
  string uuid = 13; // Глобально уникальный идентификатор пользователя (Guid), для федерации
```

Только это поле. Остальные proto-расширения — этап 0.4.

## Изменение 5 — маппинг

**Файл:** `Backend/BarkFluff.Users/Mapping/UserMapping.cs`

В `ToGrpc` добавить:

```csharp
            Uuid = domainUser.Uuid.ToString(),
```

Проверь другие места, где `Domain.User` мапится в proto `User` вручную (поиск по `new User` / `ProfilePicturePreview` в проекте Users) — если такие есть, добавить Uuid и там. Ответы, использующие свои message-типы без поля uuid (например `GetUserByUsernameResponse` для WebServer), **не трогать** — им UUID добавится позже при необходимости.

## Побочные потребители — проверить, не менять логику

- **Identity** ходит в Users по `UsersServerApi` — получает расширенный `User` автоматически, правок не требует.
- **Messages** кеширует имена/аватары — UUID не затрагивает.
- **`Backend/BarkFluff.Users.Rust`** — экспериментальный drop-in порт Users на Rust. Его НЕ трогать, но он перестанет соответствовать схеме БД (не знает колонку Uuid; с `defaultValueSql` INSERT'ы из Rust продолжат работать). Добавь строку-предупреждение в `docs/Audit/BarkFluff.Users.Rust.md` или в Obsidian `Backend/Users-Rust.md`: «схема Users получила колонку Uuid (фаза 0 федерации), порт не синхронизирован».

## Чего НЕ делать

- Не заводить таблицу `RemoteUsers` (Фаза 2).
- Не добавлять `ResolveFederatedUser`, privacy-поля, поиск по FID (Фазы 0.4/2).
- Не менять `long Id` нигде.

## Критерии готовности

1. `dotnet build Backend/BarkFluff.Users/BarkFluff.Users.csproj` — успех (после правки proto пересобрать и зависимые: Identity, Messages, AdminPanel — все, у кого `users_api.proto` GrpcServices=Client; достаточно `dotnet build` решения либо поочерёдно).
2. Миграция применяется на локальной/dev БД с существующими пользователями: у **всех** строк `Users` появился непустой уникальный `Uuid` (проверить SQL-запросом `SELECT COUNT(*) FROM "Users" WHERE "Uuid" IS NULL` → 0 и `SELECT COUNT(DISTINCT "Uuid") = COUNT(*)`).
3. Регистрация нового пользователя (draft → confirm) и `CreateBotUser` дают пользователя с Uuid.
4. `GetUser`/`GetById` возвращают поле `uuid` (проверить grpcurl'ом или существующим тестом).
5. Существующие тесты Users проходят.
6. Obsidian: `Backend/Users.md` — упомянуть Uuid (домен, индекс, поле 13 в proto).
7. Коммит: `feat(rearch-phase0): 0.2 — Users.Uuid + backfill + proto`.
