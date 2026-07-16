# BarkFluff.Beacon — реестр метрик

> ↩ Назад: [[Backend/Beacon]] · [[Backend/GrpcServer]] (общий механизм)

## Как работает сбор метрик

В Seq нет нормальной системы метрик, поэтому метрики микросервисов передаются **через структурированные логи**. Цепочка:

1. Микросервис вызывает `MetricsCollector.Increment / Add / Set` (in-memory, потокобезопасно).
2. `MetricsReporterService` (BackgroundService из `BarkFluff.GrpcServer`) **каждые 5 секунд** делает `SnapshotAndReset` и пишет лог:
   ```csharp
   _logger.LogInformation("ServiceMetrics {@Metrics}",
       new { ServiceName = "BarkFluff.Beacon", Metrics = snapshot, Timestamp = DateTime.UtcNow });
   ```
   - **Counters** обнуляются после снапшота (накопительно за 5 секунд).
   - **Gauges** не сбрасываются — последнее установленное значение.
3. Serilog шлёт лог в Seq (через `WriteTo.Seq`, batch 100 событий / 2 сек).
4. `Barkfluff.AdminPanel/Services/MetricsCollectorService` раз в час дёргает Seq по фильтру `@Message like 'ServiceMetrics%'`, парсит `Properties.Metrics.Metrics`, группирует по `Application` и пишет в LiteDB (`HourlyServiceMetrics`).
5. UI админки рендерит метрики из этого кеша.

> ⚠️ Важно: AdminPanel в каждом часе берёт **последний** снапшот по сервису. Counters в нём отражают только последние ~5 секунд — это ограничение текущей реализации (см. `MetricsCollectorService.cs:185`).

## Реестр метрик Beacon

### Счётчики (counters) — накапливаются и сбрасываются каждые 5 секунд

| Метрика                                | Где                                                                                  | Описание                                                                                                          |
| -------------------------------------- | ------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------------------- |
| `server_info_requests`                 | `Host/BeaconApiService.cs` → `GetServerInfo`                                         | Сколько раз клиенты вызвали `GetServerInfo` (включая ошибки). Базовый счётчик трафика.                            |
| `server_info_success`                  | `Host/BeaconApiService.cs` → `GetServerInfo`                                         | Сколько вызовов `GetServerInfo` завершились успешно. Соотношение к `server_info_requests` = success rate.         |
| `server_info_errors`                   | `Host/BeaconApiService.cs` → `GetServerInfo` (catch)                                 | Сколько вызовов `GetServerInfo` упали с исключением (Configuration недоступен, серверная ошибка и т.п.).          |
| `server_info_duration_ms_total`        | `Host/BeaconApiService.cs` → `GetServerInfo`                                         | Сумма времени обработки успешных вызовов `GetServerInfo` за окно. Среднее = `total / server_info_success`.        |
| `configuration_fetch_success`          | `Features/GetServerInfo/GetServerInfoCommandHandler.cs`                              | Сколько отдельных запросов в Configuration service завершились успешно (`Add(9)` на каждый `GetServerInfo` — 9 сервисов; при ошибке `Add(9 - failed)`). |
| `configuration_fetch_errors`           | `Features/GetServerInfo/GetServerInfoCommandHandler.cs`                              | Сколько запросов в Configuration service упали. Маркер проблем со связью Beacon ↔ Configuration.                  |
| `navigator_registrations`              | `Features/RegisterServer/ServerRegistrationService.cs`                               | Сколько раз сервер успешно зарегистрировался в Navigator (раз в 5 минут при штатной работе).                      |
| `navigator_registration_errors`        | `Features/RegisterServer/ServerRegistrationService.cs` (catch)                       | Сколько попыток регистрации в Navigator упали. Маркер проблем со связью Beacon ↔ Navigator.                       |
| `navigator_registration_duration_ms_total` | `Features/RegisterServer/ServerRegistrationService.cs`                           | Сумма длительности успешных RegisterServer-запросов. Среднее = `total / navigator_registrations`.                 |

### Gauges (показатели) — последнее значение, не сбрасываются

| Метрика                            | Где                                                          | Описание                                                                                                         |
| ---------------------------------- | ------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------- |
| `service_started_unix`             | `Program.cs` (один раз при старте)                           | Unix-timestamp старта процесса. Uptime = `now - service_started_unix`. Полезно для индикатора жизни сервиса.     |
| `last_server_info_request_unix`    | `Host/BeaconApiService.cs`                                   | Unix-timestamp последнего успешного `GetServerInfo`. Позволяет понять, идёт ли клиентский трафик.                |
| `last_navigator_registration_unix` | `Features/RegisterServer/ServerRegistrationService.cs`       | Unix-timestamp последней успешной регистрации в Navigator. Если значение «протухло» (>10 мин) — Navigator потерян. |
| `navigator_registration_healthy`   | `Features/RegisterServer/ServerRegistrationService.cs`, `Program.cs` (init=0) | 1 — последняя регистрация прошла успешно, 0 — упала. Бинарный health-флаг для UI.                                |

## Что считаем при отображении в админке

| Производный показатель                  | Формула                                                              |
| --------------------------------------- | -------------------------------------------------------------------- |
| Success rate `GetServerInfo`            | `server_info_success / server_info_requests`                         |
| Среднее время `GetServerInfo`, мс       | `server_info_duration_ms_total / server_info_success`                |
| Среднее время регистрации, мс           | `navigator_registration_duration_ms_total / navigator_registrations` |
| Uptime сервиса, сек                     | `now_unix - service_started_unix`                                    |
| Минут с последней регистрации Navigator | `(now_unix - last_navigator_registration_unix) / 60`                 |
| Минут с последнего клиентского запроса  | `(now_unix - last_server_info_request_unix) / 60`                    |
| Health flag                             | `navigator_registration_healthy`                                     |

## Соглашения по именованию

- `snake_case`, plurals для счётчиков (`server_info_requests`).
- Суффикс `_errors` — счётчики падений, парный к успехам.
- Суффикс `_total` — кумулятивная сумма за окно (мс, байты).
- Суффикс `_unix` — gauge с Unix-timestamp.
- Суффикс `_healthy` — бинарный gauge 0/1.

## Куда добавлять новые метрики

Любая ветка кода, которая:
- обрабатывает gRPC-запрос (вход → счётчик, успех → счётчик, ошибка → счётчик, длительность → `_duration_ms_total`),
- ходит во внешний сервис (Configuration / Navigator) — счётчики успехов/ошибок,
- стартует периодическую работу — gauge времени последнего успешного цикла,

должна писать метрики через `MetricsCollector`. Не дублируем в общий `LogInformation` — для метрик используется только формат `ServiceMetrics {@Metrics}` через `MetricsReporterService`.
