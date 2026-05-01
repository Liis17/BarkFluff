# 😄 Реакции на сообщения

> Категория: UX
> Приоритет: 🟢 Низкий (но быстро реализуемо)
> Сложность: ⭐⭐

---

## Описание

Возможность **быстро реагировать на сообщение эмодзи**, не отправляя новое сообщение в чат. Реакции видны всем участникам. Long-tap на сообщение → панель эмодзи → выбор реакции.

---

## Ключевые возможности

- Набор базовых реакций: 👍 ❤️ 😂 😮 😢 🔥 (+ кастомные из стикерпаков)
- Возможность поставить несколько разных реакций от разного пользователя
- Счётчик реакций под сообщением (сгруппировано по эмодзи)
- Tap на реакцию → список пользователей, которые поставили эту реакцию
- Снять реакцию повторным нажатием
- Анимация появления реакции (bounce)
- Real-time синхронизация через [[../Backend/Updates]]

---

## Архитектура

### База данных (Messages сервис)

```sql
CREATE TABLE message_reactions (
    id UUID PRIMARY KEY,
    message_id UUID NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
    user_id UUID NOT NULL,
    emoji TEXT NOT NULL,         -- unicode emoji или custom sticker id
    created_at TIMESTAMPTZ NOT NULL,
    UNIQUE(message_id, user_id, emoji)
);
```

### gRPC методы (Messages)

```protobuf
rpc AddReaction(AddReactionRequest) returns (Empty);
rpc RemoveReaction(RemoveReactionRequest) returns (Empty);
rpc GetReactions(GetReactionsRequest) returns (GetReactionsResponse);
```

### Real-time

- `AddReaction` / `RemoveReaction` публикует событие в RabbitMQ
- [[../Backend/Updates]] стримит `ReactionAdded` / `ReactionRemoved` всем участникам чата

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Таблица `message_reactions`, методы Add/Remove/Get |
| [[../Backend/Updates]] | События `ReactionAdded`, `ReactionRemoved` |
| [[../Shared/Proto]] | `reaction.proto` или расширение `messages.proto` |

---

## UI

- Android: long-press popup с рядом эмодзи (Material 3 стиль)
- WPF: hover → появление кнопки «+» → всплывающая панель эмодзи
- Счётчики отображаются горизонтально под пузырём сообщения
- «+N» если реакций много (показать все по tap)
