# BarkFluff macOS

SwiftUI приложение для macOS. gRPC через grpc-swift.

> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)
> Карта всех файлов проекта: [[Клиенты/macOS-ProjectMap]]

Расположение: `Mac/Barkfluff/`
Swift 6.2, platforms: macOS 26, iOS 26.

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
- Персистентный кеш (GRDB SQLite): `Database` + миграции (`Database/Migrations.swift`), записи `CachedChatRecord`, `CachedMessageRecord`, `CachedFileRecord`
- Локальные репозитории: `LocalChatRepository`, `LocalMessageRepository` — чтение/запись персистентного кеша из сервисов
- Кеш медиа на диске: `MediaCacheManager`, типизация — `CachedFileType`, статистика — `CacheStats`, парсинг ключей S3 — `S3URLParser`
- `TokenRefreshCoordinator` — автоматический рефреш через `AuthInterceptor`

## Паттерн: logout — полный wipe

Logout строится по «server-first, fail-loud» схеме: если серверный шаг упал, локальные данные **не трогаются**, чтобы пользователь мог повторить или сознательно выйти принудительно.

**Цепочка:**

1. `AppCoordinator.logout(container:) async throws` — вызывает `AuthService.logout()`. На успехе — `performLocalWipe(container:)`.
2. `AuthService.logout() async throws`:
   - `IdentityRepository.logout()` → gRPC `Identity.Logout` (на бэке `LogoutCommandHandler` удаляет refresh-токены устройства, публикует `SessionRevokedEvent` → инвалидируется access-токен по сервисам через шину, и зовёт `Users.DeleteUserDevice` для удаления записи устройства).
   - При ошибке — пробрасывается наружу, токены **не** очищаются.
   - При успехе — `tokenProvider.purgeAll()` (полная очистка, включая `device_id` и `server_host/port`).
3. `DependencyContainer.reset()` (вызывается из `performLocalWipe`):
   - `updatesService.stop()`, `onlineStatusService.stop()` — закрывают gRPC-стримы и AsyncStream-ы.
   - `userCache`/`chatCache`/`onlineStatusCache.removeAll()` — in-memory кеши.
   - `mediaCacheManager.clearAll()` — файловый медиа-кеш с диска.
   - `fileURLCache.clear()` — runtime + UserDefaults-домен `com.barkfluff.fileURLCache`.
   - `database.truncateAll()` — SQLite (`cached_message`, `cached_chat`, `cached_file`).
   - `tokenProvider.purgeAll()` — токены, даты истечения, `server_host`/`server_port`, `device_id`.
   - `serverDiscoveryService.disconnect()` + `connectionManager.shutdown()` — gRPC-эндпоинты и `ServerInfo`.
4. UI-state в `AppCoordinator` сбрасывается, `currentState = .serverSelection` (не `.authentication` — после полного wipe нужен новый bootstrap через Beacon).

**Force-вариант** (`AppCoordinator.forceLogout(container:)`): пропускает серверный шаг, локальный wipe идентичный. Используется UI-фолбэком при ошибке `logout()`. Сессия на сервере при этом остаётся «висящей» до истечения срока действия — её можно будет завершить позже через «Активные сессии» (`Identity.RemoveActiveSession`).

**UI-обработка ошибки**: `ProfileEditView` ловит throw из `coordinator.logout(container:)` и показывает алерт «Не удалось разлогиниться» с тремя кнопками — «Повторить» / «Выйти всё равно» (→ `forceLogout`) / «Отмена».

**TokenProvider.clearAll() vs purgeAll()**:
- `clearAll()` — стирает только токены и их даты (используется в `tryRestoreSession` при провале refresh, чтобы юзер потерял сессию, но сохранил `device_id` и адрес сервера для повторного логина).
- `purgeAll()` — стирает всё, включая `device_id` и эндпоинт сервера. Используется только при logout/reset.

**Сохраняется** после полного wipe: настройка `com.barkfluff.tokenStorage.type` (выбор хранилища токенов: UserDefaults / Keychain / Keychain+iCloud) — это user preference, не account data.

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

## Пересылка и ответ на сообщение

Контекстное меню на пузыре (`MessageBubbleView.contextMenu`) для подтверждённых сообщений (`message.id > 0`, не failed, не system) показывает «Ответить» и «Переслать». UX повторяет веб- и Android-клиентов (Telegram-style).

- **Ответить** — синхронно `viewModel.setPendingReply(message)`. Превью оригинала (`ReplyPreviewView`: акцентная полоса + имя автора + первая строка текста или emoji-сводка вложения + кнопка ×) появляется над `MessageInputView`. Пользователь набирает свой текст, и при отправке через `sendMessage` / `sendMessageWithAttachments` `pendingReply` снапшотится, превью очищается, в pending-сообщение добавляется placeholder через `makeForwardedPlaceholder`, а на сервер уходит `forwardedMessageID = pendingReply.id` вместе с текстом и `fileIDs`. Серверный ответ заменяет pending. Esc и кнопка × очищают `pendingReply`.
- **Переслать** — `MessageBubbleView` в callback `onForward` отдаёт `message.forwardSourceID` (computed extension в BFCore: если у сообщения уже есть вложение `FORWARDED_MESSAGE` с положительным `originalMessageID` — возвращает его, иначе свой id). Это исключает «вложенные» пересылки. `ConversationView` ставит `coordinator.presentedSheet = .forwardMessage(messageID:, sourceChatID:)`, sheet монтируется в `MainSplitView` через `.sheet(item:)`. `ForwardChatPickerViewModel` (мультивыбор) держит `selectedChatIDs: Set<String>`, `commentText: String`, `forwardToSelected()` шлёт сообщения параллельно через `withTaskGroup`. `ForwardChatPickerView` отрисовывает только аватары + имена с круглым checkmark, без last-message-превью; внизу — capsule-`TextField` для опционального комментария + круглая кнопка отправки рядом. На успехе хотя бы одной отправки модалка закрывается.

Контракт с сервером (см. [[Backend/Messages]]):
- Клиент шлёт только `OutgoingMessage.forwardedMessageID` — backend сам формирует attachment типа `FORWARDED_MESSAGE` со снимком автора/текста/вложений оригинала.
- Доменная модель в BFCore — `MessageAttachment.forwarded: ForwardedMessagePayload?`. Отрисовка: `ForwardedMessageView` (вертикальная цветная полоса слева, имя автора, текст, мини-список вложений) + `ReplyPreviewView` (компактная версия для превью над инпутом).

Регенерация proto: `make generate` в `Packages/BFProto/` (источник — `Mac/Barkfluff/Protos/`, синхронизируется с `Shared/BarkFluff.Proto/`).

## Настройки (категории-вкладки)

`SettingsView` — корневой контейнер с навигацией по `SettingsCategory` (enum в `Navigation/SettingsCategory.swift`). Каждая категория — отдельный View + при необходимости свой `@Observable` ViewModel:

| Категория       | View                          | ViewModel                          |
|-----------------|-------------------------------|------------------------------------|
| General         | `GeneralSettingsView`         | через `SettingsViewModel`          |
| Notifications   | `NotificationsSettingsView`   | —                                  |
| Cache           | `CacheSettingsView` + `CacheStackedBarView` | `CacheSettingsViewModel` |
| Cloud           | `CloudSettingsView` + `CloudStackedBarView` | `CloudSettingsViewModel` |
| Security        | `SecuritySettingsView`        | через `SettingsViewModel`          |
| Sessions        | `SessionsView`                | через `SettingsViewModel`          |
| About App       | `AboutAppSettingsView`        | —                                  |
| About Server    | `AboutServerSettingsView`     | `AboutServerSettingsViewModel`     |

`SettingsCategoryView` — общий компонент-обёртка раздела (заголовок, отступы, glass-стиль). `SettingsPlaceholderView` — для пустых/в разработке разделов.

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

## Code Conventions

- Комментарии на русском
- Services с `Protocol` суффиксом (`AuthServiceProtocol`)
- ViewModels — `@Observable` классы
- Async/await для всех асинхронных операций

## Сборка

```bash
xcodebuild -project Barkfluff.xcodeproj -scheme Barkfluff -configuration Debug build
open Barkfluff.xcodeproj
```
