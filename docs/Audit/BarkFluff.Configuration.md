# Аудит: BarkFluff.Configuration
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

BarkFluff.Configuration — централизованное хранилище конфигурации и секретов всей платформы (JWT-ключ подписи, пароли БД, межсервисные токены, ключи S3, креды RabbitMQ). Главная проблема: gRPC-сервис **не имеет никакой аутентификации/авторизации** — ни `AddXAuth`/`UseXAuth`, ни атрибутов `[Authorize]`. Любой, кто доберётся до порта, анонимно читает и перезаписывает все секреты; при этом `GetConfiguration` на любой запрос дополнительно возвращает конфигурацию `ServiceId.Unknown` (0), где лежит глобальный `JwtSettings:SecretKey`. В master-compose порт сервиса публикуется на хост без nginx/TLS, что превращает это в удалённо эксплуатируемую компрометацию всей системы. SQL-инъекций не обнаружено (EF Core параметризует запросы, raw-SQL в миграциях — только статические литералы). Производительность некритична: сервис низконагруженный, клиенты читают конфиг один раз при старте, используется `AsNoTracking`.

| Критичность | Кол-во |
| ----------- | ------ |
| Critical    | 4      |
| High        | 4      |
| Medium      | 4      |
| Low         | 4      |

## Безопасность

### S1. ~~Полное отсутствие аутентификации — анонимное чтение всех секретов платформы~~ — ~~Critical~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Program.cs:40-45,145`, `Backend/BarkFluff.Configuration/Host/ConfigurationApiService.cs:30`, `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationStorage.cs:20-28`
**Проблема:** `Program.cs` регистрирует gRPC только с `ServerExceptionInterceptor` (строки 40-43) и не вызывает `AddXAuth()`/`UseXAuth()`. На `ConfigurationApiService` и его методах нет ни одного `[Authorize]`. Метод `GetConfiguration` (ConfigurationApiService.cs:30) доступен анонимно. При этом `ConfigurationStorage.GetConfiguration` (строки 22-25) выбирает записи `x.ServiceId == serviceId || x.ServiceId == ServiceId.Unknown`, а под `ServiceId.Unknown` (0) в сид-миграции `20251123000000_SeedInitialConfigurationKeys.cs` лежат `JwtSettings:SecretKey`, `RabbitMQ:Password`, токены `UsersService`/`FilesService` и т.д.
**Почему это проблема:** Один анонимный вызов `GetConfiguration` с любым `ServiceId` возвращает глобальный JWT-ключ подписи. С этим ключом атакующий выпускает себе сервисные токены (`TokenType.Service`) и получает полный доступ ко всем микросервисам — это компрометация всей платформы. Также утекают пароли БД, ключи S3, креды брокера.
**Рекомендация:** Включить `AddXAuth`/`UseXAuth` в `Program.cs`, навесить `[Authorize(Policy = nameof(TokenType.Service))]` на `ConfigurationApiService`, выдать каждому сервису собственный сервисный токен и пускать только аутентифицированные вызовы.

### S2. ~~Клиент произвольно выбирает `ServiceId` — сервис A читает конфиг сервиса B~~ — ~~Critical~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Host/ConfigurationApiService.cs:30-40`, `Backend/BarkFluff.Configuration/Features/GetConfiguration/GetConfigurationCommand.cs:10`
**Проблема:** `ServiceId` берётся прямо из `request.ServiceId` (ConfigurationApiService.cs:39) и не сверяется ни с какой аутентифицированной идентичностью вызывающего. Нет привязки «токен сервиса X → может читать только конфиг X».
**Почему это проблема:** Даже если добавить аутентификацию по S1, без привязки `ServiceId` к идентичности любой сервис (или утёкший сервисный токен) сможет вытянуть секреты любого другого сервиса. Нарушается изоляция секретов между сервисами.
**Рекомендация:** Определять `ServiceId` из claim'ов токена вызывающего, а не из тела запроса; запросы на чужой `ServiceId` отклонять (общие записи `ServiceId.Unknown` отдавать только в составе собственного конфига).

### S3. ~~`UpdateConfiguration` без аутентификации — анонимная запись любых секретов~~ — ~~Critical~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Host/ConfigurationApiService.cs:57-72`, `Backend/BarkFluff.Configuration/Features/UpdateConfiguration/UpdateConfigurationCommandHandler.cs:20-71`
**Проблема:** `UpdateConfiguration` доступен анонимно (нет `[Authorize]`). Валидируется только корректность `ServiceId` (handler:22), но не личность вызывающего. Запись идёт в БД через `ConfigurationStorage.UpdateConfigurationAsync`.
**Почему это проблема:** Атакующий может перезаписать `JwtSettings:SecretKey` (сломав/перехватив всю аутентификацию), подменить `Host` любого сервиса на адрес под своим контролем (MITM межсервисного трафика) или подменить ключи S3/креды БД. Запись секретов должна быть доступна только админке.
**Рекомендация:** Требовать `[Authorize(Policy = nameof(TokenType.Service))]` (или отдельную admin-политику) на запись; рассмотреть ограничение источника (только AdminPanel).

### S4. ~~Сервис доступен напрямую с хоста в master без auth и без TLS~~ — ~~Critical~~ **Неактуально**
**Файл:** `Backend/docker-compose-master.yml:21-35` (строка 24: `ports: ["${CONFIGURATION_PORT}:${CONFIGURATION_PORT}"]`)
**Проблема:** В прод-compose порт Configuration публикуется на хост. nginx-конфига для Configuration нет (сервис задуман как внутренний — в `Backend/nginx/` отсутствует `configuration.conf`), то есть публикуется «голый» h2c gRPC без TLS и без прокси. В dev-compose (`docker-compose-dev.yml:22-36`) порт корректно не публикуется — сервис только в `barkfluff-network`.
**Почему это проблема:** В сочетании с S1/S3 любой, кто достанет до опубликованного порта хоста (мисконфигурация фаервола, доступ из соседней сети), получает анонимный доступ на чтение и запись всего хранилища секретов по незашифрованному каналу.
**Рекомендация:** Убрать публикацию порта Configuration на хост в master-compose; оставить сервис только во внутренней docker-сети, как в dev. Если внешний доступ нужен для админки — заводить через nginx с TLS и обязательной аутентификацией.

### S5. ~~Управляющий аудит-след (`EditedBy`/`EditedFrom`) задаётся клиентом~~ — ~~High~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Host/ConfigurationApiService.cs:70-71`, `Backend/BarkFluff.Configuration/Features/UpdateConfiguration/UpdateConfigurationCommand.cs:13-14`, `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationStorage.cs:39-40,50-51`
**Проблема:** Поля `EditedBy` и `EditedFrom` принимаются из запроса (`request.EditedBy`, `request.EditedFrom`) и пишутся в БД как есть.
**Почему это проблема:** Журнал «кто и откуда менял конфиг» полностью подделываем вызывающим — атакующий может выдать свою запись за «AdminPanel»/«system». Аудит-след недостоверен.
**Рекомендация:** Заполнять `EditedBy`/`EditedFrom` на сервере из аутентифицированной идентичности (claim'ы токена, реальный IP), а не из тела запроса.

### S6. ~~Секреты хранятся в БД открытым текстом~~ — ~~High~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Domain/ConfigurationItem.cs:16`, `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:271,345-346`
**Проблема:** Значения (`Value`), включая `JwtSettings:SecretKey`, строки подключения с паролями БД (формируются в Populator:271), ключи S3 (345-346) и сервисные токены, хранятся в таблице `Configurations` в открытом виде, без шифрования на уровне приложения.
**Почему это проблема:** Компрометация БД Configuration (бэкап, SQL-доступ, дамп) = мгновенная компрометация всех секретов платформы. Нет defense-in-depth.
**Рекомендация:** Шифровать чувствительные значения at-rest (например, через провайдер ключей/Data Protection с мастер-ключом из окружения) или вынести секреты в выделенный secret-manager; как минимум разграничить доступ к БД и шифровать бэкапы.

### S7. ~~Хардкод дефолтных кредов в Populator (minio/guest)~~ — ~~Medium~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:345-346,246-249`, `Backend/BarkFluff.Configuration/Program.cs:105-106`
**Проблема:** Авто-заполнение пустых конфигов подставляет `AccessKey="minioadmin"`/`SecretKey="minioadmin"` (Populator:345-346) и `guest/guest` как фолбэк кредов RabbitMQ (Program.cs:105-106). Populator запускается при каждом старте Configuration и заполняет любые пустые значения.
**Почему это проблема:** Если в проде какая-то запись секрета осталась пустой, она будет автоматически заполнена общеизвестными дефолтными кредами, что открывает доступ к MinIO/брокеру. Дефолты dev-окружения протекают в прод-БД.
**Рекомендация:** Не подставлять дефолтные секреты автоматически в неразработческих окружениях; при пустом секрете в проде — фейлить старт или генерировать случайное значение, а не использовать `minioadmin`/`guest`.

### S8. ~~Сервисные токены живут 10 лет и неотзываемы~~ — ~~Medium~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:356-377` (строка 372: `expires: DateTime.UtcNow.AddYears(10)`)
**Проблема:** `GenerateServiceToken` выпускает JWT `TokenType.Service` со сроком 10 лет, без механизма отзыва (revocation проверяется только для `TokenType.User`, см. `XAuthExtensions.cs:51-66`).
**Почему это проблема:** Утечка любого сервисного токена даёт атакующему валидный доступ к платформе на десятилетие; ротация требует ручной перегенерации и рассинхронизации сервисов.
**Рекомендация:** Сократить срок жизни сервисных токенов, реализовать ротацию и отзыв (например, по `jti`/версии ключа).

### S9. ~~Строки подключения с паролем БД могут попадать в логи (Seq)~~ — ~~Medium~~ **Исправлено (2026-06-23)**
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:151-153,394-400`
**Проблема:** При авто-заполнении значение логируется (`LogDebug` строка 151), маскируется только если `IsSensitive` вернёт true. `IsSensitive` (394-400) считает чувствительными лишь `Key in (SecretKey, Password, Token)` или `Section`, содержащий эти слова. Строки подключения хранятся в секциях `UsersDb`/`IdentityDb`/`FilesDb`/`MessagesDb` с пустым `Key`, поэтому `IsSensitive` их **не** ловит, и полная строка `Host=...;Password=...` уходит в лог.
**Почему это проблема:** Пароль БД утекает в Seq/файловые логи при включённом уровне Debug. Снижается уровень защиты секретов.
**Рекомендация:** Расширить `IsSensitive` (ловить значения, содержащие `Password=`/`SecretKey=`, и секции `*Db`), либо не логировать значения вовсе. Сейчас уровень `Default: Information` (appsettings.json) спасает по умолчанию, но любой переход на Debug раскроет секреты.

### S10. ~~`UpdateConfiguration` возвращает `ex.Message` клиенту~~ — ~~Low~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Features/UpdateConfiguration/UpdateConfigurationCommandHandler.cs:69`, аналогично в reserved-names обработчиках (`AddReservedNameCommandHandler.cs:39`, `DeleteReservedNameCommandHandler.cs:39`, `UpdateReservedNameCommandHandler.cs:39`)
**Проблема:** Текст внутреннего исключения возвращается в `Message` ответа.
**Почему это проблема:** Раскрытие внутренних деталей (ошибки БД, имена сущностей) вызывающему; в сочетании с анонимным доступом усиливает разведку.
**Рекомендация:** Возвращать обобщённое сообщение, детали писать только в лог.

### S11. ~~Несоответствие кодировок ключа: ASCII при выпуске токена vs UTF8 при валидации~~ — ~~Low~~ **Исправлено (2026-06-23)**
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:358` (`Encoding.ASCII.GetBytes`) против `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:26` (`Encoding.UTF8.GetBytes`)
**Проблема:** Сервисные токены подписываются ключом, преобразованным через `Encoding.ASCII`, а все сервисы валидируют тем же ключом через `Encoding.UTF8`. Для авто-сгенерированного ключа (charset ASCII в `GenerateRandomKey`, строка 384) байты совпадают, поэтому сейчас работает. Но если `SecretKey` зададут вручную с не-ASCII символом, ASCII-кодирование исказит байты ключа.
**Почему это проблема:** Скрытая хрупкость: смена ключа на не-ASCII строку сломает всю межсервисную аутентификацию (токены подписаны иначе, чем валидируются).
**Рекомендация:** Привести к единой кодировке (`UTF8`) в `GenerateServiceToken`.

### S12. Автозаполнение использует публичные LiveKit API-учётные данные — High
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:355-364`, `Backend/BarkFluff.Configuration/Persistence/Migrations/20260622000000_AddCallsConfiguration.cs:34-37`, `Backend/livekit/livekit.yaml:1-17`, `Backend/BarkFluff.Calls/Services/LiveKitTokenService.cs:27-41`, `Backend/BarkFluff.Calls/Program.cs:52-56`
**Проблема:** Миграция Calls создаёт пустые записи `LiveKit:Url`/`ApiKey`/`ApiSecret`, после чего `ConfigurationDefaultsPopulator` без проверки окружения записывает `ApiKey = "devkey"` и публично известный `ApiSecret = "devsecret_change_me_in_production_0123456789"`. Эта же пара закоммичена в `livekit.yaml`; ограничений, запрещающих такое автозаполнение в Production, нет.
**Почему это проблема:** Любой, кто знает репозиторий, получает секрет подписи LiveKit. `LiveKitTokenService` использует его для выпуска токенов с правом входа в комнату, публикации и подписки на медиа; атакующий может самостоятельно подписывать LiveKit JWT с произвольными identity/room/grants. Тем же ключом инициализируется `WebhookReceiver`, поэтому компрометируется и доверие к webhook-событиям LiveKit. При пустой или ошибочно очищенной production-конфигурации это становится рабочей компрометацией звонков.
**Рекомендация:** Не задавать LiveKit API-secret дефолтным значением вне Development. Требовать production-пару из secret-manager/переменных окружения и аварийно завершать старт при её отсутствии; удалить/ротировать опубликованную пару ключей.

> Примечание (Low, не выделяю в отдельный пункт): `GenerateRandomKey` (Populator:382-392) берёт `b % chars.Length` (68 символов), что даёт лёгкое смещение распределения (256 % 68 ≠ 0). На длине 64 символа энтропии достаточно, но для секретного ключа корректнее использовать `RandomNumberGenerator.GetString`/rejection sampling или base64url.

## Производительность

### P1. ~~Нет кэширования чтений конфигурации — запрос к БД на каждый `GetConfiguration`~~ — ~~Low~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationStorage.cs:20-28`
**Проблема:** Каждый вызов `GetConfiguration` идёт в БД. Кэша нет.
**Почему это проблема:** Потенциальная нагрузка на БД, если бы вызовы были частыми.
**Рекомендация:** Не критично: клиенты (`WebApplicationBuilderExtensions.LoadConfiguration`) тянут конфиг один раз при старте, используется `AsNoTracking()` (хорошо). Кэш можно добавить при появлении горячего пути, сейчас — необязательно.

### P2. ~~`CountAsync` после каждой записи конфигурации~~ — ~~Low~~ **Неактуально**
**Файл:** `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationStorage.cs:62-63`
**Проблема:** После каждого `UpdateConfigurationAsync` выполняется отдельный `CountAsync` для обновления gauge-метрики.
**Почему это проблема:** Лишний запрос к БД на запись.
**Рекомендация:** Незначительно — записи редкие (только админ). Можно оставить либо считать дельту инкрементально.

## Docker / nginx

### D1. ~~Публикация порта Configuration на хост в master — см. S4~~ — ~~Critical~~ **Неактуально**
**Файл:** `Backend/docker-compose-master.yml:24`
Дублируется как ключевая инфраструктурная находка: внутренний сервис-хранилище секретов не должен публиковаться на хост. В dev (`docker-compose-dev.yml:22-36`) сделано правильно — без `ports`.

### D2. ~~`env_file: .env` пробрасывает широкий набор кредов в контейнер Configuration~~ — ~~Low~~ **Неактуально**
**Файл:** `Backend/docker-compose-dev.yml:26-27`, `Backend/docker-compose-master.yml:25-26`
**Проблема:** В контейнер Configuration целиком загружается `.env` (нужно для проброса `RABBITMQ_DEFAULT_USER/PASS` в Populator).
**Почему это проблема:** В окружении процесса оказывается больше секретов, чем требуется сервису; расширяется поверхность утечки (через дамп env, логи краша).
**Рекомендация:** Пробрасывать только реально нужные переменные явным списком `environment`, а не весь `.env`.

### D3. Dockerfile — замечаний по безопасности нет — (информационно)
**Файл:** `Backend/BarkFluff.Configuration/Dockerfile:18-22`, `Dockerfile.slim:1-5`
Используется `chiseled`-образ и непривилегированный `USER $APP_UID` (uid 1654) — хорошо. Отдельных проблем нет.
