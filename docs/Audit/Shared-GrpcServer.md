# Аудит: BarkFluff.GrpcServer + Shared-библиотеки
> Дата: 2026-06-12. Область: XAuth, interceptors, Serilog, Metrics, Shared/* (Auth, Identity, Exceptions, Queue, SecurityUtilities).

## Сводка

Инфраструктурный слой содержит несколько критических проблем, каждая из которых масштабируется на **все** микросервисы, потому что весь бэкенд использует одну и ту же библиотеку `BarkFluff.GrpcServer` для аутентификации (XAuth) и загрузки конфигурации. Самое серьёзное: сервис `Configuration` раздаёт все секреты платформы (включая общий ключ подписи JWT, пароли БД, ключи S3, пароль почты) **без какой-либо аутентификации и по открытому каналу** — а `LoadConfiguration` именно так их и забирает на старте. Модель JWT построена на **едином симметричном HMAC-ключе** для всех сервисов и на **бессрочных, неотзываемых сервисных токенах**, что превращает компрометацию любого одного сервиса в полную компрометацию платформы. Дополнительно: сервер доверяет клиентскому заголовку `x-ip-address` (спуфинг IP для аудита/геолокации) и протекает текст внутренних исключений клиенту.

| Критичность | Безопасность | Производительность |
|-------------|:------------:|:------------------:|
| Critical    | 3            | 0                  |
| High        | 2            | 0                  |
| Medium      | 2            | 1                  |
| Low         | 2            | 2                  |
| **Итого**   | **9**        | **3**              |

---

## Безопасность

### S1. Configuration-сервис отдаёт все секреты без аутентификации и без TLS — Critical
**Файл:** `Backend/BarkFluff.GrpcServer/WebApplicationBuilderExtensions.cs:61-93` (клиент `LoadConfiguration`), `Backend/BarkFluff.Configuration/Host/ConfigurationApiService.cs:30-55` (сервер, нет `[Authorize]`), `Backend/BarkFluff.Configuration/Program.cs:142-145` (нет `UseXAuth`/`UseAuthorization`, включён `MapGrpcReflectionService`).
**Проблема:** `LoadConfiguration` создаёт канал `GrpcChannel.ForAddress(configurationServiceAddress)` (по умолчанию `http://localhost:7003`, в docker — `http://...`) и вызывает `GetConfiguration` **без токена, без `JwtClientInterceptor`, без TLS**. На стороне сервера `ConfigurationApiService` не помечен `[Authorize]`, в `Program.cs` отсутствуют `AddXAuth`/`UseAuthentication`/`UseAuthorization`, а gRPC-reflection включён и в проде. В `docker-compose-master.yml:21-24` порт Configuration публикуется на хост (`ports: ["${CONFIGURATION_PORT}:${CONFIGURATION_PORT}"]`). Ответ `GetConfiguration` содержит секции `JwtSettings:SecretKey`, пароли БД, `Minio/S3 SecretKey`, `Email:SenderPassword` и т.д.
**Почему это проблема:** любой, кто имеет сетевой доступ к порту Configuration (а в master он проброшен на хост), одним неаутентифицированным gRPC-вызовом (reflection помогает перечислить методы) выкачивает **все секреты всей платформы**, включая ключ подписи JWT. Это разом обходит S2/S3 и даёт полную компрометацию. **Затрагивает все сервисы** — это единая точка раздачи секретов.
**Рекомендация:** закрыть `ConfigurationApiService` политикой `Service` (`[Authorize(Policy = nameof(TokenType.Service))]`) + `UseXAuth`; передавать сервисный токен из `LoadConfiguration` через `JwtClientInterceptor`; пускать трафик к Configuration по TLS (mTLS предпочтительно); не публиковать порт Configuration на хост; отключить gRPC-reflection в проде. Возникает курица-яйцо (токен лежит в Configuration) — решается bootstrap-секретом из переменной окружения/секрет-стора, а не из самой Configuration.

### S2. Единый симметричный HMAC-ключ подписи JWT на все сервисы — Critical
**Файл:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:25-26` (валидация), `Backend/BarkFluff.Identity/Services/JwtService.cs:50-51` (подпись `HmacSha256`), ключ генерируется единожды в `Backend/BarkFluff.Configuration/Infrastructure/ConfigurationDefaultsPopulator.cs:163-184` и раздаётся всем.
**Проблема:** все сервисы валидируют токены одним и тем же `JwtSettings:SecretKey` (симметричный `SymmetricSecurityKey`), и этот же ключ доступен каждому сервису (он его получает из Configuration для проверки подписи). Симметричный ключ = ключ проверки совпадает с ключом подписи.
**Почему это проблема:** любой сервис (и любой, кто прочитал ключ через S1) может **выпустить валидный токен любого типа от имени любого сервиса или пользователя** — нет криптографического разделения доверия между сервисами. Компрометация одного сервиса = возможность подделать Identity, выписать себе `Service`-токен и ходить в любой эндпоинт. **Затрагивает все сервисы.**
**Рекомендация:** перейти на асимметричную подпись (RS256/ES256): Identity подписывает приватным ключом, остальные сервисы валидируют только публичным. Тогда утечка ключа проверки у рядового сервиса не даёт права подписи. Как минимум — пометить, что ключ проверки не должен совпадать с ключом выпуска.

### S3. Сервисный токен бессрочный и не подлежит отзыву — Critical
**Файл:** `Backend/BarkFluff.Identity/Services/JwtService.cs:41` (`Expires = new DateTime(9999,12,31...)`), `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:46-70` (отзыв проверяется только для `TokenType.User`).
**Проблема:** `GenerateServerToken` ставит срок действия 9999 год (фактически вечный). `OnTokenValidated` сверяется с `TokenRevocationCache` только если `tokenType == TokenType.User`; для `Service`-токенов отзыв вообще не проверяется. Кэш отзыва (`TokenRevocationCache`) хранит лишь пары user/device.
**Почему это проблема:** утёкший сервисный токен (а он передаётся в plaintext, см. S7, и лежит в конфиге как `{Service}:Token`) действителен **навсегда** и **не может быть отозван** никаким механизмом. С учётом того, что `Service`-токен проходит и `User`-, и `Service`-политику (S9), один такой токен — это вечный универсальный ключ ко всему API. **Затрагивает все сервисы.**
**Рекомендация:** задать конечный TTL сервисным токенам и ротацию; либо ввести список разрешённых/отозванных сервисных токенов (jti + denylist), проверяемый в `OnTokenValidated` независимо от типа токена.

### S4. Спуфинг IP-адреса через клиентский заголовок `x-ip-address` — High
**Файл:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs:68-89` (метод `ResolveIpAddress`).
**Проблема:** IP клиента определяется в порядке приоритета, где **первым** источником стоит метаданное поле `x-ip-address`, которое целиком формируется клиентом в `XIpClientInterceptor` (`Shared/BarkFluff.Shared.Auth/XIpClientInterceptor.cs`). Затем — `X-Forwarded-For`/`X-Real-IP` без проверки доверенного прокси (нет `ForwardedHeaders`/`KnownProxies`), и только в самом конце реальный IP TCP-соединения.
**Почему это проблема:** `RequestContext.IpAddress` используется для аудита логинов и геолокации. Клиент может подставить **любой** IP, сфальсифицировав журнал входов, обойдя гео-ограничения и отравив audit trail. Реальный сетевой IP при этом полностью игнорируется. **Затрагивает все сервисы**, использующие `RequestContext`/аудит.
**Рекомендация:** не доверять `x-ip-address` от клиента для аудита/безопасности — использовать `RemoteIpAddress`, а `X-Forwarded-For`/`X-Real-IP` принимать только от явно сконфигурированного списка доверенных прокси (`ForwardedHeadersOptions.KnownProxies/KnownNetworks`). Клиентский IP, если нужен, хранить отдельно как «заявленный, недоверенный».

### S5. Текст внутреннего исключения утекает клиенту через gRPC Status — High
**Файл:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs:79`.
**Проблема:** в общем `catch (Exception ex)` сервер бросает `new RpcException(new Status(StatusCode.Unknown, ex.Message), trailers)` — сырой `ex.Message` непредвиденного исключения уходит клиенту в детали статуса. Хотя trailer `x-error-code` обезличен (`BaseGrpcException`), сообщение статуса — нет.
**Почему это проблема:** в `ex.Message` попадают внутренние детали (ошибки БД/Npgsql, пути, текст null-reference, фрагменты запросов), что облегчает разведку и эксплуатацию. **Затрагивает все сервисы** через общий перехватчик.
**Рекомендация:** в общем catch возвращать клиенту обобщённое сообщение (например, `baseException.ErrorMessage`), а полный `ex.Message`/stack писать только в лог (что уже делается на строке 66).

### S6. Невалидный Base64 в метаданных роняет каждый запрос (FormatException) — Medium
**Файл:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs:112-121` (`GetMetadataValue` → `Convert.FromBase64String` без обработки).
**Проблема:** значения заголовков `x-device-name`/`x-os-name`/`x-app-*`/`x-device-id`/`x-ip-address` декодируются `Convert.FromBase64String(base64)` без try/catch. Любой клиент, приславший не-Base64 в любом из этих заголовков, вызывает `FormatException` ещё до обработчика. Поскольку `RequestContextInterceptor` обёрнут `ServerExceptionInterceptor`, исключение попадает в общий catch (S5) и возвращается как `StatusCode.Unknown` с сообщением «The input is not a valid Base-64 string…».
**Почему это проблема:** тривиально невалидируемый клиентский ввод гарантированно проваливает запрос и (через S5) подтверждает клиенту внутреннюю механику; на каждый такой запрос — бросок/перехват исключения. Лёгкий вектор массового отказа в обслуживании дешёвыми запросами. **Затрагивает все сервисы** с `RequestContextInterceptor`.
**Рекомендация:** обернуть декодирование в безопасный разбор (`Convert.TryFromBase64String`/try-catch) и при ошибке возвращать `null`, не роняя запрос.

### S7. Межсервисный трафик и секреты в открытом виде (h2c, без TLS) — High
**Файл:** `Backend/BarkFluff.GrpcServer/WebApplicationBuilderExtensions.cs:34-56` (TLS только если задан `runSettings.Tls`, иначе чистый HTTP/2), `:69` (`GrpcChannel.ForAddress` по `http://...`); сервисные токены передаются через `Shared/BarkFluff.Shared.Auth/JwtClientInterceptor.cs:59` в заголовке `x-auth-token`.
**Проблема:** внутри docker-сети сервисы общаются по чистому HTTP/2 (TLS включается только при наличии `Tls`-секции, которой по умолчанию нет). По этому же каналу в каждом запросе летят `x-auth-token` (в т.ч. вечные сервисные токены) и ответ Configuration со всеми секретами (S1).
**Почему это проблема:** любой, кто получил доступ к сети контейнеров (скомпрометированный sidecar, mirror порта, неверная сетевая политика), пассивно перехватывает сервисные токены и секреты. С учётом S2/S3 один перехваченный токен — это бессрочный полный доступ. **Затрагивает все сервисы.**
**Рекомендация:** включить TLS (желательно mTLS) для межсервисного gRPC; не полагаться на «доверенную» сеть Docker для передачи долгоживущих секретов.

### S8. Алгоритм подписи не зафиксирован (`ValidAlgorithms`) — Low
**Файл:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:23-33`.
**Проблема:** в `TokenValidationParameters` не задан `ValidAlgorithms`, ограничивающий допустимые алгоритмы строго `HS256`. `alg: none` отклоняется (по умолчанию `RequireSignedTokens = true` и задан ключ), а RS→HS confusion здесь не применим (асимметричных ключей нет), поэтому риск низкий, но защита «в глубину» отсутствует.
**Почему это проблема:** при будущем добавлении асимметричных ключей отсутствие пиннинга алгоритма открывает классическую атаку подмены алгоритма. Сейчас — потенциальная, не активная уязвимость.
**Рекомендация:** явно задать `ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 }` (а при переходе на RS256 — соответствующий асимметричный набор).

### S9. Политика `User` принимает `Service`-claim; типы токенов на «магических строках» — Low
**Файл:** `Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs:74-81`.
**Проблема:** политика `User` объявлена как `RequireClaim(IdentityClaims.TokenType, "User", "Service")` — то есть `Service`-токен проходит и пользовательские эндпоинты. Значения захардкожены строками `"Service"`/`"User"` вместо `nameof(TokenType.Service)`, что хрупко при рефакторинге enum.
**Почему это проблема:** это «by design» (сервисы действуют от имени системы), но в связке с S3 это означает, что **вечный неотзываемый** сервисный токен — универсальный ключ и к пользовательским, и к сервисным методам, что усиливает последствия S2/S3. Рассинхрон строк и enum может тихо сломать авторизацию.
**Рекомендация:** использовать `nameof(TokenType.*)` вместо литералов; пересмотреть, действительно ли сервисный токен должен проходить пользовательские политики, или ввести явный «impersonation»-механизм с ограничением.

---

## Производительность

### P1. Аллокации и линейный поиск метаданных на каждый запрос — Medium
**Файл:** `Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs:33-43, 112-121`.
**Проблема:** на каждый gRPC-вызов `GetMetadataValue` вызывается 6 раз, и каждый раз делает `metadata.FirstOrDefault(m => m.Key.Equals(key, OrdinalIgnoreCase))` — линейный проход по всем заголовкам с аллокацией замыкания (захват `key`) и LINQ-итератора. Дополнительно на каждый присутствующий заголовок — `Convert.FromBase64String` + `Encoding.UTF8.GetString` (две аллокации). Это горячий путь, общий для всех запросов всех сервисов.
**Почему это проблема:** на высоконагруженных сервисах это лишние сотни тысяч короткоживущих аллокаций/сек и нагрузка на GC без функциональной необходимости.
**Рекомендация:** один проход по `context.RequestHeaders` со `switch` по ключу вместо 6 линейных поисков; избегать замыкания (вынести компаратор/использовать индексацию `Metadata`); рассмотреть кэш декодированных значений.

### P2. `ExceptionClientInterceptor.CachedExceptions` — публичное изменяемое статическое поле без синхронизации — Low
**Файл:** `Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs:11, 36-39, 53-63`.
**Проблема:** `public static List<BaseGrpcException> CachedExceptions;` инициализируется лениво без блокировки. При первом всплеске конкурентных ошибок несколько потоков одновременно увидят `null` и параллельно выполнят `LoadExceptions()` — рефлексивное сканирование всей сборки (`Assembly.GetExecutingAssembly().GetTypes()` + `Activator.CreateInstance` для каждого подтипа). Поле публичное и мутабельное — может быть перезаписано извне.
**Почему это проблема:** гонка на старте под нагрузкой даёт дублирующее дорогое рефлексивное сканирование; публичная мутабельность — риск целостности.
**Рекомендация:** сделать поле приватным `static readonly Lazy<List<BaseGrpcException>>` (потокобезопасная одноразовая инициализация) или инициализировать в статическом конструкторе.

### P3. Аллокация `Stopwatch` на каждый вызов — Low
**Файл:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs:32` (и аналогично в `Configuration/Host/ConfigurationApiService.cs` в каждом методе).
**Проблема:** `Stopwatch.StartNew()` создаёт объект на каждый gRPC-вызов исключительно ради замера длительности.
**Почему это проблема:** на горячем пути это лишняя heap-аллокация на каждый запрос; для простого замера достаточно `Stopwatch.GetTimestamp()` (структурный, без аллокаций).
**Рекомендация:** заменить на пару `var ts = Stopwatch.GetTimestamp(); ... Stopwatch.GetElapsedTime(ts)`.

---

## Примечания (вне находок)

- **Положительное:** `ClockSkew = TimeSpan.Zero`, `ValidateLifetime/Issuer/Audience/IssuerSigningKey = true` — строгая валидация JWT; `alg: none` отклоняется. Serilog настроен корректно (Seq — асинхронный батч-синк с буфер-файлом; Console в проде ограничен Warning — горячий путь не блокируется). `MetricsCollector` использует `ConcurrentDictionary` без явных локов (lock-конкуренции нет). Ключ JWT генерируется криптостойко (`RandomNumberGenerator`, 64 символа) в `ConfigurationDefaultsPopulator`.
- **SecurityUtilities** (`Shared/BarkFluff.Shared.SecurityUtilities/SecurityUtilities.cs`) содержит только оценку сложности пароля — никакого хеширования, RNG или сравнения секретов здесь нет, поэтому timing-проблем в этом файле нет (название библиотеки шире её содержимого).
- **Shared.Queue** — простые DTO событий (например, `SessionRevokedEvent`), без логики; проблем не выявлено. Стоит учесть, что отзыв сессии (`SessionRevokedConsumer`) работает только если событие доезжает до каждого инстанса сервиса (fanout); при горизонтальном масштабировании кэш отзыва у каждой реплики свой — это функциональный нюанс, не уязвимость данной библиотеки.
