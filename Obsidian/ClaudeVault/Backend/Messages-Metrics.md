# BarkFluff.Messages — реестр метрик

> ↩ Назад: [[Backend/Messages]] · [[Backend/GrpcServer]] (общий механизм) · [[Backend/Beacon-Metrics]] (тот же механизм для Beacon)

## Как работает сбор метрик

Тот же механизм, что и в [[Beacon-Metrics]]:

1. `MetricsCollector.Increment / Add / Set` (in-memory, потокобезопасно).
2. `MetricsReporterService` каждые 5 секунд пишет лог `LogInformation("ServiceMetrics {@Metrics}", ...)`.
3. Serilog → Seq.
4. `Barkfluff.AdminPanel/Services/MetricsCollectorService` раз в час забирает **последний** снапшот часа из Seq по фильтру `@Message like 'ServiceMetrics%'` и пишет в LiteDB.

> ⚠️ AdminPanel хранит только последний снапшот часа — counters отражают активность за последние ~5 секунд. Используй `*_total` для трендов и кумулятивных оценок.

## Особенность Messages: автоматические метрики через MediatR

В сервисе зарегистрирован `MetricsBehavior<TRequest, TResponse>` (`Backend/BarkFluff.Messages/Infrastructure/Behaviors/MetricsBehavior.cs`), который для **каждой** MediatR-команды/запроса автоматически записывает 4 счётчика:

- `{op}_requests` — все вызовы handler'а
- `{op}_success` — успешные
- `{op}_errors` — упавшие с исключением
- `{op}_duration_ms_total` — сумма длительности

Имя `{op}` = имя класса Command/Query без суффикса `Command`/`Query`/`Handler`, в snake_case. Например, `SendMessageCommand` → `send_message`.

Регистрация:

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblyContaining<Program>();
    cfg.AddOpenBehavior(typeof(MetricsBehavior<,>));
});
```

## Реестр метрик Messages

### Auto-counters (MediatR pipeline) — сбрасываются каждые 5 секунд

Все 11 операций получают одинаковую четвёрку `{op}_requests / _success / _errors / _duration_ms_total`:

| Операция                  | Что делает handler                                                       |
| ------------------------- | ------------------------------------------------------------------------ |
| `send_message`            | Отправка сообщения (текст / вложения / форвард / создание DM по ходу)    |
| `list_chats`              | Список чатов пользователя                                                |
| `list_messages`           | Список сообщений чата с двунаправленной пагинацией                       |
| `mark_as_read`            | Отметка набора сообщений прочитанными                                    |
| `create_group_chat`       | Создание группового чата + системное сообщение                           |
| `kick_user`               | Исключение участника из группового чата                                  |
| `get_person_chat_id`      | Получение/создание личного (DM) чата                                     |
| `get_chat_info`           | Информация о чате (название, аватар, непрочитанные)                      |
| `list_chat_members`       | Список участников чата                                                   |
| `list_chat_attachments`   | Список вложений чата (галерея/документы)                                 |
| `get_user_all_messages`   | GDPR-экспорт всей истории пользователя                                   |

### Доменные счётчики — сбрасываются каждые 5 секунд

| Метрика                                | Где                                                                       | Что считаем                                                                |
| -------------------------------------- | ------------------------------------------------------------------------- | -------------------------------------------------------------------------- |
| `messages_sent`                        | `Features/SendMessage/SendMessageCommandHandler.cs` (в самом конце успеха)| Реально отправленные сообщения (после публикации в очередь)                |
| `messages_sent_with_text`              | `SendMessageCommandHandler.cs`                                            | Из `messages_sent` — те, что содержат текст                                |
| `messages_sent_with_attachments`       | `SendMessageCommandHandler.cs`                                            | Из `messages_sent` — те, что содержат вложения                             |
| `attachments_total`                    | `SendMessageCommandHandler.cs` (`Add(count)`)                             | Суммарное количество прикреплённых файлов за окно                          |
| `messages_forwarded`                   | `SendMessageCommandHandler.cs` (на каждое успешное добавление пересылки)  | Количество форвардов внутри сообщений                                      |
| `messages_marked_as_read`              | `Features/MarkAsRead/MarkAsReadCommandHandler.cs` (`Add(messages.Count)`) | Сколько сообщений реально отмечено (а не количество вызовов)               |
| `chats_created_person`                 | `SendMessageCommandHandler.cs` + `Features/GetPersonChatId/...Handler.cs` | Новые личные (DM) чаты                                                     |
| `chats_created_group`                  | `Features/CreateGroupChat/CreateGroupChatCommandHandler.cs`               | Новые групповые чаты                                                       |
| `chats_created_group_members_total`    | `CreateGroupChatCommandHandler.cs` (`Add(UserIds.Count)`)                 | Сумма размеров создаваемых групп. Среднее = `_total / chats_created_group` |
| `users_kicked`                         | `Features/KickUser/KickUserCommandHandler.cs` (после публикации)          | Успешные кики                                                              |

### RabbitMQ-консьюмеры

| Метрика                                | Consumer                                                                 |
| -------------------------------------- | ------------------------------------------------------------------------ |
| `rabbitmq_avatar_consumed`             | `Consumers/UserChangedAvatarConsumer.cs`                                 |
| `rabbitmq_avatar_errors`               | catch в `UserChangedAvatarConsumer`                                      |
| `rabbitmq_name_consumed`               | `Consumers/UserChangedNameConsumer.cs`                                   |
| `rabbitmq_name_errors`                 | catch в `UserChangedNameConsumer`                                        |
| `rabbitmq_session_revoked_consumed`    | `Consumers/SessionRevokedConsumer.cs`                                    |

> ⚠️ Старая объединённая метрика `rabbitmq_events_consumed` удалена — расщеплена на три именованных, чтобы можно было видеть в админке тренды по типам событий.

### Gauges — последнее значение, не сбрасываются

| Метрика                       | Где                                          | Значение                                                |
| ----------------------------- | -------------------------------------------- | ------------------------------------------------------- |
| `service_started_unix`        | `Program.cs` после `app.Build()`             | Unix-timestamp старта сервиса (для расчёта uptime)      |
| `last_message_sent_unix`      | `SendMessageCommandHandler.cs` (на успехе)   | Unix-timestamp последней успешной `SendMessage`         |

## Производные значения (примеры формул для AdminPanel)

- **Success rate** `send_message`: `send_message_success / send_message_requests`
- **Avg latency** `send_message` (мс): `send_message_duration_ms_total / (send_message_success + send_message_errors)`
- **Среднее число вложений на сообщение с вложениями**: `attachments_total / messages_sent_with_attachments`
- **Средний размер новой группы**: `chats_created_group_members_total / chats_created_group`
- **Uptime сервиса (сек)**: `now_unix - service_started_unix`
- **Минут с последней отправки**: `(now_unix - last_message_sent_unix) / 60`

## Где менять/добавлять метрики

| Что добавляем                            | Куда                                                                        |
| ---------------------------------------- | --------------------------------------------------------------------------- |
| Новая команда/запрос MediatR             | Auto-метрики появятся бесплатно через `MetricsBehavior`                     |
| Доменное событие (новый тип сообщения)   | В handler **после** валидации/публикации, не в gRPC-фасаде                  |
| Новый RabbitMQ-consumer                  | `rabbitmq_{event}_consumed` + `rabbitmq_{event}_errors` (catch)             |
| Длительность операции вне MediatR        | `Stopwatch` + `Add("{op}_duration_ms_total", sw.ElapsedMilliseconds)`       |

## Соглашения именования

- snake_case
- `_errors` — пара к `_success`
- `_total` — кумулятивная сумма (мс / число элементов)
- `_unix` — Unix-timestamp
- `_healthy` — бинарный 0/1

## Связанные файлы

- `Backend/BarkFluff.Messages/Infrastructure/Behaviors/MetricsBehavior.cs` — MediatR pipeline behavior
- `Backend/BarkFluff.Messages/Program.cs` — регистрация behavior + стартовый gauge
- `Backend/BarkFluff.GrpcServer/Metrics/MetricsCollector.cs` — общий сборщик
- `Backend/BarkFluff.GrpcServer/Metrics/MetricsReporterService.cs` — публикация в Seq
- `Backend/Barkfluff.AdminPanel/Services/MetricsCollectorService.cs` — потребитель в AdminPanel
