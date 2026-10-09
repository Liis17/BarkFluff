# BarkFluff.WebApi.Core — карта исходников

Обзор границ и поведения фасада: [[Clients/Windows-WebApiCore]].

Исходники: Windows/BarkFluff.WebApi.Core/

| Путь | Ответственность |
|---|---|
| WebApi.cs | Публичный facade: создание клиентов, токены и делегирование API |
| WebApiBase.cs | Общий доступ менеджеров к каналам; чтение и освобождение server streams |
| Managers/WebApiClientManager.cs | gRPC channels, client stubs и interceptors |
| Managers/WebApiTokenManager.cs | SafeCallAsync, refresh rotation, автообновление и события токенов |
| Managers/WebApiServerManager.cs | Navigator и Beacon: список нод и GetServerInfo |
| Managers/WebApiUserManager.cs | Профили, устройства, privacy settings, personalization и prekeys |
| Managers/WebApiAuthManager.cs, WebApiRegistrationManager.cs, WebApiPasswordManager.cs | Login/OTP, регистрация, пароль и recovery |
| Managers/WebApiFastAuthManager.cs | Анонимный QR session и авторизованные действия подтверждения |
| Managers/WebApiMessageManager.cs | Чаты, сообщения, история, вложения, группы и закрепы |
| Managers/WebApiSearchManager.cs, WebApiChatFolderManager.cs | Поиск пользователей и папки чатов |
| Managers/WebApiFileManager.cs | Files API, HTTP transfer, hash-дедупликация и stickers |
| Managers/WebApiUpdateManager.cs, WebApiOnlinerManager.cs | Updates streams и статусы/typing |
| Managers/WebApiPrivateChatManager.cs, Crypto/PrivateChatCrypto.cs | E2E приватные чаты и passphrase encryption |
| Managers/WebApiSecretChatManager.cs | Перенос opaque secret-chat envelopes |
| Managers/WebApiCallsManager.cs | Signaling API звонков |
| MessengerData/GlobalParam.cs | Токены, endpoints, профиль и legacy PIN-encrypted file format |
| MessengerData/NonSavedData/ | DTO для пользователей, чатов, сообщений и вложений |
| ErrorReturner.cs, ImageProcessor.cs | Ошибки API и обработка изображений перед upload |

WebApi facade перечисляет публичные операции; manager-файлы содержат protobuf mapping и контракт каждого RPC. Библиотека не владеет пользовательской SQLite или защищённой сессией для V2/WinUI: это зона клиентских приложений.
