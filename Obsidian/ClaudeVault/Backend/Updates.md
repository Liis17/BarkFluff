# BarkFluff.Updates

Updates преобразует RabbitMQ domain events в gRPC-потоки для пользователей и устройств. Состояние сообщений он не хранит и не ведёт журнал повторного воспроизведения; после reconnect клиент догружает состояние через сервисы-владельцы. Пути к коду ниже указаны относительно `Backend/BarkFluff.Updates/`; `Shared/` и `Tests/` — от корня репозитория.

## Потоки событий

`Shared/BarkFluff.Proto/updates_api.proto` объявляет 17 подписок:

- Обычные чаты: новое сообщение, read, edit/delete, pin/unpin, unpin-all и hide.
- Личные чаты: encrypted message/edit/delete/read и invite/resolution.
- Секретные чаты: invite/resolution и доставка encrypted envelope.

Обычные и private-потоки имеют user scope; secret-потоки — device scope. XAuth определяет пользователя/устройство. Поля событий и фильтры задаются proto и соответствующими `Features/Subscribe*/Handlers/`.

## Доставка

Каждый инстанс Updates создаёт отдельные RabbitMQ очереди с `InstanceId.Current`, `AutoDelete=true`, `Durable=false`. Каждый активный инстанс получает копию события и рассылает её своим локальным stream managers. Сообщения не сохраняются, пока инстанс выключен. `Consumers/` связывает broker events с handlers; `Host/UpdatesApiService.cs` регистрирует и снимает подписки.

Push notifications используют отдельный от stream delivery pipeline в `Features/PushNotifications/`; прочтение сообщения может отменить отложенный push.

Основные файлы: `Program.cs`, `Host/UpdatesApiService.cs`, `DependencyInjection.cs`, `Consumers/`, `Features/Subscribe*/`, `Features/PushNotifications/`.

Связанные заметки: [[Backend/Messages]], [[Backend/Onliner]], [[Backend/CloudMessaging]]. Метрики: [[Backend/Updates-Metrics]].
