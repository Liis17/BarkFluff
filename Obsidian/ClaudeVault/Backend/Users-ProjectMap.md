# BarkFluff.Users — карта проекта

Users владеет локальными профилями, устройствами, приватностью, папками чатов, бейджами и device prekeys. Пути к коду ниже указаны относительно `Backend/BarkFluff.Users/`; `Shared/` и `Tests/` — от корня репозитория.

## Точки входа

- `Program.cs` настраивает gRPC/XAuth, PostgreSQL, очередь revoke-событий и метрики.
- `Host/UsersApiService.cs` — `UsersApi` (`TokenType.User`); `Host/UsersServerApiService.cs` — `UsersServerApi` (`TokenType.Service`).
- `Features/` сгруппированы по профилю/поиску, устройствам, badges, privacy/consent, personalization/folders, bot users, federation/remote profiles и prekeys.

## Данные

- `Persistence/Contexts/UsersContext.cs` конфигурирует пользователей, контакты, устройства, prekeys, privacy, personalization, folders, badges и remote users.
- `Persistence/Services/` содержит операции хранения. Fetch prekey атомарно расходует one-time key (`DELETE … RETURNING`); общего KeyStore service нет.
- Миграции находятся в `Persistence/Migrations/`; ориентируйтесь на текущий snapshot и модели, а не на старые карты этапов.

## Интеграции

- Messages, Identity, Files, Bots, Onliner и Federation вызывают UsersServerApi для профиля, устройства, privacy, ботов и remote UUID mappings.
- `Consumers/SessionRevokedConsumer.cs` обновляет локальный token revocation cache; `Infrastructure/UserInfoQueueSender.cs` публикует изменения профиля.
- Тесты: `Tests/BarkFluff.Users.Tests`; общий helper использует InMemory, отдельные SQLite-тесты проверяют relational concurrency.

← [[Backend/Users]]
