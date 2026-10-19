# Barkfluff.Updater.CLI

Отдельная legacy-утилита установки и обновления WPF-клиента. Это .NET 8 console exe без ссылки на основной WPF-проект. Она использует собственный GitHub release flow; обновление из WPF и WinUI выполняется отдельно. Обзор клиентов: [[Clients/Windows-WPF]] и [[Clients/Windows-WinUI]].

Исходники: Windows/Barkfluff.Updater.CLI/

## CLI-контракт

- Режимы: -install/--install/-i, -update/--update/-u и -help/--help/-h/-?; без режима выбирается AutoUpdate по наличию Barkfluff.exe рядом с утилитой.
- -silent/--silent/-s/-q/--quiet отключает интерактивные паузы.
- Утилита требует запуска с правами администратора; иначе завершается с кодом 1.
- Install использует %APPDATA%/BarkFluff. Update работает в каталоге найденной установки либо в том же пути по умолчанию.

## Поток обновления

GitHubReleaseService выбирает последний канал Master/Release из репозитория Liis17/BarkFluff.Releases. UpdateCommand отправляет bf://closetoupdate запущенному клиенту, ждёт три секунды, скачивает и распаковывает ZIP, обновляет регистрацию bf:// и ярлык Start Menu, затем запускает Barkfluff.exe с --successfulupdate. Установка выполняет тот же поток без запроса на закрытие существующего клиента.

Основные точки навигации: Arguments/ArgumentParser.cs, Commands/InstallCommand.cs, Commands/UpdateCommand.cs, Services/GitHubReleaseService.cs, Services/DownloadService.cs, Services/ProtocolRegistrationService.cs и Services/ShortcutService.cs. ZIP распаковывает SharpZipLib; AWSSDK.Core указан в проекте, но текущие исходники его не используют.
