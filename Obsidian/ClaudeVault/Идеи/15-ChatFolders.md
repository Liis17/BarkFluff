# 🔇 Папки и архив чатов

> Категория: Организация
> Платформы: ВСЕ
> Приоритет: 🟡 Средний
> Сложность: ⭐⭐

---

## Описание

Пользователь может создавать **именованные папки** для сортировки чатов (например: «Работа», «Семья», «Боты») и **архивировать** ненужные чаты — они пропадают из основного списка, но остаются доступны.

---

## Ключевые возможности

### Папки
- Создать / переименовать / удалить папку
- Перетащить чат в папку (drag & drop на WPF; long-press → меню на Android)
- Счётчик непрочитанных в папке
- Иконка-эмодзи для папки (выбрать из набора)
- Вкладки папок в верхней части списка чатов

### Архив
- Архивировать чат → исчезает из основного списка
- Раздел «Архив» в самом низу или по отдельной иконке
- Авто-разархивация при новом сообщении (настраивается)
- Счётчик архивированных чатов

---

## Архитектура

**Полностью клиентская фича** — не требует изменений на сервере!

### Android

```kotlin
// SharedPreferences / Room DB
data class ChatFolder(val id: String, val name: String, val emoji: String, val chatIds: List<String>)
// LocalStorage.kt — новые методы saveFolders() / loadFolders()
```

### WPF

```csharp
// В GlobalParam.json добавить:
public List<ChatFolder> Folders { get; set; } = new();
public List<string> ArchivedChatIds { get; set; } = new();
// Зашифровано вместе с остальным GlobalParam (AES-256/PBKDF2)
```

### macOS/iOS

```swift
// UserDefaults или отдельный JSON через FileManager
// @AppStorage("chatFolders") в Settings
```

---

## Опциональная синхронизация между устройствами

Для синхронизации папок между устройствами — добавить хранение в [[../Backend/Users]] профиле:

```protobuf
rpc SaveChatFolders(SaveChatFoldersRequest) returns (Empty);
rpc GetChatFolders(GetChatFoldersRequest) returns (ChatFoldersResponse);
```

Папки хранятся как JSONB в таблице `user_settings` — лёгкое расширение без новых таблиц.

---

## UI по платформам

| Платформа | Детали |
|-----------|--------|
| **Android** | `TabLayout` над `RecyclerView` чатов; drag & drop через `ItemTouchHelper` |
| **WPF** | Горизонтальные `RadioButton`-вкладки в стиле проекта над списком чатов |
| **macOS/iOS** | SwiftUI `Picker` стиль `.segmented` или кастомные `HStack` таблы |

