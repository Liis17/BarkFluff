//
//  AppFocusState.swift
//  Barkfluff
//
//  Состояние фокуса приложения (front-most / background).
//  Используется NotificationService чтобы решить — показывать баннер или нет:
//  даже если открыт нужный чат, но окно ушло в фон, уведомление должно прийти.
//

import AppKit
import Observation

@Observable
final class AppFocusState {
    /// Активно ли приложение (front-most). Стартуем с true — приложение только что
    /// запустилось, и до прихода первого уведомления наблюдатель отработает
    /// корректное значение.
    private(set) var isActive: Bool = true

    init() {
        // AppFocusState — singleton-style, живёт всё время работы приложения,
        // поэтому ручной removeObserver не нужен. Block-based observers держатся
        // в NotificationCenter.default и автоматически освобождаются на выходе.
        let center = NotificationCenter.default

        center.addObserver(
            forName: NSApplication.didBecomeActiveNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.isActive = true }
        }

        center.addObserver(
            forName: NSApplication.didResignActiveNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            MainActor.assumeIsolated { self?.isActive = false }
        }
    }
}
