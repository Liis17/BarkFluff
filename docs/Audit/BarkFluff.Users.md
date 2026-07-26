# Аудит: BarkFluff.Users
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Сервис BarkFluff.Users построен аккуратно: оба gRPC-класса (`UsersApiService`, `UsersServerApiService`) защищены авторизацией на уровне класса (`[Authorize(Policy = TokenType.User)]` и `[Authorize(Policy = TokenType.Service)]`), все write-операции пользовательского API берут `UserId` из claims через `UserContext`, а не из тела запроса, операции над устройствами и папками чатов в хранилищах фильтруются по `UserId` владельца — классических IDOR на запись и «голых» методов без авторизации не обнаружено. SQL-поиск использует параметризованные `NpgsqlParameter`/`FromSqlInterpolated`, инъекций нет. Секретов в коде/compose/Dockerfile нет (конфигурация тянется из Configuration-сервиса), контейнеры запускаются под non-root.

Основные риски — отсутствие разграничения привилегий между сервисными токенами, rate limiting на анонимных проверках существования (энумерация) и на выдаче/регистрации prekey-бандлов, PII (email) в логах без маскирования, а также отсутствие `AsNoTracking` и пагинации на ряде read-only/broadcast-запросов.

| Критичность | Безопасность | Производительность | Docker/nginx | Всего |
| ----------- | ------------ | ------------------ | ------------ | ----- |
| Critical    | 0            | 0                  | 0            | 0     |
| High        | 1            | 0                  | 0            | 1     |
| Medium      | 4            | 3                  | 1            | 8     |
| Low         | 4            | 3                  | 0            | 7     |
| **Итого**   | **9**        | **6**              | **1**        | **16**|

---

## Безопасность

### S1. Энумерация email/username через анонимные эндпоинты без rate limiting — Medium
**Файл:** `Backend/BarkFluff.Users/Host/UsersApiService.cs:82` (CheckExistEmail), `:91` (CheckExistUsername)
**Проблема:** Оба метода помечены `[AllowAnonymous]` и возвращают точный булев флаг `Exist` для произвольного email/username. Ни в коде, ни в `nginx/users.conf` нет ограничения частоты запросов.
**Почему это проблема:** Анонимный злоумышленник может перебором узнать, какие email и username зарегистрированы в системе (user/email enumeration). Эндпоинт `CheckExistEmail` особенно чувствителен — он раскрывает факт регистрации конкретного адреса.
**Рекомендация:** Добавить rate limiting (на уровне сервиса через `AddRateLimiter` или в nginx через `limit_req`) на эти эндпоинты; рассмотреть унификацию ответа/задержки. Маскирование на стороне регистрации частично снижает риск, но не устраняет оракул.

### S2. Отсутствие rate limiting и проверки отношений на выдаче prekey-бандлов — Medium
**Файл:** `Backend/BarkFluff.Users/Host/UsersApiService.cs:369` (FetchPrekeyBundle), потребление prekey — `Backend/BarkFluff.Users/Persistence/Services/PrekeyStorage.cs:177`
**Проблема:** Любой аутентифицированный пользователь может запросить `FetchPrekeyBundle(UserId, DeviceId)` для произвольной жертвы. Каждый вызов выполняет `DELETE ... RETURNING` и расходует одну one-time prekey из пула устройства жертвы. Ограничения частоты нет, проверки наличия «отношений» между пользователями нет (в системе их и не существует).
**Почему это проблема:** Намеренными повторными вызовами злоумышленник исчерпывает пул one-time prekeys жертвы. После исчерпания бандл отдаётся без one-time prekey (`FetchPrekeyBundleQueryHandler.cs:32` — только warning), что ослабляет защиту новых X3DH-сессий (деградация forward secrecy). Дополнительно `ListPeerDevices` (`UsersApiService.cs:379`) и `FetchPrekeyBundle` позволяют любому аутентифицированному пользователю перечислять устройства (имя, last-seen) произвольного пользователя.
**Рекомендация:** Ввести rate limiting на `FetchPrekeyBundle`/`ListPeerDevices` per-caller; мониторить и алертить на аномальное потребление prekeys; рассмотреть лимит на число выдач в единицу времени на одно целевое устройство.

### S3. PII (email) в логах без маскирования — Medium
**Файлы:**
- `Backend/BarkFluff.Users/Features/FindByLogin/FindByLoginQueryHandler.cs:27` и `:46` — `Email` логируется в открытом виде (Debug и Warning).
- `Backend/BarkFluff.Users/Features/CheckExistEmail/CheckExistEmailQueryHandler.cs:24`, `:30`, `:34` — сырой email в Debug-логах.
- `Backend/BarkFluff.Users/Features/OverrideDraftUser/OverrideDraftUserCommandHandler.cs:27` и `:38` — сырой email.
**Проблема:** Email (PII) пишется в логи без маскирования, тогда как `AddDraftUserCommandHandler` (`:35`, `:53`, `:120` `MaskEmail`) корректно маскирует его. Поведение непоследовательно.
**Почему это проблема:** Логи (Seq и файлы) попадают в централизованное хранилище и могут быть доступны более широкому кругу лиц; хранение PII в plaintext-логах нарушает принципы минимизации данных и осложняет соответствие требованиям по защите ПДн.
**Рекомендация:** Применять единый хелпер маскирования email (как `MaskEmail` в AddDraftUser) во всех хендлерах, либо понизить уровень и убрать email из сообщений, оставив только маскированную форму.

### S4. Раскрытие внутренних сообщений об ошибках клиенту через сырые исключения — Low
**Файлы (Users-side источники):** `Backend/BarkFluff.Users/Features/Prekeys/FetchPrekeyBundle/FetchPrekeyBundleQueryHandler.cs:18`, `:25`; `Backend/BarkFluff.Users/Persistence/Services/DevicesStorage.cs:65`, `:89`, `:125`; `Backend/BarkFluff.Users/Persistence/Services/PrekeyStorage.cs:25`, `:101`, `:123`; `Backend/BarkFluff.Users/Features/UpdateProfileServer/UpdateProfileServerCommandHandler.cs:31`. Плюс `Guid.Parse` по клиентским полям: `Host/UsersApiService.cs:76`, `:197`; `Host/UsersServerApiService.cs:284`, `:311`.
**Проблема:** Хендлеры/хранилища бросают сырые `InvalidOperationException`/`FormatException` с внутренними сообщениями («Устройство не найдено», «Пользователь {id} не найден», «Некорректный DeviceId»). `ServerExceptionInterceptor` (в GrpcServer) для не-доменных исключений прокидывает `ex.Message` в gRPC `Status` клиенту.
**Почему это проблема:** Клиент получает внутренние детали реализации; невалидный GUID в запросе приводит к `FormatException` вместо контролируемой доменной ошибки. Логика обработки невалидных идентификаторов непоследовательна (ср. `ParseChatGuid` в `UsersApiService.cs:330`, который бросает типизированное `ChatIdNotValidException`).
**Рекомендация:** Заменить сырые исключения на типизированные `BaseGrpcException`-наследники с безопасным кодом ошибки; использовать `Guid.TryParse` с доменным исключением для всех клиентских GUID-полей.

### S5. gRPC reflection включён безусловно (в т.ч. в продакшене) — Low
**Файл:** `Backend/BarkFluff.Users/Program.cs:40` (`AddGrpcReflection`), `:107` (`MapGrpcReflectionService`)
**Проблема:** Сервис reflection регистрируется и маппится без проверки окружения, а endpoint доступен публично через nginx (`users.barkfluff.com`).
**Почему это проблема:** Reflection позволяет произвольному клиенту перечислить все сервисы и методы API, что упрощает разведку поверхности атаки.
**Рекомендация:** Включать reflection только в Development (`if (app.Environment.IsDevelopment())`).

### S6. GetUser игнорирует пользовательские настройки приватности — Low
**Файл:** `Backend/BarkFluff.Users/Host/UsersApiService.cs:60`, обработчик `Features/GetUser/GetUserQueryHandler.cs:30`
**Проблема:** `GetUser(UserId)` возвращает аватар/био/имя произвольного пользователя по его id без применения настроек `Privacy` (`AvatarVisibility`, `BioVisibility`), в отличие от `GetUserByUsername` (`GetUserByUsernameQueryHandler.cs:54`, `:58`), который их учитывает.
**Почему это проблема:** Пользователь, скрывший аватар/био, всё равно отдаёт их любому аутентифицированному вызывающему через `GetUser`. Скорее всего это сознательное решение для внутри-аппового просмотра профилей, но поведение двух путей доступа к одним и тем же полям расходится — это стоит подтвердить.
**Рекомендация:** Уточнить требования: если приватность должна действовать и внутри приложения — применять её и в `GetUser`; иначе задокументировать различие.

### S7. Хардкод пароля БД в design-time фабрике — Low
**Файл:** `Backend/BarkFluff.Users/Persistence/Contexts/UsersContextFactory.cs:13`
**Проблема:** Строка подключения с `Username=postgres;Password=password` зашита в исходниках.
**Почему это проблема:** Фабрика используется только во время разработки/миграций (design-time) и не задействована в рантайме, поэтому риск низкий, но хардкод учётных данных в репозитории — плохая практика и может быть скопирован в рабочую конфигурацию.
**Рекомендация:** Брать строку подключения из переменной окружения (например `BARKFLUFF_USERS_MIGRATIONS_CONN`) с фоллбэком, без реального пароля в коде.

---

### S8. Любой сервисный токен получает неограниченный доступ к административным и GDPR-операциям Users — High
**Файл:** `Backend/BarkFluff.Users/Host/UsersServerApiService.cs:47-48` (единая общая политика `TokenType.Service`), в частности `:257-278` (ExportData), `:385-420` (FCM-токены устройств, включая все устройства), `:451-466` (создание/удаление bot-аккаунтов), а также операции над баджами/лимитами хранилища.
**Проблема:** Все server-RPC защищены только общим типом токена, без claim-а вызывающего сервиса или набора разрешённых операций. Поэтому токен любого скомпрометированного микросервиса проходит к `ExportData(userId)`, административному управлению ботами, баджами и лимитами, а также к Firebase device tokens всех пользователей.
**Почему это проблема:** Компрометация наименее критичного сервиса эскалируется до чтения GDPR-экспорта произвольного пользователя и изменений глобальных пользовательских данных. Граница доверия «service-to-service» становится значительно шире, чем требуется конкретным клиентам API.
**Рекомендация:** Ввести аудиторию/скоупы в service JWT и проверять их для чувствительных RPC; вынести admin-операции в отдельный API с отдельным credential. Минимально ограничить ExportData, bot-операции и массовую выдачу FCM-токенов списком разрешённых ServiceId.

### S9. Регистрация и пополнение X3DH prekeys не ограничивают размер ключей и размер пула — Medium
**Файлы:** `Backend/BarkFluff.Users/Features/Prekeys/RegisterPrekeyBundle/RegisterPrekeyBundleCommandHandler.cs:22-41`, `Features/Prekeys/ReplenishOneTimePrekeys/ReplenishOneTimePrekeysCommandHandler.cs:25-32`; запись без лимита — `Persistence/Services/PrekeyStorage.cs:58-84,125-154`.
**Проблема:** Обработчики принимают произвольное количество one-time prekeys и произвольные `byte[]` для identity/signed/one-time ключей. Хранилище добавляет все ранее не встречавшиеся идентификаторы и не задаёт ни максимального числа ключей на устройство, ни допустимых размеров ключевого материала.
**Почему это проблема:** Аутентифицированный пользователь может повторно отправлять уникальные `PrekeyId` и постоянно наращивать таблицу `OneTimePrekeys` и объём `bytea`-данных. В одном запросе это ещё и создаёт большой цикл EF-вставок; существующее отсутствие rate limiting Users (D1) упрощает ресурсную атаку.
**Рекомендация:** Валидировать точные длины ключей протокола, лимитировать число prekeys в одном запросе и общий пул на устройство; при превышении атомарно отклонять или заменять старые ключи. Добавить per-user/device rate limit.

## Производительность

### P1. Отсутствие AsNoTracking на read-only запросах — Medium
**Файлы:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs:22` (GetUserByUsername), `:30` (GetUserByEmail), `:38` (GetById), `:319` (GetUserBadgesAsync), `:338` (GetBadgesForUsersAsync), `:415` (GetAllBadgesAsync), `:457` (GetAllUsersDescending); `DevicesStorage.cs:47` (GetDevicesByUserId), `:55` (GetDeviceById); `ChatFolderStorage.cs:19` (GetByOwnerAsync); `PrivacyStorage.cs:19` (Get); `PersonalizationStorage.cs:19` (Get); `PrekeyStorage.cs:198` (ListPeerDevicesAsync).
**Проблема:** Перечисленные чтения трекаются change tracker'ом, хотя возвращаемые сущности не модифицируются (единственное место с `AsNoTracking` — `GetByIds`, `:46`).
**Почему это проблема:** Лишний overhead на построение снапшотов и память на горячих read-путях (получение профиля, списка устройств, бейджей, папок). Замечание: `GetById`/`GetUserByUsername` иногда переиспользуются для последующего апдейта (`OverrideDraftUser` → `UpdateTrackedUser`), поэтому `AsNoTracking` нужно добавлять точечно на чисто read-only вызовах, а не глобально.
**Рекомендация:** Добавить `AsNoTracking()` в read-only запросы (или отдельные read-методы), оставив трекинг там, где сущность затем изменяется.

### P2. Unbounded-выборки устройств без пагинации — Medium
**Файл:** `Backend/BarkFluff.Users/Persistence/Services/DevicesStorage.cs:111` (GetAllDevicesWithFirebaseTokens), `:95` (GetDevicesWithFirebaseTokens), `:103` (...ByDeviceIds)
**Проблема:** `GetAllDevicesWithFirebaseTokens` загружает в память ВСЕ устройства системы с непустым `FirebaseDeviceToken` (broadcast push) одним списком; варианты по `userIds`/`deviceIds` не ограничивают размер входного списка. Фильтр `FirebaseDeviceToken != null AND NotificationsEnabled` не покрыт частичным индексом → seq scan.
**Почему это проблема:** При росте числа устройств broadcast-вызов даёт неограниченную аллокацию в памяти и полный скан таблицы; объём ответа также не ограничен.
**Рекомендация:** Стримить/батчить выдачу (keyset-пагинация), добавить частичный индекс `WHERE "FirebaseDeviceToken" IS NOT NULL AND "NotificationsEnabled"`, ограничить размер входных списков `userIds`/`deviceIds`.

### P3. ExportData полностью материализует данные пользователя в памяти — Medium
**Файл:** `Backend/BarkFluff.Users/Features/ExportData/ExportDataCommandHandler.cs:70`–`:122`
**Проблема:** Обработчик через `GetUserAllMessagesAsync` тянет ВСЕ сообщения пользователя, сериализует их целиком в JSON-строку и кладёт в `ExportDataResponse.Files` в памяти; затем то же для файлов.
**Почему это проблема:** Для активного пользователя с большим числом сообщений/вложений это даёт большой пик памяти и крупный gRPC-ответ без пагинации/стриминга; риск OOM и таймаутов.
**Рекомендация:** Перевести экспорт на постраничную загрузку и потоковую сериализацию (server-streaming gRPC или выгрузка частями в объектное хранилище со ссылкой).

### P4. Лишний round-trip в AssignBadgeToUserAsync — Low
**Файл:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs:360`–`:364`
**Проблема:** После `AddAsync` + `SaveChangesAsync` выполняется отдельный `Entry(userBadge).Reference(ub => ub.Badge).LoadAsync()` — дополнительный запрос для подгрузки бейджа.
**Почему это проблема:** Лишний round-trip к БД на каждое назначение бейджа.
**Рекомендация:** Предзагрузить/закешировать бейдж до `SaveChanges` или собрать gRPC-ответ из уже известных полей без отдельного `LoadAsync`.

### P5. Мёртвый метод SearchUsers (FTS) с багом подсчёта — Low
**Файл:** `Backend/BarkFluff.Users/Persistence/Services/UsersStorage.cs:62`–`:105`
**Проблема:** Метод `SearchUsers` (полнотекстовый поиск) нигде не вызывается (хендлеры используют `SearchUsersByTrigram`). При этом он считает total через `ExecuteSqlRawAsync(countSql, ...)` (`:101`), который возвращает число затронутых строк (−1 для SELECT), а не реальный COUNT — баг, уже исправленный и прокомментированный в trigram-методе (`:132`–`:134`).
**Почему это проблема:** Мёртвый код с известным дефектом вводит в заблуждение и может быть случайно переиспользован.
**Рекомендация:** Удалить неиспользуемый метод `SearchUsers` (отметка о мёртвом коде — не удалять без подтверждения, но рекомендуется к удалению).

### P6. GetUser выполняет два отдельных запроса к БД — Low
**Файл:** `Backend/BarkFluff.Users/Features/GetUser/GetUserQueryHandler.cs:40` (GetById) и `:57` (personalizationStorage.Get)
**Проблема:** Получение профиля делает два последовательных запроса — пользователь и персонализация.
**Почему это проблема:** На горячем пути (частый вызов профиля) два round-trip вместо одного.
**Рекомендация:** При необходимости объединить выборку (join/`Include`-подобный запрос) или кешировать `ProfilePosterFileId`. Низкий приоритет.

---

## Docker / nginx

### D1. nginx: отсутствие rate limiting и ограничений запросов для users-эндпоинта — Medium
**Файл:** `docker/nginx/users.conf:15`–`:24`
**Проблема:** В `location /` нет `limit_req`/`limit_conn`, нет ограничения размера сообщений; заданы только большие таймауты (`grpc_read_timeout 300s`).
**Почему это проблема:** Сетевого троттлинга нет, что усиливает S1 (энумерация через анонимные `CheckExist*`) и S2 (исчерпание prekeys) — атакующий не ограничен ни на уровне приложения, ни на уровне nginx.
**Рекомендация:** Добавить `limit_req_zone`/`limit_req` и `limit_conn` для users-сервиса (особенно для анонимных и prekey-операций), при необходимости разнести лимиты по путям.

**Положительные моменты (Docker):** оба Dockerfile (`BarkFluff.Users/Dockerfile`, `Dockerfile.slim`) используют chiseled-образ `aspnet:10.0-noble-chiseled` и запускаются под non-root (`USER $APP_UID`, `--chown=1654:1654`); секретов в образах нет. В `docker-compose-dev.yml`/`docker-compose-master.yml` секция `users` не содержит хардкод-секретов — конфигурация и токены тянутся из Configuration-сервиса.
