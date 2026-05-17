//
//  ProfileView.swift
//  Barkfluff (iOS)
//
//  Корневой экран таба «Профиль»: карточка пользователя сверху +
//  сгруппированные категории настроек (по образцу Android-клиента).
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

            Section("profile.section.account_security") {
                categoryLink(.security)
                categoryLink(.privacy)
            }

            Section("profile.section.personalization") {
                categoryLink(.personalization)
            }

            Section("profile.section.chats") {
                categoryLink(.chatFolders)
            }

            Section("profile.section.app") {
                categoryLink(.cloud)
                categoryLink(.cache)
                categoryLink(.activeSessions)
                categoryLink(.notifications)
            }

            Section("profile.section.other") {
                categoryLink(.general)
                categoryLink(.language)
                categoryLink(.aboutApp)
                categoryLink(.aboutServer)
                categoryLink(.testing)
            }
        }
        .listStyle(.insetGrouped)
        .navigationTitle("profile.title")
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

    @ViewBuilder
    private func categoryLink(_ category: SettingsCategory) -> some View {
        NavigationLink(value: category) {
            SettingsCategoryRow(category: category)
        }
    }
}

/// Назначения push-навигации в табе «Профиль» (помимо категорий настроек).
enum ProfileDestination: Hashable {
    case edit
}
