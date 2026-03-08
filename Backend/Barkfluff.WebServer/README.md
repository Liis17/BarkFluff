# Barkfluff WebServer

## Конфигурация

**Порт:** 64641  
**URL:** http://localhost:64641

## Структура проекта

### Контроллеры

1. **HomeController** (`/`)
   - Отдает главную страницу `html/barkfluff.html`

2. **InstallController** (`/install.ps1`)
   - Отдает PowerShell скрипт установки из `files/install.ps1`

3. **DownloadController** (`/download/installer`)
   - Отдает установщик `files/Barkfluff.Updater.CLI.exe`
   - Файл должен быть добавлен после запуска в контейнере

4. **FallbackController** (`/{**catchAll}`)
   - Обрабатывает все остальные пути
   - Отдает страницу пользователя `html/userpage.html`
   - HTML проходит через метод `UserPageService.ProcessUserPage()`

### Сервисы

**UserPageService**
- Метод `ProcessUserPage(string path)` - обрабатывает HTML для страницы пользователя
- TODO: Добавьте свою логику обработки (например, замена %%username%% на реальные данные)

### Файлы

- `html/barkfluff.html` - главная страница
- `html/userpage.html` - шаблон страницы пользователя
- `files/install.ps1` - скрипт PowerShell для установки
- `files/Barkfluff.Updater.CLI.exe` - установщик (добавляется после деплоя)

### Примеры использования

```bash
# Главная страница
GET http://localhost:64641/

# Скрипт установки
GET http://localhost:64641/install.ps1

# Скачать установщик
GET http://localhost:64641/download/installer

# Страница пользователя (любой путь)
GET http://localhost:64641/@username
GET http://localhost:64641/profile/user123
# и т.д.
```

### Добавление новых контроллеров

Для добавления новых страниц/маршрутов:
1. Создайте новый контроллер в папке `Controllers/`
2. Добавьте атрибуты `[ApiController]` и `[Route]` или `[HttpGet]`
3. Контроллер автоматически зарегистрируется через `MapControllers()`

**Важно**: Специфичные маршруты будут обработаны первыми, FallbackController срабатывает в последнюю очередь.
