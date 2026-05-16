//
//  SettingsCategory.swift
//  Barkfluff
//
//  Категории настроек: единый источник истины для верхнего меню
//  (Settings-сцена) и для сайдбара профиля.
//

import SwiftUI

/// Категории настроек
enum SettingsCategory: String, CaseIterable, Identifiable {
    case editProfile
    case general
    case language
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

    var id: String { rawValue }

    /// Локализованное название — `LocalizedStringKey` для реактивности к смене языка.
    /// Возврат `String` через `String(localized:)` замораживает значение в момент вычисления.
    var title: LocalizedStringKey {
        switch self {
        case .editProfile: "settings.category.edit_profile"
        case .general: "settings.category.general"
        case .language: "settings.category.language"
        case .notifications: "settings.category.notifications"
        case .security: "settings.category.security"
        case .privacy: "settings.category.privacy"
        case .personalization: "settings.category.personalization"
        case .chatFolders: "settings.category.chat_folders"
        case .cloud: "settings.category.cloud"
        case .cache: "settings.category.cache"
        case .activeSessions: "settings.category.active_sessions"
        case .aboutApp: "settings.category.about_app"
        case .aboutServer: "settings.category.about_server"
        }
    }

    var icon: String {
        switch self {
        case .editProfile: "person.crop.circle.fill"
        case .general: "gear"
        case .language: "globe"
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
        }
    }

    var iconColor: Color {
        switch self {
        case .editProfile: .blue
        case .general: .gray
        case .language: .purple
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
        }
    }
}
