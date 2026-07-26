# Аудит: BarkFluff.Updates
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

BarkFluff.Updates — сервис real-time доставки событий через gRPC server streaming: 15 streaming-методов подписки, 16 RabbitMQ-консьюмеров, рассылка через MediatR-обработчики и in-memory менеджеры подписок. Авторизация выстроена корректно: все методы закрыты `[Authorize(Policy = User)]` на уровне класса, userId берётся только из claims, маршрутизация событий идёт по серверным спискам участников из RabbitMQ-событий, клиентские поля запросов не используются (все Subscribe*-request пустые), содержимое сообщений в логи не пишется. Главные проблемы — в жизненном цикле стримов: отозванная/просроченная сессия продолжает получать события до разрыва соединения; медленный клиент способен навсегда заблокировать RabbitMQ-консьюмер; конкурентные записи в один `IServerStreamWriter` не сериализованы и приводят к потере событий. Дополнительно: нет лимита подписок на пользователя, гонка в менеджерах подписок, nginx рвёт «тихие» стримы каждые 300 секунд, а master-compose публикует plaintext-порт сервиса наружу.

| Критичность | Количество |
|---|---|
| Critical | 0 |
| High | 3 |
| Medium | 7 |
| Low | 6 |
| **Итого** | **16** |

Проверено и проблем не найдено: `[Authorize]` присутствует на каждом из 15 gRPC-методов (атрибут на классе `UpdatesApiService`, методов вне класса нет); подписка на чужие обновления невозможна (userId/deviceId только из claims, события маршрутизируются по `ChatMembers`/`RecipientUserId` из серверных событий); хардкод секретов отсутствует (конфигурация загружается из Configuration-сервиса, в `appsettings.json` только порт); БД у сервиса нет (N+1/AsNoTracking неприменимы); `GrpcChannel` на запрос не создаётся; Dockerfile корректен (chiseled-образ, non-root `USER $APP_UID`). Замечание вне области (аудит XAuth делает другой агент): политика `User` принимает и `Service`-токены (`Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:79-80`), при отсутствии claim UserId такой токен подпишется как userId=0.

## Безопасность

### S1. Отзыв сессии и истечение токена не действуют на уже открытые стримы — High
**Файл:** `Backend/BarkFluff.Updates/Host/UpdatesApiService.cs:120` (и аналогично во всех 15 методах), `Backend/BarkFluff.Updates/Consumers/SessionRevokedConsumer.cs:25`
**Проблема:** Стрим держится бесконечно: `await Task.Delay(Timeout.Infinite, context.CancellationToken)` — завершение только по разрыву соединения клиентом/прокси. JWT проверяется один раз при установке стрима (`OnTokenValidated` в XAuth). `SessionRevokedConsumer` лишь кладёт запись в `TokenRevocationCache` (`cache.Revoke(...)`, строка 25), что блокирует только **новые** подключения; уже открытые стримы отозванного устройства не разрываются и продолжают получать все новые сообщения, правки, инвайты и секретные конверты. То же с истечением срока жизни access-токена: стрим переживает его без повторной валидации.
**Почему это проблема:** Отзыв сессии — основной механизм реакции на компрометацию устройства в мессенджере. Сейчас украденная сессия после отзыва продолжает в реальном времени получать весь трафик пользователя (включая E2E-конверты секретных чатов, маршрутизируемые на устройство) неограниченно долго, пока соединение физически не порвётся.
**Рекомендация:** При обработке `SessionRevokedEvent` принудительно завершать стримы пары (userId, deviceId): хранить в менеджерах подписок `CancellationTokenSource` на подписку и отменять его (методы уже знают deviceId из claims — для user-scope менеджеров нужно начать сохранять deviceId). Дополнительно — периодическая (например, раз в 1–5 минут) проверка живости токена/ревокации внутри цикла ожидания вместо одного бесконечного `Task.Delay`.

### S2. Нет лимита подписок на пользователя — DoS по памяти и стримам — Medium
**Файл:** `Backend/BarkFluff.Updates/Features/Shared/UserStreamSubscriptionsBase.cs:21-29`, `Backend/BarkFluff.Updates/Features/Shared/DeviceStreamSubscriptionsBase.cs:22-31`, `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/StreamSubscriptionsManager.cs:18-25` (и 6 копий в других фичах), `Backend/BarkFluff.Updates/Host/UpdatesApiService.cs:112`
**Проблема:** `RegisterSubscription` без ограничений добавляет записи во вложенный `ConcurrentDictionary`. Один аутентифицированный пользователь может открыть произвольное число стримов по каждому из 15 методов (каждый — висящий HTTP/2-стрим + таймер `Task.Delay(Timeout.Infinite)` + записи в словарях). Лимиты Kestrel не настраиваются (`SetRunningAddress` задаёт только порт/протокол), на уровне nginx ограничений соединений тоже нет.
**Почему это проблема:** Злоумышленник с одним валидным токеном исчерпывает память/дескрипторы сервиса, при этом каждый его стрим дополнительно увеличивает фан-аут рассылки (на каждый стрим создаётся `Task.Run` при каждом событии — усиливает P1/P3).
**Рекомендация:** Ввести лимит активных подписок на (userId) и на (userId, deviceId) для каждого типа стрима (например, 3–5); при превышении закрывать самую старую подписку или отклонять новую с `ResourceExhausted`.

### S3. gRPC reflection включён безусловно и без аутентификации — Low
**Файл:** `Backend/BarkFluff.Updates/Program.cs:25` и `Backend/BarkFluff.Updates/Program.cs:142`
**Проблема:** `AddGrpcReflection()`/`MapGrpcReflectionService()` регистрируются без условия на окружение. На reflection-эндпоинте нет `[Authorize]`, fallback-политика не настроена — полное описание API доступно анонимно.
**Почему это проблема:** В проде раскрывается вся поверхность API (имена методов, структура событий, включая секретные чаты), что упрощает разведку. В сочетании с D2 (порт опубликован на хост в master-compose) reflection доступен напрямую извне.
**Рекомендация:** Включать reflection только в Development (`if (app.Environment.IsDevelopment())`) либо требовать Service-токен.

### S4. Отзыв сессии теряется после перезапуска сервиса — Medium
**Файл:** `Backend/BarkFluff.Updates/Program.cs:30`, `Backend/BarkFluff.Updates/Consumers/SessionRevokedConsumer.cs:15-26`, `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs:7-19`, `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:46-70`
**Проблема:** Единственный источник отзыва — singleton `TokenRevocationCache`: consumer записывает в него пару `(UserId, DeviceId)`, а `OnTokenValidated` сверяет только этот локальный словарь. Кэш не персистится и при старте не восстанавливается из Identity или отдельного хранилища; `Program.cs` лишь подключает `AddXAuth`, после рестарта словарь пуст. Уже обработанное RabbitMQ-событие не будет повторно доставлено, поэтому ранее отозванный, но ещё не истёкший access-token снова проходит аутентификацию и открывает новый стрим.
**Почему это проблема:** После штатного деплоя, рестарта или OOM-перезапуска (S2/P5/D3) скомпрометированное устройство может вновь подписаться на обычные и device-scoped события до конца TTL access-token (дефолт `JwtSettings:ExpiryMinutes` — 60 минут), обходя совершённый пользователем logout/отзыв сессии.
**Рекомендация:** Хранить revoked-сессии с TTL в общем durable-хранилище (например, Redis) либо проверять версию/состояние сессии в Identity; при старте восстановить записи до истечения токенов. Локальный кэш можно оставить только как ускоряющий слой.

## Производительность

### P1. Медленный клиент блокирует RabbitMQ-консьюмер: `WriteAsync` без таймаута внутри `Task.WhenAll` — High
**Файл:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs:56,77` (тот же паттерн во всех 15 обработчиках: `.../SubscribeMessagesRead/Handlers/ReadByNotificationHandler.cs:61,83`, `.../SubscribeSecretMessages/Handlers/NewSecretMessageNotificationHandler.cs:64,76` и др.)
**Проблема:** Рассылка выполняется внутри `Handle`, который вызывается синхронно из MassTransit-консьюмера (`_mediator.Publish` в `Consumers/NewMessageConsumer.cs:51` ожидается). `stream.WriteAsync(evt, cancellationToken)` подчиняется HTTP/2 flow control: если клиент перестал вычитывать стрим (медленная сеть, зависшее приложение, злоумышленник), `WriteAsync` после заполнения буферов ждёт неограниченно. `cancellationToken` здесь — токен консьюмера, который в норме не отменяется, таймаута нет. `await Task.WhenAll(sendTasks)` ждёт всех — один зависший подписчик навсегда блокирует обработку сообщения, занимает слот конкурентности эндпоинта, и при исчерпании prefetch вся очередь (`new-messages-updates-handler` и т.д.) останавливается — события перестают доставляться **всем** пользователям.
**Почему это проблема:** Это одновременно деградация всей real-time доставки и простой вектор DoS: достаточно открыть подписку и не читать из сокета.
**Рекомендация:** Развязать consume и доставку: на каждую подписку — bounded `Channel<TEvent>` (например, 64–256 элементов, `BoundedChannelFullMode.DropOldest` или разрыв стрима при переполнении) и один writer-цикл, который единственный пишет в `IServerStreamWriter`. Консьюмер только кладёт событие в каналы и сразу подтверждает сообщение. Как минимум — `WriteAsync` с таймаутом (`CancellationTokenSource.CancelAfter`) и принудительное закрытие стрима при просрочке.

### P2. Конкурентные `WriteAsync` в один стрим не сериализованы — потеря событий — High
**Файл:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs:56` (и все остальные обработчики, пишущие в стримы тех же менеджеров)
**Проблема:** `IServerStreamWriter<T>` в grpc-dotnet допускает только одну незавершённую запись («Only one write can be in flight at a time»). MassTransit по умолчанию обрабатывает сообщения конкурентно (prefetch/concurrency > 1), плюс 16 независимых эндпоинтов: два события для одного пользователя (например, два новых сообщения в активном чате или сообщение + read-receipt в разные менеджеры одного типа) приводят к параллельным `WriteAsync` в один и тот же стрим. Возникает `InvalidOperationException`, которая ловится `catch` (строки 64–72) и лишь логируется — событие для подписчика **молча теряется**, клиент о нём не узнаёт до полного re-sync.
**Почему это проблема:** В нагруженных чатах это систематическая, а не теоретическая потеря real-time событий; метрики `*_broadcast_errors` будут расти, но пользователи просто не получат сообщения.
**Рекомендация:** Сериализовать записи per-stream: тот же per-subscription `Channel` + единственный writer (решает заодно P1) либо `SemaphoreSlim(1,1)`, хранимый рядом со стримом в менеджере подписок.

### P3. `Task.Run` на каждый (участник × стрим) — неограниченный параллелизм на горячем пути — Medium
**Файл:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs:47` (тот же паттерн во всех обработчиках, напр. `ReadByNotificationHandler.cs:51`, `NewSecretMessageNotificationHandler.cs:60`)
**Проблема:** Для каждого стрима каждого участника чата создаётся `Task.Run(...)`. Для чата с тысячами участников каждое событие порождает тысячи заданий в thread pool без какого-либо ограничения степени параллелизма. `Task.Run` для чисто асинхронного I/O избыточен — он лишь добавляет переключение на пул.
**Почему это проблема:** Пики событий вызывают взрывной рост очереди thread pool, рост латентности всех остальных операций сервиса и GC-давление; «параллелизм» не ограничен ни по событию, ни глобально.
**Рекомендация:** Убрать `Task.Run` (просто собирать задачи `WriteAsync`) и ограничить фан-аут, например `Parallel.ForEachAsync(streams, new ParallelOptions { MaxDegreeOfParallelism = 16–64 }, ...)`. При переходе на per-subscription каналы (P1/P2) проблема исчезает сама.

### P4. Гонка Register/Remove: свежая подписка может быть выброшена из менеджера — Medium
**Файл:** `Backend/BarkFluff.Updates/Features/Shared/UserStreamSubscriptionsBase.cs:43-46`, `Backend/BarkFluff.Updates/Features/Shared/DeviceStreamSubscriptionsBase.cs:46-49`, `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/StreamSubscriptionsManager.cs:37-40` (и 6 копий: SubscribeMessagesRead:36-39, SubscribeMessagesEdited, SubscribeMessagesDeleted, SubscribeMessagesPinned, SubscribeMessagesUnpinned, SubscribeAllMessagesUnpinned)
**Проблема:** Классический check-then-act над `ConcurrentDictionary`: поток A в `RemoveSubscription` удаляет последнюю подписку, видит `userStreams.IsEmpty` и делает `TryRemove(userId)`. Между этими действиями поток B в `RegisterSubscription` через `GetOrAdd` получает **тот же** внутренний словарь и добавляет туда новую подписку — после чего A удаляет словарь целиком. Подписка B зарегистрирована (стрим у клиента открыт, `ActiveCount` инкрементирован), но недостижима для `GetUserStreams` — пользователь не получает ни одного события до переподключения; счётчик `_activeSubscriptionsCount` навсегда дрейфует вверх (RemoveSubscription для потерянной подписки не найдёт словарь).
**Почему это проблема:** Тихая потеря доставки для переподключившегося клиента (типичный сценарий: разрыв и мгновенный reconnect — ровно гонка Remove старого и Register нового) плюс искажение gauge-метрик `*_subscriptions_active`.
**Рекомендация:** Не удалять пустые внутренние словари (память на пользователя мизерная) либо удалять через `ICollection<KeyValuePair<...>>.Remove(new KeyValuePair<>(userId, userStreams))` с повторной проверкой, либо защищать удаление per-user lock'ом. И устранить 7 копий кода в пользу `UserStreamSubscriptionsBase` (см. P7), чтобы чинить в одном месте.

### P5. Push-планировщик: fire-and-forget `Task.Run` + 5-секундный таймер на каждого получателя — Medium
**Файл:** `Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs:53-130` (Task.Run — строка 58, Task.Delay — 63)
**Проблема:** Для каждого получателя каждого сообщения создаётся отдельный fire-and-forget таск с `Task.Delay(5s)`. Для группового чата на N участников каждое сообщение порождает N тасков и N CTS, удерживающих замыкание с полным `notification.Message` (включая вложения) минимум 5 секунд. Объём незавершённых тасков ничем не ограничен — при потоке сообщений из RabbitMQ память и пул растут пропорционально (сообщений/сек × участников × 5 сек). Push планируется даже получателям, у которых заведомо нет ни одного активного стрима/устройства.
**Почему это проблема:** Неограниченное фоновое потребление памяти/таймеров на горячем пути; при всплеске трафика сервис деградирует, хотя сами стримы не нагружены.
**Рекомендация:** Один отложенный таск на сообщение со списком получателей (отмена по-пользовательски — через множество «уже прочитавших»), либо отложенная публикация средствами MassTransit (scheduled/delayed messages), чтобы не держать состояние в памяти Updates.

### P6. Гонки в PendingPushTracker: dispose чужого CTS и неатомарная замена — Low
**Файл:** `Backend/BarkFluff.Updates/Features/PushNotifications/PendingPushTracker.cs:22-28`, `Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs:127`
**Проблема:** (1) В `RegisterPending` пара `TryRemove` + `_pendingPushes[key] = cts` неатомарна — параллельный `CancelPending` может попасть между ними. (2) `RemovePending` в `finally` таска удаляет запись по ключу, не сверяя экземпляр: если за время жизни таска для того же (messageId, userId) был зарегистрирован новый CTS (повторная доставка события из RabbitMQ), `finally` старого таска удалит и задиспозит **новый** CTS — его push станет неотменяемым по прочтению.
**Почему это проблема:** Редкие, но реальные сценарии redelivery приводят к «неубиваемым» push-уведомлениям и `ObjectDisposedException` при попытке отмены.
**Рекомендация:** Удалять по совпадению экземпляра: `TryRemove(KeyValuePair.Create(key, cts))`; в `RegisterPending` использовать `AddOrUpdate` с отменой вытесняемого значения.

### P7. Дублирование менеджеров подписок и создание события на каждого подписчика — Low
**Файл:** `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/StreamSubscriptionsManager.cs:10-52` (плюс 6 идентичных копий) против `Features/Shared/UserStreamSubscriptionsBase.cs`; `Backend/BarkFluff.Updates/Features/SubscribeNewMessages/Handlers/NewMessageNotificationHandler.cs:51-55` (аналогично в ReadBy/Edited/Deleted/Pinned/Unpinned/AllUnpinned)
**Проблема:** 7 классов-менеджеров — построчные копии друг друга, хотя в проекте уже есть `UserStreamSubscriptionsBase<TEvent>`, которым пользуются остальные 8 фич. Кроме того, в message-обработчиках protobuf-событие (`NewMessageEvent`, `MessageReadEvent` и т.д.) конструируется заново внутри каждого `Task.Run`, т.е. отдельный объект на каждого подписчика, тогда как invite/secret-обработчики корректно строят один `evt` на событие.
**Почему это проблема:** Любой фикс (лимит S2, гонка P4) нужно повторять в 9 местах — копии неизбежно разъедутся; лишние аллокации на каждом событии в больших чатах создают ненужное GC-давление.
**Рекомендация:** Перевести 7 дублей на `UserStreamSubscriptionsBase<TEvent>`; событие создавать один раз перед циклом рассылки (protobuf-сообщения безопасны для конкурентной сериализации при отсутствии мутаций).

### P8. ServerExceptionInterceptor не покрывает streaming-методы — мёртвый код в этом сервисе — Low
**Файл:** `Backend/BarkFluff.Updates/Program.cs:21`, `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs:24`
**Проблема:** Интерсептор переопределяет только `UnaryServerHandler`, а у Updates все 15 методов — server streaming. Метрики `grpc_requests_total/failed/errors` и единый контракт ошибок (`x-error-code` trailer) для этого сервиса не работают вовсе.
**Почему это проблема:** Слепое пятно в наблюдаемости (запросы сервиса не попадают в gRPC-метрики) и ложное ощущение, что ошибки стримов обёрнуты единообразно.
**Рекомендация:** Добавить в интерсептор override `ServerStreamingServerHandler` (в shared-библиотеке) либо убрать регистрацию в Updates как неработающую.

### P9. Information-логирование на каждое сообщение на горячем пути — Low
**Файл:** `Backend/BarkFluff.Updates/Consumers/NewMessageConsumer.cs:34-38,53-56`, `Backend/BarkFluff.Updates/Consumers/ReadByConsumer.cs:28-33,48-51`, `Backend/BarkFluff.Updates/Features/SubscribeMessagesRead/Handlers/ReadByNotificationHandler.cs:25-30,87-93`
**Проблема:** На каждое сообщение/прочтение пишется по 2–4 записи уровня `Information` (получено событие, опубликовано уведомление, итог рассылки). Для мессенджера это самый частый путь — объём логов в Seq растёт линейно с трафиком.
**Почему это проблема:** Стоимость хранения/индексации логов и накладные расходы Serilog на каждом событии; полезность таких записей на проде близка к нулю (есть метрики).
**Рекомендация:** Понизить до `Debug` (по образцу остальных обработчиков, где уже используется `LogDebug`).

## Docker / nginx

### D1. nginx обрывает «тихие» стримы каждые 300 секунд, heartbeat отсутствует — Medium
**Файл:** `Backend/nginx/updates.conf:22-23`
**Проблема:** `grpc_read_timeout 300s` / `grpc_send_timeout 300s` — таймаут между чтениями от upstream. Сервис не шлёт никаких keepalive/heartbeat-событий в стримы (стрим молчит, пока нет реальных событий), поэтому любой стрим без событий 5 минут (норма для большинства подписок: pinned/unpinned, инвайты, секретные чаты) принудительно разрывается nginx.
**Почему это проблема:** Постоянный reconnect-чурн всех клиентов каждые ≤5 минут (15 стримов на клиента!), а в окно между разрывом и переподпиской события теряются — Updates не имеет replay для обычных событий. Это также усиливает гонку P4 (каждый reconnect — параллельные Remove+Register).
**Рекомендация:** Поднять `grpc_read_timeout` для этого location (например, до часов) и/или добавить серверный heartbeat-event раз в 1–2 минуты в каждый стрим (заодно позволит обнаруживать мёртвые соединения со стороны сервиса и решает «вечные» стримы из S1).

### D2. master-compose публикует plaintext gRPC-порт Updates прямо на хост — Medium
**Файл:** `Backend/docker-compose-master.yml:109`
**Проблема:** `ports: ["${UPDATES_PORT}:${UPDATES_PORT}"]` выставляет порт 7015 на все интерфейсы хоста. Kestrel слушает h2c без TLS (TLS-терминация — задача nginx, `RunSettings:Tls` для Updates не задан), т.е. снаружи доступен нешифрованный gRPC в обход nginx: JWT в `x-auth-token` и содержимое сообщений ходят открытым текстом, плюс анонимный reflection (S3). В dev-compose (`Backend/docker-compose-dev.yml:105-114`) порт корректно не публикуется.
**Почему это проблема:** Перехват токенов/трафика в сети до хоста, обход настроенных на nginx таймаутов/ограничений; внутренние сервисы и так достучатся через `barkfluff-network` без публикации порта.
**Рекомендация:** Убрать `ports` у updates (nginx ходит на `updates:7015` по внутренней docker-сети) либо биндить на `127.0.0.1:`.

### D3. Нет healthcheck и лимитов ресурсов для контейнера updates — Low
**Файл:** `Backend/docker-compose-master.yml:106-115`, `Backend/docker-compose-dev.yml:105-114`
**Проблема:** У сервиса только `restart: always`: нет `healthcheck` (зависший процесс с живым портом не перезапустится), нет `mem_limit`/`cpus`.
**Почему это проблема:** В сочетании с S2/P5 (неограниченные подписки и фоновые таски) утечка памяти Updates заберёт ресурсы всего хоста, а зависание consume-конвейера (P1) останется незамеченным оркестратором.
**Рекомендация:** Добавить gRPC health probe (стандартный `grpc.health.v1.Health` + healthcheck в compose) и лимиты памяти/CPU.
