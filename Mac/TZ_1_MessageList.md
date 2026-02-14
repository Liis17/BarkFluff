# Техническое задание: Реализация списка сообщений

## Этап 1: Список сообщений (Дизайн, логика, скролл)

---

## 1. Обзор

### 1.1 Цель
Реализовать правую часть интерфейса мессенджера со списком сообщений в стиле **iMessage на macOS 26 (Liquid Glass)**. Включает:
- Заголовок чата с эффектом "жидкого стекла" (замыливание при скролле)
- Список сообщений с пузырьками (bubble) в стиле iMessage
- Логику загрузки, пагинации и real-time обновлений
- Плавный скролл и анимации

### 1.2 Референс дизайна
- iMessage на macOS 26 Sequoia
- Эффект Liquid Glass для заголовка
- Скругленные пузырьки сообщений
- Цвета: синний для исходящих, серый для входящих

---

## 2. КРИТИЧЕСКИЕ ЗАМЕЧАНИЯ ПО API

### 2.1 Доступные сервисы (из DependencyContainer)

```swift
// Доступно через @Environment(DependencyContainer.self)
let messageService: MessageService     // BFCore
let chatService: ChatService           // BFCore
let userService: UserService           // BFCore
let updatesService: UpdatesService     // BFCore
let fileService: FileService           // BFCore
let currentUserID: Int64               // ID текущего пользователя

// Кэши
let userCache: UserCache
let chatCache: ChatCache
```

### 2.2 MessageServiceProtocol - доступные методы

```swift
// Получение сообщений (двунаправленная пагинация)
func listMessages(
    chatID: String,
    fromMessageID: Int64,    // 0 для первого запроса
    offsetBefore: Int32,     // сколько сообщений ДО reference
    offsetAfter: Int32       // сколько сообщений ПОСЛЕ reference
) async throws -> [Message]

// Отправка сообщения
func sendMessage(
    chatID: String?,         // nil если отправляем по userID
    userID: Int64?,          // nil если отправляем по chatID
    text: String,
    fileIDs: [String]
) async throws -> Message

// Пометить как прочитанное
func markAsRead(messageIDs: [Int64]) async throws

// Подписка на новые сообщения (AsyncStream, НЕ AsyncThrowingStream!)
func subscribeToNewMessages() async throws -> AsyncThrowingStream<NewMessageEvent, Error>

// Подписка на события прочтения
func subscribeToReadEvents() async throws -> AsyncThrowingStream<MessageReadEvent, Error>
```

### 2.3 ChatServiceProtocol - ВАЖНО!

**ВНИМАНИЕ: НЕТ метода getChat(chatID:)!**

```swift
// Доступные методы:
func listChats(offset: Int32, size: Int32) async throws -> PagedResult<Chat>
func listChatMembers(chatID: String, offset: Int32, size: Int32) async throws -> PagedResult<DetailedChatMember>
func createGroupChat(userIDs: [Int64], title: String, pictureFileID: String?) async throws -> Chat
func kickUser(chatID: String, userID: Int64) async throws
```

**Решение:** Передавать объект `Chat` в ConversationView из ChatListView, либо кэшировать через `ChatCache`.

### 2.4 UpdatesServiceProtocol

```swift
func start() async
func stop() async
func getNewMessagesStream() async -> AsyncStream<NewMessageEvent>
func getReadEventsStream() async -> AsyncStream<MessageReadEvent>
func getConnectionEventsStream() async -> AsyncStream<UpdatesConnectionEvent>
func isActive() async -> Bool
```

**Важно:** `UpdatesService.start()` должен вызываться ОДИН РАЗ при запуске приложения (уже делается в AppCoordinator).

### 2.5 Модели данных

```swift
// Message (BFCore)
struct Message {
    let id: Int64
    let chatID: String
    let senderID: Int64
    let senderName: String?        // МОЖЕТ БЫТЬ nil!
    var content: MessageContent
    let sentAt: Date
    var readBy: [Int64]
    let isSystem: Bool
}

// MessageContent
struct MessageContent {
    var text: String
    var attachments: [MessageAttachment]
    var hasText: Bool
    var hasAttachments: Bool
}

// Chat (BFCore)
struct Chat {
    let id: String
    var title: String
    var pictureURL: String?
    var isGroupChat: Bool
    var lastMessage: Message?
    var unreadCount: Int64
    var members: [ChatMember]
}
```

### 2.6 Пагинация

```swift
// PaginationHelper (BFCore)
static let defaultChatsPageSize: Int32 = 30
static let defaultMessagesPageSize: Int32 = 50
static let defaultSearchPageSize: Int32 = 20

// PagedResult
struct PagedResult<T> {
    let items: [T]
    let totalCount: Int32
    let offset: Int32
    let size: Int32
    var hasMore: Bool
    var nextOffset: Int32
}
```

---

## 2. Архитектура

### 2.1 Структура файлов

```
Mac/Barkfluff/Barkfluff/Features/Conversation/
├── Views/
│   ├── ConversationView.swift           # Главный контейнер (переработать)
│   ├── ConversationHeaderView.swift     # NEW: Заголовок с Liquid Glass
│   ├── MessagesListView.swift           # NEW: ScrollView с сообщениями
│   ├── MessageBubbleView.swift          # Переработать под стиль iMessage
│   ├── MessageDateSeparatorView.swift   # NEW: Разделитель дат
│   ├── TypingIndicatorView.swift        # NEW: Индикатор набора текста
│   └── Components/
│       ├── BubbleShape.swift            # NEW: Форма пузырька iMessage
│       ├── MessageTimeView.swift        # NEW: Время + статус прочтения
│       └── ScrollToBottomButton.swift   # NEW: Кнопка скролла вниз
├── ViewModels/
│   └── ConversationViewModel.swift      # Переработать с полной логикой
└── Helpers/
    ├── MessageGrouper.swift             # NEW: Группировка сообщений по дате/отправителю
    └── ScrollPositionManager.swift      # NEW: Управление позицией скролла
```

### 2.2 Зависимости

Использовать существующие сервисы из `BFCore`:
- `MessageServiceProtocol` - загрузка/отправка сообщений
- `ChatServiceProtocol` - информация о чате
- `UserServiceProtocol` - информация о пользователях
- `UpdatesServiceProtocol` - real-time обновления

---

## 3. Компоненты (подробно)

### 3.1 ConversationView (переработка)

**Ответственность:** Главный контейнер, координация дочерних компонентов

**ВАЖНО:** Chat объект передаётся извне, т.к. ChatServiceProtocol не имеет метода getChat.

```swift
struct ConversationView: View {
    let chat: Chat  // Передаётся из ChatListView
    @Environment(DependencyContainer.self) private var container

    @State private var viewModel: ConversationViewModel?
    @State private var scrollOffset: CGFloat = 0

    var body: some View {
        VStack(spacing: 0) {
            // 1. Header с Liquid Glass эффектом
            ConversationHeaderView(
                chat: chat,
                scrollOffset: scrollOffset,
                onHeaderTap: { /* открыть профиль/инфо чата */ }
            )

            // 2. Список сообщений
            if let viewModel {
                MessagesListView(
                    messages: viewModel.groupedMessages,
                    currentUserID: container.currentUserID,
                    isLoadingMore: viewModel.isLoadingMore,
                    onLoadMore: { await viewModel.loadMoreMessages() },
                    onMessageAppear: { await viewModel.markAsRead($0) },
                    onScrollOffsetChange: { scrollOffset = $0 }
                )

                // 3. Поле ввода (будет в этапе 3)
                MessageInputView(chatID: chat.id)
            } else {
                ProgressView()
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .task {
            if viewModel == nil {
                let vm = ConversationViewModel(
                    chat: chat,
                    messageService: container.messageService,
                    userService: container.userService,
                    updatesService: container.updatesService,
                    currentUserID: container.currentUserID
                )
                viewModel = vm
                await vm.loadMessages()
                await vm.startListeningForUpdates()
            }
        }
        .onDisappear {
            viewModel?.stopListeningForUpdates()
        }
    }
}
```

**Ключевые изменения:**
- `chat: Chat` передаётся вместо `chatID: String`
- ViewModel создаётся с внедрением сервисов из container
- Подписка/отписка от updates в task/onDisappear

---

### 3.2 ConversationHeaderView (NEW)

**Ответственность:** Отображение информации о чате с эффектом Liquid Glass

**Дизайн-требования (iMessage style):**
- Фон: `.ultraThinMaterial` или кастомный материал
- При скролле вниз: blur усиливается, появляется разделитель
- Аватарка + имя чата
- Кликабельность: при нажатии открыть профиль/инфо о чате

**Структура:**

```swift
struct ConversationHeaderView: View {
    let chat: Chat?
    let scrollOffset: CGFloat
    let onHeaderTap: () -> Void

    // Вычисляемые свойства для эффекта
    private var blurOpacity: Double {
        min(1.0, max(0.3, scrollOffset / 50))
    }

    private var separatorOpacity: Double {
        min(1.0, max(0, (scrollOffset - 20) / 30))
    }

    var body: some View {
        Button(action: onHeaderTap) {
            HStack(spacing: 12) {
                // Аватар
                AvatarView(
                    imageURL: chat?.pictureURL,
                    initials: chat?.avatarInitials ?? "?",
                    size: 36
                )

                // Имя и статус
                VStack(alignment: .leading, spacing: 2) {
                    Text(chat?.title ?? "Чат")
                        .font(.headline)
                        .lineLimit(1)

                    // Для личных чатов - статус "в сети"
                    if let isOnline = chat?.members.first?.isOnline, !chat!.isGroupChat {
                        Text(isOnline ? "В сети" : "Не в сети")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                }

                Spacer()
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .background {
            // Liquid Glass эффект
            Rectangle()
                .fill(.ultraThinMaterial)
                .opacity(blurOpacity)
        }
        .overlay(alignment: .bottom) {
            // Разделитель при скролле
            Divider()
                .opacity(separatorOpacity)
        }
        .animation(.easeInOut(duration: 0.2), value: scrollOffset)
    }
}
```

**Требования к анимации:**
- Плавное появление blur при скролле
- Плавное появление разделителя
- Hover-эффект при наведении мыши

---

### 3.3 MessagesListView (NEW)

**Ответственность:** Отображение списка сообщений с пагинацией

**Ключевые функции:**
- LazyVStack для оптимизации производительности
- Обратная пагинация (загрузка старых сообщений при скролле вверх)
- Автоскролл к новому сообщению
- Группировка сообщений по дате и отправителю

**Структура:**

```swift
struct MessagesListView: View {
    let messages: [MessageGroup]  // Сгруппированные сообщения
    let currentUserID: Int64
    let onLoadMore: () async -> Void
    let onMessageAppear: (Int64) -> Void

    @Namespace private var bottomAnchor

    var body: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 2) {
                    // Индикатор загрузки старых сообщений
                    if isLoadingMore {
                        ProgressView()
                            .padding()
                    }

                    ForEach(messages) { group in
                        // Разделитель дат
                        if group.showDateSeparator {
                            MessageDateSeparatorView(date: group.date)
                        }

                        // Сообщения в группе
                        ForEach(group.messages) { message in
                            MessageBubbleView(
                                message: message,
                                isOwn: message.senderID == currentUserID,
                                showSenderName: group.showSenderName,
                                isGrouped: group.isGrouped
                            )
                            .id(message.id)
                            .onAppear {
                                onMessageAppear(message.id)
                                // Trigger load more for first visible
                                if message.id == messages.first?.messages.first?.id {
                                    Task { await onLoadMore() }
                                }
                            }
                        }
                    }

                    // Anchor для скролла вниз
                    EmptyView()
                        .id("bottom")
                }
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
            }
            .onChange(of: messages.last?.messages.last) { _, newMessage in
                if let newMessage {
                    withAnimation(.easeInOut(duration: 0.3)) {
                        proxy.scrollTo("bottom", anchor: .bottom)
                    }
                }
            }
        }
    }
}
```

---

### 3.4 MessageBubbleView (переработка)

**Ответственность:** Отображение одного сообщения в стиле iMessage

**Дизайн-требования (iMessage style):**
- Скругленные углы с "хвостиком" для крайних сообщений
- Цвета:
  - Исходящие: синий градиент (`LinearGradient` от `#007AFF` к `#5856D6`)
  - Входящие: серый/полупрозрачный (`.fill.secondary`)
- Максимальная ширина: ~70% от ширины контейнера
- Группировка: последовательные сообщения от одного отправителя без отступов
- Тени: мягкая тень для исходящих

**Структура:**

```swift
struct MessageBubbleView: View {
    let message: Message
    let isOwn: Bool
    let showSenderName: Bool
    let isGrouped: Bool  // true если сообщение в группе с предыдущим

    var body: some View {
        HStack(alignment: .bottom, spacing: 4) {
            if isOwn {
                Spacer(minLength: 50)
            }

            VStack(alignment: isOwn ? .trailing : .leading, spacing: 4) {
                // Имя отправителя (только для групповых чатов, не группированных)
                if showSenderName && !isOwn {
                    Text(message.senderName ?? "")
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .padding(.leading, 12)
                }

                // Пузырёк сообщения
                bubbleContent
                    .background {
                        BubbleShape(
                            isOwn: isOwn,
                            showTail: !isGrouped
                        )
                        .fill(bubbleFill)
                    }

                // Время и статус
                MessageTimeView(
                    time: message.sentAt,
                    isOwn: isOwn,
                    isRead: message.readBy.count > 1
                )
            }

            if !isOwn {
                Spacer(minLength: 50)
            }
        }
    }

    @ViewBuilder
    private var bubbleContent: some View {
        // Текст сообщения
        if !message.content.text.isEmpty {
            Text(message.content.text)
                .font(.body)
                .foregroundStyle(isOwn ? .white : .primary)
                .padding(.horizontal, 12)
                .padding(.vertical, 8)
        }

        // Вложения (будут реализованы в этапе 2)
        if !message.content.attachments.isEmpty {
            // TODO: Attachment preview
        }
    }

    private var bubbleFill: some ShapeStyle {
        if isOwn {
            return AnyShapeStyle(
                LinearGradient(
                    colors: [Color(hex: "007AFF"), Color(hex: "5856D6")],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
            )
        } else {
            return AnyShapeStyle(Color(nsColor: .controlBackgroundColor))
        }
    }
}
```

---

### 3.5 BubbleShape (NEW)

**Ответственность:** Кастомная форма пузырька в стиле iMessage

```swift
struct BubbleShape: Shape {
    let isOwn: Bool
    let showTail: Bool

    func path(in rect: CGRect) -> Path {
        let cornerRadius: CGFloat = 18
        let tailSize: CGFloat = 8
        let tailOffset: CGFloat = 4

        var path = Path()

        if isOwn {
            // Правая сторона - хвостик справа
            if showTail {
                // ... рисуем хвостик
            }
            // Скругленные углы
            path.addRoundedRect(
                in: rect.insetBy(dx: 0, dy: 0),
                cornerRadii: RectangleCornerRadii(
                    topLeading: cornerRadius,
                    bottomLeading: cornerRadius,
                    bottomTrailing: showTail ? cornerRadius - tailOffset : cornerRadius,
                    topTrailing: cornerRadius
                )
            )
        } else {
            // Левая сторона - хвостик слева
            // Аналогично, но зеркально
        }

        return path
    }
}
```

---

### 3.6 MessageDateSeparatorView (NEW)

**Ответственность:** Разделитель между датами

```swift
struct MessageDateSeparatorView: View {
    let date: Date

    var body: some View {
        Text(formatDate(date))
            .font(.caption)
            .foregroundStyle(.secondary)
            .padding(.vertical, 8)
            .frame(maxWidth: .infinity)
            .contentShape(Rectangle())
    }

    private func formatDate(_ date: Date) -> String {
        // "Сегодня", "Вчера", или дата
    }
}
```

---

## 4. ConversationViewModel (переработка)

### 4.1 Состояния

```swift
import BFCore

@Observable
final class ConversationViewModel {
    // MARK: - Dependencies
    private let messageService: MessageServiceProtocol
    private let userService: UserServiceProtocol
    private let updatesService: UpdatesServiceProtocol
    let currentUserID: Int64

    // MARK: - State
    let chat: Chat  // Передаётся извне
    var messages: [Message] = []
    var groupedMessages: [MessageGroup] = []

    var isLoading = false
    var isLoadingMore = false
    var errorMessage: String?

    var hasMoreMessages = true

    // MARK: - Subscriptions
    private var newMessagesTask: Task<Void, Never>?
    private var readEventsTask: Task<Void, Never>?

    // MARK: - Computed
    var chatID: String { chat.id }
    var chatTitle: String { chat.title }
    var isGroupChat: Bool { chat.isGroupChat }

    // MARK: - Init

    init(
        chat: Chat,
        messageService: MessageServiceProtocol,
        userService: UserServiceProtocol,
        updatesService: UpdatesServiceProtocol,
        currentUserID: Int64
    ) {
        self.chat = chat
        self.messageService = messageService
        self.userService = userService
        self.updatesService = updatesService
        self.currentUserID = currentUserID
    }
}
```

### 4.2 Методы

```swift
extension ConversationViewModel {

    // MARK: - Loading

    func loadMessages() async {
        isLoading = true
        errorMessage = nil

        do {
            messages = try await messageService.listMessages(
                chatID: chatID,
                fromMessageID: 0,
                offsetBefore: PaginationHelper.defaultMessagesPageSize,
                offsetAfter: 0
            )
            groupedMessages = MessageGrouper.group(messages, currentUserID: currentUserID)

            if messages.count < PaginationHelper.defaultMessagesPageSize {
                hasMoreMessages = false
            }
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    func loadMoreMessages() async {
        guard !isLoadingMore, hasMoreMessages, let oldestMessage = messages.first else { return }

        isLoadingMore = true

        do {
            let olderMessages = try await messageService.listMessages(
                chatID: chatID,
                fromMessageID: oldestMessage.id,
                offsetBefore: PaginationHelper.defaultMessagesPageSize,
                offsetAfter: 0
            )

            if olderMessages.isEmpty {
                hasMoreMessages = false
            } else {
                // Вставляем старые сообщения в начало
                messages.insert(contentsOf: olderMessages, at: 0)
                groupedMessages = MessageGrouper.group(messages, currentUserID: currentUserID)
            }
        } catch {
            // Silent failure for pagination
        }

        isLoadingMore = false
    }

    // MARK: - Real-time Updates

    func startListeningForUpdates() async {
        // UpdatesService уже запущен глобально в AppCoordinator
        // Подписываемся на стримы

        newMessagesTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getNewMessagesStream()
            for await event in stream {
                guard event.chatID == self.chatID else { continue }
                await MainActor.run {
                    self.handleNewMessage(event.message)
                }
            }
        }

        readEventsTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getReadEventsStream()
            for await event in stream {
                guard event.chatID == self.chatID else { continue }
                await MainActor.run {
                    self.handleMessageRead(event)
                }
            }
        }
    }

    func stopListeningForUpdates() {
        newMessagesTask?.cancel()
        readEventsTask?.cancel()
        newMessagesTask = nil
        readEventsTask = nil
    }

    // MARK: - Event Handlers

    private func handleNewMessage(_ message: Message) {
        messages.append(message)
        groupedMessages = MessageGrouper.group(messages, currentUserID: currentUserID)
    }

    private func handleMessageRead(_ event: MessageReadEvent) {
        guard let index = messages.firstIndex(where: { $0.id == event.messageID }) else { return }
        messages[index].readBy = event.readBy
        groupedMessages = MessageGrouper.group(messages, currentUserID: currentUserID)
    }

    // MARK: - Mark as Read

    func markAsRead(_ messageID: Int64) async {
        let message = messages.first(where: { $0.id == messageID })
        guard let message, !message.readBy.contains(currentUserID) else { return }

        do {
            try await messageService.markAsRead(messageIDs: [messageID])
        } catch {
            // Silent failure
        }
    }
}
```

### 4.3 Важно: Обработка senderName

```swift
// senderName может быть nil, нужно получать имя из других источников:
// 1. Для групповых чатов - загрузить пользователя через userService
// 2. Кэшировать имена отправителей при получении сообщений

extension ConversationViewModel {
    func getSenderName(for message: Message) async -> String {
        // Если имя уже есть
        if let name = message.senderName, !name.isEmpty {
            return name
        }

        // Если это текущий пользователь
        if message.senderID == currentUserID {
            return "Вы"
        }

        // Для личных чатов - имя собеседника
        if !chat.isGroupChat, let member = chat.members.first(where: { $0.userID != currentUserID }) {
            return member.displayName
        }

        // Загрузить пользователя
        do {
            let user = try await userService.getUser(userID: message.senderID)
            return "\(user.firstName) \(user.lastName)"
        } catch {
            return "Пользователь"
        }
    }
}
```

---

## 5. MessageGrouper (NEW)

**Ответственность:** Группировка сообщений для отображения

```swift
struct MessageGroup: Identifiable {
    let id: String
    let date: Date
    let messages: [Message]
    let showDateSeparator: Bool
    let showSenderName: Bool  // Для групповых чатов
    let isGrouped: Bool       // Сгруппировано с предыдущим
}

enum MessageGrouper {
    static func group(_ messages: [Message]) -> [MessageGroup] {
        // Логика группировки:
        // 1. Разделение по датам (показываем сепаратор)
        // 2. Группировка последовательных сообщений от одного отправителя
        // 3. Показ имени отправителя только для первого в группе
    }
}
```

**Правила группировки:**
- Сообщения от одного отправителя в течение 60 секунд группируются
- Разные дни = обязательный разделитель дат
- Групповой чат = показывать имя для первого сообщения в группе

---

## 6. ScrollPositionManager (NEW)

**Ответственность:** Управление позицией скролла

```swift
@Observable
class ScrollPositionManager {
    var offset: CGFloat = 0
    var isAtBottom: Bool = true
    var showScrollToBottom: Bool = false

    func update(offset: CGFloat, contentHeight: CGFloat, visibleHeight: CGFloat) {
        self.offset = offset

        // Показывать кнопку если не внизу
        let threshold: CGFloat = 100
        isAtBottom = offset >= contentHeight - visibleHeight - threshold
        showScrollToBottom = !isAtBottom
    }
}
```

---

## 7. Требования к производительности

### 7.1 Lazy Loading
- Использовать `LazyVStack` для виртуализации
- Не загружать изображения вложений до появления на экране

### 7.2 Мемоизация
- `MessageBubbleView` должен быть `Equatable` для избежания лишних перерисовок
- Кэшировать сгруппированные сообщения

### 7.3 Пагинация
- Загружать максимум 50 сообщений за раз
- Debounce для частых запросов пагинации

---

## 8. Accessibility

- VoiceOver: описывать сообщения с указанием отправителя и времени
- Динамический тип: поддерживать увеличение шрифта
- High Contrast: корректные цвета для режима высокой контрастности

---

## 9. Тестирование

### 9.1 Unit тесты
- `MessageGrouper` логика группировки
- `ConversationViewModel` состояние при загрузке/ошибке

### 9.2 UI тесты
- Скролл списка
- Пагинация
- Отображение различных типов сообщений

---

## 10. Критерии приёмки

- [ ] Список сообщений отображается в стиле iMessage
- [ ] Заголовок имеет эффект Liquid Glass с замыливанием при скролле
- [ ] Пузырьки сообщений имеют правильную форму и цвета
- [ ] Работает пагинация при скролле вверх
- [ ] Новые сообщения появляются в real-time
- [ ] Статусы прочтения обновляются корректно
- [ ] Плавные анимации скролла и появления
- [ ] Нет лагов при скролле большого количества сообщений

---

## 11. Связанные файлы

### Существующие (модифицировать)
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/ConversationView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessageBubbleView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/ViewModels/ConversationViewModel.swift`

### Новые (создать)
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/ConversationHeaderView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessagesListView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/MessageDateSeparatorView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Components/BubbleShape.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Components/MessageTimeView.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Views/Components/ScrollToBottomButton.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Helpers/MessageGrouper.swift`
- `Mac/Barkfluff/Barkfluff/Features/Conversation/Helpers/ScrollPositionManager.swift`

---

## 12. Примечания

- Файлы `MessageAttachmentView.swift` и `AttachmentPickerView.swift` не затрагиваются в этом этапе
- `MessageInputView.swift` будет переработан в этапе 3
- Использовать существующий `Theme.swift` для констант
- Использовать `AvatarView` из Design System

---

## 13. ВАЖНО: Изменения в MainSplitView и AppCoordinator

Требуется модификация для передачи Chat объекта вместо chatID:

### 13.1 AppCoordinator.swift - ДОБАВИТЬ

```swift
@Observable
final class AppCoordinator {
    // ... существующий код ...

    /// Выбранный чат для отображения в detail (ВМЕСТО selectedChatID)
    var selectedChat: Chat?  // NEW
}
```

### 13.2 MainSplitView.swift - ИЗМЕНИТЬ

```swift
struct MainSplitView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @State private var columnVisibility: NavigationSplitViewVisibility = .all

    var body: some View {
        NavigationSplitView(columnVisibility: $columnVisibility) {
            ChatListView()
                .navigationTitle("Чаты")
                .navigationSplitViewColumnWidth(min: 280, ideal: 320, max: 420)
        } detail: {
            // ИЗМЕНИТЬ: передавать chat вместо chatID
            if let chat = coordinator.selectedChat {
                ConversationView(chat: chat)
            } else {
                ContentUnavailableView(
                    "Выберите чат",
                    systemImage: "message",
                    description: Text("Выберите чат из списка слева")
                )
            }
        }
        .navigationSplitViewStyle(.balanced)
    }
}
```

### 13.3 ChatListView.swift - ИЗМЕНИТЬ привязку

```swift
List(selection: Binding(
    get: { coordinator.selectedChat },
    set: { coordinator.selectedChat = $0 }
)) {
    // ...
    ForEach(viewModel.chats) { chat in
        ChatRowView(chat: chat)
            .tag(chat)  // Вместо .tag(chat.id)
        // ...
    }
}
```
