# BarkFluff.Updates — карта проекта

Updates поддерживает gRPC server-streaming подписки и переводит события RabbitMQ в локальные client streams. Путь к коду компонента ниже — относительно `Backend/BarkFluff.Updates/`; `Shared/` и `Tests/` — от корня репозитория.

## Точки входа

- `Program.cs` регистрирует 17 stream RPC, consumer-ы событий и отдельные RabbitMQ очереди на каждый инстанс (`InstanceId.Current`, autodelete, non-durable).
- `Host/UpdatesApiService.cs` получает user/device из XAuth, открывает/закрывает подписки и обновляет active gauges.
- `DependencyInjection.cs` регистрирует handlers, managers и notifications.

## Семейства событий

- Обычные чаты: new/read/edited/deleted/pinned/unpinned/all-unpinned/hidden.
- Private chats: encrypted message/edit/delete/read и invite/resolution.
- Secret chats: invite/resolution и зашифрованный envelope.
- `Consumers/` принимает RabbitMQ события; `Features/Subscribe*/Handlers/` выбирает адресатов и вызывает broadcast managers.
- Обычные и private subscriptions используют user scope; secret invite/message streams — device scope. Точные фильтры и payload см. в proto и handlers.

## Ограничение

Очереди — временный fan-out каждого живого инстанса, а не долговременный replay log. После reconnect клиент восстанавливает состояние запросами сервисов-владельцев.

← [[Backend/Updates]]
