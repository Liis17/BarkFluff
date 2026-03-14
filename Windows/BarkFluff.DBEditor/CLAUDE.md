# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**BarkFluff.DBEditor** — WPF-приложение (.NET 10) для управления конфигурационными записями в PostgreSQL-базе сервиса `BarkFluff.Configuration`. Позволяет просматривать и редактировать параметры микросервисов через GUI без прямого доступа к БД.

## Build Commands

```bash
dotnet build BarkFluff.DBEditor.csproj
dotnet run --project BarkFluff.DBEditor.csproj
```

## Architecture

**MVVM** с WPF-UI (Fluent design), CommunityToolkit.Mvvm, Npgsql (без EF Core).

### Navigation Flow

```
App.OnStartup
  └─ Нет сохранённых аккаунтов → LoginWindow → MainWindow
  └─ Есть аккаунты            → AccountSelectorWindow → MainWindow
                                                          └─ Switch Account → AccountSelectorWindow
```

`App.xaml.cs` — центральный навигационный хаб. Управляет жизненным циклом окон.

### Layers

| Слой | Описание |
|------|----------|
| `Views/` | XAML + code-behind (только навигационная логика) |
| `ViewModels/` | Бизнес-логика с `[ObservableProperty]` / `[RelayCommand]` |
| `Services/DatabaseService.cs` | Прямые SQL-запросы через Npgsql |
| `Services/CredentialsService.cs` | Сохранение аккаунтов в JSON (`data/accounts.json`) |
| `Models/` | `ConfigItem`, `SavedAccount`, `ServiceGroup` |

### Key Implementation Details

- **Change tracking:** модельный уровень — `ConfigItem.IsChanged` сравнивает `Value` с `OriginalValue`
- **Таблица БД:** `"Configurations"` (Id, Section, Key, Value, EditedAt, EditedBy, EditedFrom, ServiceId)
- **ServiceGroup.GetServiceName()** — маппинг ServiceId (0–12) на имена микросервисов
- **Формат хоста:** `host:port` в поле Host (парсится в DatabaseService)
- **Формат хранения аккаунтов:** версионированный JSON v1; поддерживается миграция со старого base64-формата

### MainWindow UI

Два таба:
1. **Raw Table** — DataGrid со всеми конфигами (редактируемый столбец Value)
2. **Grouped by Service** — иерархический вид, сгруппированный по ServiceId

Кнопки Save/Revert видимы только при `HasChanges = true`.
