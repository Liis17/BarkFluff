# Каталог метрик BarkFluff

Полный перечень метрик, публикуемых сервисами BarkFluff в логи (Serilog), и готовых бандлов для импорта в Lognara.

- Источник текста отдачи: `MetricsCollector`, рендер ключ-значений вида `("имя": N)` (одна пара = один инкремент/значение, `SchemaVersion: 2`).
- Формы: `kind delta` — счётчики (Increment/Add), `gauge` — значения датчиков (Set/SetMany), одинаковые для всей платформы.
- Импорт в Lognara: файлы в `import/`, формат `lognara-metrics` v1; в бандле максимум 64 метрики; правило (regex+contains) обязательно.

## Ограничения Core

| Лимит | Значение | Как учитывалось |
|---|---|---|
| Метрик в бандле | 1..64 | бандлы разбиты на части (_partN) |
| Определений с правилом на инстанс Core | 64 | полный импорт всех частей невозможен; выбирайте подмножество |
| label_groups в правиле | до 4 | семьи имеют 1–2 группы |
| contains | 1..256 байт | все правила уложились |
| label limit | 1000 наборов меток | учтено: origin/server — имена серверов |

## Профили экспорта

| Сервис | Контейнер (nightly) | Профиль | Метрик |
|---|---|---|---|
| BarkFluff.Beacon | beacon | ImmediateByDefault | 17 |
| BarkFluff.Bots | bots | ImmediateByDefault (+buffered: bot_api_messages_sent, bot_updates_stored) | 24 |
| BarkFluff.Calls | calls | ImmediateByDefault | 20 |
| BarkFluff.ClientStorage | clientstorage | BufferAll | 7 |
| BarkFluff.CloudMessaging | cloud-messaging | BufferAll | 6 |
| BarkFluff.Developers | developers | ImmediateByDefault | 4 |
| BarkFluff.FastAuth | fast-auth | ImmediateByDefault | 11 |
| BarkFluff.Federation | federation | BufferAll | 72 |
| BarkFluff.Files | files | BufferAll | 23 |
| BarkFluff.Identity | identity | ImmediateByDefault | 72 |
| BarkFluff.Messages | messages | BufferAll | 70 |
| BarkFluff.Navigator | navigator | ImmediateByDefault | 6 |
| BarkFluff.Notification | notification | ImmediateByDefault | 4 |
| BarkFluff.Onliner | onliner | BufferAll | 48 |
| BarkFluff.Settings | settings | ImmediateByDefault | 26 |
| BarkFluff.Updates | updates | BufferAll | 132 |
| BarkFluff.Users | users | ImmediateByDefault | 102 |
| BarkFluff.Web | web | BufferAll | 2 |
| BarkFluff.WebServer | webserver | ImmediateByDefault (+buffered: http_requests_total, http_requests_errors) | 4 |

## Семьи (label-метрики)

Динамические по имени счётчики объединены в метрики с лейблами; для них поддерживается шаблон и класс значений.

- `BarkFluff.Federation` — `discovery_lookups` [source] — Резолвы servername по источнику: manual (ручная запись), cache (запись в кеше и ключи свежие), wellknown/navigator (также при недоступности второго источника). (BarkFluff.Federation\Services\ServerResolver.cs:42,61,80,91-96)
- `BarkFluff.Federation` — `remote_file_fetches` [result] — Итоги запроса удалённого файла: not_resolved, circuit_open, ok, denied, idle_timeout, error. (BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:113-212)
- `BarkFluff.Federation` — `typing_out` [result] — Итоги обработки входящего typing-события: not_configured, coalesced (слишком часто), not_resolved, ok, error. (BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:230-301)
- `BarkFluff.Federation` — `presence_subscribe_rejected` [reason] — Причины: not_resolved, blocked (запись заблокирована), limit (превышен лимит). (BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:170 BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:119-126)
- `BarkFluff.Federation` — `events_rejected` [code] — Код = причина отклонения: origin_mismatch, unknown_key, invalid_signature, author_not_origin или код ошибки бизнеса/trailer x-error-code. (BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:607-775)
- `BarkFluff.Federation` — `events_received` [type] — Тип = PayloadCase события в нижнем регистре (message, chat_created, ...). (BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:658)
- `BarkFluff.Federation` — `events_retry` [code] — Код = StatusCode gRPC ошибки (CamelCase; всегда с заглавной буквы). (BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:775-789)
- `BarkFluff.Federation` — `events_retry.unclassified` [code] — Код = StatusCode ошибки, не квалифицированной как постоянная или временная. (BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:789)
- `BarkFluff.Federation` — `chatcreated_quota_exceeded` [origin] — Лейбл origin = имя origin-сервера (AbuseGuardSender guard). (BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:680)
- `BarkFluff.Federation` — `typing_rate_limited` [origin] — Лейбл origin = имя origin-сервера. (BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:283)
- `BarkFluff.Federation` — `remote_file_circuit_open` [server] — Лейбл server = имя сервера, для которого открылся предохранитель. (BarkFluff.Federation\Services\RemoteFileCircuitBreaker.cs:86)
- `BarkFluff.Users` — `federated_resolves` [result] — Итоги: invalid, not_found, local, disabled, cache, unavailable, error, upsert_rejected, resolved. (BarkFluff.Users\Features\ResolveFederatedUser\ResolveFederatedUserQueryHandler.cs:47-132)
- `BarkFluff.Files` — `fed_download_total` [result] — Итоги: ok, aborted, origin_unavailable, denied. (BarkFluff.Files\Features\DownloadFile\FederatedDownloadService.cs:156-210)
- `BarkFluff.Messages` — `mediatr_requests` [op] — Счётчик каждой MediatR-операции в Messages до результата. (BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45)
- `BarkFluff.Messages` — `mediatr_success` [op] — Успешно завершённые MediatR-операции. (BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45)
- `BarkFluff.Messages` — `mediatr_errors` [op] — Операции, завершившиеся исключением. (BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45)
- `BarkFluff.Messages` — `mediatr_duration_ms_total` [op] — Суммарная длительность обработки, мс. (BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45)
- `BarkFluff.Settings` — `config_requests` [op] — Попытки вызова каждой операции настроек/конфигураций. (BarkFluff.Settings\Host\SettingsApiService.cs:80-96)
- `BarkFluff.Settings` — `config_success` [op] — Операции, завершившиеся успехом. (BarkFluff.Settings\Host\SettingsApiService.cs:80-96)
- `BarkFluff.Settings` — `config_errors` [op] — Операции, завершившиеся ошибкой. (BarkFluff.Settings\Host\SettingsApiService.cs:80-96)
- `BarkFluff.Settings` — `config_duration_ms_total` [op] — Суммарная длительность, мс. (BarkFluff.Settings\Host\SettingsApiService.cs:80-96)

## Каталог по сервисам

### BarkFluff.Beacon

Контейнер: `beacon`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `configuration_fetch_errors` | Ошибок загрузки конфигураций | загрузок | delta | sum |  | Число неудачных параллельных загрузок конфигураций (0..10 за запрос). | BarkFluff.Beacon\Features\GetServerInfo\GetServerInfoCommandHandler.cs:78 |
| `configuration_fetch_success` | Успешных загрузок конфигураций (по 10 на запрос) | загрузок | delta | sum |  | Batch из 10 GetConfigurationAsync на другие сервисы: +10 при полном успехе, +10-failed иначе (failed - число неудачных задач). | BarkFluff.Beacon\Features\GetServerInfo\GetServerInfoCommandHandler.cs:71 BarkFluff.Beacon\Features\GetServerInfo\GetServerInfoCommandHandler.cs:79 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `last_navigator_registration_unix` | Последняя успешная регистрация на Navigator | unix-с | gauge | last |  | Unix-время успешной регистрации. | BarkFluff.Beacon\BackgroundServices\ServerRegistrationService.cs:87 |
| `last_server_info_request_unix` | Последний GetServerInfo | unix-с | gauge | last |  | Unix-время последнего запроса. | BarkFluff.Beacon\Host\BeaconApiService.cs:32 |
| `navigator_registration_duration_ms_total` | Суммарная длительность регистраций на Navigator | мс | delta | sum |  | Суммарное время вызова регистрации, мс. | BarkFluff.Beacon\BackgroundServices\ServerRegistrationService.cs:86 |
| `navigator_registration_errors` | Ошибок регистрации на Navigator | ошибок | delta | sum |  | Ошибка фонового цикла регистрации. | BarkFluff.Beacon\BackgroundServices\ServerRegistrationService.cs:93 |
| `navigator_registration_healthy` | Исправность регистрации на Navigator |  | gauge | last |  | 1 - последняя регистрация успешна, 0 - сбой; ставится также в 0 на старте. | BarkFluff.Beacon\BackgroundServices\ServerRegistrationService.cs:88 BarkFluff.Beacon\BackgroundServices\ServerRegistrationService.cs:94 BarkFluff.Beacon\Program.cs:52 |
| `navigator_registrations` | Успешных регистраций на Navigator | регистраций | delta | sum |  | Фоновый ServerRegistrationService зарегистрировал Beacon на Navigator. | BarkFluff.Beacon\BackgroundServices\ServerRegistrationService.cs:85 |
| `server_info_duration_ms_total` | Суммарная длительность GetServerInfo | мс | delta | sum |  | Время обработки запросов, мс. | BarkFluff.Beacon\Host\BeaconApiService.cs:31 |
| `server_info_errors` | Ошибок GetServerInfo | ошибок | delta | sum |  | Запросы, завершившиеся исключением. | BarkFluff.Beacon\Host\BeaconApiService.cs:37 |
| `server_info_requests` | Запросов GetServerInfo | запросов | delta | sum |  | Входящие gRPC запросы server info. | BarkFluff.Beacon\Host\BeaconApiService.cs:24 |
| `server_info_success` | Успешных GetServerInfo | запросов | delta | sum |  | Завершённые без ошибки запросы. | BarkFluff.Beacon\Host\BeaconApiService.cs:30 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Beacon\Program.cs:51 |

### BarkFluff.Bots

Контейнер: `bots`; профиль: ImmediateByDefault (+buffered: bot_api_messages_sent, bot_updates_stored).

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `bot_api_commands_updated` | Обновлённых команд бота | команд | delta | sum |  | gRPC SetMyCommands. | BarkFluff.Bots\Features\SetMyCommands\SetMyCommandsCommandHandler.cs:37 |
| `bot_api_file_requests` | Запросов файла бота | запросов | delta | sum |  | gRPC GetBotFile. | BarkFluff.Bots\Features\GetBotFile\GetBotFileQueryHandler.cs:24 |
| `bot_api_messages_deleted` | Удалённых сообщений ботов (Bot API) | сообщений | delta | sum |  | gRPC DeleteBotMessage. | BarkFluff.Bots\Features\DeleteBotMessage\DeleteBotMessageCommandHandler.cs:27 |
| `bot_api_messages_edited` | Отредактированных сообщений ботов | сообщений | delta | sum |  | gRPC EditBotMessage. | BarkFluff.Bots\Features\EditBotMessage\EditBotMessageCommandHandler.cs:26 |
| `bot_api_messages_sent` | Отправленных сообщений ботов (Bot API) | сообщений | delta | sum |  | gRPC SendBotMessage. | BarkFluff.Bots\Features\SendBotMessage\SendBotMessageCommandHandler.cs:26 |
| `bot_api_update_streams_opened` | Открытых стримов обновлений бота | стримов | delta | sum |  | Каналы обновлений в BotsExternalApiService. | BarkFluff.Bots\Host\BotsExternalApiService.cs:132 |
| `bot_api_user_info_requests` | Запросов профиля бота | запросов | delta | sum |  | gRPC GetBotUserInfo. | BarkFluff.Bots\Features\GetBotUserInfo\GetBotUserInfoQueryHandler.cs:24 |
| `bot_updates_stored` | Сохранённых bot-обновлений | сообщений | delta | sum |  | Обновления каналов ботов, отложенные в магазин. | BarkFluff.Bots\Consumers\NewMessageConsumer.cs:85 |
| `botfather_messages_received` | Сообщений для BotFather | сообщений | delta | sum |  | Личные сообщения, направленные боту BotFather. | BarkFluff.Bots\Consumers\NewMessageConsumer.cs:73 |
| `bots_delete_requests` | Запросов удаления бота | запросов | delta | sum |  |  | BarkFluff.Bots\Host\BotsServerApiService.cs:67 |
| `bots_profile_updates` | Обновлений профилей ботов | обновлений | delta | sum |  |  | BarkFluff.Bots\Host\BotsServerApiService.cs:49 |
| `bots_token_reads` | Прочтений токена бота | токенов | delta | sum |  |  | BarkFluff.Bots\Host\BotsServerApiService.cs:61 |
| `bots_token_regenerations` | Перегенераций токена бота | токенов | delta | sum |  | Значит предыдущий токен скомпрометирован или обменён. | BarkFluff.Bots\Host\BotsServerApiService.cs:73 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `last_session_revoked_unix` | Последний отзыв сессии | unix-с | gauge | last |  | Unix-время последнего полученного события отзыва сессии. | BarkFluff.Bots\Consumers\SessionRevokedConsumer.cs:24 |
| `login_notifications_errors` | Ошибок уведомлений о входе | ошибок | delta | sum |  |  | BarkFluff.Bots\Consumers\LoginNotificationConsumer.cs:79 |
| `login_notifications_sent` | Отправленных уведомлений о входе | уведомлений | delta | sum |  | LoginNotificationConsumer обработал событие входа. | BarkFluff.Bots\Consumers\LoginNotificationConsumer.cs:74 |
| `new_message_events_consumed` | Принятых событий новых сообщений | сообщений | delta | sum |  | NewMessageConsumer получил событие из RabbitMQ. | BarkFluff.Bots\Consumers\NewMessageConsumer.cs:46 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Bots\Program.cs:188 |
| `session_revoked_received` | Получено событий отзыва сессии | событий | delta | sum |  |  | BarkFluff.Bots\Consumers\SessionRevokedConsumer.cs:18 |
| `system_bots_create_requests` | Запросов создания системного бота | запросов | delta | sum |  |  | BarkFluff.Bots\Host\BotsServerApiService.cs:33 |

### BarkFluff.Calls

Контейнер: `calls`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `call_events_subscriptions_active` | Активных подписок на события звонка | подписок | gauge | avg |  | Датчик текущего числа подписок. | BarkFluff.Calls\Host\CallsApiService.cs:68 BarkFluff.Calls\Host\CallsApiService.cs:81 BarkFluff.Calls\Program.cs:182 |
| `call_events_subscriptions_closed` | Закрытых подписок на события звонка | подписок | delta | sum |  |  | BarkFluff.Calls\Host\CallsApiService.cs:80 |
| `call_events_subscriptions_opened` | Открытых подписок на события звонка | подписок | delta | sum |  |  | BarkFluff.Calls\Host\CallsApiService.cs:67 |
| `call_system_message_errors` | Ошибок системных сообщений звонка | ошибок | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:610 |
| `calls_answered` | Отвеченных звонков | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:178 |
| `calls_ended` | Завершённых звонков | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:289 |
| `calls_initiated` | Начатых звонков | звонков | delta | sum |  | CallLifecycleHandler создал звонок. | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:144 |
| `calls_initiated_direct` | Начатых личных звонков | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:145 |
| `calls_initiated_group` | Начатых групповых звонков | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:145 |
| `calls_joined` | Присоединений к звонку | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:219 |
| `calls_missed` | Пропущенных звонков | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:476 |
| `calls_rejected` | Отклонённых звонков | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:262 |
| `calls_room_finished` | Завершённых комнат звонка (LiveKit room) | звонков | delta | sum |  |  | BarkFluff.Calls\Features\CallLifecycle\CallLifecycleHandler.cs:495 |
| `chat_member_kicked` | Обработанных событий исключения из чата | событий | delta | sum |  | ChatMemberKickedConsumer. | BarkFluff.Calls\Consumers\ChatMemberKickedConsumer.cs:28 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Calls\Program.cs:181 |
| `sessions_revoked` | Отозванных сессий | сессий | delta | sum |  | Пересоздание сессий звонка после отзыва. | BarkFluff.Calls\Consumers\SessionRevokedConsumer.cs:31 |

### BarkFluff.ClientStorage

Контейнер: `clientstorage`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `client_cache_hits` | Попаданий в кеш клиента | запросов | delta | sum |  | Отдача релиза из локального кеша. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:327 |
| `client_cache_misses` | Промахов кеша клиента | запросов | delta | sum |  | Релиза нет в кеше, требуется проксирование. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:354 |
| `client_releases_downloaded` | Отданных релизов | релизов | delta | sum |  | Ответ 200 на скачивание релиза. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:334 BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:381 |
| `client_releases_uploaded` | Принятых новых релизов | релизов | delta | sum |  | Загрузка релиза в хранилище. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:480 |
| `client_storage_download_bytes` | Прокинутых байт скачивания | байт | delta | sum |  | Объём отданного контента. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:336 BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:382 |
| `client_storage_errors` | Ошибок хранилища клиента | ошибок | delta | sum |  | Ошибки прокси/загрузки/валидации. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:363 BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:435 BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:443 |
| `client_storage_upload_bytes` | Принятых байт загрузок | байт | delta | sum |  | Объём загруженного файла релиза. | BarkFluff.ClientStorage\Controllers\ClientStorageController.cs:481 |

### BarkFluff.CloudMessaging

Контейнер: `cloud-messaging`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `fcm_pushes_failed` | Неудачных FCM-push | push | delta | sum |  | FailureCount (в т.ч. все токены при неисправимой ошибке). | Barkfluff.CloudMessaging\Services\FirebaseService.cs:155 Barkfluff.CloudMessaging\Services\FirebaseService.cs:199 Barkfluff.CloudMessaging\Services\FirebaseService.cs:251 |
| `fcm_pushes_sent` | Доставленных FCM-push (успешно) | push | delta | sum |  | SuccessCount ответов FCM. | Barkfluff.CloudMessaging\Services\FirebaseService.cs:154 Barkfluff.CloudMessaging\Services\FirebaseService.cs:250 |
| `push_jobs_received` | Полученных push-задач | задач | delta | sum |  | FirebaseService принял задачу на рассылку пушей. | Barkfluff.CloudMessaging\Services\FirebaseService.cs:121 |
| `push_target_devices` | Целевых устройств push-задачи | устройств | delta | sum |  | Fcm-токенов в задаче. | Barkfluff.CloudMessaging\Services\FirebaseService.cs:122 |
| `web_pushes_failed` | Неудачных web-push | push | delta | sum |  | FailureCount. | Barkfluff.CloudMessaging\Services\FirebaseService.cs:717 Barkfluff.CloudMessaging\Services\FirebaseService.cs:724 |
| `web_pushes_sent` | Доставленных web-push | push | delta | sum |  | SuccessCount Web Push. | Barkfluff.CloudMessaging\Services\FirebaseService.cs:716 |

### BarkFluff.Developers

Контейнер: `developers`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |

### BarkFluff.FastAuth

Контейнер: `fast-auth`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `active_subscriptions` | Открытых подписок на результат fast-auth | подписок | delta | sum |  | Increment при SubscribeFastAuthResult (не датчик). | BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs:53 |
| `active_subscriptions_closed` | Закрытых подписок на результат fast-auth | подписок | delta | sum |  | /stream закрыт или ответ выдан. | BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs:66 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `sessions_accepted` | Подтверждённых fast-auth сессий | сессий | delta | sum |  | Первичное устройство приняло вход. | BarkFluff.FastAuth\Infrastructure\FastAuthCompletion.cs:38 |
| `sessions_expired` | Истёкших fast-auth сессий | сессий | delta | sum |  | TTL-фоновые/при обращениях истечения. | BarkFluff.FastAuth\Features\RejectFastAuth\RejectFastAuthCommandHandler.cs:50 BarkFluff.FastAuth\Features\ScanFastAuth\ScanFastAuthCommandHandler.cs:36 BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs:38 BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs:101 BarkFluff.FastAuth\Features\SubscribeFastAuthResult\SubscribeFastAuthResultQueryHandler.cs:126 |
| `sessions_generated` | Сгенерированных fast-auth сессий | сессий | delta | sum |  | Issued QR-code сессии. | BarkFluff.FastAuth\Features\GenerateFastAuthToken\GenerateFastAuthTokenCommandHandler.cs:48 |
| `sessions_rejected` | Отклонённых fast-auth сессий | сессий | delta | sum |  |  | BarkFluff.FastAuth\Features\RejectFastAuth\RejectFastAuthCommandHandler.cs:59 |
| `sessions_scanned` | Сессий, отсканированных устройством | сессий | delta | sum |  | Второе устройство просканировало QR. | BarkFluff.FastAuth\Features\ScanFastAuth\ScanFastAuthCommandHandler.cs:42 |

### BarkFluff.Federation

Контейнер: `federation`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `chatcreated_quota_exceeded` | Превышение квоты создания чатов origin-сервером |  | delta | sum | origin | Лейбл origin = имя origin-сервера (AbuseGuardSender guard). | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:680 |
| `crosscheck_mismatches` | Расхождений ключей well-known/Navigator | серверов | delta | sum |  | Первый контакт при разных наборах ключей. | BarkFluff.Federation\Services\ServerResolver.cs:80 |
| `discovery_failures` | Неудачных резолвов имени сервера | резолвов | delta | sum |  | Резолв не удался ни через один доступный источник. | BarkFluff.Federation\Services\ServerResolver.cs:42 BarkFluff.Federation\Services\ServerResolver.cs:92 |
| `discovery_lookups` | Исходы резолва имени сервера |  | delta | sum | source | Резолвы servername по источнику: manual (ручная запись), cache (запись в кеше и ключи свежие), wellknown/navigator (также при недоступности второго источника). | BarkFluff.Federation\Services\ServerResolver.cs:42,61,80,91-96 |
| `events_duplicate` | Дубликатов S2S-событий | событий | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:615 |
| `events_received` | Принятые валидные S2S-события |  | delta | sum | type | Тип = PayloadCase события в нижнем регистре (message, chat_created, ...). | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:658 |
| `events_rejected` | Отклонённые входящие S2S-события |  | delta | sum | code | Код = причина отклонения: origin_mismatch, unknown_key, invalid_signature, author_not_origin или код ошибки бизнеса/trailer x-error-code. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:607-775 |
| `events_retry` | Повторы доставки S2S-события |  | delta | sum | code | Код = StatusCode gRPC ошибки (CamelCase; всегда с заглавной буквы). | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:775-789 |
| `events_retry.unclassified` | Повторы нежёстко классифицированных ошибок |  | delta | sum | code | Код = StatusCode ошибки, не квалифицированной как постоянная или временная. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:789 |
| `events_unhandled_error` | Незарегистрированных типов событий S2S | событий | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:590 |
| `federation_consumer_message_deleted` | Принято: удаление сообщения (Consumer) | событий | delta | sum |  | Из внутреннего RabbitMQ-пайпа. | BarkFluff.Federation\Consumers\MessageDeletedFederationConsumer.cs:51 |
| `federation_consumer_message_edited` | Принято: правка сообщения (Consumer) | событий | delta | sum |  |  | BarkFluff.Federation\Consumers\MessageEditedFederationConsumer.cs:68 |
| `federation_consumer_message_read` | Принято: прочтение (Consumer) | событий | delta | sum |  |  | BarkFluff.Federation\Consumers\MessageReadFederationConsumer.cs:52 |
| `federation_consumer_new_message` | Принято: новое сообщение (Consumer) | событий | delta | sum |  |  | BarkFluff.Federation\Consumers\NewMessageFederationConsumer.cs:118 |
| `fetchfile_bytes_out` | Отдано байт файла пира | байт | delta | sum |  | Отправленный объём. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:220 |
| `fetchfile_requests.denied` | Запросов файла пира: отказ | запросов | delta | sum |  | Неверная подпись/лимиты. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:177 BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:192 |
| `fetchfile_requests.ok` | Запросов файла пира: успех | запросов | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:219 |
| `fetchfile_requests.rate_limited` | Запросов файла пира: лимит | запросов | delta | sum |  | Rate limiting превысь. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:184 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `known_servers_active` | Активных известных серверов | серверов | gauge | avg |  | PeerRefreshBackgroundService строка KnownServers. | BarkFluff.Federation\BackgroundServices\PeerRefreshBackgroundService.cs:120 |
| `last_session_revoked_unix` | Последний отзыв сессии (вход) | unix-с | gauge | last |  | Unix-время события. | BarkFluff.Federation\Consumers\SessionRevokedConsumer.cs:36 |
| `last_signing_key_rotation_unix` | Последняя ротация ключа (вход) | unix-с | gauge | last |  | Unix-время события. | BarkFluff.Federation\Consumers\SigningKeyRotatedConsumer.cs:29 |
| `manual_peer_refresh.applied` | Обновлений ручных пиров | серверов | delta | sum |  | Плановая ротация ключей manual-записей применена. | BarkFluff.Federation\Services\ServerResolver.cs:127 |
| `manual_peer_refresh.skipped` | Пропущенных обновлений ручных пиров | серверов | delta | sum |  | Недоступен well-known, приватный или нет цепочки доверия. | BarkFluff.Federation\Services\ServerResolver.cs:132 |
| `outbox_deadletter.federated_dm_rejected` | DLQ: отказ федеративного ЛС | событий | delta | sum |  |  | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:308 |
| `outbox_deadletter.max_attempts` | DLQ: исчерпаны попытки | событий | delta | sum |  |  | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:336 |
| `outbox_deadletter.rejected` | DLQ: постоянный отказ получателя | событий | delta | sum |  | Отдельные причины в код. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:304 |
| `outbox_deliver_duration_ms_total` | Суммарная длительность доставки outbox | мс | delta | sum |  | Время DeliverEventsAsync, мс. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:272 |
| `outbox_delivered` | Доставленных записей outbox | событий | delta | sum |  | Успешная доставка события в origin или downstream. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:298 |
| `outbox_dispatch_errors` | Ошибок цикла outbox федерации | ошибок | delta | sum |  | Фоновый OutboxDispatcher поймал исключение. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:84 |
| `outbox_enqueued_total` | Поставлено записей outbox | записей | delta | sum |  | Sum destinations count при отправке события. | BarkFluff.Federation\Infrastructure\OutboxWriter.cs:82 |
| `outbox_pending` | Осталось записей outbox | записей | gauge | avg |  | Текущий объём несентовых записей. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:132 |
| `outbox_reclaimed` | Восстановленных залипших записей outbox | записей | delta | sum |  | Истекшие pending перезапущены. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:116 |
| `outbox_retry` | Повторов доставки outbox | повторов | delta | sum |  | Транзиентные ошибки, повтор запланирован. | BarkFluff.Federation\BackgroundServices\OutboxDispatcher.cs:343 |
| `presence_access_denied_uuids` | UUID без доступа к presence в подписке | uuid | delta | sum |  | Количество uuid, отфильтрованных по доступу. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:372 |
| `presence_events_in` | Входящих событий присутствия | событий | delta | sum |  |  | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:263 |
| `presence_events_out` | Отданных presence-событий пирам | событий | delta | sum |  | События статусов, переданные по исходящему presence-стриму (S2S). | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:473 |
| `presence_interest_resolve_errors` | Ошибок резолва сервера UUID | ошибок | delta | sum |  |  | BarkFluff.Federation\Services\RemoteUserServerCache.cs:110 |
| `presence_interest_uuid_unknown` | UUID подписки на неизвестного пользователя | uuid | delta | sum |  | RemoteUserServerCache. | BarkFluff.Federation\Services\RemoteUserServerCache.cs:97 |
| `presence_interest_uuids` | UUID, интересующих локальных подписок (объединение) | uuid | gauge | avg |  |  | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:112 |
| `presence_local_changes_observed` | Локальных изменений статуса | событий | delta | sum |  | PresenceStatusChangedConsumer. | BarkFluff.Federation\Consumers\PresenceStatusChangedConsumer.cs:52 |
| `presence_peer_ping_errors` | Ошибок пинга пиров | ошибок | delta | sum |  | PeerCapabilityCache. | BarkFluff.Federation\Services\PeerCapabilityCache.cs:80 |
| `presence_peer_unsupported` | Пиров без поддержки presence | пиров | delta | sum |  | Пир вернул Unimplemented. | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:177 |
| `presence_resubscribes` | Повторных подписок присутствия | подписок | delta | sum |  |  | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:148 |
| `presence_stream_errors` | Ошибок стримов присутствия | ошибок | delta | sum |  |  | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:214 BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:454 |
| `presence_streams_closed` | Закрытых входящих стримов presence | стримов | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:152 |
| `presence_streams_opened` | Открытых входящих стримов presence | стримов | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:137 |
| `presence_streams_out` | Активных исходящих стримов присутствия | стримов | gauge | avg |  | Датчик числа живых outbound-стримов к пира. | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:154 |
| `presence_subscribe_rejected` | Отказы на подписку присутствия со стороны origin |  | delta | sum | reason | Причины: not_resolved, blocked (запись заблокирована), limit (превышен лимит). | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:170 BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:119-126 |
| `presence_subscription_truncated` | Обрезанных UUID подписки присутствия | uuid | delta | sum |  | Batch превысил лимит и был обрезан (Add). | BarkFluff.Federation\BackgroundServices\PresenceStreamManager.cs:131 |
| `remote_file_bytes_in` | Получено байт у origin | байт | delta | sum |  | Объём загруженного файла. | BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:192 |
| `remote_file_circuit_open` | Открытие circuit breaker по серверу |  | delta | sum | server | Лейбл server = имя сервера, для которого открылся предохранитель. | BarkFluff.Federation\Services\RemoteFileCircuitBreaker.cs:86 |
| `remote_file_fetches` | Получение файла у origin-сервера |  | delta | sum | result | Итоги запроса удалённого файла: not_resolved, circuit_open, ok, denied, idle_timeout, error. | BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:113-212 |
| `remote_file_size_mismatch` | Расхождение размера файла с origin | файлов | delta | sum |  | Защита от подсовыв ложных данных. | BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:182 |
| `s2s_clock_skew_rejections` | Отброшено вызовов из-за расхождения часов | запросов | delta | sum |  | Слишком большое отклонение Timestamp. | BarkFluff.Federation\Host\XFedServerInterceptor.cs:118 |
| `s2s_requests_in` | Входящих S2S-вызовов | запросов | delta | sum |  | XFedServerInterceptor. | BarkFluff.Federation\Host\XFedServerInterceptor.cs:170 |
| `s2s_requests_out` | Исходящих S2S-вызовов | запросов | delta | sum |  | XFedClientInterceptor. | BarkFluff.Federation\Services\XFedClientInterceptor.cs:80 |
| `s2s_signature_failures` | Ошибок подписи входящих S2S-вызовов | запросов | delta | sum |  |  | BarkFluff.Federation\Host\XFedServerInterceptor.cs:160 |
| `s2s_spki_pin_rejections` | Отклонений TLS-пина пира | запросов | delta | sum |  | SPKI fingerprint не совпал с записью. | BarkFluff.Federation\Services\S2SChannelFactory.cs:138 BarkFluff.Federation\Services\S2SChannelFactory.cs:153 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Federation\Program.cs:259 |
| `session_revoked_received` | Получено событий отзыва сессии | событий | delta | sum |  |  | BarkFluff.Federation\Consumers\SessionRevokedConsumer.cs:32 |
| `signing_key_rotated_received` | Получено событий ротации ключа | событий | delta | sum |  |  | BarkFluff.Federation\Consumers\SigningKeyRotatedConsumer.cs:24 |
| `typing_in.ok` | Typing от пира: доставлен | событий | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:312 |
| `typing_out` | Пересылка typing к origin-серверу |  | delta | sum | result | Итоги обработки входящего typing-события: not_configured, coalesced (слишком часто), not_resolved, ok, error. | BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:230-301 |
| `typing_peer_unsupported` | Пиров без поддержки typing | пиров | delta | sum |  | Unimplemented от пира. | BarkFluff.Federation\Features\FederationInternalApi\FederationInternalApiHandler.cs:279 |
| `typing_rate_limited` | Typing от origin-сервера отброшен лимитом |  | delta | sum | origin | Лейбл origin = имя origin-сервера. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:283 |
| `typing_rejected.author_not_origin` | Typing: автор не из origin | событий | delta | sum |  | Защитное отклонение. | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:336 |
| `typing_rejected.not_member` | Typing: не участник чата | событий | delta | sum |  |  | BarkFluff.Federation\Features\FederationS2SApi\FederationS2SApiHandler.cs:347 |
| `wellknown_signature_failures` | Ошибок подписи well-known | запросов | delta | sum |  |  | BarkFluff.Federation\Services\WellKnownClient.cs:171 |

### BarkFluff.Files

Контейнер: `files`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `download_bytes_total` | Отданных байт файлов | байт | delta | sum |  |  | BarkFluff.Files\Host\FilesController.cs:150 |
| `fed_avatar_errors` | Ошибок федеративных аватаров | ошибок | delta | sum |  |  | BarkFluff.Files\Host\FilesController.cs:87 |
| `fed_avatar_rejected` | Отклонённых seeking актив | файлов | delta | sum |  | Актив федер-аватар не проходит проверку доверия/гигиены. | BarkFluff.Files\Host\FilesController.cs:73 |
| `fed_avatars_served` | Отданных федеративных аватаров | файлов | delta | sum |  |  | BarkFluff.Files\Host\FilesController.cs:80 |
| `fed_download_bytes_total` | Получено байт федеративных файлов | байт | delta | sum |  |  | BarkFluff.Files\Features\DownloadFile\FederatedDownloadService.cs:166 |
| `fed_download_size_exceeded` | Превышений лимита размера федеративного файла | файлов | delta | sum |  |  | BarkFluff.Files\Features\DownloadFile\FederatedDownloadService.cs:135 |
| `fed_download_total` | Итоги федеративных скачиваний файлов |  | delta | sum | result | Итоги: ok, aborted, origin_unavailable, denied. | BarkFluff.Files\Features\DownloadFile\FederatedDownloadService.cs:156-210 |
| `fed_downloads` | Федеративных скачиваний успешно | файлов | delta | sum |  |  | BarkFluff.Files\Features\DownloadFile\FederatedDownloadService.cs:165 |
| `file_traffic_bytes_total` | Суммарный трафик файлов | байт | delta | sum |  | Все отданные/принятые файловые байты. | BarkFluff.Files\Host\FilesController.cs:151 BarkFluff.Files\Host\UploadController.cs:72 |
| `files_download_errors` | Ошибок скачивания файлов | ошибок | delta | sum |  |  | BarkFluff.Files\Host\FilesController.cs:135 BarkFluff.Files\Host\FilesController.cs:140 |
| `files_downloaded` | Скачанных файлов | файлов | delta | sum |  | HTTP контроллер отдал файл. | BarkFluff.Files\Host\FilesController.cs:149 |
| `files_last_upload_buffering_ms` | Буферизация последней загрузки | мс | gauge | avg |  | Сегмент буферизации потока, мс. | BarkFluff.Files\Features\UploadFile\UploadFileCommandHandler.cs:491 |
| `files_last_upload_hashing_ms` | Хеширование последней загрузки | мс | gauge | avg |  | Сегмент вычисления хеша, мс. | BarkFluff.Files\Features\UploadFile\UploadFileCommandHandler.cs:491 |
| `files_last_upload_processing_ms` | Обработка последней загрузки | мс | gauge | avg |  | Сегмент обработки/изображения, мс. | BarkFluff.Files\Features\UploadFile\UploadFileCommandHandler.cs:491 |
| `files_last_upload_s3_ms` | Запись в S3 последней загрузки | мс | gauge | avg |  | Сегмент upload в S3, мс. | BarkFluff.Files\Features\UploadFile\UploadFileCommandHandler.cs:491 |
| `files_last_upload_total_ms` | Длительность последней загрузки | мс | gauge | avg |  | Датчик: прошедшая загрузка полностью, мс (avg на дашборде). | BarkFluff.Files\Features\UploadFile\UploadFileCommandHandler.cs:491 |
| `files_upload_errors` | Ошибок загрузки файлов | ошибок | delta | sum |  |  | BarkFluff.Files\Host\UploadController.cs:77 |
| `files_uploaded` | Загруженных файлов | файлов | delta | sum |  | Успешные загрузки. | BarkFluff.Files\Host\UploadController.cs:70 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `upload_bytes_total` | Принято байт загрузок | байт | delta | sum |  |  | BarkFluff.Files\Host\UploadController.cs:71 |

### BarkFluff.Identity

Контейнер: `identity`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `account_confirmation_failed` | Неудачных подтверждений аккаунта | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:55 BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:64 BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:76 BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:97 |
| `account_confirmation_failed_expired` | Подтверждений: код истёк | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:77 |
| `account_confirmation_failed_incorrect` | Подтверждений: неверный код | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:98 |
| `account_confirmation_failed_not_found` | Подтверждений: не найдено | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:56 BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:65 |
| `account_creation_attempts` | Попыток создания аккаунта | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityApiService.cs:80 |
| `accounts_confirmed` | Подтверждённых аккаунтов | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:170 |
| `accounts_drafted` | Создано черновиков аккаунтов | аккаунтов | delta | sum |  | Аккаунт до подтверждения email. | BarkFluff.Identity\Features\CreateAccount\CreateAccountCommandHandler.cs:62 |
| `auth_login_attempts` | Попыток входа (аутентификаций) | входов | delta | sum |  | Входящий RPC Authenticate. | BarkFluff.Identity\Host\IdentityApiService.cs:50 |
| `auth_login_failed` | Неудачных попыток входа | попыток | delta | sum |  | Все типы провала (пароль, 2FA, отсутствие пользователя). | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:114 BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:130 BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:152 BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:260 BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:292 |
| `auth_login_failed_invalid_password` | Вход отклонён: неверный пароль | попыток | delta | sum |  |  | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:153 |
| `auth_login_failed_user_not_found` | Вход отклонён: пользователь не найден | попыток | delta | sum |  |  | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:115 |
| `auth_login_success` | Успешных входов | входов | delta | sum |  | Аутентификация полностью пройдена (после возможного OTP). | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:361 |
| `auth_otp_required` | Вход приостановлен этапом OTP | событий | delta | sum |  | Пароль верен, требуется вторым фактором подтвердить. | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:237 |
| `geolocation_errors` | Ошибок геолокации | ошибок | delta | sum |  |  | BarkFluff.Identity\Infrastructure\LocationClient.cs:33 BarkFluff.Identity\Infrastructure\LocationClient.cs:61 BarkFluff.Identity\Infrastructure\LocationClient.cs:71 |
| `geolocation_requests` | Запросов геолокации | запросов | delta | sum |  | LocationClient обращается к внешнему сервису. | BarkFluff.Identity\Infrastructure\LocationClient.cs:24 |
| `geolocation_success` | Успехов геолокации | запросов | delta | sum |  |  | BarkFluff.Identity\Infrastructure\LocationClient.cs:47 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `identity_code_invalidated` | Инвалидированных защитных кодов | кодов | delta | sum |  |  | BarkFluff.Identity\Security\RedisIdentityAbuseGuard.cs:241 |
| `identity_lockouts` | Сработавших блокировок | блокировок | delta | sum |  | AbuseGuard заблокировал IP/аккаунт на время. | BarkFluff.Identity\Security\RedisIdentityAbuseGuard.cs:190 BarkFluff.Identity\Security\RedisIdentityAbuseGuard.cs:242 BarkFluff.Identity\Security\RedisIdentityAbuseGuard.cs:281 |
| `identity_protection_unavailable` | Недоступности защиты (Redis) | событий | delta | sum |  |  | BarkFluff.Identity\Security\RedisIdentityAbuseGuard.cs:370 |
| `identity_rate_limited` | Ограниченных запросов защитой | запросов | delta | sum |  | RateLimit при превышении лимитов попыток. | BarkFluff.Identity\Security\RedisIdentityAbuseGuard.cs:377 |
| `logouts` | Выходов из аккаунта | выходов | delta | sum |  |  | BarkFluff.Identity\Features\Logout\LogoutCommandHandler.cs:57 |
| `otp_authenticator_failed` | Неудачных подтверждений кода TOTP | попыток | delta | sum |  | Неверный код из приложения-аутентификатора. | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:261 BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:95 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:157 BarkFluff.Identity\Features\DisableOtpVerification\DisableOtpVerificationCommandHandler.cs:106 |
| `otp_authenticator_verified` | Подтверждений TOTP успешно | подтверждений | delta | sum |  | Проверка кода приложения-аутентификатора пройдена. | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:274 BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:113 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:172 |
| `otp_confirmation_failed` | Неудачных подтверждений 2FA | попыток | delta | sum |  | Общая ошибка этапа OTP-настройки. | BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:96 BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:135 |
| `otp_disable_failed` | Неудачных отключений 2FA | попыток | delta | sum |  |  | BarkFluff.Identity\Features\DisableOtpVerification\DisableOtpVerificationCommandHandler.cs:107 |
| `otp_disabled_authenticator` | Отключена 2FA приложения | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\DisableOtpVerification\DisableOtpVerificationCommandHandler.cs:122 |
| `otp_disabled_email` | Отключена 2FA email | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\DisableOtpVerification\DisableOtpVerificationCommandHandler.cs:131 |
| `otp_email_codes_sent` | Отправлено email-кодов | кодов | delta | sum |  | Коды одноразовые, отправляются на почту. | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:234 BarkFluff.Identity\Features\EnableOtpVerification\EnableOtpVerificationCommandHandler.cs:182 BarkFluff.Identity\Features\ResetPassword\ResetPasswordCommandHandler.cs:200 |
| `otp_email_failed` | Неудачных подтверждений email-кода | попыток | delta | sum |  |  | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:293 BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:134 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:187 |
| `otp_email_verified` | Подтверждений email-кода успешно | подтверждений | delta | sum |  |  | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:307 BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:152 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:202 |
| `otp_enabled_authenticator` | Включена 2FA через приложение | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:114 |
| `otp_enabled_email` | Включена 2FA через email | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmOtpVerification\ConfirmOtpVerificationCommandHandler.cs:153 |
| `otp_setup_authenticator` | Настроена привязка 2FA приложения | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\EnableOtpVerification\EnableOtpVerificationCommandHandler.cs:114 |
| `otp_setup_email` | Настроена привязка 2FA email | аккаунтов | delta | sum |  |  | BarkFluff.Identity\Features\EnableOtpVerification\EnableOtpVerificationCommandHandler.cs:181 |
| `password_change_failed_invalid_old` | Смен пароля: неверный старый пароль | попыток | delta | sum |  |  | BarkFluff.Identity\Features\SetPassword\SetPasswordCommandHandler.cs:63 BarkFluff.Identity\Features\SetPassword\SetPasswordCommandHandler.cs:69 |
| `password_changes` | Смен пароля | паролей | delta | sum |  | Смена пароля зарегистрированным пользователем. | BarkFluff.Identity\Features\SetPassword\SetPasswordCommandHandler.cs:80 |
| `password_changes_initial` | Установленных начальных паролей | паролей | delta | sum |  | Пароль у черновика/нового аккаунта. | BarkFluff.Identity\Features\SetPassword\SetPasswordCommandHandler.cs:83 |
| `password_reset_confirmation_failed` | Неудачных подтверждений сброса пароля | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:88 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:102 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:119 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:156 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:186 |
| `password_reset_confirmation_failed_already_used` | Подтверждения сброса: код уже использован | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:103 |
| `password_reset_confirmation_failed_expired` | Подтверждения сброса: код истёк | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:120 |
| `password_reset_confirmation_failed_not_found` | Подтверждения сброса: не найдено | попыток | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:89 |
| `password_reset_initiated_authenticator` | Сбросов через TOTP | запросов | delta | sum |  |  | BarkFluff.Identity\Features\ResetPassword\ResetPasswordCommandHandler.cs:134 |
| `password_reset_initiated_email` | Сбросов через email | запросов | delta | sum |  |  | BarkFluff.Identity\Features\ResetPassword\ResetPasswordCommandHandler.cs:199 |
| `password_reset_requests` | Запросов сброса пароля | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityApiService.cs:165 |
| `password_reset_user_not_found` | Запросов сброса: пользователь не найден | запросов | delta | sum |  |  | BarkFluff.Identity\Features\ResetPassword\ResetPasswordCommandHandler.cs:94 |
| `password_resets_confirmed` | Завершённых сбросов пароля | паролей | delta | sum |  |  | BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:230 |
| `rabbitmq_events_consumed` | Принято событий RabbitMQ | событий | delta | sum |  | SessionRevokedConsumer. | BarkFluff.Identity\Consumers\SessionRevokedConsumer.cs:17 |
| `server_bot_token_creations` | Созданий токенов ботов (сервер-сервис) | токенов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:117 |
| `server_bot_token_reads` | Прочтений токенов ботов (сервер-сервис) | токенов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:127 |
| `server_force_password_changes` | Форсирования смены пароля | запросов | delta | sum |  | Требование смены пароля от other-серверов. | BarkFluff.Identity\Host\IdentityServerApiService.cs:106 |
| `server_otp_disable_attempts` | Запросов отключения 2FA (сервер-сервис) | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:50 |
| `server_otp_lookups` | State-запросов 2FA (сервер-сервис) | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:38 |
| `server_session_creation_attempts` | Запросов создания сессии (сервер-сервис) | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:88 |
| `server_session_lookups` | Запросов сессий (сервер-сервис) | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:63 |
| `server_session_removal_attempts` | Запросов удаления сессии (сервер-сервис) | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityServerApiService.cs:75 |
| `server_session_removal_failed_not_found` | Удалений серверных сессий: не найдено | попыток | delta | sum |  |  | BarkFluff.Identity\Features\RemoveActiveSessionServer\RemoveActiveSessionServerCommandHandler.cs:52 |
| `server_sessions_created` | Создано сессий для серверов | сессий | delta | sum |  | Server API: создание сессии по запросу сервиса. | BarkFluff.Identity\Features\CreateSessionForUserServer\CreateSessionForUserServerCommandHandler.cs:116 |
| `server_sessions_removed` | Удалено серверных сессий | сессий | delta | sum |  |  | BarkFluff.Identity\Features\RemoveActiveSessionServer\RemoveActiveSessionServerCommandHandler.cs:47 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Identity\Program.cs:137 |
| `session_removal_attempts` | Попыток удаления сессии | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityApiService.cs:116 |
| `session_removal_failed_not_found` | Удалений сессий: не найдено | попыток | delta | sum |  |  | BarkFluff.Identity\Features\RemoveActiveSession\RemoveActiveSessionCommandHandler.cs:64 |
| `session_revocations_received` | Получено событий отзыва сессии | событий | delta | sum |  |  | BarkFluff.Identity\Consumers\SessionRevokedConsumer.cs:18 |
| `sessions_created` | Создано сессий | сессий | delta | sum |  | Все пути, создающие сессию (вход/подтверждение/сброс или серверные). | BarkFluff.Identity\Features\Auth\AuthCommandHandler.cs:362 BarkFluff.Identity\Features\ConfirmAccount\ConfirmAccountCommandHandler.cs:171 BarkFluff.Identity\Features\ConfirmResetPassword\ConfirmResetPasswordCommandHandler.cs:231 BarkFluff.Identity\Features\CreateSessionForUserServer\CreateSessionForUserServerCommandHandler.cs:117 |
| `sessions_removed` | Удалено сессий пользователем | сессий | delta | sum |  | Явное удаление сессии вручную. | BarkFluff.Identity\Features\RemoveActiveSession\RemoveActiveSessionCommandHandler.cs:53 |
| `sessions_revoked` | Отозванных сессий (вход) | сессий | delta | sum |  | SessionRevokedConsumer: отзыв сессии получен от Identity. | BarkFluff.Identity\Features\Logout\LogoutCommandHandler.cs:58 BarkFluff.Identity\Features\RemoveActiveSession\RemoveActiveSessionCommandHandler.cs:54 BarkFluff.Identity\Features\RemoveActiveSessionServer\RemoveActiveSessionServerCommandHandler.cs:48 |
| `tokens_refresh_attempts` | Попыток обновления токенов | запросов | delta | sum |  |  | BarkFluff.Identity\Host\IdentityApiService.cs:65 |
| `tokens_refresh_invalid` | Неверных refresh-токенов | токенов | delta | sum |  |  | BarkFluff.Identity\Features\CreateToken\CreateTokenCommandHandler.cs:27 |
| `tokens_refreshed` | Обновлённых токенов | токенов | delta | sum |  | Выдача новой пары токенов. | BarkFluff.Identity\Features\CreateToken\CreateTokenCommandHandler.cs:40 |

### BarkFluff.Messages

Контейнер: `messages`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `attachments_total` | Всего вложений в сообщениях | вложений | delta | sum |  | Sum вложений при отправке. | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:636 |
| `chats_created_federated` | Создано федеративных чатов | чатов | delta | sum |  | Пересоздание в федеративный чат. | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:223 |
| `chats_created_group` | Создано групповых чатов | чатов | delta | sum |  |  | BarkFluff.Messages\Features\CreateGroupChat\CreateGroupChatCommandHandler.cs:128 |
| `chats_created_group_members_total` | Добавлено участников при создании групп | участников | delta | sum |  | Sum members. | BarkFluff.Messages\Features\CreateGroupChat\CreateGroupChatCommandHandler.cs:129 |
| `chats_created_person` | Создано личных чатов (новых) | чатов | delta | sum |  | GetPersonChatId + создание. | BarkFluff.Messages\Features\GetPersonChatId\GetPersonChatIdCommandHandler.cs:108 BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:327 |
| `chats_deleted` | Удалено чатов | чатов | delta | sum |  | Любая форма удаления чата. | BarkFluff.Messages\Features\DeleteChat\DeleteChatCommandHandler.cs:103 |
| `chats_deleted_for_everyone` | Удалений чатов для всех | чатов | delta | sum |  | DeleteChat. | BarkFluff.Messages\Features\DeleteChat\DeleteChatCommandHandler.cs:98 |
| `chats_left` | Выходов из чата | чатов | delta | sum |  | LeaveChat. | BarkFluff.Messages\Features\LeaveChat\LeaveChatCommandHandler.cs:88 |
| `events_rejected.author_not_origin` | Отклонено: автор не origin | событий | delta | sum |  | ApplyFederatedDelete/Edit/Read с неверным автором. | BarkFluff.Messages\Features\ApplyFederatedDelete\ApplyFederatedDeleteCommandHandler.cs:92 BarkFluff.Messages\Features\ApplyFederatedEdit\ApplyFederatedEditCommandHandler.cs:101 BarkFluff.Messages\Features\ApplyFederatedRead\ApplyFederatedReadCommandHandler.cs:82 |
| `federated_chat_rejected_consumed` | Принято событий отклонения федер-чата | событий | delta | sum |  | FederatedChatRejectedConsumer. | BarkFluff.Messages\Consumers\FederatedChatRejectedConsumer.cs:21 |
| `federation_apply_delete_applied` | Федеративного удаления: применено | событий | delta | sum |  |  | BarkFluff.Messages\Features\ApplyFederatedDelete\ApplyFederatedDeleteCommandHandler.cs:184 |
| `federation_apply_delete_stale` | Федеративного удаления: устаревшее | событий | delta | sum |  | Кроме данных, комната уже удалена. | BarkFluff.Messages\Features\ApplyFederatedDelete\ApplyFederatedDeleteCommandHandler.cs:113 |
| `federation_apply_edit_applied` | Федеративной правки: применено | событий | delta | sum |  |  | BarkFluff.Messages\Features\ApplyFederatedEdit\ApplyFederatedEditCommandHandler.cs:200 |
| `federation_apply_edit_stale` | Федеративной правки: устаревшее | событий | delta | sum |  | MarkEdits не совпали с данными. | BarkFluff.Messages\Features\ApplyFederatedEdit\ApplyFederatedEditCommandHandler.cs:122 |
| `federation_apply_read_applied` | Федеративного прочтения: применено | событий | delta | sum |  |  | BarkFluff.Messages\Features\ApplyFederatedRead\ApplyFederatedReadCommandHandler.cs:118 |
| `federation_apply_read_stale` | Федеративного прочтения: устаревшее | событий | delta | sum |  |  | BarkFluff.Messages\Features\ApplyFederatedRead\ApplyFederatedReadCommandHandler.cs:92 |
| `federation_import_chat_created` | Импортировано федеративных чатов | чатов | delta | sum |  |  | BarkFluff.Messages\Features\ImportFederatedChat\ImportFederatedChatCommandHandler.cs:147 |
| `federation_import_chat_rejected_privacy` | Импорт федеративного чата: отклонено по приватности | чатов | delta | sum |  |  | BarkFluff.Messages\Features\ImportFederatedChat\ImportFederatedChatCommandHandler.cs:88 |
| `federation_import_message_created` | Импортировано федеративных сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\ImportFederatedMessage\ImportFederatedMessageCommandHandler.cs:209 |
| `group_chats_updated` | Обновлений групповых чатов | чатов | delta | sum |  |  | BarkFluff.Messages\Features\UpdateGroupChat\UpdateGroupChatCommandHandler.cs:157 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `last_message_sent_unix` | Последнее отправленное сообщение | unix-с | gauge | last |  | Unix-время. | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:638 |
| `mediatr_duration_ms_total` | Суммарная длительность MediatR-операций | мс | delta | sum | op | Суммарная длительность обработки, мс. | BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45 |
| `mediatr_errors` | Ошибок MediatR-операций |  | delta | sum | op | Операции, завершившиеся исключением. | BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45 |
| `mediatr_requests` | Попыток MediatR-операций |  | delta | sum | op | Счётчик каждой MediatR-операции в Messages до результата. | BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45 |
| `mediatr_success` | Успешных MediatR-операций |  | delta | sum | op | Успешно завершённые MediatR-операции. | BarkFluff.Messages\Infrastructure\Behaviors\MetricsBehavior.cs:28-45 |
| `message_outbox_deadletter` | Писем в DLQ outbox | записей | delta | sum |  | Исчерпаны попытки. | BarkFluff.Messages\BackgroundServices\MessageOutboxDispatcher.cs:177 |
| `message_outbox_delivered` | Доставлено публикаций Rabbit из outbox | записей | delta | sum |  |  | BarkFluff.Messages\BackgroundServices\MessageOutboxDispatcher.cs:102 |
| `message_outbox_delivery_errors` | Ошибок доставки outbox | ошибок | delta | sum |  | При отправке в RabbitMQ. | BarkFluff.Messages\BackgroundServices\MessageOutboxDispatcher.cs:183 |
| `message_outbox_dispatch_errors` | Ошибок цикла outbox сообщений | ошибок | delta | sum |  | Фоновый MessageOutboxDispatcher. | BarkFluff.Messages\BackgroundServices\MessageOutboxDispatcher.cs:64 |
| `message_outbox_pending` | Осталось публикаций в outbox | записей | gauge | avg |  | Датчик несентовых публикаций. | BarkFluff.Messages\BackgroundServices\MessageOutboxDispatcher.cs:118 |
| `message_outbox_reclaimed` | Восстановлено залипших записей outbox | записей | delta | sum |  |  | BarkFluff.Messages\BackgroundServices\MessageOutboxDispatcher.cs:85 |
| `messages_delete_noop` | Удалений сообщений: без изменений | операций | delta | sum |  | Nothing to delete after estimation. | BarkFluff.Messages\Features\DeleteMessage\DeleteMessageCommandHandler.cs:87 |
| `messages_deleted` | Удалено сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\DeleteMessage\DeleteMessageCommandHandler.cs:128 |
| `messages_edited` | Отредактированных сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\EditMessage\EditMessageCommandHandler.cs:261 |
| `messages_edited_with_attachments` | Правок с вложениями | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\EditMessage\EditMessageCommandHandler.cs:269 |
| `messages_edited_with_text` | Правок с текстом | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\EditMessage\EditMessageCommandHandler.cs:264 |
| `messages_forwarded` | Переслано сообщений | сообщений | delta | sum |  | Forward flow. | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:629 |
| `messages_marked_as_read` | Прочтено сообщений | сообщений | delta | sum |  | MarkAsRead (сумма newly-read). | BarkFluff.Messages\Features\MarkAsRead\MarkAsReadCommandHandler.cs:164 |
| `messages_pinned` | Закреплено сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\PinMessage\PinMessageCommandHandler.cs:133 |
| `messages_sent` | Отправлено сообщений | сообщений | delta | sum |  | Обычные сообщения (федеративный канал). | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:630 |
| `messages_sent_with_attachments` | Сообщений с вложениями | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:635 |
| `messages_sent_with_text` | Сообщений с текстом | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\SendMessage\SendMessageCommandHandler.cs:632 |
| `messages_unpin_noop` | Снятий закреплений: без изменений | операций | delta | sum |  | Нечего снимать. | BarkFluff.Messages\Features\UnpinMessage\UnpinMessageCommandHandler.cs:65 |
| `messages_unpinned` | Снято закреплений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\UnpinMessage\UnpinMessageCommandHandler.cs:95 |
| `messages_unpinned_all` | Снятий закрепления всех | операций | delta | sum |  | Unpin всё разом. | BarkFluff.Messages\Features\UnpinAll\UnpinAllCommandHandler.cs:92 |
| `private_chats_accepted` | Принятых личных чатов | чатов | delta | sum |  | AcceptPrivateChat. | BarkFluff.Messages\Features\AcceptPrivateChat\AcceptPrivateChatCommandHandler.cs:88 |
| `private_chats_created` | Создано личных чатов | чатов | delta | sum |  | CreatePrivateChatCommand. | BarkFluff.Messages\Features\CreatePrivateChat\CreatePrivateChatCommandHandler.cs:101 |
| `private_chats_rejected` | Отклонённых личных чатов | чатов | delta | sum |  | RejectPrivateChat. | BarkFluff.Messages\Features\RejectPrivateChat\RejectPrivateChatCommandHandler.cs:77 |
| `private_messages_ciphertext_bytes` | Объём шифротекста личных сообщений | байт | delta | sum |  | Входящие байты зашифрованного поля. | BarkFluff.Messages\Features\SendPrivateMessage\SendPrivateMessageCommandHandler.cs:99 |
| `private_messages_deleted` | Удалено личных сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\DeletePrivateMessage\DeletePrivateMessageCommandHandler.cs:67 |
| `private_messages_edited` | Отредактированных личных сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\EditPrivateMessage\EditPrivateMessageCommandHandler.cs:87 |
| `private_messages_sent` | Отправлено личных (E2E) сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\SendPrivateMessage\SendPrivateMessageCommandHandler.cs:98 |
| `rabbitmq_avatar_consumed` | Принято событий смены аватара | событий | delta | sum |  |  | BarkFluff.Messages\Consumers\UserChangedAvatarConsumer.cs:30 |
| `rabbitmq_avatar_errors` | Ошибок обработки смены аватара | ошибок | delta | sum |  |  | BarkFluff.Messages\Consumers\UserChangedAvatarConsumer.cs:69 |
| `rabbitmq_name_consumed` | Принято событий смены имени | событий | delta | sum |  |  | BarkFluff.Messages\Consumers\UserChangedNameConsumer.cs:30 |
| `rabbitmq_name_errors` | Ошибок обработки смены имени | ошибок | delta | sum |  |  | BarkFluff.Messages\Consumers\UserChangedNameConsumer.cs:71 |
| `rabbitmq_session_revoked_consumed` | Принято событий отзыва сессии | событий | delta | sum |  |  | BarkFluff.Messages\Consumers\SessionRevokedConsumer.cs:14 |
| `secret_chat_invites_accepted` | Принятых секретных приглашений | приглашений | delta | sum |  | AcceptSecretChatInvite. | BarkFluff.Messages\Features\AcceptSecretChatInvite\AcceptSecretChatInviteCommandHandler.cs:73 |
| `secret_chat_invites_rejected` | Отклонённых секретных приглашений | приглашений | delta | sum |  |  | BarkFluff.Messages\Features\RejectSecretChatInvite\RejectSecretChatInviteCommandHandler.cs:66 |
| `secret_chat_invites_sent` | Отправлено секретных приглашений | приглашений | delta | sum |  |  | BarkFluff.Messages\Features\SendSecretChatInvite\SendSecretChatInviteCommandHandler.cs:74 |
| `secret_messages_acked` | Подтверждений секретных сообщений | сообщений | delta | sum |  | AckSecretMessage. | BarkFluff.Messages\Features\AckSecretMessage\AckSecretMessageCommandHandler.cs:44 |
| `secret_messages_envelope_bytes` | Объём секретных конвертов | байт | delta | sum |  |  | BarkFluff.Messages\Features\SendSecretMessage\SendSecretMessageCommandHandler.cs:67 |
| `secret_messages_sent` | Отправлено секретных чат-сообщений | сообщений | delta | sum |  |  | BarkFluff.Messages\Features\SendSecretMessage\SendSecretMessageCommandHandler.cs:66 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Messages\Program.cs:160 |
| `users_added` | Добавлений в чат | добавлений | delta | sum |  | AddUserCommand. | BarkFluff.Messages\Features\AddUser\AddUserCommandHandler.cs:123 |
| `users_kicked` | Исключений из чата | исключений | delta | sum |  | KickUserCommand. | BarkFluff.Messages\Features\KickUser\KickUserCommandHandler.cs:134 |

### BarkFluff.Navigator

Контейнер: `navigator`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `server_lookups` | Запросов поиска сервера по имени | запросов | delta | sum |  | NavigatorApiService RPC GetServerByName (серч). | BarkFluff.Navigator\Host\NavigatorApiService.cs:30 BarkFluff.Navigator\Host\NavigatorApiService.cs:74 |
| `server_registrations` | Регистраций серверов | регистраций | delta | sum |  | Регистрация нового сервера на Navigator. | BarkFluff.Navigator\Host\NavigatorApiService.cs:68 |

### BarkFluff.Notification

Контейнер: `notification`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `emails_failed` | Неотправленных email | писем | delta | sum |  |  | BarkFluff.Notification\Consumers\EmailQueueConsumer.cs:52 |
| `emails_sent` | Отправленных email | писем | delta | sum |  |  | BarkFluff.Notification\Consumers\EmailQueueConsumer.cs:43 |
| `emails_skipped` | Пропущенных email | писем | delta | sum |  | Пользователь отключил уведомления/нет email. | BarkFluff.Notification\Consumers\EmailQueueConsumer.cs:29 |
| `rabbitmq_events_consumed` | Принято событий RabbitMQ | событий | delta | sum |  | EmailQueueConsumer. | BarkFluff.Notification\Consumers\EmailQueueConsumer.cs:25 |

### BarkFluff.Onliner

Контейнер: `onliner`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `active_subscriptions` | Активных подписок на статусы | подписок | gauge | avg |  | Датчик (MetricsSnapshotService). | BarkFluff.Onliner\BackgroundServices\MetricsSnapshotService.cs:43 |
| `change_chats_in_typing_subscription_requests` | Правок подписки typing | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:141 |
| `change_users_in_subscription_requests` | Правок живой подписки (добавить/снять участников) | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:93 |
| `db_persistence_errors` | Ошибок сохранения в БД | ошибок | delta | sum |  |  | BarkFluff.Onliner\BackgroundServices\DatabasePersistenceService.cs:68 |
| `db_persistence_runs` | Прогонов сохранения в БД | прогонов | delta | sum |  | DatabasePersistenceService. | BarkFluff.Onliner\BackgroundServices\DatabasePersistenceService.cs:51 |
| `db_records_saved_total` | Сохранено записей присутствия | записей | delta | sum |  |  | BarkFluff.Onliner\BackgroundServices\DatabasePersistenceService.cs:107 |
| `federated_typing_errors` | Ошибок федеративного typing | ошибок | delta | sum |  |  | BarkFluff.Onliner\Services\FederatedTypingSender.cs:70 |
| `federated_typing_sent` | Отправлено федеративных typing | событий | delta | sum |  | FederatedTypingSender. | BarkFluff.Onliner\Services\FederatedTypingSender.cs:66 |
| `get_online_status_requests` | Запросов статусов | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:45 |
| `get_online_status_user_ids_total` | Запрошено userId в статусах | uuid | delta | sum |  | Sum запросов. | BarkFluff.Onliner\Host\OnlinerApiService.cs:46 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `membership_check_errors` | Ошибок проверок членства | ошибок | delta | sum |  |  | BarkFluff.Onliner\Services\ChatMembershipFilter.cs:57 |
| `membership_checks` | Проверок членства | проверок | delta | sum |  | ChatMembershipFilter. | BarkFluff.Onliner\Services\ChatMembershipFilter.cs:44 |
| `offline_detection_errors` | Ошибок детекции офлайна | ошибок | delta | sum |  |  | BarkFluff.Onliner\BackgroundServices\OfflineDetectionService.cs:79 |
| `offline_detection_runs` | Прогонов детекции офлайна | прогонов | delta | sum |  | OfflineDetectionService. | BarkFluff.Onliner\BackgroundServices\OfflineDetectionService.cs:62 |
| `online_users_count` | Пользователей онлайн | пользователей | gauge | avg |  | Датчик текущего счётчика. | BarkFluff.Onliner\BackgroundServices\MetricsSnapshotService.cs:46 |
| `presence_interest_errors` | Ошибок отчётов интереса | ошибок | delta | sum |  |  | BarkFluff.Onliner\BackgroundServices\PresenceInterestReporter.cs:81 |
| `presence_interest_reports` | Отчётов интереса присутствия | отчётов | delta | sum |  | PresenceInterestReporter. | BarkFluff.Onliner\BackgroundServices\PresenceInterestReporter.cs:71 |
| `remote_snapshot_errors` | Ошибок снимка remote статусов | ошибок | delta | sum |  |  | BarkFluff.Onliner\Features\SubscribeToOnlineStatus\SubscribeToOnlineStatusQueryHandler.cs:127 |
| `remote_status_upserts` | Обновлений remote статусов | обновлений | delta | sum |  | UpsertRemoteStatus. | BarkFluff.Onliner\Features\UpsertRemoteStatus\UpsertRemoteStatusCommandHandler.cs:39 |
| `remote_tracked_uuids` | Отслеживаемых remote UUID | uuid | gauge | avg |  | Датчик. | BarkFluff.Onliner\BackgroundServices\MetricsSnapshotService.cs:45 |
| `remote_typing_injections` | Внедрений remote typing | событий | delta | sum |  | InjectRemoteTyping. | BarkFluff.Onliner\Features\InjectRemoteTyping\InjectRemoteTypingCommandHandler.cs:32 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Onliner\Program.cs:141 |
| `sessions_revoked` | Отозванных сессий (влияние на presence) | сессий | delta | sum |  | SessionRevokedConsumer. | BarkFluff.Onliner\Consumers\SessionRevokedConsumer.cs:18 |
| `set_online_status_requests` | Запросов смены статуса | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:61 |
| `set_typing_status_requests` | Запросов typing | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:108 |
| `status_changes.offline` | Переходов в офлайн (устоявшееся) | событий | delta | sum |  | Детектор констатировал офлайн. | BarkFluff.Onliner\BackgroundServices\OfflineDetectionService.cs:106 |
| `status_changes.online` | Переходов в онлайн | событий | delta | sum |  | SetOnlineStatus. | BarkFluff.Onliner\Features\SetOnlineStatus\SetOnlineStatusCommandHandler.cs:49 |
| `status_notification_errors` | Ошибок уведомлений статуса | ошибок | delta | sum |  |  | BarkFluff.Onliner\Services\OnlineStatusNotifier.cs:109 |
| `status_notifications_sent` | Доставлено уведомлений статуса | уведомлений | delta | sum |  | OnlineStatusNotifier. | BarkFluff.Onliner\Services\OnlineStatusNotifier.cs:103 |
| `subscribe_requests` | Запросов подписки на статусы | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:75 |
| `subscriptions_disconnected` | Отключений подписок статуса | подписок | delta | sum |  |  | BarkFluff.Onliner\Features\SubscribeToOnlineStatus\SubscribeToOnlineStatusQueryHandler.cs:85 |
| `subscriptions_hidden_by_privacy` | Скрытых настроек приватностью подписок | чатов | delta | sum |  | Скрыто чатов при SubscribeToOnlineStatus. | BarkFluff.Onliner\Features\SubscribeToOnlineStatus\SubscribeToOnlineStatusQueryHandler.cs:52 |
| `subscriptions_registered` | Зарегистрированных подписок статуса | подписок | delta | sum |  |  | BarkFluff.Onliner\Features\SubscribeToOnlineStatus\SubscribeToOnlineStatusQueryHandler.cs:65 |
| `tracked_unique_users` | Отслеживаемых уникальных пользователей | пользователей | gauge | avg |  | Датчик. | BarkFluff.Onliner\BackgroundServices\MetricsSnapshotService.cs:44 |
| `typing_heartbeats` | Сигналов typing | событий | delta | sum |  | SetTypingStatus heartbeat. | BarkFluff.Onliner\Features\SetTypingStatus\SetTypingStatusCommandHandler.cs:41 |
| `typing_heartbeats_rejected_by_membership` | Сигналов typing вне чата | событий | delta | sum |  | Отказ по членству. | BarkFluff.Onliner\Features\SetTypingStatus\SetTypingStatusCommandHandler.cs:49 |
| `typing_notification_errors` | Ошибок typing-уведомлений | ошибок | delta | sum |  |  | BarkFluff.Onliner\Services\TypingNotifier.cs:108 |
| `typing_notifications_sent` | Доставлено typing-уведомлений | уведомлений | delta | sum |  | TypingNotifier. | BarkFluff.Onliner\Services\TypingNotifier.cs:103 |
| `typing_subscribe_requests` | Запросов подписки на typing | запросов | delta | sum |  |  | BarkFluff.Onliner\Host\OnlinerApiService.cs:124 |
| `typing_subscriptions_disconnected` | Отключений подписок typing | подписок | delta | sum |  |  | BarkFluff.Onliner\Features\SubscribeToTyping\SubscribeToTypingQueryHandler.cs:69 |
| `typing_subscriptions_hidden_by_membership` | Убранных подписок typing по членству | чатов | delta | sum |  | Скрытые чаты при SubscribeToTyping. | BarkFluff.Onliner\Features\SubscribeToTyping\SubscribeToTypingQueryHandler.cs:46 |
| `typing_subscriptions_registered` | Зарегистрированных подписок typing | подписок | delta | sum |  |  | BarkFluff.Onliner\Features\SubscribeToTyping\SubscribeToTypingQueryHandler.cs:56 |
| `visibility_check_errors` | Ошибок проверок видимости | ошибок | delta | sum |  |  | BarkFluff.Onliner\Services\OnlineVisibilityFilter.cs:65 |
| `visibility_checks` | Проверок видимости | проверок | delta | sum |  | OnlineVisibilityFilter. | BarkFluff.Onliner\Services\OnlineVisibilityFilter.cs:52 |

### BarkFluff.Settings

Контейнер: `settings`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `config_duration_ms_total` | Суммарная длительность операций конфигураций | мс | delta | sum | op | Суммарная длительность, мс. | BarkFluff.Settings\Host\SettingsApiService.cs:80-96 |
| `config_errors` | Ошибок операций конфигураций |  | delta | sum | op | Операции, завершившиеся ошибкой. | BarkFluff.Settings\Host\SettingsApiService.cs:80-96 |
| `config_requests` | Попыток операций конфигураций |  | delta | sum | op | Попытки вызова каждой операции настроек/конфигураций. | BarkFluff.Settings\Host\SettingsApiService.cs:80-96 |
| `config_success` | Успешных операций конфигураций |  | delta | sum | op | Операции, завершившиеся успехом. | BarkFluff.Settings\Host\SettingsApiService.cs:80-96 |
| `configurations_db_writes` | Записей конфигураций в БД | записей | delta | sum |  | SettingsStorage. | BarkFluff.Settings\Persistence\Services\SettingsStorage.cs:99 BarkFluff.Settings\Persistence\Services\SettingsStorage.cs:157 |
| `db_healthy` | Исправность БД настроек |  | gauge | last |  | 1 - миграции прошли и база жива, 0 - сбой. | BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:32 BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:51 BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:72 |
| `db_migration_attempts` | Попыток миграции БД | запросов | delta | sum |  |  | BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:42 |
| `db_migration_failed` | Неудачных миграций БД | запросов | delta | sum |  |  | BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:57 BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:70 |
| `db_migration_succeeded` | Успешных миграций БД | запросов | delta | sum |  |  | BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:50 |
| `defaults_populated_total` | Вставленных дефолтных настроек | строк | delta | sum |  | SettingsSeeder добавил строки при старте. | BarkFluff.Settings\Infrastructure\SettingsSeeder.cs:117 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `last_config_get_all_items` | Число разделов в последнем GetConfigurations | шт | gauge | last |  | Датчик. | BarkFluff.Settings\Host\SettingsApiService.cs:51 |
| `last_config_get_all_unix` | Последний GetConfigurations | unix-с | gauge | last |  | Датчик даты RPC полного чтения. | BarkFluff.Settings\Host\SettingsApiService.cs:47 |
| `last_config_get_items` | Число разделов в последнем GetConfiguration | шт | gauge | last |  | Датчик количества конфигураций в ответе. | BarkFluff.Settings\Host\SettingsApiService.cs:42 |
| `last_config_get_unix` | Последний GetConfiguration | unix-с | gauge | last |  | Датчик даты RPC GetConfiguration. | BarkFluff.Settings\Host\SettingsApiService.cs:38 |
| `last_config_update_unix` | Последнее обновление конфигураций | unix-с | gauge | last |  | Датчик даты RPC UpdateConfiguration. | BarkFluff.Settings\Host\SettingsApiService.cs:56 |
| `reserved_names_count` | Зарезервированных имён в БД | шт | gauge | last |  | Датчик количества записей таблицы. | BarkFluff.Settings\Persistence\Services\SettingsStorage.cs:166 BarkFluff.Settings\Persistence\Services\SettingsStorage.cs:254 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:31 |
| `settings_bootstrap_errors_total` | Ошибок bootstrap настроек | ошибок | delta | sum |  | Инициализация настроек не удалась. | BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:58 BarkFluff.Settings\Infrastructure\SettingsStartupInitializer.cs:71 |
| `settings_missing_manual_total` | Настроек без ручного значения | шт | gauge | last |  | Датчик количества настроек, которые ещё не установлены вручную. | BarkFluff.Settings\Infrastructure\SettingsReadinessContributor.cs:62 |
| `settings_rows_total` | Строк настроек в каталоге | строк | gauge | last |  | Датчик размера справочника. | BarkFluff.Settings\Infrastructure\SettingsSeeder.cs:118 |
| `setup_completions` | Завершений установок (setup) | событий | delta | sum |  | Сигналов о завершении установок. | BarkFluff.Settings\Infrastructure\SettingsSetupCoordinator.cs:243 |
| `setup_group_fields_changed` | Изменён полей групп настроек | полей | delta | sum |  | SettingsSetupCoordinator: сколько полей обновил обновляемый батч. | BarkFluff.Settings\Infrastructure\SettingsSetupCoordinator.cs:197 |

### BarkFluff.Updates

Контейнер: `updates`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `active_subscriptions` | Открыто подписок (legacy) | подписок | delta | sum |  | Legacy-счётчик открытых подписок всех типов (аналог *_opened). | BarkFluff.Updates\Host\UpdatesApiService.cs:125 BarkFluff.Updates\Host\UpdatesApiService.cs:160 BarkFluff.Updates\Host\UpdatesApiService.cs:188 BarkFluff.Updates\Host\UpdatesApiService.cs:216 BarkFluff.Updates\Host\UpdatesApiService.cs:244 BarkFluff.Updates\Host\UpdatesApiService.cs:272 BarkFluff.Updates\Host\UpdatesApiService.cs:300 BarkFluff.Updates\Host\UpdatesApiService.cs:328 |
| `active_subscriptions_removed` | Закрыто подписок (legacy) | подписок | delta | sum |  | Legacy-счётчик закрытых подписок всех типов (аналог *_closed). | BarkFluff.Updates\Host\UpdatesApiService.cs:147 BarkFluff.Updates\Host\UpdatesApiService.cs:175 BarkFluff.Updates\Host\UpdatesApiService.cs:203 BarkFluff.Updates\Host\UpdatesApiService.cs:231 BarkFluff.Updates\Host\UpdatesApiService.cs:259 BarkFluff.Updates\Host\UpdatesApiService.cs:287 BarkFluff.Updates\Host\UpdatesApiService.cs:315 BarkFluff.Updates\Host\UpdatesApiService.cs:343 |
| `all_messages_unpinned_broadcast` | Доставлено: снятие всех закреплений | отправок | delta | sum |  | Подписчикам материала. | BarkFluff.Updates\Features\SubscribeAllMessagesUnpinned\Handlers\AllMessagesUnpinnedNotificationHandler.cs:53 |
| `all_messages_unpinned_broadcast_errors` | Ошибок: снятие всех закреплений | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeAllMessagesUnpinned\Handlers\AllMessagesUnpinnedNotificationHandler.cs:60 |
| `all_messages_unpinned_events_consumed` | Принято событий снятия всех закреплений | событий | delta | sum |  | AllMessagesUnpinnedConsumer. | BarkFluff.Updates\Consumers\AllMessagesUnpinnedConsumer.cs:31 |
| `all_messages_unpinned_events_errors` | Ошибок снятия всех закреплений | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\AllMessagesUnpinnedConsumer.cs:51 |
| `all_messages_unpinned_subscriptions_active` | Активно подписок: снятия всех закреплений | подписок | gauge | avg |  | Датчик живых стримов (all_messages_unpinned); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:301 BarkFluff.Updates\Host\UpdatesApiService.cs:316 BarkFluff.Updates\Program.cs:213 |
| `all_messages_unpinned_subscriptions_closed` | Закрытых подписок: снятия всех закреплений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:314 |
| `all_messages_unpinned_subscriptions_opened` | Открытых подписок: снятия всех закреплений | подписок | delta | sum |  | Открытие стрима подписки Updates (all_messages_unpinned). | BarkFluff.Updates\Host\UpdatesApiService.cs:299 |
| `chats_hidden_broadcast` | Доставлено: скрытие чата | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeChatHidden\Handlers\ChatHiddenNotificationHandler.cs:54 |
| `chats_hidden_broadcast_errors` | Ошибок: скрытие чата | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeChatHidden\Handlers\ChatHiddenNotificationHandler.cs:61 |
| `chats_hidden_events_consumed` | Принято событий скрытия чата | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\ChatHiddenConsumer.cs:31 |
| `chats_hidden_events_errors` | Ошибок скрытия чата | ошибок | delta | sum |  | Обработка события завершилась ошибкой. | BarkFluff.Updates\Consumers\ChatHiddenConsumer.cs:53 |
| `chats_hidden_subscriptions_active` | Активно подписок: скрытий чата | подписок | gauge | avg |  | Датчик живых стримов (chats_hidden); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:329 BarkFluff.Updates\Host\UpdatesApiService.cs:344 BarkFluff.Updates\Program.cs:214 |
| `chats_hidden_subscriptions_closed` | Закрытых подписок: скрытий чата | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:342 |
| `chats_hidden_subscriptions_opened` | Открытых подписок: скрытий чата | подписок | delta | sum |  | Открытие стрима подписки Updates (chats_hidden). | BarkFluff.Updates\Host\UpdatesApiService.cs:327 |
| `encrypted_message_deleted_events_consumed` | Принято событий удаления ЛС | событий | delta | sum |  | EncryptedMessageDeletedConsumer. | BarkFluff.Updates\Consumers\EncryptedMessageDeletedConsumer.cs:30 |
| `encrypted_message_edited_events_consumed` | Принято событий правки ЛС | событий | delta | sum |  | EncryptedMessageEditedConsumer. | BarkFluff.Updates\Consumers\EncryptedMessageEditedConsumer.cs:31 |
| `encrypted_message_edited_events_errors` | Ошибок правки ЛС | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\EncryptedMessageEditedConsumer.cs:44 |
| `events_broadcast` | Обновлений (legacy общий счётчик) | отправок | delta | sum |  | Совместимость с прежним кодом. | BarkFluff.Updates\Features\SubscribeNewMessages\Handlers\NewMessageNotificationHandler.cs:72 |
| `events_broadcast_errors` | Ошибок (legacy общий) | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeNewMessages\Handlers\NewMessageNotificationHandler.cs:80 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `messages_deleted_broadcast` | Доставлено: удаление сообщения | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesDeleted\Handlers\MessageDeletedNotificationHandler.cs:54 |
| `messages_deleted_broadcast_errors` | Ошибок: удаление сообщения | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesDeleted\Handlers\MessageDeletedNotificationHandler.cs:61 |
| `messages_deleted_events_consumed` | Принято событий удаления сообщений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\MessageDeletedConsumer.cs:31 |
| `messages_deleted_events_errors` | Ошибок удаления сообщений | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\MessageDeletedConsumer.cs:53 |
| `messages_deleted_subscriptions_active` | Активно подписок: удалений сообщений | подписок | gauge | avg |  | Датчик живых стримов (messages_deleted); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:217 BarkFluff.Updates\Host\UpdatesApiService.cs:232 BarkFluff.Updates\Program.cs:210 |
| `messages_deleted_subscriptions_closed` | Закрытых подписок: удалений сообщений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:230 |
| `messages_deleted_subscriptions_opened` | Открытых подписок: удалений сообщений | подписок | delta | sum |  | Открытие стрима подписки Updates (messages_deleted). | BarkFluff.Updates\Host\UpdatesApiService.cs:215 |
| `messages_edited_broadcast` | Доставлено: правка сообщения | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesEdited\Handlers\MessageEditedNotificationHandler.cs:56 |
| `messages_edited_broadcast_errors` | Ошибок: правка сообщения | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesEdited\Handlers\MessageEditedNotificationHandler.cs:63 |
| `messages_edited_events_consumed` | Принято событий правки сообщений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\MessageEditedConsumer.cs:33 |
| `messages_edited_events_errors` | Ошибок правки сообщений | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\MessageEditedConsumer.cs:53 |
| `messages_edited_subscriptions_active` | Активно подписок: правок сообщений | подписок | gauge | avg |  | Датчик живых стримов (messages_edited); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:189 BarkFluff.Updates\Host\UpdatesApiService.cs:204 BarkFluff.Updates\Program.cs:209 |
| `messages_edited_subscriptions_closed` | Закрытых подписок: правок сообщений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:202 |
| `messages_edited_subscriptions_opened` | Открытых подписок: правок сообщений | подписок | delta | sum |  | Открытие стрима подписки Updates (messages_edited). | BarkFluff.Updates\Host\UpdatesApiService.cs:187 |
| `messages_pinned_broadcast` | Доставлено: закрепление | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesPinned\Handlers\MessagePinnedNotificationHandler.cs:58 |
| `messages_pinned_broadcast_errors` | Ошибок: закрепление | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesPinned\Handlers\MessagePinnedNotificationHandler.cs:65 |
| `messages_pinned_events_consumed` | Принято событий закрепления | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\MessagePinnedConsumer.cs:31 |
| `messages_pinned_events_errors` | Ошибок закрепления | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\MessagePinnedConsumer.cs:55 |
| `messages_pinned_subscriptions_active` | Активно подписок: закреплений | подписок | gauge | avg |  | Датчик живых стримов (messages_pinned); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:245 BarkFluff.Updates\Host\UpdatesApiService.cs:260 BarkFluff.Updates\Program.cs:211 |
| `messages_pinned_subscriptions_closed` | Закрытых подписок: закреплений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:258 |
| `messages_pinned_subscriptions_opened` | Открытых подписок: закреплений | подписок | delta | sum |  | Открытие стрима подписки Updates (messages_pinned). | BarkFluff.Updates\Host\UpdatesApiService.cs:243 |
| `messages_unpinned_broadcast` | Доставлено: снятие закрепления | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesUnpinned\Handlers\MessageUnpinnedNotificationHandler.cs:54 |
| `messages_unpinned_broadcast_errors` | Ошибок: снятие закрепления | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesUnpinned\Handlers\MessageUnpinnedNotificationHandler.cs:61 |
| `messages_unpinned_events_consumed` | Принято событий снятия закрепления | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\MessageUnpinnedConsumer.cs:31 |
| `messages_unpinned_events_errors` | Ошибок снятия закрепления | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\MessageUnpinnedConsumer.cs:53 |
| `messages_unpinned_subscriptions_active` | Активно подписок: снятий закреплений | подписок | gauge | avg |  | Датчик живых стримов (messages_unpinned); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:273 BarkFluff.Updates\Host\UpdatesApiService.cs:288 BarkFluff.Updates\Program.cs:212 |
| `messages_unpinned_subscriptions_closed` | Закрытых подписок: снятий закреплений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:286 |
| `messages_unpinned_subscriptions_opened` | Открытых подписок: снятий закреплений | подписок | delta | sum |  | Открытие стрима подписки Updates (messages_unpinned). | BarkFluff.Updates\Host\UpdatesApiService.cs:271 |
| `new_encrypted_message_events_consumed` | Принято новых личных сообщений | событий | delta | sum |  | NewEncryptedMessageConsumer. | BarkFluff.Updates\Consumers\NewEncryptedMessageConsumer.cs:31 |
| `new_encrypted_message_events_errors` | Ошибок обработки новых ЛС | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\NewEncryptedMessageConsumer.cs:48 |
| `new_message_events_consumed` | Принято новых сообщений | событий | delta | sum |  | NewMessageConsumer. | BarkFluff.Updates\Consumers\NewMessageConsumer.cs:33 |
| `new_message_events_errors` | Ошибок обработки новых сообщений | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\NewMessageConsumer.cs:60 |
| `new_messages_broadcast` | Доставлено: новые сообщения | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeNewMessages\Handlers\NewMessageNotificationHandler.cs:71 |
| `new_messages_broadcast_errors` | Ошибок: новые сообщения | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeNewMessages\Handlers\NewMessageNotificationHandler.cs:79 |
| `new_messages_subscriptions_active` | Активно подписок: новых сообщений | подписок | gauge | avg |  | Датчик живых стримов (new_messages); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:126 BarkFluff.Updates\Host\UpdatesApiService.cs:148 BarkFluff.Updates\Program.cs:207 |
| `new_messages_subscriptions_closed` | Закрытых подписок: новых сообщений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:146 |
| `new_messages_subscriptions_opened` | Открытых подписок: новых сообщений | подписок | delta | sum |  | Открытие стрима подписки Updates (new_messages). | BarkFluff.Updates\Host\UpdatesApiService.cs:124 |
| `new_secret_message_events_consumed` | Принято новых секретных сообщений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\NewSecretMessageConsumer.cs:30 |
| `private_chat_invite_events_consumed` | Принято приглашений в личный чат | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\PrivateChatInviteConsumer.cs:30 |
| `private_chat_invite_resolution_events_consumed` | Принято результатов приглашений ЛС | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\PrivateChatInviteResolutionConsumer.cs:30 |
| `private_chat_invite_resolutions_broadcast` | Доставлено: результаты приглашений | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateChatInviteResolutions\Handlers\PrivateChatInviteResolutionNotificationHandler.cs:44 |
| `private_chat_invite_resolutions_broadcast_errors` | Ошибок: результаты приглашений | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateChatInviteResolutions\Handlers\PrivateChatInviteResolutionNotificationHandler.cs:48 |
| `private_chat_invite_resolutions_subscriptions_active` | Активно подписок: результатов приглашений | подписок | gauge | avg |  | Датчик живых стримов (private_chat_invite_resolutions); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:472 BarkFluff.Updates\Host\UpdatesApiService.cs:484 BarkFluff.Updates\Program.cs:220 |
| `private_chat_invite_resolutions_subscriptions_closed` | Закрытых подписок: результатов приглашений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:483 |
| `private_chat_invite_resolutions_subscriptions_opened` | Открытых подписок: результатов приглашений | подписок | delta | sum |  | Открытие стрима подписки Updates (private_chat_invite_resolutions). | BarkFluff.Updates\Host\UpdatesApiService.cs:471 |
| `private_chat_invites_broadcast` | Доставлено: приглашения в чат | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateChatInvites\Handlers\PrivateChatInviteNotificationHandler.cs:53 |
| `private_chat_invites_broadcast_errors` | Ошибок: приглашения в чат | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateChatInvites\Handlers\PrivateChatInviteNotificationHandler.cs:57 |
| `private_chat_invites_subscriptions_active` | Активно подписок: приглашений в чат | подписок | gauge | avg |  | Датчик живых стримов (private_chat_invites); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:449 BarkFluff.Updates\Host\UpdatesApiService.cs:461 BarkFluff.Updates\Program.cs:219 |
| `private_chat_invites_subscriptions_closed` | Закрытых подписок: приглашений в чат | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:460 |
| `private_chat_invites_subscriptions_opened` | Открытых подписок: приглашений в чат | подписок | delta | sum |  | Открытие стрима подписки Updates (private_chat_invites). | BarkFluff.Updates\Host\UpdatesApiService.cs:448 |
| `private_message_deletes_subscriptions_active` | Активно подписок: удалений ЛС | подписок | gauge | avg |  | Датчик живых стримов (private_message_deletes); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:403 BarkFluff.Updates\Host\UpdatesApiService.cs:415 BarkFluff.Updates\Program.cs:217 |
| `private_message_deletes_subscriptions_closed` | Закрытых подписок: удалений ЛС | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:414 |
| `private_message_deletes_subscriptions_opened` | Открытых подписок: удалений ЛС | подписок | delta | sum |  | Открытие стрима подписки Updates (private_message_deletes). | BarkFluff.Updates\Host\UpdatesApiService.cs:402 |
| `private_message_edits_subscriptions_active` | Активно подписок: правок ЛС | подписок | gauge | avg |  | Датчик живых стримов (private_message_edits); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:380 BarkFluff.Updates\Host\UpdatesApiService.cs:392 BarkFluff.Updates\Program.cs:216 |
| `private_message_edits_subscriptions_closed` | Закрытых подписок: правок ЛС | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:391 |
| `private_message_edits_subscriptions_opened` | Открытых подписок: правок ЛС | подписок | delta | sum |  | Открытие стрима подписки Updates (private_message_edits). | BarkFluff.Updates\Host\UpdatesApiService.cs:379 |
| `private_messages_broadcast` | Доставлено: новые ЛС | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessages\Handlers\NewEncryptedMessageNotificationHandler.cs:43 |
| `private_messages_broadcast_errors` | Ошибок: новые ЛС | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessages\Handlers\NewEncryptedMessageNotificationHandler.cs:47 |
| `private_messages_deleted_broadcast` | Доставлено: удаление ЛС | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessageDeletes\Handlers\EncryptedMessageDeletedNotificationHandler.cs:43 |
| `private_messages_deleted_broadcast_errors` | Ошибок: удаление ЛС | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessageDeletes\Handlers\EncryptedMessageDeletedNotificationHandler.cs:47 |
| `private_messages_edited_broadcast` | Доставлено: правка ЛС | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessageEdits\Handlers\EncryptedMessageEditedNotificationHandler.cs:43 |
| `private_messages_edited_broadcast_errors` | Ошибок: правка ЛС | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessageEdits\Handlers\EncryptedMessageEditedNotificationHandler.cs:47 |
| `private_messages_read_broadcast` | Доставлено: прочтения ЛС | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessagesRead\Handlers\PrivateMessagesReadNotificationHandler.cs:41 |
| `private_messages_read_broadcast_errors` | Ошибок: прочтения ЛС | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribePrivateMessagesRead\Handlers\PrivateMessagesReadNotificationHandler.cs:45 |
| `private_messages_read_events_consumed` | Принято прочтений личных сообщений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\PrivateMessagesReadConsumer.cs:24 |
| `private_messages_read_subscriptions_active` | Активно подписок: прочтений ЛС | подписок | gauge | avg |  | Датчик живых стримов (private_messages_read); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:426 BarkFluff.Updates\Host\UpdatesApiService.cs:438 BarkFluff.Updates\Program.cs:218 |
| `private_messages_read_subscriptions_closed` | Закрытых подписок: прочтений ЛС | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:437 |
| `private_messages_read_subscriptions_opened` | Открытых подписок: прочтений ЛС | подписок | delta | sum |  | Открытие стрима подписки Updates (private_messages_read). | BarkFluff.Updates\Host\UpdatesApiService.cs:425 |
| `private_messages_subscriptions_active` | Активно подписок: личных сообщений | подписок | gauge | avg |  | Датчик живых стримов (private_messages); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:357 BarkFluff.Updates\Host\UpdatesApiService.cs:369 BarkFluff.Updates\Program.cs:215 |
| `private_messages_subscriptions_closed` | Закрытых подписок: личных сообщений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:368 |
| `private_messages_subscriptions_opened` | Открытых подписок: личных сообщений | подписок | delta | sum |  | Открытие стрима подписки Updates (private_messages). | BarkFluff.Updates\Host\UpdatesApiService.cs:356 |
| `push_notifications_cancelled` | Отменённых push-уведомлений | задач | delta | sum |  | Отмена при новых событиях в чате. | BarkFluff.Updates\Features\PushNotifications\PushNotificationSchedulerHandler.cs:110 |
| `push_notifications_errors` | Ошибок push-уведомлений | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\PushNotifications\PushNotificationSchedulerHandler.cs:118 |
| `push_notifications_scheduled` | Запланированных push-уведомлений | задач | delta | sum |  | PushNotificationSchedulerHandler. | BarkFluff.Updates\Features\PushNotifications\PushNotificationSchedulerHandler.cs:56 |
| `push_notifications_sent` | Отправленных push-уведомлений | push | delta | sum |  |  | BarkFluff.Updates\Features\PushNotifications\PushNotificationSchedulerHandler.cs:101 |
| `rabbitmq_events_consumed` | Принято событий RabbitMQ | событий | delta | sum |  | Все консьюмеры Updates. | BarkFluff.Updates\Consumers\AllMessagesUnpinnedConsumer.cs:30 BarkFluff.Updates\Consumers\ChatHiddenConsumer.cs:30 BarkFluff.Updates\Consumers\EncryptedMessageDeletedConsumer.cs:29 BarkFluff.Updates\Consumers\EncryptedMessageEditedConsumer.cs:30 BarkFluff.Updates\Consumers\MessageDeletedConsumer.cs:30 BarkFluff.Updates\Consumers\MessageEditedConsumer.cs:32 BarkFluff.Updates\Consumers\MessagePinnedConsumer.cs:30 BarkFluff.Updates\Consumers\MessageUnpinnedConsumer.cs:30 BarkFluff.Updates\Consumers\NewEncryptedMessageConsumer.cs:30 BarkFluff.Updates\Consumers\NewMessageConsumer.cs:32 BarkFluff.Updates\Consumers\NewSecretMessageConsumer.cs:29 BarkFluff.Updates\Consumers\PrivateChatInviteConsumer.cs:29 BarkFluff.Updates\Consumers\PrivateChatInviteResolutionConsumer.cs:29 BarkFluff.Updates\Consumers\PrivateMessagesReadConsumer.cs:23 BarkFluff.Updates\Consumers\ReadByConsumer.cs:26 BarkFluff.Updates\Consumers\SecretChatInviteConsumer.cs:29 BarkFluff.Updates\Consumers\SecretChatInviteResolutionConsumer.cs:29 BarkFluff.Updates\Consumers\SessionRevokedConsumer.cs:18 |
| `read_by_broadcast` | Доставлено: прочтение | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesRead\Handlers\ReadByNotificationHandler.cs:62 |
| `read_by_broadcast_errors` | Ошибок: прочтение | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeMessagesRead\Handlers\ReadByNotificationHandler.cs:67 |
| `read_by_events_consumed` | Принято прочтений (read-by) сообщений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\ReadByConsumer.cs:27 |
| `read_by_events_errors` | Ошибок прочтений (read-by) | ошибок | delta | sum |  |  | BarkFluff.Updates\Consumers\ReadByConsumer.cs:56 |
| `read_by_subscriptions_active` | Активно подписок: прочтения (read-by) | подписок | gauge | avg |  | Датчик живых стримов (read_by); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:161 BarkFluff.Updates\Host\UpdatesApiService.cs:176 BarkFluff.Updates\Program.cs:208 |
| `read_by_subscriptions_closed` | Закрытых подписок: прочтения (read-by) | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:174 |
| `read_by_subscriptions_opened` | Открытых подписок: прочтения (read-by) | подписок | delta | sum |  | Открытие стрима подписки Updates (read_by). | BarkFluff.Updates\Host\UpdatesApiService.cs:159 |
| `secret_chat_invite_events_consumed` | Принято секретных приглашений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\SecretChatInviteConsumer.cs:30 |
| `secret_chat_invite_resolution_events_consumed` | Принято результатов секретных приглашений | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\SecretChatInviteResolutionConsumer.cs:30 |
| `secret_chat_invites_broadcast` | Доставлено: секретные приглашения | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretChatInvites\Handlers\SecretChatInviteNotificationHandler.cs:67 |
| `secret_chat_invites_broadcast_errors` | Ошибок: секретные приглашения | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretChatInvites\Handlers\SecretChatInviteNotificationHandler.cs:71 |
| `secret_chat_invites_buffered_only` | Пропущено: подписка без клиента | событий | delta | sum |  | Подписки на секретные chats ожидают буфер. | BarkFluff.Updates\Features\SubscribeSecretChatInvites\Handlers\SecretChatInviteNotificationHandler.cs:42 |
| `secret_chat_invites_subscriptions_active` | Активно подписок: секретных приглашений | подписок | gauge | avg |  | Датчик живых стримов (secret_chat_invites); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:499 BarkFluff.Updates\Host\UpdatesApiService.cs:511 BarkFluff.Updates\Program.cs:221 |
| `secret_chat_invites_subscriptions_closed` | Закрытых подписок: секретных приглашений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:510 |
| `secret_chat_invites_subscriptions_opened` | Открытых подписок: секретных приглашений | подписок | delta | sum |  | Открытие стрима подписки Updates (secret_chat_invites). | BarkFluff.Updates\Host\UpdatesApiService.cs:498 |
| `secret_chat_resolutions_broadcast` | Доставлено: результаты секретных приглашений | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretChatResolutions\Handlers\SecretChatInviteResolutionNotificationHandler.cs:56 |
| `secret_chat_resolutions_broadcast_errors` | Ошибок: результаты секретных приглашений | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretChatResolutions\Handlers\SecretChatInviteResolutionNotificationHandler.cs:60 |
| `secret_chat_resolutions_no_active_stream` | Доставлено: нет подписки для результата | событий | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretChatResolutions\Handlers\SecretChatInviteResolutionNotificationHandler.cs:35 |
| `secret_chat_resolutions_subscriptions_active` | Активно подписок: результатов секретных приглашений | подписок | gauge | avg |  | Датчик живых стримов (secret_chat_resolutions); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:524 BarkFluff.Updates\Host\UpdatesApiService.cs:536 BarkFluff.Updates\Program.cs:222 |
| `secret_chat_resolutions_subscriptions_closed` | Закрытых подписок: результатов секретных приглашений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:535 |
| `secret_chat_resolutions_subscriptions_opened` | Открытых подписок: результатов секретных приглашений | подписок | delta | sum |  | Открытие стрима подписки Updates (secret_chat_resolutions). | BarkFluff.Updates\Host\UpdatesApiService.cs:523 |
| `secret_messages_broadcast` | Доставлено: секретные сообщения | отправок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretMessages\Handlers\NewSecretMessageNotificationHandler.cs:65 |
| `secret_messages_broadcast_errors` | Ошибок: секретные сообщения | ошибок | delta | sum |  |  | BarkFluff.Updates\Features\SubscribeSecretMessages\Handlers\NewSecretMessageNotificationHandler.cs:69 |
| `secret_messages_buffered_only` | Пропущено: секретное сообщение без подписки | событий | delta | sum |  | Клиенты не подключены к стриму. | BarkFluff.Updates\Features\SubscribeSecretMessages\Handlers\NewSecretMessageNotificationHandler.cs:39 |
| `secret_messages_subscriptions_active` | Активно подписок: секретных сообщений | подписок | gauge | avg |  | Датчик живых стримов (secret_messages); 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:549 BarkFluff.Updates\Host\UpdatesApiService.cs:561 BarkFluff.Updates\Program.cs:223 |
| `secret_messages_subscriptions_closed` | Закрытых подписок: секретных сообщений | подписок | delta | sum |  | Закрытие стрима подписки. | BarkFluff.Updates\Host\UpdatesApiService.cs:560 |
| `secret_messages_subscriptions_opened` | Открытых подписок: секретных сообщений | подписок | delta | sum |  | Открытие стрима подписки Updates (secret_messages). | BarkFluff.Updates\Host\UpdatesApiService.cs:548 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Updates\Program.cs:206 |
| `session_revoked_events_consumed` | Принято событий отзыва сессии | событий | delta | sum |  |  | BarkFluff.Updates\Consumers\SessionRevokedConsumer.cs:19 |
| `sessions_revoked` | Отозванных сессий (вх.) | сессий | delta | sum |  |  | BarkFluff.Updates\Consumers\SessionRevokedConsumer.cs:20 |
| `subscriptions_active_total` | Активных подписок всего | подписок | gauge | avg |  | Датчик суммы открытых стримов всех типов; 0 на старте. | BarkFluff.Updates\Host\UpdatesApiService.cs:127 BarkFluff.Updates\Host\UpdatesApiService.cs:149 BarkFluff.Updates\Host\UpdatesApiService.cs:162 BarkFluff.Updates\Host\UpdatesApiService.cs:177 BarkFluff.Updates\Host\UpdatesApiService.cs:190 BarkFluff.Updates\Host\UpdatesApiService.cs:205 BarkFluff.Updates\Host\UpdatesApiService.cs:218 BarkFluff.Updates\Host\UpdatesApiService.cs:233 BarkFluff.Updates\Host\UpdatesApiService.cs:246 BarkFluff.Updates\Host\UpdatesApiService.cs:261 BarkFluff.Updates\Host\UpdatesApiService.cs:274 BarkFluff.Updates\Host\UpdatesApiService.cs:289 BarkFluff.Updates\Host\UpdatesApiService.cs:302 BarkFluff.Updates\Host\UpdatesApiService.cs:317 BarkFluff.Updates\Host\UpdatesApiService.cs:330 BarkFluff.Updates\Host\UpdatesApiService.cs:345 BarkFluff.Updates\Host\UpdatesApiService.cs:358 BarkFluff.Updates\Host\UpdatesApiService.cs:370 BarkFluff.Updates\Host\UpdatesApiService.cs:381 BarkFluff.Updates\Host\UpdatesApiService.cs:393 BarkFluff.Updates\Host\UpdatesApiService.cs:404 BarkFluff.Updates\Host\UpdatesApiService.cs:416 BarkFluff.Updates\Host\UpdatesApiService.cs:427 BarkFluff.Updates\Host\UpdatesApiService.cs:439 BarkFluff.Updates\Host\UpdatesApiService.cs:450 BarkFluff.Updates\Host\UpdatesApiService.cs:462 BarkFluff.Updates\Host\UpdatesApiService.cs:473 BarkFluff.Updates\Host\UpdatesApiService.cs:485 BarkFluff.Updates\Host\UpdatesApiService.cs:500 BarkFluff.Updates\Host\UpdatesApiService.cs:512 BarkFluff.Updates\Host\UpdatesApiService.cs:525 BarkFluff.Updates\Host\UpdatesApiService.cs:537 BarkFluff.Updates\Host\UpdatesApiService.cs:550 BarkFluff.Updates\Host\UpdatesApiService.cs:562 BarkFluff.Updates\Program.cs:224 |

### BarkFluff.Users

Контейнер: `users`; профиль: ImmediateByDefault.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `badge_lookups` | Запросов значков | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:212 BarkFluff.Users\Host\UsersServerApiService.cs:230 |
| `badges_assigned` | Назначений значков | значков | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:179 |
| `badges_created` | Создано значков | значков | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:217 |
| `badges_deleted` | Удалено значков | значков | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:256 |
| `badges_priority_updated` | Обновлений приоритета значков | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:204 |
| `badges_removed` | Снятий значков | значков | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:192 |
| `badges_updated` | Обновлений значков | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:241 |
| `bot_users_create_requests` | Запросов создания bot-пользователя | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:485 |
| `bot_users_delete_requests` | Запросов удаления bot-пользователя | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:496 |
| `chat_folder_chat_adds` | Добавлений чата в папку | операций | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:418 |
| `chat_folder_chat_removes` | Удалений чата из папки | операций | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:429 |
| `chat_folder_creates` | Созданий папок чатов | операций | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:385 |
| `chat_folder_deletes` | Удалений папок чатов | операций | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:411 |
| `chat_folder_lookups` | Запросов папок чатов | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:379 |
| `chat_folder_reorders` | Сортировок папок чатов | операций | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:459 |
| `chat_folder_updates` | Обновлений папок чатов | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:396 |
| `chat_mute_lookups` | Запросов заглушений чата | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:303 BarkFluff.Users\Host\UsersServerApiService.cs:369 |
| `chat_mute_toggles` | Переключений заглушений чата | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:288 |
| `contact_lookups` | Просмотров контактов | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:141 |
| `data_export_duration_ms_total` | Суммарная длительность экспорта | мс | delta | sum |  | мс. | BarkFluff.Users\Host\UsersServerApiService.cs:277 |
| `data_export_errors` | Ошибок экспорта | ошибок | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:283 |
| `data_exports` | Экспортов данных | выгрузок | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:267 |
| `db_migration_healthy` | Исправность БД | флаг | gauge | last |  | 0 при старте, 1 после успешных миграций. | BarkFluff.Users\Program.cs:113 BarkFluff.Users\Program.cs:120 |
| `device_app_info_updates` | Обновлений app-info устройства | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:308 |
| `device_deletions` | Удалений устройств | устройств | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:331 |
| `device_lookups` | Просмотров устройств | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:226 BarkFluff.Users\Host\UsersApiService.cs:233 BarkFluff.Users\Host\UsersServerApiService.cs:320 BarkFluff.Users\Host\UsersServerApiService.cs:419 |
| `device_lookups_all` | Списков всех устройств пользователя | списков | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:450 |
| `device_lookups_by_device_id` | Просмотров устройств по device id | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:431 |
| `device_registrations` | Регистраций устройств | устройств | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:292 |
| `device_renames` | Переименований устройств | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:240 |
| `drafts_create_errors` | Ошибок создания драфта | ошибок | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:107 |
| `drafts_create_requests` | Запросов создания драфта | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:96 |
| `drafts_created` | Создано драфтов | аккаунтов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:101 |
| `drafts_overridden` | Переопределений драфтов | аккаунтов | delta | sum |  | Пользователь уже есть, upsert. | BarkFluff.Users\Host\UsersServerApiService.cs:152 |
| `existence_checks` | Проверок существования пользователя | проверок | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:95 BarkFluff.Users\Host\UsersApiService.cs:105 BarkFluff.Users\Host\UsersServerApiService.cs:72 BarkFluff.Users\Host\UsersServerApiService.cs:80 |
| `federated_profile_hidden` | Федеративных профилей: скрыто приватностью | запросов | delta | sum |  |  | BarkFluff.Users\Features\GetFederatedProfile\GetFederatedProfileQueryHandler.cs:52 |
| `federated_profile_not_found` | Федеративных профилей: не найдено | запросов | delta | sum |  |  | BarkFluff.Users\Features\GetFederatedProfile\GetFederatedProfileQueryHandler.cs:44 |
| `federated_profile_requests` | Запросов федеративных профилей | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:528 |
| `federated_profile_served` | Отданных федеративных профилей | запросов | delta | sum |  |  | BarkFluff.Users\Features\GetFederatedProfile\GetFederatedProfileQueryHandler.cs:64 |
| `federated_resolves` | Резолв федеративного пользователя |  | delta | sum | result | Итоги: invalid, not_found, local, disabled, cache, unavailable, error, upsert_rejected, resolved. | BarkFluff.Users\Features\ResolveFederatedUser\ResolveFederatedUserQueryHandler.cs:47-132 |
| `federated_resolves_requests` | Запросов резолва федеративного пользователя | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:192 |
| `files_fetch_errors` | Ошибок запросов файлов к Files | ошибок | delta | sum |  |  | BarkFluff.Users\Features\ExportData\ExportDataCommandHandler.cs:230 BarkFluff.Users\Features\GetUserByUsername\GetUserByUsernameQueryHandler.cs:77 BarkFluff.Users\Features\SetProfilePicture\SetProfilePictureCommandHandler.cs:65 |
| `files_fetch_success` | Успешных запросов файлов к Files | запросов | delta | sum |  |  | BarkFluff.Users\Features\ExportData\ExportDataCommandHandler.cs:184 BarkFluff.Users\Features\GetUserByUsername\GetUserByUsernameQueryHandler.cs:72 BarkFluff.Users\Features\SetProfilePicture\SetProfilePictureCommandHandler.cs:61 |
| `firebase_token_clears` | Очисток FCM-токена | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:268 |
| `firebase_token_updates` | Обновлений FCM-токена | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:252 |
| `grpc_request_duration_ms_total` | Суммарная длительность gRPC-вызовов | мс | delta | sum |  | Время обработки вызовов (в т.ч. ошибочных), мс. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:37 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:43 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:63 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:78 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:94 |
| `grpc_requests_errors` | Ошибок gRPC-вызовов (неожиданная ошибка) | запросов | delta | sum |  | Иное исключение (сбойный баг/инфраструктура). | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:79 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:95 |
| `grpc_requests_failed` | Неудачных gRPC-вызовов (бизнес-ошибка) | запросов | delta | sum |  | BaseGrpcException - осмысленный доменный отказ. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:44 BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:64 |
| `grpc_requests_total` | Обработанных gRPC-вызовов | запросов | delta | sum |  | Входящий unary-вызов до обработки, общий интерсептор ServerExceptionInterceptor. | BarkFluff.GrpcServer\ServerExceptionInterceptor.cs:30 |
| `last_data_export_unix` | Последний экспорт | unix-с | gauge | last |  | Unix-время. | BarkFluff.Users\Host\UsersServerApiService.cs:278 |
| `last_device_registered_unix` | Последний регистрация устройства | unix-с | gauge | last |  | Unix-время. | BarkFluff.Users\Host\UsersServerApiService.cs:302 |
| `last_draft_created_unix` | Последний драфт | unix-с | gauge | last |  | Unix-время. | BarkFluff.Users\Host\UsersServerApiService.cs:102 |
| `last_session_revoked_unix` | Последний отзыв сессии | unix-с | gauge | last |  | Unix-время события. | BarkFluff.Users\Consumers\SessionRevokedConsumer.cs:24 |
| `last_user_confirmed_unix` | Последнее подтвержение | unix-с | gauge | last |  | Unix-время. | BarkFluff.Users\Host\UsersServerApiService.cs:120 |
| `last_user_search_unix` | Последний поиск | unix-с | gauge | last |  | Unix-время. | BarkFluff.Users\Host\UsersApiService.cs:180 |
| `legal_consents_accepted` | Принятых юридических согласий | согласий | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:321 |
| `login_lookups` | Просмотров по username | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:88 |
| `messages_fetch_errors` | Ошибок запросов сообщений к Messages | ошибок | delta | sum |  |  | BarkFluff.Users\Features\ExportData\ExportDataCommandHandler.cs:132 |
| `messages_fetch_success` | Успешных запросов сообщений к Messages | запросов | delta | sum |  | ExportData и аватарные запросы. | BarkFluff.Users\Features\ExportData\ExportDataCommandHandler.cs:78 |
| `notifications_toggles` | Переключений включённости уведомлений | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:275 |
| `one_time_prekey_replenishments` | Пополнений one-time prekey | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:495 |
| `peer_device_listings` | Списков устройств пира | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:489 |
| `personalization_updates` | Обновлений персонализации | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:333 |
| `prekey_bundle_fetches` | Получений prekey-наборов | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:479 |
| `prekey_bundle_registrations` | Регистраций prekey-наборов | регистраций | delta | sum |  | E2E-криптография. | BarkFluff.Users\Host\UsersApiService.cs:472 |
| `privacy_updates` | Обновлений настроек приватности | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:314 |
| `profile_avatar_removals` | Удалений аватара | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:80 |
| `profile_avatar_updates` | Обновлений аватара | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:82 BarkFluff.Users\Host\UsersServerApiService.cs:406 |
| `profile_bio_updates` | Обновлений описания (bio) | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:140 |
| `profile_name_updates` | Обновлений имени | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:113 |
| `profile_poster_lookups` | Запросов постеров | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:479 |
| `profile_poster_updates` | Обновлений posters | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:369 BarkFluff.Users\Host\UsersServerApiService.cs:469 |
| `profile_updates_server` | Обновлений профилей (сервер API) | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:456 |
| `profile_username_updates` | Обновлений username | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:127 |
| `public_profile_hidden` | Публичных профилей: скрыто приватностью | запросов | delta | sum |  |  | BarkFluff.Users\Features\GetUserByUsername\GetUserByUsernameQueryHandler.cs:50 |
| `public_profile_not_found` | Публичных профилей: не найдено | запросов | delta | sum |  |  | BarkFluff.Users\Features\GetUserByUsername\GetUserByUsernameQueryHandler.cs:40 |
| `public_profile_views` | Просмотров публичного профиля | просмотров | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:358 |
| `remote_users_upsert_rejected.invalid_uuid` | Upsert remote: неверный UUID | запросов | delta | sum |  |  | BarkFluff.Users\Features\UpsertRemoteUsers\UpsertRemoteUsersCommandHandler.cs:37 |
| `remote_users_upsert_rejected.local_uuid_collision` | Upsert remote: коллизия с локальным UUID | запросов | delta | sum |  |  | BarkFluff.Users\Features\UpsertRemoteUsers\UpsertRemoteUsersCommandHandler.cs:64 |
| `remote_users_upsert_rejected.server_name_mismatch` | Upsert remote: несоответствие сервера | запросов | delta | sum |  |  | BarkFluff.Users\Features\UpsertRemoteUsers\UpsertRemoteUsersCommandHandler.cs:73 |
| `remote_users_upsert_requests` | Запросов upsert remote-пользователей | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:541 |
| `service_started_unix` | Время старта сервиса | unix-с | gauge | last |  | Датчик, фиксируется один раз при старте процесса; перезапуски видны по скачкам. | BarkFluff.Users\Program.cs:112 |
| `session_revoked_received` | Получено событий отзыва сессии | событий | delta | sum |  | SessionRevokedConsumer. | BarkFluff.Users\Consumers\SessionRevokedConsumer.cs:18 |
| `signed_prekey_rotations` | Ротаций signed prekey | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:501 |
| `storage_limit_updates` | Обновлений лимита хранилища | обновлений | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:394 |
| `user_avatar_changed_published` | Опубликовано событие смены аватара | событий | delta | sum |  |  | BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:59 |
| `user_bio_changed_published` | Опубликовано событие смены bio | событий | delta | sum |  |  | BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:84 |
| `user_events_published` | Опубликовано событий пользователя | событий | delta | sum |  | UserInfoQueueSender: смена данных пользователя. | BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:31 BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:44 BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:58 BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:70 BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:83 |
| `user_lookups` | Просмотров профилей | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:72 BarkFluff.Users\Host\UsersServerApiService.cs:132 BarkFluff.Users\Host\UsersServerApiService.cs:166 |
| `user_name_changed_published` | Опубликовано событие смены имени | событий | delta | sum |  |  | BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:32 |
| `user_password_changed_published` | Опубликовано событие смены пароля | событий | delta | sum |  |  | BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:71 |
| `user_search_duration_ms_total` | Суммарная длительность поиска | мс | delta | sum |  | мс. | BarkFluff.Users\Host\UsersApiService.cs:179 |
| `user_search_errors` | Ошибок поиска | ошибок | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:185 |
| `user_searches` | Поисков пользователей | поисков | delta | sum |  |  | BarkFluff.Users\Host\UsersApiService.cs:150 BarkFluff.Users\Host\UsersServerApiService.cs:381 |
| `user_username_changed_published` | Опубликовано событие смены username | событий | delta | sum |  |  | BarkFluff.Users\Infrastructure\UserInfoQueueSender.cs:45 |
| `users_by_uuid_lookups` | Запросов по UUID | запросов | delta | sum |  |  | BarkFluff.Users\Features\GetUsersByUuid\GetUsersByUuidQueryHandler.cs:33 |
| `users_confirm_errors` | Ошибок подтверждения аккаунта | ошибок | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:125 |
| `users_confirm_requests` | Запросов подтверждения аккаунта | запросов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:114 |
| `users_confirmed` | Подтверждённых аккаунтов | аккаунтов | delta | sum |  |  | BarkFluff.Users\Host\UsersServerApiService.cs:119 |
| `users_email_conflicts` | Конфликтов email при создании драфта | конфликтов | delta | sum |  | AddDraftUser. | BarkFluff.Users\Features\AddDraftUser\AddDraftUserCommandHandler.cs:73 |
| `users_reserved_username_blocked` | Отказов: username зарезервирован | попыток | delta | sum |  |  | BarkFluff.Users\Features\AddDraftUser\AddDraftUserCommandHandler.cs:92 |
| `users_username_conflicts` | Конфликтов username | конфликтов | delta | sum |  |  | BarkFluff.Users\Features\AddDraftUser\AddDraftUserCommandHandler.cs:103 |

### BarkFluff.Web

Контейнер: `web`; профиль: BufferAll.

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `http_requests_errors` | HTTP-ошибок веб-интерфейса | запросов | delta | sum |  | Исключения при обработке. | BarkFluff.Web\Program.cs:173 |
| `http_requests_total` | Обработанных HTTP-запросов веб-интерфейса | запросов | delta | sum |  | Middleware Program.cs: incremented при приёме. | BarkFluff.Web\Program.cs:160 |

### BarkFluff.WebServer

Контейнер: `webserver`; профиль: ImmediateByDefault (+buffered: http_requests_total, http_requests_errors).

| Метрика | Название | Unit | kind | agg | Лейблы | Описание | Код |
|---|---|---|---|---|---|---|---|
| `http_requests_errors` | HTTP-ошибок веб-сервера | запросов | delta | sum |  |  | Barkfluff.WebServer\Program.cs:69 |
| `http_requests_total` | Обработанных HTTP-запросов веб-сервера | запросов | delta | sum |  |  | Barkfluff.WebServer\Program.cs:65 |
| `installer_downloads` | Скачиваний установщика | скачиваний | delta | sum |  | DownloadController. | Barkfluff.WebServer\Controllers\DownloadController.cs:29 |
| `support_requests` | Обращений в поддержку (web) | запросов | delta | sum |  | SupportChatController. | Barkfluff.WebServer\Controllers\SupportChatController.cs:36 |

## Импорт (файлы)

| Файл | Сервис | Метрик |
|---|---|---|
| `import/BarkFluff.Beacon.json` | BarkFluff.Beacon | 17 |
| `import/BarkFluff.Bots.json` | BarkFluff.Bots | 24 |
| `import/BarkFluff.Calls.json` | BarkFluff.Calls | 20 |
| `import/BarkFluff.ClientStorage.json` | BarkFluff.ClientStorage | 7 |
| `import/BarkFluff.CloudMessaging.json` | BarkFluff.CloudMessaging | 6 |
| `import/BarkFluff.Developers.json` | BarkFluff.Developers | 4 |
| `import/BarkFluff.FastAuth.json` | BarkFluff.FastAuth | 11 |
| `import/BarkFluff.Federation_part1.json` | BarkFluff.Federation | 64 |
| `import/BarkFluff.Federation_part2.json` | BarkFluff.Federation | 8 |
| `import/BarkFluff.Files.json` | BarkFluff.Files | 23 |
| `import/BarkFluff.Identity_part1.json` | BarkFluff.Identity | 64 |
| `import/BarkFluff.Identity_part2.json` | BarkFluff.Identity | 8 |
| `import/BarkFluff.Messages_part1.json` | BarkFluff.Messages | 64 |
| `import/BarkFluff.Messages_part2.json` | BarkFluff.Messages | 6 |
| `import/BarkFluff.Navigator.json` | BarkFluff.Navigator | 6 |
| `import/BarkFluff.Notification.json` | BarkFluff.Notification | 4 |
| `import/BarkFluff.Onliner.json` | BarkFluff.Onliner | 48 |
| `import/BarkFluff.Settings.json` | BarkFluff.Settings | 26 |
| `import/BarkFluff.Updates_part1.json` | BarkFluff.Updates | 64 |
| `import/BarkFluff.Updates_part2.json` | BarkFluff.Updates | 64 |
| `import/BarkFluff.Updates_part3.json` | BarkFluff.Updates | 4 |
| `import/BarkFluff.Users_part1.json` | BarkFluff.Users | 64 |
| `import/BarkFluff.Users_part2.json` | BarkFluff.Users | 38 |
| `import/BarkFluff.Web.json` | BarkFluff.Web | 2 |
| `import/BarkFluff.WebServer.json` | BarkFluff.WebServer | 4 |

## Проверки генератора

- Каждое правило: regex компилируется (.NET; синтаксис совместим с Rust regex), матчит синтетическую пару `("имя": 12)` (для семей — с примером лейбла), не матчит `имя_unrelated`.
- contains 1..256, pattern ≤4096, name/title/desc/unit в пределах канона; value_group и label-группы у семей.
- Каждый статический имён из `metric_usages.txt` покрыт описанием; дубли исключены; динамические имена покрыты семьями.
- JSON проверен схемой Lognara (metrics-schema.json, maxItems 64).
- Проверено на dotnet-пробе (metricprobe): реальный рендер `ServiceMetrics { SchemaVersion: 2, ServiceName: ..., Counters: [("имя": N), ...], Gauges: [...], Timestamp: ... }` соответствует шаблону.
