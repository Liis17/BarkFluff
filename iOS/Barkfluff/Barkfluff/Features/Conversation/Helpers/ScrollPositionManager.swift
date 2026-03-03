//
//  ScrollPositionManager.swift
//  Barkfluff
//
//  Управление позицией скролла (iOS версия)
//

import SwiftUI
import Foundation

/// Менеджер позиции скролла для списка сообщений
@Observable
final class ScrollPositionManager {
    /// ID сообщения для прокрутки (формат: "msg-{id}")
    var scrollToID: String?

    /// Показывать кнопку прокрутки вниз
    var showScrollToBottom: Bool = false

    /// Количество непрочитанных сообщений (для кнопки)
    var unreadCount: Int = 0

    /// ID первого видимого сообщения (для пагинации)
    var firstVisibleMessageID: Int64?

    /// Смещение скролла (для эффекта blur в заголовке)
    var scrollOffset: CGFloat = 0

    /// Флаг, что скролл в самом низу
    var isAtBottom: Bool = true

    /// Флаг завершения начальной загрузки (скролл вниз выполнен)
    var isInitialLoadComplete: Bool = false

    /// Прокрутить к сообщению
    func scrollTo(messageID: Int64) {
        scrollToID = "msg-\(messageID)"
    }

    /// Прокрутить к последнему сообщению
    func scrollToBottom() {
        // Сбрасываем scrollToID чтобы триггернуть onChange
        scrollToID = nil
    }

    /// Обновить состояние при скролле
    func updateScrollOffset(_ offset: CGFloat, isAtBottom: Bool) {
        self.scrollOffset = offset
        self.isAtBottom = isAtBottom
        self.showScrollToBottom = !isAtBottom
    }

    /// Сбросить состояние при смене чата
    func reset() {
        scrollToID = nil
        showScrollToBottom = false
        unreadCount = 0
        firstVisibleMessageID = nil
        scrollOffset = 0
        isAtBottom = true
        isInitialLoadComplete = false
    }
}
