# Масштабирование: BarkFluff.Users

**Вердикт: НЕ МОЖЕТ (единственный блокер — общий отзыв сессий).**

Профили, связи, бейджи — всё на PostgreSQL, доступ через Scoped/Transient. `ReservedUsernamesService`
(Singleton) безопасен: read-only список, загружаемый из конфига при старте (одинаков на всех
экземплярах). Единственный блокер — распространение отзыва сессий.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| `TokenRevocationCache` (in-memory, competing-consumer) | `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs`; эндпоинт `session-revoked-users` в `Backend/BarkFluff.Users/Program.cs`; consumer `Backend/BarkFluff.Users/Consumers/SessionRevokedConsumer.cs` | Отзыв доходит до одного экземпляра; на других отозванный токен проходит до истечения access-token |

## План реализации

1. Применить общий план → [_shared-token-revocation.md](_shared-token-revocation.md): fan-out очередь
   на экземпляр для `SessionRevokedConsumer` (`session-revoked-users-{InstanceId}` + `AutoDelete`).
2. Больше ничего: остальной код Users готов к нескольким экземплярам.

## Критерии проверки

- `dotnet build Backend/BarkFluff.Users/BarkFluff.Users.csproj`.
- Тесты `BarkFluff.Users.Tests` (в т.ч. `SessionRevokedConsumerTests`) зелёные.
- Ручная логика: отзыв на экземпляре A → оба экземпляра отвергают токен.
