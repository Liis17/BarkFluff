//
//  ProfileView.swift
//  Barkfluff (iOS)
//
//  Корневой экран таба «Профиль»: карточка пользователя + переход к редактированию.
//  Настройки вынесены в отдельный таб (см. SettingsView).
//

import SwiftUI
import BFCore

struct ProfileView: View {
    @Environment(AppCoordinator.self) private var coordinator
    @Environment(DependencyContainer.self) private var container

    var body: some View {
        List {
            Section {
                NavigationLink(value: ProfileDestination.edit) {
                    ProfileCardView()
                }
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("Профиль")
        .navigationDestination(for: ProfileDestination.self) { destination in
            switch destination {
            case .edit:
                ProfileEditView(
                    userService: container.userService,
                    fileService: container.fileService,
                    container: container
                )
            }
        }
    }
}

/// Назначения push-навигации в табе «Профиль».
enum ProfileDestination: Hashable {
    case edit
}
