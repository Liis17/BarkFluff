# Масштабирование: BarkFluff.FastAuth

**Вердикт: МОЖЕТ (реализовано).** Сессии QR-авторизации хранятся в Redis, событие подтверждения
доставляется в стрим ожидающего клиента через Redis pub/sub — вход завершается на любом инстансе.

> Статус: план реализован. Смотров историю коммитов `fastauth:` / `settings:` вокруг
> 2026-08-14. Сводка решений: Redis pub/sub для wake-up (без RabbitMQ — в сервисе нет MassTransit),
> локальный дедлайн до `ExpiresAt` вместо sweeper'а, финальный результат (с токенами) хранится
> `FinalRetention=30 сек` для реконнекта.

## Как было (блокеры до реализации)

| Блокер | Файл | Почему ломался при N экземплярах |
|--------|------|-----------------------------------|
| Singleton `FastAuthSessionsManager` (in-memory `ConcurrentDictionary` сессий) | `Backend/BarkFluff.FastAuth/Infrastructure/FastAuthSessionsManager.cs` (удалён) | QR-сессия создана на инстансе A; подтверждение прилетает на B → `TryGet` вернёт `null`, авторизация не завершится |
| `FastAuthExpirationService` (дублируемый `BackgroundService`, тик 30 сек) | `Backend/BarkFluff.FastAuth/Infrastructure/FastAuthExpirationService.cs` (удалён) | Каждый инстанс истекает только свои локальные сессии; чужие «висят» |
| `Channel<FastAuthResult>` внутри `FastAuthSession` | `Domain/FastAuthSession.cs` (удалён) | Событие Accept на B не будит стрим на A |

## Что сделано

1. **Сессии → Redis.** `IFastAuthSessionStore` + `RedisFastAuthSessionStore`: ключ
   `fastauth:session:{id}`, TTL = 5 мин + 30 сек slack (после логического истечения значение ещё
   читаемо — Expired отличим от NotFound). Переходы `Scan/Accept/Reject/Expire` — Lua-скрипты:
   атомарная проверка статуса/confirmation_code/userId/срока одним шагом (замена in-process lock).
2. **Истечение → локальный дедлайн.** `SubscribeFastAuthResult` ждёт событие до `ExpiresAt` и сам
   закрывает стрим со статусом `EXPIRED`; sweeper удалён, данные чистит TTL Redis.
3. **Wake-up стрима → Redis pub/sub.** `FastAuthEventBus`: канал `fastauth:events` + локальный
   реестр ожидающих. Переход на инстансе B публикует событие → стрим на инстансе A просыпается.
   Гонка «переход до подписки» закрыта перечитыванием стора после Attach.
4. **Единственный подписчик — глобально.** `SETNX fastauth:subscriber:{id}` с токеном владельца:
   повторный `Subscribe` отклоняется на любом инстансе; после дисконнекта захват освобождается
   (реконнект в окне FinalRetention получает токены из стора).
5. **Конфиг.** Ключ `Redis` для ServiceId=7 в Settings (миграция
   `20260814100000_AddRedisConfigurationForFastAuth`), значение подставит каталог
   Settings (`redis:6379`).

## Критерии проверки

- `dotnet build Backend/BarkFluff.FastAuth/BarkFluff.FastAuth.csproj`.
- Тесты `BarkFluff.FastAuth.Tests` зелёные (fakes стора/шины зеркалят Lua-семантику).
- Ручная логика: QR сгенерирован на A; подтверждение с телефона приходит на B; клиент, ждущий на A,
  успешно авторизуется; просроченный QR закрывается EXPIRED по дедлайну без sweeper'а.

> Замечание: образец `IConnectionMultiplexer` — `Backend/BarkFluff.Messages/Persistence/Services/SecretMessageBuffer.cs`
> (путь в ранней версии этого плана был битым).
