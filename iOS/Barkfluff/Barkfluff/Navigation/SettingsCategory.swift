//
//  SettingsCategory.swift
//  Barkfluff (iOS)
//
//  Категории настроек: единый источник истины для табы настроек
//  и навигации профиля.
//

import SwiftUI

/// Категории настроек
enum SettingsCategory: String, CaseIterable, Identifiable {
    case editProfile
    case general
    case notifications
    case security
    case privacy
    case personalization
    case chatFolders
    case cloud
    case cache
    case activeSessions
    case aboutApp
    case aboutServer
    case testing

    var id: String { rawValue }

    var title: String {
        switch self {
        case .editProfile: "Мой профиль"
        case .general: "Общие"
        case .notifications: "Уведомления и звук"
        case .security: "Безопасность"
        case .privacy: "Приватность"
        case .personalization: "Персонализация"
        case .chatFolders: "Папки чатов"
        case .cloud: "Облако"
        case .cache: "Кеш"
        case .activeSessions: "Активные сессии"
        case .aboutApp: "О приложении"
        case .aboutServer: "О сервере"
        case .testing: "Тестирование"
        }
    }

    var icon: String {
        switch self {
        case .editProfile: "person.crop.circle.fill"
        case .general: "gear"
        case .notifications: "bell.fill"
        case .security: "lock.fill"
        case .privacy: "eye.slash.fill"
        case .personalization: "paintbrush.fill"
        case .chatFolders: "folder.fill"
        case .cloud: "cloud.fill"
        case .cache: "internaldrive.fill"
        case .activeSessions: "laptopcomputer.and.iphone"
        case .aboutApp: "app.badge.fill"
        case .aboutServer: "server.rack"
        case .testing: "hammer.fill"
        }
    }

    var iconColor: Color {
        switch self {
        case .editProfile: .blue
        case .general: .gray
        case .notifications: .red
        case .security: .green
        case .privacy: .cyan
        case .personalization: .pink
        case .chatFolders: .brown
        case .cloud: .indigo
        case .cache: .purple
        case .activeSessions: .orange
        case .aboutApp: .mint
        case .aboutServer: .teal
        case .testing: .yellow
        }
    }
}
