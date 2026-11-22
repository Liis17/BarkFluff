# Barkfluff.AdminPanel

Веб-панель управления платформой. ASP.NET Minimal API на внутреннем HTTP-порту 51888; UI находится в Pages/v2/. Детали структуры: [[Backend/AdminPanel-ProjectMap]] и [[Backend/AdminPanel-Files]].

## Вход и роли

- Для bootstrap требуется ровно одна запись Telegram:Admins — неизменяемый Owner. Других администраторов добавляют по приглашению; роли и привязка к Telegram ID хранятся в LiteDB.
- Вход по имени администратора создаёт временный запрос (по умолчанию 10 минут). Telegram-бот адресует его выбранному активному администратору; после одобрения создаётся токен панели.
- API и защищённые страницы проверяют cookie auth_token. Токен в LiteDB продлевает LastActivity при использовании и истекает после 3 дней без активности (Auth:TokenExpirationDays). Login.html записывает cookie из JavaScript на 7 дней, поэтому cookie не HttpOnly; срок серверного токена короче.
- Viewer — пустой базовый набор ролей. Support управляет чтением/сессиями/паролями пользователей и почтой; ContentAdmin — контентом, ботами, зарезервированными именами, S3 просмотром и уведомлениями; OperationsAdmin — Docker, удалёнными серверами, конфигурацией и удалением Seq; SecurityAdmin — 2FA, federation, админами, аудитом и соответствующими пользовательскими действиями. Owner разрешено всё. Источник матрицы: Models/AdminPermissions.cs.
- Опасные операции дополнительно требуют step-up через Telegram. Подтверждение связано с токеном сессии, ключом действия и хэшем параметров; одноразовое, ожидание и окно после одобрения — по 5 минут. Состояние хранится в памяти процесса.

## Управление статусом аккаунта

`users.block` и `users.delete` разрешены только SecurityAdmin и Owner. Маршруты POST `/api/users/{id}/block`, POST `/unblock`, POST `/deletion` и DELETE `/deletion` проверяют обязательную причину (до 500 символов), привязывают к Telegram step-up `userId`, действие и причину и делегируют изменение `IdentityServerApi.SetAccountRestrictionServer`.

GET `/api/users` переводит `status` в набор `status_filters`: `all` (и пустой) — Active и Blocked, `blocked`, `pending_deletion` и `deleted` — один статус. Аккаунты «В удалении» и «Удалённые» видны только своими фильтрами, в том числе при поиске по ID в списке. Карточка `GET /api/users/{id}` набором не ограничивается. Users применяет набор до пагинации и считает общий total по той же выборке. Список и карточка пользователя показывают статус, причину, время ограничения и дату окончательного удаления. Tombstone со статусом Deleted отображается без действий. UI и JSON-маршруты находятся в `Pages/v2/users.html` и `Endpoints/UsersEndpoints.cs`.

## Тестовая отправка push

Администратор один раз привязывает BarkFluff user ID к своей записи (`AdminRecord.BarkFluffUserId` в LiteDB; поле необязательное, старые записи читаются без привязки). Привязка проверяется через Users: пользователь должен существовать, ответ содержит его имя и устройства с признаком включённых push. Bootstrap Owner сохраняет запись целиком, поэтому привязку не затирает. Логика — в `Services/NotificationTestService.cs`, маршруты `/api/notifications/test-recipient` и `/api/notifications/test` — в `Endpoints/NotificationsEndpoints.cs`; права — `notifications.manage`, step-up не требуется.

Тестовая отправка берёт устройства привязанного пользователя из Users и публикует `AdminBroadcastNotificationEvent` с их `TargetDeviceIds`. Пустой список в этом событии означает рассылку на все устройства, поэтому панель отказывает до публикации, если устройств нет. Недоступность Users возвращает 503. Ответ «принято к отправке» означает постановку в очередь, а не доставку: см. [[Backend/CloudMessaging]]. Тестовая отправка и привязка не пишутся в аудит — как и остальные рассылки, аудит панели покрывает действия над администраторами.

## Журнал аудита

Записи хранятся в LiteDB (`Services/AuditService.cs`, `Data/AuditDbContext.cs`) и удаляются старше 90 дней. GET `/api/audit` (permission AuditRead) отдаёт страницу новых сверху: `limit` и курсор `beforeUtc`. Необязательные фильтры:

- `admin` — username без «@», точное совпадение без учёта регистра;
- `action` — подстрока ключа действия без учёта регистра; проверяется в памяти, потому что LiteDB транслирует Contains в LIKE, где `%` и `_` стали бы шаблонами;
- `outcome` — `success` (ok или confirmed) либо точное значение без учёта регистра, например `invalid_confirmation`, `approved`, `rejected`;
- `fromUtc` — включительно, `toUtc` — исключительно; `fromUtc` позже `toUtc` возвращает 400. Значения длиннее 200 символов тоже 400.

Admin, outcome и период применяются в запросе LiteDB до лимита вместе с курсором, поэтому «Загрузить ещё» продолжает ту же отфильтрованную выборку. Курсор сравнивает только `At`: записи с тем же временем, что и граница страницы, могут не попасть в выдачу. GET `/api/audit/facets` (AuditRead) отдаёт уникальных администраторов и ключи действий для подсказок. Страница `Pages/v2/audit.html` хранит фильтры в query string.

## Ссылки на текущую выборку

`Pages/v2/logs.html`: кнопка «Скопировать ссылку» строит абсолютный `/logs` из сервиса, уровня (только в режиме событий), поиска, одного контекста (`correlationId`, `requestId` или `userId`), `view=groups` и периода `from`. `buildFilterQuery` и `parseFilterQuery` задают один набор параметров для ссылки и для восстановления фильтров при открытии. Без `navigator.clipboard` (http) копирование идёт через textarea.

`Pages/v2/users.html`: `history.replaceState` пишет `q`, `status` (не пишется для `all`), `page` — число подгруженных порций по 50 — и `userId` открытой карточки. Закрытие карточки убирает только `userId`. Users ограничивает `size` 50, поэтому страница восстанавливает порции последовательными запросами, не более 20. Старая ссылка `/users?userId=N` без `q` ищет по id и открывает карточку.
>
## Управление сервисом

При запуске `Program.cs` вызывает `LoadConfiguration(ServiceId.Unknown, requireValidSettings: false)`.
Панель ожидает доступности таблиц [[Backend/Settings]], но не валидности обязательных ручных
значений, чтобы сохранять возможность их исправления. Лимит попыток и загрузка конфигурации
описаны в [[Backend/GrpcServer]]. Остальные проверки собственных настроек панели сохраняются.

Endpoint-группы в Program.cs охватывают auth/step-up/admins/audit, Docker и удалённые серверы, конфигурацию, Seq/логи/метрики, пользователей/чаты/файлы, badges/stickers, bots/federation, reserved names, notifications, mail и S3. Точные маршруты и permission checks — в Endpoint-файлах.

## Бейджи профиля

`Endpoints/BadgesEndpoints.cs` управляет каталогом бейджей через UsersServerApi. Создание требует multipart-поля `image` (FULL) и `miniImage` (MINI); Files проверяет PNG, точные размеры и лимит 256 KiB, а `InvalidArgument` возвращается как HTTP 400 с сообщением. При обновлении новые файлы опциональны: без загрузки сохраняются текущие URL, включая необязательный `miniImageUrl` в JSON. Страница `Pages/v2/badges.html` готовит пару из одной картинки через центральный квадратный crop и ступенчатое уменьшение на canvas либо проверяет две отдельные PNG-картинки; обе версии показываются в превью и карточках.

Создание загружает FULL до MINI. Если Files отклонит MINI, загруженный FULL остаётся сиротским объектом в S3; автоматической очистки в этом потоке нет.

`Endpoints/UsersEndpoints.cs` включает `miniImageUrl` и `isPrimary` в сведения о назначенных бейджах пользователя. Страница `Pages/v2/users.html` показывает основной бейдж только для чтения; AdminPanel не меняет выбор пользователя. Назначение и снятие бейджа доступны только с `badges.manage` и требуют Telegram step-up, привязанный к пользователю/бейджу. Обновление и удаление бейджа также требуют step-up, поскольку деактивация или удаление может изменить назначенные пользователям связи.

Панель опрашивает liveness/readiness, Docker, Seq и инфраструктуру для своей страницы health; собственный HTTP endpoint — /api/health/overview. Это отдельный сборщик, не общий readiness из [[Backend/GrpcServer]]. `HealthCollectorService` опрашивает каждые 30 секунд. Имена контейнеров для health сопоставляются с Compose-сервисами текущего проекта; при отключённом Seq остаются доступны Docker-статусы и HTTP-пробы, но отсутствуют данные о свежести логов. `MetricsCollectorService` строит часовые rollups из Seq каждые 5 минут: общая статистика хранится 24 часа, ServiceMetrics — 30 дней; восстановление пробелов идёт до 72 часов, по 6 часов за цикл. `MetricsCollectorService` и `MetricsLogCompressorService` пропускают Seq-запросы, когда Seq отключён.

`DockerContainerNameResolver` связывает фактические имена контейнеров с Compose service keys. Он читает `com.docker.compose.service` и `com.docker.compose.project` из строкового поля `Labels` вывода `docker ps --format json`; проект панели определяется по labels контейнера из `$HOSTNAME`, с fallback на `admin-panel`. Список контейнеров и проект кэшируются до 10 секунд и инвалидируются после локальных Docker-изменений. Резолвер исключает контейнеры других проектов. Сервисы Compose остаются `postgres`, `admin-panel`, `rabbitmq` и т. д.; для старого nightly-стека сохранён legacy-алиас `postgres` → `postgres_barkfluff`, остальные прежние имена соответствуют Compose service key.

Массовые обновление и перезапуск используют `DeployJobService.DeployOrder`: все 16 сервисов из `PlatformServiceRegistry.BarkFluff`, Settings первым; инфраструктура и сама панель в этот список не входят. «Обновить доступные» передаёт поле `container` строк с `updateAvailable` для BarkFluff-сервисов в `/api/docker/containers/update-many`; сервер разрешает фактические имена в Compose service keys.

История задач деплоя хранится в LiteDB-файле `db/deploy_jobs.db` (`Data/DeployJobDbContext.cs`), рядом с остальными данными панели; путь не настраивается и попадает в смонтированный `/app/db`. `DeployJobService` пишет снимок задачи при постановке в очередь, при старте и завершении каждого шага и при завершении задачи; опрос health в файл не пишется. При старте панели история загружается: задачи в Queued/Running помечаются Failed с причиной «Прервано перезапуском панели» (шаги Pending/InProgress тоже Failed) и в очередь повторно не ставятся. Хранятся не больше 20 завершённых задач, лишние удаляются и из файла. Ошибка чтения или записи истории только логируется и не прерывает деплой. `GET /api/docker/deploy/jobs` отдаёт историю из памяти процесса, которая поднята из файла при старте; страница «Сервисы» показывает её в блоке «Недавние операции», а клик открывает результат задачи (упавший шаг, причина, состояние отката) без повторного запуска действий.

Nightly compose запускает панель от root и монтирует /var/run/docker.sock, данные /app/db, compose-файл и .env. Поэтому панель имеет доступ к Docker host. Deploy/update helper читает `com.docker.compose.project.config_files` активного контейнера AdminPanel и повторно передаёт все Compose-файлы, включая opt-in overlays, для pull/up и отката; каталоги файлов монтируются в helper read-only. Compose-вызовы используют `--project-name barkfluff` и имена сервисов, а `inspect`, restart и health-check разрешают фактическое имя контейнера. В исходнике cookie создаётся клиентским JS; не предполагайте HttpOnly.

`Seq:Enabled` по умолчанию `true`; переменная окружения `Seq__Enabled=false` отключает интеграцию и позволяет запускать панель без URL Seq. Seq-зависимые endpoints возвращают HTTP 503 с `available: false` и сообщением `Seq отключён`; `/api/seq/services` и `/api/seq/services/status` остаются доступны, чтобы фильтры и список сервисов работали без Seq. Страница логов отображает состояние отключения явно.

Сборка: dotnet build Backend/Barkfluff.AdminPanel/Barkfluff.AdminPanel.csproj. Конфигурация по умолчанию — Backend/Barkfluff.AdminPanel/appsettings.json.

Дашборд `Pages/v2/dashboard.html` показывает проблемные сервисы (`down`, `degraded`, `unknown`) первыми, в порядке тяжести; внутри группы порядок снимка сохраняется. Заголовок «Состояние сервисов» показывает `generatedAtUtc` снимка и обновляется при каждом опросе. Для проблемного сервиса доступна ссылка «Открыть логи»: `/logs` с `service`, `level=Error` и `from` — время снимка минус час. Снимок содержит `SeqEnabled` (значение `Seq:Enabled` на момент сбора); при `false` вместо логов ссылка ведёт в «Сервисы» на строку сервиса (подсветка через `focusLinkedService`).

Страница логов `Pages/v2/logs.html` принимает `from` (ISO UTC с часовым поясом; иное значение игнорируется). Значение уходит в `/api/seq/events` как `fromUtc` и показывается рядом с фильтрами с кнопкой сброса. Период применяется только к событиям: в режиме групп ошибок он не работает, верхней границы у периода нет.

## Логирование

Собственные логи AdminPanel подключены к `AddBarkFluffSerilog("BarkFluff.AdminPanel")` после загрузки Settings и повторного наложения env; применяются общие request trace scopes и независимые root-level flags `ConsoleOutput`, `SeqEnable`, `LognaraEnable`. Панель использует `ServiceId.Unknown`, поэтому flags sinks задаются локально; `Lognara:AgentUrl` может поступить из global Settings. Defaults, priority и lifecycle embedded agent описаны в [[Backend/GrpcServer]]. Чтение/сжатие логов и кеширование метрик панели остаются Seq-интеграцией. При Lognara-only новые сервисные логи и метрики видны в Lognara Dock; переключение UI панели на Lognara не реализовано.
