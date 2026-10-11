# BarkFluff.Federation

Federation связывает независимые узлы BarkFluff для удалённых профилей, личных чатов, вложений, presence и typing. Сервис предоставляет межузловой S2S gRPC и внутренний API для сервисов своего узла. Пути ниже указаны относительно `Backend/BarkFluff.Federation/`; `Shared/` и `Tests/` — от корня репозитория.

## Доверие и поиск узлов

- S2S-запросы используют подписи XFed, а не XAuth: `XFedClientInterceptor` подписывает канонические байты запроса; `XFedRawBytesMiddleware` и `XFedServerInterceptor` проверяют origin, destination, timestamp, ключ пира и подпись.
- Ed25519-ключи узла обслуживает `SigningKeyService`; открытые ключи и возможности публикуются в подписанном well-known документе. `ServerResolver` объединяет обнаруженные и заданные вручную пиры и обновляет ключи.
- События Federation отдельно подписывает `EventSigner`: подпись строится по protobuf-событию с очищенными полями подписи. Приёмник проверяет origin и ключ до применения события.

## Доставка событий

`OutboxWriter` создаёт подписанную запись для каждого уникального удалённого участника. Для первого сообщения сначала ставится `ChatCreated`, затем `NewMessage`. `OutboxDispatcher` обеспечивает at-least-once доставку и порядок в рамках `(Destination, ChatId)`; разные чаты отправляются независимо. Он атомарно забирает записи через PostgreSQL `FOR UPDATE SKIP LOCKED`, задаёт lease на 2 минуты, размер пакета — до 100 записей/1 МБ. Паузы повторов: 30 сек → 2 мин → 10 мин → 1 ч → 6 ч; предел — настраиваемое число попыток (по умолчанию 20, около семи суток). Отклонённые события отправляются в dead letter; `OutboxJanitor` удаляет старые доставленные записи.

`Shared/BarkFluff.Proto/federation_api.proto` задаёт S2S-контракт. `FederationS2SApiHandler` проверяет и направляет события чатов/сообщений в Messages; обработка идемпотентна по event ID. `NewMessageFederationConsumer` сохраняет стабильный `FederatedMessageId` отдельно от event ID.

`FederationS2SApiService` реализует `Ping`, `GetServerKeys`, `GetUserProfile`, `DeliverEvents`, `FetchFile`, `SubscribePresence` и `DeliverTyping`. `FetchChatHistory` объявлен в proto, но service override отсутствует. Внутренний proto также объявляет `FetchRemoteChatHistory`, но `FederationInternalApiService` его не реализует. Оба history RPC остаются `Unimplemented`; `MessagesServerApi.ExportChatEvents` тоже не реализован.

Входящие `ChatCreated` ограничены Redis-квотой на origin в час (по умолчанию 100, настраивается); повторная доставка одного event ID не расходует квоту заново. Отказ из-за приватности личного чата возвращает `FederatedDmRejected`, событие уходит в dead letter, а отправителю публикуется `FederatedChatRejectedEvent`.

## Удалённые пользователи, presence и typing

Users разрешает и хранит ссылки на удалённых пользователей по UUID сервера. Федерация применяет ограничения на origin и правила приватности перед передачей удалённых данных.

- Onliner периодически сообщает полный набор UUID, за которыми следят активные инстансы. `PresenceInterestRegistry` объединяет наборы; `PresenceStreamManager` открывает/сверяет S2S presence-потоки и убирает истёкший интерес.
- Presence фильтруется на исходном узле по членству и приватности владельца; Federation передаёт удалённый статус в UUID-кеш Onliner.
- Исходящий typing объединяет повторные heartbeat-события; получатель проверяет членство. Typing временный и не хранится в долговременной очереди.

Код: `Features/FederationInternalApi/`, `BackgroundServices/PresenceStreamManager.cs`, `Services/PresenceInterestRegistry.cs`, `Services/TypingCoalescer.cs`.

## Файлы

Для удалённого вложения Files сначала проверяет через Messages, что пользователь имеет доступ к сообщению; затем Federation получает поток у origin через `FetchFile`. На origin Messages проверяет доступ узла к вложению до вызова Files `FetchFileStream`. Загрузки используют чанки; временная ссылка на стороне пользователя является capability, а не публичным URL origin.

`RemoteFileCircuitBreaker` хранит состояние в памяти отдельно для каждого origin. Учитываются только транспортные ошибки; `NotFound`/`PermissionDenied` от доступного узла не открывают breaker. После порога ошибок circuit открывается и блокирует обращения до истечения окна. `TryEnter` после окна разрешает обращения; отдельной блокировки единственного half-open запроса в текущем коде нет.

## Внутренний API и источники

`FederationInternalApiService` защищён сервисным токеном; он предоставляет операции удалённых пользователей/файлов, управление пирами, статус и запросы исходящей доставки. S2S защищён XFed. Настройки находятся в Settings, ServiceId Federation: имя узла, ротация ключей, обнаружение пиров, пределы outbox, circuit breaker файлов и необязательные клиенты Onliner/Files/Messages/Users.

Основные файлы: `Host/FederationS2SApiService.cs`, `Host/FederationInternalApiService.cs`, `Infrastructure/OutboxWriter.cs`, `BackgroundServices/OutboxDispatcher.cs`, `Services/EventSigner.cs`, `Host/XFedServerInterceptor.cs`, `Services/ServerResolver.cs`.

Тесты: `Tests/BarkFluff.Federation.Tests`; двухузловой стенд: `Backend/dev-federation-testbed/`. Связанные заметки: [[Backend/Messages]], [[Backend/Users]], [[Backend/Onliner]], [[Backend/Files]].
