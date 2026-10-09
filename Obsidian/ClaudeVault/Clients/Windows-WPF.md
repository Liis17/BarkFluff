# Legacy WPF-клиент BarkFluff

Старый Windows-клиент на WPF/.NET 10, x64, с UI-логикой в code-behind и Reactive-обёртками для XAML. Он использует WebApi.Core напрямую и остаётся отдельным от MVVM-клиента V2 и текущего WinUI-клиента.

- Исходники: Windows/BarkFluff.Client.WPF/
- gRPC-транспорт: [[Clients/Windows-WebApiCore]]
- Карта основных файлов: [[Clients/Windows-WPF-ProjectMap]]
- Отдельный legacy-апдейтер: [[Clients/Windows-UpdaterCLI]]

## Состояние и локальные данные

App.xaml.cs держит общие объекты приложения и координирует запуск MessengerPage, real-time сервисов, кэшей и уведомлений. MessengerPage собирает контроллеры чатов, сообщений и вложений.

GlobalParam из WebApi.Core хранится в datas/GlobalParam.json. App.SaveGlobalParam и связанные методы выполняют запись после проверок приложения. Файл зашифрован AES-256-GCM; ключ выводится из PIN через PBKDF2-SHA512 с 600 000 итерациями. Чтение поддерживает старый BFV2 с PBKDF2-SHA256/100 000, запись создаёт BFV3.

История сообщений и метаданные файлов хранятся отдельно в LiteDB: datas/cache.db и datas/file_cache.db; файлы кэша размещаются в datas/cache/{type}/. Это локальные кэши, отдельные от зашифрованного файла состояния.

## Основные потоки и ограничения

- Вход включает выбор сервера, пароль/OTP или QR FastAuth; при возврате используется PIN-экран.
- RealtimeUpdateService публикует новые сообщения и квитанции чтения; OnlineStatusService обслуживает статусы присутствия. Оба — долгоживущие сервисы приложения.
- Один экземпляр приложения координируется mutex и named pipe. URI-схемы bf:// и bfdev:// используются для deep links и закрытия клиента перед legacy-обновлением.
- Встроенный WPF updater проверяет канал и запускает PowerShell-скрипт из временного файла; отдельная CLI-утилита описана в [[Clients/Windows-UpdaterCLI]].
- Настройки клиента используют как локальные поля GlobalParam, так и API WebApi.Core; локализация и темы находятся в ресурсах WPF.

## Источники

Основные границы реализации: App.xaml.cs, Pages/MessengerPage.xaml.cs, Pages/Messenger/Controllers/, Services/App/RealtimeUpdateService.cs, Services/App/OnlineStatusService.cs, Services/App/Caching/, Services/App/Update/ и UserControls/SettingsPages/.
