# Масштабирование: BarkFluff.Federation

**Вердикт: МОЖЕТ (реализовано).** Outbox забирается конкурентно без дублей (SKIP LOCKED), janitor —
single-runner, discovery-cooldown и инвалидация ключей — глобальные.

> Статус: план реализован. Смотри историю коммитов `Federation:` вокруг 2026-08-14.

## Как было (блокеры до реализации)

| Блокер | Файл | Почему ломался при N экземплярах |
|--------|------|-----------------------------------|
| `OutboxDispatcher` (дублируемый `BackgroundService`, тик 5 сек) | `BackgroundServices/OutboxDispatcher.cs` | N инстансов читают одни и те же `Pending`-записи и шлют их → дублирующая доставка, гонки по статусу |
| `OutboxJanitor` (дублируемый `BackgroundService`, тик 1 час) | `BackgroundServices/OutboxJanitor.cs` | Чистка идемпотентна, но выполняется N раз |
| `DiscoveryTriggerRateLimiter` (in-memory cooldown) | `Services/DiscoveryTriggerRateLimiter.cs` (заменён на `RedisDiscoveryTriggerRateLimiter`) | Cooldown per-instance: discovery одного сервера запустится с каждого инстанса |
| `ActiveSigningKeyCache` (singleton, refresh только локально) | `Services/ActiveSigningKeyCache.cs` | После ротации на одном инстансе остальные подписывают исходящие S2S-запросы старым ключом до рестарта |
| Отзыв сессий (shared) | эндпоинт `session-revoked-federation` был competing | Отзыв доходил до одного инстанса |

## Что сделано

1. **Outbox → claim-then-send (вариант A плана).** `OutboxDispatcher`: батч атомарно «застолблен»
   статусом `Processing` (новое значение enum, int-колонка — миграция не нужна) с lease 2 мин в
   `NextAttemptAt`; на PostgreSQL выборка идёт raw-SQL `SELECT … ORDER BY … LIMIT … FOR UPDATE
   SKIP LOCKED` в короткой транзакции (LINQ-расширения `ForUpdate()/SkipLocked()` в EFCore.PG 10
   отсутствуют; precedent — `Users/Persistence/Services/PrekeyStorage.cs`), отправка — вне
   блокировки строк. Reclaim в начале прохода возвращает застрявшие после крэша строки в Pending
   (дубли на приёме отсекает `ProcessedEvents` пира). Head-of-line-фильтр per-chat учитывает и
   `Processing`-головы — очередь чата не обходится, пока голова в полёте на другом инстансе.
2. **`OutboxJanitor` → single-runner.** Redis-лок `federation:lock:outbox-janitor`
   (`ISingleRunner`/`RedisSingleRunner` по образцу Onliner), TTL 2 ч с продлением лидером.
3. **`DiscoveryTriggerRateLimiter` → Redis.** `RedisDiscoveryTriggerRateLimiter`: ключ
   `fed:discovery:{server}` через `SET NX EX 300` — глобальный cooldown (как в плане).
4. **Ротация ключей → fan-out.** `RotateSigningKey` публикует `SigningKeyRotatedEvent`
   (`Shared.Queue.Federation`); пер-instance очередь `signing-key-rotated-federation-{InstanceId}`
   → каждый инстанс перезагружает `ActiveSigningKeyCache` **и** `WellKnownDocumentService`
   (у well-known та же проблема staleness). Подписи outbox-payload не затронуты — они кладутся
   в outbox свежим ключом в момент enqueue.
5. **Отзыв сессий** — пер-instance очередь `session-revoked-federation-{InstanceId}` (было сделано
   ранее в рамках общего плана).

## Критерии проверки

- `dotnet build Backend/BarkFluff.Federation/BarkFluff.Federation.csproj`.
- Тесты `BarkFluff.Federation.Tests` зелёные (кроме 2 известных падений `XFedIntegrationTests`
  про DI `FederationS2SApiHandler` — не связаны с масштабированием, падали до изменений).
- Ручная логика: 2 инстанса; исходящее событие уходит принимающему серверу ровно один раз
  (SKIP LOCKED); повторный discovery одного сервера в окне 5 мин не запускается со второго
  инстанса; после ротации оба инстанса подписывают новым ключом; крэш инстанса mid-flight —
  строка доедет после reclaim.
