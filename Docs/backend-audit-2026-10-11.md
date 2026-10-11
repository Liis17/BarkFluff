# Сводный аудит backend BarkFluff

Дата сохранения отчёта: **2026-10-11**.

Срез исходного аудита: ветка **nightly**, коммит **e65b34a5**, проверка от **2026-10-07**. Документ сохраняет результаты аудита из разговора; актуальность находок определяется этим срезом кода.

Область проверки: backend, shared-библиотеки, Docker/nginx/CI, контекст Obsidian и аудиты в Docs. Исходники при аудите не изменялись.

Самые серьёзные находки: **подмена закреплённых серверов Navigator, доступ к чужим файлам, перепривязка устройств между аккаунтами, неполный отзыв сессий и потеря офлайн-сообщений секретных чатов**.

## Приоритеты и трактовка результатов

- **P1** — высокий приоритет: первоочередное исправление.
- **P2** — существенные ошибки и риски.
- **P3** — менее опасные дефекты, технический долг, кандидаты для оптимизации и функциональные ограничения.

Безусловная критическая уязвимость уровня P0 в этой проверке не подтверждена. Условия эксплуатации приведены у соответствующих находок. Принятые архитектурные риски и незавершённые функции обозначены отдельно от подтверждённых ошибок.

Ссылки на код относительны к расположению этого файла. Номера строк относятся к указанному срезу аудита.

## P1 — Безопасность

- **Navigator: анонимный пользователь может изменить адреса закреплённого сервера.** Публичный список раскрывает комбинацию имени и Beacon host/port. Анонимная регистрация без ServerName использует её для upsert существующей записи и перезаписывает Web/Files endpoints, сохраняя IsManual. Это позволяет перенаправить клиентов на чужой сервер. Нужно запретить анонимное обновление ручных записей и проверять владельца при изменении адресов. [RegisterServerCommandHandler.cs](../Backend/BarkFluff.Navigator/Features/RegisterServer/RegisterServerCommandHandler.cs#L152), [ServersStorage.cs](../Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs#L38).

- **Files: выдача временной ссылки на локальный файл без проверки доступа.** Авторизованный пользователь, получив GUID чужого вложения, может создать ссылку и скачать файл вне своего чата. Проверка членства есть для федеративных файлов, но отсутствует для локальных. Проверять право доступа нужно перед выдачей временного идентификатора. [GetTempDownloadUrlCommandHandler.cs](../Backend/BarkFluff.Files/Features/GetTempDownloadUrl/GetTempDownloadUrlCommandHandler.cs#L40).

- **Users/Identity: устройство можно перепривязать к другому аккаунту.** ListPeerDevices позволяет получить GUID устройств указанного пользователя. Затем атакующий входит в собственный аккаунт с этим x-device-id; глобальный upsert устройства заменяет UserId без проверки прежнего владельца. Нарушаются управление устройством, prekey-регистрация и маршрутизация уведомлений. **Расшифровка чужих E2E-сообщений этим способом не подтверждена.** Нужны запрет смены владельца и доказательство владения устройством. [UsersApiService.cs](../Backend/BarkFluff.Users/Host/UsersApiService.cs#L487), [DevicesStorage.cs](../Backend/BarkFluff.Users/Persistence/Services/DevicesStorage.cs#L18).

- **FastAuth: готовые токены повторно выдаются по идентификатору из QR.** Подписка анонимная, а завершённый результат возвращается до проверки единственного подписчика. Знающий QR/session ID может получить access и refresh токены в течение 30 секунд после подтверждения входа. Требуются отдельный секрет подписчика, привязка клиента и однократная выдача результата. В прежнем аудите это принято как риск; поведение сохраняется. [GenerateFastAuthTokenCommandHandler.cs](../Backend/BarkFluff.FastAuth/Features/GenerateFastAuthToken/GenerateFastAuthTokenCommandHandler.cs#L55), [SubscribeFastAuthResultQueryHandler.cs](../Backend/BarkFluff.FastAuth/Features/SubscribeFastAuthResult/SubscribeFastAuthResultQueryHandler.cs#L25).

- **Сброс пароля не прекращает ранее выданные сессии.** Старые refresh-токены сохраняются после восстановления пароля; обычная смена и административная установка пароля также не отзывают их. Украденный refresh продолжает выдавать access-токены после восстановления аккаунта. Нужен атомарный отзыв прежних сессий с публикацией события отзыва. [ConfirmResetPasswordCommandHandler.cs](../Backend/BarkFluff.Identity/Features/ConfirmResetPassword/ConfirmResetPasswordCommandHandler.cs#L211), [SetPasswordCommandHandler.cs](../Backend/BarkFluff.Identity/Features/SetPassword/SetPasswordCommandHandler.cs#L74).

- **Отзыв access-токенов теряется после перезапуска сервиса.** Запреты хранятся только в памяти процесса. После рестарта ранее отозванный JWT снова проходит проверку до окончания срока действия — по стандартному TTL до 60 минут. Нужны устойчивое состояние отзыва либо версия сессии с проверкой авторитетного хранилища. [TokenRevocationCache.cs](../Backend/BarkFluff.GrpcServer/XAuth/TokenRevocationCache.cs#L5), [XAuthExtensions.cs](../Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs#L46).

- **Отзыв сессии не закрывает уже открытые потоки.** Updates и Onliner обновляют кэш запретов, но активные подписки продолжают получать сообщения, присутствие и другие события. В Calls отзыв также не завершает поток событий. Проверка JWT при открытии потока не обеспечивает последующий отзыв доступа. Нужна отмена активных подписок по идентификатору сессии. [Updates SessionRevokedConsumer.cs](../Backend/BarkFluff.Updates/Consumers/SessionRevokedConsumer.cs#L15), [Onliner subscription handler](../Backend/BarkFluff.Onliner/Features/SubscribeToOnlineStatus/SubscribeToOnlineStatusQueryHandler.cs#L71).

- **Settings: чтение секретов и изменение конфигурации без авторизации.** Основной gRPC API не использует XAuth; доступны чтение всех конфигураций, обновление и rollback. Среди значений находятся JWT signing key и сервисные токены. **Условие: доступ к внутренней Docker-сети; в проверенном compose порт Settings наружу не опубликован.** Нужны аутентификация сервисов, ограничения по операциям и scope, отдельная обработка секретов. [Program.cs](../Backend/BarkFluff.Settings/Program.cs#L72), [SettingsApiService.cs](../Backend/BarkFluff.Settings/Host/SettingsApiService.cs#L25), [SettingsStorage.cs](../Backend/BarkFluff.Settings/Persistence/Services/SettingsStorage.cs#L56).

- **Любой сервисный JWT даёт доступ к привилегированным операциям Identity над любым пользователем.** Политика проверяет только TokenType.Service, без разрешённого вызывающего сервиса. Имея токен одного сервиса, можно вызвать создание пользовательской сессии или принудительную установку пароля для указанного UserId. Требуются разрешения конкретных сервисов на конкретные RPC. [XAuthExtensions.cs](../Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs#L74), [IdentityServerApiService.cs](../Backend/BarkFluff.Identity/Host/IdentityServerApiService.cs#L85).

- **Общий симметричный JWT-ключ позволяет скомпрометированному сервису выпускать токены.** Проверяющие сервисы получают тот же HMAC-ключ, которым Identity подписывает JWT. Чтение ключа из одного процесса позволяет создавать произвольные пользовательские и сервисные claims для остальных валидаторов. Это отдельный архитектурный риск. Нужна асимметричная подпись с приватным ключом только у издателя. [JwtService.cs](../Backend/BarkFluff.Identity/Services/JwtService.cs#L64), [XAuthExtensions.cs](../Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs#L23).

- **Notification: отключена проверка сертификата SMTP.** Глобальный callback принимает любой TLS-сертификат. При атаке на сетевой маршрут возможен перехват SMTP-пароля, OTP и писем восстановления. Удалить trust-all callback и проверять сертификат сервера. [EmailSender.cs](../Backend/BarkFluff.Notification/Senders/EmailSender.cs#L37).

- **AdminPanel: SSH-клиент не проверяет ключ удалённого хоста.** Нет проверки fingerprint или known_hosts; используемая библиотека по умолчанию доверяет предложенному ключу. При подмене маршрута/DNS или MITM атакующий может получить SSH-пароль и вмешаться в команды. Нужна проверка ключа с отказом при изменении. [SshNetRemoteSshClient.cs](../Backend/Barkfluff.AdminPanel/Services/SshNetRemoteSshClient.cs#L16), [исходник SSH.NET](https://raw.githubusercontent.com/sshnet/SSH.NET/2025.1.0/src/Renci.SshNet/Common/HostKeyEventArgs.cs).

- **AdminPanel: компрометация процесса означает доступ к Docker-хосту и проектным секретам.** Панель работает как root, имеет сырой docker.sock, compose с правом записи и полный .env. Это намеренная граница доверия функции администрирования, однако последствия взлома процесса практически равны компрометации хоста. Снижать риск следует через отдельный ограниченный управляющий агент и уменьшение доступного набора секретов. [docker-compose.yml](../Docker/nightly/barkfluff/docker-compose.yml#L227).

## P1 — Потеря доставки и согласованности

- **Секретные сообщения и начальные приглашения нельзя восстановить после офлайн-доставки.** Конверты сохраняются в Redis на 24 часа, но методы чтения ожидающих сообщений/приглашений нигде не вызываются; соответствующего RPC нет. При отсутствии Updates-потока доставка пропускается, push содержит только метаданные. После подключения конверт не переотправляется, затем истекает TTL. Нужен API получения ожидающих конвертов или replay при подписке с подтверждением получения. [SecretMessageBuffer.cs](../Backend/BarkFluff.Messages/Persistence/Services/SecretMessageBuffer.cs#L96), [NewSecretMessageNotificationHandler.cs](../Backend/BarkFluff.Updates/Features/SubscribeSecretMessages/Handlers/NewSecretMessageNotificationHandler.cs#L31), [messages_api.proto](../Shared/BarkFluff.Proto/messages_api.proto#L124).

- **Импорт федеративного сообщения может сохранить неполное состояние и подавить повторные попытки.** Сообщение фиксируется раньше catch-up записи и публикации локального события. При сбое после commit повторный импорт обнаруживает существующее сообщение и возвращает успех, пропуская недостающие действия. Сообщение остаётся в БД, но уведомление и catch-up могут потеряться. Нужны общая транзакция с outbox либо восстановление незавершённых действий при повторе. [ImportFederatedMessageCommandHandler.cs](../Backend/BarkFluff.Messages/Features/ImportFederatedMessage/ImportFederatedMessageCommandHandler.cs#L93), [порядок сохранения и публикации](../Backend/BarkFluff.Messages/Features/ImportFederatedMessage/ImportFederatedMessageCommandHandler.cs#L163).

## P2 — Безопасность и приватность

- **Refresh-токены живут 9999 дней, хранятся открытым текстом и не ротируются.** Утечка БД/бэкапа раскрывает готовые bearer-credentials; обычное обновление access-токена не заменяет refresh и не обнаруживает его повторное использование. Нужны ограниченный срок, хранение хеша, ротация и обнаружение повторного использования. [RefreshTokensStorage.cs](../Backend/BarkFluff.Identity/Persistence/Services/RefreshTokensStorage.cs#L20), [CreateTokenCommandHandler.cs](../Backend/BarkFluff.Identity/Features/CreateToken/CreateTokenCommandHandler.cs#L21), [выдача refresh](../Backend/BarkFluff.Identity/Services/AuthenticationService.SignIn.cs#L253).

- **Сервисные JWT действуют десять лет и не имеют индивидуального отзыва.** XAuth проверяет revocation только для пользовательских токенов. Замена значения токена в конфигурации не делает уже украденный JWT недействительным при прежнем signing key. Нужны короткий TTL, идентификатор токена и механизм отзыва/ротации. [SettingsSeeder.cs](../Backend/BarkFluff.Settings/Infrastructure/SettingsSeeder.cs#L147), [XAuthExtensions.cs](../Backend/BarkFluff.GrpcServer/XAuth/XAuthExtensions.cs#L46).

- **Внутренние сервисные credentials передаются по plaintext h2c.** Например, AdminPanel подключается к сервисам через http:// и отправляет JWT в metadata. Наблюдатель внутреннего сетевого трафика может перехватить долгоживущие токены. Нужны TLS/mTLS либо защищённый транспорт с изоляцией сервисов. [AdminPanel Program.cs](../Backend/Barkfluff.AdminPanel/Program.cs#L147), [JwtClientInterceptor.cs](../Shared/BarkFluff.Shared.Auth/JwtClientInterceptor.cs#L53).

- **TOTP seed хранится открытым текстом.** Утечка Identity DB раскрывает секреты аутентификаторов. Нужны шифрование с отдельно управляемым ключом и процедура перевыпуска после компрометации. [AuthUserProperty.cs](../Backend/BarkFluff.Identity/Domain/AuthUserProperty.cs#L12), [AuthPropertiesStorage.cs](../Backend/BarkFluff.Identity/Persistence/Services/AuthPropertiesStorage.cs#L25).

- **Старые SHA-256 хеши паролей не обновляются после успешного входа.** Для аккаунтов со старым форматом сохраняется быстрый несолёный хеш, значительно более слабый при утечке БД. Новые пароли используют BCrypt, но миграция при входе отсутствует. Количество таких аккаунтов в production неизвестно. [PasswordHasher.cs](../Backend/BarkFluff.Identity/Services/PasswordHasher.cs#L21).

- **Обычный API сообщений принимает plaintext в Private-чате.** SendMessage проверяет членство, но не запрещает этот тип чата; текст сохраняется в обычной таблице и читается обычным ListMessages. Ошибочный или модифицированный клиент может нарушить обещание хранения только ciphertext. Это не расшифровка уже существующих E2E-сообщений. Нужна проверка типа чата на границе обычных методов. [SendMessageCommandHandler.cs](../Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs#L239), [сохранение текста](../Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs#L545).

- **Secret-message API не связывает получателя с устройством и не ограничивает накопление сообщений.** Не проверяется принадлежность RecipientDeviceId указанному пользователю; буфер строится по устройству, а push также выбирается по device ID. Зная GUID, авторизованный отправитель может создавать произвольные конверты, уведомления и объём Redis-записей на 24 часа. Нужны проверка пары user/device и квоты отправителя/получателя. [SendSecretMessageCommandHandler.cs](../Backend/BarkFluff.Messages/Features/SendSecretMessage/SendSecretMessageCommandHandler.cs#L39), [NewSecretMessagePushConsumer.cs](../Backend/Barkfluff.CloudMessaging/Consumers/NewSecretMessagePushConsumer.cs#L18).

- **Изменение приватности присутствия не отзывает существующие подписки.** Проверка выполняется при подписке. После переключения на None ранее подписавшийся клиент продолжает получать status/last-seen. Нужно пересматривать подписки при изменении настроек или проверять доступ перед отправкой. [SubscribeToOnlineStatusQueryHandler.cs](../Backend/BarkFluff.Onliner/Features/SubscribeToOnlineStatus/SubscribeToOnlineStatusQueryHandler.cs#L43), [OnlineStatusNotifier.cs](../Backend/BarkFluff.Onliner/Services/OnlineStatusNotifier.cs#L31).

- **Nginx доверяет CF-Connecting-IP от любого адреса.** При прямой доступности origin атакующий может менять заголовок и обходить ограничения по IP, а также искажать журналы. Пользовательские блокировки Identity при этом сохраняются. Доверять следует адресам Cloudflare и ограничивать доступ к origin. **Фактическая внешняя доступность origin не проверялась.** [00-real-ip.conf](../Docker/nightly/nginx/sites/00-real-ip.conf#L1), [00-rate-limits.conf](../Docker/nightly/nginx/sites/00-rate-limits.conf#L1).

- **Admin cookie не имеет Secure, HttpOnly и явного SameSite.** Cookie устанавливает JavaScript; HTTP-vhost принимает запрос до перенаправления, HSTS не выставлен. При переходе по HTTP cookie может уйти по открытому каналу; доступ из JS увеличивает последствия возможного XSS. Нужна серверная выдача защищённой cookie и HSTS. Подтверждённого CSRF-эксплойта при современных настройках браузера нет; unsafe-методам также полезна явная проверка Origin/CSRF. [Login.html](../Backend/Barkfluff.AdminPanel/Pages/v2/Login.html#L309), [admin-panel.conf](../Docker/nightly/nginx/sites/admin-panel.conf#L2).

- **CheckFileHash раскрывает наличие файла и добавляет вызывающего в Uploaders по одному хешу.** Это принятый риск глобальной дедупликации: доказательство владения содержимым не требуется, возвращается канонический GUID. Он может использоваться вместе с ошибкой временных ссылок. Сам вызов не возвращает байты файла. [CheckFileHashCommandHandler.cs](../Backend/BarkFluff.Files/Features/CheckFileHash/CheckFileHashCommandHandler.cs#L51).

## P2 — Ошибки логики, доставки и конкурентности

- **Повторный вход с того же устройства после logout блокируется.** Отзыв привязан к паре UserId/DeviceId, а новый JWT получает ту же пару. Новый вход не меняет ключ отзыва, поэтому новая сессия отвергается до окончания окна запрета — по умолчанию до 60 минут. Исправлять нужно через идентификатор или версию сессии. [LogoutCommandHandler.cs](../Backend/BarkFluff.Identity/Features/Logout/LogoutCommandHandler.cs#L33), [AuthenticationService.SignIn.cs](../Backend/BarkFluff.Identity/Services/AuthenticationService.SignIn.cs#L253).

- **Некоторые изменения сообщений сохраняются раньше публикации события.** Local delete, pin, federation edit/delete используют commit-then-publish. При сбое Rabbit состояние меняется, а событие теряется; повторные запросы нередко возвращаются раньше публикации. Нужен transactional outbox для этих путей. [DeleteMessageCommandHandler.cs](../Backend/BarkFluff.Messages/Features/DeleteMessage/DeleteMessageCommandHandler.cs#L81), [PinMessageCommandHandler.cs](../Backend/BarkFluff.Messages/Features/PinMessage/PinMessageCommandHandler.cs#L107), [ApplyFederatedEditCommandHandler.cs](../Backend/BarkFluff.Messages/Features/ApplyFederatedEdit/ApplyFederatedEditCommandHandler.cs#L188), [ApplyFederatedDeleteCommandHandler.cs](../Backend/BarkFluff.Messages/Features/ApplyFederatedDelete/ApplyFederatedDeleteCommandHandler.cs#L171).

- **Bots и Updates не дедуплицируют повторную доставку по EventId.** Outbox обеспечивает доставку как минимум один раз, но потребители повторяют побочные действия. Например, повтор /token @bot ещё раз меняет токен и делает первый ответ недействительным; обычные bot updates и live events тоже дублируются. Нужен устойчивый inbox/idempotency. [Bots NewMessageConsumer.cs](../Backend/BarkFluff.Bots/Consumers/NewMessageConsumer.cs#L44), [BotFatherService.cs](../Backend/BarkFluff.Bots/Services/BotFather/BotFatherService.cs#L133), [Updates NewMessageConsumer.cs](../Backend/BarkFluff.Updates/Consumers/NewMessageConsumer.cs#L30).

- **Несколько реплик Updates планируют одинаковый push.** Каждая получает fan-out событие и создаёт собственный пятисекундный таймер. Для непрочитанного сообщения N реплик публикуют N уведомлений; CloudMessaging обрабатывает их отдельно. Возникают лишние отправки и возможные повторы на устройстве. Нужны общий scheduler/claim либо дедупликация push. [Program.cs](../Backend/BarkFluff.Updates/Program.cs#L62), [PendingPushTracker.cs](../Backend/BarkFluff.Updates/Features/PushNotifications/PendingPushTracker.cs#L9), [PushNotificationSchedulerHandler.cs](../Backend/BarkFluff.Updates/Features/PushNotifications/PushNotificationSchedulerHandler.cs#L53).

- **В части Updates-потоков возможны параллельные WriteAsync.** Общая база хранит сырой writer без сериализации. Несколько обработчиков событий могут писать в него одновременно; ошибка логируется, событие пропускается. Для NewMessages отдельный semaphore уже есть, для других потоков защита нужна. [DeviceStreamSubscriptionsBase.cs](../Backend/BarkFluff.Updates/Features/Shared/DeviceStreamSubscriptionsBase.cs#L17), [MessageEditedNotificationHandler.cs](../Backend/BarkFluff.Updates/Features/SubscribeMessagesEdited/Handlers/MessageEditedNotificationHandler.cs#L37).

- **Onliner: гонка удаления обратного индекса подписок.** Между IsEmpty и удалением родительской записи другой поток может добавить подписчика. Заполненный индекс удаляется, и последующие уведомления не находят активную подписку. Нужна атомарная работа с индексом. [OnlineStatusSubscriptionsManager.cs](../Backend/BarkFluff.Onliner/Services/OnlineStatusSubscriptionsManager.cs#L209).

- **Onliner: offline-переход теряется при сбое после удаления Redis-записи.** Sweeper сначала делает ZREM, затем сохраняет LastSeen и публикует событие. При ошибке последующие циклы уже не видят кандидата и не повторяют незавершённые действия. Нужны устойчивый переход/outbox или сверка состояния. [OfflineDetectionService.cs](../Backend/BarkFluff.Onliner/BackgroundServices/OfflineDetectionService.cs#L98).

- **Calls: качество звонка хранится только в памяти одной реплики.** Поздний Accept/Join на другой реплике возвращает Auto вместо выбранного качества. События распределяются между репликами, это состояние — нет. [CallQualityStore.cs](../Backend/BarkFluff.Calls/Services/CallQualityStore.cs#L7), [CallLifecycleHandler.cs](../Backend/BarkFluff.Calls/Features/CallLifecycle/CallLifecycleHandler.cs#L298).

- **ClientStorage: metadata новой версии может сопровождаться байтами старой.** Новая запись БД сохраняется раньше фонового обновления общего cache-файла канала. Параллельные обновления используют одинаковый .tmp и могут завершиться в обратном порядке. Возможны неверные версия/checksum/имя и фактическое скачивание прежнего релиза. Кэш должен быть привязан к версии/S3 key/hash, публикация — атомарной. [ClientStorageController.cs](../Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs#L313), [публикация кэша](../Backend/BarkFluff.ClientStorage/Controllers/ClientStorageController.cs#L463), [LocalFileCache.cs](../Backend/BarkFluff.ClientStorage/Infrastructure/LocalFileCache.cs#L45).

- **CloudMessaging: часть отправок не разбивается на пачки по 500 адресатов.** Обычные notification, call/invite/dismiss/web-пути передают весь набор в один multicast. При превышении лимита отправка завершается ошибкой. Нужна единая пакетная отправка для всех путей. [FirebaseService.cs](../Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs#L126), [лимит Firebase Admin SDK](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk#send_one_message_to_multiple_devices).

- **CloudMessaging: ошибки отправки могут завершаться успешным потреблением события.** В частности, batch-путь ловит исключение и только логирует его. Transient FCM-ошибка не вызывает повторную доставку. Это риск best-effort модели; если уведомления требуют надёжности, нужны retry и сохранение статуса отправки. [FirebaseService.cs](../Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs#L197).

- **Notification: обработчик ожидает retry, но он не настроен.** При ошибке SMTP consumer повторно выбрасывает исключение, однако endpoint не включает retry/redelivery. Письмо попадает в обработку ошибки вместо автоматической повторной отправки. [Program.cs](../Backend/BarkFluff.Notification/Program.cs#L28), [EmailQueueConsumer.cs](../Backend/BarkFluff.Notification/Consumers/EmailQueueConsumer.cs#L40), [настройка retry в MassTransit](https://masstransit.massient.com/concepts/middleware).

## P2 — Нагрузка и оптимизация

- **Публичный support chat не ограничивает накопление данных.** Анонимный клиент может создавать произвольные GUID-сессии и сообщения. Они остаются в process-wide коллекциях без TTL/eviction; запросы также направляются в Telegram. Нужны rate limit, квоты и ограниченное хранение. [SupportChatController.cs](../Backend/Barkfluff.WebServer/Controllers/SupportChatController.cs#L23), [SupportChatService.cs](../Backend/Barkfluff.WebServer/Services/SupportChatService.cs#L7).

- **Публичный запрос входа AdminPanel не ограничен по частоте.** С действительным логином можно создавать approval-запросы, выполнять Telegram-проверки и накапливать pending state. Очистка по времени не ограничивает число одновременных записей. Нужны ограничения по IP/аккаунту и предел pending-запросов. [AuthService.cs](../Backend/Barkfluff.AdminPanel/Services/AuthService.cs#L49), [PendingAuthService.cs](../Backend/Barkfluff.AdminPanel/Services/PendingAuthService.cs#L11).

- **Calls: повторные direct-звонки не ограничены.** Каждый запрос создаёт Ringing-запись, события и push, затем работу timeout-sweeper. Нет ограничения частоты и уникальности активного прямого звонка; ограничение активного group call уже реализовано. [CallLifecycleHandler.cs](../Backend/BarkFluff.Calls/Features/CallLifecycle/CallLifecycleHandler.cs#L79), [CallSessionConfiguration.cs](../Backend/BarkFluff.Calls/Persistence/Configurations/CallSessionConfiguration.cs#L19).

- **Files: декодирование изображений не ограничено заранее по пикселям и кадрам.** Полная загрузка ImageSharp происходит до уменьшения изображения. Размер сжатого файла не ограничивает объём декодированных данных; GIF и параллельные загрузки усиливают расход CPU/RAM. Нужны предварительная проверка, лимиты кадров/размеров/памяти и ограничение параллелизма. OOM на специальном payload не воспроизводился. [ImageCompressor.cs](../Backend/BarkFluff.Files/Services/ImageCompressor.cs#L166), [рекомендации ImageSharp](https://docs.sixlabors.com/articles/imagesharp/security.html).

- **Updates: число потоков одного пользователя/устройства не ограничено.** Повторные подключения добавляют новые subscriptions и удерживают ресурсы до disconnect. Нужны пределы и управляемое вытеснение старых потоков. [DeviceStreamSubscriptionsBase.cs](../Backend/BarkFluff.Updates/Features/Shared/DeviceStreamSubscriptionsBase.cs#L22).

- **Onliner: большой запрос вызывает последовательные downstream-вызовы.** Нет явного предела числа запрошенных пользователей; privacy lookup выполняется отдельным Users RPC для каждого target. Нужны лимит количества, дедупликация ID и batch lookup. Задержки под нагрузкой не измерялись. [GetOnlineStatusQueryHandler.cs](../Backend/BarkFluff.Onliner/Features/GetOnlineStatus/GetOnlineStatusQueryHandler.cs#L47), [OnlineVisibilityFilter.cs](../Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs#L26).

- **MarkAsRead: не ограничено количество message IDs.** Обработчик загружает весь переданный набор глобальных идентификаторов. Большие запросы увеличивают работу БД и объём результатов до проверки доступа. [MessagesStorage.cs](../Backend/BarkFluff.Messages/Persistence/Services/MessagesStorage.cs#L281).

- **ListChats загружает всех участников групп, затем удаляет их из ответа.** Предел страницы в 50 чатов не ограничивает суммарное число загруженных участников. Нужна проекция, не материализующая состав групп для этого ответа. Это проблема формы запроса, а не доказанный N+1. [ChatsStorage.cs](../Backend/BarkFluff.Messages/Persistence/Services/ChatsStorage.cs#L30), [ListChatsCommandHandler.cs](../Backend/BarkFluff.Messages/Features/ListChats/ListChatsCommandHandler.cs#L118).

- **AdminPanel: Docker CLI может ждать без таймаута и блокировать общий gate.** При промахе кэша зависший процесс удерживает gate получения состояния контейнеров; следующие запросы также ждут. Нужны deadline, cancellation и завершение дочернего процесса. [DockerService.cs](../Backend/Barkfluff.AdminPanel/Services/DockerService.cs#L55), [ожидание CLI](../Backend/Barkfluff.AdminPanel/Services/DockerService.cs#L330).

- **AdminPanel: KPI fallback загружает до 50 000 Seq events последовательными страницами.** Это до 100 обращений к Seq и большая коллекция в памяти при fallback/промахе кэша. Нужны серверная агрегация или подготовленные агрегаты. Traffic-путь уже переведён на обработку страниц без накопления полного списка. [SeqEndpoints.cs](../Backend/Barkfluff.AdminPanel/Endpoints/SeqEndpoints.cs#L174), [SeqService.cs](../Backend/Barkfluff.AdminPanel/Services/SeqService.cs#L98).

## P2 — Инфраструктура и зависимости

- **CI может оставить образы со старым shared-кодом.** Например, Files/Users workflows реагируют на каталог сервиса и rebuild.trigger, но не на все потребляемые shared-библиотеки и proto. Исправление общей авторизации или контракта может не пересобрать зависимые сервисы. Нужны dependency-aware triggers. [build-backend-files.yml](../.github/workflows/build-backend-files.yml#L9), [build-backend-users.yml](../.github/workflows/build-backend-users.yml#L9).

- **Readiness возвращает HTTP 200 при частичном отказе зависимостей.** 503 выдаётся только когда все проверки имеют статус down. Отказ обязательной БД или брокера может оставить сервис формально готовым для балансировщика. Нужна классификация обязательных зависимостей и отказ readiness при их недоступности. [HealthEndpointExtensions.cs](../Backend/BarkFluff.GrpcServer/HealthEndpointExtensions.cs#L22), [ReadinessMonitorService.cs](../Backend/BarkFluff.GrpcServer/ReadinessMonitorService.cs#L181).

- **Nightly compose публикует PostgreSQL и Seq без loopback-привязки.** У Navigator также опубликован прямой h2c-порт, обходящий TLS nginx. Доступность извне зависит от firewall; она не проверялась. Следует закрыть ненужные порты или привязать их к внутреннему/loopback-интерфейсу. [PostgreSQL](../Docker/nightly/barkfluff/docker-compose.yml#L388), [Seq](../Docker/nightly/barkfluff/docker-compose.yml#L287), [Navigator compose](../Docker/nightly/navigator/nginx/docker-compose.yml#L5).

- **Navigator и ClientStorage используют уязвимую native SQLite.** Runtime dependency graph разрешает SQLitePCLRaw.lib.e_sqlite3 2.1.11 с предупреждением NU1903/CVE-2025-6965. Исправление есть в SQLite 3.50.2; нужно обновить цепочку поставки native-библиотеки и проверить итоговый бинарник. **Путь удалённой эксплуатации не подтверждён:** запросы EF/параметризованы, входа со свободным SQL не найдено. SQLite в Files.Tests/Messages.Tests относится к тестам; их runtime использует PostgreSQL. [Navigator.csproj](../Backend/BarkFluff.Navigator/BarkFluff.Navigator.csproj#L43), [ClientStorage.csproj](../Backend/BarkFluff.ClientStorage/BarkFluff.ClientStorage.csproj#L15), [advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q), [исправление SQLite](https://sqlite.org/releaselog/3_50_2.html).

## P3 — Безопасность и обработка ошибок

- **Navigator хранит signing key в репозитории.** Он используется валидатором этого сервиса. Защищённых gRPC-операций там не найдено, поэтому подтверждённый дополнительный эффект — подделка идентичности/AddedBy, а не захват защищённого API. Ключ нужно вынести и заменить. [appsettings.json](../Backend/BarkFluff.Navigator/appsettings.json#L13).

- **Legacy SetPassword обходит политику длины пароля.** Handler не применяет правило минимум 8 символов/максимум 72 байта, существующее в новом auth flow. Авторизованный пользователь может установить слишком короткий пароль через этот RPC. [SetPasswordCommandHandler.cs](../Backend/BarkFluff.Identity/Features/SetPassword/SetPasswordCommandHandler.cs#L58).

- **Один TOTP может подтвердить несколько независимых запросов в пределах допустимого окна.** Проверка не сохраняет использованный временной шаг. Это дополнительное усиление защиты от replay; самостоятельный захват аккаунта без остальных credentials здесь не подтверждён. [AuthenticationService.SignIn.cs](../Backend/BarkFluff.Identity/Services/AuthenticationService.SignIn.cs#L223).

- **MarkAsRead позволяет различать отсутствующие и существующие чужие сообщения.** Для отсутствующих ID возвращается успех, для существующих недоступных — NoAccess. Последовательные long IDs позволяют проверять наличие записей. Содержимое не выдаётся, чужие сообщения не меняются. [MarkAsReadCommandHandler.cs](../Backend/BarkFluff.Messages/Features/MarkAsRead/MarkAsReadCommandHandler.cs#L50).

- **Клиентский x-ip-address может подделывать audit/geo labels.** Auth-ограничения уже используют отдельный TrustedIpAddress, но отображаемый адрес в регистрации и security notifications остаётся подконтрольным клиенту. [RequestContextInterceptor.cs](../Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs#L70), [AuthenticationService.SignIn.cs](../Backend/BarkFluff.Identity/Services/AuthenticationService.SignIn.cs#L41).

- **Невалидная Base64 metadata вызывает исключение вместо управляемой ошибки валидации.** Это срывает собственный RPC клиента и создаёт exception logs. Утечка текста внутреннего исключения уже устранена. [RequestContextInterceptor.cs](../Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs#L153).

- **Прикладное преобразование исключений не распространяется на server-streaming.** Streaming-ошибки, например при XFed authentication, теряют ожидаемые application status и x-error-code. Проверка доступа сохраняется, но классификация ошибок непоследовательна. [ServerExceptionInterceptor.cs](../Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs#L24), [XFedServerInterceptor.cs](../Backend/BarkFluff.Federation/Host/XFedServerInterceptor.cs#L63).

- **Microsoft.OpenApi 2.0.0 имеет известную уязвимость парсера.** Исправленная ветка — 2.7.5+. Уязвимость требует обработки недоверенного OpenAPI-документа; соответствующего входного пути в проверенном коде не найдено. Это необходимость обновления зависимости, без подтверждённого удалённого DoS API. [Microsoft advisory](https://github.com/microsoft/OpenAPI.NET/security/advisories/GHSA-v5pm-xwqc-g5wc).

- **SSH.NET 2025.1.0 содержит известные уязвимости ScpClient.** Исправленная версия — 2026.0.0. AdminPanel использует SshClient/ShellStream/RunCommand, поэтому указанные SCP-пути RCE и arbitrary file write не подтверждены как достижимые. Зависимость всё равно следует обновить; отсутствие host-key validation выше — отдельная подтверждённая проблема. [SCP command advisory](https://github.com/sshnet/SSH.NET/security/advisories/GHSA-mggc-4xg6-vcxf), [SCP file-write advisory](https://github.com/sshnet/SSH.NET/security/advisories/GHSA-q939-rpr3-3284).

## P3 — AdminPanel и операции

- **KPI может возвращать неполные totals с HTTP 200.** При сбое следующей Seq-страницы helper возвращает уже полученные события; endpoint принимает их за полный результат. Нужно явно обозначать неполный ответ или возвращать ошибку. [SeqService.cs](../Backend/Barkfluff.AdminPanel/Services/SeqService.cs#L108), [SeqEndpoints.cs](../Backend/Barkfluff.AdminPanel/Endpoints/SeqEndpoints.cs#L182).

- **Очередь deploy jobs и число незавершённых jobs не ограничены.** Пользователь с Docker-control permission может ставить задачи быстрее единственного worker, особенно при медленном Docker. Нужны предел очереди и защита от повторных одинаковых задач. [DeployJobService.cs](../Backend/Barkfluff.AdminPanel/Services/DeployJobService.cs#L41).

- **Отменённый update/switch branch всё ещё запускает image prune.** После прерывания шага выполнение доходит до очистки образов. Если новый образ уже применён, прежний dangling image может быть удалён до восстановления/rollback. Очистку следует выполнять после успешного завершения и сохранять rollback-образ. [DeployJobService.cs](../Backend/Barkfluff.AdminPanel/Services/DeployJobService.cs#L133).

- **Переключение ветки самой AdminPanel игнорирует выбранную ветку.** Специальная обработка admin-panel вызывает обновление существующего compose image, не применяя step.Branch. Задача может завершиться успешно при неизменившейся ветке. [DeployJobService.cs](../Backend/Barkfluff.AdminPanel/Services/DeployJobService.cs#L217), [DockerService.cs](../Backend/Barkfluff.AdminPanel/Services/DockerService.cs#L566).

- **Отмена remote-запроса не отменяет выполняющийся SSH command.** Токен используется для connect, затем вызывается синхронный RunCommand без наблюдения отмены. [SshNetRemoteSshClient.cs](../Backend/Barkfluff.AdminPanel/Services/SshNetRemoteSshClient.cs#L23).

- **Прямые start/stop/restart обходят сериализованную deploy-очередь.** Они могут выполняться одновременно с update и конфликтовать с проверкой итогового состояния. Нужны общая сериализация операций или отказ при конфликтующей активной задаче. [DockerEndpoints.cs](../Backend/Barkfluff.AdminPanel/Endpoints/DockerEndpoints.cs#L53), [DeployJobService.cs](../Backend/Barkfluff.AdminPanel/Services/DeployJobService.cs#L121).

## P3 — Оптимизация и технический долг

- **Недействительные FCM-токены обнаруживаются, но не удаляются.** Они продолжают занимать batch slots и порождать повторную работу. [FirebaseService.cs](../Backend/Barkfluff.CloudMessaging/Services/FirebaseService.cs#L165).

- **Navigator скрывает истёкшие записи, но не очищает их из SQLite.** TTL фильтрует выдачу, а старые регистрации остаются в постоянном хранилище. Нужны периодическая очистка и предел накопления. [ServersStorage.cs](../Backend/BarkFluff.Navigator/Persistence/ServersStorage.cs#L20).

- **Developers каждый раз читает статический каталог ошибок из БД.** При заметной частоте запросов результат можно кэшировать; измеренного bottleneck здесь нет. [GetErrorCodesQueryHandler.cs](../Backend/Barkfluff.Developers/Features/GetErrorCodes/GetErrorCodesQueryHandler.cs#L17).

- **Общий request-context interceptor повторяет проходы по metadata и выделения памяти.** Шесть независимых поисков и Base64-декодирований; дополнительно создаётся Stopwatch на unary RPC. Это кандидаты для оптимизации после профилирования, без доказанного существенного влияния на throughput. [RequestContextInterceptor.cs](../Backend/BarkFluff.GrpcServer/Tracker/RequestContextInterceptor.cs#L37), [ServerExceptionInterceptor.cs](../Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs#L32).

- **Lazy-кэш типов исключений может строиться несколько раз одновременно.** Публичный mutable static list инициализируется без синхронизации. Подтверждён лишний reflection/creation work на первых параллельных ошибках; неправильное преобразование или внешняя мутация не обнаружены. [ExceptionClientInterceptor.cs](../Shared/BarkFluff.Shared.Exceptions/Interceptors/ExceptionClientInterceptor.cs#L11).

- **Сборка Files выдаёт многочисленные CS0436 из-за повторно сгенерированных Proto-типов.** Локальные определения конфликтуют с импортированными из Proto assembly. Фактическая runtime-поломка не выявлена, но источник генерации контрактов следует унифицировать. [Files.csproj](../Backend/BarkFluff.Files/BarkFluff.Files.csproj#L39).

- **Один тест Messages устарел относительно push-пути.** Handle_PublishesMessageAndSilentPush ожидает прямой SendSilentPush, тогда как код публикует событие для CloudMessaging. Это объясняет падение теста; сам путь push в исходниках присутствует. [SendSecretMessageCommandHandlerTests.cs](../Tests/BarkFluff.Messages.Tests/Features/SendSecretMessage/SendSecretMessageCommandHandlerTests.cs#L110).

- **PostgreSQL-проверки поиска остались невыполненными.** Четыре SearchMessagesPostgresTests пропущены из-за отсутствия test DB. SQL-семантика поиска и контекстного окна не получила интеграционного подтверждения в этой среде.

- **Аудиты и Obsidian местами описывают прежнее состояние.** Встречаются удалённые compose-пути, устаревшие сведения о DNS pinning, cookie AdminPanel и ещё не реализованной федерации. Аудит Users.Rust относится к удалённой реализации. Пометка «риск принят» для QR/hash означает сохранение риска, а не устранение кода.

## P3 — Функциональные пробелы и неполные контракты

- **Onliner: Friends фактически работает как None.** Проверки отношений пока нет; друзья тоже не получают присутствие при этой настройке. Это ограничение функциональности с безопасным отказом. [OnlineVisibilityFilter.cs](../Backend/BarkFluff.Onliner/Services/OnlineVisibilityFilter.cs#L50).

- **История групповых звонков неполная.** В выборку попадают групповые звонки, где пользователь инициатор, а участие остальных пользователей не покрыто. [CallLifecycleHandler.cs](../Backend/BarkFluff.Calls/Features/CallLifecycle/CallLifecycleHandler.cs#L339).

- **GetActiveCalls не заполняет документированное поле участников.** participant_user_ids есть в proto и описании API, но handler оставляет его пустым в реализации v1. [CallLifecycleHandler.cs](../Backend/BarkFluff.Calls/Features/CallLifecycle/CallLifecycleHandler.cs#L405), [backend-api-endpoints.md](backend-api-endpoints.md#L470).

- **Developers публикует неполный набор публичных контрактов.** В allowlist отсутствуют calls_api.proto и bots_api.proto. Это ограничение каталога; считать его ошибкой следует, если портал должен представлять весь пользовательский API. [PublishedProtoManifest.cs](../Backend/Barkfluff.Developers/Infrastructure/PublishedProtoManifest.cs#L5).

## Исправленные пункты прежних аудитов

- Email OTP имеет TTL, одноразовое потребление, лимиты попыток и блокировку конкурентных операций. Старые утверждения о неограниченном replay нельзя переносить на проверенный код. [AuthenticationStore.cs](../Backend/BarkFluff.Identity/Persistence/Services/AuthenticationStore.cs#L38).
- WebServer проверяет и HTML-экранирует username; публичная выдача профиля учитывает site/privacy settings. [UserPageService.cs](../Backend/Barkfluff.WebServer/Services/UserPageService.cs#L8), [GetUserByUsernameQueryHandler.cs](../Backend/BarkFluff.Users/Features/GetUserByUsername/GetUserByUsernameQueryHandler.cs#L44).
- Navigator/Federation используют привязку соединения к проверенному DNS-адресу. Старое утверждение «DNS проверяется, но адрес не закрепляется» неактуально для среза аудита. [WellKnownClient.cs](../Backend/BarkFluff.Federation/Services/WellKnownClient.cs#L39).
- Обычный SendMessage уже использует durable outbox; проблема commit-then-publish относится к перечисленным другим операциям.
- AdminPanel маскирует S3 configuration и не возвращает сохранённый SSH-пароль через GET. Есть cache статусов и объединённый SSH-запрос вместо подключения на каждый контейнер. [ConfigurationEndpoints.cs](../Backend/Barkfluff.AdminPanel/Endpoints/ConfigurationEndpoints.cs#L221), [RemoteDockerService.cs](../Backend/Barkfluff.AdminPanel/Services/RemoteDockerService.cs#L171).
- Admin step-up привязан к сессии, действию и параметрам, имеет срок и одноразовое потребление. Подмена целевой операции не обнаружена. [StepUpService.cs](../Backend/Barkfluff.AdminPanel/Services/StepUpService.cs#L92).
- Общие внутренние exception messages маскируются; reflection включается только в Development; JWT client interceptor покрывает streaming-вызовы. [ServerExceptionInterceptor.cs](../Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs#L91), [JwtClientInterceptor.cs](../Shared/BarkFluff.Shared.Auth/JwtClientInterceptor.cs#L15).
- Старый метод выдачи service JWT до года 9999 не вызывается production-кодом. Активный seeder выдаёт их на десять лет — проблема остаётся, но её фактический масштаб другой.

## Проверки и пределы отчёта

В исходном аудите запущены пять наборов:

- Identity — **270 прошли**.
- Settings — **28 прошли**.
- Messages — **452 прошли, 1 упал, 4 пропущены**.
- Updates — **23 прошли**.
- Files — **300 прошли**.

Всего: **1073 прошли, 1 упал, 4 пропущены**. По завершении исходного аудита рабочее дерево осталось чистым.

Это статический аудит с существующими тестами. Production, firewall, эксплуатационные payloads, нагрузка, планы PostgreSQL и содержимое опубликованных контейнеров не проверялись. Проверка зависимостей была выборочной; полный solution scan ограничен Windows-проектами. Условные сетевые риски и наличие уязвимых пакетов отделены от доказанного пути эксплуатации.
