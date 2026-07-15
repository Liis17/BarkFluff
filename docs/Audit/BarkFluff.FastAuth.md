# Аудит: BarkFluff.FastAuth

> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

FastAuth реализует QR-авторизацию: анонимное устройство генерирует сессию и подписывается на стрим результата, авторизованный телефон сканирует QR, получает одноразовый `ConfirmationCode` и подтверждает/отклоняет вход; токены доставляются по стриму. Криптографическая база крепкая: `Guid.NewGuid()` (CSPRNG, 122 бита энтропии) для session id и confirmation code, TTL 5 минут с фоновой очисткой, строгая одноразовость переходов состояний под локом, привязка Accept/Reject к userId сканировавшего пользователя, единственный подписчик на стрим. Главные проблемы: полное отсутствие rate limiting на анонимных эндпоинтах (DoS на память и CPU), доставка итоговых токенов защищена только знанием ID, который закодирован в самом QR (усиливает QRLjacking), спуфабельный IP в карточке подтверждения и логирование секретного session id.

| Критичность | Количество |
| ----------- | ---------- |
| Critical    | 0          |
| High        | 1          |
| Medium      | 4          |
| Low         | 8          |

## Безопасность

### S1. ~~Нет rate limiting на анонимных эндпоинтах: неограниченное создание сессий в памяти и генерация QR~~ — ~~High~~ **Исправлено (2026-06-23)**

**Файл:** `Backend/BarkFluff.FastAuth/Host/FastAuthApiService.cs:22-44` (`[AllowAnonymous]` на `GenerateFastAuthToken` и `SubscribeFastAuthResult`); `Backend/BarkFluff.FastAuth/Infrastructure/FastAuthSessionsManager.cs:14-34`; `Backend/nginx/fast-auth.conf:15-24` (location без `limit_req`/`limit_conn`)
**Проблема:** `GenerateFastAuthToken` доступен без аутентификации и без какого-либо ограничения частоты — ни в nginx, ни в сервисе. Каждый вызов создаёт объект `FastAuthSession` с unbounded `Channel` в `ConcurrentDictionary` (живёт 5 минут до экспирации + 30 секунд retention) и синхронно генерирует PNG QR-кода (`QrCodeGenerator.cs:15`, `GetGraphic(20)` — ~740x740 px) с base64-кодированием. `SubscribeFastAuthResult` так же анонимно открывает server-stream до 5 минут на сессию.
**Почему это проблема:** Дешёвый флуд с одного или нескольких IP за 5-минутное окно накапливает миллионы сессий в памяти процесса (TTL-очистка есть, но нет верхней границы количества) и загружает CPU генерацией PNG — классический memory/CPU exhaustion на публичном неаутентифицированном эндпоинте авторизационного сервиса. Падение FastAuth ломает QR-вход для всех клиентов.
**Рекомендация:** Добавить в `fast-auth.conf` `limit_req_zone`/`limit_req` (QR-генерация — редкая операция, 1-2 r/s с burst на IP достаточно) и `limit_conn` для стримов. В сервисе — жёсткий cap на количество одновременных pending-сессий (например, 10 000) с отказом `ResourceExhausted` при превышении и метрикой.

### S2. ~~Итоговые токены выдаются по знанию ID, закодированного в самом QR~~ — ~~Medium~~ **Неактуально**

**Файл:** `Backend/BarkFluff.FastAuth/Features/GenerateFastAuthToken/GenerateFastAuthTokenCommandHandler.cs:55` (payload QR = `session.Id`); `Backend/BarkFluff.FastAuth/Features/SubscribeFastAuthResult/SubscribeFastAuthResultQueryHandler.cs:14-30` (анонимная подписка по тому же ID)
**Проблема:** Один и тот же идентификатор `FastAuthId` служит и публичным «сканируемым» значением (он закодирован в QR, который отображается на экране и может быть сфотографирован), и единственным ключом доступа к стриму, по которому после Accept приходят access/refresh-токены (`fast_auth_api.proto:58-65`).
**Почему это проблема:** Любой, кто увидел QR (фото экрана в офисе/кафе, трансляция экрана, скриншот), знает ключ подписки. Если злоумышленник подпишется раньше легитимного устройства, защита «единственный подписчик» (`FastAuthSession.cs:38-50`) отдаст стрим ему: легитимное устройство получит `FastAuthInvalidStateException`, но если пользователь всё же отсканирует и подтвердит вход — токены его аккаунта уйдут злоумышленнику. Single-subscriber смягчает (легитимный клиент видит сбой), но не устраняет гонку.
**Рекомендация:** Разделить секреты: в QR кодировать `scan_id`, а для подписки выдавать отдельный `subscriber_secret` только в `GenerateFastAuthTokenResponse` (он никогда не попадает в QR). Тогда наблюдение QR не даёт пути к токенам.

### S3. IP-адрес в карточке подтверждения берётся из спуфабельных источников — Medium **→ Отложено** (требует общей работы по резолву IP в RequestContextInterceptor; сейчас часто показывается 0.0.0.0)

**Файл:** `Backend/BarkFluff.FastAuth/Features/ScanFastAuth/ScanFastAuthCommandHandler.cs:48` (отдаёт `session.IpAddress` пользователю); `Backend/BarkFluff.FastAuth/Features/GenerateFastAuthToken/GenerateFastAuthTokenCommandHandler.cs:43` (источник — `requestContext.IpAddress`); `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs:68-97`
**Проблема:** IP, который видит пользователь при решении «подтвердить/отклонить вход», резолвится с приоритетом: 1) клиентская gRPC-метадата `x-ip-address` (полностью контролируется отправителем), 2) первый элемент `X-Forwarded-For` (nginx использует `$proxy_add_x_forwarded_for`, который **дописывает** реальный IP к присланному клиентом значению — первый элемент остаётся подделанным), и только потом реальный IP соединения. `DeviceName`/`OS`/`AppName` — тоже клиентские заголовки (это неизбежно), но IP мог бы быть достоверным.
**Почему это проблема:** Карточка подтверждения — единственная защита пользователя от QRLjacking-фишинга (злоумышленник генерирует QR и подсовывает жертве). Атакующий выставляет `x-ip-address` в IP из города/сети жертвы, и карточка выглядит как вход с её собственного устройства — пользователь подтверждает чужую сессию.
**Рекомендация:** Для FastAuth-сессии использовать только серверно-достоверный IP: `X-Real-IP`, выставленный nginx (с проверкой, что запрос пришёл от доверенного прокси), либо `RemoteIpAddress`. Клиентскую метадату `x-ip-address` для этого сценария игнорировать.

### S4. ~~Секретный session id пишется в логи в открытом виде~~ — ~~Medium~~ **Исправлено (2026-06-23)**

**Файл:** `Backend/BarkFluff.FastAuth/Features/GenerateFastAuthToken/GenerateFastAuthTokenCommandHandler.cs:47-50`; также `ScanFastAuthCommandHandler.cs:38-40`, `AcceptFastAuthCommandHandler.cs:70-72`, `SubscribeFastAuthResultQueryHandler.cs:23`, `FastAuthExpirationService.cs:47`
**Проблема:** Полный `session.Id` логируется на уровне Information на каждом шаге жизненного цикла. Этот же ID — единственный ключ к анонимной подписке на стрим, по которому доставляются access/refresh-токены (см. S2).
**Почему это проблема:** Любой, у кого есть доступ к Seq/файловым логам (разработчики, операторы, скомпрометированный лог-пайплайн), в реальном времени видит идентификаторы активных pending-сессий и может подписаться на них раньше легитимного устройства, получив токены после подтверждения. Логирование bearer-эквивалентных секретов нарушает принцип минимизации.
**Рекомендация:** Логировать только префикс (например, первые 8 символов), как уже сделано для FCM-токенов в CloudMessaging, либо хэш ID. После внедрения отдельного `subscriber_secret` (S2) критичность снижается, но практику стоит поправить в любом случае.

### S5. ~~Identity-сессия создаётся до финальной атомарной проверки — осиротевшие валидные сессии~~ — ~~Low~~ **Исправлено (2026-06-23)**

**Файл:** `Backend/BarkFluff.FastAuth/Features/AcceptFastAuth/AcceptFastAuthCommandHandler.cs:44-66`
**Проблема:** `CreateSessionForUserServerAsync` (создание полноценной сессии устройства с access/refresh-токенами в Identity) вызывается **до** `session.TryAccept(...)`. Если `TryAccept` вернёт `false` (гонка с фоновой экспирацией `FastAuthExpirationService` или конкурентным Reject между проверками на строках 25-39 и вызовом на строке 63), хендлер бросает исключение, а уже созданная Identity-сессия остаётся: валидные токены существуют, но никому не доставлены и не отозваны. Похожий случай — подписчик отвалился до Accept: токены записаны в channel, через 30 секунд сессия удалена из памяти, Identity-сессия живёт.
**Почему это проблема:** На аккаунте пользователя накапливаются «призрачные» активные устройства/сессии, которые он не использует, — мусор в списке устройств и лишние валидные refresh-токены в БД Identity (расширение поверхности при компрометации БД).
**Рекомендация:** Минимально — при `TryAccept == false` компенсирующе отзывать только что созданную сессию в Identity. Чище — сначала атомарно переводить сессию в Accepted (резервируя переход), затем создавать токены и публиковать результат, с откатом статуса при сбое Identity.

### S6. ~~gRPC reflection включён безусловно (в т.ч. в проде)~~ — ~~Low~~ **Исправлено (2026-06-23)**

**Файл:** `Backend/BarkFluff.FastAuth/Program.cs:25, 40`
**Проблема:** `AddGrpcReflection()` и `MapGrpcReflectionService()` вызываются без проверки окружения — reflection доступен на публичном `fast-auth.barkfluff.com` анонимно.
**Почему это проблема:** Любой внешний клиент (`grpcurl`) получает полное описание API авторизационного сервиса, включая нереализованный `FastAuthServerApi.GetFastAuthInfo`, — упрощение разведки для атакующего. Не уязвимость сама по себе, но лишняя информация на security-критичном сервисе.
**Рекомендация:** Маппить reflection только в Development (`if (app.Environment.IsDevelopment())`).

### S7. Политика `User` пропускает Service-токены на пользовательские операции — Low **→ Отложено** (требует проверки всех сервисов где используется политика User; сейчас пропускаем)

**Файл:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:79-80` (политика общая, затрагивает `FastAuthApiService.cs:46-76`)
**Проблема:** Политика `TokenType.User` принимает claim `User` **или** `Service`. `ScanFastAuth`/`AcceptFastAuth`/`RejectFastAuth` рассчитаны на конкретного пользователя, но любой держатель сервисного токена проходит авторизацию; у Service-токена `UserContext.UserId` = 0 (`UserContext.cs:23`), и `TryScan(0)` успешно переводит чужую сессию в Scanned.
**Почему это проблема:** Сервисный токен любого внутреннего сервиса может «отсканировать» pending-сессию (зная её ID, например из логов — см. S4) и заблокировать легитимный вход (`AlreadyHandled` для настоящего пользователя). Довести до Accept не выйдет (`CreateSessionForUserServer` с UserId=0 не создаст осмысленную сессию), но DoS конкретной сессии и обход семантики «только пользователь» — реальны. Корень в общей политике GrpcServer (зона другого аудита), здесь — затронутость FastAuth.
**Рекомендация:** В хендлерах Scan/Accept/Reject проверять `userContext.TokenType == TokenType.User` (или `UserId != 0`) до обработки.

### S8. ~~`IdentityService:Token` не валидируется при старте~~ — ~~Low~~ **Исправлено (2026-07-15)**

**Файл:** `Backend/BarkFluff.FastAuth/Program.cs`
**Решение:** `IdentityService:Token` теперь `?? throw new InvalidOperationException(...)` — fail-fast при старте, как в CloudMessaging. `Host` оставлен с фолбэком на `http://identity:7000` — осознанно, не трогали.

**Положительные стороны (по чек-листу):** session id и confirmation code — `Guid.NewGuid()` (CSPRNG .NET, 122 бита, перебор нереален: `FastAuthSessionsManager.cs:22`, `FastAuthSession.cs:67`); TTL 5 минут (`FastAuthSessionsManager.cs:9`) с фоновой очисткой; одноразовость кода и переходов состояний обеспечена атомарно под локом (`FastAuthSession.cs:74-104`); Accept/Reject привязаны к userId сканировавшего (`FastAuthSession.cs:79-80, 95-96` + дублирующая проверка в хендлерах); сообщения ошибок контролируемые, без утечки деталей (`Shared/BarkFluff.Shared.Exceptions/FastAuth/*`); SQL отсутствует (всё in-memory) — инъекции неприменимы; хардкода секретов в коде и конфигах нет.

### S9. ~~TTL QR-сессии не проверяется при Accept/Reject~~ — ~~Low~~ **Исправлено (2026-07-15)**

**Файлы:** `Backend/BarkFluff.FastAuth/Domain/FastAuthSession.cs`; `Features/AcceptFastAuth/AcceptFastAuthCommandHandler.cs`; `Features/RejectFastAuth/RejectFastAuthCommandHandler.cs`.
**Решение:** `TryAccept`/`TryReject` теперь внутри лока проверяют `DateTime.UtcNow >= ExpiresAt` и при просрочке атомарно переводят сессию в `Expired` (общий приватный `ExpireLocked()`, переиспользован и в `TryExpire`), возвращая `false` — без ожидания 30-секундного sweep. Дополнительно оба хендлера (`Accept`/`Reject`) проверяют `ExpiresAt` до вызова Identity, чтобы не создавать лишнюю сессию в общем случае просрочки; компенсационный отзыв в Identity при гонке (S5) уже прикрывает оставшийся узкий race-window.

## Производительность

### P1. Состояние сессий в памяти процесса — нет горизонтального масштабирования, потеря при рестарте — Low **→ Неактуально**

**Файл:** `Backend/BarkFluff.FastAuth/Infrastructure/FastAuthSessionsManager.cs:14`
**Решение:** Осознанное ограничение при текущем масштабе (single instance). Вынос в Redis — не «удобный рефактор», а отдельная фича ради проблемы, которая пока не проявилась. Оставлено как есть.

### P2. Генерация QR PNG с избыточным разрешением на анонимном эндпоинте — Low **→ Отложено** (вариант 2 требует параллельного обновления клиентов Android/WPF/Web)

**Файл:** `Backend/BarkFluff.FastAuth/Infrastructure/QrCodeGenerator.cs:15`
**Проблема:** `GetGraphic(20)` — 20 пикселей на модуль — даёт PNG ~740x740 px для GUID-нагрузки; результат дополнительно раздувается base64 (+33%) и едет в gRPC-ответе. Генерация происходит синхронно на каждый анонимный запрос.
**Почему это проблема:** Лишние аллокации и CPU на каждый вызов незащищённого эндпоинта (умножается на S1). Клиенты в любом случае масштабируют изображение под экран — 20 px/модуль избыточно.
**Рекомендация:** Снизить до 8-10 px/модуль, либо вернуть `TokenFormat.Text` (`session.Id`) и рендерить QR на клиенте — все платформы проекта это умеют.

**По остальному чек-листу производительности:** клиенты gRPC создаются через `AddGrpcClient` (factory, не на запрос); sync-over-async отсутствует; фоновый sweep — раз в 30 секунд (`FastAuthExpirationService.cs:14`), не агрессивный; TTL-очистка in-memory хранилища реализована корректно (экспирация + retention 30 с); запросов в цикле нет.

## Docker / nginx

### D1. Порт FastAuth публикуется на хост в обход TLS-терминации nginx — Medium **→ Неактуально**

**Файл:** `Backend/docker-compose-master.yml:97` (`ports: ["${FASTAUTH_PORT}:${FASTAUTH_PORT}"]`)
**Решение:** master-compose не используется — реальный деплой идёт из `docker-compose-dev.yml`, где `ports` у fast-auth и так отсутствует (см. `project_compose_dev_is_prod` в памяти проекта).

### D2. ~~В nginx-конфиге нет rate limiting для анонимных эндпоинтов~~ — ~~Low~~ **Исправлено (2026-06-23)**

**Файл:** `Backend/nginx/fast-auth.conf:15-24`
**Проблема:** Единственный `location /` проксирует весь gRPC-трафик без `limit_req`/`limit_conn`; `grpc_read_timeout 300s` корректно согласован с TTL сессии (5 минут), но число одновременных стримов с одного IP не ограничено.
**Почему это проблема:** Реализационная часть S1: nginx — первая и самая дешёвая линия защиты публичного анонимного эндпоинта от флуда.
**Рекомендация:** `limit_req_zone $binary_remote_addr` + `limit_req` на location и `limit_conn` для ограничения одновременных стримов с одного адреса.

**Dockerfile** (`Backend/BarkFluff.FastAuth/Dockerfile`, `Dockerfile.slim`) — замечаний нет: chiseled-образ, непривилегированный пользователь (`USER $APP_UID`), NuGet-кэш через BuildKit, секреты в образ не попадают (`.dockerignore` исключает `.env`); `appsettings.json` содержит только порт и локальный адрес Configuration.
