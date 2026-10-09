# BarkFluff.Updates — метрики

Updates регистрирует общий `MetricsCollector` в режиме `BufferAll`; накопленные счётчики экспортируются раз в 10 секунд, gauges хранят последнее значение. Пути к коду Updates ниже отсчитываются от `Backend/BarkFluff.Updates/`; общая реализация метрик — `Backend/BarkFluff.GrpcServer/Metrics/` от корня репозитория.

## Действующие группы

- Открытие/закрытие streams и active counts для обычных, private и secret subscriptions. `subscriptions_active_total` суммирует все stream managers; legacy counters `active_subscriptions` сохранены для совместимости.
- RabbitMQ: общий `rabbitmq_events_consumed`, счётчики по типам событий и ошибки consumer-ов.
- Broadcast в streams: счётчики доставки/ошибок по событиям; secret envelopes различают доставку только в буфер и активный stream.
- Pipeline отложенных push notifications и gauge времени старта сервиса.

Источники точных ключей: `Host/UpdatesApiService.cs`, `Consumers/`, `Features/Subscribe*/Handlers/`, `Features/PushNotifications/`. Метрики, заданные через `MetricsCollector.Set`, — gauges; `Increment`/`Add` — counters. Общая реализация: `Backend/BarkFluff.GrpcServer/Metrics/`.
