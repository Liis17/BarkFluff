# Аудит: BarkFluff.Users.Rust
> Дата: 2026-06-12. Область: код сервиса (Rust), Cargo.toml, Dockerfile.

## Сводка

Сервис — drop-in порт `BarkFluff.Users` на Rust (tonic + sqlx + lapin). В целом код написан аккуратно: **SQL-инъекций нет** (все пользовательские значения передаются через `.bind()`, в `format!` интерполируются только константы — списки колонок и имена таблиц); **IDOR не обнаружен** (все мутации scope-ятся по `ctx.user_id`/`ctx.device_id` из claims, слои `devices`/`chat_folders`/`privacy`/`personalization` фильтруют по `UserId`/`OwnerUserId`); валидация JWT по сути корректна (явная проверка `alg == HS256`, constant-time HMAC через `verify_slice`, проверка `exp`/`iss`/`aud`); **блокирующих вызовов в async-контексте нет** (нет `std::fs`, `std::thread::sleep`, блокирующих HTTP; `std::sync::Mutex` через await не используется — взят `DashMap`); хардкода секретов нет (всё из Configuration-сервиса). Critical/High находок нет. Основные проблемы — средней критичности: утечка сырых ошибок БД в `Status`, неатомарные/много-round-trip операции в слое БД (prekeys, upsert устройства) и лишнее объявление exchange на каждую публикацию в RabbitMQ.

| Критичность | Безопасность | Производительность | Docker | Итого |
| ----------- | ------------ | ------------------ | ------ | ----- |
| Critical    | 0            | 0                  | 0      | 0     |
| High        | 0            | 0                  | 0      | 0     |
| Medium      | 1            | 3                  | 0      | 4     |
| Low         | 3            | 3                  | 1      | 7     |
| **Всего**   | **4**        | **6**              | **1**  | **11** |

## Безопасность

### S1. Сырые ошибки БД утекают в gRPC `Status` клиенту — Medium
**Файл:** `Backend/BarkFluff.Users.Rust/src/errors.rs:66` и `Backend/BarkFluff.Users.Rust/src/errors.rs:87`
**Проблема:** `From<sqlx::Error>` оборачивает ошибку как `AppError::System(format!("db error: {e}"))` (строка 66), а `From<AppError> for Status` для системных ошибок кладёт `e.to_string()` прямо в текст `Status::new(Code::Unknown, e.to_string())` (строка 87). Текст ошибки sqlx (а значит — фрагменты SQL, имена таблиц/колонок/ограничений, иногда параметры подключения) уходит клиенту в сообщении gRPC-статуса.
**Почему это проблема:** раскрытие внутренних деталей схемы и инфраструктуры помогает атакующему в разведке. Дополнительно: в пути системной ошибки нет `tracing::error!`, поэтому деталь уходит наружу, но не логируется на сервере (хуже наблюдаемость, чем у .NET, где `ServerExceptionInterceptor` пишет `LogError`). Поведение частично зеркалит .NET (там тоже `new Status(StatusCode.Unknown, ex.Message)`), но в Rust сообщение формируется из сырой sqlx-ошибки, что детальнее.
**Рекомендация:** возвращать клиенту обобщённый текст (как делает базовый GUID-код), а полную ошибку логировать на сервере через `tracing::error!`. Не вставлять `format!("db error: {e}")` в текст, видимый клиенту.

### S2. gRPC reflection доступен без аутентификации — Low
**Файл:** `Backend/BarkFluff.Users.Rust/src/main.rs:140` (регистрация сервиса), сборка на `Backend/BarkFluff.Users.Rust/src/main.rs:123`
**Проблема:** `reflection`-сервис добавляется через `.add_service(reflection)` **без** XAuth-интерцептора (в отличие от `client_svc`/`server_svc`, обёрнутых `with_interceptor`). Любой неаутентифицированный клиент через grpcurl может перечислить все методы и схемы сообщений.
**Почему это проблема:** раскрытие поверхности API упрощает атаку. Зеркалит .NET (`MapGrpcReflectionService()` тоже без авторизации), но в проде reflection обычно стоит отключать или гейтить по окружению.
**Рекомендация:** включать reflection только в dev (по env-флагу), либо не публиковать его на внешнем порту.

### S3. JWT: не проверяется claim `nbf` (not-before) — Low
**Файл:** `Backend/BarkFluff.Users.Rust/src/auth.rs:158-162`
**Проблема:** `validate` проверяет только `exp`, но не `nbf`. Токен с `nbf` в будущем будет принят.
**Почему это проблема:** отклонение от .NET, где `ValidateLifetime = true` (XAuthExtensions.cs:31) проверяет и `nbf`, и `exp` с `ClockSkew = TimeSpan.Zero`. Практический риск низкий (токены выпускаются с `nbf <= now`), но это рассинхрон валидации между .NET и Rust-портом.
**Рекомендация:** добавить проверку `nbf` (если присутствует): `if let Some(nbf) = claims.get("nbf").and_then(|v| v.as_i64()) { if Utc::now().timestamp() < nbf { return None; } }`.

### S4. Отсутствие claim `x-user-id` даёт authenticated-контекст с `user_id = 0` — Low
**Файл:** `Backend/BarkFluff.Users.Rust/src/auth.rs:181-185`
**Проблема:** если claim `x-user-id` отсутствует или имеет неожиданный тип, `user_id` молча становится `0`, при этом `authenticated = true` и `require_user()` проходит.
**Почему это проблема:** для User-токена без `x-user-id` операции выполнятся в контексте несуществующего пользователя `0` (большинство приведут к `UserNotFound`, но `get_user(None)` вернёт попытку чтения пользователя `0`). Корректные токены всегда содержат claim, поэтому риск низкий, но «тихий» дефолт `0` лучше трактовать как невалидный токен.
**Рекомендация:** при отсутствии/непарсинге `x-user-id` для User-токена возвращать `None` (отклонять токен), а не подставлять `0`.

## Производительность

### P1. Поштучные `INSERT` one-time prekeys в цикле без транзакции — Medium
**Файл:** `Backend/BarkFluff.Users.Rust/src/persistence/prekeys.rs:88-101` (`register_bundle`) и `Backend/BarkFluff.Users.Rust/src/persistence/prekeys.rs:149-162` (`replenish_one_time_prekeys`)
**Проблема:** каждый one-time prekey вставляется отдельным `sqlx::query(...).execute(pool)` в цикле `for`. Клиент обычно присылает десятки–сотни prekeys → десятки–сотни отдельных round-trip к БД. Кроме того, в `register_bundle` upsert bundle и все вставки prekeys выполняются **без общей транзакции** (в отличие от `create_user`, где есть `pool.begin()`).
**Почему это проблема:** N round-trip вместо одного батча — заметная задержка на горячем пути регистрации устройства. Отсутствие транзакции означает, что при сбое в середине часть prekeys окажется записана, а bundle/часть — нет (несогласованное состояние). В .NET (`PrekeyStorage.cs:58-84`) все вставки идут одним `SaveChangesAsync()` (EF батчит и оборачивает в неявную транзакцию).
**Рекомендация:** выполнять вставки внутри одной транзакции (`pool.begin()`) и батчить — либо многострочный `INSERT ... VALUES (...),(...)`, либо `UNNEST`-форма с массивами.

### P2. `register_or_update_device`: upsert в 3 round-trip + TOCTOU-гонка без транзакции — Medium
**Файл:** `Backend/BarkFluff.Users.Rust/src/persistence/devices.rs:39-82`
**Проблема:** метод делает `get_device_by_id` (строка 49) → затем `UPDATE` или `INSERT` → затем снова `get_device_by_id` (строка 79). Это до трёх отдельных запросов на одну операцию, и проверка существования отделена от вставки без транзакции.
**Почему это проблема:** лишние round-trip на горячем пути регистрации устройства; при параллельных вызовах с одним `(Id, UserId)` возможна гонка (две параллельные ветки прошли `get` как «нет записи» → конфликт уникального индекса на `INSERT`).
**Рекомендация:** заменить на единый `INSERT ... ON CONFLICT ("Id","UserId") DO UPDATE SET ... RETURNING <cols>` — один атомарный round-trip без дополнительного `get`.

### P3. `EventPublisher.publish` объявляет exchange при каждой публикации — Medium
**Файл:** `Backend/BarkFluff.Users.Rust/src/queue.rs:86-96`
**Проблема:** на каждое событие (`name_changed`/`username_changed`/`bio_changed`/`avatar_changed`) перед `basic_publish` вызывается `exchange_declare`. Это дополнительный round-trip к RabbitMQ на каждое изменение профиля.
**Почему это проблема:** `exchange_declare` идемпотентен, но это лишняя сетевая операция на каждое доменное событие, удваивающая стоимость публикации.
**Рекомендация:** объявлять exchange'ы один раз при старте `EventPublisher::connect` (для известного набора типов событий), а в `publish` только публиковать.

### P4. Паттерн `get_or_create` + отдельный `UPDATE` (2-3 запроса + TOCTOU) — Low
**Файл:** `Backend/BarkFluff.Users.Rust/src/persistence/privacy.rs:50` (внутри `update`), `Backend/BarkFluff.Users.Rust/src/persistence/personalization.rs:42` (`update`) и `Backend/BarkFluff.Users.Rust/src/persistence/personalization.rs:59` (`update_poster`)
**Проблема:** `update*` сначала зовут `get_or_create` (это `SELECT`, потом, возможно, `INSERT`), затем выполняют отдельный `UPDATE` — 2-3 запроса на операцию; при гонке параллельный `create` может упасть на `23505`. То же касается `get_user` в features (`Backend/BarkFluff.Users.Rust/src/features.rs:37-40` — два последовательных `SELECT`).
**Почему это проблема:** лишние round-trip и потенциальный конфликт уникальности при первой инициализации настроек.
**Рекомендация:** использовать `INSERT ... ON CONFLICT ("UserId") DO UPDATE SET ... RETURNING` одним запросом.

### P5. Жёстко заданный размер пула; параметры пула из строки подключения игнорируются — Low
**Файл:** `Backend/BarkFluff.Users.Rust/src/persistence/mod.rs:49` (`.max_connections(20)`) и `Backend/BarkFluff.Users.Rust/src/persistence/mod.rs:39`
**Проблема:** `max_connections` зафиксирован в 20 и не настраивается. Парсер Npgsql-строки (`parse_npgsql`) явно игнорирует ключи пула (`Pooling`, `MaxPoolSize` и т.п.) — ветка `_ => {}` на строке 39.
**Почему это проблема:** под нагрузкой 20 соединений может стать узким местом, а сконфигурированные в .NET-строке параметры пула не применяются — расхождение поведения с .NET-сервисом.
**Рекомендация:** вынести `max_connections` в конфиг (или читать `MaxPoolSize` из строки подключения).

### P6. Ошибки публикации событий проглатываются — Low (надёжность)
**Файл:** `Backend/BarkFluff.Users.Rust/src/queue.rs:116` (и аналогично 127, 138, 159)
**Проблема:** результат `self.publish(...).await` отбрасывается через `let _ = ...`; при сбое RabbitMQ событие теряется молча (даже без `tracing::warn!`).
**Почему это проблема:** другие сервисы не узнают об изменении профиля, при этом ни метрики ошибок, ни лог не фиксируют потерю.
**Рекомендация:** логировать ошибку публикации (`tracing::warn!`) и инкрементировать счётчик ошибок публикации.

## Docker

### D1. Dockerfile для Rust-сервиса отсутствует — Low (информационно)
**Файл:** директория `Backend/BarkFluff.Users.Rust/` (Dockerfile не найден); в `docker/backend/docker-compose-dev-backend.yml` и `docker-compose-master.yml` сервис `Users.Rust` не зарегистрирован.
**Проблема:** у Rust-порта нет Dockerfile и он не подключён к docker-compose, в отличие от остальных backend-сервисов. Аудит образа (пользователь контейнера, базовый образ, multi-stage) провести не на чем.
**Почему это проблема:** не уязвимость, но пробел в развёртывании: сервис не контейнеризован наравне с .NET-сервисами; при добавлении Dockerfile стоит сразу заложить непривилегированного пользователя и distroless/chiseled-базу (как `10.0-noble-chiseled` у .NET-сервисов).
**Рекомендация:** при выводе в прод добавить multi-stage Dockerfile (build на `rust:*`, runtime на distroless/chiseled, non-root USER) и запись в docker-compose.
