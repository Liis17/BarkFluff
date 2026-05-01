# 🖥️ Drag & Drop файлов и мультизагрузка — WPF + macOS

> Категория: Продуктивность
> Платформы: **WPF** (уже частично), **macOS**, **Linux Qt**
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

Расширенный **Drag & Drop** на десктопных клиентах: перетащить несколько файлов из файлового менеджера прямо в окно чата. Папку — автоматически ZIP-архивировать. Превью файлов до отправки с возможностью изменить порядок и удалить ненужные.

---

## Ключевые возможности

- Перетащить 1 или несколько файлов в окно чата
- Папка → авто-zip с прогресс-баром
- Превью вложений до отправки: сетка миниатюр (как в мобильных клиентах)
- Переупорядочить файлы перед отправкой (drag within preview)
- Удалить файл из очереди крестиком
- Подпись/комментарий ко всей группе файлов
- Ограничение: до 10 файлов за раз, проверка лимитов из [[../Клиенты/DesignDocument]]
- Вставка из буфера обмена (`Ctrl+V`) — скриншот или файл

---

## WPF — уже частично реализован

В [[../Клиенты/Windows-WPF]] уже есть `MessengerPage.DragDrop.cs`. Нужно расширить:

```csharp
// MessengerPage.DragDrop.cs — расширение
private async void OnFilesDropped(string[] filePaths)
{
    // 1. Папки → ZIP через System.IO.Compression
    // 2. Показать AttachmentPreviewPanel (новый UserControl)
    // 3. Пользователь редактирует очередь
    // 4. Confirm → загрузить все через AttachmentController (уже есть)
}

// Вставка из буфера
protected override void OnPreviewKeyDown(KeyEventArgs e)
{
    if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control)
    {
        if (Clipboard.ContainsImage())  // скриншот
        if (Clipboard.ContainsFileDropList())  // файлы
    }
}
```

- `AttachmentPreviewPanel.xaml` — новый `UserControl` с `WrapPanel` миниатюр
- Каждая миниатюра: `Grid` с `Image` + кнопка ✕ + drag handle для переупорядочивания (`AllowDrop` + `MouseMove`)

---

## macOS (SwiftUI + AppKit)

```swift
// SwiftUI Drop support
.onDrop(of: [.fileURL], isTargeted: $isDragTarget) { providers in
    providers.forEach { provider in
        provider.loadItem(forTypeIdentifier: UTType.fileURL.identifier) { item, _ in
            // обработка файла
        }
    }
    return true
}

// Визуальный индикатор drop zone
.border(isDragTarget ? Color.accentColor : Color.clear, width: 2)
```

- `AttachmentPreviewSheet.swift` — `sheet` с сеткой превью (LazyVGrid)
- Для папок: `Process` → `zip` утилита macOS

---

## Linux Qt

```cpp
// QWidget::dragEnterEvent + dropEvent
void ChatWidget::dropEvent(QDropEvent *event) {
    const QMimeData *mimeData = event->mimeData();
    if (mimeData->hasUrls()) {
        QList<QUrl> urls = mimeData->urls();
        // обработка списка файлов
    }
}
```

---

## UI — превью очереди отправки (все десктопы)

```
┌──────────────────────────────────────────────┐
│  [🖼 photo1.jpg ✕] [📄 doc.pdf ✕] [+ Ещё]  │
│                                              │
│  [ Добавить подпись...                    ]  │
│                              [Отмена] [📤]   │
└──────────────────────────────────────────────┘
```

