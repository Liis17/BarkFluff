# Тестирование

Parent: [[Index]]

## Где находятся проверки

Backend unit/integration проекты находятся в `Tests/BarkFluff.*.Tests/` (проекты `.csproj`). `.github/workflows/pr-tests.yml` запускается для каждого PR в `nightly`, `dev`, `master`; job `changes` выбирает затронутые наборы через `.github/scripts/pr_tests.py`. Для Android-only PR запускаются только Android-проверки; backend, Windows и macOS выбираются по своим исходникам/тестам. Shared-контракты и сам механизм выбора запускают все наборы; docs-only PR выполняет выбор и итоговый check без сборок приложений. Manual run запускает все наборы. Diff берётся между synthetic PR merge и его первым родителем (`checkout fetch-depth: 2`), без несвязанных изменений целевой ветки.

`Tests/BarkFluff.Builder.Tests/` — тестовый проект `net10.0` для модели, генераторов, валидации, записи/повторного открытия конфигурации и guard-проверок против дрейфа Docker/nginx и Lognara agent. `Tools/` и этот проект выбирают backend-набор в `.github/scripts/pr_tests.py`. Отдельный `.github/workflows/build-builder.yml` также запускает тесты на Windows и проверяет сгенерированные примеры на Ubuntu; детали — [[Tools/Builder|BarkFluff Builder]].

Выбранный reusable `test-backend.yml` обнаруживает все backend-проекты и запускает каждый отдельной matrix job; Windows-проекты исключаются по Windows TargetFramework. PostgreSQL 16 в service container включает Messages search и Developers concurrent seed через соответствующие env; рабочие БД не используются.

`test-clients.yml` запускает Windows test projects на Windows, Android JVM tests (`:core:testDebugUnitTest`, `:app-v1:testNightlyDebugUnitTest`) и UI checker на Ubuntu, BFNetworking и BFCore Swift tests на macOS 26. Android instrumented tests с эмулятором, iOS UI и Linux Qt в этот набор не входят. `test-scripts.yml` запускает Python CI helper tests и JavaScript-тесты Web/AdminPanel. TRX и Android XML сохраняются как artifacts.

Android instrumented-тесты локально компилируют как артефакт (`:app-v1:assembleStableDebugAndroidTest`) и прогоняют только с подключённым эмулятором/устройством (`connectedStableDebugAndroidTest`). Задачи именуют с flavor: короткие `compileDebugKotlin`/`testDebugUnitTest`/`assembleDebug` неоднозначны из-за dev/nightly/stable, на Windows обёртку запускают через gradlew.bat (gradlew падает из-за CRLF). Выборочная очистка кеша покрыта `app/src/androidTest/java/com/barkfluff/client/cache/ChatCacheRepositoryClearTest.kt`: clearHistoryCache/clearSecretHistory с переоткрываемостью БД по ключу, wipeAll(scope) с изоляцией аккаунтов/серверов, ожиданием upsert (flushAll/clearAfterSent), сохранением более нового редактирования, ComposerAttachmentStore.clearScope и clearOutgoingKeepReads с сохранением pending_message_reads и SENT-строк. OutgoingMessageQueueTest проверяет перепланирование worker для сохранённых read receipts после cancelPendingOutgoing. ChatCacheRepositoryClearTest удаляет тестовую БД: запускай его отдельным instrumentation-процессом от тестов, использующих singleton cache приложения. JVM-обёртка чекера `AndroidUiCheckerTest` стартует интерпретатор как `python3`: на Windows без `python3` в PATH тест падает с `CreateProcess error=2`; эквивалентная содержательная проверка — `py -3 Android/tools/check_android_ui.py` (скрипт не зависит от CWD и использует только stdlib).

Единый PR check `Tests passed` выполняется с `always()`: выбор должен завершиться успешно, каждый выбранный набор — иметь `success`, каждый невыбранный — `skipped`. Ошибка выбора, failure/cancellation или неожиданный skip блокируют merge. Для нескольких клиентских платформ используется matrix с `fail-fast: false`. Правило GitHub требует отдельной установки; порядок — [[Architecture#Развёртывание и CI]]. Push/manual workflows сборок сначала выполняют скриптовые проверки и тесты своего компонента; backend также проверяет GrpcServer. Для Beacon/WebServer без собственного test project используется GrpcServer. Это не означает наличие тестов бизнес-логики этих сервисов.

```bash
dotnet test Tests/BarkFluff.Messages.Tests/BarkFluff.Messages.Tests.csproj
```

Для .NET нужен SDK из `global.json`. Клиентские проверки находятся в собственных деревьях; точки входа — [[Clients/Android-FileIndex]], [[Clients/macOS-ProjectMap]], [[Clients/iOS-ProjectMap]], Windows/Web-заметки.

## Swift auth tests

- `swift test --package-path Mac/Barkfluff/Packages/BFNetworking` — `SignInMappingTests`: Identity status/trailer, отмена Task/смена endpoint, wire enum, challenge reference и валидность токенов.
- `swift test --package-path Mac/Barkfluff/Packages/BFCore` — `SignInFlowTests`: challenge/recovery, polling с управляемыми async boundaries, retry Complete, resend cooldown с часами, гонки QR/challenge и откат реального AuthService при отмене во время saveTokens/loadCurrentUser.
- В BFCoreTests `MacLoginViewModel.swift` — относительный symlink на production LoginViewModel; `MacLoginViewModelTests` компилирует и проверяет реальные login actions и переход в main с тестовым AppCoordinator, включая Telegram polling и потерянный Complete. Это не сборка всего приложения и не UI/snapshot тест SwiftUI LoginView. Полная macOS сборка остаётся отдельной проверкой.

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
python3 .github/tests/test_pr_tests.py
```

Наборы проверяют version promotion/pagination/error paths и Telegram helper через локальные фикстуры, точное совпадение регистра `ProjectReference` с путями Git, выбор PR-наборов, shallow merge diff и обработку результатов gate. Для PowerShell-кейсов WinUI нужен `pwsh` или `TEST_PWSH`; пропуск такого кейса сообщай отдельно. Эти команды не заменяют сборку приложений. Детали CI — [[Architecture]].
