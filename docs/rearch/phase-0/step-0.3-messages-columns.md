# Этап 0.3 — Подготовка схемы Messages

## Цель

Добавить в Messages колонки, необходимые будущей федерации ([../05-chat-replication.md](../05-chat-replication.md)):

- `Message.LastChangeAt` — единая UTC-метка последнего изменения (основа LWW-разрешения конфликтов). **Поддерживается кодом сразу** — иначе протухнет.
- `Message.FederatedId` (Guid?, nullable) — глобальный ID сообщения для федеративных чатов. Пока никем не заполняется.
- `Message.SenderUuid` (Guid?, nullable) — UUID автора. Пока никем не заполняется.
- `ChatMembers.UserUuid` (Guid?, nullable) — UUID участника (для будущих remote-участников). Пока никем не заполняется.

Поведение сервиса не меняется: все новые поля либо пассивны (nullable, NULL), либо (`LastChangeAt`) — чистая запись без чтения.

## Изменение 1 — доменные модели

**Файл:** `Backend/BarkFluff.Messages/Domain/Message.cs` — добавить (стиль файла сохранить):

```csharp
    public DateTime LastChangeAt { get; set; }

    public Guid? FederatedId { get; set; }

    public Guid? SenderUuid { get; set; }
```

**Файл:** `Backend/BarkFluff.Messages/Domain/ChatMember.cs` — добавить:

```csharp
    public Guid? UserUuid { get; set; }
```

`Chat.cs` **не трогать** (признак федеративности и UUID-пара — Фаза 2).

## Изменение 2 — контекст и миграция

**Файл:** контекст Messages (`Backend/BarkFluff.Messages/Persistence/...` — найди `MessagesContext`). В `OnModelCreating` для `Message` добавить уникальный частичный индекс под будущую идемпотентность импорта:

```csharp
modelBuilder.Entity<Message>()
    .HasIndex(m => new { m.ChatId, m.FederatedId })
    .IsUnique()
    .HasFilter("\"FederatedId\" IS NOT NULL");
```

**Миграция** `AddFederationMessageColumns` (порядок как в README фазы, п.4; образцы — соседние миграции, например `20260508163127_AddMessageEditedDeletedFlags.*`). `Up`:

```csharp
// LastChangeAt: NOT NULL с backfill из EditedAt/SentAt.
// Приём: добавить nullable → UPDATE → ужесточить до NOT NULL.
migrationBuilder.AddColumn<DateTime>(
    name: "LastChangeAt",
    table: "Messages",
    type: "timestamp with time zone",
    nullable: true);

migrationBuilder.Sql(
    """UPDATE "Messages" SET "LastChangeAt" = COALESCE("EditedAt", "SentAt");""");

migrationBuilder.AlterColumn<DateTime>(
    name: "LastChangeAt",
    table: "Messages",
    type: "timestamp with time zone",
    nullable: false);

migrationBuilder.AddColumn<Guid>(
    name: "FederatedId",
    table: "Messages",
    type: "uuid",
    nullable: true);

migrationBuilder.AddColumn<Guid>(
    name: "SenderUuid",
    table: "Messages",
    type: "uuid",
    nullable: true);

migrationBuilder.CreateIndex(
    name: "IX_Messages_ChatId_FederatedId",
    table: "Messages",
    columns: new[] { "ChatId", "FederatedId" },
    unique: true,
    filter: "\"FederatedId\" IS NOT NULL");

migrationBuilder.AddColumn<Guid>(
    name: "UserUuid",
    table: "ChatMembers",
    type: "uuid",
    nullable: true);
```

`Down` — зеркально (DropIndex, DropColumn ×4).

Внимание при большой таблице `Messages`: `UPDATE` без WHERE перепишет все строки — на dev это секунды; для прод-объёма это допустимо (одноразовая миграция), но выполнить в окно техработ. Отметь это в сообщении коммита.

Ручная миграция ⇒ обязателен `.Designer.cs` с `[Migration]`/`[DbContext(typeof(MessagesContext))]` + обновление `MessagesContextModelSnapshot.cs` (три свойства Message, свойство ChatMember, индекс).

## Изменение 3 — поддержка LastChangeAt в коде

`LastChangeAt` обязан обновляться при **каждом** изменяющем действии над сообщением. Найди все места, где Messages создаёт/мутирует `Message`, и добавь установку. Известные точки (пути уточни поиском по `Features/`):

| Операция | Где искать | Что ставить |
|----------|-----------|-------------|
| Создание сообщения (обычное, системное, серверное `SendMessageServer`, `PostCallSystemMessage`) | общий путь создания — вероятно один метод в `MessagesStorage`/`SendMessageCommandHandler`; проверь, что системные сообщения (создание группы, kick, pin) идут тем же путём | `LastChangeAt = SentAt` |
| Правка | `EditMessageCommandHandler` (ставит `IsEdited`, `EditedAt`) | `LastChangeAt = EditedAt` |
| Удаление | `DeleteMessageCommandHandler` (ставит `IsDeleted`) | `LastChangeAt = DateTime.UtcNow` |

Надёжный способ ничего не пропустить: grep по проекту Messages на `IsEdited =`, `IsDeleted = true`, `SentAt =` — каждая точка присваивания сопровождается `LastChangeAt`. `MarkAsRead` и pin/unpin **сам объект Message не мутируют** (ReadBy — отдельная семантика, PinnedMessages — отдельная таблица) — для них LastChangeAt НЕ трогать (прочтения в федерации идут отдельным каналом, см. [../05-chat-replication.md](../05-chat-replication.md)).

`EncryptedMessage` (приватные чаты) — **не трогать**: федерация E2E-чатов — отдельная будущая фаза.

## Чего НЕ делать

- Не заполнять `FederatedId`/`SenderUuid`/`UserUuid` — они остаются NULL до Фазы 2.
- Не добавлять proto-поля Messages (этап 0.4) и import/export-RPC (Фаза 2).
- Не менять выдачу (`ListMessages` и т.д.) — `LastChangeAt` наружу пока не отдаётся.
- Не трогать `ReadBy`, `Chat`, `PinnedMessage`, `EncryptedMessage`.

## Критерии готовности

1. `dotnet build Backend/BarkFluff.Messages/BarkFluff.Messages.csproj` — успех.
2. Миграция применяется на dev-БД с данными: `SELECT COUNT(*) FROM "Messages" WHERE "LastChangeAt" IS NULL` → 0; для старых неправленых сообщений `LastChangeAt = SentAt`, для правленых = `EditedAt` (выборочно проверить).
3. Ручной прогон: отправить сообщение → в БД `LastChangeAt == SentAt`; отредактировать → `LastChangeAt == EditedAt`; удалить → `LastChangeAt` обновился. (Через клиент/grpcurl + SQL.)
4. Существующие тесты Messages проходят; сценарии список чатов/сообщений/пины/приватные чаты не регрессировали (smoke через клиент или тесты).
5. Obsidian: `Backend/Messages.md` — секция «База данных» дополнена новыми колонками с пометкой «фаза 0 федерации, пока пассивны» (кроме LastChangeAt — активна).
6. Коммит: `feat(rearch-phase0): 0.3 — Messages: LastChangeAt + federated-колонки`.
