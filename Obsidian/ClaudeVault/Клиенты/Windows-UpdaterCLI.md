# Windows — Barkfluff.Updater.CLI

Автономная консольная утилита установки/обновления WPF-клиента BarkFluff на Windows. Не является частью основного приложения — отдельный exe, вызываемый в обход WPF-клиента (у самого WPF есть свой независимый механизм обновления через PowerShell-скрипт, см. [[Клиенты/Windows-WPF]]).

Расположение: `Windows/Barkfluff.Updater.CLI/`

## Tech Stack

| Технология | Версия | Назначение |
|------------|--------|-----------|
| .NET | 8.0 (не net10, как остальной Windows-стек) | `TargetFramework: net8.0`, `OutputType: Exe` |
| AWSSDK.Core | 4.0.7.4 | Присутствует как зависимость, прямого использования S3 в коде не обнаружено при аудите — вероятно задел на будущее (доставка билдов через S3/R2) |
| SharpZipLib | 1.4.2 | Распаковка ZIP-архива релиза |
| `app.manifest` | — | Манифест для запроса прав администратора (UAC) |

## Режимы работы (`AppMode`)

| Аргументы | Режим | Действие |
|-----------|-------|----------|
| `-install`, `--install`, `-i` | `Install` | Первичная установка в `%AppData%` |
| `-update`, `--update`, `-u` | `Update` | Обновление существующей установки |
| (без аргументов) | `AutoUpdate` | Определяет наличие `Barkfluff.exe` рядом (`DownloadService.IsLocalInstallation()`) и выбирает `Update` или `Install` |
| `-help`, `--help`, `-h`, `-?`, `/?` | `Help` | Справка |

Флаг `-silent`/`--silent`/`-s`/`-q`/`--quiet` — без интерактивных пауз/подтверждений.

Приложение требует прав администратора (`AdminService.IsRunningAsAdmin()`) — при отсутствии печатает предупреждение и завершается с кодом 1.

## Архитектура

```
Program.cs                          — точка входа, парсинг режима, UAC-проверка
Arguments/ArgumentParser.cs         — парсинг CLI-аргументов → ParsedArguments { Mode, Silent }
Commands/
  ├─ InstallCommand.cs              — первичная установка
  ├─ UpdateCommand.cs               — обновление существующей установки
  └─ HelpCommand.cs                 — вывод справки
Services/
  ├─ GitHubReleaseService.cs        — GetLatestStableReleaseAsync(): последний стабильный релиз (канал Master/Release) с GitHub
  ├─ DownloadService.cs             — DownloadToTempAsync/ExtractZip/CleanupTempFile/GetDefaultInstallPath/GetUpdatePath/IsLocalInstallation
  ├─ AdminService.cs                — IsRunningAsAdmin/RestartAsAdmin/GetBarkFluffExecutablePath
  ├─ ProtocolRegistrationService.cs — регистрация URI-схемы `bf://` в HKEY_CLASSES_ROOT (RegisterProtocol/UnregisterProtocol/IsProtocolRegistered)
  └─ ShortcutService.cs             — создание/удаление ярлыка в Start Menu (через IShellLink/COM)
UI/
  ├─ ConsoleUI.cs                   — форматированный вывод (заголовки, прогресс, цвета, градиенты)
  └─ LogoAssets.cs                  — ASCII-логотипы для Install/Update
```

## Поток `UpdateCommand`

1. Определяет путь установки (`IsLocalInstallation()` → путь рядом с текущим exe, иначе `GetDefaultInstallPath()`).
2. Запрашивает последний стабильный релиз через `GitHubReleaseService`.
3. Отправляет `bf://closetoupdate` (`Process.Start` с `UseShellExecute`) — просит запущенный WPF-клиент закрыться, ждёт 3 секунды.
4. Скачивает ZIP во временную папку, распаковывает поверх целевой директории (SharpZipLib).
5. Удаляет временный файл.
6. Обновляет системную интеграцию: регистрирует протокол `bf://` и создаёт ярлык в Start Menu (пропускается с предупреждением, если не хватает прав).
7. Запускает обновлённый `Barkfluff.exe` с аргументом `--successfulupdate` (независимо от `-silent` — флаг `silent` управляет только выводом в консоль, а не запуском).

`InstallCommand` — тот же поток без шага 3 (закрытия существующего приложения) и с установкой в `GetDefaultInstallPath()` (`%AppData%`) вместо обнаруженного пути.

## Регистрация протокола

`ProtocolRegistrationService` создаёт ветку `HKEY_CLASSES_ROOT\bf` с `shell\open\command`, указывающим на установленный `Barkfluff.exe`. Используется WPF-клиентом и этой же утилитой для `bf://closetoupdate` и глубоких ссылок (deep links).

## Что не задокументировано / требует уточнения у автора

- Нет README/SECURITY_AUDIT в самом проекте.
- Не описан сценарий, при котором эту CLI-утилиту запускает конечный пользователь напрямую (инсталлятор? CI/CD? отдельная раздача с сайта?) — сам код только показывает *как* она работает, не *когда/кем* вызывается.
- `AWSSDK.Core` в зависимостях не используется в текущем коде — назначение неясно (см. выше).
