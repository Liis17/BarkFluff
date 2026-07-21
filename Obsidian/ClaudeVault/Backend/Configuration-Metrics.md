# BarkFluff.Configuration — реестр метрик

> ↩ Назад: [[Backend/Configuration]] · [[Backend/GrpcServer]] (общий механизм) · [[Backend/Beacon-Metrics]] (пример общей схемы)

Общая схема сбора — та же, что у всех сервисов (см. [[Backend/Beacon-Metrics]]): `MetricsCollector.Increment/Add/Set` → `MetricsReporterService` раз в 5 сек пишет `ServiceMetrics {@Metrics}` в Seq → `AdminPanel` раз в час забирает последний снапшот часа.

Регистрация в `Program.cs`:
```csharp
builder.AddBarkFluffSerilog("BarkFluff.Configuration");
builder.Services.AddBarkFluffMetrics("BarkFluff.Configuration");
```

## gRPC-уровень (`ServerExceptionInterceptor`, общее для всех методов)

**Counters:**
- `grpc_requests_total` — все unary-запросы
- `grpc_requests_failed` — бизнес-ошибки (`BaseGrpcException`)
- `grpc_requests_errors` — необработанные исключения
- `grpc_request_duration_ms_total` — сумма длительности всех запросов; средняя = `_total / grpc_requests_total`

## Эндпоинт `GetConfiguration`

- `config_get_requests` / `_success` / `_errors` / `_duration_ms_total`
- `last_config_get_unix` (gauge) — время последнего вызова
- `last_config_get_items` (gauge) — кол-во записей в последнем ответе

## Эндпоинт `UpdateConfiguration`

- `config_update_requests` / `_success` / `_errors` / `_duration_ms_total`
- `last_config_update_unix` (gauge)
- ⚠️ `_errors` инкрементится и при `RpcException`, и при `response.Success == false` (handler ловит исключение сам и возвращает `Success=false`)

## Reserved Names (`Get/Add/Update/Delete`)

- `reserved_names_get_requests` / `_success` / `_errors` / `_duration_ms_total`
- `reserved_names_add_requests` / `_success` / `_errors` / `_duration_ms_total`
- `reserved_names_update_requests` / `_success` / `_errors` / `_duration_ms_total`
- `reserved_names_delete_requests` / `_success` / `_errors` / `_duration_ms_total`

## Storage / БД (gauges, не сбрасываются)

- `configurations_total` — общее число записей в `Configurations`; обновляется при старте (Populator) и на каждый `UpdateConfigurationAsync`
- `configurations_db_writes` (counter) — количество физических записей через `UpdateConfigurationAsync`
- `reserved_names_count` — текущее число зарезервированных имён; обновляется при Get/Add/Update/Delete

## Старт сервиса и инфраструктура

**Gauges:**
- `service_started_unix`
- `db_healthy` — 0/1, 1 если последняя миграция прошла успешно
- `configurations_empty_at_startup` — сколько было пустых конфигов при старте (до автозаполнения)

**Counters:**
- `db_migration_attempts` — попытки `ctx.Database.Migrate()` (retry до 5 раз)
- `db_migration_succeeded`
- `db_migration_failed`
- `defaults_populated_total` — сколько строк автозаполнено `ConfigurationDefaultsPopulator`
- `defaults_populator_failed`

## Где менять

| Файл | Что добавляется |
|------|------|
| `Backend/BarkFluff.Configuration/Host/ConfigurationApiService.cs` | per-method counters/duration/last_*_unix |
| `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationStorage.cs` | gauges `configurations_total`, `reserved_names_count`, counter `configurations_db_writes` |
| `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs` | `defaults_populated_total`, `configurations_empty_at_startup`, `configurations_total` |
| `Backend/BarkFluff.Configuration/Program.cs` | `service_started_unix`, `db_*`, `defaults_populator_failed` |
| `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs` | общие `grpc_*` (действует во всех сервисах) |

## Соглашения именования

- snake_case
- `_requests` — счётчик всех запросов; `_success`/`_errors` — пары исходов
- `_duration_ms_total` — кумулятивная сумма мс
- `_total` — общее значение (gauge или сумма)
- `_unix` — Unix-timestamp в gauge
- `_healthy` — бинарный 0/1
- `last_*_unix` — gauge timestamp последнего события

## Известные ограничения

1. AdminPanel берёт **последний** 5-секундный снапшот часа → counters недооценены (видна только активность последних 5 сек).
2. `ConfigurationStorage` инжектится как scoped, `MetricsCollector` — singleton: безопасно (singleton в scoped — стандартно).
3. `MetricsCollector` в Populator передаётся опционально (`MetricsCollector? metrics = null`) — populator создаётся через `new`, не через DI.
4. `configurations_total` пересчитывается полным `CountAsync` после каждого `UpdateConfigurationAsync` — операция редкая (только админ), нагрузка на БД минимальна.
