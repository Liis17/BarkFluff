# Масштабирование: BarkFluff.Bots

**Вердикт: НЕ МОЖЕТ.** Четыре in-memory Singleton-сервиса держат состояние, критичное для Bot API
(rate-limit, сигнал новых апдейтов, реестр ботов, guard одиночного поллинга). В коде уже есть прямые
пометки разработчика «при масштабировании переезжает в Redis».

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| `BotRateLimiter` (in-memory счётчик 30 req/s) | `Backend/BarkFluff.Bots/Services/BotRateLimiter.cs:9-27` | Счётчик per-instance: при N инстансах бот получает 30×N req/s вместо 30 |
| `BotUpdateNotifier` (in-memory `TaskCompletionSource`) | `Backend/BarkFluff.Bots/Services/BotUpdateNotifier.cs:6-46` | Сигнал «новый update» будит только ждущих на этом инстансе; long-poll/стрим на другом инстансе не проснётся (комментарий в коде прямо это отмечает) |
| `BotRegistryCache` (in-memory реестр ботов) | `Backend/BarkFluff.Bots/Services/BotRegistryCache.cs:7-37` | Обновление бота через один инстанс не видно другим до перезагрузки |
| `BotPollingGuard` (in-memory guard одного активного потока) | `Backend/BarkFluff.Bots/Services/BotPollingGuard.cs:5-17` | Два клиента на разных инстансах одновременно пройдут guard для одного бота |
| `BotAccessValidator` (зависит от двух предыдущих) | `Backend/BarkFluff.Bots/Services/BotAccessValidator.cs:13-46` | Наследует проблемы `BotRegistryCache` + `BotRateLimiter` |
| `BotsCleanupService` (дублируемый таймер) | `Backend/BarkFluff.Bots/Program.cs:73` | Каждый час на каждом инстансе; идемпотентный DELETE, не критично |
| Отзыв сессий (shared) | эндпоинт `session-revoked-*` bots | См. [_shared-token-revocation.md](_shared-token-revocation.md) |

## План реализации

Всё сводится к переносу четырёх состояний в **Redis** (уже подключён; образец —
`Backend/BarkFluff.Messages/Infrastructure/SecretMessageBuffer.cs`).

1. **`BotRateLimiter` → Redis.** Скользящее окно через `INCR` ключа `botrate:{botId}:{unixSecond}` +
   `EXPIRE 1`; лимит проверять по значению после `INCR`. Общий счётчик на все инстансы.
2. **`BotUpdateNotifier` → Redis pub/sub.** `Signal(botId)` → `PUBLISH bot-updates:{botId}`; ждущие
   на всех инстансах подписаны на канал и просыпаются. (Комментарий в коде уже предполагает этот путь.)
3. **`BotRegistryCache` → инвалидация через pub/sub.** Оставить локальный кэш для скорости, но при
   `Set()` публиковать инвалидацию `bot-registry-invalidate:{botId}`; подписчики на всех инстансах
   перечитывают бота из БД. Либо просто читать из БД/Redis без локального кэша (Bots — единственный
   писатель, нагрузка на чтение невелика).
4. **`BotPollingGuard` → распределённый лок.** `SET pollguard:{botId} {instanceId} NX EX <ttl>` для
   входа; `Exit` — `DEL` (с проверкой владельца). Гарантирует один активный поток на бота глобально.
5. **`BotsCleanupService`** — по желанию под распределённым локом (не критично, DELETE идемпотентен).
6. Отзыв сессий — по общему плану.

> Объём работы здесь больше, чем в остальных сервисах: затрагивает 4 сервиса и их потребителей.
> Порядок приоритета: `BotUpdateNotifier` и `BotPollingGuard` (иначе доставка апдейтов ломается) →
> `BotRateLimiter` (безопасность лимитов) → `BotRegistryCache` (консистентность данных).

## Критерии проверки

- `dotnet build Backend/BarkFluff.Bots/BarkFluff.Bots.csproj`.
- Тесты `BarkFluff.Bots.Tests` зелёные; добавить тесты на Redis-версии rate-limiter/guard.
- Ручная логика: 2 инстанса; бот делает 30 req/s суммарно — 31-й отклоняется; апдейт, принятый на A,
  будит long-poll клиента на B; одновременный getUpdates с двух инстансов — второй получает «занято».
