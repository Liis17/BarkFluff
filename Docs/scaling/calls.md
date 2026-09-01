# Масштабирование: BarkFluff.Calls

**Вердикт: НЕ МОЖЕТ.** Сигнализация звонков через gRPC-стримы + in-memory планировщик таймаутов
звонка.

## Блокеры

| Блокер | Файл | Почему ломается при N экземплярах |
|--------|------|-----------------------------------|
| Singleton `CallEventSubscriptionsManager` (in-memory реестр стримов) | `Backend/BarkFluff.Calls/Services/CallEventSubscriptionsManager.cs:19`; используется в `Host/CallsApiService.cs:65` | Событие звонка не найдёт подписчиков, подключённых к другому инстансу |
| Singleton `CallTimeoutScheduler` (in-memory `CancellationTokenSource` + `Task.Delay`) | `Backend/BarkFluff.Calls/Services/CallTimeoutScheduler.cs:16` | Таймаут «дозвона» (45 сек) запланирован в памяти одного инстанса; другой инстанс не сможет его отменить/обработать; при рестарте — теряется |
| `SemaphoreSlim` per-subscription (in-process lock) | `CallEventSubscriptionsManager.cs:78` | Защищает запись в стрим только внутри процесса — при fan-out это ок (стрим локальный) |
| Named `ReceiveEndpoint` (competing) | `Backend/BarkFluff.Calls/Program.cs` | Событие уходит одному инстансу, стрим — на другом |
| Отзыв сессий (shared) | эндпоинт `session-revoked-calls` | См. [_shared-token-revocation.md](_shared-token-revocation.md) |

## План реализации

1. **Стрим-эндпоинты → fan-out** (уникальная очередь на инстанс + `AutoDelete`), как в
   [updates.md](updates.md). `CallEventSubscriptionsManager` и его `SemaphoreSlim` остаются
   локальными и корректны: стрим живёт на одном инстансе, fan-out-событие туда придёт.
2. **`CallTimeoutScheduler` → durable-планировщик.** Заменить in-memory `Task.Delay` на отложенное
   сообщение MassTransit (`ScheduleSend`/RabbitMQ delayed exchange): при инициации звонка планируется
   `CallRingTimeout` через 45 сек; сообщение обработает **любой** инстанс, который проверит по БД,
   остался ли звонок в состоянии «звонит», и завершит его. Отмена таймаута — по факту принятия/сброса
   звонка (проверка статуса в обработчике таймаута), либо `CancelScheduledSend`.
   Это снимает привязку таймаута к конкретному инстансу и переживает рестарты.
3. Отзыв сессий — по общему плану.

## Критерии проверки

- `dotnet build Backend/BarkFluff.Calls/BarkFluff.Calls.csproj`.
- Тесты `BarkFluff.Calls.Tests` зелёные.
- Ручная логика: звонок инициирован на A, вызываемый подписан на B — получает `CallEvent`; при
  отсутствии ответа звонок завершается по таймауту, даже если его обработал инстанс C.
