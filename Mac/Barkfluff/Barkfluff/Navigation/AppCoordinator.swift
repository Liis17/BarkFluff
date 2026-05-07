//
//  AppCoordinator.swift
//  Barkfluff
//
//  Координатор навигации приложения
//

import SwiftUI
import Observation
import BFCore

/// Координатор состояния приложения.
/// Управляет навигацией между основными экранами.
@Observable
final class AppCoordinator {
    // MARK: - Sidebar Navigation

    /// Вкладки сайдбара
    enum SidebarTab: String, CaseIterable {
        case chats
        case profile
    }

    /// Активная вкладка сайдбара
    var activeTab: SidebarTab = .chats

    /// Выбранная категория настроек в профиле
    var selectedSettingsCategory: SettingsCategory?

    // MARK: - App State

    /// Состояние приложения
    enum AppState {
        case loading
        case serverSelection
        case authentication
        case main
    }

    /// Экран авторизации
    enum AuthScreen {
        case login
        case register
    }

    /// Текущее состояние
    var currentState: AppState = .loading

    /// Текущий экран авторизации
    var authScreen: AuthScreen = .login

    /// Выбранный чат для отображения в detail
    var selectedChat: Chat? {
        didSet {
            if oldValue?.id != selectedChat?.id {
                showProfilePanel = false
            }
        }
    }

    // MARK: - Profile Panel

    /// Показывать панель профиля
    var showProfilePanel: Bool = false

    /// Чат для отображения в панели профиля
    var profilePanelChat: Chat?

    // MARK: - Chat List Reference

    /// Weak ссылка на ChatListViewModel для уведомлений о прочтении
    weak var chatListViewModel: ChatListViewModel?

    /// Weak ссылка на NotificationService — нужна, чтобы при открытии чата
    /// снять уже показанные системные уведомления из этого чата.
    weak var notificationService: NotificationService?

    /// Тип модального окна
    enum SheetType: Identifiable {
        case createGroupChat
        case userSearch
        /// Переслать сообщение `messageID` из чата `sourceChatID` в выбранный пользователем чат
        case forwardMessage(messageID: Int64, sourceChatID: String)

        var id: String {
            switch self {
            case .createGroupChat: return "createGroupChat"
            case .userSearch: return "userSearch"
            case .forwardMessage(let id, _): return "forwardMessage_\(id)"
            }
        }
    }

    /// Активное модальное окно
    var presentedSheet: SheetType?

    /// Запуск приложения: auto-reconnect + auto-login
    func onAppLaunch(
        serverDiscovery: ServerDiscoveryServiceProtocol,
        authService: AuthServiceProtocol
    ) async {
        // 1. Попробовать переподключиться к последнему серверу
        let reconnected = await serverDiscovery.tryReconnect()
        guard reconnected else {
            currentState = .serverSelection
            return
        }

        // 2. Попробовать восстановить сессию (auto-login)
        let restored = await authService.tryRestoreSession()
        guard restored else {
            currentState = .authentication
            authScreen = .login
            return
        }

        // 3. Всё ОК — главный экран
        currentState = .main
    }

    /// Сессия истекла — вернуться на логин
    func handleSessionExpired() {
        currentState = .authentication
        authScreen = .login
    }

    /// Запустить прослушивание обновлений
    func startUpdates(updatesService: UpdatesServiceProtocol) async {
        await updatesService.start()
    }

    /// Остановить прослушивание обновлений
    func stopUpdates(updatesService: UpdatesServiceProtocol) async {
        await updatesService.stop()
    }

    // MARK: - Profile Panel

    /// Открыть/закрыть панель профиля для чата
    func toggleProfilePanel(for chat: Chat) {
        if profilePanelChat?.id == chat.id && showProfilePanel {
            // Закрыть панель если тот же чат
            closeProfilePanel()
        } else {
            // Открыть для нового чата
            profilePanelChat = chat
            showProfilePanel = true
        }
    }

    /// Закрыть панель профиля
    func closeProfilePanel() {
        showProfilePanel = false
        profilePanelChat = nil
    }

    /// Уведомить о прочтении чата (вызывается при открытии чата)
    func notifyChatOpened(_ chatID: String) {
        chatListViewModel?.markChatAsReadLocally(chatID: chatID)
        if let service = notificationService {
            Task { await service.clearDelivered(for: chatID) }
        }
    }

    /// Уведомить об отправке сообщения (вызывается для обновления lastMessage в списке чатов)
    func notifyMessageSent(chatID: String, message: BFCore.Message) {
        chatListViewModel?.updateLastMessage(chatID: chatID, message: message)
    }

    /// Выход из аккаунта.
    ///
    /// Сначала уведомляет сервер (`Identity.Logout`) — он удаляет refresh-токены и устройство из БД,
    /// инвалидирует access-токен через шину. Только при успехе серверного шага выполняется
    /// полный локальный wipe (кеши, БД, токены, device_id, эндпоинты) и переход на экран выбора сервера.
    ///
    /// Если серверный шаг падает — бросает ошибку. Локальные данные при этом остаются нетронутыми,
    /// чтобы UI мог предложить «Повторить» или вызвать `forceLogout(...)` для выхода без сервера.
    func logout(container: DependencyContainer) async throws {
        try await container.authService.logout()
        await performLocalWipe(container: container)
    }

    /// Принудительный выход без серверного шага. Локальные данные стираются полностью.
    /// На сервере при этом остаётся «висящая» сессия — она протухнет естественным образом,
    /// либо её можно будет завершить позже через «Активные сессии».
    func forceLogout(container: DependencyContainer) async {
        await container.authService.forceLocalLogout()
        await performLocalWipe(container: container)
    }

    private func performLocalWipe(container: DependencyContainer) async {
        await container.reset()

        selectedChat = nil
        activeTab = .chats
        selectedSettingsCategory = nil
        presentedSheet = nil
        showProfilePanel = false
        profilePanelChat = nil

        currentState = .serverSelection
        authScreen = .login
    }
}
