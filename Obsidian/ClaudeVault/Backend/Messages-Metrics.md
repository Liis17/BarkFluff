# BarkFluff.Messages — метрики

Messages регистрирует общий `MetricsCollector` в режиме `BufferAll`; `MetricsReporterService` экспортирует накопленные счётчики пакетами раз в 10 секунд, gauges сохраняют последнее значение. Пути к коду Messages ниже отсчитываются от `Backend/BarkFluff.Messages/`; общая реализация метрик — `Backend/BarkFluff.GrpcServer/Metrics/` от корня репозитория.

## Операции MediatR

`Program.cs` добавляет `MetricsBehavior<TRequest,TResponse>`. `Infrastructure/Behaviors/MetricsBehavior.cs` строит snake_case имя по типу запроса и записывает `<operation>_requests`, `<operation>_success` или `<operation>_errors`, а также `<operation>_duration_ms_total`. Это относится к обработчикам через MediatR; прямые host/stream методы могут иметь отдельные метрики.

## Явные бизнес-метрики

Имена задаются рядом с операцией. Основные группы:

- Действия с сообщениями/чатами: отправка, правка/удаление, прочтение, членство, исходы private/secret приглашений.
- Outbox: gauge `message_outbox_pending`; counters доставок, повторов/ошибок, reclaimed и dead-letter.
- Федерация: импорт сообщений и применённые/устаревшие/отклонённые edit/delete/read события.
- Secret envelopes: отправка/приглашения/принятие и общий размер envelope в байтах.

Точные действующие ключи находятся в вызовах `MetricsCollector` в `Program.cs`, behavior, `BackgroundServices/MessageOutboxDispatcher.cs`, `Features/` и `Consumers/`. `MetricsCollector.Set` задаёт gauge; его значение сохраняется между flush.
