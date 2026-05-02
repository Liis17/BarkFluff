# BarkFluff macOS

SwiftUI приложение для macOS. gRPC через grpc-swift.

> UI/UX спецификация всех экранов и сценариев: [[Клиенты/DesignDocument]] (источник: `dd.md`)
> Карта всех файлов проекта: [[Клиенты/macOS-ProjectMap]]

Расположение: `Mac/Barkfluff/`
Swift 6.2, platforms: macOS 26, iOS 26.

## Зависимости

- `grpc-swift 2.0.0+`
- `swift-protobuf 1.28.0+`
- `KeychainAccess 4.2.2+` — токены
- `Nuke` — загрузка изображений

## Архитектура (MVVM + Coordinator)

### Локальные пакеты (`Packages/`)

```
BFProto/       # Proto definitions и gRPC код
BFNetworking/  # gRPC клиент, соединения, репозитории
BFCore/        # Бизнес-логика: сервисы, модели, кеш
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
- Caches: `UserCache`, `ChatCache` (in-memory)
- `TokenRefreshCoordinator` — автоматический рефреш через `AuthInterceptor`

## Паттерн: logout + повторный вход

`AuthService.logout()` **не вызывает** `connectionManager.shutdown()` — эндпоинты сервисов не привязаны к пользователю и остаются валидными для повторного входа на тот же сервер. Шатдаун нужен только при смене сервера (через `ServerDiscoveryService.disconnect()`).

`UpdatesStreamManager.start()` **пересоздаёт AsyncStream-ы** при каждом запуске: после `stop()` continuations завершаются (`.finish()`), и старые потоки мертвы — без пересоздания `forwardNewMessagesTask` в `UpdatesService` сразу завершался бы без событий.

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
