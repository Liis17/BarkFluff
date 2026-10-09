# BarkFluff.ClientV2.WPF

Предшествующий WinUI клиент Windows на WPF/.NET 10. Он реализует MVVM, DI и WPF UI независимо от legacy [[Clients/Windows-WPF]]. Текущий Windows UI — [[Clients/Windows-WinUI]].

- Исходники: Windows/BarkFluff.ClientV2.WPF/
- Транспорт: прямой project reference на [[Clients/Windows-WebApiCore]]; проект не ссылается на BarkFluff.Client.Core.
- Карта ключевых файлов: [[Clients/Windows-WPF-V2-ProjectMap]]
- Архитектурные указания и тестовый проект: Windows/BarkFluff.ClientV2.WPF/docs/Architecture.md и Tests/BarkFluff.ClientV2.WPF.Tests/.

## Архитектура и запуск

Views — XAML, ViewModels — CommunityToolkit.Mvvm, сервисы и WPF-контролы зарегистрированы через Microsoft.Extensions.Hosting. NodeAddressParser проверяет адрес Beacon, NodeConnectionService запрашивает GetServerInfo и сохраняет endpoints выбранной ноды. OnboardingNavigationService выбирает стартовый маршрут.

При запуске приложение инициализирует SQLite, тему и язык, восстанавливает выбранную ноду, затем пробует восстановить сессию через refresh token. Авторизация поддерживает логин/почту с OTP, FastAuth QR, регистрацию и сброс пароля.

## SQLite и защита сессии

SqliteApplicationDataStore хранит data/barkfluff.db рядом с приложением: настройки, выбранную ноду и JSON-конфигурацию endpoints, а также таблицы secure_session и private_chat_keys. Access/refresh tokens сериализуются как StoredSession, защищаются DPAPI с DataProtectionScope.CurrentUser и сохраняются BLOB в SQLite. При восстановлении refresh token обновляется; повреждённый или недействительный blob удаляется.

Ключ приватного чата также сохраняется DPAPI-блобом в SQLite по области node/user/chat и дополнительно кэшируется в памяти. DPAPI CurrentUser связывает восстановление с учётной записью Windows, под которой blob был создан.

## Клиентские контракты и ограничения

- INodeConnectionService управляет списком публичных нод и соединением с Beacon; IAuthenticationService обслуживает пароль/OTP, FastAuth, регистрацию, восстановление пароля и сохранённую сессию.
- IMessengerService предоставляет чаты, историю по offset, текстовую отправку, правку/удаление и закрепление сообщений. Его отправка использует устаревшее одиночное поле ForwardingLetter.ForwardedMessageId для ответа или пересылки; новый контракт с отдельным reply и списком пересылаемых сообщений используют WinUI и Web-клиент.
- RealtimeMessengerService слушает только обычные и приватные квитанции чтения, пересоздавая эти подписки после TokenRefreshed. Клиент не подписан на новые сообщения, правки, удаления и закрепления, поэтому такие изменения с другого устройства не обновляются real-time.
- Приватные чаты используют совместимую с Android passphrase-криптографию из WebApi.Core и отдельное хранилище ключей; транспорт клиента принимает только текст, защищённые вложения не реализованы.
- Локального кэша истории сообщений в V2 нет. SQLite используется для настроек и сессии, а не как база чатов.
- Настройки закрытия окна хранятся в SQLite; режим MinimizeToTray прячет окно, а пункт «Выход» завершает процесс. Асинхронный host останавливается вне UI-потока с ограничением ожидания, чтобы зависший gRPC-стрим не блокировал закрытие.

## Сборка

Команды проекта:

    dotnet build Windows/BarkFluff.ClientV2.WPF/BarkFluff.ClientV2.WPF.csproj
    dotnet test Tests/BarkFluff.ClientV2.WPF.Tests/BarkFluff.ClientV2.WPF.Tests.csproj
