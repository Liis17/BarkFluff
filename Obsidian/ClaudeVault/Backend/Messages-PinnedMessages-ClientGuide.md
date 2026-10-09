# Закреплённые сообщения — контракт клиента

Закрепление — метаданные сообщения в конкретном чате; содержимое сообщения не меняется. RPC находятся в `Shared/BarkFluff.Proto/messages_api.proto`. Ниже пути к коду относительны `Backend/BarkFluff.Messages/`, пути `Shared/` и `Tests/` — от корня репозитория.

## RPC

| RPC | Семантика |
|---|---|
| `PinMessage(chat_id, message_id)` | Закрепляет существующее неудалённое сообщение этого чата. Повтор возвращает текущее `PinnedMessageInfo` без дубля. Лимит — 100 закреплений на чат. |
| `UnpinMessage(chat_id, message_id)` | Удаляет закрепление; если его нет, запрос ничего не меняет. |
| `ListPinnedMessages(chat_id, pagination)` | Возвращает страницу `PinnedMessageInfo` и `total_count`; сервер ограничивает размер страницы 50. |
| `UnpinAll(chat_id)` | Удаляет все закрепления и возвращает `unpinned_count`; пустой список — no-op. |

Операции доступны участникам чата. Успешный pin/unpin/unpin-all добавляет системное сообщение в обычную историю и отправляет событие участникам. При идемпотентном no-op системное сообщение и update не создаются. `PinnedMessageInfo` содержит метаданные закрепления и снимок сообщения; локальные вложения разрешаются через Files, федеративные — из сохранённого снимка.

## Realtime-события

Для изменений слушайте `SubscribeMessagesPinned`, `SubscribeMessagesUnpinned` и `SubscribeAllMessagesUnpinned` из `Shared/BarkFluff.Proto/updates_api.proto`. События содержат chat и message ID; pin дополнительно содержит pinner и timestamp. Считайте события дельтой/инвалидацией кеша; после reconnect или пропуска события восстановите список через `ListPinnedMessages`.

## Состояние клиента

Храните pins отдельно от обычного порядка истории. Для начальной загрузки/страниц используйте `ListPinnedMessages`; результат pin применяйте из ответа. `AllMessagesUnpinned` очищает список этого чата. Закреплённое сообщение может быть старше загруженной истории: переходите к нему по chat/message ID и подгружайте историю через Messages.

Системные сообщения дополняют историю, но не заменяют update events. В pin event есть ID закрепившего, но нет его имени; при необходимости запросите профиль через Users.

Код: `Features/PinMessage/`, `Features/UnpinMessage/`, `Features/ListPinnedMessages/`, `Features/UnpinAll/`, `Infrastructure/MessageQueueSender.cs`. Связанные заметки: [[Backend/Messages]], [[Backend/Updates]].
