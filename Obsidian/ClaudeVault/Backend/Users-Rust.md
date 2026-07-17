# BarkFluff.Users.Rust

Тестовый **drop-in порт** микросервиса [[Backend/Users]] (.NET 10) на **Rust**.
Расположение: `Backend/BarkFluff.Users.Rust/`. Порт: **7001**. `ServiceId.Users = 2`.

Цель эксперимента — оценить Rust на одном реальном сервисе с полным паритетом по проводу:
тот же `users_api.proto` (оба сервиса, 68 RPC: 34 UsersApi + 34 UsersServerApi), JWT XAuth, коды ошибок `x-error-code`,
схема PostgreSQL, события RabbitMQ (MassTransit), метрики `ServiceMetrics`.

> ⚠️ **Порт не поддерживается активно.** Последний коммит в Rust-версию — 18 июня 2026 (`d051b962`); после этого в .NET-версию добавлены новые метрики (`prekey_bundle_registrations`, `prekey_bundle_fetches`, `peer_device_listings`, `one_time_prekey_replenishments`, `signed_prekey_rotations`, `device_lookups_by_device_id`, `device_lookups_all`, `profile_poster_lookups` — см. [[Backend/Users-Metrics]]), которых в `metrics.rs` нет. Не считать этот файл источником истины по паритету без сверки с актуальным [[Backend/Users]].
>
> ⚠️ **Схема Users получила колонку `Uuid`** (Фаза 0 федерации, миграция `AddUserUuid`) — порт не синхронизирован, `domain.rs`/`FromRow` про неё не знает. Прямые INSERT из Rust продолжат работать благодаря `defaultValueSql: "gen_random_uuid()"` на колонке, но Rust-код не сможет читать/писать `Uuid` осознанно, и `.proto` поле `User.uuid` (13) он не заполняет.

## Стек

| Слой | .NET | Rust |
|------|------|------|
| gRPC | Grpc.AspNetCore | `tonic` 0.12 + `prost` (proto компилируется `tonic-build`, `protoc` — vendored) |
| БД | EF Core + Npgsql | `sqlx` 0.8, runtime-запросы, **без TLS** (→ без `ring`) |
| RabbitMQ | MassTransit | `lapin` 2.5 (executor — tokio; конверт + топология exchange'ей вручную под MassTransit) |
| JWT | JwtBearer (HS256) | `hmac`+`sha2`+`base64` (HS256, чистый Rust) |
| CQRS | MediatR | прямые функции-хендлеры в `features.rs` |
| Метрики | MetricsCollector | `metrics.rs` (counters/gauges + репортер каждые 5с) |
| Конфиг | LoadConfiguration(gRPC) | `config.rs` — gRPC-клиент ConfigurationApi |

## Соответствие слоёв

| .NET | Rust |
|------|------|
| `Domain/*.cs` | `src/domain.rs` (10 сущностей, `FromRow`, rename на PascalCase-колонки) |
| `Persistence/Services/*.cs` | `src/persistence/{users,devices,privacy,personalization,chat_folders,prekeys}.rs` |
| `Host/UsersApiService.cs` | `src/host/client_api.rs` (политика User; CheckExist* — анонимные) |
| `Host/UsersServerApiService.cs` | `src/host/server_api.rs` (политика Service) |
| `Features/**/*Handler.cs` | `src/features.rs` |
| `Mapping/*.cs` | `src/mapping.rs` |
| `Infrastructure/UserInfoQueueSender.cs` + `Consumers/SessionRevokedConsumer.cs` | `src/queue.rs` |
| `Services/{ReservedUsernames,UsernameFormatValidator}.cs` | `src/services.rs` |
| `BarkFluff.GrpcServer/XAuth/*` | `src/auth.rs` |
| `BarkFluff.GrpcServer/Metrics/*` + `ServerExceptionInterceptor` | `src/metrics.rs` + `src/errors.rs` |
| `Program.cs` | `src/main.rs` |

## Перенесённые ключевые детали

- **Схема БД** — НЕ мигрируется (создаётся .NET-сервисом); sqlx-запросы используют точные
  PascalCase-идентификаторы EF Core (`"Users"`, `"FirstName"`, …).
- **Raw SQL дословно**: trigram-поиск (`similarity > 0.3`, `COUNT(*) OVER()`, фильтр `SearchVisible`);
  атомарный claim prekey (`DELETE … FOR UPDATE SKIP LOCKED RETURNING`); обработка `23505` →
  EmailExist/UsernameExist; регистронезависимый поиск `LOWER()`.
- **Коды ошибок** `x-error-code` (GUID) перенесены 1:1; бизнес → `FailedPrecondition`, системные → `Unknown`.
- **JWT**: заголовок `x-auth-token`, HS256, claims `x-user-id`/`x-token-type`/`x-device-id`,
  политики User/Service, `TokenRevocationCache` (пополняется из SessionRevokedConsumer).
- **RabbitMQ MassTransit**: publish UserChanged{Name,Username,Avatar,Bio,Password} в fanout-exchange
  `BarkFluff.Shared.Queue.Users:<Type>` с конвертом `application/vnd.masstransit+json`; consume
  `SessionRevokedEvent` из очереди `session-revoked-users`.
- **proto3 optional** (`priority`, `limit`): воспроизведено поведение .NET (отсутствие → 0).

## Сборка и запуск

См. `Backend/BarkFluff.Users.Rust/README.md`. Кратко:
```bash
export PATH="$HOME/.cargo/bin:$PATH"
cargo build            # cargo check / clippy — чисто (0 ошибок)
```
Запуск против `docker-compose-dev.yml` (PostgreSQL со схемой Users + Configuration + RabbitMQ).
`protoc` системный не нужен (vendored).

## Ограничения / зоны риска

- **MassTransit-совместимость** — поведение библиотеки, не отражённое в репозитории; топологию
  exchange'ей и формат конверта нужно сверить с живым кластером и .NET-издателем/потребителем.
- Запросы sqlx рантаймовые (без compile-time `query!`/offline-кэша).
- Для полной интеграции метрик с Seq нужен Seq-слой для `tracing` (HTTP appender).

→ Эталон логики: [[Backend/Users]], [[Backend/Users-ProjectMap]], [[Backend/Users-Metrics]].
→ Инфраструктура: [[Shared/Proto]], [[Shared/Identity]], [[Shared/Queue]], [[Shared/Exceptions]], [[Backend/GrpcServer]].
