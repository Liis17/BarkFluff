# 🗓️ Запланированные сообщения

> Категория: Продуктивность
> Платформы: ВСЕ
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐

---

## Описание

Написать сообщение сейчас, а отправить его **в конкретное время** — через час, завтра утром, или точно в 09:00 в пятницу. Удобно для поздравлений, напоминаний команде, отложенных ответов в другом часовом поясе.

---

## Ключевые возможности

- «Отправить позже» при зажатии кнопки отправки
- DateTimePicker с быстрыми пресетами: «Через 1 час», «Сегодня вечером (20:00)», «Завтра утром (9:00)»
- Список «Запланированных сообщений» в меню чата
- Редактировать / удалить запланированное сообщение до отправки
- Поддержка вложений в запланированных сообщениях
- Отправка по серверному времени (не зависит от того, онлайн ли клиент)

---

## Архитектура

### База данных (Messages)

```sql
CREATE TABLE scheduled_messages (
    id UUID PRIMARY KEY,
    chat_id UUID NOT NULL,
    sender_id UUID NOT NULL,
    text TEXT,
    attachment_ids UUID[],
    scheduled_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    status TEXT DEFAULT 'pending'  -- pending / sent / cancelled
);
CREATE INDEX idx_scheduled_messages_at ON scheduled_messages(scheduled_at) WHERE status = 'pending';
```

### BackgroundService

```csharp
// В BarkFluff.Messages — ScheduledMessageDispatcher
// Каждую минуту: SELECT * FROM scheduled_messages WHERE scheduled_at <= NOW() AND status='pending'
// → публикует как обычное сообщение → статус = 'sent'
```

### gRPC методы (Messages)

```protobuf
rpc ScheduleMessage(ScheduleMessageRequest) returns (ScheduledMessageResponse);
rpc GetScheduledMessages(GetScheduledMessagesRequest) returns (ScheduledMessagesListResponse);
rpc EditScheduledMessage(EditScheduledMessageRequest) returns (ScheduledMessageResponse);
rpc CancelScheduledMessage(CancelScheduledMessageRequest) returns (Empty);
```

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Таблица `scheduled_messages`, `ScheduledMessageDispatcher` BackgroundService |
| [[../Shared/Proto]] | Методы Schedule/Get/Edit/Cancel в `messages.proto` |

---

## UI по платформам

| Платформа | Детали реализации |
|-----------|------------------|
| **Android** | Long-press на кнопку отправки → `BottomSheetDialog` с `TimePicker` + пресеты |
| **WPF** | Long-press / ПКМ на кнопку → кастомный `Popup` с `DateTimePicker` |
| **macOS/iOS** | `.contextMenu` на кнопку отправки → `DatePicker` sheet |

Иконка часов ⏰ на запланированном сообщении в превью чата и в самом чате до момента отправки.

