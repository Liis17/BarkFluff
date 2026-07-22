# Масштабирование: BarkFluff.Onliner

**Вердикт: НЕ МОЖЕТ.** Трекинг онлайн-статусов и «печатает…» через gRPC-стримы + фоновый детектор
оффлайна. Проблема двойная: доставка в стримы (как в Updates) **и** общий источник истины о presence.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| Singleton `OnlineStatusSubscriptionsManager` (in-memory реестр + reverse-index) | `Backend/BarkFluff.Onliner/Services/OnlineStatusSubscriptionsManager.cs:17-23` | Подписки на статус — только в памяти инстанса подписчика |
| Singleton `TypingSubscriptionsManager` (in-memory) | `Backend/BarkFluff.Onliner/Services/TypingSubscriptionsManager.cs:17-23` | То же для событий «печатает…» |
| Named `ReceiveEndpoint` (competing) | `Backend/BarkFluff.Onliner/Program.cs` | Событие presence/typing уходит одному инстансу, стрим — на другом |
| `OfflineDetectionService` (дублируемый `BackgroundService`, тик 1 сек) | `Backend/BarkFluff.Onliner/BackgroundServices/OfflineDetectionService.cs:32-55` | N инстансов = N параллельных проходов, гонки в `SetOffline` |
| Источник presence | хранилище статусов (`_storage`) | Онлайн вычисляется из активных соединений; при N инстансах ни один не видит полную картину без общего стора |
| Отзыв сессий (shared) | эндпоинт `session-revoked-onliner` | См. [_shared-token-revocation.md](_shared-token-revocation.md) |

## План реализации

1. **Presence-состояние → Redis.** Онлайн/last-seen хранить в Redis (напр. `SET online:{userId}`
   с TTL-heartbeat, продлеваемым, пока у пользователя есть активный стрим на любом инстансе).
   Тогда presence консистентен между инстансами. Образец работы с `IConnectionMultiplexer` —
   `Backend/BarkFluff.Messages/Infrastructure/SecretMessageBuffer.cs`.
2. **Стрим-эндпоинты → fan-out** (уникальная очередь на инстанс + `AutoDelete`), как в
   [updates.md](updates.md). In-memory реестры подписок остаются локальными; изменение presence
   публикуется fan-out, каждый инстанс доставляет своим подписчикам.
3. **`OfflineDetectionService` → single-runner.** Оффлайн-детекция при Redis-TTL становится почти не
   нужна (TTL сам «гасит» онлайн). Если задача остаётся — выполнять под распределённым локом (Redis)
   или на выделенном singleton-инстансе, чтобы не дублировать проходы и не ловить гонки.
4. **Typing** — эфемерен, тоже через fan-out (хранить в Redis не обязательно, достаточно доставить
   всем инстансам-подписчикам).
5. Отзыв сессий — по общему плану.

## Критерии проверки

- `dotnet build Backend/BarkFluff.Onliner/BarkFluff.Onliner.csproj`.
- Тесты `BarkFluff.Onliner.Tests` (в т.ч. `SessionRevokedConsumerTests`) зелёные.
- Ручная логика: пользователь онлайн на инстансе A; подписчик на инстансе B видит его онлайн;
  при разрыве соединения статус гаснет (TTL) независимо от инстанса.
