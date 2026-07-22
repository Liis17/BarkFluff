# Масштабирование: BarkFluff.Messages

**Вердикт: НЕ МОЖЕТ (единственный блокер — общий отзыв сессий).**

Сообщения, групповые чаты, read-receipts — на PostgreSQL. Singleton-сервисы, которые могли бы быть
блокерами, уже вынесены в **Redis** и потому безопасны:

- `SecretMessageBuffer` — данные в Redis (`Backend/BarkFluff.Messages/Infrastructure/SecretMessageBuffer.cs`,
  `IConnectionMultiplexer` / `Db.StringSetAsync`).
- `PrivateChatInviteStore` — данные в Redis (`.../PrivateChatInviteStore.cs`).

Это же — эталон для остальных сервисов, куда стоит вынести состояние. Единственный оставшийся блокер —
распространение отзыва сессий.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| `TokenRevocationCache` (in-memory, competing-consumer) | `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs`; эндпоинт `session-revoked-messages` в `Backend/BarkFluff.Messages/Program.cs`; consumer `Backend/BarkFluff.Messages/Consumers/SessionRevokedConsumer.cs` | Отзыв доходит до одного экземпляра; на других отозванный токен проходит до истечения access-token |

## План реализации

1. Применить общий план → [_shared-token-revocation.md](_shared-token-revocation.md): fan-out очередь
   на экземпляр для `SessionRevokedConsumer` (`session-revoked-messages-{InstanceId}` + `AutoDelete`).
2. Больше ничего: остальной код Messages готов к нескольким экземплярам (PostgreSQL + Redis).

## Критерии проверки

- `dotnet build Backend/BarkFluff.Messages/BarkFluff.Messages.csproj`.
- Тесты `BarkFluff.Messages.Tests` зелёные.
- Ручная логика: отзыв на экземпляре A → оба экземпляра отвергают токен.
