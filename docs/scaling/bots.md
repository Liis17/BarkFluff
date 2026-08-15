# Масштабирование: BarkFluff.Bots

**Вердикт: МОЖЕТ (реализовано).** Все четыре in-memory состояния перенесены в Redis/RabbitMQ
fan-out; отзыв сессий — по общему fan-out-паттерну.

> Статус: план реализован. Смотри историю коммитов `Bots:` / `Federation:` вокруг 2026-08-14.

## Как было (блокеры до реализации)

| Блокер | Файл | Почему ломался при N экземплярах |
|--------|------|-----------------------------------|
| `BotRateLimiter` (in-memory счётчик 30 req/s) | `Services/BotRateLimiter.cs` (заменён на `RedisBotRateLimiter`) | Счётчик per-instance: при N инстансах бот получает 30×N req/s вместо 30 |
| `BotUpdateNotifier` (in-memory `TaskCompletionSource`) | `Services/BotUpdateNotifier.cs` | Сигнал «новый update» будит только ждущих на этом инстансе; long-poll/стрим на другом инстансе не проснётся |
| `BotRegistryCache` (in-memory реестр ботов) | `Services/BotRegistryCache.cs` | Обновление бота через один инстанс не видно другим до перезагрузки |
| `BotPollingGuard` (in-memory guard одного активного потока) | `Services/BotPollingGuard.cs` (заменён на `RedisBotPollingGuard`) | Два клиента на разных инстансах одновременно пройдут guard для одного бота |
| `BotsCleanupService` (дублируемый таймер) | `Services/BotsCleanupService.cs` | Каждый час на каждом инстансе; идемпотентный DELETE, не критично — оставлен как есть |
| Отзыв сессий (shared) | эндпоинт `session-revoked-bots` отсутствовал | `TokenRevocationCache` никогда не наполнялся |

## Что сделано

1. **`BotRateLimiter` → Redis.** `RedisBotRateLimiter`: скользящее окно `INCR botrate:{botId}:{unixSecond}`
   + `EXPIRE` — общий счётчик на все инстансы (пункт плана 1).
2. **`BotUpdateNotifier` → fan-out через RabbitMQ** (вместо Redis pub/sub из плана — эквивалентно):
   `Signal` публикует `BotUpdateSignalEvent`, пер-instance очередь `bot-update-signal-{InstanceId}`
   (AutoDelete) будит локальных waiter'ов на каждом инстансе (пункт 2).
3. **`BotRegistryCache` → инвалидация fan-out.** Локальный кэш остался; `Set/Remove` публикуют
   `BotRegistryChangedEvent`, очередь `bot-registry-changed-{InstanceId}` → каждый инстанс
   перечитывает бота из БД (пункт 3, вариант A плана).
4. **`BotPollingGuard` → распределённый лок.** `RedisBotPollingGuard`: `SET NX EX 90` с владельцем
   `InstanceId`, owner-checked `DEL` + `RenewAsync` для долгих стримов (пункт 4).
5. **Отзыв сессий.** `SessionRevokedConsumer` + пер-instance очередь
   `session-revoked-bots-{InstanceId}` — консистентно с остальными сервисами. Сегодня Bots
   обслуживает только Service/Bot-токены (user-путь отсутствует), так что это консистентность
   паттерна и future-proofing.
6. `BotsCleanupService` не тронут (план: опционально, DELETE идемпотентен).

## Критерии проверки

- `dotnet build Backend/BarkFluff.Bots/BarkFluff.Bots.csproj`.
- Тесты `BarkFluff.Bots.Tests` зелёные (включая `SessionRevokedConsumerTests`).
- Ручная логика: 2 инстанса; бот делает 30 req/s суммарно — 31-й отклоняется; апдейт, принятый на A,
  будит long-poll клиента на B; одновременный getUpdates с двух инстансов — второй получает «занято».
