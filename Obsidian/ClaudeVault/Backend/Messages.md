# BarkFluff.Messages

Messages управляет членством в чатах, историей обычных сообщений и зашифрованными личными сообщениями. Сервис предоставляет пользовательский и межсервисный API. Пути кода ниже указаны относительно `Backend/BarkFluff.Messages/`; `Shared/` и `Tests/` — от корня репозитория.

## Контракты

- `Shared/BarkFluff.Proto/messages_api.proto` описывает `MessagesApi` (`TokenType.User`) и `MessagesServerApi` (`TokenType.Service`). Пользовательские RPC охватывают чаты/участников, список и поиск, отправку/правку/удаление/прочтение, drafts, pins, личные приглашения и секретные чаты.
- Типы `ChatType`: `Regular`, `Private`, `Secret` (`Domain/ChatType.cs`, `Shared/BarkFluff.Proto/shared.proto`). Обычные группы и личные чаты используют `Regular`; зашифрованные личные чаты и секретные чаты между устройствами имеют отдельные модели.
- `ListChats` возвращает drafts, mute для чата, unread и последнюю активность. `DeleteChat` скрывает чат только у пользователя: он снова появится после новой активности. Отказ от личного приглашения сохраняет запись со статусом `Rejected` и удаляет pending invite; чат не удаляется.
- `SearchMessages` ищет по доступным пользователю обычным чатам с фильтрами по запросу, автору (user ID или UUID), полуоткрытому диапазону времени, вложениям и курсору. Зашифрованные личные/секретные сообщения не индексируются.
- Drafts и закрепления имеют отдельные RPC. В чате может быть не более 100 pins; успешные изменения pin/unpin публикуют update и системное сообщение.

## Отправка и события

`SendMessageCommandHandler` атомарно сохраняет сообщение и сериализованный `NewMessageEvent` через `MessagesStorage.AddMessageWithOutboxAsync`. `MessageOutboxDispatcher` забирает batch через PostgreSQL `FOR UPDATE SKIP LOCKED`, использует lease, публикует и повторяет отправку; истёкшие lease возвращаются в обработку. Доставка at-least-once, downstream-потребители должны учитывать event ID/идемпотентность. Остальные типы message events используют отдельные publishers.

Dispatcher polling — 2 секунды, batch до 100, lease 2 минуты; backoff 1/5/30 секунд → 2/10/30 минут, окно retries 7 дней, затем DeadLetter. `MessageOutboxJanitor` раз в час удаляет Delivered старше 7 дней по CreatedAt; DeadLetter автоматически не удаляет. Обычный outbox не гарантирует порядок по чату.

## Личные и секретные чаты

- Состояние приглашения в зашифрованный личный чат хранится в Messages; pending invitations находятся в Redis до принятия, отказа или истечения срока. `AcceptPrivateChat` добавляет участника; отказ оставляет статус `Rejected`.
- Секретные чаты привязаны к устройству. Messages хранит только непрозрачный зашифрованный envelope в Redis с TTL 24 часа до подтверждения доставки; plaintext сервис не видит. Этот TTL не удаляет обычные сообщения.
- Одноразовые prekeys устройств хранятся в Users; Messages не реализует общий KeyStore.

## Граница федерации

`MessagesServerApiService` реализует импорт федеративных чатов/сообщений, применение edit/delete/read, проверки членства/участников, получение сведений о чате, системные сообщения звонков, серверные send/edit/delete и проверки доступа для файлов/presence. `ExportChatEvents` объявлен в proto, но override отсутствует, RPC остаётся `Unimplemented`.

Участник удалённого чата может задаваться UUID/server без локального user ID. Метаданные вложения сохраняются в сообщении снимком, чтобы отображать удалённый файл без запроса к Files исходного узла. Edit/delete/read проверяют origin и применяют детерминированный LWW resolver. Messages отвечает на RPC проверки членства/файла/presence, которыми пользуются Federation, Files и Onliner.

## Хранение и источники

PostgreSQL-модель находится в `Persistence/MessagesContext.cs` и миграциях. Redis используется для chat cache, состояния приглашений и 24-часового secret buffer. `Program.cs` связывает пользовательский/межсервисный API, publishers/consumers, outbox dispatcher и janitor.

Основной код: `Host/MessagesApiService.cs`, `Host/MessagesServerApiService.cs`, `Features/SendMessage/SendMessageCommandHandler.cs`, `Persistence/Services/MessagesStorage.cs`, `BackgroundServices/MessageOutboxDispatcher.cs`, `Persistence/Services/ChatsStorage.cs`.

Связанные заметки: [[Backend/Updates]], [[Backend/Users]], [[Backend/Files]], [[Backend/Federation]], [[Backend/Calls]]. Тесты: `Tests/BarkFluff.Messages.Tests`.
