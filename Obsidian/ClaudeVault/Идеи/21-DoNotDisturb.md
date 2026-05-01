# 🌙 Расписание «Не беспокоить» (DND)

> Категория: Уведомления / Приватность
> Платформы: ВСЕ
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

Режим **«Не беспокоить»** по расписанию — пользователь задаёт часы тишины (например, с 23:00 до 8:00), и в это время push-уведомления и звуки отключаются. Исключения: «важные» контакты пробивают режим.

---

## Ключевые возможности

- Ручное включение DND (как в телефоне)
- Расписание: «каждый день с HH:mm до HH:mm»
- Дни недели (только будни / только выходные / конкретные дни)
- Исключения: список «VIP-контактов» — уведомления от них всегда проходят
- Режим «отключить на 1 час / до утра / навсегда» (быстрые пресеты)
- Иконка луны 🌙 в шапке при активном DND

---

## Архитектура — полностью клиентская

Никаких изменений на сервере. Логика живёт на клиенте — при получении события из [[../Backend/Updates]] клиент решает, показывать уведомление или нет.

### Android

```kotlin
// DndManager.kt — синглтон
object DndManager {
    fun isQuietNow(): Boolean {
        val prefs = EncryptedSharedPreferences...
        val schedule = loadSchedule(prefs)
        return schedule.isActive(LocalTime.now(), DayOfWeek.now())
    }
}

// В RealtimeUpdateService при получении сообщения:
if (!DndManager.isQuietNow() || DndManager.isVip(senderId)) {
    notificationManager.showNotification(...)
}
```

- `WorkManager` с `PeriodicWorkRequest` на 15 мин → включает/выключает DND по расписанию
- Интеграция с Android системным DND через `NotificationManager.setInterruptionFilter()` (опционально)

### WPF

```csharp
// В NotificationManager.cs — новый метод
public bool ShouldNotify(string senderId)
{
    var dnd = App.GParam.DndSettings;
    if (!dnd.IsEnabled) return true;
    if (dnd.VipContacts.Contains(senderId)) return true;
    return !dnd.IsActiveNow(DateTime.Now);
}
```

- `DndSettings` добавляется в `GlobalParam.json` (зашифровано вместе с остальным)
- `DispatcherTimer` на 1 мин проверяет расписание → обновляет `App.GParam.IsDndActive`

### macOS / iOS

```swift
// DndService.swift — @Observable
// UserNotifications framework: UNNotificationCategory с настройками тишины
// Или просто клиентская фильтрация без системного DND
```

---

## UI

- Раздел «Уведомления» → «Не беспокоить» в настройках
- Быстрое включение через long-press на иконку колокольчика в профиле
- Индикатор 🌙 в статусбаре / шапке при активном DND
- Android: плитка быстрого доступа (Tile API) для включения DND одним свайпом

