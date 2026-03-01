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

    /// Путь навигации в профиле
    var profileNavigationPath = NavigationPath()

    // MARK: - Chat List Reference

    /// Weak ссылка на ChatListViewModel для уведомлений о прочтении
    weak var chatListViewModel: ChatListViewModel?

    // MARK: - Sheet Presentation

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
        chatNavigationPath = NavigationPath()
        profileNavigationPath = NavigationPath()
        currentState = .authentication
        authScreen = .login
    }
}
