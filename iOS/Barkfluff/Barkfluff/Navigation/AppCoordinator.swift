//
//  AppCoordinator.swift
//  Barkfluff
//
//  Координатор навигации приложения (iOS версия)
//

import SwiftUI
import Observation
import BFCore
import BFNetworking

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

    /// Первая партия чатов загружена (или попытка завершилась с ошибкой).
    /// Используется `RootView`, чтобы держать `SplashView` поверх `MainTabView`,
    /// пока `ChatListView` ещё ждёт первый ответ от сервера/кеша.
    /// Один раз становится `true` и в течение жизни `AppCoordinator` больше не сбрасывается —
    /// splash хотим видеть только на cold start.
    var isInitialChatsLoaded: Bool = false

    /// Готовность сетевого слоя: beacon endpoints получены, refresh-токен валиден.
    /// До `true` нельзя делать gRPC-вызовы (`listChats`, `getCurrentUser`,
    /// `onlineStatusService.track`) — упадут с «Messages не настроено».
    /// Сбрасывается в `false` при `logout`/`handleSessionExpired`.
    var isConnectionReady: Bool = false

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

    /// Запуск приложения.
    ///
    /// Если у пользователя есть refresh-токен (значит он уже логинился) —
    /// сразу переходим в `.main` и показываем `MainTabView` с пустым `ChatListView`
    /// и плейсхолдерами/крутилкой. В фоне делаем `tryReconnect` + `tryRestoreSession`
    /// и выставляем `isConnectionReady = true`. До этого `ChatListView.task` ждёт
    /// флаг (`waitForConnectionReady()`) и только потом дёргает `listChats`,
    /// подписки, профиль и онлайн-статусы — иначе будет «Messages не настроено»
    /// и поломанные онлайн-статусы.
    ///
    /// Если refresh-токена нет — обычный flow: reconnect к beacon (нужен для login)
    /// и `.authentication`/`.serverSelection`.
    func onAppLaunch(
        serverDiscovery: ServerDiscoveryServiceProtocol,
        authService: AuthServiceProtocol,
        tokenProvider: any TokenProvider
    ) async {
        let hasServerEndpoint = await tokenProvider.savedServerHost != nil
        let hasRefreshToken = await tokenProvider.hasRefreshToken

        guard hasServerEndpoint else {
            currentState = .serverSelection
            return
        }

        guard hasRefreshToken else {
            // Нужны живые endpoints для login — reconnect синхронно.
            let reconnected = await serverDiscovery.tryReconnect()
            if reconnected {
                currentState = .authentication
                authScreen = .login
            } else {
                currentState = .serverSelection
            }
            return
        }

        // Refresh-токен есть → сразу в .main; UI покажет крутилку,
        // запросы в ChatListView ждут isConnectionReady через waitForConnectionReady().
        currentState = .main

        Task { [weak self] in
            guard let self else { return }
            let reconnected = await serverDiscovery.tryReconnect()
            guard reconnected else {
                // Сеть мертва — VM сама покажет «Нет соединения», isConnectionReady остаётся false.
                return
            }
            let restored = await authService.tryRestoreSession()
            guard restored else {
                // Refresh-токен не подошёл — мягкий разлогин.
                await MainActor.run { self.handleSessionExpired() }
                return
            }
            await MainActor.run { self.isConnectionReady = true }
        }
    }

    /// Ждёт пока сетевой слой будет готов (или истечёт таймаут).
    /// До готовности нельзя выполнять gRPC-вызовы — упадут с network error.
    /// Возвращает `true` если соединение готово, `false` если таймаут.
    @discardableResult
    func waitForConnectionReady(timeout: Duration = .seconds(30)) async -> Bool {
        if isConnectionReady { return true }
        let deadline = ContinuousClock.now.advanced(by: timeout)
        while !isConnectionReady, ContinuousClock.now < deadline {
            try? await Task.sleep(for: .milliseconds(100))
        }
        return isConnectionReady
    }

    /// Сессия истекла — вернуться на логин
    func handleSessionExpired() {
        isConnectionReady = false
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

        isConnectionReady = false
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
