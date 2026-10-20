# Тестирование

Parent: [[Index]]

## Где находятся проверки

Backend unit/integration проекты находятся в `Tests/BarkFluff.*.Tests/` (проекты `.csproj`). `.github/workflows/pr-tests.yml` запускается для каждого PR в `nightly`, `dev`, `master` без фильтра путей. Reusable `test-backend.yml` обнаруживает все backend-проекты и запускает каждый отдельной matrix job; Windows-проекты исключаются по Windows TargetFramework. PostgreSQL 16 в service container включает Messages search и Developers concurrent seed через соответствующие env; рабочие БД не используются.

`test-clients.yml` запускает Windows test projects на Windows, Android JVM tests (`:core:testDebugUnitTest`, `:app-v1:testNightlyDebugUnitTest`) и UI checker на Ubuntu, BFCore Swift tests на macOS 26. Android instrumented tests с эмулятором, iOS UI и Linux Qt в этот набор не входят. `test-scripts.yml` запускает Python CI helper tests и JavaScript-тесты Web/AdminPanel. TRX и Android XML сохраняются как artifacts.

Единый PR check `Tests passed` выполняется с `always()` и успешен только при `success` всех трёх reusable-наборов: failure, cancellation и skipped не разрешают merge. Правило GitHub требует отдельной установки; порядок — [[Architecture#Развёртывание и CI]]. Push/manual workflows сборок сначала выполняют скриптовые проверки и тесты своего компонента; backend также проверяет GrpcServer. Для Beacon/WebServer без собственного test project используется GrpcServer. Это не означает наличие тестов бизнес-логики этих сервисов.

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

PostgreSQL integration-тесты условно пропускаются без соответствующих env; backend CI предоставляет обе переменные через изолированный PostgreSQL service. Их отсутствие в локальном прогоне не означает проверенную PostgreSQL-семантику. SQLite полезен и для Messages/Users, но не заменяет PostgreSQL-specific raw SQL.

## Известные ограничения

- `Tests/BarkFluff.Users.Tests/Features/Prekeys/FetchPrekeyBundleQueryHandlerTests.cs` содержит явно пропущенный кейс: EF InMemory не выполняет raw SQL `DELETE … RETURNING`. Для проверки этого поведения нужен PostgreSQL. Точный путь сверять по дереву тестов при переносе feature.
- `MarkAsReadCommandHandlerTests` в Messages — обычные активные unit tests без PostgreSQL Skip. Наличие unit tests само по себе не доказывает полный relational путь.
- Redis/MassTransit/gRPC проверки используют mocks/fakes; для каждого теста различай проверку доменной логики и реального транспорта. Moq требует виртуальных перехватываемых методов; нельзя объяснять каждый сбой этим ограничением без воспроизведения.
- GrpcServer Lognara-тесты меняют общие `Console.Out/Error`, `Log.Logger` и Serilog SelfLog; коллекция `Process logging` с `DisableParallelization` изолирует их от остальных тестовых хостов процесса.
- Карты компонентов описывают значимые тестовые области, а не процент покрытия. Отсутствие Skip или наличие test project не означает полноту покрытия.

## Проверки вспомогательных инструментов

Без отправки сообщений, публикации образов и обращения к production:

```bash
python3 .github/tests/test_docker_version.py
python3 .github/tests/test_client_version.py
python3 .github/tests/test_send_telegram.py
python3 .github/tests/test_project_references.py
```

Наборы проверяют version promotion/pagination/error paths и Telegram helper через локальные фикстуры, а также точное совпадение регистра `ProjectReference` тестовых проектов с путями Git для сборки на Linux. Для PowerShell-кейсов WinUI нужен `pwsh` или `TEST_PWSH`; пропуск такого кейса сообщай отдельно. Эти команды не заменяют сборку приложений. Детали CI — [[Architecture]].
