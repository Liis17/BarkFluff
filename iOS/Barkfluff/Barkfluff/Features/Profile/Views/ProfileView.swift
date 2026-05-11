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

            Section("Аккаунт и безопасность") {
                categoryLink(.security)
                categoryLink(.privacy)
            }

            Section("Персонализация") {
                categoryLink(.personalization)
            }

            Section("Приложение") {
                categoryLink(.cloud)
                categoryLink(.cache)
                categoryLink(.activeSessions)
                categoryLink(.notifications)
            }

            Section("Другое") {
                categoryLink(.general)
                categoryLink(.aboutApp)
                categoryLink(.aboutServer)
                categoryLink(.testing)
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
