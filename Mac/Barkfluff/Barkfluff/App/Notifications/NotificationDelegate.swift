//
//  NotificationDelegate.swift
//  Barkfluff
//
//  Делегат UNUserNotificationCenter — клик по баннеру открывает нужный чат
//  и фокусирует приложение.
//

import AppKit
import UserNotifications
import BFCore

final class NotificationDelegate: NSObject, UNUserNotificationCenterDelegate, @unchecked Sendable {

    weak var coordinator: AppCoordinator?

    /// `willPresent` определяет, что показать когда уведомление приходит при
    /// активном приложении. Финальное решение «показывать или нет» уже принято
    /// в NotificationService.shouldShow на этапе постинга — поэтому здесь
    /// просто разрешаем баннер.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        willPresent notification: UNNotification
    ) async -> UNNotificationPresentationOptions {
        return [.banner, .sound, .list]
    }

    /// Клик по баннеру или открытие из Notification Center.
    func userNotificationCenter(
        _ center: UNUserNotificationCenter,
        didReceive response: UNNotificationResponse
    ) async {
        let userInfo = response.notification.request.content.userInfo
        guard let chatID = userInfo["chatID"] as? String else { return }

        // Сначала фокусируем приложение и пробуем открыть чат, если он уже в списке.
        let openedImmediately = await MainActor.run { [weak self] () -> Bool in
            NSApp.activate(ignoringOtherApps: true)
            guard let coordinator = self?.coordinator else { return true }
            coordinator.activeTab = .chats
            if let chat = coordinator.chatListViewModel?.chats.first(where: { $0.id == chatID }) {
                coordinator.selectedChat = chat
                return true
            }
            return false
        }
        if openedImmediately { return }

        // Polling fallback: до 2 секунд ждём пока ChatListViewModel загрузит чаты.
        for _ in 0..<20 {
            try? await Task.sleep(for: .milliseconds(100))
            let opened = await MainActor.run { [weak self] () -> Bool in
                guard let coordinator = self?.coordinator else { return true }
                if let chat = coordinator.chatListViewModel?.chats.first(where: { $0.id == chatID }) {
                    coordinator.selectedChat = chat
                    return true
                }
                return false
            }
            if opened { return }
        }
    }
}
