# Тестирование

Parent: [[Index]]

## Где находятся проверки

Backend unit/integration проекты находятся в `Tests/BarkFluff.*.Tests/` (проекты `.csproj`). `.github/workflows/pr-tests.yml` запускается для каждого PR в `nightly`, `dev`, `master`; job `changes` выбирает затронутые наборы через `.github/scripts/pr_tests.py`. Для Android-only PR запускаются только Android-проверки; backend, Windows и macOS выбираются по своим исходникам/тестам. Shared-контракты и сам механизм выбора запускают все наборы; docs-only PR выполняет выбор и итоговый check без сборок приложений. Manual run запускает все наборы. Diff берётся между synthetic PR merge и его первым родителем (`checkout fetch-depth: 2`), без несвязанных изменений целевой ветки.

`Tests/BarkFluff.Builder.Tests/` — тестовый проект `net10.0` для модели, генераторов, валидации, записи/повторного открытия конфигурации и guard-проверок против дрейфа Docker/nginx и Lognara agent. `Tools/` и этот проект выбирают backend-набор в `.github/scripts/pr_tests.py`. Отдельный `.github/workflows/build-builder.yml` также запускает тесты на Windows и проверяет сгенерированные примеры на Ubuntu; детали — [[Tools/Builder|BarkFluff Builder]].

Выбранный reusable `test-backend.yml` обнаруживает все backend-проекты и запускает каждый отдельной matrix job; Windows-проекты исключаются по Windows TargetFramework. PostgreSQL 16 в service container включает Messages search и Developers concurrent seed через соответствующие env; рабочие БД не используются.

`test-clients.yml` запускает Windows test projects на Windows, Android JVM tests (`:core:testDebugUnitTest`, `:app-v1:testNightlyDebugUnitTest`) и UI checker на Ubuntu, BFNetworking и BFCore Swift tests на macOS 26. Android instrumented tests с эмулятором, iOS UI и Linux Qt в этот набор не входят. `test-scripts.yml` запускает Python CI helper tests и JavaScript-тесты Web/AdminPanel. TRX и Android XML сохраняются как artifacts.

Android instrumented-тесты локально компилируют как артефакт (`:app-v1:assembleStableDebugAndroidTest`) и прогоняют только с подключённым эмулятором/устройством (`connectedStableDebugAndroidTest`). Задачи именуют с flavor: короткие `compileDebugKotlin`/`testDebugUnitTest`/`assembleDebug` неоднозначны из-за dev/nightly/stable, на Windows обёртку запускают через gradlew.bat (gradlew падает из-за CRLF). Выборочная очистка кеша покрыта `app/src/androidTest/java/com/barkfluff/client/cache/ChatCacheRepositoryClearTest.kt`: clearHistoryCache/clearSecretHistory с переоткрываемостью БД по ключу, wipeAll(scope) с изоляцией аккаунтов/серверов, ожиданием upsert (flushAll/clearAfterSent), сохранением более нового редактирования, ComposerAttachmentStore.clearScope и clearOutgoingKeepReads с сохранением exact/range read queues и SENT-строк. OutgoingMessageQueueTest проверяет перепланирование worker для сохранённых read receipts после cancelPendingOutgoing. ChatCacheRepositoryClearTest удаляет тестовую БД: запускай его отдельным instrumentation-процессом от тестов, использующих singleton cache приложения. `AndroidUiCheckerTest` использует `py -3` на Windows и `python3` на остальных ОС; эквивалентная самостоятельная команда — `py -3 Android/tools/check_android_ui.py` (скрипт не зависит от CWD и использует только stdlib).

`PendingChatReadRangeDaoTest` checks cutoff coalescing and stale completion; migration instrumentation covers Room v5→v6. `MessageNotificationTest` covers scoped actions, extras/history roundtrip, hash/legacy collision safety, bounded sender icons, late event/avatar merge, reply/read persistence failures and cold/warm/foreign-scope navigation. `ColdNotificationScenarioTest` covers reply/read-range recovery after process death and independent read retry. Compile/assemble does not execute these instrumented scenarios.

## Android PQXDH

`Android/core/src/androidTest/java/com/barkfluff/client/repository/PqxdhHandshakeInstrumentedTest.kt` использует настоящий libsignal JNI и production `PrekeyBundle.toLibsignal()` mapping: handshake/reply после повторного открытия store, optional EC one-time key, Kyber one-time и last-resort fallback для разных sender devices, отказ malformed/signature-invalid bundle, replay tuple и rollback failed decrypt. `Android/core/src/androidTest/java/com/barkfluff/client/crypto/PrekeyManagerInstrumentedTest.kt` покрывает stable registration/replenishment retry, подтверждение marker через own status, старый сервер, serialized bootstrap, rotation/retention, legacy identity/session migration, lost-ack rotation с reopen и отклонением несовпадающего ключа, no-change status RPC count и logout во время upload. `BarkFluffSignalStoreInstrumentedTest.kt` проверяет rollback с reopen после прерванной операции и сохранение старого snapshot плюс fail-closed поведение при отклонённом durable commit. Repository instrumentation дополнительно покрывает initial history handoff, прерывание после cache upsert до replay marker, delayed invite, pending accept RPC retry, expiry/reinvite с тем же chat ID, отсутствие resurrection после clear и logout во время bundle fetch/handoff, scrub pending initial при reject/reinvite с сохранением старого invite ID, ack вне history lock и восстановление после сбоя crypto scrub при clear. Store test проверяет failed cleanup/retry без credentials, изоляцию другого scope, очистку unmigrated legacy-файлов при пустом и null scope и независимый retry каждой записи журнала. JVM-тесты app (`CryptoExitSequenceTest`, `SecretSendOutcomeTest`) проверяют gating REJECTED/UNAVAILABLE, завершение logout после отмены и разделение успешной отправки и ошибки истории; `ChatCacheRepositoryClearTest` — видимость SQLCipher записи после возврата suspend API и idempotent upsert. Эти instrumentation tests требуют Android runtime; compile или assemble не доказывает, что JNI сценарии прошли.

Сборка и выборочный запуск на изолированном instrumentation target из `Android/`:

```powershell
.\gradlew.bat :core:assembleDebugAndroidTest
adb devices -l
.\gradlew.bat :core:connectedDebugAndroidTest -Pandroid.testInstrumentationRunnerArguments.class=com.barkfluff.client.repository.PqxdhHandshakeInstrumentedTest
```

Для двухустройственного smoke нужен отдельный dev Users/Messages/Updates backend с поддержкой PQXDH и два disposable Android устройства/эмулятора, авторизованных в тестовых аккаунтах. Собери `:app-v1:assembleNightlyDebug`, установи один и тот же build на оба устройства, включи `secretChatsEnabled` в Testing Settings (переключатель запускает bootstrap; без него bootstrap, retry и invite UI выключены), войди в отдельные аккаунты, дождись регистрации обоих prekey pools, затем на A создай secret chat к device B. На B явно прими приглашение; проверь доставку начального текста и двусторонних сообщений, force-stop/reopen обоих клиентов и повторный обмен. Для last-resort пути в изолированной среде исчерпай Kyber one-time pool перед новым приглашением; не выполняй этот шаг на production или рабочем аккаунте. Build и instrumentation compile проверяют клиентские границы, но только этот smoke проверяет реальные gRPC streams, Users status/fetch и Messages accept/send между устройствами.

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
| Messages read-range concurrency/outbox | `Tests/BarkFluff.Messages.Tests/Persistence/MessagesStorageReadPostgresTests.cs`: `BARKFLUFF_SEARCH_POSTGRES`, disposable PostgreSQL; concurrent range reads, rollback, persisted retry и приоритет NewMessage |
| Users handler-тесты | `Tests/BarkFluff.Users.Tests/TestHelper.cs`: EF InMemory |
| Users device concurrency | `Tests/BarkFluff.Users.Tests/Features/Devices/DevicesStorageConcurrencyTests.cs`: SQLite с несколькими соединениями |
| Messages PostgreSQL search | `Tests/BarkFluff.Messages.Tests/Persistence/SearchMessagesPostgresTests.cs`: `BARKFLUFF_SEARCH_POSTGRES`, disposable DB и право CREATE DATABASE |
| Developers concurrent seed | `Tests/BarkFluff.Developers.Tests/PostgresConcurrentSeedTests.cs`: `DEVELOPERS_TEST_POSTGRES`; инфраструктура — `TestInfrastructure.cs` |

PostgreSQL integration-тесты условно пропускаются без соответствующих env; backend CI предоставляет обе переменные через изолированный PostgreSQL service. Их отсутствие в локальном прогоне не означает проверенную PostgreSQL-семантику. SQLite полезен и для Messages/Users, но не заменяет PostgreSQL-specific raw SQL.

`Tests/BarkFluff.Users.Tests/Features/Prekeys/PrekeyPostgresTests.cs` использует `BARKFLUFF_PREKEY_POSTGRES` как admin connection к disposable PostgreSQL с правом `CREATE DATABASE`; каждый сценарий создаёт и удаляет собственную тестовую БД. Набор проверяет конкурентный fetch, Kyber exhaustion/fallback, tombstone re-upload/conflicts для обоих пулов, регистрацию и replacement с повторным использованием consumed IDs обоих пулов под новой identity, fetch-races, own-device status/ownership, cascade deletion и upgrade старой Users-схемы. `.github/workflows/test-backend.yml` передаёт эту переменную из того же изолированного PostgreSQL 16 service. Без неё эти integration-тесты пропускаются; InMemory не доказывает блокировки, `UPDATE ... RETURNING` и миграцию PostgreSQL.

## Известные ограничения

- `Tests/BarkFluff.Users.Tests/Features/Prekeys/FetchPrekeyBundleQueryHandlerTests.cs` содержит явно пропущенный кейс: EF InMemory не выполняет raw SQL `UPDATE … RETURNING`. Для проверки этого поведения нужен PostgreSQL. Точный путь сверять по дереву тестов при переносе feature.
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
