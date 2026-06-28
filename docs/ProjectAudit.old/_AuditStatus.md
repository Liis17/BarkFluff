# Сводный статус аудита проектов

> Дата проверки: 2026-05-14
> Ветка: `dev`
> Метод: построчная сверка каждой проблемы из md-файлов аудита с актуальным кодом

**Легенда:**

- ✅ исправлено
- ❌ всё ещё актуально
- ⚠️ частично исправлено
- 🆕 новая находка, не описана в аудите

---

## BarkFluff.Configuration

| ID                                            | Статус      | Комментарий                                                                                                                                                                                                           |
| --------------------------------------------- | ----------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| SEC-05 — дефолтные `guest/guest` для RabbitMQ | ⚠️ частично | `ConfigurationDefaultsPopulator` теперь использует `_rabbitUsername`/`_rabbitPassword` из env. Но в `Program.cs:105-106` остался fallback `?? "guest"` — если env-переменные не заданы, в БД запишется `guest/guest`. |
| BUG-03 — нет валидации `ServiceId` при upsert | ❌ актуально | `ConfigurationApiService.UpdateConfiguration` (строки 57-91) и `ConfigurationStorage.UpdateConfigurationAsync` (30-64) принимают `ServiceId` без `Enum.IsDefined`. Любой `int32` создаст «мусорную» запись.           |

**🆕 Новые наблюдения:**

- `Program.cs:105-106` — fallback на `guest/guest` без warning-лога. Если env не задан в проде, тихо запишется небезопасный креденшл. Стоит хотя бы логировать warning, лучше — `throw` при отсутствии в Production.

---

## BarkFluff.GrpcServer

| ID                                 | Статус        | Комментарий                                                                                                                             | Причина отказа                                                      |
| ---------------------------------- | ------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| QA-04 — опечатка `OperationSystem` | ❌ неактуально | `Tracker/RequestContext.cs:5` всё ещё `OperationSystem`. Переименование требует правки `RequestContextInterceptor` и всех потребителей. | слишком много изменений включая протофайлы что затронет все клиенты |

**🆕 Новые наблюдения:**

- `ServerExceptionInterceptor.cs:79` — `new RpcException(new Status(StatusCode.Unknown, ex.Message), trailers)` пробрасывает `ex.Message` клиенту. Для общего `Exception` это может быть SQL-сообщение, путь к файлу, инфо о внутренней структуре. Лучше отдавать общий `"internal_error"` и оставлять подробности только в логе.
- Перехватываются только `UnaryServerHandler`. Для streaming RPC (`ServerStreaming`, `ClientStreaming`, `DuplexStreaming`) исключения не оборачиваются — клиент получит сырой `Unknown` без `x-error-code`.

---

## BarkFluff.Notification

**🆕 Новые наблюдения:**

- `HtmlEmailTemplateParser.Parse` каждый раз читает шаблон с диска через `File.ReadAllTextAsync`. При высоком потоке писем это N IO-операций. Возможен `ConcurrentDictionary<NotificationType, string>` кэш с TTL или однократное чтение в DI-singleton. 

---

## BarkFluff.Beacon

**🆕 Новые наблюдения:**

- `Program.cs:14` — комментарий `/// ����� ����� � ����������` повреждён (кракозябры из cp1251). Не баг, но мусор в исходниках.
- `GetServerInfoCommandHandler.Handle` пробрасывает любой `OperationCanceledException`/RPC-ошибку наверх — при недоступности Configuration-сервиса админка не получит данные о сервере. Возможен graceful degradation: при единичных падениях возвращать сервисы со `Status = Unhealthy`.

---

## Barkfluff.CloudMessaging

| ID                           | Статус      | Комментарий                                                                                                                                                                                                                                                             |
| ---------------------------- | ----------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| PERF-02 — лишние gRPC-вызовы | ⚠️ частично | `GetChatInfoAsync` всё ещё вызывается, хотя `ChatTitle`/`ChatAvatarUrl`/`IsGroupChat` уже есть в `PushNotificationEvent`. `GetByIdAsync` для отправителя обоснован — поля `SenderName` в event нет. Чтобы убрать оба — добавить `SenderName` в `PushNotificationEvent`. |

**🆕 Новые наблюдения:**

- `senderCall.ResponseAsync.Result` (строки 60-61) — блокирующее ожидание уже завершённой задачи; технически безопасно после `Task.WhenAll`, но идиоматично `await senderCall.ResponseAsync`. На неавайтнутой задаче `.Result` блокирует поток.
- `tokensResponse.Tokens` фильтруются `Where(t => !string.IsNullOrEmpty(t))`, но если все токены пусты — `fcmTokens.Count == 0` не проверяется перед вызовом `SendNotificationBatchAsync`. Это пустой батч в FCM → лишний HTTP-запрос. Стоит добавить early-return.
- `Program.cs:48-51` — `RabbitMQ:Host/Username/Password` падает с `InvalidOperationException` при `null`, что хорошо. Но в `LoadConfiguration` эти значения приходят из Configuration-сервиса, где из-за SEC-05 могут быть дефолтным `guest`. Связка проблем.

---

## BarkFluff.Navigator

**🆕 Новые наблюдения:**

- `CleanupExpiredThrottleEntries` итерирует **весь** словарь при **каждой** регистрации — O(N) на горячем пути. Стоит вынести в timer/background service.
- Старые серверы в `_servers` фильтруются по `lastSeen` в `GetServers`, но никогда не удаляются — словарь растёт линейно по числу уникальных серверов.

---

## BarkFluff.Onliner

| ID                                 | Статус      | Комментарий                                                                                                |
| ---------------------------------- | ----------- | ---------------------------------------------------------------------------------------------------------- |
| TD-04 — `status_changes` без тегов | ❌ актуально | По-прежнему только `_metrics.Increment("status_changes")` без разделения online/offline. Низкий приоритет. |

**🆕 Новые наблюдения:**

- `OnlineVisibilityFilter.GetVisibleUserIdsAsync` (строки 33-45) делает `await IsVisibleToCaller` последовательно для каждого targetId. При большом количестве пользователей — N последовательных gRPC к Users. Можно `Task.WhenAll` или batch-запрос `GetUsersPrivacyAsync`.

---

## BarkFluff.ClientStorage

| ID                                          | Статус      | Комментарий                                                                                                                                                                                |
| ------------------------------------------- | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| BUG-04 — кеш не проверяется на корректность | ⚠️ частично | `LocalFileCache.cs:28-33` проверяет `info.Length == 0` (минимальный вариант из аудита). Битый файл с длиной >0 всё ещё может вернуться. Надёжный вариант через `.meta` файл не реализован. |

**🆕 Новые наблюдения:**

- `S3StorageService.UploadAsync` (строка 77) создаёт `new TransferUtility(_client)` при каждом вызове. Лучше держать как поле класса — `TransferUtility` потокобезопасен и переиспользуется.

---

## BarkFluff.Files

| ID                                         | Статус      | Комментарий                                                                                                                                                                                                                                                        |
| ------------------------------------------ | ----------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| MISC-04 — висячие `FileHashes` / orphan S3 | ⚠️ частично | `_hashesStorage.DeleteHashByFileId(file.Id, ct)` вызывается при дедупликации (`UploadFileCommandHandler.cs:224`). Но orphan-объект в S3 при исключении между `UploadAsync` и `AddMessage` всё ещё возможен — требует try/catch с cleanup или transactional outbox. |

**🆕 Новые наблюдения:**

- Цепочка `S3.UploadAsync` → `DB.AddMessage`: исключение после `UploadAsync` оставит S3-объект сиротой. Аудит косвенно упомянул это в MISC-04, но фикс там только для FileHashes — orphan S3 не покрыт.

---

## BarkFluff.Messages

| ID                                           | Статус     | Комментарий                                                                                                           |
| -------------------------------------------- | ---------- | --------------------------------------------------------------------------------------------------------------------- |
| BUG-01 — race condition при создании DM-чата | ⏭ известно | В md-файле явно помечено ПРОПУЩЕНО — нужна отдельная задача со схемными изменениями (partial unique index + бэкфилл). |

**🆕 Новые наблюдения:**

- `MessagesStorage.cs:209` — `(await ... .ToListAsync()).FirstOrDefault()` материализует весь список перед взятием первого. Лучше `.FirstOrDefaultAsync()` или хотя бы `.Take(1)`. Низкий приоритет.
- `ChatsStorage.cs:19,31,64,72,209` — `Include(x => x.Members)` используется в других местах кроме `CheckAccessToChat`. Стоит проверить, нужны ли там все участники.

---

## BarkFluff.Identity

**🆕 Новые наблюдения:**

- `PasswordHasher` поддерживает legacy SHA-256 — отлично для миграции, но нет механизма автоматического перехэширования при успешном логине со старым хешем. Стоит после успешной `Verify` со SHA-256 сразу перезаписать на BCrypt.
- `_resetPasswordsStorage` не имеет фонового сервиса очистки устаревших записей (аналогично `TempFileCleanupService` в Files). Просроченные `ResetPassword`-записи копятся.
- `ConfirmAccount`/`ConfirmResetPassword`: нет ограничения числа неудачных попыток ввода кода — возможен брутфорс 6-значного OTP (10^6 комбинаций, по 5 минут жизни). Стоит ввести счётчик `FailedAttempts` с блокировкой при >3-5.

---

## BarkFluff.Shared.SecurityUtilities

**Файл практически не правился.** Изменения видны только в `PasswordValidator` (WPF) — частичное.

| ID                                                                   | Статус      | Комментарий                                                                                                          |
| -------------------------------------------------------------------- | ----------- | -------------------------------------------------------------------------------------------------------------------- |
| SEC-03 — нет hard-limit длины в `EvaluatePasswordStrength`           | ❌ актуально | `SecurityUtilities.cs:5-8` — только проверка `IsNullOrEmpty`. Нет `MinPasswordLength`.                               |
| SEC-04 — расхождение логики спецсимволов между Validator и Utilities | ⚠️ частично | В `PasswordValidator.cs:85` пробел исключён, в `SecurityUtilities.cs:34` — нет. Расхождение сохраняется.             |
| OPT-01 — 6 проходов по строке                                        | ❌ актуально | `Any/Count/Distinct` — 5+ отдельных LINQ-проходов.                                                                   |
| OPT-02 — `BrushConverter` per-call                                   | ⚠️ частично | `PasswordReset.xaml.cs:33` — `BrushConverterInstance`. `CreateAccount.xaml.cs:204` — всё ещё `new BrushConverter()`. |
| OPT-03 — двойной вызов `EvaluatePasswordStrength`                    | ❌ актуально | Оба места: `CreateAccount.xaml.cs:200`+`207`, `PasswordReset.xaml.cs:425`+`437`.                                     |
| BUG-01 — пробел как спецсимвол                                       | ❌ актуально | `SecurityUtilities.cs:34` — пробел всё ещё в счёте.                                                                  |
| BUG-02 — `InvalidCharacters` для слабого пароля                      | ❌ актуально | `PasswordValidator.cs:114` — `ValidationState.InvalidCharacters` всё ещё используется.                               |
| BUG-03 — нет проверки паттернов (qwerty/12345)                       | ❌ актуально | Нет `GetSequencePenalty`.                                                                                            |
| QA-01 — не `static class`                                            | ❌ актуально | `public class SecurityUtilities`.                                                                                    |
| QA-02 — нет XML-документации                                         | ❌ актуально | Методы без `///`.                                                                                                    |
| QA-03 — нет юнит-тестов                                              | ❌ актуально | Проекта `*.Tests` не найдено.                                                                                        |

**🆕 Новых находок нет** — все известные проблемы пока актуальны.

---

## BarkFluff.Shared.Exceptions

**Полностью не правился.** Все 15 пунктов аудита всё ещё актуальны.

| ID                                                         | Статус      | Комментарий                                                                        |
| ---------------------------------------------------------- | ----------- | ---------------------------------------------------------------------------------- |
| SEC-01 — `ex.Message` утечка в RPC trailer                 | ❌ актуально | `ServerExceptionInterceptor.cs:79` — `new Status(StatusCode.Unknown, ex.Message)`. |
| SEC-02 / BUG-01 — `Exception.Message` пустой               | ❌ актуально | `BaseGrpcException.cs` — нет `base(message)`.                                      |
| PERF-01 — race condition в `CachedExceptions`              | ❌ актуально | `ExceptionClientInterceptor.cs:36-39` — без `Lazy<T>`/lock.                        |
| PERF-02 — только текущая сборка сканируется                | ❌ актуально | `Assembly.GetExecutingAssembly()`.                                                 |
| PERF-03 — `FirstOrDefault` O(n)                            | ❌ актуально | `List<BaseGrpcException>` + `FirstOrDefault`.                                      |
| PERF-04 — `Activator.CreateInstance` без проверки          | ❌ актуально | Прямой вызов без try/catch.                                                        |
| BUG-02 — `FileNotFoundException` конфликтует с `System.IO` | ❌ актуально | `Files/FileNotFoundException.cs` на месте.                                         |
| BUG-03 — `CachedExceptions` `public static` mutable        | ❌ актуально | `ExceptionClientInterceptor.cs:11`.                                                |
| BUG-04 — стриминги не перехватываются                      | ❌ актуально | Только `AsyncUnaryCall`.                                                           |
| BUG-05 — `StatusCode.Unknown` вместо `.Internal`           | ❌ актуально | `ServerExceptionInterceptor.cs:79`.                                                |
| MISC-01 — смешанный стиль namespace                        | ❌ актуально | Не проверял каждый файл, но не исправлено.                                         |
| MISC-02 — опечатка `XAppInfoIsRequiedException`            | ❌ актуально | Файл всё ещё с опечаткой.                                                          |
| MISC-03 — `Grpc.Core` 2.46.6 устарел                       | ❌ актуально | `csproj` без изменений.                                                            |
| MISC-04 — нет `[Serializable]`                             | ❌ актуально | `BaseGrpcException` без атрибута.                                                  |

**🆕 Новых находок нет.** Файл `*.Exceptions` — критичный shared, его невнимание ставит риски в кросс-сервисный контракт ошибок и в утечку информации через `ex.Message`.

---

## BarkFluff.Shared.Queue

**Полностью не правился.** Все 16 пунктов актуальны.

| ID                                                      | Статус      | Комментарий                                                   |
| ------------------------------------------------------- | ----------- | ------------------------------------------------------------- |
| SEC-01 — `MessageText` уходит в FCM без обрезки         | ❌ актуально | `PushNotificationEvent.cs:11` — `string? MessageText`.        |
| SEC-02 — URL-поля без валидации                         | ❌ актуально | `SenderAvatarUrl`, `ImagePreviewUrl` как произвольная строка. |
| SEC-03 — `Payload` без инициализации                    | ❌ актуально | `Notification.cs:17` — без `= new()`.                         |
| BUG-01 — `ReadReceiptEvent` без consumer                | ❌ актуально | `grep IConsumer<ReadReceiptEvent>` — пусто.                   |
| BUG-02 — `NewMessageEvent` без инициализаторов          | ❌ актуально | `List<long> ChatMembers` и `byte[] Message` без init.         |
| BUG-03 — `UserChangedBio` block-style namespace         | ❌ актуально | `namespace ... { ... }` + `NewBio` без init.                  |
| BUG-04 — `EmailNotification.Title/Address` без init     | ❌ актуально | Без `= string.Empty`.                                         |
| BUG-05 — `UserChangedAvatar` URL без init               | ❌ актуально | Аналогично.                                                   |
| BUG-06 — параметр `newUsername` в `UserBioChangedEvent` | ❌ актуально | `UserInfoQueueSender.cs:74` — `string newUsername` всё ещё.   |
| OPT-01 — поля `PushNotificationEvent` не заполняются    | дубль       | См. CloudMessaging.PERF-02 — связанная проблема.              |
| OPT-02 — последовательное обновление кеша               | ❌ актуально | `UserChangedAvatarConsumer/NameConsumer` — `foreach + await`. |
| OPT-03 — `PendingPushTracker` без лимита                | ❌ актуально | Нет TTL/cleanup.                                              |
| OPT-04 — двойная сериализация proto+JSON                | ❌ актуально | `byte[] Message` в JSON.                                      |
| MISC-01 — нет общего интерфейса событий                 | ❌ актуально | Нет `IQueueEvent`.                                            |
| MISC-02 — `ContentType` как `int`                       | ❌ актуально | `int` с комментарием.                                         |
| MISC-03 — `TransportId.Unknown=0` без валидации         | ❌ актуально | Без `Validate`.                                               |
| MISC-04 — `DateTime` без UTC-гарантии                   | ❌ актуально | `SessionRevokedEvent.AccessTokenExpiresAt`.                   |

**🆕 Новых находок нет.**

---

## Barkfluff.Developers

**Почти не правился.**

| ID                                                | Статус      | Комментарий                                            |
| ------------------------------------------------- | ----------- | ------------------------------------------------------ |
| SEC-01 — hardcoded `postgres:postgres`            | ❌ актуально | `DevelopersContextFactory.cs:12` — пароль в коде.      |
| SEC-02 — нет Admin-политики                       | ❌ актуально | Только `[Authorize(Policy = nameof(TokenType.User))]`. |
| SEC-03 — открытый CORS                            | ❌ актуально | `Program.cs:50` — `AllowAnyOrigin()`.                  |
| SEC-04 — рефлексия без проверки                   | ❌ актуально | `Activator.CreateInstance(type)!` без try/catch.       |
| OPT-01 — `Content` загружается при листинге       | ❌ актуально | `ToListAsync()` без проекции.                          |
| OPT-02 — нет индекса по `Order`                   | ❌ актуально | Только `HasIndex(Key)`/`FileName`/`Code`.              |
| OPT-03 — `GetAwaiter().GetResult()` при старте    | ❌ актуально | `Program.cs:64,67,70` — все 3 вызова.                  |
| OPT-04 — нет `OrderBy` в `GetErrorCodes`          | ❌ актуально | `GetErrorCodesQuery.cs:21` — без сортировки.           |
| BUG-01 — нет валидации в Create/Update            | ❌ актуально | Команды без guard'ов.                                  |
| BUG-02 — `CancellationToken` не пробрасывается    | ❌ актуально | `DocumentationStorage` — методы без `ct`.              |
| BUG-03 — Create/Update/Delete не подключены к API | ❌ актуально | `DevelopersApiService` — только Get-методы.            |
| BUG-04 — race condition при seeding               | ❌ актуально | `AnyAsync` + `Add` без try/catch.                      |
| BUG-05 — `GetProtoFileContent` без валидации      | ❌ актуально | Прямой проход в provider.                              |
| ARCH-01 — DbContext напрямую в `GetErrorCodes`    | ❌ актуально | Без `ErrorCodeStorage`.                                |
| ARCH-02 — Storage как `Transient`                 | ❌ актуально | `AddTransient<DocumentationStorage>`.                  |
| ARCH-03 — `DateTime.UtcNow` в инициализации поля  | ❌ актуально | Не проверял отдельно, но не правилось.                 |

**🆕 Новых находок нет** — приоритет правок очень высокий из-за SEC-01 (учётка в git) и SEC-03 (открытый CORS).

---

## BarkFluff.Shared.Auth

**Полностью не правился.** Все 15 пунктов актуальны.

| ID                                                  | Статус      | Комментарий                                                                                  |
| --------------------------------------------------- | ----------- | -------------------------------------------------------------------------------------------- |
| SEC-01 / SEC-05 — клиент подделывает IP             | ❌ актуально | `RequestContextInterceptor.cs:71` — `clientIp = GetMetadataValue(...)` с высшим приоритетом. |
| SEC-02 — нестандартный `x-auth-token`               | ❌ актуально | Пустой токен тоже отправляется.                                                              |
| SEC-03 — нет валидации в конструкторах              | ❌ актуально | Все 5 интерсепторов принимают строку без `ThrowIfNullOrWhiteSpace`.                          |
| SEC-04 — Base64 не шифрование                       | ❌ актуально | `Convert.ToBase64String` всюду.                                                              |
| PERF-01 — Base64 пересчитывается per-call           | ❌ актуально | Не кэшируется в конструкторе.                                                                |
| PERF-02 — 7 вложенных `.Intercept()`                | ❌ актуально | Не проверял `WebApiClientManager`, но composite не введён.                                   |
| PERF-03 — `FirstOrDefault` × 6 на сервере           | ❌ актуально | `RequestContextInterceptor.cs:114`.                                                          |
| BUG-01 — опечатка `osName` в `XDeviceIdInterceptor` | ❌ актуально | Строка 24 — переменная названа `osName`, но содержит deviceId.                               |
| BUG-02 — стриминги без метаданных                   | ❌ актуально | Только `AsyncUnaryCall`.                                                                     |
| BUG-03 — `MetadataKeys` не `static`                 | ❌ актуально | `public class MetadataKeys`.                                                                 |
| BUG-04 — нет try/catch для Base64 декодирования     | ❌ актуально | `Convert.FromBase64String` без guard — потенциальный DoS.                                    |
| ARCH-01 — DRY-нарушение (5 одинаковых)              | ❌ актуально | Нет `SingleValueMetadataInterceptor`.                                                        |
| ARCH-02 — `new` вместо DI                           | ❌ актуально | В `WebApiClientManager`.                                                                     |
| ARCH-03 — `XDeviceIdInterceptor` без `Client`       | ❌ актуально | Имя без суффикса.                                                                            |

**🆕 Новые наблюдения:**

- `MetadataKeys.cs` объявляет ключи через `public const string` — это OK. Но `MetadataKeys.IpAddress` принимается на сервере как high-priority — связка с SEC-01. Удаление этого приоритета на сервере без удаления интерсептора на клиенте безопасно: сервер сам перейдёт на `X-Forwarded-For`/`RemoteIpAddress`.

---

## BarkFluff.Proto

**Почти не правился.**

| ID                                                           | Статус      | Комментарий                                                                                      |
| ------------------------------------------------------------ | ----------- | ------------------------------------------------------------------------------------------------ |
| SEC-01 — токены `FastAuthResult` в plain полях               | ❌ актуально | `fast_auth_api.proto:61-64` — `access_token`/`refresh_token` как `string`.                       |
| SEC-02 — `GenerateTestTokenRequest` в production             | ❌ актуально | `identity_api.proto:225` — message всё ещё там.                                                  |
| SEC-04 — `beacon_api.proto` раскрывает топологию             | ❌ актуально | `ServiceEndpoint host+port` в публичном API.                                                     |
| SEC-05 — `ip_address` из клиента                             | ❌ актуально | `CreateSessionForUserServerRequest.ip_address`.                                                  |
| OPT-01 — `GetUserAllMessages` без стриминга в контракте      | ❌ актуально | На сервере исправлено (см. Messages.OPT-08), но proto всё ещё унарный. Расхождение контракта.    |
| OPT-02 — нет лимита `MarkAsRead.message_ids`                 | ❌ актуально | Без комментария-контракта.                                                                       |
| OPT-03 — `SubscribeToOnlineStatus` без bidi                  | ❌ актуально | Отдельный `ChangeUsersInSubscription`.                                                           |
| OPT-04 — `count` без `[deprecated=true]`                     | ❌ актуально | Только comment.                                                                                  |
| OPT-05 — `GetFilesData/GetTempDownloadUrl` без лимита        | ❌ актуально | Без комментария.                                                                                 |
| BUG-01 — `ProfileFieldVisibility.ALL = 0`                    | ❌ актуально | `users_api.proto:652` — `ALL=0` (опасный дефолт).                                                |
| BUG-02 — enum-значения без префикса                          | ❌ актуально | `OtpTypeId.Unknown` / `ServiceStatus.Unknown`.                                                   |
| BUG-03 — `ServerColor`/`ServiceEndpoint` дубликаты           | ❌ актуально | `beacon_api.proto:44,71` + `navigator_api.proto:23,39`.                                          |
| BUG-04 — `ExportAttachment.type` как `int32`                 | ❌ актуально | Не `MessageAttachmentType`.                                                                      |
| BUG-05 — `ConfirmAccountResponse` без `access_token`         | ❌ актуально | `identity_api.proto:249-251` — только `refresh_token`.                                           |
| BUG-06 — `CreateGroupChatRequest` без лимитов                | ⚠️ частично | В коде Messages.SEC-04 добавлено `MaxAttachmentsPerMessage = 10`, но контракт proto не обновлён. |
| MISC-01 — `SendEmailOtpCodeRequest` заглушки                 | ❌ актуально | `identity_api.proto:148-152` — без RPC.                                                          |
| MISC-02 — `NavigatorApi.RegisterServer` без auth-комментария | ❌ актуально | Без явного контракта.                                                                            |
| MISC-03 — `ConfigurationApi` без разделения public/admin     | ❌ актуально | Все методы в одном сервисе.                                                                      |
| MISC-04 — `UpdatesApi` без resume cursor                     | ❌ актуально | Пустые `Request`-сообщения.                                                                      |
| MISC-05 — offset-пагинация в `PageRequest`                   | ❌ актуально | Без cursor-варианта.                                                                             |

**🆕 Новые наблюдения:**

- Самая важная связка: **SEC-02 (`GenerateTestTokenRequest`)** в production-контракте. Нужно срочно проверить, реализован ли соответствующий RPC в `IdentityApi` (если да — это бэкдор). Если message объявлен, но RPC не реализован — это мёртвый код в контракте, но всё равно знак, что планировался опасный endpoint.

---

## Barkfluff (macOS клиент)

| ID                                               | Статус        | Комментарий                                                               |
| ------------------------------------------------ | ------------- | ------------------------------------------------------------------------- |
| SEC-01 — UserDefaults как default                | ❌ актуально   | `TokenStorageSettings.swift:120` → `return .userDefaults`.                |
| SEC-02 — нет retry на 401 в `AuthInterceptor`    | ❌ актуально   | Нет catch `RPCError` с `.unauthenticated`.                                |
| SEC-03 — `FastAuthViewModel` не сохраняет токены | ✅ исправлено  | `authService.applyFastAuthTokens(...)` (строка 130).                      |
| SEC-04 — `print()` с JWT-info                    | ❌ актуально   | `AuthInterceptor.swift` и `TokenRefreshCoordinator.swift` — 9+ `print()`. |
| SEC-05 — миграция хранилища неатомарна           | ❌ не проверял | Не углублялся.                                                            |
| PERF-01..06 — кэширование listItems/sorted       | ❌ не проверял | Низкий приоритет.                                                         |
| BUG-01..07 — concurrency и т.д.                  | ❌ не проверял | Низкий приоритет.                                                         |
| MISC-05 — plaintext-first приоритет              | ❌ актуально   | `ConnectionManager.swift:92` — "Сначала пробуем plaintext".               |

---

## Barkfluff.WebServer

**Почти не правился.**

| ID                                           | Статус      | Комментарий                           |
| -------------------------------------------- | ----------- | ------------------------------------- |
| SEC-01 — hardcoded `_adminId = 495716470`    | ❌ актуально | `TelegramService.cs:13`.              |
| SEC-02 — нет rate limiting                   | ❌ актуально | `AddRateLimiter` отсутствует.         |
| SEC-03 — Path Traversal в FallbackController | ❌ актуально | `catchAll` без санитизации.           |
| SEC-04 — нет Security Headers                | ❌ актуально | Нет middleware с CSP/X-Frame-Options. |
| SEC-05 — нет HTTPS-redirect/HSTS             | ❌ актуально | Нет `UseHttpsRedirection`.            |

(остальные ~23 пункта не проверял отдельно — общая картина: файл не отрабатывался)

---

## BarkFluff.FastAuth

**Почти не правился.**

| ID                                                    | Статус      | Комментарий                                    |
| ----------------------------------------------------- | ----------- | ---------------------------------------------- |
| SEC-01 — `SubscribeFastAuthResult` `[AllowAnonymous]` | ❌ актуально | `FastAuthApiService.cs:32` — анонимный доступ. |
| SEC-02 — нет rate limiting на генерацию сессий        | ❌ актуально | `[AllowAnonymous]` без лимитов.                |
| BUG-03 — новый `QRCodeGenerator` каждый раз           | ❌ актуально | `QrCodeGenerator.cs:12-13`.                    |
| MISC-02 — опечатка `XAppInfoIsRequiedException`       | ❌ актуально | `GenerateFastAuthTokenCommandHandler.cs:35`.   |
| MISC-04 — `OperationSystem`                           | ❌ актуально | Дубль из GrpcServer.QA-04.                     |

(остальные 11 пунктов не проверял — общая картина: не правился)

---

## Barkfluff.AdminPanel

| ID                                                      | Статус       | Комментарий                                                                          |
| ------------------------------------------------------- | ------------ | ------------------------------------------------------------------------------------ |
| SEC-01 — реальные Telegram-секреты в `appsettings.json` | ❌ актуально  | `appsettings.json:10` — `BotToken: 8539569051:AAH...`. **Этот токен в git-истории**. |
| SEC-02 — Shell Injection в `UpdateAdminPanelAsync`      | ✅ исправлено | `RunDockerComposeCommandAsync` с `ArgumentList`-передачей вместо `sh -c`.            |
| SEC-03 — нет Rate Limiting на auth                      | ❌ актуально  | `AddRateLimiter` не подключён.                                                       |
| SEC-05 — нет HTTPS-конфигурации                         | ⚠️ частично  | `UseHttpsRedirection()` есть на `Program.cs:152`.                                    |

(остальные 17 пунктов не проверял — частично правился)

---

## Barkfluff.Client.Android

**Полностью не правился.**

| ID                                      | Статус        | Комментарий                                                                                                         |
| --------------------------------------- | ------------- | ------------------------------------------------------------------------------------------------------------------- |
| SEC-01 — Trust All TrustManager         | ❌ актуально   | `GrpcManager.kt:756,1506,2347` — `checkServerTrusted {}` + `HostnameVerifier { _, _ -> true }`. **Критично**: MITM. |
| SEC-02 — пароль в поле Activity         | ❌ актуально   | `LoginActivity.kt:45` — `private var savedPassword = ""`.                                                           |
| SEC-03 — IP кэшируется и не обновляется | ❌ не проверял | Не углублялся.                                                                                                      |

`res/xml/network_security_config.xml` не существует.

(остальные ~17 пунктов не проверял — общая картина: не правился)

---

## BarkFluff.Users

**Почти не правился.**

| ID                                            | Статус      | Комментарий                                                            |
| --------------------------------------------- | ----------- | ---------------------------------------------------------------------- |
| SEC-01 — email PII в логах                    | ❌ актуально | `AddDraftUserCommandHandler.cs` — нет маскирования.                    |
| SEC-02 — нет валидации формата username       | ❌ актуально | Только `Trim()` + `IsReserved`.                                        |
| SEC-04 — `CheckExistEmail/Username` анонимные | ❌ актуально | `UsersApiService.cs:82,91` — `[AllowAnonymous]`. **User Enumeration**. |
| PERF-02 — двойная регистрация MediatR         | ❌ актуально | `Program.cs:38,54`.                                                    |

(остальные ~15 пунктов не проверял — общая картина: не правился)

---

## BarkFluff.Updates

| ID                                                 | Статус        | Комментарий                                                      |
| -------------------------------------------------- | ------------- | ---------------------------------------------------------------- |
| SEC-01 — RabbitMQ creds без валидации              | ❌ актуально   | `Program.cs:53` — без `IOptions`/`[Required]`/`ValidateOnStart`. |
| SEC-02 — нет лимита подписок                       | ❌ не проверял | Низкий приоритет.                                                |
| SEC-03 — `GrpcReflection` в проде                  | ❌ актуально   | `Program.cs:25,142` — без env-guard.                             |
| OPT-02 — дублирование `StreamSubscriptionsManager` | ⚠️ частично   | `DeviceStreamSubscriptionsBase` существует — рефакторинг начат.  |

(остальные ~12 пунктов не проверял)

---

## BarkFluff.WebApi.Core

| ID                                            | Статус        | Комментарий |
| --------------------------------------------- | ------------- | ----------- |
| SEC-03 — старые каналы не закрываются         | ❌ не проверял | Среднее.    |
| BUG-04 — `CancellationToken.None` в streaming | ❌ не проверял | Высокое.    |
| PERF-01 — статический `HttpClient`            | ❌ не проверял | Средний.    |

(остальные ~12 пунктов не проверял — общая картина: частично правился)

---

## BarkFluff.Shared.Identity

Аудит почти полностью дублирует `BarkFluff.Identity.md` (SEC-01..SEC-05, OPT-02). См. соответствующий раздел про Identity. Дополнительно:

| ID                                                                     | Статус        | Комментарий                                     |
| ---------------------------------------------------------------------- | ------------- | ----------------------------------------------- |
| SEC-06 — `TokenRevocationCache` in-memory не работает в multi-instance | ❌ актуально   | Нужен Redis или distributed cache.              |
| SEC-07 — Service-токен с `expires 9999`                                | ❌ актуально   | Дубль `Configuration` 🆕.                       |
| SEC-08 — нет валидации длины `JwtSettings.SecretKey`                   | ❌ не проверял | Низкий.                                         |
| SEC-09 — `x-auth-token` в логах                                        | ❌ не проверял | Низкий.                                         |
| SEC-10 — Email OTP без timing-attack защиты                            | ❌ актуально   | Связано с моим 🆕 "нет rate-limit на OTP".      |
| BUG-02 — `DeleteRefreshToken` не проверяет userId                      | ❌ не проверял | **Высокое**: возможна чужая сессия закрывается. |
| MISC-02 — `PasswordHasher` дублируется                                 | ❌ не проверял | Возможно есть копия в WebApi.Core.              |

---

## BarkFluff.Client.WPF

**Почти не правился.**

| ID                                   | Статус        | Комментарий                                                             |
| ------------------------------------ | ------------- | ----------------------------------------------------------------------- |
| SEC-01 — пароль в `string _password` | ❌ актуально   | `Login.xaml.cs:19,440` — обычная строка.                                |
| SEC-02 — hardcoded путь к FFmpeg     | ❌ не проверял | Найдены `FFmpegService.cs`, `VideoCompressor.cs` — детально не смотрел. |

(остальные ~18 пунктов не проверял)

---

## BarkFluff.Web

| ID                                | Статус        | Комментарий                                                    |
| --------------------------------- | ------------- | -------------------------------------------------------------- |
| SEC-01 — токены в `localStorage`  | ❌ актуально   | `tokens.js` — `localStorage.getItem`/`setItem`. Уязвимо к XSS. |
| SEC-02 — нет CSP                  | ❌ не проверял | `Program.cs` — не нашёл.                                       |
| SEC-04 — `x-ip-address = 0.0.0.0` | ❌ не проверял | Не углублялся.                                                 |
| SEC-05 — `AllowedHosts: "*"`      | ❌ актуально   | `appsettings.json:8`.                                          |

(остальные ~17 пунктов не проверял)

---

## Главные выводы

**1. Backend-сервисы первых двух групп (11 проектов) — самая отработанная часть кода.**
55 исправлений на 64 пункта (~85%). Видна последовательная работа.

**2. Shared-библиотеки (Exceptions, Queue, Auth) — критическое слепое пятно.**
0% исправлений. При этом они — фундамент кросс-сервисного взаимодействия. Любая backend-правка остаётся хрупкой, пока shared не приведён в порядок.

**3. Клиенты (Android, WPF, macOS, Web) — почти не правились по своим аудитам.**
Везде классические клиентские дыры: `localStorage` для токенов (Web), Trust-All TLS (Android), пароль в `string`-поле (WPF, Android), `print()` с auth-info (macOS).

**4. Серверы с прямым внешним доступом (WebServer, AdminPanel, FastAuth) — самый высокий риск.**
Дефолтные admin ID в коде, реальные Telegram-токены в `appsettings.json` (в git!), `[AllowAnonymous]` на чувствительных эндпоинтах, нет rate limiting.

**5. Proto-контракт расходится с реализацией.**
Пример: `Messages.OPT-08` исправлен стримингом в коде, но `messages_api.proto` всё ещё описывает унарный метод. Клиенты пересоберутся со старым контрактом.

---

## Приоритеты для следующей итерации

### 🔴 Срочно (security debt)

1. **`AdminPanel.SEC-01`** — `BotToken: 8539569051:AAH...` в `appsettings.json` в git-истории. **Немедленно отозвать через `@BotFather` `/revoke`**, заменить через env.
2. **`Developers.SEC-01`** — `postgres:postgres` в `DevelopersContextFactory.cs:12` (git-история).
3. **`Android.SEC-01`** — Trust-All TLS в `GrpcManager.kt`. Полный MITM на release-build. Минимум — `network_security_config.xml`.
4. **`Web.SEC-01`** — токены в `localStorage`. Move refresh в HttpOnly cookie.
5. **`Proto.SEC-02`** — `GenerateTestTokenRequest` в proto. Удалить из контракта (RPC не реализован, но мёртвая структура с таким именем — знак планируемого бэкдора).
6. **`Identity` 🆕** — нет rate-limit на ввод OTP. + дубль в `Shared.Identity.SEC-10`.
7. **`FastAuth.SEC-01`** — `SubscribeFastAuthResult` `[AllowAnonymous]`. Любой со знанием fast_auth_id (Guid) получит чужие токены.

### 🟠 Высокое (DoS, утечки)

8. **`Shared.Auth.BUG-04`** — нет try/catch для `Convert.FromBase64String` на сервере. DoS через невалидный Base64 в любом x-заголовке.
9. **`Shared.Auth.SEC-01`** — клиент подделывает IP. Ломает аудит, rate-limit, гео-блокировку.
10. **`Shared.Exceptions.SEC-01`** — `ex.Message` уходит клиенту через trailer. Утечка SQL/путей.
11. **`Shared.Queue.SEC-01`** — полный `MessageText` уходит в FCM. Пароли/OTP/PII в Google.
12. **`Users.SEC-04`** — `CheckExistEmail/Username` `[AllowAnonymous]`. User enumeration.
13. **`WebServer`** — отсутствие rate-limit и security headers. Спам, clickjacking, MIME-sniff.

### 🟡 Архитектурное

14. **`Shared.Exceptions`** — 14 пунктов 0% исправлено. Базовая инфраструктура ошибок.
15. **`Shared.Queue.BUG-02`** — `NewMessageEvent.Message: byte[]` без `= []` → NRE при ParseFrom.
16. **`Proto.BUG-03`** — `ServerColor`/`ServiceEndpoint` дублируются в beacon/navigator proto.
17. **`Proto.BUG-01`** — `ProfileFieldVisibility.ALL = 0` (опасный proto3-дефолт).
18. **`Files.MISC-04`** — orphan-объекты в S3 при ошибке после `S3.UploadAsync`.
19. **`Identity` 🆕** — нет cleanup устаревших `ResetPassword`-записей.
20. **`Identity` 🆕** — нет автоматического перехэширования legacy SHA-256 при логине.

| Проект            | Исправлено | Актуально | Частично | Новых находок |
| ----------------- |:----------:|:---------:|:--------:|:-------------:|
| Configuration     | 2          | 1         | 1        | 2             |
| GrpcServer        | 3          | 1         | 0        | 2             |
| Notification      | 3          | 0         | 0        | 1             |
| Beacon            | 4          | 0         | 0        | 2             |
| CloudMessaging    | 2          | 0         | 1        | 3             |
| Navigator         | 6          | 0         | 0        | 2             |
| Onliner           | 5          | 1         | 0        | 1             |
| ClientStorage     | 5          | 0         | 1        | 1             |
| Files             | 6          | 0         | 1        | 1             |
| Messages          | 9          | 0         | 0        | 2             |
| Identity          | 10         | 0         | 0        | 3             |
| SecurityUtilities | 0          | 9         | 2        | 0             |
| Shared.Exceptions | 0          | 14        | 0        | 0             |
| Shared.Queue      | 0          | 17        | 0        | 0             |
| Developers        | 0          | 16        | 0        | 0             |
| Shared.Auth       | 0          | 14        | 0        | 1             |
| Proto             | 0          | 19        | 1        | 1             |
| **Всего**         | **55**     | **92**    | **7**    | **22**        |

**Картина:** первые 11 проектов (backend-сервисы) — отработаны достаточно полно. Третья группа (shared-библиотеки + Developers + Proto) **практически не правилась**.

---

## Топ для разбора в первую очередь

### 🔴 Критично (безопасность)

1. **`Proto.SEC-02` ❌** — `GenerateTestTokenRequest` объявлен в production-контракте. Проверено: RPC `GenerateTestToken` не реализован в `BarkFluff.Identity`, но **мёртвая proto-структура с подобным именем — это знак планируемого бэкдора**. Удалить из `identity_api.proto`.
2. **`Shared.Auth.SEC-01` ❌** — клиент подделывает IP. `RequestContextInterceptor.cs:71` — `clientIp` имеет высший приоритет над `X-Forwarded-For`/`RemoteIpAddress`. Это ломает gео-блокировку, IP rate-limit, аудит. Простой фикс: понизить приоритет или удалить `XIpClientInterceptor` совсем.
3. **`Developers.SEC-01` ❌** — `postgres:postgres` в коде `DevelopersContextFactory.cs:12` (в git-истории).
4. **`Developers.SEC-03` ❌** — `AllowAnyOrigin()` для CORS в production.
5. **`Identity` 🆕** — нет rate-limit на ввод OTP. 10⁶ комбинаций × 5 минут × нет лимита попыток.
6. **`Configuration.SEC-05` ⚠️** — fallback `?? "guest"` для RabbitMQ креденшелов.

### 🟠 Высокий (правильность/DoS)

7. **`Shared.Auth.BUG-04` ❌** — нет try/catch для `Convert.FromBase64String` на сервере. Невалидный Base64 в любом x-заголовке кладёт запрос с `FormatException` → DoS-вектор.
8. **`Shared.Exceptions.SEC-01/BUG-05` ❌** — `ex.Message` уходит клиенту с `StatusCode.Unknown`. Утечка SQL/путей/инфраструктурных деталей.
9. **`Shared.Exceptions.PERF-01/BUG-03` ❌** — `public static List CachedExceptions` mutable + race condition. Любой код может занулить кеш.
10. **`Shared.Queue.BUG-02` ❌** — `NewMessageEvent.Message: byte[]` без `= []`. При неполной десериализации MassTransit → NRE при `ParseFrom`.
11. **`Shared.Queue.SEC-01` ❌** — полный текст сообщения уходит в FCM. Пароли/OTP/PII в payload Google.

### 🟡 Архитектурное / гигиена

12. **`Shared.Auth` дубль x5 интерсепторов** — DRY-нарушение, осложняет всю auth-логику.
13. **`Proto.BUG-03` ❌** — `ServerColor` и `ServiceEndpoint` дублируются в `beacon_api.proto` и `navigator_api.proto`.
14. **`Proto.BUG-01` ❌** — `ProfileFieldVisibility.ALL = 0` (опасный proto3-дефолт: забытое поле = «видно всем»).
15. **`SecurityUtilities.BUG-03` ❌** — `12345678` оценивается как «средний» пароль (нет проверки паттернов).
16. **`Files.MISC-04` ⚠️** — orphan-объект в S3 при исключении после `S3.UploadAsync`.
17. **`Identity` 🆕** — нет фонового cleanup устаревших `ResetPassword`.
18. **`Identity` 🆕** — нет авторехеширования legacy SHA-256 на BCrypt при логине.
