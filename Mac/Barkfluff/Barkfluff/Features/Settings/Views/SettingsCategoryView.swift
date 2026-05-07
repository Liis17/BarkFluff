//
//  SettingsCategoryView.swift
//  Barkfluff
//
//  Единый источник истины для рендера экрана настроек по `SettingsCategory`.
//  Используется и в верхнем меню (Settings-сцена), и в сайдбаре профиля.
//

import SwiftUI

struct SettingsCategoryView: View {
    @Environment(DependencyContainer.self) private var container
    let category: SettingsCategory

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
            NotificationsSettingsView()
        case .privacy:
            SecuritySettingsView(viewModel: container.settingsViewModel)
        case .cloud:
            CloudSettingsView()
        case .cache:
            CacheSettingsView()
        case .activeSessions:
            SessionsView(viewModel: container.settingsViewModel)
        case .aboutApp:
            AboutAppSettingsView()
        case .aboutServer:
            AboutServerSettingsView(serverViewModel: container.settingsViewModel)
        }
    }
}
