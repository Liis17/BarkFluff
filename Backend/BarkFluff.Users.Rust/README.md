# BarkFluff.Users.Rust

Rust drop-in порт микросервиса [`BarkFluff.Users`](../BarkFluff.Users) (.NET 10).
Полный паритет по проводу: тот же `users_api.proto` (оба сервиса, 62 RPC), JWT XAuth,
коды ошибок `x-error-code`, схема PostgreSQL, события RabbitMQ (MassTransit), метрики.

## Стек

| Слой | Технология |
|------|-----------|
| gRPC | `tonic` 0.12 + `prost` (компиляция proto через `tonic-build` + vendored `protoc`) |
| БД | `sqlx` 0.8 (PostgreSQL, runtime-запросы; **без TLS** → без `ring`) |
| RabbitMQ | `lapin` 2.5 (executor — tokio; конверт/топология как у MassTransit) |
| JWT | `hmac`+`sha2`+`base64` (HS256, чистый Rust, без `ring`) |
| Конфиг | gRPC-клиент к Configuration-сервису (`ServiceId.Users = 2`) |

## Структура

```
build.rs            # компиляция .proto из ../../Shared/BarkFluff.Proto
proto/              # вендорённый google/protobuf/timestamp.proto
src/
  config.rs         # загрузка конфига из Configuration-сервиса
  auth.rs           # JWT (x-auth-token, HS256), UserContext, политики, TokenRevocationCache
  errors.rs         # AppError → tonic Status (+ trailer x-error-code, точные GUID)
  metrics.rs        # MetricsCollector + репортер (лог ServiceMetrics каждые 5с)
  domain.rs         # 10 сущностей (FromRow)
  persistence/      # storage-модули (тот же raw SQL: trigram, prekey-claim, 23505)
  mapping.rs        # domain → proto
  clients.rs        # gRPC-клиенты Files/Messages (+ JWT)
  queue.rs          # publisher UserChanged* + consumer SessionRevokedEvent (MassTransit)
  features.rs       # бизнес-логика всех RPC (эквивалент MediatR-хендлеров)
  host/             # реализации UsersApi + UsersServerApi (tonic)
  main.rs           # bootstrap
```

## Сборка

`rustup`/`cargo` установлены в `~/.cargo/bin` (через rustup-прокси). Если их нет в PATH:

```bash
export PATH="$HOME/.cargo/bin:$PATH"   # Windows (Git Bash): /c/Users/<user>/.cargo/bin
cargo build            # debug
cargo build --release  # release
cargo clippy
```

`protoc` подтягивается автоматически (`protoc-bin-vendored`) — системный не нужен.

## Запуск

Конфигурация и схема БД берутся из существующей инфраструктуры (drop-in):

```bash
cd ../  # Backend/
docker-compose -f docker-compose-dev.yml up -d   # PostgreSQL (схема Users), Configuration, RabbitMQ, Seq
```

Затем (адрес Configuration-сервиса — env или дефолт `http://localhost:7003`):

```bash
CONFIGURATION_SERVICE_URL=http://localhost:7003 ./target/debug/barkfluff-users
```

Сервис при старте:
1. тянет конфиг из Configuration (`UsersDb`, `JwtSettings:*`, `RabbitMQ:*`, `FilesService:*`, `MessagesService:*`, `ReservedNames:Usernames`);
2. подключается к PostgreSQL (**миграции не выполняет** — схема создаётся .NET-сервисом);
3. поднимает publisher + consumer RabbitMQ;
4. слушает gRPC на `RunSettings:Port` (по умолчанию 7001), с gRPC reflection.

## Проверка

```bash
grpcurl -plaintext -H "x-auth-token: <JWT>" localhost:7001 list
grpcurl -plaintext -H "x-auth-token: <service-JWT>" -d '{"user_id":123}' \
  localhost:7001 barkfluff.users.UsersServerApi/GetById
```

## Заметки / ограничения

- **MassTransit-совместимость RabbitMQ** — самая чувствительная часть; топологию exchange'ей
  и формат конверта стоит сверить с живым кластером и .NET-издателем/потребителем.
- Запросы sqlx — рантайм (`query_as`), чтобы не привязывать сборку к живой БД; можно перейти
  на compile-time `query!` с offline-кэшем.
- Лог метрик пишется в формате `ServiceMetrics` через `tracing`; для полной интеграции с Seq
  нужен Seq-слой (HTTP appender).
