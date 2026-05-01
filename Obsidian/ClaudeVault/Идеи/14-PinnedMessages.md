# 📌 Закреплённые сообщения в чате

> Категория: UX / Организация
> Платформы: ВСЕ
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

Возможность **закрепить** одно или несколько сообщений в верхней части чата. Закреплённое сообщение показывается в виде баннера под шапкой чата. В групповых чатах закреплять могут только администраторы.

---

## Ключевые возможности

- Закрепить / открепить любое сообщение (long-press → меню)
- Баннер закреплённого сообщения под шапкой чата (tap → scroll to message)
- Несколько закреплённых сообщений — свайп по баннеру переключает между ними
- Уведомление в чате: «Иван закрепил сообщение»
- В групповых чатах: право закреплять — только Owner/Admin
- Поддерживаются все типы: текст, фото, видео, документ

---

## Архитектура

### База данных (Messages)

```sql
ALTER TABLE chats ADD COLUMN pinned_message_ids UUID[] DEFAULT '{}';
-- или отдельная таблица для множественного закрепа:
CREATE TABLE pinned_messages (
    chat_id UUID NOT NULL,
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    pinned_by UUID NOT NULL,
    pinned_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (chat_id, message_id)
);
```

### gRPC методы (Messages)

```protobuf
rpc PinMessage(PinMessageRequest) returns (Empty);
rpc UnpinMessage(UnpinMessageRequest) returns (Empty);
rpc GetPinnedMessages(GetPinnedMessagesRequest) returns (PinnedMessagesResponse);
```

- При pin/unpin → событие `MessagePinned` / `MessageUnpinned` в RabbitMQ → [[../Backend/Updates]] стримит всем
- `ChatResponse` дополняется полем `repeated PinnedMessage pinned_messages`

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Таблица `pinned_messages`, методы Pin/Unpin/Get |
| [[../Backend/Updates]] | События `MessagePinned`, `MessageUnpinned` |
| [[../Shared/Proto]] | Методы и поля в `messages.proto` |

---

## UI по платформам

| Платформа | Детали |
|-----------|--------|
| **Android** | `PinnedMessageBanner` — кастомная View под Toolbar; анимация slide-down при появлении |
| **WPF** | `Border` с текстом поверх `MessagesScrollViewer`, привязан к `ReactivePinnedMessage` |
| **macOS/iOS** | SwiftUI `VStack` с `pinnedMessageView` через `@State` |

