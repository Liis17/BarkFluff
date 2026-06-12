# Аудит: BarkFluff.Navigator

> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Navigator — публичный реестр серверов BarkFluff (`navigator.barkfluff.com`): `RegisterServer` принимает саморегистрацию серверов, `ListServers` отдаёт каталог клиентам. Оба RPC доступны анонимно: для `ListServers` это by design, но анонимный `RegisterServer` без какой-либо верификации владения BeaconHost позволяет любому опубликовать вредоносный «сервер» в каталоге, которому доверяют все клиенты, — это прямой фишинговый вектор. Вторая серьёзная проблема — JWT-секрет XAuth захардкожен в `appsettings.json` и закоммичен в репозиторий, при этом именно он действует в проде, потому что Navigator — единственный сервис, который не вызывает `LoadConfiguration`. Троттлинг регистраций обходится сменой одного символа имени, а хранилище `_servers` никогда не очищается — анонимный клиент может неограниченно раздувать память процесса. nginx-конфига для navigator в `Backend/nginx/` нет, а собственный compose сервиса публикует голый h2c-порт наружу.

| Критичность | Количество |
| ----------- | ---------- |
| Critical    | 1          |
| High        | 2          |
| Medium      | 3          |
| Low         | 6          |

## Безопасность

### S1. Анонимная регистрация серверов — спуфинг каталога (фишинг клиентов) — Critical

**Файл:** `Backend/BarkFluff.Navigator/Host/NavigatorApiService.cs:31` (метод без `[Authorize]`), `Backend/BarkFluff.Navigator/Host/NavigatorApiService.cs:46` (`AddedBy = "Anonymous"`)
**Проблема:** `RegisterServer` не требует аутентификации. XAuth подключён (`Program.cs:33,41`), но ни на сервисе, ни на методе нет `[Authorize]`, а строка 46 (`AddedBy = _userContext.IsAuthenticated ? ... : "Anonymous"`) явно узаконивает анонимный вызов. Валидация в `Features/RegisterServer/RegisterServerCommandHandler.cs:43-114` проверяет только формат полей (длины, hex-цвета, синтаксис hostname) — нет ни верификации владения BeaconHost (challenge/обратный вызов к Beacon), ни модерации, ни привязки к аккаунту.
**Почему это проблема:** каталог Navigator — точка доверия для всех клиентов (Android/WPF/Web): пользователь выбирает сервер из списка и отправляет на его Beacon/Identity логин, пароль и OTP. Злоумышленник анонимно регистрирует запись с именем и оформлением, неотличимыми от официального сервера («BarkFluff Official», те же цвета), но со своим BeaconHost — и собирает учётные данные пользователей. Дополнительно: ключ записи — `Name:BeaconHost:BeaconPort` (`Persistence/ServersStorage.cs:32`), а `AddOrUpdate` (`ServersStorage.cs:43-47`) полностью заменяет объект, так что любой может «перерегистрировать» чужую запись с тем же ключом и подменить её Description/ServerPublicName/Location/цвета.
**Рекомендация:** требовать аутентификацию (`[Authorize(Policy = nameof(TokenType.Service))]` или отдельный регистрационный токен, выдаваемый вручную), верифицировать владение BeaconHost (запрос к заявленному Beacon с одноразовым challenge), хранить владельца записи и запрещать перезапись чужих ключей. Для публичного каталога — премодерация новых серверов.

### S2. Захардкоженный JWT-секрет XAuth в репозитории — действует в проде — High

**Файл:** `Backend/BarkFluff.Navigator/appsettings.json:13`
**Проблема:** `JwtSettings:SecretKey = "JKASDFHJKKEF8w7728JHFDWHJJWEF23423489FJJFD7#&@93hHFHFF"` закоммичен в git. В отличие от остальных сервисов, Navigator не вызывает `LoadConfiguration(...)` (в `Program.cs` его нет — конфигурация из Configuration-сервиса не подтягивается, переменная `CONFIGURATION_SERVICE_URL` из compose просто игнорируется), поэтому XAuth (`Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:26`) валидирует токены именно этим публично известным ключом.
**Почему это проблема:** любой, у кого есть доступ к репозиторию, может подписать валидный JWT (Issuer `BarkFluffNavigator`, Audience `BarkFluffMicroservices`) с произвольными claims — в том числе `TokenType=Service` и любым `UserId`. Сейчас это даёт лишь подделку поля `AddedBy`, но как только на любом методе Navigator появится `[Authorize]`, защита будет полностью обойдена. Кроме того, секрет выглядит как «дефолтный» и может совпадать с дев-окружениями других сервисов.
**Рекомендация:** убрать секрет из `appsettings.json`, подключить `LoadConfiguration(ServiceId...)` как в остальных сервисах либо передавать секрет через переменную окружения/секрет-хранилище; ротировать скомпрометированный ключ.

### S3. Троттлинг регистраций обходится и подвержен гонке — Medium

**Файл:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs:32-41`
**Проблема:** ключ троттлинга `$"{server.Name}:{server.BeaconHost}:{server.BeaconPort}"` (строка 32) целиком контролируется клиентом — достаточно менять один символ в `Name`, чтобы каждый запрос имел новый ключ и проверка на строках 34-41 никогда не срабатывала. Глобального или per-IP лимита нет. Дополнительно проверка не атомарна: `TryGetValue` (строка 34) и запись `_lastRegistrationTimes[serverKey] = now` (строка 49) разнесены, параллельные запросы с одним ключом проходят оба.
**Почему это проблема:** троттлинг — единственная защита анонимного эндпоинта от флуда, и она не работает против атакующего, что напрямую питает P1 (рост памяти) и замусоривание каталога. Гонка дополнительно позволяет обходить лимит даже без смены ключа.
**Рекомендация:** лимитировать по адресу клиента (per-IP rate limiting, например `AddRateLimiter` ASP.NET Core или на уровне nginx), а не по содержимому запроса; для атомарности использовать `ConcurrentDictionary.TryAdd`/`AddOrUpdate` с проверкой внутри фабрики.

### S4. gRPC reflection включён на публичном эндпоинте — Low

**Файл:** `Backend/BarkFluff.Navigator/Program.cs:29,39`
**Проблема:** `AddGrpcReflection()` и `MapGrpcReflectionService()` вызываются безусловно, не только в Development.
**Почему это проблема:** публичный `navigator.barkfluff.com` отдаёт полное описание API любому (`grpcurl describe`), упрощая разведку поверхности атаки. Для приватных сервисов это приемлемо, для публичного — лишняя информация.
**Рекомендация:** включать reflection только в `app.Environment.IsDevelopment()`.

### S5. Ошибки валидации и троттлинга уходят клиенту сырыми сообщениями без кода ошибки — Low

**Файл:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs:38-40`, `Backend/BarkFluff.Navigator/Features/RegisterServer/RegisterServerCommandHandler.cs:66,82,88,93,99,104,109`
**Проблема:** часть валидаций бросает типизированные исключения (`BeaconHostEmptyException`, `InvalidBeaconHostException`, `InvalidHexColorException` — наследники `BaseGrpcException`, клиент получает `x-error-code`), а часть — голые `ArgumentException`/`InvalidOperationException`. Общий интерцептор (`Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs:79`) превращает их в `StatusCode.Unknown` с сырым `ex.Message` и логирует как «КРИТИЧЕСКАЯ ОШИБКА».
**Почему это проблема:** клиенты не могут программно различать ошибки (нет `x-error-code`), любое внутреннее исключение тоже утечёт своим сообщением наружу, а штатные отказы валидации засоряют лог уровня Error и метрику `grpc_requests_errors`.
**Рекомендация:** завести типизированные исключения (наследники `BaseGrpcException`) для всех валидаций и троттлинга, как уже сделано для BeaconHost/цветов.

### S6. Нет централизованного логирования и метрик у публичного сервиса — Low

**Файл:** `Backend/BarkFluff.Navigator/Program.cs` (отсутствуют вызовы `AddBarkFluffSerilog` / `AddBarkFluffMetrics`)
**Проблема:** в отличие от остальных сервисов, Navigator не подключает Serilog→Seq и `MetricsCollector` — логи остаются в консоли контейнера на отдельном хосте, метрик нет вовсе.
**Почему это проблема:** Navigator — единственный полностью публичный анонимный эндпоинт платформы; именно здесь важно видеть всплески регистраций/запросов и попытки злоупотреблений, но обнаружить атаку (S1/S3/P1) по факту нечем.
**Рекомендация:** подключить `AddBarkFluffSerilog("BarkFluff.Navigator")` и `AddBarkFluffMetrics(...)` по образцу других сервисов (счётчики регистраций, отклонённых запросов, размера каталога).

## Производительность

### P1. Хранилище `_servers` никогда не очищается — неограниченный рост памяти от анонимных запросов — High

**Файл:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs:9,43-47`
**Проблема:** записи добавляются в `_servers` через `AddOrUpdate` (строки 43-47), но ни один путь кода их не удаляет: `GetServers()` (строки 20-27) лишь фильтрует протухшие записи по `lastSeen`, оставляя их в словаре, а `CleanupExpiredThrottleEntries` (строки 54-63) чистит только `_lastRegistrationTimes`. Каждый уникальный ключ (а он полностью контролируется клиентом, см. S3) — это навсегда удерживаемый объект `ServerInfo` размером до ~3 КБ (Name 64 + Description 512 + Location 128 + BeaconHost до 2048 символов).
**Почему это проблема:** в связке с анонимным `RegisterServer` и обходимым троттлингом это дешёвый DoS: цикл регистраций с уникальными именами монотонно раздувает память процесса до OOM-kill контейнера (`restart: always` превратит это в флаппинг). Даже без атаки память течёт от каждого переименованного/переехавшего сервера.
**Рекомендация:** удалять записи с `lastSeen` старше `ActivePeriod` (фоновый таймер или ленивая чистка в `RegisterServer`), плюс жёсткий верхний предел на размер словаря с отказом в регистрации сверх лимита.

### P2. Полный проход по словарю троттлинга на каждой регистрации — Low

**Файл:** `Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs:51,54-63`
**Проблема:** `CleanupExpiredThrottleEntries` вызывается на каждом `RegisterServer` и линейно обходит весь `_lastRegistrationTimes`.
**Почему это проблема:** O(n) на каждый запрос; при большом количестве ключей (см. S3/P1 — их создаёт сам атакующий) регистрация дорожает квадратично от потока запросов. На текущих объёмах не критично.
**Рекомендация:** чистить по таймеру (раз в `ThrottlePeriod`), а не на каждом запросе; либо решится само при переходе на per-IP rate limiter (S3).

### P3. `ListServers` без пагинации и кэша — усиление трафика — Low

**Файл:** `Backend/BarkFluff.Navigator/Features/ListServers/ListServersQueryHandler.cs:27,34-58`
**Проблема:** каждый анонимный запрос материализует весь список (`GetServers()` — полный проход + `ToList`) и сериализует все поля всех серверов в ответ. Пагинации, лимита и кэширования нет; `AccountsCount` всегда 0 (строка 45) — поле мёртвое.
**Почему это проблема:** маленький запрос → большой ответ; если каталог раздут (P1), анонимный `ListServers` становится амплификатором трафика и CPU. Для честного списка из десятков серверов — не существенно.
**Рекомендация:** короткий кэш сформированного ответа (каталог меняется редко) и верхний предел числа возвращаемых записей.

### P4. Мёртвые зависимости и нереализованная персистентность — Low

**Файл:** `Backend/BarkFluff.Navigator/BarkFluff.Navigator.csproj:27,32-36,40`, `Backend/BarkFluff.Navigator/Domain/ServerInfo.cs:7-8`
**Проблема:** подключены `Npgsql.EntityFrameworkCore.PostgreSQL`, `EntityFrameworkCore.Tools`, `AWSSDK.Core`, заведена пустая папка `Persistence/Migrations/` и атрибут `[Key]` на `ServerInfo.Id` — но никакой БД сервис не использует, хранилище чисто in-memory (рестарт контейнера обнуляет каталог, и до повторных регистраций серверов клиенты видят пустой список). Также `beacon_api.proto` подключён как Client (`csproj:19`), но Beacon-клиент нигде не создаётся.
**Почему это проблема:** лишние зависимости увеличивают образ и поверхность атаки, а незавершённая персистентность — это потеря каталога при каждом деплое публичного сервиса.
**Рекомендация:** либо довести до конца хранение в PostgreSQL (тогда уйдёт и P1), либо удалить неиспользуемые пакеты/папку/атрибуты.

## Docker / nginx

### D1. Нет nginx-конфига — наружу опубликован голый h2c-порт без TLS — Medium

**Файл:** `Backend/nginx/` (файл `navigator.conf` отсутствует), `Backend/BarkFluff.Navigator/docker-compose-master.yml:9` и `docker-compose-dev.yml:9` (`ports: [ "${NAVIGATOR_PORT}:${NAVIGATOR_PORT}" ]`), `Backend/BarkFluff.Navigator/Program.cs:15-21`
**Проблема:** в `Backend/nginx/` есть конфиги для beacon/identity/users и т.д., но не для navigator — TLS-терминация `navigator.barkfluff.com:443` живёт вне репозитория и не воспроизводима из кода. При этом compose сервиса публикует контейнерный порт напрямую на хост, а путь запуска через `NAVIGATOR_PORT` (`Program.cs:15-21`) поддерживает только cleartext HTTP/2 (h2c) — опции TLS в нём нет вообще (в отличие от ветки `SetRunningAddress` с `RunSettings:Tls`).
**Почему это проблема:** если nginx на хосте не настроен или порт доступен в обход него (а он опубликован), весь трафик — включая `x-auth-token` аутентифицированных вызовов — идёт открытым текстом; конфигурация прокси не под контролем версий и теряется при переезде хоста.
**Рекомендация:** добавить `Backend/nginx/navigator.conf` (grpc_pass + TLS) в репозиторий, в compose привязать публикацию порта к loopback (`127.0.0.1:${NAVIGATOR_PORT}:${NAVIGATOR_PORT}`) или убрать её, оставив доступ только через nginx.

### D2. Захардкоженный внутренний IP Configuration-сервиса в compose — и он не используется — Medium

**Файл:** `Backend/BarkFluff.Navigator/docker-compose-master.yml:2`, `Backend/BarkFluff.Navigator/docker-compose-dev.yml:2`
**Проблема:** `CONFIGURATION_SERVICE_URL: "http://192.168.1.177:7003"` захардкожен в обоих compose-файлах (в остальных сервисах это `${CONFIGURATION_SERVICE_URL}` из `.env`). При этом Navigator не вызывает `LoadConfiguration`, так что переменная мёртвая — но раскрывает внутренний LAN-адрес Configuration-сервиса в репозитории и вводит в заблуждение, будто конфигурация централизована (на деле действует локальный `appsettings.json` с секретом из S2).
**Почему это проблема:** утечка внутренней топологии (адрес сервиса, раздающего секреты всей платформы без аутентификации) + рассинхронизация с реальным источником конфигурации.
**Рекомендация:** убрать хардкод (заменить на `${CONFIGURATION_SERVICE_URL}`), а после подключения `LoadConfiguration` (см. S2) переменная станет рабочей.

### D3. Образ собран корректно (положительное наблюдение)

**Файл:** `Backend/BarkFluff.Navigator/Dockerfile:18-21`, `Dockerfile.slim`
Runtime-образ `aspnet:10.0-noble-chiseled`, файлы скопированы с `--chown=1654:1654`, процесс запускается под непривилегированным `USER $APP_UID`, multi-stage сборка с кэшем NuGet. Замечаний нет.
