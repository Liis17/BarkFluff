# Масштабирование: BarkFluff.Federation

**Вердикт: НЕ МОЖЕТ.** Исходящие federated-события рассылает фоновый outbox-диспетчер; при N
экземплярах он дублируется, а in-memory rate-limiter discovery не работает глобально.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| `OutboxDispatcher` (дублируемый `BackgroundService`, тик 5 сек) | `Backend/BarkFluff.Federation/BackgroundServices/OutboxDispatcher.cs:56-76` | N инстансов читают одни и те же `Pending`-записи и шлют их → дублирующая доставка чужим серверам, гонки по статусу |
| `OutboxJanitor` (дублируемый `BackgroundService`, тик 1 час) | `Backend/BarkFluff.Federation/BackgroundServices/OutboxJanitor.cs:26-45` | Чистка через `ExecuteDeleteAsync` идемпотентна, но выполняется N раз |
| Singleton `DiscoveryTriggerRateLimiter` (in-memory cooldown) | `Backend/BarkFluff.Federation/Services/DiscoveryTriggerRateLimiter.cs:11-23` | Cooldown per-instance: discovery для одного сервера запустится с каждого инстанса → флуд запросов к чужим well-known |
| Singleton `ActiveSigningKeyCache` | `Backend/BarkFluff.Federation/Program.cs:71` | После ротации ключей инстансы рассинхронизированы; менее критично (можно TTL/инвалидация) |
| Отзыв сессий (shared) | эндпоинт `session-revoked-*` federation; consumer `Consumers/SessionRevokedConsumer.cs` | См. [_shared-token-revocation.md](_shared-token-revocation.md) |

## План реализации

1. **Outbox → безопасный конкурентный забор.** Вариант A (минимальный, без Redis): в `OutboxDispatcher`
   выбирать и «застолбить» записи атомарно через PostgreSQL `SELECT … FOR UPDATE SKIP LOCKED`
   (пометить `Processing` в одной транзакции). Тогда несколько инстансов делят работу без дублей и
   даже ускоряют рассылку. Вариант B: единственный экземпляр под распределённым локом (Redis) /
   leader election.
2. **`OutboxJanitor` → single-runner** под распределённым локом (или на лидере). Не критично, но
   убирает лишние проходы.
3. **`DiscoveryTriggerRateLimiter` → Redis.** Cooldown-ключ `discovery:{serverName}` со `SET NX EX 300`
   — глобальный, один discovery-триггер на сервер в пределах окна.
4. **`ActiveSigningKeyCache`** — добавить TTL/инвалидацию через pub/sub при ротации ключей, чтобы все
   инстансы переключались на новый активный ключ.
5. Отзыв сессий — по общему плану.

## Критерии проверки

- `dotnet build Backend/BarkFluff.Federation/BarkFluff.Federation.csproj`.
- Тесты `BarkFluff.Federation.Tests` (в т.ч. `SessionRevokedConsumerTests`) зелёные; добавить тест на
  то, что при 2 инстансах запись outbox доставляется ровно один раз (SKIP LOCKED).
- Ручная логика: 2 инстанса; исходящее событие уходит принимающему серверу ровно один раз; повторный
  discovery одного сервера в окне 5 мин не запускается со второго инстанса.
