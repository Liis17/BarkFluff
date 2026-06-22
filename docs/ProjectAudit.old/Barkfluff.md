# Аудит проекта: Barkfluff (macOS клиент)

> **Путь проекта:** `Mac/Barkfluff/`  
> **Стек:** Swift, SwiftUI, gRPC (swift-grpc v2), Observation framework  
> **Дата аудита:** 2025  
> **Охват:** Весь Swift-код — App, Features, Navigation, Packages (BFNetworking, BFCore)

---

## Содержание

1. [🔴 Безопасность](#-безопасность)
2. [🟠 Производительность и оптимизация](#-производительность-и-оптимизация)
3. [🟡 Баги и недоработки](#-баги-и-недоработки)
4. [🔵 Прочее (Качество кода, архитектура)](#-прочее-качество-кода-архитектура)

---

## 🔴 Безопасность

---

### SEC-01: Токены хранятся в UserDefaults по умолчанию

**Проблема / описание**  
`UserDefaults` — рекомендуемый вариант по умолчанию для хранения токенов (`TokenStorageSettings`), и так описан в UI как *"рекомендуется"*. На macOS `UserDefaults` хранится в `~/Library/Preferences/*.plist` — незашифрованном файле, читаемом любым процессом с тем же UID. Access token и refresh token в открытом виде доступны любому приложению пользователя.

**Конкретно в чём проблема**  
Refresh token обеспечивает долгосрочный доступ к аккаунту. Его утечка = захват сессии. Хранение в `UserDefaults` не является защищённым хранилищем ни по стандарту macOS, ни по рекомендациям Apple.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/App/Models/TokenStorageSettings.swift` : 21–27, 33  
**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Auth/UserDefaultsTokenProvider.swift` : 1–60

```swift
// ❌ ПРОБЛЕМА: UserDefaults отображается как "рекомендуется"
var displayName: String {
    switch self {
    case .userDefaults:
        return "UserDefaults (рекомендуется)" // ← вводит пользователя в заблуждение
    case .keychain:
        return "Keychain"
    // ...
    }
}

// ❌ ПРОБЛЕМА: по умолчанию выбирается небезопасный вариант
static func initialStorageType() -> TokenStorageType {
    if let savedRaw = UserDefaults.standard.string(forKey: storageTypeKey),
       let saved = TokenStorageType(rawValue: savedRaw) {
        return saved
    }
    return .userDefaults  // ← дефолт небезопасен
}
```

**Варианты решения**

1. Изменить дефолт на `.keychain` (Keychain без iCloud — баланс между безопасностью и удобством).
2. Переименовать `.userDefaults` как *"Только RAM (небезопасно)"* и показывать предупреждение.
3. Полностью убрать `UserDefaults` как опцию для токенов — оставить только Keychain-варианты.

```swift
// ✅ РЕШЕНИЕ: дефолт — Keychain, UserDefaults помечен как небезопасный
var displayName: String {
    switch self {
    case .userDefaults:
        return "UserDefaults ⚠️ (небезопасно)"
    case .keychain:
        return "Keychain (рекомендуется)"
    case .keychainICloud:
        return "Keychain + iCloud"
    }
}

static func initialStorageType() -> TokenStorageType {
    if let savedRaw = UserDefaults.standard.string(forKey: storageTypeKey),
       let saved = TokenStorageType(rawValue: savedRaw) {
        return saved
    }
    return .keychain  // ✅ безопасный дефолт
}
```

---

### SEC-02: AuthInterceptor не обрабатывает 401 после истечения токена в рантайме

**Проблема / описание**  
`AuthInterceptor` выполняет *проактивное* обновление токена перед запросом, но **не перехватывает ответ сервера**. Если сервер вернул 401/Unauthenticated (например, токен отозван принудительно), ошибка просто пробрасывается наверх без попытки обновления и без вызова `onSessionExpired`.

**Конкретно в чём проблема**  

- Принудительный logout с другого устройства → текущая сессия продолжает работать (refresh token ещё валиден, но access уже отозван на уровне сервера).
- `onSessionExpired` вызывается только при падении `refreshAccessToken()`, то есть когда refresh token тоже невалиден. Промежуточное состояние (access отозван, refresh ещё нет) не обрабатывается.

**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Auth/AuthInterceptor.swift` : 43–72

```swift
// ❌ ПРОБЛЕМА: ответ сервера не анализируется
public func intercept<Input, Output>(...) async throws -> StreamingClientResponse<Output> {
    // ... подготовка токена ...

    // 3. Отправляем запрос — и всё. Если сервер вернул 401 — ошибка летит вверх
    // без попытки обновить токен или вызвать onSessionExpired
    return try await next(modifiedRequest, context)
    // ← нет catch для RPCError с кодом .unauthenticated
}
```

**Варианты решения**  
Перехватывать `RPCError` с кодом `.unauthenticated`, делать одну попытку обновления токена, и только при повторной неудаче вызывать `onSessionExpired`.

```swift
// ✅ РЕШЕНИЕ: retry-on-401 + вызов onSessionExpired
public func intercept<Input: Sendable, Output: Sendable>(
    request: StreamingClientRequest<Input>,
    context: ClientContext,
    next: (StreamingClientRequest<Input>, ClientContext) async throws -> StreamingClientResponse<Output>
) async throws -> StreamingClientResponse<Output> {
    var accessToken = await tokenProvider.currentAccessToken
    // ... проактивное обновление (существующий код) ...

    var metadata = request.metadata
    if let token = accessToken { metadata.addString(token, forKey: "x-auth-token") }
    let modifiedRequest = StreamingClientRequest<Input>(metadata: metadata, producer: request.producer)

    do {
        return try await next(modifiedRequest, context)
    } catch let error as RPCError where error.code == .unauthenticated {
        // ✅ Попытка обновить токен после 401
        do {
            let freshToken = try await refreshCoordinator.refreshAccessToken()
            var retryMetadata = request.metadata
            retryMetadata.addString(freshToken, forKey: "x-auth-token")
            let retryRequest = StreamingClientRequest<Input>(metadata: retryMetadata, producer: request.producer)
            return try await next(retryRequest, context)
        } catch {
            // ✅ Refresh тоже не вышел — сессия мертва
            onSessionExpired()
            throw error
        }
    }
}
```

---

### SEC-03: FastAuthViewModel не сохраняет токены при QR-авторизации

**Проблема / описание**  
После успешной QR-авторизации сервер возвращает `accessToken` в `FastAuthResult`, но ViewModel его не сохраняет — стоит `TODO`.

**Конкретно в чём проблема**  
QR-авторизация выглядит рабочей для пользователя (статус `.accepted`), но токены не сохраняются → при следующем запросе пользователь получает 401, сессия не восстанавливается. Фактически функция авторизации через QR сломана.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/FastAuth/ViewModels/FastAuthViewModel.swift` : 58–64

```swift
// ❌ ПРОБЛЕМА: TODO — токены не сохраняются
if result.status == .accepted, result.accessToken != nil {
    // Авторизация успешна — сохранить токены
    // TODO: Сохранить токены через AuthService  ← токены теряются!
}
```

**Варианты решения**  
Инжектировать `AuthServiceProtocol` (или `TokenProvider`) в `FastAuthViewModel` и сохранять токены при `accepted`.

```swift
// ✅ РЕШЕНИЕ: добавить authService в зависимости и сохранять токены
private let authService: AuthServiceProtocol

// ...в subscribeToResult():
if result.status == .accepted,
   let accessToken = result.accessToken,
   let refreshToken = result.refreshToken {
    // ✅ Сохраняем токены и переходим в .main
    try? await authService.saveTokens(
        accessToken: accessToken,
        refreshToken: refreshToken
    )
    await MainActor.run {
        coordinator.currentState = .main
    }
}
```

---

### SEC-04: print() с JWT токенами в production-коде

**Проблема / описание**  
В `AuthInterceptor` и `TokenRefreshCoordinator` используется `print()` для логирования, включая сообщения *"Token refreshed"*, *"No refresh token"*, *"Token refresh failed"*. Хотя сами значения токенов не печатаются, логи утекают в `Console.app` и могут быть прочитаны другими приложениями.

**Конкретно в чём проблема**  
Логи в `Console.app` на macOS доступны любому приложению без привилегий. Паттерн *"Token refreshed"* + таймстамп + статус может помочь атакующему понять lifecycle авторизации и таргетировать replay-атаки.

**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Auth/AuthInterceptor.swift` : 48–55  
**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Auth/TokenRefreshCoordinator.swift` : 30–68  
**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Repositories/UpdatesRepository.swift` : 36

```swift
// ❌ ПРОБЛЕМА: print() в auth-чувствительных местах
print("🔄 [AuthInterceptor] Token expiring soon or missing, refreshing...")
print("✅ [AuthInterceptor] Token refreshed proactively")
print("❌ [TokenRefreshCoordinator] No refresh token available")
// В UpdatesRepository:
print("📡 [UpdatesRepository] Received event - chatID: \(chatID), msgID: \(msg.id), text: \(msg.content.text.prefix(30))")
// ↑ Частичный текст сообщений — тоже нежелательно
```

**Варианты решения**  
Заменить `print()` на `os.Logger` с `.debug` уровнем (отключается в release-билдах через флаги).

```swift
// ✅ РЕШЕНИЕ: использовать os.Logger
import OSLog

private let logger = Logger(subsystem: "com.barkfluff", category: "Auth")

// Вместо print():
logger.debug("Token expiring soon, refreshing proactively")
logger.error("Token refresh failed: \(error.localizedDescription, privacy: .public)")

// Для UpdatesRepository — убрать текст сообщения из лога:
logger.debug("Received event chatID=\(chatID, privacy: .private) msgID=\(msg.id)")
```

---

### SEC-05: Миграция хранилища токенов не атомарна — окно уязвимости

**Проблема / описание**  
При переключении типа хранилища токенов в `SettingsViewModel.switchTokenStorage()`: данные экспортируются из старого провайдера, импортируются в новый, затем старый очищается. Между импортом и очисткой существует окно, где токены присутствуют в **обоих** хранилищах.

**Конкретно в чём проблема**  
Если приложение завершится (crash или force-quit) после `importAllData()` но до `clearAll()` — токены останутся в старом хранилище (UserDefaults). Это неожиданно для пользователя, который думает, что перенёс данные в Keychain.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Settings/ViewModels/SettingsViewModel.swift` : 120–155

```swift
// ❌ ПРОБЛЕМА: неатомарная миграция
let exportData = await currentProvider.exportAllData()    // шаг 1
try await newProvider.importAllData(exportData)            // шаг 2
// ← если crash здесь — данные в ОБОИХ хранилищах
await currentProvider.clearAll()                           // шаг 3
```

**Варианты решения**  
Использовать флаг в UserDefaults *"migration in progress"*: устанавливать перед import, сбрасывать после clearAll. При запуске проверять флаг и, если он установлен, повторно выполнять clearAll для старого хранилища.

```swift
// ✅ РЕШЕНИЕ: crash-safe миграция через флаг
private static let migrationPendingKey = "com.barkfluff.tokenMigration.pending"
private static let migrationSourceKey = "com.barkfluff.tokenMigration.source"

func switchTokenStorage(to newType: TokenStorageType) async {
    // 1. Сохраняем флаг "миграция началась" с указанием источника
    UserDefaults.standard.set(true, forKey: Self.migrationPendingKey)
    UserDefaults.standard.set(currentType.rawValue, forKey: Self.migrationSourceKey)

    let exportData = await currentProvider.exportAllData()
    try await newProvider.importAllData(exportData)

    // 2. Очищаем источник
    await currentProvider.clearAll()

    // 3. Сбрасываем флаг только после успешной очистки
    UserDefaults.standard.removeObject(forKey: Self.migrationPendingKey)
    UserDefaults.standard.removeObject(forKey: Self.migrationSourceKey)
}

// При старте приложения:
static func recoverFromInterruptedMigration() {
    guard UserDefaults.standard.bool(forKey: migrationPendingKey),
          let sourceRaw = UserDefaults.standard.string(forKey: migrationSourceKey),
          let sourceType = TokenStorageType(rawValue: sourceRaw) else { return }
    // Очищаем старое хранилище, если миграция была прервана
    let sourceProvider = makeProvider(for: sourceType)
    Task { await sourceProvider.clearAll() }
    UserDefaults.standard.removeObject(forKey: migrationPendingKey)
}
```

---

## 🟠 Производительность и оптимизация

---

### PERF-01: `listItems` в ConversationViewModel — вычисляется при каждом доступе

**Проблема / описание**  
`listItems` — вычисляемое свойство без кэширования: каждое обращение к нему вызывает `MessageGrouper.groupMessages()`, который итерирует весь массив `messages` и строит новый массив `[MessageListItem]`. SwiftUI обращается к этому свойству при каждой перерисовке.

**Конкретно в чём проблема**  
При большом числе сообщений (1000+) каждая перерисовка (скролл, курсор, изменение размера окна) вызывает полный перегруппировка. `MessageGrouper` создаёт `Calendar.current`, вызывает `isDate(_:inSameDayAs:)` для каждой пары и создаёт новые `MessageGroupInfo` — дорогостоящие операции.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift` : 80–82

```swift
// ❌ ПРОБЛЕМА: вычисляется каждый раз
var listItems: [MessageListItem] {
    MessageGrouper.groupMessages(messages, currentUserID: currentUserID)
    // ← нет кэша, вызов при каждом @Observable обновлении
}
```

**Варианты решения**  
Кэшировать результат, инвалидировать только при изменении `messages`.

```swift
// ✅ РЕШЕНИЕ: кэшированное свойство
private var _listItemsCache: [MessageListItem] = []
private var _messagesHashForCache: Int = 0

var listItems: [MessageListItem] {
    // Используем count + последний id как быстрый fingerprint
    let currentHash = messages.count &* 31 &+ Int(messages.last?.id ?? 0)
    if currentHash != _messagesHashForCache {
        _listItemsCache = MessageGrouper.groupMessages(messages, currentUserID: currentUserID)
        _messagesHashForCache = currentHash
    }
    return _listItemsCache
}
```

---

### PERF-02: `applyFilter()` в ChatListViewModel — сортировка при каждом событии

**Проблема / описание**  
`applyFilter()` вызывается при каждом входящем сообщении (`handleNewMessage`), каждом событии прочтения (`handleMessageRead`), каждом изменении `searchText`. Она выполняет `filter` + `sorted` на полном массиве чатов.

**Конкретно в чём проблема**  
При активной переписке в нескольких чатах одновременно каждое сообщение тригерит пересортировку всего списка на `MainActor`. При 100+ чатах это заметная работа на главном потоке, особенно при пагинации.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/ChatList/ViewModels/ChatListViewModel.swift` : 320–330

```swift
// ❌ ПРОБЛЕМА: полная пересортировка при каждом событии
private func applyFilter() {
    let filtered = searchText.isEmpty
        ? allChats
        : allChats.filter { $0.title.localizedCaseInsensitiveContains(searchText) }
    chats = filtered.sorted { lhs, rhs in    // ← O(n log n) на каждое сообщение
        let lhsDate = lhs.lastMessage?.sentAt ?? .distantPast
        let rhsDate = rhs.lastMessage?.sentAt ?? .distantPast
        return lhsDate > rhsDate
    }
}
```

**Варианты решения**  
При `handleNewMessage` делать *incremental update*: перемещать только изменённый чат в начало списка вместо полной пересортировки.

```swift
// ✅ РЕШЕНИЕ: incremental sort при обновлении одного чата
private func moveToTop(chatID: String) {
    guard let index = allChats.firstIndex(where: { $0.id == chatID }), index > 0 else { return }
    let chat = allChats.remove(at: index)
    allChats.insert(chat, at: 0)
}

private func handleNewMessage(_ event: BFCore.NewMessageEvent) {
    if let index = allChats.firstIndex(where: { $0.id == event.chatID }) {
        allChats[index].lastMessage = event.message
        if event.message.senderID != currentUserID && !isChatActive(event.chatID) {
            allChats[index].unreadCount += 1
        }
        moveToTop(chatID: event.chatID)    // ✅ O(n) вместо O(n log n)
        // applyFilter() вызываем только если нужна фильтрация
        if !searchText.isEmpty { applyFilter() } else { chats = allChats }
    } else {
        Task { await loadChats() }
    }
}
```

---

### PERF-03: `DateFormatter` создаётся внутри `formatDateSeparator` при каждом вызове

**Проблема / описание**  
`MessageGrouper.formatDateSeparator()` создаёт новый `DateFormatter` при каждом вызове. `DateFormatter` — один из самых дорогих объектов Foundation для создания.

**Конкретно в чём проблема**  
При группировке 500 сообщений с разными датами создаётся до 500 экземпляров `DateFormatter`. Это вызывает давление на аллокатор и дополнительные cycles на инициализацию locale.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Conversation/Helpers/MessageGrouper.swift` : 148–161

```swift
// ❌ ПРОБЛЕМА: новый DateFormatter при каждом вызове
static func formatDateSeparator(_ date: Date) -> String {
    // ...
    let formatter = DateFormatter()          // ← дорогое создание
    formatter.locale = Locale(identifier: "ru_RU")
    formatter.dateFormat = "d MMMM"
    return formatter.string(from: date)
}
```

**Варианты решения**  
Вынести `DateFormatter` в статическое свойство.

```swift
// ✅ РЕШЕНИЕ: статический DateFormatter (создаётся один раз)
private static let dateSeparatorFormatter: DateFormatter = {
    let formatter = DateFormatter()
    formatter.locale = Locale(identifier: "ru_RU")
    formatter.dateFormat = "d MMMM"
    return formatter
}()

static func formatDateSeparator(_ date: Date) -> String {
    let calendar = Calendar.current
    if calendar.isDateInToday(date) { return "Сегодня" }
    if calendar.isDateInYesterday(date) { return "Вчера" }
    return dateSeparatorFormatter.string(from: date)   // ✅ переиспользуем
}
```

---

### PERF-04: `FileDownloadHelper` загружает файл целиком в память (`Data`)

**Проблема / описание**  
`downloadToTemp()` использует `URLSession.shared.data(from:)`, которая загружает весь файл в `Data` (память) перед записью на диск.

**Конкретно в чём проблема**  
Для больших файлов (видео 500MB+) это означает удвоение памяти: файл сначала в RAM, затем записывается на диск. На macOS с ограниченной памятью возможна нехватка и OOM.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Conversation/Helpers/FileDownloadHelper.swift` : 79–93

```swift
// ❌ ПРОБЛЕМА: весь файл в память
let (data, _) = try await URLSession.shared.data(from: url)
// ↑ для видео 100MB — 100MB в heap
try data.write(to: destinationURL)
```

**Варианты решения**  
Использовать `URLSession.download(from:)` — скачивает напрямую во временный файл без буферизации в RAM.

```swift
// ✅ РЕШЕНИЕ: streaming download
let tempDir = FileManager.default.temporaryDirectory
    .appendingPathComponent("BarkfluffShare", isDirectory: true)
try FileManager.default.createDirectory(at: tempDir, withIntermediateDirectories: true)
let destinationURL = tempDir.appendingPathComponent(fileName)

// Используем download(from:) — файл не попадает в память
let (downloadedURL, _) = try await URLSession.shared.download(from: url)
try? FileManager.default.removeItem(at: destinationURL)
try FileManager.default.moveItem(at: downloadedURL, to: destinationURL)

return destinationURL
```

---

### PERF-05: `optimizeImage` использует `lockFocus/unlockFocus` (deprecated path)

**Проблема / описание**  
`ConversationViewModel.optimizeImage()` использует `NSImage.lockFocus()` / `unlockFocus()` для ресайза. Этот API давно устарел и работает через `NSGraphicsContext`, что медленнее чем `Core Image` или `ImageIO`.

**Конкретно в чём проблема**  
При отправке нескольких изображений подряд каждое проходит через `lockFocus` — синхронная операция на главном потоке (NSImage требует context). Это создаёт задержку UI при выборе медиа.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift` : 579–600

```swift
// ❌ ПРОБЛЕМА: устаревший NSGraphicsContext-based ресайз
let resizedImage = NSImage(size: newSize)
resizedImage.lockFocus()    // ← устаревший path
image.draw(in: NSRect(origin: .zero, size: newSize), ...)
resizedImage.unlockFocus()
return resizedImage.jpegData(compressionQuality: 0.85) ?? data
```

**Варианты решения**  
Использовать `ImageIO` / `CGImageDestination` или `Core Image` — они используют GPU-ускорение и не требуют main thread.

```swift
// ✅ РЕШЕНИЕ: Core Image-based ресайз (аппаратное ускорение)
private func optimizeImage(_ data: Data) -> Data? {
    guard let source = CGImageSourceCreateWithData(data as CFData, nil),
          let cgImage = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return nil }

    let maxDimension: CGFloat = 2048
    let width = CGFloat(cgImage.width)
    let height = CGFloat(cgImage.height)

    let scale = min(maxDimension / width, maxDimension / height, 1.0)  // не увеличиваем
    let newWidth = Int(width * scale)
    let newHeight = Int(height * scale)

    let outputData = NSMutableData()
    guard let destination = CGImageDestinationCreateWithData(
        outputData, UTType.jpeg.identifier as CFString, 1, nil
    ) else { return nil }

    let options: [CFString: Any] = [
        kCGImageDestinationLossyCompressionQuality: 0.85,
        kCGImageDestinationImageMaxPixelSize: max(newWidth, newHeight)
    ]
    CGImageDestinationAddImage(destination, cgImage, options as CFDictionary)
    guard CGImageDestinationFinalize(destination) else { return nil }

    return outputData as Data
}
```

---

### PERF-06: Симуляция прогресса загрузки через `Task.sleep` — неточная и ресурсоёмкая

**Проблема / описание**  
При загрузке вложений создаётся отдельный `Task` с циклом `Task.sleep(150ms)`, который периодически обновляет `uploadProgress` случайным значением. Это фиктивная симуляция — не отражает реальный прогресс загрузки.

**Конкретно в чём проблема**  

- Создаётся дополнительный Task на каждый файл, конкурирующий с Task загрузки.
- `Double.random` — недетерминированная анимация, прогресс может "прыгать".
- Реальная загрузка может завершиться быстрее симуляции → `progressTask.cancel()` с гонкой.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift` : 295–320

```swift
// ❌ ПРОБЛЕМА: фиктивная симуляция прогресса
let progressTask = Task { @MainActor [weak self] in
    guard let self else { return }
    var current = baseProgress * 0.9 + 0.02
    while !Task.isCancelled && current < targetProgress {
        try? await Task.sleep(for: .milliseconds(150))
        current += Double.random(in: 0.01...0.03)  // ← не реальный прогресс
        // ...
    }
}
let fileID = try await fileService.uploadFile(...)
progressTask.cancel()   // ← race condition
```

**Варианты решения**  
Передавать реальный `progressHandler` в `fileService.uploadFile()` через `URLSession` delegate или callback.

```swift
// ✅ РЕШЕНИЕ: реальный прогресс через callback
// В FileServiceProtocol:
func uploadFile(
    data: Data,
    fileName: String,
    fileType: FileType,
    progressHandler: ((Double) -> Void)?    // ← новый параметр
) async throws -> String

// В ConversationViewModel:
let fileID = try await fileService.uploadFile(
    data: data,
    fileName: attachment.displayName,
    fileType: attachment.uploadFileType,
    progressHandler: { [weak self] progress in
        guard let self else { return }
        let overall = (baseProgress + progress / totalAttachments)
        self.updatePendingMessage(localID: localID) { msg in
            msg.uploadProgress = overall * 0.9
        }
    }
)
```

---

## 🟡 Баги и недоработки

---

### BUG-01: `pendingIDCounter` — статическая переменная без защиты от concurrency

**Проблема / описание**  
`ConversationViewModel.pendingIDCounter` — `private static var`, изменяемая из `nextPendingID()`. Все экземпляры `ConversationViewModel` разделяют одну переменную. Поскольку ViewModel помечен `@Observable` и вызывается из `Task` на произвольных акторах, возможна гонка данных.

**Конкретно в чём проблема**  
Если пользователь открыл несколько окон чатов одновременно (macOS поддерживает multi-window), два потока могут читать и инкрементировать `pendingIDCounter` одновременно → дублирующиеся pending ID → неправильная дедупликация → сообщения не заменятся на confirmed.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift` : 43–49

```swift
// ❌ ПРОБЛЕМА: статическая переменная без синхронизации
private static var pendingIDCounter: Int64 = 0    // ← shared mutable state

private static func nextPendingID() -> Int64 {
    pendingIDCounter -= 1    // ← не атомарная операция
    return pendingIDCounter
}
```

**Варианты решения**  
Использовать `OSAllocatedUnfairLock` (iOS 16+ / macOS 13+) или перевести на инстанс-переменную (у каждого чата свой счётчик).

```swift
// ✅ РЕШЕНИЕ 1: инстанс-переменная (проще и правильнее)
private var pendingIDCounter: Int64 = 0

private func nextPendingID() -> Int64 {
    pendingIDCounter -= 1
    return pendingIDCounter
}

// ✅ РЕШЕНИЕ 2: атомарный счётчик если нужна статика
import os
private static let pendingIDLock = OSAllocatedUnfairLock(initialState: Int64(0))

private static func nextPendingID() -> Int64 {
    pendingIDLock.withLock { state in
        state -= 1
        return state
    }
}
```

---

### BUG-02: `SettingsViewModel` — `weak var dependencyContainer` может быть `nil` без обработки

**Проблема / описание**  
`SettingsViewModel.dependencyContainer` — `weak var`. `SettingsView` создаётся SwiftUI как отдельная сцена (`Settings { SettingsView() }`), которая живёт независимо от `WindowGroup`. При разных lifecycle сцен `DependencyContainer` (из `WindowGroup`) может быть освобождён раньше, чем `SettingsViewModel`.

**Конкретно в чём проблема**  
Методы `loadSessions()`, `terminateSession()`, `terminateAllOtherSessions()` при `dependencyContainer == nil` молча ничего не делают — список сессий остаётся пустым без объяснения причин пользователю.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Settings/ViewModels/SettingsViewModel.swift` : 53–70

```swift
// ❌ ПРОБЛЕМА: silent fail при nil
func loadSessions() async {
    guard let dc = dependencyContainer else { return }  // ← нет ошибки, нет лога
    // ...
}
```

**Варианты решения**  
Сделать зависимость обязательной (`strong`) — `DependencyContainer` — singleton на уровне приложения и не должен освобождаться. Либо показывать ошибку при `nil`.

```swift
// ✅ РЕШЕНИЕ: strong dependency
var dependencyContainer: DependencyContainer?  // → убрать weak

// Или — явная обработка:
func loadSessions() async {
    guard let dc = dependencyContainer else {
        errorMessage = "Конфигурация приложения недоступна"
        return
    }
    // ...
}
```

---

### BUG-03: `handleBioStep` в RegisterViewModel — ошибка сохранения игнорируется

**Проблема / описание**  
При неудаче сохранения bio во время регистрации возвращается `true` (продолжить). Это означает, что пользователь завершит регистрацию с пустым bio, даже если он его ввёл — без каких-либо уведомлений.

**Конкретно в чём проблема**  
Ошибка сети при сохранении bio → пользователь думает, что bio сохранено → на сервере bio пустое. Особенно неприятно, если требование заполнить bio — обязательное в будущих версиях.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Auth/ViewModels/RegisterViewModel.swift` : 248–260

```swift
// ❌ ПРОБЛЕМА: ошибка bio игнорируется
private func handleBioStep() async -> Bool {
    guard !data.bio.isEmpty else { return true }
    // ...
    do {
        try await userService.changeBio(newBio: data.bio)
        bioSaved = true
        return true
    } catch {
        // Не критично — можно продолжить   ← информация теряется
        return true
    }
}
```

**Варианты решения**  
Минимум — показывать нетоковое предупреждение. Если bio опционально, явно логировать ошибку.

```swift
// ✅ РЕШЕНИЕ: логировать и уведомить пользователя
private func handleBioStep() async -> Bool {
    guard !data.bio.isEmpty else { return true }
    isLoading = true
    defer { isLoading = false }

    do {
        try await userService.changeBio(newBio: data.bio)
        bioSaved = true
    } catch {
        // Bio опционально — продолжаем, но сообщаем
        logger.warning("Failed to save bio during registration: \(error)")
        errorMessage = "Не удалось сохранить bio, можно добавить позже в профиле"
        // Не блокируем регистрацию
    }
    return true
}
```

---

### BUG-04: `GroupChatViewModel.searchUsers()` — нет debounce

**Проблема / описание**  
`GroupChatViewModel` при вызове `searchUsers()` немедленно делает сетевой запрос без debounce. `ChatListViewModel` правильно использует `Task` с `Task.sleep(300ms)`, но `GroupChatViewModel` — нет.

**Конкретно в чём проблема**  
При вводе в поле поиска группового чата каждый символ вызывает сетевой запрос → избыточная нагрузка на `UserService.searchUsers` → возможные race conditions (более ранний ответ может прийти позже).

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/GroupChat/ViewModels/GroupChatViewModel.swift` : 38–56

```swift
// ❌ ПРОБЛЕМА: нет debounce, нет отмены предыдущего запроса
func searchUsers() async {
    guard !searchQuery.isEmpty else { searchResults = []; return }
    isLoading = true
    defer { isLoading = false }
    do {
        let result = try await userService.searchUsers(query: searchQuery, offset: 0, size: 20)
        searchResults = result.items    // ← может прийти старый результат после нового
    } catch { errorMessage = error.localizedDescription }
}
```

**Варианты решения**  
Добавить `searchTask: Task<Void, Never>?` и debounce аналогично `ChatListViewModel`.

```swift
// ✅ РЕШЕНИЕ: debounce + отмена предыдущей задачи
private var searchTask: Task<Void, Never>?

func onSearchQueryChanged() {
    searchTask?.cancel()
    guard searchQuery.count >= 2 else { searchResults = []; return }

    searchTask = Task { [weak self] in
        guard let self else { return }
        try? await Task.sleep(for: .milliseconds(300))
        guard !Task.isCancelled else { return }

        isLoading = true
        do {
            let result = try await userService.searchUsers(query: searchQuery, offset: 0, size: 20)
            guard !Task.isCancelled else { return }
            await MainActor.run { self.searchResults = result.items }
        } catch {
            guard !Task.isCancelled else { return }
            await MainActor.run { self.errorMessage = error.localizedDescription }
        }
        isLoading = false
    }
}
```

---

### BUG-05: `ProfileEditViewModel.logout()` — пустая реализация

**Проблема / описание**  
Метод `logout()` в `ProfileEditViewModel` полностью пуст — только комментарий *"Это будет обработано через AppCoordinator"*. Если кнопка "Выйти" в профиле вызывает этот метод — нажатие ничего не делает.

**Конкретно в чём проблема**  
Пользователь нажимает "Выйти" → ничего не происходит → аккаунт не сброшен, токены не очищены. Это не баг "приложение падает", но полный функциональный провал критической функции безопасности.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Profile/ViewModels/ProfileEditViewModel.swift` : 240–243

```swift
// ❌ ПРОБЛЕМА: метод не реализован
func logout() async {
    // Это будет обработано через AppCoordinator
}
```

**Варианты решения**  
Инжектировать `AppCoordinator` и вызывать полный сброс через `DependencyContainer.reset()`.

```swift
// ✅ РЕШЕНИЕ: реализовать logout
private let coordinator: AppCoordinator  // ← добавить в init

func logout() async {
    await container.reset()          // сброс кешей, токенов, стримов
    coordinator.currentState = .authentication
    coordinator.authScreen = .login
}
```

---

### BUG-06: `UpdatesStreamManager` — после `stop()` старые continuations не финализируются явно

**Проблема / описание**  
В `UpdatesStreamManager.start()` при перезапуске (после `stop()`) создаются новые `AsyncStream` и перезаписывают свойства `newMessages`, `messageReads`, `connectionEvents`. Старые `AsyncStream.Continuation` не вызывают `.finish()` перед заменой.

**Конкретно в чём проблема**  
Подписчики на старые потоки (например, `ChatListViewModel.newMessagesTask`) зависнут в `for await in stream` навсегда — задача не получит `nil` (конец потока) и не завершится. Это утечка Task.

**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Repositories/UpdatesRepository.swift` : 200–230

```swift
// ❌ ПРОБЛЕМА: старые continuations не закрываются
public func start() async {
    guard !isRunning else { return }
    isRunning = true

    // Пересоздаём потоки — но старые continuations не получают .finish()!
    var newMsgCont: AsyncStream<NewMessageEvent>.Continuation!
    newMessages = AsyncStream { continuation in newMsgCont = continuation }
    newMessagesContinuation = newMsgCont
    // ...
}
```

**Варианты решения**  
Вызывать `.finish()` на старых continuations перед заменой.

```swift
// ✅ РЕШЕНИЕ: финализировать старые потоки
public func start() async {
    guard !isRunning else { return }
    isRunning = true

    // ✅ Закрываем старые потоки — все подписчики получат конец последовательности
    newMessagesContinuation?.finish()
    messagesReadContinuation?.finish()
    connectionContinuation?.finish()

    var newMsgCont: AsyncStream<NewMessageEvent>.Continuation!
    newMessages = AsyncStream { continuation in newMsgCont = continuation }
    newMessagesContinuation = newMsgCont
    // ...
}
```

---

### BUG-07: Параллельная загрузка чатов при реконнекте — возможное дублирование

**Проблема / описание**  
`handleConnectionEvent(.reconnected)` запускает `Task { await loadChats() }`. Если реконнект происходит несколько раз подряд (нестабильная сеть), запускается несколько параллельных `loadChats()`. `loadChats()` проверяет `isLoading`, но первая итерация успевает сбросить флаг до завершения обработки событий.

**Конкретно в чём проблема**  
Два параллельных `loadChats()` могут записать в `allChats` разные версии данных (одна более свежая, другая старее из-за гонки). Результат — непредсказуемый порядок чатов или потерянные сообщения в `lastMessage`.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/ChatList/ViewModels/ChatListViewModel.swift` : 173–177

```swift
// ❌ ПРОБЛЕМА: параллельные reload при нескольких reconnect
private func handleConnectionEvent(_ event: BFCore.UpdatesConnectionEvent) {
    switch event {
    case .reconnected:
        Task { await loadChats() }  // ← может создаться несколько Task
    }
}
```

**Варианты решения**  
Хранить `reconnectTask` и отменять предыдущий перед запуском нового.

```swift
// ✅ РЕШЕНИЕ: отменяем предыдущий reload
private var reconnectTask: Task<Void, Never>?

private func handleConnectionEvent(_ event: BFCore.UpdatesConnectionEvent) {
    switch event {
    case .reconnected:
        reconnectTask?.cancel()
        reconnectTask = Task { [weak self] in
            guard let self, !Task.isCancelled else { return }
            await self.loadChats()
        }
    case .connectionLost:
        break
    }
}
```

---

## 🔵 Прочее (Качество кода, архитектура)

---

### MISC-01: `SettingsViewModel.storageInfo` и `serverInfo` — заглушки в production коде

**Проблема / описание**  
`StorageInfo` и `ServerInfo` в `SettingsViewModel.init()` инициализируются захардкоженными значениями: `usedGB: 2.5`, `limitGB: 10`, `version: "1.0.0"`. Это данные для превью, не реальные.

**Конкретно в чём проблема**  
Пользователь видит в настройках "Использовано 2.5 ГБ из 10 ГБ" — это фиктивные данные. Если сервер настроен с другими лимитами — информация вводит в заблуждение.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Settings/ViewModels/SettingsViewModel.swift` : 35–42

```swift
// ❌ ПРОБЛЕМА: захардкоженные данные
self.storageInfo = StorageInfo(usedGB: 2.5, limitGB: 10)  // ← фиктивные
self.serverInfo = ServerInfo(
    name: "BarkFluff Server",
    version: "1.0.0",          // ← не реальная версия сервера
    description: "Основной сервер BarkFluff"
)
```

**Варианты решения**  
Получать реальные данные из `ConnectionManager.serverInfo` и Users API.

```swift
// ✅ РЕШЕНИЕ: загружать из реальных источников
func loadSettings() async {
    isLoading = true
    await loadSessions()

    if let dc = dependencyContainer {
        // Информация о сервере из bootstrap
        if let info = await dc.connectionManager.serverInfo {
            serverInfo = ServerInfo(name: info.name, version: info.version, description: nil)
        }
        // Лимит хранилища из профиля пользователя
        if let user = dc.currentUser, let limitBytes = user.storageLimitBytes {
            let limitGB = Int(limitBytes / 1_073_741_824)
            storageInfo = StorageInfo(usedGB: 0, limitGB: limitGB)  // usedGB из отдельного API
        }
    }
    isLoading = false
}
```

---

### MISC-02: `DependencyContainer.settingsViewModel` — создаёт новый экземпляр при каждом обращении

**Проблема / описание**  
`settingsViewModel` — вычисляемое свойство, которое при каждом обращении создаёт новый `SettingsViewModel`. Если SwiftUI обращается к нему дважды (например, в `.environment` и в самом View) — создаётся два разных объекта.

**Конкретно в чём проблема**  
Каждый `SettingsViewModel` будет отдельно загружать сессии при появлении. Изменения в одном экземпляре не отразятся в другом. Это классическая проблема нестабильного identity объекта.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/App/DI/DependencyContainer.swift` : 66–71

```swift
// ❌ ПРОБЛЕМА: новый экземпляр при каждом обращении
var settingsViewModel: SettingsViewModel {
    let vm = SettingsViewModel()
    vm.dependencyContainer = self
    return vm    // ← каждый вызов — новый объект
}
```

**Варианты решения**  
Хранить `SettingsViewModel` как `lazy var` или `let`.

```swift
// ✅ РЕШЕНИЕ: lazy инициализация — один экземпляр
lazy var settingsViewModel: SettingsViewModel = {
    let vm = SettingsViewModel()
    vm.dependencyContainer = self
    return vm
}()
```

---

### MISC-03: `2FA enable/disable` в SettingsViewModel — не реализовано

**Проблема / описание**  
Методы `enable2FA()` и `disable2FA()` содержат только `// TODO`. При этом `SecuritySettingsView` вероятно показывает переключатель 2FA пользователю.

**Конкретно в чём проблема**  
Пользователь может взаимодействовать с UI 2FA без какого-либо эффекта — настройки не применяются. Это критично с точки зрения безопасности: пользователь думает, что включил 2FA, но она не активна.

**Путь к файлу:** `Mac/Barkfluff/Barkfluff/Features/Settings/ViewModels/SettingsViewModel.swift` : 94–101

```swift
// ❌ ПРОБЛЕМА: 2FA не реализована
func enable2FA() async {
    // TODO: Implement via IdentityService
}

func disable2FA() async {
    // TODO: Implement via IdentityService
}
```

**Варианты решения**  
До реализации — скрыть UI элемент 2FA или показывать `"В разработке"`. После реализации — вызывать `IdentityRepository` для включения/выключения.

```swift
// ✅ ВРЕМЕННОЕ РЕШЕНИЕ: заблокировать UI
// В SecuritySettingsView:
Toggle("Двухфакторная аутентификация", isOn: $viewModel.twoFactorEnabled)
    .disabled(true)  // ← до реализации
    .overlay(alignment: .trailing) {
        Text("Скоро").font(.caption).foregroundStyle(.secondary)
    }

// ✅ ПОСТОЯННОЕ РЕШЕНИЕ: реализовать через Identity API
func enable2FA() async {
    guard let dc = dependencyContainer else { return }
    do {
        try await dc.identityRepository.enableTwoFactor()
        twoFactorEnabled = true
    } catch {
        errorMessage = "Не удалось включить 2FA: \(error.localizedDescription)"
    }
}
```

---

### MISC-04: `RetryExecutor` создаётся как `actor`, но нигде не используется

**Проблема / описание**  
`RetryExecutor` и `RetryPolicy` — продуманная инфраструктура в `BFNetworking`, но нигде в репозиториях не применяется. Все репозитории делают прямые вызовы gRPC без retry-логики.

**Конкретно в чём проблема**  
Временные сетевые ошибки (packet loss, кратковременная недоступность) не обрабатываются: первый же `RPCError` летит в UI как ошибка пользователя. `RetryPolicy.streaming` и `RetryPolicy.standard` объявлены, но не применены.

**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Utilities/RetryPolicy.swift` : 1–115  
**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Repositories/MessagesRepository.swift` : 28–50

```swift
// ❌ ПРОБЛЕМА: RetryExecutor создан, но не используется
// MessagesRepository:
return try await connectionManager.withAuthorizedClient(for: .messages) { client in
    let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
    return try await messagesClient.listChats(req)   // ← нет retry при временной ошибке
}
```

**Варианты решения**  
Обернуть критичные запросы в `RetryExecutor.execute()` с фильтром по типу ошибки.

```swift
// ✅ РЕШЕНИЕ: применить RetryExecutor для unary запросов
private let retryExecutor = RetryExecutor(policy: .standard)

public func listChats(offset: Int32, size: Int32) async throws -> (chats: [ChatInfo], totalCount: Int32) {
    // ...
    return try await retryExecutor.execute(
        operation: {
            try await self.connectionManager.withAuthorizedClient(for: .messages) { client in
                let messagesClient = Barkfluff_Messages_MessagesApi.Client(wrapping: client)
                let response = try await messagesClient.listChats(req)
                // ... маппинг ...
                return (chats: ..., totalCount: ...)
            }
        },
        shouldRetry: { error in
            // Не повторяем при auth-ошибках и бизнес-ошибках
            if let rpcError = error as? RPCError {
                return rpcError.code == .unavailable || rpcError.code == .deadlineExceeded
            }
            return false
        }
    )
}
```

---

### MISC-05: `ConnectionManager.bootstrap` — plaintext имеет приоритет перед TLS

**Проблема / описание**  
`bootstrap()` сначала пробует plaintext-соединение и только при его неудаче переключается на TLS. Это означает, что при подключении к серверу, поддерживающему оба протокола, будет выбран **plaintext**.

**Конкретно в чём проблема**  
Для production-серверов, принимающих и plaintext и TLS, клиент всегда подключится по незащищённому каналу. Трафик (включая токены в метаданных gRPC, сообщения) передаётся в открытом виде.

**Путь к файлу:** `Mac/Barkfluff/Packages/BFNetworking/Sources/BFNetworking/Connection/ConnectionManager.swift` : 84–120

```swift
// ❌ ПРОБЛЕМА: plaintext имеет приоритет
do {
    // Сначала пробуем plaintext ← небезопасно для production
    let transport = try HTTP2ClientTransport.Posix(
        target: .dns(host: host, port: port),
        transportSecurity: .plaintext
    )
    // ...если успех — работаем по plaintext
} catch {
    // Только если plaintext упал — пробуем TLS
}
```

**Варианты решения**  
Инвертировать приоритет: сначала TLS, при неудаче — plaintext (для dev-окружений).

```swift
// ✅ РЕШЕНИЕ: TLS-first с fallback на plaintext
do {
    // Сначала пробуем TLS — безопасно по умолчанию
    let transport = try HTTP2ClientTransport.Posix(
        target: .dns(host: host, port: port),
        transportSecurity: .tls()
    )
    response = try await withGRPCClient(transport: transport, interceptors: interceptors) { ... }
} catch {
    // Fallback на plaintext — только для dev / локальных серверов
    #if DEBUG
    let plaintextTransport = try HTTP2ClientTransport.Posix(
        target: .dns(host: host, port: port),
        transportSecurity: .plaintext
    )
    response = try await withGRPCClient(transport: plaintextTransport, interceptors: interceptors) { ... }
    #else
    throw ConnectionError.connectionFailed("TLS required in production: \(error)")
    #endif
}
```

---

*Документ сгенерирован в результате полного статического анализа кодовой базы проекта `Mac/Barkfluff`.*  
*Для вопросов по конкретным проблемам см. указанные пути к файлам и диапазоны строк.*
