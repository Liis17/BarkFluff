# Масштабирование: BarkFluff.Identity

**Вердикт: НЕ МОЖЕТ (единственный блокер — общий отзыв сессий).**

Identity — эмитент JWT и **источник** событий отзыва (`SessionRevokedEvent`). Логика auth/2FA/сессий
работает через PostgreSQL и сама по себе stateless. Единственное, что ломается при N экземплярах, —
распространение отзыва сессий.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| `TokenRevocationCache` (in-memory, competing-consumer) | `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs`; эндпоинт `session-revoked-identity` в `Backend/BarkFluff.Identity/Program.cs` | Отзыв доходит до одного экземпляра; на других отозванный токен проходит до истечения access-token |
| `TokenRevocationCleanupService` (дублируемый таймер) | `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCleanupService.cs` | Каждые 5 мин на каждом экземпляре; не критично (идемпотентная очистка своего кэша) |

Отзыв инициируется в хендлерах `Logout`, `RemoveActiveSession`, `RemoveActiveSessionServer`
(`Backend/BarkFluff.Identity/Features/**`) — они публикуют `SessionRevokedEvent` в RabbitMQ.

## План реализации

1. Применить общий план → [_shared-token-revocation.md](_shared-token-revocation.md): fan-out очередь
   на экземпляр для `SessionRevokedConsumer` (`session-revoked-identity-{InstanceId}` + `AutoDelete`).
2. Больше ничего: остальной код Identity уже подходит для нескольких экземпляров (PostgreSQL,
   Scoped/Transient, idempotent-миграции на старте).

## Критерии проверки

- `dotnet build Backend/BarkFluff.Identity/BarkFluff.Identity.csproj`.
- Тесты `BarkFluff.Identity.Tests` (в т.ч. `SessionRevokedConsumerTests`, `LogoutCommandHandlerTests`,
  `RemoveActiveSession*Tests`) зелёные.
- Ручная логика: отзыв на экземпляре A → оба экземпляра отвергают токен.
