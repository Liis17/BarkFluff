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

    /// Тип модального окна
    enum SheetType: Identifiable {
        case createGroupChat
        case userSearch

        var id: String {
            switch self {
            case .createGroupChat: return "createGroupChat"
            case .userSearch: return "userSearch"
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
    }

    /// Уведомить об отправке сообщения (вызывается для обновления lastMessage в списке чатов)
    func notifyMessageSent(chatID: String, message: BFCore.Message) {
        chatListViewModel?.updateLastMessage(chatID: chatID, message: message)
    }

    /// Выход из аккаунта
    func logout(authService: AuthServiceProtocol, updatesService: UpdatesServiceProtocol) async {
        await updatesService.stop()
        await authService.logout()
        selectedChat = nil
        activeTab = .chats
        selectedSettingsCategory = nil
        currentState = .authentication
        authScreen = .login
    }
}
