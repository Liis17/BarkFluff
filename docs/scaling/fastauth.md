# Масштабирование: BarkFluff.FastAuth

**Вердикт: НЕ МОЖЕТ.** QR-авторизация держит сессии в памяти процесса; вход завершается на том же
инстансе, что создал QR, — а балансировщик этого не гарантирует.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| Singleton `FastAuthSessionsManager` (in-memory `ConcurrentDictionary` сессий) | `Backend/BarkFluff.FastAuth/Infrastructure/FastAuthSessionsManager.cs:14` | QR-сессия создана на инстансе A; подтверждение прилетает на B → `TryGet` вернёт `null`, авторизация не завершится |
| `FastAuthExpirationService` (дублируемый `BackgroundService`, тик 30 сек) | `Backend/BarkFluff.FastAuth/Infrastructure/FastAuthExpirationService.cs:16-34` | Каждый инстанс истекает только свои локальные сессии; чужие «висят» |

## План реализации

1. **Сессии → Redis.** Хранить `FastAuthSession` в Redis по ключу `fastauth:{sessionId}` с TTL,
   равным времени жизни QR. `Create/TryGet/Update` — операции над Redis. Тогда любой инстанс видит и
   продвигает любую сессию. Образец `IConnectionMultiplexer` —
   `Backend/BarkFluff.Messages/Infrastructure/SecretMessageBuffer.cs`.
2. **Истечение → TTL Redis.** При хранении в Redis отдельный sweeper не нужен — TTL сам удаляет
   просроченные сессии. `FastAuthExpirationService` можно удалить. Если требуется отправить клиенту
   явное событие «QR истёк» по gRPC-стриму — оставить лёгкий single-runner под распределённым локом
   либо доставлять статус реактивно при следующем запросе.
3. Если у сервиса есть gRPC-стрим ожидания статуса QR (клиент ждёт подтверждения) — применить fan-out
   для события подтверждения, как в [updates.md](updates.md), чтобы разбудить стрим на нужном инстансе.

## Критерии проверки

- `dotnet build Backend/BarkFluff.FastAuth/BarkFluff.FastAuth.csproj`.
- Тесты FastAuth зелёные.
- Ручная логика: QR сгенерирован на A; подтверждение с телефона приходит на B; клиент, ждущий на A,
  успешно авторизуется; просроченный QR исчезает по TTL без участия sweeper'а.
