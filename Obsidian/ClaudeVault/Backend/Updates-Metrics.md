# BarkFluff.Updates — реестр метрик

> ↩ Назад: [[Backend/Updates]] · [[Backend/GrpcServer]] (общий механизм) · [[Backend/Beacon-Metrics]] (общая схема + Beacon)

## Как работает сбор метрик

В Seq нет нормальной системы метрик, поэтому метрики микросервисов передаются **через структурированные логи**. Полная схема описана в [[Backend/Beacon-Metrics]] — здесь только специфика Updates.

Кратко: `MetricsCollector` (in-memory) → `MetricsReporterService` каждые 5 сек пишет лог `ServiceMetrics {@Metrics}` → Serilog шлёт в Seq → AdminPanel раз в час парсит фильтром `@Message like 'ServiceMetrics%'` и кеширует в LiteDB.

> ⚠️ AdminPanel в каждом часе берёт **последний** снапшот по сервису. Counters в нём отражают только последние ~5 секунд.

## Регистрация в Program.cs

```csharp
builder.AddBarkFluffSerilog("BarkFluff.Updates");
builder.Services.AddBarkFluffMetrics("BarkFluff.Updates");
// Стартовые gauges
startupMetrics.Set("service_started_unix", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
startupMetrics.Set("new_messages_subscriptions_active", 0);
startupMetrics.Set("read_by_subscriptions_active", 0);
startupMetrics.Set("messages_edited_subscriptions_active", 0);
startupMetrics.Set("messages_deleted_subscriptions_active", 0);
startupMetrics.Set("subscriptions_active_total", 0);
```

## Реестр метрик Updates

### Подписки (gRPC server-streaming)

**Counters** (за окно 5 сек):

| Метрика                                | Где                                      | Описание                                                                              |
| -------------------------------------- | ---------------------------------------- | ------------------------------------------------------------------------------------- |
| `new_messages_subscriptions_opened`    | `Host/UpdatesApiService.cs::SubscribeNewMessages`     | Сколько gRPC-стримов `SubscribeNewMessages` было открыто за окно. Маркер коннектов клиентов. |
| `new_messages_subscriptions_closed`    | `Host/UpdatesApiService.cs::SubscribeNewMessages` (finally) | Сколько стримов закрыто (отмена, дисконнект, выход).                          |
| `read_by_subscriptions_opened`         | `Host/UpdatesApiService.cs::SubscribeMessagesRead`    | Открытые стримы `SubscribeMessagesRead`.                                              |
| `read_by_subscriptions_closed`         | `Host/UpdatesApiService.cs::SubscribeMessagesRead` (finally) | Закрытые стримы `SubscribeMessagesRead`.                                       |
| `messages_edited_subscriptions_opened` | `Host/UpdatesApiService.cs::SubscribeMessagesEdited`  | Открытые стримы `SubscribeMessagesEdited`.                                            |
| `messages_edited_subscriptions_closed` | `Host/UpdatesApiService.cs::SubscribeMessagesEdited` (finally) | Закрытые стримы `SubscribeMessagesEdited`.                                  |
| `messages_deleted_subscriptions_opened`| `Host/UpdatesApiService.cs::SubscribeMessagesDeleted` | Открытые стримы `SubscribeMessagesDeleted`.                                           |
| `messages_deleted_subscriptions_closed`| `Host/UpdatesApiService.cs::SubscribeMessagesDeleted` (finally) | Закрытые стримы `SubscribeMessagesDeleted`.                                |
| `active_subscriptions`                 | все методы API                            | (legacy) суммарный счётчик открытий всех типов. Сохранён ради совместимости с дашбордом. |
| `active_subscriptions_removed`         | все методы API                            | (legacy) суммарный счётчик закрытий всех типов.                                       |

**Gauges** (последнее значение):

| Метрика                              | Где                                              | Описание                                                                                              |
| ------------------------------------ | ------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| `new_messages_subscriptions_active`  | `Host/UpdatesApiService.cs` + `Program.cs` (init=0) | Реальное число открытых стримов `SubscribeNewMessages`. Считывается из `StreamSubscriptionsManager.ActiveCount`. |
| `read_by_subscriptions_active`       | `Host/UpdatesApiService.cs` + `Program.cs` (init=0) | Реальное число открытых стримов `SubscribeMessagesRead`.                                              |
| `messages_edited_subscriptions_active` | `Host/UpdatesApiService.cs` + `Program.cs` (init=0) | Реальное число открытых стримов `SubscribeMessagesEdited`.                                          |
| `messages_deleted_subscriptions_active`| `Host/UpdatesApiService.cs` + `Program.cs` (init=0) | Реальное число открытых стримов `SubscribeMessagesDeleted`.                                         |
| `subscriptions_active_total`         | `Host/UpdatesApiService.cs` + `Program.cs` (init=0) | Сумма активных подписок всех 4 типов. Используется как индикатор «онлайн-клиентов» в админке.       |

### RabbitMQ-события (consumers)

| Метрика                              | Где                                              | Описание                                                                                       |
| ------------------------------------ | ------------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| `rabbitmq_events_consumed`           | все consumer-ы                                   | Общий счётчик потребления RMQ-событий (NewMessage + ReadBy + SessionRevoked + Edited + Deleted). |
| `new_message_events_consumed`        | `Consumers/NewMessageConsumer.cs`                | События `NewMessageEvent` из очереди `new-messages-updates-handler`.                           |
| `new_message_events_errors`          | `Consumers/NewMessageConsumer.cs` (catch)        | Ошибки парсинга бинарного `Message` или паблишинга MediatR-нотификации.                        |
| `read_by_events_consumed`            | `Consumers/ReadByConsumer.cs`                    | События `MessageReadEvent` из очереди `read-receipts-updates-handler`.                         |
| `read_by_events_errors`              | `Consumers/ReadByConsumer.cs` (catch)            | Ошибки обработки события прочтения.                                                            |
| `messages_edited_events_consumed`    | `Consumers/MessageEditedConsumer.cs`             | События `MessageEditedEvent` из очереди `messages-edited-updates-handler`.                     |
| `messages_edited_events_errors`      | `Consumers/MessageEditedConsumer.cs` (catch)     | Ошибки парсинга/публикации MediatR при правке.                                                 |
| `messages_deleted_events_consumed`   | `Consumers/MessageDeletedConsumer.cs`            | События `MessageDeletedEvent` из очереди `messages-deleted-updates-handler`.                   |
| `messages_deleted_events_errors`     | `Consumers/MessageDeletedConsumer.cs` (catch)    | Ошибки публикации MediatR-уведомления при удалении.                                            |
| `session_revoked_events_consumed`    | `Consumers/SessionRevokedConsumer.cs`            | События `SessionRevokedEvent` из очереди `session-revoked-updates`.                            |
| `sessions_revoked`                   | `Consumers/SessionRevokedConsumer.cs`            | Сессии, инвалидированные через `TokenRevocationCache.Revoke()`. Маркер форс-логаутов.          |

### Доставка в стримы (handlers / broadcast)

| Метрика                              | Где                                                                                  | Описание                                                                                         |
| ------------------------------------ | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------ |
| `new_messages_broadcast`             | `Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs`            | Сколько `NewMessageEvent` успешно записано в gRPC-стримы (одна запись = один пользователь × один девайс). |
| `new_messages_broadcast_errors`      | тот же файл (catch)                                                                  | Ошибки записи в стрим (мёртвый клиент). Не критично — стрим закроется через `OperationCanceledException`. |
| `events_broadcast`                   | тот же файл                                                                          | (legacy) синоним `new_messages_broadcast`. Сохранён для совместимости с дашбордом.               |
| `events_broadcast_errors`            | тот же файл                                                                          | (legacy) синоним `new_messages_broadcast_errors`.                                                |
| `read_by_broadcast`                  | `Features/SubscribeMessagesRead/Handlers/ReadByNotificationHandler.cs`               | Сколько `MessageReadEvent` успешно записано в стримы.                                            |
| `read_by_broadcast_errors`           | тот же файл (catch)                                                                  | Ошибки записи `MessageReadEvent` в стрим.                                                        |
| `messages_edited_broadcast`          | `Features/SubscribeMessagesEdited/Handlers/MessageEditedNotificationHandler.cs`      | Сколько `MessageEditedEvent` успешно записано в стримы.                                          |
| `messages_edited_broadcast_errors`   | тот же файл (catch)                                                                  | Ошибки записи `MessageEditedEvent` в стрим.                                                      |
| `messages_deleted_broadcast`         | `Features/SubscribeMessagesDeleted/Handlers/MessageDeletedNotificationHandler.cs`    | Сколько `MessageDeletedEvent` успешно записано в стримы.                                         |
| `messages_deleted_broadcast_errors`  | тот же файл (catch)                                                                  | Ошибки записи `MessageDeletedEvent` в стрим.                                                     |

### Push-уведомления (отложенный пайплайн)

| Метрика                              | Где                                                                                   | Описание                                                                                                 |
| ------------------------------------ | ------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| `push_notifications_scheduled`       | `Features/PushNotifications/PushNotificationSchedulerHandler.cs`                      | Сколько push-уведомлений было запланировано (по одному на каждого получателя, отложено на 5 сек).        |
| `push_notifications_sent`            | тот же файл                                                                           | Сколько `PushNotificationEvent` успешно опубликовано в RabbitMQ → CloudMessaging.                        |
| `push_notifications_cancelled`       | тот же файл (`OperationCanceledException`)                                            | Сколько push-уведомлений было отменено из-за прочтения сообщения в окне 5 сек.                           |
| `push_notifications_errors`          | тот же файл (`Exception`)                                                             | Ошибки при публикации `PushNotificationEvent` (RabbitMQ недоступен и т.п.).                              |

### Системные gauges

| Метрика                              | Где                  | Описание                                                                  |
| ------------------------------------ | -------------------- | ------------------------------------------------------------------------- |
| `service_started_unix`               | `Program.cs`         | Unix-timestamp старта процесса. Uptime = `now - service_started_unix`.    |

## Производные показатели для админки

| Производный показатель                         | Формула                                                                                              |
| ---------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| Uptime сервиса, сек                            | `now_unix - service_started_unix`                                                                    |
| Текущее число клиентов онлайн                  | `subscriptions_active_total` (gauge)                                                                 |
| Скорость подключений новых клиентов            | `new_messages_subscriptions_opened / 5s`                                                             |
| Текучка подписок (churn)                       | `(new_messages_subscriptions_closed + read_by_subscriptions_closed) / 5s`                            |
| Доля упавших broadcast'ов NewMessage           | `new_messages_broadcast_errors / (new_messages_broadcast + new_messages_broadcast_errors)`           |
| Доля упавших broadcast'ов ReadBy               | `read_by_broadcast_errors / (read_by_broadcast + read_by_broadcast_errors)`                          |
| Доля отменённых пушей (читают быстро)          | `push_notifications_cancelled / push_notifications_scheduled`                                        |
| Доля отправленных пушей                        | `push_notifications_sent / push_notifications_scheduled`                                             |
| Среднее число доставок на одно RMQ-событие     | `new_messages_broadcast / new_message_events_consumed` (≈ среднее число активных стримов на чат)    |
| Доля упавших RMQ-событий NewMessage            | `new_message_events_errors / new_message_events_consumed`                                            |

## Соглашения по именованию

- `snake_case`, plurals для счётчиков.
- Префиксы по домену (`new_messages_*`, `read_by_*`, `push_notifications_*`, `session_*`).
- Суффикс `_errors` — счётчики падений, парный к успехам.
- Суффикс `_active` — gauge с текущим числом.
- Суффикс `_unix` — gauge с Unix-timestamp.
- `_opened` / `_closed` — для подписочных событий с симметрией.

## Куда добавлять новые метрики

Любая ветка кода, которая:

- открывает или закрывает gRPC-стрим — `Host/UpdatesApiService.cs` (counter + обновление gauge через `manager.ActiveCount`).
- обрабатывает RabbitMQ-сообщение — соответствующий `Consumers/*Consumer.cs` (общий + специализированный счётчик + парный `_errors`).
- рассылает событие в gRPC-стримы — соответствующий `Handlers/*Handler.cs` (`*_broadcast` + `*_broadcast_errors`).
- работает с push-пайплайном — `Features/PushNotifications/PushNotificationSchedulerHandler.cs`.

Метрики не дублируем в `LogInformation` — они уходят строго через `MetricsCollector` → `MetricsReporterService` в формате `ServiceMetrics {@Metrics}`.
