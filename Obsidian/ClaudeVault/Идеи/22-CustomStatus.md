# 🎨 Кастомный статус и «сейчас слушаю»

> Категория: Персонализация / Социальное
> Платформы: ВСЕ
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

Пользователь может установить **текстовый статус с эмодзи** (как в Discord/Slack), который видят все собеседники рядом с именем. Опционально — автоматическое подтягивание **«Сейчас слушаю»** из Spotify/Apple Music/VK Музыки.

---

## Ключевые возможности

- Статус: произвольный текст до 60 символов + 1 эмодзи
- Время действия: 30 мин / 1 час / 4 часа / сегодня / не очищать
- Быстрые пресеты: 🏖 «В отпуске», 🤒 «Болею», 💻 «На работе», 🎮 «Играю»
- Автоочистка по истечении времени
- Отображение под именем в профиле и шапке чата
- «Сейчас слушает 🎵 — Название трека · Исполнитель» (интеграция с музыкой)

---

## Архитектура

### Хранение (Users сервис)

```sql
ALTER TABLE users ADD COLUMN status_emoji TEXT;
ALTER TABLE users ADD COLUMN status_text TEXT;
ALTER TABLE users ADD COLUMN status_expires_at TIMESTAMPTZ;
```

```protobuf
rpc SetStatus(SetStatusRequest) returns (Empty);
rpc ClearStatus(ClearStatusRequest) returns (Empty);

message SetStatusRequest {
  string emoji = 1;
  string text = 2;
  google.protobuf.Timestamp expires_at = 3;  // null = не очищать
}
```

- `UserResponse` дополняется полями `status_emoji`, `status_text`
- Фоновый воркер в [[../Backend/Users]] каждые 5 мин очищает истёкшие статусы
- При смене статуса → событие в RabbitMQ → [[../Backend/Updates]] уведомляет контакты (обновление онлайн-статуса уже работает через [[../Backend/Onliner]])

---

## Интеграция «Сейчас слушаю»

### Android

```kotlin
// NotificationListenerService — слушает медиа-уведомления
class MediaNotificationListener : NotificationListenerService() {
    override fun onNotificationPosted(sbn: StatusBarNotification) {
        val extras = sbn.notification.extras
        val title = extras.getString(Notification.EXTRA_TITLE)   // трек
        val text = extras.getString(Notification.EXTRA_TEXT)     // исполнитель
        // обновить статус через gRPC если пользователь включил эту функцию
    }
}
```

- Требует разрешения `BIND_NOTIFICATION_LISTENER_SERVICE`

### macOS

```swift
// MusicKit / MediaRemote (private, только для macOS)
// Или MPNowPlayingInfoCenter
let nowPlaying = MPNowPlayingInfoCenter.default().nowPlayingInfo
let title = nowPlaying?[MPMediaItemPropertyTitle] as? String
```

### WPF

```csharp
// SMTC (System Media Transport Controls) через WinRT
var smtc = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().Result;
var session = smtc.GetCurrentSession();
var info = session?.TryGetMediaPropertiesAsync().AsTask().Result;
// info.Title, info.Artist
```

---

## UI

- Строка статуса под именем в профиле: `😴 Сплю до 10:00`
- В шапке чата под именем собеседника: `🎵 Imagine Dragons · Believer`
- Экран установки статуса: поле эмодзи + текст + chips для времени + пресеты
- Иконка музыкальной ноты 🎵 с анимацией (пульсация) при активном «Сейчас слушаю»

