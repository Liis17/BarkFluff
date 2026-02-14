//
//  SettingsCategory.swift
//  Barkfluff
//
//  Категории настроек в профиле
//

import SwiftUI

/// Категории настроек
enum SettingsCategory: String, CaseIterable, Identifiable {
    case editProfile
    case general
    case notifications
    case privacy
    case dataAndStorage
    case activeSessions

    var id: String { rawValue }

    var title: String {
        switch self {
        case .editProfile: "Мой профиль"
        case .general: "Общие"
        case .notifications: "Уведомления и звук"
        case .privacy: "Конфиденциальность"
        case .dataAndStorage: "Данные и Память"
        case .activeSessions: "Активные сессии"
        }
    }

    var icon: String {
        switch self {
        case .editProfile: "person.crop.circle.fill"
        case .general: "gear"
        case .notifications: "bell.fill"
        case .privacy: "lock.shield.fill"
        case .dataAndStorage: "internaldrive.fill"
        case .activeSessions: "laptopcomputer.and.iphone"
        }
    }

    var iconColor: Color {
        switch self {
        case .editProfile: .blue
        case .general: .gray
        case .notifications: .red
        case .privacy: .green
        case .dataAndStorage: .purple
        case .activeSessions: .orange
        }
    }
}
