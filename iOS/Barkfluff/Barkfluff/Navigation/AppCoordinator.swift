//
//  AppCoordinator.swift
//  Barkfluff
//
//  Координатор навигации приложения (iOS версия)
//

import SwiftUI
import Observation
import BFCore

/// Координатор состояния приложения.
/// Управляет навигацией между основными экранами.
@Observable
final class AppCoordinator {
    // MARK: - Tab Navigation

    /// Вкладки таб-бара
    enum Tab: String, CaseIterable {
        case chats
        case profile
    }

    /// Активная вкладка
    var activeTab: Tab = .chats

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

    /// Выбранный чат для навигации
    var selectedChat: Chat?

    /// Путь навигации в чатах
    var chatNavigationPath = NavigationPath()

    /// Путь навигации в профиле (включая категории настроек)
    var profileNavigationPath = NavigationPath()

    // MARK: - Chat List Reference

    /// Weak ссылка на ChatListViewModel для уведомлений о прочтении
    weak var chatListViewModel: ChatListViewModel?

    // MARK: - Sheet Presentation

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

    // MARK: - App Lifecycle

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

    // MARK: - Navigation Helpers

    /// Открыть чат
    func openChat(_ chat: Chat) {
        selectedChat = chat
        chatNavigationPath.append(chat)
    }

    /// Закрыть текущий чат
    func closeChat() {
        selectedChat = nil
        if !chatNavigationPath.isEmpty {
            chatNavigationPath.removeLast()
        }
    }

    /// Открыть профиль собеседника (push в стек чата)
    func openUserProfile(for chat: Chat) {
        chatNavigationPath.append(ConversationDestination.userProfile(chat))
    }

    /// Уведомить о прочтении чата (вызывается при открытии чата)
    func notifyChatOpened(_ chatID: String) {
        chatListViewModel?.markChatAsReadLocally(chatID: chatID)
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
    /// чтобы UI мог предложить «Повторить» или вызвать `forceLogout(...)`.
    func logout(container: DependencyContainer) async throws {
        try await container.authService.logout()
        await performLocalWipe(container: container)
    }

    /// Принудительный выход без серверного шага. Локальные данные стираются полностью.
    func forceLogout(container: DependencyContainer) async {
        await container.authService.forceLocalLogout()
        await performLocalWipe(container: container)
    }

    private func performLocalWipe(container: DependencyContainer) async {
        await container.reset()

        selectedChat = nil
        activeTab = .chats
        presentedSheet = nil
        chatNavigationPath = NavigationPath()
        profileNavigationPath = NavigationPath()

        // Если адрес сервера сохранён — восстанавливаем подключение к Beacon, чтобы
        // login-флоу сразу имел готовые service endpoints, и уводим на логин того же сервера.
        // Иначе — на экран выбора сервера.
        if await container.serverDiscoveryService.currentServerEndpoint() != nil,
           await container.serverDiscoveryService.tryReconnect() {
            currentState = .authentication
        } else {
            currentState = .serverSelection
        }
        authScreen = .login
    }
}

/// Назначения push-навигации внутри стека чата (помимо самого `Chat`).
enum ConversationDestination: Hashable {
    /// Полноэкранный профиль собеседника / групповой инфо-экран
    case userProfile(Chat)
}
