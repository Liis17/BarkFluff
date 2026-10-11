# BarkFluff.Client.WinUI

Текущий Windows-клиент BarkFluff: WinUI 3, Windows App SDK 2.3.1, .NET 10 и MSIX. WPF V2 — предшествующая реализация и источник совместимости данных, см. [[Clients/Windows-WPF-V2]].

- Исходники: Windows/BarkFluff.Client.WinUI/ и Windows/BarkFluff.Client.Core/
- Общий Windows gRPC-клиент: [[Clients/Windows-WebApiCore]]

## Границы сборок

BarkFluff.Client.WinUI содержит App, MainWindow, Pages и WinUI adapters. BarkFluff.Client.Core содержит модели, ViewModels, сервисы, SQLite/DPAPI storage, Markdown и интерфейсы UI-зависимостей; он ссылается на WebApi.Core. Core отделён от WinUI, потому что обращение к типам WinUI запускает Windows App SDK module initializer и требует пакетной идентичности; отдельная сборка позволяет тестировать клиентскую логику вне MSIX.

В приложении используются Frame/Page и ViewLocator; локализованные строки загружаются из RU/EN ResourceDictionary при старте. Trim и AOT отключены в проекте: XAML-типы, gRPC/Protobuf и сериализация StoredSession используют runtime metadata/reflection.

## Запуск, нода и сохранение данных

App.OnLaunched импортирует legacy DB при необходимости, инициализирует SQLite, тему, язык и сервисы, затем восстанавливает выбранную ноду и сессию. NodeConnectionService подключается к Beacon через GetServerInfo и сохраняет endpoint-конфигурацию; AuthenticationService поддерживает пароль/OTP, FastAuth QR, регистрацию и восстановление пароля. ClientMetadata задаёт одинаковые имя и версию клиента в gRPC metadata и показывает читаемую версию Windows.

AppDataPaths использует ApplicationData.Current.LocalFolder в MSIX и %LOCALAPPDATA%/BarkFluff без package identity; база находится в data/barkfluff.db. LegacyDatabaseImporter копирует старую базу вместе с -wal/-shm только когда целевой базы ещё нет.

SqliteApplicationDataStore хранит настройки, выбранную ноду/endpoints и защищённые blob-значения. DpapiSecureSessionStore сериализует access/refresh token пару и защищает её DPAPI CurrentUser; blob записывается в SQLite и загружается при следующем запуске. DpapiPrivateChatKeyStore сохраняет ключи тем же способом с областью node/user/chat и держит рабочий memory cache. DPAPI entropy сохранена от WPF V2, чтобы импортированные session/key blobs оставались читаемы. При неуспешном восстановлении сессии blob удаляется.

## Клиентские API

- INodeConnectionService и IAuthenticationService обслуживают подключение к ноде и вход; IAccountSettingsService — чтение/изменение профиля и logout.
- IMessengerService предоставляет чаты, историю, отправку текста с отдельными replyToMessageId и forwardedMessageIds, правку/удаление, закрепы, приватные сообщения и список вложений чата.
- IRealtimeMessengerService публикует новые сообщения, обычные и приватные read receipts и состояние соединения. Подписки используют StreamRetryLoop с отменой и backoff; после TokenRefreshed они пересоздаются. OnlinePresenceService отдельно управляет подпиской и keepalive статусов.
- IUserPreferencesService, ISecuritySettingsService и IDeviceSettingsService оборачивают серверные настройки приватности/папок, безопасность, активные устройства и уведомления.
- Приватные чаты используют passphrase-криптографию WebApi.Core. Ключи хранятся приложением в защищённом виде; текущий транспорт приватных сообщений принимает только текст.
- MarkdownParser/MarkdownSanitizer реализуют общий с Android и Web поднабор; ссылки разрешены только http/https/mailto, изображения — только http/https, размеры ограничены 1–2048 px.

Встроенный updater сопоставляет identities 7895OrbitinSpace.Barkfluff, 7895OrbitinSpace.Barkfluff.Dev и 7895OrbitinSpace.Barkfluff.Nightly с каналами release, dev и nightly. Он проверяет соответствующий endpoint https://storage.barkfluff.com/get/barkfluffwinui/{channel}/version, затем распаковывает архив канала во временную папку, выбирает MSIX того же identity и более новой версии и открывает его через системный App Installer.

## Профиль

ProfileViewModel показывает собственный или чужой профиль как overlay поверх MessengerPage. Вложения загружаются через IMessengerService.ListChatAttachmentsAsync отдельными вкладками Image, Video, Document и Voice с ленивой загрузкой и постраничной выдачей. Редактирование чужого профиля отсутствует; почта отображается только в собственном профиле.

## Ограничения

- Настройки и сессия хранятся локально, но кэш истории сообщений в SQLite не реализован.
- Клиентский realtime слушает новые сообщения и read receipts; правки, удаления и закрепления из других устройств не обновляются потоком.
- Папки чатов поддерживают CRUD и порядок, но выбор чатов для папки пока не реализован.
- Приватные чаты не поддерживают защищённые вложения.
- Тестовый проект: Tests/BarkFluff.Client.WinUI.Tests/. Ключевые реализации находятся в Windows/BarkFluff.Client.Core/Services/, ViewModels/ и Infrastructure/Storage/; представления — в Windows/BarkFluff.Client.WinUI/Views/.
