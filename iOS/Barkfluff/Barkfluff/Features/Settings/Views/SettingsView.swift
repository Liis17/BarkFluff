//
//  SettingsView.swift
//  Barkfluff (iOS)
//
//  Контейнер для row-компонента настроек и роутера категорий.
//  Корневой экран настроек как отдельный таб больше не используется —
//  категории показываются внутри таба «Профиль» (см. ProfileView).
//

import SwiftUI

/// Row для отображения категории настроек (используется из ProfileView).
struct SettingsCategoryRow: View {
    let category: SettingsCategory

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: category.icon)
                .font(.system(size: 16, weight: .semibold))
                .foregroundStyle(.white)
                .frame(width: 30, height: 30)
                .background(category.iconColor.gradient, in: RoundedRectangle(cornerRadius: 7, style: .continuous))
            Text(category.title)
                .font(.body)
        }
    }
}

/// Маршрутизация по категориям к конкретным экранам.
struct SettingsCategoryView: View {
    let category: SettingsCategory
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        switch category {
        case .editProfile:
            ProfileEditView(
                userService: container.userService,
                fileService: container.fileService,
                container: container
            )
        case .general:
            GeneralSettingsView()
        case .notifications:
            ContentUnavailableView(
                "Уведомления",
                systemImage: "bell.fill",
                description: Text("Push-уведомления для iOS-клиента ещё не реализованы")
            )
            .navigationTitle("Уведомления")
            .navigationBarTitleDisplayMode(.inline)
        case .security:
            SecuritySettingsView()
        case .privacy:
            PrivacySettingsView()
        case .personalization:
            PersonalizationSettingsView()
        case .chatFolders:
            ChatFoldersListView()
        case .cloud:
            CloudSettingsView()
        case .cache:
            CacheSettingsView()
        case .activeSessions:
            SessionsView()
        case .aboutApp:
            AboutAppSettingsView()
        case .aboutServer:
            AboutServerSettingsView()
        case .testing:
            TestingSettingsView()
        }
    }
}
