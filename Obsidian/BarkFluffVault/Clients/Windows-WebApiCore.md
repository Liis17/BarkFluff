# BarkFluff.WebApi.Core

Общий .NET 10 Windows gRPC-клиент BarkFluff. Используется напрямую legacy WPF и WPF V2, а текущий WinUI-клиент обращается к нему через BarkFluff.Client.Core.

- Исходники: Windows/BarkFluff.WebApi.Core/
- Карта менеджеров и ключевых API: [[Clients/Windows-WebApiCore-ProjectMap]]

## Фасад и публичный контракт

WebApi реализует IDisposable и владеет gRPC-каналами и специализированными менеджерами. CreateOnlyBeaconAC и CreateNavigatorAC используются до авторизации; GetServerInfo получает endpoints, CreateAC инициализирует авторизованные клиенты. CallsAvailable показывает, объявил ли Beacon сервис звонков; при его отсутствии методы звонков возвращают ошибку.

Фасад группирует API по нодам, identity/auth/OTP/FastAuth, профилям и устройствам, чатам/сообщениям, папкам, файлам, онлайн-статусам, звонкам, приватным чатам и секретным чатам. Message manager включает историю, отправку, правку/удаление, read receipts и закрепления; File manager отвечает за HTTP upload/download поверх gRPC-контрактов Files.

WinUI identifier-first flow использует additive `ResolveSignIn`, `BeginSignIn`, `GetAuthChallenge`, `CompleteAuthChallenge`, `ResendAuthChallenge` и `CancelAuthChallenge` методы, соответствующие существующим RPC в `identity_api.proto`. Каждый unary-вызов принимает `CancellationToken` и передаёт его сгенерированному gRPC-клиенту. `CreatePreLoginAuthenticationClient` создаёт только Identity channel на отдельном `WebApi`; он сохраняет X-Device/X-Device-Id/X-OS/X-App/X-IP, JWT и exception interceptors, но не вызывает `TokenManager` и не меняет основной facade. `GetUserDataForAuthentication` загружает профиль по тому же attempt-scoped клиенту с отменяемым прямым `UsersAC.GetUserAsync`.

`GenerateFastAuthToken` принимает необязательный `CancellationToken`. WinUI создаёт и подписывает FastAuth-канал на attempt-scoped `WebApi`; полученные access/refresh tokens проходят тот же profile/session commit, что и challenge login. `Authorizations` и legacy WPF-вызовы остаются отдельным существующим контрактом.

WinUI при восстановлении сохранённой сессии вызывает `RefreshTokenForSessionRestoreAsync` и `GetUserDataForSessionRestoreAsync` до запуска стримов. Вызовы принимают отмену, ограничены пятисекундным дедлайном каждый и сохраняют исключения с типом отказа Identity или транспортным gRPC-статусом. Legacy WPF использует прежний контракт обновления токена.

TokenInvalidated сообщает о непригодном refresh token. TokenRefreshed поднимается после проактивного обновления: клиенты должны пересоздать streaming RPC. TokenManager сериализует параллельные обновления, поскольку refresh tokens ротируются. SafeCallAsync повторяет вызов после обновления access token. WebApiBase.ReadStream освобождает gRPC-вызов, но завершает перечисление при RPC/сетевых ошибках; клиент, которому нужно восстановление связи, должен повторно создать поток.

SubscribeToChatFoldersChanged передаёт пустые сигналы Updates и сохраняет gRPC-статус потока, чтобы WinUI мог остановить повторы при UNIMPLEMENTED. Перегрузки Add/Remove для папок принимают CancellationToken. Прежние сигнатуры сохранены.

Для индикатора набора WinUI использует `SubscribeToTypingWithStatus` с сохранением gRPC-статуса потока и `SetTypingStatusOnceAsync` с двухсекундным дедлайном на отправку. Оба вызова поддерживают отмену и используют текущий клиент Onliner.

`ListChatsPageAsync` используется для полного списка чатов WinUI, обновления счётчика непрочитанного по первой странице и поиска отсутствующего в списке чата при открытии результата поиска или системного уведомления. Метод передаёт offset и размер страницы в `ListChats`, возвращает общее число чатов и поддерживает отмену. WinUI также передаёт токен сессии в `GetPersonChatId` при открытии результата поиска людей и при чтении и записи флага уведомлений устройства.

SearchMessagesAsync передаёт запрос и отмену в Messages API, возвращает результаты с курсором и сохраняет gRPC-статус ошибки. Перегрузка SearchUser с отменой используется поиском людей в WinUI.

`UserData.AccountStatus` заполняется из Users API и используется WinUI для локальных подписей ограниченных профилей и read-only личных чатов. Менеджеры токенов и FastAuth переводят `AccountBlockedException`/`AccountPendingDeletionException` в локализуемые ключи ошибки; отправка regular/private/secret сообщений, создание личных чатов/secret invite и вызов переводят `UserUnavailableException` в `Messenger_UserUnavailable`. WebApi не получает причину блокировки из legacy RPC.

Для WinUI фасад предоставляет `GetChatDraft`, `UpsertChatDraft` и `DeleteChatDraft` с отменой. Клиент привязан к экземпляру параметров подключения и поддерживает назначение ID пользователя после создания канала при автовходе. Адаптер WinUI сверяет ноду и ID пользователя с сессией черновиков перед каждым вызовом фасада. Пустой ответ чтения означает отсутствие черновика, отсутствие самого ответа означает сбой. При удалении ожидаемая ревизия передаётся в Messages API.

## Модели и безопасность

GlobalParam содержит endpoints, профиль, токены и параметры интерфейса. Для legacy WPF методы Save/Load шифруют datas/GlobalParam.json AES-256-GCM; BFV3 выводит ключ PBKDF2-SHA512/600 000, чтение BFV2 поддерживает PBKDF2-SHA256/100 000. WPF V2 и WinUI сохраняют сессию собственными DPAPI CurrentUser хранилищами в SQLite; не смешивать эти форматы.

ForwardingLetter отражает текущий протокол отправки: ReplyToMessageId и список ForwardedMessageIds разделяют ответ и пересылку. Одиночный ForwardedMessageId оставлен для совместимости с WPF V2 и не должен смешиваться с новыми полями.

PrivateChatCrypto выводит 32-байтовый ключ из passphrase через Argon2id v1.3 (t=3, m=64 MiB, p=4), затем шифрует AES-256-GCM; AAD привязан к chatId. Ключ держит приложение, WebApi.Core не сохраняет его между вызовами. SecretChatManager передаёт byte[] envelopes; X3DH/Double Ratchet и локальное хранилище сессий не входят в библиотеку. Calls manager предоставляет signaling API, но не LiveKit media SDK.

WebApiFileManager очищает имя файла перед загрузкой и не записывает в Debug реальные локальные пути, имена файлов, S3 URL или тела ошибок. Эти значения могут содержать пользовательские данные или секреты.

Перегрузка загрузки для WinUI проверяет фактический размер multipart-запроса, передаёт имя с поддержкой Unicode и сообщает прогресс по отправленным байтам. HTTP-клиент останавливает передачу после 60 секунд без прогресса и ждёт ответ до 630 секунд после отправки формы. У загрузки есть отмена. Для фото JPEG-конвертация применяет EXIF-ориентацию до уменьшения, в том числе на пути ImageSharp. GIF и документы идут исходными байтами. ID берётся из JSON ответа POST или статуса completed после ограниченного ожидания по Retry-After. Дедупликация выполняется сервером с учётом типа, совпадающий файл того же типа может сохранить имя первой загрузки.

Обычная очередь отправки WinUI использует перегрузки `SendMessage` и `UploadFileAsync` с UUID для идемпотентности. Фасад привязывает вызовы к параметрам авторизованного подключения. Загрузка проверяет состояние зарезервированного слота: для completed возвращает серверный ID, для processing ждёт ограниченное время, для pending передаёт файл. Маппинг `MessageModel.ClientOperationId` общий для ответа, истории и событий. Старые сигнатуры сохраняются для WPF.

Основные зависимости проекта: gRPC/Protobuf, Shared.Auth, Shared.Exceptions, Argon2id и ImageSharp.

При пагинации истории распознанный ответ `MessageNotFound` для ненулевого якоря получает код `ErrorReturner.HistoryAnchorNotFound`. Клиент может попробовать соседний якорь. Пустой ответ обычной истории сохраняет код 1. Если приватный RPC ноды скрывает причину ошибки, фасад возвращает общую ошибку.

WinUI использует отменяемые операции создания личных и групповых аудио- и видеозвонков, принятия, присоединения, отклонения и завершения. Перегрузки для общего качества голоса, активных звонков и истории также принимают отмену и сохраняют исходный статус gRPC. История передаёт серверу фильтр и курсор по времени начала звонка. Поток `ReadCallEventsAsync` использует тот же авторизованный канал и сохраняет `Unimplemented` для отключения подписки на старой ноде. Существующий фасад WPF возвращает ошибки и события в прежнем формате.

## Ограничения

- При серверной дедупликации старый путь загрузки WPF возвращает ID, зарезервированный при выдаче ссылки, хотя POST возвращает ID существующего файла.
