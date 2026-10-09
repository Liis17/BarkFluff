# BarkFluff.ClientV2.WPF — карта исходников

Обзор границ, хранилища и ограничений: [[Clients/Windows-WPF-V2]].

Исходники: Windows/BarkFluff.ClientV2.WPF/

| Путь | Ответственность |
|---|---|
| App.xaml(.cs), MainWindow.xaml(.cs) | DI-host, инициализация хранилища и запуск окна |
| Services/NodeConnectionService.cs | Список нод, Beacon GetServerInfo и восстановление endpoints |
| Services/AuthenticationService.cs | Вход/OTP, FastAuth, регистрация, recovery и восстановление DPAPI-сессии |
| Services/MessengerService.cs, IMessengerService.cs | Граница между ViewModel и WebApi.Core для сообщений, истории и закреплений |
| Services/RealtimeMessengerService.cs | Read receipts обычных и приватных чатов |
| Infrastructure/Storage/SqliteApplicationDataStore.cs | SQLite app settings, selected node, secure_session и private_chat_keys |
| Infrastructure/Storage/DpapiSecureSessionStore.cs | CurrentUser DPAPI для access/refresh token blob |
| Infrastructure/Storage/DpapiPrivateChatKeyStore.cs | Защищённые ключи приватных чатов |
| ViewModels/ | Welcome, node selection, auth, messenger и settings |
| Views/ и Views/Controls/ | WPF UI-страницы и основные компоненты чатов |
| Tests/BarkFluff.ClientV2.WPF.Tests/ | Тесты parser, mapper, SQLite и ViewModel |

Общий gRPC-контракт находится в [[Clients/Windows-WebApiCore]].
