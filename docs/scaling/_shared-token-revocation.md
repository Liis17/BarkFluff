# Общий блокер: отзыв сессий (TokenRevocation) при масштабировании

Затрагивает: **Identity, Users, Messages, Files, Bots, Updates, Onliner, Calls** — все, кто
подключает `AddXAuth` и держит `SessionRevokedConsumer`.

## Суть проблемы

`Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs`:

```csharp
public class TokenRevocationCache
{
    private readonly ConcurrentDictionary<string, DateTime> _revokedSessions = new();
    public void Revoke(long userId, string deviceId, DateTime accessTokenExpiresAt) { ... }
    public bool IsRevoked(long userId, string deviceId) => _revokedSessions.ContainsKey(...);
}
```

Регистрируется как `Singleton` в `XAuthExtensions.cs`. Кэш заполняется из RabbitMQ-события
`SessionRevokedEvent` через `SessionRevokedConsumer` каждого сервиса. Пример (Users):

```csharp
// Backend/BarkFluff.Users/Consumers/SessionRevokedConsumer.cs
cache.Revoke(msg.UserId, msg.DeviceId, msg.AccessTokenExpiresAt);
```

Но `ReceiveEndpoint` — фиксированное именованное имя очереди:

```csharp
// Backend/BarkFluff.Users/Program.cs
cfg.ReceiveEndpoint("session-revoked-users", e => e.ConfigureConsumer<SessionRevokedConsumer>(context));
```

**Почему ломается при N экземплярах:** два экземпляра Users подписываются на **одну и ту же** очередь
`session-revoked-users`. RabbitMQ отдаёт каждое сообщение только **одному** из них (competing
consumers). Значит `Revoke()` вызовется лишь на одном экземпляре; на остальных `IsRevoked()`
вернёт `false`, и отозванный токен пройдёт проверку до истечения access-token.

## Рекомендуемое решение — fan-out очередь на экземпляр

Минимальное изменение, без новой инфраструктуры: каждый экземпляр должен получать **каждое** событие
отзыва. Для этого имя `ReceiveEndpoint` для `SessionRevokedConsumer` должно быть **уникальным на
экземпляр** (тогда MassTransit создаёт отдельную очередь, привязанную к тому же exchange, и каждая
получает копию события).

### Шаги

1. Ввести идентификатор экземпляра. Взять `Environment.GetEnvironmentVariable("HOSTNAME")` (в Docker
   это ID контейнера) или сгенерировать `Guid` на старте. Вынести в хелпер, например
   `InstanceId.Current` рядом с `XAuthExtensions`.
2. Для эндпоинта `SessionRevokedConsumer` в **каждом** сервисе заменить фиксированное имя на
   уникальное и сделать очередь авто-удаляемой (чтобы не копить мёртвые очереди после рестартов):

   ```csharp
   cfg.ReceiveEndpoint($"session-revoked-users-{InstanceId.Current}", e =>
   {
       e.AutoDelete = true;           // очередь исчезает при отключении экземпляра
       e.Durable = false;
       e.ConfigureConsumer<SessionRevokedConsumer>(context);
   });
   ```

   Файлы: `Program.cs` сервисов Identity, Users, Messages, Files, Bots, Updates, Onliner, Calls
   (эндпоинты `session-revoked-*`).
3. `TokenRevocationCache` и `TokenRevocationCleanupService` менять не нужно — они уже per-instance и
   станут консистентны, как только каждый экземпляр начнёт получать все события.

> Тот же fan-out-приём нужен и для доставки контента в Updates/Onliner/Calls — см. их файлы. Там его
> удобно применить единообразно ко всем стрим-эндпоинтам.

## Альтернатива — общий Redis

Если fan-out по каким-то причинам нежелателен, вынести отзыв в общий стор:

- `TokenRevocationCache` → обёртка над Redis: `Revoke` = `SET revoked:{userId}:{deviceId}` с TTL до
  `accessTokenExpiresAt`; `IsRevoked` = `EXISTS`. TTL сам чистит записи — `TokenRevocationCleanupService`
  становится не нужен.
- `SessionRevokedConsumer` можно оставить (или убрать, публикуя прямо из Identity в Redis).
- Плюс: не плодит очередей. Минус: +1 сетевой вызов Redis на каждый gRPC-запрос в XAuth
  (можно смягчить локальным кэшем с коротким TTL).

Redis в проекте уже подключён — образец использования `IConnectionMultiplexer` в
`Backend/BarkFluff.Messages/Infrastructure/SecretMessageBuffer.cs`.

## Критерии проверки

- `dotnet build` всех затронутых сервисов проходит.
- Существующие тесты `SessionRevokedConsumerTests` (Users/Identity/Onliner/Files/Federation)
  зелёные; при необходимости добавить тест на «два экземпляра — оба видят отзыв» (два кэша, событие
  доставлено обоим при fan-out).
- Ручная логика: при 2 экземплярах отзыв сессии на любом из них → `IsRevoked` == `true` на обоих в
  пределах времени доставки RabbitMQ.
