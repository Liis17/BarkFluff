# ⌨️ Горячие клавиши и командная строка — WPF / macOS

> Категория: Продуктивность
> Платформы: **WPF**, **macOS**, **Linux Qt**
> Приоритет: 🟢 Простая реализация
> Сложность: ⭐⭐

---

## Описание

**Command Palette** (`Ctrl+K` / `⌘K`) — всплывающее поле быстрого поиска и выполнения действий, как в VS Code или Raycast. Плюс расширенная система горячих клавиш для всего мессенджера.

---

## Ключевые возможности

### Command Palette
- `Ctrl+K` / `⌘K` → всплывающий поиск
- Найти чат по имени → открыть
- Найти пользователя → создать диалог
- Команды: `/new-chat Иван`, `/settings`, `/logout`, `/theme dark`
- История последних открытых (без ввода)
- Стрелки вверх/вниз для навигации, Enter — выполнить, Esc — закрыть

### Горячие клавиши

| Клавиша (Win)  | Клавиша (Mac) | Действие                            |
| -------------- | ------------- | ----------------------------------- |
| `Ctrl+K`       | `⌘K`          | Command Palette                     |
| `Ctrl+F`       | `⌘F`          | Поиск в текущем чате                |
| `Ctrl+Shift+F` | `⌘⇧F`         | Глобальный поиск                    |
| `Alt+↑/↓`      | `⌥↑/↓`        | Переключение между чатами           |
| `Ctrl+N`       | `⌘N`          | Новый чат                           |
| `Ctrl+,`       | `⌘,`          | Открыть настройки                   |
| `Esc`          | `Esc`         | Закрыть открытую панель             |
| `Ctrl+Enter`   | `⌘Enter`      | Отправить сообщение (если включено) |
| `Ctrl+D`       | `⌘D`          | Заархивировать чат                  |
| `F5`           | —             | Обновить (переподключиться)         |

---

## WPF — реализация

```csharp
// CommandPalettePopup.xaml — новый UserControl поверх MessengerPage
// Активируется через KeyBinding в MainWindow:
<KeyBinding Key="K" Modifiers="Ctrl" Command="{Binding OpenCommandPaletteCommand}"/>

// CommandPaletteViewModel.cs
public class CommandPaletteViewModel
{
    private readonly IEnumerable<IChatItem> _chats;

    public ObservableCollection<PaletteResult> Results { get; } = new();

    public void Search(string query)
    {
        Results.Clear();
        // 1. Фильтр чатов по имени
        // 2. Системные команды (если query начинается с '/')
        // 3. Пользователи из Users кеша
    }
}
```

- `PaletteResult` — union: ChatResult / CommandResult / UserResult
- `ListBox` с кастомными `DataTemplate` для каждого типа
- Анимация появления: `DoubleAnimation` Opacity 0→1 + translateY

---

## macOS (SwiftUI)

```swift
// Overlay поверх всего контента
.overlay {
    if showCommandPalette {
        CommandPaletteView(isPresented: $showCommandPalette)
            .frame(maxWidth: 600)
            .padding(.top, 100)
            .transition(.opacity.combined(with: .move(edge: .top)))
    }
}
.onKeyPress(.return, phases: .down) { ... }

// KeyboardShortcut в Commands
CommandMenu("Навигация") {
    Button("Открыть Command Palette") { ... }
        .keyboardShortcut("k", modifiers: .command)
}
```

---

## Linux Qt

```cpp
// QShortcut
QShortcut *paletteShortcut = new QShortcut(QKeySequence("Ctrl+K"), this);
connect(paletteShortcut, &QShortcut::activated, this, &MainWindow::openCommandPalette);

// QDialog поверх основного окна
```

---

## UI командной строки

```
╔══════════════════════════════════════╗
║  🔍  Поиск чатов и команд...         ║
╠══════════════════════════════════════╣
║  💬 Иван Иванов           последний  ║
║  💬 Команда разработки    2 часа     ║
║  ─────────────────────────────────   ║
║  ⚙️  /settings — Настройки           ║
║  🌙  /theme dark — Тёмная тема       ║
╚══════════════════════════════════════╝
```

