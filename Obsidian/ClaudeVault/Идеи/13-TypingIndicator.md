# ✏️ Статус «печатает...» и «записывает голос...»

> Категория: UX / Real-time
> Платформы: ВСЕ
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

Классический индикатор **"Иван печатает..."** под именем контакта или в нижней части чата. При записи голосового сообщения — меняется на **"записывает голос..."**. Работает через [[../Backend/Updates]] стриминг без хранения в БД.

---

## Ключевые возможности

- «печатает...» при вводе текста (debounce 2 сек — сбрасывается если нет ввода)
- «записывает голос...» при зажатии кнопки записи
- В групповом чате: «Иван, Мария печатают...»
- Анимированные три точки (●●●)
- Автоматически скрывается через 5 сек даже если событие Stop не пришло

---

## Архитектура

### Proto

```protobuf
rpc SendTypingEvent(TypingEventRequest) returns (Empty);

message TypingEventRequest {
  string chat_id = 1;
  TypingEventType type = 2;
}

enum TypingEventType {
  TYPING_START  = 0;
  TYPING_STOP   = 1;
  VOICE_RECORD  = 2;
}
```

- Новый метод в [[../Backend/Updates]] или [[../Backend/Messages]]
- Клиент отправляет `TypingStart` при первом символе, `TypingStop` при паузе/отправке
- Сервер транслирует через gRPC stream всем участникам чата
- **Не хранится в PostgreSQL** — только relay через Redis Pub/Sub

### Redis

```
PUBLISH typing:{chat_id} {user_id}:{type}:{timestamp}
TTL ключа = 5 сек (auto-expire защита от зависших статусов)
```

---

## Клиентская реализация

| Платформа | Реализация |
|-----------|-----------|
| **Android** | `TextWatcher.afterTextChanged()` → gRPC, debounce через `Handler.postDelayed` |
| **WPF** | `TextBox.TextChanged` → debounce через `DispatcherTimer`, отправка через `WebApi` |
| **macOS/iOS** | `onChange(of: draftText)` в SwiftUI, `Task.sleep` для debounce |
| **Linux/Qt** | `QLineEdit::textChanged` сигнал |

---

## UI

- Строка под именем в шапке чата (1-on-1)
- Строка вместо последнего сообщения в списке чатов при активном typing
- Анимация: три точки с CSS/Canvas/Lottie пульсацией
