# Web — надёжность сетевых операций

> Исследование для [[Клиенты/Web]]. Исходный код не изменён. Актуально на 2026-08-25.
> Связанные сервисы: [[Backend/Web]], [[Backend/Messages]], [[Backend/Files]], [[Backend/Identity]].

## Решение в одном абзаце

Нужен единый ограниченный по времени primitive для callback-style gRPC-Web, который задаёт
`deadline`, сохраняет возвращённый call handle и отменяет его через `cancel()`; поверх него —
только явно разрешённые retry-политики. Для XHR нужны wall-clock timeout, watchdog отсутствия
прогресса и доступная пользователю отмена. Черновик должен оставаться `dirty` до подтверждения
сервера и не очищаться при таймауте отправки. Автоматически повторять `SendMessage` и
завершившийся неизвестно чем upload сейчас нельзя: в протоколе нет ключа идемпотентности и API
проверки результата. До серверной доработки такой исход надо показывать как «не удалось
подтвердить отправку», сохранять черновик и сверять с realtime/историей.

## Что гарантируют используемые API

### gRPC-Web 1.5.0

- Callback-style unary stub возвращает `ClientUnaryCallImpl`; он делегирует `cancel()`
  внутреннему stream. Значит, результат `method(request, metadata, callback)` в
  `clients.js` можно и нужно сохранять для ручной отмены и `AbortSignal`.
  [Исходник `rpcCall` 1.5.0](https://github.com/grpc/grpc-web/blob/1.5.0/javascript/net/grpc/web/grpcwebclientbase.js),
  [исходник `ClientUnaryCallImpl.cancel()` 1.5.0](https://github.com/grpc/grpc-web/blob/1.5.0/javascript/net/grpc/web/clientunarycallimpl.js).
- В 1.5.0 intentional `cancel()` вызывает abort, но подавляет transport error callback. Поэтому
  Promise-wrapper должен сам сначала перейти в rejected/cancelled state, затем вызвать
  `call.cancel()`; ожидать, что callback завершит Promise, нельзя.
  [Реализация cancel 1.5.0](https://github.com/grpc/grpc-web/blob/1.5.0/javascript/net/grpc/web/grpcwebclientreadablestream.js#L351-L358),
  [подавление intentional abort](https://github.com/grpc/grpc-web/blob/1.5.0/javascript/net/grpc/web/grpcwebclientreadablestream.js#L239-L264).
- Версия 1.5.0 принимает в metadata абсолютный Unix timestamp в миллисекундах под ключом
  `deadline`; runtime превращает его в `grpc-timeout` и ставит собственный XHR timeout
  примерно в 110% от остатка, минимум 1 секунду. Уже просроченное значение не включает timeout,
  поэтому deadline надо вычислять непосредственно перед каждой попыткой, а не переиспользовать
  metadata предыдущей.
  [README 1.5.0 — Setting Deadline](https://github.com/grpc/grpc-web/blob/1.5.0/README.md#setting-deadline),
  [реализация deadline 1.5.0](https://github.com/grpc/grpc-web/blob/1.5.0/javascript/net/grpc/web/grpcwebclientbase.js#L326-L343).
- У gRPC по умолчанию deadline отсутствует, поэтому вызов действительно может ждать
  неограниченно долго. Deadline следует выбирать по операции и проверять нагрузочными
  измерениями. Истечение даёт `DEADLINE_EXCEEDED`.
  [gRPC Deadlines](https://grpc.io/docs/guides/deadlines/).
- Отмена сообщает серверу, что клиент больше не ждёт результат, но обработчик сервера сам
  обязан прекратить работу и распространить cancellation дальше; отмена не является откатом
  уже выполненной мутации.
  [gRPC Cancellation](https://grpc.io/docs/guides/cancellation/).

### Retry и неизвестный исход

- gRPC рекомендует выбирать пригодные для повтора операции, ограничивать число попыток и
  применять exponential backoff с jitter; без retry policy библиотека не может безопасно
  повторять большинство RPC.
  [gRPC Retry](https://grpc.io/docs/guides/retry/).
- `UNAVAILABLE` обычно временный и допускает backoff, но официальный справочник отдельно
  предупреждает, что non-idempotent операцию повторять безопасно не всегда.
  `DEADLINE_EXCEEDED` для мутации возможен даже после её успешного выполнения.
  [gRPC Status Codes](https://grpc.io/docs/guides/status-codes/).

Следствие: код статуса сам по себе не делает retry безопасным. Сначала метод должен быть
read-only, идемпотентным или иметь серверный idempotency key.

### XMLHttpRequest

- Ненулевой `XMLHttpRequest.timeout` измеряется в миллисекундах от начала fetch и завершает
  незаконченный запрос событием `timeout`. Это общий wall-clock лимит, а не таймаут отсутствия
  прогресса, поэтому для больших файлов нужен отдельный progress-watchdog.
  [XMLHttpRequest Standard — timeout](https://xhr.spec.whatwg.org/#the-timeout-attribute).
- `xhr.abort()` отменяет сетевую активность и запускает error steps с событием `abort`.
  [XMLHttpRequest Standard — abort](https://xhr.spec.whatwg.org/#the-abort()-method).

### Состояние сети и локальная очередь

- `navigator.onLine` и события `online`/`offline` — только подсказка: стандарт прямо называет
  атрибут ненадёжным, потому что наличие локальной сети не означает доступность Интернета.
  Поэтому `online` может ускорить flush, но не должен быть условием retry или доказательством
  доступности выбранной BarkFluff-ноды.
  [HTML Standard — NavigatorOnLine](https://html.spec.whatwg.org/multipage/system-state.html#navigator.online).
- `localStorage.setItem()` может завершиться `QuotaExceededError`, а стандарт не задаёт
  блокировку между вкладками. Текущего localStorage достаточно для небольшого текстового
  черновика при обработке ошибок записи, но не для бинарного outbox.
  [HTML Standard — Web Storage](https://html.spec.whatwg.org/multipage/webstorage.html#the-storage-interface).
- IndexedDB умеет хранить `File` и `Blob`; транзакция с durability hint `strict` просит браузер
  подтвердить commit после записи в постоянное хранилище, хотя это именно hint и он дороже по
  времени/энергии. Запись также может упасть по quota.
  [IndexedDB — values](https://w3c.github.io/IndexedDB/#value-construct),
  [durability](https://w3c.github.io/IndexedDB/#durability),
  [QuotaExceededError](https://w3c.github.io/IndexedDB/#exceptions).

## Что происходит в BarkFluff сейчас

### gRPC

- `clients.js:49-81`: `refreshToken()` не задаёт deadline. Один зависший refresh навсегда
  оставляет `refreshPromise` pending и блокирует все ожидающие его запросы.
- `clients.js:100-137`: `authCall()` не принимает policy/`AbortSignal`, не добавляет deadline и
  игнорирует возвращаемый unary call handle. Единственный retry — refresh после кода 16.
- Через этот wrapper проходят 69 методов `api.js`, среди них и чтения, и non-idempotent
  мутации. Одна глобальная retry-политика для них неверна.
- На локальном стенде без TLS браузер держит 10–11 server-streaming подписок на
  origin ноды, но HTTP/1.1 даёт только 6 одновременных соединений. Unary RPC поэтому могут
  стоять в браузерной очереди ещё до ответа сервера. Deadline сделает симптом конечным,
  но корневое условие устраняется запуском через nginx/TLS с HTTP/2 либо сокращением/
  агрегацией подписок. Подробности текущей топологии зафиксированы в [[Клиенты/Web]].
- `auth.js`, `register.js`, `fast-auth.js`, `nodepicker.js` и `node.js` вызывают собственные
  callback clients мимо `BF.clients.authCall`; исправление только messenger-wrapper не покроет
  логин, регистрацию и выбор ноды.

### Upload

- `files.js:124-158`: XHR не имеет `timeout`/`timeout` handler; `abort` handler есть, но сам XHR
  не возвращается и нигде не регистрируется, поэтому UI не способен отменить загрузку.
- `main.js:1151-1165` загружает вложения последовательно. Отменить текущий XHR или оставшиеся
  элементы цепочки нельзя.
- `GetUploadUrl` создаёт новый slot с новым `fileId` и TTL 2 часа
  (`GetUploadUrlCommandHandler.cs:42-66`). Повторный POST на уже заполненный `fileId` возвращает
  400 `FileAlreadyUploadedException` (`FilesController.cs:60-71`,
  `UploadFileCommandHandler.cs:101-114`). Если ответ первого POST потерялся, клиент не может
  отличить «успешно загрузилось» от настоящей ошибки. Автоматический upload retry пока небезопасен.

### Черновик и отправка

- `drafts.js:24-34` сразу пишет текст/reply/generation в localStorage — это хорошая основа.
- Но `drafts.js:51-55` перед RPC сохраняет `dirty = false`. При бесконечно pending RPC catch не
  выполнится; `pagehide` запишет ложное clean-состояние, а следующий `load()` может заменить
  локальный текст старой серверной версией. Состояние `syncing` нельзя персистить как `clean`.
- `drafts.js:17` использует `online` как дополнительный flush-trigger. Его следует сохранить,
  но retry также должен запускаться таймером/backoff и при foreground/resync.
- `main.js:1065-1094`: обычный send держит кнопку disabled до ответа. После введения deadline
  текст и generation надо оставить, кнопку разблокировать, а исход отметить как неизвестный.
- `main.js:1213-1219` очищает composer до upload, а изменённая в attach-modal подпись не
  сохраняется как новый draft. При зависании/ошибке она может остаться только в памяти.
- `main.js:1136-1144` хранит pending attachment send только в `Map`. Перезагрузка теряет
  `File`-объекты и возможность продолжить операцию. Это уже полноценный бинарный outbox и выходит
  за минимальную правку текстового черновика.
- В `SendMessageRequest` нет client operation/idempotency ID
  (`Shared/BarkFluff.Proto/messages_api.proto:228-240`), а handler создаёт новую запись сообщения.
  Поэтому timeout/отмена не дают права автоматически вызывать `SendMessage` ещё раз.

## Рекомендуемая граница реализации

### Этап 1 — клиентская надёжность, без изменения контрактов

1. Добавить низкоуровневый `unaryCall(method, request, metadata, policy)`:
   - перед каждой попыткой создавать новую metadata и ставить
     `deadline = min(Date.now() + attemptTimeoutMs, overallDeadline)`;
   - сохранить возвращённый call handle;
   - связать `policy.signal` с собственным reject, после него вызвать `call.cancel()`;
   - единоразово settle Promise, удалять listener/timer при любом исходе;
   - возвращать нормализованную ошибку `{kind, code, retryable, outcomeUnknown}`.
2. Оставить `authCall()` ответственным только за токен и один refresh по коду 16, а timeout/retry
   делегировать `unaryCall`. Сам refresh тоже обязан иметь deadline и освобождать
   `refreshPromise` после ошибки.
3. Политику retry задавать явно в `api.js` по семантике метода; default — deadline есть, retry нет.
   Прямые клиенты страниц входа/регистрации/выбора ноды перевести на тот же primitive.
4. `uploadFile(..., {signal})`: задать XHR wall timeout, добавить `timeout` handler, сбрасывать
   watchdog на каждом upload progress, но отключать stall-watchdog после передачи 100% байтов —
   дальше сервер может долго обрабатывать видео без upload progress. Хранить активный XHR и
   предоставить cancel. Причина `user_cancel` должна отличаться от `stalled`/`wall_timeout`.
5. В `drafts.js` держать `dirty = true` до server ACK; текущую generation помечать как in-flight
   только в памяти и разрешать максимум один in-flight sync на чат. Timeout оставляет entry dirty
   и планирует следующий sync. Server ACK может очистить только ту generation, которую отправлял.
6. Перед стартом обычного send или attachment flow синхронно сохранить точные
   `{text/caption, replyToMessageId, generation}`. Очищать draft только после подтверждённого
   `SendMessageResponse` для той же generation. При таймауте — сохранить, разблокировать UI и
   выполнить quiet resync; без совпадения показать «Статус отправки неизвестен», без auto-retry.
7. `online` использовать только как немедленный дополнительный trigger; основа восстановления —
   bounded backoff и реальный успешный RPC.

Добавленный metadata header `grpc-timeout` должен проходить CORS preflight. В текущем
`Backend/BarkFluff.Web/Program.cs:81-85` уже настроен `AllowAnyHeader()`, поэтому отдельная правка
CORS для BarkFluff-шлюза не нужна.

### Этап 2 — серверная опора для безопасного retry

- Добавить в send-контракт `client_operation_id` (UUID одного логического сообщения), уникальность
  `(sender_id, client_operation_id)` и возврат ранее созданного сообщения при повторе. Только после
  этого `SendMessage` можно автоматически повторять после timeout/`UNAVAILABLE`.
- Сделать POST upload по зарезервированному `fileId` идемпотентным либо добавить status endpoint:
  повтор после потерянного ответа должен вернуть итоговый `fileId`, а не неразличимый 400.
- Передавать `ServerCallContext.CancellationToken`/`HttpContext.RequestAborted` в MediatR и далее в
  БД, Files и очередь. Иначе клиентский timeout освобождает UI, но сервер продолжает работу.

Полный durable outbox вложений (хранение `File` в IndexedDB, восстановление после reload,
quota/privacy/cleanup UI) лучше делать отдельной задачей после этапа 2. В текущую задачу достаточно
сохранить текст/подпись/reply и дать отменить живой upload.

## Стартовая матрица timeout/retry

Значения ниже — безопасная начальная политика; после выпуска их надо скорректировать по p95/p99 и
размеру файла. `Попытки` включают первую.

| Операция | Deadline / watchdog | Попытки | Автоматически повторять |
|---|---:|---:|---|
| Unary read (`Get/List/Search/Check`) | 12 с на попытку, общий бюджет 35 с | 3 | Коды 2/4/14 и transport error; exponential backoff с full jitter 250 мс → 2 с |
| Refresh token | 10 с | 2 | Только transport/14; код 16 и явный invalid-refresh не повторять, существующее правило очистки токена сохранить |
| Draft upsert/delete | 8 с | 3 | 2/4/14; после исчерпания entry остаётся dirty, фоновый backoff 2 → 30 с |
| Явно идемпотентный setter/ack | 10 с | 2 | 2/4/14, только после ручной классификации метода |
| `SendMessage`, create/edit/delete и неизвестная мутация | 15 с | 1 | Нет; resync + `outcomeUnknown`, draft не очищать |
| `GetUploadUrl` | 10 с | 1 | Нет автоматически: timeout мог создать orphan slot |
| XHR upload | 60 с без progress до передачи всех байтов; 30 мин wall | 1 | Нет до идемпотентного upload/status API; доступна ручная отмена |

Не повторять глобально коды 1, 3, 5–13, 15–16. Исключения должны быть осознанными: код 16 —
существующий единичный refresh; код 8 — только с явным server retry hint; код 10 — повтор всей
read-modify-write операции, а не отдельного RPC. Один общий лимит попыток должен включать и
повтор после refresh, чтобы auth-retry не умножался на transport-retry.

## Проверка

### Unit-тесты в текущем стиле `scripts/test-*.js`

- Fake unary method возвращает `{cancel()}` и управляемый callback: проверить deadline metadata,
  timeout, `AbortSignal`, вызов `cancel()` ровно один раз, игнорирование позднего callback и cleanup.
- Fake timers: таблица кодов/идемпотентности, лимит попыток, jitter в допустимом диапазоне, общий
  бюджет; отдельно — 16 → один refresh → одна повторная попытка.
- Зависший refresh: все ожидающие запросы завершаются за deadline, `refreshPromise` очищается,
  локальная сессия не удаляется при transport error.
- Fake XHR: `timeout` установлен; `timeout`, `error`, `abort`, user cancel и stall имеют разные
  ошибки; progress перезапускает watchdog; после 100% upload долгое серверное ожидание не даёт
  ложный stall; гонка `abort`/`load` не settle Promise дважды.
- Draft: во время зависшего upsert персистится `dirty:true`; reload возвращает локальный текст;
  timeout планирует retry; ACK старой generation не стирает новую; `clearSent` удаляет только
  точный snapshot.
- Main flow: timeout send разблокирует UI и сохраняет text/caption/reply; success очищает ровно
  отправленную generation; timeout attachment send сохраняет caption и не запускает второй send.

### Интеграционные сценарии

- Шлюз принимает gRPC-Web запрос и никогда не отвечает: UI выходит из loading в заданный срок,
  call отменён, draft восстановим после reload.
- Сервер применяет `SendMessage`, но ответ задерживается за deadline: до этапа 2 нет auto-retry,
  после idempotency key повтор возвращает то же message ID.
- XHR перестаёт отдавать progress, затем полностью offline: сначала срабатывает stall watchdog;
  `online` лишь ускоряет следующую проверку, но успех определяется запросом к ноде.
- Потерян ответ успешного upload: текущий API воспроизводит неразличимый 400; будущий
  idempotent/status API возвращает исходный `fileId`.
- Переключение чата, logout и page navigation отменяют только принадлежащие им операции; поздние
  callback не меняют новый экран.

## Критерии готовности

- Ни один unary RPC или XHR upload не остаётся pending без конечного срока.
- Пользователь может отменить загрузку; после отмены нет фонового send и утечки ObjectURL/XHR.
- Transport timeout никогда автоматически не дублирует non-idempotent мутацию.
- Текст, подпись и reply переживают timeout и reload; более новый draft не удаляется ACK старой
  операции.
- Retry ограничен по попыткам и общему времени, имеет jitter и проверен детерминированными тестами.
