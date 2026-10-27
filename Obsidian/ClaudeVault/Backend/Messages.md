# BarkFluff.Messages

Messages управляет членством в чатах, историей обычных сообщений и зашифрованными личными сообщениями. Сервис предоставляет пользовательский и межсервисный API. Пути кода ниже указаны относительно `Backend/BarkFluff.Messages/`; `Shared/` и `Tests/` — от корня репозитория.

## Контракты

- `Shared/BarkFluff.Proto/messages_api.proto` описывает `MessagesApi` (`TokenType.User`) и `MessagesServerApi` (`TokenType.Service`). Пользовательские RPC охватывают чаты/участников, список и поиск, отправку/правку/удаление/прочтение, drafts, pins, личные приглашения и секретные чаты.
- Типы `ChatType`: `Regular`, `Private`, `Secret` (`Domain/ChatType.cs`, `Shared/BarkFluff.Proto/shared.proto`). Обычные группы и личные чаты используют `Regular`; зашифрованные личные чаты и секретные чаты между устройствами имеют отдельные модели.
- `ListChats` возвращает drafts, mute для чата, unread и последнюю активность. `DeleteChat` скрывает чат только у пользователя (или у всех локальных участников при `delete_for_everyone`): он снова появится после новой активности; скрытие публикуется как `ChatHiddenEvent`. `LeaveChat` (группы) удаляет участника и рассылает системное сообщение, но `ChatHiddenEvent` не публикует. Отказ от личного приглашения сохраняет запись со статусом `Rejected` и удаляет pending invite; чат не удаляется.
- `SearchMessages` ищет по доступным пользователю обычным чатам с фильтрами по запросу, автору (user ID или UUID), полуоткрытому диапазону времени, вложениям и курсору. Зашифрованные личные/секретные сообщения не индексируются.
- Drafts и закрепления имеют отдельные RPC. В чате может быть не более 100 pins; успешные изменения pin/unpin публикуют update и системное сообщение.

## Отправка и события

`SendMessageCommandHandler` атомарно сохраняет сообщение и сериализованный `NewMessageEvent` через `MessagesStorage.AddMessageWithOutboxAsync`. `MessageOutboxDispatcher` забирает batch через PostgreSQL `FOR UPDATE SKIP LOCKED`, использует lease, публикует и повторяет отправку; истёкшие lease возвращаются в обработку. Доставка at-least-once, downstream-потребители должны учитывать event ID/идемпотентность. Остальные типы message events используют отдельные publishers.

Dispatcher polling — 2 секунды, batch до 100, lease 2 минуты; backoff 1/5/30 секунд → 2/10/30 минут, окно retries 7 дней, затем DeadLetter. `MessageOutboxJanitor` раз в час удаляет Delivered старше 7 дней по CreatedAt; DeadLetter автоматически не удаляет. Обычный outbox не гарантирует порядок по чату.

## Диапазон прочтения и read outbox

Additive `MessagesApi.MarkChatAsReadUpTo(chat_id, up_to_message_id)` возвращает `has_more`; прежний `MarkAsRead(message_ids)` сохраняет explicit-ID контракт. Новый RPC по умолчанию отвечает `UNIMPLEMENTED` при `Messages:ChatReadRangeEnabled=false` или отсутствующей настройке. При включении проверяются положительный cutoff, доступ к чату, `ChatType.Regular` и принадлежность anchor чату; anchor может быть soft-deleted. Меняются максимум 100 сообщений за вызов по предикату `chat_id + id<=cutoff + !deleted + user not in ReadBy`, без исключения sender/system/imported типов. `has_more` сообщает о следующих непрочитанных строках.

При gate=true range RPC и exact MarkAsRead записывают изменение `ReadBy` и `MessageReadEvent` в одной транзакции; при gate=false exact RPC сохраняет прежний прямой publisher. `MessageOutboxEntry.EventKind=0` — прежний NewMessage payload, `1` — ReadBy; миграция `20261024002540_AddReadByMessageOutbox` добавляет default 0, сохраняет уникальность `EventId`, ограничивает уникальность MessageId только kind 0 и добавляет claim index по Status/EventKind/Id/NextAttemptAt. Dispatcher обрабатывает оба вида даже при выключенном gate, публикует не более 100 за batch и сортирует NewMessage перед ReadBy. ReadBy snapshots сохраняют `ReadBy`, `NewReaders`, локальных `ChatMembers` и прежний Updates wire semantics. Метрики имеют отдельный суффикс `new_message`/`read_by` для pending, delivered, reclaim, ошибок и dead letter.

Миграция подготовлена, но не применена. Включать gate можно только после обновления всех Messages replicas и их dispatcher; при откате сначала выключить gate, дождаться отсутствия Pending/Processing строк EventKind=1 и разрешить все DeadLetter до возврата старого бинарника. Уже Delivered rows могут оставаться до обычного retention. Полная последовательность: [ChatReadRangeRollout](../../../Docs/ChatReadRangeRollout.md).

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

## Trace-контекст outbox

Внутренние nullable-поля `TraceParent`, `TraceState`, `CorrelationId` в `MessageOutboxEntry` сохраняют контекст операции отдельно от event payload. Dispatcher восстанавливает его через `BarkFluff.GrpcServer.Tracing.TraceContext`: повторные попытки сохраняют TraceId, получая новый SpanId. Строки без контекста по-прежнему доставляются. Миграция `20261019211114_AddOutboxTraceContext` добавляет только nullable text-колонки.
