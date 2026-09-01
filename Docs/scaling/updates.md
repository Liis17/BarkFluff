# Масштабирование: BarkFluff.Updates

**Вердикт: НЕ МОЖЕТ.** Real-time доставка обновлений через gRPC server-streaming; подписки хранятся
в памяти процесса, а события разбираются competing-consumer'ами.

## Как работает сейчас

Клиент открывает long-lived gRPC-стрим (`SubscribeNewMessages` и ещё ~15 методов). Стрим
регистрируется в Singleton-менеджере и висит до отмены:

```csharp
// Backend/BarkFluff.Updates/Host/UpdatesApiService.cs
var subscriptionId = _newMessagesSubscriptionsManager.RegisterSubscription(userId, responseStream);
await Task.Delay(Timeout.Infinite, context.CancellationToken);
```

События из других сервисов приходят по RabbitMQ, консьюмер находит стримы в **локальном** реестре и
пишет в них:

```csharp
// Backend/BarkFluff.Updates/Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs
var streams = _subscriptionsManager.GetUserStreams(memberId);   // только этот процесс
foreach (var stream in streams) await stream.WriteAsync(newMessageEvent, ct);
```

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| 16 Singleton `StreamSubscriptionsManager` c `ConcurrentDictionary<…, IServerStreamWriter<…>>` | `Backend/BarkFluff.Updates/DependencyInjection.cs:11-26`; напр. `Features/SubscribeNewMessages/StreamSubscriptionsManager.cs:13` | Реестр стримов — только в памяти инстанса, к которому подключён клиент |
| Named `ReceiveEndpoint` (competing consumers) | `Backend/BarkFluff.Updates/Program.cs:60-143` (`new-messages-updates-handler` и др.) | Событие получает **один** инстанс; если подписчик подключён к другому — доставки нет |
| Отзыв сессий (shared) | эндпоинт `session-revoked-updates` в `Program.cs:70` | См. [_shared-token-revocation.md](_shared-token-revocation.md) |

Итог: с competing-consumer событие попадает на случайный инстанс, а стрим клиента — на конкретный.
Совпадение — лотерея, большинство обновлений теряется.

## План реализации

Ключевая идея: **не нужно** реплицировать реестр стримов между инстансами. Достаточно, чтобы
**каждый** инстанс получал **каждое** событие (fan-out); стрим клиента живёт ровно на одном инстансе,
туда fan-out-событие и придёт, остальные инстансы просто не найдут локальных стримов (no-op).

1. **Сделать все стрим-эндпоинты fan-out** — уникальное имя очереди на инстанс + `AutoDelete`.
   Правится единообразно для всех `ReceiveEndpoint` в `Backend/BarkFluff.Updates/Program.cs`:

   ```csharp
   cfg.ReceiveEndpoint($"new-messages-updates-{InstanceId.Current}", e =>
   {
       e.AutoDelete = true; e.Durable = false;
       e.ConfigureConsumer<NewMessageConsumer>(context);
   });
   ```

   (Идентификатор инстанса — см. [_shared-token-revocation.md](_shared-token-revocation.md).)
2. **Оставить `StreamSubscriptionsManager` и хендлеры как есть** — локальный реестр корректен при
   fan-out. `GetUserStreams` вернёт пусто на инстансах без подписчика — это нормально.
3. **Метрики** `*_subscriptions_active` (`Program.cs:158-174`) становятся per-instance — учитывать
   это в дашбордах (суммировать по инстансам), либо экспортировать с меткой инстанса.
4. Отзыв сессий — по общему плану (тот же fan-out для `session-revoked-updates`).

> Замечание про издержки: fan-out означает, что каждый инстанс получает каждое событие и фильтрует
> локально. Для presence/сообщений объём приемлем; если в будущем событий станет очень много —
> рассмотреть Redis pub/sub-backplane с маршрутизацией по владельцу подписки. Для v1 fan-out проще и
> не требует новой инфраструктуры.

## Критерии проверки

- `dotnet build Backend/BarkFluff.Updates/BarkFluff.Updates.csproj`.
- Тесты `BarkFluff.Updates.Tests` зелёные.
- Ручная логика: 2 инстанса Updates; клиент подписан на A; событие из Messages → доставлено клиенту
  вне зависимости от того, на каком инстансе оно было сгенерировано.
