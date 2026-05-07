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
    case notifications
    case privacy
    case cloud
    case cache
    case activeSessions
    case about

    var id: String { rawValue }

    var title: String {
        switch self {
        case .editProfile: "Мой профиль"
        case .general: "Общие"
        case .notifications: "Уведомления и звук"
        case .privacy: "Конфиденциальность"
        case .cloud: "Облако"
        case .cache: "Кеш"
        case .activeSessions: "Активные сессии"
        case .about: "О сервере"
        }
    }

    var icon: String {
        switch self {
        case .editProfile: "person.crop.circle.fill"
        case .general: "gear"
        case .notifications: "bell.fill"
        case .privacy: "lock.shield.fill"
        case .cloud: "cloud.fill"
        case .cache: "internaldrive.fill"
        case .activeSessions: "laptopcomputer.and.iphone"
        case .about: "info.circle.fill"
        }
    }

    var iconColor: Color {
        switch self {
        case .editProfile: .blue
        case .general: .gray
        case .notifications: .red
        case .privacy: .green
        case .cloud: .indigo
        case .cache: .purple
        case .activeSessions: .orange
        case .about: .teal
        }
    }
}
