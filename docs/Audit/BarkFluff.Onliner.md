# Аудит: BarkFluff.Onliner
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Сервис в целом спроектирован аккуратно: все 7 gRPC-методов закрыты политикой `TokenType.User` на уровне класса (`[AllowAnonymous]` нигде нет), `user_id` всегда берётся из claims (`UserContext`), а не из запроса; фильтры приватности и членства в чатах работают fail-closed. Хардкода секретов нет (в design-time factory — заглушка `your_password`, не реальный секрет), в логи попадают только числовые UserId/DeviceId, не контент. Основные проблемы — DoS-направление и горячий путь: ни одного лимита на размер списков подписки и число стримов на пользователя, N+1 последовательные gRPC-вызовы при проверке приватности, прямая запись в клиентские стримы без очередей (блокировка медленным подписчиком + несинхронизированный конкурентный `WriteAsync`), N+1 запросы к БД в персистенции и неограниченный рост in-memory storage.

| Критичность | Безопасность | Производительность | Docker/nginx | Всего |
| ----------- | ------------ | ------------------ | ------------ | ----- |
| Critical    | 0            | 0                  | 0            | 0     |
| High        | 1            | 3                  | 0            | 4     |
| Medium      | 3            | 5                  | 2            | 10    |
| Low         | 2            | 2                  | 1            | 5     |
| **Итого**   | **6**        | **10**             | **3**        | **19** |

## Безопасность

### S1. Отсутствие лимитов на подписки — DoS памятью и амплификация запросов — High
**Файл:** `Backend/BarkFluff.Onliner/Features/SubscribeToOnlineStatus/SubscribeToOnlineStatusQueryHandler.cs:53`, `Backend/BarkFluff.Onliner/Services/OnlineStatusSubscriptionsManager.cs:37-59`, `Backend/BarkFluff.Onliner/Host/OnlinerApiService.cs:66-84`
**Проблема:** Нигде не ограничено: (1) количество одновременных стримов `SubscribeToOnlineStatus`/`SubscribeToTyping` на одного пользователя, (2) размер списка `user_ids`/`chat_ids` в подписке (лимит — только дефолтные 4 МБ gRPC-сообщения, это сотни тысяч int64). Каждый tracked id создаёт запись в обратном индексе (`OnlineStatusSubscriptionsManager.AddToReverseIndex`, строки 151-164).
**Почему это проблема:** Один аутентифицированный клиент может открыть тысячи стримов с сотнями тысяч id в каждом: неограниченный рост `_subscriptions`/`_reverseIndex` (память), а в связке с P3 — каждый id в запросе порождает отдельный gRPC-вызов в Users (амплификация: 1 запрос → сотни тысяч исходящих вызовов). Memory-limit в compose не задан (см. D3), значит OOM затронет хост.
**Рекомендация:** Ввести лимиты: максимум стримов на пользователя (например, 5-10), максимум tracked id на подписку (например, 500-1000), отклонять превышение `ResourceExhausted`. Дополнительно — ограничить `MaxReceiveMessageSize` в `AddGrpc`.

### S2. Фильтры приватности/членства проверяются только в момент подписки — Medium
**Файл:** `Backend/BarkFluff.Onliner/Features/SubscribeToOnlineStatus/SubscribeToOnlineStatusQueryHandler.cs:38-41`, `Backend/BarkFluff.Onliner/Features/SubscribeToTyping/SubscribeToTypingQueryHandler.cs:38-41`, `Backend/BarkFluff.Onliner/Services/OnlineStatusNotifier.cs:36`
**Проблема:** `OnlineVisibilityFilter` и `ChatMembershipFilter` применяются один раз при регистрации подписки (и при `Change*InSubscription`). Notifier при рассылке (`GetStreamsTrackingUser` / `GetStreamsTrackingChat`) повторных проверок не делает.
**Почему это проблема:** Если пользователь B скрыл онлайн-статус (`OnlineVisibility = NONE`) после того, как A подписался, A продолжает получать статусы B, пока не переподключится. Аналогично, исключённый из чата участник продолжает получать typing-события чата (кто и когда печатает) до разрыва стрима. Стримы долгоживущие, окно утечки — часы.
**Рекомендация:** Слушать события смены приватности/состава чата (RabbitMQ) и вычищать соответствующие id из активных подписок, либо периодически ревалидировать tracked-наборы долгоживущих стримов.

### S3. Отзыв сессии не разрывает активные стримы — Medium
**Файл:** `Backend/BarkFluff.Onliner/Consumers/SessionRevokedConsumer.cs:15-25`
**Проблема:** При `SessionRevokedEvent` сессия добавляется в `TokenRevocationCache` — это блокирует только новые вызовы (проверка в `OnTokenValidated`). Уже открытые стримы `SubscribeToOnlineStatus`/`SubscribeToTyping` отозванного устройства не находятся и не закрываются.
**Почему это проблема:** Украденный/отозванный токен продолжает получать онлайн-статусы и typing-события неограниченно долго после отзыва сессии — пока клиент сам не отключится.
**Рекомендация:** В consumer'е находить подписки по `UserId` (менеджеры уже индексируют по subscriberId) и отменять их (хранить `CancellationTokenSource` в `SubscriptionData`, фильтровать по DeviceId, если он сохранён при регистрации).

### S4. gRPC reflection включён и доступен без аутентификации — Low
**Файл:** `Backend/BarkFluff.Onliner/Program.cs:40,97`
**Проблема:** `AddGrpcReflection()` + `MapGrpcReflectionService()` регистрируются безусловно (не только в Development) и без `RequireAuthorization()`.
**Почему это проблема:** Любой неаутентифицированный клиент через публичный `onliner.barkfluff.com` может выгрузить полную схему API (методы, типы сообщений) — упрощает разведку для атакующего.
**Рекомендация:** Включать reflection только в Development (`if (app.Environment.IsDevelopment())`) либо добавить `.RequireAuthorization()` к маппингу.

### S5. Service-токены проходят политику User с UserId = 0 — Low
**Файл:** `Backend/BarkFluff.Onliner/Host/OnlinerApiService.cs:20`, `Backend/BarkFluff.Onliner/Features/SetOnlineStatus/SetOnlineStatusCommandHandler.cs:36`
**Проблема:** Политика `TokenType.User` (XAuth, `XAuthExtensions.cs:79-80`) принимает и `Service`-токены. У Service-токена нет claim `UserId`, поэтому `UserContext.UserId = 0`. Хендлеры Onliner используют `UserId` без проверки `IsAuthenticated`: Service-токен любого сервиса может вызвать `SetOnlineStatus` и создать запись для «пользователя 0», подписаться от его имени и т.п.
**Почему это проблема:** Семантически некорректные записи в storage/БД; межсервисный токен, утёкший из любого сервиса, получает доступ к user-уровневому API Onliner. Сама политика — зона ответственности XAuth (аудит отдельно), но Onliner мог бы защититься локально.
**Рекомендация:** В хендлерах, завязанных на личность пользователя, отклонять вызовы с `UserId == 0` / `TokenType != User`, либо завести отдельную политику «строго User».

### S6. Отзыв сессии теряется после перезапуска сервиса — Medium
**Файл:** `Backend/BarkFluff.Onliner/Program.cs:53`, `Backend/BarkFluff.Onliner/Consumers/SessionRevokedConsumer.cs:15-24`, `Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs:7-19`, `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:46-70`
**Проблема:** Единственный источник отзыва — singleton `TokenRevocationCache`: consumer записывает в него пару `(UserId, DeviceId)`, а `OnTokenValidated` сверяет только этот локальный словарь. Кэш не персистится и при старте не восстанавливается из Identity или отдельного хранилища; `Program.cs` лишь подключает `AddXAuth`, после рестарта словарь пуст. Уже обработанное RabbitMQ-событие не будет повторно доставлено, поэтому ранее отозванный, но ещё не истёкший access-token снова проходит аутентификацию при новом вызове или новой подписке.
**Почему это проблема:** После штатного деплоя, рестарта или OOM-перезапуска (S1/D3) скомпрометированное устройство может восстановить доступ к online-статусам и typing до конца TTL access-token (дефолт `JwtSettings:ExpiryMinutes` — 60 минут), хотя пользователь уже отозвал сессию.
**Рекомендация:** Хранить revoked-сессии с TTL в общем durable-хранилище (например, Redis) либо проверять версию/состояние сессии в Identity; при старте восстановить записи до истечения токенов. Локальный кэш можно оставить только как ускоряющий слой.

## Производительность

### P1. Медленный подписчик блокирует heartbeat'ы и цикл offline-детекции (head-of-line blocking) — High
**Файл:** `Backend/BarkFluff.Onliner/Services/OnlineStatusNotifier.cs:56-57,68`, `Backend/BarkFluff.Onliner/Features/SetOnlineStatus/SetOnlineStatusCommandHandler.cs:56`, `Backend/BarkFluff.Onliner/BackgroundServices/OfflineDetectionService.cs:69-88`
**Проблема:** Уведомления пишутся напрямую в клиентские `IServerStreamWriter` и ожидаются в вызывающем пути: `SetOnlineStatus` (heartbeat) ждёт `Task.WhenAll` всех `WriteAsync`, а `OfflineDetectionService` ждёт `NotifyStatusChanged` последовательно для каждого пользователя (строка 85). Если клиент-подписчик не читает стрим, HTTP/2 flow-control заполняется и `WriteAsync` зависает.
**Почему это проблема:** Один намеренно (или из-за плохой сети) медленный подписчик: (1) подвешивает heartbeat-RPC всех, на кого он подписан — heartbeat не отвечает >5 с, и `OfflineDetectionService` помечает живого пользователя offline (флаппинг статусов); (2) останавливает весь однопоточный цикл offline-детекции — статусы offline перестают рассылаться всем.
**Рекомендация:** Развязать публикацию и доставку: на каждую подписку — bounded `Channel<T>` с политикой drop-oldest; нотификатор кладёт событие в канал и не ждёт; отдельный writer-таск на стрим читает канал и пишет в стрим. Подписчиков, не успевающих читать, отключать.

### P2. Конкурентные WriteAsync в один IServerStreamWriter без синхронизации — потеря уведомлений — High
**Файл:** `Backend/BarkFluff.Onliner/Services/OnlineStatusNotifier.cs:56-57,68`, `Backend/BarkFluff.Onliner/Services/TypingNotifier.cs:56-57,68`
**Проблема:** `NotifyStatusChanged`/`NotifyTyping` вызываются параллельно из разных запросов (heartbeat'ы разных пользователей) и из `OfflineDetectionService`. Один и тот же стрим-подписчик отслеживает многих пользователей/чатов, поэтому в него возможны одновременные `WriteAsync`. gRPC `IServerStreamWriter` не поддерживает параллельные записи — второй `WriteAsync` бросает `InvalidOperationException`, которая глотается catch'ем в `SendToStreamAsync` (увеличивается только `status_notification_errors`).
**Почему это проблема:** Под нагрузкой уведомления о смене статуса тихо теряются (клиент видит устаревший онлайн-статус/typing), при этом стрим жив и переподключения не происходит. Баг проявляется только при конкуренции — на дев-стенде невидим.
**Рекомендация:** Та же мера, что и для P1: один writer-таск на стрим с очередью (`Channel<T>`) гарантирует последовательность записей. Минимальный вариант — `SemaphoreSlim(1)` на подписку.

### P3. N+1 последовательные gRPC-вызовы в OnlineVisibilityFilter — High
**Файл:** `Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs:33-45`
**Проблема:** `GetVisibleUserIdsAsync` в цикле `foreach` последовательно (`await` на каждой итерации) вызывает `GetUserPrivacyAsync` для каждого target-пользователя. Используется в `SubscribeToOnlineStatus`, `ChangeUsersInSubscription` и `GetOnlineStatus` — то есть на каждый запрос статусов списка контактов.
**Почему это проблема:** Подписка/запрос на N контактов = N последовательных round-trip'ов в Users (контакт-лист из 200 человек → 200 RTT только на проверку приватности). Латентность растёт линейно, Users получает кратную нагрузку; в сочетании с S1 это вектор амплификации DoS. Для сравнения: `ChatMembershipFilter` уже использует batch-метод `CheckChatMembership`.
**Рекомендация:** Добавить batch-метод `GetUsersPrivacy(repeated user_ids)` в Users API (по образцу `CheckChatMembership`) и/или короткий TTL-кэш (30-60 с) результатов приватности в Onliner.

### P4. gRPC-проверка членства в чате на каждый typing-heartbeat — Medium
**Файл:** `Backend/BarkFluff.Onliner/Features/SetTypingStatus/SetTypingStatusCommandHandler.cs:38-39`
**Проблема:** Каждый `SetTypingStatus` (heartbeat, шлётся клиентом каждые несколько секунд при наборе) делает удалённый вызов `CheckChatMembership` в Messages без какого-либо кэша.
**Почему это проблема:** Горячий путь с сетевым round-trip'ом: латентность typing-индикатора = латентность Messages; нагрузка на Messages пропорциональна числу печатающих. Membership меняется редко — проверять его на каждый heartbeat избыточно. Также это амплификация: злоумышленник может слать heartbeat'ы с произвольными chat_id, генерируя поток запросов в Messages.
**Рекомендация:** Кэшировать результат membership (`userId+chatId`) с TTL 30-60 с (например, `IMemoryCache`); негативные результаты тоже кэшировать.

### P5. N+1 запросы к БД и отсутствие дедупликации в GetOnlineStatus — Medium
**Файл:** `Backend/BarkFluff.Onliner/Features/GetOnlineStatus/GetOnlineStatusQueryHandler.cs:50-60,77-78`
**Проблема:** Для каждого userId из запроса, не найденного в памяти, выполняется отдельный `FirstOrDefaultAsync` к Postgres внутри цикла. Цикл идёт по `request.UserIds` без `Distinct()` — дубликаты id порождают повторные запросы к БД.
**Почему это проблема:** Запрос статусов K пользователей после рестарта сервиса (память пуста) = K последовательных SQL-запросов; дубликаты в запросе амплифицируют нагрузку на БД произвольно (лимита на размер списка нет — см. S1).
**Рекомендация:** Собрать id, отсутствующие в памяти, и выбрать одним запросом `WHERE UserId IN (...)`; дедуплицировать `request.UserIds` в начале обработки.

### P6. DatabasePersistenceService: per-row SELECT по всему storage каждые 10 минут — Medium
**Файл:** `Backend/BarkFluff.Onliner/BackgroundServices/DatabasePersistenceService.cs:79-94`
**Проблема:** Цикл по всем статусам делает для каждого отдельный `FirstOrDefaultAsync` (строки 80-81), затем один `SaveChangesAsync`. Сохраняются все записи, включая не менявшиеся с прошлого прогона; все сущности трекаются одним контекстом.
**Почему это проблема:** N записей в storage → N последовательных SELECT каждые 10 минут + пиковое потребление памяти change-tracker'ом. Так как storage никогда не очищается (P7), N растёт монотонно — цикл персистенции будет занимать минуты и грузить БД на ровном месте.
**Рекомендация:** Использовать bulk upsert (`INSERT ... ON CONFLICT (UserId) DO UPDATE` через `ExecuteSqlRaw`/Npgsql binary copy) и сохранять только записи, изменённые с прошлого прогона (флаг dirty или сравнение по `LastSeen`).

### P7. Unbounded рост OnlineStatusStorage — записи никогда не удаляются — Medium
**Файл:** `Backend/BarkFluff.Onliner/Services/OnlineStatusStorage.cs:14,56-86`
**Проблема:** `_statuses` только пополняется: `UpdateStatus` добавляет запись на первый heartbeat, `SetOffline` переводит её в Offline, но не удаляет. Методов eviction/TTL нет.
**Почему это проблема:** Память растёт линейно с числом уникальных пользователей с момента старта процесса и освобождается только рестартом. Усугубляет P6 (объём персистенции) и O(N)-сканы P8/метрик. Memory-limit у контейнера не задан (D3).
**Рекомендация:** Удалять записи, находящиеся в Offline дольше порога (например, 1 час) — статус и так персистится в БД и поднимается оттуда в `GetOnlineStatus`; чистку можно добавить в существующий цикл `OfflineDetectionService` (реже, раз в минуту).

### P8. OfflineDetectionService: полный скан storage каждую секунду — Low
**Файл:** `Backend/BarkFluff.Onliner/BackgroundServices/OfflineDetectionService.cs:17,51`, `Backend/BarkFluff.Onliner/Services/OnlineStatusStorage.cs:108-117`
**Проблема:** Каждую секунду `GetOnlineUsersOlderThan` перебирает весь словарь статусов (O(N)), включая Offline-записи, которые никогда не удаляются (P7).
**Почему это проблема:** Постоянная фоновая CPU-нагрузка, растущая с размером storage; при больших N секундный интервал перестаёт выдерживаться (плюс блокировки из P1 в том же цикле).
**Рекомендация:** После устранения P7 проблема в основном уходит; при дальнейшем росте — индекс «времени истечения» (например, priority queue по LastSeen) вместо полного скана.

### P9. Гонка обновления обратного индекса — осиротевшие записи и записи в мёртвые стримы — Medium
**Файл:** `Backend/BarkFluff.Onliner/Services/OnlineStatusSubscriptionsManager.cs:110-128,64-81`, `Backend/BarkFluff.Onliner/Services/TypingSubscriptionsManager.cs:115-133,69-85`
**Проблема:** В `UpdateAllSubscriptions` замена `SubscriptionData` (CAS `TryUpdate`, строка 122) и синхронизация обратного индекса (строки 125-126) не атомарны. При конкурентных `ChangeUsersInSubscription` или гонке с `RemoveSubscription` (клиент отключился во время апдейта) возможна последовательность, при которой `AddToReverseIndex` выполняется после удаления подписки: в `_reverseIndex` навсегда остаются записи с ссылкой на закрытый стрим.
**Почему это проблема:** Утечка памяти обратного индекса (записи не удаляются никогда — `RemoveSubscription` чистит только набор из актуальной `SubscriptionData`) и постоянные попытки записи в мёртвые стримы при каждой смене статуса (`status_notification_errors` растёт, лишние исключения на горячем пути).
**Рекомендация:** Сериализовать мутации в пределах одной подписки (lock per connection) либо периодической фоновой сверкой удалять из `_reverseIndex` connectionId, отсутствующие в `_subscriptions`.

### P10. Нет финального сохранения статусов при остановке сервиса — Low
**Файл:** `Backend/BarkFluff.Onliner/BackgroundServices/DatabasePersistenceService.cs:42`
**Проблема:** `await Task.Delay(SaveInterval, stoppingToken)` при остановке бросает `OperationCanceledException`, и `ExecuteAsync` завершается без финального `SaveStatusesToDatabaseAsync`.
**Почему это проблема:** При каждом деплое/рестарте теряется до 10 минут обновлений `LastSeen` — после рестарта `GetOnlineStatus` отдаёт устаревший last seen из БД.
**Рекомендация:** Обернуть `Task.Delay` и выполнить финальный flush в `finally` (или переопределить `StopAsync`) с независимым от `stoppingToken` таймаутом.

## Docker / nginx

### D1. Порт Onliner опубликован на хост в master-compose — обход nginx/TLS — Medium
**Файл:** `Backend/docker-compose-master.yml:120`
**Проблема:** `ports: ["${ONLINER_PORT}:${ONLINER_PORT}"]` публикует Kestrel-порт 7009 (plaintext HTTP/2, TLS в `SetRunningAddress` включается только при заданном `RunSettings:Tls`) прямо на хост, минуя nginx-терминацию TLS. В dev-compose порт корректно не публикуется.
**Почему это проблема:** Если порт не закрыт внешним firewall'ом, JWT-токены (`x-auth-token`) и онлайн-статусы пользователей ходят по сети в открытом виде, плюс появляется прямой путь к сервису мимо ограничений nginx. Docker сам вписывает правила в iptables, обходя типичные host-firewall настройки (ufw).
**Рекомендация:** Убрать публикацию порта (внутри `barkfluff-network` nginx достучится до `onliner:7009` без `ports:`) либо привязать к loopback: `127.0.0.1:${ONLINER_PORT}:${ONLINER_PORT}`.

### D2. nginx grpc_read_timeout 300s обрывает простаивающие подписочные стримы — Medium
**Файл:** `Backend/nginx/onliner.conf:22-23`
**Проблема:** `grpc_read_timeout 300s` — если в стриме `SubscribeToOnlineStatus`/`SubscribeToTyping` нет ни одного события 5 минут (вполне обычно: никто из отслеживаемых не менял статус), nginx разрывает соединение. HTTP/2 keepalive-пинги это таймаут не сбрасывают — нужны именно данные.
**Почему это проблема:** Все клиенты вынуждены пере-подписываться каждые ≤5 минут простоя: лишние reconnect-штормы (а каждая переподписка — это ещё и N gRPC-вызовов проверки приватности, см. P3) и пропуски событий в окне переподключения.
**Рекомендация:** Поднять `grpc_read_timeout`/`grpc_send_timeout` для этого сервиса (например, 24h, как принято для streaming-локаций) либо слать серверные keepalive-сообщения в стрим раз в 1-2 минуты.

### D3. Нет лимитов памяти/healthcheck для контейнера со state в памяти — Low
**Файл:** `Backend/docker-compose-master.yml:117-126`, `Backend/docker-compose-dev.yml:116-125`
**Проблема:** Для onliner не заданы `mem_limit`/`deploy.resources` и `healthcheck`; при этом весь state сервиса — неограниченные in-memory структуры (S1, P7).
**Почему это проблема:** При раздувании памяти (атака из S1 или органический рост из P7) контейнер без лимита давит память хоста и соседние сервисы; OOM-killer убьёт произвольный процесс. Без healthcheck завязший процесс (например, остановившийся offline-цикл из P1) останется «работающим» для оркестратора.
**Рекомендация:** Задать memory-limit контейнеру и добавить gRPC healthcheck (grpc_health_probe или TCP-проверка порта).

---

*Dockerfile замечаний не вызвал: multi-stage сборка, runtime — `aspnet:10.0-noble-chiseled`, запуск от непривилегированного пользователя (`USER $APP_UID`), секреты в образ не копируются. В `OnlineStatusContextFactory.cs:17` — placeholder-пароль design-time подключения, реальным секретом не является. Создания `GrpcChannel`/`HttpClient` на запрос нет (клиенты через `AddGrpcClient`-фабрику), sync-over-async не обнаружен.*
