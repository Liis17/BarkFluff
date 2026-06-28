# Аудит: BarkFluff.Identity
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Сервис аутентификации в целом использует корректные криптопримитивы: пароли — BCrypt (work factor 12) со сравнением через `BCrypt.Verify`/`FixedTimeEquals` для legacy-хешей; OTP-коды и refresh-токены генерируются криптостойким `RandomNumberGenerator`; JWT валидируется полно (issuer/audience/lifetime/signing key, `ClockSkew = 0`); SQL-инъекций нет (везде параметризованный EF Core LINQ); пароли/токены/OTP-коды не логируются.

Однако главная системная проблема — **полное отсутствие rate limiting и lockout** как на уровне приложения, так и на уровне nginx. Это превращает все короткие 6-значные коды (вход по email-OTP, сброс пароля, подтверждение регистрации) в перебираемые. Усугубляют ситуацию: коды email-OTP входа без TTL, отправка OTP-письма и обращения к стороннему гео-API ещё до проверки пароля (пре-аутентификационная амплификация и user enumeration), смена/сброс пароля без отзыва остальных сессий, refresh-токены со сроком ~27 лет и сервисные токены с истечением в 9999 году. По производительности — отсутствуют индексы по `UserId`/`DeviceId` (полный скан на каждом логине) и блокирующий внешний HTTP-вызов гео-API без таймаута на горячем пути.

| Критичность | Безопасность | Производительность | Docker/nginx |
|-------------|--------------|--------------------|--------------|
| Critical    | 1            | 0                  | 0            |
| High        | 6            | 2                  | 1            |
| Medium      | 4            | 2                  | 1            |
| Low         | 4            | 1                  | 0            |
| **Итого**   | **15**       | **5**              | **2**        |

Положительные моменты (не находки): BCrypt с work factor 12 (`PasswordHasher.cs:8`), `CryptographicOperations.FixedTimeEquals` для legacy-хеша (`PasswordHasher.cs:25`), `RandomNumberGenerator.GetInt32`/`GetBytes` для кодов и токенов (`CodeGenerator.cs:16`, `RefreshTokenGenerator.cs:10`), полная валидация JWT в XAuth (`XAuthExtensions.cs:23-33`), анти-энумерация для несуществующего пользователя в сбросе пароля (`ResetPasswordCommandHandler.cs:99-101`), запуск контейнера под non-root `USER $APP_UID` (`Dockerfile:22`).

---

## Безопасность

### S1. Отсутствие rate limiting и lockout на входе и проверке OTP — High
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:30` (весь обработчик), `Backend/BarkFluff.Identity/Host/IdentityApiService.cs:43`
**Проблема:** Ни один эндпоинт (`Auth`, `ConfirmResetPassword`, `ConfirmAccount`, `ConfirmOtpVerification`) не ведёт счётчик неудачных попыток и не реализует временную блокировку. На уровне nginx (`identity.conf`) тоже нет `limit_req`. Неверный пароль/код просто инкрементирует метрику и бросает исключение.
**Почему это проблема:** Это базовый фундамент под все остальные перебор-атаки (S2, S3, S5). Пароль и любые 6-значные коды можно перебирать с неограниченной скоростью. Для auth-сервиса это критичный пробел.
**Рекомендация:** Ввести лимит попыток на пару (логин+IP) и (userId+тип кода) с экспоненциальной задержкой/блокировкой; добавить `limit_req` в nginx для эндпоинта Identity. Хранить счётчик и метку блокировки (например, в Redis или в `AuthUserProperty`).

### S2. Брутфорс кода сброса пароля по email → захват аккаунта — Critical
**Файл:** `Backend/BarkFluff.Identity/Features/ResetPassword/ResetPasswordCommandHandler.cs:152` (генерация 6-значного кода), `Backend/BarkFluff.Identity/Features/ConfirmResetPassword/ConfirmResetPasswordCommandHandler.cs:137` (сравнение кода)
**Проблема:** Сброс пароля по email создаёт 6-значный код (1 000 000 комбинаций), действующий 5 минут (`ResetPasswordCommandHandler.cs:159`). `ConfirmResetPassword` сравнивает код без какого-либо ограничения числа попыток (S1). При успехе обработчик **сразу выдаёт access+refresh токены и обнуляет хеш пароля** (`ConfirmResetPasswordCommandHandler.cs:158-169`) — то есть достаточно знать логин/email жертвы и подобрать код, пароль при этом не требуется.
**Почему это проблема:** Без rate limiting код перебирается за время жизни (5 минут) при достаточной скорости запросов; результат — полный захват аккаунта (выданная сессия + сброшенный пароль). Это самый опасный путь в сервисе.
**Рекомендация:** Ограничить число попыток ввода кода (например, 5, затем инвалидация `ResetId`), удлинить код или использовать одноразовую ссылку с криптостойким токеном; обязательный rate limiting (S1).

### S3. Код email-OTP при входе не имеет TTL, регистронезависим, без лимита попыток → обход 2FA — High
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:181`, `Backend/BarkFluff.Identity/Domain/AuthUserProperty.cs:20`
**Проблема:** Код email-OTP хранится в `AuthUserProperty.LastEmailAuthCode` (поле `text`, без срока действия) и сравнивается через `string.Equals(..., StringComparison.InvariantCultureIgnoreCase)`. После генерации код остаётся валидным бессрочно, пока не будет перезаписан следующей попыткой входа. Лимита попыток нет (S1).
**Почему это проблема:** Второй фактор (email-OTP) фактически перебираем: 6 цифр, нет TTL, нет счётчика попыток. Это сводит на нет защиту 2FA по email (пароль ещё нужен, поэтому High, а не Critical). Регистронезависимое сравнение здесь не даёт выигрыша (код цифровой), но это анти-паттерн для сравнения секретов.
**Рекомендация:** Добавить срок действия кода (поле с `ExpiresAt`), инвалидировать после первой успешной/N неудачных проверок, сравнивать ordinal. Желательно сравнение в постоянном времени.

### S4. Отправка email-OTP и обращение к гео-API до проверки пароля → пре-аутентификационная амплификация / email-бомбинг — High
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:101-148`
**Проблема:** Если у пользователя включён email-OTP, обработчик генерирует код, перезаписывает `LastEmailAuthCode`, вызывает гео-API и **публикует письмо с кодом ещё до какой-либо проверки пароля** (проверка пароля — только на строке 200). Аутентификация для этого не требуется — достаточно знать логин/email жертвы.
**Почему это проблема:** Неаутентифицированный злоумышленник может бесконечно слать жертве письма с кодами (харассмент/«email-бомбинг»), нагружать SMTP-очередь и сторонний гео-сервис, а также непрерывно ротировать `LastEmailAuthCode`. Это и DoS-вектор, и усиление перебора.
**Рекомендация:** Отправлять email-OTP только после успешной проверки пароля (сначала пароль, затем второй фактор), плюс rate limiting на отправку писем по userId.

### S5. Перечисление пользователей (user enumeration) — Medium
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:101-148` и `Backend/BarkFluff.Identity/Features/ResetPassword/ResetPasswordCommandHandler.cs:107-114`
**Проблема:** В `Auth` для несуществующего пользователя бросается `InvalidLoginOrPasswordException` (хорошо), но для существующего пользователя с включённым OTP при отсутствии кода бросается `OtpCodeNeedException` ещё до проверки пароля. Разница в ответе позволяет отличить существующих пользователей (и узнать, что у них включён 2FA) без знания пароля. В `ResetPassword` есть анти-энумерация для несуществующего пользователя (фейковый `ResetId` + задержка), но для существующего пользователя без настроенного Authenticator при запросе `OtpType.Authenticator` бросается `OtpNotCreatedException` (`ResetPasswordCommandHandler.cs:113`) — снова отличимый ответ, что обходит защиту.
**Почему это проблема:** Перечисление логинов/email и факта наличия 2FA облегчает целевые атаки и фишинг.
**Рекомендация:** Возвращать одинаковый ответ/тайминг независимо от существования пользователя и состояния 2FA; не раскрывать `OtpCodeNeedException`/`OtpNotCreatedException` до проверки пароля.

### S6. Смена и сброс пароля не отзывают остальные сессии — High
**Файл:** `Backend/BarkFluff.Identity/Features/SetPassword/SetPasswordCommandHandler.cs:74-78`, `Backend/BarkFluff.Identity/Features/ConfirmResetPassword/ConfirmResetPasswordCommandHandler.cs:158-169`, `Backend/BarkFluff.Identity/Features/ForceSetPasswordServer/ForceSetPasswordServerCommandHandler.cs:36-37`
**Проблема:** При смене пароля пользователем (`SetPassword`), сбросе пароля (`ConfirmResetPassword`) и принудительной смене администратором (`ForceSetPasswordServer`) существующие refresh-токены не удаляются и не публикуется `SessionRevokedEvent`. Отзыв сессий реализован только в `RemoveActiveSession`/`Logout`.
**Почему это проблема:** Классический сценарий — аккаунт скомпрометирован, пользователь/админ меняет пароль, но сессии злоумышленника (refresh-токены с почти бесконечным сроком, см. S7) остаются валидными. Смена пароля не достигает цели «выгнать злоумышленника».
**Рекомендация:** После любой смены/сброса пароля удалять все refresh-токены пользователя (кроме, опционально, текущего устройства) и публиковать `SessionRevokedEvent` для инвалидации активных access-токенов.

### S7. Refresh-токены живут ~27 лет — High
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:28` (`ExpDaysRefreshToken = 9999`), также `Features/ConfirmAccount/ConfirmAccountCommandHandler.cs:27`, `Features/ConfirmResetPassword/ConfirmResetPasswordCommandHandler.cs:34`, `Features/CreateSessionForUserServer/CreateSessionForUserServerCommandHandler.cs:29`
**Проблема:** Все refresh-токены создаются со сроком 9999 дней (~27 лет). Срок не сокращается и не «скользит».
**Почему это проблема:** Украденный refresh-токен остаётся валидным практически вечно; единственный способ отзыва — ручное удаление сессии. Это многократно усиливает ущерб от утечки (вместе с S6).
**Рекомендация:** Задать разумный срок (например, 30–90 дней) со скользящим продлением при использовании; хранить срок в конфиге.

### S8. Сервисные токены не истекают (год 9999) — High
**Файл:** `Backend/BarkFluff.Identity/Services/JwtService.cs:41`
**Проблема:** `GenerateServerToken` выставляет `Expires = new DateTime(9999, 12, 31, 23, 59, 59)` — токен практически бессрочен. Эти токены передаются сервисам как обычные переменные окружения (`IDENTITY_SERVICE_TOKEN` и т.п.) и дают политику `Service`, которая (в XAuth) проходит и под пользовательские эндпоинты (`XAuthExtensions.cs:80`).
**Почему это проблема:** Утечка сервисного токена даёт постоянный доступ уровня сервиса без возможности ротации иначе как сменой общего ключа подписи. Отсутствует механизм истечения/ротации.
**Рекомендация:** Выдавать сервисным токенам конечный срок и предусмотреть ротацию; рассмотреть отдельный ключ/механизм для service-to-service вместо «вечного» JWT.

### S9. Брутфорс кода подтверждения регистрации (окно 6 часов) → захват черновика аккаунта — Medium
**Файл:** `Backend/BarkFluff.Identity/Features/CreateAccount/CreateAccountCommandHandler.cs:77,83`, `Backend/BarkFluff.Identity/Features/ConfirmAccount/ConfirmAccountCommandHandler.cs:80`
**Проблема:** Код регистрации — 6 цифр, срок действия **6 часов** (`Expires = DateTime.UtcNow.AddHours(6)`), сравнение регистронезависимое, без лимита попыток (S1). Успешное подтверждение сразу выдаёт refresh-токен (авто-логин, `ConfirmAccountCommandHandler.cs:138-140`).
**Почему это проблема:** Очень широкое окно (6 ч) + 6 цифр + отсутствие лимита делают перебор кода реалистичным; результат — захват свежезарегистрированного (draft) аккаунта с авто-входом.
**Рекомендация:** Сократить срок действия (5–15 минут), ограничить число попыток, сравнивать ordinal.

### S10. TOTP-секреты и OTP/reset-коды хранятся в БД в открытом виде — Medium
**Файл:** `Backend/BarkFluff.Identity/Domain/AuthUserProperty.cs:16,20`, `Backend/BarkFluff.Identity/Domain/ResetPassword.cs:17`
**Проблема:** `OtpSecret` (TOTP-секрет), `LastEmailAuthCode` и `ResetPassword.OtpCode` хранятся как обычный `text` без шифрования на уровне приложения.
**Почему это проблема:** Компрометация БД (или дамп/бэкап) сразу раскрывает TOTP-секреты всех пользователей — злоумышленник сможет генерировать валидные коды 2FA. Это наиболее чувствительные секреты сервиса.
**Рекомендация:** Шифровать `OtpSecret` at-rest (например, провайдером шифрования EF Core / Data Protection / KMS); короткоживущие коды — хранить как хеш либо с TTL и одноразовостью.

### S11. CORS: `AllowAnyOrigin` + `AllowAnyHeader` + `AllowAnyMethod` на auth-сервисе — Medium
**Файл:** `Backend/BarkFluff.Identity/Program.cs:47-53`
**Проблема:** Политика `IdentityCors` разрешает запросы с любого origin с любыми заголовками. Эндпоинты — gRPC-Web (`MapGrpcService<IdentityApiService>().EnableGrpcWeb()`), включая логин/регистрацию/сброс пароля.
**Почему это проблема:** Любой сторонний сайт может из браузера вызывать auth-эндпоинты (логин, отправка OTP-писем — усиливает S4). Поскольку аутентификация на bearer-заголовках, классический CSRF ограничен, но это всё равно избыточно широкая поверхность.
**Рекомендация:** Ограничить origins списком доверенных клиентских доменов (`WithOrigins(...)`), сузить заголовки.

### S12. TOTP-коды допускают повтор в окне валидности (replay) — Medium
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:159`, `Backend/BarkFluff.Identity/Features/ConfirmResetPassword/ConfirmResetPasswordCommandHandler.cs:119`, `Features/ConfirmOtpVerification/ConfirmOtpVerificationCommandHandler.cs:74`, `Features/DisableOtpVerification/DisableOtpVerificationCommandHandler.cs:84`
**Проблема:** Везде вызывается `totp.VerifyTotp(code, out long timeStepMatched, ...)`, но `timeStepMatched` отбрасывается и нигде не сохраняется. Один и тот же TOTP-код принимается повторно в пределах окна (RFC delay).
**Почему это проблема:** Перехваченный (например, через фишинг/плечо) TOTP-код можно переиспользовать в течение окна. RFC 6238 рекомендует запрещать повторное использование уже принятого time-step.
**Рекомендация:** Сохранять последний принятый `timeStepMatched` для пользователя и отклонять коды с тем же или меньшим шагом.

### S13. Логирование IP-адресов пользователей (PII) — Low
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:91-95,206-211`, `Infrastructure/LocationClient.cs:48-54`
**Проблема:** IP-адреса и связка логин/устройство пишутся в логи на уровне Information/Warning. Пароли, токены и OTP-коды НЕ логируются (это хорошо), но IP — персональные данные.
**Почему это проблема:** Накопление PII в логах/Seq повышает требования к их защите и хранению.
**Рекомендация:** Маскировать/усечать IP в логах или ограничить уровень/срок хранения.

### S14. Гео-запрос идёт по HTTP (plaintext) к стороннему сервису с передачей IP пользователя — Low
**Файл:** `Backend/BarkFluff.Identity/Infrastructure/LocationClient.cs:9`
**Проблема:** `BaseUrl = "http://ip-api.com/json/"` — незашифрованный HTTP к внешнему сервису, в URL передаётся IP пользователя.
**Почему это проблема:** Утечка IP пользователей третьей стороне и MITM-возможность подменить строку локации (низкое влияние — только для отображения/писем), плюс зависимость auth-флоу от внешнего HTTP.
**Рекомендация:** Минимум — HTTPS; рассмотреть локальную GeoIP-базу (offline) вместо внешнего запроса на горячем пути (см. P2).

### S15. `Guid.Parse` без обработки некорректного ввода — Low
**Файл:** `Backend/BarkFluff.Identity/Host/IdentityApiService.cs:180` (ConfirmResetPassword), `Backend/BarkFluff.Identity/Features/ConfirmAccount/ConfirmAccountCommandHandler.cs:41`
**Проблема:** `Guid.Parse(request.ResetId)` / `Guid.Parse(request.CodeId)` бросают `FormatException` при некорректной строке, превращаясь в необработанную внутреннюю ошибку gRPC.
**Почему это проблема:** Невалидный ввод от клиента приводит к Internal-ошибке вместо контролируемого `InvalidArgument`; шумит в логах, потенциально раскрывает детали исключения.
**Рекомендация:** Использовать `Guid.TryParse` и возвращать корректный доменный/`InvalidArgument` ответ.

---

## Производительность

### P1. Отсутствуют индексы по `UserId` и `DeviceId` — полный скан на каждом логине — High
**Файл:** `Backend/BarkFluff.Identity/Persistence/Contexts/IdentityContext.cs:25-27` (единственный индекс — `RefreshTokens.Value`), подтверждено снапшотом `Persistence/Migrations/IdentityContextModelSnapshot.cs`
**Проблема:** Запросы выполняются по неиндексированным колонкам:
- `AuthUserProperties` — `WHERE UserId = ?` в каждом логине и операции 2FA (`AuthPropertiesStorage.cs:20,27,51,91,...`), индекса по `UserId` нет.
- `UserPasswords` — `WHERE UserId = ?` на каждом логине (`PasswordsStorage.cs:44`), индекса нет.
- `RefreshTokens` — `WHERE DeviceId = ? AND UserId = ?` (удаление старых токенов при каждом логине, `RefreshTokensStorage.cs:45,65`) и `WHERE UserId = ?` (`GetRefreshTokens`, `:40`), индексов по `UserId`/`DeviceId` нет.
**Почему это проблема:** По мере роста таблиц каждый логин выполняет несколько Seq Scan, что линейно деградирует с числом пользователей/токенов на самом нагруженном пути.
**Рекомендация:** Добавить индексы: `AuthUserProperties(UserId)`, `UserPasswords(UserId)` (можно UNIQUE — на пользователя одна запись), `RefreshTokens(UserId)` и `RefreshTokens(UserId, DeviceId)`.

### P2. Блокирующий внешний HTTP-вызов гео-API на горячем пути логина без таймаута — High
**Файл:** `Backend/BarkFluff.Identity/Infrastructure/LocationClient.cs:21-29`, использование в `Features/Auth/AuthCommandHandler.cs:120,216,262`
**Проблема:** `LocationClient.GetLocation` синхронно ожидает ответ `ip-api.com` (`await _httpClient.GetAsync(url)`), таймаут на `HttpClient` не задан (в `Program.cs` `AddHttpClient<LocationClient>` без `ConfigureHttpClient`/`Timeout`). На успешном логине гео-вызов делается минимум один раз, на неудачном пароле — ещё раз, плюс при email-OTP — ещё раз.
**Почему это проблема:** Медленный/недоступный внешний сервис напрямую тормозит (или подвешивает) завершение логина; внешняя зависимость на критическом пути. Бесплатный `ip-api.com` к тому же имеет собственный rate limit, что усугубит задержки под нагрузкой.
**Рекомендация:** Задать короткий таймаут и делать вызов неблокирующим/фоновым (локацию для письма можно считать вне критической секции); рассмотреть offline GeoIP-базу.

### P3. Множество последовательных gRPC/RMQ/HTTP-вызовов на пути логина — Medium
**Файл:** `Backend/BarkFluff.Identity/Features/Auth/AuthCommandHandler.cs:85,200,254-305`
**Проблема:** Успешный логин последовательно выполняет: `FindByLogin` (gRPC) → `GetUserAuthProperties` (БД) → `GetUserPasswordHash` (БД) → delete+create refresh-токенов (2× SaveChanges) → `CreateToken` (ещё одно чтение токена из БД) → `GetLocationString` (HTTP) → `RegisterDevice` (gRPC) → `GetUserContacts` (gRPC) → публикация письма (RMQ). Всё строго последовательно.
**Почему это проблема:** Латентность логина — сумма всех внешних задержек; часть вызовов (гео, контакты, регистрация устройства, письмо) не нужны для выдачи токена и могли бы выполняться параллельно или после ответа.
**Рекомендация:** Выносить пост-логин действия (письмо, регистрация устройства, гео) за пределы критической секции (fire-and-forget/после формирования ответа); распараллелить независимые вызовы.

### P4. Двойная/конфликтующая регистрация `LocationClient` в DI — Medium
**Файл:** `Backend/BarkFluff.Identity/Program.cs:65-66`
**Проблема:** Сначала `AddHttpClient<LocationClient>()` (типизированный клиент через `IHttpClientFactory`), затем `AddScoped<LocationClient>()`. Вторая регистрация перекрывает первую при резолве, минуя фабрику типизированного клиента.
**Почему это проблема:** В лучшем случае строка избыточна; в худшем — `LocationClient` создаётся в обход `IHttpClientFactory`, что ломает пуллинг/ротацию `HttpMessageHandler` (риск исчерпания сокетов/устаревания DNS под нагрузкой). Это противоречивая конфигурация, которую нужно устранить.
**Рекомендация:** Удалить строку `AddScoped<LocationClient>()`; оставить только `AddHttpClient<LocationClient>()`.

### P5. Чтение на пути логина без `AsNoTracking` — Low
**Файл:** `Backend/BarkFluff.Identity/Persistence/Services/AuthPropertiesStorage.cs:89-92` (`GetUserAuthProperties`)
**Проблема:** Read-only выборки настроек 2FA на логине выполняются с трекингом изменений (в отличие от `PasswordsStorage.GetUserPasswordHash`, который использует `AsNoTracking`).
**Почему это проблема:** Лишние накладные расходы на снапшоты трекинга на горячем пути (незначительно, но систематически).
**Рекомендация:** Добавить `AsNoTracking()` для чисто читающих методов.

---

## Docker / nginx

### D1. nginx не ограничивает частоту запросов к Identity (`limit_req` отсутствует) — High
**Файл:** `Backend/nginx/identity.conf:15-24`
**Проблема:** В `location /` нет `limit_req`/`limit_conn`. Это единственный сетевой рубеж перед самым чувствительным сервисом, и он не сдерживает перебор.
**Почему это проблема:** В связке с отсутствием app-level lockout (S1) делает реализуемыми S2/S3/S9 (перебор паролей и всех 6-значных кодов) прямо через публичный `identity.barkfluff.com`.
**Рекомендация:** Объявить `limit_req_zone` (по IP) и применить `limit_req` к Identity, особенно к методам входа/сброса; рассмотреть `limit_conn`.

### D2. В master-compose порт Identity опубликован на хост, в обход nginx — Medium
**Файл:** `Backend/docker-compose-master.yml:53` (`ports: ["${IDENTITY_PORT}:${IDENTITY_PORT}"]`)
**Проблема:** Контейнер публикует gRPC-порт (h2c, plaintext) на интерфейс хоста. В dev-compose порты не публикуются (только внутренняя сеть), а в master — публикуются.
**Почему это проблема:** Прямое подключение к незашифрованному gRPC-порту в обход nginx (TLS-терминация и любой будущий `limit_req`/доступовые ограничения). Если фаервол хоста не закрывает порт, это открытая plaintext-точка auth-сервиса.
**Рекомендация:** Не публиковать порт Identity на хост (доступ только через nginx по внутренней сети) либо биндить на `127.0.0.1`; внешний трафик пускать исключительно через TLS-прокси.

---

## Не выявлено (проверено)
- **SQL-инъекции:** не найдены — весь доступ к данным через параметризованный EF Core LINQ, raw SQL отсутствует.
- **Хардкод секретов в сервисе:** `JwtSettings.SecretKey` берётся из централизованного Configuration-сервиса (`Program.cs:41`), захардкоженного ключа в коде/`appsettings*.json` нет. (Замечание уровня Low/архитектура: один симметричный HS256-ключ общий для всех сервисов и его длина нигде не валидируется — вне области Identity, но стоит учитывать вместе с аудитом XAuth.)
- **Слабые алгоритмы хеширования/ГПСЧ:** пароли — BCrypt; коды/токены — `RandomNumberGenerator`; `System.Random`/`Guid` для секретов не используются (`Random.Shared` применяется лишь как джиттер задержки в анти-энумерации, что допустимо).
- **Логирование секретов:** пароли, JWT и OTP-коды в логи не пишутся.
