# Аудит: BarkFluff.Messages
> Дата: 2026-06-12. Область: код сервиса, Dockerfile, nginx, docker-compose.

## Сводка

Сервис сообщений в целом аккуратно реализует авторизацию: оба gRPC-класса защищены на уровне типа (`MessagesApiService` — `TokenType.User`, `MessagesServerApiService` — `TokenType.Service`), членство в чате почти везде проверяется через `CheckAccessToChat`, а владение сообщением — через сравнение `SenderId` с `UserContext.UserId`. Тем не менее найдены реальные проблемы: **пересылка чужого сообщения раскрывает текст/вложения сообщения из чата, к которому отправитель имеет доступ, но контроль доступа к самому пересылаемому сообщению проверяется не на конкретное сообщение, а на чат**; **`MarkAsRead` и публикация событий ReadBy раскрывают сам факт и состав чужих чатов через перечисление произвольных messageId**; в логи на уровне Information систематически попадают тексты системных сообщений и метаданные. По производительности главная проблема — горячий путь отправки/чтения сообщений делает несколько последовательных межсервисных gRPC-вызовов и грузит **всех** участников чата (`Take(int.MaxValue)`) на каждое сообщение/прочтение, плюс `ListChats` строит коррелированные подзапросы (потенциальный N+1 по числу чатов на странице).

| Критичность | Кол-во |
| ----------- | ------ |
| Critical    | 0      |
| High        | 3      |
| Medium      | 8      |
| Low         | 6      |
| **Итого**   | **17** |

Распределение по категориям: Безопасность — 9, Производительность — 6, Docker/nginx — 2.

## Безопасность

### S1. IDOR в MarkAsRead: отметка чужих сообщений и раскрытие состава чужих чатов — High
**Файл:** `Backend/BarkFluff.Messages/Features/MarkAsRead/MarkAsReadCommandHandler.cs:50-88`
**Проблема:** Метод принимает произвольный список `MessageIds` от клиента, поднимает сообщения `GetMessagesByIds(request.MessageIds)` без какой-либо привязки к чату, затем по каждому чату делает `CheckAccessToChat`. Если хотя бы один messageId принадлежит чужому чату — бросается `NoAccessToChatException`, что превращает метод в **оракул принадлежности**: атакующий перебором `long Id` (последовательные, не GUID — см. `Message.Id`) определяет, какие сообщения существуют и в каких чатах он состоит. Дополнительно, для каждого чата вызывается `GetChatMembers(..., int.MaxValue)` и публикуется `MessageReadEvent` с полным списком `ChatMembers` — но проверка доступа защищает от записи в чужой чат, так что основной риск именно перечисление/oracle.
**Почему это проблема:** Нарушение object-level контроля доступа на чтение метаданных: последовательные `Message.Id` позволяют итеративно прощупывать чужие сообщения и членство. Бросок исключения на «первом чужом» сообщении подтверждает существование объекта.
**Рекомендация:** Принимать `chatId` явно и фильтровать сообщения запросом `WHERE ChatId = @chatId AND Id = ANY(@ids)` (как сделано в `GetMessagesByIdsInChatAsync`), проверять доступ к одному чату заранее. Чужие/несуществующие id молча игнорировать, не бросая исключение.

### S2. Пересылка сообщения раскрывает контент по chat-level, а не message-level доступу — Medium
**Файл:** `Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs:224-303`
**Проблема:** При пересылке (`ForwardedMessageId`) сообщение поднимается `GetMessagesByIds([id])` (без ограничения чата), затем проверяется `CheckAccessToChat(originalMessage.ChatId, userId)`. Проверка корректна — пользователь должен состоять в исходном чате. Но `GetMessagesByIds` (`MessagesStorage.cs:133`) не фильтрует по чату вызывающего, поэтому если злоумышленник подберёт `ForwardedMessageId` сообщения из чата, в котором он **состоит** (например, общий групповой чат), он может переслать его текст/вложения в любой другой чат — это ожидаемое поведение пересылки. Реальная проблема — отсутствует проверка, что пересылаемое сообщение не помечено `IsDeleted` некорректно и что forward не утечёт через гонку; основной риск низкий, но проверка доступа к forwarded-вложениям (`forwardedFilesInfo`) не выполняется повторно — берутся `FileId` из снапшота оригинала.
**Почему это проблема:** Слабый контроль приводит к тому, что доступ определяется фактом членства в исходном чате на момент пересылки; модель в целом корректна, но проверка завязана на `ChatId` объекта, а не на отдельное право читать конкретное сообщение. Это даёт расширение поверхности при будущих изменениях (например, частичный выход из чата).
**Рекомендация:** Поднимать оригинал запросом, явно проверяющим доступ (`GetMessagesByIdsInChatAsync` от чатов пользователя) либо после проверки `CheckAccessToChat` дополнительно валидировать, что сообщение не системное/удалённое (последнее частично есть через `!IsDeleted` в `GetMessagesByIds`).

### S3. Тексты системных сообщений и метаданные пишутся в логи уровня Information (PII/раскрытие) — Medium
**Файл:** `Backend/BarkFluff.Messages/Features/CreateGroupChat/CreateGroupChatCommandHandler.cs:45-50,131-137`; `Backend/BarkFluff.Messages/Consumers/UserChangedNameConsumer.cs:34-39`; `Backend/BarkFluff.Messages/Persistence/Services/SecretMessageBuffer.cs:71-73`
**Проблема:** В лог уровня Information попадают: название группового чата (`request.Title`), имена и фамилии пользователей (`UserChangedNameConsumer`), userId отправителя/получателя секретных сообщений с messageId. Это PII (имена) и чувствительные метаданные переписки (кто кому пишет, названия чатов). Тексты обычных сообщений в лог не пишутся (проверено: `SendMessageCommandHandler` логирует только факты, не `Text`).
**Почему это проблема:** Логи (Serilog → Seq, см. память проекта) обычно имеют более широкий круг доступа, чем сама БД сообщений. Имена, названия чатов и граф «кто кому» — персональные данные; их попадание в общий лог увеличивает поверхность утечки.
**Рекомендация:** Понизить такие записи до Debug либо логировать только идентификаторы (userId/chatId), без имён и названий. Граф отправитель→получатель секретных сообщений — минимум, что стоит из Information убрать.

### S4. CheckChatMembership доступен любому сервисному токену без скоупа — Low
**Файл:** `Backend/BarkFluff.Messages/Host/MessagesServerApiService.cs:36-47`; `Backend/BarkFluff.Messages/Features/CheckChatMembership/CheckChatMembershipQueryHandler.cs:18-44`
**Проблема:** Метод принимает произвольный `UserId` и список `ChatIds` и возвращает, в каких из них пользователь состоит. Защищён только политикой `TokenType.Service` (общая для всех сервисов). Любой сервис с сервисным токеном может массово проверять членство любого пользователя в любых чатах.
**Почему это проблема:** Нет разграничения между сервисами — компрометация токена любого микросервиса (а токены лежат в конфиге, см. `UsersService:Token`/`FilesService:Token`) даёт возможность реконструировать граф членства. Это metadata-leak, не критичный сам по себе, но расширяет последствия компрометации одного сервиса.
**Рекомендация:** Принять как осознанный выбор для S2S, либо ввести более узкие скоупы для сервисных токенов (claim с разрешённым набором операций), чтобы Updates/Onliner не имели доступа к произвольным проверкам.

### S5. GetChatInfo для сервисного токена раскрывает чат без проверки членства — Low
**Файл:** `Backend/BarkFluff.Messages/Features/GetChatInfo/GetChatInfoCommandHandler.cs:42-55`
**Проблема:** Для `TokenType.Service` проверка `CheckAccessToChat` намеренно пропускается (комментарий «сервисные токены имеют полный доступ»), возвращаются `MembersId`, счётчик непрочитанных и т.д. Метод доступен по политике `TokenType.User`, которая (см. `XAuthExtensions.cs:79-80`) разрешает И `User`, И `Service`.
**Почему это проблема:** Любой сервисный токен получает полную информацию о любом чате по GUID, включая список участников. В сочетании с S4 — реконструкция графа. Риск низкий, т.к. требует сервисного токена и знания GUID чата.
**Рекомендация:** Подтвердить, что такой доступ нужен (вероятно для экспорта/админки). Если нет — убрать bypass или ограничить набор полей для сервисного доступа.

### S6. KickUser: проверка членства вызывающего, но не запрет на исключение самого себя/создателя — Low
**Файл:** `Backend/BarkFluff.Messages/Features/KickUser/KickUserCommandHandler.cs:45-91`
**Проблема:** Право кика проверяется через `groupChatInfo.UsersCanKick.Contains(userId)` — это корректно. Однако нет проверок: нельзя кикнуть самого себя, нельзя кикнуть последнего админа/создателя. Пользователь с правом кика может исключить создателя чата (`Creator`), оставив чат без управляющего, т.к. список `UsersCanKick` нигде в коде не пополняется/не пересобирается при кике.
**Почему это проблема:** Логический недочёт контроля доступа: возможна «узурпация»/«осиротевший» групповой чат. Не утечка данных, но нарушение целостности управления чатом.
**Рекомендация:** Запретить кик создателя (`groupChatInfo.Creator`) кем-либо кроме него самого, либо запретить кикать пользователей из `UsersCanKick`. Рассмотреть запрет кика самого себя (для выхода должен быть отдельный метод Leave).

### S7. Раскрытие текста внутреннего исключения в gRPC-ответе — Low
**Файл:** `Backend/BarkFluff.GrpcServer/ServerExceptionInterceptor.cs:60-80` (общий для сервиса, применяется в `Program.cs:39`)
**Проблема:** Для не-бизнес исключений (`catch (Exception)`) клиенту возвращается `new Status(StatusCode.Unknown, ex.Message)` — текст оригинального исключения. Это может быть сообщение Npgsql/EF (SQL-фрагменты, имена столбцов), gRPC-клиента к Users/Files и т.п.
**Почему это проблема:** Раскрытие внутренних деталей облегчает разведку (имена таблиц/столбцов, адреса сервисов, версии). `x-error-code` отдаётся обобщённый, но `Status.Detail` — нет.
**Рекомендация:** Возвращать обобщённый detail (например, «Внутренняя ошибка»), а `ex.Message` писать только в лог. (Замечание относится к общему GrpcServer — аудит XAuth/GrpcServer ведёт другой агент, но проявляется и в Messages.)

### S8. Pending-инвайты приватных чатов хранятся бессрочно и создаются без лимита — Medium
**Файлы:** `Backend/BarkFluff.Messages/Features/CreatePrivateChat/CreatePrivateChatCommandHandler.cs:68-85`; `Persistence/Services/ChatsStorage.cs:171-188`; `Persistence/Services/PrivateChatInviteStore.cs:22-27`.
**Проблема:** Каждый `CreatePrivateChat` создаёт постоянную запись `Chat` в PostgreSQL и Redis-ключ `private_invite:{chatId}`. `StringSetAsync` вызывается без TTL, а фоновой очистки/лимита числа pending-инвайтов нет. Пока получатель не выполнит Reject, инициатор также не имеет пути удалить этот чат.
**Почему это проблема:** Аутентифицированный пользователь может многократно приглашать произвольного существующего пользователя, бесконечно накапливая невидимые пустые чаты и Redis-ключи, а также генерируя поток invite-событий. Это постоянный DB/Redis resource-exhaustion и вектор спама, а не временная очередь.
**Рекомендация:** Задать короткий TTL pending-invite и фоново удалять просроченные чаты без второго участника; ограничить один pending-invite на пару пользователей и ввести rate limit на создание.

### S9. Secret-chat Redis-буфер не ограничен по числу envelope на устройство — Medium
**Файлы:** `Backend/BarkFluff.Messages/Features/SendSecretMessage/SendSecretMessageCommandHandler.cs:46-66`, `Features/SendSecretChatInvite/SendSecretChatInviteCommandHandler.cs:51-72`; `Persistence/Services/SecretMessageBuffer.cs:15,43-68,144-171`; отсутствие app-level лимита — `Program.cs:37-40`.
**Проблема:** Входные envelope ограничены 16 КиБ, но количество `secret_msg`/`secret_invite` на устройство за 24 часа не ограничивается. Каждый вызов добавляет отдельный JSON payload и ID в Redis SET; ACK требуется от получателя, а TTL индекса дополнительно продлевается при каждой новой записи. Ни per-sender/device quota, ни rate limiting в сервисе нет.
**Почему это проблема:** Пользователь может направлять сообщения на собственное или известное устройство и удерживать значительный объём Redis-памяти 24 часа, повторяя запросы. Это влияет на общий Redis Messages и приводит к отказу секретной доставки/кеша для остальных пользователей.
**Рекомендация:** Ввести лимит pending envelope на recipient-device и общий byte-budget, отклонять/удалять самые старые записи при достижении лимита; добавить rate limit на отправителя и метрики переполнения.

## Производительность

### P1. Горячий путь SendMessage: до 4 последовательных межсервисных вызовов + загрузка всех участников на каждое сообщение — High
**Файл:** `Backend/BarkFluff.Messages/Features/SendMessage/SendMessageCommandHandler.cs:143-339`
**Проблема:** На отправку одного сообщения в худшем случае выполняется: `GetByIdAsync` (Users) для определения чата, при создании чата ещё один `GetByIdAsync` (Users), `GetFilesDataAsync` (Files) для вложений, при пересылке — ещё `GetByIdAsync` (Users) + `GetFilesDataAsync` (Files). Затем `GetChatMembers(chatId, 0, int.MaxValue)` (`:321`) грузит **всех** участников чата (для больших групп — тысячи строк) только чтобы получить список userId для события в очередь. Все вызовы последовательные (`await` подряд), не распараллелены.
**Почему это проблема:** Это самый горячий путь сервиса. Несколько последовательных сетевых round-trip-ов к Users/Files на каждое сообщение умножают латентность; `Take(int.MaxValue)` по членам — unbounded выборка, линейно растущая с размером группы, на каждое сообщение.
**Рекомендация:** Грузить только `UserId` участников проекцией (`Select(m => m.UserId)`) без полной сущности; кэшировать список участников чата (Redis) с инвалидацией при kick/join; распараллелить независимые вызовы (`Task.WhenAll`) к Users/Files; рассмотреть отдачу списка участников consumer-у Updates по chatId вместо передачи всего списка в каждом событии.

### P2. ListChats: коррелированные подзапросы на каждый чат страницы (потенциальный N+1/тяжёлый запрос) — High
**Файл:** `Backend/BarkFluff.Messages/Persistence/Services/ChatsStorage.cs:26-57`
**Проблема:** В проекции `GetUserChats` на каждый чат строятся четыре коррелированных подзапроса к `Messages`: `CountUnread` (Count с `!ReadBy.Contains(userId)`), `FirstUnreadMessageId` (Min), `LastMessage` (OrderByDescending + FirstOrDefault). Плюс `Include(x => x.Members)`. Для страницы из 50 чатов это 50× коррелированных подзапросов с фильтром по массиву `ReadBy` (`@>`/contains по `bigint[]`), который без GIN-индекса не индексируется. Дополнительно `GetTotalUserChats` (`:96-103`) делает ещё один Count с вложенным `Any` по Messages.
**Почему это проблема:** `ReadBy.Contains(userId)` транслируется в проверку по массиву, не покрываемую индексом `(ChatId, SentAt)`; на больших чатах с длинной историей `CountUnread`/`FirstUnreadMessageId` сканируют много строк. Латентность списка чатов растёт с историей и числом чатов.
**Рекомендация:** Денормализовать счётчик непрочитанных (поддерживать `unread_count`/`last_message_id` per (chat,user) в отдельной таблице или Redis), либо считать непрочитанные через таблицу «последний прочитанный message_id» вместо массива `ReadBy`. Как минимум — GIN-индекс на `ReadBy` если массивный contains остаётся.

### P3. GetChatMembers с int.MaxValue на каждый рассыл события — Medium
**Файл:** `Backend/BarkFluff.Messages/Features/MarkAsRead/MarkAsReadCommandHandler.cs:83`; `EditMessage...:223`; `DeleteMessage...:97`; `PinMessage...:107`; `UnpinMessage...:72`; `UnpinAll...:69`; `SendMessage...:321`; `KickUser...:119`
**Проблема:** Восемь хендлеров вызывают `GetChatMembers(chatId, 0, int.MaxValue)` для получения списка участников перед публикацией события. Это unbounded-выборка полных сущностей `ChatMember` (а нужен только `UserId`).
**Почему это проблема:** Для крупных групп каждое действие (отправка, правка, удаление, пин, отметка прочтения) грузит всю таблицу участников в память. Линейный рост стоимости с размером группы на каждую операцию.
**Рекомендация:** Добавить метод `GetChatMemberIds(chatId)` с проекцией `Select(m => m.UserId)` и кэшировать результат (Redis) с инвалидацией при изменении состава. Это же снимает часть нагрузки P1.

### P4. GetChatInfo делает GetChat дважды и не использует AsNoTracking — Medium
**Файл:** `Backend/BarkFluff.Messages/Features/GetChatInfo/GetChatInfoCommandHandler.cs:94,125`
**Проблема:** В одном запросе `GetChat(chatId)` (с `Include(Members)`) вызывается дважды: один раз для определения собеседника в личном чате (`:94`), второй — для списка участников (`:125`). Между ними возможен ещё вызов Users API. `ChatsStorage.GetChat` (`:215-221`) не использует `AsNoTracking`, хотя данные read-only.
**Почему это проблема:** Дублирующийся запрос с `Include` к БД на каждый `GetChatInfo`; tracking создаёт лишний оверхед на read-only пути.
**Рекомендация:** Поднять чат с участниками один раз и переиспользовать; добавить `AsNoTracking()` в `GetChat`. Также `GetChatInfo` (`ChatsStorage.cs:235-256`) сам строит коррелированные подзапросы Count/Min/Max по Messages — те же замечания, что в P2.

### P5. Большинство read-only запросов в ChatsStorage/PinnedMessagesStorage/EncryptedMessagesStorage без AsNoTracking — Medium
**Файл:** `Backend/BarkFluff.Messages/Persistence/Services/ChatsStorage.cs:16-24,59-79,118-121,210-221,235-256`; `PinnedMessagesStorage.cs:16-35`; `EncryptedMessagesStorage.cs:35-102`
**Проблема:** `GetDmChatsWithUser`, `GetUserChatIdWithPerson`, `GetChat`, `GetChatInfo`, `GetTotalChatMembers`, чтения пинов и шифрованных сообщений (`GetPinByMessageIdAsync`, `ListByChatAsync`, `CountByChatAsync`, `GetByIdAsync`, `ListByChatAsync`) выполняются без `AsNoTracking`, хотя сущности используются только для чтения/маппинга. `GetChatMessages`/`GetChatMessagesWithOffset` (`MessagesStorage.cs`) и `GetUserChats` — корректно с `AsNoTracking`.
**Почему это проблема:** EF Change Tracker строит снапшоты для каждой материализованной сущности — лишние CPU/память на read-only запросах, особенно `GetDmChatsWithUser` (вызывается в consumer-ах на каждое изменение имени/аватара и грузит все DM-чаты пользователя с участниками).
**Рекомендация:** Добавить `AsNoTracking()` ко всем перечисленным read-only запросам.

### P6. EncryptedMessages: история приватного чата читается без проверки лимита суммарной выборки — Low
**Файл:** `Backend/BarkFluff.Messages/Persistence/Services/EncryptedMessagesStorage.cs:46-102`; `Backend/BarkFluff.Messages/Features/ListPrivateMessages/ListPrivateMessagesQueryHandler.cs:46-50`
**Проблема:** `offsetBefore`/`offsetAfter` каждый ограничен `MaxOffset=50`, но суммарно возвращается до `before(50)+reference(1)+after(50)=101` сообщение, каждое с полным `Ciphertext` (до 64 KiB). При запросе обоих направлений на максимуме — до ~6.4 МБ шифротекста за один вызов. Индекс `(ChatId, SentAt)` есть — это смягчает, но размер ответа не ограничен по байтам.
**Почему это проблема:** Потенциально крупные ответы на горячем пути чтения; для обычного текста некритично, но для приватных чатов с большими ciphertext суммарный объём растёт.
**Рекомендация:** Ограничить суммарное число сообщений за запрос (например, общий бюджет 50), либо ограничить по совокупному размеру ciphertext.

## Производительность / прочее (подтверждено отсутствие проблем)

- Sync-over-async (`.Result`/`.Wait()`/`GetAwaiter().GetResult()`) — не найдено (grep по сервису пуст).
- `MarkMessagesAsRead` использует параметризованный `ExecuteSqlRawAsync` с `NpgsqlParameter` — инъекции нет.
- `GetChatAttachmentsAsync` (`MessagesStorage.cs:180-242`) строит SQL интерполяцией, но интерполируются только серверные значения (`(int)attachmentType` и константы сортировки `DESC`/`ASC`/`!= 8`), а `chatId`/`take`/`skip` параметризованы — SQL-инъекции нет (см. ниже отдельную заметку D2 о хрупкости).
- `GetUserAllMessagesQueryHandler` использует `AsAsyncEnumerable()` (стриминг) и `AsNoTracking` — история не грузится целиком в память.
- Индексы под частые запросы есть: `Messages (ChatId, SentAt)`, `EncryptedMessages (ChatId, SentAt)`, `ChatMembers (ChatId, UserId)`, `PinnedMessages (ChatId, MessageId) unique`.
- gRPC-клиенты Users/Files регистрируются через `AddGrpcClient` (фабрика каналов), а не создаются на запрос — корректно.

## Docker / nginx

### D1. nginx: gRPC-эндпоинт messages без ограничения размера тела запроса — Medium
**Файл:** `Backend/nginx/messages.conf:15-24`
**Проблема:** В `location /` не задан `client_max_body_size`/`grpc_buffer_size`, а сам сервис ограничивает размеры только на уровне приложения (текст 4096, вложений ≤10, ciphertext 64 KiB, envelope 16 KiB). gRPC `SendMessageRequest`/`SendPrivateMessage` не ограничены по суммарному размеру на уровне прокси и Kestrel (в `Program.cs:37-40` `AddGrpc` без `MaxReceiveMessageSize`).
**Почему это проблема:** Дефолтный лимит gRPC в .NET — 4 МБ на сообщение, но прикладные проверки `MaxCiphertextLength`/`MaxEnvelopeLength` выполняются уже после полной десериализации тела. Большие тела (в пределах 4 МБ × частота) — вектор ресурсной нагрузки. `AssociatedData`/`Ciphertext` валидируются после `ToByteArray()`.
**Рекомендация:** Явно задать разумный `MaxReceiveMessageSize` в `AddGrpc` (с запасом над максимальным валидным сообщением, напр. 256 KiB–1 МБ) и/или `client_max_body_size` в nginx, чтобы переполненные тела отбрасывались до обработки.

### D2. Хрупкая интерполяция типа вложения в SQL (не инъекция, но риск регрессии) — Low
**Файл:** `Backend/BarkFluff.Messages/Persistence/Services/MessagesStorage.cs:188-232`
**Проблема:** `attachmentTypeFilter` собирается строкой через конкатенацию `"AND a.\"Type\" = " + typeValue`, где `typeValue` — `(int)` enum. Инъекция невозможна (int), но это образец, который при будущем рефакторинге (например, прокинуть строковый фильтр) легко превратить в уязвимость. `sortOrder` тоже подставляется строкой.
**Почему это проблема:** Сам по себе код безопасен, но смешение параметризованных (`@chatId`, `@take`, `@skip`) и интерполированных значений в одном запросе — антипаттерн, повышающий вероятность ошибки при доработке.
**Рекомендация:** Параметризовать и тип (`new NpgsqlParameter("@type", typeValue)`), сортировку оставлять только из whitelisted-констант (уже так). Не критично, отметка на будущее.

### D3. Dockerfile.slim копирует локальный publish/ — риск рассинхрона артефактов — Low
**Файл:** `Backend/BarkFluff.Messages/Dockerfile.slim:1-6`
**Проблема:** `Dockerfile.slim` копирует уже собранный `publish/` без шага восстановления/сборки. Основной `Dockerfile` корректен (chiseled, non-root `USER $APP_UID`, multi-stage). Slim полагается на внешний build, что не аудит-проблема безопасности, но источник «протухших»/непроверенных артефактов.
**Почему это проблема:** Нет гарантии воспроизводимости/чистоты артефакта; при ручной сборке slim может попасть лишнее из локального `publish/`.
**Рекомендация:** Документировать, какой pipeline наполняет `publish/`, или собирать slim из того же multi-stage. Низкий приоритет.

## Замечания по конфигурации (для сведения)

- Секретов в `appsettings.json`/`appsettings.Development.json` сервиса нет — конфигурация (`MessagesDb`, `Redis`, `RabbitMQ:*`, `JwtSettings:SecretKey`, `*Service:Token`) подгружается из Configuration-сервиса (`LoadConfiguration`). Хардкод найден только в design-time factory `MessagesContextFactory.cs:12` (`Username=postgres;Password=postgres`) — это только для `dotnet ef` миграций локально, в рантайм не используется; держать осознанно.
- `AllowedHosts: "*"` в `appsettings.json:8` — стандартно для gRPC за nginx, не проблема.
