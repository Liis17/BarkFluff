# BarkFluff macOS

SwiftUI приложение для macOS. gRPC через grpc-swift.

> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)
> Карта всех файлов проекта: [[Клиенты/macOS-ProjectMap]]

Расположение: `Mac/Barkfluff/`
Swift: app-таргет `SWIFT_VERSION = 6.0` (локальные пакеты BFCore/BFNetworking/BFProto/BFCalls объявляют `swift-tools-version: 6.2`). Platforms: macOS 26, iOS 26.

## Зависимости

- `grpc-swift-2 2.3.0+`
- `grpc-swift-protobuf 2.0.0+`
- `grpc-swift-nio-transport 2.0.0+` — HTTP/2 транспорт
- `swift-protobuf 1.28.0+`
- `KeychainAccess 4.2.2+` — токены (BFNetworking)
- `GRDB.swift 7.0.0+` — SQLite ORM для персистентного кеша (BFCore)
- `Nuke` — загрузка/кеш изображений (подключён на уровне Xcode-проекта)

## Архитектура (MVVM + Coordinator)

### Локальные пакеты (`Packages/`)

```
BFProto/       # Proto definitions и gRPC код
BFNetworking/  # gRPC клиент, соединения, репозитории
BFCore/        # Бизнес-логика: сервисы, модели, кеш, БД (GRDB)
```

Зависимость: `BFProto ← BFNetworking ← BFCore ← Main App`

### Структура приложения

```
Barkfluff/
├── App/
│   ├── BarkfluffApp.swift      # @main, lifecycle
│   └── DI/DependencyContainer  # DI
├── Navigation/
│   ├── AppCoordinator.swift    # @Observable, состояния: loading → serverSelection → authentication → main
│   └── RootView.swift
├── Features/                   # MVVM per feature
│   ├── Auth/, ChatList/, Conversation/, GroupChat/
│   ├── Profile/, Settings/, UserSearch/, FastAuth/
└── DesignSystem/
    ├── Components/
    └── LiquidGlass/            # macOS 26 glass effects
```

## Navigator

- Адрес: `navigator.barkfluff.com:443` (TLS через nginx)
- Реализация: `BFNetworking/Repositories/NavigatorRepository.swift`
- Транспорт: `HTTP2ClientTransport.Posix`, `transportSecurity: .tls`
- DependencyContainer явно передаёт `host: "navigator.barkfluff.com", port: 443`

## Data Flow

Views → ViewModels → Services → Repositories → gRPC
- In-memory кеши: `InMemoryCache` (actor, generic), `OnlineStatusCache`, `FileURLCache` (presigned S3)
- Персистентный кеш (GRDB SQLite): `Database` + миграции (`Database/Migrations.swift`, последняя — `v6_current_user`), записи `CachedChatRecord`, `CachedMessageRecord`, `CachedFileRecord`, `CachedChatFolderRecord`, `CachedStickerPackRecord`, `CachedStickerRecord`, **`CachedCurrentUserRecord`** (single-row кеш текущего пользователя — id, имя, username, bio, аватар/постер, бейджи, storage limit)
- Локальные репозитории: `LocalChatRepository`, `LocalMessageRepository`, `LocalChatFolderRepository`, **`LocalCurrentUserRepository`** (`loadCachedCurrentUser()` / `save(_:)` / `clear()` — INSERT OR REPLACE single-row семантика)
- Кеш медиа на диске: `MediaCacheManager`, типизация — `CachedFileType`, статистика — `CacheStats`, парсинг ключей S3 — `S3URLParser`
- `TokenRefreshCoordinator` — автоматический рефреш через `AuthInterceptor`

## Стартовый флоу (stale-first + connection gate)

`AppCoordinator.onAppLaunch(serverDiscovery:, authService:, tokenProvider:)`:

- Нет `savedServerHost` → `.serverSelection`.
- Нет `hasRefreshToken` → синхронный `tryReconnect` → `.authentication` или `.serverSelection`.
- **Есть `hasRefreshToken`** → **сразу `.main`**. UI показывает `MainSplitView` с `ChatListView` (плейсхолдеры/крутилка). В фоне `Task`: `tryReconnect` → `tryRestoreSession`. OK — `coordinator.isConnectionReady = true`; refresh-токен невалиден → `handleSessionExpired()`.

**Connection gate**. `isConnectionReady: Bool` — инвариант «endpoints есть И access-токен валиден». Без этого `listChats`/`onlineStatusService.track`/`getCurrentUser`/`notificationService.start` падают с «Messages не настроено» и онлайн-статусы поломаны. `waitForConnectionReady(timeout: .seconds(30))` — polling каждые 100 мс. Выставляется в `true`: фон после `tryRestoreSession`, `LoginViewModel.login`, `RegisterViewModel.completeRegistration`, FastAuth `onAuthenticated`. Сбрасывается: `handleSessionExpired`, `performLocalWipe`.

`ChatListView.task` (порядок):
1. `vm.isLoading = true` → `ChatRowPlaceholderView` в списке.
2. `await coordinator.waitForConnectionReady()`.
3. Параллельно `loadFolders` / `loadChats` / `loadCurrentUser`.
4. `vm.startListeningForUpdates()`.
5. `notificationService.start(...)` (нужен `currentUserID`).

`DependencyContainer.loadCurrentUser()` — **stale-first + fire-and-forget revalidate** (с v6):
1. Читает `cached_current_user` из SQLite через `LocalCurrentUserRepository` и мгновенно выставляет `container.currentUser` / `container.currentUserID` (если ещё nil).
2. Запускает в фоне `Task { await revalidateCurrentUser() }` — сетевой `getCurrentUser` + write-through в SQLite. Возвращается **сразу** после п.1, не блокирует `ChatListView.task`.

`revalidateCurrentUser()` — публичный write-through. Вызывается:
- из `loadCurrentUser` в фоне;
- из `ProfileEditViewModel.saveChanges` после `changeName/changeUsername/changeBio`;
- из `ProfileEditViewModel.uploadCropped` после `setProfilePicture`;
- из `PersonalizationSettingsViewModel.uploadPoster` после `setProfilePoster`.
Гарантия: после успешного редактирования профиля свежие данные попадают и в `container.currentUser` (мгновенно для UI), и в `cached_current_user` (для следующего cold-start). Если сетевой запрос упал — кешированный юзер остаётся видимым, без сброса UI.

`RootView.onChange` — только `notificationService.stop()` при выходе из `.main`. Стартовая `loadCurrentUser`/`notificationService.start` живут в `ChatListView.task` после gate.

`ChatListViewModel.loadChats()` неблокирующий: показывает кеш и запускает фоновый `revalidateTask`. `revalidateChats()` (await-able) — для pull-to-refresh, `.reconnected`, нового чата, удаления последнего сообщения. `loadFolders()` — то же: кеш мгновенно, refresh в фоне.

Флаг `isOffline` рисует `ErrorBannerView` (с `onRetry: { revalidateChats() }`) в `.safeAreaInset(.top)` поверх списка.

## Паттерн: logout — wipe всего, кроме адреса сервера

Logout строится по «server-first, fail-loud» схеме: если серверный шаг упал, локальные данные **не трогаются**, чтобы пользователь мог повторить или сознательно выйти принудительно.

**Цепочка:**

1. `AppCoordinator.logout(container:) async throws` — вызывает `AuthService.logout()`. На успехе — `performLocalWipe(container:)`.
2. `AuthService.logout() async throws`:
   - `IdentityRepository.logout()` → gRPC `Identity.Logout` (на бэке `LogoutCommandHandler` удаляет refresh-токены устройства, публикует `SessionRevokedEvent` → инвалидируется access-токен по сервисам через шину, и зовёт `Users.DeleteUserDevice` для удаления записи устройства).
   - При ошибке — пробрасывается наружу, токены **не** очищаются.
   - При успехе — `tokenProvider.purgeAll()` локально (полная очистка токенов и `device_id`).
3. `DependencyContainer.reset()` (вызывается из `performLocalWipe`):
   - `updatesService.stop()`, `onlineStatusService.stop()` — закрывают gRPC-стримы и AsyncStream-ы.
   - `userCache`/`chatCache`/`onlineStatusCache.removeAll()` — in-memory кеши.
   - `mediaCacheManager.clearAll()` — файловый медиа-кеш с диска.
   - `fileURLCache.clear()` — runtime + UserDefaults-домен `com.barkfluff.fileURLCache`.
   - `database.truncateAll()` — SQLite (`cached_message`, `cached_chat`, `cached_chat_folder`, `cached_file`, `cached_sticker`, `cached_sticker_pack`, **`cached_current_user`**).
   - `tokenProvider.purgeForLogout()` — токены, даты истечения, `device_id`. **`server_host`/`server_port` сохраняются.**
   - `personalizationSettings.reset()` + `appearanceSettings.reset()` — локальные UserDefaults-настройки клиента (тема, радиус пузыря, blur/dim, фон чата) сбрасываются к дефолтам.
   - `connectionManager.shutdown()` — закрывает gRPC-соединения. `serverDiscoveryService.disconnect()` **больше не вызывается** — мы хотим оставить сам адрес.
4. UI-state в `AppCoordinator` сбрасывается, затем `performLocalWipe` пробует `serverDiscoveryService.tryReconnect()` (использует сохранённый host/port). При успехе — `currentState = .authentication`, `authScreen = .login` (пользователь сразу видит логин того же сервера). При неудаче — fallback на `.serverSelection`.

**Force-вариант** (`AppCoordinator.forceLogout(container:)`): пропускает серверный шаг, локальный wipe идентичный. Используется UI-фолбэком при ошибке `logout()`. Сессия на сервере при этом остаётся «висящей» до истечения срока действия — её можно будет завершить позже через «Активные сессии» (`Identity.RemoveActiveSession`).

**UI-обработка ошибки**: `ProfileEditView` ловит throw из `coordinator.logout(container:)` и показывает алерт «Не удалось разлогиниться» с тремя кнопками — «Повторить» / «Выйти всё равно» (→ `forceLogout`) / «Отмена».

**TokenProvider API** (общий пакет `BFNetworking`, реализации `UserDefaultsTokenProvider` / `KeychainTokenProvider`):
- `clearAll()` — стирает только токены и их даты (используется в `tryRestoreSession` при провале refresh).
- `purgeForLogout()` — стирает токены + `device_id`, **сохраняет** `server_host`/`server_port`. Используется при штатном logout.
- `purgeAll()` — стирает всё, включая `device_id` и адрес сервера. Зарезервирован под будущий «забыть сервер» / диагностический reset.

**Сохраняется** после logout-wipe: `server_host`/`server_port` + настройка `com.barkfluff.tokenStorage.type` (выбор хранилища токенов: UserDefaults / Keychain / Keychain+iCloud) — это user preference, не account data.

`UpdatesStreamManager.start()` **пересоздаёт AsyncStream-ы** при каждом запуске: после `stop()` continuations завершаются (`.finish()`), и старые потоки мертвы — без пересоздания `forwardNewMessagesTask` в `UpdatesService` сразу завершался бы без событий.

## Быстрый вход через QR (FastAuth)

Параллельно паролёвой форме `LoginView` показывает `QRPanelView` (правая колонка `HStack`), повторяющий поведение WPF-клиента (`Windows/.../Pages/SetupPages/Login.xaml`). Сценарий точно совпадает с серверным контрактом из [[Backend/FastAuth]]:

1. При появлении панели `FastAuthViewModel.startSession()` вызывает `FastAuthService.generateToken(.qr)` → `FastAuthRepository.generateFastAuthToken` → `connectionManager.withPublicClient(for: .fastauth)` (анонимно, без `AuthInterceptor`, но с `DeviceMetadataInterceptor`, который кладёт `x-device-name`, `x-os-name`, `x-app-name`, `x-app-version`, `x-ip-address`, `x-device-id` в gRPC-метаданные — backend забирает их в `GenerateFastAuthTokenCommandHandler`).
2. Сервер возвращает `fast_auth_id`, PNG-base64 (`token.value`) и `expires_at` (TTL 5 мин). PNG декодируется в `NSImage` через `Data(base64Encoded:)`/`NSImage(data:)` и кладётся в `currentTokenImage`. Параллельно стартует посекундный countdown-таймер.
3. Тут же подписываемся на server-stream: `FastAuthRepository.subscribeFastAuthResult` → `connectionManager.withPublicClient(for: .fastauth)` + `client.subscribeFastAuthResult(req) { for try await event in response.messages { ... } }`. Стримерные события мапятся в доменный `FastAuthResult` (поля `accessTokenExpiresAt` / `refreshTokenExpiresAt` пробрасываются и через BFNetworking-DTO, и через BFCore-модель, чтобы дойти до ViewModel).
4. Реакция на статусы:
   - `.pending` (включая серверный `SCANNED`, который мы сводим к `pending` — у macOS-логина нет отдельного промежуточного UI) — продолжаем ждать.
   - `.accepted` — `AuthService.applyFastAuthTokens(...)` сохраняет пару токенов через `tokenProvider.saveTokens`, ставит `hasFinished = true` и зовёт `onAuthenticated` колбэк, который из `LoginView` вызывает `container.loadCurrentUser()` и переключает `coordinator.currentState = .main`. Никакого отдельного `Identity.Auth` шага не нужно — токены уже выпустил FastAuth-сервис через `IdentityServerApi.CreateSessionForUserServer`.
   - `.rejected` — показываем `errorMessage` «Вход через QR отклонён на мобильном устройстве» и автоматически перезапускаем `runSession()` (новый QR), идентично WPF.
   - `.expired` — тихий перезапрос QR.
5. При уходе со страницы (`.onDisappear` у `QRPanelView`) — `viewModel.cancel()` отменяет `sessionTask` и `timerTask`, gRPC-стрим закрывается через `continuation.onTermination` в `BFCore.FastAuthService.subscribeToResult`.

Ключевые файлы:
- UI: `Features/FastAuth/Views/FastAuthQRView.swift` — встраиваемый `QRPanelView`. Старый модальный `FastAuthQRView` удалён, как и `QRCodePlaceholder` (PNG приходит готовый с сервера, локальный `CIFilter.qrCodeGenerator` не нужен).
- ViewModel: `Features/FastAuth/ViewModels/FastAuthViewModel.swift` — `@Observable @MainActor`, держит `sessionTask` и `timerTask`, авто-перезапуск при rejected/expired.
- Репозиторий: `Packages/BFNetworking/.../Repositories/FastAuthRepository.swift` — `generateFastAuthToken` + server-streaming `subscribeFastAuthResult`. Остальные методы (Scan/Accept/Reject/connectDevice…) — заглушки: это мобильный сценарий, на macOS-клиенте не используется.
- Сервис: `Packages/BFCore/.../Services/Implementations/AuthService.swift` метод `applyFastAuthTokens` — сохраняет уже выданные токены через `tokenProvider.saveTokens`, без серверного шага.
- Интеграция в логин: `Features/Auth/Views/LoginView.swift` — `HStack` из `formCard(viewModel)` слева и `QRPanelView(viewModel: fastAuthViewModel)` справа.

Ошибки/edge-cases:
- Если эндпоинт `.fastauth` не получен из Beacon (`mapServiceEndpoints`), `withPublicClient` бросит `ConnectionError.serviceNotConfigured(.fastauth)` — `FastAuthViewModel` покажет это через `errorMessage`, паролёвая форма продолжает работать.
- Если стрим оборвался до финального статуса — `runSession()` спит 1 секунду и пробует заново (без вспышки QR — UI остаётся со старым изображением до момента, когда новый QR будет получен).
- `hasFinished == true` после `.accepted` гарантирует, что повторный вызов `runSession()` не сработает после успешного входа (защита от race с автоперезапуском).

## Liquid Glass (macOS 26 / iOS 26)

**ВАЖНО**: При работе с UI-эффектами читать `LiquidGlassGuide.md` в корне проекта.

Краткие правила:
- `.glassEffect(.regular, in: .capsule)` вместо `.ultraThinMaterial` для контролов
- `.glassEffect(.clear)` — для мелких контролов поверх медиа
- `.buttonStyle(.glass)` / `.buttonStyle(.glassProminent)` — для кнопок
- `GlassEffectContainer` — обязателен при нескольких glass-элементах рядом
- Glass — только для навигационного слоя, НИКОГДА для контента

## Онлайн-статусы (per-user observation + ref-counted tracking)

`OnlineStatusService` (actor, singleton) — single source of truth для статусов всех отслеживаемых пользователей. Бекенд: `OnlinerApi` (см. [[Backend/Onliner]]).

Контракт для UI-консумера:
- `currentStatus(for: userID)` — мгновенный snapshot из кеша.
- `statusStream(for: userID)` — `AsyncStream` с diff-обновлениями ТОЛЬКО для этого `userID`.
- `track(userID)` / `untrack(userID)` — ref-counted; первый track делает authoritative GET и добавляет юзера в gRPC-подписку, последний untrack — удаляет. Каждый `track` ОБЯЗАН быть парным.

Консумер-паттерн в SwiftUI:
```swift
@State var status: OnlineStatus = .unknown

.task(id: otherUserID) {
    status = await service.currentStatus(for: otherUserID)
    await service.track(otherUserID)
    await withTaskCancellationHandler {
        for await new in await service.statusStream(for: otherUserID) {
            status = new
        }
    } onCancel: {
        Task { await service.untrack(otherUserID) }
    }
}
```

Ключевые свойства:
- **`applyStatus` с dedup'ом** — если новое значение совпадает с кешем, broadcast не идёт (предотвращает лишние UI-обновления).
- **Per-user multicast** — никаких общих `@Observable` словарей в ViewModel'ях, чтобы изменение одного юзера не инвалидило весь список чатов.
- **Authoritative fetch** — `track` всегда тащит свежий статус с сервера, даже если в кеше что-то есть (защита от stale-cached значений).
- **Reconnect refresh** — после восстановления соединения `refreshAllTrackedStatuses` синхронизирует кеш через `applyStatus` (с dedup'ом, без flicker'а).

Консумеры: `ChatRowView` (через `.task(id:)`), `ConversationViewModel.startListeningForOnlineStatus` (для статуса под именем в открытом чате), `UserProfilePanelViewModel`.

## Контекстное меню сообщения

`MessageBubbleView.contextMenu` для подтверждённых сообщений (`message.id > 0`, не failed, не system) собирает пункты по правилам:

| Пункт                       | Условие показа |
|-----------------------------|----------------|
| **Изменить**                | `senderID == currentUserID` и есть текст или нефорвард-вложения |
| **Ответить**                | всегда |
| **Переслать**               | всегда (`message.forwardSourceID` исключает «вложенные» пересылки) |
| **Копировать текст**        | `!message.content.text.isEmpty` |
| **Скопировать изображение** | ровно одно вложение типа `image` |
| **Сохранить изображение(я)**| есть вложения типа `image` (label сингулярный/плюральный) |
| **Сохранить в загрузки**    | есть вложения типа `document`, `audio` или `voice` |
| **Удалить**                 | `senderID == currentUserID` (с `.confirmationDialog`) |

Колбеки пробрасываются `MessagesListView` → `ConversationView`:
- `onCopyText` — `NSPasteboard.general.setString(... , forType: .string)`
- `onCopyImage`/`onSaveImages`/`onSaveDocuments` — `MediaActions` (см. ниже)
- `onEdit` — seed `messageText = message.content.text` + `viewModel.enterEditMode(message)`
- `onDelete` — устанавливает `pendingDeleteMessageID`, открывает `.confirmationDialog`

## Редактирование и удаление сообщений

### Edit-flow (`MessagesApi.EditMessage`)

`ConversationViewModel`:
- `editingMessage: Message?` — состояние режима редактирования (взаимоисключающее с `pendingReply`).
- `editingOriginalFileIDs: [String]` — fileIDs нефорвард-вложений оригинала (на macOS UI редактирования attachments нет, передаём как есть).
- `enterEditMode(_:)` — gating по `senderID == currentUserID && id > 0`, сбрасывает `pendingReply`.
- `submitEdit(text:)` — optimistic правка `messages[idx]` + `messageService.editMessage(chatID:messageID:text:fileIDs:)` + `localMessageRepository.update(_:)`. На ошибке — `loadMessages()` для восстановления.
- `cancelEdit()` — выход из режима без отправки.

UI (`ConversationView`):
- `EditPreviewView` (новый компонент по образцу `ReplyPreviewView`) — рисуется над `MessageInputView`, иконка `pencil`, заголовок «Редактирование сообщения», текст оригинала.
- `MessageInputView.isEditMode` — флаг прокидывается в `SendButton.isEditMode` → иконка стрелки заменяется на `checkmark`.
- Esc выходит из edit-режима первым (до `pendingReply` и attachments).
- В `MessageTimeView` рядом со временем отображается иконка `pencil`, если `message.isEdited`.

### Delete-flow (`MessagesApi.DeleteMessage`)

- `ConversationViewModel.deleteMessage(messageID:)` — optimistic `messages.removeAll`, затем `messageService.deleteMessage` + `localMessageRepository.delete`. На ошибке — rollback по snapshot.
- `.confirmationDialog("Удалить сообщение?")` в `ConversationView` с двумя кнопками: «Удалить» (destructive) / «Отмена».

### Real-time подписки (`UpdatesApi`)

`SubscribeMessagesEdited` → `MessageEditedEvent{chat_id, message}` и `SubscribeMessagesDeleted` → `MessageDeletedEvent{chat_id, message_id}`:

- `UpdatesStreamManager` (BFNetworking): два новых таска `messagesEditedTask` / `messagesDeletedTask` с тем же retry/backoff паттерном, что и `newMessagesTask`. Continuations для `AsyncStream<MessageEditedEventDTO>` / `AsyncStream<MessageDeletedEventDTO>` пересоздаются в `start()`, гасятся в `stop()`.
- `UpdatesService` (BFCore): `getEditedMessagesStream()` / `getDeletedMessagesStream()` (broadcaster по UUID-подписчикам), forward-таски маппят DTO → доменные `MessageEditedEvent` / `MessageDeletedEvent`.
- `ConversationViewModel.startListeningForUpdates()` подписывается на оба стрима и вызывает `applyEditedMessage(_:)` (merge с сохранением `localID`/`sendingState`) и удаление с анимацией. Локальный SQLite-кеш обновляется через `LocalMessageRepository.update(_:)` / `.delete(messageID:chatID:)`.
- `ChatListViewModel` подписывается на те же стримы для обновления `lastMessage` в превью списка чатов; при удалении last_message — `loadChats()` (backend вернёт актуальный last).

### Поля в shared.proto

В `Barkfluff_Shared_Message` добавлены `is_edited: bool` (field 7) и `edited_at: Timestamp` (field 8). Маппятся в `BFCore.Message.isEdited` / `editedAt` через `MessageMapper.toDomain(_:)` и `MessagesRepository.mapMessage`. SQLite-таблица `cached_message` мигрирована до v3 — добавлены колонки `is_edited` и `edited_at`.

## Пересылка и ответ на сообщение

UX повторяет веб- и Android-клиентов (Telegram-style).

- **Ответить** — синхронно `viewModel.setPendingReply(message)`. Превью оригинала (`ReplyPreviewView`: акцентная полоса + имя автора + первая строка текста или emoji-сводка вложения + кнопка ×) появляется над `MessageInputView`. Пользователь набирает свой текст, и при отправке через `sendMessage` / `sendMessageWithAttachments` `pendingReply` снапшотится, превью очищается, в pending-сообщение добавляется placeholder через `makeForwardedPlaceholder`, а на сервер уходит `forwardedMessageID = pendingReply.id` вместе с текстом и `fileIDs`. Серверный ответ заменяет pending. Esc и кнопка × очищают `pendingReply`. При входе в edit-режим reply очищается, и наоборот.
- **Переслать** — `MessageBubbleView` в callback `onForward` отдаёт `message.forwardSourceID` (computed extension в BFCore: если у сообщения уже есть вложение `FORWARDED_MESSAGE` с положительным `originalMessageID` — возвращает его, иначе свой id). Это исключает «вложенные» пересылки. `ConversationView` ставит `coordinator.presentedSheet = .forwardMessage(messageID:, sourceChatID:)`, sheet монтируется в `MainSplitView` через `.sheet(item:)`. `ForwardChatPickerViewModel` (мультивыбор) держит `selectedChatIDs: Set<String>`, `commentText: String`, `forwardToSelected()` шлёт сообщения параллельно через `withTaskGroup`. `ForwardChatPickerView` отрисовывает только аватары + имена с круглым checkmark, без last-message-превью; внизу — capsule-`TextField` для опционального комментария + круглая кнопка отправки рядом. На успехе хотя бы одной отправки модалка закрывается.

Контракт с сервером (см. [[Backend/Messages]]):
- Клиент шлёт только `OutgoingMessage.forwardedMessageID` — backend сам формирует attachment типа `FORWARDED_MESSAGE` со снимком автора/текста/вложений оригинала.
- Доменная модель в BFCore — `MessageAttachment.forwarded: ForwardedMessagePayload?`. Отрисовка: `ForwardedMessageView` (вертикальная цветная полоса слева, имя автора, текст, мини-список вложений) + `ReplyPreviewView` (компактная версия для превью над инпутом).

## Медиа-действия из контекстного меню (MediaActions)

`Conversation/Helpers/MediaActions.swift` (`@MainActor enum`):
- `copyImageToPasteboard(_:container:)` — `mediaCacheManager.resolveURL(for:type:.image)` → `NSImage(contentsOf:)` → `NSPasteboard.general.writeObjects([img])` (вставляется в Pages/Mail/Preview/Telegram).
- `saveImages(_:container:)` / `saveDocuments(_:container:)` — копируют файлы из локального кеша в `~/Downloads/BarkFluff/`. Папка создаётся автоматически (`FileManager.urls(for: .downloadsDirectory)` + `appendingPathComponent("BarkFluff", isDirectory: true)`). Конфликт имён разруливается суффиксами ` (2)`, ` (3)`. По завершении — `NSWorkspace.activateFileViewerSelecting(...)` (один файл — выделяет файл, несколько — открывает папку).

Sandbox: добавлен entitlement `com.apple.security.files.downloads.read-write` (рядом с `files.user-selected.read-write`) — даёт прямой доступ к `~/Downloads` без NSSavePanel.

Регенерация proto: `make generate` в `Packages/BFProto/` (источник — `Mac/Barkfluff/Protos/`, синхронизируется с `Shared/BarkFluff.Proto/`).

## Отдельный файловый адрес ноды (мимо CDN)

Beacon отдаёт `files_media_endpoint` — второй публичный origin файлового HTTP ноды
(`files2.barkfluff.com`, [[Backend/Nginx]]), не проходящий через CDN с его лимитом на размер файла.

- `ConnectionManager.filesMediaOrigin` заполняется в `bootstrap` (сбрасывается в `shutdown`),
  `rewriteToMediaOrigin(_:)` подменяет в ссылке **только** схему/хост/порт — путь `/web/...` тот же.
- Применяется в `FilesRepository`: `getUploadURL`, `getTempDownloadURL(s)`. Этого достаточно, потому
  что все загрузки и скачивания (включая `MediaCacheManager` и `FileDownloadHelper`) берут URL
  оттуда. Аватары из профилей (`presignedURLHint`) намеренно остаются на основном адресе Files —
  они мелкие, и CDN для них полезен.
- Пустое значение (нода не объявила адрес) = поведение как раньше. Изменения лежат в общих пакетах,
  поэтому распространяются и на [[Клиенты/iOS]].

## Отправка изображений (клиентская оптимизация)

Перед загрузкой на сервер вложения с `uploadFileType == .messageAttachmentImage` (PNG/JPEG/WebP/HEIC/BMP по расширению) проходят пайплайн в `ConversationViewModel.optimizeImage(_:)` в соответствии с разделом «Изображения» из [[Клиенты/DesignDocument]]:

1. Если длинная сторона > 2500 px — пропорциональный downscale до 2500 px (`NSImage.lockFocus` + `draw`).
2. Кодирование в JPEG через `NSBitmapImageRep.representation(using: .jpeg, properties: [.compressionFactor:])`. Стартовое качество **0.9**; если результат > 2 МБ — пошагово 0.9 → 0.8 → 0.7 → 0.6 → 0.5, пока не уложится. На последнем шаге (0.5) возвращается то, что получилось, даже при превышении.
3. После успешной оптимизации имя файла нормализуется в `*.jpg` через `jpegFileName(from:)`. Это нужно, потому что `FilesRepository.contentType(for:)` определяет `Content-Type` по расширению — без переименования multipart-загрузка PNG-имени с JPEG-байтами уехала бы как `image/png`. Сервер всё равно классифицирует тип по magic bytes, но MIME-заголовок должен соответствовать содержимому.

Видео, документы, GIF, аудио и голосовые сообщения через `optimizeImage` **не проходят** — `if attachment.uploadFileType == .messageAttachmentImage` фильтрует только IMAGE.

Точки вызова: `ConversationViewModel.sendMessageWithAttachments` — отдельные ветки для нового диалога (без optimistic) и существующего чата (optimistic flow с `uploadProgress`). Логика одинаковая, продублирована намеренно — общий progress-таск завязан на индекс в массиве, выносить хелпер пока избыточно.

Превью медиа в `MediaPreviewCard` отображает **исходные** данные, не оптимизированные — пользователь видит то, что выбрал, а сжатие происходит уже в момент отправки.

## Стикеры (стикер-пикер)

Полноценный стикер-пикер по паритету с iOS/Android. Файлы:
- `Features/Conversation/ViewModels/StickerPickerViewModel.swift` — состояние паков/недавних
- `Views/Stickers/{StickerImageView, StickerPickerView, StickersGridView, StickerPackTabsView, StickerThumbView}.swift`
- `Views/Attachments/StickerMessageView.swift` — рендер стикера в ленте
- `Helpers/RecentStickersStore.swift` — недавно использованные стикеры
- вызывается из `Features/Conversation/Views/MessageInputView.swift`
- BFCore: `Services/Implementations/StickersService.swift`, кеш `CachedStickerPackRecord`/`CachedStickerRecord`

## Настройки (категории-вкладки)

`SettingsView` — корневой контейнер с навигацией по `SettingsCategory` (enum в `Navigation/SettingsCategory.swift`). Каждая категория — отдельный View + при необходимости свой `@Observable` ViewModel:

| Категория       | View                          | ViewModel                          |
|-----------------|-------------------------------|------------------------------------|
| General         | `GeneralSettingsView`         | `AppearanceSettings` (UserDefaults) |
| Notifications   | `NotificationsSettingsView`   | —                                  |
| Personalization | `PersonalizationSettingsView` + `PosterPreviewCard`/`BubblePreviewView`/`BackgroundsGrid` | `PersonalizationSettingsViewModel` |
| Cache           | `CacheSettingsView` + `CacheStackedBarView` | `CacheSettingsViewModel` |
| Cloud           | `CloudSettingsView` + `CloudStackedBarView` | `CloudSettingsViewModel` |
| Security        | `SecuritySettingsView` (case `.security`, «Безопасность», `lock.fill`) | через `SettingsViewModel` |
| Privacy         | `PrivacySettingsView` (case `.privacy`, «Приватность», `eye.slash.fill`) | `PrivacySettingsViewModel` |
| Sessions        | `SessionsView`                | через `SettingsViewModel`          |
| About App       | `AboutAppSettingsView`        | —                                  |
| About Server    | `AboutServerSettingsView`     | `AboutServerSettingsViewModel`     |

`SettingsCategoryView` — общий компонент-обёртка раздела (заголовок, отступы, glass-стиль). `SettingsPlaceholderView` — для пустых/в разработке разделов.

### Тема приложения (General → Внешний вид)

`AppearanceSettings` (`App/Settings/AppearanceSettings.swift`) — `@MainActor @Observable` поверх `UserDefaults` (ключ `appearance.theme`). Enum `AppTheme`: `.system` / `.light` / `.dark`; `nsAppearance` мапит в `NSAppearance?` (`.aqua` / `.darkAqua` / `nil` для системной).

`GeneralSettingsView` — `Picker` поверх `AppTheme.allCases`, биндится к `container.appearanceSettings.theme` через `@Bindable`.

**Применение темы — через `NSApplication.shared.appearance`**, а не SwiftUI-модификатор `.preferredColorScheme()`. На macOS `.preferredColorScheme(nil)` некорректно «отпускает» форсированную тему на части AppKit-бэкэнда (NSWindow chrome, sidebar, `NSVisualEffectView`), из-за чего после dark → system половина UI остаётся тёмной. Прямое присвоение `NSApp.appearance` каскадно прокидывается во все окна процесса, а `nil` корректно возвращает «следовать системе». `apply()` вызывается из `init` (старт приложения) и из `didSet` свойства `theme` (мгновенное переключение). Семантические цвета (`Color.primary/.secondary/.accentColor`, `Color(nsColor: .windowBackgroundColor)`) и Liquid Glass подхватывают новую тему автоматически.

## Системные уведомления

`NotificationService` (`App/Notifications/NotificationService.swift`) — отдельный `@Observable`-сервис, живёт в `DependencyContainer`. Подписан на тот же стрим, что и `ChatListViewModel` (`UpdatesService.getNewMessagesStream()`), но через **независимую** подписку — `UpdatesService.newMessagesSubscribers` мультиподписан по UUID.

Условие показа баннера (Telegram-style):

1. `event.message.isSystem == false`
2. `event.message.senderID != currentUserID`
3. `settings.showNotifications == true`
4. `coordinator.currentState == .main`
5. **НЕ** `(coordinator.selectedChat?.id == event.chatID && appFocusState.isActive)` — не дублируем баннер для чата, который и так открыт + активен.

Состояние фокуса — `AppFocusState` (`App/Notifications/AppFocusState.swift`), обёртка над `NSApplication.didBecomeActive/didResignActive`. UI-state (`selectedChat`, `isActive`, `currentState`) читается за один MainActor-хоп в `handle(_:)`.

Содержимое уведомления собирает `NotificationContentBuilder`:
- DM: title = имя отправителя, body = текст или сводка по вложениям.
- Группа: title = название чата, subtitle = имя отправителя, body = текст/сводка.
- Сводка вложений: эмодзи + RU-плюрализация (`🖼 3 фото`, `📎 Документ`, `🎤 Голосовое`, `🎥 Видео`, `🎵 N аудио`, `GIF`, `🎭 Стикер`). Для смешанных типов — «самый визуальный» + `+N`.
- Аватар: `MediaCacheManager.resolveURL(.avatar, presignedURLHint:)` → копия в `NSTemporaryDirectory()` → `UNNotificationAttachment`. Для группы предпочитаем `chat.pictureURL`, fallback — `sender.profilePicturePreviewURL`.
- `threadIdentifier = chatID` — системная группировка баннеров по чату.
- `userInfo = ["chatID": ..., "messageID": ...]` — используется делегатом при клике.

**Клик по баннеру** → `NotificationDelegate.userNotificationCenter(_:didReceive:)` → `NSApp.activate(ignoringOtherApps:)` + `coordinator.selectedChat = chat`. Если `ChatListViewModel` ещё не загрузил список (cold start через клик), polling-fallback на 2 секунды.

**Очистка** при открытии чата: `AppCoordinator.notifyChatOpened(_:)` дополнительно вызывает `notificationService.clearDelivered(for: chatID)`, который снимает все уже показанные баннеры этого чата из Notification Center.

**Настройки** — `NotificationSettings` (`App/Notifications/NotificationSettings.swift`), `@Observable` поверх `UserDefaults`:
- `notif.showNotifications` (default true)
- `notif.playSound` (default true)

UI настроек — `Features/Settings/Views/NotificationsSettingsView.swift`, тогглеры байндятся к `container.notificationSettings`. При переключении `showNotifications` в OFF вьюха через `.onChange` вызывает `notificationService.removeAllDelivered()`, чтобы старые баннеры не висели в Notification Center после выключения фичи. `playSound` читает `NotificationContentBuilder.build(...)` — когда выключен, `content.sound` не ставится, остаётся только визуальный баннер. Главный гейт по `showNotifications` — в `NotificationService.handle(_:)` (ранний `return` до резолва аватара/имени).

**Lifecycle**: `RootView.task` ставит делегата и спрашивает разрешение один раз при старте; `RootView.onChange(of: currentState)` запускает `notificationService.start(coordinator:currentUserID:)` при переходе в `.main` и `stop()` при выходе. На `stop()` все баннеры снимаются (`removeAllDeliveredNotifications`).

## Персонализация чата

Раздел `Settings → Персонализация` повторяет реализацию Android-клиента и состоит из трёх блоков:

1. **Постер профиля** — превью карточки (постер 3:1 + аватар поверх + имя/`@username`) с кнопкой смены через `.fileImporter`. Загрузка: `FileService.uploadFile(fileType: .userProfilePoster)` → `UserService.setProfilePoster(fileID:)` → `container.loadCurrentUser()` для немедленного обновления `ProfileHeaderSection` и `ProfileSidebarView`. Фрейм 3:1 держится через `Color.clear.aspectRatio(3, .fit).overlay { posterView }` — без этого `aspectRatio` применяется к view без intrinsic size и постер раздувается в свой исходный размер. Постер кешируется как отдельный тип `CachedFileType.poster` (свой каталог `posters/` в кеше, своя строка в Settings → Кеш) с downscale через `Nuke.ImageProcessors.Resize(1200×400, .aspectFill)`, чтобы LazyImage не держал в памяти full-resolution декодированную картинку.
2. **Закругление пузырей** — превью из 5 фейковых пузырей на `MessageBubbleShape` + слайдер 0…30 pt. Радиус читается из `PersonalizationSettings.bubbleCornerRadius` через `@Environment(DependencyContainer.self)` в `MessageBubbleView` и attachment-вьюхах (`ImageAttachmentView`, `VideoAttachmentView`, `GIFAttachmentView`). `@Observable` гарантирует rerender открытого `ConversationView` в реальном времени.
3. **Фон чата** — тогл размытия + слайдеры радиуса блюра (1…25) и затемнения (0…100%) + сетка 3×N с вертикальными ячейками 1:2 (`Color.clear.aspectRatio(0.5, .fit).overlay { CachedImageView }`) и ячейкой добавления. Long-press / context menu переключает delete-mode на ячейке. Применяется в `Conversation/Views/Components/ChatBackgroundView.swift` через `.background { ChatBackgroundView() }` модификатор у корневого ZStack `ConversationView` (а **не** как первый слот ZStack — так intrinsic size картинки не растягивает контейнер и не выталкивает плавающие шапку и поле ввода): `CachedImageView(.image)` с `.frame(maxWidth/Height: .infinity)` + `.blur(radius:opaque: true)` + `.clipped()` + dim-overlay цвета `Color(nsColor: .windowBackgroundColor)` с `.opacity(percent / 100)`.

Хранилище:
- Локальные настройки (radius, blur on/off, blur radius, dim, currentBackgroundFileID) — `App/Settings/PersonalizationSettings.swift` поверх `UserDefaults` (ключи `personalization.*`). **Очищаются при logout** через `PersonalizationSettings.reset()` (см. раздел [[Клиенты/macOS#Паттерн: logout — wipe всего, кроме адреса сервера]]) — на чистый аккаунт пользователь приходит с дефолтным радиусом пузыря, без фона.
- Постер и список фонов — синхронизируются с сервером через `Users.GetPersonalization` / `Users.UpdatePersonalization` / `Users.GetProfilePoster` / `Users.SetProfilePoster`. Реализация в `BFNetworking/Repositories/UsersRepository.swift` (DTO `PersonalizationInfo`) и `BFCore/Services/Implementations/UserService.swift`.

Контракт `UpdatePersonalization` затирает обе стороны (`profilePosterFileID` + `chatBackgroundFileIds`), поэтому `PersonalizationSettingsViewModel` хранит текущий `posterFileID` и при апдейте списка фонов передаёт его без изменений — иначе сервер обнулит постер. Фоны заливаются как `UploadFileType.messageAttachmentImage` (паритет с Android), постер — `UploadFileType.userProfilePoster`.

Постер перед заливкой проходит через `ImageCropperView` (см. раздел [[Клиенты/macOS#Кропер изображений — ImageCropperView]]): `fileImporter` → `NSImage(data:)` → `pendingPosterImage` + `showPosterCropper = true` → после `onCrop` — `uploadPoster(_ image: NSImage)` → `jpegData(0.85)` → `fileService.uploadFile(.userProfilePoster)`. Фоны кропом не идут.

## Кропер изображений — `ImageCropperView`

`DesignSystem/Components/ImageCropperView.swift` — общий SwiftUI-кропер для аватара (1:1, output 1024×1024) и постера (3:1, output 1500×500). Жесты — `MagnifyGesture` (pinch на трекпаде) + `DragGesture` (pan), двойной клик — toggle zoom. Минимальный масштаб подбирается так, что картинка всегда покрывает окно кропа по обеим осям, max — `minScale * 5`. Offset после каждого жеста клампится к `(imageSize * scale − cropSize) / 2`, поэтому край картинки не может выехать внутрь окна кропа.

Параметры: `image: NSImage`, `aspectRatio: CGFloat` (1 / 3), `outputWidth: CGFloat`, `onCancel`, `onCrop`. Презентация — `.sheet(isPresented:)`. Тулбар с «Отмена» / «Готово» (cancel/default action keyboard shortcuts).

На «Готово» считается `srcRect` в координатах logical NSImage points (`imgW/2 − offset.x/scale − cropW/(2*scale)`, аналогично по Y, `srcSize = cropSize / scale`), и через `NSImage.draw(in:from:operation:.copy)` рендерится в новый `NSImage` целевого размера с `imageInterpolation = .high`.

Точки вызова:
- `Features/Auth/Views/Steps/AvatarStepView.swift` — регистрация, 1:1.
- `Features/Profile/Views/ProfileEditView.swift` — настройки профиля, 1:1, биндится к `ProfileEditViewModel.showCropper`.
- `Features/Settings/Personalization/Components/PosterPreviewCard.swift` — персонализация, 3:1, биндится к `PersonalizationSettingsViewModel.showPosterCropper`.

Общий flow: `fileImporter` → `Data(contentsOf:)` → `NSImage(data:)` → cropper → `NSImage.jpegData(compressionQuality: 0.85)` → `fileService.uploadFile` → `setProfilePicture` / `setProfilePoster`.

`NSImage.jpegData(compressionQuality:)` живёт в `DesignSystem/Extensions/NSImage+JPEG.swift` (общий для всего таргета — переиспользуется и кропером, и `ConversationViewModel` при отправке изображений-вложений).

Ключевые файлы:
- UI: `Features/Settings/Personalization/PersonalizationSettingsView.swift` + `Components/{PosterPreviewCard,BubblePreviewView,BackgroundsGrid}.swift`.
- VM: `Features/Settings/Personalization/PersonalizationSettingsViewModel.swift` (`@MainActor @Observable`).
- Применение в чате: `Features/Conversation/Views/Components/ChatBackgroundView.swift`, плюс правки в `MessageBubbleView`/`Image|Video|GIFAttachmentView`.

### Приватность

Раздел `Settings → Приватность` повторяет реализацию Android-клиента ([[Android]] → `PrivacySettingsActivity`) и состоит из двух секций по 6 настроек:

1. **Профиль** — `Toggle` «Профиль на сайте» (`profileVisibleOnSite`), `Picker` «Видимость аватара/описания/почты» (`avatarVisibility`, `bioVisibility`, `emailVisibility`) с тремя значениями: «Все»/«Друзья»/«Никто».
2. **Поиск и онлайн** — `Toggle` «Видимость в поиске» (`searchVisible`), `Picker` «Видимость онлайна» (`onlineVisibility`).

Контракт — `Barkfluff_Users_PrivacySettings` ([[Shared/Proto]], `users_api.proto`), RPC `GetPrivacySettings`/`UpdatePrivacySettings`. На клиенте:

- DTO: `BFNetworking.PrivacySettingsInfo` + enum `ProfileFieldVisibility` (`.all/.friends/.none`).
- Repository: `UsersRepository.getPrivacySettings()` / `updatePrivacySettings(_:)` — стандартный паттерн `withAuthorizedClient(for: .users)` + nonisolated мапперы `mapPrivacySettings` / `mapVisibilityTo|FromProto`.
- Service: `BFCore.UserService.getPrivacySettings()` / `updatePrivacySettings(_:)` — простая делегация в репозиторий, без кеша.
- VM: `Features/Settings/ViewModels/PrivacySettingsViewModel.swift` (`@MainActor @Observable`). Загрузка в `.task`. Метод `update(_ mutate:)` применяет мутацию к локальной копии, шлёт `updatePrivacySettings`; **на ошибке откатывает локальную копию и показывает `errorMessage`** — UI всегда отражает реальное состояние сервера.
- UI: `Features/Settings/Views/PrivacySettingsView.swift`. Биндинги через приватные `boolBinding(_:)` / `visibilityBinding(_:)` с `WritableKeyPath` — `set` запускает `Task { await viewModel.update { ... } }`, поэтому каждое переключение Toggle/Picker автосохраняется без отдельной кнопки.

### Папки чатов

Полный паритет с [[Android]]-клиентом (контракт описан в [[Backend/Users-ChatFolders-ClientGuide]]). UI — горизонтальный ряд «чипов» над сайдбаром: первый таб «Все чаты», далее пользовательские папки с бейджем непрочитанных. Раздел `Settings → Папки чатов` управляет порядком (drag-drop) и создаёт/редактирует папки через sheet.

Shared-слой (общий с [[iOS]]):
- `BFCore.ChatFolder` — доменная модель (`id`, `name`, `icon`, `chatIDs`, `sortOrder`).
- `BFNetworking.ChatFoldersRepository` — gRPC-репозиторий, 7 методов (`getChatFolders`/`create`/`update`/`delete`/`addChatToFolder`/`removeChatFromFolder`/`reorder`).
- `BFCore.ChatFolderService` — stale-while-revalidate. Persist через `LocalChatFolderRepository` (GRDB, миграция `v5_chat_folders`).

Mac-специфика:
- `Features/Settings/Views/ChatFoldersListView.swift` (список с drag-drop), `EditChatFolderView.swift` (sheet — имя, эмодзи-grid, выбор чатов), `FolderChatPickerView.swift`.
- Категория `.chatFolders` в `Navigation/SettingsCategory.swift`, маршрутизация в `SettingsCategoryView`.
- `Features/ChatList/Views/FolderTabChip.swift` + `ChatFolderTabsBar` — горизонтальный ряд табов над `ChatListView`.
- `ChatListViewModel.folders/selectedFolderID/excludeFolderChatsFromAll` + `loadFolders()`, `selectFolder(_:)`, `unreadCount(for:)`. Метод `applyFilter()` учитывает выбранную папку и флаг исключения чатов папок из «Все чаты».
- Персонализация: два `@AppStorage` ключа `folders.compact` и `folders.excludeFromAll`. Тогглы в `PersonalizationSettingsView`.

## Локализация (RU / EN)

Полное двуязычное приложение, мгновенное переключение без перезапуска. Источник истины — `Barkfluff/Resources/Localizable.xcstrings` (sourceLanguage = `en`, локализации `ru` + `en`). На первом запуске берётся системная локаль, если не `ru` — fallback на `en`. Реализация общая с [[Клиенты/iOS]].

**Инфраструктура:**
- `App/Settings/LocalizationSettings.swift` — `@Observable @MainActor`, `enum AppLanguage { system, ru, en }`, computed `appliedLocale: Locale`, persist через UserDefaults (`localization.language`).
- `App/DI/DependencyContainer.swift` — `let localizationSettings: LocalizationSettings`, сбрасывается в `reset()`.
- `App/BarkfluffApp.swift` — `.environment(\.locale, container.localizationSettings.appliedLocale)` на **обеих сценах** (`WindowGroup` и `Settings`). Чтение computed из `body` делает корень реактивным к смене языка через `@Observable`.
- Команды (`CommandGroup(after: .sidebar)`) — кнопки `"commands.chats"` / `"commands.profile"` с `keyboardShortcut`.
- `Navigation/SettingsCategory.swift` — `case language` + `title: LocalizedStringKey`. Экран `Features/Settings/Views/LanguageSettingsView.swift` — `Picker` по `AppLanguage.allCases`.
- `Barkfluff.xcodeproj/project.pbxproj` — `knownRegions` содержит `ru` и `en`.

**Конвенция ключей:** `<area>.<screen>.<element>` snake_case через точку. iOS и macOS используют **одни и те же** ключи там, где экраны совпадают (`auth.login.*`, `conversation.*`, `chat_list.*`, `settings.*`). Mac-only screens (NSOpenPanel, viewers) — собственные ключи в том же неймспейсе.

**Типы строк:**
- `Text("key")` / `Button("key")` / `.help("key")` (Mac tooltip) / `.navigationTitle("key")` — `LocalizedStringKey` литерал.
- `LocalizedStringResource?` — для error/state-полей в ViewModel и валидаторах (`errorMessage: LocalizedStringResource?`). `PasswordStrength.label: LocalizedStringResource`.
- `enum.titleKey: LocalizedStringKey` — для перечислений (`AppTheme`, `NotificationMode`, `ProfileFieldVisibility`, `SettingsCategory`). `String(localized:)` из computed property **запрещён** — замораживает значение.
- `String(localized: "key")` — только для не-SwiftUI API (NSOpenPanel.message, имена файлов при сохранении).

**BFCore (общий пакет с iOS):**
- Каталог: `Packages/BFCore/Sources/BFCore/Resources/Localizable.xcstrings`, объявлен через `resources: [.process("Resources")]` в `Package.swift`. Внутри пакета — `String(localized: "key", bundle: .module, locale: …)`. `Bundle.module` **обязателен**, иначе runtime вернёт ключ.
- Locale-aware overloads везде, где enum/struct отдаёт user-facing строку:
  - `OnlineStatus.displayText(in: Locale)`, `displayText` (default `.current`)
  - `AttachmentType.displayName(in:)`, `previewText(fileName:in:)`
  - `ChatMemberRole.displayName(in:)`, `FastAuthStatus.displayName(in:)`, `DeviceType.displayName(in:)`, `SharedMediaFilter.displayName(in:)`
  - `BFError.errorDescription(in:)` / `recoverySuggestion(in:)`, `MediaCacheError.errorDescription(in:)`
  - `DateFormatterHelper.formatForChatList/formatForMessage/formatLastActive(_: locale:)` — внутри собирают локаль-специфичный `DateFormatter`. `formatForChatList`: сегодня → время (`16:27`), вчера/позавчера → слово+время (`Вчера 16:27`, ключи `bfcore.date.yesterday`, `bfcore.date.day_before_yesterday`), старше → короткая дата с двузначным годом через template `ddMMyy` (`23.05.26` / `5/23/26`)
  - `ErrorLocalizer.localize(_:locale:)` / `recoverySuggestion(for:locale:)`
- Plurals для last-seen (`bfcore.online.last_seen_minutes/_hours/_days %lld`) — native xcstrings plural variants. RU: one/few/many; EN: one/other.

**Реактивность во вьюхах:** компоненты инжектят `@Environment(\.locale) private var locale` и пробрасывают в API: `status.displayText(in: locale)`, `DateFormatterHelper.formatForChatList(date, locale: locale)`, `ReplyPreviewView.makeSnippet(message, locale: locale)`, `MessageGrouper.formatDateSeparator(_:locale:)`. Дефолт `.current` — для логов и notification center, где SwiftUI environment недоступен.

**Hardcoded `Locale(identifier: "ru_RU")` запрещены** — заменены на `@Environment(\.locale)` (`Conversation/Helpers/MessageGrouper.swift`, `Features/UserProfile/Views/ProfileInfoSection.swift`).

## Code Conventions

- Комментарии на русском
- Services с `Protocol` суффиксом (`AuthServiceProtocol`)
- ViewModels — `@Observable` классы
- Async/await для всех асинхронных операций

## Звонки

Аудио/видео, 1-на-1 и групповые. Сигнализация — gRPC ([[Backend/Calls]]) через `BFNetworking.CallsRepository` + `CallEventsStreamManager` (фоновый стрим `SubscribeCallEvents`; эндпоинт Calls обнаруживается через [[Backend/Beacon]] `GetServerInfoResponse.calls`). Медиа — **LiveKit Swift SDK** в пакете `BFCalls`.

- `BFCalls.CallController` (`@MainActor @Observable`) — state-машина (idle/outgoing/incoming/connecting/active/ended), LiveKit `Room`, медиа-контролы (mic/cam/screen/качество), плитки участников, резолвер имён через `UserService`. Синглтон-аксессор `DependencyContainer.callController`; старт/стоп из `RootView` по `.main`.
- Общие SwiftUI-вью (в `BFCalls`, кросс-платформенные): `IncomingCallView` (ринг), `CallScreenView` (экран+контролы+сетка участников+self-PiP+панель качества+таймер), `CallMinimizedBar`.
- Оверлей: `Features/Call/Views/CallOverlayView.swift` — **плавающий немодальный** поверх чата (клики мимо карточки проходят к чату), свёрнутый/развёрнутый, перетаскиваемый.
- Точки входа: кнопки аудио/видео в `ConversationHeaderView`.
- Разрешения: entitlements `com.apple.security.device.audio-input` + `…camera`, `NSMicrophone/NSCameraUsageDescription`.
  - ⚠️ **Критично:** нужен ещё `com.apple.security.network.server` — иначе App Sandbox блокирует UDP-приём WebRTC и `room.connect` падает с `Code=202 (subscriber transport .failed)` (сигналинг подключается, медиа — нет). На iOS этого нет.
  - Камера на macOS **не включается автоматически** при connect (авто-старт захвата роняет CMIO/WebRTC) — включается кнопкой внутри звонка.

## Сборка

```bash
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build
open Barkfluff.xcodeproj
```
