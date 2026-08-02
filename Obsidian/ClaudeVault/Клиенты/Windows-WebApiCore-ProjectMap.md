# PROJECT_MAP — BarkFluff.WebApi.Core

> **Описание:** Детальная карта внутреннего строения WebApi.Core — все файлы, менеджеры, методы и зависимости. Предназначена для быстрой навигации по проекту без чтения исходников.
> Исходный проект: `Windows/BarkFluff.WebApi.Core/`
> См. также: [[Windows-WebApiCore]]

gRPC-клиентская библиотека для WPF-клиента. Единая точка входа для всех обращений к бэкенду.

---

## Точка входа

### `WebApi.cs`

Публичный фасад. Создаёт и держит gRPC-каналы и менеджеры, включая анонимный `FastAuthAC`, авторизованный `FastAuthUserAC` и опциональный `CallsAC`. Реализует `IDisposable` — при `Dispose()` закрываются все gRPC-каналы.

**Свойства:**
- `bool ACisnull` — `true`, если основные клиенты не инициализированы
- `bool BeaconIsnull` — `true`, если Beacon-клиент не инициализирован
- `event EventHandler? TokenInvalidated` — срабатывает когда refresh-токен умер (перенаправить на авторизацию)
- `event EventHandler? TokenRefreshed` — срабатывает после каждого **проактивного** обновления токена; подписчики **обязаны** пересоздать все стримы

**Методы авто-обновления токена:**
- `StartAutoRefresh(GlobalParam)` — запустить фоновый обновитель (вызвать при логине)
- `StopAutoRefresh()` — остановить (автоматически вызывается в `Dispose`)

---

## Инфраструктурные классы

### `WebApiBase.cs`

Абстрактный базовый класс всех менеджеров. Предоставляет `protected`-доступ к gRPC-клиентам и каналам (включая `FastAuthAC`, `FastAuthUserAC`, `CallsAC`) через ссылку на родительский `WebApi`. `ReadStream<T>` централизованно читает server-streaming RPC, обрабатывает отмену/ошибки и освобождает вызов.

### `ErrorReturner.cs`

Стандартный результат любого API-вызова.

```
ErrorReturner
├── bool IsSuccess
├── string? ErrorMessage
└── int ErrorCode    — 0: ок; 1: список сообщений пустой
```

### `ImageProcessor.cs`

Обработка изображений перед загрузкой. JPEG-качество 90%, максимум 2500×2500 px, максимум 50 МБ.

**Методы:**

| Метод | Что делает |
|-------|-----------|
| `ProcessImageForUploadAsync(path)` | Конвертация + ресайз → temp-файл |
| `ShouldConvertToJpeg(path)` | Нужна ли конвертация (GIF — нет) |
| `ConvertToJpegAsync(data, ext)` | Конвертация через ImageSharp или System.Drawing |

**Зависимости:** `SixLabors.ImageSharp` 3.1.12, `System.Drawing.Common` 9.0.0

---

## Менеджеры

### `WebApiClientManager.cs` — Создание клиентов

**Зависимости:** `BarkFluff.Shared.Auth` (interceptors)

| Метод | Что делает |
|-------|-----------|
| `CreateOnlyBeaconAC(gParam)` | Только Beacon-канал (до авторизации) |
| `CreateNavigatorAC(url)` | Navigator-канал (публичный реестр серверов) |
| `CreateAC(gParam, deviceName, os, appName, appVersion, ip)` | Пересоздаёт основные авторизованные каналы, `FastAuthUserAC` и — при непустом `SocketCalls` — `CallsAC`; `Navigator` и анонимный FastAuth создаются отдельными методами |

**Interceptors (порядок):** `XDeviceClientInterceptor`, `XDeviceIdInterceptor`, `JwtClientInterceptor`, `XOsClientInterceptor`, `XAppClientInterceptor`, `ExceptionClientInterceptor`, `XIpInterceptor`

---

### `WebApiTokenManager.cs` — Токены

| Метод / Событие | Сигнатура | Описание |
|-----------------|-----------|----------|
| `TokenUpdate` | `(GlobalParam) → Task<(ErrorReturner, string)>` | Обновить access token через `IdentityApi.CreateToken` |
| `SafeCallAsync<T>` | `(Func<Task<T>>, GlobalParam) → Task<T>` | Выполнить gRPC-вызов с авторетраем при 401 |
| `EnsureTokenValidAsync` | `(GlobalParam, bufferMinutes=5) → Task<ErrorReturner>` | Проверить срок токена перед streaming |
| `ForceRefreshTokenAsync` | `(GlobalParam) → Task<ErrorReturner>` | Принудительный рефреш + переинициализация клиентов |
| `StartAutoRefresh` | `(GlobalParam) → void` | Запустить `PeriodicTimer` (тик 30 сек); обновляет токен когда `timeLeft ≤ 1 min`; после успеха → переинициализирует клиентов + вызывает `TokenRefreshed` |
| `StopAutoRefresh` | `() → void` | Отменить и освободить `CancellationTokenSource` таймера |
| `event TokenInvalidated` | `EventHandler?` | Если рефреш-токен умер во время авто-обновления |
| `event TokenRefreshed` | `EventHandler?` | После каждого успешного проактивного обновления; клиент должен пересоздать стримы |

**Сериализация refresh (важно!):**
`SemaphoreSlim _refreshLock` + общий `Task<bool>? _ongoingRefresh` в `RefreshOnceAsync`. Все пути обновления (`ExecuteWithTokenRefresh`, `EnsureTokenValidAsync`, `ForceRefreshTokenAsync`, авто-таймер) идут через единый `RefreshOnceAsync` → одновременно работает максимум один `TokenUpdate`. Параллельные вызывающие дожидаются результата. Если access-токен уже сменился под lock'ом — refresh пропускается (значит другой поток уже обновил). Это закрывает race, при котором одноразовый refresh-токен с rotation инвалидировал самого себя при concurrent 401 → cascade logout.

**Логика авто-обновления:**
```
PeriodicTimer(30s)
  → проверить ExpirationDate AccessToken
  → если timeLeft ≤ 1 min:
      RefreshOnceAsync() → если false → TokenInvalidated
      (внутри: TokenUpdate + AddInterceptor под lock'ом)
      TokenRefreshed.Invoke()
```

---

### `WebApiServerManager.cs` — Информация о серверах

**gRPC:** `BeaconApi`, `NavigatorApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `GetServerInfo` | `(GlobalParam)` | `(ErrorReturner, GetServerInfoResponse?)` |
| `GetServerList` | `(GlobalParam)` | `(ErrorReturner, List<ServerDataElement>)` |

**`ServerDataElement`** — `Title`, `Description`, `Ip` (`"host:port"`), `UserCount`, `PublicName`, `Location`, `HexColor`

---

### `WebApiUserManager.cs` — Пользователи

**gRPC:** `UsersApi`, `IdentityApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `Authorizations` | `(email, username, password, otpCode, GlobalParam)` | `(ErrorReturner, Token? refresh, Token? access, bool needOtp)` |
| `GetUserData` | `(GlobalParam, userId=0)` | `(ErrorReturner, UserData?)` |
| `GetUserAvatar` | `(GlobalParam, userId=0)` | `(ErrorReturner, string? url)` |
| `ChangeBio` | `(bio, GlobalParam)` | `Task<ErrorReturner>` |
| `ChangeUsername` | `(username, GlobalParam)` | `Task<ErrorReturner>` |
| **`ChangeName`** | `(firstName, lastName, GlobalParam)` | `Task<ErrorReturner>` |
| `CheckEmail` | `(email, GlobalParam)` | `(ErrorReturner, bool exists)` |
| `CheckUsername` | `(username, GlobalParam)` | `(ErrorReturner, bool exists)` |
| `GetDevicesList` | `(GlobalParam)` | `(ErrorReturner, List<Session>?)` — через IdentityApi |
| **`GetDevices`** | `(GlobalParam)` | `(ErrorReturner, List<Device>?)` — через UsersApi |
| `GetCurrentDevice` | `(GlobalParam)` | `(ErrorReturner, Device?)` |
| `RenameDevice` | `(deviceId, customName, GlobalParam)` | `Task<ErrorReturner>` |
| `RemoveActiveSession` | `(deviceId, GlobalParam)` | `Task<ErrorReturner>` |
| `GetUserBadges` | `(GlobalParam, userId=0, limit=null)` | `(ErrorReturner, List<UserBadge>?)` |
| **`GetPrivacySettings`** | `(GlobalParam)` | `(ErrorReturner, PrivacySettings?)` |
| **`UpdatePrivacySettings`** | `(PrivacySettings, GlobalParam)` | `Task<ErrorReturner>` |
| **`SetNotificationsEnabled`** | `(enabled, GlobalParam)` | `Task<ErrorReturner>` |
| **`GetPersonalization`** | `(GlobalParam)` | `(ErrorReturner, UserPersonalizationData?)` |
| **`UpdatePersonalization`** | `(UserPersonalizationData, GlobalParam)` | `Task<ErrorReturner>` — полностью перезаписывает данные, постер передавать всегда |
| **`GetProfilePoster`** | `(GlobalParam)` | `(ErrorReturner, string fileId)` — пустая строка если постер не задан |
| **`SetProfilePoster`** | `(fileId, GlobalParam)` | `Task<ErrorReturner>` — атомарно; пустая строка = удалить постер |
| **`RegisterPrekeyBundle`** | `(registrationId, identityPubkey, signedPrekey, oneTimePrekeys, GlobalParam)` | Регистрация готового prekey-bundle текущего устройства |
| **`FetchPrekeyBundle`** | `(userId, deviceId, GlobalParam)` | `(ErrorReturner, FetchPrekeyBundleResponse?)` — bundle передаётся без обработки |
| **`ListPeerDevices`** | `(userId, GlobalParam)` | `(ErrorReturner, List<PeerDeviceInfo>?)` |
| **`ReplenishOneTimePrekeys`** | `(prekeys, GlobalParam)` | `(ErrorReturner, totalOneTimePrekeys)` |
| **`RotateSignedPrekey`** | `(signedPrekey, GlobalParam)` | `Task<ErrorReturner>` |

> **Жирным** — добавлено в последних обновлениях

**`UserData`** — `Username`, `FirstName`, `LastName`, `Email`, `Id`, `RegistrationDate`, `Badges`, `ProfilePictureUrl`, `ProfilePicturePreviewUrl`, `Description`

**`Proto.Users.PrivacySettings`** — `ProfileVisibleOnSite`, `AvatarVisibility`, `BioVisibility`, `EmailVisibility`, `SearchVisible`, `OnlineVisibility` (enum `ProfileFieldVisibility`: ALL/FRIENDS/NONE)

---

### `WebApiAuthManager.cs` — 2FA / OTP

**gRPC:** `IdentityApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `OtpReceipt` | `(GlobalParam)` | `(ErrorReturner, string? qrBase64, string? code)` |
| `OtpAccept` | `(GlobalParam, code)` | `Task<ErrorReturner>` |
| **`OtpDisable`** | `(GlobalParam)` | `Task<ErrorReturner>` |
| **`OtpStatus`** | `(GlobalParam)` | `(ErrorReturner, bool authenticatorEnabled, bool emailEnabled)` |

---

### `WebApiRegistrationManager.cs` — Регистрация

**gRPC:** `IdentityApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `CreateAccount` | `(firstName, lastName, email, login, GlobalParam)` | `(ErrorReturner, string? codeId)` |
| `ConfirmAccount` | `(codeId, codeValue, GlobalParam)` | `(ErrorReturner, Token? refreshToken)` |

---

### `WebApiPasswordManager.cs` — Пароль

**gRPC:** `IdentityApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `SetPassword` | `(newPassword, GlobalParam)` | `Task<ErrorReturner>` |
| `ResetPassword` | `(email, username, GlobalParam)` | `(ErrorReturner, string? resetId)` |
| `ConfirmResetCode` | `(resetId, otpCode, GlobalParam)` | `(ErrorReturner, Token? refreshToken)` |

---

### `WebApiMessageManager.cs` — Сообщения и чаты

**gRPC:** `MessagesApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `GetChats` | `(GlobalParam)` | `(ErrorReturner, List<Chat>?)` |
| `GetChatInfo` | `(GlobalParam, chatId)` | `(ErrorReturner, ChatInfo)` |
| `SendMessage` | `(GlobalParam, (isUserId, recipient), ForwardingLetter)` | `(ErrorReturner, MessageModel?)` |
| `CreateGroupChat` | `(GlobalParam, chatName, userIds)` | `(bool, string?)` |
| `CreateChat` | `(GlobalParam, userId)` | `(bool, string?)` |
| `GetMessages` | `(GlobalParam, chatId, fromMessageId)` | `(ErrorReturner, List<MessageModel>?)` |
| `GetMessagesWithOffset` | `(GlobalParam, chatId, fromMessageId, offsetBefore, offsetAfter)` | `(ErrorReturner, List<MessageModel>?)` |
| `MarkMessageAsRead` | `(GlobalParam, List<long> messageIds)` | `Task<ErrorReturner>` |
| `GetPersonChatId` | `(GlobalParam, userId)` | `(ErrorReturner, string chatId)` |
| **`ListChatMembers`** | `(GlobalParam, chatId, offset=0, size=50)` | `(ErrorReturner, List<DetailedChatMemberInfo>?, int totalCount)` |
| **`ListChatAttachments`** | `(GlobalParam, chatId, attachmentType=Unknown, sortDesc=true, offset=0, size=50)` | `(ErrorReturner, List<ChatAttachmentInfo>?, int totalCount)` |
| **`KickUser`** | `(GlobalParam, chatId, userId)` | `Task<ErrorReturner>` |
| **`EditMessage` / `DeleteMessage`** | | Правка и soft-delete сообщений |
| **`PinMessage` / `UnpinMessage` / `ListPinnedMessages` / `UnpinAll`** | | Управление закреплёнными сообщениями |
| **`AddUser` / `UpdateGroupChat`** | | Управление составом и данными группы |

### `WebApiSecretChatManager.cs` — Секретные чаты

Транспорт для `SendSecretChatInvite`, `AcceptSecretChatInvite`, `RejectSecretChatInvite`, `SendSecretMessage`, `AckSecretMessage`. `byte[]` envelope передаётся как есть; Double Ratchet/X3DH/libsignal и локальное хранилище сессий не входят в библиотеку.

### `WebApiPrivateChatManager.cs` и `Crypto/PrivateChatCrypto.cs` — Приватные чаты

Восемь RPC приватных чатов и AES-256-GCM шифрование. `PrivateChatCrypto` совместим с Android: Argon2id v1.3 (`t=3`, `m=65536 KiB`, `p=4`), 32-байтовые salt/ключ, nonce 12 байт, HMAC-SHA256 verifier и AAD `barkfluff:private:{chatId}`. Ключ остаётся у приложения.

### `WebApiCallsManager.cs` — Звонки

Сигнализация `InitiateCall`, `JoinCall`, `AcceptCall`, `RejectCall`, `EndCall`, `SetCallAudioQuality`, `SubscribeCallEvents`, `ListCallHistory`, `GetActiveCalls`. При отсутствии `CallsAC` все методы возвращают ошибку «Звонки недоступны на этом сервере»; библиотека не содержит LiveKit media SDK.

**`ChatInfo`** — `ChatId`, `Members: List<long>`, `Title`, `CountUnread`, `FirstUnreadId`, `IsGroup`, `LastMessageId`, `Picture`

**`MessageModel`** — `MessageId`, `ChatId`, `Text`, `Attachments: List<AttachmentsModel>`, `SenderId`, `SentAt`, `Type`, `ReadBy: List<long>`, `IsSystemMessage`

**`AttachmentsModel`** — `Id`, `Type (MessageAttachmentType)`, `PreviewUrl`, `FileId`, `PreviewFileId`, `FileName`, `Size`, `ImageWidth`, `ImageHeight`

**`ForwardingLetter`** — `Text: string`, `FilesId: List<string>`

---

### `WebApiSearchManager.cs` — Поиск

**gRPC:** `UsersApi`

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `SearchUser` | `(GlobalParam, query)` | `(ErrorReturner, List<UserData>?)` — до 50 результатов |

---

### `WebApiFileManager.cs` — Файлы

**gRPC:** `FilesApi` + HTTP для загрузки

**Singleton:** `private static readonly HttpClient _httpClient` (timeout 5 мин)

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `GetUserStorageInfoAsync` | `(GlobalParam)` | `(ErrorReturner, long used, long total, Dict<UploadFileType, long>)` |
| `UploadFileAsync` | `(GlobalParam, filePath, UploadFileType)` | `(ErrorReturner, string? fileId)` |
| `UploadFileAsync` | `(GlobalParam, filePath, UploadFileType, IProgress<double>?)` | `(ErrorReturner, string? fileId)` |
| `UploadUserAvatarAsync` | `(GlobalParam, byte[] jpegBytes)` | `Task<ErrorReturner>` |
| `GetFile` | `(GlobalParam, fileId)` | `(ErrorReturner, string? url)` |
| `GetFiles` | `(GlobalParam, List<string> fileIds)` | `(ErrorReturner, List<string>? urls)` |
| `CheckFileHashAsync` | `(GlobalParam, fileHash)` | `(ErrorReturner, string fileId)` — если уже загружен |
| `ListStickerPacksAsync` | `(GlobalParam, offset=0, size=50)` | `(ErrorReturner, List<StickerPackInfo>?, int total)` |
| `GetStickerPackAsync` | `(GlobalParam, packId)` | `(ErrorReturner, StickerPackInfo?, List<StickerInfo>?)` |
| `ComputeFileHashAsync` (static) | `(filePath)` | `Task<string>` — SHA-256 |
| `ComputeDataHash` (static) | `(byte[])` | `string` — SHA-256 |

**Процесс загрузки файла:**
1. Если изображение — `ImageProcessor.ProcessImageForUploadAsync()`
2. Проверка свободного места в хранилище
3. SHA-256 хеш → `CheckFileHash` (дедупликация)
4. `FilesApi.GetUploadUrl` → multipart HTTP POST

---

### `WebApiUpdateManager.cs` — Real-time обновления

**gRPC:** `UpdatesApi` (server-side streaming)

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `JustUpdate` | `(GlobalParam, CancellationToken ct = default)` | `(ErrorReturner, IAsyncEnumerable<NewMessageEvent>?)` |
| `SubscribeToReadReceipts` | `(GlobalParam, CancellationToken ct = default)` | `(ErrorReturner, IAsyncEnumerable<MessageReadEvent>?)` |
| **`SubscribeToMessagesEdited/Deleted/Pinned/Unpinned/AllMessagesUnpinned`** | | Updates обычных сообщений |
| **`SubscribeToPrivate*`** | | Шесть Updates-стримов приватных чатов |
| **`SubscribeToSecretChatInvites/Resolutions/SecretMessages`** | | Три Updates-стрима секретных чатов; envelope opaque |

**CancellationToken обязателен для отмены стрима.** `ct` прокидывается и в сам streaming-call (`cancellationToken: ct`), и в `MoveNext(ct)`. Без него стрим невозможно закрыть со стороны клиента — он висит на сокете до тайм-аута/обрыва сервером, что давало утечку соединений и race со свежими стримами после `TokenRefreshed`/`Dispose()`.

**`NewMessageEvent`** — `Message`, `ChatId`

**`MessageReadEvent`** — `ChatId`, `MessageId`, `NewReadBy: List<long>` (полный список прочитавших, не дельта)

---

### `WebApiOnlinerManager.cs` — Онлайн-статусы

**gRPC:** `OnlinerApi` (server-side streaming + unary)

| Метод | Сигнатура | Возвращает |
|-------|-----------|-----------|
| `SubscribeToOnlineStatus` | `(List<long> userIds, GlobalParam, CancellationToken ct = default)` | `(ErrorReturner, IAsyncEnumerable<UserOnlineStatus>?)` — `ct` пробрасывается в streaming-call + `MoveNext` |
| `SetOnlineStatus` | `(GlobalParam)` | `Task<ErrorReturner>` |
| `GetOnlineStatus` | `(List<long> userIds, GlobalParam)` | `(ErrorReturner, GetOnlineStatusResponse?)` |
| `ChangeUsersInSubscription` | `(List<long> userIds, GlobalParam)` | `Task<ErrorReturner>` |

**`UserOnlineStatus`** — `UserId`, `Status (ONLINE/OFFLINE)`, `LastSeen`

---

## Модели данных

### `MessengerData/GlobalParam.cs`

Главный контейнер состояния приложения. Сохраняется/загружается зашифрованным.

**Шифрование:** AES-256-GCM, ключ из PBKDF2-SHA512 (600 000 итераций), формат файла BFV3: `[magic 4][salt 16][nonce 12][tag 16][ciphertext]`. Читает легаси-формат BFV2 (PBKDF2-SHA256 × 100 000).

**Поля:** `SocketBeacon`, `SocketUsers`, `SocketIdentity`, `SocketFiles`, `SocketMessages`, `SocketUpdates`, `SocketOnliner`, `SocketFastAuth`, `SocketCalls`, `LivekitUrl`, `ServerDnsName`, `FederationEnabled`, токены и UI-настройки.

**`NotificationDisplayMode`:**
- `0` Disabled — отключены
- `1` HiddenContent — «Новое сообщение»
- `2` SenderOnly — только отправитель
- `3` FullTextNoPreview — отправитель + текст
- `4` FullWithPreview — полное отображение

### `MessengerData/ClientColors.cs`

`LiteHex`, `MainHex`, `HardHex` — цветовая схема сервера.

### `MessengerData/NonSavedData/`

Транзиентные модели (не сохраняются на диск):

| Класс | Поля |
|-------|------|
| `UserData` | `Username`, `FirstName`, `LastName`, `Email`, `Id`, `RegistrationDate`, `Badges`, `ProfilePictureUrl`, `ProfilePicturePreviewUrl`, `Description` |
| `ChatInfo` | `ChatId`, `Members`, `Title`, `CountUnread`, `FirstUnreadId`, `IsGroup`, `LastMessageId`, `Picture` |
| `MessageModel` | `MessageId`, `ChatId`, `Text`, `Attachments`, `SenderId`, `SentAt`, `Type`, `ReadBy`, `IsSystemMessage`, `IsEdited`, `EditedAt` |
| `AttachmentsModel` | `Id`, `Type`, `PreviewUrl`, `FileId`, `PreviewFileId`, `FileName`, `Size`, `ImageWidth`, `ImageHeight`, `ForwardedMessage?` |
| `ForwardedMessageModel` | `AuthorName`, `OriginalMessageId`, `Text`, `Attachments` |
| `ForwardingLetter` | `Text`, `FilesId: List<string>`, `ForwardedMessageId` |
| `ServerDataElement` | `Title`, `Description`, `Ip`, `UserCount`, `PublicName`, `Location`, `HexColor` |
| `ChatCacheClass` | `ChatId`, `ChatName`, `AvatarFileId`, `LastMessage?` |

---

## gRPC-клиенты и сервисы

| Клиент | Сервис | Proto-namespace | Менеджер(ы) |
|--------|--------|-----------------|-------------|
| `UsersAC` | `UsersApi` | `BarkFluff.Proto.Users` | UserManager, SearchManager |
| `IdentityAC` | `IdentityApi` | `BarkFluff.Proto.Identity` | UserManager, AuthManager, RegistrationManager, PasswordManager, TokenManager |
| `MessagesAC` | `MessagesApi` | `BarkFluff.Proto.Messages` | MessageManager |
| `FilesAC` | `FilesApi` | `BarkFluff.Proto.Files` | FileManager |
| `UpdatesAC` | `UpdatesApi` | `BarkFluff.Proto.Updates` | UpdateManager |
| `OnlinerAC` | `OnlinerApi` | `BarkFluff.Proto.Onliner` | OnlinerManager |
| `BeaconAC` | `BeaconApi` | `BarkFluff.Proto.Beacon` | ServerManager, ClientManager |
| `NavigatorAC` | `NavigatorApi` | `BarkFluff.Proto.Navigator` | ServerManager |
| `FastAuthAC` / `FastAuthUserAC` | `FastAuthApi` | `BarkFluff.Proto.FastAuth` | FastAuthManager |
| `CallsAC` | `CallsApi` | `BarkFluff.Proto.Calls` | CallsManager |
