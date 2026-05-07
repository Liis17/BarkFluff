//
//  NotificationService.swift
//  Barkfluff
//
//  Подписка на real-time стрим новых сообщений и постинг системных уведомлений
//  через UNUserNotificationCenter. Работает параллельно с ChatListViewModel —
//  использует независимую подписку через UpdatesService.getNewMessagesStream().
//

import Foundation
import AppKit
import UserNotifications
import Observation
import BFCore

@Observable
final class NotificationService {

    // MARK: - Dependencies

    @ObservationIgnored private let updatesService: UpdatesServiceProtocol
    @ObservationIgnored private let userService: UserServiceProtocol
    @ObservationIgnored private let chatService: ChatServiceProtocol
    @ObservationIgnored private let userCache: UserCache
    @ObservationIgnored private let chatCache: ChatCache
    @ObservationIgnored private let mediaCacheManager: MediaCacheManager
    @ObservationIgnored private let appFocusState: AppFocusState

    let settings: NotificationSettings

    @ObservationIgnored weak var coordinator: AppCoordinator?

    @ObservationIgnored private var subscriptionTask: Task<Void, Never>?
    @ObservationIgnored private var currentUserID: Int64 = 0
    @ObservationIgnored private var isStarted: Bool = false

    // MARK: - Init

    init(
        updatesService: UpdatesServiceProtocol,
        userService: UserServiceProtocol,
        chatService: ChatServiceProtocol,
        userCache: UserCache,
        chatCache: ChatCache,
        mediaCacheManager: MediaCacheManager,
        appFocusState: AppFocusState,
        settings: NotificationSettings
    ) {
        self.updatesService = updatesService
        self.userService = userService
        self.chatService = chatService
        self.userCache = userCache
        self.chatCache = chatCache
        self.mediaCacheManager = mediaCacheManager
        self.appFocusState = appFocusState
        self.settings = settings
    }

    // MARK: - Lifecycle

    /// Запросить разрешения и установить делегата для UNUserNotificationCenter.
    /// Вызывать один раз при старте приложения. Безопасно вызывать повторно —
    /// authorization-запрос идемпотентен.
    @MainActor
    func setupNotificationCenter(delegate: NotificationDelegate) async {
        let center = UNUserNotificationCenter.current()
        center.delegate = delegate
        do {
            _ = try await center.requestAuthorization(options: [.alert, .sound, .badge])
        } catch {
            // Пользователь мог отказать — это ОК, постинг просто будет проваливаться.
        }
    }

    /// Подписаться на стрим обновлений и начать показывать уведомления.
    /// Вызвать после успешного логина (когда currentUserID известен).
    func start(coordinator: AppCoordinator, currentUserID: Int64) async {
        guard !isStarted else { return }
        isStarted = true
        self.coordinator = coordinator
        self.currentUserID = currentUserID

        subscriptionTask = Task { [weak self] in
            guard let self else { return }
            let stream = await self.updatesService.getNewMessagesStream()
            for await event in stream {
                await self.handle(event)
            }
        }
    }

    /// Отписаться. Вызвать на logout.
    func stop() async {
        isStarted = false
        subscriptionTask?.cancel()
        subscriptionTask = nil
        currentUserID = 0
        coordinator = nil
        // Заодно стираем все уже показанные баннеры — они потеряли смысл после logout.
        UNUserNotificationCenter.current().removeAllDeliveredNotifications()
    }

    /// Снять баннеры конкретного чата (вызывается при открытии чата).
    func clearDelivered(for chatID: String) async {
        let center = UNUserNotificationCenter.current()
        let delivered = await center.deliveredNotifications()
        let toRemove = delivered
            .filter { ($0.request.content.userInfo["chatID"] as? String) == chatID }
            .map(\.request.identifier)
        if !toRemove.isEmpty {
            center.removeDeliveredNotifications(withIdentifiers: toRemove)
        }
    }

    /// Снять вообще все наши баннеры из Notification Center.
    /// Вызывается при выключении уведомлений в настройках — чтобы старые
    /// баннеры не висели в центре уведомлений после того, как фича отключена.
    func removeAllDelivered() {
        UNUserNotificationCenter.current().removeAllDeliveredNotifications()
    }

    // MARK: - Event handling

    private func handle(_ event: NewMessageEvent) async {
        // 1. Базовые отсечки до тяжёлых операций.
        if event.message.isSystem { return }
        if event.message.senderID == currentUserID { return }
        if !settings.showNotifications { return }

        // 2. Проверяем UI-состояние (нужен MainActor): главный «телеграмный»
        //    критерий — НЕ показываем, если открыт именно этот чат И приложение в фокусе.
        let context = await MainActor.run { [weak self] () -> NotificationContext in
            guard let self else {
                return NotificationContext(selectedChatID: nil, isAppActive: false, isInMain: false)
            }
            return NotificationContext(
                selectedChatID: self.coordinator?.selectedChat?.id,
                isAppActive: self.appFocusState.isActive,
                isInMain: self.coordinator?.currentState == .main
            )
        }

        guard context.isInMain else { return }
        if context.selectedChatID == event.chatID && context.isAppActive {
            return
        }

        // 3. Резолвим отправителя и чат для красивого title/avatar.
        let sender = await resolveSender(event.message.senderID)
        let chat = await resolveChat(event.chatID)

        // 4. Готовим аватар (опционально).
        let attachmentURL = await prepareAvatarFile(sender: sender, chat: chat)

        // 5. Собираем UNNotificationRequest и постим.
        let request = NotificationContentBuilder.build(
            event: event,
            sender: sender,
            chat: chat,
            attachmentFileURL: attachmentURL,
            playSound: settings.playSound
        )

        do {
            try await UNUserNotificationCenter.current().add(request)
        } catch {
            // Permission denied / системная ошибка — молча игнорируем,
            // нет смысла валить логику на каждое сообщение.
        }
    }

    // MARK: - Resolve sender / chat

    private func resolveSender(_ id: Int64) async -> User? {
        if let cached = await userCache.getUser(userID: id) {
            return cached
        }
        return try? await userService.getUser(userID: id)
    }

    private func resolveChat(_ id: String) async -> Chat? {
        if let cached = await chatCache.getChat(chatID: id) {
            return cached
        }
        // Сетевого «getChat by id» в ChatService нет, но обычно чат уже в кеше:
        // его кладёт ChatListViewModel при загрузке списка. Если нет — fallback на nil,
        // уведомление уйдёт без названия группы (для DM имя берётся из sender).
        return nil
    }

    // MARK: - Avatar pipeline

    /// Подготовить файл аватара для UNNotificationAttachment:
    /// - резолвнуть в MediaCacheManager (по необходимости — скачать)
    /// - скопировать в уникальный temp-файл (UNNotificationAttachment может «забрать» оригинал)
    private func prepareAvatarFile(sender: User?, chat: Chat?) async -> URL? {
        // Для группового чата с картинкой — её и используем; иначе fallback на аватар отправителя.
        let urlHint: String?
        if let chat, chat.isGroupChat, let pic = chat.pictureURL, !pic.isEmpty {
            urlHint = pic
        } else {
            urlHint = sender?.profilePicturePreviewURL ?? sender?.profilePictureURL
        }

        guard let urlHint, let fileID = S3URLParser.fileID(from: urlHint) else {
            return nil
        }

        let cachedURL: URL
        do {
            cachedURL = try await mediaCacheManager.resolveURL(
                for: fileID,
                type: .avatar,
                presignedURLHint: urlHint
            )
        } catch {
            return nil
        }

        // UNNotificationAttachment копирует файл в свою «attachment store», но требует
        // уникальный URL. Чтобы не задеть кеш — копируем в temp под новым именем.
        let ext = cachedURL.pathExtension.isEmpty ? "jpg" : cachedURL.pathExtension
        let tmp = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("notif-\(UUID().uuidString)")
            .appendingPathExtension(ext)

        do {
            try FileManager.default.copyItem(at: cachedURL, to: tmp)
            return tmp
        } catch {
            return nil
        }
    }
}

private struct NotificationContext: Sendable {
    let selectedChatID: String?
    let isAppActive: Bool
    let isInMain: Bool
}
