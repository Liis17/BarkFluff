# 📊 Опросы и голосования в чатах

> Категория: UX / Интерактивность
> Платформы: ВСЕ
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐⭐

---

## Описание

Создание **опросов (poll)** и **викторин (quiz)** прямо в чате. Участники голосуют нажатием, результаты отображаются в реальном времени в виде прогресс-баров. Особенно полезно для групповых чатов и будущих каналов ([[06-Channels]]).

---

## Ключевые возможности

- Опрос с 2–10 вариантами ответа
- Анонимное / публичное голосование (видно кто проголосовал)
- Один вариант / несколько вариантов
- Режим «Викторина» — один правильный ответ, показывается после голосования
- Закрыть голосование вручную (только создатель)
- Real-time обновление процентов без перезагрузки
- Нельзя изменить голос (или можно — настройка)

---

## Архитектура

### Опрос как особый тип сообщения

Опрос хранится в [[../Backend/Messages]] как сообщение с `message_type = POLL`, тело — JSON.

```sql
-- В таблице messages: type = 'POLL', content = JSON
-- Отдельная таблица для голосов:
CREATE TABLE poll_votes (
    poll_message_id UUID NOT NULL,
    user_id UUID NOT NULL,
    option_indexes INT[] NOT NULL,
    voted_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (poll_message_id, user_id)
);
```

### gRPC методы

```protobuf
rpc CreatePoll(CreatePollRequest) returns (MessageResponse);   // возвращает обычный MessageResponse
rpc VoteInPoll(VoteInPollRequest) returns (PollStateResponse);
rpc GetPollResults(GetPollResultsRequest) returns (PollStateResponse);
rpc ClosePoll(ClosePollRequest) returns (Empty);
```

### Real-time

- `VoteInPoll` → публикует `PollVoteEvent` в RabbitMQ → [[../Backend/Updates]] транслирует обновлённые проценты всем участникам чата

---

## Изменения в существующих сервисах

| Сервис | Изменение |
|--------|-----------|
| [[../Backend/Messages]] | Тип сообщения `POLL`, таблица `poll_votes`, методы Vote/Get/Close |
| [[../Backend/Updates]] | Событие `PollUpdated` → real-time проценты |
| [[../Shared/Proto]] | `PollContent` в `MessageContent`, методы в `messages.proto` |

---

## UI по платформам

### Пузырь опроса (все платформы)
```
┌─────────────────────────────┐
│ 🗳️ Где проведём митап?       │
│                             │
│ ○ Москва          35% ████░ │
│ ● Санкт-Петербург 50% █████ │  ← выбранный
│ ○ Казань          15% ██░░░ │
│                             │
│ 20 голосов · Закрыто        │
└─────────────────────────────┘
```

| Платформа | Детали |
|-----------|--------|
| **Android** | Кастомный `PollMessageViewHolder` в `MessageAdapter`; кнопки-варианты через `RadioGroup` / `CheckBox` |
| **WPF** | `DataTemplate` для Poll в `MessageTypeMapper.cs`; `ProgressBar` для процентов |
| **macOS/iOS** | SwiftUI `PollMessageView` с анимированным `ProgressView` |

Создание опроса — кнопка скрепки в панели ввода → «Опрос» в меню вложений.

