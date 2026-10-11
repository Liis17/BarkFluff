# BarkFluff.WebApi.Core

Общий .NET 10 Windows gRPC-клиент BarkFluff. Используется напрямую legacy WPF и WPF V2, а текущий WinUI-клиент обращается к нему через BarkFluff.Client.Core.

- Исходники: Windows/BarkFluff.WebApi.Core/
- Карта менеджеров и ключевых API: [[Clients/Windows-WebApiCore-ProjectMap]]

## Фасад и публичный контракт

WebApi реализует IDisposable и владеет gRPC-каналами и специализированными менеджерами. CreateOnlyBeaconAC и CreateNavigatorAC используются до авторизации; GetServerInfo получает endpoints, CreateAC инициализирует авторизованные клиенты. CallsAvailable показывает, объявил ли Beacon сервис звонков; при его отсутствии методы звонков возвращают ошибку.

Фасад группирует API по нодам, identity/auth/OTP/FastAuth, профилям и устройствам, чатам/сообщениям, папкам, файлам, онлайн-статусам, звонкам, приватным чатам и секретным чатам. Message manager включает историю, отправку, правку/удаление, read receipts и закрепления; File manager отвечает за HTTP upload/download поверх gRPC-контрактов Files.

TokenInvalidated сообщает о непригодном refresh token. TokenRefreshed поднимается после проактивного обновления: клиенты должны пересоздать streaming RPC. TokenManager сериализует параллельные обновления, поскольку refresh tokens ротируются. SafeCallAsync повторяет вызов после обновления access token. WebApiBase.ReadStream освобождает gRPC-вызов, но завершает перечисление при RPC/сетевых ошибках; клиент, которому нужно восстановление связи, должен повторно создать поток.

## Модели и безопасность

GlobalParam содержит endpoints, профиль, токены и параметры интерфейса. Для legacy WPF методы Save/Load шифруют datas/GlobalParam.json AES-256-GCM; BFV3 выводит ключ PBKDF2-SHA512/600 000, чтение BFV2 поддерживает PBKDF2-SHA256/100 000. WPF V2 и WinUI сохраняют сессию собственными DPAPI CurrentUser хранилищами в SQLite; не смешивать эти форматы.

ForwardingLetter отражает текущий протокол отправки: ReplyToMessageId и список ForwardedMessageIds разделяют ответ и пересылку. Одиночный ForwardedMessageId оставлен для совместимости с WPF V2 и не должен смешиваться с новыми полями.

PrivateChatCrypto выводит 32-байтовый ключ из passphrase через Argon2id v1.3 (t=3, m=64 MiB, p=4), затем шифрует AES-256-GCM; AAD привязан к chatId. Ключ держит приложение, WebApi.Core не сохраняет его между вызовами. SecretChatManager передаёт byte[] envelopes; X3DH/Double Ratchet и локальное хранилище сессий не входят в библиотеку. Calls manager предоставляет signaling API, но не LiveKit media SDK.

WebApiFileManager очищает имя файла перед загрузкой и не записывает в Debug реальные локальные пути, имена файлов, S3 URL или тела ошибок. Эти значения могут содержать пользовательские данные или секреты.

Основные зависимости проекта: gRPC/Protobuf, Shared.Auth, Shared.Exceptions, Argon2id и ImageSharp.
