# Тестирование

Parent: [[Index]]

## Где находятся проверки

Backend unit/integration проекты находятся в `Tests/BarkFluff.*.Tests/` (проекты `.csproj`); не все сервисы выполняются в CI. Backend workflows запускают `dotnet test` для Settings, Setup и Developers. Прочие проверки запускать по затронутому компоненту; результаты и число пройденных тестов не хранить как постоянное свойство архитектуры.

```bash
dotnet test Tests/BarkFluff.Messages.Tests/BarkFluff.Messages.Tests.csproj
```

Для .NET нужен SDK из `global.json`. Клиентские проверки находятся в собственных деревьях; точки входа — [[Clients/Android-FileIndex]], [[Clients/macOS-ProjectMap]], [[Clients/iOS-ProjectMap]], Windows/Web-заметки.

## Базы данных и ограничения провайдеров

Продакшн доменные БД используют PostgreSQL/Npgsql. EF InMemory не доказывает корректность SQL, транзакций, concurrency, `ExecuteDelete` и PostgreSQL operators. Выбирай provider по проверяемому поведению:

| Проверка | Источник и provider |
|----------|--------------------|
| Files storage/relational queries | `Tests/BarkFluff.Files.Tests/TestHelper.cs`: SQLite in-memory, открытое соединение и `EnsureCreated` |
| Messages обычные handler-тесты | `Tests/BarkFluff.Messages.Tests/TestHelper.cs`: EF InMemory |
| Messages transactional outbox | `Tests/BarkFluff.Messages.Tests/Persistence/MessagesStorageOutboxRelationalTests.cs`: SQLite; dispatcher unit tests используют InMemory/fakes |
| Users handler-тесты | `Tests/BarkFluff.Users.Tests/TestHelper.cs`: EF InMemory |
| Users device concurrency | `Tests/BarkFluff.Users.Tests/Features/Devices/DevicesStorageConcurrencyTests.cs`: SQLite с несколькими соединениями |
| Messages PostgreSQL search | `Tests/BarkFluff.Messages.Tests/Persistence/SearchMessagesPostgresTests.cs`: `BARKFLUFF_SEARCH_POSTGRES`, disposable DB и право CREATE DATABASE |
| Developers concurrent seed | `Tests/BarkFluff.Developers.Tests/PostgresConcurrentSeedTests.cs`: `DEVELOPERS_TEST_POSTGRES`; инфраструктура — `TestInfrastructure.cs` |

PostgreSQL integration-тесты условно пропускаются без соответствующих env. Их отсутствие в обычном прогоне не означает проверенную PostgreSQL-семантику. SQLite полезен и для Messages/Users, но не заменяет PostgreSQL-specific raw SQL.

## Известные ограничения

- `Tests/BarkFluff.Users.Tests/Features/Prekeys/FetchPrekeyBundleQueryHandlerTests.cs` содержит явно пропущенный кейс: EF InMemory не выполняет raw SQL `DELETE … RETURNING`. Для проверки этого поведения нужен PostgreSQL. Точный путь сверять по дереву тестов при переносе feature.
- `MarkAsReadCommandHandlerTests` в Messages — обычные активные unit tests без PostgreSQL Skip. Наличие unit tests само по себе не доказывает полный relational путь.
- Redis/MassTransit/gRPC проверки используют mocks/fakes; для каждого теста различай проверку доменной логики и реального транспорта. Moq требует виртуальных перехватываемых методов; нельзя объяснять каждый сбой этим ограничением без воспроизведения.
- Карты компонентов описывают значимые тестовые области, а не процент покрытия. Отсутствие Skip или наличие test project не означает полноту покрытия.

## Проверки вспомогательных инструментов

Без отправки сообщений, публикации образов и обращения к production:

```bash
python3 .github/tests/test_docker_version.py
python3 .github/tests/test_client_version.py
python3 .github/tests/test_send_telegram.py
```

Наборы проверяют version promotion/pagination/error paths и Telegram helper через локальные фикстуры. Для PowerShell-кейсов WinUI нужен `pwsh` или `TEST_PWSH`; пропуск такого кейса сообщай отдельно. Эти команды не заменяют сборку приложений. Детали CI — [[Architecture]].
