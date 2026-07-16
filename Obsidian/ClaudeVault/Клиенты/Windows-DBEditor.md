# BarkFluff.DBEditor

WPF-приложение (.NET 10) для управления конфигурационными записями в PostgreSQL-базе [[Backend/Configuration]].

Расположение: `Windows/BarkFluff.DBEditor/`

## Сборка

```bash
dotnet build BarkFluff.DBEditor.csproj
dotnet run --project BarkFluff.DBEditor.csproj
```

## Архитектура

**MVVM** с WPF-UI (Fluent design), CommunityToolkit.Mvvm, Npgsql (без EF Core). `MainWindow`/`AccountSelectorWindow` используют ViewModels; `LoginWindow` — исключение: без ViewModel, бизнес-логика (валидация полей, проверка дублей имён, тест подключения к БД, сохранение) в code-behind.

### Навигация

```
App.OnStartup
  └─ Нет аккаунтов → LoginWindow → MainWindow
  └─ Есть аккаунты → AccountSelectorWindow → MainWindow
                                              └─ Switch Account → AccountSelectorWindow
```

`App.xaml.cs` — центральный навигационный хаб.

### Слои

| Слой | Описание |
|------|----------|
| `Views/` | XAML + code-behind (навигация; `LoginWindow` — ещё и бизнес-логика) |
| `ViewModels/` | `[ObservableProperty]` / `[RelayCommand]` |
| `Services/DatabaseService.cs` | Прямые SQL через Npgsql |
| `Services/CredentialsService.cs` | Аккаунты в JSON (`data/accounts.json`) |
| `Models/` | `ConfigItem`, `SavedAccount`, `ServiceGroup` |

### Детали реализации

- **Change tracking**: `ConfigItem.IsChanged` сравнивает `Value` с `OriginalValue`
- **Таблица БД**: `"Configurations"` (Id, Section, Key, Value, EditedAt, EditedBy, EditedFrom, ServiceId)
- **ServiceGroup.GetServiceName()** — маппинг ServiceId (0–12) на имена сервисов
- **Формат хоста**: `host:port` (парсится в DatabaseService)
- **Аккаунты**: версионированный JSON v1; поддерживается миграция со старого base64-формата

### MainWindow

Два таба:
1. **Raw Table** — DataGrid всех конфигов (редактируемый столбец Value)
2. **Grouped by Service** — иерархический вид по ServiceId

Кнопки Save/Revert видимы только при `HasChanges = true`.
